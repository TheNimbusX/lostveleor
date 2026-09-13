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
                new CampDummyDefinition(new FixVec2(Fix64.FromInt(8),Fix64.FromInt(4)),health,Fix64.FromInt(200),Fix64.FromInt(75)) });
            return session;
        }
        static InputFrame Attack => new InputFrame { Aim = new FixVec2(Fix64.FromInt(2),Fix64.Zero), Flags = (byte)InputFlags.Attack, AttackTarget = 1 };

        [Test]
        public void AuthoredDummiesReceiveRealAttacksAndKeepTheirPositions()
        {
            var session = Create();
            var sim = session.CampSim;
            var position = sim.Entities.Position[1];
            for (int i=0;i<180;i++) session.Step(Attack);
            Assert.That(session.Mode, Is.EqualTo(GameMode.Camp));
            Assert.That(session.OnProvingGround, Is.False);
            Assert.That(sim.Entities.Count, Is.EqualTo(3));
            Assert.That(sim.Entities.Health[1], Is.LessThan(1000));
            Assert.That(session.Training.DamageTotal, Is.GreaterThan(0));
            Assert.That(session.Training.Hits, Is.GreaterThan(0));
            Assert.That(session.Training.DamagePerSecond, Is.GreaterThan(0));
            Assert.That(sim.Entities.Position[1], Is.EqualTo(position));
            Assert.That(sim.Entities.Health[0], Is.EqualTo(sim.Entities.MaxHealth[0]));
            Assert.That(ForcedMotion.Begin(sim.Entities,1,FixVec2.Zero,20,ForcedMotionKind.Dragged), Is.False);
        }
        [Test]
        public void KilledDummyRecoversAndRunReturnRestoresBothTargets()
        {
            var session = Create(1);
            for (int i=0;i<180;i++) session.Step(Attack);
            Assert.That(session.Training.Hits, Is.GreaterThan(1));
            Assert.That(session.CampSim.Entities.Alive[1], Is.True);
            Assert.That(session.CampSim.Entities.Health[1], Is.EqualTo(1));
            session.EnterRift(); session.ReturnToCamp();
            Assert.That(session.ActiveSim, Is.SameAs(session.CampSim));
            Assert.That(session.CampSim.Entities.Count, Is.EqualTo(3));
            Assert.That(session.CampSim.Entities.Position[2], Is.EqualTo(new FixVec2(Fix64.FromInt(8),Fix64.FromInt(4))));
            Assert.That(session.Training.DamageTotal, Is.Zero);
        }
        [Test]
        public void TrainingDoesNotStartItsClockBeforeFirstHitAndLegacyToggleCannotLeaveCamp()
        {
            var session = Create();
            for (int i=0;i<200;i++) session.Step(InputFrame.Empty);
            Assert.That(session.Training.Ticks, Is.Zero);
            session.Step(new InputFrame { Command = (byte)CampCommand.ToggleProvingGround });
            Assert.That(session.ActiveSim, Is.SameAs(session.CampSim));
            Assert.That(session.OnProvingGround, Is.False);
            for (int i=0;i<100;i++) session.Step(Attack);
            session.Training.ResetCounters();
            Assert.That(session.Training.Hits, Is.Zero);
            Assert.That(session.Training.DamagePerSecond, Is.Zero);
        }
        [Test]
        public void RepeatedTrainingInputsProduceIdenticalCombatState()
        {
            var a = Create(); var b = Create();
            for (int i=0;i<600;i++) { var input = i%120<80 ? Attack : InputFrame.Empty; a.Step(input); b.Step(input); }
            Assert.That(a.CampSim.StateHash(), Is.EqualTo(b.CampSim.StateHash()));
            Assert.That(a.Training.DamageTotal, Is.EqualTo(b.Training.DamageTotal));
        }
    }
}
