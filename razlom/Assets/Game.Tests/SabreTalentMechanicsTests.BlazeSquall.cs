using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public partial class SabreTalentMechanicsTests
    {
        /// <summary>Поджечь саблю: нажатие и ожидание, пока жест дойдёт до огня.</summary>
        private static void Ignite(Simulation sim, int slot)
        {
            sim.Step(Press(slot));
            Idle(sim, Simulation.BlazeIgnitionDelayTicks + 2);
            Assert.IsTrue(sim.BlazeActive, "сабля не загорелась");
        }

        // ---- «Ладно смазал» ----

        [Test]
        public void BlazeIgniteSetsTargetsOnFire()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Blaze, 4);
            int victim = Enemy(sim, 1.5f, 0);
            Ignite(sim, 0);

            var swing = new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = victim,
                Aim = new FixVec2(Fix64.Ratio(15, 10), Fix64.Zero) };
            int guard = 0;
            while (sim.Entities.Health[victim] == 10000 && guard++ < 60) sim.Step(in swing);

            Assert.IsTrue(sim.IsIgnited(victim), "обычная атака не подожгла цель");
        }

        [Test]
        public void BlazeTrailIsLeftByRollWhileBurning()
        {
            var sim = Arena();
            Give(sim, 2, SabreTalentLine.Blaze, 1);
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            Ignite(sim, 2);

            sim.Step(Press(4));
            Idle(sim, 12);

            Assert.Greater(sim.BlazeTrailCount(null), 0, "кувырок под огнём не оставил следа");
        }

        // ---- Шквал ----

        private static int SquallHits(Simulation sim, out int total)
        {
            int hits = 0;
            total = 0;
            for (int i = 0; i < 60; i++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Damage && e.Source == Simulation.PlayerId && e.DamageOrigin == DamageOrigin.Ability)
                    { hits++; total += e.Amount; }
            }
            return hits;
        }

        [Test]
        public void SquallFinisherDoublesTheLastHop()
        {
            int Total(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Squall, rank);
                int target = Enemy(sim, 2, 0, 100000);
                sim.Step(Press(0, target));
                SquallHits(sim, out int total);
                return total;
            }

            Assert.AreEqual(4 * 85, Total(3));
            Assert.AreEqual(3 * 85 + 2 * 85, Total(4));
        }

    }
}
