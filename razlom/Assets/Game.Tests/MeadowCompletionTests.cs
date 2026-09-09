#if UNITY_5_3_OR_NEWER
using Game.Data;
using Game.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MeadowCompletionTests
    {
        private static GameSession Session(ulong seed)
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            Assert.That(profile.CompleteAtEnd, Is.True);
            Assert.That(profile.LevelCount, Is.EqualTo(10));
            var session = new GameSession(seed, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            session.EnterRift();
            return session;
        }

        private static void ReachReward(GameSession session)
        {
            var run = session.Run;
            for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
            session.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
        }

        private static void Choose(GameSession session)
            => session.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });

        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(999UL)]
        public void TenLevels_EndAfterFinalReward_AndCanBeRepeated(ulong seed)
        {
            var session = Session(seed);
            for (int level = 1; level <= 10; level++)
            {
                var run = session.Run;
                Assert.That(run.Depth, Is.EqualTo(level));
                Assert.That(run.TotalLevels, Is.EqualTo(10));
                Assert.That(run.BossId >= 0, Is.EqualTo(level == 10));
                var seeds = RiftLevelSeeds.ForLevel(session.LastRunSeed, level);
                Assert.That(run.LayoutSeed, Is.EqualTo(seeds.Layout));
                Assert.That(run.SpawnSeed, Is.EqualTo(seeds.Spawns));
                if (level == 10)
                {
                    int boss = run.BossId;
                    for (int i = 1; i < run.Sim.Entities.Count; i++)
                        if (i != boss) run.Sim.Entities.Alive[i] = false;
                    session.Step(InputFrame.Empty);
                    Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "Boss must gate completion");
                    Assert.That(run.CountRequiredEnemies(), Is.EqualTo(1));
                    Assert.That(run.Sim.Entities.MaxHealth[boss], Is.EqualTo(run.LevelSettings.EnemyHealth * 6));
                }
                ReachReward(session);
                int rewards = run.TakenRewardCount;
                Choose(session);
                Assert.That(run.TakenRewardCount, Is.EqualTo(rewards + 1));
                Assert.That(run.RiftsCleared, Is.EqualTo(level));
            }
            Assert.That(session.Mode, Is.EqualTo(GameMode.Summary));
            Assert.That(session.LastRun.Outcome, Is.EqualTo(RunOutcome.Completed));
            Assert.That(session.LastRun.Depth, Is.EqualTo(10));
            Assert.That(session.Run.Depth, Is.EqualTo(10), "Level eleven must not be generated");
            ulong hash = session.Run.Hash();
            Choose(session);
            Assert.That(session.Run.Hash(), Is.EqualTo(hash), "Summary cannot award twice");
            session.ReturnToCamp();
            Assert.That(session.Mode, Is.EqualTo(GameMode.Camp));
            session.EnterRift();
            Assert.That(session.Run.Depth, Is.EqualTo(1));
            Assert.That(session.Run.TakenRewardCount, Is.Zero);
            Assert.That(session.Run.BossEnraged, Is.False);
        }

        [Test]
        public void BossEnragesOnce_DeathStillWins_AndReplaysMatch()
        {
            var a = Session(17); var b = Session(17);
            for (int level = 1; level < 10; level++)
            {
                ReachReward(a); ReachReward(b); Choose(a); Choose(b);
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            }
            int id = a.Run.BossId;
            var damage = a.Run.Sim.Entities.Damage[id];
            foreach (var session in new[] { a, b })
            {
                var run = session.Run;
                run.Sim.Entities.Health[run.BossId] = run.Sim.Entities.MaxHealth[run.BossId] / 2;
                session.Step(InputFrame.Empty);
                Assert.That(run.BossEnraged, Is.True);
                Assert.That(run.Sim.Entities.Damage[run.BossId], Is.EqualTo((damage * 13 + 5) / 10));
                session.Step(InputFrame.Empty);
                Assert.That(run.Sim.Entities.Damage[run.BossId], Is.EqualTo((damage * 13 + 5) / 10));
            }
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            a.Run.Sim.Entities.Alive[0] = false;
            a.Run.Sim.Entities.Alive[id] = false;
            a.Step(InputFrame.Empty);
            Assert.That(a.LastRun.Outcome, Is.EqualTo(RunOutcome.Died));
            Assert.That(a.LastRun.RiftsCleared, Is.EqualTo(9));
        }

        [Test]
        public void LeavingFinalReward_IsNotVictory()
        {
            var session = Session(51);
            for (int level = 1; level < 10; level++) { ReachReward(session); Choose(session); }
            ReachReward(session);
            session.Step(new InputFrame { Command = (byte)RunCommand.Leave });
            Assert.That(session.LastRun.Outcome, Is.EqualTo(RunOutcome.Left));
            Assert.That(session.Run.TakenRewardCount, Is.EqualTo(9));
        }

        [Test]
        public void BossSpawnsAlone_InConnectedDedicatedArenaAcrossSeeds()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var final = profile.GetLevel(10);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var seeds = RiftLevelSeeds.ForLevel(seed, 10);
                var map = new LayoutMap(profile.Modules, profile.MaxModules);
                final.Generate(new LayoutGenerator(), profile.Modules, map, seeds.Layout);
                var a = new Simulation(seed, 512); var b = new Simulation(seed, 512);
                var plan = final.Spawn(a, map, seeds.Spawns);
                final.Spawn(b, map, seeds.Spawns);
                Assert.That(map.PlacedCount, Is.EqualTo(4));
                Assert.That(map.RewardBranchCount, Is.Zero);
                Assert.That(a.Entities.Count, Is.EqualTo(2));
                Assert.That(a.Entities.Position[plan.BossId], Is.EqualTo(map.CenterOf(map.GetPlaced(map.GetExit(0)).Parent)));
                Assert.That(FixVec2.DistanceSq(a.Entities.Position[plan.BossId], map.EntryPoint) >= Fix64.FromInt(196), Is.True);
                Assert.That(FixVec2.DistanceSq(a.Entities.Position[plan.BossId], map.ExitPoint(0)) >= Fix64.FromInt(196), Is.True);
                Assert.That(plan.Get(plan.ForEntity(plan.BossId)).Module, Is.Not.EqualTo(map.GetExit(0)));
                Assert.That(plan.BossId, Is.GreaterThan(0), "seed " + seed);
                Assert.That(a.Entities.Kind[plan.BossId], Is.EqualTo(EnemyKind.ForestGuardian));
                Assert.That(a.Entities.Count, Is.EqualTo(b.Entities.Count));
                for (int i = 0; i < a.Entities.Count; i++)
                {
                    Assert.That(a.Entities.Position[i], Is.EqualTo(b.Entities.Position[i]));
                    if (i != plan.BossId) Assert.That(a.Entities.Health[i], Is.EqualTo(b.Entities.Health[i]));
                }
            }
        }

        [Test]
        public void DeveloperJump_UsesLevelSeeds_AndSafeBossApproach()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var session = Session(71);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                session.StartDeveloperRift(profile, 10, true, seed);
                var run = session.Run;
                Assert.That(session.IsDeveloperRun, Is.True);
                Assert.That(run.Depth, Is.EqualTo(10));
                Assert.That(run.RiftsCleared, Is.Zero);
                Assert.That(run.TakenRewardCount, Is.Zero);
                var seeds = RiftLevelSeeds.ForLevel(seed, 10);
                Assert.That(run.LayoutSeed, Is.EqualTo(seeds.Layout));
                Assert.That(run.SpawnSeed, Is.EqualTo(seeds.Spawns));
                var player = run.Sim.Entities.Position[0];
                Assert.That(run.Map.IsWalkable(player, run.Sim.Entities.BodyRadius[0]), Is.True);
                var distance = FixVec2.DistanceSq(player, run.Sim.Entities.Position[run.BossId]);
                Assert.That(distance >= Fix64.FromInt(16) && distance <= Fix64.FromInt(100), Is.True, "seed " + seed);
                for (int i = 1; i < run.Sim.Entities.Count; i++)
                {
                    var spacing = run.Sim.Entities.BodyRadius[i] + run.Sim.Entities.BodyRadius[0];
                    Assert.That(FixVec2.DistanceSq(player, run.Sim.Entities.Position[i]) > spacing * spacing, Is.True);
                }
            }
        }

        [Test]
        public void DeveloperTravel_DoesNotAwardLootOrAdvanceNormalSeedStream()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var a = Session(83); var b = Session(83);
            ulong before = 0; a.Camp.HashInto(ref before);
            a.StartDeveloperRift(profile, 10, true, 42);
            ReachReward(a); Choose(a);
            Assert.That(a.Mode, Is.EqualTo(GameMode.Summary));
            Assert.That(a.LastRun.ItemsKept, Is.Zero);
            ulong after = 0; a.Camp.HashInto(ref after);
            Assert.That(after, Is.EqualTo(before));
            a.ReturnToCamp();
            Assert.That(a.IsDeveloperRun, Is.False);
            a.EnterRift(); b.EnterRift();
            Assert.That(a.LastRunSeed, Is.EqualTo(b.LastRunSeed));
            Assert.That(a.Run.Depth, Is.EqualTo(1));
            a.StartDeveloperRift(profile, 3, false, 42);
            Assert.That(a.Run.Depth, Is.EqualTo(3));
            Assert.That(a.Run.Sim.Entities.Position[0], Is.EqualTo(a.Run.Map.EntryPoint));
            var run = a.Run;
            Assert.Throws<System.ArgumentException>(() => a.StartDeveloperRift(profile, 11, false, 42));
            Assert.Throws<System.ArgumentException>(() => a.StartDeveloperRift(profile, 1, true, 42));
            Assert.That(a.Run, Is.SameAs(run));
        }
    }
}
#endif
