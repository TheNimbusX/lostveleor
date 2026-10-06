using System;

namespace Game.Sim
{
    /// <summary>
    /// Хозяин Чащи — твёрдое тело (владелец 02.10: «сквозь босса не должны иметь
    /// возможность проходить, только дешем»).
    ///
    /// КОРПУС — семь кругов вдоль тела по взгляду: грудь с головой, две передние
    /// лапы, талия, два бедра, корень хвоста. Сняты с сетки модели 3,6 м
    /// (ThicketMaster_29k, вид сверху ниже 2,3 м) и растут ×1,15 вместе с ней.
    /// Тело сущности (0,95, потолок EntityStore.MaxBodyRadius) остаётся для
    /// сетки расталкивания; корпус — свой слой поверх.
    ///
    /// ПРОХОД. После хода и расталкивания (Step: SeparateBodies →
    /// PushOutOfThicketHulls) каждое тело, вошедшее в корпус, ставится на
    /// ближайшую точку снаружи — не мягким толчком, который можно перешагнуть.
    /// За тик — не дальше ThicketHullPushPerTick (ход поверхности корпуса при
    /// повороте и шаге) плюс путь, которым тело само вошло за этот тик: шаг,
    /// выпад, волок — снимаются целиком, а поворот на месте и выход из нырка
    /// на герое выдавливают плавно. Стены — как у прочих толчков: точка, до
    /// которой не дойти, пропускается, берётся следующая. Вошедший снаружи
    /// глубоко или насквозь (тяга якоря, Шквал: последний тик берёт весь
    /// остаток пути) встаёт там, где вошёл, — не у ближайшей точки из глубины.
    /// Не держит: рывок героя (всё окно неуязвимости, Simulation.Dash), фазу
    /// Лика Пустоты, нырок (ThicketShielded — от ухода до выхода), мёртвого.
    /// Рывок, кончившийся внутри, доводится насквозь по своему направлению,
    /// если выход ближе ThicketDashExitReach (иначе — ближайшая точка), не
    /// медленнее ThicketDashExitPerTick.
    ///
    /// ПОПАДАНИЯ. Удары героя меряют тело врага по центру и BodyRadius (сабля,
    /// способности, якорь — файлы Пелага). На окно ударов героя (Step: от
    /// ResolveArtifactUse до ResolveAttacks) тело босса для попаданий — от
    /// центра до точки корпуса, ближайшей к герою (BeginThicketHitBodies), потом
    /// прежнее. Вихрь и вспышка Пламени ищут по центрам (QueryRadiusIntoScratch),
    /// дальность цели способности — тоже (ValidAbilityTarget): там босс
    /// меряется по корпусу (ThicketHullWithin).
    /// </summary>
    public sealed partial class Simulation
    {
        // ---------- корпус ----------

        /// <summary>Кругов корпуса: 0 грудь и голова, 1 левая лапа, 2 правая, 3 талия, 4 левое бедро, 5 правое, 6 корень хвоста.</summary>
        public const int ThicketHullCircleCount = 7;

        // Сантиметры модели 3,6 м: вперёд по взгляду, вбок (+ — влево: взгляд,
        // повёрнутый на 90° против часовой), радиус. ×ThicketSizePercent / 100.
        private static readonly int[] ThicketHullForwardCm = { 165, 172, 172, 79, -45, -45, -131 };
        private static readonly int[] ThicketHullSideCm = { 0, 120, -120, 0, 60, -60, 0 };
        private static readonly int[] ThicketHullRadiusCm = { 73, 77, 77, 81, 87, 87, 46 };

        private static readonly Fix64[] ThicketHullForward = ThicketHullScaled(ThicketHullForwardCm);
        private static readonly Fix64[] ThicketHullSide = ThicketHullScaled(ThicketHullSideCm);
        private static readonly Fix64[] ThicketHullRadius = ThicketHullScaled(ThicketHullRadiusCm);

        /// <summary>Дальняя точка корпуса от центра босса (3,30 м — край лапы): отсев.</summary>
        public static readonly Fix64 ThicketHullReach = ThicketHullFarthest();

        /// <summary>
        /// Потолок выдавливания за тик сверх своего хода тела — 0,4 м: край лапы
        /// (3,3 м от центра) на повороте 2,5° за тик идёт 0,14 м, шаг босса 0,07.
        /// </summary>
        public static readonly Fix64 ThicketHullPushPerTick = Fix64.Ratio(2, 5);

