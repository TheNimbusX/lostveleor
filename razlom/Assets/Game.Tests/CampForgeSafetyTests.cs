using System;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampForgeSafetyTests
    {
        static Camp Ready(ItemDatabase db = null, int materials = 5)
        {
            // Sandbox стоит на ранге 3: переплавка, добавление и сердце открыты без боссов.
            var camp = new Camp(db ?? PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 5000); camp.Earn(CurrencyType.Shards, 1000); camp.Earn(CurrencyType.Steel, materials); return camp;
        }
        /// <summary>
        /// Без денег ни одно действие Эни не меняет ничего: ни вещь, ни кошелёк, ни сердца,
        /// ни сессию. Цена проверяется до первого изменения (Quote), списание не частичное.
        /// </summary>
        [Test] public void FailedPaymentChangesNothingForEveryAction()
        {
            var camp = new Camp(PrototypeContent.Items()); camp.Earn(CurrencyType.Steel, 5); camp.DeveloperAddHeart(0);
            camp.Bag.Put(0, new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Rare, 123), true);
            // Обычная вещь с двумя трещинами: попытки кончились — остаётся рискованный удар.
            var crack = new CraftStep(ForgeOperation.Crack, CraftingRecipe.BaseSlot, 0, Fix64.Zero);
            camp.Bag.Put(1, new ItemInstance(StableId.Of("base.rusty_sword"), 5, ItemRarity.Normal, 11, crafting: new CraftingRecipe(new[] { crack, crack })), false);
            var bytes = CampSaveCodec.Encode(camp); var bag = ForgeTarget.Bag(0); int property = SmithTests.Temperable(camp, bag);
            Assert.False(camp.Quote(EniAction.Temper, bag, property).Affordable);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.Strike(bag, property, out _));
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.BeginRemelt(bag, 0));
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.BeginAdd(bag, out bool cracked)); Assert.False(cracked);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketRoots));
            Assert.True(camp.Quote(EniAction.Temper, ForgeTarget.Bag(1), -1).Risky);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.RiskyStrike(ForgeTarget.Bag(1), out bool masterpiece, out int shards));
            Assert.False(masterpiece); Assert.AreEqual(0, shards);
            Assert.False(camp.Session.IsOpen); Assert.AreEqual(1, camp.HeartCount(RunBossKeys.ThicketMaster));
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            camp.Earn(CurrencyType.Gold, 79); camp.Earn(CurrencyType.Shards, 5);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.Strike(bag, property, out _), "79 из 80 — тоже нет");
            camp.Earn(CurrencyType.Gold, 1); Assert.AreEqual(SmithResult.Success, camp.Strike(bag, property, out _));
            Assert.AreEqual(0, camp.Money(CurrencyType.Gold)); Assert.AreEqual(0, camp.Money(CurrencyType.Shards)); Assert.True(camp.Bag.IsKept(0));
        }
        static ItemDatabase Restrictions()
        {
            return new ItemDatabase(new[] { new ItemBaseDefinition(100, ItemCategory.Weapon), new ItemBaseDefinition(200, ItemCategory.Armor), new ItemBaseDefinition(300, ItemCategory.Artifact) },
                new[] {
                    new AffixDefinition(1001, 11, StatType.Damage, ModifierOp.Flat, Fix64.FromInt(2), Fix64.FromInt(6), 1, 1, AffixDefinition.Mask(ItemCategory.Weapon)),
                    new AffixDefinition(1002, 22, StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(15), Fix64.FromInt(60), 1, 1, AffixDefinition.Mask(ItemCategory.Weapon, ItemCategory.Armor)),
                    new AffixDefinition(1003, 33, StatType.AttackSpeed, ModifierOp.Increased, Fix64.Ratio(5,100), Fix64.Ratio(20,100), 10, 1, AffixDefinition.Mask(ItemCategory.Weapon)),
                    new AffixDefinition(1004, 44, StatType.Armor, ModifierOp.Flat, Fix64.FromInt(10), Fix64.FromInt(45), 1, 1, AffixDefinition.Mask(ItemCategory.Armor)) });
        }
        static ItemInstance Single(ItemDatabase db, int baseId, short level, int affix)
        {
            var generated = new GeneratedItem();
            for (ulong seed = 1; seed < 10000; seed++)
            {
                var item = new ItemInstance(baseId, level, ItemRarity.Magic, seed); ItemGenerator.Generate(item, db, generated);
                if (generated.AffixCount == 1 && generated.GetAffix(0).AffixId == affix) return item;
            }
            throw new InvalidOperationException("Нет контрольного сида");
        }
        /// <summary>Без стали переплавка, без сердца вплавление не проходят и ничего не трогают.</summary>
        [Test] public void MissingSteelOrHeartKeepsItemAndWallet()
        {
            var db = Restrictions(); var camp = Ready(db, materials: 0);
            camp.Bag.Put(0, Single(db, 100, 20, 1001), false);
            var bytes = CampSaveCodec.Encode(camp); var bag = ForgeTarget.Bag(0);
            var remelt = camp.Quote(EniAction.Remelt, bag, 0);
            Assert.AreEqual(SmithResult.Success, remelt.Status); Assert.AreEqual(1, remelt.Steel); Assert.False(remelt.Affordable);
            Assert.AreEqual(SmithResult.InsufficientFunds, camp.BeginRemelt(bag, 0)); CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            var heart = camp.Quote(EniAction.Heart, bag, 0, RunBossKeys.ThicketMaster, HeartFacet.ThicketBloom);
            Assert.AreEqual(1, heart.Hearts); Assert.False(heart.Affordable);
            Assert.AreEqual(SmithResult.NoHeart, camp.InlayHeart(bag, RunBossKeys.ThicketMaster, HeartFacet.ThicketBloom));
            Assert.AreEqual(SmithResult.NoHeart, camp.Quote(EniAction.Heart, bag, 0, RunBossKeys.Second).Status, "у второго босса граней пока нет");
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(camp));
            camp.Earn(CurrencyType.Steel, 1); Assert.AreEqual(SmithResult.Success, camp.BeginRemelt(bag, 0));
            Assert.AreEqual(0, camp.Money(CurrencyType.Steel)); Assert.AreEqual(4880, camp.Money(CurrencyType.Gold));
        }
        static Camp WithStep(ItemDatabase db, ItemInstance source, CraftStep step)
        {
            var camp = Ready(db);
            camp.Bag.Put(0, new ItemInstance(source.BaseId, source.ItemLevel, source.Rarity, source.Seed, crafting: new CraftingRecipe(new[] { step })), false);
            return camp;
        }
        /// <summary>Шаг, который не мог записать ни один кузнец, — это порча файла, а не смена правил.</summary>
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void CraftedDecoderRejectsStructurallyBrokenSteps(int fault)
        {
            var db = Restrictions(); var source = Single(db, 100, 20, 1001);
            // Перенос (3) удалён 06.10 и не принимается никогда.
            CraftStep step = new CraftStep((ForgeOperation)3, 0, 1002, Fix64.Half);
            if (fault == 1) step = new CraftStep(ForgeOperation.Remelt, 6, 1002, Fix64.Half);
            if (fault == 2) step = new CraftStep(ForgeOperation.Remelt, 0, 1002, -Fix64.Half);
            if (fault == 3) step = new CraftStep(ForgeOperation.Remelt, 0, 1002, Fix64.FromInt(2));
            if (fault == 4) step = new CraftStep((ForgeOperation)7, CraftingRecipe.BaseSlot, 0, Fix64.Zero);
            var camp = WithStep(db, source, step);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveCodec.Encode(camp), db));
        }
        /// <summary>
        /// Шаг устроен правильно, но нынешние правила или справочник его не разворачивают:
        /// рецепт срезается целиком, вещь остаётся исходным роллом, профиль грузится.
        /// </summary>
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void CraftedDecoderCutsRecipesTheRulesNoLongerAllow(int fault)
        {
            var db = Restrictions(); var source = Single(db, 100, 20, 1001);
            CraftStep step = new CraftStep(ForgeOperation.Remelt, CraftingRecipe.BaseSlot, 1002, Fix64.Half);
            if (fault == 1) step = new CraftStep(ForgeOperation.Remelt, 0, 9999, Fix64.Half);
            if (fault == 2) { source = Single(db, 200, 20, 1002); step = new CraftStep(ForgeOperation.Remelt, 0, 1003, Fix64.Half); }
            if (fault == 3) { source = Single(db, 100, 1, 1001); step = new CraftStep(ForgeOperation.Remelt, 0, 1003, Fix64.Half); }
            if (fault == 4) step = new CraftStep(ForgeOperation.Add, 1, 1001, Fix64.Half);
            if (fault == 5) { source = new ItemInstance(source.BaseId, source.ItemLevel, ItemRarity.Unique, source.Seed); step = new CraftStep(ForgeOperation.Remelt, 0, 1002, Fix64.Half); }
            // Базовое свойство закаляет только обычная вещь.
            if (fault == 6) step = new CraftStep(ForgeOperation.Temper, CraftingRecipe.BaseSlot, 0, Fix64.Half);
            // Сердце снятого босса.
            if (fault == 7) step = new CraftStep(ForgeOperation.Heart, (byte)HeartFacet.ThicketRoots, StableId.Of("boss.removed"), Fix64.Zero);
            // Закалка без роста.
            if (fault == 8) step = new CraftStep(ForgeOperation.Temper, 0, 0, Fix64.Zero);
            var camp = WithStep(db, source, step);
            var loaded = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), db).Bag.At(0);
            Assert.IsNull(loaded.Crafting); Assert.AreEqual(source.BaseId, loaded.BaseId);
            Assert.AreEqual(source.Seed, loaded.Seed); Assert.AreEqual(source.Rarity, loaded.Rarity);
        }
        [Test] public void CraftedDecoderRejectsExcessiveLengthBeforeAllocatingHistory()
        {
            var camp = Ready(); var bytes = CampSaveCodec.Encode(camp);
            // Заголовок файла, секция ядра (флаги, акт, сумка, уровень, опыт, кошелёк из пяти
            // валют), заголовок секции сумки — и поля первой вещи до числа шагов рецепта.
            int bag = 8 + 6 + (1 + 1 + 4 + 4 + 4 + 1 + 4 * (int)CurrencyType.Count) + 6;
            int recipeCount = bag + 4 + 2 + 1 + 8;
            bytes[recipeCount] = 1; bytes[recipeCount + 1] = 4;
            uint hash = 2166136261; for (int i = 0; i < bytes.Length - 4; i++) { hash ^= bytes[i]; hash = unchecked(hash * 16777619); }
            Array.Copy(BitConverter.GetBytes(hash), 0, bytes, bytes.Length - 4, 4);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(bytes, camp.Items));
        }
    }
}
