using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class ForestBudTests
    {
        private static Simulation Arena(int count = 1, ForestBudSettings config = null)
        {
            var sim = new Simulation(123, 64, config);
            sim.SetupForestBudEncounter(null, 123, count);
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.RefreshStats(0);
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            for (int i = 1; i < sim.Entities.Count; i++)
            {
                sim.Entities.NextAttackTick[i] = 0;
                sim.Entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.RefreshStats(i);
            }
            return sim;
        }

        private static void Until(Simulation sim, int tick)
        { while (sim.Tick < tick) sim.Step(InputFrame.Empty); }

        private static ForestFruitState FirstFruit(Simulation sim)
        {
            for (int i = 0; i < sim.ForestFruitCapacity; i++)
                if (sim.TryGetForestFruit(i, out var fruit)) return fruit;
            Assert.Fail("No live fruit.");
            return default;
        }

        [TestCase(1000, true)]
        [TestCase(1001, false)]
        public void TenMetersIsTheInclusiveLaunchLimit(int centimeters, bool canShoot)
        {
            var sim = Arena();
            sim.Entities.Position[1] = new FixVec2(Fix64.Ratio(centimeters, 100), Fix64.Zero);
            Until(sim, 25);
            Assert.That(sim.ForestFruitActiveCount, Is.EqualTo(canShoot ? 1 : 0));
        }

        [Test]
        public void EscapingBeyondTenMetersCancelsUnfiredFruit()
        {
            var sim = Arena(); Until(sim, 25);
            sim.Entities.Position[0] = new FixVec2(Fix64.FromInt(-5), Fix64.Zero);
            Until(sim, 49);
            Assert.That(sim.ForestFruitActiveCount, Is.EqualTo(1));
            Assert.That(sim.TryGetForestBudAttack(1, out _), Is.False);
            Assert.That(FirstFruit(sim).Target, Is.EqualTo(FixVec2.Zero));
        }

        [Test]
        public void BudYieldsTheFrontlineToMeleeAllies()
        {
            var sim = Arena(); sim.Entities.NextAttackTick[1] = 1000;
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed, sim.ForestBudConfig.MoveSpeed);
            sim.Entities.RefreshStats(1);
            int ally = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(7), Fix64.Zero), 100, Faction.Orvill);
            sim.Entities.Kind[ally] = EnemyKind.ForestGuardian;
            sim.Entities.Stats[ally].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(ally); sim.Entities.NextAttackTick[ally] = 1000;
            Until(sim, 180);
            Assert.That((sim.Entities.Position[1].X - sim.Entities.Position[ally].X).ToDouble(), Is.GreaterThan(1.0));
            Assert.That(sim.Entities.Position[1].X.ToDouble(), Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void VolleyHasFiveStaggeredShotsAndEachFlightIsExactlyFortyFiveTicks()
        {
            var sim = Arena();
            var launches = new List<int>(); var impacts = new List<int>();
            var indices = new List<int>();
            for (int t = 0; t <= 110; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    Assert.That(e.Type == SimEventType.Attack && e.Source == 1, Is.False,
                        "Дальнобой не должен дополнительно наносить ближний удар.");
                    if (e.Type == SimEventType.ForestFruitLaunched)
                    {
                        launches.Add(t); indices.Add(e.ActionVariant);
                        Assert.That(sim.TryGetForestFruit(e.Amount, out var fruit), Is.True);
                        Assert.That(fruit.LaunchTick, Is.EqualTo(t));
                        Assert.That(fruit.ImpactTick - fruit.LaunchTick, Is.EqualTo(45));
                        Assert.That(fruit.Target, Is.EqualTo(e.Position));
                    }
                    if (e.Type == SimEventType.ForestFruitImpact) impacts.Add(t);
                }
                if (t == 0)
                {
                    Assert.That(sim.TryGetForestBudAttack(1, out var attack), Is.True);
                    Assert.That(attack.StartTick, Is.Zero);
                    Assert.That(attack.FirstShotTick, Is.EqualTo(24));
                    Assert.That(attack.EndTick, Is.EqualTo(66));
                }
            }
            CollectionAssert.AreEqual(new[] { 24, 30, 36, 42, 48 }, launches);
            CollectionAssert.AreEqual(new[] { 69, 75, 81, 87, 93 }, impacts);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, indices);
            // Пять плодов по 11 — урон одного плода из таблицы видов.
            Assert.That(sim.ForestBudConfig.Damage, Is.EqualTo(EnemyArchetypes.ForestBudDamage));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 5 * EnemyArchetypes.ForestBudDamage));
            Assert.That(sim.ForestFruitActiveCount, Is.Zero);
        }

        [Test]
        public void EveryShotAcquiresCurrentPlayerPositionAndEarlierTargetsStayFixed()
        {
            var sim = Arena();
            var launched = new Dictionary<int, ForestFruitState>();
            for (int t = 0; t <= 50; t++)
            {
                if (t >= 24) sim.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.Ratio(t - 24, 6));
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.ForestFruitLaunched) continue;
                    sim.TryGetForestFruit(e.Amount, out var fruit);
                    launched.Add(e.Amount, fruit);
                    Assert.That(fruit.Target, Is.EqualTo(sim.Entities.Position[0]));
                    Assert.That(fruit.Target.Y, Is.EqualTo(Fix64.FromInt(fruit.ShotIndex)));
                }
                foreach (var pair in launched)
                {
                    Assert.That(sim.TryGetForestFruit(pair.Key, out var current), Is.True);
                    Assert.That(current.Target, Is.EqualTo(pair.Value.Target));
                    Assert.That(current.ImpactTick, Is.EqualTo(pair.Value.ImpactTick));
                }
            }
            Assert.That(launched.Count, Is.EqualTo(5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeathOrStunCancelsUnfiredShotsButAirborneFruitStillLands(bool kill)
        {
            var sim = Arena();
            Until(sim, 31);
            Assert.That(sim.ForestFruitActiveCount, Is.EqualTo(2));
            if (kill) sim.ApplyAbilityDamage(0, 1, 1000, -1, DamageType.Physical);
            else sim.Statuses.ApplyStun(1, sim.Tick + 15);
            int impacts = 0, newLaunches = 0, cancellations = 0;
            while (sim.Tick <= 110)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.ForestFruitLaunched) newLaunches++;
                    if (e.Type == SimEventType.ForestFruitImpact) impacts++;
                    if (e.Type == SimEventType.ForestBudVolleyCancelled) cancellations++;
                }
            }
            Assert.That(newLaunches, Is.Zero);
            Assert.That(impacts, Is.EqualTo(2));
            Assert.That(cancellations, Is.EqualTo(1));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000 - 2 * EnemyArchetypes.ForestBudDamage));
            Assert.That(sim.TryGetForestBudAttack(1, out _), Is.False);
        }

        [Test]
        public void LeavingFixedRedDiskAvoidsImpactWithoutHoming()
        {
            var sim = Arena(); Until(sim, 25);
            var fruit = FirstFruit(sim);
            sim.Statuses.ApplyStun(1, 200);
            sim.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.FromInt(4));
            Until(sim, 69);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.ForestFruitImpact && e.Position.Equals(fruit.Target)));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
        }

        [TestCase(124, true)]
        [TestCase(126, false)]
        public void ImpactUsesTheDisplayedDiskAndPlayerBodyRadius(int hundredths, bool hit)
        {
            var sim = Arena(); Until(sim, 25);
            sim.Statuses.ApplyStun(1, 200);
            sim.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.Ratio(hundredths, 100));
            Until(sim, 85);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(hit ? 10000 - EnemyArchetypes.ForestBudDamage : 10000));
        }

        [Test]
        public void FruitSnapshotsDerivedDamageAtLaunchAndRespectsArmor()
        {
            var sim = Arena();
            sim.Entities.Stats[1].SetBase(StatType.Damage, Fix64.FromInt(20));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.FromInt(100));
            Until(sim, 25);
            Assert.That(FirstFruit(sim).Damage, Is.EqualTo(20));
            sim.Entities.Stats[1].SetBase(StatType.Damage, Fix64.FromInt(200));
            sim.Statuses.ApplyStun(1, 200);
            Until(sim, 85);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(9990));
        }

        [Test]
        public void DeveloperInvulnerabilityKeepsImpactButSuppressesDamage()
        {
            var sim = Arena(); sim.PlayerInvulnerable = true;
            Until(sim, 94);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(10000));
            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e => e.Type == SimEventType.ForestFruitImpact));
        }

        [Test]
        public void ArenaResetDropsFlyingFruitAndAttackClocks()
        {
            var sim = Arena(); Until(sim, 50);
            Assert.That(sim.ForestFruitActiveCount, Is.EqualTo(5));
            sim.SetupTestArena(1);
            Assert.That(sim.ForestFruitActiveCount, Is.Zero);
            Assert.That(sim.TryGetForestBudAttack(1, out _), Is.False);
            for (int i = 0; i < sim.ForestFruitCapacity; i++) Assert.That(sim.TryGetForestFruit(i, out _), Is.False);
            Assert.That(sim.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestGuardian));
            while (sim.Tick <= 110)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Events, Has.None.Matches<SimEvent>(e => e.Type == SimEventType.ForestFruitImpact));
            }
        }

        [Test]
        public void HashIncludesLaunchedTargetEvenAfterPlayerReturnsToSamePosition()
        {
            var a = Arena(); var b = Arena(); Until(a, 24); Until(b, 24);
            b.Entities.Position[0] = new FixVec2(Fix64.Zero, Fix64.One);
            a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
            b.Entities.Position[0] = a.Entities.Position[0];
            b.Entities.Facing[1] = a.Entities.Facing[1];
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
        }

        [Test]
        public void IdenticalInputsWithMultipleBudsRemainDeterministic()
        {
            var a = Arena(8); var b = Arena(8);
            a.PlayerInvulnerable = b.PlayerInvulnerable = true;
            for (int t = 0; t < 600; t++)
            {
                var input = new InputFrame { Flags = (byte)InputFlags.MoveOrder,
                    Aim = new FixVec2(Fix64.FromInt(t / 90 % 2 == 0 ? -3 : 2), Fix64.FromInt(t / 120 % 2 == 0 ? 2 : -2)) };
                a.Step(input); b.Step(input);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "tick " + t);
            }
        }

        [Test]
        public void RangedBodyKeepsDistanceAndDoesNotChaseIntoMelee()
        {
            var sim = new Simulation(42, 4);
            sim.SetupForestBudEncounter(null, 42);
            sim.Entities.NextAttackTick[1] = int.MaxValue;
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(3), Fix64.Zero);
            Until(sim, 60);
            var distance = (sim.Entities.Position[1] - sim.Entities.Position[0]).Length;
            Assert.That(distance.ToDouble(), Is.InRange(4, 4.5));
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(12), Fix64.Zero);
            Until(sim, 270);
            distance = (sim.Entities.Position[1] - sim.Entities.Position[0]).Length;
            Assert.That(distance.ToDouble(), Is.InRange(sim.ForestBudConfig.PreferredRange.ToDouble(), sim.ForestBudConfig.PreferredRange.ToDouble() + .3));
        }

        [Test]
        public void MaximumAttackSpeedCannotOverflowFixedFruitPool()
        {
            var sim = Arena(3); sim.PlayerInvulnerable = true;
            // Пул проверяется на трёх залпах сразу: крупный жетон здесь снят,
            // иначе залпы шли бы по очереди и пул не нагружался бы вовсе.
            sim.BigAttackTokenLimit = 3;
            for (int id = 1; id <= 3; id++) sim.Entities.Stats[id].SetBase(StatType.AttackSpeed, Fix64.FromInt(100));
            int peak = 0;
            for (int t = 0; t < 1200; t++)
            { sim.Step(InputFrame.Empty); peak = System.Math.Max(peak, sim.ForestFruitActiveCount); }
            Assert.That(peak, Is.InRange(15, 30));
        }

        [Test]
        public void DeveloperFightUsesNormalSessionAndCanBeRestarted()
        {
            var camp = PrototypeContent.NewCamp();
            var session = new GameSession(42, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            session.CampLoadout.Put(0, 1);
            int generation = session.Generation;
            session.StartForestBudTest(null, 42, 1);
            Assert.That(session.Mode, Is.EqualTo(GameMode.Rift));
            Assert.That(session.IsDeveloperRun, Is.True);
            Assert.That(session.Generation, Is.GreaterThan(generation));
            Assert.That(session.Run.CountRequiredEnemies(), Is.EqualTo(1));
            Assert.That(session.Run.Sim.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestBud));
            Assert.That(session.Run.Loadout.PoolIndexAt(0), Is.EqualTo(1));
            var first = session.Run.Sim;
            session.StartForestBudTest(null, 42, 2);
            Assert.That(session.Run.Sim, Is.Not.SameAs(first));
            Assert.That(session.Run.CountRequiredEnemies(), Is.EqualTo(2));
            Assert.That(session.Run.Sim.ForestFruitActiveCount, Is.Zero);
        }

        [Test]
        public void AuthoredEncountersAcceptBudAndKeepConfiguredDamageAndBody()
        {
            var bud = new EncounterGroup(EnemyKind.ForestBud, 1, 1, 80);
            var pack = new EncounterPack(1, 100, new[] { bud });
            var exit = new EncounterPack(2, 100, new[] {
                new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, elite: true), bud });
            var settings = new EncounterSettings(new[] { pack }, new[] { pack }, new[] { pack },
                new[] { exit }, 3, 0, 100, Fix64.FromInt(4));
            var modules = PrototypeContent.Modules(); var map = new LayoutMap(modules, 64);
            new LayoutGenerator().Generate(modules, 42, map, 12);
            var sim = new Simulation(42);
            sim.SetupEncounters(map, 42, 100, settings);
            int buds = 0;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (sim.Entities.Kind[id] != EnemyKind.ForestBud) continue;
                buds++;
                // 300 из таблицы видов × 100% уровня × 80% группы.
                Assert.That(sim.Entities.MaxHealth[id], Is.EqualTo(240));
                Assert.That(sim.Entities.Damage[id], Is.EqualTo(11));
                Assert.That(sim.Entities.BodyRadius[id], Is.EqualTo(sim.ForestBudConfig.BodyRadius));
                Assert.That(map.IsWalkable(sim.Entities.Position[id], sim.Entities.BodyRadius[id]), Is.True);
            }
            Assert.That(buds, Is.GreaterThan(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestStageKeepsPlayerAndBudWalkableWithRetreatAndSideRoomAcrossSeeds(bool naturalGlade)
        {
            var modules = PrototypeContent.Modules();
            for (int sample = 0; sample <= 64; sample++)
            {
                ulong seed = sample == 64 ? 20260829UL : (ulong)sample + 1;
                var map = new LayoutMap(modules, 64);
                var settings = new RiftLevelSettings(11, 1, 1, 2, 1, 3, 60,
                    solidEnvironment: naturalGlade, naturalGlade: naturalGlade);
                var levelSeeds = RiftLevelSeeds.ForLevel(seed, 1);
                settings.Generate(new LayoutGenerator(), modules, map, levelSeeds.Layout);
                var sim = new Simulation(seed, 16);
                var plan = sim.SetupForestBudEncounter(map, levelSeeds.Spawns, 1);
                Assert.That(plan.Count, Is.EqualTo(1));
                for (int id = 0; id < sim.Entities.Count; id++)
                    Assert.That(map.IsWalkable(sim.Entities.Position[id], sim.Entities.BodyRadius[id]),
                        Is.True, "seed " + seed + ", entity " + id);
                var distance = (sim.Entities.Position[0] - sim.Entities.Position[1]).Length.ToDouble();
                Assert.That(distance, Is.InRange(5.5, 7.5), "seed " + seed);
                var direction = (sim.Entities.Position[1] - sim.Entities.Position[0]).Normalized();
                var side = new FixVec2(-direction.Y, direction.X);
                var retreat = sim.Entities.Position[1] + direction * Fix64.Ratio(7, 2);
                var clearance = sim.ForestBudConfig.BodyRadius + Fix64.Ratio(1, 2);
                for (int lane = -1; lane <= 1; lane++)
                {
                    var offset = side * Fix64.FromInt(lane);
                    Assert.That(map.IsWalkable(sim.Entities.Position[0] + offset, clearance), Is.True, "seed " + seed);
                    Assert.That(map.CanTravel(sim.Entities.Position[0] + offset, retreat + offset, clearance),
                        Is.True, "seed " + seed + ", lane " + lane);
                }
                if (seed == 20260829)
                {
                    TestContext.WriteLine("Natural=" + naturalGlade + ", hero=" + sim.Entities.Position[0]
                        + ", bud=" + sim.Entities.Position[1] + ", retreat=" + retreat);
                    var start = sim.Entities.Position[1];
                    sim.PlayerInvulnerable = true;
                    for (int tick = 0; tick < 180; tick++)
                    {
                        var input = tick >= 18 && tick < 100
                            ? new InputFrame { Flags = (byte)InputFlags.MoveOrder, Aim = sim.Entities.Position[1] }
                            : InputFrame.Empty;
                        sim.Step(input);
                    }
                    Assert.That((sim.Entities.Position[1] - start).Length.ToDouble(), Is.GreaterThan(1.5),
                        "Тестовый подход героя должен показывать отход моба, а не упор в край карты.");
                    Assert.That(map.IsWalkable(sim.Entities.Position[1], sim.ForestBudConfig.BodyRadius), Is.True);
                }
            }
        }
    }
}
