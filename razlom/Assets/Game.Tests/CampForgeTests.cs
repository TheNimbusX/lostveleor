using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampForgeTests
    {
        static Camp Ready(ItemRarity rarity = ItemRarity.Rare, ulong seed = 123)
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.DeveloperSetLevel(10); for (int rank = 0; rank < 3; rank++) camp.TryUpgradeResident(CampResident.Smith);
            camp.Earn(CurrencyType.Gold, 10000); camp.Earn(CurrencyType.Shards, 10000);
            camp.EarnForgeMaterial(ForgeMaterial.Steel, 10); camp.EarnForgeMaterial(ForgeMaterial.Core, 10);
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 25, rarity, seed)); return camp;
        }
        [Test] public void ReplacementOffersAreFixedAcrossReopenSaveAndDoNotSpendUntilConfirmation()
        {
            var camp = Ready(); var before = CampSaveCodec.Encode(camp);
            var first = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace, 0);
            Assert.AreEqual(SmithResult.Success, first.Status); Assert.AreEqual(3, first.CandidateCount);
            var again = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace, 0);
            Assert.True(first.After.SameRecipe(again.After)); CollectionAssert.AreEqual(before, CampSaveCodec.Encode(camp));
            var loaded = CampSaveCodec.Decode(before, camp.Items); var replay = loaded.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace, 0);
            Assert.True(first.After.SameRecipe(replay.After)); Assert.AreEqual(SmithResult.Success, camp.CommitForge(first));
            Assert.AreEqual(9940, camp.Money(CurrencyType.Gold)); Assert.AreEqual(9992, camp.Money(CurrencyType.Shards)); Assert.AreEqual(9, camp.MaterialCount(ForgeMaterial.Steel));
            var old = new GeneratedItem(); var next = new GeneratedItem(); ItemGenerator.Generate(first.Before, camp.Items, old); ItemGenerator.Generate(first.After, camp.Items, next);
            Assert.AreEqual(old.AffixCount, next.AffixCount); for (int i = 1; i < old.AffixCount; i++) { Assert.AreEqual(old.GetAffix(i).AffixId, next.GetAffix(i).AffixId); Assert.AreEqual(old.GetAffix(i).Value, next.GetAffix(i).Value); }
            Assert.AreEqual(SmithResult.StalePreview, camp.CommitForge(first));
            var save = CampSaveCodec.Encode(camp); var back = CampSaveCodec.Decode(save, camp.Items); Assert.True(camp.Bag.At(0).SameRecipe(back.Bag.At(0)));
        }
        [Test] public void AddStopsAtRarityCapAndLevelAndGroupRestrictionsApply()
        {
            Camp camp = null; var generated = new GeneratedItem();
            for (ulong seed = 1; seed < 100; seed++) { camp = Ready(ItemRarity.Magic, seed); ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated); if (generated.AffixCount == 1) break; }
            var preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Add);
            Assert.AreEqual(SmithResult.Success, preview.Status); Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview));
            ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated); Assert.AreEqual(2, generated.AffixCount);
            Assert.AreEqual(SmithResult.NoSpace, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Add).Status);
            for (int i = 0; i < generated.AffixCount; i++) for (int j = i + 1; j < generated.AffixCount; j++)
                Assert.AreNotEqual(camp.Items.GetAffix(camp.Items.IndexOfAffix(generated.GetAffix(i).AffixId)).Group, camp.Items.GetAffix(camp.Items.IndexOfAffix(generated.GetAffix(j).AffixId)).Group);
            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.rusty_sword"), 1, ItemRarity.Magic, 17), false);
            preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace);
            for (int i = 0; i < preview.CandidateCount; i++) Assert.LessOrEqual(camp.Items.GetAffix(camp.Items.IndexOfAffix(preview.Candidate(i).AffixId)).MinItemLevel, 1);
            Assert.AreEqual(SmithResult.NoSpace, Ready(ItemRarity.Normal).PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Add).Status);
        }
        [Test] public void TransferConsumesOnlyUnprotectedDonorAtFinalCommitAndSupportsWornTarget()
        {
            var camp = Ready(); camp.EquipFromBag(0); ForgePreview preview = null;
            for (ulong seed = 1; seed < 100; seed++)
            {
                camp.Bag.Put(1, new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Rare, seed), false);
                for (int slot = 0; slot < 4; slot++) for (int donorAffix = 0; donorAffix < 4; donorAffix++)
                {
                    var candidate = camp.PreviewForge(ForgeTarget.Worn(EquipSlot.Weapon), ForgeOperation.Transfer, slot, donorSlot: 1, donorAffix: donorAffix);
                    if (candidate.Status == SmithResult.Success) preview = candidate;
                }
                if (preview != null) break;
            }
            Assert.NotNull(preview); var original = CampSaveCodec.Encode(camp);
            Assert.False(camp.Bag.IsEmpty(1)); CollectionAssert.AreEqual(original, CampSaveCodec.Encode(camp));
            camp.Bag.SetKeep(1, true); Assert.AreEqual(SmithResult.Protected, camp.CommitForge(preview)); Assert.True(camp.Worn.Worn(EquipSlot.Weapon).SameRecipe(preview.Before));
            camp.Bag.SetKeep(1, false); camp.Spend(CurrencyType.Shards, 10000);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.CommitForge(preview)); Assert.False(camp.Bag.IsEmpty(1)); Assert.True(camp.Worn.Worn(EquipSlot.Weapon).SameRecipe(preview.Before));
            camp.Earn(CurrencyType.Shards, 20); Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview)); Assert.True(camp.Bag.IsEmpty(1));
            Assert.True(camp.Worn.Worn(EquipSlot.Weapon).SameRecipe(preview.After)); Assert.AreEqual(9, camp.MaterialCount(ForgeMaterial.Core));
        }
        [Test] public void ChangedTargetsAndUniqueArtifactsCannotBeForgedOrCharged()
        {
            var camp = Ready(); var preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace);
            camp.Bag.Swap(0, 4); var save = CampSaveCodec.Encode(camp); Assert.AreEqual(SmithResult.StalePreview, camp.CommitForge(preview)); CollectionAssert.AreEqual(save, CampSaveCodec.Encode(camp));
            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Unique, 1), false);
            Assert.AreEqual(SmithResult.InvalidItem, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Refine).Status);
            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.memory_shard"), 25, ItemRarity.Rare, 1), false);
            Assert.AreEqual(SmithResult.InvalidItem, camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace).Status);
        }
        [Test] public void ProgressiveRanksUnlockOperationsInOrder()
        {
            var camp = PrototypeContent.NewCamp(); camp.DeveloperSetLevel(10);
            Assert.True(camp.ForgeOperationUnlocked(ForgeOperation.Refine)); Assert.False(camp.ForgeOperationUnlocked(ForgeOperation.Replace));
            camp.TryUpgradeResident(CampResident.Smith); Assert.True(camp.ForgeOperationUnlocked(ForgeOperation.Replace)); Assert.False(camp.ForgeOperationUnlocked(ForgeOperation.Add));
            camp.TryUpgradeResident(CampResident.Smith); Assert.True(camp.ForgeOperationUnlocked(ForgeOperation.Add)); Assert.False(camp.ForgeOperationUnlocked(ForgeOperation.Transfer));
            camp.TryUpgradeResident(CampResident.Smith); Assert.True(camp.ForgeOperationUnlocked(ForgeOperation.Transfer));
        }
        [Test] public void CraftingRecipeRebalancesRangesWithoutFreezingOldComputedStats()
        {
            var camp = Ready(); var preview = camp.PreviewForge(ForgeTarget.Bag(0), ForgeOperation.Replace);
            Assert.AreEqual(SmithResult.Success, camp.CommitForge(preview));
            var generated = new GeneratedItem(); ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated);
            int selectedId = generated.GetAffix(0).AffixId; var value = generated.GetAffix(0).Value;
            var bases = new ItemBaseDefinition[camp.Items.BaseCount]; var affixes = new AffixDefinition[camp.Items.AffixCount];
            for (int i = 0; i < bases.Length; i++) bases[i] = camp.Items.GetBase(i);
            for (int i = 0; i < affixes.Length; i++)
            {
                var definition = camp.Items.GetAffix(i);
                affixes[i] = definition.Id != selectedId ? definition : new AffixDefinition(definition.Id, definition.Group, definition.Stat, definition.Op,
                    definition.MinValue * Fix64.FromInt(2), definition.MaxValue * Fix64.FromInt(2), definition.MinItemLevel, definition.Weight, definition.AllowedCategories);
            }
            var balanced = new ItemDatabase(bases, affixes); var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), balanced);
            Assert.True(ItemGenerator.Generate(restored.Bag.At(0), balanced, generated));
            Assert.AreEqual(selectedId, generated.GetAffix(0).AffixId); Assert.Greater(generated.GetAffix(0).Value.Raw, value.Raw);
            Assert.True(camp.Bag.At(0).SameRecipe(restored.Bag.At(0)));
        }
    }
}
