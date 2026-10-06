namespace Game.Sim
{
    /// <summary>
    /// Фаза Броска якоря (Simulation.AnchorThrow). Значения навсегда, только дописывать:
    /// идут в хеш и в снимок для вида.
    /// </summary>
    public enum AnchorThrowPhase : byte
    {
        None = 0,

        /// <summary>Замах: герой стоит AnchorThrowState.WindupTicks (2, курсор за спиной — 3), взгляд Sim уже на Dir.</summary>
        Windup = 1,

        /// <summary>Якорь летит по прямой от руки (шаг Step на тик) и бьёт всех на полосе.</summary>
        Flight = 2,

        /// <summary>Натяг — один тик TautTick: голова в конце, цепь прямая, задетые трогаются (ForcedMotionKind.Reeled).</summary>
        Taut = 3,

        /// <summary>Возврат: голова и тела по кривой Водоворота (MaelstromPullProgress) к ловле.</summary>
        Return = 4,

        /// <summary>Ловля в CatchTick и удержание AnchorThrowHoldTicks.</summary>
        Catch = 5,

        /// <summary>Выход в стойку сабли; с ExitWalkTick ходьба срывает выход.</summary>
        Exit = 6,
    }

    /// <summary>Как кончился Бросок якоря — Amount события AnchorThrowEnded. Только дописывать.</summary>
    public enum AnchorThrowEnd : byte
    {
        /// <summary>Выход доигран — или после ловли каст сняла другая способность (связка «Бросок → Вихрь»).</summary>
        Done = 0,

        /// <summary>Хвост выхода сорван ходьбой.</summary>
        WalkedOut = 1,

        /// <summary>Сняли до ловли: рывок, оглушение, смерть, чужой отброс героя. Тяги сняты, тела стоят.</summary>
        Interrupted = 2,
    }

    /// <summary>Чем кончился полёт полосы (StopKind0/1/2). Только дописывать.</summary>
    public enum AnchorThrowStop : byte
    {
        /// <summary>Вся длина цепи.</summary>
        Full = 0,

        /// <summary>Стена, дерево, край поляны: дальность — последняя проходимая проба.</summary>
        Wall = 1,

        /// <summary>Корпус Хозяина Чащи: голова втыкается, за корпусом никто не задет.</summary>
        Boss = 2,

        /// <summary>Гарпун: голова вонзилась в первого задетого.</summary>
        Harpoon = 3,
    }

    /// <summary>
    /// Снимок Броска якоря. Пишет только симуляция; вид читает, откуда и куда
    /// летят головы (AnchorThrowHead), когда натяг и ловля, кто тянется. Входит
    /// в хеш, пока за расстановку был хоть один Бросок.
    /// Контракт anchor-core/DESIGN.md §1.4 — свойства-синонимы внизу, второго состояния нет.
    /// </summary>
    public struct AnchorThrowState
    {
        /// <summary>Номер каста с начала расстановки; 0 — Броска ещё не было.</summary>
        public int Serial;

        public int Slot;
        public AnchorThrowPhase Phase;

        /// <summary>Форма каста (PelagForm.AnchorThrowNet/Fan/Harpoon или None).</summary>
        public PelagForm Form;

        /// <summary>
        /// Каст; замах этого каста; выпуск (= CastTick + WindupTicks); тики полёта самой
        /// длинной полосы; натяг; тики возврата; ловля (= TautTick + ReturnTicks); конец
        /// стоячей фазы; с какого тика ходьба срывает выход.
        /// </summary>
        public int CastTick, WindupTicks, ReleaseTick, FlightTicks, TautTick, ReturnTicks, CatchTick, PhaseEndTick, ExitWalkTick;

        /// <summary>Центр героя в каст (от него меряются полосы) и рука в выпуск (Center + Dir · 0,5).</summary>
        public FixVec2 Center, Origin;

        /// <summary>Главная полоса — к курсору; Веер — ещё Dir1 (+30°) и Dir2 (−30°).</summary>
        public FixVec2 Dir, Dir1, Dir2;

        /// <summary>Полос: 1 или 3 (Веер).</summary>
        public int Lanes;

        /// <summary>Дальность полосы от центра героя (после стен, корпуса босса и укуса Гарпуна).</summary>
        public Fix64 Reach0, Reach1, Reach2;

        /// <summary>Шаг головы полосы за тик полёта, м.</summary>
        public Fix64 Step0, Step1, Step2;

        /// <summary>Тики полёта полосы (голова в конце — ReleaseTick + FlightL).</summary>
        public int Flight0, Flight1, Flight2;

        /// <summary>Чем кончился полёт полосы.</summary>
        public AnchorThrowStop StopKind0, StopKind1, StopKind2;

        /// <summary>Полуширина полосы удара (без тела); полуширина сети Невода (0 — сети нет).</summary>
        public Fix64 HalfWidth, NetHalfWidth;

        /// <summary>Цель Гарпуна (−1 — нет).</summary>
        public int HarpoonTarget;

        /// <summary>Сколько тел пошло на тягу в натяг; радиус полукольца мест (0 — тянуть некого).</summary>
        public int ReeledCount;
        public Fix64 RingRadius;

        // ---- контракт anchor-core/DESIGN.md §1.4 (синонимы) ----

        public int ReachTick => TautTick;
        public int YankStartTick => TautTick;
        public int YankEndTick => CatchTick;
        public FixVec2 Direction => Dir;
        public Fix64 Range => Reach0;
        public bool Stuck => StopKind0 == AnchorThrowStop.Harpoon;
        public int Target => HarpoonTarget;

        /// <summary>Угол призраков Веера, радианы (π/6); не Веер — 0.</summary>
        public Fix64 GhostAngle => Lanes == 3 ? Simulation.AnchorThrowFanAngle : Fix64.Zero;

        public FixVec2 LaneDir(int lane) => lane == 1 ? Dir1 : lane == 2 ? Dir2 : Dir;
        public Fix64 LaneReach(int lane) => lane == 1 ? Reach1 : lane == 2 ? Reach2 : Reach0;
        public Fix64 LaneStep(int lane) => lane == 1 ? Step1 : lane == 2 ? Step2 : Step0;
        public int LaneFlight(int lane) => lane == 1 ? Flight1 : lane == 2 ? Flight2 : Flight0;
        public AnchorThrowStop LaneStop(int lane) => lane == 1 ? StopKind1 : lane == 2 ? StopKind2 : StopKind0;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Slot); Hashing.Mix(ref hash, (int)Phase);
            Hashing.Mix(ref hash, (int)Form);
            Hashing.Mix(ref hash, CastTick); Hashing.Mix(ref hash, WindupTicks); Hashing.Mix(ref hash, ReleaseTick);
            Hashing.Mix(ref hash, FlightTicks); Hashing.Mix(ref hash, TautTick); Hashing.Mix(ref hash, ReturnTicks);
            Hashing.Mix(ref hash, CatchTick); Hashing.Mix(ref hash, PhaseEndTick); Hashing.Mix(ref hash, ExitWalkTick);
            Hashing.Mix(ref hash, Center.X); Hashing.Mix(ref hash, Center.Y);
            Hashing.Mix(ref hash, Origin.X); Hashing.Mix(ref hash, Origin.Y);
            Hashing.Mix(ref hash, Dir.X); Hashing.Mix(ref hash, Dir.Y);
            Hashing.Mix(ref hash, Dir1.X); Hashing.Mix(ref hash, Dir1.Y);
            Hashing.Mix(ref hash, Dir2.X); Hashing.Mix(ref hash, Dir2.Y);
            Hashing.Mix(ref hash, Lanes);
            Hashing.Mix(ref hash, Reach0); Hashing.Mix(ref hash, Reach1); Hashing.Mix(ref hash, Reach2);
            Hashing.Mix(ref hash, Step0); Hashing.Mix(ref hash, Step1); Hashing.Mix(ref hash, Step2);
            Hashing.Mix(ref hash, Flight0); Hashing.Mix(ref hash, Flight1); Hashing.Mix(ref hash, Flight2);
            Hashing.Mix(ref hash, (int)StopKind0 | (int)StopKind1 << 8 | (int)StopKind2 << 16);
            Hashing.Mix(ref hash, HalfWidth); Hashing.Mix(ref hash, NetHalfWidth);
            Hashing.Mix(ref hash, HarpoonTarget); Hashing.Mix(ref hash, ReeledCount); Hashing.Mix(ref hash, RingRadius);
        }
    }

    /// <summary>
    /// БРОСОК ЯКОРЯ — новый навык 03.10 по спеке artifacts/anchor-throw/plan/SPEC.md
    /// (владелец: «летит сразу», на ~7 м, бьёт всех на линии, на возврате тянет
    /// задетых почти вплотную; тяжёлые, элита и босс не тянутся). Быстрый каст:
    /// нажатие = бросок к курсору, цели не нужно.
    ///
    /// * ЗАМАХ 2 тика стоя (курсор дальше 90° от взгляда — 3), взгляд Sim сразу на Dir.
    /// * ПОЛЁТ по прямой от руки (0,5 м) до дальности R (7 м; короче — стена, корпус
    ///   босса, укус Гарпуна): F = clamp(⌈(R − 0,5) / 1⌉, 1, 10) тиков; тик полёта k
    ///   бьёт отрезок [0,5 + s(k−1), 0,5 + s·k] (первый — с 0) полосой 0,9 м.
    /// * НАТЯГ тиком после полёта: задетые лёгкие едут (ForcedMotionKind.Reeled) на
    ///   полукольцо перед героем (AnchorThrowRing), без наложений.
    /// * ВОЗВРАТ R = clamp(round((R − 0,5) / 0,8), 4, 8) тиков по кривой Водоворота.
    /// * ЛОВЛЯ: доехавшим в своей тяге — оглушение 15; удержание 3, выход 6 (2 без ходьбы).
    ///
    /// Формы (владелец 03.10) — Simulation.AnchorThrow.Forms: Невод, Веер, Гарпун.
    /// Все числа — именованные ЗАГЛУШКИ под приёмку и общий проход баланса.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- сроки и шаги ----

        /// <summary>Полёт: путь от руки делится на тики по 1 м (≈ 28 м/с при 7 м).</summary>
        public static readonly Fix64 AnchorThrowFlightStep = Fix64.One;

        /// <summary>Возврат: тиков = round((дальность − 0,5) / 0,8), в пределах 4…8.</summary>
        public static readonly Fix64 AnchorThrowReturnStep = Fix64.Ratio(8, 10);

        public const int AnchorThrowMinReturnTicks = 4, AnchorThrowMaxReturnTicks = 8;
        public const int AnchorThrowMinFlightTicks = 1, AnchorThrowMaxFlightTicks = 10;

        /// <summary>Удержание ловли, выход, первые тики выхода без ходьбы — числа Абордажа и Шквала.</summary>
        public const int AnchorThrowHoldTicks = AbordageHoldTicks;
        public const int AnchorThrowExitTicks = AbordageExitTicks;
        public const int AnchorThrowExitLockedTicks = AbordageExitLockedTicks;

        // ---- стены ----

        /// <summary>Проба стены: тело головы 0,3 м, шаг вдоль оси 0,25 м (LayoutMap.CellSize / 8).</summary>
        public static readonly Fix64 AnchorThrowWallProbe = Fix64.Ratio(3, 10);
        public static readonly Fix64 AnchorThrowWallStep = Fix64.Ratio(1, 4);

        /// <summary>Дальность меньше этого — «бросок в стену»: F = 1, R = 4, никого.</summary>
        public static readonly Fix64 AnchorThrowWallMinReach = Fix64.One;

        // ---- полукольцо мест (AnchorThrowRing) ----

        /// <summary>Зазор от тела героя до ближнего края тела на полукольце.</summary>
        public static readonly Fix64 AnchorThrowRingGap = Fix64.Ratio(3, 10);

        /// <summary>Зазор между соседями по дуге.</summary>
        public static readonly Fix64 AnchorThrowRingSpacing = Fix64.Ratio(15, 100);

        /// <summary>Потолок радиуса, пока дуга не шире 288° (иначе кольцо растёт дальше).</summary>
        public static readonly Fix64 AnchorThrowRingMax = Fix64.Ratio(26, 10);

        /// <summary>Дуга не шире 1,6π (288°): ρ ≥ S / (1,6π) — первое и последнее тело не сходятся.</summary>
        public static readonly Fix64 AnchorThrowRingArcLimit = Fix64.Pi * Fix64.Ratio(16, 10);

        // ---- состояние ----

        private AnchorThrowState _anchorThrow = new AnchorThrowState { HarpoonTarget = -1 };

        // Вне снимка, ёмкость Entities.Capacity (как буферы Абордажа): EntityStore индексы не переиспользует.
        private byte[] _throwHit;            // 0 — не задет; иначе полоса + 1 (сеть — 4), бит ThrowHitPull — «на тягу»
        private bool[] _throwReeled;         // в своей тяге (натяг…ловля)
        private Fix64[] _throwPullLength;    // путь тела по плану тяги
        private FixVec2[] _throwSlotAt;      // место на полукольце
        private int[] _ringIds;              // черновики полукольца
        private FixVec2[] _ringPositions, _ringSpots;
        private Fix64[] _ringRadii;

        private const byte ThrowHitPull = 0x10;

        /// <summary>Бросок якоря: фазы, полосы, головы, натяг и ловля.</summary>
        public AnchorThrowState AnchorThrow => _anchorThrow;

        /// <summary>Бросок идёт (от каста до конца выхода).</summary>
        public bool AnchorThrowActive => _anchorThrow.Phase != AnchorThrowPhase.None;

        /// <summary>Бросок держит героя: своим шагом он не идёт, сабля и ЛКМ молчат. Кроме хвоста выхода.</summary>
        public bool AnchorThrowHoldsHero => _anchorThrow.Phase != AnchorThrowPhase.None
            && !(_anchorThrow.Phase == AnchorThrowPhase.Exit && Tick >= _anchorThrow.ExitWalkTick);

        /// <summary>Тело сейчас в тяге этого Броска (от натяга до тика перед ловлей).</summary>
        public bool AnchorThrowReeled(int id)
            => _throwReeled != null && (uint)id < (uint)_throwReeled.Length && _throwReeled[id];

        /// <summary>Место тела на полукольце (имеет смысл, пока AnchorThrowReeled).</summary>
        public FixVec2 AnchorThrowLandingSpot(int id)
            => _throwSlotAt != null && (uint)id < (uint)_throwSlotAt.Length ? _throwSlotAt[id] : FixVec2.Zero;

        /// <summary>Голова главной полосы на последнем шагнувшем тике (между тиками — Tick − 1).</summary>
        public FixVec2 AnchorThrowAnchorAt => AnchorThrowHead(_anchorThrow, 0, Tick - 1);

        /// <summary>Угол призраков Веера: 30° (как «Три направления» Удара якорем).</summary>
        public static readonly Fix64 AnchorThrowFanAngle = Fix64.Pi / 6;

        /// <summary>
        /// Голова полосы lane на тике tick — одна формула для Sim, вида и тестов (вид
        /// держит её во float). До выпуска — рука; полёт — Center + dir · (0,5 + s·k),
        /// в последнем тике полёта — ровно дальность; до натяга ждёт в конце; возврат —
        /// от конца к руке полосы по MaelstromPullProgress(тик − натяг, R); после ловли — рука.
        /// </summary>
        public static FixVec2 AnchorThrowHead(in AnchorThrowState s, int lane, int tick)
        {
            if (lane < 0 || lane >= s.Lanes) lane = 0;
            FixVec2 dir = s.LaneDir(lane);
            FixVec2 hand = s.Center + dir * AbordageHandReach;
            if (s.Serial == 0 || tick <= s.ReleaseTick) return hand;
            int flight = s.LaneFlight(lane);
            Fix64 reach = s.LaneReach(lane);
            FixVec2 end = s.Center + dir * reach;
            int k = tick - s.ReleaseTick;
            if (k < flight)
            {
                Fix64 at = AbordageHandReach + s.LaneStep(lane) * k;
                return s.Center + dir * (at < reach ? at : reach);
            }
            if (tick <= s.TautTick) return end;
            if (tick >= s.CatchTick) return hand;
            return end + (hand - end) * MaelstromPullProgress(tick - s.TautTick, s.ReturnTicks);
        }

        /// <summary>Тики полёта до дальности reach: clamp(⌈(reach − 0,5) / 1⌉, 1, 10).</summary>
        public static int AnchorThrowFlightTicks(Fix64 reach)
        {
            Fix64 path = (reach - AbordageHandReach) / AnchorThrowFlightStep;
            int ticks = path.Raw <= 0 ? 0 : -((-path).ToInt());
            return ticks < AnchorThrowMinFlightTicks ? AnchorThrowMinFlightTicks
                : ticks > AnchorThrowMaxFlightTicks ? AnchorThrowMaxFlightTicks : ticks;
        }

        /// <summary>Тики возврата с дальности reach: clamp(round((reach − 0,5) / 0,8), 4, 8).</summary>
        public static int AnchorThrowReturnTicks(Fix64 reach)
        {
            Fix64 path = (reach - AbordageHandReach) / AnchorThrowReturnStep;
            int ticks = path.Raw <= 0 ? 0 : (path + Fix64.Half).ToInt();
            return ticks < AnchorThrowMinReturnTicks ? AnchorThrowMinReturnTicks
                : ticks > AnchorThrowMaxReturnTicks ? AnchorThrowMaxReturnTicks : ticks;
        }

        private AbilityBuild AnchorThrowBuild
        {
            get
            {
                AbilityBuild build = (uint)_anchorThrow.Slot < (uint)AbilitySlots ? _abilityBuilds[_anchorThrow.Slot] : null;
                return build != null && build.DefinitionId == AbilityDefinition.AnchorThrowId ? build : null;
            }
        }

        private void AnchorThrowFace()
        {
            if (_anchorThrow.Dir.LengthSq.Raw != 0) Entities.Facing[PlayerId] = _anchorThrow.Dir;
        }
    }
}
