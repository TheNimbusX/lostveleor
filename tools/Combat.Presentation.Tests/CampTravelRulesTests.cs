using Game.Sim;
using Game.View;
using NUnit.Framework;

// Окно «Перед походом» у стола сборов (06.10, table-a в «Дыме и свете»): окно обязано честно показывать решения Sim —
// куда встаёт зелье по клику, какие зелья в ряду «Другие», когда активна «Отправиться», когда в «с собой» идут
// артефакты, какой ранг Лео подписан под закрытым рецептом. Проверки — на настоящем Camp: первый подход, после
// первого босса, Sandbox, нулевой запас.
public sealed class CampTravelRulesTests
{
    /// <summary>Sandbox стоит на ранге 3: все жители, рецепты и дары открыты.</summary>
    static Camp Sandbox() => new Camp(PrototypeContent.Items(), act: 3);

    /// <summary>Первый подход к столу: новый профиль после первого завершённого забега.</summary>
    static Camp FirstVisit()
    {
        var camp = PrototypeContent.NewCamp();
        camp.RecordRealAttemptEnded(1, 0);
        Assert.True(camp.HasTravelTable);
        return camp;
    }

    [Test]
    public void SkillRowStaysCenteredWhenFewerThanThreeOffers()
    {
        Assert.AreEqual(0f, CampTravelRules.SkillOffset(0, 1));
        Assert.AreEqual(-.5f, CampTravelRules.SkillOffset(0, 2));
        Assert.AreEqual(.5f, CampTravelRules.SkillOffset(1, 2));
        Assert.AreEqual(-1f, CampTravelRules.SkillOffset(0, 3));
        Assert.AreEqual(0f, CampTravelRules.SkillOffset(1, 3));
        Assert.AreEqual(1f, CampTravelRules.SkillOffset(2, 3));
    }

    [Test]
    public void FirstVisitHasNoCarryAndBlocksDepartureUntilChosen()
    {
        Camp camp = FirstVisit();
        Assert.AreEqual(CarryKind.None, camp.PreparedCarry.Kind, "первый подход — ячейка пуста");
        Assert.False(CampTravelRules.CanDepart(camp), "без «с собой» в путь не выйти");
        Assert.False(CampTravelRules.ArtifactsJoinCarry(camp));
        Assert.AreEqual(CampTravelRules.CarrySlots, camp.CarryOfferCount);
        for (int i = 0; i < camp.CarryOfferCount; i++) Assert.AreEqual(CarryKind.Gift, camp.CarryOfferAt(i).Kind, "до босса — только дары");
        Assert.AreEqual(CarryKind.None, CampTravelRules.ShownCarry(camp, -1).Kind, "большая ячейка пуста, пока ничего не выбрано");
        Assert.True(CampTravelRules.ShownCarry(camp, 2).SameAs(camp.CarryOfferAt(2)), "наведение подсматривает вариант");
        Assert.AreEqual(CarryKind.None, CampTravelRules.ShownCarry(camp, 3).Kind, "наведение вне вариантов — не подсматривает");

        Assert.True(camp.SelectCarry(camp.CarryOfferAt(1)));
        Assert.True(CampTravelRules.CanDepart(camp));
        Assert.True(CampTravelRules.ShownCarry(camp, -1).SameAs(camp.CarryOfferAt(1)));
    }

    /// <summary>Повтор приватного правила Sim: подпись колонки «с собой» должна говорить то же, что делают предложения.</summary>
    [Test]
    public void ArtifactsJoinCarryExactlyAfterTheFirstBoss()
    {
        Camp camp = FirstVisit();
        for (int i = 0; i < RunArtifacts.Count; i++) camp.OpenArtifact(RunArtifacts.At(i));
        for (int attempt = 0; attempt < 12; attempt++)
        {
            camp.RecordRealAttemptEnded(1, 0);
            Assert.False(CampTravelRules.ArtifactsJoinCarry(camp));
            for (int i = 0; i < camp.CarryOfferCount; i++) Assert.AreNotEqual(CarryKind.Artifact, camp.CarryOfferAt(i).Kind, "до босса артефакта нет");
        }

        camp.DeveloperCreditBoss(0);
        Assert.True(CampTravelRules.ArtifactsJoinCarry(camp));
        bool seen = false;
        for (int attempt = 0; attempt < 40 && !seen; attempt++)
        {
            camp.RecordRealAttemptEnded(1, 0);
            for (int i = 0; i < camp.CarryOfferCount; i++) seen |= camp.CarryOfferAt(i).Kind == CarryKind.Artifact;
        }
        Assert.True(seen, "после первого босса артефакты встречаются среди вариантов");
    }

