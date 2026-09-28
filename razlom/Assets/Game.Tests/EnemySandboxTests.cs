using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class EnemySandboxTests
    {
        private static GameSession Session(int capacity = 64)
            => new GameSession(42, PrototypeContent.NewCamp(), Simulation.EnemySandboxModules(),
                PrototypeContent.ItemBaseIds(), capacity);
        private static EnemySandboxSpawn Spawn(EnemyKind kind, int count = 1)
            => new EnemySandboxSpawn(kind, new FixVec2(Fix64.FromInt(8), Fix64.Zero), count);
        private static void Step(GameSession session, int count = 1)
        { for (int i = 0; i < count; i++) session.Step(InputFrame.Empty); }

        [Test]
        public void EveryDefinedEnemyHasRealStatsAndSandboxNeverOffersRewards()
        {
            var session = Session();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                if (!EnemyArchetypes.IsDefined(kind)) continue;
                session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(kind) });
                var sim = session.Run.Sim;
                Assert.That(sim.Entities.Kind[1], Is.EqualTo(kind));
                Assert.That(sim.Entities.Damage[1], Is.GreaterThan(0));
                Assert.That(sim.Entities.MaxHealth[1], Is.EqualTo(sim.ArchetypeHealth(kind)));
                sim.QueueEnemySandbox(EnemySandboxAction.Clear);
                Step(session, 2);
                Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.Clearing));
                Assert.That(session.Run.TakenRewardCount, Is.Zero);
                Assert.That(session.Run.Gold, Is.Zero);
            }
        }

        [Test]
        public void SpawnWaitsForTickAndEmitsExactlyOneSpawnEvent()
        {
            var session = Session(); session.StartEnemySandbox(42, FixVec2.Zero, Array.Empty<EnemySandboxSpawn>());
            var sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Spawn, Spawn(EnemyKind.ForestGuardian));
            Assert.That(sim.Entities.Count, Is.EqualTo(1));
            Step(session);
            Assert.That(sim.Entities.Count, Is.EqualTo(2));
            int spawns = 0; foreach (var e in sim.Events) if (e.Type == SimEventType.Spawn) spawns++;
            Assert.That(spawns, Is.EqualTo(1));
        }

        [Test]
        public void RemoveDoesNotSplitButKillDoesAndClearCancelsPendingBirth()
        {
            var session = Session(); session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestSplitter) });
            var sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Remove, entity: 1); Step(session, 20);
            Assert.That(sim.Entities.Count, Is.EqualTo(2)); Assert.That(sim.CountAliveEnemies(), Is.Zero);
            session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestSplitter) }); sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Kill, entity: 1); Step(session);
            Assert.That(sim.HasPendingSplits, Is.True);
            Step(session, Simulation.SplitterDeathReleaseTicks + 1);
            Assert.That(sim.CountAliveEnemies(), Is.EqualTo(2));
            session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestSplitter) }); sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Kill, entity: 1); Step(session);
            sim.QueueEnemySandbox(EnemySandboxAction.Clear); Step(session, 20);
            Assert.That(sim.HasPendingSplits, Is.False); Assert.That(sim.CountAliveEnemies(), Is.Zero);
        }

        [Test]
        public void CapacityReservesPendingChildrenAndRejectsWithoutPartialSpawn()
        {
            var session = Session(8);
            session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestSplitter, 2) });
            var sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Kill, entity: 1); Step(session);
            sim.QueueEnemySandbox(EnemySandboxAction.Spawn, Spawn(EnemyKind.ForestGuardian, 2)); Step(session);
            Assert.That(sim.Entities.Count, Is.EqualTo(3));
            Assert.That(sim.EnemySandboxError, Is.Not.Null);
        }

        [Test]
        public void RepeatedSpawnsCannotConsumeTheChildrenSlotsOfALivingParent()
        {
            var session = Session(8);
            session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestSplitter) });
            var sim = session.Run.Sim;
            sim.QueueEnemySandbox(EnemySandboxAction.Spawn, Spawn(EnemyKind.ForestGuardian, 4));
            sim.QueueEnemySandbox(EnemySandboxAction.Spawn, Spawn(EnemyKind.ForestGuardian));
            Step(session);
            Assert.That(sim.Entities.Count, Is.EqualTo(6), "four guardians fit; the fifth would steal a child slot");
            Assert.That(sim.EnemySandboxError, Is.Not.Null);
            sim.QueueEnemySandbox(EnemySandboxAction.Kill, entity: 1);
            Step(session);
            Assert.That(sim.PendingSplitCount, Is.EqualTo(1));
            Step(session, Simulation.SplitterDeathReleaseTicks + 1);
            Assert.That(sim.HasPendingSplits, Is.False);
            Assert.That(sim.Entities.Count, Is.EqualTo(sim.Entities.Capacity));
            Assert.That(sim.Entities.Kind[6], Is.EqualTo(EnemyKind.ForestSplitling));
            Assert.That(sim.Entities.Kind[7], Is.EqualTo(EnemyKind.ForestSplitling));
        }

        [Test]
        public void ResetRepeatsInitialStateAndCombatDoesNotChangeCamp()
        {
            var session = Session(); var spawns = new[] { Spawn(EnemyKind.ForestGuardian) };
            session.StartEnemySandbox(42, FixVec2.Zero, spawns);
            ulong before = session.Run.Sim.StateHash(), camp = 0; session.Camp.HashInto(ref camp);
            session.Run.Sim.QueueEnemySandbox(EnemySandboxAction.Kill, entity: 1); Step(session, 5);
            ulong afterCamp = 0; session.Camp.HashInto(ref afterCamp);
            Assert.That(afterCamp, Is.EqualTo(camp));
            int generation = session.Generation;
            session.StartEnemySandbox(42, FixVec2.Zero, spawns);
            Assert.That(session.Generation, Is.EqualTo(generation + 1));
            Assert.That(session.Run.Sim.StateHash(), Is.EqualTo(before));
        }

        [Test]
        public void ResetRebuildsNavigationForTheCurrentAuthoredObstacles()
        {
            var session = Session();
            var from = new FixVec2(Fix64.FromInt(7), Fix64.One);
            var hero = new FixVec2(Fix64.FromInt(-7), Fix64.One);
            var obstacle = new FixVec2(Fix64.One, Fix64.One);
            session.StartEnemySandbox(42, hero, Array.Empty<EnemySandboxSpawn>(),
                new[] { new LayoutObstacle(obstacle, Fix64.FromInt(2), 0) });
            var map = session.Run.Map;
            Assert.That(map.Routes, Is.Not.Null, "the sandbox must enable the same route navigation as the arena");
            Assert.That(map.Routes.CellCount, Is.EqualTo(400));
            Assert.That(session.Run.Sim.Entities.Position[0], Is.EqualTo(hero));
            var routes = map.Routes;
            var previousGrid = new EnemyNavGrid(map);
            Assert.That(map.CanTravel(from, hero, Fix64.Ratio(1, 2)), Is.False);
            Assert.That(previousGrid.TryHeading(from, hero, Fix64.Ratio(1, 2), out var detour), Is.True);
            Assert.That(detour.Y.Raw, Is.Not.Zero, "navigation must route around the authored obstacle");

            var movedObstacle = new FixVec2(Fix64.One, Fix64.FromInt(9));
            session.StartEnemySandbox(42, hero, Array.Empty<EnemySandboxSpawn>(),
                new[] { new LayoutObstacle(movedObstacle, Fix64.FromInt(2), 0) });
            map = session.Run.Map;
            Assert.That(map.Routes, Is.Not.SameAs(routes));
            Assert.That(previousGrid.Matches(map), Is.False, "the same obstacle count must not reuse old route data");
            Assert.That(map.IsWalkable(obstacle, Fix64.Ratio(1, 2)), Is.True);
            Assert.That(map.IsWalkable(movedObstacle, Fix64.Ratio(1, 2)), Is.False);
            Assert.That(map.CanTravel(from, hero, Fix64.Ratio(1, 2)), Is.True);
            var currentGrid = new EnemyNavGrid(map);
            Assert.That(currentGrid.TryHeading(from, hero, Fix64.Ratio(1, 2), out var direct), Is.True);
            Assert.That(direct.Y.Raw, Is.Zero, "reset must use the moved obstacle, leaving this route straight");
        }

        [Test]
        public void AuthoredObstacleBlocksMapAndSpawnInsideItIsClamped()
        {
            var session = Session(); var center = new FixVec2(Fix64.FromInt(8), Fix64.Zero);
            session.StartEnemySandbox(42, FixVec2.Zero, new[] { Spawn(EnemyKind.ForestStonehoof) },
                new[] { new LayoutObstacle(center, Fix64.FromInt(2), 0) });
            Assert.That(session.Run.Map.IsWalkable(center, Fix64.One), Is.False);
            Assert.That(session.Run.Map.IsWalkable(session.Run.Sim.Entities.Position[1],
                session.Run.Sim.Entities.BodyRadius[1]), Is.True);
        }
    }
}
