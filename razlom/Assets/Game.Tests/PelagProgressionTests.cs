using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Основа постоянной прокачки: лавидий, опыт и уровень, дающий статы.
    ///
    /// Числа ресурса, стоимостей и прибавок за уровень — решения владельца
    /// (13 и 15 сентября), поэтому проверяются точно. Награды за убийство и
    /// кривая уровня — заглушки баланса, и тесты держат только их правила.
    /// </summary>
    public class PelagProgressionTests
    {
        private const int Player = Simulation.PlayerId;

        private static Simulation Arena(AbilityDefinition definition, int slot = 0)
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.SetAbility(slot, definition, new AbilityNode[0], 0);
            return sim;
        }

        private static int Enemy(Simulation sim, int x10, int health)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(x10, 10), Fix64.Zero), health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Press(int slot)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        private static float Lavidium(Simulation sim) => sim.Entities.Lavidium[Player].ToFloat();

        // ---- лавидий ----

        [Test]
        public void HeroStartsWithAFullPoolOfTwoHundred()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            Assert.AreEqual(200, sim.Entities.MaxLavidium[Player]);
            Assert.AreEqual(200f, Lavidium(sim), 0.001f);
        }

        [Test]
        public void CastSpendsItsCost()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            sim.Step(Press(0));
            Assert.AreEqual(170f, Lavidium(sim), 0.001f);
        }

        /// <summary>
        /// Без ресурса нет каста: кулдаун не тратится и лавидий не уходит в минус.
        /// Иначе игрок терял бы кнопку на полный кулдаун за нажатие, которое
        /// ничего не сделало.
        /// </summary>
        [Test]
        public void UnaffordableCastDoesNothing()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            sim.Entities.Lavidium[Player] = Fix64.FromInt(29);

            sim.Step(Press(0));

            Assert.LessOrEqual(sim.AbilityReadyTick(0), sim.Tick, "кулдаун потрачен без каста");
            foreach (var e in sim.Events)
                Assert.AreNotEqual(SimEventType.AbilityCast, e.Type, "каст прошёл без ресурса");
            Assert.GreaterOrEqual(Lavidium(sim), 29f, "лавидий ушёл, хотя каста не было");
        }

        // ---- опыт ----

        [Test]
        public void ExperienceCurveLevelsUpAndCarriesTheRest()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.AreEqual(1, camp.Level);
            Assert.AreEqual(100, camp.ExperienceToNextLevel);

            Assert.AreEqual(0, camp.GainExperience(99));
            Assert.AreEqual(1, camp.Level);

            Assert.AreEqual(1, camp.GainExperience(1));
            Assert.AreEqual(2, camp.Level);
            Assert.AreEqual(0, camp.Experience);

            // 150 до третьего и ещё 10 сверху — остаток переносится.
            Assert.AreEqual(1, camp.GainExperience(160));
            Assert.AreEqual(3, camp.Level);
            Assert.AreEqual(10, camp.Experience);
        }

        // ---- статы уровня ----

        /// <summary>Решение владельца: +30 жизни, +5 урона, +10 лавидия за уровень.</summary>
        [Test]
        public void EachLevelAddsHealthDamageAndLavidium()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            int health = sim.Entities.MaxHealth[Player];
            int damage = sim.Entities.Damage[Player];
            int lavidium = sim.Entities.MaxLavidium[Player];

            sim.SetPlayerLevel(3);

            Assert.AreEqual(health + 60, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(damage + 10, sim.Entities.Damage[Player]);
            Assert.AreEqual(lavidium + 20, sim.Entities.MaxLavidium[Player]);
        }

        [Test]
        public void SessionGivesTheCampLevelToTheCampAndTheRift()
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3);
            camp.DeveloperSetLevel(3);
            var session = new GameSession(7, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());

            Assert.AreEqual(220, session.CampSim.Entities.MaxLavidium[Player], "лагерь без прибавок уровня");

            camp.GainExperience(camp.ExperienceToNextLevel);
            session.SyncPlayerLevel();
            Assert.AreEqual(230, session.CampSim.Entities.MaxLavidium[Player], "повышение не дошло до лагеря");

            session.EnterRift();
            Assert.AreEqual(4, session.Run.Sim.PlayerLevel);
            Assert.AreEqual(230, session.Run.Sim.Entities.MaxLavidium[Player], "Разлом без прибавок уровня");
        }

        // ---- сохранение ----

        /// <summary>
        /// Сохранение версии 2 несло ранги постоянных талантов. Они больше не
        /// существуют: уровень и опыт переезжают, ранги отбрасываются.
        /// </summary>
        [Test]
        public void VersionTwoSaveKeepsLevelAndDropsTalents()
        {
            var camp = PrototypeContent.NewCamp();
            camp.GainExperience(275);
            camp.Earn(CurrencyType.Gold, 12);

            var restored = CampSaveCodec.Decode(EncodeLegacy(camp, 2, new[] { 2, 0, 1, 0 }), PrototypeContent.Items());

            Assert.AreEqual(camp.Level, restored.Level);
            Assert.AreEqual(camp.Experience, restored.Experience);
            Assert.AreEqual(12, restored.Money(CurrencyType.Gold));
        }

        /// <summary>Прежние форматы байт в байт: версия 1 без прокачки, версия 2 с рангами талантов.</summary>
        private static byte[] EncodeLegacy(Camp camp, int version, int[] ranks)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(version); w.Write(camp.Act); w.Write(camp.Bag.Capacity);
                for (int i = 0; i < (int)CurrencyType.Count; i++) w.Write(camp.Money((CurrencyType)i));
                for (int i = 0; i < camp.Bag.Capacity; i++) { WriteItem(w, camp.Bag.At(i)); w.Write(camp.Bag.IsKept(i)); }
                for (int i = 0; i < (int)EquipSlot.Count; i++) WriteItem(w, camp.Worn.Worn((EquipSlot)i));
                if (version >= 2)
                {
                    w.Write(camp.Level); w.Write(camp.Experience);
                    foreach (int rank in ranks) w.Write(rank);
                }
                w.Flush();
                byte[] payload = stream.ToArray();
                uint h = 2166136261;
                for (int i = 0; i < payload.Length; i++) { h ^= payload[i]; h = unchecked(h * 16777619); }
                w.Write(h);
                w.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteItem(BinaryWriter w, ItemInstance item)
        {
            w.Write(item.BaseId); w.Write(item.ItemLevel); w.Write((byte)item.Rarity); w.Write(item.Seed);
        }
    }
}
