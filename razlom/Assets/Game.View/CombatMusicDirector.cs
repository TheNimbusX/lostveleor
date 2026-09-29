using System;

namespace Game.View
{
    /// <summary>
    /// МУЗЫКА БОЯ ЛЕСА: что звучит и как одно сменяет другое. Треки — владельца
    /// (29.09.2026, генератор музыки, выбраны на слух): две обычные темы, затишье,
    /// тяжёлая, элитная и тема босса. Музыка идёт за боем, как в Hades 2, и не
    /// рвётся: смена — только плавным переходом, а трек, который ушёл, ждёт на
    /// паузе и продолжит с того же места, когда вернётся в этом же забеге.
    ///
    /// Чистый C# без UnityEngine: решение и учёт позиций проверяют тесты
    /// tools/Combat.Presentation.Tests (CombatMusicDirectorTests). Источники звука,
    /// громкость из настроек и чтение забега — CombatMusicView.
    ///
    /// НАСТРОЕНИЯ. Тишина — лагерь, главное меню, итоги и смерть. Затишье — арена
    /// зачищена или открыт экран награды, начало арены, пока никто не проснулся, и
    /// промежуток между волнами, но только если ни одного бодрствующего врага нет
    /// QuietBeforeLull секунд ИГРОВОГО времени: короткая пауза перед следующей
    /// волной музыку не меняет. Проснулся враг — снова бой этой арены.
    ///
    /// ТРЕК БОЯ — у арены: босс > элита > тяжёлая (А7–А8) > обычная (А1–А6;
    /// нечётные — тема A, чётные — B, чтобы темы чередовались).
    ///
    /// ПЕТЛЯ — без AudioSource.loop: за LoopCrossfade секунд до конца петли тот же
    /// клип стартует вторым голосом с LoopStart, и голоса меняются перекрёстно.
    /// У каждого трека два голоса; учёт их позиций и громкостей — здесь.
    /// Второй голос встаёт в очередь звуковых часов за LoopScheduleLead до точки
    /// петли (AudioSource.PlayScheduled в CombatMusicView) и до неё молчит «в
    /// ожидании» (VoicePending): старт по сэмплу, а не по кадру. От кадра
    /// голоса разъезжались бы на 20–50 мс — на перекрёстной петле это двойной
    /// удар (флэм), а потоку Streaming нужно время открыться.
    ///
    /// ВНЕ ЗАБЕГА (лагерь, итоги) замолчавший трек не держится на паузе, а
    /// останавливается: следующий забег всё равно начнёт его с начала, а
    /// паузный поток держал бы голос и буфер всё время в лагере.
    /// </summary>
    public sealed class CombatMusicDirector
    {
        public enum Track : byte
        {
            Lull = 0,
            NormalA = 1,
            NormalB = 2,
            Hard = 3,
            Elite = 4,
            Boss = 5,
        }

        public const int TrackCount = 6;
        public const int VoicesPerTrack = 2;

        public enum Mood : byte
        {
            Silence = 0,
            Lull = 1,
            Combat = 2,
        }

        /// <summary>Где игрок — для музыки. Всё, кроме забега, — тишина.</summary>
        public enum Place : byte
        {
            Menu = 0,
            Camp = 1,
            Run = 2,
            Summary = 3,
        }

        public enum Status : byte
        {
            /// <summary>Не играет, позиция в начале.</summary>
            Stopped = 0,
            Playing = 1,
            /// <summary>Ушёл в ноль: голоса стоят на паузе и держат позицию.</summary>
            Paused = 2,
        }

        /// <summary>Почему сменилось настроение — для журнала -capture-music-log.</summary>
        public enum Reason : byte
        {
            None = 0,
            MainMenu = 1,
            Camp = 2,
            Summary = 3,
            HeroDied = 4,
            ArenaStart = 5,
            HostileAwake = 6,
            TrackChanged = 7,
            QuietBetweenWaves = 8,
            ArenaCleared = 9,
            RewardScreen = 10,
        }

