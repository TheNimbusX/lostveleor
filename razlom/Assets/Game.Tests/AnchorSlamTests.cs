using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class AnchorSlamTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.SetAbility(2, AbilityDefinition.AnchorSlam(), new AbilityNode[0], 0);
            return sim;
        }

        private static int Enemy(Simulation sim, int x10, int y10 = 0)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(x10, 10), Fix64.Ratio(y10, 10)), 10000, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Cast()
        {
            var input = InputFrame.Empty;
            input.AbilityMask = 4;
            input.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            return input;
        }

        private static void Impact(Simulation sim)
        {
            sim.Step(Cast());
            for (int i = 0; i < 15; i++) sim.Step(InputFrame.Empty);
        }

        [Test]
        public void SingleDelayedHitCoversLongNarrowStrip()
        {
            var sim = Arena();
            int near = Enemy(sim, 15), far = Enemy(sim, 44);
            int side = Enemy(sim, 30, 10), behind = Enemy(sim, -10), beyond = Enemy(sim, 48);
            sim.Step(Cast());
            for (int i = 0; i < 14; i++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(10000, sim.Entities.Health[near]);
            sim.Step(InputFrame.Empty);
            Assert.Less(sim.Entities.Health[near], 10000);
            Assert.Less(sim.Entities.Health[far], 10000);
            Assert.AreEqual(10000, sim.Entities.Health[side]);
            Assert.AreEqual(10000, sim.Entities.Health[behind]);
            Assert.AreEqual(10000, sim.Entities.Health[beyond]);
            int health = sim.Entities.Health[near];
            for (int i = 0; i < 35; i++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(health, sim.Entities.Health[near]);
            Assert.IsFalse(sim.AnchorSlamActive);
            Assert.IsFalse(sim.CycloneActive);
        }

        [Test]
        public void StunCancelsPendingAttackAndExpiresAfterFifteenTicks()
        {
            var sim = Arena();
            int enemy = Enemy(sim, 15);
            sim.Entities.PendingAttackTarget[enemy] = 0;
            sim.Entities.AttackImpactTick[enemy] = 17;
            Impact(sim);
            Assert.AreEqual(-1, sim.Entities.PendingAttackTarget[enemy]);
            Assert.AreEqual(30, sim.Statuses.StunUntilTick[enemy]);
            int health = sim.Entities.Health[0];
            sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed, Fix64.FromInt(3));
            sim.Entities.RefreshStats(enemy);
            sim.Entities.Position[enemy] = new FixVec2(Fix64.FromInt(6), Fix64.Zero);
            sim.Entities.Aggro[enemy] = true;
            var pos = sim.Entities.Position[enemy];
            while (sim.Tick < 30) sim.Step(InputFrame.Empty);
            Assert.AreEqual(pos, sim.Entities.Position[enemy]);
            Assert.AreEqual(health, sim.Entities.Health[0]);
            Assert.IsFalse(sim.Statuses.IsStunned(enemy, sim.Tick));
            sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(pos, sim.Entities.Position[enemy]);
        }

        [Test]
        public void NewAbilityCancelsUnlandedSlam()
        {
            var sim = Arena();
            int enemy = Enemy(sim, 40);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            sim.Step(Cast());
            var replacement = InputFrame.Empty;
            replacement.AbilityMask = 1;
            sim.Step(replacement);
            for (int i = 0; i < 30; i++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(10000, sim.Entities.Health[enemy]);
            Assert.IsFalse(sim.AnchorSlamActive);
        }

        [Test]
        public void DeathCancelsUnlandedSlam()
        {
            var sim = Arena();
            int enemy = Enemy(sim, 40);
            sim.Step(Cast());
            sim.Entities.Alive[0] = false;
            for (int i = 0; i < 30; i++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(10000, sim.Entities.Health[enemy]);
        }

        [Test]
        public void ReplayIncludesPendingSlamAndStun()
        {
            var a = Arena(); var b = Arena();
            Enemy(a, 30); Enemy(b, 30);
            for (int i = 0; i < 70; i++)
            {
                var input = i == 0 ? Cast() : InputFrame.Empty;
                a.Step(input); b.Step(input);
                Assert.AreEqual(a.StateHash(), b.StateHash());
            }
        }
    }
}
