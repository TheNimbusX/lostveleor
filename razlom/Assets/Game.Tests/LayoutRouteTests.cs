using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class LayoutRouteTests
    {
        private static LayoutMap Map(ulong seed, int loops = 0)
        {
            var modules = PrototypeContent.Modules();
            var map = new LayoutMap(modules, 64);
            new LayoutGenerator().Generate(modules, seed, map, 16, maxLoops: loops);
            return map;
        }

        [Test]
        public void Routes_StayOnFloor_AndChooseFarthestLeaf()
        {
            for (ulong seed = 1; seed <= 150; seed++)
            {
                var map = Map(seed);
                Assert.That(map.ContainsWorld(0, map.EntryPoint), Is.True);
                Assert.That(map.IsWalkable(map.EntryPoint, Fix64.Ratio(62, 100)), Is.True);
                Assert.That(map.Routes.ExitDistanceCells, Is.GreaterThan(10), $"seed {seed}");
                for (int m = 1; m < map.PlacedCount; m++)
                    if (!map.HasChild(m))
                        Assert.That(map.Routes.ExitDistanceCells, Is.GreaterThanOrEqualTo(map.Routes.DistanceToModule(m)));
                for (int c = 0; c < map.Routes.CellCount; c++)
                {
                    if (!map.Routes.IsRoadCell(c)) continue;
                    var a = map.Routes.GetCell(c).Center;
                    int parent = map.Routes.ParentCell(c);
                    var b = parent < 0 ? a : map.Routes.GetCell(parent).Center;
                    Assert.That(map.IsWalkable((a + b) * Fix64.Ratio(1, 2), Fix64.Ratio(62, 100)), Is.True);
                }
                for (int b = 0; b < map.RewardBranchCount; b++)
                    Assert.That(map.Routes.IsMainModule(map.GetRewardBranch(b)), Is.False);
            }
        }

        [Test]
        public void SafeEntry_EnemiesDoNotNoticeIdlePlayer()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var map = Map(seed, 2);
                var sim = new Simulation(seed, 512);
                sim.SetupRift(map, seed, 2, 4, 100);
                Assert.That(sim.Entities.Position[0], Is.EqualTo(map.EntryPoint));
                for (int i = 1; i < sim.Entities.Count; i++)
                    Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint),
                        Is.GreaterThanOrEqualTo(LayoutRoutes.SafeSpawnRadius * LayoutRoutes.SafeSpawnRadius));
                for (int tick = 0; tick < 60; tick++) sim.Step(InputFrame.Empty);
                for (int i = 1; i < sim.Entities.Count; i++) Assert.That(sim.Entities.Aggro[i], Is.False);
            }
        }

        private static RiftRun BranchRun()
        {
            var modules = PrototypeContent.Modules();
            var location = new LocationDefinition(1, modules,
                new[] { new RiftLevelSettings(16, 1, 1, 2, 2, 2, 100) });
            for (ulong seed = 1; seed < 100; seed++)
            {
                var run = new RiftRun(new Simulation(seed, 512), modules, PrototypeContent.Items(),
                    PrototypeContent.ItemBaseIds(), location: location);
                run.StartRun();
                if (run.Map.RewardBranchCount > 0 && run.BranchGuardsAlive(0) > 0) return run;
            }
            Assert.Fail("No guarded bonus branch found");
            return null;
        }

        [Test]
        public void BonusGuard_DoesNotBlockExit_AndLootNeedsGuardDeath()
        {
            var run = BranchRun();
            int branch = run.Map.GetRewardBranch(0);
            var entities = run.Sim.Entities;
            entities.Position[0] = run.Map.CenterOf(branch);
            ulong loot = run.Sim.Rng.Loot.State, affix = run.Sim.Rng.Affix.State;
            run.Step(InputFrame.Empty);
            Assert.That(run.TakenRewardCount, Is.Zero, "Guarded loot must not be collected");
            for (int i = 1; i < entities.Count; i++)
                if (!run.Map.ContainsWorld(branch, entities.Position[i])) entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0));
            for (int i = 1; i < entities.Count; i++) entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            Assert.That(run.TakenRewardCount, Is.EqualTo(1));
            Assert.That(run.GetTaken(0).Kind, Is.EqualTo(RewardKind.Item));
            Assert.That(run.BranchesClaimed, Is.EqualTo(1));
            run.Step(InputFrame.Empty);
            Assert.That(run.TakenRewardCount, Is.EqualTo(1), "Loot can only be claimed once");
            Assert.That(run.Sim.Rng.Loot.State, Is.EqualTo(loot));
            Assert.That(run.Sim.Rng.Affix.State, Is.EqualTo(affix));
            entities.Position[0] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
        }

        [Test]
        public void WalkingDistance_UsesActualShortcutInsteadOfParentDepth()
        {
            var module = new ModuleDefinition("route.cell", 1, 1, new ModuleConnector[0]);
            var map = new LayoutMap(new ModuleSet(new[] { module }), 4);
            map.TryPlace(0, 0, -1, -1);
            map.TryPlace(0, 0, 0, -1, 0);
            map.TryPlace(0, 0, 0, 0, 1);
            map.TryPlace(0, 0, -1, 0, 2);
            var routes = new LayoutRoutes(map);
            Assert.That(map.DepthOf(3), Is.EqualTo(3));
            Assert.That(routes.DistanceToModule(3), Is.EqualTo(1));
            Assert.That(routes.CellAt(map.CenterOf(0)), Is.EqualTo(0), "Negative coordinates must round down");
        }

        [Test]
        public void LivingBonusGuards_CanBeSkippedForTheNextRift()
        {
            var run = BranchRun();
            int branch = run.Map.GetRewardBranch(0);
            var entities = run.Sim.Entities;
            for (int i = 1; i < entities.Count; i++)
                if (!run.Map.ContainsWorld(branch, entities.Position[i])) entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            entities.Position[0] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            Assert.That(run.BranchGuardsAlive(0), Is.GreaterThan(0));
            Assert.That(run.TakenRewardCount, Is.Zero);
        }

        [Test]
        public void ExitTriggersAtEndpoint_NotAtRoomCenter()
        {
            var map = Map(55);
            var sim = new Simulation(1, 64);
            sim.SetupRift(map, 1, 0, 0, 100);
            sim.Entities.Position[0] = map.CenterOf(map.GetExit(0));
            Assert.That(sim.PlayerReachedExit(map), Is.False);
            sim.Entities.Position[0] = map.ExitPoint(0);
            Assert.That(sim.PlayerReachedExit(map), Is.True);
        }
    }
}
