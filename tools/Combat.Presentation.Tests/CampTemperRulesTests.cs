using Game.Sim;
using Game.View;
using NUnit.Framework;

// Кузница Эни вкладками (06.10, temper-a.png): окно до нажатия говорит, что сделает основная кнопка, сколько это стоит,
// чего не хватает и чем закрыта вкладка. Цена и допуск — из Camp.Quote, поэтому план проверяется на настоящем лагере.
public sealed class CampTemperRulesTests
{
    static Camp NewCamp(int gold = 0, int shards = 0, int steel = 0, bool progressive = false)
    {
        var camp = new Camp(PrototypeContent.Items(), progressive: progressive);
        if (gold > 0) camp.Earn(CurrencyType.Gold, gold);
        if (shards > 0) camp.Earn(CurrencyType.Shards, shards);
        if (steel > 0) camp.Earn(CurrencyType.Steel, steel);
        return camp;
    }

    static ItemInstance MagicSword => new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 123);
    static ItemInstance NormalSword => new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Normal, 7);

    static int Property(Camp camp, int slot, bool worn = false)
    {
        var roll = new GeneratedItem();
        var item = worn ? camp.Worn.Worn((EquipSlot)slot) : camp.Bag.At(slot);
        ItemGenerator.Generate(item, camp.Items, roll);
        return CampShopDeals.DefaultAffix(camp, slot, worn, roll.AffixCount);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TabRanksMatchTheSimUnlocks(int rank)
    {
        // Подпись замка «Эни · ранг N» не должна врать: пороги окна = пороги Camp.EniActionUnlocked.
        var camp = NewCamp(progressive: true);
        camp.DeveloperSetLevel(18);
        for (int boss = 0; boss < rank; boss++) camp.DeveloperCreditBoss(boss);
        Assert.That(camp.CampRank, Is.EqualTo(rank));
        for (int tab = 0; tab < (int)EniTab.Dismantle; tab++)
        {
            bool expected = rank >= CampTemperRules.TabRank((EniTab)tab);
            Assert.That(camp.EniActionUnlocked((EniAction)tab), Is.EqualTo(expected), ((EniTab)tab).ToString());
            Assert.That(CampTemperRules.TabOpen(camp, (EniTab)tab), Is.EqualTo(expected), ((EniTab)tab).ToString());
        }
        Assert.That(CampTemperRules.TabOpen(camp, EniTab.Dismantle), Is.True);
        Assert.That(camp.HeartSlots, Is.EqualTo(rank >= CampTemperRules.SecondHeartRank ? 2 : 1));
    }

    [Test]
    public void TabRanksFollowTheDesign()
    {
        Assert.That(CampTemperRules.TabRank(EniTab.Temper), Is.EqualTo(0));
        Assert.That(CampTemperRules.TabRank(EniTab.Remelt), Is.EqualTo(1));
        Assert.That(CampTemperRules.TabRank(EniTab.Add), Is.EqualTo(2));
        Assert.That(CampTemperRules.TabRank(EniTab.Heart), Is.EqualTo(1));
        Assert.That(CampTemperRules.TabRank(EniTab.Dismantle), Is.EqualTo(0));
        Assert.That(CampTemperRules.TabCount, Is.EqualTo(5));
    }

    [TestCase(0, 0f)]
    [TestCase(15, .075f)]
    [TestCase(30, .15f)]
    [TestCase(50, .25f)]
    [TestCase(100, .5f)]
    [TestCase(-5, 0f)]
    [TestCase(150, .5f)]
    public void RiskArcIsTheUpperHalfOfTheCircle(int risk, float fill) =>
        Assert.That(CampTemperRules.RiskFill(risk), Is.EqualTo(fill).Within(1e-5f));

    [Test]
    public void RiskTurnsRedAtFifty()
    {
        Assert.That(CampTemperRules.RiskIsBad(30), Is.False);
        Assert.That(CampTemperRules.RiskIsBad(49), Is.False);
        Assert.That(CampTemperRules.RiskIsBad(50), Is.True);
    }

    [Test]
    public void PipsShowCracksFirstThenSpentThenFree()
    {
        // Эпическая (4 попытки): одна трещина, две попытки потрачены.
        Assert.That(CampTemperRules.Pip(0, 2, 4, 1), Is.EqualTo(TemperPip.Crack));
        Assert.That(CampTemperRules.Pip(1, 2, 4, 1), Is.EqualTo(TemperPip.Spent));
        Assert.That(CampTemperRules.Pip(2, 2, 4, 1), Is.EqualTo(TemperPip.Free));
        Assert.That(CampTemperRules.Pip(3, 2, 4, 1), Is.EqualTo(TemperPip.Free));
        // Обычная (2 попытки): третья и четвёртая отметки скрыты; сверх четырёх мест — тоже.
        Assert.That(CampTemperRules.Pip(2, 0, 2, 0), Is.EqualTo(TemperPip.Hidden));
        Assert.That(CampTemperRules.Pip(4, 0, 9, 0), Is.EqualTo(TemperPip.Hidden));
        Assert.That(CampTemperRules.Pip(-1, 0, 4, 0), Is.EqualTo(TemperPip.Hidden));
    }

    [Test]
    public void PipsStayCentredUnderTheItem()
    {
        Assert.That(CampTemperRules.PipX(0, 2, 800f, 56f), Is.EqualTo(772f).Within(1e-4f));
        Assert.That(CampTemperRules.PipX(1, 2, 800f, 56f), Is.EqualTo(828f).Within(1e-4f));
        Assert.That(CampTemperRules.PipX(0, 4, 800f, 56f), Is.EqualTo(716f).Within(1e-4f));
        Assert.That(CampTemperRules.PipX(3, 4, 800f, 56f), Is.EqualTo(884f).Within(1e-4f));
        Assert.That(CampTemperRules.PipX(1, 3, 800f, 56f), Is.EqualTo(800f).Within(1e-4f));
    }

    [Test]
    public void NoItemSleepsEveryButton()
    {
        var camp = NewCamp(2000, 200);
        foreach (EniTab tab in new[] { EniTab.Temper, EniTab.Remelt, EniTab.Add, EniTab.Heart, EniTab.Dismantle })
        {
            var plan = CampTemperRules.PlanTab(camp, tab, -1, false, 0, -1);
            Assert.That(plan.Step, Is.EqualTo(TemperStep.NoItem), tab.ToString());
            Assert.That(plan.CanAct, Is.False);
            Assert.That(plan.ShowsPrice, Is.False);
        }
    }

    [Test]
    public void FirstStrikeIsPaidAndSafeThenFreeWithGrowingRisk()
    {
        var camp = NewCamp(2000, 200);
        int slot = camp.Bag.Add(MagicSword);
        int property = Property(camp, slot);
        var first = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, property, -1);
        var quote = camp.Quote(EniAction.Temper, ForgeTarget.Bag(slot), property);
        Assert.That(first.Step, Is.EqualTo(TemperStep.Strike));
        Assert.That(first.RiskPercent, Is.EqualTo(0));
        Assert.That(first.Gold, Is.EqualTo(quote.Gold));
        Assert.That(first.Shards, Is.EqualTo(quote.Shards));
        Assert.That(first.CanAct && first.ShowsPrice && !first.CanTake && !first.NeedsConfirm, Is.True);
        Assert.That(first.AttemptLimit, Is.EqualTo(Camp.TemperAttempts(ItemRarity.Magic)));

        Assert.That(camp.Strike(ForgeTarget.Bag(slot), property, out var outcome), Is.EqualTo(SmithResult.Success));
        Assert.That(outcome, Is.EqualTo(StrikeOutcome.Grew));
        var next = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, property, -1);
        Assert.That(next.Step, Is.EqualTo(TemperStep.NextStrike));
        Assert.That(next.RiskPercent, Is.EqualTo(Camp.CrackPercent(1)));
        Assert.That(next.Strikes, Is.EqualTo(1));
        Assert.That(next.CanTake && next.CanAct, Is.True);
        Assert.That(next.ShowsPrice, Is.False, "следующий удар бесплатный");
        Assert.That(CampTemperRules.TemperOpenOn(camp, ForgeTarget.Bag(slot)), Is.True);

        // Другое свойство в открытой закалке — не продолжение: «Взять» там не предлагается.
        var other = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, property + 1, -1);
        Assert.That(other.CanTake, Is.False);

        Assert.That(camp.TakeTemper(), Is.EqualTo(SmithResult.Success));
        var again = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, property, -1);
        Assert.That(again.Step, Is.EqualTo(TemperStep.Strike));
        Assert.That(again.Gold, Is.EqualTo(Camp.Escalate(Camp.TemperGold, 1)), "оплаченное действие дорожает ×1,5");
        Assert.That(again.AttemptsUsed, Is.EqualTo(1));
    }

    [Test]
    public void ShortFundsAreNamedPerCurrency()
    {
        var camp = NewCamp(10, 0);
        int slot = camp.Bag.Add(MagicSword);
        var plan = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, Property(camp, slot), -1);
        Assert.That(plan.Step, Is.EqualTo(TemperStep.Strike));
        Assert.That(plan.CanAct, Is.False);
        Assert.That(plan.GoldShort, Is.EqualTo(Camp.TemperGold - 10));
        Assert.That(plan.ShardsShort, Is.EqualTo(Camp.TemperShards));
        Assert.That(plan.Short && plan.ShowsPrice, Is.True);
    }

    [Test]
    public void ClosedTabsShowTheirRankAndSleep()
    {
        var camp = NewCamp(2000, 200, 10, progressive: true);
        int slot = camp.Bag.Add(MagicSword);
        foreach (EniTab tab in new[] { EniTab.Remelt, EniTab.Add, EniTab.Heart })
        {
            var plan = CampTemperRules.PlanTab(camp, tab, slot, false, 0, 0);
            Assert.That(plan.Step, Is.EqualTo(TemperStep.Locked), tab.ToString());
            Assert.That(plan.Status, Is.EqualTo(SmithResult.Locked));
            Assert.That(plan.CanAct, Is.False);
        }
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, Property(camp, slot), -1).Step, Is.EqualTo(TemperStep.Strike));
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Dismantle, slot, false, 0, -1).Step, Is.EqualTo(TemperStep.Dismantle));
    }

    [Test]
    public void PaidRemeltLocksTheWholeForgeUntilAChoice()
    {
        var camp = NewCamp(2000, 200);
        int slot = camp.Bag.Add(MagicSword);
        int other = camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 456));
        var noSteel = CampTemperRules.PlanTab(camp, EniTab.Remelt, slot, false, 0, -1);
        Assert.That(noSteel.Step, Is.EqualTo(TemperStep.Pay));
        Assert.That(noSteel.SteelShort, Is.EqualTo(Camp.RemeltSteel));
        Assert.That(noSteel.CanAct, Is.False);

        camp.Earn(CurrencyType.Steel, 5);
        var pay = CampTemperRules.PlanTab(camp, EniTab.Remelt, slot, false, 0, -1);
        Assert.That(pay.CanAct && pay.ShowsPrice, Is.True);
        Assert.That(pay.Steel, Is.EqualTo(Camp.RemeltSteel));
        Assert.That(camp.BeginRemelt(ForgeTarget.Bag(slot), 0), Is.EqualTo(SmithResult.Success));

        Assert.That(CampTemperRules.ChoicePending(camp), Is.True);
        Assert.That(CampTemperRules.PendingTab(camp), Is.EqualTo(EniTab.Remelt));
        var choose = CampTemperRules.PlanTab(camp, EniTab.Remelt, slot, false, 0, -1);
        Assert.That(choose.Step, Is.EqualTo(TemperStep.Choose));
        Assert.That(choose.CanAct, Is.False, "выбор обязателен — без варианта кнопка спит");
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Remelt, slot, false, 0, 0).CanAct, Is.True);
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Remelt, slot, false, 0, 3).CanAct, Is.False);

        // Ждущий выбор запирает и другие вкладки, и другие вещи; разобрать вещь сессии нельзя.
        var temper = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, 0, -1);
        Assert.That(temper.Step, Is.EqualTo(TemperStep.Blocked));
        Assert.That(temper.Status, Is.EqualTo(SmithResult.SessionOpen));
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Remelt, other, false, 0, 0).Status, Is.EqualTo(SmithResult.SessionOpen));
        var scrap = CampTemperRules.PlanTab(camp, EniTab.Dismantle, slot, false, 0, -1);
        Assert.That(scrap.DismantleBlock, Is.EqualTo(CampShopDeals.Block.Session));
        Assert.That(scrap.CanAct, Is.False);

        // Окно открывается сразу на ждущей вещи и вкладке.
        Assert.That(CampTemperRules.DefaultTarget(camp, out bool worn), Is.EqualTo(slot));
        Assert.That(worn, Is.False);

        Assert.That(camp.ChooseSessionCandidate(0), Is.EqualTo(SmithResult.Success));
        Assert.That(CampTemperRules.ChoicePending(camp), Is.False);
    }

    [Test]
    public void RemeltAndAddRefuseNormalItems()
    {
        var camp = NewCamp(2000, 200, 10);
        int slot = camp.Bag.Add(NormalSword);
        foreach (EniTab tab in new[] { EniTab.Remelt, EniTab.Add })
        {
            var plan = CampTemperRules.PlanTab(camp, tab, slot, false, 0, -1);
            Assert.That(plan.Step, Is.EqualTo(TemperStep.Blocked), tab.ToString());
            Assert.That(plan.Status, Is.EqualTo(SmithResult.InvalidItem));
        }
    }

    [Test]
    public void HeartNeedsAHeartAndAFacet()
    {
        var camp = NewCamp(2000, 200);
        int slot = camp.Bag.Add(MagicSword);
        Assert.That(CampTemperRules.HeartBoss(camp), Is.EqualTo(RunBossKeys.ThicketMaster));
        var none = CampTemperRules.PlanTab(camp, EniTab.Heart, slot, false, 0, 0);
        Assert.That(none.Step, Is.EqualTo(TemperStep.Heart));
        Assert.That(none.HeartsShort, Is.EqualTo(1));
        Assert.That(none.CanAct, Is.False);
        Assert.That(none.RiskPercent, Is.EqualTo(0));

        camp.DeveloperAddHeart(0);
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Heart, slot, false, 0, -1).CanAct, Is.False, "без грани кнопка спит");
        var facet = CampTemperRules.PlanTab(camp, EniTab.Heart, slot, false, 0, 1);
        Assert.That(facet.CanAct && facet.ShowsPrice, Is.True);
        Assert.That(facet.Hearts, Is.EqualTo(1));
        Assert.That(facet.Gold, Is.EqualTo(Camp.HeartGold));
    }

    [Test]
    public void ExhaustedAttemptsTurnIntoAConfirmedRiskyStrike()
    {
        var camp = NewCamp(5000, 500);
        int slot = camp.Bag.Add(MagicSword);
        int property = Property(camp, slot);
        for (int i = 0; i < Camp.TemperAttempts(ItemRarity.Magic); i++)
        {
            Assert.That(camp.Strike(ForgeTarget.Bag(slot), property, out _), Is.EqualTo(SmithResult.Success), "попытка " + (i + 1));
            Assert.That(camp.TakeTemper(), Is.EqualTo(SmithResult.Success));
            property = Property(camp, slot);
        }
        var plan = CampTemperRules.PlanTab(camp, EniTab.Temper, slot, false, property, -1);
        Assert.That(plan.Step, Is.EqualTo(TemperStep.Risky));
        Assert.That(plan.NeedsConfirm, Is.True);
        Assert.That(plan.RiskPercent, Is.EqualTo(100 - Camp.RiskySuccessPercent));
        Assert.That(plan.Yield, Is.EqualTo(camp.SalvageShards(camp.Bag.At(slot))));
        Assert.That(plan.Yield, Is.GreaterThan(0));
        Assert.That(plan.CanAct && plan.ShowsPrice, Is.True);
        Assert.That(CampTemperRules.Pip(Camp.TemperAttempts(ItemRarity.Magic) - 1, plan.AttemptsUsed, plan.AttemptLimit, plan.Cracks),
            Is.Not.EqualTo(TemperPip.Free));
    }

    [Test]
    public void WornItemsTemperInPlaceButNeverDismantle()
    {
        var camp = NewCamp(2000, 200);
        camp.Bag.Add(MagicSword);
        Assert.That(camp.EquipFromBag(0), Is.True);
        Assert.That(CampTemperRules.DefaultTarget(camp, out bool worn), Is.EqualTo((int)EquipSlot.Weapon));
        Assert.That(worn, Is.True);
        int weapon = (int)EquipSlot.Weapon;
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Temper, weapon, true, Property(camp, weapon, true), -1).Step, Is.EqualTo(TemperStep.Strike));
        var scrap = CampTemperRules.PlanTab(camp, EniTab.Dismantle, weapon, true, 0, -1);
        Assert.That(scrap.Step, Is.EqualTo(TemperStep.Dismantle));
        Assert.That(scrap.DismantleBlock, Is.EqualTo(CampShopDeals.Block.Worn));
        Assert.That(scrap.CanAct, Is.False);
    }

    [Test]
    public void DismantleFromTheBagAsksFirstAndShowsTheYield()
    {
        var camp = NewCamp();
        int slot = camp.Bag.Add(MagicSword);
        var plan = CampTemperRules.PlanTab(camp, EniTab.Dismantle, slot, false, 0, -1);
        Assert.That(plan.CanAct && plan.NeedsConfirm, Is.True);
        Assert.That(plan.Yield, Is.EqualTo(camp.SalvageShards(camp.Bag.At(slot))));
        Assert.That(plan.ShowsPrice, Is.False, "у разбора строка «Выход», а не цена");
        camp.Bag.SetKeep(slot, true);
        Assert.That(CampTemperRules.PlanTab(camp, EniTab.Dismantle, slot, false, 0, -1).DismantleBlock, Is.EqualTo(CampShopDeals.Block.Protected));
    }

    [Test]
    public void DefaultTargetWithoutAnythingIsEmpty()
    {
        var camp = NewCamp();
        camp.Bag.Add(MagicSword);
        Assert.That(CampTemperRules.DefaultTarget(camp, out bool worn), Is.EqualTo(-1));
        Assert.That(worn, Is.False);
    }
}
