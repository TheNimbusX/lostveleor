using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>
    /// Первый набор экипировки (21 сентября): в каждом слоте две обычные и две
    /// редкие основы. Обычная находка не может оказаться редкой основой и наоборот,
    /// слот при этом не меняется, а потоки RNG расходуются как раньше.
    /// </summary>
    public class ItemTierTests
    {
        [Test] public void FindsKeepSlotAndTakeBaseOfTheirRarity()
        {
            var items=PrototypeContent.Items();
            foreach(int id in PrototypeContent.ItemBaseIds())
            {
                var category=Base(items,id).Category;
                for(ulong seed=1;seed<400;seed++)
                    foreach(var rarity in new[]{ItemRarity.Normal,ItemRarity.Magic,ItemRarity.Rare})
                    {
                        var item=items.MatchTier(new ItemInstance(id,5,rarity,seed*0x9E3779B97F4A7C15UL));
                        var definition=Base(items,item.BaseId);
                        Assert.AreEqual(category,definition.Category);
                        Assert.AreEqual(rarity,item.Rarity);
                        if(category!=ItemCategory.Artifact)Assert.AreEqual(rarity>=ItemRarity.Magic,definition.Rare);
                    }
            }
        }

        [Test] public void EverySlotHasTwoCommonAndTwoRareBasesAndAllCanDrop()
        {
            var items=PrototypeContent.Items();
            var seen=new System.Collections.Generic.HashSet<int>();
            foreach(int id in PrototypeContent.ItemBaseIds())
                for(ulong seed=1;seed<200;seed++)
                {
                    seen.Add(items.MatchTier(new ItemInstance(id,5,ItemRarity.Normal,seed<<32)).BaseId);
                    seen.Add(items.MatchTier(new ItemInstance(id,5,ItemRarity.Magic,seed<<32)).BaseId);
                }
            foreach(var category in new[]{ItemCategory.Weapon,ItemCategory.Armor,ItemCategory.Jewellery,ItemCategory.Talisman})
            {
                int common=0,rare=0;
                for(int i=0;i<items.BaseCount;i++)
                {
                    var definition=items.GetBase(i);if(definition.Category!=category)continue;
                    if(definition.Rare)rare++;else common++;
                    Assert.True(seen.Contains(definition.Id),"основа не выпадает: "+definition.Id);
                }
                Assert.AreEqual(2,common);Assert.AreEqual(2,rare);
            }
        }

        [Test] public void TraderShowsEachCommonBaseAndRarePositionsUseRareBases()
        {
            var camp=PrototypeContent.NewCamp();camp.Earn(CurrencyType.Gold,100000);
            Assert.AreEqual(8,camp.TraderStockCount);
            for(int generation=0;generation<100;generation++)
            {
                camp.RefreshTrader();
                for(int i=0;i<camp.TraderStockCount;i++)
                {
                    var item=camp.TraderStock(i);
                    Assert.AreEqual(item.Rarity==ItemRarity.Magic,Base(camp.Items,item.BaseId).Rare);
                }
            }
        }

        [Test] public void OldSaveWithSmallerStallRerollsInsteadOfFailing()
        {
            var camp=PrototypeContent.NewCamp();
            camp.RestoreTrader(3,false,new ItemInstance[4]);
            Assert.AreEqual(3,camp.TraderGeneration);
            Assert.AreEqual(8,camp.TraderStockCount);
            for(int i=0;i<camp.TraderStockCount;i++)Assert.False(camp.TraderStock(i).IsEmpty);
            var save=CampSaveCodec.Encode(camp);
            CollectionAssert.AreEqual(save,CampSaveCodec.Encode(CampSaveCodec.Decode(save,camp.Items)));
        }

        static ItemBaseDefinition Base(ItemDatabase items,int id)=>items.GetBase(items.IndexOfBase(id));
    }
}
