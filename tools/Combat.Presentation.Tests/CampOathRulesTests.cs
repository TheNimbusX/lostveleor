using Game.Sim;
using Game.View;
using NUnit.Framework;

// Вкладки палатки «Клятвы» и «Атлас» (06.10, owner-review-0610): что окно показывает и что нажимает, без Unity.
// Доска должна совпадать с Sim — пороги слотов с Camp.OathSlots, порядок ряда с CreateRunBoons, кнопка карточки с тем, что
// Sim разрешит, — иначе игрок увидит «Поклясться» там, где покупка откажет, или слот, которого нет.
public sealed class CampOathRulesTests
{
    static Camp RichCamp(int ash = 100000)
    {
        Camp camp = PrototypeContent.NewCamp();
        if (ash > 0) camp.Earn(CurrencyType.Ash, ash);
        return camp;
    }

    [Test]
    public void GroupsSplitAllSeventeenOathsInBoardOrder()
    {
        int[] sizes = { 6, 4, 3, 4 };
        var seen = new bool[OathIds.Count + 1];
        for (int g = 0; g < 4; g++)
        {
            OathId[] members = CampOathRules.Members((OathGroup)g);
            Assert.AreEqual(sizes[g], members.Length, "группа " + g);
            for (int i = 0; i < members.Length; i++)
            {
                Assert.IsFalse(seen[(int)members[i]], members[i] + " в двух группах");
                seen[(int)members[i]] = true;
                Assert.AreEqual((OathGroup)g, CampOathRules.GroupOf(members[i]), members[i].ToString());
                if (i > 0) Assert.Less((int)members[i - 1], (int)members[i], "печати группы — по номерам");
            }
        }
        for (int id = 1; id <= OathIds.Count; id++) Assert.IsTrue(seen[id], (OathId)id + " без группы");
        Assert.AreEqual("Удача забега", CampOathRules.GroupRu(OathGroup.Fortune));
    }

    [Test]
    public void IconFileIsTheSaveKeyWithoutPrefix()
    {
        // Окончательные значки кладут в Resources/UI/OathIcons/<ключ без «oath.»>.png — имя обязано совпасть с ключом сохранения.
        for (int id = 1; id <= OathIds.Count; id++)
        {
            var oath = (OathId)id;
            Assert.AreEqual(OathIds.Key(oath), StableId.Of("oath." + CampOathRules.IconFile(oath)), oath.ToString());
            Assert.AreEqual(oath, CampOathRules.At(CampOathRules.IndexOf(oath)));
            Assert.IsNotEmpty(CampOathRules.PlaceholderIcon(oath));
            Assert.IsNotEmpty(CampOathRules.NameRu(oath));
            Assert.IsNotEmpty(CampOathRules.EffectRu(oath));
        }
        Assert.AreEqual(-1, CampOathRules.IndexOf(OathId.None));
        Assert.AreEqual(OathId.None, CampOathRules.At(OathIds.Count));
    }

    [Test]
    public void SlotThresholdsMatchCampSlots()
    {
        Camp camp = RichCamp(0);
        for (int level = 1; level <= 25; level++)
        {
            camp.DeveloperSetLevel(level);
            int open = 0;
            for (int s = 0; s < CampOathRules.SlotPlaces; s++) if (CampOathRules.SlotUnlockLevel(s) <= level) open++;
            Assert.AreEqual(camp.OathSlots, open, "ур. " + level);
        }
        Assert.AreEqual(6, CampOathRules.SlotUnlockLevel(3));
        Assert.AreEqual(12, CampOathRules.SlotUnlockLevel(4));
        Assert.AreEqual(18, CampOathRules.SlotUnlockLevel(5));
    }

