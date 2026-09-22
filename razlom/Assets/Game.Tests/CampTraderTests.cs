using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>
    /// Торговец распоряжается деньгами игрока, поэтому проверяется не только
    /// удачная сделка, но и каждый отказ: молча списанное золото — худшее,
    /// что может сделать лавка.
    /// </summary>
    public class CampTraderTests
    {
        [Test] public void SoldStockAndRefreshSurviveSaveWithoutReroll()
        {
            var c=Rich();var first=c.TraderStock(0);c.BuyFromTrader(0);
            Assert.True(c.TraderStock(0).IsEmpty);Assert.AreEqual(0,c.BuyFromTrader(0));
            c=CampSaveCodec.Decode(CampSaveCodec.Encode(c),c.Items);Assert.True(c.TraderStock(0).IsEmpty);
            int gold=c.Money(CurrencyType.Gold);Assert.True(c.RefreshTrader());Assert.AreEqual(gold-50,c.Money(CurrencyType.Gold));
            Assert.AreNotEqual(first.Seed,c.TraderStock(0).Seed);
            var save=CampSaveCodec.Encode(c);c=CampSaveCodec.Decode(save,c.Items);CollectionAssert.AreEqual(save,CampSaveCodec.Encode(c));
        }
        [Test] public void FailedTransactionsDoNotChangeSaveAndKeptItemsCannotBeSold()
        {
            var c=Rich(0);var before=CampSaveCodec.Encode(c);
            Assert.False(c.RefreshTrader());Assert.AreEqual(0,c.BuyFromTrader(0));Assert.AreEqual(0,c.SellToTrader(-1));Assert.AreEqual(0,c.SellToTrader(48));
            CollectionAssert.AreEqual(before,CampSaveCodec.Encode(c));
            c.Bag.Put(0,c.TraderStock(0),true);before=CampSaveCodec.Encode(c);Assert.AreEqual(0,c.SellToTrader(0));CollectionAssert.AreEqual(before,CampSaveCodec.Encode(c));
        }
        [Test] public void BossRefreshIsFreeAndOnlyIncreasesRareThreshold()
        {
            var paid=Rich(100000);var boss=Rich(100000);int rarePaid=0,rareBoss=0;
            for(int generation=0;generation<200;generation++)
            {
                paid.RefreshTrader();boss.RefreshTraderAfterBoss();
                for(int i=0;i<paid.TraderStockCount;i++)
                {
                    var a=paid.TraderStock(i);var b=boss.TraderStock(i);
                    Assert.AreEqual(a.Seed,b.Seed);Assert.LessOrEqual(a.Rarity,ItemRarity.Magic);Assert.LessOrEqual(b.Rarity,ItemRarity.Magic);
                    Assert.AreNotEqual(ItemCategory.Artifact,paid.Items.GetBase(paid.Items.IndexOfBase(a.BaseId)).Category);
                    if(a.Rarity==ItemRarity.Magic){rarePaid++;Assert.AreEqual(ItemRarity.Magic,b.Rarity);}
                    if(b.Rarity==ItemRarity.Magic)rareBoss++;
                }
            }
            Assert.Greater(rareBoss,rarePaid);Assert.Greater(rarePaid,0);Assert.AreEqual(100000,boss.Money(CurrencyType.Gold));
            boss=CampSaveCodec.Decode(CampSaveCodec.Encode(boss),boss.Items);Assert.True(boss.TraderBossStock);boss.RefreshTrader();Assert.False(boss.TraderBossStock);
        }
        [Test] public void OrdinaryRunExitDoesNotRefreshShop()
        {
            var session=PrototypeContent.NewSession(123);session.EnterRift();
            session.Step(new InputFrame{Command=(byte)RunCommand.Leave});Assert.AreEqual(0,session.Camp.TraderGeneration);
        }
        static Camp Rich(int gold = 10000)
        {
            var c = PrototypeContent.NewCamp();
            c.Earn(CurrencyType.Gold, gold);
            return c;
        }

        [Test] public void BuyPutsItemInBagAndTakesGold()
        {
            var c = Rich(); int before = c.Money(CurrencyType.Gold);
            int price = c.BuyFromTrader(0);
            Assert.That(price, Is.GreaterThan(0));
            Assert.That(c.Bag.Used, Is.EqualTo(1));
            Assert.That(c.Money(CurrencyType.Gold), Is.EqualTo(before - price));
        }

        /// <summary>
        /// Покупка обязана стоить дороже продажи. Иначе цикл «купил — продал»
        /// печатает золото из воздуха, и вся экономика теряет смысл.
        /// </summary>
        [Test] public void BuyingCostsMoreThanSellingReturns()
        {
            var c = Rich();
            var item = c.TraderStock(0);
            Assert.That(c.BuyPriceOf(in item), Is.GreaterThan(Camp.PriceOf(in item)));
        }

        [Test] public void FullBagKeepsGoldAndBuysNothing()
        {
            var c = Rich();
            var filler = new ItemInstance(StableId.Of("base.rusty_sword"), 1, ItemRarity.Normal, 7);
            while (!c.Bag.IsFull) c.Bag.Add(in filler);
            int before = c.Money(CurrencyType.Gold);
            Assert.That(c.BuyFromTrader(0), Is.EqualTo(0));
            Assert.That(c.Money(CurrencyType.Gold), Is.EqualTo(before));
        }

    }
}
