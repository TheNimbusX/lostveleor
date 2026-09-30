using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class StonehoofTests
    {
        private static Simulation Arena(bool wall = false, int count = 1)
        {
            LayoutMap map = null;
            if (wall)
            {
                var room = new ModuleDefinition("stonehoof.test", 20, 20, new ModuleConnector[0], isEntrance: true);
                map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
                map.AddTestObstacle(new LayoutObstacle(new FixVec2(Fix64.FromInt(-5), Fix64.Zero), Fix64.One, 0));
            }
            var sim = new Simulation(76, 64); sim.SetupStonehoofEncounter(map, 76, count);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0); sim.Entities.Health[0] = 10000;
            return sim;
        }
        private static void Until(Simulation sim, int tick) { while (sim.Tick < tick) sim.Step(InputFrame.Empty); }
        [Test]
        public void FullWarningLocksDirectionAndEndWhileHeroMoves()
        {
            var s = Arena(); s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out var a), Is.True);
            Assert.That(a.LaunchTick - a.StartTick, Is.EqualTo(30));
            s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(4));
            Until(s, a.LaunchTick); Assert.That(s.Entities.Position[1], Is.EqualTo(a.Origin));
            s.TryGetStonehoofAction(1, out var b); Assert.That(b.Target, Is.EqualTo(a.Target));
            Assert.That(b.Direction, Is.EqualTo(a.Direction)); Until(s, a.StopTick + 1);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }
        [Test]
        public void HitsOnceAndPushesSidewaysThenSkidsAtOpenEdge()
        {
            var s = Arena(); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            int hits = 0; Until(s, a.LaunchTick);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
            while (s.Tick <= a.StopTick + 10)
            {
                s.Step(InputFrame.Empty);
                foreach (var e in s.Events) if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == 0) hits++;
            }
            // Урон тарана — из таблицы видов; отброс урона не добавляет.
            int charge = EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BaseDamage;
            Assert.That(hits, Is.EqualTo(1)); Assert.That(s.Entities.Health[0], Is.EqualTo(10000 - charge));
            Assert.That(Fix64.Abs(s.Entities.Position[0].Y).ToFloat(), Is.InRange(.95f, 1.05f));
            Assert.That(a.StopReason, Is.EqualTo(StonehoofStop.ArenaEdge));
            Assert.That(a.StopTick - a.BrakeTick, Is.EqualTo(Simulation.StonehoofBrakeTicks));
            Assert.That(s.Entities.NextAttackTick[1], Is.EqualTo(a.StopTick + Simulation.StonehoofRestTicks));
        }
        [Test]
        public void AcceleratesForSixTicksThenKeepsTwelveMetresPerSecond()
        {
            var s = Arena(); s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(3));
            s.Step(InputFrame.Empty); Until(s, 15); s.TryGetStonehoofAction(1, out var a);
            Assert.That(a.Serial, Is.GreaterThan(0)); Until(s, a.LaunchTick + 7);
            Assert.That((s.Entities.Position[1] - a.Origin).Length.ToFloat(), Is.EqualTo(1.2f).Within(.002f));
            var previous = s.Entities.Position[1]; s.Step(InputFrame.Empty);
            Assert.That((s.Entities.Position[1] - previous).Length.ToFloat(), Is.EqualTo(.4f).Within(.002f));
        }
        [Test]
        public void WallStopsSweptBodyAndStunsFor36Ticks()
        {
            var s = Arena(true); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            Assert.That(a.StopReason, Is.EqualTo(StonehoofStop.Obstacle));
            Assert.That(a.Target.X.ToFloat(), Is.EqualTo(-3.3f).Within(.003f));
            Until(s, a.StopTick + 1); Assert.That(s.Statuses.IsStunned(1, s.Tick), Is.True);
            Assert.That(s.Statuses.StunUntilTick[1], Is.EqualTo(a.StopTick + Simulation.StonehoofWallTicks));
            Assert.That(s.Entities.Position[1], Is.EqualTo(a.Target)); Until(s, a.EndTick);
            Assert.That(s.Entities.Position[1], Is.EqualTo(a.Target));
        }
        [TestCase(4, 0)] [TestCase(29, 1)] [TestCase(33, 2)]
        public void DeathStunAndOneTickForcedMotionCancelBeforeContact(int time, int reason)
        {
            var s = Arena(); s.Step(InputFrame.Empty); Until(s, time);
            if (reason == 0) s.Entities.Alive[1] = false;
            else if (reason == 1) s.Statuses.ApplyStun(1, s.Tick + 3);
            else { ForcedMotion.Begin(s.Entities, 1, s.Entities.Position[1], 2, ForcedMotionKind.Dragged); s.Entities.ForcedTicksLeft[1] = 1; }
            s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out _), Is.False);
            Until(s, 110); Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }
        [Test]
        public void OrdinaryDamageDoesNotCancelCharge()
        {
            var s = Arena(); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            Until(s, 32); s.Entities.Health[1] -= 5; s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofAction(1, out var b), Is.True); Assert.That(b.Serial, Is.EqualTo(a.Serial));
        }
        [Test]
        public void AlliesCannotDeflectLockedLineOrReceiveChargeDamage()
        {
            var s = Arena(false, 3); s.Step(InputFrame.Empty); s.TryGetStonehoofAction(1, out var a);
            s.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(6));
            for (int i = 2; i <= 3; i++) { s.Entities.Position[i] = a.Origin - new FixVec2(Fix64.FromInt(i), Fix64.Zero); s.Statuses.ApplyStun(i, 999); }
            while (s.Tick < a.StopTick)
            { s.Step(InputFrame.Empty); Assert.That(s.Entities.Position[1].Y.ToFloat(), Is.EqualTo(a.Origin.Y.ToFloat()).Within(.001f)); }
            int full = EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BaseHealth;
            Assert.That(s.Entities.Health[2], Is.EqualTo(full)); Assert.That(s.Entities.Health[3], Is.EqualTo(full));
        }
        [Test]
        public void RepeatClearsActionsAndRandomnessIsDeterministic()
        {
            // Единственный повтор, где таран идёт несколько раз: 450 тиков — не меньше двух разбегов.
            var a = Arena(); var b = Arena(); int charges = 0;
            for (int t = 0; t < 450; t++)
            {
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty); Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t);
                foreach (var e in a.Events) if (e.Type == SimEventType.StonehoofStarted) charges++;
            }
            Assert.That(charges, Is.GreaterThanOrEqualTo(2), "таран повторился меньше двух раз");
            a.SetupStonehoofEncounter(null, 76);
            Assert.That(a.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestStonehoof));
            Assert.That(a.Entities.Health[1], Is.EqualTo(EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BaseHealth));
            Assert.That(a.TryGetStonehoofAction(1, out _), Is.False);
            Assert.That(a.TryGetStonehoofTusk(1, out _), Is.False);
        }
        [Test]
        public void AFailedRetreatStillAllowsFullWarningUpClose()
        {
            // Вплотную (между телами ≤ 1 м спереди, центры ≤ 2,15 м) с 29.09 бьют клыки,
            // а не таран; «в упор» для тарана — 2,5 м: ближе порога отхода (3 м), но дальше клыков.
            var s = Arena(); s.Entities.Position[0] = new FixVec2(Fix64.Ratio(5, 2), Fix64.Zero);
            s.Step(InputFrame.Empty); Assert.That(s.TryGetStonehoofAction(1, out var a), Is.True);
            Assert.That(a.LaunchTick - a.StartTick, Is.EqualTo(30)); Until(s, 30);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000));
        }

        // ---- разворот: один шаг 6° за тик ----

        private static readonly FixVec2 West = new FixVec2(-Fix64.One, Fix64.Zero);

        private static double Degrees(FixVec2 a, FixVec2 b)
        {
            double ax = a.X.ToDouble(), ay = a.Y.ToDouble(), bx = b.X.ToDouble(), by = b.Y.ToDouble();
            double dot = (ax * bx + ay * by) / (System.Math.Sqrt(ax * ax + ay * ay) * System.Math.Sqrt(bx * bx + by * by));
            return System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, dot))) * 180.0 / System.Math.PI;
        }

        [Test]
        public void AimTurnsOncePerTickAndChargesOnlyWhenFacingTheHero()
        {
            // Раньше прицел доворачивал и в движении, и в UpdateStonehooves — 24° за тик.
            var s = Arena(); s.Entities.Facing[1] = -West;
            var previous = s.Entities.Facing[1]; int started = -1;
            for (int t = 0; t < 45 && started < 0; t++)
            {
                s.Step(InputFrame.Empty);
                Assert.That(Degrees(previous, s.Entities.Facing[1]),
                    Is.LessThanOrEqualTo(Simulation.StonehoofTurnDegreesPerTick + .05), "tick " + t);
                previous = s.Entities.Facing[1];
                if (s.TryGetStonehoofAction(1, out var a)) started = a.StartTick;
            }
            Assert.That(started, Is.InRange(29, 30));
            s.TryGetStonehoofAction(1, out var charge);
            Assert.That(Degrees(charge.Direction, West), Is.LessThan(.01));
            Assert.That(Degrees(s.Entities.Facing[1], West), Is.LessThan(.01));
        }

        [Test]
        public void NeverStepsForwardUntilFacingWithinFiftyThreeDegrees()
        {
            // Герой за 11 м за спиной: сначала разворот на месте, шаг — только вдоль взгляда,
            // когда корпус довернулся до ≈53° (cos 0,6): поворот и шаг дальше идут вместе, дугой.
            var s = Arena(); s.Entities.Position[0] = new FixVec2(Fix64.FromInt(-6), Fix64.Zero);
            s.Entities.Facing[1] = -West; s.Entities.NextAttackTick[1] = 10000;
            int firstStep = -1;
            for (int t = 0; t < 60; t++)
            {
                var before = s.Entities.Position[1];
                var wanted = (s.Entities.Position[0] - before).Normalized();
                s.Step(InputFrame.Empty);
                var moved = s.Entities.Position[1] - before;
                if (moved.LengthSq == Fix64.Zero) continue;
                if (firstStep < 0) firstStep = t;
                Assert.That(FixVec2.Dot(s.Entities.Facing[1], wanted), Is.GreaterThanOrEqualTo(Simulation.StonehoofWalkAlignCos), "tick " + t);
                Assert.That(Degrees(moved, s.Entities.Facing[1]), Is.LessThan(.5), "боком, тик " + t);
            }
            // Шаг не раньше, чем корпус довернулся до 53° (cos 0,6): 127° / 6° — 22-й тик, индекс 21.
            Assert.That(firstStep, Is.InRange(20, 22));
        }

        // ---- взмах клыками и оглушение тараном (ревью владельца 29.09) ----

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Герой в нуле, кабан в boar смотрит в facing; отдыхает (таран через 10000 тиков) или готов к тарану.</summary>
        private static Simulation Tusk(FixVec2 boar, FixVec2 facing, bool resting = true)
        {
            var s = Arena();
            s.Entities.Position[1] = boar; s.Entities.Facing[1] = facing;
            s.Entities.NextAttackTick[1] = resting ? 10000 : 0;
            return s;
        }

        /// <summary>Сколько событий type про клыки Камнекопыта в последнем тике.</summary>
        private static int TuskEvents(Simulation s, SimEventType type)
        {
            int n = 0;
            foreach (var e in s.Events)
                if (e.Type == type && e.ActionVariant == (int)EnemyActionKind.StonehoofTusk) n++;
            return n;
        }

        private static double HeroDistance(Simulation s)
            => (s.Entities.Position[1] - s.Entities.Position[0]).Length.ToDouble();

        private static int Guardian(Simulation s, FixVec2 at)
        {
            int id = s.SpawnEnemy(at, 1000, EnemyKind.ForestGuardian);
            s.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero); s.Entities.RefreshStats(id);
            s.Entities.Facing[id] = (s.Entities.Position[0] - at).Normalized(); s.Entities.Aggro[id] = true;
            return id;
        }

        [Test]
        public void TuskOnlyWhenAdjacent()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);

            // Вплотную спереди на отдыхе — клыки в первый же тик: замах 14 тиков, метки на земле нет.
            var s = Tusk(At(1.4, 0), West); s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofTusk(1, out var tusk), Is.True, "вплотную спереди, отдых");
            Assert.That(tusk.ImpactTick - tusk.StartTick, Is.EqualTo(Simulation.StonehoofTuskWindupTicks));
            Assert.That(TuskEvents(s, SimEventType.EnemyActionStarted), Is.EqualTo(1));
            foreach (var e in s.Events) Assert.That(e.Type, Is.Not.EqualTo(SimEventType.TelegraphOpened), "метка на земле");

            // Прицел (таран готов, полоса чиста): вплотную — клыки, а не разбег в упор.
            s = Tusk(At(1.4, 0), West, resting: false); s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofTusk(1, out _), Is.True, "вплотную спереди, прицел");
            Assert.That(s.TryGetStonehoofAction(1, out _), Is.False, "таран поверх клыков");

            // 2,3 м между центрами — между телами 1,15 м (0,7 + 0,45), больше метра:
            // уже не вплотную, кабан пятится и клыками не бьёт.
            s = Tusk(At(2.3, 0), West);
            for (int t = 0; t < 60; t++)
            {
                s.Step(InputFrame.Empty);
                Assert.That(s.TryGetStonehoofTusk(1, out _), Is.False, "2,3 м, тик " + t);
            }

            // 2,1 м — между телами 0,95 м: ещё вплотную. Порог от центров (1,7 м) этого
            // героя не видел — так стоит прижавшийся и рубящий кабана (съёмка 29.09).
            s = Tusk(At(2.1, 0), West); s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofTusk(1, out _), Is.True, "2,1 м — метр от тела ещё не набран");

            // Сбоку (90°): пока корпус не довернулся до ±60° (6° за тик), клыков нет.
            s = Tusk(At(1.2, 0), new FixVec2(Fix64.Zero, Fix64.One));
            int started = -1;
            for (int t = 0; t < 20 && started < 0; t++)
            {
                s.Step(InputFrame.Empty);
                if (!s.TryGetStonehoofTusk(1, out var side)) continue;
                started = side.StartTick;
                Assert.That(Degrees(s.Entities.Facing[1], West), Is.LessThanOrEqualTo(60.05), "клыки боком");
            }
            Assert.That(started, Is.InRange(4, 5), "90° − 60° = 30°, пять шагов доворота");

            // Готов к тарану, но за героем камень и полосы нет: кабан заходит на точку
            // кольца — идёт, а не отдыхает и не целится, и клыками не бьёт.
            var room = new ModuleDefinition("stonehoof.tusk", 20, 20, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room })); map.TryPlace(0, 0, -10, -10);
            map.AddTestObstacle(new LayoutObstacle(At(-1.6, 0), Fix64.Ratio(1, 2), 0));
            s = new Simulation(76, 64); s.SetupStonehoofEncounter(map, 76);
            s.Entities.Position[0] = FixVec2.Zero; s.Entities.Position[1] = At(1.4, 0);
            s.Entities.Facing[1] = West; s.Entities.NextAttackTick[1] = 0;
            for (int t = 0; t < 30; t++)
            {
                s.Step(InputFrame.Empty);
                Assert.That(s.TryGetStonehoofTusk(1, out _), Is.False, "заход на точку, тик " + t);
                Assert.That(s.TryGetStonehoofAction(1, out _), Is.False, "таран без полосы, тик " + t);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TuskNeverApproaches(bool resting)
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            // Герой стоит вплотную и сам не ходит. Кабан бьёт клыками, отдыхая пятится,
            // готовый — разгоняется. Расстояние до героя сокращает только таран.
            var s = Tusk(At(1.4, 0), West, resting);
            int tusks = 0, charges = 0;
            for (int t = 0; t < 240; t++)
            {
                double before = HeroDistance(s);
                bool charging = s.TryGetStonehoofAction(1, out _);
                s.Step(InputFrame.Empty);
                tusks += TuskEvents(s, SimEventType.EnemyActionStarted);
                if (s.TryGetStonehoofAction(1, out _))
                {
                    if (!charging) charges++;
                    charging = true;
                }
                if (charging) continue;
                Assert.That(HeroDistance(s), Is.GreaterThanOrEqualTo(before - 1e-4), "кабан шагнул к герою, тик " + t);
            }
            Assert.That(tusks, Is.GreaterThanOrEqualTo(1), "клыков не было");
            if (!resting) Assert.That(charges, Is.GreaterThanOrEqualTo(1), "готовый кабан так и не разогнался");
        }

        [Test]
        public void TuskKnocks1m()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            var s = Tusk(At(1.4, 0), West); s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofTusk(1, out var tusk), Is.True);
            Until(s, tusk.ImpactTick);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000), "удар раньше контакта");
            var before = s.Entities.Position[0];
            s.Step(InputFrame.Empty);
            bool touched = false;
            foreach (var e in s.Events)
                if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.StonehoofTusk) touched = e.Flag;
            Assert.That(touched, Is.True, "клыки не задели");
            // Урон клыков — доля урона тарана (StonehoofTuskDamagePercent).
            int tuskDamage = s.StonehoofTuskDamageOf(1);
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000 - tuskDamage));
            Until(s, tusk.ImpactTick + 1 + Simulation.StonehoofTuskKnockbackTicks + 2);
            var moved = s.Entities.Position[0] - before;
            Assert.That(moved.Length.ToFloat(), Is.InRange(.95f, 1.05f), "отброс на метр");
            Assert.That(Degrees(moved, West), Is.LessThan(1.0), "отброс прочь от кабана");
            Assert.That(s.Entities.Health[0], Is.EqualTo(10000 - tuskDamage), "клыки бьют один раз");
            // Всё восстановление стоит там, где начал замах; следующий взмах — не раньше 60 тиков от начала.
            Assert.That(s.Tick, Is.LessThan(tusk.RecoverUntil));
            Assert.That(s.Entities.Position[1], Is.EqualTo(tusk.Origin));
        }

        [Test]
        public void ChargeStuns30Ticks()
        {
            Assert.That(Simulation.StonehoofChargeStunTicks, Is.EqualTo(30));
            var s = Arena(); s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofAction(1, out var a), Is.True);
            int hitTick = -1, controlTick = -1, controls = 0; SimEvent control = default;
            while (s.Tick <= a.StopTick + 5)
            {
                s.Step(InputFrame.Empty);
                foreach (var e in s.Events)
                {
                    if (e.Type == SimEventType.Damage && e.Source == 1 && e.Target == 0) hitTick = s.Tick - 1;
                    if (e.Type == SimEventType.HeroControl) { controls++; control = e; controlTick = s.Tick - 1; }
                }
            }
            Assert.That(hitTick, Is.GreaterThanOrEqualTo(0), "таран не попал");
            Assert.That(controls, Is.EqualTo(1), "оглушение одно на удар");
            Assert.That(controlTick, Is.EqualTo(hitTick), "оглушение — в тик удара");
            Assert.That(control.Source, Is.EqualTo(1), "оглушил кабан");
            Assert.That(control.Target, Is.EqualTo(Simulation.PlayerId));
            Assert.That(control.Amount, Is.EqualTo(30));
            Assert.That(control.Flag, Is.False, "оглушение, а не корни");
            // Тридцать шагов героя, начиная со следующего за ударом тика; отброс вбок доезжает.
            Assert.That(s.Statuses.IsStunned(0, hitTick + 1), Is.True);
            Assert.That(s.Statuses.IsStunned(0, hitTick + 30), Is.True);
            Assert.That(s.Statuses.IsStunned(0, hitTick + 31), Is.False);
            Assert.That(Fix64.Abs(s.Entities.Position[0].Y).ToFloat(), Is.InRange(.95f, 1.05f));
        }

        [Test]
        public void TuskUsesMeleeToken()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);

            // Два Хранителя в замахе держат оба ближних жетона: кабан вплотную не бьёт.
            // Один Хранитель — жетон свободен, клыки в тот же тик.
            for (int guardians = 2; guardians >= 1; guardians--)
            {
                var g = new Simulation(777UL, 32); g.SetupTestArena(0); g.PlayerInvulnerable = true;
                // Здесь жетоны проверяются сами по себе — два замаха в один тик. В игре
                // их ещё разносит такт ударов (AttackRhythmTests), как в AttackTokenTests.
                g.AttackRhythmEnabled = false;
                g.Entities.Position[0] = FixVec2.Zero;
                int first = Guardian(g, At(2, 0));
                int second = guardians == 2 ? Guardian(g, At(-1, 1.732)) : -1;
                int boar = g.SpawnEnemy(At(0, -1.4), 1000, EnemyKind.ForestStonehoof);
                g.Entities.Facing[boar] = new FixVec2(Fix64.Zero, Fix64.One);
                g.Entities.Aggro[boar] = true; g.Entities.NextAttackTick[boar] = 10000;
                g.Step(InputFrame.Empty);
                Assert.That(g.TryGetEnemySwing(first, out var swing) && !swing.HitResolved, Is.True, "Хранитель не замахнулся");
                if (second >= 0)
                    Assert.That(g.TryGetEnemySwing(second, out var other) && !other.HitResolved, Is.True, "второй Хранитель не замахнулся");
                Assert.That(g.TryGetStonehoofTusk(boar, out _), Is.EqualTo(guardians == 1),
                    guardians == 2 ? "клыки поверх двух замахов" : "свободный жетон, а клыков нет");
            }

            // Два кабана вплотную с двух сторон: два жетона — оба бьют в один тик.
            // Жетон держится от начала замаха до контакта, восстановление его не держит.
            var s = Arena(false, 2);
            s.AttackRhythmEnabled = false; // жетоны сами по себе: такт развёл бы два контакта
            s.Entities.Position[1] = At(1.4, 0); s.Entities.Facing[1] = West; s.Entities.NextAttackTick[1] = 10000;
            s.Entities.Position[2] = At(-1.4, 0); s.Entities.Facing[2] = -West; s.Entities.NextAttackTick[2] = 10000;
            s.Step(InputFrame.Empty);
            Assert.That(s.TryGetStonehoofTusk(1, out var t1), Is.True);
            Assert.That(s.TryGetStonehoofTusk(2, out var t2), Is.True, "второй жетон свободен, а второй кабан ждёт");
            Assert.That(t2.StartTick, Is.EqualTo(t1.StartTick));
            Assert.That(s.StonehoofTuskHoldsMeleeToken(1), Is.True);
            Assert.That(s.StonehoofTuskHoldsMeleeToken(2), Is.True);
            Until(s, t1.ImpactTick + 1);
            Assert.That(s.StonehoofTuskHoldsMeleeToken(1), Is.False, "жетон свободен с контакта");
            Assert.That(s.TryGetStonehoofTusk(1, out _), Is.True, "восстановление ещё идёт");
        }

        // ---- клыки в настоящем бою (съёмка stonehoof-hug 29.09) ----
        //
        // Тесты выше ставят кабана в 1,4 м руками — и проходили, пока в игре клыки
        // не срабатывали ни разу: отдыхающий кабан пятится, и герой держится в
        // 1,87–1,94 м, за прежним порогом 1,7 м от центров. Здесь кабана руками
        // не двигают: стендовая расстановка (герой и кабан в 6 м), кабан сам
        // доворачивает и пятится, герой подходит своими ногами.

        /// <summary>Стенд Камнекопыта как есть; кабан отдыхает после тарана до конца теста.</summary>
        private static Simulation RestingEncounter()
        {
            var s = new Simulation(76, 64); s.SetupStonehoofEncounter(null, 76, 1);
            s.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            s.Entities.RefreshStats(0); s.Entities.Health[0] = 10000;
            s.Entities.NextAttackTick[1] = 10000;
            return s;
        }

        /// <summary>«Прижаться», как съёмка stonehoof-hug: дальше 1,7 м от центра — приказ встать в 1,4 м.</summary>
        private static InputFrame Hug(Simulation s)
        {
            var input = InputFrame.Empty;
            var center = s.Entities.Position[1]; var to = center - s.Entities.Position[0];
            Fix64 hold = Fix64.Ratio(7, 5), slack = hold + Fix64.Ratio(3, 10);
            if (to.LengthSq > slack * slack)
            { input.Flags = (byte)InputFlags.MoveOrder; input.Aim = center - to.Normalized() * hold; }
            return input;
        }

        /// <summary>Рубить кабана автоатакой: герой сам подходит на дистанцию удара.</summary>
        private static InputFrame Chop(Simulation s)
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.Attack; input.AttackTarget = 1; input.Aim = s.Entities.Position[1];
            return input;
        }

        /// <summary>
        /// Гоняет стенд ticks тиков с приказами героя order. Возвращает тик первого
        /// начала клыков (-1 — не было), расстояние в этот тик, число начал и
        /// промахов (контакт, не задевший героя), тик, когда герой впервые ближе
        /// порога отхода; на каждом тике проверяет, что кабан не шагнул к герою.
        /// </summary>
        private static int RunTusks(Simulation s, System.Func<Simulation, InputFrame> order, int ticks,
            out double startDistance, out int starts, out int misses, out int firstClose)
        {
            int first = -1; startDistance = 0; starts = misses = 0; firstClose = -1;
            for (int t = 0; t < ticks; t++)
            {
                var boarBefore = s.Entities.Position[1];
                s.Step(order(s));
                var step = s.Entities.Position[1] - boarBefore;
                var toHero = s.Entities.Position[0] - boarBefore;
                if (toHero.LengthSq.Raw != 0)
                    Assert.That(FixVec2.Dot(step, toHero.Normalized()).ToDouble(), Is.LessThanOrEqualTo(1e-4),
                        "кабан шагнул к герою, тик " + s.Tick);
                if (firstClose < 0 && HeroDistance(s) < Simulation.StonehoofBackoffStart.ToDouble()) firstClose = s.Tick;
                foreach (var e in s.Events)
                {
                    if (e.ActionVariant != (int)EnemyActionKind.StonehoofTusk) continue;
                    if (e.Type == SimEventType.EnemyActionStarted)
                    {
                        starts++;
                        if (first < 0) { first = s.Tick; startDistance = HeroDistance(s); }
                    }
                    if (e.Type == SimEventType.EnemyActionImpact && !e.Flag) misses++;
                }
            }
            return first;
        }

        [Test]
        public void Tusk_HitsTheHeroHuggingTheRestingBoar()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            var s = RestingEncounter();
            int first = RunTusks(s, Hug, 300, out double at, out int starts, out int misses, out int close);
            Assert.That(close, Is.GreaterThanOrEqualTo(0), "герой так и не подошёл");
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "прижатый кабан клыками не бьёт");
            // Отход начинается с 3 м; герой (4,5 м/с) догоняет пятящегося (1,5 м/с) за
            // десяток тиков, и клыки — в первый же тик, когда тела в метре друг от друга.
            Assert.That(first - close, Is.LessThanOrEqualTo(20), "клыки запоздали");
            Assert.That(at, Is.GreaterThan(1.7), "порог от центров (1,7 м) такого героя не видел");
            // Перезарядка 60 тиков: за 10 с прижатия — не один взмах, и прижавшийся
            // стоит в секторе — ни одного взмаха мимо.
            Assert.That(starts, Is.GreaterThanOrEqualTo(3), "клыки один раз и больше не бьют");
            Assert.That(misses, Is.EqualTo(0), "взмах мимо стоящего вплотную");
        }

        [Test]
        public void Tusk_HitsTheHeroChoppingTheRestingBoarFromMeleeRange()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            var s = RestingEncounter();
            int boarHealth = s.Entities.Health[1];
            int first = RunTusks(s, Chop, 300, out double at, out int starts, out int misses, out int close);
            Assert.That(s.Entities.Health[1], Is.LessThan(boarHealth), "герой так и не рубанул");
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "рубящего героя клыки не достают");
            Assert.That(first - close, Is.LessThanOrEqualTo(20), "клыки запоздали");
            // Автоатака встаёт в 1,875 м от центра (0,75 дальности 2,5): тела в 0,73 м.
            Assert.That(at, Is.GreaterThan(1.7), "порог от центров (1,7 м) такого героя не видел");
            Assert.That(starts, Is.GreaterThanOrEqualTo(3), "клыки один раз и больше не бьют");
            Assert.That(misses, Is.EqualTo(0), "рубящий стоит на месте — клыки задевают");
        }

        [Test]
        public void Tusk_ReadyBoarHoldsGroundUntilItsBeat()
        {
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            // Герой в 2 м перед отдыхающим кабаном и оглушён: такт не даёт клыкам лечь
            // в оглушение. Кабан не пятится (иначе за 5 тиков ушёл бы из досягаемости),
            // а стоит и бьёт, как только контакт ляжет после оглушения и запаса.
            var s = RestingEncounter();
            s.Entities.Position[0] = s.Entities.Position[1] + West * Fix64.FromInt(2);
            Assert.That(s.ApplyHeroStun(Simulation.StonehoofChargeStunTicks), Is.True);
            int free = s.Tick + s.HeroStunTicksLeft;
            var origin = s.Entities.Position[1];
            StonehoofTuskState tusk = default;
            for (int t = 0; t < 60 && !s.TryGetStonehoofTusk(1, out tusk); t++)
            {
                s.Step(InputFrame.Empty);
                if (!s.TryGetStonehoofTusk(1, out tusk))
                    Assert.That(s.Entities.Position[1], Is.EqualTo(origin), "клыки готовы, а кабан пятится, тик " + s.Tick);
            }
            Assert.That(tusk.Serial, Is.GreaterThan(0), "клыков так и не было");
            Assert.That(tusk.ImpactTick, Is.EqualTo(free + Simulation.HeroControlGraceTicks), "контакт — сразу после оглушения и запаса");

            // После взмаха клыки на перезарядке — отдых снова пятится (отход начат ещё
            // с 2 м и держится до 5 м), хотя отброс только что отнёс героя на метр.
            Until(s, tusk.RecoverUntil + 1);
            var before = s.Entities.Position[1];
            s.Step(InputFrame.Empty);
            Assert.That((s.Entities.Position[1] - before).Length.ToDouble(), Is.GreaterThan(.04), "на перезарядке клыков кабан не пятится");
        }
    }
}