    [Test]
    public void HeroOathGoesSwearStrengthenThenToggle()
    {
        Camp camp = RichCamp();
        OathId id = OathId.ToughHide;
        Assert.AreEqual(OathAction.Swear, CampOathRules.ActionOf(camp, id));
        Assert.AreEqual(OathLook.NotBought, CampOathRules.LookOf(camp, id));
        Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
        Assert.AreEqual(OathAction.Strengthen, CampOathRules.ActionOf(camp, id));
        Assert.AreEqual(OathLook.Active, CampOathRules.LookOf(camp, id), "первая ступень сама занимает свободный слот");
        Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
        Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
        Assert.AreEqual(OathAction.Disable, CampOathRules.ActionOf(camp, id));
        Assert.IsFalse(CampOathRules.IsPurchase(CampOathRules.ActionOf(camp, id)));
        Assert.AreEqual(OathResult.Success, camp.SetOathActive(id, false));
        Assert.AreEqual(OathAction.Enable, CampOathRules.ActionOf(camp, id));
        Assert.AreEqual(OathLook.Owned, CampOathRules.LookOf(camp, id));

        Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.Steadfast));
        Assert.AreEqual(OathAction.Disable, CampOathRules.ActionOf(camp, OathId.Steadfast), "у клятв в одну ступень после покупки — сразу «Выключить»");
    }

    [Test]
    public void WarningsBlockExactlyWhatSimRefuses()
    {
        Camp poor = RichCamp(Camp.OathBasePrice - 1);
        Assert.AreEqual(OathWarning.NotEnoughAsh, CampOathRules.WarningOf(poor, OathId.Favor));
        Assert.IsTrue(CampOathRules.Blocks(OathWarning.NotEnoughAsh));
        Assert.AreEqual(OathLook.Unaffordable, CampOathRules.LookOf(poor, OathId.Favor));
        Assert.AreEqual(OathResult.NotEnoughAsh, poor.BuyOath(OathId.Favor), "Sim отказывает там же, где кнопка притухла");

        Camp camp = RichCamp();
        camp.DeveloperSetLevel(1);
        foreach (OathId id in new[] { OathId.ToughHide, OathId.HeavyHand, OathId.Steadfast }) Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
        Assert.AreEqual(OathWarning.GoesToReserve, CampOathRules.WarningOf(camp, OathId.RingingCoin));
        Assert.IsFalse(CampOathRules.Blocks(OathWarning.GoesToReserve), "покупка в запас разрешена");
        Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.RingingCoin));
        Assert.IsFalse(camp.OathActive(OathId.RingingCoin));
        Assert.AreEqual(OathWarning.NoFreeSlot, CampOathRules.WarningOf(camp, OathId.RingingCoin));
        Assert.IsTrue(CampOathRules.Blocks(OathWarning.NoFreeSlot));
        Assert.AreEqual(OathResult.NoFreeSlot, camp.SetOathActive(OathId.RingingCoin, true));
        Assert.AreEqual(OathWarning.None, CampOathRules.WarningOf(camp, OathId.Steadfast), "выключить можно всегда");
        Assert.IsNotEmpty(CampOathRules.WarningRu(OathWarning.NotEnoughAsh, 80, 10));
        foreach (OathResult result in new[] { OathResult.InvalidOath, OathResult.MaxRank, OathResult.NotEnoughAsh, OathResult.NotOwned, OathResult.NoFreeSlot })
            Assert.IsNotEmpty(CampOathRules.ResultRu(result), result.ToString());
    }

    [Test]
    public void SlotRowFollowsRunSnapshotOrder()
    {
        Camp camp = RichCamp();
        camp.DeveloperSetLevel(6);
        OathId[] bought = { OathId.RuneSage, OathId.LightStep, OathId.EnemyBlood, OathId.ToughHide, OathId.SecondLook };
        foreach (OathId id in bought) Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
        var row = new OathId[CampOathRules.SlotPlaces];
        int taken = CampOathRules.SlotOrder(camp, row);
        Assert.AreEqual(4, taken, "ур. 6 — четыре слота, пятая клятва в запасе");
        RunBoons boons = camp.CreateRunBoons();
        for (int s = 0; s < taken; s++)
        {
            Assert.Greater(boons.Rank(row[s]), 0, "в ряду — то, что уйдёт в забег: " + row[s]);
            if (s > 0) Assert.Less((int)row[s - 1], (int)row[s]);
        }
        for (int s = taken; s < row.Length; s++) Assert.AreEqual(OathId.None, row[s]);
    }

    [Test]
    public void HeroProgressShowsNowAndNext()
    {
        StringAssert.Contains("+" + Simulation.ToughHideHealthPerRank, CampOathRules.ProgressRu(OathId.ToughHide, 0));
        StringAssert.Contains("+" + 2 * Simulation.ToughHideHealthPerRank, CampOathRules.ProgressRu(OathId.ToughHide, 1));
        StringAssert.Contains("последняя", CampOathRules.ProgressRu(OathId.ToughHide, 3));
        Assert.IsEmpty(CampOathRules.ProgressRu(OathId.Steadfast, 1), "у клятв в одну ступень строки «дальше» нет");
        StringAssert.Contains((RunBoons.AshTrailPercent - 100) + "%", CampOathRules.EffectRu(OathId.AshTrail));
    }

    [Test]
    public void OnlyInstinctAndFavorWaitForLootBlock()
    {
        for (int id = 1; id <= OathIds.Count; id++)
        {
            var oath = (OathId)id;
            Assert.AreEqual(oath == OathId.Instinct || oath == OathId.Favor, CampOathRules.WaitsForLootBlock(oath), oath.ToString());
        }
    }

    [Test]
    public void AtlasHasEightArtifactsAndFourActTwoPlaces()
    {
        Assert.AreEqual(12, CampOathRules.AtlasPlaces);
        for (int i = 0; i < CampOathRules.AtlasPlaces; i++)
            Assert.AreEqual(i >= RunArtifacts.Count, CampOathRules.AtlasPlaceIsFuture(i), "место " + i);

        Camp camp = RichCamp(0);
        Assert.IsFalse(CampOathRules.ArtifactsCarryUnlocked(camp), "до первого босса артефакты на стол не идут");
        camp.RecordBossDefeat(RunBossKeys.ThicketMaster);
        Assert.IsTrue(CampOathRules.ArtifactsCarryUnlocked(camp));
    }
}
