using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Основа постоянной прокачки: лавидий, опыт, уровень и база героя.
    ///
    /// Числа ресурса и стоимостей — решения владельца (13 и 15 сентября),
    /// база героя — решение от 29 сентября (уровень статов не даёт, герой —
    /// прежний 5-й уровень), поэтому проверяются точно. Награды за убийство
    /// и кривая уровня — заглушки баланса, и тесты держат только их правила.
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

        // ---- база героя вместо статов за уровень ----

        /// <summary>
        /// Решение владельца от 29 сентября: герой всегда — прежний 5-й уровень,
        /// то есть четыре прежних прибавки уровня: +120 жизни, +20 урона, +40 лавидия.
        /// </summary>
        [Test]
        public void HeroBaselineAddsTheFormerFifthLevel()
        {
            Assert.AreEqual(5, Progression.ReferenceHeroLevel);
            Assert.AreEqual(120, Progression.HeroBaselineHealth);
            Assert.AreEqual(20, Progression.HeroBaselineDamage);
            Assert.AreEqual(40, Progression.HeroBaselineLavidium);
            Assert.AreEqual(270, Progression.ReferenceHeroHealth);
            Assert.AreEqual(54, Progression.ReferenceHeroDamage);

            var sim = Arena(AbilityDefinition.Whirlwind());
            Assert.IsFalse(sim.HasHeroBaseline, "голая симуляция тестов — без базы героя");
            int health = sim.Entities.MaxHealth[Player];
            int damage = sim.Entities.Damage[Player];
            int lavidium = sim.Entities.MaxLavidium[Player];

            sim.ApplyHeroBaseline();

            Assert.IsTrue(sim.HasHeroBaseline);
            Assert.AreEqual(health + 120, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(damage + 20, sim.Entities.Damage[Player]);
            Assert.AreEqual(Progression.ReferenceHeroDamage, sim.Entities.Damage[Player]);
            Assert.AreEqual(lavidium + 40, sim.Entities.MaxLavidium[Player]);
        }

        /// <summary>
        /// Первая постановка базы на живого героя доводит и текущее здоровье,
        /// повтор ничего не меняет — лечения «за уровень» больше нет. Расстановка
        /// стирает модификаторы, и база обязана вернуться вместе с героем.
        /// </summary>
        [Test]
        public void HeroBaselineIsAppliedOnceAndSurvivesRespawn()
        {
            var sim = Arena(AbilityDefinition.Whirlwind());
            int max = sim.Entities.MaxHealth[Player];
            sim.Entities.Health[Player] = max - 500;

            sim.ApplyHeroBaseline();
            Assert.AreEqual(max + 120, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(max - 500 + 120, sim.Entities.Health[Player], "прибавка к потолку — и в текущее");

            ulong hash = sim.StateHash();
            sim.ApplyHeroBaseline();
            Assert.AreEqual(max - 500 + 120, sim.Entities.Health[Player], "повтор — без лечения");
            Assert.AreEqual(max + 120, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(hash, sim.StateHash(), "повтор не меняет состояние");

            sim.SetupTestArena(0);
            Assert.AreEqual(max + 120, sim.Entities.MaxHealth[Player], "расстановка потеряла базу героя");
            Assert.AreEqual(max + 120, sim.Entities.Health[Player]);
            Assert.AreEqual(Progression.ReferenceHeroDamage, sim.Entities.Damage[Player]);
        }

        /// <summary>
        /// Главное правило 29 сентября: статы героя не зависят от уровня лагеря.
        /// Герой 1-го и 20-го уровня в Разломе леса одинаков — 270 здоровья и 54
        /// урона, как прежний 5-й уровень, — и в лагере тоже.
        /// </summary>
        [TestCase(1)]
        [TestCase(20)]
        public void HeroStatsIndependentOfCampLevel(int level)
        {
            var location = ArenaEncounterTests.ForestLocation();
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperSetLevel(level);
            var session = new GameSession(7, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            Assert.AreEqual(level, session.Camp.Level);

            Assert.IsTrue(session.CampSim.HasHeroBaseline, "лагерь без базы героя");
            Assert.AreEqual(240, session.CampSim.Entities.MaxLavidium[Player], "лагерь: 200 + 40 базы героя");

            session.EnterRift();
            var sim = session.Run.Sim;
            Assert.IsTrue(sim.HasHeroBaseline, "Разлом без базы героя");
            Assert.AreEqual(Progression.ReferenceHeroHealth, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(270, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(270, sim.Entities.Health[Player], "в Разлом входят с полным здоровьем");
            Assert.AreEqual(Progression.ReferenceHeroDamage, sim.Entities.Damage[Player]);
            Assert.AreEqual(54, sim.Entities.Damage[Player]);
            Assert.AreEqual(240, sim.Entities.MaxLavidium[Player]);
        }

        /// <summary>
        /// Уровень лагеря не входит в хеш симуляции: лагерь и Разлом героя 1-го и
        /// 20-го уровня с одним сидом — одно и то же состояние.
        /// </summary>
        [Test]
        public void CampLevelIsNotInTheSimulationHash()
        {
            var location = ArenaEncounterTests.ForestLocation();
            var low = PrototypeContent.NewCamp();
            var high = PrototypeContent.NewCamp();
            high.DeveloperSetLevel(20);
            var lowSession = new GameSession(7, low, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            var highSession = new GameSession(7, high, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            Assert.AreEqual(lowSession.CampSim.StateHash(), highSession.CampSim.StateHash(), "лагерь");

            lowSession.EnterRift();
            highSession.EnterRift();
            for (int tick = 0; tick < 60; tick++)
            {
                lowSession.Step(InputFrame.Empty);
                highSession.Step(InputFrame.Empty);
            }
            Assert.AreEqual(lowSession.Run.Sim.StateHash(), highSession.Run.Sim.StateHash(), "Разлом");
        }

        /// <summary>
        /// Повышение посреди боя ничего не даёт герою: ни потолка, ни лечения.
        /// Путь кнопок разработчика — ручной уровень и SyncPlayerLevel — тоже
        /// только меняет число уровня.
        /// </summary>
        [Test]
        public void LevelUpMidRunChangesNeitherStatsNorHealth()
        {
            var location = ArenaEncounterTests.ForestLocation();
            var camp = PrototypeContent.NewCamp();
            var session = new GameSession(7, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            var sim = session.Run.Sim;
            sim.Entities.Health[Player] = 100;
            ulong hash = sim.StateHash();

            Assert.AreEqual(1, camp.GainExperience(camp.ExperienceToNextLevel));
            session.SyncPlayerLevel();
            camp.DeveloperGrantLevel();
            session.SyncPlayerLevel();
            camp.DeveloperSetLevel(20);
            session.SyncPlayerLevel();

            Assert.AreEqual(20, camp.Level);
            Assert.AreEqual(100, sim.Entities.Health[Player], "повышение больше не лечит");
            Assert.AreEqual(270, sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(54, sim.Entities.Damage[Player]);
            Assert.AreEqual(240, session.CampSim.Entities.MaxLavidium[Player]);
            Assert.AreEqual(hash, sim.StateHash(), "уровень изменил состояние боя");
        }

        // ---- сохранение ----

        /// <summary>
        /// Сохранение героя высокого уровня (риск плана 29 сентября): уровень и опыт
        /// переезжают как есть, а статы герой берёт от базы — тот же 270 / 54.
        /// </summary>
        [Test]
        public void SavedHighLevelCampLoadsWithTheReferenceHero()
        {
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperSetLevel(12);
            camp.GainExperience(40);

            var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), PrototypeContent.Items());
            Assert.AreEqual(12, restored.Level);
            Assert.AreEqual(40, restored.Experience);

            var location = ArenaEncounterTests.ForestLocation();
            var session = new GameSession(7, restored, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            Assert.AreEqual(270, session.Run.Sim.Entities.MaxHealth[Player]);
            Assert.AreEqual(54, session.Run.Sim.Entities.Damage[Player]);
        }

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
