using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Таланты якорных способностей, утверждены владельцем 15 сентября. Как и
    /// у сабли, каждый механический тест сравнивает ранг с талантом и без него
    /// на одной сцене: проверяется разница, которую даёт талант.
    /// </summary>
    public class AnchorTalentTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            return sim;
        }

        private static AbilityBuild Give(Simulation sim, int slot, SabreTalentLine line, int rank)
        {
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = SabreTalents.AppendNodes(line, rank, buffer, 0);
            sim.SetAbility(slot, PelagKit.PoolDefinition(SabreTalents.PoolIndexOf(line)), buffer, count);
            return sim.GetAbility(slot);
        }

        private static AbilityBuild Build(SabreTalentLine line, int rank) => Give(Arena(), 0, line, rank);

        private static int Enemy(Simulation sim, float x, float y, int health = 10000)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio((int)(x * 1000), 1000), Fix64.Ratio((int)(y * 1000), 1000)),
                health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Press(int slot, float aimX = 2, float aimY = 0, int target = -1)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.Ratio((int)(aimX * 1000), 1000), Fix64.Ratio((int)(aimY * 1000), 1000));
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        private static int Lost(Simulation sim, int id, int health = 10000) => health - sim.Entities.Health[id];

        private static float Stat(AbilityBuild build, AbilityStatType stat) => build.Get(stat).ToFloat();

        // ---- числовые ----

        [Test]
        public void AnchorSlamNumbers()
        {
            Assert.AreEqual(15, Build(SabreTalentLine.AnchorSlam, 0).Get(AbilityStatType.WindupTicks).ToInt());
            Assert.AreEqual(9, Build(SabreTalentLine.AnchorSlam, 1).Get(AbilityStatType.WindupTicks).ToInt(), "быстрый замах");
            Assert.AreEqual(30, Build(SabreTalentLine.AnchorSlam, 2).Get(AbilityStatType.StunTicks).ToInt(), "долгий стан");
            Assert.AreEqual(1.8f, Stat(Build(SabreTalentLine.AnchorSlam, 3), AbilityStatType.Width), 0.001f, "широкая полоса");
        }

        // ---- Удар якорем ----

        [Test]
        public void AnchorSlamThreeWaysHitsTheSideLane()
        {
            int Hit(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.AnchorSlam, rank);
                int side = Enemy(sim, 2.598f, 1.5f);
                sim.Step(Press(0));
                Idle(sim, 25);
                return Lost(sim, side);
            }

            Assert.AreEqual(0, Hit(4), "боковая цель задета без таланта");
            Assert.Greater(Hit(5), 0, "три направления не задели боковую полосу");
        }

        // ---- Крушение ----

        private static int PlayWreck(Simulation sim, int enemy, int presses)
        {
            for (int p = 0; p < presses; p++)
            {
                sim.Step(Press(0));
                Idle(sim, 10);
            }
            Idle(sim, 40);
            return Lost(sim, enemy);
        }

        [Test]
        public void WreckFourthStrikeAddsTripleGroundHit()
        {
            var without = Arena();
            Give(without, 0, SabreTalentLine.Wreck, 4);
            int a = Enemy(without, 1.5f, 0);
            Assert.AreEqual(70 + 70 + 140, PlayWreck(without, a, 4), "без таланта серия длиннее трёх ударов");

            var with = Arena();
            Give(with, 0, SabreTalentLine.Wreck, 5);
            int b = Enemy(with, 1.5f, 0);
            Assert.AreEqual(70 + 70 + 140 + 210, PlayWreck(with, b, 4));
        }

        // ---- Абордаж ----

        [Test]
        public void BoardingStunsAndSweeps()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Boarding, 5);
            int target = Enemy(sim, 4, 0);
            int neighbour = Enemy(sim, 4, 1.2f);
            sim.Step(Press(0, 4, 0, target));
            Idle(sim, 34);

            Assert.AreEqual(75, Lost(sim, target), "кулак не попал в цель");
            Assert.AreEqual(75, Lost(sim, neighbour), "на абордаж! не задел соседа");
            Assert.IsTrue(sim.Statuses.IsStunned(target, sim.Tick), "тяжёлый кулак не оглушил");

            var plain = Arena();
            Give(plain, 0, SabreTalentLine.Boarding, 1);
            int t = Enemy(plain, 4, 0);
            int n = Enemy(plain, 4, 1.2f);
            plain.Step(Press(0, 4, 0, t));
            Idle(plain, 34);
            Assert.AreEqual(0, Lost(plain, n), "сосед задет без таланта");
            Assert.IsFalse(plain.Statuses.IsStunned(t, plain.Tick), "оглушение без таланта");
        }

        // ---- Взрывная смесь ----

        private static void Throw(Simulation sim, float x)
        {
            sim.Step(Press(0, x, 0));
            Idle(sim, 12);
        }

        [Test]
        public void FlaskFuelAddsTwentyPercentInsideThePool()
        {
            int Hit(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Flask, rank);
                int enemy = Enemy(sim, 3, 0, 100000);
                Throw(sim, 3);
                int before = sim.Entities.Health[enemy];
                sim.ApplyAbilityDamage(Simulation.PlayerId, enemy, 100, 0, DamageType.Physical);
                return before - sim.Entities.Health[enemy];
            }

            Assert.AreEqual(100, Hit(2));
            Assert.AreEqual(120, Hit(3));
        }

        [Test]
        public void FlaskRingLightsThreeMorePools()
        {
            int Pools(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Flask, rank);
                Throw(sim, 3);
                int active = 0;
                for (int i = 0; i < 8; i++) if (sim.FirePoolActive(i)) active++;
                return active;
            }

            Assert.AreEqual(1, Pools(4));
            Assert.AreEqual(4, Pools(5));
        }
    }
}
