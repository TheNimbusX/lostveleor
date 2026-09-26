using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Шипомёт (план новых мобов от 26.09): линия из четырёх сегментов с шипами
    /// на 27/33/39/45 тиках, одно попадание за линию, препятствия обрезают
    /// линию, оглушение и смерть снимают несработавшее, 30 тиков стойки после
    /// последнего шипа, всплеск против объятий, глубина, детерминизм.
    /// </summary>
    public sealed class ThorncasterTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Стенд вида: герой в нуле лицом по +X, Шипомёт в 6 м по оси и смотрит на героя.</summary>
        private static Simulation Arena(int count = 1, LayoutMap map = null, int arena = 1, int hard = 100,
            int distance = 6)
        {
            var sim = new Simulation(61, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThorncaster, count, map, 61, arena, hard, Fix64.FromInt(distance));
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

        [Test]
        public void RockAtItsFeet_BlocksTheWholeLine()
        {
            // Камень между телом и началом первого сегмента: линии нет вовсе, жетон не взят.
            var sim = Arena(map: Glade(4.7, 0.3));
            for (int t = 0; t < 20; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetThorncasterAction(1, out _), Is.False, "тик " + t);
                Assert.That(Marks(sim, 1), Is.Empty);
            }
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

            // Перезарядка 90 от начала. Линии вплотную нет (она с 3 м), и назад он не шагает.
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
                if (sim.TryGetThorncasterAction(2, out var b)) second = b.StartTick;
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
            Assert.That(sim.Entities.MaxHealth[1], Is.EqualTo(EnemyArchetypes.ScaleHealth(1700,
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
    }
}
