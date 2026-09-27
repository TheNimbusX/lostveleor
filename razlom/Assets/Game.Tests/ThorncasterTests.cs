using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Шипомёт (план новых мобов от 26.09): линия из четырёх сегментов с шипами
    /// на 27/33/39/45 тиках, одно попадание за линию, препятствия обрезают
    /// линию, оглушение и смерть снимают несработавшее, 30 тиков стойки после
    /// последнего шипа, всплеск против объятий, глубина, детерминизм. Выстрел
    /// шипом (требование владельца от 26.09): без метки на земле («просто
    /// проджектайл, от которого можно увернуться»), выпуск на 21, полёт 0,6 м
    /// за тик, одно попадание и только там, где шип пролетел, шаг вбок спасает,
    /// снаряд переживает смерть стрелка, приоритеты, перезарядка 60.
    /// </summary>
    public sealed class ThorncasterTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Стенд вида: герой в нуле лицом по +X, Шипомёт в 6 м по оси и смотрит на героя.</summary>
        private static Simulation Arena(int count = 1, LayoutMap map = null, int arena = 1, int hard = 100,
            double distance = 6)
        {
            var sim = new Simulation(61, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThorncaster, count, map, 61, arena, hard, Fix64.FromDouble(distance));
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0); sim.Entities.Health[0] = 10000;
            return sim;
        }

        private static void Until(Simulation sim, int tick) { while (sim.Tick < tick) sim.Step(InputFrame.Empty); }

        /// <summary>Метки источника source в пуле, по возрастанию номера.</summary>
        private static List<EnemyTelegraph> Marks(Simulation sim, int source)
        {
            var list = new List<EnemyTelegraph>();
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.Source == source) list.Add(t);
            list.Sort((x, y) => x.Serial.CompareTo(y.Serial));
            return list;
        }

        private static EnemyTelegraph Mark(Simulation sim, int serial)
        {
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.Serial == serial) return t;
            return default;
        }

        /// <summary>Точка на оси линии: along метров от тела, lateral — вбок.</summary>
        private static FixVec2 OnLine(in ThorncasterState a, double along, double lateral = 0)
        {
            var side = new FixVec2(-a.Direction.Y, a.Direction.X);
            return a.Origin + a.Direction * Fix64.FromDouble(along) + side * Fix64.FromDouble(lateral);
        }

        /// <summary>Середина сегмента k вдоль линии.</summary>
        private static double SegmentCenter(int k)
            => (Simulation.ThornLineStartOffset + Simulation.ThornSegmentLength * k
                + Simulation.ThornSegmentLength / 2).ToDouble();

        /// <summary>
        /// Шагает до тика until и записывает контакты: тик, номер шипа, попал ли,
        /// и тики, в которые Шипомёт ранил героя.
        /// </summary>
        private static void Run(Simulation sim, int until, List<int> impactTicks, List<int> stages, List<bool> hits,
            List<int> damageTicks)
        {
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.EnemyActionImpact && e.Source == 1)
                    { impactTicks?.Add(tick); stages?.Add(e.Amount); hits?.Add(e.Flag); }
                    if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == Simulation.PlayerId)
                        damageTicks?.Add(tick);
                }
            }
        }

        // ---- линия ----

        [Test]
        public void LineOpensFourTouchingSharedLanesAtStart()
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var a), Is.True);
            Assert.That(a.Action, Is.EqualTo(ThornAction.Line));
            Assert.That(a.StartTick, Is.EqualTo(0));
            Assert.That(a.Segments, Is.EqualTo(Simulation.ThornLineSegments));
            Assert.That(FixVec2.Distance(a.Direction, new FixVec2(-Fix64.One, Fix64.Zero)).ToDouble(), Is.LessThan(1e-4),
                "линия по взгляду на героя");
            Assert.That(sim.Entities.Facing[1], Is.EqualTo(a.Direction));
            Assert.That(a.ImpactTick - a.StartTick, Is.EqualTo(Simulation.ThornLineLastImpactTicks));
            Assert.That(sim.ThorncasterHoldsBigToken(1), Is.True);

            var lanes = Marks(sim, 1);
            Assert.That(lanes.Count, Is.EqualTo(4), "все четыре сегмента открыты сразу");
            for (int k = 0; k < lanes.Count; k++)
            {
                var t = lanes[k];
                Assert.That(t.Serial, Is.EqualTo(a.FirstTelegraphSerial + k), "номера подряд");
                Assert.That(t.Shape, Is.EqualTo(TelegraphShape.Lane));
                Assert.That(t.SharedView, Is.True, "рисует общий вид");
                Assert.That(t.IsActive, Is.True);
                Assert.That(t.ImpactTick, Is.EqualTo(27 + 6 * k));
                Assert.That(t.Length, Is.EqualTo(Fix64.Ratio(7, 4)));
                Assert.That(t.Width, Is.EqualTo(Fix64.Ratio(7, 5)));
                Assert.That(t.Direction, Is.EqualTo(a.Direction));
                double start = 1 + 1.75 * k;
                Assert.That(FixVec2.Distance(t.Origin, OnLine(a, start)).ToDouble(), Is.LessThan(1e-3),
                    "сегмент " + k + " начинается в " + start + " м перед телом");
                if (k > 0)
                {
                    var previousEnd = lanes[k - 1].Origin + lanes[k - 1].Direction * lanes[k - 1].Length;
                    Assert.That(FixVec2.Distance(previousEnd, t.Origin).ToDouble(), Is.LessThan(1e-3), "сегменты стыкуются");
                }
            }

            int started = 0, opened = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type == SimEventType.EnemyActionStarted && e.Source == 1)
                {
                    started++;
                    Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornLine));
                    Assert.That(e.Target, Is.EqualTo(Simulation.PlayerId));
                }
                if (e.Type == SimEventType.TelegraphOpened && e.Source == 1 && e.Flag) opened++;
                Assert.That(e.Type, Is.Not.EqualTo(SimEventType.Attack), "Шипомёт не бьёт общим ударом");
            }
            Assert.That(started, Is.EqualTo(1));
            Assert.That(opened, Is.EqualTo(4));
            Assert.That(sim.TryGetEnemySwing(1, out _), Is.False);
        }

        [Test]
        public void SpikesEruptAt27_33_39_45_AndTheLineFillsOutward()
        {
            var sim = Arena();
            var ticks = new List<int>(); var stages = new List<int>(); var damage = new List<int>();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            Run(sim, 28, ticks, stages, null, damage);
            // В тик первого шипа сработал только ближний сегмент, дальние ещё заполняются.
            var lanes = Marks(sim, 1);
            Assert.That(Mark(sim, a.FirstTelegraphSerial).State, Is.EqualTo(TelegraphState.Resolved));
            for (int k = 1; k < 4; k++)
                Assert.That(Mark(sim, a.FirstTelegraphSerial + k).State, Is.EqualTo(TelegraphState.Active), "сегмент " + k);
            Assert.That(lanes.Count, Is.EqualTo(4));

            Run(sim, 80, ticks, stages, null, damage);
            Assert.That(ticks, Is.EqualTo(new[] { 27, 33, 39, 45 }));
            Assert.That(stages, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            // Герой в нуле стоит на стыке третьего и четвёртого сегментов: удар один, третьим шипом.
            Assert.That(damage, Is.EqualTo(new[] { 39 }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - EnemyArchetypes.ThorncasterSpikeDamage));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void HitsOnlyWhereTheDrawnSegmentStands(int segment)
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Середина сегмента: тело героя (0,45 м) целиком внутри него и не задевает соседей.
            sim.Entities.Position[0] = OnLine(a, SegmentCenter(segment));
            var hits = new List<bool>(); var damage = new List<int>();
            Run(sim, a.EndTick, null, null, hits, damage);
            Assert.That(hits.Count, Is.EqualTo(4));
            for (int k = 0; k < 4; k++) Assert.That(hits[k], Is.EqualTo(k == segment), "шип " + k);
            Assert.That(damage, Is.EqualTo(new[] { 27 + 6 * segment }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 30));
        }

        [TestCase(true)] [TestCase(false)]
        public void SideEdgeIsTheDrawnEdge(bool inside)
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Плечо героя на 5 см внутри нарисованной кромки второго сегмента — или на 5 см снаружи.
            double edge = (Simulation.ThornSegmentWidth / 2 + sim.Entities.BodyRadius[0]).ToDouble();
            sim.Entities.Position[0] = OnLine(a, SegmentCenter(1), inside ? edge - 0.05 : edge + 0.05);
            var damage = new List<int>();
            Run(sim, a.EndTick, null, null, null, damage);
            Assert.That(damage, Is.EqualTo(inside ? new[] { 33 } : new int[0]));
        }

        [Test]
        public void OneHitPerCast_EvenOnTheSeamOfTwoSegments()
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Стык второго и третьего сегментов: 1 + 1,75 × 2 = 4,5 м от тела.
            sim.Entities.Position[0] = OnLine(a, 4.5);
            var stages = new List<int>(); var hits = new List<bool>(); var damage = new List<int>();
            Run(sim, a.EndTick, null, stages, hits, damage);
            Assert.That(stages, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(hits, Is.EqualTo(new[] { false, true, false, false }), "третий шип героя не бьёт повторно");
            Assert.That(damage, Is.EqualTo(new[] { 33 }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 30));
        }

        [Test]
        public void SideStepEscapes_AndTheLineStaysLocked()
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Два метра вбок — за кромкой (0,7 + 0,45 м): линия за героем не поворачивает.
            sim.Entities.Position[0] = At(0, 2);
            var hits = new List<bool>(); var damage = new List<int>();
            while (sim.Tick < a.EndTick)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Facing[1], Is.EqualTo(a.Direction), "взгляд зафиксирован, тик " + tick);
                Assert.That(sim.Entities.Position[1], Is.EqualTo(a.Origin), "стоит на месте, тик " + tick);
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.EnemyActionImpact && e.Source == 1) hits.Add(e.Flag);
                    if (e.Type == SimEventType.Damage && e.Source == 1) damage.Add(tick);
                }
            }
            Assert.That(hits, Is.EqualTo(new[] { false, false, false, false }));
            Assert.That(damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
            foreach (var t in Marks(sim, 1)) Assert.That(t.Direction, Is.EqualTo(a.Direction));
        }

        /// <summary>Поляна 40×40 м; rock — камень на линии Шипомёта (6,0) → герой (0,0).</summary>
        private static LayoutMap Glade(double rockX = double.NaN, double rockRadius = 0.5)
        {
            var room = new ModuleDefinition("thorncaster.test", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
            if (!double.IsNaN(rockX))
                map.AddTestObstacle(new LayoutObstacle(At(rockX, 0), Fix64.FromDouble(rockRadius), 0));
            return map;
        }

        [Test]
        public void ObstacleClipsTheLine_AndOnlyDrawnSegmentsExist()
        {
            // Без камня на той же поляне — все четыре сегмента, герой в нуле получает шип.
            var open = Arena(map: Glade());
            open.Step(InputFrame.Empty);
            Assert.That(open.TryGetThorncasterAction(1, out var full), Is.True);
            Assert.That(full.Segments, Is.EqualTo(4));

            // Камень в 2,5 м от героя стоит на втором сегменте: линия кончается на первом.
            var sim = Arena(map: Glade(2.5));
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var a), Is.True);
            Assert.That(a.Segments, Is.EqualTo(1));
            Assert.That(a.ImpactTick - a.StartTick, Is.EqualTo(Simulation.ThornLineWindupTicks), "последний шип — первый");
            Assert.That(a.EndTick - a.ImpactTick, Is.EqualTo(Simulation.ThornLineRecoveryTicks));
            Assert.That(Marks(sim, 1).Count, Is.EqualTo(1), "за камнем меток нет");
            var ticks = new List<int>(); var damage = new List<int>();
            Run(sim, a.EndTick, ticks, null, null, damage);
            Assert.That(ticks, Is.EqualTo(new[] { 27 }));
            Assert.That(damage, Is.Empty, "где сегмент не нарисован, там и не бьёт");
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        /// <summary>
        /// Шагает до until и следит, что Шипомёт не пятится: дальше, чем
        /// встал, от героя не бывает, и за тик не отходит больше чем на
        /// сантиметр — это скольжение вбок вдоль камня (шаг — 7 см), а не отход.
        /// Пишет тики начала линий, сами линии и тики урона по герою; after —
        /// своя проверка после каждого шага (номер шага).
        /// </summary>
        private static void RunNeverRetreating(Simulation sim, int until, List<int> lineTicks,
            List<ThorncasterState> lines, List<int> damageTicks, System.Action<int> after = null)
        {
            double previous = FixVec2.Distance(sim.Entities.Position[1], sim.Entities.Position[0]).ToDouble();
            double start = previous;
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.EnemyActionStarted && e.Source == 1
                        && e.ActionVariant == (int)EnemyActionKind.ThornLine)
                    {
                        lineTicks.Add(tick);
                        sim.TryGetThorncasterAction(1, out var a); lines.Add(a);
                    }
                    if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == Simulation.PlayerId)
                        damageTicks.Add(tick);
                }
                double distance = FixVec2.Distance(sim.Entities.Position[1], sim.Entities.Position[0]).ToDouble();
                Assert.That(distance, Is.LessThanOrEqualTo(previous + 0.01), "отступил, тик " + tick);
                Assert.That(distance, Is.LessThanOrEqualTo(start + 1e-6), "дальше, чем встал, тик " + tick);
                previous = distance;
                after?.Invoke(tick);
            }
        }

        [Test]
        public void RockAtItsFeet_NoLineIntoTheRock_StepsAsideAndShoots()
        {
            // Камень между телом и началом первого сегмента: отсюда линии нет
            // вовсе, жетон не взят. Но и столбом он не стоит, пока герой в 6 м
            // бьёт издали: обходит камень, не пятясь, и стреляет с чистой земли.
            var sim = Arena(map: Glade(4.7, 0.3));
            var spawn = sim.Entities.Position[1];
            var ticks = new List<int>(); var lines = new List<ThorncasterState>(); var damage = new List<int>();
            RunNeverRetreating(sim, 900, ticks, lines, damage, tick =>
            {
                if (!sim.Entities.Position[1].Equals(spawn)) return;
                Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False, "линия в камень, тик " + tick);
                Assert.That(Marks(sim, 1), Is.Empty);
            });
            Assert.That(lines, Is.Not.Empty, "обошёл камень и выстрелил");
            Assert.That(ticks[0], Is.LessThan(60), "обход — дело секунды-двух, не стойка до конца арены");
            foreach (var a in lines) Assert.That(a.Origin, Is.Not.EqualTo(spawn), "линия не с места у камня");
            Assert.That(damage, Is.Not.Empty, "с чистой земли линия достаёт героя");
        }

        [Test]
        public void HeroBehindARock_TheNextLineGoesAroundIt()
        {
            // Герой за камнем: первая линия упирается в камень одним сегментом.
            // Пока она перезаряжается, Шипомёт обходит камень и следующей
            // линией достаёт героя — за камнем не отсидеться всю арену.
            var sim = Arena(map: Glade(2.5));
            var ticks = new List<int>(); var lines = new List<ThorncasterState>(); var damage = new List<int>();
            RunNeverRetreating(sim, 400, ticks, lines, damage);
            Assert.That(ticks[0], Is.EqualTo(0));
            Assert.That(lines[0].Segments, Is.EqualTo(1), "первая линия — в камень");
            Assert.That(ticks.Count, Is.GreaterThanOrEqualTo(2));
            // Линия готова к 150. Обойдя камень, он стреляет шипом (выстрел — между
            // линиями), и начатый выстрел доигрывается: линия ждёт не дольше его замаха и стойки.
            Assert.That(ticks[1], Is.InRange(Simulation.ThornLineCooldownTicks, Simulation.ThornLineCooldownTicks
                + Simulation.ThornShotWindupTicks + Simulation.ThornShotRecoveryTicks), "к перезарядке уже обошёл");
            Assert.That(lines[1].Segments, Is.GreaterThan(1));
            Assert.That(damage, Is.Not.Empty);
            Assert.That(damage[0], Is.GreaterThan(lines[0].EndTick), "первая линия героя не задела");
        }

        [TestCase(2.65)] [TestCase(2.8)] [TestCase(2.95)]
        public void NoDeadBandBetweenBurstAndLine(double distance)
        {
            // Между всплеском (до 2,6 м) и линией нет полосы, где Шипомёт стоит
            // без атаки: герой, вставший в 2,6–3 м, получает линию каждые 150 тиков.
            var sim = Arena(distance: distance);
            var spot = sim.Entities.Position[1];
            var ticks = new List<int>(); var lines = new List<ThorncasterState>(); var damage = new List<int>();
            RunNeverRetreating(sim, 600, ticks, lines, damage);
            Assert.That(ticks, Is.EqualTo(new[] { 0, 150, 300, 450 }));
            Assert.That(damage.Count, Is.EqualTo(4), "каждая линия попадает");
            Assert.That(sim.Entities.Position[1], Is.EqualTo(spot), "ближе 5 м стоит");
        }

        // ---- снятие и стойка ----

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void StunOrDeathCancelsTheSpikesNotYetErupted(int reason)
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Герой на дальнем сегменте: без снятия его ударил бы четвёртый шип.
            sim.Entities.Position[0] = OnLine(a, SegmentCenter(3));
            Until(sim, 34);
            if (reason == 0) sim.Statuses.ApplyStun(1, 1000);
            else if (reason == 1) sim.Entities.Alive[1] = false;
            else sim.ApplyAbilityDamage(Simulation.PlayerId, 1, 1000000, -1, DamageType.Physical);
            sim.Step(InputFrame.Empty);

            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False);
            int cancelled = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionCancelled && e.Source == 1)
                {
                    cancelled++;
                    Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornLine));
                }
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(Mark(sim, a.FirstTelegraphSerial + 1).State, Is.EqualTo(TelegraphState.Resolved), "сработавший доживает вспышку");
            Assert.That(Mark(sim, a.FirstTelegraphSerial + 2).State, Is.EqualTo(TelegraphState.Cancelled));
            Assert.That(Mark(sim, a.FirstTelegraphSerial + 3).State, Is.EqualTo(TelegraphState.Cancelled));
            Assert.That(sim.ThorncasterHoldsBigToken(1), Is.False);

            var ticks = new List<int>(); var damage = new List<int>();
            Run(sim, 80, ticks, null, null, damage);
            Assert.That(ticks, Is.Empty);
            Assert.That(damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [Test]
        public void StandsFrozenForThirtyTicksAfterTheLastSpike()
        {
            var sim = Arena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            Assert.That(a.ImpactTick, Is.EqualTo(45));
            Assert.That(a.EndTick - a.ImpactTick, Is.EqualTo(30));
            Until(sim, a.ImpactTick + 1);
            // Герой отбежал на 12 м: без стойки Шипомёт сразу пошёл бы следом.
            sim.Entities.Position[0] = At(-6, 0);
            var frozen = sim.Entities.Position[1];
            while (sim.Tick < a.EndTick)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Position[1], Is.EqualTo(frozen), "шаг в стойке, тик " + tick);
                Assert.That(sim.TryGetThorncasterAction(1, out _), Is.True, "тик " + tick);
            }
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False, "стойка кончилась");
            Assert.That(sim.Entities.Position[1], Is.EqualTo(frozen), "ход этого тика ещё в стойке");
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[1], Is.Not.EqualTo(frozen), "после стойки идёт к герою");
        }

        // ---- всплеск ----

        [Test]
        public void BurstWhenHugged_CircleAroundItself_Every90Ticks()
        {
            var sim = Arena(distance: 2);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var a), Is.True);
            Assert.That(a.Action, Is.EqualTo(ThornAction.Burst));
            Assert.That(a.ImpactTick - a.StartTick, Is.EqualTo(21));
            Assert.That(a.EndTick - a.ImpactTick, Is.EqualTo(15));
            Assert.That(sim.ThorncasterHoldsBigToken(1), Is.False, "всплеск жетона не берёт");
            var marks = Marks(sim, 1);
            Assert.That(marks.Count, Is.EqualTo(1));
            Assert.That(marks[0].Shape, Is.EqualTo(TelegraphShape.Circle));
            Assert.That(marks[0].Radius, Is.EqualTo(Fix64.Ratio(12, 5)));
            Assert.That(marks[0].Origin, Is.EqualTo(sim.Entities.Position[1]));
            Assert.That(marks[0].SharedView, Is.True);
            bool started = false;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionStarted && e.Source == 1)
                { started = true; Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornBurst)); }
            Assert.That(started, Is.True);

            var ticks = new List<int>(); var hits = new List<bool>(); var damage = new List<int>();
            Run(sim, 40, ticks, null, hits, damage);
            Assert.That(ticks, Is.EqualTo(new[] { 21 }));
            Assert.That(hits, Is.EqualTo(new[] { true }));
            Assert.That(damage, Is.EqualTo(new[] { 21 }));
            Assert.That(sim.ThornBurstDamageOf(1), Is.EqualTo(22));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 22));

            // Перезарядка 90 от начала. Линии вплотную нет (она с 2,6 м), и назад он не шагает.
            var stand = sim.Entities.Position[1];
            while (sim.Tick < 90)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False, "тик " + (sim.Tick - 1));
                Assert.That(sim.Entities.Position[1], Is.EqualTo(stand), "не отступает, тик " + (sim.Tick - 1));
            }
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var second), Is.True);
            Assert.That(second.Action, Is.EqualTo(ThornAction.Burst));
            Assert.That(second.StartTick, Is.EqualTo(90));
        }

        [Test]
        public void BurstIgnoresTheBigToken()
        {
            var sim = Arena(2);
            // Второй Шипомёт вплотную к герою; первый возьмёт единственный крупный жетон линией.
            sim.Entities.Position[2] = At(0, 2);
            sim.Entities.Facing[2] = new FixVec2(Fix64.Zero, -Fix64.One);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.BigAttackTokenLimit, Is.EqualTo(1));
            Assert.That(sim.TryGetThorncasterAction(1, out var line), Is.True);
            Assert.That(line.Action, Is.EqualTo(ThornAction.Line));
            Assert.That(sim.TryGetThorncasterAction(2, out var burst), Is.True);
            Assert.That(burst.Action, Is.EqualTo(ThornAction.Burst));
        }

        // ---- жетон, глубина, походка, детерминизм ----

        [TestCase(1, 46)]
        [TestCase(2, 0)]
        public void LineHoldsTheBigTokenUntilItsLastSpike(int limit, int secondStart)
        {
            var sim = Arena(2);
            sim.BigAttackTokenLimit = limit;
            int second = -1;
            for (int t = 0; t < 60 && second < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                // Без жетона второй не стоит — стреляет шипом; здесь важна его линия.
                if (sim.TryGetThorncasterAction(2, out var b) && b.Action == ThornAction.Line) second = b.StartTick;
            }
            Assert.That(sim.TryGetThorncasterAction(1, out var first), Is.True);
            Assert.That(first.StartTick, Is.EqualTo(0), "младший индекс берёт жетон первым");
            // Одна крупная атака (А1–4): второй ждёт до тика после последнего шипа первого.
            Assert.That(second, Is.EqualTo(secondStart));
            Assert.That(sim.TryGetThorncasterAction(2, out var line), Is.True);
            Assert.That(line.Action, Is.EqualTo(ThornAction.Line));
        }

        [Test]
        public void DepthAndHardRouteScaleTheSpikeAndTheBurst()
        {
            int hard = EnemyArchetypes.HardRoutePercent;
            var sim = Arena(arena: 8, hard: hard);
            Assert.That(sim.Entities.MaxHealth[1], Is.EqualTo(EnemyArchetypes.ScaleHealth(EnemyArchetypes.ThorncasterHealth,
                EnemyArchetypes.DepthHealthPercent(8), hard)));
            int spike = sim.ThornSpikeDamageOf(1);
            double expected = 30.0 * EnemyArchetypes.DepthDamagePercent(8) * hard / 10000.0;
            Assert.That(spike, Is.EqualTo(sim.Entities.Damage[1]));
            Assert.That(spike, Is.EqualTo(expected).Within(0.51));
            Assert.That(spike, Is.GreaterThan(30));
            Assert.That(sim.ThornBurstDamageOf(1), Is.EqualTo(EnemyArchetypes.Share(spike, 22, 30)));

            // Настоящий шип бьёт ровно уроном листа: герой в нуле — третий шип.
            var damage = new List<int>();
            Run(sim, 60, null, null, null, damage);
            Assert.That(damage, Is.EqualTo(new[] { 39 }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - spike));
        }

        private static double Degrees(FixVec2 a, FixVec2 b)
        {
            double ax = a.X.ToDouble(), ay = a.Y.ToDouble(), bx = b.X.ToDouble(), by = b.Y.ToDouble();
            double dot = (ax * bx + ay * by) / (System.Math.Sqrt(ax * ax + ay * ay) * System.Math.Sqrt(bx * bx + by * by));
            return System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, dot))) * 180.0 / System.Math.PI;
        }

        [Test]
        public void WalksAlongItsGaze_StopsAtFiveMetres_NeverRetreats()
        {
            // Спиной к герою в 12 м: сначала разворот, шаг — только вдоль взгляда.
            var sim = Arena(distance: 12);
            sim.Entities.Facing[1] = new FixVec2(Fix64.One, Fix64.Zero);
            double previous = FixVec2.Distance(sim.Entities.Position[1], sim.Entities.Position[0]).ToDouble();
            bool lined = false;
            for (int t = 0; t < 400; t++)
            {
                var before = sim.Entities.Position[1];
                var toHero = sim.Entities.Position[0] - before;
                sim.Step(InputFrame.Empty);
                var moved = sim.Entities.Position[1] - before;
                double distance = FixVec2.Distance(sim.Entities.Position[1], sim.Entities.Position[0]).ToDouble();
                Assert.That(distance, Is.LessThanOrEqualTo(previous + 1e-6), "отступил, тик " + t);
                previous = distance;
                if (sim.TryGetThorncasterAction(1, out var a) && a.Action == ThornAction.Line) lined = true;
                if (moved.LengthSq == Fix64.Zero) continue;
                Assert.That(Degrees(moved, sim.Entities.Facing[1]), Is.LessThan(0.5), "боком, тик " + t);
                Assert.That(FixVec2.Dot(sim.Entities.Facing[1], toHero.Normalized()).ToDouble(),
                    Is.GreaterThan(Simulation.ThorncasterWalkAlignFrom.ToDouble()), "шаг до разворота, тик " + t);
            }
            Assert.That(lined, Is.True, "по дороге стреляет линией");
            Assert.That(previous, Is.InRange(4.8, 5.0), "подходит на 5–6 м и встаёт");
        }

        [Test]
        public void StateHashIsIdenticalRunToRun_AndSetupClearsActions()
        {
            var spots = new[] { At(0, 0), At(-2, 3), At(1, -1), At(4.5, 0.5), At(-3, -4) };
            var a = Arena(2); var b = Arena(2);
            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()));
            for (int t = 0; t < 600; t++)
            {
                if (t % 60 == 0)
                {
                    a.Entities.Position[0] = spots[t / 60 % spots.Length];
                    b.Entities.Position[0] = spots[t / 60 % spots.Length];
                }
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "тик " + t);
            }

            a.SetupKindTestArena(EnemyKind.ForestThorncaster);
            Assert.That(a.TryGetThorncasterAction(1, out _), Is.False);
            Assert.That(Marks(a, 1), Is.Empty);
            // Новая расстановка — новый Шипомёт: линия готова сразу.
            a.Step(InputFrame.Empty);
            Assert.That(a.TryGetThorncasterAction(1, out var fresh), Is.True);
            Assert.That(fresh.Action, Is.EqualTo(ThornAction.Line));
        }

        // ---- выстрел шипом ----

        /// <summary>Стенд выстрела: герой в 9 м — дальше линии (8,5 м), ближе предела выстрела (10 м).</summary>
        private static Simulation ShotArena(LayoutMap map = null, int arena = 1, int hard = 100)
            => Arena(map: map, arena: arena, hard: hard, distance: 9);

        /// <summary>
        /// Шип выстрела с тика 0 точно упал: выпуск на 21, 10 м — 17 тиков полёта.
        /// Следующее действие начинается не раньше 33-го и бьёт не раньше 54-го
        /// (всплеск), так что до этого тика весь урон — от выстрела.
        /// </summary>
        private const int ShotOver = Simulation.ThornShotWindupTicks + 17 + 1;

        /// <summary>Точка на пути шипа выстрела a: along м от НАЧАЛА ПУТИ (0,8 м перед телом), lateral — вбок.</summary>
        private static FixVec2 OnShotPath(in ThorncasterState a, double along, double lateral = 0)
            => OnLine(a, Simulation.ThornShotStartOffset.ToDouble() + along, lateral);

        /// <summary>
        /// Тик попадания в героя на оси пути в along м от его начала: шип
        /// выпущен в release и за тик пролетает 0,6 м; тело задето, когда
        /// остриё подходит к центру героя на радиус тела — впереди острия
        /// толщины шипа нет.
        /// </summary>
        private static int ShotHitTick(int release, double along, double body)
        {
            double speed = Simulation.ThornShotSpeed.ToDouble();
            return release + System.Math.Max(0, (int)System.Math.Ceiling((along - body) / speed) - 1);
        }

        private sealed class ShotLog
        {
            public readonly List<int> Impacts = new List<int>(), Damage = new List<int>(), Amounts = new List<int>();
            public readonly List<int> Launches = new List<int>(), Opened = new List<int>();
            public readonly List<bool> Hits = new List<bool>();
            public readonly List<FixVec2> Stops = new List<FixVec2>();
        }

        /// <summary>
        /// Шагает до until и пишет выпуски и остановки шипа Шипомёта 1 (тик,
        /// попал ли, где встал), урон по герою от него и открытые им метки.
        /// before — своя правка перед шагом (тик).
        /// </summary>
        private static ShotLog RunShot(Simulation sim, int until, System.Action<int> before = null, ShotLog log = null)
        {
            log = log ?? new ShotLog();
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                before?.Invoke(tick);
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Source != 1) continue;
                    if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.ThornShot)
                    { log.Impacts.Add(tick); log.Hits.Add(e.Flag); log.Stops.Add(e.Position); }
                    if (e.Type == SimEventType.EnemyProjectileLaunched) log.Launches.Add(tick);
                    if (e.Type == SimEventType.TelegraphOpened) log.Opened.Add(tick);
                    if (e.Type == SimEventType.Damage && e.Target == Simulation.PlayerId)
                    { log.Damage.Add(tick); log.Amounts.Add(e.Amount); }
                }
            }
            return log;
        }

        [Test]
        public void ShotOpensNoMark_TheThornItselfIsTheWarning()
        {
            // Владелец, 26.09: «не делай на земле видимую траекторию, просто
            // проджектайл, от которого можно увернуться». Ни общей метки, ни своей.
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var a), Is.True);
            Assert.That(a.Action, Is.EqualTo(ThornAction.Shot));
            Assert.That(a.StartTick, Is.EqualTo(0));
            Assert.That(a.ImpactTick, Is.EqualTo(Simulation.ThornShotWindupTicks), "выпуск на 21");
            Assert.That(a.EndTick - a.ImpactTick, Is.EqualTo(Simulation.ThornShotRecoveryTicks));
            Assert.That(a.NextShotTick, Is.EqualTo(Simulation.ThornShotCooldownTicks));
            Assert.That(a.NextLineTick, Is.EqualTo(0), "линию выстрел не трогает");
            Assert.That(a.FirstTelegraphSerial, Is.Zero, "метки у выстрела нет");
            Assert.That(a.Segments, Is.EqualTo(1));
            Assert.That(FixVec2.Distance(a.Direction, new FixVec2(-Fix64.One, Fix64.Zero)).ToDouble(), Is.LessThan(1e-4));
            Assert.That(sim.Entities.Facing[1], Is.EqualTo(a.Direction));
            Assert.That(sim.ThorncasterHoldsBigToken(1), Is.False, "выстрел — обычная атака, без жетона");
            Assert.That(sim.TryGetThornShot(1, out _), Is.False, "до выпуска шипа в воздухе нет");
            Assert.That(Marks(sim, 1), Is.Empty, "на земле ничего не встало");

            int started = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type == SimEventType.EnemyActionStarted && e.Source == 1)
                {
                    started++;
                    Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornShot));
                    Assert.That(e.Target, Is.EqualTo(Simulation.PlayerId));
                }
                Assert.That(e.Type, Is.Not.EqualTo(SimEventType.TelegraphOpened), "выстрел меток не открывает");
            }
            Assert.That(started, Is.EqualTo(1));

            // Стоит в замахе и смотрит по пути шипа, даже если герой ушёл вбок;
            // ни в замахе, ни в полёте метки нет.
            sim.Entities.Position[0] = At(0, 3);
            var log = RunShot(sim, a.EndTick, tick =>
            {
                Assert.That(Marks(sim, 1), Is.Empty, "метка, тик " + tick);
                if (tick == 0) return;
                Assert.That(sim.Entities.Position[1], Is.EqualTo(a.Origin), "стоит, тик " + tick);
                Assert.That(sim.Entities.Facing[1], Is.EqualTo(a.Direction), "взгляд зафиксирован, тик " + tick);
            });
            Assert.That(log.Opened, Is.Empty);
            Assert.That(log.Launches, Is.EqualTo(new[] { Simulation.ThornShotWindupTicks }));
        }

        [Test]
        public void ShotPathIsClippedByARock_AndEndsAtIt()
        {
            // Камень за героем: путь кончается у камня (та же проба, что у линии), а не через него.
            var open = ShotArena(Glade());
            open.Step(InputFrame.Empty);
            Assert.That(open.TryGetThorncasterAction(1, out var free), Is.True);
            Assert.That(free.Action, Is.EqualTo(ThornAction.Shot));
            Assert.That(open.ThornShotFlightLength(free.Origin, free.Direction), Is.EqualTo(Fix64.FromInt(10)), "без камня — 10 м");

            var sim = ShotArena(Glade(-1.2, 0.3));
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetThorncasterAction(1, out var a), Is.True);
            Assert.That(a.Action, Is.EqualTo(ThornAction.Shot), "путь до героя достаёт — выстрел есть");
            var length = sim.ThornShotFlightLength(a.Origin, a.Direction);
            Assert.That(length.ToDouble(), Is.LessThan(9.1), "обрезан камнем");
            // Проба 0,1 м упирается в камень 0,3 м с центром в −1,2: конец пути — у −0,8, с точностью до 1/32 м.
            double end = OnShotPath(a, length.ToDouble()).X.ToDouble();
            Assert.That(end, Is.InRange(-0.8 - 1e-3, -0.8 + 1.0 / 32 + 1e-3));

            // Шип летит ровно этим путём, долетает до героя и бьёт; за камень не летит.
            Until(sim, Simulation.ThornShotWindupTicks + 1);
            Assert.That(sim.TryGetThornShot(1, out var shot), Is.True);
            Assert.That(shot.Length, Is.EqualTo(length));
            var log = RunShot(sim, ShotOver);
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { EnemyArchetypes.ThorncasterShotDamage }));
        }

        [Test]
        public void RockBetween_NoShotIntoTheRock_EveryShotReachesTheHero()
        {
            // Камень между Шипомётом и героем: путь упёрся бы в камень за 4 м до
            // героя. Пустого выстрела в камень нет; шип летит, только когда его
            // путь дотягивается до тела героя — после обхода.
            var sim = Arena(map: Glade(4.5), distance: 9);
            var shots = new List<int>();
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Source != 1
                        || e.ActionVariant != (int)EnemyActionKind.ThornShot) continue;
                    shots.Add(t);
                    sim.TryGetThorncasterAction(1, out var a);
                    double reach = Simulation.ThornShotStartOffset.ToDouble()
                        + sim.ThornShotFlightLength(a.Origin, a.Direction).ToDouble()
                        + sim.Entities.BodyRadius[0].ToDouble();
                    double distance = FixVec2.Distance(a.Origin, sim.Entities.Position[0]).ToDouble();
                    Assert.That(reach, Is.GreaterThanOrEqualTo(distance - 1e-3), "выстрел в камень, тик " + t);
                }
            }
            Assert.That(shots, Is.Not.Empty, "обошёл камень и стреляет");
            Assert.That(shots[0], Is.GreaterThan(0), "с места у камня не стреляет");
        }

        [Test]
        public void ReleaseAtTick21_TheThornFliesPointSixMetresATick()
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            // Герой вне пути: шип летит до конца и никого не задевает.
            sim.Entities.Position[0] = At(0, 3);
            var launch = RunShot(sim, Simulation.ThornShotWindupTicks);
            Assert.That(launch.Launches, Is.Empty, "до 21 шипа нет");
            Assert.That(sim.TryGetThornShot(1, out _), Is.False);

            // Тик выпуска: событие для вида — номер выстрела и начало пути.
            sim.Step(InputFrame.Empty);
            int launched = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyProjectileLaunched) continue;
                launched++;
                Assert.That(e.Source, Is.EqualTo(1));
                Assert.That(e.Target, Is.EqualTo(Simulation.PlayerId));
                Assert.That(e.Amount, Is.EqualTo(a.Serial), "номер выстрела");
                Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornShot));
                Assert.That(FixVec2.Distance(e.Position, OnLine(a, 0.8)).ToDouble(), Is.LessThan(1e-3), "с 0,8 м перед телом");
            }
            Assert.That(launched, Is.EqualTo(1));

            for (int k = 0; k < Simulation.ThornShotFlightTicks(Fix64.FromInt(10)) - 1; k++)
            {
                if (k > 0) sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetThornShot(1, out var shot), Is.True, "полёт, тик " + (sim.Tick - 1));
                Assert.That(shot.Serial, Is.EqualTo(a.Serial));
                Assert.That(shot.ReleaseTick, Is.EqualTo(21));
                Assert.That(FixVec2.Distance(shot.Origin, OnLine(a, 0.8)).ToDouble(), Is.LessThan(1e-3), "из начала пути");
                Assert.That(shot.Direction, Is.EqualTo(a.Direction));
                Assert.That(shot.Length, Is.EqualTo(Fix64.FromInt(10)), "в поле — все 10 м");
                Assert.That(shot.Damage, Is.EqualTo(EnemyArchetypes.ThorncasterShotDamage));
                Assert.That(shot.Travelled, Is.EqualTo(Simulation.ThornShotSpeed * (k + 1)), "0,6 м за тик");
                Assert.That(Marks(sim, 1), Is.Empty, "в полёте метки нет");
            }
            Assert.That(Simulation.ThornShotFlightTicks(Fix64.FromInt(10)), Is.EqualTo(17), "10 м — 17 тиков");

            // Последний тик — до конца пути: шип падает там.
            var log = RunShot(sim, sim.Tick + 1);
            Assert.That(log.Impacts, Is.EqualTo(new[] { 21 + 16 }));
            Assert.That(log.Hits, Is.EqualTo(new[] { false }));
            Assert.That(FixVec2.Distance(log.Stops[0], OnShotPath(a, 10)).ToDouble(), Is.LessThan(1e-3), "упал в конце пути");
            Assert.That(sim.TryGetThornShot(1, out _), Is.False);
            Assert.That(log.Damage, Is.Empty);
        }

        [TestCase(0.5)] [TestCase(2.3)] [TestCase(5.1)] [TestCase(8.2)]
        public void HeroOnThePath_IsHitWhenTheThornReachesHim(double along)
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, along);
            double body = sim.Entities.BodyRadius[0].ToDouble();
            int expected = ShotHitTick(a.ImpactTick, along, body);

            var log = RunShot(sim, ShotOver);
            Assert.That(log.Impacts, Is.EqualTo(new[] { expected }), "шип летит 0,6 м за тик");
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Damage, Is.EqualTo(new[] { expected }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { EnemyArchetypes.ThorncasterShotDamage }), "14 на А1, без крита");
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 14));
            // Шип встал у тела, перед героем: остриё — на краю тела, не в центре.
            double stop = FixVec2.Distance(log.Stops[0], sim.Entities.Position[0]).ToDouble();
            Assert.That(stop, Is.InRange(body - 1e-3, body + 1e-3));
        }

        [Test]
        public void OneHit_TheThornStopsInTheHero()
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, 3.1);
            int hit = ShotHitTick(a.ImpactTick, 3.1, sim.Entities.BodyRadius[0].ToDouble());
            // Сразу после попадания герой отскакивает дальше по пути: застрявший шип второй раз не бьёт.
            var log = RunShot(sim, ShotOver, tick => { if (tick == hit + 1) sim.Entities.Position[0] = OnShotPath(a, 7.5); });
            Assert.That(log.Impacts, Is.EqualTo(new[] { hit }));
            Assert.That(log.Damage, Is.EqualTo(new[] { hit }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 14));
            Assert.That(sim.TryGetThornShot(1, out _), Is.False, "шипа больше нет");
        }

        [Test]
        public void HeroStepsAsideBeforeTheThornArrives_IsNotHit()
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, 8.2);
            int hit = ShotHitTick(a.ImpactTick, 8.2, sim.Entities.BodyRadius[0].ToDouble());
            Assert.That(hit, Is.EqualTo(33));
            // Шип уже летит (выпущен на 21), герою хватает короткого шага вбок до его прихода.
            double aside = Simulation.ThornShotRadius.ToDouble() + sim.Entities.BodyRadius[0].ToDouble() + 0.2;
            var log = RunShot(sim, ShotOver, tick => { if (tick == hit - 3) sim.Entities.Position[0] = OnShotPath(a, 8.2, aside); });
            Assert.That(log.Hits, Is.EqualTo(new[] { false }));
            Assert.That(log.Impacts, Is.EqualTo(new[] { 21 + 16 }), "долетел до конца пути");
            Assert.That(log.Damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [TestCase(10.5, 0.0)]   // за концом пути: остриё падает в 10 м, тело начинается в 10,05
        [TestCase(5.0, 0.71)]   // сбоку: плечо на сантиметр дальше толщины шипа (0,25 + 0,45)
        [TestCase(5.0, 0.78)]   // здесь прежняя полоса 0,7 м ещё била; шип — уже нет
        public void HeroOffTheThornsPath_IsNeverHit(double along, double lateral)
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, along, lateral);
            var log = RunShot(sim, ShotOver);
            Assert.That(log.Hits, Is.EqualTo(new[] { false }));
            Assert.That(log.Damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [Test]
        public void ShoulderTouchingTheThorn_IsHit()
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            double edge = Simulation.ThornShotRadius.ToDouble() + sim.Entities.BodyRadius[0].ToDouble();
            sim.Entities.Position[0] = OnShotPath(a, 5.0, edge - 0.02);
            var log = RunShot(sim, ShotOver);
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { 14 }));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void StunOrDeathBeforeRelease_NoThorn(int reason)
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, 5.1);
            Until(sim, 15);
            if (reason == 0) sim.Statuses.ApplyStun(1, 1000);
            else if (reason == 1) sim.Entities.Alive[1] = false;
            else sim.ApplyAbilityDamage(Simulation.PlayerId, 1, 1000000, -1, DamageType.Physical);
            sim.Step(InputFrame.Empty);

            Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False);
            int cancelled = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionCancelled && e.Source == 1)
                {
                    cancelled++;
                    Assert.That(e.ActionVariant, Is.EqualTo((int)EnemyActionKind.ThornShot));
                }
            Assert.That(cancelled, Is.EqualTo(1));

            var log = RunShot(sim, 80);
            Assert.That(sim.TryGetThornShot(1, out _), Is.False, "снятый замах шипа не выпускает");
            Assert.That(log.Launches, Is.Empty);
            Assert.That(log.Impacts, Is.Empty);
            Assert.That(log.Damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void AfterRelease_StunOrDeathOfTheCaster_DoesNotStopTheThorn(int reason)
        {
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, 5.1);
            int hit = ShotHitTick(a.ImpactTick, 5.1, sim.Entities.BodyRadius[0].ToDouble());
            Until(sim, a.ImpactTick + 2);
            Assert.That(sim.TryGetThornShot(1, out _), Is.True);
            if (reason == 0) sim.Statuses.ApplyStun(1, 1000);
            else if (reason == 1) sim.Entities.Alive[1] = false;
            else sim.ApplyAbilityDamage(Simulation.PlayerId, 1, 1000000, -1, DamageType.Physical);

            var log = RunShot(sim, ShotOver, tick =>
            {
                if (tick >= hit) return;
                Assert.That(sim.TryGetThornShot(1, out _), Is.True, "шип летит, тик " + tick);
            });
            Assert.That(log.Impacts, Is.EqualTo(new[] { hit }), "долетел, как без помехи");
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { 14 }), "урон снят при выпуске");
            Assert.That(sim.TryGetThornShot(1, out _), Is.False);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 14));
        }

        [Test]
        public void ShotOpensNoMark_LineWhileItsThornFlies()
        {
            // Стойка выстрела — 12 тиков после выпуска, полёт на 10 м — 17: линия
            // может встать, пока шип ещё летит. Общих полос у Шипомёта при этом
            // ровно четыре — линия; шип своей не добавляет (EnemyTelegraphTests).
            var sim = ShotArena();
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            Assert.That(a.Action, Is.EqualTo(ThornAction.Shot));
            // Герой в 6 м и на 20° от пути: шип пролетает мимо, линия — по новому взгляду.
            double angle = 20 * System.Math.PI / 180;
            var side = new FixVec2(-a.Direction.Y, a.Direction.X);
            sim.Entities.Position[0] = a.Origin + a.Direction * Fix64.FromDouble(6 * System.Math.Cos(angle))
                + side * Fix64.FromDouble(6 * System.Math.Sin(angle));
            int most = 0, lineStart = -1;
            bool lineWhileFlying = false;
            while (sim.Tick < 60)
            {
                sim.Step(InputFrame.Empty);
                if (lineStart < 0 && sim.TryGetThorncasterAction(1, out var line) && line.Action == ThornAction.Line)
                {
                    lineStart = line.StartTick;
                    lineWhileFlying = sim.TryGetThornShot(1, out _);
                }
                int lanes = 0;
                foreach (var t in Marks(sim, 1)) if (t.SharedView && t.Shape == TelegraphShape.Lane) lanes++;
                most = System.Math.Max(most, lanes);
            }
            Assert.That(lineStart, Is.InRange(a.EndTick, a.ImpactTick + Simulation.ThornShotFlightTicks(Fix64.FromInt(10))),
                "линия встала, пока шип летит");
            Assert.That(lineWhileFlying, Is.True);
            Assert.That(most, Is.EqualTo(Simulation.ThornLineSegments));
        }

        [Test]
        public void TenSecondsOfFighting_EveryShotWithoutAMark()
        {
            // Герой в 7 м стоит: Шипомёт подходит на 5–6 м, бьёт линиями и между
            // ними стреляет. Метки открывают только линия и всплеск: пока идёт
            // выстрел, ни одной действующей метки у Шипомёта нет.
            var sim = Arena(distance: 7);
            int shots = 0, lines = 0;
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                bool shotStarted = false; int opened = 0;
                foreach (var e in sim.Events)
                {
                    if (e.Source != 1) continue;
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThornShot)
                    { shotStarted = true; shots++; }
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThornLine) lines++;
                    if (e.Type == SimEventType.TelegraphOpened) opened++;
                }
                if (shotStarted) Assert.That(opened, Is.Zero, "выстрел открыл метку, тик " + t);
                if (!sim.TryGetThorncasterAction(1, out var a) || a.Action != ThornAction.Shot) continue;
                foreach (var mark in Marks(sim, 1))
                    Assert.That(mark.IsActive, Is.False, "действующая метка во время выстрела, тик " + t);
            }
            Assert.That(shots, Is.GreaterThanOrEqualTo(3), "стрелял");
            Assert.That(lines, Is.GreaterThanOrEqualTo(2), "и бил линиями");
        }

        [Test]
        public void Hourglass_StopsTheThornInTheAir_ThenItFliesOnAndHits()
        {
            // Песочные Часы: «враги и их снаряды стоят». Оглушение шип не отзывает,
            // поэтому раньше он долетал и бил героя посреди остановленного времени.
            var sim = ShotArena();
            sim.SetArtifact(RunArtifact.Hourglass);
            sim.Step(InputFrame.Empty);
            sim.TryGetThorncasterAction(1, out var a);
            sim.Entities.Position[0] = OnShotPath(a, 8.2);
            int hit = ShotHitTick(a.ImpactTick, 8.2, sim.Entities.BodyRadius[0].ToDouble());
            Assert.That(hit, Is.EqualTo(33));
            const int stopAt = 25;
            Until(sim, stopAt);
            Assert.That(sim.TryGetThornShot(1, out var before), Is.True, "шип в воздухе к остановке");

            var use = InputFrame.Empty;
            use.Flags = (byte)InputFlags.UseArtifact;
            sim.Step(use);
            Assert.That(sim.TimeStopped, Is.True);
            foreach (var e in sim.Events)
            {
                Assert.That(e.Type == SimEventType.EnemyActionImpact && e.Source == 1, Is.False, "шип встал в тик остановки");
                Assert.That(e.Type == SimEventType.Damage && e.Target == Simulation.PlayerId, Is.False, "удар в тик остановки");
            }
            System.Action<int> frozenInTheAir = tick =>
            {
                Assert.That(sim.TryGetThornShot(1, out var frozen), Is.True, "шип никуда не делся, тик " + tick);
                Assert.That(frozen.Travelled, Is.EqualTo(before.Travelled), "шип стоит, тик " + tick);
            };
            var log = RunShot(sim, stopAt + Simulation.HourglassTicks, frozenInTheAir);
            frozenInTheAir(sim.Tick);
            Assert.That(log.Impacts, Is.Empty, "в остановленном времени шип не бьёт");
            Assert.That(log.Damage, Is.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
            Assert.That(sim.TimeStopped, Is.False, "время снова идёт");

            // Время пошло — шип летит дальше с того же места и бьёт на столько же позже.
            RunShot(sim, hit + Simulation.HourglassTicks + 1, log: log);
            Assert.That(log.Impacts, Is.EqualTo(new[] { hit + Simulation.HourglassTicks }));
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { 14 }));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 14));
        }

        [Test]
        public void Priority_HuggedBurst_ReadyLine_OtherwiseShot()
        {
            // Прижали — всплеск.
            var hugged = Arena(distance: 2);
            hugged.Step(InputFrame.Empty);
            Assert.That(hugged.TryGetThorncasterAction(1, out var burst), Is.True);
            Assert.That(burst.Action, Is.EqualTo(ThornAction.Burst));

            // Линия готова, жетон свободен — линия, хотя выстрел тоже готов.
            var ready = Arena(distance: 6);
            ready.Step(InputFrame.Empty);
            Assert.That(ready.TryGetThorncasterAction(1, out var line), Is.True);
            Assert.That(line.Action, Is.EqualTo(ThornAction.Line));
            Assert.That(line.NextShotTick, Is.EqualTo(0), "линия перезарядку выстрела не тратит");

            // Дальше линии (8,5 м) — выстрел.
            var far = ShotArena();
            far.Step(InputFrame.Empty);
            Assert.That(far.TryGetThorncasterAction(1, out var shot), Is.True);
            Assert.That(shot.Action, Is.EqualTo(ThornAction.Shot));

            // Жетон занят линией соседа — не стоит, а стреляет: выстрелу жетон не нужен.
            var pair = Arena(2);
            pair.Step(InputFrame.Empty);
            Assert.That(pair.BigAttackTokenLimit, Is.EqualTo(1));
            Assert.That(pair.TryGetThorncasterAction(1, out var first), Is.True);
            Assert.That(first.Action, Is.EqualTo(ThornAction.Line));
            Assert.That(pair.TryGetThorncasterAction(2, out var second), Is.True);
            Assert.That(second.Action, Is.EqualTo(ThornAction.Shot));

            // Ближе 3,5 м выстрела нет: на 3 м линия на перезарядке — стоит без атаки.
            var close = Arena(distance: 3);
            int lineEnd = -1;
            for (int t = 0; t < Simulation.ThornLineCooldownTicks; t++)
            {
                close.Step(InputFrame.Empty);
                if (!close.TryGetThorncasterAction(1, out var c)) continue;
                if (c.Action == ThornAction.Line) lineEnd = c.EndTick;
                Assert.That(c.Action, Is.Not.EqualTo(ThornAction.Shot), "выстрел ближе 3,5 м, тик " + t);
            }
            Assert.That(lineEnd, Is.GreaterThan(0));
        }

        [Test]
        public void ShotCooldownIsSixtyTicksFromItsStart()
        {
            // Герой в 6 м: линия с нуля, потом выстрелы. Выстрел кончается на 33-м
            // тике от начала, следующий — только на 60-м; линия готова к 150 и
            // ждёт конца уже начатого выстрела.
            var sim = Arena();
            var shots = new List<int>(); var lines = new List<int>();
            bool idleBetween = false;
            for (int t = 0; t < 200; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Source != 1) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThornShot) shots.Add(t);
                    if (e.ActionVariant == (int)EnemyActionKind.ThornLine) lines.Add(t);
                }
                if (shots.Count == 1 && t > shots[0] + 33 && !sim.TryGetThorncasterAction(1, out _)) idleBetween = true;
            }
            Assert.That(lines[0], Is.EqualTo(0));
            Assert.That(shots.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(shots[0], Is.EqualTo(Simulation.ThornLineLastImpactTicks + Simulation.ThornLineRecoveryTicks),
                "сразу после стойки линии");
            Assert.That(shots[1] - shots[0], Is.EqualTo(Simulation.ThornShotCooldownTicks));
            Assert.That(idleBetween, Is.True, "между концом выстрела и перезарядкой стоит без атаки");
            Assert.That(lines[1], Is.EqualTo(shots[1] + Simulation.ThornShotWindupTicks + Simulation.ThornShotRecoveryTicks));
        }

        [Test]
        public void ShotDamageScalesWithDepthAndHardRoute()
        {
            int hard = EnemyArchetypes.HardRoutePercent;
            var sim = ShotArena(arena: 8, hard: hard);
            int shot = sim.ThornShotDamageOf(1);
            double expected = 14.0 * EnemyArchetypes.DepthDamagePercent(8) * hard / 10000.0;
            Assert.That(shot, Is.EqualTo(EnemyArchetypes.Share(sim.ThornSpikeDamageOf(1), 14, 30)));
            Assert.That(shot, Is.EqualTo(expected).Within(1.0));
            Assert.That(shot, Is.GreaterThan(14));
            Assert.That(ShotArena().ThornShotDamageOf(1), Is.EqualTo(14), "14 на А1");

            var log = RunShot(sim, ShotOver);
            Assert.That(log.Hits, Is.EqualTo(new[] { true }));
            Assert.That(log.Amounts, Is.EqualTo(new[] { shot }), "настоящий шип бьёт ровно этим уроном");
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - shot));
        }

        [Test]
        public void ShotStateHashIsIdenticalRunToRun_AndSetupClearsAThornInFlight()
        {
            var spots = new[] { At(-3, 0), At(0, 0), At(-1, 2), At(2, -1), At(-2, -3) };
            var a = Arena(2, distance: 9); var b = Arena(2, distance: 9);
            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()));
            int shotsInFlight = 0;
            for (int t = 0; t < 600; t++)
            {
                if (t % 45 == 0)
                {
                    a.Entities.Position[0] = spots[t / 45 % spots.Length];
                    b.Entities.Position[0] = spots[t / 45 % spots.Length];
                }
                // Шипомёт 2 гибнет с шипом в воздухе: шип живёт дальше и тоже в хеше.
                if (a.TryGetThornShot(2, out _) && a.Entities.Alive[2] && t > 100)
                {
                    a.ApplyAbilityDamage(Simulation.PlayerId, 2, 1000000, -1, DamageType.Physical);
                    b.ApplyAbilityDamage(Simulation.PlayerId, 2, 1000000, -1, DamageType.Physical);
                }
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "тик " + t);
                if (a.TryGetThornShot(1, out _) || a.TryGetThornShot(2, out _)) shotsInFlight++;
            }
            Assert.That(shotsInFlight, Is.GreaterThan(0), "шипы летали");
            Assert.That(a.Entities.Alive[2], Is.False, "стрелок погиб с шипом в воздухе");

            // Одинаковый хеш двух прогонов ещё не значит, что шип в нём есть: поля
            // шипа мёртвого стрелка правятся по одному, и хеш обязан это заметить.
            var d = ShotArena();
            Until(d, Simulation.ThornShotWindupTicks + 2);
            Assert.That(d.TryGetThornShot(1, out _), Is.True);
            d.ApplyAbilityDamage(Simulation.PlayerId, 1, 1000000, -1, DamageType.Physical);
            Assert.That(d.Entities.Alive[1], Is.False);
            var shots = (ThornShotState[])typeof(Simulation).GetField("_thornShots",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(d);
            ulong clean = d.StateHash();
            shots[1].Travelled += Fix64.Ratio(1, 100);
            Assert.That(d.StateHash(), Is.Not.EqualTo(clean), "пройденное шипа — в хеше");
            shots[1].Travelled -= Fix64.Ratio(1, 100);
            shots[1].ReleaseTick++;
            Assert.That(d.StateHash(), Is.Not.EqualTo(clean), "тик выпуска — в хеше");
            shots[1].ReleaseTick--;
            shots[1].Damage++;
            Assert.That(d.StateHash(), Is.Not.EqualTo(clean), "урон шипа — в хеше");
            shots[1].Damage--;
            Assert.That(d.StateHash(), Is.EqualTo(clean));

            // Сброс посреди полёта: ни шипа, ни меток.
            var c = ShotArena();
            Until(c, 24);
            Assert.That(c.TryGetThornShot(1, out _), Is.True);
            c.SetupKindTestArena(EnemyKind.ForestThorncaster);
            Assert.That(c.TryGetThornShot(1, out _), Is.False);
            Assert.That(Marks(c, 1), Is.Empty);
            c.Step(InputFrame.Empty);
            Assert.That(c.TryGetThornShot(1, out _), Is.False);
        }
    }
}
