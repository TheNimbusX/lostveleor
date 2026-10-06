using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Экономика лагеря 06.10 на стороне лагеря: ранг «босс + уровень», флаги прибытия,
    /// победы и сердца, скупка Вена, база героя по профилю. Начисление из забега
    /// проверяют тесты сессии (RunHaulTests).
    /// </summary>
    public sealed class CampEconomyTests
    {
        static int Base(string key) => StableId.Of(key);

        [Test] public void RanksNeedBossAndLevelTogether()
        {
            Assert.AreEqual(0, Camp.RankRequirement(1).BossIndex); Assert.AreEqual(6, Camp.RankRequirement(1).Level);
            Assert.AreEqual(1, Camp.RankRequirement(2).BossIndex); Assert.AreEqual(12, Camp.RankRequirement(2).Level);
            Assert.AreEqual(2, Camp.RankRequirement(3).BossIndex); Assert.AreEqual(18, Camp.RankRequirement(3).Level);
            Assert.AreEqual(-1, Camp.RankRequirement(4).BossIndex); Assert.AreEqual(int.MaxValue, Camp.RankRequirement(0).Level);

            var camp = PrototypeContent.NewCamp();
            camp.DeveloperCreditBoss(0); Assert.AreEqual(0, camp.CampRank, "босс без уровня");
            camp.DeveloperSetLevel(5); Assert.AreEqual(0, camp.CampRank);
            camp.DeveloperSetLevel(6); Assert.AreEqual(1, camp.CampRank);
            camp.DeveloperSetLevel(18); Assert.AreEqual(1, camp.CampRank, "уровень без второго босса");
            camp.DeveloperCreditBoss(2); Assert.AreEqual(1, camp.CampRank, "ранг 2 требует именно второго босса");
            camp.DeveloperCreditBoss(1); Assert.AreEqual(3, camp.CampRank);
            Assert.AreEqual(3, camp.Rank(CampResident.Trader)); Assert.AreEqual(3, camp.Rank(CampResident.Alchemist));
            camp.DeveloperSetLevel(11); Assert.AreEqual(1, camp.CampRank, "понижение уровня через F8 снимает ранги");
        }

        /// <summary>Победа над боссом: +победа, +сердце, сталь сразу. Чужой ключ и номер вне акта — ничего.</summary>
        [Test] public void BossDefeatCreditsHeartAndSteelUnknownKeyIgnored()
        {
            var camp = PrototypeContent.NewCamp(); var before = CampSaveCodec.Encode(camp);
            camp.RecordBossDefeat(StableId.Of("boss.unknown")); camp.RecordBossDefeat(0); camp.DeveloperCreditBoss(3); camp.DeveloperCreditBoss(-1);
            CollectionAssert.AreEqual(before, CampSaveCodec.Encode(camp));
            camp.RecordBossDefeat(RunBossKeys.Of(EnemyKind.ForestGuardian)); camp.RecordBossDefeat(RunBossKeys.ThicketMaster);
            Assert.AreEqual(2, camp.BossDefeats(RunBossKeys.ThicketMaster)); Assert.AreEqual(2, camp.HeartCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(2 * RunEconomy.BossSteel, camp.Money(CurrencyType.Steel));
            Assert.False(camp.BossDefeated(RunBossKeys.Second)); Assert.AreEqual(0, camp.HeartCount(RunBossKeys.Third));
        }

        /// <summary>
        /// Скупка 06.10: половина прежней цены. Корзина удачного леса — три редкие и одна
        /// эпическая вещь уровня 10 — даёт 44 золота, а не прежние ≈90.
        /// </summary>
        [Test] public void BuybackOfGoodForestIsFortyToSixty()
        {
            var basket = new[]
            {
                new ItemInstance(Base("base.rusty_sword"), 10, ItemRarity.Magic, 1),
                new ItemInstance(Base("base.quilted_jacket"), 10, ItemRarity.Magic, 2),
                new ItemInstance(Base("base.sea_knot"), 10, ItemRarity.Magic, 3),
                new ItemInstance(Base("base.officer_sabre"), 10, ItemRarity.Rare, 4),
            };
            var camp = new Camp(PrototypeContent.Items()); int sum = 0;
            foreach (var item in basket) { sum += Camp.PriceOf(in item); camp.Bag.Add(in item); }
            Assert.AreEqual(44, sum);
            Assert.GreaterOrEqual(sum, 40); Assert.LessOrEqual(sum, 60);
            int gold = 0; for (int slot = 0; slot < basket.Length; slot++) gold += camp.SellToTrader(slot);
            Assert.AreEqual(44, gold); Assert.AreEqual(44, camp.Money(CurrencyType.Gold));
        }

        /// <summary>Цены лавки владелец не трогал: товар акта I стоит 18 (обычный) и 36 (редкий).</summary>
        [Test] public void ActOneStockPricesUnchanged()
        {
            var normal = new ItemInstance(Base("base.rusty_sword"), 3, ItemRarity.Normal, 1);
            var magic = new ItemInstance(Base("base.rusty_sword"), 3, ItemRarity.Magic, 2);
            var camp = new Camp(PrototypeContent.Items());
            Assert.AreEqual(18, camp.BuyPriceOf(in normal)); Assert.AreEqual(36, camp.BuyPriceOf(in magic));
            for (int i = 0; i < camp.TraderStockCount; i++)
            {
                var item = camp.TraderStock(i); if (item.IsEmpty) continue;
                Assert.AreEqual(3, item.ItemLevel);
                Assert.AreEqual(item.Rarity == ItemRarity.Normal ? 18 : 36, camp.BuyPriceOf(in item));
            }
        }

        /// <summary>Купил и тут же продал — всегда в убыток: иначе лавка печатает золото.</summary>
        [Test] public void SellPriceAlwaysBelowBuyPrice()
        {
            var camp = new Camp(PrototypeContent.Items());
            for (int rarity = 0; rarity <= (int)ItemRarity.Unique; rarity++)
                for (short level = 0; level <= 80; level++)
                {
                    var item = new ItemInstance(Base("base.rusty_sword"), level, (ItemRarity)rarity, 5);
                    Assert.GreaterOrEqual(Camp.PriceOf(in item), 1);
                    Assert.Less(Camp.PriceOf(in item), camp.BuyPriceOf(in item));
                }
        }

        /// <summary>
        /// Флаг прибытия поднимается один раз, гаснет только подтверждением и переживает
        /// сохранение; уже показанное после перезагрузки не объявляется заново.
        /// </summary>
        [Test] public void PendingUnlocksRaiseOnceAndAcknowledge()
        {
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperSetLevel(Camp.ResidentsLevel);
            Assert.AreEqual(CampUnlock.Trader | CampUnlock.Alchemist, camp.PendingUnlocks);
            camp.AcknowledgeUnlocks(CampUnlock.Trader); Assert.AreEqual(CampUnlock.Alchemist, camp.PendingUnlocks);
            camp.DeveloperSetLevel(4); camp.GainExperience(camp.ExperienceToNextLevel);
            Assert.AreEqual(CampUnlock.Alchemist, camp.PendingUnlocks, "Вен уже показан");
            camp = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.AreEqual(CampUnlock.Alchemist, camp.PendingUnlocks);
            camp.RecordRealAttemptEnded(1, 0); Assert.AreEqual(CampUnlock.Alchemist | CampUnlock.TravelTable, camp.PendingUnlocks);
            camp.AcknowledgeUnlocks(CampUnlock.Alchemist | CampUnlock.TravelTable); Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
            camp.DeveloperSetLevel(6); Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
            camp.DeveloperCreditBoss(0); Assert.AreEqual(CampUnlock.Rank1, camp.PendingUnlocks);
            Assert.AreEqual(6, camp.TraderStockCount, "ранг 1 расширяет лавку Вена");
            camp.AcknowledgeUnlocks(CampUnlock.Rank1); camp.DeveloperCreditBoss(0); camp.RecordRealAttemptEnded(1, 0);
            Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
            camp = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
        }

        /// <summary>
        /// Sandbox тестов и проверочных лагерей — сразу ранг 3, все жители и стол, без
        /// уведомлений, с эталонным героем. Прогрессивный профиль начинает героем 200/40,
        /// тестовый забег всегда эталонный (решение M6).
        /// </summary>
        [Test] public void SandboxIsRankThree()
        {
            var sandbox = new Camp(PrototypeContent.Items());
            Assert.AreEqual(3, sandbox.CampRank); Assert.True(sandbox.HasTravelTable);
            foreach (CampResident resident in System.Enum.GetValues(typeof(CampResident)))
            { Assert.True(sandbox.HasResident(resident)); Assert.AreEqual(3, sandbox.Rank(resident)); }
            Assert.AreEqual(CampUnlock.None, sandbox.PendingUnlocks);
            Assert.True(sandbox.EniActionUnlocked(EniAction.Add)); Assert.True(sandbox.PotionUnlocked(PotionKind.Clear));
            Assert.True(sandbox.HeroBaselineFor(false).Equals(HeroBaseline.Reference));
            sandbox.DeveloperSetLevel(20); sandbox.RecordRealAttemptEnded(1, 0);
            Assert.AreEqual(CampUnlock.None, sandbox.PendingUnlocks);
            var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(sandbox), sandbox.Items);
            Assert.False(restored.IsProgressive); Assert.AreEqual(3, restored.CampRank);

            var fresh = PrototypeContent.NewCamp();
            Assert.True(fresh.HeroBaselineFor(false).Equals(HeroBaseline.Fresh));
            Assert.True(fresh.HeroBaselineFor(true).Equals(HeroBaseline.Reference));
            Assert.AreEqual(200, 150 + HeroBaseline.Fresh.Health); Assert.AreEqual(40, 34 + HeroBaseline.Fresh.Damage);
            Assert.AreEqual(HeroBaseline.Reference.Lavidium, HeroBaseline.Fresh.Lavidium);
        }
    }
}