        private static readonly string[] ReasonKeys =
        {
            "none", "main-menu", "camp", "summary", "hero-died", "arena-start",
            "hostile-awake", "track-changed", "quiet-3s", "arena-cleared", "reward-screen",
        };

        public static string KeyOf(Reason reason)
            => (int)reason < ReasonKeys.Length ? ReasonKeys[(int)reason] : "unknown";

        // ---- переходы, секунды (задание владельца от 29.09) ----

        public const float LullToCombatFade = 1f;
        public const float CombatToLullFade = 3f;
        /// <summary>Бой в другой бой: например, трек арены сменился посреди драки.</summary>
        public const float CombatToCombatFade = 2f;
        public const float ToSilenceFade = 2f;
        public const float DeathToSilenceFade = 1.5f;
        /// <summary>Вход в забег: затишье проявляется, тема лагеря уже погасла.</summary>
        public const float SilenceToLullFade = 2f;
        /// <summary>Бой сразу из тишины — если враги проснулись раньше, чем зазвучало затишье.</summary>
        public const float SilenceToCombatFade = 1f;

        /// <summary>Столько игровых секунд без бодрствующих врагов, прежде чем бой уйдёт в затишье.</summary>
        public const float QuietBeforeLull = 3f;

        /// <summary>Меню паузы приглушает музыку, но не глушит (как лагерь под панелями).</summary>
        public const float PauseDuck = .45f;
        public const float DuckSharpness = 6f;

        /// <summary>
        /// Запасная длина петли для строки без своей. Для треков леса НЕ годится:
        /// при 120 BPM 3 с — полтакта (гармония съезжает), у босса — полдоли (флэм).
        /// </summary>
        public const float DefaultLoopCrossfade = 3f;

        /// <summary>
        /// За столько секунд до начала перекрёстной петли второй голос ставится
        /// в очередь звуковых часов. С запасом на провал кадра и на открытие потока.
        /// </summary>
        public const float LoopScheduleLead = 1f;

        private const float MinLoopCrossfade = .05f;
        private const float MinFade = .01f;

        /// <summary>С этой арены обычный бой звучит тяжёлой темой.</summary>
        public const int FirstHardArena = 7;

        /// <summary>Resources.Load: "Audio/Music/Forest/&lt;Resource&gt;".</summary>
        public const string Folder = "Audio/Music/Forest";

        /// <summary>
        /// Строка таблицы треков. Gain — громкость клипа до ползунка «Музыка».
        /// LoopStart/LoopEnd — окно петли в секундах (LoopEnd 0 — конец клипа);
        /// первый проход всегда с нуля, повтор — с LoopStart. LoopCrossfade —
        /// длина перекрёстной петли: целое число тактов трека, иначе второй
        /// голос входит не в долю.
        /// </summary>
        public readonly struct TrackInfo
        {
            public readonly Track Track;
            public readonly string Resource;
            public readonly float Gain;
            public readonly float LoopStart;
            public readonly float LoopEnd;
            public readonly float LoopCrossfade;

            public TrackInfo(Track track, string resource, float gain, float loopStart = 0f,
                float loopEnd = 0f, float loopCrossfade = DefaultLoopCrossfade)
            {
                Track = track;
                Resource = resource;
                Gain = gain;
                LoopStart = loopStart;
                LoopEnd = loopEnd;
                LoopCrossfade = loopCrossfade;
            }
        }

