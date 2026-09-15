using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class EncounterTests
    {
        private static EncounterSettings Settings(int level, int branchCount = 3)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var swarm = new EncounterGroup(EnemyKind.ForestRootSwarm, 3, 4, 30, growWithDepth: true);
            return new EncounterSettings(
                new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian, swarm }),
                    new EncounterPack(3, 100, new[] { swarm }), new EncounterPack(4, 100, new[] { guardian, guardian }) },
                new[] { new EncounterPack(5, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, branchCount, branchCount) }) },
                new[] { new EncounterPack(6, 100, new[] {
                    new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, 240, 160, elite: true), swarm }) },
                Math.Min(5, 3 + (level - 1) / 3), (level - 1) / 4, 100 + (level - 1) * 5, Fix64.FromInt(4));
        }

        private static LayoutMap MakeMap(ulong seed, int level)
        {
            var modules = PrototypeContent.Modules();
            var map = new LayoutMap(modules, 64);
            new LayoutGenerator().Generate(modules, seed, map, 10 + level);
            return map;
        }

        [Test]
        public void Packs_FollowRoute_ProtectCaches_AndGuaranteeEliteAtExit()
        {
            foreach (int level in new[] { 1, 5, 10 })
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    var map = MakeMap(seed, level);
                    var sim = new Simulation(seed, 512);
                    EncounterPlan plan = null;
                    Assert.DoesNotThrow(() => plan = sim.SetupEncounters(map, seed, 100 + 25 * level, Settings(level)), $"seed {seed}, level {level}");
                    int exits = 0, branches = 0;
                    for (int e = 0; e < plan.Count; e++)
                    {
                        var encounter = plan.Get(e);
                        Assert.That(encounter.EnemyCount, Is.GreaterThan(0));
                        if (encounter.Role == EncounterRole.RewardBranch)
                        {
                            branches++;
                            Assert.That(encounter.Module, Is.EqualTo(map.GetRewardBranch(encounter.Branch)));
                        }
                        else Assert.That(map.Routes.IsMainModule(encounter.Module), Is.True);
                        if (encounter.Role == EncounterRole.Introduction) Assert.That(encounter.EnemyCount, Is.EqualTo(1));
                        if (encounter.Role == EncounterRole.ExitGuard)
                        {
                            exits++;
                            Assert.That(map.IsExit(encounter.Module), Is.True);
                            Assert.That(plan.IsElite(encounter.FirstEntity), Is.True);
                            Assert.That(sim.Entities.Kind[encounter.FirstEntity], Is.EqualTo(EnemyKind.ForestGuardian));
                            Assert.That(sim.Entities.MaxHealth[encounter.FirstEntity], Is.GreaterThan(100 + 25 * level));
                        }
                    }
                    Assert.That(exits, Is.EqualTo(map.ExitCount));
                    Assert.That(branches, Is.EqualTo(map.RewardBranchCount));
                    for (int i = 1; i < sim.Entities.Count; i++)
                    {
                        Assert.That(plan.ForEntity(i), Is.GreaterThanOrEqualTo(0));
                        Assert.That(map.IsWalkable(sim.Entities.Position[i], sim.Entities.BodyRadius[i]), Is.True);
                        Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint),
                            Is.GreaterThanOrEqualTo(LayoutRoutes.SafeSpawnRadius * LayoutRoutes.SafeSpawnRadius));
                        for (int j = 1; j < i; j++)
                        {
                            var radius = sim.Entities.BodyRadius[i] + sim.Entities.BodyRadius[j];
                            Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], sim.Entities.Position[j]), Is.GreaterThanOrEqualTo(radius * radius));
                        }
                    }
                }
        }

        [Test]
        public void RequiredFightsUnlockExit_WithoutKillingCacheGuards()
        {
            var modules = PrototypeContent.Modules();
            var location = new LocationDefinition(1, modules,
                new[] { new RiftLevelSettings(16, 1, 1, 2, 0, 0, 150, Settings(1)) });
            var run = new RiftRun(new Simulation(17, 512), modules, PrototypeContent.Items(), PrototypeContent.ItemBaseIds(), location: location);
            run.StartRun();
            Assert.That(run.Encounters, Is.Not.Null);
            for (int e = 0; e < run.Encounters.Count; e++)
            {
                var encounter = run.Encounters.Get(e);
                if (encounter.Role == EncounterRole.RewardBranch) continue;
                for (int i = 0; i < encounter.EnemyCount; i++) run.Sim.Entities.Alive[encounter.FirstEntity + i] = false;
            }
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0));
            run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            Assert.That(run.CountRequiredEnemies(), Is.GreaterThan(0));
        }

    }
}