        /// <summary>Рывок, кончившийся внутри корпуса, доводится насквозь, если выход по его направлению не дальше 2,5 м.</summary>
        public static readonly Fix64 ThicketDashExitReach = Fix64.Ratio(5, 2);

        /// <summary>Сколько тиков после окна рывка выход ищется по его направлению.</summary>
        public const int ThicketDashExitTicks = 12;

        /// <summary>
        /// Доводка рывка насквозь — не медленнее 1,25 м за тик: выход не дальше
        /// ThicketDashExitReach, значит, тело отпускает не дольше чем за два тика
        /// (окно неуязвимости рывка к этому времени уже кончилось).
        /// </summary>
        public static readonly Fix64 ThicketDashExitPerTick = Fix64.Ratio(5, 4);

        /// <summary>
        /// Стоит, а не идёт на героя, если между телом героя и корпусом не больше
        /// 0,25 м: шаг босса 0,087 и столько же он докатывается, гася разгон
        /// (AccelerationTicks) — к герою корпус не доходит, шаг его не толкает.
        /// </summary>
        public static readonly Fix64 ThicketHoldGap = Fix64.Ratio(1, 4);

        /// <summary>Лапа начинается и по корпусу: герой в ±40°, не дальше ThicketPawRadius и вплотную (зазор ≤ 0,3 м).</summary>
        public static readonly Fix64 ThicketPawHullSlack = Fix64.Ratio(3, 10);

        private static readonly Fix64 ThicketHullEpsilon = Fix64.Ratio(1, 10000);
        private const int ThicketHullExitSlots = ThicketHullCircleCount + ThicketHullCircleCount * (ThicketHullCircleCount - 1);
        private const int ThicketHullExitTries = 6;

        private static Fix64[] ThicketHullScaled(int[] cm)
        {
            var result = new Fix64[cm.Length];
            for (int k = 0; k < cm.Length; k++) result[k] = Fix64.Ratio(cm[k] * ThicketSizePercent, 10000);
            return result;
        }

        private static Fix64 ThicketHullFarthest()
        {
            Fix64 far = Fix64.Zero;
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                Fix64 at = new FixVec2(ThicketHullForward[k], ThicketHullSide[k]).Length + ThicketHullRadius[k];
                if (at > far) far = at;
            }
            return far;
        }

        // Временное (в пределах тика) — в хеш не идёт: положения до хода, мировые круги, кандидаты выхода.
        private FixVec2[] _thicketHullStart;
        private int _thicketHullStartTick = -1, _thicketHullStartCount;
        private FixVec2[] _thicketHullWorld;
        private FixVec2[] _thicketHullExit;
        private Fix64[] _thicketHullExitDistSq;
        private int[] _thicketHitIds;
        private Fix64[] _thicketHitSaved, _thicketHitValue;
        private int _thicketHitCount;
        // Передние лапы этого босса в замахе (ThicketPawsLifted) — на время его выдавливания.
        private bool _thicketHullPawsLifted;
        // Герой сам летел (тяга якоря, Шквал), и корпус его остановил в этом тике (BeginThicketHitBodies).
        private bool _thicketHeroForced;
        private int _thicketHeroRamTick = -1, _thicketHeroRamBoss = -1;

        /// <summary>Круг корпуса index в осях босса (метры): вперёд, вбок (+ — влево), радиус.</summary>
        public static void ThicketHullLocal(int index, out Fix64 forward, out Fix64 side, out Fix64 radius)
        {
            forward = ThicketHullForward[index];
            side = ThicketHullSide[index];
            radius = ThicketHullRadius[index];
        }

        /// <summary>Корпус держит проход и попадания: живой Хозяин Чащи не в нырке.</summary>
        public bool ThicketHullActive(int id)
            => (uint)id < (uint)Entities.Count && Entities.Kind[id] == EnemyKind.ForestThicketMaster
               && Entities.Alive[id] && !ThicketShielded(id);

        /// <summary>
        /// Круг корпуса index в мире (для отладочной отрисовки и тестов): центр и
        /// радиус. false — корпуса нет (не босс, мёртв, в нырке).
        /// </summary>
        public bool TryGetThicketHullCircle(int id, int index, out FixVec2 center, out Fix64 radius)
        {
            center = FixVec2.Zero;
            radius = Fix64.Zero;
            if ((uint)index >= ThicketHullCircleCount || !ThicketHullActive(id)) return false;
            FixVec2 facing = ThicketHullFacing(id);
            center = ThicketHullCenter(Entities.Position[id], facing, index);
            radius = ThicketHullRadius[index];
            return true;
        }

