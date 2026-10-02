using System;

namespace Game.Sim
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ, этап 2 «Атаки» (спецификация 01.10): нырок в корни,
    /// прорастание, облака пыльцы (Simulation.ForestBoss.Pollen.cs), ягодный
    /// ливень. Ядро (Simulation.ForestBoss.cs / .Brain.cs) зовёт частичные
    /// методы этого файла; своё состояние — ленивые массивы ниже, свой хеш.
    ///
    /// Фазы: нырок — 1–3, прорастание и пыльца — 2–3, ливень — 3.
    ///
    /// НЫРОК. Замах — уход в землю (ThicketDiveBurrowTicks), потом бугор
    /// ThicketDiveTravelTicks едет к герою своим ходом босса (без оглядки на
    /// поворот, с поводком и стенами; шаг — остаток пути на оставшиеся тики,
    /// не больше ThicketMoundMaxStep), в тик фиксации круг r3,5 встаёт под
    /// героем (не дальше поводка + круга от точки появления — Target), через
    /// ThicketDiveLockTicks босс вылезает (контакт) в точке выхода (Origin):
    /// в поводке, тело целиком помещается, бугор доезжает по прямой, всегда
    /// внутри круга (ThicketDiveLock). Потом стоит ThicketDiveStandTicks.
    /// Неуязвимости нет: бугор живой и бьётся; под землёй тело
    /// ThicketMoundRadius, чтобы не перегораживало проход.
    /// Stage: 0 — уходит, 1 — бугор едет, 2 — круг лежит, 3 — вылез.
    /// EnemyActionStarted ThicketDive: Amount 0 — уход, 1 — бугор, 2 — круг;
    /// EnemyActionImpact — выход (Position — где вылез, Flag — задел).
    ///
    /// ПРОРАСТАНИЕ. 6 кругов r1,5 там, где герой стоит, новый раз в 9 тиков,
    /// удар каждого через 30 после его метки, одно попадание на круг, без
    /// корней. Босс стоит ThicketSproutStandTicks. Вес каста в бюджете — 2.
    /// EnemyActionImpact ThicketSprout: Amount — номер круга, Position — центр.
    ///
    /// ЛИВЕНЬ. 3 залпа по 4 круга r1,4 вокруг героя по шаблону со щелями
    /// не меньше 2 м (ThicketRainTemplate), залпы через 15 тиков, удар через
    /// 30 после метки залпа, за залп — не больше одного попадания; после
    /// последнего удара стоит ThicketRainStandTicks. Вес каста — 2.
    /// EnemyActionImpact ThicketRain: Amount — номер залпа.
    ///
    /// Если пул меток полон, удар всё равно решается по сохранённой фигуре —
    /// как у лапы и топота ядра.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- нырок в корни ----

        public const int ThicketDiveBurrowTicks = 12, ThicketDiveTravelTicks = 30;
        public const int ThicketDiveLockTicks = 36, ThicketDiveStandTicks = 36;
        public static readonly Fix64 ThicketDiveRadius = Fix64.Ratio(7, 2);

        /// <summary>Нырок — если герой дальше 7 м (между центрами) или раз в ~10 с.</summary>
        public static readonly Fix64 ThicketDiveFarRange = Fix64.FromInt(7);

        /// <summary>«Раз в ~10 с» от начала прошлого нырка: ×1,25 при подмоге, ×0,85 с половины здоровья, Часы сдвигают.</summary>
        public const int ThicketDiveEveryTicks = 300;

        /// <summary>Самое частое — раз в 5 с от начала; первый — не раньше 5 с после первого выбора.</summary>
        public const int ThicketDiveCooldownTicks = 150;

        /// <summary>Тело под землёй — не перегораживает проход, но бьётся.</summary>
        public static readonly Fix64 ThicketMoundRadius = Fix64.Ratio(1, 2);

        /// <summary>Бугор — не быстрее 0,4 м за тик (12 м/с).</summary>
        public static readonly Fix64 ThicketMoundMaxStep = Fix64.Ratio(2, 5);

        // ---- прорастание ----

        public const int ThicketSproutCircles = 6, ThicketSproutEveryTicks = 9, ThicketSproutImpactTicks = 30;
        /// <summary>Перезарядка 15 с (было 11): стойка 90 тиков на одну атаку тянула темп фазы 2 к 2,9 с.</summary>
        public const int ThicketSproutStandTicks = 90, ThicketSproutCooldownTicks = 450;
        public static readonly Fix64 ThicketSproutRadius = Fix64.Ratio(3, 2);

        // ---- ягодный ливень ----

        public const int ThicketRainVolleys = 3, ThicketRainCircles = 4, ThicketRainEveryTicks = 15, ThicketRainImpactTicks = 30;
        /// <summary>Перезарядка 15 с (было 10), как у прорастания: стойка 60 после залпов — темп фазы 3.</summary>
        public const int ThicketRainStandTicks = 60, ThicketRainCooldownTicks = 450;
        public static readonly Fix64 ThicketRainRadius = Fix64.Ratio(7, 5);

        /// <summary>Щель между кругами одного залпа — не меньше 2 м от края до края.</summary>
        public static readonly Fix64 ThicketRainGap = Fix64.FromInt(2);

        /// <summary>Шаблон залпа: один круг на герое, три — в 5–5,6 м от него через 120° ± 15°.</summary>
        public static readonly Fix64 ThicketRainRingMin = Fix64.FromInt(5);
        public static readonly Fix64 ThicketRainRingJitter = Fix64.Ratio(3, 5);
        private static readonly Fix64 ThicketRainAngleJitter = Fix64.Pi / 12;

        // ---- общее ----

        /// <summary>Вес каста прорастания и ливня в бюджете крупных меток.</summary>
        public const int ThicketCastMarkWeight = 2;

        /// <summary>Касты (прорастание, пыльца, ливень) — когда герой ближе 10 м.</summary>
        public static readonly Fix64 ThicketCastRange = Fix64.FromInt(10);

        /// <summary>Веса взвешенного выбора рядом с лапой (у неё 10).</summary>
        public const int ThicketSproutWeight = 6, ThicketPollenWeight = 4, ThicketRainWeight = 6;

        /// <summary>Мест под круги каста на одного босса: 6 прорастания или 3 × 4 ливня.</summary>
        public const int ThicketShapeSlots = 12;

        private const byte ThicketShapeEmpty = 0, ThicketShapePending = 1, ThicketShapeDone = 2;

        private int[] _thicketShapeSerial, _thicketShapeImpact;
        private byte[] _thicketShapeState;
        private FixVec2[] _thicketShapeCenter;
        private FixVec2[] _thicketRainScratch;

        private int[] ThicketShapeSerial => _thicketShapeSerial ??= new int[Entities.Capacity * ThicketShapeSlots];
        private int[] ThicketShapeImpact => _thicketShapeImpact ??= new int[Entities.Capacity * ThicketShapeSlots];
        private byte[] ThicketShapeState => _thicketShapeState ??= new byte[Entities.Capacity * ThicketShapeSlots];
        private FixVec2[] ThicketShapeCenter => _thicketShapeCenter ??= new FixVec2[Entities.Capacity * ThicketShapeSlots];

        // ---- чтение для вида и тестов ----

        /// <summary>Босс под землёй: бугор едет или круг нырка лежит.</summary>
        public bool ThicketUnderground(int id)
        {
            if (_thicketMasters == null || (uint)id >= (uint)_thicketMasters.Length) return false;
            var a = _thicketMasters[id];
            return a.Serial != 0 && a.Action == ThicketMasterAction.Dive && !a.HitResolved && a.Stage >= 1;
        }

        /// <summary>
        /// Круг каста index (прорастание 0–5, ливень 4 × залп + круг): центр,
        /// тик удара, сработал ли. false — круга нет.
        /// </summary>
        public bool TryGetThicketShape(int id, int index, out FixVec2 center, out int impactTick, out bool resolved)
        {
            center = default; impactTick = 0; resolved = false;
            if (_thicketShapeState == null || (uint)id >= (uint)Entities.Capacity || (uint)index >= ThicketShapeSlots) return false;
            int k = id * ThicketShapeSlots + index;
            if (_thicketShapeState[k] == ThicketShapeEmpty) return false;
            center = _thicketShapeCenter[k]; impactTick = _thicketShapeImpact[k];
            resolved = _thicketShapeState[k] == ThicketShapeDone;
            return true;
        }

        public int ThicketDiveDamageOf(int id) => ThicketShareOf(id, ThicketDiveDamageA9);
        public int ThicketSproutDamageOf(int id) => ThicketShareOf(id, ThicketSproutDamageA9);
        public int ThicketRainDamageOf(int id) => ThicketShareOf(id, ThicketRainDamageA9);

        public static EnemyTelegraph ThicketDiveCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketDiveRadius);
        public static EnemyTelegraph ThicketSproutCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketSproutRadius);
        public static EnemyTelegraph ThicketRainCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketRainRadius);

        /// <summary>
        /// Шаблон залпа ливня вокруг hero (centers — 4 места): один круг на
        /// герое, три — на 5–5,6 м через 120° с разбросом ±15°, поворот всего
        /// шаблона случайный. Край к краю — не меньше ThicketRainGap: от
        /// центрального 5 − 2,8 = 2,2 м, между внешними (угол ≥ 90°) ≥ 4,2 м.
        /// </summary>
        public static void ThicketRainTemplate(ref Pcg32 rng, FixVec2 hero, FixVec2[] centers)
        {
            centers[0] = hero;
            Fix64 turn = rng.NextFix() * Fix64.TwoPi;
            for (int k = 1; k < ThicketRainCircles; k++)
            {
                Fix64 angle = turn + Fix64.TwoPi * Fix64.Ratio(k - 1, 3)
                    + (rng.NextFix() * 2 - Fix64.One) * ThicketRainAngleJitter;
                Fix64 ring = ThicketRainRingMin + rng.NextFix() * ThicketRainRingJitter;
                centers[k] = hero + FixVec2.FromAngle(angle) * ring;
            }
        }

        private int ThicketReadyAt(int id, ThicketMasterAction action) => ThicketReady[id * ThicketActionSlots + (int)action];

        // ---- выбор ----

        /// <summary>
        /// Нырок: герой дальше 7 м (или дальше поводка + лапы от точки появления —
        /// пешком босс его не достанет) или пришёл срок «раз в ~10 с», перезарядка
        /// готова. Герой и за поводком не в безопасности: круг ляжет на краю
        /// досягаемого (ThicketDiveLock). Первый выбор только заводит часы:
        /// первый нырок — не раньше ThicketDiveCooldownTicks, по сроку — через
        /// ThicketDiveEveryTicks (оба — со множителями подмоги и ярости).
        /// </summary>
        partial void ThicketChooseRule(int id, ref ThicketMasterAction choice)
        {
            ref var m = ref ThicketMemory[id];
            if (m.DiveNextTick == 0)
            {
                m.DiveNextTick = Tick + ThicketScaled(id, ThicketDiveEveryTicks);
                // Стенд или тест мог уже отложить нырок дальше — не укорачивать.
                int first = Tick + ThicketScaled(id, ThicketDiveCooldownTicks);
                if (ThicketReadyAt(id, ThicketMasterAction.Dive) < first)
                    ThicketReady[id * ThicketActionSlots + (int)ThicketMasterAction.Dive] = first;
                return;
            }
            if (Tick < ThicketReadyAt(id, ThicketMasterAction.Dive)) return;
            FixVec2 hero = Entities.Position[PlayerId];
            bool far = FixVec2.DistanceSq(hero, Entities.Position[id]) > ThicketDiveFarRange * ThicketDiveFarRange;
            // Пешком не достать: босс на поводке, герой дальше лапы от его края —
            // ныряет, как по дальнему герою, а не стоит у края до срока.
            Fix64 foot = ThicketLeash + ThicketPawStartRange;
            bool beyond = FixVec2.DistanceSq(hero, m.Home) > foot * foot;
            if (far || beyond || Tick >= m.DiveNextTick) choice = ThicketMasterAction.Dive;
        }

        /// <summary>Касты фаз 2–3 рядом с лапой: герой ближе 10 м и перезарядка готова.</summary>
        partial void ThicketAddCandidates(int id)
        {
            int phase = ThicketMemory[id].Phase;
            if (phase < 2) return;
            Fix64 range = ThicketCastRange;
            if (FixVec2.DistanceSq(Entities.Position[PlayerId], Entities.Position[id]) > range * range) return;
            if (Tick >= ThicketReadyAt(id, ThicketMasterAction.Sprout))
                AddThicketCandidate(ThicketMasterAction.Sprout, ThicketSproutWeight);
            if (Tick >= ThicketReadyAt(id, ThicketMasterAction.Pollen) && ThicketLivePollenZones() == 0)
                AddThicketCandidate(ThicketMasterAction.Pollen, ThicketPollenWeight);
            if (phase >= 3 && Tick >= ThicketReadyAt(id, ThicketMasterAction.Rain))
                AddThicketCandidate(ThicketMasterAction.Rain, ThicketRainWeight);
        }

        partial void ThicketStartExtra(int id, ThicketMasterAction action, ref bool started)
        {
            switch (action)
            {
                case ThicketMasterAction.Dive: started = StartThicketDive(id); return;
                case ThicketMasterAction.Sprout: started = StartThicketSprout(id); return;
                case ThicketMasterAction.Pollen: started = StartThicketPollen(id); return;
                case ThicketMasterAction.Rain: started = StartThicketRain(id); return;
                case ThicketMasterAction.Storm: started = StartThicketStorm(id); return;
            }
        }

        partial void ThicketAdvanceExtra(int id)
        {
            switch (ThicketMasters[id].Action)
            {
                case ThicketMasterAction.Dive: AdvanceThicketDive(id); return;
                case ThicketMasterAction.Sprout:
                case ThicketMasterAction.Rain: AdvanceThicketCast(id); return;
                case ThicketMasterAction.Pollen: AdvanceThicketPollen(id); return;
                case ThicketMasterAction.Storm: AdvanceThicketStorm(id); return;
            }
        }

        /// <summary>Снятое действие: тело из-под земли — в свой размер, круги каста и падающая пыльца — прочь.</summary>
        partial void ThicketCancelExtra(int id)
        {
            var a = ThicketMasters[id];
            if (a.Action == ThicketMasterAction.Dive)
            {
                // Снят под землёй (смерть героя): тело — в своё место, не в дерево.
                Fix64 body = EnemyArchetypes.ThicketMasterBodyRadius;
                if (Entities.Alive[id] && _layout != null && !_layout.IsWalkable(Entities.Position[id], body))
                    Entities.Position[id] = ThicketDiveSurface(id, Entities.Position[id]);
                Entities.BodyRadius[id] = body;
            }
            ClearThicketShapes(id);
            if (a.Action == ThicketMasterAction.Pollen) DropFallingPollen(id);
        }

        private void ClearThicketShapes(int id)
        {
            if (_thicketShapeState == null) return;
            int from = id * ThicketShapeSlots;
            Array.Clear(_thicketShapeState, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeSerial, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeImpact, from, ThicketShapeSlots);
            Array.Clear(_thicketShapeCenter, from, ThicketShapeSlots);
        }

        // ---------- нырок в корни ----------

        /// <summary>Нырок: крупная метка весом 1 бронируется с начала ухода — круг ляжет на 42-м тике.</summary>
        private bool StartThicketDive(int id)
        {
            int impact = Tick + ThicketDiveBurrowTicks + ThicketDiveTravelTicks + ThicketDiveLockTicks;
            if (!BigMarkAllowed(id, 1, impact)) return false;
            BeginThicketAction(id, ThicketMasterAction.Dive, impact, impact, impact + ThicketRecoveryOf(id, ThicketDiveStandTicks), 1,
                Entities.Facing[id], Entities.Position[PlayerId]);
            SetThicketCooldown(id, ThicketMasterAction.Dive, ThicketDiveCooldownTicks);
            ThicketMemory[id].DiveNextTick = Tick + ThicketScaled(id, ThicketDiveEveryTicks);
            return true;
        }

        /// <summary>Тик фиксации круга. Считается от ImpactTick — его сдвигают Часы, StartTick — нет.</summary>
        private static int ThicketDiveLockTick(in ThicketMasterState a) => a.ImpactTick - ThicketDiveLockTicks;

        /// <summary>
        /// Ход под землёй — свой, вместо ядра. Уход: стоит. Бугор: к герою,
        /// шаг — остаток пути на оставшиеся тики (в тик фиксации — под ним),
        /// не больше ThicketMoundMaxStep. Круг лежит: к точке выхода (Origin). Поводок и
        /// стены — как у обычного шага; круп не выталкивает.
        /// </summary>
        partial void ThicketMoveExtra(int id, ref bool handled)
        {
            var a = ThicketMasters[id];
            if (a.Action != ThicketMasterAction.Dive || a.HitResolved) return;
            handled = true;
            if (a.Stage == 0) return;
            FixVec2 from = Entities.Position[id];
            FixVec2 goal = a.Stage == 1 ? Entities.Position[PlayerId] : a.Origin;
            FixVec2 toGoal = goal - from;
            if (toGoal.LengthSq.Raw == 0) return;
            Fix64 distance = toGoal.Length;
            FixVec2 direction = toGoal / distance;
            Fix64 step = ThicketMoundMaxStep;
            if (a.Stage == 1)
            {
                int remaining = Math.Max(1, ThicketDiveLockTick(a) - Tick + 1);
                step = Fix64.Min(step, distance / remaining);
            }
            FixVec2 delta = direction * Fix64.Min(step, distance);
            Entities.Facing[id] = direction;
            FixVec2 next = from + delta;
            ref var m = ref ThicketMemory[id];
            Fix64 leashSq = ThicketLeash * ThicketLeash;
            if (FixVec2.DistanceSq(next, m.Home) > leashSq && FixVec2.DistanceSq(next, m.Home) > FixVec2.DistanceSq(from, m.Home))
                return;
            if (delta.LengthSq.Raw == 0) return;
            Entities.Position[id] = EnemyStep(id, from, delta);
            Entities.Velocity[id] = Entities.Position[id] - from;
        }

        private void AdvanceThicketDive(int id)
        {
            ref var a = ref ThicketMasters[id];
            int lockTick = ThicketDiveLockTick(a);
            if (a.Stage == 0 && Tick >= lockTick - ThicketDiveTravelTicks)
            {
                // Ушёл: дальше едет бугор, тело — маленькое.
                a.Stage = 1;
                a.StageStartTick = Tick;
                Entities.BodyRadius[id] = ThicketMoundRadius;
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketDive, Entities.Position[id], 1));
            }
            else if (a.Stage == 1 && Tick >= lockTick)
            {
                // Круг фиксируется под героем (или на краю досягаемого); бугор
                // доезжает к точке выхода — она всегда внутри круга.
                a.Stage = 2;
                a.StageStartTick = Tick;
                ThicketDiveLock(id, ref a);
                OpenThicketMark(id, ref a, ThicketDiveCircle(a.Target), TelegraphFlags.SharedView);
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketDive, a.Target, 2));
            }
            if (a.Stage == 2 && !a.HitResolved && Tick >= a.ImpactTick)
            {
                ResolveThicketDive(id);
                return;
            }
            // Связка «Нырок→Топот» (фаза 3): стойки после выхода нет — сразу замах топота.
            if (a.Stage == 3 && (Tick >= a.EndTick || ThicketChainCuts(id, a.ImpactTick + 1)))
                FinishThicketAction(id, ThicketRestTicks(id));
        }

        /// <summary>Выход: тело — в свой размер, удар по кругу (подброс без контроля — только вид).</summary>
        private void ResolveThicketDive(int id)
        {
            ref var a = ref ThicketMasters[id];
            // Состояние — до урона: отражение может убить босса внутри ApplyAbilityDamage.
            a.HitResolved = true;
            a.Stage = 3;
            a.StageStartTick = Tick;
            a.Direction = Entities.Facing[id];
            // Вылезает в точке выхода (Origin): тело там помещается, она в круге.
            Entities.Position[id] = a.Origin;
            Entities.BodyRadius[id] = EnemyArchetypes.ThicketMasterBodyRadius;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool hit = live && Entities.Alive[PlayerId]
                && TelegraphContains(ThicketDiveCircle(a.Target), Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketDive, Entities.Position[id], 3, hit));
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketDiveDamageOf(id), -1, DamageType.Physical);
        }

        /// <summary>
        /// Фиксация круга нырка. Круг (Target) — под героем, но не дальше
        /// поводка + круга от точки появления: герой за поводком тоже под
        /// угрозой. Точка выхода (Origin) — ближайшая к кругу в поводке, где
        /// тело босса целиком помещается и куда бугор доедет по прямой
        /// (ThicketDiveSurface). Круг при нужде подвигается к ней — босс всегда
        /// вылезает в своём круге, удар и выход совпадают.
        /// </summary>
        private void ThicketDiveLock(int id, ref ThicketMasterState a)
        {
            FixVec2 home = ThicketMemory[id].Home;
            FixVec2 circle = ThicketWithin(home, Entities.Position[PlayerId], ThicketLeash + ThicketDiveRadius);
            FixVec2 surface = ThicketDiveSurface(id, ThicketWithin(home, circle, ThicketLeash - ThicketLeashSlack));
            FixVec2 off = circle - surface;
            if (off.LengthSq > ThicketDiveRadius * ThicketDiveRadius)
                circle = surface + off.Normalized() * (ThicketDiveRadius - ThicketLeashSlack);
            a.Target = circle;
            a.Origin = surface;
        }

        /// <summary>Шаг поиска точки выхода и запас на округление у края поводка.</summary>
        private static readonly Fix64 ThicketSurfaceProbe = Fix64.Ratio(1, 2);
        private static readonly Fix64 ThicketLeashSlack = Fix64.Ratio(1, 100);

        /// <summary>point, подтянутая к home не дальше radius.</summary>
        private static FixVec2 ThicketWithin(FixVec2 home, FixVec2 point, Fix64 radius)
        {
            FixVec2 off = point - home;
            return off.LengthSq > radius * radius ? home + off.Normalized() * radius : point;
        }

        /// <summary>
        /// Где вылезет босс: ближайшая к want точка в поводке, где его тело
        /// помещается и куда бугор (ThicketMoundRadius) доедет от себя по прямой —
        /// кольца через 0,5 м до радиуса круга. Не нашлось (стена между бугром и
        /// героем) — то же вокруг последней точки прямой бугор → want, куда он
        /// доезжает; совсем тупик — ближайшее место тела на карте.
        /// </summary>
        private FixVec2 ThicketDiveSurface(int id, FixVec2 want)
        {
            if (_layout == null) return want;
            FixVec2 mound = Entities.Position[id];
            if (ThicketSurfaceAround(id, mound, want, out FixVec2 found)) return found;
            FixVec2 reach = mound;
            FixVec2 toWant = want - mound;
            if (toWant.LengthSq.Raw > 0)
            {
                Fix64 length = toWant.Length;
                FixVec2 direction = toWant / length;
                int steps = (length / ThicketSurfaceProbe).ToInt();
                for (int k = 1; k <= steps; k++)
                {
                    FixVec2 p = mound + direction * (ThicketSurfaceProbe * k);
                    if (!_layout.CanTravel(mound, p, ThicketMoundRadius)) break;
                    reach = p;
                }
            }
            if (ThicketSurfaceAround(id, mound, reach, out found)) return found;
            return _layout.ClampToWalkable(mound, EnemyArchetypes.ThicketMasterBodyRadius);
        }

        private bool ThicketSurfaceAround(int id, FixVec2 mound, FixVec2 center, out FixVec2 found)
        {
            FixVec2 home = ThicketMemory[id].Home;
            Fix64 leashSq = ThicketLeash * ThicketLeash;
            Fix64 body = EnemyArchetypes.ThicketMasterBodyRadius;
            int rings = (ThicketDiveRadius / ThicketSurfaceProbe).ToInt();
            for (int ring = 0; ring <= rings; ring++)
            {
                int directions = ring == 0 ? 1 : 12;
                for (int k = 0; k < directions; k++)
                {
                    FixVec2 c = center + FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 12)) * (ThicketSurfaceProbe * ring);
                    if (FixVec2.DistanceSq(c, home) > leashSq || !_layout.IsWalkable(c, body)
                        || !_layout.CanTravel(mound, c, ThicketMoundRadius)) continue;
                    found = c;
                    return true;
                }
            }
            found = center;
            return false;
        }

        // ---------- прорастание и ливень: касты из нескольких кругов ----------

        /// <summary>
        /// Прорастание: первый круг — сразу под героем, остальные 5 — раз в 9
        /// тиков. Stages — кругов всего, Stage — сколько уже встало,
        /// StageStartTick — когда встанет следующий, ImpactTick — ближайший удар.
        /// </summary>
        private bool StartThicketSprout(int id)
        {
            int first = Tick + ThicketSproutImpactTicks;
            int last = first + ThicketSproutEveryTicks * (ThicketSproutCircles - 1);
            if (!BigMarkAllowed(id, ThicketCastMarkWeight, first) || !HeroContactAllowed(id, first, last)) return false;
            ClearThicketShapes(id);
            // Стоит ThicketSproutStandTicks от начала; в фазе 3 режется остаток после последнего удара.
            int end = last + ThicketRecoveryOf(id, Math.Max(0, Tick + ThicketSproutStandTicks - last));
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Sprout, first, last, end,
                ThicketSproutCircles, Entities.Facing[id], Entities.Position[PlayerId]);
            OpenThicketCastStage(id, ref a);
            SetThicketCooldown(id, ThicketMasterAction.Sprout, ThicketSproutCooldownTicks);
            return true;
        }

        /// <summary>Ливень: первый залп — сразу, остальные — раз в 15 тиков; Stages — залпов.</summary>
        private bool StartThicketRain(int id)
        {
            int first = Tick + ThicketRainImpactTicks;
            int last = first + ThicketRainEveryTicks * (ThicketRainVolleys - 1);
            if (!BigMarkAllowed(id, ThicketCastMarkWeight, first) || !HeroContactAllowed(id, first, last)) return false;
            ClearThicketShapes(id);
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Rain, first, last, last + ThicketRecoveryOf(id, ThicketRainStandTicks),
                ThicketRainVolleys, Entities.Facing[id], Entities.Position[PlayerId]);
            OpenThicketCastStage(id, ref a);
            SetThicketCooldown(id, ThicketMasterAction.Rain, ThicketRainCooldownTicks);
            return true;
        }

        /// <summary>Следующий шаг каста: круг прорастания под героем или залп ливня вокруг него.</summary>
        private void OpenThicketCastStage(int id, ref ThicketMasterState a)
        {
            FixVec2 hero = Entities.Position[PlayerId];
            bool rain = a.Action == ThicketMasterAction.Rain;
            int impact = Tick + (rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks);
            int stage = a.Stage;
            if (rain)
            {
                _thicketRainScratch ??= new FixVec2[ThicketRainCircles];
                ThicketRainTemplate(ref ThicketMemory[id].Rng, hero, _thicketRainScratch);
                for (int k = 0; k < ThicketRainCircles; k++)
                    OpenThicketShape(id, stage * ThicketRainCircles + k, ThicketRainCircle(_thicketRainScratch[k]), impact, ref a);
            }
            else OpenThicketShape(id, stage, ThicketSproutCircle(hero), impact, ref a);
            a.Stage = stage + 1;
            a.StageStartTick = Tick + (rain ? ThicketRainEveryTicks : ThicketSproutEveryTicks);
            a.Target = hero;
        }

        private void OpenThicketShape(int id, int index, in EnemyTelegraph shape, int impact, ref ThicketMasterState a)
        {
            int k = id * ThicketShapeSlots + index;
            int slot = OpenTelegraph(id, shape, impact, impact + TelegraphLingerTicks, TelegraphFlags.SharedView);
            int serial = TryGetTelegraph(slot, out var t) ? t.Serial : 0;
            ThicketShapeSerial[k] = serial;
            ThicketShapeImpact[k] = impact;
            ThicketShapeCenter[k] = shape.Origin;
            ThicketShapeState[k] = ThicketShapePending;
            a.TelegraphSerial = serial;
        }

        /// <summary>
        /// Тик каста: встаёт следующий круг (залп), срабатывают созревшие —
        /// каждый круг прорастания бьёт сам, залп ливня — не больше одного раза
        /// на все 4 круга. Стойка до EndTick, потом отдых.
        /// </summary>
        private void AdvanceThicketCast(int id)
        {
            {
                ref var a = ref ThicketMasters[id];
                if (a.Stage < a.Stages && Tick >= a.StageStartTick) OpenThicketCastStage(id, ref a);
            }
            ResolveThicketShapesDue(id);
            var s = ThicketMasters[id];
            if (s.Serial == 0) return;
            int next = NextThicketShapeImpact(id, s);
            ref var b = ref ThicketMasters[id];
            if (next != int.MaxValue) b.ImpactTick = next;
            else { b.ImpactTick = b.LastImpactTick; b.HitResolved = true; }
            if (b.HitResolved && Tick >= b.EndTick) FinishThicketAction(id, ThicketRestTicks(id));
        }

        /// <summary>Ближайший ещё не случившийся удар каста (встал или встанет); MaxValue — всё отбито.</summary>
        private int NextThicketShapeImpact(int id, in ThicketMasterState a)
        {
            int best = int.MaxValue;
            int from = id * ThicketShapeSlots;
            for (int k = 0; k < ThicketShapeSlots; k++)
                if (ThicketShapeState[from + k] == ThicketShapePending && ThicketShapeImpact[from + k] < best)
                    best = ThicketShapeImpact[from + k];
            if (best == int.MaxValue && a.Stage < a.Stages)
                best = a.StageStartTick + (a.Action == ThicketMasterAction.Rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks);
            return best;
        }

        private void ResolveThicketShapesDue(int id)
        {
            var a = ThicketMasters[id];
            bool rain = a.Action == ThicketMasterAction.Rain;
            int from = id * ThicketShapeSlots;
            int groups = rain ? ThicketRainVolleys : ThicketSproutCircles;
            int size = rain ? ThicketRainCircles : 1;
            for (int g = 0; g < groups; g++)
            {
                int first = from + g * size;
                if (ThicketShapeState[first] != ThicketShapePending || ThicketShapeImpact[first] > Tick) continue;
                // Состояние всех кругов группы — до урона.
                bool hit = false;
                FixVec2 at = ThicketShapeCenter[first];
                FixVec2 hero = Entities.Position[PlayerId];
                Fix64 body = Entities.BodyRadius[PlayerId];
                for (int k = first; k < first + size; k++)
                {
                    ThicketShapeState[k] = ThicketShapeDone;
                    bool live = ResolveTelegraphSerial(ThicketShapeSerial[k]) || ThicketShapeSerial[k] == 0;
                    var circle = rain ? ThicketRainCircle(ThicketShapeCenter[k]) : ThicketSproutCircle(ThicketShapeCenter[k]);
                    if (!hit && live && Entities.Alive[PlayerId] && TelegraphContains(in circle, hero, body))
                    { hit = true; at = ThicketShapeCenter[k]; }
                }
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                    rain ? EnemyActionKind.ThicketRain : EnemyActionKind.ThicketSprout, at, g, hit));
                if (!hit) continue;
                ApplyAbilityDamage(id, PlayerId, rain ? ThicketRainDamageOf(id) : ThicketSproutDamageOf(id), -1, DamageType.Physical);
                // Отражение могло убить босса: дальше круги снимет его смерть.
                if (!Entities.Alive[id]) return;
            }
        }

        // ---------- бюджет, такт, Часы, сброс, хеш ----------

        /// <summary>
        /// Вес в бюджете крупных меток: нырок — 1 с начала ухода до выхода
        /// (круг ляжет на 42-м тике, место бронируется сразу), прорастание и
        /// ливень — 2 до последнего удара, пыльца — 1 до падения, буря — весь
        /// бюджет (не меньше 4) до второй волны (Simulation.ForestBoss.Storm.cs).
        /// </summary>
        partial void ThicketMarkWeightExtra(int id, ref int weight, ref int start, ref int impact)
        {
            var a = _thicketMasters[id];
            if (a.HitResolved) return;
            switch (a.Action)
            {
                case ThicketMasterAction.Dive:
                case ThicketMasterAction.Pollen:
                    weight = 1; start = a.StartTick; impact = a.ImpactTick;
                    return;
                case ThicketMasterAction.Sprout:
                case ThicketMasterAction.Rain:
                    weight = ThicketCastMarkWeight; start = a.StartTick; impact = a.ImpactTick;
                    return;
                case ThicketMasterAction.Storm:
                    weight = ThicketStormWeight; start = a.StartTick; impact = a.ImpactTick;
                    return;
            }
        }

        /// <summary>Контакты в такт ударов: выход из нырка, каждый круг прорастания, каждый залп ливня (вставшие и будущие).</summary>
        partial void ThicketContactsExtra(int id)
        {
            var a = _thicketMasters[id];
            switch (a.Action)
            {
                case ThicketMasterAction.Dive:
                    if (!a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                    return;
                case ThicketMasterAction.Sprout:
                case ThicketMasterAction.Rain:
                {
                    bool rain = a.Action == ThicketMasterAction.Rain;
                    int size = rain ? ThicketRainCircles : 1;
                    int from = id * ThicketShapeSlots;
                    for (int k = 0; k < a.Stage; k++)
                    {
                        int slot = from + k * size;
                        if (ThicketShapeState[slot] == ThicketShapePending && ThicketShapeImpact[slot] >= Tick)
                            AddHeroContact(id, ThicketShapeImpact[slot], ThicketShapeImpact[slot]);
                    }
                    int every = rain ? ThicketRainEveryTicks : ThicketSproutEveryTicks;
                    int delay = rain ? ThicketRainImpactTicks : ThicketSproutImpactTicks;
                    for (int k = a.Stage; k < a.Stages; k++)
                    {
                        int impact = a.StageStartTick + every * (k - a.Stage) + delay;
                        AddHeroContact(id, impact, impact);
                    }
                    return;
                }
                case ThicketMasterAction.Storm:
                    AddThicketStormContacts(id, a);
                    return;
            }
        }

        /// <summary>Часы: ещё не сработавшие круги каста и пыльца этого босса ждут вместе с ним.</summary>
        partial void ThicketHourglassExtra(int id, int ticks)
        {
            if (_thicketShapeState != null)
            {
                int from = id * ThicketShapeSlots;
                for (int k = from; k < from + ThicketShapeSlots; k++)
                    if (_thicketShapeState[k] == ThicketShapePending && _thicketShapeImpact[k] >= Tick) _thicketShapeImpact[k] += ticks;
            }
            DelayThicketPollen(id, ticks);
        }

        partial void ThicketResetExtra()
        {
            if (_thicketShapeState != null)
            {
                Array.Clear(_thicketShapeState, 0, _thicketShapeState.Length);
                Array.Clear(_thicketShapeSerial, 0, _thicketShapeSerial.Length);
                Array.Clear(_thicketShapeImpact, 0, _thicketShapeImpact.Length);
                Array.Clear(_thicketShapeCenter, 0, _thicketShapeCenter.Length);
            }
            ResetThicketPollen();
        }

        partial void ThicketHashExtra(ref ulong hash)
        {
            if (_thicketShapeState != null)
            {
                Hashing.Mix(ref hash, 0x54485348); // "THSH"
                for (int id = 1; id < Entities.Count; id++)
                {
                    if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                    int from = id * ThicketShapeSlots;
                    for (int k = from; k < from + ThicketShapeSlots; k++)
                    {
                        Hashing.Mix(ref hash, (int)_thicketShapeState[k]);
                        if (_thicketShapeState[k] == ThicketShapeEmpty) continue;
                        Hashing.Mix(ref hash, _thicketShapeSerial[k]); Hashing.Mix(ref hash, _thicketShapeImpact[k]);
                        Hashing.Mix(ref hash, _thicketShapeCenter[k].X); Hashing.Mix(ref hash, _thicketShapeCenter[k].Y);
                    }
                }
            }
            HashThicketPollen(ref hash);
        }
    }
}
