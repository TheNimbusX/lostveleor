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
        {
            session.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
            // Способность при полной панели разбирается: петля локации проверяет уровни, а не набор.
            if (session.Mode == GameMode.Rift && session.Run.Phase == RunPhase.ReplacingAbility)
                session.Step(new InputFrame { Command = (byte)RunCommand.SalvageAbility });
        }

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

    }
}
#endif
