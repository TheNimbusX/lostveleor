using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Основа постоянной прокачки: лавидий, опыт, уровень и очки талантов.
    ///
    /// Числа ресурса и стоимостей — решение владельца от 13 сентября, поэтому
    /// проверяются точно. Награды за убийство и кривая уровня — заглушки
    /// баланса, и тесты держат только их правила, а не конкретные значения
    /// там, где правило важнее числа.
    /// </summary>
    public class PelagProgressionTests
    {
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

        private static float Lavidium(Simulation sim) => sim.Entities.Lavidium[Simulation.PlayerId].ToFloat();

        // ---- лавидий ----

        [Test]
        public void HeroStartsWithAFullHundredLavidium()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            Assert.AreEqual(100, sim.Entities.MaxLavidium[Simulation.PlayerId]);
            Assert.AreEqual(100f, Lavidium(sim), 0.001f);
        }

        [Test]
        public void SabreCostsMatchTheOwnerSheet()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                sim.SetAbility(slot, PelagKit.Definition(CombatBranch.Sabre, slot), new AbilityNode[0], 0);

            Assert.AreEqual(30, Simulation.LavidiumCostOf(sim.GetAbility(0)), "Вихрь");
            Assert.AreEqual(15, Simulation.LavidiumCostOf(sim.GetAbility(1)), "Рассекающий удар");
            Assert.AreEqual(10, Simulation.LavidiumCostOf(sim.GetAbility(2)), "«Ладно смазал»");
            Assert.AreEqual(40, Simulation.LavidiumCostOf(sim.GetAbility(3)), "Шквал");
            Assert.AreEqual(0, Simulation.LavidiumCostOf(sim.GetAbility(4)), "кувырок бесплатный");
        }

        [Test]
        public void CastSpendsItsCost()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            sim.Step(Press(0));
            Assert.AreEqual(70f, Lavidium(sim), 0.001f);
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
            sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(29);

            sim.Step(Press(0));

            Assert.LessOrEqual(sim.AbilityReadyTick(0), sim.Tick, "кулдаун потрачен без каста");
            foreach (var e in sim.Events)
                Assert.AreNotEqual(SimEventType.AbilityCast, e.Type, "каст прошёл без ресурса");
            Assert.GreaterOrEqual(Lavidium(sim), 29f, "лавидий ушёл, хотя каста не было");
        }

        [Test]
        public void RegeneratesThreePerSecondUpToTheCap()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.Zero;

            Idle(sim, Simulation.TicksPerSecond);
            Assert.AreEqual(3f, Lavidium(sim), 0.01f);

            Idle(sim, Simulation.TicksPerSecond * 60);
            Assert.AreEqual(100f, Lavidium(sim), 0.001f, "восстановление перелило потолок");
        }

        // ---- опыт ----

        [Test]
        public void KillingAnEnemyGivesPendingExperience()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            int victim = Enemy(sim, 15, 1);

            sim.Step(Press(0));
            Idle(sim, 20);

            Assert.IsFalse(sim.Entities.Alive[victim], "Вихрь не убил цель");
            Assert.AreEqual(Progression.NormalKillXp, sim.PendingXp);
            Assert.AreEqual(Progression.NormalKillXp, sim.TakePendingXp());
            Assert.AreEqual(0, sim.PendingXp, "забранный опыт не обнулился");
        }

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

        [Test]
        public void OneGainCanRaiseSeveralLevels()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.AreEqual(2, camp.GainExperience(250));
            Assert.AreEqual(3, camp.Level);
            Assert.AreEqual(0, camp.Experience);
        }

        // ---- очки талантов ----

        [Test]
        public void EachLevelGivesOneTalentPoint()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.AreEqual(1, camp.AvailableTalentPoints);

            Assert.IsTrue(camp.TakeSabreTalent(SabreTalentLine.Whirlwind));
            Assert.AreEqual(0, camp.AvailableTalentPoints);
            Assert.IsFalse(camp.TakeSabreTalent(SabreTalentLine.Cleave), "талант без очка");

            camp.GainExperience(camp.ExperienceToNextLevel);
            Assert.IsTrue(camp.TakeSabreTalent(SabreTalentLine.Whirlwind));
            Assert.AreEqual(2, camp.SabreTalentRank(SabreTalentLine.Whirlwind));
        }

        [Test]
        public void LineStopsAfterFiveTalents()
        {
            var camp = PrototypeContent.NewCamp();
            for (int i = 0; i < 10; i++) camp.DeveloperGrantLevel();

            for (int i = 0; i < SabreTalents.TalentsPerLine; i++)
                Assert.IsTrue(camp.TakeSabreTalent(SabreTalentLine.Squall), $"талант {i + 1}");
            Assert.IsFalse(camp.TakeSabreTalent(SabreTalentLine.Squall), "шестой талант в направлении");
            Assert.AreEqual(SabreTalents.TalentsPerLine, camp.SabreTalentRank(SabreTalentLine.Squall));
        }

        [Test]
        public void ResetReturnsAllPoints()
        {
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperGrantLevel();
            camp.TakeSabreTalent(SabreTalentLine.Blaze);
            camp.TakeSabreTalent(SabreTalentLine.Cleave);

            camp.ResetTalents();

            Assert.AreEqual(camp.TalentPoints, camp.AvailableTalentPoints);
            Assert.AreEqual(0, camp.SabreTalentRank(SabreTalentLine.Blaze));
        }

        // ---- сохранение ----

        [Test]
        public void SaveKeepsLevelExperienceAndTalents()
        {
            var camp = PrototypeContent.NewCamp();
            camp.GainExperience(275);
            camp.TakeSabreTalent(SabreTalentLine.Whirlwind);
            camp.TakeSabreTalent(SabreTalentLine.Squall);

            var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), PrototypeContent.Items());

            ulong a = 0, b = 0;
            camp.HashInto(ref a);
            restored.HashInto(ref b);
            Assert.AreEqual(a, b);
            Assert.AreEqual(camp.Level, restored.Level);
            Assert.AreEqual(camp.Experience, restored.Experience);
            Assert.AreEqual(1, restored.SabreTalentRank(SabreTalentLine.Squall));
        }

        /// <summary>
        /// Старое сохранение без прокачки обязано открываться: игрок, у которого
        /// оно лежит, получает героя первого уровня, а не отказ.
        /// </summary>
        [Test]
        public void VersionOneSaveOpensAsFirstLevel()
        {
            var camp = PrototypeContent.NewCamp();
            camp.Earn(CurrencyType.Gold, 77);

            var restored = CampSaveCodec.Decode(EncodeVersionOne(camp), PrototypeContent.Items());

            Assert.AreEqual(1, restored.Level);
            Assert.AreEqual(0, restored.Experience);
            Assert.AreEqual(77, restored.Money(CurrencyType.Gold));
            Assert.AreEqual(1, restored.AvailableTalentPoints);
        }

        /// <summary>Прежний формат байт в байт: тот, что писал лагерь до прокачки.</summary>
        private static byte[] EncodeVersionOne(Camp camp)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(1); w.Write(camp.Act); w.Write(camp.Bag.Capacity);
                for (int i = 0; i < (int)CurrencyType.Count; i++) w.Write(camp.Money((CurrencyType)i));
                for (int i = 0; i < camp.Bag.Capacity; i++) { WriteItem(w, camp.Bag.At(i)); w.Write(camp.Bag.IsKept(i)); }
                for (int i = 0; i < (int)EquipSlot.Count; i++) WriteItem(w, camp.Worn.Worn((EquipSlot)i));
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
