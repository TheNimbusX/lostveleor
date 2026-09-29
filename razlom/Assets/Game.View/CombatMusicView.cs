using System;
using System.Globalization;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// МУЗЫКА БОЯ ЛЕСА — источники звука. Что и когда звучит, решает
    /// CombatMusicDirector (чистый C#, под тестами); здесь — чтение забега,
    /// голоса AudioSource, ползунок «Музыка» и журнал.
    ///
    /// Откуда сигналы (только чтение, симуляцию не трогаем):
    ///  • где игрок — GameSession.Mode (лагерь, забег, итоги) и MainMenuView.IsOpen;
    ///  • арена — RiftRun.Depth; босс — ArenaRunPlan.IsBoss(Depth) (и уровень с
    ///    боссом, и живой BossId); элита — RiftRun.CurrentEncounter.HasElite, а без
    ///    шаблона (стенды разработчика) — замеченная на арене элита Simulation.IsElite;
    ///  • бой — живые враги (не Wole) с EntityStore.Aggro: заметивший героя враг
    ///    не «забывает» его, поэтому это и есть «проснувшийся»;
    ///  • зачистка — RunPhase.SeekingExit; экран награды — ChoosingReward,
    ///    ReplacingAbility, ChoosingRoute;
    ///  • смерть — герой мёртв или итоги с RunOutcome.Died;
    ///  • игровое время для тишины между волнами — приращение Simulation.Tick;
    ///  • пауза — TickDriver.GameplayPaused и PauseMenu.IsOpen, как у лагеря.
    ///
    /// Тема лагеря и тема меню не наслаиваются на бой: пока CampSoundscape не
    /// погас (CurrentBlend) или меню ещё гасит свою тему, режиссёр ждёт в тишине.
    /// Выход из забега (лагерь, итоги) — тишина с затуханием.
    ///
    /// Голоса 2D (spatialBlend 0), мимо зон реверберации, без AudioSource.loop.
    /// Приоритет числом НИЖЕ всех голосов боя (у них 56–256): при нехватке
    /// настоящих голосов Unity вытесняет удары, а не музыку. Голос на паузе
    /// (трек ушёл в ноль и ждёт возврата) — наоборот, последний в очереди: он
    /// молчит и не должен держать настоящий голос вместо удара.
    ///
    /// Петля — по звуковым часам: голос в очереди режиссёра (VoicePending)
    /// стартует AudioSource.PlayScheduled в DSP-миг, когда ведущий голос дойдёт
    /// до точки петли (его timeSamples и AudioSettings.dspTime читаются подряд).
    /// Клипы — Streaming: перемотка потока Vorbis неточна, поэтому петля всегда
    /// входит с LoopStart (у леса — ноль), а возврат с паузы — UnPause, без перемотки.
    ///
    /// Съёмка: -capture-music-log пишет в лог каждую смену с причиной и временем
    /// ([music]); -capture-no-music глушит музыку для замеров звука ударов.
    /// Ставится на объект арены одной строкой в ArenaView (EnsureOn).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TickDriver))]
    public sealed class CombatMusicView : MonoBehaviour
    {
        /// <summary>Unity: 0 — важнее всего. Голоса боя — 56..256 (CombatAudio).</summary>
        private const int MusicPriority = 24;

        /// <summary>Голос на паузе или без звука: первым уступает настоящий голос.</summary>
        private const int ParkedPriority = 256;

        /// <summary>
        /// Голос петли ждёт кадр, пока ведущий снимается с паузы, только если до
        /// точки петли больше этого, секунды; иначе стартует по учёту режиссёра.
        /// </summary>
        private const float DeferLoopStartAbove = .1f;

        /// <summary>Сколько гаснет тема главного меню после «Играть» (MainMenuView.MusicFadeSeconds).</summary>
        private const float MainMenuThemeFadeSeconds = .6f;

        /// <summary>Ниже этого смешения лагерь считается замолчавшим.</summary>
        private const float CampSilentBelow = .02f;

        private const int VoiceCount = CombatMusicDirector.TrackCount * CombatMusicDirector.VoicesPerTrack;

        private static readonly bool LogSwitches =
            Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-music-log") >= 0;

        private static readonly bool Muted =
            Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-no-music") >= 0;

        public static CombatMusicView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<CombatMusicView>();
            return view != null ? view : host.AddComponent<CombatMusicView>();
        }

        private CombatMusicDirector _director = new CombatMusicDirector();
        private TickDriver _driver;
        private PauseMenu _pause;
        private CampSoundscape _camp;
        private GameObject _root;
        private readonly AudioClip[] _clips = new AudioClip[CombatMusicDirector.TrackCount];
        private readonly AudioSource[] _voices = new AudioSource[VoiceCount];
        private readonly int[] _appliedSerial = new int[VoiceCount];
        private readonly int[] _appliedPriority = new int[VoiceCount];
        private readonly int[] _resumedFrame = new int[VoiceCount];
        private readonly CombatMusicDirector.Status[] _applied = new CombatMusicDirector.Status[VoiceCount];
        private bool _loaded;

        private Simulation _lastSim;
        private int _lastTick;
        private int _arenaRun = int.MinValue, _arenaDepth = int.MinValue;
        private bool _templateElite, _hasTemplate, _eliteSeen;
        private bool _menuWasOpen;
        private float _menuClosedAt = float.NegativeInfinity;
        private int _loggedTransitions, _loggedLoops, _campSearchRun = int.MinValue;

        /// <summary>Для съёмки и проверок: состояние режиссёра.</summary>
        public CombatMusicDirector Director => _director;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _pause = GetComponent<PauseMenu>();
            if (_pause == null) _pause = FindAnyObjectByType<PauseMenu>();
            _camp = FindAnyObjectByType<CampSoundscape>();
            if (LogSwitches) Debug.Log("[music] журнал музыки боя включён" + (Muted ? " (музыка заглушена -capture-no-music)" : ""));
        }

        private void LateUpdate()
        {
            // Переходы — по реальному времени: пауза и стоп-кадр удара их не морозят.
            // Под записью звука кадр фиксированный — шаг тот же, что у AudioRenderer.
            float dt = CombatAudioCapture.Recording && Time.captureDeltaTime > 0f
                ? Time.captureDeltaTime
                : Time.unscaledDeltaTime;

            CombatMusicDirector.Signals signals = ReadSignals(out float gameDt);
            if (!_loaded && !Muted && signals.Place == CombatMusicDirector.Place.Run) LoadClips();
            if (_loaded) SyncPositions();
            _director.Update(in signals, dt, gameDt);
            if (_loaded) Apply();
            if (LogSwitches) LogChanges(in signals);
        }

        private CombatMusicDirector.Signals ReadSignals(out float gameDt)
        {
            var signals = new CombatMusicDirector.Signals();
            gameDt = 0f;

            bool menu = MainMenuView.IsOpen;
            if (_menuWasOpen && !menu) _menuClosedAt = Time.unscaledTime;
            _menuWasOpen = menu;
            signals.OtherMusicAudible = menu || Time.unscaledTime - _menuClosedAt < MainMenuThemeFadeSeconds
                || (_camp != null && _camp.isActiveAndEnabled && _camp.CurrentBlend > CampSilentBelow);
            signals.Paused = (_driver != null && _driver.GameplayPaused) || (_pause != null && _pause.IsOpen);

            GameSession session = _driver != null ? _driver.Session : null;
            if (menu || session == null)
            {
                signals.Place = CombatMusicDirector.Place.Menu;
                return signals;
            }
            if (session.Mode == GameMode.Summary)
            {
                signals.Place = CombatMusicDirector.Place.Summary;
                signals.HeroDead = session.LastRun.Outcome == RunOutcome.Died;
                return signals;
            }

            RiftRun run = session.Mode == GameMode.Rift ? session.Run : null;
            Simulation sim = run != null ? run.Sim : null;
            // Стенд врагов редактора — не забег: там музыка молчит, как в лагере.
            if (run == null || sim == null || run.IsEnemySandbox)
            {
                signals.Place = CombatMusicDirector.Place.Camp;
                _lastSim = null;
                return signals;
            }

            signals.Place = CombatMusicDirector.Place.Run;
            signals.RunNumber = session.RunNumber;
            signals.Depth = run.Depth;

            // Лагерь на месте, пока идёт новый забег: при первой съёмке его могло не быть.
            if (_camp == null && _campSearchRun != session.RunNumber)
            {
                _campSearchRun = session.RunNumber;
                _camp = FindAnyObjectByType<CampSoundscape>();
            }

            int tick = sim.Tick;
            if (ReferenceEquals(sim, _lastSim) && tick >= _lastTick)
                gameDt = (tick - _lastTick) / (float)Simulation.TicksPerSecond;
            _lastSim = sim;
            _lastTick = tick;

            RunPhase phase = run.Phase;
            signals.Choosing = phase == RunPhase.ChoosingReward || phase == RunPhase.ReplacingAbility
                               || phase == RunPhase.ChoosingRoute;
            signals.Cleared = phase == RunPhase.SeekingExit;

            if (_arenaRun != session.RunNumber || _arenaDepth != run.Depth)
            {
                // Шаблон встречи ставится вместе с глубиной; его элита — на всю арену,
                // даже если сама элита выйдет только поздней волной.
                _arenaRun = session.RunNumber;
                _arenaDepth = run.Depth;
                ArenaEncounterTemplate template = run.CurrentEncounter;
                _hasTemplate = template != null;
                _templateElite = _hasTemplate && template.HasElite;
                _eliteSeen = false;
            }

            EntityStore entities = sim.Entities;
            int bossId = run.BossId, awake = 0;
            bool eliteHere = false;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (entities.Aggro[i]) awake++;
                if (i != bossId && sim.IsElite(i)) eliteHere = true;
            }
            if (eliteHere) _eliteSeen = true;

            signals.AwakeHostiles = awake;
            signals.HeroDead = entities.Count > Simulation.PlayerId && !entities.Alive[Simulation.PlayerId];
            signals.Boss = (run.Plan != null && run.Plan.IsBoss(run.Depth)) || run.LevelSettings.Boss || bossId >= 0;
            signals.Elite = _hasTemplate ? _templateElite : _eliteSeen;
            return signals;
        }

        private void LoadClips()
        {
            _loaded = true;
            _root = new GameObject("Музыка боя");
            _root.transform.SetParent(transform, false);
            for (int t = 0; t < CombatMusicDirector.TrackCount; t++)
            {
                CombatMusicDirector.TrackInfo info = CombatMusicDirector.Tracks[t];
                string path = CombatMusicDirector.Folder + "/" + info.Resource;
                AudioClip clip = Resources.Load<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogWarning("[music] нет клипа Resources/" + path + " — трек " + info.Track + " молчит.");
                    continue;
                }
                _clips[t] = clip;
                _director.SetClipLength(info.Track, clip.length);
                for (int v = 0; v < CombatMusicDirector.VoicesPerTrack; v++)
                {
                    AudioSource source = _root.AddComponent<AudioSource>();
                    source.clip = clip;
                    source.playOnAwake = false;
                    // Петля — вторым голосом из режиссёра, не силами источника.
                    source.loop = false;
                    source.spatialBlend = 0f;
                    source.dopplerLevel = 0f;
                    source.bypassReverbZones = true;
                    source.priority = ParkedPriority;
                    source.volume = 0f;
                    _voices[t * CombatMusicDirector.VoicesPerTrack + v] = source;
                    _appliedPriority[t * CombatMusicDirector.VoicesPerTrack + v] = ParkedPriority;
                }
                if (LogSwitches)
                    Debug.Log("[music] клип " + info.Resource + " " + Seconds(clip.length) + "s, " + clip.frequency + " Hz, " + clip.loadType);
            }
        }

        /// <summary>Позиции играющих голосов — из самих источников, до решения кадра.</summary>
        private void SyncPositions()
        {
            for (int i = 0; i < VoiceCount; i++)
            {
                AudioSource source = _voices[i];
                if (source == null || _applied[i] != CombatMusicDirector.Status.Playing || !source.isPlaying) continue;
                var track = (CombatMusicDirector.Track)(i / CombatMusicDirector.VoicesPerTrack);
                int voice = i % CombatMusicDirector.VoicesPerTrack;
                // Голос в очереди PlayScheduled уже «играет», но стоит на LoopStart — его позицию не берём.
                if (_director.VoicePending(track, voice)) continue;
                _director.SyncVoicePosition(track, voice, source.timeSamples / (float)source.clip.frequency);
            }
        }

        private void Apply()
        {
            float music = GameUserSettings.MusicGain;
            for (int i = 0; i < VoiceCount; i++)
            {
                AudioSource source = _voices[i];
                if (source == null) continue;
                var track = (CombatMusicDirector.Track)(i / CombatMusicDirector.VoicesPerTrack);
                int voice = i % CombatMusicDirector.VoicesPerTrack;

                if (!_director.VoiceActive(track, voice))
                {
                    if (_applied[i] != CombatMusicDirector.Status.Stopped)
                    {
                        source.Stop();
                        _applied[i] = CombatMusicDirector.Status.Stopped;
                    }
                    source.volume = 0f;
                    continue;
                }

                int serial = _director.VoiceStartSerial(track, voice);
                if (serial != _appliedSerial[i])
                {
                    // Новый старт: первый проход с нуля, петля с LoopStart по звуковым часам
                    // или (кадр проскочил точку петли) сразу с той же доли.
                    AudioClip clip = source.clip;
                    float start = _director.VoiceStartPosition(track, voice);
                    int startSample = Mathf.Clamp(Mathf.RoundToInt(start * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
                    if (_director.VoicePending(track, voice))
                    {
                        float countdown = start - _director.VoicePosition(track, voice);
                        if (!TryLoopStartDsp(track, voice, clip, out double at))
                        {
                            // Ведущий голос только что снят с паузы: его позиция по звуку верна
                            // со следующего кадра. Время есть — старт ждёт кадр, нет — по учёту.
                            if (countdown > DeferLoopStartAbove)
                            {
                                source.volume = 0f;
                                continue;
                            }
                            at = AudioSettings.dspTime + Math.Max(0.0, countdown);
                        }
                        source.Stop();
                        source.timeSamples = startSample;
                        source.PlayScheduled(at);
                    }
                    else
                    {
                        // С позиции старта, а не с учётной (та уже на кадр впереди): у первого
                        // прохода это ноль — без перемотки потока Vorbis.
                        source.Stop();
                        source.timeSamples = startSample;
                        source.Play();
                    }
                    _appliedSerial[i] = serial;
                    _applied[i] = CombatMusicDirector.Status.Playing;
                }

                CombatMusicDirector.Status status = _director.StatusOf(track);
                if (status == CombatMusicDirector.Status.Paused && _applied[i] == CombatMusicDirector.Status.Playing)
                {
                    source.Pause();
                    _applied[i] = CombatMusicDirector.Status.Paused;
                }
                else if (status == CombatMusicDirector.Status.Playing && _applied[i] == CombatMusicDirector.Status.Paused)
                {
                    // Вернулся — с того же места: источник снимается с паузы, а не стартует заново.
                    source.UnPause();
                    _applied[i] = CombatMusicDirector.Status.Playing;
                    _resumedFrame[i] = Time.frameCount;
                }

                source.volume = _director.VoiceVolume(track, voice) * music;
                int priority = status == CombatMusicDirector.Status.Playing ? MusicPriority : ParkedPriority;
                if (priority != _appliedPriority[i])
                {
                    _appliedPriority[i] = priority;
                    source.priority = priority;
                }
            }
        }

        /// <summary>
        /// DSP-миг старта голоса петли: когда ведущий голос того же трека дойдёт до
        /// точки петли. Позиция источника и звуковые часы читаются подряд; если
        /// между чтениями звук успел смешать блок — ещё раз, чтобы пара была одного
        /// блока. false — ведущий не звучит или снят с паузы в этом кадре (UnPause
        /// вступит со следующего блока, и его позиция по звуку ещё не та).
        /// </summary>
        private bool TryLoopStartDsp(CombatMusicDirector.Track track, int voice, AudioClip clip, out double at)
        {
            at = 0;
            int leadIndex = (int)track * CombatMusicDirector.VoicesPerTrack + (1 - voice);
            AudioSource lead = _voices[leadIndex];
            if (lead == null || _applied[leadIndex] != CombatMusicDirector.Status.Playing || !lead.isPlaying
                || _resumedFrame[leadIndex] == Time.frameCount)
                return false;

            float clipLength = _director.ClipLength(track);
            CombatMusicDirector.LoopWindow(track, clipLength, out _, out float end, out float crossfade);
            int frequency = clip.frequency;
            // Точка петли в сэмплах: от конца клипа, как её мерили при нарезке.
            int endSample = end >= clipLength ? clip.samples : Mathf.RoundToInt(end * frequency);
            int loopSample = endSample - Mathf.RoundToInt(crossfade * frequency);

            double dsp = 0;
            int position = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                dsp = AudioSettings.dspTime;
                position = lead.timeSamples;
                if (AudioSettings.dspTime == dsp) break;
            }
            at = dsp + (loopSample - position) / (double)frequency;
            return true;
        }

        private void LogChanges(in CombatMusicDirector.Signals signals)
        {
            if (_director.TransitionCount != _loggedTransitions)
            {
                _loggedTransitions = _director.TransitionCount;
                CombatMusicDirector.Transition change = _director.LastTransition;
                Debug.Log("[music] t=" + Seconds(Time.unscaledTime) + " frame=" + Time.frameCount
                          + " tick=" + (_lastSim != null ? _lastTick : -1) + " run=" + signals.RunNumber
                          + " depth=" + signals.Depth + " " + Describe(change.FromMood, change.FromTrack)
                          + " -> " + Describe(change.ToMood, change.ToTrack) + " fade=" + Seconds(change.Fade)
                          + "s reason=" + CombatMusicDirector.KeyOf(change.Reason)
                          + " awake=" + signals.AwakeHostiles + " boss=" + (signals.Boss ? 1 : 0)
                          + " elite=" + (signals.Elite ? 1 : 0) + " place=" + signals.Place);
            }
            if (_director.LoopCount != _loggedLoops)
            {
                _loggedLoops = _director.LoopCount;
                CombatMusicDirector.Track track = _director.LastLoopTrack;
                CombatMusicDirector.LoopWindow(track, _director.ClipLength(track), out float start, out float end,
                    out float crossfade);
                Debug.Log("[music] t=" + Seconds(Time.unscaledTime) + " frame=" + Time.frameCount + " loop "
                          + track + " " + Seconds(end - crossfade) + "s -> " + Seconds(start) + "s crossfade="
                          + Seconds(crossfade) + "s");
            }
        }

        private static string Describe(CombatMusicDirector.Mood mood, CombatMusicDirector.Track track)
            => mood == CombatMusicDirector.Mood.Silence ? "Silence"
                : mood == CombatMusicDirector.Mood.Lull ? "Lull" : "Combat/" + track;

        private static string Seconds(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private void OnDisable()
        {
            // Выключенный компонент не звучит; включённый заново начинает с чистого листа.
            for (int i = 0; i < VoiceCount; i++)
            {
                if (_voices[i] != null)
                {
                    _voices[i].Stop();
                    _voices[i].volume = 0f;
                }
                _applied[i] = CombatMusicDirector.Status.Stopped;
                _appliedSerial[i] = 0;
            }
            _director = new CombatMusicDirector();
            for (int t = 0; t < CombatMusicDirector.TrackCount; t++)
                if (_clips[t] != null) _director.SetClipLength((CombatMusicDirector.Track)t, _clips[t].length);
            _loggedTransitions = _loggedLoops = 0;
            _lastSim = null;
        }
    }
}
