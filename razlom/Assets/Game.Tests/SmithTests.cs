using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class SmithTests
    {
        static Camp Ready()
        {
            var camp=new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold,1000);camp.Earn(CurrencyType.Shards,1000);
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),10,ItemRarity.Rare,123));return camp;
        }
        [Test] public void ThreeAttemptsImproveOnlyChosenAffixAndPersistThroughEquipment()
        {
            var camp=Ready();var before=new GeneratedItem();var after=new GeneratedItem();
            for(int attempt=0;attempt<3;attempt++)
            {
                ItemGenerator.Generate(camp.Bag.At(0),camp.Items,before);
                int selected=attempt%before.AffixCount;
                Assert.AreEqual(SmithResult.Success,camp.ReforgeRange(0,selected,out var lo,out var hi));
                Assert.AreEqual(SmithResult.Success,camp.Reforge(0,selected));
                ItemGenerator.Generate(camp.Bag.At(0),camp.Items,after);
                Assert.AreEqual(before.AffixCount,after.AffixCount);
                for(int i=0;i<after.AffixCount;i++)
                {
                    Assert.AreEqual(before.GetAffix(i).AffixId,after.GetAffix(i).AffixId);
                    if(i==selected){Assert.GreaterOrEqual(after.GetAffix(i).Value.Raw,lo.Raw);Assert.LessOrEqual(after.GetAffix(i).Value.Raw,hi.Raw);}
                    else Assert.AreEqual(before.GetAffix(i).Value.Raw,after.GetAffix(i).Value.Raw);
                }
                Assert.AreEqual(attempt+1,camp.Bag.At(0).ReforgeCount);
                Assert.AreEqual(10,camp.Bag.At(0).OriginalLevel);
            }
            Assert.AreEqual(820,camp.Money(CurrencyType.Gold));Assert.AreEqual(982,camp.Money(CurrencyType.Shards));
            Assert.AreEqual(SmithResult.Exhausted,camp.Reforge(0,0));
            camp.EquipFromBag(0);camp=CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items);
            camp.UnequipToBag(EquipSlot.Weapon);
            Assert.AreEqual(3,camp.Bag.At(0).ReforgeCount);Assert.AreEqual(SmithResult.Exhausted,camp.Reforge(0,0));
        }
        [Test] public void FailedPaymentIsAtomicAndSalvageCannotBeRepeated()
        {
            var camp=Ready();camp.Spend(CurrencyType.Shards,1000);var original=CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.InsufficientFunds,camp.Reforge(0,0));CollectionAssert.AreEqual(original,CampSaveCodec.Encode(camp));
            int expected=Inventory.ShardsFor(camp.Bag.At(0));
            Assert.AreEqual(SmithResult.Success,camp.Dismantle(0,out int shards));Assert.AreEqual(expected,shards);
            Assert.AreEqual(SmithResult.InvalidItem,camp.Dismantle(0,out _));Assert.AreEqual(expected,camp.Money(CurrencyType.Shards));
            Assert.AreEqual(SmithResult.InvalidItem,camp.Reforge(-1,0));Assert.AreEqual(SmithResult.InvalidItem,camp.Dismantle(48,out _));
        }
        [Test] public void KeptItemsAreNotDestroyedAndNormalsHaveNoReforge()
        {
            var camp=Ready();camp.Bag.Put(0,camp.Bag.At(0),true);
            Assert.AreEqual(SmithResult.Protected,camp.Dismantle(0,out _));Assert.False(camp.Bag.IsEmpty(0));
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),5,ItemRarity.Normal,11));
            Assert.AreEqual(SmithResult.NoAffix,camp.Reforge(1,0));
        }
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)] public void LegacySavesLoadWithoutForgeHistory(int version)
        {
            var camp=Ready();byte[] data;
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {
                writer.Write(0x43575254);writer.Write(version);writer.Write(1);writer.Write(48);
                for(int i=0;i<(int)CurrencyType.Count;i++)writer.Write(0);
                for(int i=0;i<48+(int)EquipSlot.Count;i++)
                {var item=i==0?camp.Bag.At(0):default;writer.Write(item.BaseId);writer.Write(item.ItemLevel);writer.Write((byte)item.Rarity);writer.Write(item.Seed);if(version>=4)writer.Write(item.ForgeRecipe);if(i<48)writer.Write(false);}
                if(version>=2){writer.Write(1);writer.Write(0);if(version==2)for(int i=0;i<4;i++)writer.Write(0);}
                writer.Flush();uint h=2166136261;foreach(byte b in stream.ToArray()){h^=b;h=unchecked(h*16777619);}writer.Write(h);writer.Flush();data=stream.ToArray();
            }
            var restored=CampSaveCodec.Decode(data,camp.Items);Assert.AreEqual(0,restored.Bag.At(0).ReforgeCount);Assert.AreEqual(camp.Bag.At(0).Seed,restored.Bag.At(0).Seed);
        }
    }
}
