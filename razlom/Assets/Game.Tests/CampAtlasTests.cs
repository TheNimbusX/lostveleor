using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>
    /// Атлас находок (21 сентября): основа открывается, как только вещь легла в сумку,
    /// переживает сохранение, а старые сохранения открывают то, что уже есть у игрока.
    /// </summary>
    public class CampAtlasTests
    {
        static int Id(string key)=>StableId.Of(key);

        [Test] public void BaseOpensWhenItemReachesTheBagAndStaysAfterSale()
        {
            var camp=PrototypeContent.NewCamp();
            Assert.AreEqual(0,camp.DiscoveredCount);
            int slot=camp.Bag.Add(new ItemInstance(Id("base.officer_sabre"),5,ItemRarity.Magic,77));
            Assert.True(camp.Discovered(Id("base.officer_sabre")));
            Assert.False(camp.Discovered(Id("base.rusty_sword")));
            camp.Bag.Remove(slot);
            Assert.True(camp.Discovered(Id("base.officer_sabre")),"открытое остаётся открытым");
        }

        [Test] public void TraderPurchaseOpensTheBase()
        {
            var camp=PrototypeContent.NewCamp();camp.Earn(CurrencyType.Gold,100000);
            var item=camp.TraderStock(0);
            Assert.AreNotEqual(0,camp.BuyFromTrader(0));
            Assert.True(camp.Discovered(item.BaseId));
        }

        [Test] public void AtlasSurvivesSaveAndOrderIsStable()
        {
            var camp=PrototypeContent.NewCamp();
            camp.Bag.Add(new ItemInstance(Id("base.sea_knot"),3,ItemRarity.Magic,5));
            camp.Bag.Add(new ItemInstance(Id("base.rusty_sword"),3,ItemRarity.Normal,6));
            int worn=camp.Bag.Add(new ItemInstance(Id("base.quilted_jacket"),3,ItemRarity.Normal,7));
            camp.EquipFromBag(worn);
            camp.Bag.Remove(0);
            var save=CampSaveCodec.Encode(camp);
            var back=CampSaveCodec.Decode(save,camp.Items);
            Assert.AreEqual(3,back.DiscoveredCount);
            Assert.True(back.Discovered(Id("base.sea_knot")));
            for(int i=1;i<back.DiscoveredCount;i++)Assert.Less(back.DiscoveredAt(i-1),back.DiscoveredAt(i));
            CollectionAssert.AreEqual(save,CampSaveCodec.Encode(back));
        }

        [Test] public void UnknownBaseInSaveIsRejected()
        {
            var camp=PrototypeContent.NewCamp();
            camp.RestoreDiscovered(new int[0]);
            Assert.Throws<System.IO.InvalidDataException>(()=>camp.RestoreDiscovered(new[]{12345}));
        }
    }
}
