using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampProgressionTests
    {
        [Test] public void RealAttemptStagesAndRanksAreIndependentOfChapters()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.True(camp.UsesCampProgression); Assert.True(camp.HasResident(CampResident.Smith));
            Assert.False(camp.HasResident(CampResident.Trader)); Assert.False(camp.HasResident(CampResident.Alchemist)); Assert.False(camp.HasTravelTable);
            Assert.AreEqual(CampUpgradeResult.LevelRequired, camp.TryUpgradeResident(CampResident.Smith));
            camp.DeveloperSetLevel(2); Assert.AreEqual(1, camp.AvailableCampPoints);
            Assert.AreEqual(CampUpgradeResult.Success, camp.TryUpgradeResident(CampResident.Smith));
            Assert.AreEqual(0, camp.AvailableCampPoints); Assert.AreEqual(CampChapterStatus.Active, camp.ChapterStatus(CampResident.Smith));
            camp.RecordRealAttemptEnded(3, 0); Assert.True(camp.HasResident(CampResident.Trader)); Assert.False(camp.HasTravelTable);
            camp.RecordRealAttemptEnded(1, 0); Assert.True(camp.HasTravelTable); Assert.False(camp.HasResident(CampResident.Alchemist));
            camp.RecordRealAttemptEnded(1, 0); Assert.True(camp.HasResident(CampResident.Alchemist));
            camp.DeveloperSetLevel(4); Assert.AreEqual(CampUpgradeResult.LevelRequired, camp.TryUpgradeResident(CampResident.Smith));
            camp.DeveloperSetLevel(5); Assert.AreEqual(CampUpgradeResult.Success, camp.TryUpgradeResident(CampResident.Smith));
            camp.DeveloperSetLevel(7); Assert.AreEqual(CampUpgradeResult.LevelRequired, camp.TryUpgradeResident(CampResident.Smith));
            camp.DeveloperSetLevel(10);
            Assert.AreEqual(CampUpgradeResult.Success, camp.TryUpgradeResident(CampResident.Smith));
            for (int resident = 1; resident < 3; resident++) for (int rank = 0; rank < 3; rank++)
                Assert.AreEqual(CampUpgradeResult.Success, camp.TryUpgradeResident((CampResident)resident));
            Assert.AreEqual(9, camp.SpentCampPoints); Assert.AreEqual(0, camp.AvailableCampPoints);
            Assert.AreEqual(CampUpgradeResult.MaximumRank, camp.TryUpgradeResident(CampResident.Smith));
        }
        [Test] public void ChapterHistoryPersistsBeforeActivationAndRewardsOnlyOnce()
        {
            var camp = PrototypeContent.NewCamp();
            camp.RecordRealPotionUsed(PotionKind.SmallHealth); camp.RecordRealPotionUsed(PotionKind.SmallLavidium);
            camp.RecordRealAttemptEnded(4, 1); camp.RecordRealAttemptEnded(1, 0); camp.RecordRealAttemptEnded(1, 0);
            Assert.AreEqual(CampChapterStatus.Hidden, camp.ChapterStatus(CampResident.Trader));
            Assert.False(camp.TurnInChapter(CampResident.Smith)); camp.DiscussSmithFind();
            Assert.True(camp.TurnInChapter(CampResident.Smith)); Assert.AreEqual(1, camp.MaterialCount(ForgeMaterial.Steel));
            Assert.AreEqual(CampChapterStatus.Ready, camp.ChapterStatus(CampResident.Trader));
            camp = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.True(camp.TurnInChapter(CampResident.Trader)); Assert.AreEqual(50, camp.Money(CurrencyType.Gold)); Assert.AreEqual(2, camp.MaterialCount(ForgeMaterial.Steel));
            int healthBefore = camp.PotionCount(PotionKind.SmallHealth), lavidiumBefore = camp.PotionCount(PotionKind.SmallLavidium);
            Assert.AreEqual(CampChapterStatus.Ready, camp.ChapterStatus(CampResident.Alchemist)); Assert.True(camp.TurnInChapter(CampResident.Alchemist));
            Assert.AreEqual(healthBefore + 2, camp.PotionCount(PotionKind.SmallHealth)); Assert.AreEqual(lavidiumBefore + 2, camp.PotionCount(PotionKind.SmallLavidium));
            foreach (CampResident resident in Enum.GetValues(typeof(CampResident))) Assert.False(camp.TurnInChapter(resident));
            Assert.AreEqual(50, camp.Money(CurrencyType.Gold));
        }
        [Test] public void ProgressiveSaveKeepsHiddenStockRanksMaterialsAndChoices()
        {
            var camp = PrototypeContent.NewCamp(); var early = CampSaveCodec.Encode(camp);
            var restored = CampSaveCodec.Decode(early, camp.Items); CollectionAssert.AreEqual(early, CampSaveCodec.Encode(restored));
            restored.RecordRealAttemptEnded(1, 0); Assert.AreEqual(4, restored.TraderStockCount);
            restored.DeveloperSetLevel(10); for (int rank = 0; rank < 3; rank++) restored.TryUpgradeResident(CampResident.Trader);
            restored.ReserveTraderStock(0); restored.ChooseTraderCategory(ItemCategory.Talisman); restored.EarnForgeMaterial(ForgeMaterial.Core, 2);
            var save = CampSaveCodec.Encode(restored); var back = CampSaveCodec.Decode(save, camp.Items);
            CollectionAssert.AreEqual(save, CampSaveCodec.Encode(back)); Assert.AreEqual(3, back.Rank(CampResident.Trader));
            Assert.AreEqual(0, back.TraderReservedSlot); Assert.AreEqual((int)ItemCategory.Talisman, back.TraderCategoryChoice); Assert.AreEqual(2, back.MaterialCount(ForgeMaterial.Core));
        }
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] public void LegacyFixedLayoutKeepsOldServicesSoldSlotsRecipesAndOrders(int version)
        {
            var db = PrototypeContent.Items(); var bytes = LegacySave(db, version);
            var camp = CampSaveCodec.Decode(bytes, db);
            Assert.False(camp.UsesCampProgression); Assert.True(camp.HasTravelTable); Assert.True(camp.HasResident(CampResident.Alchemist));
            Assert.AreEqual(4, camp.TraderStockCount); Assert.True(camp.TraderStock(1).IsEmpty); Assert.AreEqual(13, camp.TraderGeneration);
            Assert.AreEqual(2, camp.Bag.At(7).ReforgeCount); Assert.True(camp.Bag.IsKept(7)); Assert.AreEqual(500, camp.Money(CurrencyType.Gold));
            if (version >= 6) { Assert.AreEqual(2, camp.PotionCount(PotionKind.LargeHealth)); Assert.AreEqual(4, camp.PotionCount(PotionKind.LargeLavidium)); }
            if (version >= 8) { Assert.AreEqual(5, camp.PotionCount(PotionKind.LivingResin)); Assert.AreEqual(6, camp.PotionCount(PotionKind.LavidiumSurge)); Assert.AreEqual(AlchemistOrderStatus.Unlocked, camp.AlchemyStatus(AlchemistOrder.Resin)); }
            var next = CampSaveCodec.Encode(camp); CollectionAssert.AreEqual(next, CampSaveCodec.Encode(CampSaveCodec.Decode(next, db)));
        }
        static byte[] LegacySave(ItemDatabase db, int version)
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(version); w.Write(3); w.Write(48);
                w.Write(500); w.Write(20); w.Write(3);
                var item = new ItemInstance(StableId.Of("base.rusty_sword"), 31, ItemRarity.Rare, 123, 0x21);
                for (int i = 0; i < 48; i++) { WriteLegacyItem(w, i == 7 ? item : default); w.Write(i == 7); }
                for (int i = 0; i < (int)EquipSlot.Count; i++) WriteLegacyItem(w, default);
                w.Write(10); w.Write(1); w.Write(13); w.Write(false); w.Write(4);
                for (int i = 0; i < 4; i++) WriteLegacyItem(w, i == 1 ? default : new ItemInstance(StableId.Of("base.rusty_sword"), 9, ItemRarity.Normal, (ulong)(i + 1)));
                if (version >= 6) { for (int i = 0; i < (version >= 8 ? 6 : 4); i++) w.Write(i + 1); w.Write((byte)(version >= 8 ? 1 : 3)); if (version >= 8) w.Write((byte)3); }
                // Версии 6–7 содержат флаги размеров, версия 8 — два вида.
                if (version >= 7) { w.Write(1); w.Write(item.BaseId); }
                if (version >= 8) { w.Write(true); w.Write((byte)AlchemistOrderStatus.Unlocked); w.Write((byte)AlchemistOrderStatus.Unlocked); }
                w.Flush(); uint hash = 2166136261; foreach (byte b in stream.ToArray()) { hash ^= b; hash = unchecked(hash * 16777619); }
                w.Write(hash); return stream.ToArray();
            }
        }
        static void WriteLegacyItem(BinaryWriter writer, ItemInstance item)
        { writer.Write(item.BaseId); writer.Write(item.ItemLevel); writer.Write((byte)item.Rarity); writer.Write(item.Seed); writer.Write(item.ForgeRecipe); }
    }
}
