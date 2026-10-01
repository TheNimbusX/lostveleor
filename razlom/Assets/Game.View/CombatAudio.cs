using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Sound = Game.View.CombatSound;

namespace Game.View
{
    /// <summary>
    /// Сведение подтверждённых событий боя из записей владельца и прежних банков.
    /// Звук следует за симуляцией и не определяет урон.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1100)]
    public sealed class CombatAudio : MonoBehaviour
    {
        private static readonly float AttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;
        [Header("Mix")]
        [Range(0f, 1f)] public float Master = 0.72f;
        [Range(0f, 1f)] public float WhooshVolume = 0.40f;
        [Range(0f, 1f)] public float MetalVolume = 0.34f;
        [Range(0f, 1f)] public float BodyVolume = 0.52f;
        [Range(0f, 1f)] public float KillVolume = 0.56f;
        [Range(0f, 1f)] public float DissolveVolume = 0.44f;
        [Range(0f, 1f)] public float WhirlwindVolume = 0.60f;
        [Range(0f, 1f)] public float CastVolume = 0.50f;
        [Range(0f, 1f)] public float RewardVolume = 0.60f;
        [Range(0f, 1f)] public float AbilityVolume = 0.58f;
        [Range(0f, 1f)] public float FootstepVolume = 0.30f;

        [Header("Враги")]
        [Tooltip("Сигнал в начале крупного телеграфа: таран, коготь, прыжок, вой, залп.")]
        [Range(0f, 1f)] public float WarningVolume = 0.46f;
        [Tooltip("Тихий взмах в начале обычного замаха хранителя.")]
        [Range(0f, 1f)] public float EnemySwingVolume = 0.16f;
        [Range(0f, 1f)] public float BudVolume = 0.44f;
        [Tooltip("Земля под встающими из неё и уходящими в неё врагами (волны встречи, конец выживания).")]
        [Range(0f, 1f)] public float EarthVolume = 0.42f;

        [Header("Мобы леса")]
        [Tooltip("Разовые звуки мобов: взмахи, укусы, шипы, плевки. Клипы сведены к -18 LUFS, как HitBody.")]
        [Range(0f, 1f)] public float MobVolume = 0.46f;
        [Tooltip("Голос боли моба — слой под ударом сабли, раз в 0,35 с на моба.")]
        [Range(0f, 1f)] public float MobHurtVolume = 0.30f;
        [Range(0f, 1f)] public float MobDeathVolume = 0.50f;
        [Tooltip("Общий слоёный удар убийства; у крупных мобов громче и ниже.")]
        [Range(0f, 1f)] public float KillImpactVolume = 0.62f;
        [Tooltip("Удар моба по герою: когти, клыки, таран, шип.")]
        [Range(0f, 1f)] public float HeroImpactVolume = 0.60f;
        [Tooltip("Оглушение и корни на герое.")]
        [Range(0f, 1f)] public float HeroControlVolume = 0.55f;
        [Tooltip("Фон: топот роя, шипение лужи, галоп тарана, лечение Корнехвата (клипы -20 LUFS).")]
        [Range(0f, 1f)] public float MobBedVolume = 0.40f;

        [Header("Шаги")]
        [Tooltip("Сколько метров проходит герой между шагами.")]
        [Min(0.4f)] public float FootstepDistance = 1.35f;

        [Header("Density")]
        [Tooltip("AoE contacts in one frame are mixed into one readable impact.")]
        [Min(1)] public int MaxPerKindPerFrame = 1;
        // 14 → 18 (29.09): у мобов леса появились свои взмахи, боль, смерти и удар убийства.
        [Min(4)] public int Voices = 18;

        public CombatAudioProfile Profile;
        // Volume < 0 — звук смерти по старому правилу (громкость по банку, высота 1).
        // Cause — чем звук поставлен в очередь (журнал съёмки -capture-audio-log).
        private struct DelayedCue { public float Due; public Sound Sound; public float Volume, Pitch, Spread; public CauseInfo Cause; }
        private readonly DelayedCue[] _deathCues = new DelayedCue[64];
        private int _deathCueCount;
        private float _anchorImpactAt = -1f, _anchorLandAt = -1f;
        private CombatVoiceBudget _voiceBudget;
        private float _whirlwindEndAt = -1f;

        /// <summary>
        /// Импульс удержания Вихря: тот же свист, чуть выше и тише, и конец
        /// сдвигается за последний оборот. Вызывает контроллер VFX по тику Sim —
        /// у симуляции нет события импульса.
        /// </summary>
        public void PlayWhirlwindPulse()
        {
            if (Profile == null) return;
            SetCause(Cause.WhirlwindPulse);
            Play(Sound.WhirlwindPulse, WhirlwindVolume * .9f, 1f, .03f, 0f);
            _whirlwindEndAt = Time.time + Simulation.WhirlwindPulseTicks / (float)Simulation.TicksPerSecond + .18f;
        }
        private bool _chainSoundActive;
        // Чей бой звучит: режим, симуляция и арена забега (баг 29.09 — см. CombatSoundScope).
        private readonly CombatSoundScope _scope = new CombatSoundScope();
        private readonly CombatSoundEntry[] _entries = new CombatSoundEntry[(int)Sound.Count];

        private TickDriver _driver;
        private AudioSource[] _voices;
        private Sound[] _voiceSounds;
        private readonly AudioClip[][] _variants = new AudioClip[(int)Sound.Count][];
        private readonly int[] _lastVariant = new int[(int)Sound.Count];
        private readonly int[] _playedThisFrame = new int[(int)Sound.Count];
        private uint _random = 0x2545F491u;
        private float _whooshDelay = -1f;
        private int _whooshAttackVariant;
        private struct BasicWhooshCue
        {
            public PelagBasicAttackState Action;
            public CauseInfo Cause;
        }
        private readonly List<BasicWhooshCue> _basicWhooshes = new List<BasicWhooshCue>(4);
        private float[] _voiceGains, _fadeLeft;
        private bool _paused, _blazePreparing, _blazeBurning;
        private double _pausedAt;
        private float _finisherReadyAt;
        private float _warningReadyAt;
        // Три бутона, раскрывшиеся почти разом, дают один сигнал, а не хор.
        private const float WarningSpacing = .30f;
        // Волна встаёт из земли десятком тел за тик — земля звучит одним разом на волну.
        private float _earthReadyAt;
        private const float EarthSpacing = .45f;

        // ---- мобы леса ----
        //
        // Звук, чей пик должен лечь на тик контакта (взмах, взлёт, вой, галоп с
        // разгона), ждёт в _mobCues до своего тика. Каждый кадр он сверяется с
        // Sim по номеру действия: снятое действие (оглушение, смерть, волок)
        // молча выпадает, и свист когтей не звучит над сбитым замахом.
        private enum MobCueKind : byte { Swing, Tusk, Wendigo, Stonehoof }
        private struct MobCue
        {
            public MobCueKind Kind;
            public int Entity, Serial;
            public float Tick, Volume, Pitch;
            public Sound Sound;
            public bool Owned;
            public CauseInfo Cause;
        }
        private readonly MobCue[] _mobCues = new MobCue[32];
        private int _mobCueCount;

        // Длинные звуки, которые гасит событие своего моба: галоп — остановка тарана,
        // лечение и перекат — снятие, вой — снятый вой, шипение — ушедшая лужа.
        private struct OwnedVoice { public int Owner, Slot; public Sound Sound; }
        private readonly OwnedVoice[] _owned = new OwnedVoice[16];
        private int _ownedCount;

        private float[] _hurtReadyAt = new float[64];
        private float _hurtAnyReadyAt, _swarmDeathReadyAt, _killImpactReadyAt;
        // Топот роя: только пока Sim шагает и только от бегущих в кадре (баг 29.09, SwarmScuttleClock).
        private readonly SwarmScuttleClock _scuttle = new SwarmScuttleClock();
        // Удар убийства этого кадра: 0 — нет, 1 — мелкий моб, 2 — крупный. Один на кадр, крупный важнее.
        private int _killImpactThisFrame;
        private CauseInfo _killImpactCause;
        private const float HurtSpacing = .35f, HurtAnySpacing = .06f, KillImpactSpacing = .09f;

        // ---- журнал съёмки (-capture-audio-log) ----
        //
        // Каждый сыгранный боевой звук — строкой [audio-log] в журнал плеера с причиной:
        // событие Sim (тип, кто, на ком, тик), ожидание моба, отложенный звук, фон или
        // шаг. Причина ставится перед Play и едет вместе с отложенными звуками. Звук, для
        // которого не нашлось голоса, — строкой drop; сбросы на смене боя, отброшенные
        // события кадра смены, заглушённый топот и прочие источники звука сцены
        // (source-start/stop, LogOtherSources) — своими строками. Без флага строк нет;
        // копия причины — пара присваиваний структуры на событие.
        private enum Cause : byte
        {
            None, Event, MobCue, Scuttle, Footstep, Whoosh, Cleave, Anchor, WhirlwindPulse, WhirlwindEnd, ChainEnd,
        }
        private struct CauseInfo
        {
            public Cause Kind;
            public SimEvent Event;
            // Тик события (FrameEventContext.SimulationTick).
            public int Tick;
            // Ожидание моба: вид ожидания, номер действия и тик, к которому звук ждал; фон: сколько бегущих.
            public int Detail, Serial, DueTick;
            // Отложенный звук: когда поставлен (Time.time), -1 — не откладывался.
            public float QueuedAt;
        }
        private CauseInfo _cause;
        private float _scuttleLogAt;

        // ---- шаги ----
        //
        // Считаются по ПРОЙДЕННОМУ ПУТИ, а не по таймеру. Таймер отвязан от
        // скорости: замедленный герой продолжал бы частить, ускоренный —
        // скользить беззвучно. Путь даёт постоянную длину шага при любой
        // скорости, и это ровно то, что слышно как походка.
        private Vector2 _stepAnchor;
        private bool _stepAnchorSet;
        private int _stepPart;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            for (int i = 0; i < _lastVariant.Length; i++) _lastVariant[i] = -1;
        }

        private void Start()
        {
            if (Profile == null) Profile = Resources.Load<CombatAudioProfile>("Combat/CombatAudio");
            LoadClips();
            BuildVoices();
            if (CaptureRig.AudioLog) LogLine("on voices=" + _voices.Length + " profile=" + (Profile != null ? Profile.name : "-"));
        }