        /// <summary>
        /// Таблица треков, по индексу = (int)Track. Клипы уже нарезаны петлёй
        /// (Resources/Audio/Music/Forest/LICENSES.md): начало клипа — сильная доля,
        /// конец — сильная доля, окно петли — весь клип (LoopStart 0, LoopEnd 0).
        ///
        /// ПЕТЛЯ — ровно столько, сколько мерили швы при нарезке: второй голос
        /// с нуля, когда первый дошёл до «длина − LoopCrossfade», кривая равной
        /// мощности. Период петли (длина − кроссфейд) — целые такты: леса 120 BPM
        /// (такт 2 с), босс ~130 BPM (8 долей по 0,4616 с = 3,6928 с). Другая
        /// длина петли сдвигает гармонию или долю — проверяет CombatMusicClipTests.
        ///
        /// ГРОМКОСТЬ. Клипы сведены к −22 LUFS (затишье −24, на 2 дБ тише боя), true
        /// peak ≤ −8,5 dBTP. Gain 0,5 и «Музыка» 0,75 по умолчанию — около −30,5 LUFS
        /// в миксе: удары и убийства (клипы −18 LUFS-M × 0,46–0,62) — на 5–8 дБ
        /// громче музыки, фон мобов (−20 × 0,4) — вровень. Тема лагеря (Camp_Music
        /// −23 LUFS, импорт с normalize — +7 дБ, слой 0,3) — около −29 LUFS: переход
        /// лагерь ↔ забег без скачка громкости. Клипы леса импортируются БЕЗ normalize.
        /// </summary>
        public static readonly TrackInfo[] Tracks =
        {
            // «Затишье»: 104 с с самого начала песни; петля — 1 такт.
            new TrackInfo(Track.Lull, "Forest_Lull", .5f, loopCrossfade: 2f),
            // «Чаща» v2: 132 с без тонкого вступления; петля — 1 такт.
            new TrackInfo(Track.NormalA, "Forest_Normal_A", .5f, loopCrossfade: 2f),
            // «Чаща» v2 (вторая): 86 с без вступления; петля — 2 такта.
            new TrackInfo(Track.NormalB, "Forest_Normal_B", .5f, loopCrossfade: 4f),
            // «Последние арены»: вся песня, её тихая концовка ложится на тихое начало; 2 такта.
            new TrackInfo(Track.Hard, "Forest_Hard", .5f, loopCrossfade: 4f),
            // «Вендиго»: без жуткого вступления, до концовки; петля — 1 такт.
            new TrackInfo(Track.Elite, "Forest_Elite", .5f, loopCrossfade: 2f),
            // Тема босса акта I: без вступления и последних 12 с; петля — 2 такта (8 долей).
            new TrackInfo(Track.Boss, "Forest_Boss", .5f, loopCrossfade: 3.6928f),
        };

        /// <summary>Что видно из забега на этом кадре. Заполняет CombatMusicView.</summary>
        public struct Signals
        {
            public Place Place;
            /// <summary>Тема лагеря или главного меню ещё звучит: бой не наслаивается на неё.</summary>
            public bool OtherMusicAudible;
            /// <summary>Номер забега сессии: сменился — позиции треков с начала.</summary>
            public int RunNumber;
            /// <summary>Глубина — номер арены с единицы.</summary>
            public int Depth;
            public bool Boss;
            /// <summary>У встречи арены есть элита (в любой волне).</summary>
            public bool Elite;
            /// <summary>Экран награды, маршрута или замены способности.</summary>
            public bool Choosing;
            /// <summary>Арена зачищена, герой идёт к выходу.</summary>
            public bool Cleared;
            public bool HeroDead;
            /// <summary>Живые враги, уже заметившие героя.</summary>
            public int AwakeHostiles;
            /// <summary>Меню паузы или другая системная пауза.</summary>
            public bool Paused;
        }

        /// <summary>Одна смена настроения или трека — для журнала.</summary>
        public readonly struct Transition
        {
            public readonly float Time;
            public readonly Mood FromMood;
            public readonly Track FromTrack;
            public readonly Mood ToMood;
            public readonly Track ToTrack;
            public readonly float Fade;
            public readonly Reason Reason;
            public readonly int Depth;

            public Transition(float time, Mood fromMood, Track fromTrack, Mood toMood, Track toTrack,
                float fade, Reason reason, int depth)
            {
                Time = time;
                FromMood = fromMood;
                FromTrack = fromTrack;
                ToMood = toMood;
                ToTrack = toTrack;
                Fade = fade;
                Reason = reason;
                Depth = depth;
            }
        }

