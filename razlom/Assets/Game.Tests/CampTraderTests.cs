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