        private void LateUpdate()
        {
            if (_voices == null) return;
            if (CaptureRig.AudioLog) LogOtherSources();
            bool paused = _driver.GameplayPaused || Time.timeScale == 0f;
            if (paused != _paused)
            {
                _paused = paused;
                if (paused) _pausedAt = AudioSettings.dspTime;
                else _voiceBudget.Shift(AudioSettings.dspTime - _pausedAt);
                foreach (var voice in _voices) { if (paused) voice.Pause(); else voice.UnPause(); }
                // Голоса боя на паузе стоят — и сердцебиение с ними (StopHeartbeat).
                if (paused) StopHeartbeat("pause");
            }
            UpdateVoiceMix();
            if (paused) return;
            for (int i = 0; i < _playedThisFrame.Length; i++) _playedThisFrame[i] = 0;

            CombatSoundScope.Change change = ScopeChange();
            UpdateWhoosh();
            if (_driver.Sim != null)
            {
            // Кадр смены симуляции: его события — прежней Sim (тики до смены внутри цикла
            // TickDriver) или повтор давнего списка новой (SyncGeneration на возврате в лагерь:
            // SetupTestArena список лагерной Sim не чистит, и в нём лежит её последний шаг
            // перед забегом). У новой Sim в этом кадре — только расстановка, ей звучать нечем.
            if (CombatSoundScope.HearsFrameEvents(change)) ConsumeEvents();
            else if (CaptureRig.AudioLog && _driver.FrameEvents.Count > 0)
                LogLine("dropped " + _driver.FrameEvents.Count + " events of the simulation swap frame");
            FlushKillImpact();
            UpdateBasicWhooshes();
            UpdateMobCues();
            UpdateSwarmScuttle();
            UpdateCleaveSound();
            UpdateBlazeSound();
            if (_anchorImpactAt >= 0f && Time.time >= _anchorImpactAt)
            {
                _anchorImpactAt = -1f;
                SetCause(Cause.Anchor);
                Play(Sound.HitMetal, MetalVolume * .85f, .78f, .02f);
                Play(Sound.HitBody, BodyVolume * .55f, .82f, .02f);
            }
            if (_anchorLandAt >= 0f && Time.time >= _anchorLandAt)
            {
                _anchorLandAt = -1f;
                SetCause(Cause.Anchor);
                PlayFootstepPart();
            }
                UpdateFootsteps();
            }

            // ПОСЛЕ ConsumeEvents. Смерть этого кадра ставится в очередь на
            // без малого секунду вперёд, так что раньше следующего кадра
            // сработать всё равно не может, — а вот вчерашние очереди должны
            // успеть прозвучать до того, как кадр закончится.
            FlushDissolves();
            if (_whirlwindEndAt >= 0f && Time.time >= _whirlwindEndAt)
            {
                _whirlwindEndAt = -1f;
                SetCause(Cause.WhirlwindEnd);
                Play(Sound.WhirlwindEnd, AbilityVolume * .5f, 1f, .01f);
            }
            bool chain = _driver.Sim != null && _driver.Sim.ChainTargetId >= 0;
            if (_chainSoundActive && !chain)
            {
                StopKind(Sound.ChainStep);
                SetCause(Cause.ChainEnd);
                Play(Sound.ChainStepEnd, AbilityVolume * .5f, 1f, .01f);
            }
            _chainSoundActive = chain;
        }

        private int _cleaveSoundCast = -1;
        private bool _cleaveSoundPlayed;
        private void UpdateCleaveSound()
        {
            var sim = _driver.Sim;
            if (sim == null || !sim.CleaveActive) { _cleaveSoundCast = -1; return; }
            if (_cleaveSoundCast != sim.CleaveStartTick)
            { _cleaveSoundCast = sim.CleaveStartTick; _cleaveSoundPlayed = false; }
            // Пик новой записи находится через 0,25 с после начала; совмещаем с контактом.
            if (!_cleaveSoundPlayed && sim.Tick - 1 + _driver.Alpha >= Mathf.Max(sim.CleaveStartTick,
                sim.CleaveContactTick - .25f * Simulation.TicksPerSecond))
            {
                _cleaveSoundPlayed = true;
                SetCause(Cause.Cleave);
                Play(Sound.Cleave, AbilityVolume, 1f, .01f);
            }
        }

        /// <summary>
        /// Сменился ли бой: режим, симуляция или арена забега (CombatSoundScope). Сброс —
        /// по виду смены; ответ решает, звучат ли события этого кадра.
        /// </summary>
        private CombatSoundScope.Change ScopeChange()
        {
            GameSession session = _driver.Session;
            if (session == null) return CombatSoundScope.Change.None;
            RiftRun run = session.Run;
            CombatSoundScope.Change change = _scope.Update(session.Mode, session.Generation, run != null ? run.Depth : -1);
            if (change == CombatSoundScope.Change.None) return change;
            if (CaptureRig.AudioLog)
                LogLine("reset " + change + " delayed dropped=" + _deathCueCount + " mob cues dropped=" + _mobCueCount
                    + " playing=" + PlayingVoices());
            if (CombatSoundScope.EndsFight(change)) ResetFight();
            else ResetArena();
            return change;
        }

        /// <summary>Смена режима или симуляции обрывает бой на середине.</summary>
        private void ResetFight()
        {
            _basicWhooshes.Clear();
            // Осыпание, поставленное в очередь за долю секунды до выхода из Разлома,
            // прозвучало бы уже в лагере — над пустой поляной, без тела.
            _deathCueCount = 0;
            _whirlwindEndAt = -1f;
            _chainSoundActive = false;
            _whooshDelay = -1f;
            _stepAnchorSet = false;
            _blazePreparing = _blazeBurning = false;
            _finisherReadyAt = _warningReadyAt = _earthReadyAt = 0f;
            for (int i = 0; i < _voices.Length; i++) _voices[i].Stop();
            _voiceBudget.Clear();
            _anchorImpactAt = _anchorLandAt = -1f;
            ResetMobState();
            StopHeartbeat("fight ended");
            // Конец забега звучит в RunEndBeat: у смерти, победы и ухода свои фразы, а не общий звон награды.
        }

        /// <summary>
        /// Следующая арена забега — та же Sim и то же поколение (RiftRun.EnterNextRift
        /// расставляет сущности заново). Номера сущностей и действий на новой арене
        /// начинаются заново: ожидания мобов, хозяева длинных звуков и отложенные звуки
        /// прошлой арены к ней не относятся. Голоса не гасим: смена идёт под дымной
        /// завесой, и прошлая арена к ней уже отзвучала.
        /// </summary>
        private void ResetArena()
        {
            _basicWhooshes.Clear();
            _deathCueCount = 0;
            _whooshDelay = -1f;
            _anchorImpactAt = _anchorLandAt = -1f;
            _stepAnchorSet = false;
            ResetMobState();
        }

        /// <summary>
        /// Сердцебиение низкого здоровья — петля GameSound, её каждый кадр ставит и снимает
        /// боевой HUD (CombatHudView.RefreshHero), пока он виден. Спрятанный HUD её не
        /// снимает, а прячется он как раз там, где боя нет: смерть в тот же тик кончает
        /// забег — итоги прячут HUD, и стук шёл весь экран итогов (баг 29.09); уход и
        /// победа на низком здоровье — так же; на паузе — поверх стоящих голосов боя.
        /// Гасим вместе с голосами боя: если HUD виден и здоровья всё ещё мало, он
        /// включит петлю снова со следующего кадра.
        /// </summary>
        private void StopHeartbeat(string why)
        {
            GameSound.Loop(null);
            if (CaptureRig.AudioLog) LogLine("heartbeat loop off: " + why);
        }

        private void ConsumeEvents()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            IReadOnlyList<FrameEventContext> contexts = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                _cause = new CauseInfo
                {
                    Kind = Cause.Event, Event = e, QueuedAt = -1f,
                    Tick = i < contexts.Count ? contexts[i].SimulationTick : _driver.Sim.Tick,
                };
                // Flush elapsed frozen windups before a later event can cancel the action.
                // Reading only final Sim here would lose a hit+cancel inside one rendered frame.
                // FrameEventContext is captured after Step increments Tick; action/event clocks use the completed tick.
                FlushBasicWhooshes(_cause.Tick - 1, false);
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        if (e.Source == Simulation.PlayerId)
                        {
                            if (e.BasicAttackState.Serial > 0)
                            {
                                _whooshDelay = -1f;
                                _basicWhooshes.Add(new BasicWhooshCue { Action = e.BasicAttackState, Cause = _cause });
                                break;
                            }
                            // Whoosh leads the shared contact tick; keeping the
                            // lead relative to Simulation avoids drift when the
                            // attack windup is tuned.
                            _whooshDelay = (_driver.Sim.PlayerAttackWindupTicks / (float)Simulation.TicksPerSecond) *
                                (e.ActionVariant == 1 ? 0.50f : 0.55f);
                            _whooshAttackVariant = e.ActionVariant;
                        }
                        // Обычный замах — не крупный телеграф: общий сигнал на каждый удар
                        // приучил бы его не слушать. Свой звук замаха — PlayEnemySwingStart.
                        else PlayEnemySwingStart(in e);
                        break;

                    // Сигнал — только в начале крупных телеграфов: таран Камнекопыта,
                    // коготь, прыжок и вой Вендиго (все идут через WendigoStarted), залп бутона.
                    // Поверх сигнала — голос самого действия (храп и галоп, свист, взлёт, вой).
                    case SimEventType.StonehoofStarted:
                        PlayWarning();
                        PlayStonehoofStart(in e);
                        break;
                    case SimEventType.WendigoStarted:
                        PlayWarning();
                        PlayWendigoStart(in e);
                        break;
                    // Таран кончился: галоп гаснет; удар о ствол или камень (Flag) — тяжёлый глухой удар.
                    case SimEventType.StonehoofStopped:
                        FadeOwned(e.Source, Sound.StonehoofCharge);
                        if (e.Flag) Cue(Sound.StonehoofCollision, MobVolume * 1.1f, .92f, .03f);
                        break;
                    case SimEventType.StonehoofCancelled:
                        FadeOwned(e.Source, Sound.StonehoofCharge);
                        break;
                    // Приземление прыжка Вендиго; удар когтя по герою звучит его Damage.
                    case SimEventType.WendigoImpact:
                        if (e.ActionVariant == (int)WendigoAction.Leap) Cue(Sound.WendigoLand, MobVolume * 1.1f, 1f, .03f);
                        break;
                    case SimEventType.WendigoCancelled:
                        FadeOwned(e.Source, Sound.WendigoHowl);
                        break;
                    // Контроль героя: корни (Flag) или оглушение.
                    case SimEventType.HeroControl:
                        if (e.Target == Simulation.PlayerId && !e.Flag) _basicWhooshes.Clear();
                        Cue(e.Flag ? Sound.HeroRooted : Sound.HeroStunned, HeroControlVolume, 1f, .02f);
                        break;
                    // Кислая лужа легла — шипит, пока не уйдёт (вытесненная гаснет раньше).
                    case SimEventType.PuddleOpened:
                    {
                        int slot = Play(Sound.BudPuddle, MobBedVolume, 1f, .04f);
                        if (slot >= 0) Own(PuddleOwner(e.Amount), Sound.BudPuddle, slot);
                        break;
                    }
                    case SimEventType.PuddleClosed:
                        FadeOwned(PuddleOwner(e.Amount), Sound.BudPuddle);
                        break;
                    // Новые мобы леса: линия шипов и всплеск Шипомёта, удар корнями Корнехвата
                    // и круг когтей Вендиго — общий сигнал; разбор по видам — PlayEnemyActionStart.
                    case SimEventType.EnemyActionStarted:
                        PlayEnemyActionStart(in e);
                        break;
                    // Лечение и перекат сбиты: их длинные звуки гаснут. Клыки и круг когтей
                    // сняты — их свист выпадает из ожидания сам (UpdateMobCues).
                    case SimEventType.EnemyActionCancelled:
                        if (e.ActionVariant == (int)EnemyActionKind.SnarerMend) FadeOwned(e.Source, Sound.SnarerMend);
                        else if (e.ActionVariant == (int)EnemyActionKind.SplitterRoll) FadeOwned(e.Source, Sound.SplitterRoll);
                        break;
                    // Шип сорвался с руки Шипомёта (кадр 21 клипа, снятый до выпуска выстрел
                    // события не шлёт): свист стрелы из записи владельца.
                    case SimEventType.EnemyProjectileLaunched:
                        if (e.ActionVariant == (int)EnemyActionKind.ThornShot)
                            Cue(Sound.ThornShot, MobVolume * .8f, 1f, .04f);
                        break;
                    case SimEventType.EnemyActionImpact:
                        PlayEnemyImpact(in e);
                        break;
                    // Корнехват вбил лапы в землю — круг встал: тяжёлый удар по земле.
                    case SimEventType.TelegraphOpened:
                        if (IsKind(e.Source, EnemyKind.ForestRootSnarer))
                            Cue(Sound.SnarerSlam, MobVolume * 1.1f, 1f, .03f);
                        break;
                    // Расщепень распался (через 0,4 с после смерти): скорлупа лопается, детёныши
                    // выскакивают один за другим. Трещина по телу прозвучала на самой смерти.
                    case SimEventType.SplitterSplit:
                        Cue(Sound.SplitterCrack, MobVolume, 1f, .03f);
                        Cue(Sound.SplitlingPop, MobVolume * .7f, 1.05f, .05f, .06f);
                        Cue(Sound.SplitlingPop, MobVolume * .6f, 1.2f, .05f, .16f);
                        break;