        // Все массивы — один раз: Update зовётся каждый кадр и не мусорит.
        private readonly float[] _clipLength = new float[TrackCount];
        private readonly float[] _level = new float[TrackCount];
        private readonly float[] _target = new float[TrackCount];
        private readonly float[] _rate = new float[TrackCount];
        private readonly Status[] _status = new Status[TrackCount];
        private readonly bool[] _resetPending = new bool[TrackCount];
        private readonly int[] _primary = new int[TrackCount];
        private readonly bool[] _voiceActive = new bool[TrackCount * VoicesPerTrack];
        /// <summary>Голос петли стоит в очереди звуковых часов и ещё не звучит.</summary>
        private readonly bool[] _voicePending = new bool[TrackCount * VoicesPerTrack];
        private readonly float[] _voicePosition = new float[TrackCount * VoicesPerTrack];
        private readonly float[] _voiceStartAt = new float[TrackCount * VoicesPerTrack];
        private readonly float[] _voiceGain = new float[TrackCount * VoicesPerTrack];
        private readonly float[] _voiceTarget = new float[TrackCount * VoicesPerTrack];
        private readonly int[] _voiceSerial = new int[TrackCount * VoicesPerTrack];

        private int _runNumber = int.MinValue;
        private int _depth = int.MinValue;
        private int _combatDepth = int.MinValue;
        private float _quiet;
        private float _duck = 1f;
        /// <summary>Игрок вне забега: замолчавший трек останавливается, а не ждёт на паузе.</summary>
        private bool _release;

        public Mood CurrentMood { get; private set; }

        /// <summary>Трек настроения; в тишине — последний звучавший.</summary>
        public Track CurrentTrack { get; private set; }

        /// <summary>Секунды реального времени с создания — метка журнала.</summary>
        public float Clock { get; private set; }

        public float Duck => _duck;

        /// <summary>Сколько игровых секунд бой идёт без бодрствующих врагов.</summary>
        public float QuietSeconds => _quiet;

        public int TransitionCount { get; private set; }
        public Transition LastTransition { get; private set; }
        public int LoopCount { get; private set; }
        public Track LastLoopTrack { get; private set; }

        /// <summary>Трек боя арены.</summary>
        public static Track ChooseTrack(int depth, bool boss, bool elite)
        {
            if (boss) return Track.Boss;
            if (elite) return Track.Elite;
            if (depth >= FirstHardArena) return Track.Hard;
            // А1, А3, А5 — тема A; А2, А4, А6 — тема B.
            return depth % 2 == 0 ? Track.NormalB : Track.NormalA;
        }

        /// <summary>Длина перехода между настроениями.</summary>
        public static float FadeSeconds(Mood from, Mood to, bool heroDied)
        {
            if (to == Mood.Silence) return heroDied ? DeathToSilenceFade : ToSilenceFade;
            if (from == Mood.Silence) return to == Mood.Lull ? SilenceToLullFade : SilenceToCombatFade;
            if (to == Mood.Combat) return from == Mood.Combat ? CombatToCombatFade : LullToCombatFade;
            return CombatToLullFade;
        }

        /// <summary>Окно петли трека при такой длине клипа, с поправкой на короткий клип.</summary>
        public static void LoopWindow(Track track, float clipLength, out float start, out float end, out float crossfade)
        {
            TrackInfo info = Tracks[(int)track];
            end = info.LoopEnd > 0f && info.LoopEnd < clipLength ? info.LoopEnd : clipLength;
            if (end < 0f) end = 0f;
            start = Clamp(info.LoopStart, 0f, end * .5f);
            crossfade = info.LoopCrossfade > 0f ? info.LoopCrossfade : DefaultLoopCrossfade;
            crossfade = Math.Max(MinLoopCrossfade, Math.Min(crossfade, (end - start) * .5f));
        }

