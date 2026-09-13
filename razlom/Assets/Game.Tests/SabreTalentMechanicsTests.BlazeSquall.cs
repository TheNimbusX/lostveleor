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
        public void BlazeWithoutIgniteTalentDoesNotBurn()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Blaze, 3);
            int victim = Enemy(sim, 1.5f, 0);
            Ignite(sim, 0);

            var swing = new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = victim,
                Aim = new FixVec2(Fix64.Ratio(15, 10), Fix64.Zero) };
            for (int i = 0; i < 40; i++) sim.Step(in swing);

            Assert.IsFalse(sim.IsIgnited(victim));
        }

        [Test]
        public void BlazeFinalTalentAddsFireToAbilities()
        {
            int FireHits(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Whirlwind, 0);
                Give(sim, 2, SabreTalentLine.Blaze, rank);
                Enemy(sim, 1, 0);
                Ignite(sim, 2);
                sim.Step(Press(0));
                int fire = 0;
                for (int i = 0; i < 14; i++)
                {
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.Damage && e.Source == Simulation.PlayerId && e.DamageKind == DamageType.Fire)
                            fire++;
                }
                return fire;
            }

            Assert.AreEqual(0, FireHits(4), "без финального таланта способности огня не получают");
            Assert.AreEqual(1, FireHits(5), "Вихрь под огнём должен добавить огненный удар");
        }

        [Test]
        public void BlazeTrailIsLeftByRollWhileBurning()
        {
            var sim = Arena();
            Give(sim, 2, SabreTalentLine.Blaze, 1);
            sim.SetAbility(4, PelagKit.Definition(CombatBranch.Sabre, 4), new AbilityNode[0], 0);
            Ignite(sim, 2);

            sim.Step(Press(4));
            Idle(sim, 12);

            Assert.Greater(sim.BlazeTrailCount(null), 0, "кувырок под огнём не оставил следа");
        }

        [Test]
        public void RollWithoutTrailTalentLeavesNothing()
        {
            var sim = Arena();
            Give(sim, 2, SabreTalentLine.Blaze, 0);
            sim.SetAbility(4, PelagKit.Definition(CombatBranch.Sabre, 4), new AbilityNode[0], 0);
            Ignite(sim, 2);

            sim.Step(Press(4));
            Idle(sim, 12);

            Assert.AreEqual(0, sim.BlazeTrailCount(null));
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
        public void SquallFiveHopsAddsAHop()
        {
            int Hops(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Squall, rank);
                int target = Enemy(sim, 2, 0, 100000);
                sim.Step(Press(0, target));
                return SquallHits(sim, out _);
            }

            Assert.AreEqual(4, Hops(4));
            Assert.AreEqual(5, Hops(5));
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

        [Test]
        public void SquallShieldsTheHeroDuringHops()
        {
            bool Shielded(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Squall, rank);
                int target = Enemy(sim, 2, 0, 100000);
                sim.Step(Press(0, target));
                sim.Step(InputFrame.Empty);
                return sim.SquallShielded;
            }

            Assert.IsFalse(Shielded(2));
            Assert.IsTrue(Shielded(3));
        }

        [Test]
        public void SquallKillsDuringTheSeriesShortenTheCooldown()
        {
            int Ready(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Squall, rank);
                int weak = Enemy(sim, 2, 0, 1);
                Enemy(sim, 2.5f, 0.6f, 100000);
                sim.Step(Press(0, weak));
                Idle(sim, 40);
                return sim.AbilityReadyTick(0);
            }

            Assert.AreEqual(Ready(0) - Simulation.TicksPerSecond / 2, Ready(1));
        }
    }
}
