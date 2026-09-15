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

    }
}
#endif