                    case SimEventType.ForestBudVolleyStarted:
                        PlayWarning();
                        Play(Sound.BudVolley, BudVolume * .9f, 1f, .03f);
                        break;
                    // Хлопок — по вылету каждого плода, а не склейкой от начала залпа:
                    // хит-стоп, оглушение и уход героя из дальности не разводят звук с плодами.
                    // Выстрел горохострела из выбора 29.09 (-18 LUFS, прежние хлопки были -24) — тише в миксе.
                    case SimEventType.ForestFruitLaunched:
                        Play(Sound.BudPop, BudVolume * .5f, 1f, .02f, fixedVariant: e.ActionVariant);
                        break;
                    // То же событие, по которому ForestBudImpactView ставит брызги.
                    case SimEventType.ForestFruitImpact:
                        Play(Sound.BudFruitImpact, BudVolume * .6f, 1f, .04f);
                        // Гнилой плод (Flag): мокрый шлепок ниже — лопается в кислую лужу.
                        if (e.Flag) Cue(Sound.Dissolve, EarthVolume * .9f, .62f, .04f);
                        break;
                    // Оглушённый или убитый бутон не дораскрывается. Гасится раскрытие всех
                    // бутонов разом — два залпа в одну долю секунды почти не встречаются.
                    case SimEventType.ForestBudVolleyCancelled:
                        FadeKind(Sound.BudVolley);
                        break;

                    // Выход из-под земли (поздняя волна, подмога босса) и уход в неё (конец выживания).
                    case SimEventType.Spawn:
                        if (e.Flag) PlayEarth(false);
                        break;
                    case SimEventType.Burrowed:
                        PlayEarth(true);
                        break;

                    case SimEventType.Damage:
                        PlayDamage(in e);
                        break;

                    case SimEventType.Evaded:
                        if (e.Source == Simulation.PlayerId) _basicWhooshes.Clear();
                        break;
                    case SimEventType.Stun:
                        if (e.Target == Simulation.PlayerId) _basicWhooshes.Clear();
                        break;
                    case SimEventType.ArtifactUsed:
                        if (e.Source == Simulation.PlayerId && e.ActionVariant == (int)RunArtifact.VoidVisage)
                            _basicWhooshes.Clear();
                        break;

                    case SimEventType.Death:
                        if (e.Target == Simulation.PlayerId)
                        { _anchorImpactAt = _anchorLandAt = -1f; _basicWhooshes.Clear(); }
                        if (e.Target != Simulation.PlayerId)
                        {
                            var kind = _driver.Sim.Entities.Kind[e.Target];
                            // Один акцент на группу смертей, без трёх полных слоёв поверх него.
                            if (Time.time >= _finisherReadyAt)
                            {
                                Play(Sound.Finisher, KillVolume, 1f, .02f);
                                _finisherReadyAt = Time.time + .10f;
                            }
                            // Слоёный удар убийства — один на кадр, крупный моб важнее (FlushKillImpact).
                            int size = IsBigMob(kind) ? 2 : 1;
                            if (size > _killImpactThisFrame) _killImpactCause = _cause;
                            _killImpactThisFrame = Mathf.Max(_killImpactThisFrame, size);
                            QueueDeathSounds(kind, e.Target);
                        }
                        break;

