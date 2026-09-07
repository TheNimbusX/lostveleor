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
            for (int i = 1; i < entities.Count; i++)
            {
                Assert.That(entities.Alive[i], Is.True);
                Assert.That(run.Map.IsWalkable(entities.Position[i], entities.BodyRadius[i]), Is.True);
                if (entities.Kind[i] == EnemyKind.ForestGuardian) guardians++;
                if (entities.Kind[i] != EnemyKind.ForestRootSwarm) continue;
                swarm++;
                Assert.That(entities.Health[i], Is.EqualTo(30));
                Assert.That(entities.Damage[i], Is.EqualTo(4));
                Assert.That(entities.AttackCooldown[i], Is.EqualTo(24));
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
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.CenterOf(run.Map.GetExit(0));
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

        [TestCase(false)]
        [TestCase(true)]
        public void Scratch_HasItsOwnReachAndNineTickTelegraph(bool naive)
        {
            Simulation sim = IsolatedSwarm();
            sim.DebugUseNaiveTargeting = naive;
            EntityStore entities = sim.Entities;
            entities.Stats[4].SetBase(StatType.MoveSpeed, Fix64.Zero);
            entities.RefreshStats(4);
            FixVec2 player = entities.Position[0];
            entities.Position[4] = player + new FixVec2(Fix64.Ratio(16, 10), Fix64.Zero);
            for (int tick = 0; tick < 15; tick++) sim.Step(InputFrame.Empty);
            Assert.That(entities.PendingAttackTarget[4], Is.EqualTo(-1),
                "Корнеполз не должен доставать с дистанции Хранителя");

            entities.Position[4] = player + new FixVec2(Fix64.Ratio(12, 10), Fix64.Zero);
            int health = entities.Health[0];
            int start = sim.Tick;
            sim.Step(InputFrame.Empty);
            Assert.That(entities.AttackImpactTick[4], Is.EqualTo(start + 9));
            for (int tick = 1; tick < 9; tick++) sim.Step(InputFrame.Empty);
            Assert.That(entities.Health[0], Is.EqualTo(health));
            sim.Step(InputFrame.Empty);
            Assert.That(entities.Health[0], Is.EqualTo(health - 4));
        }

        [TestCase(30, 3.4f)]
        [TestCase(19, 5f)]
        public void Swarm_AcceleratesOnlyOnTheLastTwoMeters(int distanceTenths, float speed)
        {
            Simulation sim = IsolatedSwarm();
            for (int tick = 0; tick < 30; tick++)
            {
                sim.Entities.Position[4] = sim.Entities.Position[0]
                    + new FixVec2(Fix64.Ratio(distanceTenths, 10), Fix64.Zero);
                sim.Step(InputFrame.Empty);
            }
            float actual = Fix64.Sqrt(sim.Entities.Velocity[4].LengthSq).ToFloat()
                * Simulation.TicksPerSecond;
            Assert.That(actual, Is.EqualTo(speed).Within(0.01f));
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
