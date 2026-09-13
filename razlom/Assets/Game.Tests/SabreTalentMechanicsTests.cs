using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Механические таланты сабельной ветки. Каждый тест сравнивает способность
    /// с талантом и без него на одной и той же сцене: проверяется разница,
    /// которую даёт талант, а не абсолютный баланс.
    /// </summary>
    public partial class SabreTalentMechanicsTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            return sim;
        }

        private static void Give(Simulation sim, int slot, SabreTalentLine line, int rank)
        {
            var buffer = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = SabreTalents.AppendNodes(line, rank, buffer, 0);
            sim.SetAbility(slot, PelagKit.Definition(CombatBranch.Sabre, SabreTalents.SlotOf(line)), buffer, count);
        }

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

        private static InputFrame Press(int slot, int target = -1)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.AbilityHoldMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        private static InputFrame Hold(int slot)
        {
            var input = InputFrame.Empty;
            input.AbilityHoldMask = (byte)(1 << slot);
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        private static int Lost(Simulation sim, int id, int health = 10000) => health - sim.Entities.Health[id];

        private static float Lavidium(Simulation sim) => sim.Entities.Lavidium[Simulation.PlayerId].ToFloat();

        // ---- Вихрь ----

        [Test]
        public void WhirlwindCrowdAddsTenPercentPerVictim()
        {
            int Hit(int rank)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Whirlwind, rank);
                int a = Enemy(sim, 1, 0); Enemy(sim, 0, 1); Enemy(sim, -1, 0);
                sim.Step(Press(0));
                Idle(sim, 12);
                return Lost(sim, a);
            }

            Assert.AreEqual(120, Hit(2), "без таланта");
            Assert.AreEqual(156, Hit(3), "три задетых — +30%");
        }

        [Test]
        public void WhirlwindRefundReturnsThreePerVictim()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Whirlwind, 4);
            Enemy(sim, 1, 0); Enemy(sim, -1, 0);
            sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(50);

            sim.Step(Press(0));
            Idle(sim, 12);

            // 50 − 30 за каст + 6 за двух задетых + восстановление за 13 тиков.
            Assert.AreEqual(27.3f, Lavidium(sim), 0.6f);
        }

        [Test]
        public void WhirlwindChannelKeepsHittingWhileHeld()
        {
            int Damage(bool hold)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Whirlwind, 5);
                int dummy = Enemy(sim, 1, 0, 100000);
                sim.Step(Press(0));
                for (int i = 0; i < 70; i++) sim.Step(hold ? Hold(0) : InputFrame.Empty);
                return Lost(sim, dummy, 100000);
            }

            int single = Damage(false);
            Assert.Greater(single, 0, "первый контакт не прошёл");
            Assert.GreaterOrEqual(Damage(true), single * 3, "удержание не добавило оборотов");
        }

        [Test]
        public void WhirlwindChannelDrainsLavidium()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Whirlwind, 5);
            Enemy(sim, 1, 0, 100000);
            sim.Step(Press(0));
            for (int i = 0; i < 40; i++) sim.Step(Hold(0));

            Assert.IsTrue(sim.WhirlwindChanneling, "удержание не началось");
            Assert.Less(Lavidium(sim), 70f, "удержание не тратит лавидий");
        }

        // ---- Рассекающий удар ----

        [Test]
        public void CleaveOnTheMoveDoesNotStopTheHero()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Cleave, 1);
            sim.Step(Press(0));
            Assert.IsTrue(sim.CleaveMovable, "талант «На ходу» не включился");

            var move = InputFrame.Empty;
            move.Flags = (byte)InputFlags.MoveOrder;
            move.Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(6));
            move.AttackTarget = -1;
            FixVec2 before = sim.Entities.Position[Simulation.PlayerId];
            for (int i = 0; i < 8; i++) sim.Step(in move);

            Assert.IsTrue(sim.CleaveActive, "удар отменился от шага");
            Assert.Greater((sim.Entities.Position[Simulation.PlayerId] - before).LengthSq.ToFloat(), 0.01f, "герой стоит");
        }

        [Test]
        public void CleaveBigGameHitsElitesHarder()
        {
            int Hit(int rank, bool elite)
            {
                var sim = Arena();
                Give(sim, 0, SabreTalentLine.Cleave, rank);
                int target = Enemy(sim, 1.2f, 0);
                if (elite) sim.MarkElite(target);
                sim.Step(Press(0));
                Idle(sim, 25);
                return Lost(sim, target);
            }

            Assert.AreEqual(Hit(2, true), Hit(3, false), "обычная цель не должна получать прибавку");
            Assert.AreEqual(Hit(2, true) * 140 / 100, Hit(3, true));
        }

        [Test]
        public void CleaveKillRefundsItsCost()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Cleave, 4);
            Enemy(sim, 1.2f, 0, 1);
            sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(50);

            sim.Step(Press(0));
            Idle(sim, 25);

            // 50 − 15 + 15 за убийство + восстановление.
            Assert.GreaterOrEqual(Lavidium(sim), 50f);
        }

        [Test]
        public void CleaveFanHitsThreeDirections()
        {
            var sim = Arena();
            Give(sim, 0, SabreTalentLine.Cleave, 5);
            int center = Enemy(sim, 1.2f, 0);
            int left = Enemy(sim, 0.983f, 0.688f);
            int right = Enemy(sim, 0.983f, -0.688f);

            sim.Step(Press(0));
            Idle(sim, 25);

            Assert.Greater(Lost(sim, center), 0, "центр");
            Assert.Greater(Lost(sim, left), 0, "+35°");
            Assert.Greater(Lost(sim, right), 0, "−35°");
        }
    }
}