        /// <summary>Длина клипа; 0 — клипа нет, и трек молчит.</summary>
        public void SetClipLength(Track track, float seconds)
        {
            int t = (int)track;
            _clipLength[t] = seconds > 0f ? seconds : 0f;
            // Клип доехал, когда трек уже звали: начать его сейчас, в своей огибающей.
            if (_target[t] > 0f && _status[t] == Status.Stopped) StartTrack(t);
        }

        public float ClipLength(Track track) => _clipLength[(int)track];
        public Status StatusOf(Track track) => _status[(int)track];

        /// <summary>Огибающая перехода, линейная 0..1 (слышимая громкость — равная мощность от неё).</summary>
        public float LevelOf(Track track) => _level[(int)track];
        public float TargetOf(Track track) => _target[(int)track];
        public int PrimaryVoice(Track track) => _primary[(int)track];

        /// <summary>Позиция ведущего голоса — с неё трек продолжит после паузы.</summary>
        public float Position(Track track) => _voicePosition[(int)track * VoicesPerTrack + _primary[(int)track]];

        public bool VoiceActive(Track track, int voice) => _voiceActive[(int)track * VoicesPerTrack + voice];

        /// <summary>
        /// Позиция голоса по учёту режиссёра. У голоса в очереди (VoicePending) она
        /// меньше LoopStart ровно на время до точки петли.
        /// </summary>
        public float VoicePosition(Track track, int voice) => _voicePosition[(int)track * VoicesPerTrack + voice];

        /// <summary>
        /// Голос петли поставлен в очередь звуковых часов и ещё не звучит: источник
        /// стартует PlayScheduled с LoopStart в миг, когда ведущий голос дойдёт до
        /// LoopPoint. Громкость у такого голоса ноль, позицию источника он не берёт.
        /// </summary>
        public bool VoicePending(Track track, int voice) => _voicePending[(int)track * VoicesPerTrack + voice];

        /// <summary>Точка петли: позиция ведущего голоса, в которой второй входит с LoopStart.</summary>
        public static float LoopPoint(Track track, float clipLength)
        {
            LoopWindow(track, clipLength, out _, out float end, out float crossfade);
            return end - crossfade;
        }

        /// <summary>
        /// Растёт на каждый старт голоса с позиции (первый запуск, новая петля).
        /// Возврат с паузы его не меняет: источник снимается с паузы, а не
        /// запускается заново, — так трек и продолжает с того же места.
        /// </summary>
        public int VoiceStartSerial(Track track, int voice) => _voiceSerial[(int)track * VoicesPerTrack + voice];

        /// <summary>
        /// С какой позиции клипа запущен голос (последний старт): первый проход — 0,
        /// петля — LoopStart, проскочившая петля — LoopStart плюс проскочившее.
        /// Источник стартует отсюда, а не с учётной позиции: та уже ушла на кадр вперёд.
        /// </summary>
        public float VoiceStartPosition(Track track, int voice) => _voiceStartAt[(int)track * VoicesPerTrack + voice];

        /// <summary>Громкость голоса без ползунка «Музыка»: переход × петля × таблица × приглушение.</summary>
        public float VoiceVolume(Track track, int voice)
        {
            int t = (int)track, i = t * VoicesPerTrack + voice;
            if (_status[t] != Status.Playing || !_voiceActive[i]) return 0f;
            return EqualPower(_level[t]) * EqualPower(_voiceGain[i]) * Tracks[t].Gain * _duck;
        }

