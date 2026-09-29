using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;
using Layout = Game.View.RunLootLedger.Layout;

// Строка добычи под панелью забега (выбор владельца 30.09, кадр b1-loot-strip-under-panel): что
// унесёшь — золото и вещи. Здесь проверяются правила строки: какие награды уносятся, порядок,
// окно ряда и «+K», место в сумке лагеря, счёт золота и то, что ряд не заходит в середину экрана
// (полоса босса, «Новый уровень») при масштабе интерфейса 80–120% и 16:10.
public sealed class RunLootLedgerTests
{
    static ItemInstance Item(string key, ItemRarity rarity = ItemRarity.Normal, short level = 5, ulong seed = 7) =>
        new ItemInstance(StableId.Of("base." + key), level, rarity, seed);

    static RewardOffer ItemOffer(string key, ItemRarity rarity = ItemRarity.Normal, short level = 5, ulong seed = 7) =>
        RewardOffer.OfItem(Item(key, rarity, level, seed));

    [Test]
    public void OnlyItemsLeaveTheRun()
    {
        // Ровно то, что GameSession.FinishRun кладёт в лагерь: вещи. Остальное живёт только в забеге.
        Assert.That(RunLootLedger.Carried(ItemOffer("leather_jacket")), Is.True);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfArtifact(RunArtifact.SunSeal)), Is.False);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfAbility(1)), Is.False);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfTalent(1, 0)), Is.False);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfSpring(30)), Is.False);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfStat(StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(10))), Is.False);
        Assert.That(RunLootLedger.Carried(RewardOffer.OfItem(default)), Is.False, "пустая вещь — не вещь");
    }

    [Test]
    public void TakeKeepsItemsInTakenOrderAndCountsEverything()
    {
        var ledger = new RunLootLedger();
        RewardOffer[] taken =
        {
            RewardOffer.OfAbility(2),
            ItemOffer("rusty_sword", ItemRarity.Normal, 3),
            RewardOffer.OfArtifact(RunArtifact.Hourglass),
            ItemOffer("lavidium_ring", ItemRarity.Rare, 9),
            RewardOffer.OfSpring(25),
            ItemOffer("scout_jacket", ItemRarity.Magic, 7),
        };
        int grown = 0;
        foreach (RewardOffer offer in taken) if (ledger.Take(offer)) grown++;

        Assert.That(ledger.Scanned, Is.EqualTo(taken.Length), "просмотрены все награды, не только вещи");
        Assert.That(grown, Is.EqualTo(3));
        Assert.That(ledger.Count, Is.EqualTo(3));
        // Слева направо — от старой к новой: новая вещь встаёт в конец ряда.
        Assert.That(ledger[0].BaseId, Is.EqualTo(StableId.Of("base.rusty_sword")));
        Assert.That(ledger[1].BaseId, Is.EqualTo(StableId.Of("base.lavidium_ring")));
        Assert.That(ledger[2].BaseId, Is.EqualTo(StableId.Of("base.scout_jacket")));
        Assert.That(ledger[1].Rarity, Is.EqualTo((int)ItemRarity.Rare));
        Assert.That(ledger[1].Level, Is.EqualTo(9));
        Assert.That(new[] { ledger[0].TakenIndex, ledger[1].TakenIndex, ledger[2].TakenIndex }, Is.EqualTo(new[] { 1, 3, 5 }));

        ledger.Reset();
        Assert.That(ledger.Count, Is.Zero);
        Assert.That(ledger.Scanned, Is.Zero);
    }

    [Test]
    public void LedgerHoldsAsManyItemsAsTheRunRemembers()
    {
        var ledger = new RunLootLedger();
        for (int i = 0; i < RunLootLedger.MaxEntries + 5; i++) ledger.Take(ItemOffer("copper_ring", seed: (ulong)(i + 1)));
        Assert.That(ledger.Count, Is.EqualTo(RunLootLedger.MaxEntries));
        Assert.That(ledger.Scanned, Is.EqualTo(RunLootLedger.MaxEntries + 5));
    }

    [TestCase(0, 6, 0, 0, 0)]
    [TestCase(4, 6, 0, 4, 0)]
    [TestCase(6, 6, 0, 6, 0)]
    // Не влезают — последние пять и «+2» на первом месте.
    [TestCase(7, 6, 2, 5, 2)]
    [TestCase(20, 6, 15, 5, 15)]
    // Меньше двух мест не бывает: один значок и «+K».
    [TestCase(5, 0, 4, 1, 4)]
    [TestCase(5, 1, 4, 1, 4)]
    [TestCase(2, 1, 0, 2, 0)]
    public void WindowShowsNewestAndFoldsOldestIntoMore(int count, int slots, int first, int shown, int hidden)
    {
        RunLootLedger.Window w = RunLootLedger.WindowFor(count, slots);
        Assert.That(w.First, Is.EqualTo(first));
        Assert.That(w.Shown, Is.EqualTo(shown));
        Assert.That(w.Hidden, Is.EqualTo(hidden));
        Assert.That(w.HasMore, Is.EqualTo(hidden > 0));
        Assert.That(w.Slots, Is.LessThanOrEqualTo(Math.Max(RunLootLedger.MinSlots, slots)), "ряд не шире мест");
        Assert.That(w.First + w.Shown, Is.EqualTo(count), "последняя вещь всегда видна");
        Assert.That(w.Hidden, Is.EqualTo(w.First), "в «+K» — ровно те, что левее ряда");
    }

    [Test]
    public void FitsFollowsTheOrderTheCampBagIsFilledIn()
    {
        // Сумка на 3 места, одно занято: FinishRun кладёт вещи по порядку, две влезут, остальные пропадут.
        var bag = new Inventory(3);
        bag.Add(Item("smith_ring", seed: 99));
        int free = bag.Free;
        Assert.That(free, Is.EqualTo(2));

        var ledger = new RunLootLedger();
        for (int i = 0; i < 4; i++) ledger.Take(ItemOffer("fang_cord", seed: (ulong)(i + 1)));
        for (int i = 0; i < ledger.Count; i++)
        {
            bool added = bag.Add(Item("fang_cord", seed: (ulong)(i + 1))) >= 0;
            Assert.That(RunLootLedger.Fits(i, free), Is.EqualTo(added), "вещь " + i);
        }
        Assert.That(ledger.Overflowing(free), Is.EqualTo(2));
        Assert.That(ledger.Overflowing(-1), Is.Zero, "сумки нет — всё считается влезшим");
        Assert.That(RunLootLedger.Fits(30, -1), Is.True);
        Assert.That(RunLootLedger.Fits(0, 0), Is.False);
    }

    // Холст RunHudWc: эталон 1920×1080 по высоте, делённый на масштаб интерфейса (UiScaleFollower).
    static float CanvasWidth(float scale, float aspect) => 1080f / scale * aspect;
    static float CanvasHeight(float scale) => 1080f / scale;

    // Строка — по левому краю панели состояния (x 24), верх — 4 ниже её кромки (−134): −138…−182.
    const float StripLeft = 24f, StripBottom = 138f + Layout.Height;
    // Подсказка под строкой: отступ 6, три строки текста и поля — не выше 110.
    const float TipDepth = 6f + 110f;
    // Боевой HUD (CombatHudWcBuilder): карта 253 в правом верхнем углу с полем 24; столбик всплывашек
    // над портретом — низ на 240 от низа экрана, четыре по 54, высота 44 и дым 22 сверху.
    const float MapLeftFromRight = 24f + 253f, ToastsTopFromBottom = 240f + 3f * 54f + 44f + 22f;
    // Полоса босса и «Новый уровень» — до 680 по центру.
    const float CentreContentHalf = 340f;

    [TestCase(.8f, 16f / 9f)]
    [TestCase(1f, 16f / 9f)]
    [TestCase(1.2f, 16f / 9f)]
    [TestCase(.8f, 16f / 10f)]
    [TestCase(1f, 16f / 10f)]
    [TestCase(1.2f, 16f / 10f)]
    public void StripStaysClearOfCentreMapAndToasts(float scale, float aspect)
    {
        float width = CanvasWidth(scale, aspect), height = CanvasHeight(scale);
        // Число золота — от трёх до шести цифр (Nunito 20 полужирный: ~11 единиц на цифру).
        foreach (float goldWidth in new[] { 0f, 34f, 66f })
        {
            float rowStart = StripLeft + Layout.RowStart(goldWidth);
            int places = RunLootLedger.Capacity(width, rowStart, Layout.PoolSlots);
            Assert.That(places, Is.InRange(RunLootLedger.MinSlots, Layout.PoolSlots));
            float lastRight = rowStart + (places - 1) * Layout.Pitch + Layout.Icon;
            // «+1» за последним кругом — тоже вне середины.
            Assert.That(lastRight + Layout.ArrivalReserve, Is.LessThanOrEqualTo(width * .5f - CentreContentHalf),
                "ряд заходит под полосу босса / «Новый уровень»: масштаб " + scale + ", ширина числа " + goldWidth);
            float stripRight = StripLeft + Layout.Width(goldWidth, places);
            Assert.That(stripRight, Is.LessThan(width - MapLeftFromRight), "строка доходит до карты");
        }
        Assert.That(StripBottom + TipDepth, Is.LessThan(height - ToastsTopFromBottom), "подсказка строки ложится на всплывашки");
    }

    [Test]
    public void TightestScreenStillShowsSeveralItems()
    {
        // 120% и 16:10 — самый тесный случай: хотя бы три круга и «+K».
        int places = RunLootLedger.Capacity(CanvasWidth(1.2f, 1.6f), StripLeft + Layout.RowStart(34f), Layout.PoolSlots);
        Assert.That(places, Is.GreaterThanOrEqualTo(4));
        // 100% и 16:9 — весь пул.
        Assert.That(RunLootLedger.Capacity(1920f, StripLeft + Layout.RowStart(34f), Layout.PoolSlots), Is.EqualTo(Layout.PoolSlots));
    }

    [Test]
    public void CapacityGrowsWithRoomAndNeverExceedsPool()
    {
        int last = 0;
        for (float width = 1000f; width <= 4000f; width += 50f)
        {
            int places = RunLootLedger.Capacity(width, 120f, 8);
            Assert.That(places, Is.GreaterThanOrEqualTo(last));
            Assert.That(places, Is.InRange(RunLootLedger.MinSlots, 8));
            last = places;
        }
        Assert.That(last, Is.EqualTo(8));
    }

    [Test]
    public void LayoutWidthCoversGoldDividerAndRow()
    {
        Assert.That(Layout.RowStart(0f), Is.EqualTo(Layout.RowStart(Layout.GoldMinWidth)), "ряд не прыгает на коротком числе");
        Assert.That(Layout.RowStart(60f), Is.GreaterThan(Layout.DividerX(60f)));
        Assert.That(Layout.DividerX(60f), Is.GreaterThan(Layout.GoldX + 60f));
        Assert.That(Layout.Width(40f, 3), Is.EqualTo(Layout.RowStart(40f) + 2 * Layout.Pitch + Layout.Icon + Layout.RightPad).Within(.001f));
        Assert.That(Layout.Width(40f, 0), Is.LessThan(Layout.RowStart(40f)), "без вещей — только золото");
    }

    [Test]
    public void GoldCountsUpSmoothlyAndSnapsOnNewRun()
    {
        var gold = new RunLootLedger.GoldCounter { Duration = .6f };
        gold.Snap(20);
        Assert.That(gold.Shown, Is.EqualTo(20));
        Assert.That(gold.Retarget(20), Is.False, "та же цель — не счёт");
        Assert.That(gold.Retarget(128), Is.True);
        Assert.That(gold.Counting, Is.True);

        int previous = gold.Shown;
        float elapsed = 0f;
        bool halfwayFast = false;
        while (gold.Counting)
        {
            int now = gold.Advance(1f / 60f);
            elapsed += 1f / 60f;
            Assert.That(now, Is.GreaterThanOrEqualTo(previous), "счёт только вверх");
            Assert.That(now, Is.InRange(20, 128));
            if (elapsed >= .3f && !halfwayFast) { halfwayFast = true; Assert.That(now, Is.GreaterThan(20 + (128 - 20) / 2), "быстро в начале"); }
            previous = now;
            Assert.That(elapsed, Is.LessThan(.7f), "досчитывает за Duration");
        }
        Assert.That(gold.Shown, Is.EqualTo(128));

        // Посреди счёта — новая цель: счёт продолжается от показанного, не с начала.
        gold.Retarget(200);
        gold.Advance(.1f);
        int mid = gold.Shown;
        Assert.That(gold.Retarget(260), Is.True);
        Assert.That(gold.Advance(0f), Is.EqualTo(mid));

        // Новый забег — меньше, чем было: сразу, без счёта вниз.
        Assert.That(gold.Retarget(0), Is.False);
        Assert.That(gold.Shown, Is.Zero);
        Assert.That(gold.Counting, Is.False);

        // Шаг больше длительности (склейка арены) — сразу до цели.
        gold.Retarget(50);
        Assert.That(gold.Advance(5f), Is.EqualTo(50));
    }
}
