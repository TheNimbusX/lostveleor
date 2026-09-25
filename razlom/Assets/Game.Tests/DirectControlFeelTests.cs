using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class DirectControlFeelTests
    {
        [Test]
        public void DirectMovementStartsOnFirstTickAndStopsOnRelease()
        {
            var sim = new Simulation(0xC1D3UL);
            sim.SetupTestArena(0);
            Fix64 full = sim.Entities.MoveStep[Simulation.PlayerId];
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.DirectMovement;
            input.MoveDirection = new FixVec2(Fix64.One, Fix64.Zero);
            input.Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero);
            sim.Step(in input);
            double expected = (full * Fix64.Ratio(3, 4)).ToDouble();
            Assert.GreaterOrEqual(sim.Entities.Velocity[Simulation.PlayerId].LengthSq.ToDouble(),
                expected * expected * .99);

            input.MoveDirection = FixVec2.Zero;
            sim.Step(in input);
            Assert.AreEqual(FixVec2.Zero, sim.Entities.Velocity[Simulation.PlayerId],
                "После отпускания стика герой не должен докатываться к опасности.");
        }

        [Test]
        public void DirectAimDoesNotSilenceAttackForEnemyBehindCursor()
        {
            var sim = new Simulation(0xC1D3UL, 16);
            sim.SetupTestArena(0);
            int enemy = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(-2), Fix64.Zero),
                5000, Faction.Orvill);
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[enemy].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(enemy);
            sim.Entities.NextAttackTick[enemy] = int.MaxValue;

            var input = InputFrame.Empty;
            input.Flags = (byte)(InputFlags.DirectMovement | InputFlags.Attack);
            input.Aim = new FixVec2(Fix64.FromInt(8), Fix64.Zero);
            int emptySwings = 0;
            for (int tick = 0; tick < 45; tick++)
            {
                sim.Step(in input);
                foreach (SimEvent e in sim.Events)
                    if (e.Type == SimEventType.Attack && e.Source == Simulation.PlayerId && e.Target < 0)
                        emptySwings++;
            }
            Assert.Greater(emptySwings, 0, "Удержание атаки отвечает взмахом даже без цели по направлению прицела.");
            Assert.AreEqual(5000, sim.Entities.Health[enemy], "Враг за спиной не перехватывает прямое прицеливание.");
        }
    }
}