        /// <summary>
        /// Настоящая позиция играющего источника. Кадровое время и звуковые часы
        /// расходятся; петля и учёт паузы идут по звуку, а не по сумме кадров.
        /// </summary>
        public void SyncVoicePosition(Track track, int voice, float seconds)
        {
            int t = (int)track, i = t * VoicesPerTrack + voice;
            // Голос в очереди ещё стоит на LoopStart: его «позиция» — отсчёт до старта.
            if (_status[t] == Status.Playing && _voiceActive[i] && !_voicePending[i] && seconds >= 0f)
                _voicePosition[i] = seconds;
        }

        /// <param name="deltaSeconds">Реальное время кадра: переходы идут и в паузе, и в стоп-кадре удара.</param>
        /// <param name="gameDeltaSeconds">Игровое время кадра (тики боя): по нему меряется тишина между волнами.</param>
        public void Update(in Signals signals, float deltaSeconds, float gameDeltaSeconds)
        {
            float dt = deltaSeconds > 0f ? deltaSeconds : 0f;
            float gameDt = gameDeltaSeconds > 0f ? gameDeltaSeconds : 0f;
            Clock += dt;
            _release = signals.Place != Place.Run;

            if (signals.Place == Place.Run)
            {
                if (signals.RunNumber != _runNumber)
                {
                    _runNumber = signals.RunNumber;
                    BeginRun();
                }
                if (signals.Depth != _depth)
                {
                    _depth = signals.Depth;
                    _quiet = 0f;
                }
            }

            Mood mood = Decide(in signals, gameDt, out Track track, out Reason reason);
            if (mood != CurrentMood || (mood != Mood.Silence && track != CurrentTrack))
                Switch(mood, track, reason, signals.HeroDead, signals.Depth);

            float duck = signals.Paused ? PauseDuck : 1f;
            _duck = duck + (_duck - duck) * (float)Math.Exp(-dt * DuckSharpness);

            for (int t = 0; t < TrackCount; t++) Advance(t, dt);
        }

        private Mood Decide(in Signals signals, float gameDt, out Track track, out Reason reason)
        {
            track = CurrentTrack;
            if (signals.Place != Place.Run)
            {
                reason = signals.HeroDead ? Reason.HeroDied
                    : signals.Place == Place.Menu ? Reason.MainMenu
                    : signals.Place == Place.Summary ? Reason.Summary
                    : Reason.Camp;
                return Mood.Silence;
            }

            // Вход в забег: пока гаснет тема лагеря или меню, бой молчит, а не наслаивается.
            if (CurrentMood == Mood.Silence && signals.OtherMusicAudible)
            {
                reason = Reason.None;
                return Mood.Silence;
            }

            if (signals.HeroDead)
            {
                reason = Reason.HeroDied;
                return Mood.Silence;
            }

            if (signals.Choosing)
            {
                track = Track.Lull;
                reason = Reason.RewardScreen;
                return Mood.Lull;
            }

            // Бодрствующий враг — бой. И на пути к выходу: страж тайника, который
            // ещё дерётся, зачистку не отменяет, но драка идёт — и музыка с ней.
            if (signals.AwakeHostiles > 0)
            {
                _quiet = 0f;
                track = ChooseTrack(signals.Depth, signals.Boss, signals.Elite);
                reason = CurrentMood == Mood.Combat ? Reason.TrackChanged : Reason.HostileAwake;
                return Mood.Combat;
            }

            track = Track.Lull;
            if (signals.Cleared)
            {
                reason = Reason.ArenaCleared;
                return Mood.Lull;
            }

            // Никто не бодрствует, арена не зачищена: пауза между волнами. Бой
            // держится QuietBeforeLull игровых секунд — следующая волна успевает
            // встать, не сбив музыку.
            if (CurrentMood == Mood.Combat && _combatDepth == signals.Depth)
            {
                _quiet += gameDt;
                if (_quiet < QuietBeforeLull)
                {
                    track = CurrentTrack;
                    reason = Reason.None;
                    return Mood.Combat;
                }
                reason = Reason.QuietBetweenWaves;
                return Mood.Lull;
            }

            reason = Reason.ArenaStart;
            return Mood.Lull;
        }

