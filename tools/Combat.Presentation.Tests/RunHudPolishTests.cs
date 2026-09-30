using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Полировка экранов забега (выбор владельца 30.09, доска concepts-2026-09-30-hud-polish: 1a + подпись из 1b, 3a, 4):
// засечки и фазы полосы босса, монеты в строку добычи, блок ввода на экранах выбора, сравнение «было → станет»
// на карточках награды и итоги со статистикой. Здесь — чистая логика этих экранов без Unity.
public sealed class RunHudPolishTests
{
    // ---------------------------------------------------------------- полоса босса

    [Test]
    public void BossMarksSitAtAddsAndRageInPassOrder()
    {
        Assert.That(RunHudBossMarks.Count, Is.EqualTo(3));
        // По порядку прохождения: первая подмога, ярость, вторая подмога — пороги симуляции и RiftRun.
        Assert.That(RunHudBossMarks.PercentAt(0), Is.EqualTo(Simulation.BossAddFirstPercent));
        Assert.That(RunHudBossMarks.PercentAt(1), Is.EqualTo(50));
        Assert.That(RunHudBossMarks.PercentAt(2), Is.EqualTo(Simulation.BossAddSecondPercent));
        Assert.That(RunHudBossMarks.KindAt(0), Is.EqualTo(RunHudBossMarks.Kind.Adds));
        Assert.That(RunHudBossMarks.KindAt(1), Is.EqualTo(RunHudBossMarks.Kind.Rage));
        Assert.That(RunHudBossMarks.KindAt(2), Is.EqualTo(RunHudBossMarks.Kind.Adds));
        Assert.That(RunHudBossMarks.Fraction(0), Is.EqualTo(.66f).Within(1e-4f));
        Assert.That(RunHudBossMarks.Fraction(1), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(RunHudBossMarks.Fraction(2), Is.EqualTo(.33f).Within(1e-4f));
    }

    [Test]
    public void BossMarkLightsExactlyWhereTheSimulationCallsAdds()
    {
        // Simulation.UpdateBossAdds: подмога, когда здоровье·100 ≤ макс·процент.
        Assert.That(RunHudBossMarks.Passed(0, 3301, 5000), Is.False);
        Assert.That(RunHudBossMarks.Passed(0, 3300, 5000), Is.True);
        Assert.That(RunHudBossMarks.Passed(2, 1651, 5000), Is.False);
        Assert.That(RunHudBossMarks.Passed(2, 1650, 5000), Is.True);
        // Ярость RiftRun: Health ≤ MaxHealth / 2, в том числе при нечётном максимуме.
        Assert.That(RunHudBossMarks.Passed(1, 2500, 5001), Is.True);
        Assert.That(RunHudBossMarks.Passed(1, 2501, 5001), Is.False);
        Assert.That(RunHudBossMarks.Crossed(10, 0, 50), Is.False, "без максимума засечек нет");
    }

    [Test]
    public void BossPhaseCountsReinforcementsAndSubtitleReadsLikeTheConcept()
    {
        Assert.That(RunHudBossMarks.Phase(5000, 5000), Is.EqualTo(1));
        Assert.That(RunHudBossMarks.Phase(2900, 5000), Is.EqualTo(2), "кадр 1b: 2900 / 5000 — фаза 2");
        Assert.That(RunHudBossMarks.Phase(1200, 5000), Is.EqualTo(3));
        Assert.That(RunHudBossMarks.Subtitle(1, false), Is.EqualTo("Босс"));
        Assert.That(RunHudBossMarks.Subtitle(2, false), Is.EqualTo("Босс · фаза 2"));
        Assert.That(RunHudBossMarks.Subtitle(2, true), Is.EqualTo("Босс · фаза 2 · ярость"));
        Assert.That(RunHudBossMarks.Subtitle(3, true), Is.EqualTo("Босс · фаза 3 · ярость"));
    }

    [Test]
    public void BossIntroWritesTheNameFirstThenFillsTheBar()
    {
        Assert.That(RunHudBossMarks.IntroFill(.8f, 0f), Is.EqualTo(0f));
        Assert.That(RunHudBossMarks.IntroFill(.8f, RunHudBossMarks.IntroFillDelay), Is.EqualTo(0f));
        float previous = 0f;
        for (float t = RunHudBossMarks.IntroFillDelay; t <= 2f; t += .05f)
        {
            float fill = RunHudBossMarks.IntroFill(.8f, t);
            Assert.That(fill, Is.GreaterThanOrEqualTo(previous - 1e-6f), "полоса не откатывается назад");
            Assert.That(fill, Is.LessThanOrEqualTo(.8f + 1e-6f), "и не перелетает здоровье");
            previous = fill;
        }
        float done = RunHudBossMarks.IntroFillDelay + RunHudBossMarks.IntroFillDuration;
        Assert.That(RunHudBossMarks.IntroFill(.8f, done), Is.EqualTo(.8f));
        Assert.That(RunHudBossMarks.IntroDone(done), Is.True);
        Assert.That(RunHudBossMarks.IntroDone(done - .01f), Is.False);
    }

    // ---------------------------------------------------------------- монеты

    [Test]
    public void CoinsAreThreeToSixAndNeverMoreThanThePoolHasFree()
    {
        Assert.That(RunHudCoins.CountFor(0), Is.EqualTo(0));
        Assert.That(RunHudCoins.CountFor(1), Is.EqualTo(3));
        Assert.That(RunHudCoins.CountFor(19), Is.EqualTo(3));
        Assert.That(RunHudCoins.CountFor(20), Is.EqualTo(4));
        Assert.That(RunHudCoins.CountFor(45), Is.EqualTo(5));
        Assert.That(RunHudCoins.CountFor(70), Is.EqualTo(6));
        Assert.That(RunHudCoins.CountFor(100000), Is.EqualTo(6));
        Assert.That(RunHudCoins.CountFor(70, 2), Is.EqualTo(2), "пул почти занят — летят сколько есть");
        Assert.That(RunHudCoins.CountFor(70, 0), Is.EqualTo(0), "пул занят — число досчитает само");
        Assert.That(RunHudCoins.Pool, Is.GreaterThanOrEqualTo(RunHudCoins.MaxPerDrop * 2), "две горсти подряд летят целиком");
    }

    [Test]
    public void CoinSharesAddUpToTheGoldExactly()
    {
        int[] amounts = { 1, 2, 5, 17, 40, 70, 101, 999 };
        foreach (int amount in amounts)
        {
            int count = RunHudCoins.CountFor(amount);
            int sum = 0;
            for (int i = 0; i < count; i++)
            {
                int share = RunHudCoins.Share(amount, count, i);
                Assert.That(share, Is.GreaterThanOrEqualTo(0));
                sum += share;
            }
            Assert.That(sum, Is.EqualTo(amount), "число досчитывает ровно до золота забега: " + amount);
        }
    }

    [Test]
    public void CoinsLaunchInTurnAndAllLandWithinASecondAndAQuarter()
    {
        float latest = 0f;
        for (int i = 0; i < RunHudCoins.MaxPerDrop; i++)
        {
            Assert.That(RunHudCoins.Launch(i), Is.EqualTo(i * RunHudCoins.Stagger).Within(1e-6f));
            Assert.That(RunHudCoins.Landing(i), Is.GreaterThan(RunHudCoins.Launch(i)));
            Assert.That(RunHudCoins.Progress(i, RunHudCoins.Launch(i) - .01f), Is.LessThan(0f), "до вылета монеты нет");
            Assert.That(RunHudCoins.Progress(i, RunHudCoins.Landing(i)), Is.EqualTo(1f));
            latest = System.Math.Max(latest, RunHudCoins.Landing(i));
        }
        Assert.That(latest, Is.LessThan(1.25f), "горсть не висит в воздухе дольше, чем длится бой вокруг");
    }

    [Test]
    public void CoinArcStartsAtTheSourceRisesAboveTheChordAndEndsInTheCoin()
    {
        const float fromX = 900f, fromY = -600f, toX = 60f, toY = -150f;
        for (int i = 0; i < RunHudCoins.MaxPerDrop; i++)
        {
            RunHudCoins.Arc(fromX, fromY, toX, toY, i, 0f, out float sx, out float sy);
            float burst = (float)System.Math.Sqrt((sx - fromX) * (sx - fromX) + (sy - fromY) * (sy - fromY));
            Assert.That(burst, Is.LessThanOrEqualTo(RunHudCoins.Burst + 1e-3f), "вылет — из точки источника, с разлётом горсти");
            Assert.That(sy, Is.GreaterThanOrEqualTo(fromY - 1e-3f), "горсть разлетается вверх, не в землю");

            RunHudCoins.Arc(fromX, fromY, toX, toY, i, 1f, out float ex, out float ey);
            Assert.That(ex, Is.EqualTo(toX));
            Assert.That(ey, Is.EqualTo(toY));

            RunHudCoins.Arc(fromX, fromY, toX, toY, i, .5f, out float mx, out float my);
            float chordY = (sy + toY) * .5f;
            Assert.That(my, Is.GreaterThan(chordY), "мягкая дуга: середина пути над хордой");
        }
        Assert.That(RunHudCoins.Scale(-1f), Is.EqualTo(0f));
        Assert.That(RunHudCoins.Scale(.5f), Is.EqualTo(1f));
        Assert.That(RunHudCoins.Alpha(1f), Is.EqualTo(0f).Within(1e-5f), "монету съедает знак золота");
    }

    // ---------------------------------------------------------------- блок ввода

    [Test]
    public void ChoiceLockHoldsForItsDurationAndReleasesOnce()
    {
        var gate = new RunHudChoiceLock();
        Assert.That(gate.Locked(0f), Is.False, "до открытия экрана блока нет");
        Assert.That(gate.Progress(0f), Is.EqualTo(1f));

        gate.Start(10f);
        Assert.That(RunHudChoiceLock.DefaultDuration, Is.InRange(1f, 2f), "владелец: 1–2 с");
        Assert.That(gate.Locked(10f), Is.True);
        Assert.That(gate.Locked(10f + RunHudChoiceLock.DefaultDuration - .01f), Is.True);
        Assert.That(gate.Progress(10f + RunHudChoiceLock.DefaultDuration * .5f), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(gate.TakeRelease(10.5f), Is.False, "кейкапы не загораются, пока кольцо не дошло");

        float after = 10f + RunHudChoiceLock.DefaultDuration + 1e-3f;
        Assert.That(gate.Locked(after), Is.False);
        Assert.That(gate.Progress(after), Is.EqualTo(1f));
        Assert.That(gate.TakeRelease(after), Is.True, "кейкапы загораются");
        Assert.That(gate.TakeRelease(after + 1f), Is.False, "один раз");

        gate.Start(20f);
        gate.Clear();
        Assert.That(gate.Locked(20.1f), Is.False, "экран закрыт — блока нет");
        Assert.That(gate.TakeRelease(30f), Is.False, "закрытый экран не загорается");
    }

    // ---------------------------------------------------------------- сравнение

    [Test]
    public void CompareLineColoursTheNewValueAndPointsTheArrow()
    {
        string up = RunHudCompare.Line("Здоровье", "120", "150", 1, 1, "#8FE3A8", "#FF6A5A");
        Assert.That(up, Is.EqualTo("Здоровье  120 → <color=#8FE3A8>150 ▲</color>"));
        string worse = RunHudCompare.Line("Броня", "6", "4", -1, -1, "#8FE3A8", "#FF6A5A");
        Assert.That(worse, Is.EqualTo("Броня  6 → <color=#FF6A5A>4 ▼</color>"));
        // Перезарядка короче — число вниз, но это лучше: зелёным.
        string cooldown = RunHudCompare.Line("Перезарядка", "8 с", "6 с", -1, 1, "#8FE3A8", "#FF6A5A");
        Assert.That(cooldown, Is.EqualTo("Перезарядка  8 с → <color=#8FE3A8>6 с ▼</color>"));
        Assert.That(RunHudCompare.Line("Урон", "5", "5", 0, 0, "#0", "#1"), Is.EqualTo("Урон  5 → 5"));
        Assert.That(RunHudCompare.Worn("Потёртая куртка"), Is.EqualTo("Надето: Потёртая куртка"));
    }

    [Test]
    public void AbilityNumbersReadInMetresAndSeconds()
    {
        Assert.That(RunHudCompare.Number(3.5f), Is.EqualTo("3,5"));
        Assert.That(RunHudCompare.Number(8f), Is.EqualTo("8"));
        Assert.That(RunHudCompare.Number(.84f), Is.EqualTo("0,8"));
        Assert.That(RunHudCompare.AbilityStatValue(AbilityStatType.CooldownTicks, Fix64.FromInt(8 * Simulation.TicksPerSecond)), Is.EqualTo("8 с"));
        Assert.That(RunHudCompare.AbilityStatValue(AbilityStatType.StunTicks, Fix64.FromInt(Simulation.TicksPerSecond / 2)), Is.EqualTo("0,5 с"));
        Assert.That(RunHudCompare.AbilityStatValue(AbilityStatType.Radius, Fix64.Ratio(7, 2)), Is.EqualTo("3,5 м"));
        Assert.That(RunHudCompare.AbilityStatValue(AbilityStatType.LavidiumCost, Fix64.FromInt(28)), Is.EqualTo("28"));
        Assert.That(RunHudCompare.LowerIsBetter(AbilityStatType.CooldownTicks), Is.True);
        Assert.That(RunHudCompare.LowerIsBetter(AbilityStatType.Radius), Is.False);
        Assert.That(RunHudCompare.AbilityStatName(SabreTalentLine.Boarding, AbilityStatType.Radius), Is.EqualTo("Длина цепи"));
    }

    [Test]
    public void NumericTalentComparesTheAbilityBeforeAndAfter()
    {
        var loadout = new RunLoadout();
        int whirlwind = SabreTalents.PoolIndexOf(SabreTalentLine.Whirlwind);
        Assume.That(loadout.Owns(whirlwind), "стартовая способность — Вихрь");
        var before = new AbilityBuild();
        var after = new AbilityBuild();
        var buffer = new AbilityNode[16];

        // «Чаще»: перезарядка −25%.
        Assert.That(RunHudCompare.TalentRow(loadout, whirlwind, 1, before, after, buffer, out RunHudCompare.AbilityRow row), Is.True);
        Assert.That(row.Stat, Is.EqualTo(AbilityStatType.CooldownTicks));
        Assert.That(row.After, Is.LessThan(row.Before));
        Assert.That(row.Direction, Is.EqualTo(-1));
        Assert.That(row.Better, Is.EqualTo(1), "короче перезарядка — лучше");

        // «Шире круг» уже взят: «Чаще» считается поверх него, а сам «Шире круг» больше не прибавляет.
        loadout.TakeTalent(whirlwind, 0);
        Assert.That(RunHudCompare.TalentRow(loadout, whirlwind, 0, before, after, buffer, out RunHudCompare.AbilityRow radius), Is.True);
        Assert.That(radius.Stat, Is.EqualTo(AbilityStatType.Radius));
        Assert.That(radius.After, Is.GreaterThan(radius.Before));

        // Механическое усиление (флаг) числа не меняет — на карточке остаётся описание.
        Assert.That(RunHudCompare.TalentRow(loadout, whirlwind, 2, before, after, buffer, out _), Is.False);
    }

    [Test]
    public void ItemComparesAgainstTheWornItemOfTheSameSlot()
    {
        ItemDatabase items = PrototypeContent.Items();
        var sheet = new StatSheet();
        sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(100));
        var roll = new GeneratedItem();
        // Надета кожаная куртка: броня +8.
        var worn = new ItemInstance(StableId.Of("base.leather_jacket"), 3, ItemRarity.Normal, 11);
        Assert.That(ItemGenerator.Generate(in worn, items, roll), Is.True);
        roll.ApplyTo(sheet, (int)EquipSlot.Armor);

        var scratch = new StatSheet();
        var rows = new RunHudCompare.StatRow[4];
        var quilted = new ItemInstance(StableId.Of("base.quilted_jacket"), 3, ItemRarity.Normal, 12);
        int count = RunHudCompare.ItemRows(sheet, in quilted, items, roll, scratch, rows, out EquipSlot slot);

        Assert.That(slot, Is.EqualTo(EquipSlot.Armor));
        Assert.That(count, Is.EqualTo(2), "меняются только броня и здоровье");
        // Порядок — как в палатке: броня раньше здоровья.
        Assert.That(rows[0].Stat, Is.EqualTo(StatType.Armor));
        Assert.That(rows[0].Before, Is.EqualTo(Fix64.FromInt(8)));
        Assert.That(rows[0].After, Is.EqualTo(Fix64.Zero));
        Assert.That(rows[0].Direction, Is.EqualTo(-1));
        Assert.That(rows[1].Stat, Is.EqualTo(StatType.MaxHealth));
        Assert.That(rows[1].Before, Is.EqualTo(Fix64.FromInt(100)));
        Assert.That(rows[1].After, Is.EqualTo(Fix64.FromInt(115)));
        Assert.That(sheet.Get(StatType.Armor), Is.EqualTo(Fix64.FromInt(8)), "настоящий лист героя не тронут");

        // Та же вещь, что надета, — сравнивать нечего.
        Assert.That(RunHudCompare.ItemRows(sheet, in worn, items, roll, scratch, rows, out _), Is.EqualTo(0));
        // Незнакомая основа — сравнения нет вовсе (и слот неизвестен).
        var unknown = new ItemInstance(StableId.Of("base.no_such_thing"), 3, ItemRarity.Normal, 1);
        Assert.That(RunHudCompare.ItemRows(sheet, in unknown, items, roll, scratch, rows, out _), Is.EqualTo(-1));
    }

    [Test]
    public void StatBoostShowsTheHeroNumberNowAndAfter()
    {
        var sheet = new StatSheet();
        sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(270));
        var scratch = new StatSheet();
        Assert.That(RunHudCompare.StatBoostRow(sheet, StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(10), scratch, out RunHudCompare.StatRow row), Is.True);
        Assert.That(row.Before, Is.EqualTo(Fix64.FromInt(270)));
        Assert.That(row.After, Is.EqualTo(Fix64.FromInt(280)));
        Assert.That(sheet.ModifierCount, Is.EqualTo(0), "черновик, не лист героя");
    }

