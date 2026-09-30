using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampForgeSafetyTests
    {
        static Camp Ready(ItemDatabase db = null, int materials = 5)
        {
            var camp = new Camp(db ?? PrototypeContent.Items()); camp.DeveloperSetLevel(10);
            for (int rank = 0; rank < 3; rank++) camp.TryUpgradeResident(CampResident.Smith);
            camp.Earn(CurrencyType.Gold, 5000); camp.Earn(CurrencyType.Shards, 1000);
            camp.EarnForgeMaterial(ForgeMaterial.Steel, materials); camp.EarnForgeMaterial(ForgeMaterial.Core, materials); return camp;
        }
        [Test] public void WornLegacyPipsRemainThroughReplacementLastRefineSaveAndStatReapply()
        {
            var camp = Ready(); var original = new ItemInstance(StableId.Of("base.rusty_sword"), 31, ItemRarity.Rare, 123, 0x21);
            var sheet = new StatSheet(); for (int i = 0; i < (int)StatType.Count; i++) sheet.SetBase((StatType)i, Fix64.FromInt(10));
            camp.Worn.Bind(sheet); camp.Worn.Equip(original, out _);
            var before = new GeneratedItem(); var replacement = new GeneratedItem(); ItemGenerator.Generate(original, camp.Items, before);
            var preview = camp.PreviewForge(ForgeTarget.Worn(EquipSlot.Weapon), ForgeOperation.Replace, 0);
            Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview));
            var next = camp.Worn.Worn(EquipSlot.Weapon); Assert.AreEqual(31, next.ItemLevel); Assert.AreEqual(25, next.OriginalLevel);
            Assert.AreEqual(2, next.ReforgeCount); Assert.AreEqual(0x21, next.ForgeRecipe); Assert.AreEqual(1, next.Crafting.Count);
            ItemGenerator.Generate(next, camp.Items, replacement);
            for (int i = 1; i < before.AffixCount; i++) { Assert.AreEqual(before.GetAffix(i).AffixId, replacement.GetAffix(i).AffixId); Assert.AreEqual(before.GetAffix(i).Value, replacement.GetAffix(i).Value); }
            preview = camp.PreviewForge(ForgeTarget.Worn(EquipSlot.Weapon), ForgeOperation.Refine, 0);
            Assert.AreEqual(90, preview.Gold); Assert.AreEqual(9, preview.Shards); Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview));
            next = camp.Worn.Worn(EquipSlot.Weapon); Assert.AreEqual(34, next.ItemLevel); Assert.AreEqual(25, next.OriginalLevel); Assert.AreEqual(3, next.ReforgeCount);
            var bytes = CampSaveCodec.Encode(camp); Assert.AreEqual(SmithResult.StalePreview, camp.CommitForge(preview)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            Assert.AreEqual(SmithResult.Exhausted, camp.Reforge(EquipSlot.Weapon, 0));
            var restored = CampSaveCodec.Decode(bytes, camp.Items); Assert.True(next.SameRecipe(restored.Worn.Worn(EquipSlot.Weapon)));
            var afterSheet = new StatSheet(); for (int i = 0; i < (int)StatType.Count; i++) afterSheet.SetBase((StatType)i, Fix64.FromInt(10)); restored.Worn.Bind(afterSheet);
            for (int i = 0; i < (int)StatType.Count; i++) Assert.AreEqual(sheet.Get((StatType)i), afterSheet.Get((StatType)i));
        }
        [Test] public void InvalidOrForeignPreviewsAreNotAffordableAndChangingFundsCannotPartiallyCommit()
        {
            var camp = Ready(); camp.Bag.Put(0, new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Rare, 123), true);
            Assert.False(camp.CanAffordForge(camp.PreviewForge(ForgeTarget.Bag(47), ForgeOperation.Replace)));
            var preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace); var foreign = Ready();
            Assert.False(foreign.CanAffordForge(preview)); Assert.AreEqual(SmithResult.StalePreview, foreign.CommitForge(preview));
            camp.Spend(CurrencyType.Gold, 5000); var bytes = CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.CommitForge(preview)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            camp.Earn(CurrencyType.Gold, 60); Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview)); Assert.True(camp.Bag.IsKept(0));
            bytes = CampSaveCodec.Encode(camp); Assert.AreEqual(SmithResult.StalePreview, camp.CommitForge(preview)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
        }
        static ItemDatabase Restrictions()
        {
            return new ItemDatabase(new[] { new ItemBaseDefinition(100, ItemCategory.Weapon), new ItemBaseDefinition(200, ItemCategory.Armor), new ItemBaseDefinition(300, ItemCategory.Artifact) },
                new[] {
                    new AffixDefinition(1001, 11, StatType.Damage, ModifierOp.Flat, Fix64.FromInt(2), Fix64.FromInt(6), 1, 1, AffixDefinition.Mask(ItemCategory.Weapon)),
                    new AffixDefinition(1002, 22, StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(15), Fix64.FromInt(60), 1, 1, AffixDefinition.Mask(ItemCategory.Weapon, ItemCategory.Armor)),
                    new AffixDefinition(1003, 33, StatType.AttackSpeed, ModifierOp.Increased, Fix64.Ratio(5,100), Fix64.Ratio(20,100), 10, 1, AffixDefinition.Mask(ItemCategory.Weapon)),
                    new AffixDefinition(1004, 44, StatType.Armor, ModifierOp.Flat, Fix64.FromInt(10), Fix64.FromInt(45), 1, 1, AffixDefinition.Mask(ItemCategory.Armor)) });
        }
        static ItemInstance Single(ItemDatabase db, int baseId, short level, int affix)
        {
            var generated = new GeneratedItem();
            for (ulong seed = 1; seed < 10000; seed++)
            {
                var item = new ItemInstance(baseId, level, ItemRarity.Magic, seed); ItemGenerator.Generate(item, db, generated);
                if (generated.AffixCount == 1 && generated.GetAffix(0).AffixId == affix) return item;
            }
            throw new InvalidOperationException("Нет контрольного сида");
        }
        [Test] public void TransferChecksDonorLevelCategoryAndRemainingTargetGroups()
        {
            var db = Restrictions(); var camp = Ready(db); var high = Single(db, 100, 20, 1003);
            camp.Bag.Put(1, high, false); camp.Bag.Put(0, Single(db, 100, 1, 1001), false);
            Assert.AreEqual(SmithResult.Incompatible, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, 0, donorSlot: 1).Status);
            camp.Bag.Put(0, Single(db, 200, 20, 1002), false);
            Assert.AreEqual(SmithResult.Incompatible, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, 0, donorSlot: 1).Status);
            var rare = new ItemInstance(100, 20, ItemRarity.Rare, 321); var generated = new GeneratedItem(); ItemGenerator.Generate(rare, db, generated);
            int damage = -1; for (int i = 0; i < generated.AffixCount; i++) if (generated.GetAffix(i).AffixId == 1001) damage = i;
            camp.Bag.Put(0, rare, false); camp.Bag.Put(1, Single(db, 100, 20, 1002), false);
            Assert.AreEqual(SmithResult.Incompatible, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, damage, donorSlot: 1).Status);
            camp.EquipFromBag(1); Assert.AreEqual(SmithResult.InvalidDonor, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, damage, donorSlot: 1).Status);
            camp.Bag.Put(1, new ItemInstance(300, 20, ItemRarity.Rare, 22), false);
            Assert.AreEqual(SmithResult.InvalidDonor, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, damage, donorSlot: 1).Status);
        }
        [Test] public void ChangedOrMovedDonorMakesTransferTokenStaleWithoutSpending()
        {
            var db = Restrictions(); var camp = Ready(db); camp.Bag.Put(0, Single(db, 100, 20, 1001), false); camp.Bag.Put(1, Single(db, 100, 20, 1002), false);
            var preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, donorSlot: 1); Assert.AreEqual(SmithResult.Success, preview.Status);
            camp.Bag.Swap(1, 3); var bytes = CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.StalePreview, camp.CommitForge(preview)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            camp.Bag.Swap(1, 3); camp.Bag.SetKeep(1, true); bytes = CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.Protected, camp.CommitForge(preview)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
        }
        [Test] public void MissingMaterialsKeepTargetDonorAndWalletUntouched()
        {
            var db = Restrictions(); var camp = Ready(db, materials: 0);
            camp.Bag.Put(0, Single(db, 100, 20, 1001), false); camp.Bag.Put(1, Single(db, 100, 20, 1002), false);
            var bytes = CampSaveCodec.Encode(camp);
            var replace = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace);
            Assert.AreEqual(SmithResult.Success, replace.Status); Assert.False(camp.CanAffordForge(replace));
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.CommitForge(replace)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            var transfer = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Transfer, donorSlot: 1);
            Assert.AreEqual(SmithResult.Success, transfer.Status); Assert.False(camp.CanAffordForge(transfer));
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.CommitForge(transfer)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void CraftedDecoderRejectsInvalidHistoryRatherThanLoadingCorruptedStats(int fault)
        {
            var db = Restrictions(); var camp = Ready(db); var source = Single(db, 100, 20, 1001);
            CraftStep step = new CraftStep(ForgeOperation.Replace, 0, 1002, Fix64.Half);
            if (fault == 0) step = new CraftStep((ForgeOperation)255, 0, 1002, Fix64.Half);
            if (fault == 1) step = new CraftStep(ForgeOperation.Replace, 6, 1002, Fix64.Half);
            if (fault == 2) step = new CraftStep(ForgeOperation.Replace, 0, 1002, Fix64.FromInt(2));
            if (fault == 3) { source = Single(db, 200, 20, 1002); step = new CraftStep(ForgeOperation.Replace, 0, 1003, Fix64.Half); }
            if (fault == 4) { source = Single(db, 100, 1, 1001); step = new CraftStep(ForgeOperation.Replace, 0, 1003, Fix64.Half); }
            if (fault == 5) step = new CraftStep(ForgeOperation.Add, 1, 1001, Fix64.Half);
            if (fault == 6) source = new ItemInstance(source.BaseId, source.ItemLevel, ItemRarity.Unique, source.Seed);
            camp.Bag.Put(0, new ItemInstance(source.BaseId, source.ItemLevel, source.Rarity, source.Seed, crafting: new CraftingRecipe(new[] { step })), false);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveCodec.Encode(camp), db));
        }
        [Test] public void CraftedDecoderRejectsExcessiveLengthBeforeAllocatingHistory()
        {
            var camp = Ready(); var bytes = CampSaveCodec.Encode(camp);
            int recipeCount = 16 + (int)CurrencyType.Count * 4 + 17;
            bytes[recipeCount] = 1; bytes[recipeCount + 1] = 4;
            uint hash = 2166136261; for (int i = 0; i < bytes.Length - 4; i++) { hash ^= bytes[i]; hash = unchecked(hash * 16777619); }
            Array.Copy(BitConverter.GetBytes(hash), 0, bytes, bytes.Length - 4, 4);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(bytes, camp.Items));
        }
    }
}
