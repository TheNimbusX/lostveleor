using System;
using Game.Sim;
#if UNITY_5_3_OR_NEWER
using Game.Data;
#endif
using NUnit.Framework;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Game.Tests
{
    public sealed class LocationProfileTests
    {

        [Test]
        public void AuthoredSettings_AreUsedBySessionAndEachRift()
        {
            var modules = PrototypeContent.Modules();
            var settings = new[] {
                new RiftLevelSettings(6, 1, 0, 0, 1, 1, 137),
                new RiftLevelSettings(16, 2, 1, 2, 2, 2, 251)
            };
            var location = new LocationDefinition(123, modules, settings);
            var session = new GameSession(71, PrototypeContent.NewCamp(), modules,
                PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            for (int level = 1; level <= 3; level++)
            {
                var run = session.Run;
                var authored = location.GetLevel(level);
                var seeds = RiftLevelSeeds.ForLevel(session.LastRunSeed, level);
                var preview = new LayoutMap(modules, 64);
                authored.Generate(new LayoutGenerator(), modules, preview, seeds.Layout);
                var sim = new Simulation(session.LastRunSeed, 512);
                authored.Spawn(sim, preview, seeds.Spawns);
                Assert.That(run.Depth, Is.EqualTo(level));
                Assert.That(run.LayoutSeed, Is.EqualTo(seeds.Layout));
                Assert.That(run.SpawnSeed, Is.EqualTo(seeds.Spawns));
                Assert.That(run.Map.Hash(), Is.EqualTo(preview.Hash()));
                Assert.That(run.Sim.Entities.Count, Is.EqualTo(sim.Entities.Count));
                for (int i = 0; i < sim.Entities.Count; i++)
                {
                    Assert.That(run.Sim.Entities.Position[i], Is.EqualTo(sim.Entities.Position[i]));
                    if (i != Simulation.PlayerId)
                        Assert.That(run.Sim.Entities.MaxHealth[i], Is.EqualTo(sim.Entities.MaxHealth[i]));
                }
                for (int i = 1; i < run.Sim.Entities.Count; i++) run.Sim.Entities.Alive[i] = false;
                run.Step(InputFrame.Empty);
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                run.Step(InputFrame.Empty);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
                run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                if (run.Phase == RunPhase.ReplacingAbility)
                    run.Step(new InputFrame { Command = (byte)RunCommand.SalvageAbility });
            }
        }

        [Test]
        public void InvalidSettings_FailBeforeGeneration()
        {
            Assert.Throws<ArgumentException>(() => new RiftLevelSettings(10, 1, 1, 2, 4, 2, 100));
            Assert.Throws<ArgumentException>(() => new RiftLevelSettings(10, 0, 1, 2, 1, 2, 100));
            var location = new LocationDefinition(1, PrototypeContent.Modules(),
                new[] { new RiftLevelSettings(10, 1, 1, 2, 8, 8, 100) });
            Assert.Throws<ArgumentException>(() => location.ValidateCapacity(32));
        }

#if UNITY_5_3_OR_NEWER
#endif
    }
}
