using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Закалка Эни «Ещё удар?» (решение 06.10, план §7 T1): первый удар без риска, рост и
    /// риск по ударам, трещины и раскол, попытки по редкости, рост цены, рискованный удар,
    /// сердца, сессия за вещью и детерминизм исходов.
    /// </summary>
    public sealed class EniTemperTests
    {
        static readonly int Sword = StableId.Of("base.rusty_sword");

        static Camp Rich(ItemRarity rarity, ulong seed, short level = 25)
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 100000); camp.Earn(CurrencyType.Shards, 100000); camp.Earn(CurrencyType.Steel, 100);
            camp.Bag.Add(new ItemInstance(Sword, level, rarity, seed));
            return camp;
        }

        /// <summary>Бьёт, пока растёт и есть куда; возвращает последний исход и число ударов.</summary>
        static StrikeOutcome Hammer(Camp camp, ForgeTarget target, int property, out int strikes)
        {
            strikes = 0;
            while (true)
            {
                var result = camp.Strike(target, property, out var outcome);
                if (result == SmithResult.AtMaximum) { camp.TakeTemper(); return StrikeOutcome.Grew; }
                Assert.AreEqual(SmithResult.Success, result);
                strikes++;
                if (outcome != StrikeOutcome.Grew) return outcome;
            }
        }

        static Fix64 Value(Camp camp, ItemInstance item, int property)
        {
            var roll = new GeneratedItem(); Assert.True(ItemGenerator.Generate(item, camp.Items, roll));
            return property < 0 ? roll.ImplicitValue : roll.GetAffix(property).Value;
        }

        [Test] public void FirstStrikeIsSafeAndGrowthRisesPerStrike()
        {
            Assert.AreEqual(Fix64.Ratio(10, 100), Camp.TemperGrowth(0)); Assert.AreEqual(Fix64.Ratio(15, 100), Camp.TemperGrowth(1));
            Assert.AreEqual(Fix64.Ratio(20, 100), Camp.TemperGrowth(2)); Assert.AreEqual(Fix64.Ratio(25, 100), Camp.TemperGrowth(3));
            Assert.AreEqual(Fix64.Ratio(25, 100), Camp.TemperGrowth(7));
            // Первый удар не трескается ни у одной вещи.
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var camp = Rich(ItemRarity.Rare, seed); var bag = ForgeTarget.Bag(0);
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, SmithTests.Temperable(camp, bag), out var outcome));
                Assert.AreEqual(StrikeOutcome.Grew, outcome);
            }
            // Базовое свойство обычной вещи: 10%, затем +15%, +20% от основы — пока удары удачны.
            bool checkedThree = false;
            for (ulong seed = 1; seed <= 200 && !checkedThree; seed++)
            {
                var camp = Rich(ItemRarity.Normal, seed); var bag = ForgeTarget.Bag(0);
                var baseline = Value(camp, camp.Bag.At(0), -1);
                for (int k = 0; k < 3; k++)
                {
                    var quote = camp.Quote(EniAction.Temper, bag, -1);
                    Assert.AreEqual(Camp.TemperGrowth(k), quote.NextGrowth); Assert.AreEqual(k, quote.Strikes);
                    Assert.AreEqual(k == 0 ? 80 : 0, quote.Gold, "следующий удар в сессии бесплатный");
                    Assert.AreEqual(SmithResult.Success, camp.Strike(bag, -1, out var outcome));
                    if (outcome != StrikeOutcome.Grew) break;
                    Assert.AreEqual((baseline + baseline * Camp.SessionGrowth(k + 1)).Raw, Value(camp, camp.Bag.At(0), -1).Raw);
                    if (k == 2) checkedThree = true;
                }
            }
            Assert.True(checkedThree);
            Assert.AreEqual(Camp.TemperGrowth(0) + Camp.TemperGrowth(1) + Camp.TemperGrowth(2), Camp.SessionGrowth(3)); Assert.AreEqual(Fix64.One, Camp.SessionGrowth(9));
        }

        [Test] public void CrackChanceIsZeroFifteenThirtyFiftyAndQuoteNeverRevealsOutcome()
        {
            Assert.AreEqual(0, Camp.CrackPercent(0)); Assert.AreEqual(15, Camp.CrackPercent(1));
            Assert.AreEqual(30, Camp.CrackPercent(2)); Assert.AreEqual(50, Camp.CrackPercent(3)); Assert.AreEqual(50, Camp.CrackPercent(6));
            // Две вещи после первого удара: у одной следующий удар треснет, у другой нет.
            // Цена и риск, которые видит игрок, у них одинаковы.
            EniQuote? cracks = null, holds = null;
            for (ulong seed = 1; seed <= 400 && (cracks == null || holds == null); seed++)
            {
                var camp = Rich(ItemRarity.Normal, seed); var bag = ForgeTarget.Bag(0);
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, -1, out _));
                var quote = camp.Quote(EniAction.Temper, bag, -1);
                Assert.AreEqual(15, quote.RiskPercent); Assert.AreEqual(1, quote.Strikes);
                var probe = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
                Assert.AreEqual(SmithResult.Success, probe.Strike(bag, -1, out var outcome));
                if (outcome == StrikeOutcome.Grew) holds = quote; else cracks = quote;
            }
            Assert.NotNull(cracks); Assert.NotNull(holds);
            var a = cracks.Value; var b = holds.Value;
            Assert.AreEqual(a.Status, b.Status); Assert.AreEqual(a.Gold, b.Gold); Assert.AreEqual(a.Shards, b.Shards);
            Assert.AreEqual(a.RiskPercent, b.RiskPercent); Assert.AreEqual(a.NextGrowth, b.NextGrowth); Assert.AreEqual(a.Strikes, b.Strikes);
            Assert.AreEqual(a.AttemptsLeft, b.AttemptsLeft); Assert.AreEqual(a.Cracks, b.Cracks); Assert.AreEqual(a.Affordable, b.Affordable);
        }

        [Test] public void CrackDiscardsSessionGrowthAndEatsAttempt()
        {
            for (ulong seed = 1; seed <= 400; seed++)
            {
                var camp = Rich(ItemRarity.Rare, seed); var bag = ForgeTarget.Bag(0); int property = SmithTests.Temperable(camp, bag);
                var before = camp.Bag.At(0); int gold = camp.Money(CurrencyType.Gold);
                var outcome = Hammer(camp, bag, property, out int strikes);
                if (outcome != StrikeOutcome.Cracked || strikes < 3) continue;
                // Треснул удар после двух удачных: рост обоих сгорел, попытка съедена.
                var after = camp.Bag.At(0);
                Assert.AreEqual(Value(camp, before, property).Raw, Value(camp, after, property).Raw);
                Assert.AreEqual(ForgeOperation.Crack, after.Crafting.Step(after.Crafting.Count - 1).Operation);
                Assert.AreEqual(1, after.Crafting.Count, "в рецепте только трещина");
                Assert.AreEqual(1, camp.CrackCount(after)); Assert.AreEqual(1, camp.AttemptsUsed(after)); Assert.AreEqual(3, camp.AttemptsLeft(after));
                Assert.False(camp.Session.IsOpen); Assert.AreEqual(gold - 80, camp.Money(CurrencyType.Gold), "оплата не возвращается");
                Assert.AreEqual(120, camp.Quote(EniAction.Temper, bag, property).Gold, "треснувшее действие тоже дорожит следующее");
                return;
            }
            Assert.Fail("Нет вещи с трещиной после двух удачных ударов");
        }

        [Test] public void ThreeCracksShatterWearableAndSalvageWithBonus()
        {
            for (ulong seed = 1; seed <= 800; seed++)
            {
                var camp = Rich(ItemRarity.Rare, seed, 10); var bag = ForgeTarget.Bag(0); int property = SmithTests.Temperable(camp, bag);
                // Одна закалка без трещины, затем три сессии подряд до трещины.
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, property, out _)); camp.TakeTemper();
                bool shattered = false; int cracks = 0;
                for (int session = 0; session < 3; session++)
                {
                    if (camp.Quote(EniAction.Temper, bag, property).Status != SmithResult.Success) break;
                    var outcome = Hammer(camp, bag, property, out _);
                    if (outcome == StrikeOutcome.Grew) break;
                    cracks++; shattered = outcome == StrikeOutcome.Shattered;
                }
                if (cracks < 3) continue;
                Assert.True(shattered, "третья трещина раскалывает");
                var item = camp.Bag.At(0);
                Assert.True(camp.IsShattered(item)); Assert.AreEqual(0, camp.AttemptsLeft(item));
                foreach (EniAction action in System.Enum.GetValues(typeof(EniAction)))
                    Assert.AreEqual(SmithResult.Shattered, camp.Quote(action, bag, property, RunBossKeys.ThicketMaster).Status);
                Assert.AreEqual(SmithResult.Shattered, camp.RiskyStrike(bag, out _, out _));
                // Расколотая носится...
                Assert.True(camp.EquipFromBag(0)); Assert.True(camp.Worn.Worn(EquipSlot.Weapon).SameRecipe(item));
                Assert.True(camp.UnequipToBag(EquipSlot.Weapon));
                // ...и разбирается с бонусом за оплаченное без трещины: 4 действия − 3 трещины = +5.
                int slot = 0; while (camp.Bag.IsEmpty(slot)) slot++;
                Assert.AreEqual(4, item.Crafting.PaidActions);
                Assert.AreEqual(8 + 10 / 5 + 5, Inventory.ShardsFor(item));
                int shards = camp.Money(CurrencyType.Shards);
                Assert.AreEqual(SmithResult.Success, camp.Dismantle(slot, out int got)); Assert.AreEqual(15, got);
                Assert.AreEqual(shards + 15, camp.Money(CurrencyType.Shards));
                return;
            }
            Assert.Fail("Нет вещи с тремя трещинами подряд");
        }

        [Test] public void AttemptsByRarityAreTwoThreeFourTwo()
        {
            Assert.AreEqual(2, Camp.TemperAttempts(ItemRarity.Normal)); Assert.AreEqual(3, Camp.TemperAttempts(ItemRarity.Magic));
            Assert.AreEqual(4, Camp.TemperAttempts(ItemRarity.Rare)); Assert.AreEqual(2, Camp.TemperAttempts(ItemRarity.Unique));
            var camp = Rich(ItemRarity.Normal, 5); var bag = ForgeTarget.Bag(0);
            for (int session = 0; session < 2; session++)
            {
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, -1, out var outcome)); Assert.AreEqual(StrikeOutcome.Grew, outcome);
                Assert.AreEqual(SmithResult.Success, camp.TakeTemper());
            }
            Assert.AreEqual(2, camp.AttemptsUsed(camp.Bag.At(0))); Assert.AreEqual(0, camp.AttemptsLeft(camp.Bag.At(0)));
            Assert.True(camp.Quote(EniAction.Temper, bag, -1).Risky, "попытки кончились — остаётся рискованный удар");
            Assert.AreEqual(SmithResult.Exhausted, camp.Strike(bag, -1, out _));
            // Переплавка тратит попытку, сердце — нет (пробел №13).
            var magic = Rich(ItemRarity.Magic, 9); magic.DeveloperAddHeart(0); var target = ForgeTarget.Bag(0);
            Assert.AreEqual(SmithResult.Success, magic.BeginRemelt(target, 0)); Assert.AreEqual(SmithResult.Success, magic.ChooseSessionCandidate(0));
            Assert.AreEqual(1, magic.AttemptsUsed(magic.Bag.At(0)));
            Assert.AreEqual(SmithResult.Success, magic.InlayHeart(target, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots));
            Assert.AreEqual(1, magic.AttemptsUsed(magic.Bag.At(0))); Assert.AreEqual(2, magic.AttemptsLeft(magic.Bag.At(0)));
        }

        [Test] public void PriceEscalatesByHalfPerPaidAction()
        {
            int[] gold = { 80, 120, 180, 270, 405 }, shards = { 5, 8, 12, 17, 26 };
            for (int n = 0; n < gold.Length; n++) { Assert.AreEqual(gold[n], Camp.Escalate(80, n)); Assert.AreEqual(shards[n], Camp.Escalate(5, n)); }
            // Базовое свойство обычной вещи растёт до +100%: две закалки подряд всегда возможны.
            var camp = Rich(ItemRarity.Normal, 9); var bag = ForgeTarget.Bag(0);
            for (int n = 0; n < 3; n++)
            {
                var quote = camp.Quote(EniAction.Temper, bag, -1);
                Assert.AreEqual(gold[n], quote.Gold); Assert.AreEqual(shards[n], quote.Shards);
                if (n == 2) break;
                int before = camp.Money(CurrencyType.Gold), beforeShards = camp.Money(CurrencyType.Shards);
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, -1, out _)); camp.TakeTemper();
                Assert.AreEqual(before - gold[n], camp.Money(CurrencyType.Gold)); Assert.AreEqual(beforeShards - shards[n], camp.Money(CurrencyType.Shards));
            }
            // Сталь и сердце не дорожают; золото сердца и переплавки — дорожает.
            var magic = Rich(ItemRarity.Magic, 9); var target = ForgeTarget.Bag(0); magic.DeveloperAddHeart(0); magic.DeveloperAddHeart(0);
            Assert.AreEqual(100, magic.Quote(EniAction.Heart, target, 0, RunBossKeys.ThicketMaster).Gold);
            Assert.AreEqual(SmithResult.Success, magic.InlayHeart(target, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots));
            var heart = magic.Quote(EniAction.Heart, target, 0, RunBossKeys.ThicketMaster);
            Assert.AreEqual(150, heart.Gold); Assert.AreEqual(1, heart.Hearts);
            Assert.AreEqual(SmithResult.Success, magic.InlayHeart(target, RunBossKeys.ThicketMaster, HeartFacet.ThicketPollen));
            var remelt = magic.Quote(EniAction.Remelt, target, 0);
            Assert.AreEqual(270, remelt.Gold); Assert.AreEqual(1, remelt.Steel); Assert.AreEqual(0, remelt.Shards);
        }

        [Test] public void RiskyStrikeOnlyWhenTemperedGivesMasterpieceOrShards()
        {
            bool sawMasterpiece = false, sawShards = false;
            for (ulong seed = 1; seed <= 200 && !(sawMasterpiece && sawShards); seed++)
            {
                var camp = Rich(ItemRarity.Normal, seed, 15); var bag = ForgeTarget.Bag(0); camp.DeveloperAddHeart(0);
                Assert.AreEqual(SmithResult.NotTempered, camp.RiskyStrike(bag, out _, out _), "пока есть попытки — только закалка");
                for (int session = 0; session < 2; session++) { camp.Strike(bag, -1, out _); camp.TakeTemper(); }
                var before = camp.Bag.At(0); var baseline = Value(camp, new ItemInstance(Sword, 15, ItemRarity.Normal, seed), -1);
                var quote = camp.Quote(EniAction.Temper, bag, -1);
                Assert.True(quote.Risky); Assert.AreEqual(50, quote.RiskPercent); Assert.AreEqual(180, quote.Gold); Assert.AreEqual(12, quote.Shards);
                int shardsBefore = camp.Money(CurrencyType.Shards);
                Assert.AreEqual(SmithResult.Success, camp.RiskyStrike(bag, out bool masterpiece, out int shards));
                if (masterpiece)
                {
                    sawMasterpiece = true; var item = camp.Bag.At(0);
                    Assert.True(camp.IsMasterpiece(item)); Assert.AreEqual(0, shards);
                    var expected = baseline + baseline * (Camp.SessionGrowth(1) + Camp.SessionGrowth(1) + CraftingRecipe.MasterpieceStep);
                    Assert.AreEqual(expected.Raw, Value(camp, item, -1).Raw);
                    // После шедевра — только сердце.
                    Assert.AreEqual(SmithResult.Masterpiece, camp.Quote(EniAction.Temper, bag, -1).Status);
                    Assert.AreEqual(SmithResult.Success, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketBloom));
                    var back = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items); Assert.True(back.Bag.At(0).SameRecipe(camp.Bag.At(0)));
                }
                else
                {
                    sawShards = true;
                    Assert.True(camp.Bag.IsEmpty(0), "вещь рассыпалась");
                    Assert.AreEqual(camp.SalvageShards(before), shards); Assert.AreEqual(1 + 15 / 5 + 2 * 5, shards);
                    Assert.AreEqual(shardsBefore - 12 + shards, camp.Money(CurrencyType.Shards));
                }
            }
            Assert.True(sawMasterpiece && sawShards, "оба исхода встречаются");
        }

        [Test] public void HeartOnePerItemSecondAtRankThreeDistinctFacet()
        {
            var camp = PrototypeContent.NewCamp(); camp.Spend(CurrencyType.Gold, camp.Money(CurrencyType.Gold)); camp.Earn(CurrencyType.Gold, 10000);
            int slot = camp.Bag.Add(new ItemInstance(Sword, 25, ItemRarity.Rare, 123)); var bag = ForgeTarget.Bag(slot);
            camp.DeveloperAddHeart(0);
            Assert.AreEqual(SmithResult.Locked, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots), "сердце — с ранга 1");
            camp.DeveloperSetLevel(6); camp.DeveloperCreditBoss(0); camp.DeveloperAddHeart(0);
            Assert.AreEqual(3, camp.HeartCount(RunBossKeys.ThicketMaster)); Assert.AreEqual(1, camp.HeartSlots);
            Assert.AreEqual(3, Camp.HeartFacetCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(HeartFacet.ThicketRoots, Camp.HeartFacetAt(RunBossKeys.ThicketMaster, 0)); Assert.AreEqual(HeartFacet.ThicketBloom, Camp.HeartFacetAt(RunBossKeys.ThicketMaster, 2));
            var quote = camp.Quote(EniAction.Heart, bag, 0, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots);
            Assert.True(quote.Allowed); Assert.AreEqual(100, quote.Gold); Assert.AreEqual(1, quote.Hearts);
            Assert.AreEqual(SmithResult.Success, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots));
            var item = camp.Bag.At(slot);
            Assert.AreEqual(2, camp.HeartCount(RunBossKeys.ThicketMaster)); Assert.AreEqual(1, item.Crafting.HeartCount);
            item.Crafting.HeartAt(0, out int key, out var facet); Assert.AreEqual(RunBossKeys.ThicketMaster, key); Assert.AreEqual(HeartFacet.ThicketRoots, facet);
            Assert.AreEqual(0, camp.AttemptsUsed(item)); Assert.AreEqual(9900, camp.Money(CurrencyType.Gold));
            Assert.AreEqual(SmithResult.HeartsFull, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketPollen), "второе — только на ранге 3");

            camp.DeveloperSetLevel(18); camp.DeveloperCreditBoss(1); camp.DeveloperCreditBoss(2); Assert.AreEqual(2, camp.HeartSlots);
            Assert.AreEqual(SmithResult.Incompatible, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots), "та же пара (босс, грань)");
            Assert.AreEqual(SmithResult.Incompatible, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.None));
            Assert.AreEqual(SmithResult.NoHeart, camp.InlayHeart(bag, RunBossKeys.Second, HeartFacet.ThicketRoots), "у второго босса граней пока нет");
            Assert.AreEqual(SmithResult.Success, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketBloom));
            Assert.AreEqual(9900 - 150, camp.Money(CurrencyType.Gold), "сердце дорожит золото, не сердце");
            Assert.AreEqual(SmithResult.HeartsFull, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketPollen));

            Assert.AreEqual(0u, camp.WornHeartFacetMask(), "в сумке грань не действует");
            Assert.True(camp.EquipFromBag(slot));
            uint expected = (1u << (int)HeartFacet.ThicketRoots) | (1u << (int)HeartFacet.ThicketBloom);
            Assert.AreEqual(expected, camp.WornHeartFacetMask());
            var back = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.AreEqual(expected, back.WornHeartFacetMask()); Assert.AreEqual(1, back.HeartCount(RunBossKeys.ThicketMaster));
        }

        /// <summary>
        /// Открытая закалка следует за вещью (сумка → герой), переживает сохранение с тем же
        /// номером удара, а в Разлом вещь входит с набранным ростом; конец забега закрывает сессию.
        /// </summary>
        [Test] public void OpenSessionFollowsMovedItemSurvivesSaveAndSettlesOnRiftEntry()
        {
            var camp = new Camp(PrototypeContent.Items(), progressive: true);
            camp.Earn(CurrencyType.Gold, 1000); camp.Earn(CurrencyType.Shards, 100);
            int slot = camp.Bag.Add(new ItemInstance(Sword, 25, ItemRarity.Rare, 123)); var bag = ForgeTarget.Bag(slot);
            int property = SmithTests.Temperable(camp, bag);
            Assert.AreEqual(SmithResult.Success, camp.Strike(bag, property, out var outcome)); Assert.AreEqual(StrikeOutcome.Grew, outcome);
            var grown = camp.Bag.At(slot);
            Assert.True(camp.Bag.Swap(slot, 7)); Assert.True(camp.Session.Target.Same(ForgeTarget.Bag(7)));
            Assert.True(camp.EquipFromBag(7)); Assert.True(camp.Session.Target.Same(ForgeTarget.Worn(EquipSlot.Weapon)));
            Assert.AreEqual(1, camp.Session.Strikes); Assert.AreEqual(ForgeSessionKind.Temper, camp.Session.Kind);

            var loaded = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.AreEqual(ForgeSessionKind.Temper, loaded.Session.Kind); Assert.AreEqual(1, loaded.Session.Strikes);
            Assert.AreEqual(property, loaded.Session.Property); Assert.True(loaded.Session.Snapshot.SameRecipe(camp.Session.Snapshot));
            var worn = ForgeTarget.Worn(EquipSlot.Weapon);
            Assert.AreEqual(15, loaded.Quote(EniAction.Temper, worn, property).RiskPercent, "номер удара пережил сохранение");
            ulong a = 0, b = 0; camp.HashInto(ref a); loaded.HashInto(ref b); Assert.AreEqual(a, b);

            var s = new GameSession(4242, loaded, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            s.EnterRift();
            Assert.False(loaded.Session.IsOpen, "вход в Разлом закрыл закалку (GameSession.BeginRift)");
            Assert.True(s.Run.PlayerEquipment.Worn(EquipSlot.Weapon).SameRecipe(grown), "в Разлом вещь входит с набранным");
            s.ActiveSim.Entities.Health[0] = 0; s.ActiveSim.Entities.Alive[0] = false; s.Step(InputFrame.Empty);
            Assert.AreEqual(1, loaded.AttemptCount); Assert.False(loaded.Session.IsOpen, "забег закрыл закалку");
            Assert.True(loaded.Worn.Worn(EquipSlot.Weapon).SameRecipe(grown));
            Assert.AreEqual(1, loaded.AttemptsUsed(loaded.Worn.Worn(EquipSlot.Weapon)));
        }

        [Test] public void OutcomesAreIdenticalAfterReload()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var camp = Rich(ItemRarity.Magic, seed); var bag = ForgeTarget.Bag(0); int property = SmithTests.Temperable(camp, bag);
                var twin = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
                // Перезагрузка посреди сессии: после первого удара.
                Assert.AreEqual(SmithResult.Success, camp.Strike(bag, property, out _));
                var reloaded = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
                var first = Hammer(camp, bag, property, out int strikes);
                var second = Hammer(reloaded, bag, property, out int reloadedStrikes);
                Assert.AreEqual(first, second); Assert.AreEqual(strikes, reloadedStrikes);
                Assert.True(camp.Bag.At(0).SameRecipe(reloaded.Bag.At(0)));
                // Тот же путь без перезагрузки на копии до оплаты.
                Assert.AreEqual(SmithResult.Success, twin.Strike(bag, property, out _));
                var third = Hammer(twin, bag, property, out _);
                Assert.AreEqual(first, third); Assert.True(camp.Bag.At(0).SameRecipe(twin.Bag.At(0)));
                // Переплавка и перелив тоже детерминированы: кандидаты те же после перезагрузки.
                var magic = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
                if (magic.Quote(EniAction.Remelt, bag, 0).Status != SmithResult.Success) continue;
                Assert.AreEqual(SmithResult.Success, magic.BeginRemelt(bag, 0));
                var again = CampSaveCodec.Decode(CampSaveCodec.Encode(magic), magic.Items);
                Assert.AreEqual(magic.SessionCandidateCount, again.SessionCandidateCount);
                for (int i = 0; i < magic.SessionCandidateCount; i++) Assert.True(magic.SessionCandidateItem(i).SameRecipe(again.SessionCandidateItem(i)));
            }
        }
    }
}
