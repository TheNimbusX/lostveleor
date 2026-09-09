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
        public void DefaultSettings_PreserveOldGenerationAndSpawnRolls()
        {
            var modules = PrototypeContent.Modules();
            var generator = new LayoutGenerator();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var streams = new RngStreams(seed);
                for (int depth = 1; depth <= 10; depth++)
                {
                    ulong layout = LayoutGenerator.RollSeed(ref streams.Layout);
                    ulong spawn = LayoutGenerator.RollSeed(ref streams.Spawns);
                    var oldMap = new LayoutMap(modules, 64);
                    generator.Generate(modules, layout, oldMap, 10 + depth);
                    var newMap = new LayoutMap(modules, 64);
                    var settings = RiftLevelSettings.Prototype(depth);
                    settings.Generate(generator, modules, newMap, layout);
                    Assert.That(newMap.Hash(), Is.EqualTo(oldMap.Hash()), $"seed {seed}, level {depth}");
                    var oldSim = new Simulation(seed, 512);
                    oldSim.SetupRift(oldMap, spawn, 1 + depth / 3, 3 + depth / 2, 100 + 100 * depth / 4);
                    var newSim = new Simulation(seed, 512);
                    settings.Spawn(newSim, newMap, spawn);
                    Assert.That(newSim.StateHash(), Is.EqualTo(oldSim.StateHash()));
                    var previewSeeds = RiftLevelSeeds.ForLevel(seed, depth);
                    Assert.That(previewSeeds.Layout, Is.EqualTo(layout));
                    Assert.That(previewSeeds.Spawns, Is.EqualTo(spawn));
                }
            }
        }

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
        [Test]
        public void ModuleAuthoring_RejectsConnectorsThatDoNotFaceOutward()
        {
            var module = ScriptableObject.CreateInstance<ModuleAsset>();
            try
            {
                module.Connectors = new[] { new ConnectorAsset { Cell = new Vector2Int(2, 2), Facing = Direction.North } };
                Assert.Throws<ArgumentException>(() => module.ToDefinition());
                module.Connectors[0].Cell = new Vector2Int(2, module.Height - 1);
                Assert.DoesNotThrow(() => module.ToDefinition());
            }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }
#endif
    }
}
