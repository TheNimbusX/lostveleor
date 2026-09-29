using Game.Sim;
using Game.View;
using NUnit.Framework;
using Block = Game.View.CampShopDeals.Block;

// Кузнец и торговец на одной странице (ревью владельца 29.09, кадры «Кузнец А» и «Торговец А»):
// окно до нажатия говорит цену, выход, нехватку и запрет — эти правила и проверяются здесь, вместе
// с масштабом раскладки при 80–120% интерфейса и 16:10.
public sealed class CampShopDealsTests
{
    static Camp NewCamp(int gold = 0, int shards = 0)
    {
        var camp = new Camp(PrototypeContent.Items());
        if (gold > 0) camp.Earn(CurrencyType.Gold, gold);
        if (shards > 0) camp.Earn(CurrencyType.Shards, shards);
        return camp;
    }

    static ItemInstance MagicSword => new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 123);

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(4, 1)]
    [TestCase(5, 2)]
    [TestCase(11, 2)]
    [TestCase(12, 2)]
    [TestCase(14, 2)]
    [TestCase(21, 0)]
    [TestCase(22, 1)]
    [TestCase(25, 2)]
    [TestCase(111, 2)]
    [TestCase(0, 2)]
    public void RussianPluralForms(int count, int form) => Assert.That(CampShopDeals.PluralForm(count), Is.EqualTo(form));

    [TestCase(1920f, 1080f, 1f)]
    [TestCase(2400f, 1350f, 1f)]
    [TestCase(1600f, 900f, 1600f / 1920f)]
    [TestCase(1728f, 1080f, .9f)]
    [TestCase(1440f, 900f, .75f)]
    [TestCase(2560f, 1080f, 1f)]
    public void LayoutFitsTheCanvasAtEveryUiScale(float width, float height, float expected)
    {
        // Холст — в единицах после масштаба интерфейса: 80% — 2400×1350, 120% — 1600×900, 16:10 — 1728×1080.
        float scale = CampShopDeals.FitScale(width, height);
        Assert.That(scale, Is.EqualTo(expected).Within(1e-4f));
        Assert.That(1920f * scale, Is.LessThanOrEqualTo(width + 1e-3f));
        Assert.That(1080f * scale, Is.LessThanOrEqualTo(height + 1e-3f));
    }

    [Test]
    public void FitScaleSurvivesAnEmptyCanvas() => Assert.That(CampShopDeals.FitScale(0f, 0f), Is.EqualTo(1f));

    [Test]
    public void ReforgeShowsPriceAndLevelBeforeTheClick()
    {
        var camp = NewCamp(200, 20);
        int slot = camp.Bag.Add(MagicSword);
        var plan = CampShopDeals.PlanReforge(camp, slot, false, 0);
        Assert.That(plan.Block, Is.EqualTo(Block.None));
        Assert.That(plan.Gold, Is.EqualTo(Camp.ReforgeGold(camp.Bag.At(slot))));
        Assert.That(plan.Shards, Is.EqualTo(Camp.ReforgeShards(camp.Bag.At(slot))));
        Assert.That(plan.LevelFrom, Is.EqualTo(10));
        Assert.That(plan.LevelTo, Is.EqualTo(12), "уровень растёт на 1 + редкость (Camp.TryPayReforge)");
        Assert.That(plan.Upper, Is.GreaterThan(plan.Lower));

        Assert.That(camp.Reforge(slot, 0), Is.EqualTo(SmithResult.Success));
        Assert.That(camp.Bag.At(slot).ItemLevel, Is.EqualTo(plan.LevelTo), "предпросмотр совпадает с тем, что делает кузнец");
    }

    [Test]
    public void ReforgeNamesTheExactShortfall()
    {
        var camp = NewCamp(10, 1);
        int slot = camp.Bag.Add(MagicSword);
        var plan = CampShopDeals.PlanReforge(camp, slot, false, 0);
        Assert.That(plan.Block, Is.EqualTo(Block.Funds));
        Assert.That(plan.GoldShort, Is.EqualTo(plan.Gold - 10));
        Assert.That(plan.ShardsShort, Is.EqualTo(plan.Shards - 1));
        Assert.That(plan.ShowsCost, Is.True);
    }

    [Test]
    public void ThirdReforgeClosesTheButtonAndHidesThePrice()
    {
        var camp = NewCamp(1000, 100);
        int slot = camp.Bag.Add(MagicSword);
        for (int i = 0; i < CampShopDeals.ReforgeLimit; i++) Assert.That(camp.Reforge(slot, 0), Is.EqualTo(SmithResult.Success));
        var plan = CampShopDeals.PlanReforge(camp, slot, false, 0);
        Assert.That(plan.Block, Is.EqualTo(Block.Exhausted));
        Assert.That(plan.Used, Is.EqualTo(3));
        Assert.That(plan.ShowsCost, Is.False);
    }

    [Test]
    public void NormalItemHasNothingToReforgeButCanBeDismantled()
    {
        var camp = NewCamp(1000, 100);
        int slot = camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 4, ItemRarity.Normal, 7));
        Assert.That(CampShopDeals.PlanReforge(camp, slot, false, 0).Block, Is.EqualTo(Block.NoAffix));
        Assert.That(CampShopDeals.PlanDismantle(camp, slot, false).Allowed, Is.True);
    }

    [Test]
    public void WornItemReforgesButDoesNotDismantle()
    {
        var camp = NewCamp(200, 20);
        camp.Bag.Add(MagicSword);
        Assert.That(camp.EquipFromBag(0), Is.True);
        int weapon = (int)EquipSlot.Weapon;
        Assert.That(CampShopDeals.PlanReforge(camp, weapon, true, 0).Allowed, Is.True);
        var scrap = CampShopDeals.PlanDismantle(camp, weapon, true);
        Assert.That(scrap.Block, Is.EqualTo(Block.Worn));
        Assert.That(scrap.Allowed, Is.False);
    }

    [Test]
    public void DismantleShowsYieldAndRespectsKeep()
    {
        var camp = NewCamp();
        int slot = camp.Bag.Add(MagicSword);
        var plan = CampShopDeals.PlanDismantle(camp, slot, false);
        Assert.That(plan.Block, Is.EqualTo(Block.None));
        Assert.That(plan.Shards, Is.EqualTo(Inventory.ShardsFor(camp.Bag.At(slot))));
        camp.Bag.SetKeep(slot, true);
        Assert.That(CampShopDeals.PlanDismantle(camp, slot, false).Block, Is.EqualTo(Block.Protected));
        Assert.That(camp.Dismantle(slot, out _), Is.EqualTo(SmithResult.Protected), "запрет окна совпадает с запретом лагеря");
        Assert.That(CampShopDeals.PlanDismantle(camp, -1, false).Block, Is.EqualTo(Block.NoItem));
    }

    [Test]
    public void DefaultAffixIsReforgeable()
    {
        var camp = NewCamp(1000, 100);
        int slot = camp.Bag.Add(MagicSword);
        var roll = new GeneratedItem();
        Assert.That(ItemGenerator.Generate(camp.Bag.At(slot), camp.Items, roll), Is.True);
        int affix = CampShopDeals.DefaultAffix(camp, slot, false, roll.AffixCount);
        Assert.That(camp.ReforgeRange(slot, affix, out _, out _), Is.EqualTo(SmithResult.Success));
        Assert.That(CampShopDeals.DefaultAffix(camp, slot, false, 0), Is.EqualTo(0));
    }

    [Test]
    public void BuyShowsPriceGoldAfterAndSoldOut()
    {
        var camp = NewCamp(1000);
        var deal = CampShopDeals.PlanBuy(camp, 0);
        int price = camp.BuyPriceOf(camp.TraderStock(0));
        Assert.That(deal.Block, Is.EqualTo(Block.None));
        Assert.That(deal.Price, Is.EqualTo(price));
        Assert.That(deal.GoldAfter, Is.EqualTo(1000 - price));
        Assert.That(camp.BuyFromTrader(0), Is.EqualTo(price));
        Assert.That(camp.Money(CurrencyType.Gold), Is.EqualTo(deal.GoldAfter));
        Assert.That(CampShopDeals.PlanBuy(camp, 0).Block, Is.EqualTo(Block.SoldOut));
        Assert.That(CampShopDeals.PlanBuy(camp, camp.TraderStockCount).Block, Is.EqualTo(Block.NoItem));
    }

    [Test]
    public void BuyNamesMissingGoldAndFullBagFirst()
    {
        var camp = NewCamp(5);
        var deal = CampShopDeals.PlanBuy(camp, 0);
        Assert.That(deal.Block, Is.EqualTo(Block.Funds));
        Assert.That(deal.GoldShort, Is.EqualTo(deal.Price - 5));
        while (!camp.Bag.IsFull) camp.Bag.Add(MagicSword);
        Assert.That(CampShopDeals.PlanBuy(camp, 0).Block, Is.EqualTo(Block.BagFull), "как Camp.BuyFromTrader: место проверяется раньше денег");
    }

    [Test]
    public void SellShowsGainAndGuardsKeptAndWorn()
    {
        var camp = NewCamp(100);
        int slot = camp.Bag.Add(MagicSword);
        var deal = CampShopDeals.PlanSell(camp, slot, false);
        Assert.That(deal.Allowed, Is.True);
        Assert.That(deal.Price, Is.EqualTo(Camp.PriceOf(camp.Bag.At(slot))));
        Assert.That(deal.GoldAfter, Is.EqualTo(100 + deal.Price));
        camp.Bag.SetKeep(slot, true);
        Assert.That(CampShopDeals.PlanSell(camp, slot, false).Block, Is.EqualTo(Block.Protected));
        Assert.That(camp.SellToTrader(slot), Is.EqualTo(0), "запрет окна совпадает с запретом лагеря");
        camp.Bag.SetKeep(slot, false);
        Assert.That(camp.EquipFromBag(slot), Is.True);
        Assert.That(CampShopDeals.PlanSell(camp, (int)EquipSlot.Weapon, true).Block, Is.EqualTo(Block.Worn));
    }
}
