using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    public sealed class PotionTests
    {
        static GameSession Ready()
        {
            var camp=new Camp(PrototypeContent.Items(),act:3);camp.Earn(CurrencyType.Gold,1000);
            for(int i=0;i<4;i++)for(int n=0;n<2;n++)Assert.True(camp.BuyPotion((PotionKind)i));
            return new GameSession(77,camp,PrototypeContent.Modules(),PrototypeContent.ItemBaseIds());
        }
        static void DisableFoes(Simulation sim)
        {for(int id=1;id<sim.Entities.Count;id++)sim.Entities.Alive[id]=false;}
        [Test] public void PurchaseHasExactPriceAndRejectsWithoutChangingWallet()
        {
            var c=new Camp(PrototypeContent.Items());Assert.False(c.BuyPotion(PotionKind.SmallHealth));
            c.Earn(CurrencyType.Gold,110);for(int i=0;i<4;i++)Assert.True(c.BuyPotion((PotionKind)i));
            Assert.AreEqual(0,c.Money(CurrencyType.Gold));var before=CampSaveCodec.Encode(c);
            Assert.False(c.BuyPotion(PotionKind.LargeHealth));Assert.False(c.BuyPotion((PotionKind)255));
            CollectionAssert.AreEqual(before,CampSaveCodec.Encode(c));
        }
        [Test] public void SharedCooldownAndSlotPriorityUseOneBottleAndRejectFullHealing()
        {
            var s=Ready();s.EnterRift();DisableFoes(s.ActiveSim);var e=s.ActiveSim.Entities;
            e.Health[0]=1;e.MaxHealth[0]=200;e.MaxLavidium[0]=100;e.Lavidium[0]=Fix64.Zero;
            s.Step(new InputFrame{PotionSlotMask=3});
            Assert.AreEqual(21,e.Health[0]);Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));
            Assert.AreEqual(2,s.Camp.PotionCount(PotionKind.SmallLavidium));
            s.Step(new InputFrame{PotionSlotMask=2});Assert.AreEqual(2,s.Camp.PotionCount(PotionKind.SmallLavidium));
            while(s.PotionCooldownTicksLeft>0)s.Step(InputFrame.Empty);
            s.Step(new InputFrame{PotionSlotMask=2});Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallLavidium));
            while(s.PotionCooldownTicksLeft>0)s.Step(InputFrame.Empty);
            e.Health[0]=e.MaxHealth[0];s.Step(new InputFrame{PotionSlotMask=1});
            Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));Assert.AreEqual(0,s.PotionCooldownTicksLeft);
        }
        [Test] public void DummyPracticeIsFreeAndCampOutsideDummyZoneDoesNotDrink()
        {
            var s=Ready();int stock=s.Camp.PotionCount(PotionKind.SmallHealth);s.CampSim.Entities.Health[0]=1;
            s.Step(new InputFrame{PotionSlotMask=1});Assert.AreEqual(1,s.CampSim.Entities.Health[0]);
            s.ConfigureCampTraining(new[]{new CampDummyDefinition(new FixVec2(Fix64.One,Fix64.Zero),10000,Fix64.Zero,Fix64.Zero)},FixVec2.Zero,Fix64.FromInt(4));
            s.CampSim.Entities.Health[0]=1;s.Step(new InputFrame{PotionSlotMask=1});
            Assert.Greater(s.CampSim.Entities.Health[0],1);Assert.AreEqual(stock,s.Camp.PotionCount(PotionKind.SmallHealth));
        }
        [Test] public void DeadPlayerCannotDrinkAndEmptyFramesDoNotRepeat()
        {
            var s=Ready();s.EnterRift();DisableFoes(s.ActiveSim);s.ActiveSim.Entities.Health[0]=1;
            s.Step(new InputFrame{PotionSlotMask=1});for(int i=0;i<30;i++)s.Step(InputFrame.Empty);
            Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));
            s.ActiveSim.Entities.Alive[0]=false;s.Step(new InputFrame{PotionSlotMask=3});
            Assert.AreEqual(1,s.Camp.PotionCount(PotionKind.SmallHealth));
        }
        [Test] public void StockAndArbitrarySelectionPersistAndCannotChangeDuringRun()
        {
            var s=Ready();Assert.True(s.SetPreparedPotion(0,PotionKind.LargeHealth));
            Assert.True(s.SetPreparedPotion(1,PotionKind.SmallHealth));
            var bytes=CampSaveCodec.Encode(s.Camp);var c=CampSaveCodec.Decode(bytes,s.Camp.Items);
            CollectionAssert.AreEqual(bytes,CampSaveCodec.Encode(c));
            Assert.AreEqual(PotionKind.LargeHealth,c.SelectedPotion(0));Assert.AreEqual(PotionKind.SmallHealth,c.SelectedPotion(1));
            s.EnterRift();Assert.False(s.SetPreparedPotion(0,PotionKind.LargeLavidium));
            s.Step(new InputFrame{PotionMask=48});
            Assert.AreEqual(PotionKind.LargeHealth,s.Run.Preparation.Potion1);Assert.AreEqual(PotionKind.SmallHealth,s.Run.Preparation.Potion2);
        }
        [Test] public void VersionFiveMigratesWithEmptyPotionsAndRetainsMerchant()
        {
            var c=Ready().Camp;c.RefreshTrader();
            var restored=CampSaveCodec.Decode(LegacyCampSaveFixture.Encode(c,5),c.Items);
            Assert.AreEqual(c.TraderGeneration,restored.TraderGeneration);Assert.AreEqual(c.Money(CurrencyType.Gold),restored.Money(CurrencyType.Gold));
            for(int i=0;i<Camp.PotionKindCount;i++)Assert.AreEqual(0,restored.PotionCount((PotionKind)i));
        }
    }
}