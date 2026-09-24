using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class AlchemistRecipeTests
    {
        static Camp CampWithFunds()
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 500);
            camp.MeetAlchemist();
            Assert.That(camp.AcceptAlchemyOrder(AlchemistOrder.Resin), Is.EqualTo(AlchemistActionResult.Success));
            Assert.That(camp.AcceptAlchemyOrder(AlchemistOrder.Surge), Is.EqualTo(AlchemistActionResult.Success));
            return camp;
        }

        [Test]
        public void BothExchangePathsUnlockOnceAndPreserveSelectedBottles()
        {
            var camp = CampWithFunds();
            Assert.That(camp.TryBuyPotion(PotionKind.LivingResin), Is.EqualTo(PotionPurchaseResult.RecipeLocked));
            int slot = camp.Bag.Add(new ItemInstance(PrototypeContent.ItemBaseIds()[0], 1, ItemRarity.Magic, 99));
            camp.Bag.SetKeep(slot, true);
            Assert.That(camp.ExchangeRareForResin(slot), Is.EqualTo(AlchemistActionResult.ProtectedItem));
            camp.Bag.SetKeep(slot, false);
            Assert.That(camp.ExchangeRareForResin(slot), Is.EqualTo(AlchemistActionResult.Success));
            Assert.That(camp.ExchangeRareForResin(slot), Is.EqualTo(AlchemistActionResult.AlreadyUnlocked));
            Assert.That(camp.Bag.IsEmpty(slot), Is.True);
            Assert.That(camp.ExchangeShardsForSurge(), Is.EqualTo(AlchemistActionResult.InsufficientShards));
            camp.Earn(CurrencyType.Shards, 12);
            Assert.That(camp.ExchangeShardsForSurge(), Is.EqualTo(AlchemistActionResult.Success));
            Assert.That(camp.ExchangeShardsForSurge(), Is.EqualTo(AlchemistActionResult.AlreadyUnlocked));
            Assert.That(camp.Money(CurrencyType.Shards), Is.Zero);
            Assert.That(camp.BuyPotion(PotionKind.LivingResin), Is.True);
            Assert.That(camp.BuyPotion(PotionKind.LavidiumSurge), Is.True);
            Assert.That(camp.Money(CurrencyType.Gold), Is.EqualTo(360));
            Assert.That(camp.SelectPotion(PotionKind.LivingResin), Is.True);
            Assert.That(camp.SelectPotion(PotionKind.LavidiumSurge), Is.True);
            var bytes = CampSaveCodec.Encode(camp);
            var restored = CampSaveCodec.Decode(bytes, camp.Items);
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(restored));
            Assert.That(restored.SelectedPotion(0), Is.EqualTo(PotionKind.LivingResin));
            Assert.That(restored.SelectedPotion(1), Is.EqualTo(PotionKind.LavidiumSurge));
        }

        [Test]
        public void EffectsRefreshWithoutStackingAndExpireOnSimulationTicks()
        {
            var camp = CampWithFunds();
            camp.Earn(CurrencyType.Shards, 12);
            camp.ExchangeShardsForSurge();
            int slot = camp.Bag.Add(new ItemInstance(PrototypeContent.ItemBaseIds()[0], 1, ItemRarity.Magic, 99));
            camp.ExchangeRareForResin(slot);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(camp.BuyPotion(PotionKind.LivingResin), Is.True);
                Assert.That(camp.BuyPotion(PotionKind.LavidiumSurge), Is.True);
            }
            var session = new GameSession(71, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            var sim = session.ActiveSim;
            var baseMoveSpeed = sim.Entities.Stats[0].Get(StatType.MoveSpeed);
            sim.Entities.Health[0] = 1;
            sim.Entities.Lavidium[0] = Fix64.Zero;
            session.Step(new InputFrame { PotionMask = (byte)(Camp.PotionInputBit(PotionKind.LivingResin) |
                Camp.PotionInputBit(PotionKind.LavidiumSurge)) });
            Assert.That(sim.ResinTicksLeft, Is.GreaterThan(0));
            Assert.That(sim.SurgeTicksLeft, Is.GreaterThan(0));
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Ratio(20, 100)));
            Assert.That(sim.Entities.Stats[0].Get(StatType.MoveSpeed),
                Is.EqualTo(baseMoveSpeed * Fix64.Ratio(120, 100)));
            Assert.That(sim.Entities.Lavidium[0], Is.GreaterThan(Fix64.Zero));
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            int before = sim.Entities.Health[0];
            sim.ApplyAbilityDamage(1, 0, 40, -1, DamageType.Physical);
            Assert.That(before - sim.Entities.Health[0], Is.EqualTo(30));
            for (int i = 0; i < 30; i++) session.Step(InputFrame.Empty);
            session.Step(new InputFrame { PotionMask = Camp.PotionInputBit(PotionKind.LavidiumSurge) });
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Ratio(20, 100)));
            Assert.That(sim.SurgeTicksLeft, Is.GreaterThan(170));
            for (int i = 0; i < Simulation.PotionEffectTicks; i++) session.Step(InputFrame.Empty);
            Assert.That(sim.SurgeTicksLeft, Is.Zero);
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Zero));
            Assert.That(camp.PotionCount(PotionKind.LavidiumSurge), Is.Zero);
        }

        [Test]
        public void CleanLevelRequiresExitAndOnlyConsumedPotionInvalidatesIt()
        {
            var camp = CampWithFunds();
            var session = new GameSession(73, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            session.EnterRift();
            Assert.That(session.AlchemyCleanLevelInProgress, Is.True);
            session.Step(new InputFrame { PotionMask = Camp.PotionInputBit(PotionKind.SmallHealth) });
            Assert.That(session.AlchemyCleanLevelInProgress, Is.True, "Empty bottle must not count as drinking");
            for (int i = 1; i < session.Run.Sim.Entities.Count; i++) session.Run.Sim.Entities.Alive[i] = false;
            session.Step(InputFrame.Empty);
            Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Surge), Is.EqualTo(AlchemistOrderStatus.Accepted));
            session.Run.Sim.Entities.Position[0] = session.Run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Surge), Is.EqualTo(AlchemistOrderStatus.Ready));
            Assert.That(camp.TurnInAlchemyOrder(AlchemistOrder.Surge), Is.EqualTo(AlchemistActionResult.Success));
            Assert.That(camp.TurnInAlchemyOrder(AlchemistOrder.Surge), Is.EqualTo(AlchemistActionResult.AlreadyUnlocked));
        }

        [Test]
        public void DrinkingDuringExitWalkResetsAttemptAndLeavingDoesNotAwardIt()
        {
            var camp = CampWithFunds();
            Assert.That(camp.BuyPotion(PotionKind.SmallHealth), Is.True);
            var session = new GameSession(74, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            session.EnterRift();
            for (int i = 1; i < session.Run.Sim.Entities.Count; i++) session.Run.Sim.Entities.Alive[i] = false;
            session.Step(InputFrame.Empty);
            Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.SeekingExit));
            session.Step(new InputFrame { PotionMask = Camp.PotionInputBit(PotionKind.SmallHealth) });
            Assert.That(session.AlchemyCleanLevelInProgress, Is.False);
            session.Run.Sim.Entities.Position[0] = session.Run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Surge), Is.EqualTo(AlchemistOrderStatus.Accepted));
            session.Step(new InputFrame { Command = (byte)RunCommand.Leave });
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Surge), Is.EqualTo(AlchemistOrderStatus.Accepted));
        }

        [Test]
        public void DeathEventCarriesTheActualKiller()
        {
            var sim = new Simulation(5);
            sim.SetupForestBudEncounter(null, 7);
            sim.ApplyAbilityDamage(0, 1, 1000, -1, DamageType.Physical);
            bool found = false;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Death && e.Target == 1)
                { Assert.That(e.Source, Is.EqualTo(0)); found = true; }
            Assert.That(found, Is.True);
        }

        [Test]
        public void BudKillRequiresAcceptedOrderAndNormalRun()
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.MeetAlchemist();
            var session = new GameSession(81, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            session.EnterRift();
            int id = 1;
            Assert.That(session.Run.Sim.Entities.Count, Is.GreaterThan(id));
            session.Run.Sim.Entities.Kind[id] = EnemyKind.ForestBud;
            session.Run.Sim.ApplyAbilityDamage(0, id, 10000, -1, DamageType.Physical);
            session.RecordAlchemyDeaths(session.Run.Sim.Events, session.Run.Sim);
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Resin), Is.EqualTo(AlchemistOrderStatus.Available));
            Assert.That(camp.AcceptAlchemyOrder(AlchemistOrder.Resin), Is.EqualTo(AlchemistActionResult.Success));
            session.Run.Sim.Step(InputFrame.Empty);
            int fresh = session.Run.Sim.Entities.Spawn(FixVec2.Zero, 100, Faction.Orvill);
            session.Run.Sim.Entities.Kind[fresh] = EnemyKind.ForestBud;
            session.Run.Sim.ApplyAbilityDamage(0, fresh, 10000, -1, DamageType.Physical);
            session.RecordAlchemyDeaths(session.Run.Sim.Events, session.Run.Sim);
            Assert.That(camp.AlchemyStatus(AlchemistOrder.Resin), Is.EqualTo(AlchemistOrderStatus.Ready));

            var other = CampWithFunds();
            var developer = new GameSession(82, other, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            developer.StartForestBudTest(null, 82);
            developer.Run.Sim.ApplyAbilityDamage(0, 1, 10000, -1, DamageType.Physical);
            developer.RecordAlchemyDeaths(developer.Run.Sim.Events, developer.Run.Sim);
            Assert.That(other.AlchemyStatus(AlchemistOrder.Resin), Is.EqualTo(AlchemistOrderStatus.Accepted));
        }

        [Test]
        public void AlchemistTrialUsesNormalQuestRulesWithGuaranteedBud()
        {
            var camp = CampWithFunds();
            var modules = PrototypeContent.Modules();
            var level = new RiftLevelSettings(11, 1, 1, 2, 1, 3, 60);
            var location = new LocationDefinition(31, modules, new[] { level });
            var session = new GameSession(83, camp, modules, PrototypeContent.ItemBaseIds(), location: location);
            session.StartAlchemyBudTrial(location, 83);
            Assert.That(session.IsDeveloperRun, Is.False);
            Assert.That(session.Run.Sim.Entities.Kind[1], Is.EqualTo(EnemyKind.ForestBud));
            Assert.That(session.Run.ForestBudShowcaseCount, Is.EqualTo(1));
            Assert.That(session.AlchemyCleanLevelInProgress, Is.True);
        }

        [Test]
        public void VersionSevenLoadsWithOriginalFourAndClosedRecipes()
        {
            var camp = CampWithFunds();
            camp.BuyPotion(PotionKind.LargeHealth);
            camp.SelectPotion(PotionKind.LargeHealth);
            var current = CampSaveCodec.Encode(camp);
            int atlasBytes = 4 + 4 * camp.DiscoveredCount;
            int potionStart = current.Length - 4 - 3 - atlasBytes - 26;
            var old = new byte[current.Length - 12];
            Array.Copy(current, 0, old, 0, potionStart + 16);
            old[potionStart + 16] = camp.PotionSelection;
            Array.Copy(current, potionStart + 26, old, potionStart + 17, atlasBytes);
            BitConverter.GetBytes(7).CopyTo(old, 4);
            uint hash = 2166136261;
            for (int i = 0; i < old.Length - 4; i++) { hash ^= old[i]; hash = unchecked(hash * 16777619); }
            BitConverter.GetBytes(hash).CopyTo(old, old.Length - 4);
            var restored = CampSaveCodec.Decode(old, camp.Items);
            Assert.That(restored.PotionCount(PotionKind.LargeHealth), Is.EqualTo(1));
            Assert.That(restored.SelectedPotion(0), Is.EqualTo(PotionKind.LargeHealth));
            Assert.That(restored.PotionCount(PotionKind.LivingResin), Is.Zero);
            Assert.That(restored.HasMetAlchemist, Is.False);
            Assert.That(restored.AlchemyStatus(AlchemistOrder.Resin), Is.EqualTo(AlchemistOrderStatus.Hidden));
        }

        [Test]
        public void BuffsClearOnDeathAndSceneChangeWhileIdleFramesAreTheOnlyClock()
        {
            var sim = new Simulation(91);
            sim.SetupTestArena(1);
            sim.ApplyResinPotion(); sim.ApplySurgePotion();
            int time = sim.ResinTicksLeft;
            Assert.That(sim.ResinTicksLeft, Is.EqualTo(time));
            sim.ApplyAbilityDamage(1, 0, 10000, -1, DamageType.Physical);
            Assert.That(sim.Entities.Alive[0], Is.False);
            Assert.That(sim.ResinTicksLeft, Is.Zero);
            Assert.That(sim.SurgeTicksLeft, Is.Zero);
            sim.SetupCamp(FixVec2.Zero, null);
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Zero));
            sim.ApplySurgePotion();
            sim.SetupTestArena(0);
            Assert.That(sim.SurgeTicksLeft, Is.Zero);
        }
    }
}
