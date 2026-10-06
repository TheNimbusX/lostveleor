using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Эни 06.10: переплавка (оплата → три кандидата → обязательный выбор), добавление
    /// с переливом, допуск по редкости и рангу лагеря, ребаланс справочника.
    /// </summary>
    public sealed class CampForgeTests
    {
        static Camp Ready(ItemRarity rarity = ItemRarity.Rare, ulong seed = 123, short level = 25)
        {
            // Sandbox стоит на ранге 3: все действия Эни открыты без боссов.
            var camp = new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 10000); camp.Earn(CurrencyType.Shards, 10000); camp.Earn(CurrencyType.Steel, 10);
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), level, rarity, seed)); return camp;
        }
        static int Group(Camp camp, int affixId) => camp.Items.GetAffix(camp.Items.IndexOfAffix(affixId)).Group;

        [Test] public void RemeltCandidatesSurfaceOnlyAfterPaymentAndSurviveSave()
        {
            var camp = Ready(); var bag = ForgeTarget.Bag(0); var before = CampSaveCodec.Encode(camp);
            var quote = camp.Quote(EniAction.Remelt, bag, 0);
            Assert.AreEqual(SmithResult.Success, quote.Status); Assert.True(quote.Affordable);
            Assert.AreEqual(120, quote.Gold); Assert.AreEqual(0, quote.Shards); Assert.AreEqual(1, quote.Steel);
            Assert.AreEqual(0, camp.SessionCandidateCount, "кандидатов до оплаты не видно");
            CollectionAssert.AreEqual(before, CampSaveCodec.Encode(camp), "цена ничего не меняет");

            var old = new GeneratedItem(); ItemGenerator.Generate(camp.Bag.At(0), camp.Items, old);
            Assert.AreEqual(SmithResult.Success, camp.BeginRemelt(bag, 0));
            Assert.AreEqual(9880, camp.Money(CurrencyType.Gold)); Assert.AreEqual(9, camp.Money(CurrencyType.Steel));
            Assert.AreEqual(ForgeSessionKind.Remelt, camp.Session.Kind); Assert.AreEqual(3, camp.SessionCandidateCount);
            for (int i = 0; i < 3; i++) Assert.AreNotEqual(Group(camp, old.GetAffix(0).AffixId), Group(camp, camp.SessionCandidate(i).AffixId), "старого свойства среди вариантов нет");

            // Окно закрыли — выбор ждёт; другие действия и продажа вещь не трогают.
            camp.SettleForgeSession(); Assert.True(camp.Session.IsOpen);
            Assert.AreEqual(SmithResult.SessionOpen, camp.Strike(bag, 1, out _));
            Assert.AreEqual(SmithResult.SessionOpen, camp.BeginAdd(bag, out _));
            Assert.AreEqual(SmithResult.SessionOpen, camp.Dismantle(0, out _));
            Assert.AreEqual(0, camp.SellToTrader(0)); Assert.False(camp.Bag.IsEmpty(0));

            var loaded = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.AreEqual(ForgeSessionKind.Remelt, loaded.Session.Kind); Assert.AreEqual(3, loaded.SessionCandidateCount);
            for (int i = 0; i < 3; i++)
            { Assert.AreEqual(camp.SessionCandidate(i).AffixId, loaded.SessionCandidate(i).AffixId); Assert.AreEqual(camp.SessionCandidate(i).Value, loaded.SessionCandidate(i).Value); }

            var chosen = loaded.SessionCandidate(1);
            Assert.AreEqual(SmithResult.Success, loaded.ChooseSessionCandidate(1)); Assert.False(loaded.Session.IsOpen);
            Assert.AreEqual(SmithResult.NoSession, loaded.ChooseSessionCandidate(0));
            var next = new GeneratedItem(); ItemGenerator.Generate(loaded.Bag.At(0), loaded.Items, next);
            Assert.AreEqual(chosen.AffixId, next.GetAffix(0).AffixId); Assert.AreEqual(old.AffixCount, next.AffixCount);
            for (int i = 1; i < old.AffixCount; i++) { Assert.AreEqual(old.GetAffix(i).AffixId, next.GetAffix(i).AffixId); Assert.AreEqual(old.GetAffix(i).Value, next.GetAffix(i).Value); }
            Assert.AreEqual(1, loaded.AttemptsUsed(loaded.Bag.At(0)));
            var back = CampSaveCodec.Decode(CampSaveCodec.Encode(loaded), loaded.Items); Assert.True(loaded.Bag.At(0).SameRecipe(back.Bag.At(0)));
        }

        static Camp MagicWithOneAffix(ulong from, out ulong seed)
        {
            var generated = new GeneratedItem();
            for (seed = from; seed < from + 1000; seed++)
            {
                var camp = Ready(ItemRarity.Magic, seed); ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated);
                if (generated.AffixCount == 1) return camp;
            }
            Assert.Fail("Нет редкой вещи с одним свойством"); return null;
        }

        [Test] public void AddWithinCapIsSafeOverflowRollsCrackAndHardCapRejects()
        {
            var generated = new GeneratedItem(); bool sawCrack = false, sawOverflow = false; ulong seed = 1;
            while (!(sawCrack && sawOverflow) && seed < 2000)
            {
                var camp = MagicWithOneAffix(seed, out seed); seed++; var bag = ForgeTarget.Bag(0);
                // В пределах лимита редкости (2) — безопасно: сессия выбора, без броска.
                var safe = camp.Quote(EniAction.Add, bag);
                Assert.AreEqual(SmithResult.Success, safe.Status); Assert.False(safe.Overflow); Assert.AreEqual(150, safe.Gold); Assert.AreEqual(2, safe.Steel);
                Assert.AreEqual(SmithResult.Success, camp.BeginAdd(bag, out bool cracked)); Assert.False(cracked);
                Assert.AreEqual(SmithResult.Success, camp.ChooseSessionCandidate(0));
                ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated); Assert.AreEqual(2, generated.AffixCount);
                Assert.AreNotEqual(Group(camp, generated.GetAffix(0).AffixId), Group(camp, generated.GetAffix(1).AffixId));

                // Перелив: +1 сверх лимита с трещиной 50%; цена выросла ×1,5 за прошлое действие.
                var overflow = camp.Quote(EniAction.Add, bag);
                Assert.AreEqual(SmithResult.Success, overflow.Status); Assert.True(overflow.Overflow); Assert.AreEqual(50, overflow.RiskPercent);
                Assert.AreEqual(225, overflow.Gold);
                Assert.AreEqual(SmithResult.Success, camp.BeginAdd(bag, out cracked));
                if (cracked)
                {
                    sawCrack = true; Assert.False(camp.Session.IsOpen, "трещина перелива — без выбора");
                    Assert.AreEqual(1, camp.CrackCount(camp.Bag.At(0))); Assert.AreEqual(2, camp.AttemptsUsed(camp.Bag.At(0)));
                    ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated); Assert.AreEqual(2, generated.AffixCount);
                    Assert.AreEqual(6, camp.Money(CurrencyType.Steel));
                    continue;
                }
                sawOverflow = true;
                Assert.AreEqual(SmithResult.Success, camp.ChooseSessionCandidate(0));
                ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated); Assert.AreEqual(3, generated.AffixCount);
                // Жёсткий предел: лимит + 1, дальше нельзя, даже с попыткой в запасе.
                Assert.AreEqual(1, camp.AttemptsLeft(camp.Bag.At(0)));
                Assert.AreEqual(SmithResult.NoSpace, camp.Quote(EniAction.Add, bag).Status);
                var back = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items); Assert.True(camp.Bag.At(0).SameRecipe(back.Bag.At(0)));
            }
            Assert.True(sawCrack && sawOverflow, "оба исхода перелива встречаются");

            // Переплавка низкой вещи берёт только допустимые по уровню свойства.
            var low = Ready(ItemRarity.Magic, 17, 1);
            if (low.BeginRemelt(ForgeTarget.Bag(0), 0) == SmithResult.Success)
                for (int i = 0; i < low.SessionCandidateCount; i++) Assert.LessOrEqual(low.Items.GetAffix(low.Items.IndexOfAffix(low.SessionCandidate(i).AffixId)).MinItemLevel, 1);
            Assert.AreEqual(SmithResult.InvalidItem, Ready(ItemRarity.Normal).Quote(EniAction.Add, ForgeTarget.Bag(0)).Status);
        }

        /// <summary>Уникальная вещь: закалка и сердце — да, переплавка и добавление — нет. Артефакт Эни не трогает.</summary>
        [Test] public void ChangedTargetsAndUniqueArtifactsCannotBeForgedOrCharged()
        {
            var camp = Ready(); camp.DeveloperAddHeart(0);
            camp.Bag.Swap(0, 4); var save = CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.InvalidItem, camp.Strike(ForgeTarget.Bag(0), 0, out _));
            Assert.AreEqual(SmithResult.InvalidItem, camp.BeginRemelt(ForgeTarget.Bag(0), 0));
            CollectionAssert.AreEqual(save, CampSaveCodec.Encode(camp));

            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Unique, 1), false);
            var unique = ForgeTarget.Bag(0);
            Assert.AreEqual(2, Camp.TemperAttempts(ItemRarity.Unique));
            Assert.AreEqual(SmithResult.Success, camp.Quote(EniAction.Temper, unique, SmithTests.Temperable(camp, unique)).Status);
            Assert.AreEqual(SmithResult.InvalidItem, camp.Quote(EniAction.Remelt, unique, 0).Status);
            Assert.AreEqual(SmithResult.InvalidItem, camp.Quote(EniAction.Add, unique).Status);
            Assert.AreEqual(SmithResult.Success, camp.InlayHeart(unique, RunBossKeys.ThicketMaster, HeartFacet.ThicketPollen));
            Assert.AreEqual(1, camp.Bag.At(0).Crafting.HeartCount);

            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.memory_shard"), 25, ItemRarity.Rare, 1), false);
            foreach (EniAction action in System.Enum.GetValues(typeof(EniAction)))
                Assert.AreEqual(SmithResult.InvalidItem, camp.Quote(action, ForgeTarget.Bag(0), 0, RunBossKeys.ThicketMaster).Status);
        }

        /// <summary>
        /// Действия Эни открывает ранг лагеря (06.10): закалка — сразу, переплавка и сердце —
        /// ранг 1, добавление — ранг 2, второе сердце в вещь — ранг 3.
        /// </summary>
        [Test] public void EniActionsFollowCampRank()
        {
            var camp = PrototypeContent.NewCamp(); camp.DeveloperSetLevel(5);
            Assert.True(camp.EniActionUnlocked(EniAction.Temper));
            Assert.False(camp.EniActionUnlocked(EniAction.Remelt)); Assert.False(camp.EniActionUnlocked(EniAction.Heart));
            Assert.AreEqual(SmithResult.Locked, camp.Quote(EniAction.Remelt, ForgeTarget.Bag(0), 0).Status);
            camp.DeveloperCreditBoss(0); Assert.False(camp.EniActionUnlocked(EniAction.Remelt), "босс без уровня ранга не даёт");
            camp.DeveloperSetLevel(6);
            Assert.True(camp.EniActionUnlocked(EniAction.Remelt)); Assert.True(camp.EniActionUnlocked(EniAction.Heart));
            Assert.False(camp.EniActionUnlocked(EniAction.Add)); Assert.AreEqual(1, camp.HeartSlots);
            camp.DeveloperSetLevel(18); Assert.False(camp.EniActionUnlocked(EniAction.Add), "уровень без босса ранга не даёт");
            camp.DeveloperCreditBoss(1); Assert.True(camp.EniActionUnlocked(EniAction.Add)); Assert.AreEqual(1, camp.HeartSlots);
            camp.DeveloperCreditBoss(2); Assert.AreEqual(3, camp.CampRank); Assert.AreEqual(2, camp.HeartSlots);
            Assert.False(camp.EniActionUnlocked((EniAction)9));
        }

        [Test] public void CraftingRecipeRebalancesRangesWithoutFreezingOldComputedStats()
        {
            var camp = Ready(); var bag = ForgeTarget.Bag(0); int selected = SmithTests.Temperable(camp, bag);
            Assert.AreEqual(SmithResult.Success, camp.Strike(bag, selected, out _)); Assert.AreEqual(SmithResult.Success, camp.TakeTemper());
            Assert.AreEqual(ForgeOperation.Temper, camp.Bag.At(0).Crafting.Step(0).Operation);
            var generated = new GeneratedItem(); ItemGenerator.Generate(camp.Bag.At(0), camp.Items, generated);
            int selectedId = generated.GetAffix(selected).AffixId; var value = generated.GetAffix(selected).Value;
            var bases = new ItemBaseDefinition[camp.Items.BaseCount]; var affixes = new AffixDefinition[camp.Items.AffixCount];
            for (int i = 0; i < bases.Length; i++) bases[i] = camp.Items.GetBase(i);
            for (int i = 0; i < affixes.Length; i++)
            {
                var definition = camp.Items.GetAffix(i);
                affixes[i] = definition.Id != selectedId ? definition : new AffixDefinition(definition.Id, definition.Group, definition.Stat, definition.Op,
                    definition.MinValue * Fix64.FromInt(2), definition.MaxValue * Fix64.FromInt(2), definition.MinItemLevel, definition.Weight, definition.AllowedCategories);
            }
            var balanced = new ItemDatabase(bases, affixes); var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), balanced);
            Assert.True(ItemGenerator.Generate(restored.Bag.At(0), balanced, generated));
            Assert.AreEqual(selectedId, generated.GetAffix(selected).AffixId); Assert.Greater(generated.GetAffix(selected).Value.Raw, value.Raw);
            Assert.True(camp.Bag.At(0).SameRecipe(restored.Bag.At(0)));
        }
    }
}