        private void Switch(Mood mood, Track track, Reason reason, bool heroDied, int depth)
        {
            Mood fromMood = CurrentMood;
            Track fromTrack = CurrentTrack;
            float fade = FadeSeconds(fromMood, mood, heroDied);
            if (fromMood != Mood.Silence) FadeTo((int)fromTrack, 0f, fade);
            if (mood != Mood.Silence)
            {
                FadeTo((int)track, 1f, fade);
                CurrentTrack = track;
            }
            CurrentMood = mood;
            if (mood == Mood.Combat) _combatDepth = depth;
            _quiet = 0f;
            TransitionCount++;
            LastTransition = new Transition(Clock, fromMood, fromTrack, mood, CurrentTrack, fade, reason, depth);
        }

        /// <summary>
        /// Новый забег — треки с начала. Замолчавший трек останавливается сразу;
        /// ещё гаснущий (быстрый повтор с итогов) догасает и тогда встаёт в начало.
        /// </summary>
        private void BeginRun()
        {
            _depth = int.MinValue;
            _combatDepth = int.MinValue;
            _quiet = 0f;
            for (int t = 0; t < TrackCount; t++)
            {
                if (_status[t] == Status.Stopped) continue;
                if (_level[t] <= 0f && _target[t] <= 0f) StopTrack(t);
                else _resetPending[t] = true;
            }
        }

        private void FadeTo(int t, float target, float seconds)
        {
            _target[t] = target;
            _rate[t] = 1f / Math.Max(MinFade, seconds);
            if (target <= 0f) return;

            // Трек снова нужен: если он ждал сброса нового забега и уже молчит —
            // сначала в начало; если ещё звучит — продолжает без щелчка.
            if (_resetPending[t])
            {
                _resetPending[t] = false;
                if (_level[t] <= 0f) StopTrack(t);
            }
            if (_status[t] == Status.Paused) _status[t] = Status.Playing;
            else if (_status[t] == Status.Stopped) StartTrack(t);
        }

        private void StartTrack(int t)
        {
            // Клипа нет — трек молчит; огибающая и решения идут как шли.
            if (_clipLength[t] <= 0f) return;
            _primary[t] = 0;
            StartVoice(t, 0, 0f, 0f, 1f);
            _status[t] = Status.Playing;
        }

        /// <param name="startAt">С какой позиции клипа голос зазвучит.</param>
        /// <param name="countdown">Через сколько секунд: больше нуля — голос в очереди звуковых часов.</param>
        private void StartVoice(int t, int voice, float startAt, float countdown, float gain)
        {
            int i = t * VoicesPerTrack + voice;
            _voiceActive[i] = true;
            _voicePending[i] = countdown > 0f;
            _voicePosition[i] = startAt - Math.Max(0f, countdown);
            _voiceStartAt[i] = startAt;
            _voiceGain[i] = gain;
            _voiceTarget[i] = 1f;
            _voiceSerial[i]++;
        }

        private void DropVoice(int i)
        {
            _voiceActive[i] = false;
            _voicePending[i] = false;
            _voicePosition[i] = 0f;
            _voiceGain[i] = 0f;
            _voiceTarget[i] = 0f;
        }

        private void StopTrack(int t)
        {
            for (int v = 0; v < VoicesPerTrack; v++) DropVoice(t * VoicesPerTrack + v);
            _status[t] = Status.Stopped;
            _primary[t] = 0;
            _resetPending[t] = false;
        }

