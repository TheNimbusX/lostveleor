using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class CombatTimingTests
    {
        private static void MakeStationary(Simulation sim, int entity)
        {
            sim.Entities.Stats[entity].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[entity].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(entity);
            sim.Entities.NextAttackTick[entity] = int.MaxValue;
        }

        [Test]
        public void MeleeDamage_LandsAtTheBladeContactTick()
        {
            var sim = new Simulation(7001UL, 16);
            sim.SetupTestArena(0);
            int victim = sim.Entities.Spawn(
                new FixVec2(Fix64.One, Fix64.Zero), 1000, Faction.Orvill);
            var attack = new InputFrame { Flags = (byte)InputFlags.Attack };

            int healthBefore = sim.Entities.Health[victim];
            sim.Step(in attack);

            Assert.AreEqual(healthBefore, sim.Entities.Health[victim],
                "замах не должен наносить урон в своём первом кадре");
            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.Attack && e.Target == victim));

            for (int i = 1; i < Simulation.AttackWindupTicks; i++)
                sim.Step(in attack);

            Assert.AreEqual(healthBefore, sim.Entities.Health[victim],
                "урон не должен опережать контакт клинка");

            sim.Step(in attack);
            Assert.Less(sim.Entities.Health[victim], healthBefore);
            Assert.That(sim.Events, Has.Some.Matches<SimEvent>(e =>
                e.Type == SimEventType.Damage && e.Target == victim));
        }

        [Test]
        public void HeavyB_CleavesTwoNearbyEnemiesButNotTheRearTarget()
        {
            var sim = new Simulation(7011UL, 16);
            sim.SetupTestArena(0);
            int primary = sim.Entities.Spawn(
                new FixVec2(Fix64.One, Fix64.Zero), 5000, Faction.Orvill);
            int upper = sim.Entities.Spawn(
                new FixVec2(Fix64.Ratio(7, 5), Fix64.Ratio(4, 5)), 5000, Faction.Orvill);
            int lower = sim.Entities.Spawn(
                new FixVec2(Fix64.Ratio(7, 5), Fix64.Ratio(-4, 5)), 5000, Faction.Orvill);
            int rear = sim.Entities.Spawn(
                new FixVec2(Fix64.FromInt(-1), Fix64.Zero), 5000, Faction.Orvill);
            MakeStationary(sim, primary);
            MakeStationary(sim, upper);
            MakeStationary(sim, lower);
            MakeStationary(sim, rear);

            int upperBefore = sim.Entities.Health[upper];
            int lowerBefore = sim.Entities.Health[lower];
            int rearBefore = sim.Entities.Health[rear];
            var held = new InputFrame { Flags = (byte)InputFlags.Attack };
            int throughSecondContact = sim.Entities.AttackCooldown[Simulation.PlayerId]
                                       + Simulation.AttackWindupTicks;
            for (int tick = 0; tick <= throughSecondContact; tick++) sim.Step(in held);

            Assert.Less(sim.Entities.Health[upper], upperBefore,
                "тяжёлый B должен прорубать соседа сверху");
            Assert.Less(sim.Entities.Health[lower], lowerBefore,
                "тяжёлый B должен прорубать соседа снизу");
            Assert.AreEqual(rearBefore, sim.Entities.Health[rear],
                "cleave не имеет права бить за спину");
        }

        [Test]
        public void BasicKill_RefundsPartOfWhirlwindCooldown()
        {
            var sim = new Simulation(7014UL, 16);
            sim.SetupTestArena(0);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);

            InputFrame cast = InputFrame.Empty;
            cast.AbilityMask = 1;
            sim.Step(in cast);
            int readyWithoutKill = sim.AbilityReadyTick(0);

            // Дожидаемся пустого impact Вихря и только затем создаём жертву:
            // иначе delayed radius честно убьёт её способностью, а тест должен
            // измерять refund именно от сабли.
            InputFrame released = InputFrame.Empty;
            for (int i = 0; i <= 10; i++) sim.Step(in released);

            int victim = sim.Entities.Spawn(
                new FixVec2(Fix64.One, Fix64.Zero), 1, Faction.Orvill);
            MakeStationary(sim, victim);
            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = victim,
            };
            bool killed = false;
            for (int i = 0; i < 60 && !killed; i++)
            {
                sim.Step(in attack);
                killed = !sim.Entities.Alive[victim];
            }

            Assert.IsTrue(killed);
            Assert.Less(sim.AbilityReadyTick(0), readyWithoutKill,
                "сабельное убийство должно приблизить следующий burst");
        }

    }
}