        /// <summary>Где был бы центр круга index при теле на месте — и в нырке, когда корпус не держит (отладка, тесты).</summary>
        public FixVec2 ThicketHullCircleCenter(int id, int index)
            => (uint)id < (uint)Entities.Count && (uint)index < ThicketHullCircleCount
                ? ThicketHullCenter(Entities.Position[id], ThicketHullFacing(id), index)
                : FixVec2.Zero;

        /// <summary>
        /// Зазор от точки до корпуса: наименьшее (расстояние до центра круга −
        /// радиус); меньше нуля — точка внутри. Без корпуса (не босс, нырок,
        /// смерть) — до круга тела BodyRadius.
        /// </summary>
        public Fix64 ThicketHullGap(int id, FixVec2 point)
        {
            if (!ThicketHullActive(id))
                return (uint)id < (uint)Entities.Count
                    ? FixVec2.Distance(point, Entities.Position[id]) - Entities.BodyRadius[id]
                    : Fix64.FromInt(1000);
            FixVec2 boss = Entities.Position[id], facing = ThicketHullFacing(id);
            Fix64 gap = Fix64.FromInt(1000);
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                Fix64 at = FixVec2.Distance(point, ThicketHullCenter(boss, facing, k)) - ThicketHullRadius[k];
                if (at < gap) gap = at;
            }
            return gap;
        }

        /// <summary>Герой вплотную к корпусу: между его телом и корпусом не больше slack.</summary>
        private bool ThicketHeroAgainstHull(int id, Fix64 slack)
            => Entities.Alive[PlayerId] && ThicketHullActive(id)
               && ThicketHullGap(id, Entities.Position[PlayerId]) - Entities.BodyRadius[PlayerId] <= slack;

        /// <summary>
        /// Передние лапы в замахе подняты (баланс 02.10, ночь): от знака удара серии до его контакта
        /// круги обеих передних лап корпуса (1 и 2) никого не держат и не выдавливают — грудь, талия,
        /// бёдра и хвост держат, как всегда. Иначе корпус, доворачивающий к удару (до 59,5° у первого,
        /// 3,5°/тик), лапой возил стоящего у бока героя вперёд себя — из нарисованного сектора
        /// («подмышка» между лапой и бедром: 75% времени у эксперта, удар туда не попадал никогда). Обе,
        /// а не только бьющая: к герою слева доворачивает и несёт его левая, даже когда бьёт правая.
        /// Вид — тот же замах; герой под поднятой лапой стоит, пока она не ударит, потом корпус
        /// выдавливает его, как обычно (не быстрее 0,4 м за тик). Попадания героя, зазор, полосы и
        /// отладочные круги — по всему корпусу.
        /// </summary>
        public bool ThicketPawsLifted(int id)
        {
            var masters = _thicketMasters;
            if (masters == null || (uint)id >= (uint)masters.Length) return false;
            ref var a = ref masters[id];
            return a.Serial != 0 && a.Action == ThicketMasterAction.Paw && !a.HitResolved;
        }

        /// <summary>Круг корпуса k не держит в этом выдавливании (передние лапы в замахе).</summary>
        private bool ThicketHullSkips(int k) => _thicketHullPawsLifted && (k == 1 || k == 2);

        private FixVec2 ThicketHullFacing(int id)
        {
            FixVec2 facing = Entities.Facing[id].Normalized();
            return facing.LengthSq.Raw != 0 ? facing : new FixVec2(Fix64.One, Fix64.Zero);
        }

        private static FixVec2 ThicketHullCenter(FixVec2 boss, FixVec2 facing, int index)
        {
            var left = new FixVec2(-facing.Y, facing.X);
            return boss + facing * ThicketHullForward[index] + left * ThicketHullSide[index];
        }

        private bool AnyThicketHull()
        {
            if (_thicketMemory == null) return false;
            for (int id = 1; id < Entities.Count; id++)
                if (ThicketHullActive(id)) return true;
            return false;
        }

        // ---------- проход ----------

        /// <summary>Положения до хода этого тика: путь, которым тело само вошло в корпус, снимается целиком.</summary>
        private void MarkThicketHullStart()
        {
            _thicketHullStartTick = -1;
            _thicketHeroForced = false;
            if (!AnyThicketHull()) return;
            _thicketHeroForced = Entities.ForcedTicksLeft[PlayerId] > 0
                && (Entities.ForcedKind[PlayerId] == (byte)ForcedMotionKind.Lunge
                    || Entities.ForcedKind[PlayerId] == (byte)ForcedMotionKind.Skewer);
            if (_thicketHullStart == null || _thicketHullStart.Length < Entities.Capacity)
                _thicketHullStart = new FixVec2[Entities.Capacity];
            Array.Copy(Entities.Position, _thicketHullStart, Entities.Count);
            _thicketHullStartCount = Entities.Count;
            _thicketHullStartTick = Tick;
        }

        /// <summary>
        /// Корпус выдавливает всех, кто в него вошёл: героя (кроме рывка и фазы
        /// Лика) и прочих (кроме тех, чьим положением владеет их приём, и
        /// неподвижных — как в SeparateBodies). Зовётся после расталкивания и до
        /// пересборки сетки: бой видит тела уже снаружи.
        /// </summary>
        private void PushOutOfThicketHulls()
        {
            if (_thicketMemory == null) return;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (!ThicketHullActive(id)) continue;
                FixVec2 boss = Entities.Position[id], facing = ThicketHullFacing(id);
                _thicketHullWorld ??= new FixVec2[ThicketHullCircleCount];
                for (int k = 0; k < ThicketHullCircleCount; k++) _thicketHullWorld[k] = ThicketHullCenter(boss, facing, k);
                // Передние лапы в замахе подняты: доворот к удару героя у бока не возит (ThicketPawsLifted).
                _thicketHullPawsLifted = ThicketPawsLifted(id);
                for (int i = 0; i < Entities.Count; i++)
                {
                    if (i == id || !Entities.Alive[i] || Entities.Kind[i] == EnemyKind.ForestThicketMaster) continue;
                    if (i == PlayerId)
                    {
                        if (VoidPhased || DashInvulnerable) continue;
                    }
                    else if (Entities.PushWeight[i].Raw <= 0 || IsWendigoAirborne(i) || StonehoofOwnsPosition(i)
                             || SplitterOwnsPosition(i)) continue;
                    FixVec2 before = Entities.Position[i];
                    PushOutOfThicketHull(boss, i);
                    if (i == PlayerId && _thicketHeroForced && !before.Equals(Entities.Position[i]))
                    {
                        _thicketHeroRamTick = Tick;
                        _thicketHeroRamBoss = id;
                    }
                }
                _thicketHullPawsLifted = false;
            }
        }

        private void PushOutOfThicketHull(FixVec2 boss, int i)
        {
            FixVec2 p = Entities.Position[i];
            Fix64 body = Entities.BodyRadius[i];
            Fix64 bound = ThicketHullReach + body;
            bool marked = _thicketHullStartTick == Tick && i < _thicketHullStartCount;
            FixVec2 start = marked ? _thicketHullStart[i] : p;
            // Отсев — по всему пути тика: быстрый выпад может пролететь корпус насквозь.
            if (ThicketSegmentDistanceSq(boss, start, p) >= bound * bound) return;
            bool inside = InsideThicketHull(p, body);
            bool startInside = marked && InsideThicketHull(start, body);

            Fix64 cap = ThicketHullPushPerTick;
            if (marked) cap += FixVec2.Distance(p, start);

            // Вошёл снаружи за этот тик сам (шаг, выпад, тяга якоря, волок). Мелко — скользит
            // к ближайшей точке снаружи, как шаг вдоль стены. Глубже ThicketHullPushPerTick или
            // насквозь (последний тик тяги берёт весь остаток пути) — встаёт там, где вошёл:
            // иначе ближайшая точка от места в глубине тела — уже другой бок, телепорт.
            // «Глубоко» — середина пути внутри глубже потолка, или (кончил внутри) ближайшая точка
            // снаружи от конца далеко от входа: прошёл тело и чуть не вышел с той стороны.
            if (marked && !startInside && !start.Equals(p)
                && ThicketHullSweep(start, p, body, out FixVec2 entry, out FixVec2 middle))
            {
                bool deep = ThicketHullNearestExit(boss, middle, body, out _) > ThicketHullPushPerTick;
                if (!deep && inside)
                {
                    ThicketHullNearestExit(boss, p, body, out FixVec2 near);
                    deep = FixVec2.Distance(near, entry) > ThicketHullPushPerTick;
                }
                if (deep)
                {
                    if (!TryThicketHullStep(i, p, entry, cap)) Entities.Position[i] = start;
                    return;
                }
                if (!inside) return;   // задел край и вышел за тот же тик — не держит
            }
            if (!inside) return;

            // Насквозь — только тот, кто уже был внутри до хода этого тика (рывок кончился в теле),
            // а не тот, кто вошёл шагом вскоре после рывка мимо. Со скоростью не ниже
            // ThicketDashExitPerTick: окно неуязвимости уже кончилось, ползти сквозь тело нельзя.
            if (i == PlayerId && startInside && ThicketDashExit(p, body, out FixVec2 through)
                && TryThicketHullStep(i, p, through, Fix64.Max(cap, ThicketDashExitPerTick))) return;

            int n = CollectThicketHullExits(boss, p, body);
            if (n == 0) return;
            int nearest = -1;
            for (int attempt = 0; attempt < ThicketHullExitTries; attempt++)
            {
                int best = -1;
                for (int k = 0; k < n; k++)
                    if (_thicketHullExitDistSq[k].Raw >= 0
                        && (best < 0 || _thicketHullExitDistSq[k] < _thicketHullExitDistSq[best])) best = k;
                if (best < 0) break;
                if (nearest < 0) nearest = best;
                if (TryThicketHullStep(i, p, _thicketHullExit[best], cap)) return;
                _thicketHullExitDistSq[best] = -Fix64.One;
            }
            // Все ближние выходы за стеной — вдоль стены к ближайшему, как обычный толчок.
            Entities.Position[i] = MoveInsideLayout(i, p, (_thicketHullExit[nearest] - p).ClampLength(cap));
        }

        private bool InsideThicketHull(FixVec2 p, Fix64 body)
        {
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                if (ThicketHullSkips(k)) continue;
                Fix64 r = ThicketHullRadius[k] + body;
                if (FixVec2.DistanceSq(p, _thicketHullWorld[k]) < r * r) return true;
            }
            return false;
        }

        /// <summary>Шаг к точке выхода не длиннее cap; false — путь упирается в стену.</summary>
        private bool TryThicketHullStep(int i, FixVec2 p, FixVec2 goal, Fix64 cap)
        {
            FixVec2 delta = (goal - p).ClampLength(cap);
            if (delta.LengthSq.Raw == 0) return true;
            FixVec2 to = p + delta;
            if ((_layout != null || _campWalkMap != null) && !CanTravel(p, to, Entities.BodyRadius[i])) return false;
            Entities.Position[i] = to;
            return true;
        }

        /// <summary>
        /// Точки снаружи корпуса (раздутого на тело), ближайшие к p: проекция на
        /// каждый круг и точки пересечения пар кругов, не лежащие ни в одном
        /// круге. Ближайшая из них — ближайшая точка снаружи объединения.
        /// </summary>
        private int CollectThicketHullExits(FixVec2 boss, FixVec2 p, Fix64 body)
        {
            _thicketHullExit ??= new FixVec2[ThicketHullExitSlots];
            _thicketHullExitDistSq ??= new Fix64[ThicketHullExitSlots];
            var world = _thicketHullWorld;
            int n = 0;
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                if (ThicketHullSkips(k)) continue;
                Fix64 r = ThicketHullRadius[k] + body + ThicketHullEpsilon;
                FixVec2 offset = p - world[k];
                FixVec2 direction = offset.Normalized();
                if (direction.LengthSq.Raw == 0) direction = (p - boss).Normalized();
                if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
                AddThicketHullExit(world[k] + direction * r, p, body, ref n);
            }
            for (int a = 0; a < ThicketHullCircleCount; a++)
            {
                if (ThicketHullSkips(a)) continue;
                Fix64 ra = ThicketHullRadius[a] + body + ThicketHullEpsilon;
                for (int b = a + 1; b < ThicketHullCircleCount; b++)
                {
                    if (ThicketHullSkips(b)) continue;
                    Fix64 rb = ThicketHullRadius[b] + body + ThicketHullEpsilon;
                    FixVec2 d = world[b] - world[a];
                    Fix64 distance = d.Length;
                    if (distance.Raw == 0 || distance >= ra + rb || distance <= Fix64.Abs(ra - rb)) continue;
                    Fix64 along = (ra * ra - rb * rb + distance * distance) / (distance * 2);
                    Fix64 hSq = ra * ra - along * along;
                    if (hSq.Raw <= 0) continue;
                    Fix64 h = Fix64.Sqrt(hSq);
                    FixVec2 unit = d / distance;
                    FixVec2 mid = world[a] + unit * along;
                    var perp = new FixVec2(-unit.Y, unit.X);
                    AddThicketHullExit(mid + perp * h, p, body, ref n);
                    AddThicketHullExit(mid - perp * h, p, body, ref n);
                }
            }
            return n;
        }

        private void AddThicketHullExit(FixVec2 q, FixVec2 p, Fix64 body, ref int n)
        {
            if (InsideThicketHull(q, body)) return;
            _thicketHullExit[n] = q;
            _thicketHullExitDistSq[n] = FixVec2.DistanceSq(q, p);
            n++;
        }

        /// <summary>
        /// Герой внутри сразу после рывка: выход по направлению рывка (первая
        /// точка луча снаружи всех кругов), если он не дальше ThicketDashExitReach.
        /// </summary>
        private bool ThicketDashExit(FixVec2 p, Fix64 body, out FixVec2 exit)
        {
            exit = p;
            if (_dash.Serial == 0 || Tick > _dash.InvulnerableUntilTick + ThicketDashExitTicks) return false;
            FixVec2 d = _dash.Direction;
            if (d.LengthSq.Raw == 0) return false;
            Fix64 t = Fix64.Zero;
            for (int pass = 0; pass <= ThicketHullCircleCount; pass++)
            {
                bool moved = false;
                for (int k = 0; k < ThicketHullCircleCount; k++)
                {
                    if (ThicketHullSkips(k)) continue;
                    Fix64 r = ThicketHullRadius[k] + body + ThicketHullEpsilon;
                    FixVec2 rel = p - _thicketHullWorld[k];
                    Fix64 b = FixVec2.Dot(d, rel);
                    Fix64 disc = b * b - (rel.LengthSq - r * r);
                    if (disc.Raw <= 0) continue;
                    Fix64 root = Fix64.Sqrt(disc);
                    Fix64 enter = -b - root, leave = -b + root;
                    if (enter <= t && t < leave) { t = leave; moved = true; }
                }
                if (!moved) break;
            }
            if (t.Raw <= 0 || t > ThicketDashExitReach) return false;
            exit = p + d * t;
            return true;
        }

        /// <summary>
        /// Путь from → to тела body (from снаружи) входит в корпус: entry — первая точка
        /// пути на краю корпуса (снаружи), middle — середина куска пути внутри корпуса
        /// (до to, если путь кончился внутри). false — путь корпуса не касается.
        /// </summary>
        private bool ThicketHullSweep(FixVec2 from, FixVec2 to, Fix64 body, out FixVec2 entry, out FixVec2 middle)
        {
            entry = middle = from;
            FixVec2 delta = to - from;
            Fix64 length = delta.Length;
            if (length.Raw <= 0) return false;
            FixVec2 d = delta / length;
            Fix64 first = length;
            bool hit = false;
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                if (ThicketHullSkips(k)) continue;
                Fix64 r = ThicketHullRadius[k] + body + ThicketHullEpsilon;
                FixVec2 rel = from - _thicketHullWorld[k];
                Fix64 b = FixVec2.Dot(d, rel);
                Fix64 disc = b * b - (rel.LengthSq - r * r);
                if (disc.Raw <= 0) continue;
                Fix64 root = Fix64.Sqrt(disc);
                Fix64 enter = -b - root, leave = -b + root;
                if (leave.Raw <= 0 || enter > length) continue;
                if (enter.Raw < 0) enter = Fix64.Zero;
                if (!hit || enter < first) { first = enter; hit = true; }
            }
            if (!hit) return false;
            // Выход из объединения кругов после входа — как у ThicketDashExit.
            Fix64 t = first;
            for (int pass = 0; pass <= ThicketHullCircleCount; pass++)
            {
                bool moved = false;
                for (int k = 0; k < ThicketHullCircleCount; k++)
                {
                    if (ThicketHullSkips(k)) continue;
                    Fix64 r = ThicketHullRadius[k] + body + ThicketHullEpsilon;
                    FixVec2 rel = from - _thicketHullWorld[k];
                    Fix64 b = FixVec2.Dot(d, rel);
                    Fix64 disc = b * b - (rel.LengthSq - r * r);
                    if (disc.Raw <= 0) continue;
                    Fix64 root = Fix64.Sqrt(disc);
                    Fix64 enter = -b - root, leave = -b + root;
                    if (enter <= t && t < leave) { t = leave; moved = true; }
                }
                if (!moved || t >= length) break;
            }
            Fix64 end = t < length ? t : length;
            entry = from + d * first;
            if (InsideThicketHull(entry, body)) entry = from;   // округление: from снаружи заведомо
            middle = from + d * ((first + end) / 2);
            return true;
        }

        /// <summary>
        /// Глубина точки в корпусе тела body — путь до ближайшей точки снаружи (без стен),
        /// и сама эта точка. Снаружи — 0 и q.
        /// </summary>
        private Fix64 ThicketHullNearestExit(FixVec2 boss, FixVec2 q, Fix64 body, out FixVec2 exit)
        {
            exit = q;
            if (!InsideThicketHull(q, body)) return Fix64.Zero;
            int n = CollectThicketHullExits(boss, q, body);
            if (n == 0) return Fix64.FromInt(1000);
            int best = 0;
            for (int k = 1; k < n; k++)
                if (_thicketHullExitDistSq[k] < _thicketHullExitDistSq[best]) best = k;
            exit = _thicketHullExit[best];
            return Fix64.Sqrt(_thicketHullExitDistSq[best]);
        }

        private static Fix64 ThicketSegmentDistanceSq(FixVec2 point, FixVec2 a, FixVec2 b)
        {
            FixVec2 ab = b - a;
            Fix64 lengthSq = ab.LengthSq;
            if (lengthSq.Raw <= 0) return FixVec2.DistanceSq(point, a);
            Fix64 t = Fix64.Clamp(FixVec2.Dot(point - a, ab) / lengthSq, Fix64.Zero, Fix64.One);
            return FixVec2.DistanceSq(point, a + ab * t);
        }

        // ---------- попадания героя ----------

        /// <summary>
        /// Окно ударов героя (Step: ResolveArtifactUse … ResolveAttacks): тело
        /// босса для попаданий — от его центра до точки корпуса, ближайшей к
        /// герою. Удары героя меряют цель по центру и BodyRadius; так герой,
        /// стоящий вплотную к крупу, боку или голове, достаёт с той же дальности,
        /// что и до тела обычного моба. Ход, расталкивание и всё прочее видят
        /// прежние 0,95: EndThicketHitBodies возвращает их в конце окна.
        /// </summary>
        private void BeginThicketHitBodies()
        {
            _thicketHitCount = 0;
            if (_thicketMemory == null || !Entities.Alive[PlayerId]) return;
            FixVec2 hero = Entities.Position[PlayerId];
            for (int id = 1; id < Entities.Count; id++)
            {
                if (!ThicketHullActive(id)) continue;
                Fix64 saved = Entities.BodyRadius[id];
                Fix64 reach = FixVec2.Distance(hero, Entities.Position[id]) - ThicketHullGap(id, hero);
                // Влетел в корпус сам (Шквал, тяга якоря) — в упор: тело достаёт до его центра.
                // Иначе Шквал, остановленный грудью в 0,45 м (тело героя), прошёл бы мимо неё.
                if (_thicketHeroRamTick == Tick && _thicketHeroRamBoss == id)
                    reach = Fix64.Max(reach, FixVec2.Distance(hero, Entities.Position[id]));
                if (reach <= saved) continue;
                if (_thicketHitIds == null || _thicketHitCount >= _thicketHitIds.Length)
                {
                    int size = Math.Max(4, _thicketHitCount * 2);
                    Array.Resize(ref _thicketHitIds, size);
                    Array.Resize(ref _thicketHitSaved, size);
                    Array.Resize(ref _thicketHitValue, size);
                }
                _thicketHitIds[_thicketHitCount] = id;
                _thicketHitSaved[_thicketHitCount] = saved;
                _thicketHitValue[_thicketHitCount] = reach;
                _thicketHitCount++;
                Entities.BodyRadius[id] = reach;
            }
        }

        /// <summary>Конец окна ударов героя: тело — прежнее (если внутри окна его не поменял сам босс).</summary>
        private void EndThicketHitBodies()
        {
            for (int k = 0; k < _thicketHitCount; k++)
            {
                int id = _thicketHitIds[k];
                if (Entities.BodyRadius[id] == _thicketHitValue[k]) Entities.BodyRadius[id] = _thicketHitSaved[k];
            }
            _thicketHitCount = 0;
        }

        /// <summary>
        /// Поиск по центрам (Вихрь, вспышка Пламени): босс входит, если корпус
        /// ближе radius за вычетом его тела 0,95 — как центр обычного моба на
        /// глубине своего тела. Без корпуса и без босса — ответ сетки как есть.
        /// </summary>
        private int AddThicketHullsToQuery(int found, FixVec2 center, Fix64 radius, int exclude)
        {
            if (_thicketMemory == null) return found;
            for (int id = 1; id < Entities.Count && found < HitScratch.Length; id++)
            {
                if (id == exclude || !ThicketHullActive(id)) continue;
                bool listed = false;
                for (int k = 0; k < found && !listed; k++) listed = HitScratch[k] == id;
                if (listed) continue;
                if (ThicketHullWithin(id, center, radius)) HitScratch[found++] = id;
            }
            return found;
        }

        /// <summary>
        /// Поиск «центр цели ближе radius» для босса — по корпусу: корпус ближе
        /// radius за вычетом тела 0,95 (центр обычного моба лежит на глубине
        /// своего тела). Вихрь, вспышка Пламени, дальность цели способности
        /// (ValidAbilityTarget: Абордаж, Шаг по цепи). Без корпуса — false.
        /// </summary>
        private bool ThicketHullWithin(int id, FixVec2 point, Fix64 radius)
            => _thicketMemory != null && ThicketHullActive(id)
               && ThicketHullGap(id, point) + EnemyArchetypes.ThicketMasterBodyRadius <= radius;

        /// <summary>
        /// Тело цели для площади с центром point (лужа, взрыв склянки, след Пламени):
        /// «Distance(point, центр) ≤ R + тело» ⇔ «корпус ближе R». Не зависит от того,
        /// где герой, — в отличие от BodyRadius в окне ударов героя. Не босс, нырок,
        /// смерть — обычное BodyRadius.
        /// </summary>
        internal Fix64 ThicketBodyFrom(int target, FixVec2 point)
            => ThicketHullActive(target)
                ? FixVec2.Distance(point, Entities.Position[target]) - ThicketHullGap(target, point)
                : Entities.BodyRadius[target];

        /// <summary>
        /// Фигура удара героя (сектор сабли и обычной атаки, круг, кольцо) задевает тело
        /// цели: у босса — любой из семи кругов корпуса, у прочих — круг BodyRadius.
        /// Для файлов Пелага (Simulation.SabreCombo, Simulation.BasicAttack): точнее
        /// подмены BodyRadius на окно ударов — сектор, повёрнутый мимо бока, воздух не бьёт.
        /// </summary>
        internal bool ThicketHitTouches(in EnemyTelegraph shape, int target)
        {
            if (!ThicketHullActive(target))
                return TelegraphContains(in shape, Entities.Position[target], Entities.BodyRadius[target]);
            FixVec2 boss = Entities.Position[target], facing = ThicketHullFacing(target);
            for (int k = 0; k < ThicketHullCircleCount; k++)
                if (TelegraphContains(in shape, ThicketHullCenter(boss, facing, k), ThicketHullRadius[k])) return true;
            return false;
        }

        /// <summary>
        /// Полоса от origin по direction длиной length и полушириной halfWidth задевает
        /// корпус босса (Удар якоря, AnchorSlam.InsideSlamLane). Без корпуса — false:
        /// вызывающий меряет обычное тело.
        /// </summary>
        internal bool ThicketHullInLane(int target, FixVec2 origin, FixVec2 direction, Fix64 length, Fix64 halfWidth)
        {
            if (!ThicketHullActive(target)) return false;
            FixVec2 boss = Entities.Position[target], facing = ThicketHullFacing(target);
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                FixVec2 delta = ThicketHullCenter(boss, facing, k) - origin;
                Fix64 along = FixVec2.Dot(delta, direction);
                Fix64 across = Fix64.Abs(delta.X * direction.Y - delta.Y * direction.X);
                Fix64 dx = along - Fix64.Clamp(along, Fix64.Zero, length);
                Fix64 dy = across > halfWidth ? across - halfWidth : Fix64.Zero;
                if (dx * dx + dy * dy <= ThicketHullRadius[k] * ThicketHullRadius[k]) return true;
            }
            return false;
        }
    }
}