                    case SimEventType.AbilityCast:
                    case SimEventType.ActionStageStarted:
                        if (e.Source == Simulation.PlayerId)
                        {
                            _basicWhooshes.Clear();
                            _anchorImpactAt = _anchorLandAt = -1f;
                            _whirlwindEndAt = -1f;
                            StopKind(Sound.Whirlwind);
                            _whooshDelay = -1f;
                            FadeKind(Sound.PelagAttack);
                            FadeKind(Sound.Cleave);
                            FadeKind(Sound.BlazePrepare);
                            _cleaveSoundCast = -1;
                            if (_driver.Sim.GetAbility(e.Amount)?.DefinitionId == AbilityDefinition.CleaveId) break;
                            if (_driver.Sim.GetAbility(e.Amount)?.DefinitionId == AbilityDefinition.AnchorLeapId)
                            {
                                // Даже промах имеет контакт с землёй; попадания во врагов звучат по Damage.
                                _anchorImpactAt = Time.time + PelagAbilityTiming.LeapWindup;
                                _anchorLandAt = Time.time + (_driver.Sim.PlayerAction.ContactTick - _driver.Sim.Tick + 1) / (float)Simulation.TicksPerSecond;
                            }
                            if (IsWhirlwindSlot(e.Amount))
                            {
                                // Whirlwind cancels a primed basic attack in
                                // Sim. Cancel its delayed whoosh too, otherwise
                                // the old swing lands acoustically inside the
                                // ability and makes the next combo feel late.
                                _whooshDelay = -1f;
                                // Пик записи 0,21 с совмещается с контактом Вихря.
                                Play(Sound.Whirlwind, WhirlwindVolume, 1f, 0f,
                                    Mathf.Max(0f, (_driver.Sim.PlayerAction.ContactTick - _driver.Sim.PlayerAction.StartTick) / (float)Simulation.TicksPerSecond - .21f));
                                _whirlwindEndAt = Time.time + CharacterAnimatorView.WhirlwindClipDuration;
                            }
                            else
                            {
                                // У Подсечки и Шага по цепи теперь свои записи.
                                // У Броска якоря записи нет — он берёт общий
                                // Cast, поднятый по тону: рывок должен звучать
                                // суше и выше волока, иначе две способности на
                                // одной цепи сливаются на слух.
                                Sound sound = AbilitySound(e.Amount);
                                Play(sound,
                                    sound == Sound.Cast ? CastVolume : AbilityVolume,
                                    sound == Sound.Cast ? AbilityCastPitch(e.Amount) : 0.98f,
                                    0.03f);
                            }
                        }
                        break;
                    case SimEventType.BlazeBegin:
                        if (e.Source == Simulation.PlayerId)
                        {
                            FadeKind(Sound.BlazePrepare);
                            StopKind(Sound.BlazeFire);
                            Play(Sound.BlazeFire, AbilityVolume, 1f, 0f);
                            _blazeBurning = true;
                        }
                        break;
                    case SimEventType.BackblastBurst:
                    case SimEventType.FlaskBurst:
                        if (e.Source == Simulation.PlayerId) Play(Sound.BlazeFire, AbilityVolume * .75f, 1.1f, 0f);
                        break;
                }
            }
        }

        /// <summary>
        /// Трёхкомпонентная поступь: pt1 → pt2 → pt3 → pt1.
        ///
        /// Части идут ПОДРЯД, а не случайно. Владелец сдал их как один звук
        /// ходьбы, где части продолжают друг друга; случайный выбор из трёх
        /// превратил бы связную поступь в дробь.
        /// </summary>
        private void UpdateFootsteps()
        {
            Simulation sim = _driver.Sim;
            GameSession session = _driver.Session;
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;

            bool walking = session != null
                           && session.Mode == GameMode.Rift
                           && e.Alive[player]
                           && e.Velocity[player].LengthSq.Raw != 0
                           && e.ForcedTicksLeft[player] <= 0;

            if (!walking)
            {
                // Якорь сбрасывается на остановке, чтобы первый шаг после
                // паузы звучал сразу, а не через полтора метра.
                _stepAnchorSet = false;
                return;
            }

            Vector2 here = new Vector2(
                e.Position[player].X.ToFloat(), e.Position[player].Y.ToFloat());

            if (!_stepAnchorSet)
            {
                _stepAnchor = here;
                _stepAnchorSet = true;
                return;
            }

            float step = Mathf.Max(0.4f, FootstepDistance);
            if ((here - _stepAnchor).sqrMagnitude < step * step) return;

            _stepAnchor = here;
            SetCause(Cause.Footstep);
            PlayFootstepPart();
        }

        private void PlayFootstepPart()
        {
            AudioClip[] parts = _variants[(int)Sound.Footstep];
            if (parts == null || parts.Length == 0) return;

            AudioClip clip = parts[_stepPart % parts.Length];
            _stepPart++;
            if (clip == null) return;

            Play(Sound.Footstep, FootstepVolume, 1f, 0.02f, fixedVariant: (_stepPart - 1) % parts.Length);
        }

        private void UpdateWhoosh()
        {
            if (_whooshDelay < 0f) return;
            var sim = _driver.Sim;
            if (sim == null || !sim.Entities.Alive[Simulation.PlayerId]
                || sim.Entities.AttackImpactTick[Simulation.PlayerId] <= sim.Tick)
            { _whooshDelay = -1f; return; }
            _whooshDelay -= Time.deltaTime;
            if (_whooshDelay > 0f) return;

            _whooshDelay = -1f;
            bool heavy = _whooshAttackVariant == 1;
            SetCause(Cause.Whoosh);
            Play(Sound.PelagAttack, WhooshVolume * (heavy ? 1.10f : 1f),
                heavy ? .96f : 1.03f, .025f);
        }

        private void UpdateBasicWhooshes()
        {
            var sim = _driver.Sim;
            if (sim == null || !sim.Entities.Alive[Simulation.PlayerId])
            { _basicWhooshes.Clear(); return; }
            FlushBasicWhooshes(sim.Tick - 1 + _driver.Alpha, true);
        }

        private void FlushBasicWhooshes(float tick, bool checkInterruption)
        {
            var sim = _driver.Sim;
            CauseInfo eventCause = _cause;
            for (int i = 0; i < _basicWhooshes.Count;)
            {
                BasicWhooshCue cue = _basicWhooshes[i];
                if (checkInterruption && sim != null && sim.PelagBasicAttack.Serial == cue.Action.Serial
                    && sim.PelagBasicAttack.Interrupted)
                { _basicWhooshes.RemoveAt(i); continue; }
                if (tick < PelagBasicAttackTiming.WhooshTick(cue.Action)) { i++; continue; }
                // A long rendered frame can cross both windup and contact: the frozen cue still plays once.
                _cause = cue.Cause;
                bool heavy = cue.Action.Stage == 2;
                // Five caught-up ticks can cross two minimum-length attacks. Keep this allowance local to the candidate.
                Play(Sound.PelagAttack, WhooshVolume * (heavy ? 1.10f : 1f), heavy ? .96f : 1.03f, .025f,
                    maxPerFrameOverride: 2);
                _basicWhooshes.RemoveAt(i);
            }
            _cause = eventCause;
        }

        /// <summary>
        /// Короткий сигнал в начале крупного телеграфа. Одновременные телеграфы
        /// (три бутона в одном кадре, таран под прыжок) звучат одним сигналом.
        /// </summary>
        private void PlayWarning()
        {
            if (Time.time < _warningReadyAt) return;
            if (Play(Sound.EnemyWarning, WarningVolume, 1f, .02f) >= 0)
                _warningReadyAt = Time.time + WarningSpacing;
        }

        /// <summary>
        /// Земля разошлась или сомкнулась над телом. Своя семья звуков (новых записей нет):
        /// осыпание песка ниже тоном — шорох земли, и глухой шаг ещё ниже — толчок из-под
        /// ног. Уход в землю ниже и тише выхода. Тела одной волны звучат одним разом.
        /// </summary>
        private void PlayEarth(bool burrow)
        {
            if (Time.time < _earthReadyAt) return;
            int slot = Play(Sound.Dissolve, EarthVolume, burrow ? .66f : .8f, .03f);
            Play(Sound.Footstep, EarthVolume * (burrow ? 1.1f : 1.35f), burrow ? .5f : .58f, .03f, .03f);
            if (slot >= 0) _earthReadyAt = Time.time + EarthSpacing;
        }

        private void PlayDamage(in SimEvent e)
        {
            // Player damage keeps its visual flash/recoil but intentionally has
            // no one-shot until a dedicated, approved hurt cue exists.
            if (e.Target == Simulation.PlayerId)
            { Play(Sound.PlayerHurt, 0.65f, 1f, 0.02f); PlayHeroImpact(in e); return; }

            if (e.Source != Simulation.PlayerId) return;

            // Подтверждённая смерть в этом кадре получает один финальный контакт.
            if (!_driver.Sim.Entities.Alive[e.Target]) return;

            // Сталь сабли общая, тело — своё: у бутона сочное «тук» вместо удара по дереву.
            EnemyKind targetKind = _driver.Sim.Entities.Kind[e.Target];
            Sound bodySound = targetKind == EnemyKind.ForestRootSwarm || targetKind == EnemyKind.ForestSplitling ? Sound.RootSwarmHit
                : targetKind == EnemyKind.ForestBud ? Sound.BudHurt : Sound.HitBody;
            bool ability = e.DamageOrigin == DamageOrigin.Ability;
            if (ability && _driver.Sim.GetAbility(e.ActionVariant)?.DefinitionId == AbilityDefinition.CleaveId
                && e.DamageKind != DamageType.Physical) return;
            bool whirlwind = ability && IsWhirlwindSlot(e.ActionVariant);
            bool heavy = e.Flag || ability || PelagBasicAttackTiming.Heavy(e.ActionVariant,
                _driver.Sim.PelagBasicComboEnabled && e.DamageOrigin == DamageOrigin.BasicAttack);
            if (ability && _driver.Sim.GetAbility(e.ActionVariant)?.DefinitionId == AbilityDefinition.ChainStepId)
                Play(Sound.ChainStepHop, AbilityVolume * .65f, 1f, .025f);

            // Blade definition and body weight are separate layers. The AoE cap
            // turns a whole Whirlwind contact into one large, clean event.
            if (whirlwind)
            {
                // The spin is the hero layer. Contact only adds definition and
                // weight; full-strength metal+body masked the sweep and could
                // sum into a clipped wall together with a same-frame kill.
                // Свой удар по толпе (укол плоти + низкий гул) и немного стали для резкости.
                // Второй и третий задетые в том же кадре упираются в MaxPerFrame —
                // это и есть «один чистый удар по толпе», запасной звук тела
                // нужен только если набора Вихря нет вовсе.
                // Удар Вихря уже содержит тело и сталь — отдельная сталь сверху делала звук разнобойным.
                var whirlwindHits = _variants[(int)Sound.WhirlwindHit];
                if (whirlwindHits != null && whirlwindHits.Length > 0)
                    Play(Sound.WhirlwindHit, BodyVolume * 1.15f, 1f, 0.03f);
                else
                {
                    Play(Sound.HitMetal, MetalVolume * 0.70f, 0.96f, 0.025f);
                    Play(bodySound, BodyVolume * 0.65f, 0.88f, 0.025f);
                }
            }
            else
            {
                Play(Sound.HitMetal, MetalVolume * (heavy ? 1.24f : 1f),
                    heavy ? 0.92f : 1.02f, 0.05f);
                Play(bodySound, BodyVolume * (heavy ? 1.22f : 1f),
                    ability ? 0.76f : (heavy ? 0.86f : 0.94f), 0.045f);
            }
            PlayMobHurt(e.Target, targetKind);
        }

        private bool IsWhirlwindSlot(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == AbilityDefinition.WhirlwindId;
        }

        /// <summary>
        /// Высота общего звука каста под конкретную способность.
        ///
        /// Спрашивается по DefinitionId, а не по номеру слота: слот — это
        /// позиция на панели, и она уже один раз переезжала.
        /// </summary>
        /// <summary>
        /// Своя запись способности или общий каст, если записи нет.
        ///
        /// Разбор по DefinitionId, а не по номеру слота: слот — это позиция
        /// на панели, и она уже дважды переезжала.
        /// </summary>
        private Sound AbilitySound(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return Sound.Cast;

            AbilityBuild build = sim.GetAbility(slot);
            if (build == null) return Sound.Cast;

            if (build.DefinitionId == AbilityDefinition.ChainStepId) return Sound.ChainStep;
            if (build.DefinitionId == AbilityDefinition.DashId || build.DefinitionId == AbilityDefinition.SkewerId
                || build.DefinitionId == AbilityDefinition.BackblastId) return Sound.Dash;
            if (build.DefinitionId == AbilityDefinition.BlazeId)
            { _blazePreparing = true; return Sound.BlazePrepare; }
            return Sound.Cast;
        }

        private float AbilityCastPitch(int slot)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return 0.95f;

            AbilityBuild build = sim.GetAbility(slot);
            if (build == null) return 0.95f;

            if (build.DefinitionId == AbilityDefinition.AnchorLeapId) return 1.22f;
            if (build.DefinitionId == AbilityDefinition.ChainStepId) return 1.02f;
            return 0.95f;
        }

        /// <summary>
        /// Контакт действия нового моба леса. Всё — через Cue: звук встаёт после
        /// событий кадра, и удар героя в том же кадре не теряет свой HitBody.
        /// </summary>
        private void PlayEnemyImpact(in SimEvent e)
        {
            switch ((EnemyActionKind)e.ActionVariant)
            {
                // Шип линии вышел из земли: лопата в землю из записи владельца;
                // к концу линии (Amount — номер шипа 0..3) чуть выше.
                case EnemyActionKind.ThornLine:
                    Cue(Sound.ThornSpike, MobVolume * .9f, .96f + .03f * e.Amount, .03f);
                    break;
                // Всплеск: сухой разлом тонкого дерева.
                case EnemyActionKind.ThornBurst:
                    Cue(Sound.ThornBurst, MobVolume * 1.1f, 1f, .03f);
                    break;
                // Шип выстрела встал: попал — звучит Damage героя; мимо — тише клюёт в землю.
                case EnemyActionKind.ThornShot:
                    if (!e.Flag) Cue(Sound.ThornSpike, MobVolume * .45f, .9f, .05f);
                    break;
                // Корни Корнехвата рвутся из круга.
                case EnemyActionKind.SnarerSlam:
                    Cue(Sound.SnarerRoots, MobVolume, 1f, .03f);
                    break;
                // Волна лечения: земля мягко расходится кругом — выше и тише удара корнями.
                case EnemyActionKind.SnarerMend:
                    Cue(Sound.Dissolve, EarthVolume * .8f, 1.05f, .03f);
                    Cue(Sound.Footstep, EarthVolume * .9f, .7f, .03f);
                    break;
                // Перекат Расщепеня: пуск (Amount 0) — деревянный клубок катится (гаснет на
                // остановке) и толчок от земли; стоп (1) — о стену сухой удар по коре и осыпь,
                // в конце полосы — глухой юз по земле. Попадание в героя звучит его Damage.
                case EnemyActionKind.SplitterRoll:
                    if (e.Amount == 0)
                    {
                        int slot = Play(Sound.SplitterRoll, MobVolume, 1f, .04f);
                        if (slot >= 0) Own(e.Source, Sound.SplitterRoll, slot);
                        Cue(Sound.Footstep, EarthVolume * 1.2f, .5f, .03f);
                        break;
                    }
                    FadeOwned(e.Source, Sound.SplitterRoll);
                    if (_driver.Sim != null && _driver.Sim.TryGetSplitterRoll(e.Source, out var roll) && roll.WallStop)
                    {
                        Cue(Sound.HitBody, BodyVolume * .9f, .55f, .03f);
                        Cue(Sound.Dissolve, EarthVolume * .9f, .8f, .03f);
                    }
                    else Cue(Sound.Footstep, EarthVolume * 1.1f, .62f, .03f);
                    break;
            }
        }

        private bool IsKind(int entity, EnemyKind kind)
        {
            var sim = _driver.Sim;
            return sim != null && (uint)entity < (uint)sim.Entities.Count && sim.Entities.Kind[entity] == kind;
        }

        // ================= мобы леса (поток K, выбор владельца 29.09) =================

        private static float Ticks(float seconds) => seconds * Simulation.TicksPerSecond;

        /// <summary>
        /// Начало ближнего замаха моба. Хранитель — свист когтей пиком в тик контакта
        /// (прежний тихий взмах в начале замаха им заменён); Расщепень — прежний взмах
        /// в начале и укус в контакт; детёныш и корнеползы — укус в контакт. Укусов
        /// роя разом не больше трёх — их держит жетон укусов в Sim.
        /// </summary>
        private void PlayEnemySwingStart(in SimEvent e)
        {
            var sim = _driver.Sim;
            if ((uint)e.Source >= (uint)sim.Entities.Count || !sim.TryGetEnemySwing(e.Source, out var swing)) return;
            switch (sim.Entities.Kind[e.Source])
            {
                case EnemyKind.ForestGuardian:
                    QueueMobCue(MobCueKind.Swing, e.Source, swing.Serial, swing.ImpactTick - Ticks(MobSoundBank.SwingPeakSeconds),
                        Sound.GuardianClawSwing, MobVolume, 1f);
                    break;
                case EnemyKind.ForestSplitter:
                    Play(Sound.GuardianSwing, EnemySwingVolume, 1.08f, .04f);
                    QueueMobCue(MobCueKind.Swing, e.Source, swing.Serial, swing.ImpactTick, Sound.SplitterBite, MobVolume * .9f, 1f);
                    break;
                case EnemyKind.ForestSplitling:
                    QueueMobCue(MobCueKind.Swing, e.Source, swing.Serial, swing.ImpactTick, Sound.SplitterBite, MobVolume * .6f, 1.25f);
                    break;
                case EnemyKind.ForestRootSwarm:
                    QueueMobCue(MobCueKind.Swing, e.Source, swing.Serial, swing.ImpactTick, Sound.RootSwarmBite, MobVolume * .7f, 1f);
                    break;
            }
        }

        /// <summary>Таран: храп на замахе (1 с), галоп с тика разгона до остановки.</summary>
        private void PlayStonehoofStart(in SimEvent e)
        {
            Cue(Sound.StonehoofSnort, MobVolume, 1f, .03f);
            if (_driver.Sim.TryGetStonehoofAction(e.Source, out var charge) && charge.Serial == e.Amount)
                QueueMobCue(MobCueKind.Stonehoof, e.Source, charge.Serial, charge.LaunchTick, Sound.StonehoofCharge,
                    MobBedVolume * 1.3f, 1f, owned: true);
        }

        /// <summary>
        /// Коготь — свист пиком в контакт; прыжок — обратный свист пиком в отрыв
        /// (приземление звучит по WendigoImpact); вой — крик пиком в удар кольца,
        /// снятый вой гаснет.
        /// </summary>
        private void PlayWendigoStart(in SimEvent e)
        {
            if (!_driver.Sim.TryGetWendigoAction(e.Source, out var action) || action.Serial != e.Amount) return;
            switch (action.Kind)
            {
                case WendigoAction.Claw:
                    QueueMobCue(MobCueKind.Wendigo, e.Source, action.Serial, action.ImpactTick - Ticks(MobSoundBank.SwingPeakSeconds),
                        Sound.WendigoClaw, MobVolume, 1f);
                    break;
                case WendigoAction.Leap:
                    QueueMobCue(MobCueKind.Wendigo, e.Source, action.Serial, action.LaunchTick - Ticks(MobSoundBank.LeapPeakSeconds),
                        Sound.WendigoLeap, MobVolume, 1f);
                    break;
                case WendigoAction.Howl:
                    QueueMobCue(MobCueKind.Wendigo, e.Source, action.Serial, action.ImpactTick - Ticks(MobSoundBank.HowlPeakSeconds),
                        Sound.WendigoHowl, MobVolume * 1.2f, 1f, owned: true);
                    break;
            }
        }

        /// <summary>
        /// Начало действия новых мобов леса. Выстрел шипом — обычная атака Шипомёта, не
        /// крупный телеграф: тихий взмах, как у Хранителя, выше. Лечение Корнехвата — не
        /// угроза: лапы глухо уходят в землю, звучит набор силы природы (гаснет, если
        /// сбили). Клыки Камнекопыта — короткий ближний взмах со знаком на теле: вместо
        /// общего сигнала короткий храп, свист клыков — пиком в контакт. Круг когтей
        /// Вендиго — крупная атака: сигнал и низкий свист пиком в удар круга. Остальное
        /// (линия шипов, всплеск, удар корнями) — общий сигнал.
        /// </summary>
        private void PlayEnemyActionStart(in SimEvent e)
        {
            var sim = _driver.Sim;
            switch ((EnemyActionKind)e.ActionVariant)
            {
                case EnemyActionKind.ThornShot:
                    Play(Sound.GuardianSwing, EnemySwingVolume, 1.15f, .04f);
                    break;
                case EnemyActionKind.SnarerMend:
                {
                    Cue(Sound.HitBody, BodyVolume * .5f, .55f, .03f);
                    Cue(Sound.Footstep, EarthVolume * 1.1f, .6f, .03f);
                    int slot = Play(Sound.SnarerMend, MobBedVolume, 1f, .02f);
                    if (slot >= 0) Own(e.Source, Sound.SnarerMend, slot);
                    break;
                }
                case EnemyActionKind.StonehoofTusk:
                    Play(Sound.StonehoofSnort, MobVolume * .7f, 1.12f, .04f);
                    if (sim.TryGetStonehoofTusk(e.Source, out var tusk))
                        QueueMobCue(MobCueKind.Tusk, e.Source, tusk.Serial, tusk.ImpactTick - Ticks(MobSoundBank.SwingPeakSeconds),
                            Sound.StonehoofTusk, MobVolume, 1f);
                    break;
                case EnemyActionKind.WendigoSweep:
                    PlayWarning();
                    if (sim.TryGetWendigoAction(e.Source, out var sweep) && sweep.Kind == WendigoAction.Sweep)
                        QueueMobCue(MobCueKind.Wendigo, e.Source, sweep.Serial, sweep.ImpactTick - Ticks(MobSoundBank.SweepPeakSeconds),
                            Sound.WendigoSweep, MobVolume * 1.1f, 1f);
                    break;
                default:
                    PlayWarning();
                    break;
            }
        }

        /// <summary>
        /// Удар моба по герою. Хранитель — когти с деревянным ударом; коготь и круг
        /// Вендиго — те же когти (выше и ниже); таран — тяжёлый удар, клыки — он же
        /// легче и выше; шип Шипомёта — лопата в землю. Укус, плод, прыжок, вой и
        /// корни звучат своими событиями — второй слой им не нужен.
        /// </summary>
        private void PlayHeroImpact(in SimEvent e)
        {
            var sim = _driver.Sim;
            if (e.Source == Simulation.PlayerId || (uint)e.Source >= (uint)sim.Entities.Count) return;
            switch (sim.Entities.Kind[e.Source])
            {
                case EnemyKind.ForestGuardian:
                    Cue(Sound.GuardianClawImpact, HeroImpactVolume, 1f, .04f);
                    break;
                case EnemyKind.ForestWendigo:
                    if (sim.TryGetWendigoAction(e.Source, out var claw)
                        && (claw.Kind == WendigoAction.Claw || claw.Kind == WendigoAction.Sweep))
                        Cue(Sound.GuardianClawImpact, HeroImpactVolume, claw.Kind == WendigoAction.Sweep ? .95f : 1.1f, .04f);
                    break;
                case EnemyKind.ForestStonehoof:
                {
                    bool tuskHit = sim.TryGetStonehoofTusk(e.Source, out var tuskState) && tuskState.HitResolved;
                    Cue(Sound.StonehoofCollision, HeroImpactVolume * (tuskHit ? .7f : 1f), tuskHit ? 1.15f : 1f, .03f);
                    break;
                }
                case EnemyKind.ForestThorncaster:
                    Cue(Sound.ThornSpike, HeroImpactVolume * .8f, 1.1f, .04f);
                    break;
            }
        }

        /// <summary>
        /// Голос боли моба под ударом сабли: не чаще раза в 0,35 с на моба и 0,06 с на
        /// всех — Вихрь по толпе даёт один голос, а не хор. Сучки Шипомёта и мох
        /// Корнехвата — острые записи, упёрлись в пик тише цели: чуть громче в миксе.
        /// </summary>
        private void PlayMobHurt(int target, EnemyKind kind)
        {
            Sound sound;
            float gain = 1f, pitch = 1f;
            switch (kind)
            {
                case EnemyKind.ForestGuardian: sound = Sound.GuardianHurt; break;
                case EnemyKind.ForestRootSwarm: sound = Sound.RootSwarmHurt; gain = .7f; break;
                case EnemyKind.ForestBud: sound = Sound.BudGurgle; break;
                case EnemyKind.ForestStonehoof: sound = Sound.StonehoofHurt; break;
                case EnemyKind.ForestWendigo: sound = Sound.WendigoHurt; break;
                case EnemyKind.ForestThorncaster: sound = Sound.ThorncasterHurt; gain = 1.8f; break;
                case EnemyKind.ForestRootSnarer: sound = Sound.SnarerHurt; gain = 1.3f; break;
                case EnemyKind.ForestSplitter: sound = Sound.SplitterHurt; break;
                case EnemyKind.ForestSplitling: sound = Sound.SplitterHurt; gain = .7f; pitch = 1.3f; break;
                default: return;
            }
            float now = Time.time;
            if (now < _hurtAnyReadyAt || target < 0) return;
            if (target >= _hurtReadyAt.Length) System.Array.Resize(ref _hurtReadyAt, Mathf.NextPowerOfTwo(target + 1));
            if (now < _hurtReadyAt[target]) return;
            if (Play(sound, Mathf.Min(1f, MobHurtVolume * gain), pitch, .05f) < 0) return;
            _hurtReadyAt[target] = now + HurtSpacing;
            _hurtAnyReadyAt = now + HurtAnySpacing;
        }

        /// <summary>Своя смерть вида; Корнеполз, Плюй-плод и Расщепень — в QueueDeathSounds отдельно.</summary>
        private static Sound DeathSound(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestGuardian: return Sound.GuardianDeath;
                case EnemyKind.ForestStonehoof: return Sound.StonehoofDeath;
                case EnemyKind.ForestWendigo: return Sound.WendigoDeath;
                case EnemyKind.ForestThorncaster: return Sound.ThorncasterDeath;
                case EnemyKind.ForestRootSnarer: return Sound.SnarerDeath;
                default: return Sound.Count;
            }
        }

        /// <summary>Крупные — всё, кроме корнеполза, детёныша и Плюй-плода: им удар убийства громче и ниже.</summary>
        private static bool IsBigMob(EnemyKind kind)
            => kind != EnemyKind.ForestRootSwarm && kind != EnemyKind.ForestSplitling && kind != EnemyKind.ForestBud;

        /// <summary>
        /// Общий слоёный удар убийства (разлом дерева + плотный удар) — один на кадр.
        /// Мелкие убийства не чаще раза в KillImpactSpacing; крупное звучит всегда.
        /// </summary>
        private void FlushKillImpact()
        {
            int size = _killImpactThisFrame;
            _killImpactThisFrame = 0;
            if (size == 0 || (size == 1 && Time.time < _killImpactReadyAt)) return;
            bool big = size == 2;
            _cause = _killImpactCause;
            if (Play(Sound.KillImpact, KillImpactVolume * (big ? 1f : .55f), big ? .94f : 1.1f, .03f) >= 0)
                _killImpactReadyAt = Time.time + KillImpactSpacing;
        }

        private void QueueMobCue(MobCueKind kind, int entity, int serial, float tick, Sound sound, float volume, float pitch,
            bool owned = false)
        {
            if (_mobCueCount >= _mobCues.Length) return;
            _mobCues[_mobCueCount++] = new MobCue
            {
                Kind = kind, Entity = entity, Serial = serial, Tick = tick, Sound = sound,
                Volume = volume, Pitch = pitch, Owned = owned, Cause = _cause,
            };
        }

        /// <summary>Действие, под которое ждёт звук, ещё идёт: тот же номер в Sim.</summary>
        private bool MobCueAlive(in MobCue cue)
        {
            var sim = _driver.Sim;
            if ((uint)cue.Entity >= (uint)sim.Entities.Count || !sim.Entities.Alive[cue.Entity]) return false;
            switch (cue.Kind)
            {
                case MobCueKind.Swing: return sim.TryGetEnemySwing(cue.Entity, out var swing) && swing.Serial == cue.Serial;
                case MobCueKind.Tusk: return sim.TryGetStonehoofTusk(cue.Entity, out var tusk) && tusk.Serial == cue.Serial;
                case MobCueKind.Wendigo: return sim.TryGetWendigoAction(cue.Entity, out var action) && action.Serial == cue.Serial;
                default: return sim.TryGetStonehoofAction(cue.Entity, out var charge) && charge.Serial == cue.Serial;
            }
        }

        /// <summary>Тик — тот же, что у звука Рассечения: время кадра в тиках Sim с долей между ними.</summary>
        private void UpdateMobCues()
        {
            if (_mobCueCount == 0) return;
            var sim = _driver.Sim;
            float now = sim.Tick - 1 + _driver.Alpha;
            int write = 0;
            for (int i = 0; i < _mobCueCount; i++)
            {
                var cue = _mobCues[i];
                if (!MobCueAlive(in cue)) continue;
                if (now < cue.Tick) { _mobCues[write++] = cue; continue; }
                // Причина — событие, поставившее ожидание; вид ожидания и номер действия — к нему.
                _cause = cue.Cause;
                _cause.Kind = Cause.MobCue;
                _cause.Detail = (int)cue.Kind;
                _cause.Serial = cue.Serial;
                _cause.DueTick = Mathf.CeilToInt(cue.Tick);
                int slot = Play(cue.Sound, cue.Volume, cue.Pitch, .04f);
                if (cue.Owned && slot >= 0) Own(cue.Entity, cue.Sound, slot);
            }
            _mobCueCount = write;
        }

        /// <summary>Лужа — не сущность: хозяин её звука — отрицательный ключ по слоту лужи.</summary>
        private static int PuddleOwner(int puddleSlot) => -1000 - puddleSlot;

        private void Own(int owner, Sound sound, int slot)
        {
            // Выписываем голоса, которые уже заняты другим звуком или отзвучали.
            int write = 0;
            for (int i = 0; i < _ownedCount; i++)
            {
                var o = _owned[i];
                if (o.Slot != slot && _voiceSounds[o.Slot] == o.Sound && _voices[o.Slot].isPlaying) _owned[write++] = o;
            }
            _ownedCount = write;
            if (_ownedCount >= _owned.Length) return;
            _owned[_ownedCount++] = new OwnedVoice { Owner = owner, Sound = sound, Slot = slot };
        }

        /// <summary>Гасит за 0,1 с длинный звук этого хозяина, если голос ещё играет его.</summary>
        private void FadeOwned(int owner, Sound sound)
        {
            int write = 0;
            for (int i = 0; i < _ownedCount; i++)
            {
                var o = _owned[i];
                if (o.Owner != owner || o.Sound != sound) { _owned[write++] = o; continue; }
                if (_voiceSounds[o.Slot] == sound && _voices[o.Slot].isPlaying && _fadeLeft[o.Slot] < 0f)
                    _fadeLeft[o.Slot] = .10f;
            }
            _ownedCount = write;
        }

        /// <summary>
        /// Топот роя: пока корнеползы бегут, раз в SwarmScuttleClock.Spacing — один шорох,
        /// громче с числом бегущих. Двадцать корнеползов — всё равно один голос, и тот берёт
        /// только свободный (приоритет 5).
        ///
        /// Только в бою арены (зачистка и путь к выходу), только пока Sim шагает и только
        /// от бегущих в кадре (баг 29.09): на итогах после смерти или ухода, на награде и
        /// маршруте, под завесой Sim стоит, а скорость у бегущих остаётся — шорох шёл без
        /// конца; стартовая волна бежит к входу из-за края кадра — шорох был «ни от кого».
        /// </summary>
        private void UpdateSwarmScuttle()
        {
            var sim = _driver.Sim;
            float now = Time.time;
            if (!_scuttle.Due(sim.Tick, now))
            {
                if (CaptureRig.AudioLog && _scuttle.Frozen(now)) LogScuttleMuted("frozen", 0);
                return;
            }
            GameSession session = _driver.Session;
            RiftRun run = _driver.Run;
            bool fighting = session != null && session.Mode == GameMode.Rift && run != null
                            && (run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit);
            int running = fighting
                ? SwarmScuttleClock.CountRunning(sim.Entities, Simulation.PlayerId, SwarmScuttleClock.HearingRadius)
                : 0;
            if (CaptureRig.AudioLog) LogScuttleMuted(fighting ? "far" : "not-fighting", running);
            if (!_scuttle.Counted(running, now)) return;
            SetCause(Cause.Scuttle);
            _cause.Detail = running;
            Play(Sound.RootSwarmScuttle, MobBedVolume * Mathf.Min(1f, .4f + .12f * running), 1f, .06f);
        }

        private void QueueDeathSounds(EnemyKind kind, int entity)
        {
            if (kind == EnemyKind.ForestSplitter || kind == EnemyKind.ForestSplitling)
            {
                // Расщепень не падает и не осыпается — раскалывается (SplitterCombatView):
                // в миг смерти трещит скорлупа, через BreakDelaySeconds раскол — у родителя
                // он звучит по SplitterSplit, у детёныша (своего распада нет) — здесь же;
                // потом половины коры глухо ложатся на землю. Детёныш — мельче и выше.
                bool child = kind == EnemyKind.ForestSplitling;
                float breakAt = SplitterCombatView.BreakDelaySeconds;
                Cue(Sound.SplitterDeath, MobDeathVolume * (child ? .6f : 1f), child ? 1.25f : 1f, .04f);
                if (child) Cue(Sound.SplitterCrack, MobDeathVolume * .6f, 1.3f, .04f, breakAt);
                Cue(Sound.HitBody, BodyVolume * (child ? .28f : .4f), child ? .85f : .6f, .04f,
                    breakAt + SplitterCombatView.ShellLandSeconds);
                return;
            }
            var timing = EnemyPresentationProfile.Death(kind);
            // Такт убийства (поток I, 29.09): тело трескается в залпе BurstAt (у тяжёлых —
            // после стоп-кадра) и уходит к BodyGoneAt. Осыпание звучит на залпе, а не на
            // старом DissolveAt; «упал» — не позже ухода тела (клип ложится к FallSeconds,
            // когда тела уже нет).
            var beat = EnemyDeathFxView.BeatFor(_driver.Sim, entity);
            float dissolveAt = beat.BurstAt;
            float fallAt = Mathf.Min(timing.FallSeconds, Mathf.Max(dissolveAt, beat.BodyGoneAt - .05f));
            if (kind == EnemyKind.ForestBud)
            {
                // Мокрый шлепок-лопание из выбора 29.09 в миг смерти; осыпание общее,
                // на залпе распада.
                Play(Sound.BudDeath, MobDeathVolume, 1f, .03f);
                Queue(Sound.Dissolve, dissolveAt);
                return;
            }
            // Голос или разлом самого вида — в миг смерти (у Хранителя падение дерева
            // в записи ложится через ~0,9 с, как тело). Корнеползы гибнут пачками — их
            // хруст не чаще раза в 0,07 с.
            if (kind == EnemyKind.ForestRootSwarm)
            {
                if (Time.time >= _swarmDeathReadyAt)
                {
                    Cue(Sound.RootSwarmDeath, MobDeathVolume * .7f, 1f, .06f);
                    _swarmDeathReadyAt = Time.time + .07f;
                }
            }
            else
            {
                Sound death = DeathSound(kind);
                if (death != Sound.Count) Cue(death, MobDeathVolume, 1f, .03f);
            }
            // Детёныш Расщепеня мелкий и падает, как корнеполз.
            bool swarm = kind == EnemyKind.ForestRootSwarm || kind == EnemyKind.ForestSplitling;
            Queue(swarm ? Sound.RootSwarmFall : Sound.GuardianFall, fallAt);
            Queue(swarm ? Sound.RootSwarmDissolve : Sound.Dissolve, dissolveAt);
        }

        private void Queue(Sound sound, float delay)
        {
            if (_deathCueCount >= _deathCues.Length) return;
            _deathCues[_deathCueCount++] = new DelayedCue { Sound = sound, Due = Time.time + delay, Volume = -1f, Cause = Queued() };
        }

        /// <summary>Текущая причина с отметкой, когда звук отложен.</summary>
        private CauseInfo Queued()
        {
            CauseInfo cause = _cause;
            cause.QueuedAt = Time.time;
            return cause;
        }

        /// <summary>
        /// Звук своей громкости и высоты через delay секунд; 0 — в этом же кадре, но
        /// после всех событий кадра (FlushDissolves идёт за ConsumeEvents).
        /// </summary>
        private void Cue(Sound sound, float volume, float pitch, float spread, float delay = 0f)
        {
            if (_deathCueCount >= _deathCues.Length) return;
            _deathCues[_deathCueCount++] = new DelayedCue
                { Sound = sound, Due = Time.time + delay, Volume = volume, Pitch = pitch, Spread = spread, Cause = Queued() };
        }

        private void FlushDissolves()
        {
            int write = 0;
            for (int i = 0; i < _deathCueCount; i++)
            {
                var cue = _deathCues[i];
                if (cue.Due > Time.time) { _deathCues[write++] = cue; continue; }
                _cause = cue.Cause;
                if (cue.Volume >= 0f) { Play(cue.Sound, cue.Volume, cue.Pitch, cue.Spread); continue; }
                bool dissolve = cue.Sound == Sound.Dissolve || cue.Sound == Sound.RootSwarmDissolve;
                Play(cue.Sound, dissolve ? DissolveVolume : BodyVolume * 0.7f, 1f, 0f);
            }
            _deathCueCount = write;
        }

        private int Play(Sound sound, float volume, float pitchCenter, float pitchSpread,
            float delay = 0f, int fixedVariant = -1, int maxPerFrameOverride = 0)
        {
            int index = (int)sound;
            AudioClip[] clips = _variants[index];
            if (clips == null || clips.Length == 0) return -1;
            var entry = _entries[index];
            int frameLimit = maxPerFrameOverride > 0 ? maxPerFrameOverride
                : entry != null ? entry.MaxPerFrame : MaxPerKindPerFrame;
            if (_playedThisFrame[index] >= frameLimit) return -1;
            int variant = fixedVariant >= 0 ? fixedVariant % clips.Length : PickVariant(index, clips.Length);
            AudioClip clip = clips[variant];
            if (clip == null) return -1;
            float spread = entry != null ? entry.PitchVariation : pitchSpread;
            float pitch = Mathf.Clamp(pitchCenter * (entry != null ? entry.Pitch : 1f)
                + (Random01() - 0.5f) * spread * 2f, 0.5f, 2f);
            int priority = entry != null ? entry.Priority : CombatAudioProfile.DefaultPriority(sound);
            int slot = _voiceBudget.Acquire(AudioSettings.dspTime, delay + clip.length / pitch, priority);
            if (slot < 0)
            {
                // Все голоса заняты звуками важнее: в журнале съёмки это видно («drop»).
                if (CaptureRig.AudioLog) LogPlayed(sound, clip, -1f, delay);
                return -1;
            }
            _playedThisFrame[index]++;
            AudioSource voice = _voices[slot];
            voice.Stop();
            _voiceSounds[slot] = sound;
            _fadeLeft[slot] = -1f;
            voice.clip = clip;
            _voiceGains[slot] = Mathf.Clamp01(volume * Master
                * (Profile != null ? Profile.Gain : 0.8f) * (entry != null ? entry.Gain : 1f));
            voice.volume = _voiceGains[slot] * GameUserSettings.EffectsVolume;
            voice.pitch = pitch;
            voice.priority = 256 - Mathf.Clamp(priority * 2, 0, 256);
            if (delay > 0f) voice.PlayDelayed(delay);
            else voice.Play();
            if (CombatAudioCapture.Recording)
                Debug.Log($"[capture-cue] {sound} clip={clip.name} load={clip.loadState} volume={voice.volume} playing={voice.isPlaying} dsp={AudioSettings.dspTime}");
            if (CaptureRig.AudioLog) LogPlayed(sound, clip, voice.volume, delay);
            return slot;
        }

        // ================= журнал съёмки (-capture-audio-log) =================

        private void SetCause(Cause kind) => _cause = new CauseInfo { Kind = kind, QueuedAt = -1f };

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Служебная строка журнала: сброс боя, отброшенные события, заглушённый топот, прочие источники.</summary>
        private void LogLine(string text) => Debug.Log(LogHead(new System.Text.StringBuilder(192)).Append(text).ToString());

        private System.Text.StringBuilder LogHead(System.Text.StringBuilder line)
        {
            var sim = _driver != null ? _driver.Sim : null;
            GameSession session = _driver != null ? _driver.Session : null;
            RiftRun run = session != null ? session.Run : null;
            return line.Append("[audio-log] f=").Append(Time.frameCount)
                .Append(" t=").Append(Time.time.ToString("0.000", Inv))
                .Append(" tick=").Append(sim != null ? sim.Tick : -1)
                .Append(" mode=").Append(session != null ? session.Mode.ToString() : "-")
                .Append(" gen=").Append(session != null ? session.Generation : -1)
                .Append(" depth=").Append(run != null ? run.Depth : 0)
                .Append(" phase=").Append(run != null ? run.Phase.ToString() : "-")
                .Append(' ');
        }

        /// <summary>Сыгранный звук с причиной; volume &lt; 0 — не сыгран: голоса заняты звуками важнее («drop»).</summary>
        private void LogPlayed(Sound sound, AudioClip clip, float volume, float delay)
        {
            var line = LogHead(new System.Text.StringBuilder(256))
                .Append(volume >= 0f ? "play " : "drop ").Append(sound).Append(" clip=").Append(clip.name);
            if (volume >= 0f) line.Append(" vol=").Append(volume.ToString("0.000", Inv));
            else line.Append(" budget-full");
            if (delay > 0f) line.Append(" delay=").Append(delay.ToString("0.000", Inv));
            line.Append(" cause=");
            CauseInfo c = _cause;
            switch (c.Kind)
            {
                case Cause.Event:
                    AppendEvent(line, in c);
                    break;
                case Cause.MobCue:
                    line.Append("mob-cue:").Append((MobCueKind)c.Detail).Append(" serial=").Append(c.Serial)
                        .Append(" due=").Append(c.DueTick).Append(" from ");
                    AppendEvent(line, in c);
                    break;
                case Cause.Scuttle:
                    line.Append("scuttle running=").Append(c.Detail).Append(" within=")
                        .Append(SwarmScuttleClock.HearingRadius.ToString("0", Inv)).Append('m');
                    break;
                default:
                    line.Append(c.Kind);
                    break;
            }
            if (c.QueuedAt >= 0f) line.Append(" queued=").Append((Time.time - c.QueuedAt).ToString("0.000", Inv)).Append("s-ago");
            Debug.Log(line.ToString());
        }

        private void AppendEvent(System.Text.StringBuilder line, in CauseInfo c)
        {
            SimEvent e = c.Event;
            line.Append(e.Type).Append(" src=");
            AppendWho(line, e.Source);
            line.Append(" tgt=");
            AppendWho(line, e.Target);
            line.Append(" amt=").Append(e.Amount).Append(" var=").Append(e.ActionVariant);
            if (e.Flag) line.Append(" flag");
            line.Append(" evtick=").Append(c.Tick);
        }

        private void AppendWho(System.Text.StringBuilder line, int id)
        {
            var sim = _driver.Sim;
            if (id < 0) { line.Append('-'); return; }
            if (id == Simulation.PlayerId) { line.Append("hero"); return; }
            line.Append('#').Append(id);
            if (sim == null || id >= sim.Entities.Count) { line.Append(":?"); return; }
            line.Append(':').Append(sim.Entities.Kind[id]);
            if (!sim.Entities.Alive[id]) line.Append("(dead)");
        }

        /// <summary>
        /// Топот, который до правки 29.09 прозвучал бы: бегущие корнеползы есть, но Sim
        /// стоит, бой кончился или они за краем кадра. Не чаще раза в секунду.
        /// </summary>
        private void LogScuttleMuted(string reason, int heard)
        {
            if (Time.time < _scuttleLogAt || _driver.Sim == null) return;
            int all = SwarmScuttleClock.CountRunning(_driver.Sim.Entities, Simulation.PlayerId, -1f);
            if (all <= heard) return;
            _scuttleLogAt = Time.time + 1f;
            LogLine("scuttle-muted reason=" + reason + " running=" + all + " heard=" + heard);
        }

        private int PlayingVoices()
        {
            int playing = 0;
            if (_voices != null)
                for (int i = 0; i < _voices.Length; i++)
                    if (_voices[i] != null && _voices[i].isPlaying) playing++;
            return playing;
        }

        // Прочие источники звука сцены: GameSound (переходы, награды, сердцебиение), лагерь,
        // музыка. «Лишний звук» мог быть и не боевым — журнал ловит каждый, что стал
        // слышен (играет, не заглушён, громкость больше нуля), сменил клип или начался
        // заново, и каждый, что замолк. Разовые PlayOneShot поверх уже звучащего
        // источника не видны: у них нет своего клипа в источнике.
        private struct OtherSource { public bool Audible; public AudioClip Clip; public float Time; }
        private Dictionary<AudioSource, OtherSource> _others = new Dictionary<AudioSource, OtherSource>(),
            _othersNext = new Dictionary<AudioSource, OtherSource>();
        private Transform _voiceRoot;
        private double _dspSeen = -1d;
        private float _dspSeenAt;
        private bool _dspStandLogged;

        private void LogOtherSources()
        {
            // Съёмочный плеер без звукового устройства: dspTime стоит, бюджет голосов
            // считает по нему и, раз заполнившись, больше их не отдаёт — дальше в журнале
            // «drop» вместо «play». Решение звука от этого не меняется, только голос.
            double dsp = AudioSettings.dspTime;
            if (dsp != _dspSeen) { _dspSeen = dsp; _dspSeenAt = Time.unscaledTime; }
            else if (!_dspStandLogged && Time.unscaledTime - _dspSeenAt > 1f)
            {
                _dspStandLogged = true;
                LogLine("dsp-clock stands at " + dsp.ToString("0.000", Inv)
                    + ": no audio device? the voice budget stops freeing voices, later sounds log as drop");
            }

            foreach (AudioSource source in FindObjectsByType<AudioSource>())
            {
                if (source == null || source.transform.parent == _voiceRoot) continue;
                bool audible = source.isPlaying && !source.mute && source.volume > .001f;
                AudioClip clip = source.clip;
                float time = audible ? source.time : 0f;
                _others.TryGetValue(source, out OtherSource was);
                bool restarted = audible && was.Audible
                                 && (clip != was.Clip || (!source.loop && clip != null && time + .05f < was.Time));
                if (audible && (!was.Audible || restarted))
                    LogLine("source-start " + SourceName(source) + " clip=" + (clip != null ? clip.name : "one-shot")
                        + " loop=" + (source.loop ? 1 : 0) + " vol=" + source.volume.ToString("0.00", Inv)
                        + " spatial=" + source.spatialBlend.ToString("0.##", Inv));
                else if (!audible && was.Audible)
                    LogLine("source-stop " + SourceName(source));
                _othersNext[source] = new OtherSource { Audible = audible, Clip = clip, Time = time };
            }
            // Выключенные вместе с объектом и удалённые: были слышны — значит, замолкли.
            foreach (var pair in _others)
                if (pair.Value.Audible && !_othersNext.ContainsKey(pair.Key))
                    LogLine("source-gone " + (pair.Key != null ? SourceName(pair.Key) : "(destroyed)"));
            var swap = _others;
            _others = _othersNext;
            _othersNext = swap;
            _othersNext.Clear();
        }

        private static string SourceName(AudioSource source)
        {
            Transform parent = source.transform.parent;
            return parent != null ? parent.name + "/" + source.name : source.name;
        }

        private void OnDisable()
        {
            _basicWhooshes.Clear();
            _deathCueCount = 0;
            _whooshDelay = _whirlwindEndAt = _anchorImpactAt = _anchorLandAt = -1f;
            _warningReadyAt = _earthReadyAt = 0f;
            _chainSoundActive = false;
            _blazePreparing = _blazeBurning = _paused = false;
            if (_voices != null) foreach (var voice in _voices) if (voice != null) voice.Stop();
            _voiceBudget?.Clear();
            ResetMobState();
        }

        /// <summary>Новый бой — новые номера сущностей: ожидания, хозяева голосов и паузы боли не переносятся.</summary>
        private void ResetMobState()
        {
            _mobCueCount = _ownedCount = _killImpactThisFrame = 0;
            _hurtAnyReadyAt = _swarmDeathReadyAt = _killImpactReadyAt = 0f;
            _scuttle.Reset();
            System.Array.Clear(_hurtReadyAt, 0, _hurtReadyAt.Length);
        }

        private void StopKind(Sound sound)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voiceSounds[i] != sound) continue;
                _voices[i].Stop();
                _voiceBudget.Release(i);
            }
        }

        private void FadeKind(Sound sound)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (_voiceSounds[i] == sound && _voices[i].isPlaying && _fadeLeft[i] < 0f)
                    _fadeLeft[i] = .10f;
        }

        private void UpdateBlazeSound()
        {
            var sim = _driver.Sim;
            if (_blazePreparing && !sim.BlazeCasting)
            { FadeKind(Sound.BlazePrepare); _blazePreparing = false; }
            if (_blazeBurning && !sim.BlazeActive)
            { FadeKind(Sound.BlazeFire); _blazeBurning = false; }
            if (!sim.Entities.Alive[Simulation.PlayerId])
            {
                FadeKind(Sound.PelagAttack); FadeKind(Sound.Cleave); FadeKind(Sound.Whirlwind); FadeKind(Sound.Dash);
                _whooshDelay = _whirlwindEndAt = -1f;
            }
        }

        private void UpdateVoiceMix()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                float fade = 1f;
                if (_fadeLeft[i] >= 0f)
                {
                    if (!_paused) _fadeLeft[i] -= Time.deltaTime;
                    fade = Mathf.Clamp01(_fadeLeft[i] / .10f);
                    if (fade <= 0f) { _voices[i].Stop(); _voiceBudget.Release(i); _fadeLeft[i] = -1f; }
                }
                _voices[i].volume = _voiceGains[i] * GameUserSettings.EffectsVolume * fade;
            }
        }

        private int PickVariant(int soundIndex, int count)
        {
            if (count <= 1) return 0;
            int pick = Mathf.Min(count - 1, (int)(Random01() * count));
            if (pick == _lastVariant[soundIndex]) pick = (pick + 1) % count;
            _lastVariant[soundIndex] = pick;
            return pick;
        }

        private void BuildVoices()
        {
            Transform root = new GameObject("Combat audio voices").transform;
            root.SetParent(transform, false);
            _voiceRoot = root;
            _voices = new AudioSource[Mathf.Max(4, Voices)];
            _voiceBudget = new CombatVoiceBudget(_voices.Length);
            _voiceSounds = new Sound[_voices.Length];
            _voiceGains = new float[_voices.Length];
            _fadeLeft = new float[_voices.Length];
            for (int i = 0; i < _fadeLeft.Length; i++) _fadeLeft[i] = -1f;

            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject($"Voice {i}");
                go.transform.SetParent(root, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.playOnAwake = false;
                source.loop = false;
                source.bypassReverbZones = true;
                _voices[i] = source;
            }
        }

        private void LoadClips()
        {
            _variants[(int)Sound.Whoosh] = Resources.LoadAll<AudioClip>("Audio/Combat/Whoosh");
            _variants[(int)Sound.HitMetal] = Resources.LoadAll<AudioClip>("Audio/Combat/HitMetal");
            _variants[(int)Sound.HitBody] = Resources.LoadAll<AudioClip>("Audio/Combat/HitBody");
            _variants[(int)Sound.Kill] = Resources.LoadAll<AudioClip>("Audio/Combat/Kill");
            _variants[(int)Sound.Dissolve] = Resources.LoadAll<AudioClip>("Audio/Combat/Dissolve");
            AudioClip whirlwind = Resources.Load<AudioClip>("Audio/Combat/whirlwind_pelag_pcm");
            _variants[(int)Sound.Whirlwind] = whirlwind != null
                ? new[] { whirlwind }
                : new AudioClip[0];
            if (whirlwind == null)
                Debug.LogWarning("[CombatAudio] Missing Resources/Audio/Combat/whirlwind_pelag_pcm.", this);
            _variants[(int)Sound.Cast] = Resources.LoadAll<AudioClip>("Audio/Combat/Cast");
            _variants[(int)Sound.Reward] = Resources.LoadAll<AudioClip>("Audio/Combat/Reward");
            _variants[(int)Sound.AnchorSweep] = Resources.LoadAll<AudioClip>("Audio/Combat/AnchorSweep");
            _variants[(int)Sound.ChainStep] = Resources.LoadAll<AudioClip>("Audio/Combat/ChainStep");

            // Шаги сортируются по имени: LoadAll порядок не гарантирует, а
            // здесь он и есть смысл — pt1, pt2, pt3 идут подряд.
            AudioClip[] steps = Resources.LoadAll<AudioClip>("Audio/Combat/Footstep");
            if (steps != null && steps.Length > 1)
                System.Array.Sort(steps, (a, b) => string.CompareOrdinal(a.name, b.name));
            _variants[(int)Sound.Footstep] = steps;

            // Плюй-плод и сигналы врагов (26.09): здесь лежит кандидат, выбранный на
            // странице ART/SFX/candidates-2026-09-26 (build-bud-sfx.py --install), под
            // игровым именем. Клипы, назначенные в профиле, по-прежнему важнее.
            AudioClip[] bud = Resources.LoadAll<AudioClip>("Audio/Combat/Bud");
            _variants[(int)Sound.BudVolley] = Named(bud, "BudVolley");
            // BudPop_01..05 по имени: номер плода выбирает свой хлопок.
            _variants[(int)Sound.BudPop] = Named(bud, "BudPop");
            _variants[(int)Sound.BudFruitImpact] = Named(bud, "BudFruitImpact");
            _variants[(int)Sound.BudHurt] = Named(bud, "BudHurt");
            _variants[(int)Sound.BudDeath] = Named(bud, "BudDeath");
            _variants[(int)Sound.EnemyWarning] = Resources.LoadAll<AudioClip>("Audio/Combat/EnemyWarning");
            // Пустая папка — выбор «тишина»: замах хранителя тогда молчит.
            _variants[(int)Sound.GuardianSwing] = Resources.LoadAll<AudioClip>("Audio/Combat/GuardianSwing");

            // Мобы леса (поток K, выбор владельца 29.09): Mobs/<Моб>/<слот>_NN.wav, таблица —
            // MobSoundBank. У Плюй-плода выстрел, шлепок и смерть заменяют кандидатов 26.09
            // (те остаются запасом, если новых клипов нет); «тук» тела BudHurt и раскрытие
            // залпа — прежние. Шаги Хранителя лежат про запас: хука шагов врагов нет.
            LoadMob(Sound.GuardianClawSwing, "Guardian", "swing");
            LoadMob(Sound.GuardianClawImpact, "Guardian", "impact");
            LoadMob(Sound.GuardianHurt, "Guardian", "hurt");
            LoadMob(Sound.GuardianDeath, "Guardian", "death");
            LoadMob(Sound.RootSwarmBite, "RootSwarm", "bite");
            LoadMob(Sound.RootSwarmScuttle, "RootSwarm", "scuttle");
            LoadMob(Sound.RootSwarmHurt, "RootSwarm", "hurt");
            LoadMob(Sound.RootSwarmDeath, "RootSwarm", "death");
            LoadMob(Sound.BudPop, "Bud", "spit");
            LoadMob(Sound.BudFruitImpact, "Bud", "splat");
            LoadMob(Sound.BudPuddle, "Bud", "puddle");
            LoadMob(Sound.BudGurgle, "Bud", "hurt");
            LoadMob(Sound.BudDeath, "Bud", "death");
            LoadMob(Sound.StonehoofSnort, "Stonehoof", "snort");
            LoadMob(Sound.StonehoofCharge, "Stonehoof", "charge");
            LoadMob(Sound.StonehoofCollision, "Stonehoof", "collision");
            LoadMob(Sound.StonehoofTusk, "Stonehoof", "tusk");
            LoadMob(Sound.StonehoofHurt, "Stonehoof", "hurt");
            LoadMob(Sound.StonehoofDeath, "Stonehoof", "death");
            LoadMob(Sound.WendigoClaw, "Wendigo", "claw");
            LoadMob(Sound.WendigoLeap, "Wendigo", "leap");
            LoadMob(Sound.WendigoLand, "Wendigo", "land");
            LoadMob(Sound.WendigoHowl, "Wendigo", "howl");
            LoadMob(Sound.WendigoSweep, "Wendigo", "sweep");
            LoadMob(Sound.WendigoHurt, "Wendigo", "hurt");
            LoadMob(Sound.WendigoDeath, "Wendigo", "death");
            LoadMob(Sound.ThornSpike, "Thorncaster", "spike");
            LoadMob(Sound.ThornBurst, "Thorncaster", "burst");
            LoadMob(Sound.ThornShot, "Thorncaster", "shot");
            LoadMob(Sound.ThorncasterHurt, "Thorncaster", "hurt");
            LoadMob(Sound.ThorncasterDeath, "Thorncaster", "death");
            LoadMob(Sound.SnarerSlam, "RootSnarer", "slam");
            LoadMob(Sound.SnarerRoots, "RootSnarer", "roots");
            LoadMob(Sound.SnarerMend, "RootSnarer", "mend");
            LoadMob(Sound.SnarerHurt, "RootSnarer", "hurt");
            LoadMob(Sound.SnarerDeath, "RootSnarer", "death");
            LoadMob(Sound.SplitterBite, "Splitter", "bite");
            LoadMob(Sound.SplitterRoll, "Splitter", "roll");
            LoadMob(Sound.SplitterCrack, "Splitter", "crack");
            LoadMob(Sound.SplitlingPop, "Splitter", "pop");
            LoadMob(Sound.SplitterHurt, "Splitter", "hurt");
            LoadMob(Sound.SplitterDeath, "Splitter", "death");
            LoadMob(Sound.KillImpact, "Generic", "kill");
            LoadMob(Sound.HeroStunned, "Generic", "stun");
            LoadMob(Sound.HeroRooted, "Generic", "rooted");

            // До получения новых записей используем прежние банки как временную основу.
            _variants[(int)Sound.WhooshHeavy] = _variants[(int)Sound.Whoosh];
            _variants[(int)Sound.CycloneTurn] = _variants[(int)Sound.Whoosh];
            _variants[(int)Sound.RootSwarmHit] = _variants[(int)Sound.HitBody];
            _variants[(int)Sound.RootSwarmKill] = _variants[(int)Sound.Kill];
            _variants[(int)Sound.RootSwarmDissolve] = _variants[(int)Sound.Dissolve];
            if (_variants[(int)Sound.BudHurt].Length == 0) _variants[(int)Sound.BudHurt] = _variants[(int)Sound.HitBody];
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = Profile != null ? Profile.Find((Sound)i) : null;
                if (_entries[i]?.Clips != null && _entries[i].Clips.Length > 0)
                    _variants[i] = _entries[i].Clips;
            }
        }

        private readonly Dictionary<string, AudioClip[]> _mobFolders = new Dictionary<string, AudioClip[]>();

        /// <summary>Клипы слота моба по имени «слот_»; нет клипов — прежний банк звука остаётся.</summary>
        private void LoadMob(Sound sound, string mob, string slot)
        {
            if (!_mobFolders.TryGetValue(mob, out var all))
                _mobFolders[mob] = all = Resources.LoadAll<AudioClip>(MobSoundBank.Folder + "/" + mob);
            AudioClip[] clips = Named(all, slot + "_");
            if (clips.Length > 0 || _variants[(int)sound] == null) _variants[(int)sound] = clips;
        }

        private static AudioClip[] Named(AudioClip[] clips, string prefix)
        {
            var found = new List<AudioClip>();
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && clips[i].name.StartsWith(prefix, System.StringComparison.Ordinal))
                    found.Add(clips[i]);
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found.ToArray();
        }

        // Audio presentation must not touch UnityEngine.Random: keeping a private
        // generator prevents sound variation from perturbing any other system.
        private float Random01()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return (_random & 0xFFFFFFu) / 16777215f;
        }
    }
}
