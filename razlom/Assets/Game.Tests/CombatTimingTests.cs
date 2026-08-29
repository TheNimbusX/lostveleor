using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class CombatTimingTests
    {
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
        public void MeleeSwing_MissesWhenTargetLeavesBeforeContact()
        {
            var sim = new Simulation(7002UL, 16);
            sim.SetupTestArena(0);
            int victim = sim.Entities.Spawn(
                new FixVec2(Fix64.One, Fix64.Zero), 1000, Faction.Orvill);
            var attack = new InputFrame { Flags = (byte)InputFlags.Attack };

            sim.Step(in attack);
            sim.Entities.Position[victim] = new FixVec2(Fix64.FromInt(20), Fix64.Zero);
            int healthBefore = sim.Entities.Health[victim];

            for (int i = 0; i < Simulation.AttackWindupTicks; i++)
                sim.Step(in attack);

            Assert.AreEqual(healthBefore, sim.Entities.Health[victim],
                "ушедшая из дуги цель не должна получить отложенный удар");
        }
    }
}
