using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class ForestEncounterTests
    {
        private static RiftRun NewRun(ulong seed)
        {
            var run = new RiftRun(new Simulation(seed, 128), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            return run;
        }

        private static void AssertPack(RiftRun run)
        {
            EntityStore entities = run.Sim.Entities;
            int guardians = 0, swarm = 0;
            // Прототипный забег растит здоровье тем же процентом глубины, что и
            // авторские уровни: 100 на первой арене, 107 на второй.
            int percent = EnemyArchetypes.DepthHealthPercent(run.Depth);
            Assert.That(run.LevelSettings.EnemyHealth, Is.EqualTo(percent));
            for (int i = 1; i < entities.Count; i++)
            {
                Assert.That(entities.Alive[i], Is.True);
                Assert.That(run.Map.IsWalkable(entities.Position[i], entities.BodyRadius[i]), Is.True);
                if (entities.Kind[i] == EnemyKind.ForestGuardian)
                {
                    guardians++;
                    Assert.That(entities.Health[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(550, percent)));
                    Assert.That(entities.Damage[i], Is.EqualTo(14));
                }
                if (entities.Kind[i] != EnemyKind.ForestRootSwarm) continue;
                swarm++;
                Assert.That(entities.Health[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(130, percent)));
                // Укус 3, цикл 30 тиков (стенд баланса, 26.09, проход 2).
                Assert.That(entities.Damage[i], Is.EqualTo(3));
                Assert.That(entities.AttackCooldown[i], Is.EqualTo(30));
                Assert.That(entities.BodyRadius[i], Is.EqualTo(Fix64.Ratio(45, 100)));
                for (int j = 4; j < i; j++)
                {
                    Fix64 spacing = Fix64.Sqrt((entities.Position[i] - entities.Position[j]).LengthSq);
                    Assert.That(spacing.ToFloat(), Is.InRange(0.9f, 2.5f),
                        "рой должен появляться одной пачкой без пересечений тел");
                }
            }
            Assert.That(guardians, Is.EqualTo(3));
            Assert.That(swarm, Is.EqualTo(6));
            Assert.That(entities.Count, Is.EqualTo(10));
        }

        [Test]
        public void EveryRunAndNextRift_HasThreeGuardiansAndSixRootSwarm()
        {
            for (ulong seed = 1; seed <= 32; seed++)
            {
                RiftRun run = NewRun(seed);
                AssertPack(run);
                for (int i = 1; i < run.Sim.Entities.Count; i++)
                    run.Sim.Entities.Alive[i] = false;
                run.Step(InputFrame.Empty);
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                run.Step(InputFrame.Empty);
                run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
                AssertPack(run);
                run.StartRun();
                AssertPack(run);
            }
        }

        private static Simulation IsolatedSwarm()
        {
            Simulation sim = NewRun(123UL).Sim;
            for (int i = 1; i < sim.Entities.Count; i++) sim.Entities.Alive[i] = i == 4;
            sim.Entities.Facing[4] = new FixVec2(-Fix64.One, Fix64.Zero);
            return sim;
        }

        [Test]
        public void ReusedEntitySlot_DropsPreviousEnemyKind()
        {
            var entities = new EntityStore(2);
            int first = entities.Spawn(FixVec2.Zero, 30, Faction.Orvill);
            entities.Kind[first] = EnemyKind.ForestRootSwarm;
            entities.Clear();
            int player = entities.Spawn(FixVec2.Zero, 100, Faction.Wole);
            Assert.That(entities.Kind[player], Is.EqualTo(EnemyKind.None));
        }
    }
}
