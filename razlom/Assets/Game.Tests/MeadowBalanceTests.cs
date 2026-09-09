#if UNITY_5_3_OR_NEWER
using System;
using Game.Data;
using Game.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MeadowBalanceTests
    {
        [Test]
        public void LaterLevels_AddPopulationAndElites_WithoutHealthSponges()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            int[] population = new int[3];
            int[] levels = { 1, 4, 9 };
            for (int tier = 0; tier < levels.Length; tier++)
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var settings = profile.GetLevel(levels[tier]);
                    var seeds = RiftLevelSeeds.ForLevel(seed, levels[tier]);
                    var map = new LayoutMap(profile.Modules, profile.MaxModules);
                    settings.Generate(new LayoutGenerator(), profile.Modules, map, seeds.Layout);
                    var sim = new Simulation(seed, 512);
                    var plan = settings.Spawn(sim, map, seeds.Spawns);
                    for (int i = 1; i < sim.Entities.Count; i++)
                        Assert.That(FixVec2.DistanceSq(sim.Entities.Position[i], map.EntryPoint) >= Fix64.FromInt(196), Is.True);
                    population[tier] += sim.Entities.Count - 1;
                    Assert.That(sim.Entities.MaxHealth[0], Is.EqualTo(150));
                    for (int e = 0; e < plan.Count; e++)
                    {
                        var site = plan.Get(e);
                        int elite = 0;
                        for (int i = site.FirstEntity; i < site.FirstEntity + site.EnemyCount; i++)
                        {
                            if (plan.IsElite(i)) { elite++; continue; }
                            int hits = (sim.Entities.Health[i] + sim.Entities.Damage[0] - 1) / sim.Entities.Damage[0];
                            Assert.That(hits, Is.LessThanOrEqualTo(4), "Normal mobs should die in at most four base attacks");
                            if (sim.Entities.Kind[i] == EnemyKind.ForestGuardian)
                                Assert.That(sim.Entities.Damage[i], Is.GreaterThanOrEqualTo(12));
                        }
                        if (site.Role == EncounterRole.MainPath)
                            Assert.That(elite, Is.EqualTo(tier == 0 ? 0 : tier == 1 ? 1 : 2), "seed " + seed);
                    }
                }
            TestContext.WriteLine("Enemies across 30 seeds, levels 1/4/9: " + string.Join(", ", population));
            Assert.That(population[1], Is.GreaterThan(population[0]));
            Assert.That(population[2], Is.GreaterThan(population[1]));
            Assert.That(population[2], Is.GreaterThan(population[0] * 3 / 2));
        }

        [Test]
        public void StandingStillAtBoss_IsLethalWithinTwentySeconds()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var session = new GameSession(1, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            session.StartDeveloperRift(profile, 10, true, 42);
            for (int tick = 0; tick < 600 && session.Mode == GameMode.Rift; tick++) session.Step(InputFrame.Empty);
            TestContext.WriteLine($"Health {session.Run.Sim.Entities.Health[0]}, boss damage {session.Run.Sim.Entities.Damage[session.Run.BossId]}, aggro {session.Run.Sim.Entities.Aggro[session.Run.BossId]}, player {session.Run.Sim.Entities.Position[0]}, boss {session.Run.Sim.Entities.Position[session.Run.BossId]}");
            Assert.That(session.Mode, Is.EqualTo(GameMode.Summary));
            Assert.That(session.LastRun.Outcome, Is.EqualTo(RunOutcome.Died));
        }

        [Test]
        public void LevelRanges_RejectGapsAndInvalidBounds()
        {
            var profile = ScriptableObject.CreateInstance<EncounterProfileAsset>();
            try
            {
                foreach (var pack in profile.MainPath) pack.MinLevel = 4;
                Assert.Throws<ArgumentException>(() => profile.ToDefinition(1));
                Assert.DoesNotThrow(() => profile.ToDefinition(4));
                profile.MainPath[0].MaxLevel = 2;
                Assert.Throws<ArgumentException>(() => profile.ToDefinition(4));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void DeveloperImmortality_BlocksAttacksAbilitiesAndBurn_AndCanBeDisabled()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var session = new GameSession(1, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            Assert.Throws<InvalidOperationException>(() => session.SetDeveloperInvulnerable(true));
            session.StartDeveloperRift(profile, 10, true, 42);
            var sim = session.Run.Sim;
            int health = sim.Entities.Health[0];
            ulong normalHash = sim.StateHash();
            session.SetDeveloperInvulnerable(true);
            Assert.That(sim.StateHash(), Is.Not.EqualTo(normalHash));
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Physical);
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Fire, overTime: true);
            for (int tick = 0; tick < 600; tick++) session.Step(InputFrame.Empty);
            Assert.That(session.Mode, Is.EqualTo(GameMode.Rift));
            Assert.That(sim.Entities.Alive[0], Is.True);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(health));
            session.SetDeveloperInvulnerable(false);
            sim.ApplyAbilityDamage(session.Run.BossId, 0, 1000000, 0, DamageType.Physical);
            Assert.That(sim.Entities.Alive[0], Is.False);
        }

        [Test]
        public void DeveloperImmortality_PersistsAcrossTestJumps_ButNotNormalRuns()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var session = new GameSession(1, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            session.StartDeveloperRift(profile, 10, true, 42);
            session.SetDeveloperInvulnerable(true);
            session.StartDeveloperRift(profile, 1, false, 42);
            Assert.That(session.DeveloperInvulnerable, Is.True);
            session.ReturnToCamp();
            Assert.That(session.DeveloperInvulnerable, Is.False);
            session.EnterRift();
            Assert.That(session.DeveloperInvulnerable, Is.False);
            session.SetDeveloperInvulnerable(true);
            Assert.That(session.DeveloperInvulnerable, Is.True);
            session.SetDeveloperInvulnerable(false);
            session.Run.Sim.ApplyAbilityDamage(1, 0, 1000000, 0, DamageType.Physical);
            Assert.That(session.Run.Sim.Entities.Alive[0], Is.False);
        }

        [Test]
        public void Immortality_CanBeEnabledInNormalRun_AndSurvivesAllTenLevels()
        {
            var profile = Resources.Load<LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            var session = new GameSession(71, PrototypeContent.NewCamp(), profile.Modules,
                PrototypeContent.ItemBaseIds(), location: profile);
            session.EnterRift();
            var initialRun = session.Run;
            session.SetDeveloperInvulnerable(true);
            Assert.That(session.Run, Is.SameAs(initialRun));
            for (int level = 1; level <= 10; level++)
            {
                var run = session.Run;
                Assert.That(run.Depth, Is.EqualTo(level));
                Assert.That(session.DeveloperInvulnerable, Is.True);
                run.Sim.ApplyAbilityDamage(1, 0, 1000000, 0, DamageType.Physical);
                Assert.That(run.Sim.Entities.Alive[0], Is.True);
                for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                session.Step(InputFrame.Empty);
                run.Sim.Entities.Position[0] = run.Map.ExitPoint(0);
                session.Step(InputFrame.Empty);
                session.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
            }
            Assert.That(session.Mode, Is.EqualTo(GameMode.Summary));
            Assert.That(session.LastRun.ItemsKept, Is.Zero);
            session.ReturnToCamp();
            Assert.That(session.DeveloperInvulnerable, Is.False);
        }
    }
}
#endif