        private void Advance(int t, float dt)
        {
            _level[t] = MoveTowards(_level[t], _target[t], _rate[t] * dt);
            if (_status[t] != Status.Playing)
            {
                if (_status[t] == Status.Paused && (_resetPending[t] || _release)) StopTrack(t);
                return;
            }

            LoopWindow((Track)t, _clipLength[t], out float start, out float end, out float crossfade);
            int primary = t * VoicesPerTrack + _primary[t];
            int other = t * VoicesPerTrack + (1 - _primary[t]);
            float gainStep = dt / crossfade;
            for (int v = 0; v < VoicesPerTrack; v++)
            {
                int i = t * VoicesPerTrack + v;
                if (!_voiceActive[i]) continue;
                _voicePosition[i] += dt;
                // Голос в очереди молчит до своего сэмпла.
                if (_voicePending[i]) continue;
                _voiceGain[i] = MoveTowards(_voiceGain[i], _voiceTarget[i], gainStep);
                // Отыгравший голос петли: ушёл в ноль или дошёл до конца окна — дальше ему играть нечего.
                if (i != primary && _voiceTarget[i] <= 0f && (_voiceGain[i] <= 0f || _voicePosition[i] >= end))
                    DropVoice(i);
            }

            if (_voiceActive[other] && _voicePending[other])
            {
                // Второй голос дошёл до своего сэмпла: перекрёстная петля началась.
                if (_voicePosition[other] >= start)
                {
                    _voicePending[other] = false;
                    BeginLoopCrossfade(t, primary, other, (_voicePosition[other] - start) / crossfade);
                }
            }
            else if (_voiceActive[primary] && !_voiceActive[other])
            {
                // Петля: за LoopScheduleLead до точки петли второй голос встаёт в очередь
                // с LoopStart; его позиция — отсчёт: дойдёт до LoopStart ровно в точке петли.
                float loopAt = end - crossfade;
                float lead = Math.Min(LoopScheduleLead, Math.Max(0f, loopAt - start - crossfade));
                float until = loopAt - _voicePosition[primary];
                if (until <= lead)
                {
                    if (until > 0f) StartVoice(t, 1 - _primary[t], start, until, 0f);
                    else
                    {
                        // Кадр проскочил точку петли (провал кадра, короткий клип): второй голос
                        // сразу и дальше от LoopStart на проскочившее — в ту же долю.
                        StartVoice(t, 1 - _primary[t], start - until, 0f, 0f);
                        BeginLoopCrossfade(t, primary, other, -until / crossfade);
                    }
                }
            }

            // Ушёл в ноль — пауза с позицией; в новом забеге и вне забега — остановка в начало.
            if (_level[t] <= 0f && _target[t] <= 0f)
            {
                if (_resetPending[t] || _release)
                {
                    StopTrack(t);
                    return;
                }
                _status[t] = Status.Paused;
                // Голос в очереди снимается (источник — Stop): после паузы петля встанет
                // в очередь заново, от настоящей позиции ведущего голоса.
                int queued = t * VoicesPerTrack + (1 - _primary[t]);
                if (_voicePending[queued]) DropVoice(queued);
            }
        }

        /// <summary>Равная мощность на двух голосах: старый уходит, новый входит за crossfade.</summary>
        private void BeginLoopCrossfade(int t, int oldVoice, int newVoice, float into)
        {
            into = Clamp(into, 0f, 1f);
            _voiceTarget[oldVoice] = 0f;
            _voiceGain[oldVoice] = Math.Min(_voiceGain[oldVoice], 1f - into);
            _voiceGain[newVoice] = into;
            _primary[t] = newVoice - t * VoicesPerTrack;
            LoopCount++;
            LastLoopTrack = (Track)t;
        }

        /// <summary>Равная мощность: перекрёстный переход не проседает посередине.</summary>
        public static float EqualPower(float x) => (float)Math.Sin(Clamp(x, 0f, 1f) * (Math.PI * .5));

        private static float MoveTowards(float current, float target, float step)
        {
            if (current < target) return current + step >= target ? target : current + step;
            if (current > target) return current - step <= target ? target : current - step;
            return target;
        }

        private static float Clamp(float value, float min, float max)
            => value < min ? min : value > max ? max : value;
    }
}
