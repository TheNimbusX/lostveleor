using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    public class CampDragTests
    {
        static ItemInstance Sword(ulong seed) => new ItemInstance(StableId.Of("base.rusty_sword"),1,ItemRarity.Normal,seed);
        [Test] public void ExchangeReturnsToSourceEvenWithEarlierHole()
        {
            var c=PrototypeContent.NewCamp(); c.Bag.Add(Sword(1)); c.Bag.Add(Sword(2)); c.EquipFromBag(0);
            c.EquipFromBag(1); Assert.That(c.Bag.At(0).IsEmpty);Assert.That(c.Bag.At(1).Seed,Is.EqualTo(1));
        }
        [Test] public void DragBagKeepsProtectionAndDoesNotDuplicate()
        {
            var c=PrototypeContent.NewCamp();c.Bag.Add(Sword(1));c.Bag.SetKeep(0,true);
            c.Bag.Swap(0,47);Assert.That(c.Bag.Used,Is.EqualTo(1));Assert.That(c.Bag.IsKept(47));Assert.That(c.Bag.IsEmpty(0));
        }
        [Test] public void IncompatibleUnequipExchangeIsAtomic()
        {
            var c=PrototypeContent.NewCamp();c.Bag.Add(Sword(1));c.EquipFromBag(0);
            c.Bag.Add(new ItemInstance(StableId.Of("base.leather_jacket"),1,ItemRarity.Normal,2));
            Assert.That(c.UnequipToSlot(EquipSlot.Weapon,0),Is.False);Assert.That(c.Worn.Worn(EquipSlot.Weapon).Seed,Is.EqualTo(1));Assert.That(c.Bag.At(0).Seed,Is.EqualTo(2));
        }
        [Test] public void FullBagCanExchangeEquipment()
        {
            var c=PrototypeContent.NewCamp();c.Bag.Add(Sword(1));c.EquipFromBag(0);
            for(int i=0;i<48;i++)c.Bag.Add(Sword((ulong)i+2));
            Assert.That(c.UnequipToSlot(EquipSlot.Weapon,47));Assert.That(c.Bag.Used,Is.EqualTo(48));Assert.That(c.Bag.At(47).Seed,Is.EqualTo(1));
        }
        [Test] public void SaveRestoresExactSlotsEquipmentAndCurrency()
        {
            var c=PrototypeContent.NewCamp();c.Bag.Add(Sword(5));c.EquipFromBag(0);c.Bag.Add(Sword(6));c.Bag.Swap(0,47);c.Bag.SetKeep(47,true);c.Earn(CurrencyType.Gold,123);
            var restored=CampSaveCodec.Decode(CampSaveCodec.Encode(c),PrototypeContent.Items());
            ulong a=0,b=0;c.HashInto(ref a);restored.HashInto(ref b);Assert.That(b,Is.EqualTo(a));
        }
        [Test] public void SaveRejectsPartialAndCorruptBytes()
        {
            var bytes=CampSaveCodec.Encode(PrototypeContent.NewCamp());bytes[20]^=1;
            Assert.Throws<System.IO.InvalidDataException>(()=>CampSaveCodec.Decode(bytes,PrototypeContent.Items()));
            Assert.Throws<System.IO.InvalidDataException>(()=>CampSaveCodec.Decode(new byte[5],PrototypeContent.Items()));
        }
    }
}