    // ---------------------------------------------------------------- итоги

    [Test]
    public void SummaryRowsFollowTheConceptColumns()
    {
        Assert.That(RunHudSummary.Rows, Is.EqualTo(new[]
        {
            RunHudSummary.Stat.Time, RunHudSummary.Stat.Arenas, RunHudSummary.Stat.Depth, RunHudSummary.Stat.Levels, RunHudSummary.Stat.DamageDealt,
            RunHudSummary.Stat.DamageTaken, RunHudSummary.Stat.BestHit, RunHudSummary.Stat.Crits, RunHudSummary.Stat.Potions,
        }));
        Assert.That(RunHudSummary.LeftColumn, Is.EqualTo(5));
        var captions = new HashSet<string>();
        var icons = new HashSet<string>();
        foreach (RunHudSummary.Stat stat in RunHudSummary.Rows)
        {
            Assert.That(captions.Add(RunHudSummary.Caption(stat)), Is.True, "подписи не повторяются");
            Assert.That(icons.Add(RunHudSummary.Icon(stat)), Is.True, "значки не повторяются: " + stat);
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(RepoRoot.Path, "razlom", "Assets", "UI", "RunIcons", RunHudSummary.Icon(stat) + ".png")),
                Is.True, "значок из набора забега: " + RunHudSummary.Icon(stat));
        }
    }

    [Test]
    public void SummaryNumbersReadLikeTheConcept()
    {
        Assert.That(RunHudSummary.Clock(767), Is.EqualTo("12:47"));
        Assert.That(RunHudSummary.Clock(59), Is.EqualTo("0:59"));
        Assert.That(RunHudSummary.Clock(3725), Is.EqualTo("1:02:05"));
        Assert.That(RunHudSummary.Number(18420), Is.EqualTo("18 420"));
        Assert.That(RunHudSummary.Number(2960), Is.EqualTo("2 960"));
        Assert.That(RunHudSummary.Number(612), Is.EqualTo("612"));
        Assert.That(RunHudSummary.Number(1234567), Is.EqualTo("1 234 567"));
        Assert.That(RunHudSummary.Number(-64), Is.EqualTo("−64"));
        Assert.That(RunHudSummary.Format(RunHudSummary.Stat.Levels, 3), Is.EqualTo("+3"));
        Assert.That(RunHudSummary.Format(RunHudSummary.Stat.Levels, 0), Is.EqualTo("0"));
        Assert.That(RunHudSummary.Format(RunHudSummary.Stat.Time, 767), Is.EqualTo("12:47"));
        Assert.That(RunHudSummary.KillCount(38), Is.EqualTo("×38"));
    }

    [Test]
    public void SummaryCountsUpOneRowAfterAnother()
    {
        // До задержки — нули: цифры проявляются из дыма.
        Assert.That(RunHudSummary.CountK(RunHudSummary.CountDelay - .01f, 0), Is.EqualTo(0f));
        for (int order = 1; order < RunHudSummary.Rows.Length; order++)
        {
            float start = RunHudSummary.CountDelay + order * RunHudSummary.CountStagger;
            Assert.That(RunHudSummary.CountK(start - 1e-3f, order), Is.EqualTo(0f), "строка " + order + " ждёт своей очереди");
            Assert.That(RunHudSummary.CountK(start, order - 1), Is.GreaterThan(0f), "предыдущая уже считает");
        }
        Assert.That(RunHudSummary.Counted(18420, 0f), Is.EqualTo(0));
        Assert.That(RunHudSummary.Counted(18420, 1f), Is.EqualTo(18420));
        Assert.That(RunHudSummary.Counted(18420, .5f), Is.InRange(1L, 18419L));
        float end = RunHudSummary.CountDelay + (RunHudSummary.Rows.Length - 1) * RunHudSummary.CountStagger + RunHudSummary.CountDuration;
        Assert.That(RunHudSummary.CountDone(end, RunHudSummary.Rows.Length), Is.True);
        Assert.That(RunHudSummary.CountDone(end - .05f, RunHudSummary.Rows.Length), Is.False);
    }

    [Test]
    public void KillsAreOrderedByCountThenByFirstKill()
    {
        var sim = new Simulation(1234, 64);
        sim.SetupTestArena(0);
        const int hero = Simulation.PlayerId;
        var at = FixVec2.Zero;
        int Foe(EnemyKind kind)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(3 + sim.Entities.Count), Fix64.Zero), 100, Faction.Orvill);
            sim.Entities.Kind[id] = kind;
            return id;
        }
        var events = new List<SimEvent>();
        // Первым убит Камнекопыт (1), потом три Корнеполза, два Вендиго и один Плюй-плод.
        events.Add(SimEvent.Death(hero, Foe(EnemyKind.ForestStonehoof), at));
        for (int i = 0; i < 3; i++) events.Add(SimEvent.Death(hero, Foe(EnemyKind.ForestRootSwarm), at));
        for (int i = 0; i < 2; i++) events.Add(SimEvent.Death(hero, Foe(EnemyKind.ForestWendigo), at));
        events.Add(SimEvent.Death(hero, Foe(EnemyKind.ForestBud), at));
        var stats = new RunStats();
        stats.Record(events, sim, bossId: -1);

        var order = new EnemyKind[RunHudSummary.KillSlots];
        int count = RunHudSummary.KillOrder(stats, order);
        Assert.That(count, Is.EqualTo(4));
        Assert.That(order[0], Is.EqualTo(EnemyKind.ForestRootSwarm));
        Assert.That(order[1], Is.EqualTo(EnemyKind.ForestWendigo));
        // Равные по одному — в порядке первого убийства: Камнекопыт раньше Плюй-плода.
        Assert.That(order[2], Is.EqualTo(EnemyKind.ForestStonehoof));
        Assert.That(order[3], Is.EqualTo(EnemyKind.ForestBud));

        // Мест меньше, чем видов: остаются самые частые.
        var two = new EnemyKind[2];
        Assert.That(RunHudSummary.KillOrder(stats, two), Is.EqualTo(2));
        Assert.That(two, Is.EqualTo(new[] { EnemyKind.ForestRootSwarm, EnemyKind.ForestWendigo }));
        Assert.That(RunHudSummary.KillOrder(RunStats.Empty, order), Is.EqualTo(0));
    }

    [Test]
    public void EveryEnemyKindHasAPortraitFrame()
    {
        foreach (EnemyKind kind in System.Enum.GetValues(typeof(EnemyKind)))
        {
            float height = RunHudSummary.PortraitHeight(kind);
            Assert.That(height, Is.InRange(.5f, 4f), "кадр портрета по росту тела: " + kind);
        }
        Assert.That(RunHudSummary.PortraitHeight(EnemyKind.ForestGuardian), Is.GreaterThan(RunHudSummary.PortraitHeight(EnemyKind.ForestRootSwarm)));
    }

    [Test]
    public void LostAndKeptLinesTellWhatStaysInTheRiftAndWhatGoesToCamp()
    {
        var lines = new string[4];
        // Гибель: вещи и золото остались в Разломе, опыт — нет.
        var died = new RunSummary(RunOutcome.Died, 3, 2, 0, 0, goldKept: 0, itemsLeftBehind: 3, goldLeftBehind: 64);
        int lost = RunHudSummary.LostLines(in died, false, lines);
        Assert.That(lost, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("3 вещи в Разломе"));
        Assert.That(lines[1], Is.EqualTo("−64 золота"));
        int kept = RunHudSummary.KeptLines(in died, false, lines);
        Assert.That(kept, Is.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo("Опыт сохранён"));

        // Ушёл с добычей: вещи в сумке и золото, не влезшее — в «Потеряно».
        var left = new RunSummary(RunOutcome.Left, 2, 2, 5, 1, goldKept: 1250);
        kept = RunHudSummary.KeptLines(in left, false, lines);
        Assert.That(kept, Is.EqualTo(3));
        Assert.That(lines[1], Is.EqualTo("5 вещей в сумке лагеря"));
        Assert.That(lines[2], Is.EqualTo("+1 250 золота"));
        lost = RunHudSummary.LostLines(in left, false, lines);
        Assert.That(lost, Is.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo("Не влезло в сумку: 1"));

        var clean = new RunSummary(RunOutcome.Completed, 5, 5, 1, 0, goldKept: 10);
        Assert.That(RunHudSummary.LostLines(in clean, false, lines), Is.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo("Ничего"));

        Assert.That(RunHudSummary.KeptLines(in left, true, lines), Is.EqualTo(1));
        Assert.That(lines[0], Does.Contain("ничего"), "тестовый забег ничего не уносит");
        Assert.That(RunHudSummary.Plural(1), Is.EqualTo(0));
        Assert.That(RunHudSummary.Plural(3), Is.EqualTo(1));
        Assert.That(RunHudSummary.Plural(11), Is.EqualTo(2));
        Assert.That(RunHudSummary.Plural(21), Is.EqualTo(0));
    }

    [Test]
    public void FreezeCaptionNamesTheBlow()
    {
        Assert.That(RunHudSummary.FreezeCaption(RunOutcome.Died, "Лесной вендиго", 64, 3), Is.EqualTo("Последний удар · Лесной вендиго · 64"));
        Assert.That(RunHudSummary.FreezeCaption(RunOutcome.Completed, "Хранитель лугов", 612, 7), Is.EqualTo("Победный удар · Хранитель лугов · 612"));
        Assert.That(RunHudSummary.FreezeCaption(RunOutcome.Died, null, 0, 3), Is.EqualTo("Последний удар"));
        Assert.That(RunHudSummary.FreezeCaption(RunOutcome.Left, null, 0, 3), Is.EqualTo("Уход из Разлома · арена 3"));
    }
}
