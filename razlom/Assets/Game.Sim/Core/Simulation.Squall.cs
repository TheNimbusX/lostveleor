namespace Game.Sim
{
    /// <summary>
    /// Фаза Шквала (Simulation.Squall). Значения навсегда, только дописывать:
    /// идут в хеш и в снимок для вида.
    /// </summary>
    public enum SquallPhase : byte
    {
        None = 0,

        /// <summary>Замах при касте: герой стоит SquallWindupTicks, взгляд на первую цель.</summary>
        Windup = 1,

        /// <summary>Полёт к цели (ForcedMotion Lunge), тики по длине прыжка.</summary>
        Flight = 2,

        /// <summary>Опора: приземлился и рубанул, SquallStopTicks до следующего старта (или до возврата).</summary>
        Stop = 3,

        /// <summary>Последний удар держится SquallFinalHoldTicks, потом выход.</summary>
        Hold = 4,

        /// <summary>Прыжок назад к точке каста: талант «Возврат» или форма Неуловимый. Лицом по пути.</summary>
        Return = 5,

        /// <summary>Выход в стойку сабли; с ExitWalkTick ходьба срывает выход.</summary>
        Exit = 6,
    }

    /// <summary>Как кончился Шквал — Amount события SquallEnded. Только дописывать.</summary>
    public enum SquallEnd : byte
    {
        /// <summary>Выход доигран.</summary>
        Done = 0,

        /// <summary>Хвост выхода сорван ходьбой.</summary>
        WalkedOut = 1,

        /// <summary>Сняли: уход, другая способность, удар сабли, оглушение, смерть, чужой отброс.</summary>
        Interrupted = 2,

        /// <summary>Цель умерла в замахе, а другой в радиусе нет — ни одного прыжка.</summary>
        NoTarget = 3,
    }

    /// <summary>
    /// Снимок Шквала. Пишет только симуляция; вид читает отсюда, откуда и куда
    /// летит прыжок, когда удар, сторону удара и путь возврата. Входит в хеш,
    /// пока за расстановку был хоть один Шквал.
    /// </summary>
    public struct SquallState
    {
        /// <summary>Номер каста с начала расстановки; 0 — Шквала ещё не было.</summary>
        public int Serial;

        public int Slot;
        public SquallPhase Phase;
        public int CastTick;

        /// <summary>Номер текущего (последнего начатого) прыжка с нуля.</summary>
        public int Index;

        /// <summary>Цель текущего прыжка.</summary>
        public int Target;

        /// <summary>Следующая цель, выбранная в тик удара (к ней поворот в опоре); −1 — нет.</summary>
        public int NextTarget;

        /// <summary>Откуда и куда текущий полёт (у возврата To — точка каста).</summary>
        public FixVec2 From, To;

        /// <summary>Тик старта полёта: тело стоит в From, первый шаг — в следующий тик.</summary>
        public int FlightStartTick;

        /// <summary>Тик прибытия; у прыжка к цели это и тик удара.</summary>
        public int ArriveTick;

        /// <summary>Конец текущей стоячей фазы (замах, опора, удержание, выход).</summary>
        public int PhaseEndTick;

        /// <summary>Сторона удара текущего прыжка: false — прямой (справа налево), true — обратный. Строго чередуется.</summary>
        public bool Backhand;

        /// <summary>Облёт одинокой цели: −1/+1 — в какую сторону вокруг неё (против часовой — +1), 0 — облёта не было.</summary>
        public int OrbitSign;

        /// <summary>Где герой стоял при касте — точка возврата.</summary>
        public FixVec2 Origin;

        /// <summary>Путь возврата: 0 — прямой, 2 — дугой через Via0, Via1. Leg — текущий отрезок (0..ViaCount).</summary>
        public int ViaCount, Leg;
        public FixVec2 Via0, Via1;

        /// <summary>Охота: сколько лишних прыжков уже дали убийства.</summary>
        public int BonusHops;

        /// <summary>С этого тика ходьба срывает выход.</summary>
        public int ExitWalkTick;

        public bool Moving => Phase == SquallPhase.Flight || Phase == SquallPhase.Return;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Slot); Hashing.Mix(ref hash, (int)Phase);
            Hashing.Mix(ref hash, CastTick); Hashing.Mix(ref hash, Index); Hashing.Mix(ref hash, Target);
            Hashing.Mix(ref hash, NextTarget);
            Hashing.Mix(ref hash, From.X); Hashing.Mix(ref hash, From.Y);
            Hashing.Mix(ref hash, To.X); Hashing.Mix(ref hash, To.Y);
            Hashing.Mix(ref hash, FlightStartTick); Hashing.Mix(ref hash, ArriveTick); Hashing.Mix(ref hash, PhaseEndTick);
            Hashing.Mix(ref hash, Backhand ? 1 : 0); Hashing.Mix(ref hash, OrbitSign);
            Hashing.Mix(ref hash, Origin.X); Hashing.Mix(ref hash, Origin.Y);
            Hashing.Mix(ref hash, ViaCount); Hashing.Mix(ref hash, Leg);
            Hashing.Mix(ref hash, Via0.X); Hashing.Mix(ref hash, Via0.Y);
            Hashing.Mix(ref hash, Via1.X); Hashing.Mix(ref hash, Via1.Y);
            Hashing.Mix(ref hash, BonusHops); Hashing.Mix(ref hash, ExitWalkTick);
        }
    }

    /// <summary>
    /// ШКВАЛ (Шаг по цепи) — переделка 02.10 по разбору «ломается тело»
    /// (scratchpad squall/diagnosis.md, план 3.1). Резко и быстро, но с опорой:
    ///
    /// * ЗАМАХ: каст — SquallWindupTicks стоя, взгляд на первую цель.
    /// * ПОЛЁТ ПО ДЛИНЕ: тики = round(путь / 0,66 м), 2…6 (≈20 м/с, как рывок).
    ///   Прямо по земле, стены как у прежнего прыжка (ForcedMotion Lunge).
    /// * ОПОРА: урон в тик прибытия, следующий старт — через SquallStopTicks.
    ///   В тик удара выбирается следующая цель и взгляд встаёт на неё.
    /// * ПОСАДКА вплотную, а не внутрь: радиус цели + радиус героя + 0,1 м —
    ///   расталкивание после посадки героя не сдвигает.
    /// * ЦЕЛИ: ближайшая непосещённая в конусе поворота ≤110° с бонусом за
    ///   смену стороны поворота (зигзаг под чередование прямого и обратного
    ///   удара); шире конуса — только если в конусе никого. Одинокую цель не
    ///   пролетает насквозь: облёт по окружности посадки шагом 60°.
    /// * ВЗГЛЯД ведёт Sim: старт — по прыжку, удар — на следующую цель, конец
    ///   серии — по последнему прыжку (вид не дёргается к старому взгляду).
    /// * КОНЕЦ: последний удар держится SquallFinalHoldTicks, выход
    ///   SquallExitTicks; первые SquallExitLockedTicks выхода ходьба не идёт,
    ///   потом срывает выход. Способности — после первого удара, рывок — всегда.
    ///
    /// ФОРМЫ (владелец 02.10) — только когда в слоте форма (FormIs):
    /// * ОХОТА — прыжки к самому раненому; убийство ударом даёт лишний прыжок
    ///   (не больше HuntBonusHopsMax за каст).
    /// * ПЕННЫЙ СЛЕД — каждый прыжок оставляет полосу пены: бьёт и замедляет.
    /// * НЕУЛОВИМЫЙ — неуязвим в прыжках (PlayerImmune, как талант
    ///   «Неуязвимость»), последний прыжок — дугой к точке каста. Возврат один на
    ///   форму и талант «Возврат»: не два возврата, а одна механика.
    ///
    /// Урон, радиус, число прыжков и таланты — прежние. Числа форм — заглушки
    /// под приёмку владельцем, все именованные.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- сроки ----

        /// <summary>Замах при касте: столько тиков герой стоит до первого шага полёта.</summary>
        public const int SquallWindupTicks = 2;

        /// <summary>Опора на цели: с тика удара до старта следующего прыжка.</summary>
        public const int SquallStopTicks = 2;

        /// <summary>Последний удар держится столько тиков (с тика удара).</summary>
        public const int SquallFinalHoldTicks = 3;

        /// <summary>Выход в стойку после последнего удара или возврата.</summary>
        public const int SquallExitTicks = 6;

        /// <summary>Первые тики выхода ходьба не идёт; дальше срывает выход.</summary>
        public const int SquallExitLockedTicks = 2;

        public const int SquallMinFlightTicks = 2, SquallMaxFlightTicks = 6;

        /// <summary>Путь полёта за тик: 0,66 м — ≈20 м/с, как у рывка (4 м за 6 тиков).</summary>
        public static readonly Fix64 SquallFlightStep = Fix64.Ratio(66, 100);

        /// <summary>Типичный полёт (1,7 м) — только для оценки конца серии в часах действия.</summary>
        private const int SquallTypicalFlightTicks = 3;

        // ---- посадка и цели ----

        /// <summary>Зазор между телами при посадке.</summary>
        public static readonly Fix64 SquallLandingGap = Fix64.Ratio(1, 10);

        /// <summary>
        /// Запас досягаемости удара сверх посадки: цель за время полёта могла
        /// отойти. 0,9 — прежний отступ Шквала.
        /// </summary>
        public static readonly Fix64 SquallContactSlack = Fix64.Ratio(9, 10);

        /// <summary>Конус поворота к следующей цели: cos 110°.</summary>
        public static readonly Fix64 SquallTurnConeCos = Fix64.Ratio(-342, 1000);

        /// <summary>Бонус к близости цели на «своей» стороне поворота (зигзаг).</summary>
        public static readonly Fix64 SquallZigzagBonus = Fix64.One;

        /// <summary>Облёт одинокой цели: шаг 60° по окружности посадки.</summary>
        private static readonly Fix64 SquallOrbitCos = Fix64.Ratio(1, 2);
        private static readonly Fix64 SquallOrbitSin = Fix64.Ratio(8660254, 10000000);

        // ---- возврат ----

        /// <summary>Ближе этого к точке каста возврата нет — герой и так там.</summary>
        public static readonly Fix64 SquallReturnMinDistance = Fix64.Ratio(1, 2);

        /// <summary>Изгиб дуги возврата вбок: 30% пути, 1–1,5 м.</summary>
        public static readonly Fix64 SquallReturnBendMin = Fix64.One, SquallReturnBendMax = Fix64.Ratio(3, 2);
        private static readonly Fix64 SquallReturnBendShare = Fix64.Ratio(3, 10);

        // ---- Охота ----

        /// <summary>Охота: лишних прыжков за каст не больше этого. ЗАГЛУШКА.</summary>
        public const int HuntBonusHopsMax = 4;

        private SquallState _squall;

        /// <summary>Шквал: фаза, прыжок, сторона удара, путь возврата.</summary>
        public SquallState Squall => _squall;

        /// <summary>Шквал идёт (от каста до конца выхода).</summary>
        public bool SquallActive => _squall.Phase != SquallPhase.None;

        /// <summary>Шквал держит героя: своим шагом он не идёт, сабля не бьёт. Кроме хвоста выхода.</summary>
        public bool SquallHoldsHero => _squall.Phase != SquallPhase.None
            && !(_squall.Phase == SquallPhase.Exit && Tick >= _squall.ExitWalkTick);

        /// <summary>Окно прыжков: замах, полёты, опоры между ними, возврат — без удержания и выхода.</summary>
        private bool SquallJumpWindow => _squall.Phase == SquallPhase.Windup || _squall.Phase == SquallPhase.Flight
            || _squall.Phase == SquallPhase.Stop || _squall.Phase == SquallPhase.Return;

        /// <summary>Неуязвим в прыжках: талант «Неуязвимость» или форма Неуловимый.</summary>
        private bool SquallInvulnerableNow => SquallJumpWindow
            && (BuildHas(_squall.Slot, AbilityFlag.SquallInvulnerable, AbilityDefinition.ChainStepId)
                || FormIs(_squall.Slot, PelagForm.SquallElusive));

        /// <summary>Прыжок длиной distance: round(distance / 0,66 м), 2…6 тиков.</summary>
        public static int SquallFlightTicks(Fix64 distance)
        {
            int ticks = (Fix64.Max(Fix64.Zero, distance) / SquallFlightStep + Fix64.Half).ToInt();
            return ticks < SquallMinFlightTicks ? SquallMinFlightTicks : ticks > SquallMaxFlightTicks ? SquallMaxFlightTicks : ticks;
        }

        /// <summary>Отрезок возврата: та же скорость, без потолка, не меньше 2 тиков.</summary>
        private static int SquallLegTicks(FixVec2 a, FixVec2 b)
        {
            int ticks = (FixVec2.Distance(a, b) / SquallFlightStep + Fix64.Half).ToInt();
            return ticks < ForcedMotion.MinTicks ? ForcedMotion.MinTicks : ticks;
        }

        /// <summary>Отступ посадки от центра цели: радиусы тел и зазор.</summary>
        public Fix64 SquallLandingDistance(int target)
            => Entities.BodyRadius[target] + Entities.BodyRadius[PlayerId] + SquallLandingGap;

        private AbilityBuild SquallBuild
        {
            get
            {
                AbilityBuild build = (uint)_squall.Slot < (uint)AbilitySlots ? _abilityBuilds[_squall.Slot] : null;
                return build != null && build.DefinitionId == AbilityDefinition.ChainStepId ? build : null;
            }
        }

        private bool SquallTargetValid(int id)
            => id > PlayerId && id < Entities.Count && Entities.Alive[id]
               && Entities.Side[id] != Entities.Side[PlayerId] && !ThicketShielded(id);

        private void SquallFace(FixVec2 direction)
        {
            if (direction.LengthSq.Raw != 0) Entities.Facing[PlayerId] = direction.Normalized();
        }

        private bool SquallCanTravel(FixVec2 from, FixVec2 to)
            => (_layout == null && _campWalkMap == null) || CanTravel(from, to, Entities.BodyRadius[PlayerId]);
    }
}