    [Test]
    public void OtherPotionsListEveryKindButTheTwoChosenInOrder()
    {
        var into = new PotionKind[CampTravelRules.OtherPotionSlots];
        Assert.AreEqual(6, CampTravelRules.OtherPotions(PotionKind.SmallHealth, PotionKind.SmallLavidium, into));
        CollectionAssert.AreEqual(new[]
        {
            PotionKind.LargeHealth, PotionKind.LargeLavidium, PotionKind.LivingResin, PotionKind.LavidiumSurge, PotionKind.Mixed, PotionKind.Clear,
        }, into);
        Assert.AreEqual(6, CampTravelRules.OtherPotions(PotionKind.Clear, PotionKind.LargeHealth, into));
        CollectionAssert.AreEqual(new[]
        {
            PotionKind.SmallHealth, PotionKind.SmallLavidium, PotionKind.LargeLavidium, PotionKind.LivingResin, PotionKind.LavidiumSurge, PotionKind.Mixed,
        }, into);
        Assert.AreEqual(0, CampTravelRules.OtherPotions(PotionKind.SmallHealth, PotionKind.SmallLavidium, null));
    }

    [Test]
    public void ClickedPotionGoesToItsFamilySlotOrTheActiveOne()
    {
        Assert.AreEqual(0, CampTravelRules.PotionTarget(PotionKind.LargeHealth, 1), "здоровье — в ячейку здоровья, даже если активна вторая");
        Assert.AreEqual(0, CampTravelRules.PotionTarget(PotionKind.LivingResin, 1));
        Assert.AreEqual(1, CampTravelRules.PotionTarget(PotionKind.LavidiumSurge, 0), "концентрация — в свою ячейку");
        Assert.AreEqual(1, CampTravelRules.PotionTarget(PotionKind.Mixed, 1), "без семьи — в активную");
        Assert.AreEqual(0, CampTravelRules.PotionTarget(PotionKind.Clear, 0));
        Assert.AreEqual(0, CampTravelRules.PotionTarget(PotionKind.Clear, 7), "активная вне 0..1 — первая");

        // Любой клик в ряду «Другие» Sim принимает: открытый рецепт встаёт, выбранные остаются разными.
        var session = new GameSession(1701, Sandbox(), PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
        Camp camp = session.Camp;
        var others = new PotionKind[CampTravelRules.OtherPotionSlots];
        for (int active = 0; active < 2; active++)
            for (int pick = 0; pick < CampTravelRules.OtherPotionSlots; pick++)
            {
                CampTravelRules.OtherPotions(camp.SelectedPotion(0), camp.SelectedPotion(1), others);
                PotionKind kind = others[pick];
                int slot = CampTravelRules.PotionTarget(kind, active);
                Assert.True(session.SetPreparedPotion(slot, kind), kind + " в ячейку " + slot);
                Assert.AreEqual(kind, camp.SelectedPotion(slot));
                Assert.AreNotEqual(camp.SelectedPotion(0), camp.SelectedPotion(1));
            }
    }

    /// <summary>Подпись «Лео · ранг N» под закрытым рецептом совпадает с тем, когда Sim его открывает.</summary>
    [Test]
    public void LockedRecipeRankMatchesTheRankThatUnlocksIt()
    {
        for (int rank = 0; rank <= 3; rank++)
        {
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperSetLevel(rank == 0 ? 3 : 6 * rank);
            for (int boss = 0; boss < rank; boss++) camp.DeveloperCreditBoss(boss);
            Assert.True(camp.HasResident(CampResident.Alchemist), "Лео в лагере");
            Assert.AreEqual(rank, camp.Rank(CampResident.Alchemist));
            for (int k = 0; k < Camp.PotionKindCount; k++)
            {
                var kind = (PotionKind)k;
                Assert.AreEqual(CampTravelRules.PotionRank(kind) <= rank, camp.PotionUnlocked(kind), kind + " на ранге " + rank);
            }
        }
    }

    [Test]
    public void EmptyStockDoesNotBlockDeparture()
    {
        var camp = Sandbox();
        Assert.True(camp.SelectCarry(camp.CarryOfferAt(0)));
        Assert.AreEqual(0, camp.PotionCount(camp.SelectedPotion(0)), "Sandbox без запаса");
        // Окно подписывает «× 0 · купить у Лео», но уйти не мешает: зелье просто не пьётся (CanUsePotionSlot).
        Assert.True(CampTravelRules.CanDepart(camp));
    }

    [Test]
    public void DescriptionsFollowHoverThenChoice()
    {
        var camp = Sandbox();
        Assert.AreEqual(3, camp.SkillOfferCount);
        Assert.True(camp.SelectStarterSkill(camp.SkillOfferAt(2)));
        Assert.AreEqual(camp.SkillOfferAt(2), CampTravelRules.DescribedSkill(camp, -1), "без наведения — выбранное");
        Assert.AreEqual(camp.SkillOfferAt(0), CampTravelRules.DescribedSkill(camp, 0), "наведённое");
        Assert.AreEqual(camp.SkillOfferAt(2), CampTravelRules.DescribedSkill(camp, 5), "наведение вне ряда — выбранное");

        Assert.True(camp.SelectPotionForSlot(1, PotionKind.Clear));
        Assert.AreEqual(PotionKind.SmallHealth, CampTravelRules.DescribedPotion(camp, 0, -1));
        Assert.AreEqual(PotionKind.Clear, CampTravelRules.DescribedPotion(camp, 1, -1), "действие активной ячейки");
        Assert.AreEqual(PotionKind.LivingResin, CampTravelRules.DescribedPotion(camp, 1, (int)PotionKind.LivingResin), "наведённое зелье");
    }

    [Test]
    public void PotionArtFollowsTheHudBottles()
    {
        Assert.AreEqual("potion_health_small", CampTravelRules.PotionArtFile(PotionKind.SmallHealth));
        Assert.AreEqual("potion_health_large", CampTravelRules.PotionArtFile(PotionKind.LargeHealth));
        Assert.AreEqual("potion_lavidium_small", CampTravelRules.PotionArtFile(PotionKind.SmallLavidium));
        Assert.AreEqual("potion_lavidium_large", CampTravelRules.PotionArtFile(PotionKind.LargeLavidium));
        Assert.AreEqual("potion_health_large", CampTravelRules.PotionArtFile(PotionKind.LivingResin));
        Assert.AreEqual("potion_lavidium_large", CampTravelRules.PotionArtFile(PotionKind.LavidiumSurge));
        Assert.AreEqual("potion_lavidium_large", CampTravelRules.PotionArtFile(PotionKind.Mixed));
        Assert.AreEqual("potion_lavidium_large", CampTravelRules.PotionArtFile(PotionKind.Clear));
    }

    [Test]
    public void EveryGiftHasItsOwnIconKeyAndPlaceholder()
    {
        Assert.IsNull(CampTravelRules.GiftKey(CampGift.None));
        Assert.IsNull(CampTravelRules.GiftPlaceholder(CampGift.None));
        var keys = new System.Collections.Generic.HashSet<string>();
        var signs = new System.Collections.Generic.HashSet<string>();
        for (int gift = 1; gift <= (int)CampGift.SpareFlask; gift++)
        {
            Assert.IsNotNull(CampTravelRules.GiftKey((CampGift)gift));
            Assert.True(keys.Add(CampTravelRules.GiftKey((CampGift)gift)), "ключ значка у каждого дара свой");
            Assert.True(signs.Add(CampTravelRules.GiftPlaceholder((CampGift)gift)), "временный знак у каждого дара свой");
        }
    }
}
