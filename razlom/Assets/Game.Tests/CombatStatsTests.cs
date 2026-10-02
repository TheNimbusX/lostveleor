using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Статы решают бой.
    ///
    /// Приёмка задачи: надетый предмет меняет числа в бою, снятый возвращает их
    /// ровно назад, а порядок надевания на результат не влияет.
    ///
    /// Базовые числа боя здесь не закрепляются: урон героя —
    /// PelagProgressionTests, темп Хранителя — GuardianTempoTests, шаг толпы
    /// (3,1 м/с) — хеши CrowdStepHashPinTests.
    /// </summary>
    public class CombatStatsTests
    {
        private static ItemInstance Sword(ulong seed, short itemLevel = 20)
            => new ItemInstance(StableId.Of("base.rusty_sword"), itemLevel, ItemRarity.Rare, seed);

        private static ItemInstance Jacket(ulong seed, short itemLevel = 20)
            => new ItemInstance(StableId.Of("base.leather_jacket"), itemLevel, ItemRarity.Rare, seed);

        private static Equipment Bound(ItemDatabase items, StatSheet sheet)
        {
            var equipment = new Equipment(items);
            equipment.Bind(sheet);
            return equipment;
        }

        private static Simulation Arena(ulong seed = 1UL, int enemies = 4)
        {
            var sim = new Simulation(seed, 64);
            sim.SetupTestArena(enemies);
            return sim;
        }

        // ---- перевод статов в числа тика ----

        [Test]
        public void AttackSpeed_TurnsIntoCooldownTicks()
        {
            Assert.AreEqual(24, CombatStats.AttackCooldownTicks(Fix64.Ratio(30, 24)),
                "1.25 удара в секунду при 30 Гц — это 24 тика");
            Assert.AreEqual(36, CombatStats.AttackCooldownTicks(Fix64.Ratio(30, 36)),
                "0.833 удара в секунду — это 36 тиков");
        }

        // ---- снаряжение ----

        [Test]
        public void Unequip_RestoresTheSheetExactly()
        {
            Simulation sim = Arena();
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];
            var equipment = Bound(PrototypeContent.Items(), sheet);

            int bareDamage = sim.Entities.Damage[Simulation.PlayerId];
            ulong bareHash = Hashing.Offset;
            sheet.HashInto(ref bareHash);

            ItemInstance replaced;
            Assert.IsTrue(equipment.Equip(Sword(7UL), out replaced), "меч должен надеться");
            Assert.IsTrue(replaced.IsEmpty, "в пустом слоте нечего было менять");
            sim.RefreshPlayerStats(false);
            Assert.Greater(sim.Entities.Damage[Simulation.PlayerId], bareDamage,
                "надетое оружие обязано менять урон, иначе весь лут — украшение");

            equipment.Unequip(EquipSlot.Weapon);
            sim.RefreshPlayerStats(false);

            ulong afterHash = Hashing.Offset;
            sheet.HashInto(ref afterHash);

            Assert.AreEqual(bareDamage, sim.Entities.Damage[Simulation.PlayerId], "урон вернулся");
            Assert.AreEqual(bareHash, afterHash,
                "снятие обязано убирать ровно свои прибавки и ничего кроме них");
        }

        // ---- атака по приказу ----

        [Test]
        public void Player_DoesNotAttackWithoutTheOrder()
        {
            Simulation sim = Arena(31UL, 6);
            int enemyHealth = sim.Entities.Health[1];

            for (int t = 0; t < 300; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            int totalDamage = 0;
            for (int i = 1; i < sim.Entities.Count; i++)
                totalDamage += sim.Entities.MaxHealth[i] - sim.Entities.Health[i];

            Assert.AreEqual(0, totalDamage,
                "персонаж, который бьёт сам по себе, отнимает у игрока единственное решение боя");
            Assert.AreEqual(enemyHealth, sim.Entities.Health[1]);
        }

        // ---- приказ атаковать ----

        /// <summary>
        /// Серия сабли (01.10): один клик — один удар, без «приказа бить, пока
        /// цель жива». Бьёт тот, кто держит кнопку или нажимает снова.
        /// </summary>
        [Test]
        public void OneClick_IsOneHit_NoStandingOrder()
        {
            Simulation sim = Arena(51UL, 0);

            // Враг прямо перед игроком: взгляд по умолчанию по оси X.
            int victim = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 5000, Faction.Orvill);

            var click = new InputFrame
            {
                Flags = (byte)InputFlags.AttackPressed,
                AttackTarget = victim,
                Aim = new FixVec2(Fix64.FromInt(3), Fix64.Zero),
            };
            sim.Step(in click);
            for (int t = 0; t < 200; t++)
            {
                InputFrame idle = InputFrame.Empty;
                sim.Step(in idle);
            }

            Assert.AreEqual(1, sim.SabreSwing.Serial, "один клик — один удар");
            Assert.Less(sim.Entities.Health[victim], 5000, "и он попал");
            Assert.AreEqual(-1, sim.AttackTarget, "приказа «бей, пока жив» больше нет");
        }

        // ---- расталкивание тел ----

        [Test]
        public void Bodies_DoNotStackInOnePoint()
        {
            var sim = new Simulation(7UL, 32);
            sim.SetupTestArena(0);

            // Игрок «мёртв», чтобы враги никуда не шли: проверяется именно
            // расталкивание, а не движение к цели.
            sim.Entities.Alive[Simulation.PlayerId] = false;

            var spot = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            int a = sim.Entities.Spawn(spot, 100, Faction.Orvill);
            int b = sim.Entities.Spawn(spot, 100, Faction.Orvill);

            for (int t = 0; t < 120; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            float distance = FixVec2.Distance(sim.Entities.Position[a],
                sim.Entities.Position[b]).ToFloat();

            Assert.Greater(distance, 0.7f,
                "два тела в одной точке обязаны разъехаться, иначе толпа не читается");
        }

        // ---- защита ----

        [Test]
        public void Armor_MitigatesSmallHitsMuchMoreThanBigOnes()
        {
            // Ровно та причина, по которой выбрана кривая, а не плоский процент:
            // одна и та же броня спасает от роя и почти не мешает удару босса.
            Assert.AreEqual(1, CombatStats.MitigateByArmor(10, Fix64.FromInt(500)),
                "мелкий удар при броне 500 гасится почти целиком");
            Assert.AreEqual(909, CombatStats.MitigateByArmor(1000, Fix64.FromInt(500)),
                "крупный удар при той же броне гасится на девять процентов");
        }

        [Test]
        public void Resistance_IsCappedAtThreeQuarters()
        {
            Assert.AreEqual(70, CombatStats.MitigateByResistance(100, Fix64.Ratio(30, 100)),
                "30% сопротивления снимают тридцать процентов");
            Assert.AreEqual(25, CombatStats.MitigateByResistance(100, Fix64.One),
                "сто процентов сопротивления обрезаются до потолка в 75%");
            Assert.AreEqual(25, CombatStats.MitigateByResistance(100, Fix64.FromInt(4)),
                "выше потолка не пускает никакая сумма аффиксов");
        }

        // ---- забег ----

        [Test]
        public void Equipment_SurvivesEnteringTheNextRift()
        {
            ItemDatabase items = PrototypeContent.Items();

            var sim = new Simulation(4242UL, 1024);
            var run = new RiftRun(sim, PrototypeContent.Modules(), items,
                PrototypeContent.ItemBaseIds());

            var equipment = Bound(items, sim.Entities.Stats[Simulation.PlayerId]);
            ItemInstance replaced;
            equipment.Equip(Sword(0x5EEDUL), out replaced);
            run.PlayerEquipment = equipment;

            run.StartRun();

            int armedDamage = sim.Entities.Damage[Simulation.PlayerId];
            Assert.Greater(armedDamage, 34, "в первом Разломе оружие уже работает");

            // Зачищаем напрямую: проверяется петля со снаряжением, а не бой.
            for (int i = 0; i < sim.Entities.Count; i++)
                if (sim.Entities.Side[i] != Faction.Wole) sim.Entities.Alive[i] = false;

            InputFrame idle = InputFrame.Empty;
            run.Step(in idle);

            // Враги мертвы, но экран награды теперь приходит только у выхода —
            // телепортируем игрока к нему, как делает RiftRunTests.
            sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(in idle);

            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);

            var pick = new InputFrame { Command = (byte)RunCommand.ChooseReward1 };
            run.Step(in pick);

            Assert.AreEqual(2, run.Depth, "начался следующий Разлом");
            Assert.GreaterOrEqual(sim.Entities.Damage[Simulation.PlayerId], armedDamage,
                "вход в Разлом рождает игрока заново — снаряжение обязано вернуться на лист");
            // Здоровье между аренами переносится, но урона здесь не было —
            // значит и недостачи нет, и прибавки награды не пропали.
            Assert.AreEqual(sim.Entities.MaxHealth[Simulation.PlayerId],
                sim.Entities.Health[Simulation.PlayerId],
                "без полученного урона игрок входит в новый Разлом с полным здоровьем");
        }

    }
}
