using System;
using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    public sealed class PotionTests
    {
        static GameSession Ready()
        {
            var camp=new Camp(PrototypeContent.Items());camp.Earn(CurrencyType.Gold,1000);
            for(int i=0;i<4;i++)for(int n=0;n<2;n++)Assert.True(camp.BuyPotion((PotionKind)i));
            return new GameSession(77,camp,PrototypeContent.Modules(),PrototypeContent.ItemBaseIds());
        }
        [Test] public void PurchaseHasExactPriceAndRejectsWithoutChangingWallet()
        {
            var c=new Camp(PrototypeContent.Items());Assert.False(c.BuyPotion(PotionKind.SmallHealth));
            c.Earn(CurrencyType.Gold,110);for(int i=0;i<4;i++)Assert.True(c.BuyPotion((PotionKind)i));
            Assert.AreEqual(0,c.Money(CurrencyType.Gold));var before=CampSaveCodec.Encode(c);
            Assert.False(c.BuyPotion(PotionKind.LargeHealth));Assert.False(c.BuyPotion((PotionKind)255));
            CollectionAssert.AreEqual(before,CampSaveCodec.Encode(c));
        }
        [TestCase(false)][TestCase(true)] public void RestorePercentAndConsumeEvenAtMaximum(bool rift)
        {
            var s=Ready();if(rift)s.EnterRift();var e=s.ActiveSim.Entities;
            for(int id=1;id<e.Count;id++)e.Alive[id]=false;
            e.Health[0]=1;e.MaxHealth[0]=200;e.MaxLavidium[0]=100;e.Lavidium[0]=Fix64.Zero;
            s.Step(new InputFrame{PotionMask=5});Assert.AreEqual(21,e.Health[0]);Assert.True(e.Lavidium[0]>=Fix64.FromInt(10));
            s.Step(new InputFrame{PotionMask=10});Assert.AreEqual(81,e.Health[0]);Assert.True(e.Lavidium[0]>=Fix64.FromInt(40));
            e.Health[0]=200;e.Lavidium[0]=Fix64.FromInt(100);s.Step(new InputFrame{PotionMask=15});
            Assert.AreEqual(200,e.Health[0]);Assert.AreEqual(Fix64.FromInt(100),e.Lavidium[0]);
            for(int i=0;i<4;i++)Assert.AreEqual(0,s.Camp.PotionCount((PotionKind)i));
            s.Step(new InputFrame{PotionMask=15});for(int i=0;i<4;i++)Assert.AreEqual(0,s.Camp.PotionCount((PotionKind)i));
        }
        [Test] public void DeadPlayerCannotDrinkAndEmptyFramesDoNotRepeat()
        {
            var s=Ready();s.Step(new InputFrame{PotionMask=1});for(int i=0;i<30;i++)s.Step(InputFrame.Empty);
            Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));
            s.ActiveSim.Entities.Alive[0]=false;s.Step(new InputFrame{PotionMask=15});
            Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));Assert.AreEqual(2,s.Camp.PotionCount(PotionKind.LargeHealth));
        }
        [Test] public void StockAndSelectionPersistWithoutRefillAndReplayDeterministically()
        {
            var a=Ready();var b=Ready();
            foreach(byte mask in new byte[]{48,10,0,5,1,4,0})
            {a.Step(new InputFrame{PotionMask=mask});b.Step(new InputFrame{PotionMask=mask});CollectionAssert.AreEqual(CampSaveCodec.Encode(a.Camp),CampSaveCodec.Encode(b.Camp));}
            var bytes=CampSaveCodec.Encode(a.Camp);var c=CampSaveCodec.Decode(bytes,a.Camp.Items);
            Assert.AreEqual(3,c.PotionSelection);CollectionAssert.AreEqual(bytes,CampSaveCodec.Encode(c));
            a.EnterRift();a.Step(new InputFrame{Command=(byte)RunCommand.Leave});
            for(int i=0;i<4;i++)Assert.AreEqual(c.PotionCount((PotionKind)i),a.Camp.PotionCount((PotionKind)i));
        }
        [Test] public void VersionFiveMigratesWithEmptyPotionsAndRetainsMerchant()
        {
            var c=Ready().Camp;c.RefreshTrader();var current=CampSaveCodec.Encode(c);
            // Хвост v8: шесть зелий и два выбора, атлас, три байта заказов.
            var old=new byte[current.Length-26-4-4*c.DiscoveredCount-3];Array.Copy(current,old,old.Length-4);BitConverter.GetBytes(5).CopyTo(old,4);
            uint hash=2166136261;for(int i=0;i<old.Length-4;i++){hash^=old[i];hash=unchecked(hash*16777619);}BitConverter.GetBytes(hash).CopyTo(old,old.Length-4);
            var restored=CampSaveCodec.Decode(old,c.Items);Assert.AreEqual(c.TraderGeneration,restored.TraderGeneration);
            Assert.AreEqual(c.Money(CurrencyType.Gold),restored.Money(CurrencyType.Gold));for(int i=0;i<4;i++)Assert.AreEqual(0,restored.PotionCount((PotionKind)i));
        }
    }
}
