using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class CampTrainingTests
    {
        static GameSession Create(int health = 1000)
        {
            var session = PrototypeContent.NewSession(7123UL);
            session.ConfigureCampTraining(new[] {
                new CampDummyDefinition(new FixVec2(Fix64.Ratio(3,2),Fix64.Zero),health,Fix64.Zero,Fix64.Zero),
                new CampDummyDefinition(new FixVec2(Fix64.FromInt(8),Fix64.FromInt(4)),health,Fix64.FromInt(200),Fix64.Ratio(3,4)) });
            return session;
        }
        static InputFrame Attack => new InputFrame { Aim = new FixVec2(Fix64.FromInt(2),Fix64.Zero), Flags = (byte)InputFlags.Attack, AttackTarget = 1 };

        [Test]
        public void AuthoredDummiesReceiveRealAttacksAndKeepTheirPositions()
        {
            var session = Create();
            var sim = session.CampSim;
            var position = sim.Entities.Position[1];
            int dummyAttacks = 0;
            for (int i=0;i<180;i++)
            {
                session.Step(Attack);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && (e.Source == 1 || e.Source == 2)) dummyAttacks++;
            }
            Assert.That(session.Mode, Is.EqualTo(GameMode.Camp));
            Assert.That(session.OnProvingGround, Is.False);
            Assert.That(sim.Entities.Count, Is.EqualTo(3));
            Assert.That(sim.Entities.Health[1], Is.LessThan(1000));
            Assert.That(session.Training.DamageTotal, Is.GreaterThan(0));
            Assert.That(session.Training.Hits, Is.GreaterThan(0));
            Assert.That(session.Training.DamagePerSecond, Is.GreaterThan(0));
            Assert.That(sim.Entities.Position[1], Is.EqualTo(position));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(sim.Entities.MaxHealth[0]));
            Assert.That(dummyAttacks, Is.Zero, "манекены не запускают даже первый атакующий тик");
            Assert.That(ForcedMotion.Begin(sim.Entities,1,FixVec2.Zero,20,ForcedMotionKind.Dragged), Is.False);
        }

        [Test]
        public void ProvingGroundSecondDummyDoesNotAttack()
        {
            var sim = new Simulation(7123UL, 8);
            sim.SetupProvingGround(10000, Fix64.Zero, Fix64.Zero);
            int attacks = 0;
            for (int i = 0; i < 180; i++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source != Simulation.PlayerId) attacks++;
            }
            Assert.That(sim.Entities.MaxHealth[1], Is.GreaterThanOrEqualTo(10000));
            Assert.That(sim.Entities.MaxHealth[2], Is.GreaterThanOrEqualTo(10000));
            Assert.That(attacks, Is.Zero);
            Assert.That(sim.Entities.Health[Simulation.PlayerId], Is.EqualTo(sim.Entities.MaxHealth[Simulation.PlayerId]));
        }
    }
}
