using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Клипы Хозяина Чащи — контракт artifacts/tools/wf/boss-clip-spec.md (02.10):
    /// имя состояния контроллера = имя клипа, дубль в FBX — «ThicketMaster_&lt;Клип&gt;».
    /// Значения идут в контроллер по порядку <see cref="ThicketMasterClipRules.All"/>.
    /// </summary>
    public enum ThicketClip : byte
    {
        Idle = 0,
        Sleep = 1,
        Wake = 2,
        Walk = 3,
        TurnL = 4,
        TurnR = 5,
        PawR = 6,
        PawL = 7,
        Stomp = 8,
        Roar = 9,
        DiveIn = 10,
        Emerge = 11,
        SproutCast = 12,
        PollenShake = 13,
        BerryVolley = 14,
        Storm = 15,
        Death = 16,
    }

    /// <summary>
    /// Поза в кадре: клип, кадр клипа (0…длина) и доля для Motion Time
    /// (параметр «&lt;Клип&gt;Phase»). Burrowed — тело под землёй (нырок между
    /// концом DiveIn и выходом): вид прячет его, бой рисует бугор.
    /// </summary>
    public readonly struct ThicketClipPose
    {
        public readonly ThicketClip Clip;
        public readonly float Frame;
        public readonly float Phase;
        public readonly bool Burrowed;

        public ThicketClipPose(ThicketClip clip, float frame, bool burrowed = false)
        {
            Clip = clip;
            Frame = frame;
            Phase = ThicketMasterClipRules.PhaseOf(clip, frame);
            Burrowed = burrowed;
        }

        /// <summary>Поза с готовой долей — для Walk, у которого длина клипа задаётся шагом, а не кадрами.</summary>
        public static ThicketClipPose WithPhase(ThicketClip clip, float phase)
            => new ThicketClipPose(clip, phase, true, false);

        private ThicketClipPose(ThicketClip clip, float phase, bool explicitPhase, bool burrowed)
        {
            Clip = clip;
            Frame = phase * Math.Max(1, ThicketMasterClipRules.Frames(clip));
            Phase = phase;
            Burrowed = burrowed;
        }
    }

    /// <summary>
    /// ХОЗЯИН ЧАЩИ: ДЕЙСТВИЕ SIM → КЛИП И ЕГО КАДР. Без UnityEngine — правило
    /// проверяют тесты вне Unity (tools/Combat.Presentation.Tests/ThicketMasterClipRulesTests.cs),
    /// в том числе на живой симуляции: в тик каждого EnemyActionImpact самого босса
    /// кадр клипа — ровно кадр контакта.
    ///
    /// ТЕМП 02.10 (artifacts/tools/wf/boss-tempo-contract.md). Клипы авторские (контракт
    /// клипов, кадр N = тик N), Sim короче — вид растягивает и сжимает их по тикам
    /// Sim, новые клипы не нужны. Время берётся только из полей ThicketMasterState:
    /// «Сложно» удлиняет первый замах серии до 30 — контакт всё равно на тике удара.
    /// • Серия лапы П/Л/П: замах шага — от его знака до удара (15, дальше по 9). Удар
    ///   k и знак k+1 Sim даёт в одном тике: тик удара показывает контакт прошлой лапы
    ///   (кадр 24→25), следующая лапа — со следующего тика. PawL кадр 0 = PawR кадр 25
    ///   (клип собран как связка) — переход П→Л без смеси; Л→П — смесь
    ///   <see cref="PawLeftToRightBlendTicks"/>. Отход последней лапы (24–49) идёт уже
    ///   после конца действия — <see cref="TailEndTick"/>.
    /// • Топот: дыбом 0–33 к первому удару, лапы в землю 33–35, присед в замедлении
    ///   35–<see cref="StompRingFrame"/> до кольца (тело прижато, пока уходит вторая
    ///   волна), подъём после кольца — в хвосте.
    /// • Касты — только жест 18 тиков, круги и облака идут сами: прорастание — нажим
    ///   и быстрое вырывание, пыльца — тряска в жест, оседание в хвосте, ливень —
    ///   толчки груди циклом раз в 12 тиков, пик толчка — на знаке залпа (ягоды
    ///   вылетают), пока тело свободно.
    /// • Хвост: действие Sim кончилось, а клип ещё доигрывает (отход лапы, подъём
    ///   после топота, оседание каста) — вид продолжает ту же функцию по запомненному
    ///   действию до TailEndTick, если босс не пошёл и не начал новое.
    ///
    /// ПЕСОЧНЫЕ ЧАСЫ. Sim сдвигает StageStartTick, ImpactTick (впереди), LastImpactTick
    /// (впереди) и EndTick, но не StartTick. Поэтому отрезки строятся только от
    /// сдвигаемых полей, а вид держит позу, пока босс стоит под Часами
    /// (ThicketMasterFrozenTicksLeft). Прошедший удар Sim не сдвигает — его догоняет
    /// <see cref="ThicketHourglassTrack"/>: после Часов кадр продолжается с того же места
    /// и в замахе, и в стойке после удара.
    /// </summary>
    public static class ThicketMasterClipRules
    {
        public const float FramesPerSecond = Simulation.TicksPerSecond;

        /// <summary>Префикс дублей в FBX: «ThicketMaster_PawR».</summary>
        public const string TakePrefix = "ThicketMaster_";

        // ---- длины клипов, кадры (контракт) ----

        public const int IdleFrames = 90, SleepFrames = 90, WakeFrames = 30, TurnFrames = 30;
        public const int PawFrames = 49, StompFrames = 71, RoarFrames = 60, DiveInFrames = 12, EmergeFrames = 36;
        public const int SproutFrames = 90, PollenFrames = 48, VolleyFrames = 120, StormFrames = 159, DeathFrames = 90;

        // ---- ключевые кадры (контракт) ----

        /// <summary>Лапа: замах 0–24 (верх 16–19), контакт 24 (лапа легла), отход 25–49 (к Idle 0).</summary>
        public const int PawContactFrame = 24;

        /// <summary>
        /// Отход последней лапы серии 24→49 — за столько тиков (×1,4): Sim снимает действие через
        /// тик после удара, отход играет хвост, следующая серия может начаться через 14–18 тиков.
        /// </summary>
        public const int PawRecoveryTicks = 18;

        /// <summary>Топот: лапы в землю на 33 (контакт 33–35), присед 35–39, подъём 39–71.</summary>
        public const int StompContactFrame = 33, StompPlantedFrame = 35;

        /// <summary>
        /// Кадр второго кольца топота: 35→42 (присед, хлыст кроны и куста) тянется на 13 тиков
        /// между лапами в земле и кольцом — тело прижато, пока уходит волна; подъём 42–71 — 1:1.
        /// </summary>
        public const int StompRingFrame = 42;

        /// <summary>Рёв: вдох 0–15, рёв 15–45 (отброс на 45), оседание 45–60.</summary>
        public const int RoarContactFrame = 45;

        /// <summary>Пыльца: тряска кроной 4–24 (сжата в жест), оседание 24–48 (хвост, 1:1).</summary>
        public const int PollenContactFrame = 24;

        /// <summary>Прорастание: лапы вдавлены к 8, держит 8–80, вырывает 80–90.</summary>
        public const int SproutPressedFrame = 8, SproutReleaseFrame = 80;

        /// <summary>Прорастание в жесте: вырывание 80–90 — за последние столько тиков жеста.</summary>
        public const int SproutReleaseTicks = 8;

        /// <summary>
        /// Ливень (клип BerryVolley): пики толчков груди 3 / 18 / 33 (ягоды вылетают), отрезок
        /// 18–33 — ровный цикл (все дорожки клипа повторяются через 15). Толчок v ставится на
        /// знак залпа v (тик каста + 12v): 3→18 за первые 12 тиков, дальше цикл 18→33 до
        /// последнего толчка, потом 33→60 — взгляд за ягодами до последнего шлепка, 60→120 —
        /// тяжёлая стойка и подъём за <see cref="VolleySettleTicks"/>.
        /// </summary>
        public const int VolleyFirstPopFrame = 3, VolleyLoopFrom = 18, VolleyLoopTo = 33, VolleyWatchFrame = 60;
        public const int VolleySettleTicks = 40;

        /// <summary>Старые метки клипа (залпы на 0/15/30, удары 30/45/60) — для справки и сборщика.</summary>
        public const int VolleyFirstImpactFrame = 30, VolleyLastImpactFrame = 60;

        // ---- смеси, тики Sim (вид переводит в секунды) ----

        /// <summary>Новое действие из покоя, хода, хвоста или другого действия.</summary>
        public const int ActionBlendTicks = 3;

        /// <summary>Правая лапа после левой в серии: PawL кончается не в начале PawR — короткая смесь.</summary>
        public const int PawLeftToRightBlendTicks = 4;

        /// <summary>Покой, сон, ход между собой и из хвоста.</summary>
        public const int LocomotionBlendTicks = 6;

        /// <summary>Разворот на месте: в него и из него.</summary>
        public const int TurnBlendTicks = 5;

        public const int DeathBlendTicks = 4;

        /// <summary>Буря: крона раскрывается 0–12, волны на 75 и 135, закрывается 135–159.</summary>
        public const int StormFirstWaveFrame = 75, StormSecondWaveFrame = 135;

        /// <summary>Смерть: заваливается на бок 45–70 — касание земли боком (LandsAt профиля), лежит 70–90.</summary>
        public const int DeathGroundFrame = 65;

        /// <summary>Разворот на месте: ~90° за клип.</summary>
        public const float TurnClipDegrees = 90f;

        /// <summary>
        /// Метров на цикл Walk (два шага пар) в масштабе 1 — замер анимации 02.10
        /// после правки (production/unity_package/export.json: stride_m_per_cycle 1,7857,
        /// цикл 30 кадров; Sim 2,6 м/с → ×1,456). Сборщик ThicketMasterBuilder читает
        /// свежий замер из отчёта клипов и пишет его в префаб; это число — запас без отчёта.
        /// </summary>
        public const float DefaultWalkStride = 1.7857f;

        /// <summary>Все клипы контракта в порядке состояний контроллера.</summary>
        public static readonly ThicketClip[] All =
        {
            ThicketClip.Idle, ThicketClip.Sleep, ThicketClip.Wake, ThicketClip.Walk, ThicketClip.TurnL, ThicketClip.TurnR,
            ThicketClip.PawR, ThicketClip.PawL, ThicketClip.Stomp, ThicketClip.Roar, ThicketClip.DiveIn, ThicketClip.Emerge,
            ThicketClip.SproutCast, ThicketClip.PollenShake, ThicketClip.BerryVolley, ThicketClip.Storm, ThicketClip.Death,
        };

        private static readonly string[] Names =
        {
            "Idle", "Sleep", "Wake", "Walk", "TurnL", "TurnR", "PawR", "PawL", "Stomp", "Roar", "DiveIn", "Emerge",
            "SproutCast", "PollenShake", "BerryVolley", "Storm", "Death",
        };

        private static readonly string[] Phases =
        {
            "IdlePhase", "SleepPhase", "WakePhase", "WalkPhase", "TurnLPhase", "TurnRPhase", "PawRPhase", "PawLPhase",
            "StompPhase", "RoarPhase", "DiveInPhase", "EmergePhase", "SproutCastPhase", "PollenShakePhase",
            "BerryVolleyPhase", "StormPhase", "DeathPhase",
        };

        /// <summary>Имя состояния контроллера (Base Layer.&lt;имя&gt;) и роли клипа.</summary>
        public static string Name(ThicketClip clip) => Names[(int)clip];

        /// <summary>Параметр Motion Time состояния.</summary>
        public static string PhaseParameter(ThicketClip clip) => Phases[(int)clip];

        /// <summary>Имя дубля в FBX.</summary>
        public static string Take(ThicketClip clip) => TakePrefix + Names[(int)clip];

        /// <summary>
        /// Второе состояние того же клипа (свой параметр Motion Time): клип, который идёт сам за
        /// собой (серия фазы 3 кончается правой и начинается правой; топот за топотом), иначе
        /// прыгнул бы с отхода на кадр 0 — со смесью Motion Time одного состояния не смешать.
        /// Вид чередует копии; сборщик заводит копию рядом с основным состоянием.
        /// </summary>
        public static bool HasAlternate(ThicketClip clip)
            => clip == ThicketClip.PawR || clip == ThicketClip.PawL || clip == ThicketClip.Stomp;

        /// <summary>Имя второго состояния: «PawRAlt».</summary>
        public static string AlternateName(ThicketClip clip) => Names[(int)clip] + "Alt";

        /// <summary>Параметр Motion Time второго состояния: «PawRAltPhase».</summary>
        public static string AlternatePhaseParameter(ThicketClip clip) => Names[(int)clip] + "AltPhase";

        /// <summary>
        /// Смесь в тиках при смене клипа действия. sameAction — тот же номер действия (шаг серии):
        /// П→Л — 0 (PawL кадр 0 = PawR кадр 25), Л→П — <see cref="PawLeftToRightBlendTicks"/>;
        /// всё прочее — <see cref="ActionBlendTicks"/>.
        /// </summary>
        public static int ActionBlend(ThicketClip from, ThicketClip to, bool sameAction)
        {
            if (sameAction && from == ThicketClip.PawR && to == ThicketClip.PawL) return 0;
            if (sameAction && from == ThicketClip.PawL && to == ThicketClip.PawR) return PawLeftToRightBlendTicks;
            return ActionBlendTicks;
        }

        /// <summary>Длина клипа в кадрах по контракту; 0 — длина задаётся шагом (Walk), сборщик её не сверяет.</summary>
        public static int Frames(ThicketClip clip)
        {
            switch (clip)
            {
                case ThicketClip.Idle: return IdleFrames;
                case ThicketClip.Sleep: return SleepFrames;
                case ThicketClip.Wake: return WakeFrames;
                case ThicketClip.TurnL:
                case ThicketClip.TurnR: return TurnFrames;
                case ThicketClip.PawR:
                case ThicketClip.PawL: return PawFrames;
                case ThicketClip.Stomp: return StompFrames;
                case ThicketClip.Roar: return RoarFrames;
                case ThicketClip.DiveIn: return DiveInFrames;
                case ThicketClip.Emerge: return EmergeFrames;
                case ThicketClip.SproutCast: return SproutFrames;
                case ThicketClip.PollenShake: return PollenFrames;
                case ThicketClip.BerryVolley: return VolleyFrames;
                case ThicketClip.Storm: return StormFrames;
                case ThicketClip.Death: return DeathFrames;
                default: return 0;
            }
        }

        /// <summary>Циклы: покой, сон, ход. Остальное играется один раз фазой от тиков.</summary>
        public static bool Loops(ThicketClip clip)
            => clip == ThicketClip.Idle || clip == ThicketClip.Sleep || clip == ThicketClip.Walk;

        /// <summary>Без клипа игра всё равно идёт (вид подставляет Idle): обязателен только покой.</summary>
        public static bool Required(ThicketClip clip) => clip == ThicketClip.Idle;

        /// <summary>Доля Motion Time для кадра: цикл — по модулю, разовый — 0…1, последний кадр держится.</summary>
        public static float PhaseOf(ThicketClip clip, float frame)
        {
            int frames = Frames(clip);
            if (frames <= 0) return Repeat(frame);
            float phase = frame / frames;
            return Loops(clip) ? Repeat(phase) : Clamp01(phase);
        }

        /// <summary>Лапа удара stage серии: чётный — правая (PawR), нечётный — левая (PawL): П/Л/П.</summary>
        public static bool PawIsRight(int stage) => (stage & 1) == 0;

        public static ThicketClip PawClip(int stage) => PawIsRight(stage) ? ThicketClip.PawR : ThicketClip.PawL;

        /// <summary>Событие Thicket* (EnemyActionKind 11…19).</summary>
        public static bool IsThicketKind(EnemyActionKind kind)
            => kind >= EnemyActionKind.ThicketPaw && kind <= EnemyActionKind.ThicketStorm;

        /// <summary>Тик, с которого тело под землёй: конец ухода в нырке (ImpactTick − фиксация − ход бугра).</summary>
        public static int BurrowEndTick(in ThicketMasterState a)
            => a.ImpactTick - Simulation.ThicketDiveLockTicks - Simulation.ThicketDiveTravelTicks;

        /// <summary>
        /// Поза идущего действия на тике tick (тик Sim с долей кадра: Tick − 1 + Alpha).
        /// Действие без клипа (None) — покой.
        /// </summary>
        public static ThicketClipPose Action(in ThicketMasterState a, float tick)
        {
            switch (a.Action)
            {
                case ThicketMasterAction.Wake:
                    // 30 тиков пробуждения = 30 кадров: лапы из земли к 20, голова к 30; дальше рёв.
                    return new ThicketClipPose(ThicketClip.Wake, Segment(tick, a.StageStartTick, a.EndTick, 0f, WakeFrames));

                case ThicketMasterAction.Roar:
                    return new ThicketClipPose(ThicketClip.Roar,
                        Two(tick, a.StageStartTick, a.ImpactTick, a.EndTick, 0f, RoarContactFrame, RoarFrames));

                case ThicketMasterAction.Paw:
                {
                    // Удар прошлой лапы и знак этой Sim даёт в одном тике (StageStartTick):
                    // тот тик — контакт прошлой лапы (24→25), замах этой — со следующего.
                    // PawL кадр 0 = PawR кадр 25: переход П→Л без скачка.
                    const int strike = Simulation.ThicketPawStrikeTicks;
                    int start = a.StageStartTick;
                    if (a.Stage > 0)
                    {
                        if (tick < start + strike)
                            return new ThicketClipPose(PawClip(a.Stage - 1),
                                Segment(tick, start, start + strike, PawContactFrame, PawContactFrame + strike));
                        start += strike;
                    }
                    if (tick < a.ImpactTick)
                        return new ThicketClipPose(PawClip(a.Stage), Segment(tick, start, a.ImpactTick, 0f, PawContactFrame));
                    // Последняя лапа отходит 24→49 за PawRecoveryTicks — уже в хвосте после конца действия.
                    bool last = a.Stage + 1 >= a.Stages;
                    return new ThicketClipPose(PawClip(a.Stage), last
                        ? Segment(tick, a.ImpactTick, a.ImpactTick + PawRecoveryTicks, PawContactFrame, PawFrames)
                        : Segment(tick, a.ImpactTick, a.ImpactTick + strike, PawContactFrame, PawContactFrame + strike));
                }

                case ThicketMasterAction.Stomp:
                    return new ThicketClipPose(ThicketClip.Stomp, StompFrame(a, tick));

                case ThicketMasterAction.Dive:
                {
                    if (a.HitResolved)
                    {
                        // Выход (= удар) — кадр 0 Emerge; стойка после него — остаток клипа.
                        int from = a.Stage >= 3 ? a.StageStartTick : a.ImpactTick;
                        return new ThicketClipPose(ThicketClip.Emerge, Segment(tick, from, a.EndTick, 0f, EmergeFrames));
                    }
                    int burrowEnd = BurrowEndTick(a);
                    if (a.Stage == 0 && tick < burrowEnd)
                        return new ThicketClipPose(ThicketClip.DiveIn,
                            Segment(tick, a.StageStartTick, burrowEnd, 0f, DiveInFrames));
                    // Бугор едет и круг лежит: тела нет до выхода.
                    return new ThicketClipPose(ThicketClip.DiveIn, DiveInFrames, burrowed: true);
                }

                case ThicketMasterAction.Sprout:
                {
                    // Жест (StageStartTick … EndTick, Часы сдвигают оба): подъём и нажим 0–8 1:1,
                    // короткое удержание 8→, вырывание 80–90 за последние SproutReleaseTicks.
                    // Круги идут без босса (фоновая опасность) — удары их в клип не ключены.
                    int start = a.StageStartTick;
                    int pressed = Math.Min(start + SproutPressedFrame, a.EndTick);
                    int release = Math.Max(pressed, a.EndTick - SproutReleaseTicks);
                    float held = Math.Min(SproutReleaseFrame, SproutPressedFrame + (release - pressed));
                    if (tick < pressed) return new ThicketClipPose(ThicketClip.SproutCast, Segment(tick, start, pressed, 0f, SproutPressedFrame));
                    if (tick < release) return new ThicketClipPose(ThicketClip.SproutCast, Segment(tick, pressed, release, SproutPressedFrame, held));
                    return new ThicketClipPose(ThicketClip.SproutCast, Segment(tick, release, a.EndTick, SproutReleaseFrame, SproutFrames));
                }

                case ThicketMasterAction.Pollen:
                    // Тряска 0–24 — в жест (облака падают сами на 24-м тике), оседание 24–48 — хвост 1:1.
                    return new ThicketClipPose(ThicketClip.PollenShake,
                        Two(tick, a.StageStartTick, a.EndTick, a.EndTick + (PollenFrames - PollenContactFrame),
                            0f, PollenContactFrame, PollenFrames));

                case ThicketMasterAction.Rain:
                    return new ThicketClipPose(ThicketClip.BerryVolley, VolleyFrame(tick, a.StageStartTick));

                case ThicketMasterAction.Storm:
                {
                    int first = a.LastImpactTick - Simulation.ThicketStormSecondWaveTicks;
                    int start = first - Simulation.ThicketStormFirstWaveTicks;
                    return new ThicketClipPose(ThicketClip.Storm, Three(tick, start, first, a.LastImpactTick, a.EndTick,
                        0f, StormFirstWaveFrame, StormSecondWaveFrame, StormFrames));
                }

                default:
                    return new ThicketClipPose(ThicketClip.Idle, 0f);
            }
        }

        /// <summary>
        /// Кадр контакта для события EnemyActionImpact самого босса (amount — его Amount): лапа k,
        /// круг (0) и кольцо (1) топота, рёв, выход нырка, волны бури. False — удар не тела:
        /// круги прорастания, залпы ливня и облака пыльцы бьют сами (фоновая опасность после
        /// жеста), босс в это время свободен — идёт, стоит или бьёт лапой.
        /// </summary>
        public static bool TryContact(EnemyActionKind kind, int amount, out ThicketClip clip, out float frame)
        {
            switch (kind)
            {
                case EnemyActionKind.ThicketPaw: clip = PawClip(amount); frame = PawContactFrame; return true;
                case EnemyActionKind.ThicketStomp:
                    clip = ThicketClip.Stomp;
                    frame = amount == 0 ? StompContactFrame : StompRingFrame;
                    return true;
                case EnemyActionKind.ThicketRoar: clip = ThicketClip.Roar; frame = RoarContactFrame; return true;
                case EnemyActionKind.ThicketDive: clip = ThicketClip.Emerge; frame = 0f; return true;
                case EnemyActionKind.ThicketStorm:
                    clip = ThicketClip.Storm;
                    frame = amount == 0 ? StormFirstWaveFrame : StormSecondWaveFrame;
                    return true;
                default:
                    clip = ThicketClip.Idle; frame = 0f; return false;
            }
        }

        /// <summary>
        /// Топот: шаг 0 — дыбом 0→33 к удару круга. Шаг 1 (кольцо; встаёт в тик удара круга,
        /// StageStartTick — этот тик): лапы в земле 33→35 за ThicketStompStrikeTicks, присед
        /// 35→<see cref="StompRingFrame"/> до кольца, подъём →71 — 1:1 после него (хвост).
        /// Топот в один круг (стенд, старые данные) — присед и подъём после удара 1:1.
        /// </summary>
        private static float StompFrame(in ThicketMasterState a, float tick)
        {
            const int strike = Simulation.ThicketStompStrikeTicks;
            if (a.Stage == 0)
            {
                if (tick < a.ImpactTick) return Segment(tick, a.StageStartTick, a.ImpactTick, 0f, StompContactFrame);
                return a.Stages > 1
                    ? Segment(tick, a.ImpactTick, a.ImpactTick + strike, StompContactFrame, StompPlantedFrame)
                    : Two(tick, a.ImpactTick, a.ImpactTick + strike, a.ImpactTick + strike + (StompFrames - StompPlantedFrame),
                        StompContactFrame, StompPlantedFrame, StompFrames);
            }
            int planted = a.StageStartTick + strike;
            if (tick < planted) return Segment(tick, a.StageStartTick, planted, StompContactFrame, StompPlantedFrame);
            if (tick < a.ImpactTick) return Segment(tick, planted, a.ImpactTick, StompPlantedFrame, StompRingFrame);
            return Segment(tick, a.ImpactTick, a.ImpactTick + (StompFrames - StompRingFrame), StompRingFrame, StompFrames);
        }

        /// <summary>
        /// Ливень от тика каста start: толчок v (пик груди, ягоды вылетают) — на знаке залпа v
        /// (start + 12v). 3→18 за первый промежуток, цикл 18→33 до последнего толчка, 33→60 до
        /// последнего шлепка, 60→120 за VolleySettleTicks.
        /// </summary>
        public static float VolleyFrame(float tick, int start)
        {
            int every = Simulation.ThicketRainEveryTicks;
            int lastPop = start + every * Math.Max(1, Simulation.ThicketRainVolleys - 1);
            if (tick < start + every)
                return Segment(tick, start, start + every, VolleyFirstPopFrame, VolleyLoopFrom);
            if (tick < lastPop)
                return VolleyLoopFrom + (VolleyLoopTo - VolleyLoopFrom) * Repeat((tick - start - every) / every);
            int land = lastPop + Simulation.ThicketRainImpactTicks;
            if (tick < land) return Segment(tick, lastPop, land, VolleyLoopTo, VolleyWatchFrame);
            return Segment(tick, land, land + VolleySettleTicks, VolleyWatchFrame, VolleyFrames);
        }

        /// <summary>Тик последнего толчка ливня (каст в start): после него ягоды больше не вылетают.</summary>
        public static int VolleyLastPopTick(int start)
            => start + Simulation.ThicketRainEveryTicks * Math.Max(1, Simulation.ThicketRainVolleys - 1);

        /// <summary>
        /// До какого тика клип действия ещё играет (дальше — последний кадр, вид уходит в покой).
        /// После EndTick это хвост: отход последней лапы, подъём после кольца топота, оседание
        /// пыльцы, толчки и стойка ливня. У прочих клип кончается вместе с действием.
        /// </summary>
        public static int TailEndTick(in ThicketMasterState a)
        {
            switch (a.Action)
            {
                case ThicketMasterAction.Paw:
                    return a.Stage + 1 >= a.Stages ? Math.Max(a.EndTick, a.ImpactTick + PawRecoveryTicks) : a.EndTick;
                case ThicketMasterAction.Stomp:
                    return a.Stage + 1 >= a.Stages && a.Stages > 1
                        ? Math.Max(a.EndTick, a.ImpactTick + (StompFrames - StompRingFrame))
                        : Math.Max(a.EndTick, a.ImpactTick + Simulation.ThicketStompStrikeTicks + (StompFrames - StompPlantedFrame));
                case ThicketMasterAction.Pollen:
                    return a.EndTick + (PollenFrames - PollenContactFrame);
                case ThicketMasterAction.Rain:
                    return VolleyLastPopTick(a.StageStartTick) + Simulation.ThicketRainImpactTicks + VolleySettleTicks;
                default:
                    return a.EndTick;
            }
        }

        /// <summary>Все сроки действия на ticks позже: хвост, пока босс стоит под Песочными Часами.</summary>
        public static ThicketMasterState Shift(ThicketMasterState a, int ticks)
        {
            a.StartTick += ticks; a.StageStartTick += ticks;
            a.ImpactTick += ticks; a.LastImpactTick += ticks; a.EndTick += ticks;
            return a;
        }

        /// <summary>
        /// Тик для сроков действия босса id (tick — Tick − 1 + Alpha). Под Песочными Часами Sim
        /// уже сдвинула его сроки на всю заморозку, а босс стоит: часы стоят на её конце
        /// (Tick − 1 + оставшиеся тики), и после Часов отсчёт идёт дальше без скачка.
        /// </summary>
        public static float BossClock(Simulation sim, int id, float tick)
        {
            int frozen = sim.ThicketMasterFrozenTicksLeft(id);
            return frozen > 0 ? sim.Tick - 1 + frozen : tick;
        }

        // ---------- вне действия ----------

        /// <summary>Доля цикла покоя или сна по тикам Sim: пауза и Часы держат позу сами.</summary>
        public static float LoopPhase(float ticks, int frames) => Repeat(ticks / Math.Max(1, frames));

        /// <summary>Сколько циклов Walk за metres пути при шаге stride (м на цикл) и масштабе тела scale.</summary>
        public static float WalkCycles(float metres, float stride, float scale = 1f)
            => metres / Math.Max(.05f, stride * Math.Max(.1f, scale));

        /// <summary>
        /// Доля клипа разворота по накопленному повороту: ~90° на клип, дальше по кругу.
        /// Ровно кратный 90° поворот — конец клипа, а не его начало.
        /// </summary>
        public static float TurnPhase(float travelledDegrees)
        {
            float travel = Math.Abs(travelledDegrees);
            float phase = Repeat(travel / TurnClipDegrees);
            return phase < .0001f && travel > 1f ? 1f : phase;
        }

        /// <summary>
        /// Кадр Death через seconds секунд от смерти: стоп-кадр тяжёлого убийства (hold)
        /// держит кадр 0, потом 0–65 к fall (касание земли боком — LandsAt профиля),
        /// 65–90 за rest, последний кадр держится.
        /// </summary>
        public static float DeathFrame(float seconds, float hold, float fall, float rest)
        {
            if (seconds <= hold) return 0f;
            fall = Math.Max(hold + .05f, fall);
            if (seconds < fall) return DeathGroundFrame * (seconds - hold) / (fall - hold);
            return DeathGroundFrame + (DeathFrames - DeathGroundFrame) * Clamp01((seconds - fall) / Math.Max(.05f, rest));
        }

        // ---------- отрезки ----------

        /// <summary>Кадр на отрезке [t0, t1] → [f0, f1]; пустой отрезок — ступенька в t0.</summary>
        public static float Segment(float tick, int t0, int t1, float f0, float f1)
        {
            if (t1 <= t0) return tick < t0 ? f0 : f1;
            return f0 + (f1 - f0) * Clamp01((tick - t0) / (t1 - t0));
        }

        private static float Two(float tick, int t0, int t1, int t2, float f0, float f1, float f2)
            => tick < t1 ? Segment(tick, t0, t1, f0, f1) : Segment(tick, t1, t2, f1, f2);

        private static float Three(float tick, int t0, int t1, int t2, int t3, float f0, float f1, float f2, float f3)
            => tick < t1 ? Segment(tick, t0, t1, f0, f1)
                : tick < t2 ? Segment(tick, t1, t2, f1, f2) : Segment(tick, t2, t3, f2, f3);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static float Repeat(float value) => value - (float)Math.Floor(value);
    }

    /// <summary>
    /// ПЕСОЧНЫЕ ЧАСЫ В СТОЙКЕ. Sim (ShiftThicketMastersForHourglass) сдвигает StageStartTick
    /// и EndTick на всю заморозку всегда, а ImpactTick / LastImpactTick — только впереди:
    /// прошедший удар остаётся. Отрезок отхода [удар, конец] растянулся бы на 60 тиков, и
    /// кадр после Часов прыгнул бы вперёд (стойка ливня — на треть клипа). Трекер видит
    /// сдвиг StageStartTick внутри того же шага — сам по себе он меняется только сменой
    /// шага — и сдвигает прошедшие контакты на столько же. Только для вида: Sim не трогается.
    /// Один трекер на тело; новый номер действия обнуляет сдвиг.
    /// </summary>
    public struct ThicketHourglassTrack
    {
        private int _serial, _stage, _stageStart, _impact, _last, _impactShift, _lastShift;

        /// <summary>Действие для <see cref="ThicketMasterClipRules.Action"/>: прошедшие контакты догнали Часы.</summary>
        public ThicketMasterState Apply(in ThicketMasterState a)
        {
            if (a.Serial != _serial)
                _impactShift = _lastShift = 0;
            else if (a.Stage != _stage)
            {
                // Новый шаг: новый удар — свой счёт; общий последний удар действия сдвиг держит.
                if (a.ImpactTick != _impact) _impactShift = 0;
                if (a.LastImpactTick != _last) _lastShift = 0;
            }
            else if (a.StageStartTick > _stageStart)
            {
                int shift = a.StageStartTick - _stageStart;
                if (a.ImpactTick == _impact) _impactShift += shift;
                if (a.LastImpactTick == _last) _lastShift += shift;
            }
            _serial = a.Serial; _stage = a.Stage; _stageStart = a.StageStartTick;
            _impact = a.ImpactTick; _last = a.LastImpactTick;
            ThicketMasterState shown = a;
            shown.ImpactTick += _impactShift;
            shown.LastImpactTick += _lastShift;
            return shown;
        }
    }
}
