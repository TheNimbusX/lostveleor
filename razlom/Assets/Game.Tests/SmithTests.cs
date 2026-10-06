using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Эни 06.10, основа: закалка растит только выбранное свойство, живёт в рецепте вещи
    /// (надевание, сохранение), надетое закаляется на месте, оплата атомарна, «беречь»
    /// защищает от разбора, обычная вещь закаляет базовое свойство.
    /// </summary>
    public sealed class SmithTests
    {
        static Camp Ready()
        {
            var camp=new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold,1000);camp.Earn(CurrencyType.Shards,1000);
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),10,ItemRarity.Rare,123));return camp;
        }
        /// <summary>Первое свойство, которое ещё может вырасти.</summary>
        internal static int Temperable(Camp camp,ForgeTarget target)
        {
            for(int p=0;p<GeneratedItem.MaxAffixes;p++)if(camp.Quote(EniAction.Temper,target,p).Status==SmithResult.Success)return p;
            Assert.Fail("Нет свойства под закалку");return -1;
        }
        /// <summary>Ожидаемое значение после роста growth: доля всего диапазона, не выше максимума.</summary>
        internal static Fix64 Grown(Camp camp,RolledAffix affix,Fix64 growth)
        {
            var definition=camp.Items.GetAffix(camp.Items.IndexOfAffix(affix.AffixId));
            return Fix64.Min(affix.Value+(definition.MaxValue-definition.MinValue)*growth,Fix64.Max(affix.Value,definition.MaxValue));
        }
        [Test] public void TemperedAffixPersistsThroughEquipAndSave()
        {
            var camp=Ready();var before=new GeneratedItem();var after=new GeneratedItem();
            int selected=Temperable(camp,ForgeTarget.Bag(0));
            ItemGenerator.Generate(camp.Bag.At(0),camp.Items,before);
            Assert.AreEqual(SmithResult.Success,camp.Strike(ForgeTarget.Bag(0),selected,out var outcome));
            Assert.AreEqual(StrikeOutcome.Grew,outcome,"первый удар без риска");
            ItemGenerator.Generate(camp.Bag.At(0),camp.Items,after);
            Assert.AreEqual(before.AffixCount,after.AffixCount);
            for(int i=0;i<after.AffixCount;i++)
            {
                Assert.AreEqual(before.GetAffix(i).AffixId,after.GetAffix(i).AffixId);
                var expected=i==selected?Grown(camp,before.GetAffix(i),Camp.TemperGrowth(0)):before.GetAffix(i).Value;
                Assert.AreEqual(expected.Raw,after.GetAffix(i).Value.Raw);
            }
            Assert.AreEqual(SmithResult.Success,camp.TakeTemper());Assert.False(camp.Session.IsOpen);
            Assert.AreEqual(1,camp.AttemptsUsed(camp.Bag.At(0)));Assert.AreEqual(3,camp.AttemptsLeft(camp.Bag.At(0)));
            Assert.AreEqual(10,camp.Bag.At(0).ItemLevel,"закалка уровень вещи не меняет");
            Assert.AreEqual(920,camp.Money(CurrencyType.Gold));Assert.AreEqual(995,camp.Money(CurrencyType.Shards));
            camp.EquipFromBag(0);camp=CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items);
            camp.UnequipToBag(EquipSlot.Weapon);
            ItemGenerator.Generate(camp.Bag.At(0),camp.Items,before);
            Assert.AreEqual(after.GetAffix(selected).Value.Raw,before.GetAffix(selected).Value.Raw);
            Assert.AreEqual(1,camp.AttemptsUsed(camp.Bag.At(0)));
        }
        [Test] public void WornItemTempersInPlaceAndStatsFollow()
        {
            var camp=Ready();var sheet=new StatSheet();sheet.SetBase(StatType.Damage,Fix64.FromInt(10));
            camp.Worn.Bind(sheet);camp.EquipFromBag(0);
            Assert.True(camp.Bag.IsEmpty(0));
            var target=ForgeTarget.Worn(EquipSlot.Weapon);var before=new GeneratedItem();
            ItemGenerator.Generate(camp.Worn.Worn(EquipSlot.Weapon),camp.Items,before);
            int selected=Temperable(camp,target);var stat=before.GetAffix(selected).Stat;var statBefore=sheet.Get(stat);
            Assert.AreEqual(SmithResult.Success,camp.Strike(target,selected,out var outcome));Assert.AreEqual(StrikeOutcome.Grew,outcome);
            var worn=camp.Worn.Worn(EquipSlot.Weapon);
            Assert.AreEqual(1,camp.AttemptsUsed(worn));Assert.True(camp.Bag.IsEmpty(0));
            Assert.Greater(sheet.Get(stat).Raw,statBefore.Raw);
            Assert.AreEqual(920,camp.Money(CurrencyType.Gold));Assert.AreEqual(995,camp.Money(CurrencyType.Shards));
            camp.SettleForgeSession();Assert.False(camp.Session.IsOpen);
            camp=CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items);
            Assert.True(camp.Worn.Worn(EquipSlot.Weapon).SameRecipe(worn));
            Assert.AreEqual(SmithResult.InvalidItem,camp.Strike(ForgeTarget.Worn(EquipSlot.Armor),0,out _));
            Assert.AreEqual(SmithResult.InvalidItem,camp.Quote(EniAction.Temper,ForgeTarget.Worn((EquipSlot)9),0).Status);
        }
        [Test] public void FailedPaymentIsAtomicAndSalvageCannotBeRepeated()
        {
            var camp=Ready();camp.Spend(CurrencyType.Shards,1000);var original=CampSaveCodec.Encode(camp);
            Assert.AreEqual(SmithResult.InsufficientFunds,camp.Strike(ForgeTarget.Bag(0),Temperable(camp,ForgeTarget.Bag(0)),out var outcome));
            Assert.AreEqual(StrikeOutcome.None,outcome);Assert.False(camp.Session.IsOpen);
            CollectionAssert.AreEqual(original,CampSaveCodec.Encode(camp));
            int expected=camp.SalvageShards(camp.Bag.At(0));Assert.AreEqual(Inventory.ShardsFor(camp.Bag.At(0)),expected,"без клятв — без надбавки");
            Assert.AreEqual(SmithResult.Success,camp.Dismantle(0,out int shards));Assert.AreEqual(expected,shards);
            Assert.AreEqual(SmithResult.InvalidItem,camp.Dismantle(0,out _));Assert.AreEqual(expected,camp.Money(CurrencyType.Shards));
            Assert.AreEqual(SmithResult.InvalidItem,camp.Strike(ForgeTarget.Bag(-1),0,out _));Assert.AreEqual(SmithResult.InvalidItem,camp.Dismantle(48,out _));
        }
        [Test] public void KeptItemsAreNotSalvagedAndNormalsTemperTheirBase()
        {
            var camp=Ready();camp.Bag.Put(0,camp.Bag.At(0),true);
            Assert.AreEqual(SmithResult.Protected,camp.Dismantle(0,out _));Assert.False(camp.Bag.IsEmpty(0));
            Assert.AreEqual(0,camp.SalvageJunk());Assert.False(camp.Bag.IsEmpty(0));
            int slot=camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),5,ItemRarity.Normal,11));
            var target=ForgeTarget.Bag(slot);
            // Обычная вещь: аффиксов нет, закаляется базовое свойство основы (property −1).
            Assert.AreEqual(SmithResult.NoAffix,camp.Quote(EniAction.Temper,target,0).Status);
            Assert.AreEqual(SmithResult.Success,camp.Quote(EniAction.Temper,target,-1).Status);
            var roll=new GeneratedItem();ItemGenerator.Generate(camp.Bag.At(slot),camp.Items,roll);var implicitBefore=roll.ImplicitValue;
            Assert.AreEqual(SmithResult.Success,camp.Strike(target,-1,out var outcome));Assert.AreEqual(StrikeOutcome.Grew,outcome);
            ItemGenerator.Generate(camp.Bag.At(slot),camp.Items,roll);
            Assert.AreEqual((implicitBefore+implicitBefore*Camp.TemperGrowth(0)).Raw,roll.ImplicitValue.Raw);
            Assert.AreEqual(CraftingRecipe.BaseSlot,camp.Bag.At(slot).Crafting.Step(0).Slot);
            Assert.AreEqual(2,Camp.TemperAttempts(ItemRarity.Normal));
            // Обычная вещь не переплавляется и не дополняется.
            Assert.AreEqual(SmithResult.InvalidItem,camp.Quote(EniAction.Remelt,target,0).Status);
            Assert.AreEqual(SmithResult.InvalidItem,camp.Quote(EniAction.Add,target).Status);
        }
    }
}
