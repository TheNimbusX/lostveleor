using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class AlchemistRecipeTests
    {
        /// <summary>Sandbox стоит на ранге 3: все восемь рецептов Лео открыты без заказов.</summary>
        static Camp CampWithFunds()
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 500);
            return camp;
        }

        /// <summary>
        /// Рецепты Лео (06.10) открывает только ранг лагеря — босс и уровень вместе;
        /// заказов больше нет. Малые бутылки доступны всегда.
        /// </summary>
        [Test]
        public void RecipesFollowCampRankWithoutOrders()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.That(camp.PotionUnlocked(PotionKind.SmallHealth), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.LargeHealth), Is.False);
            camp.DeveloperSetLevel(Camp.ResidentsLevel);
            Assert.That(camp.HasResident(CampResident.Alchemist), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.LargeLavidium), Is.False);
            camp.DeveloperSetLevel(6); camp.DeveloperCreditBoss(0);
            Assert.That(camp.PotionUnlocked(PotionKind.LargeHealth), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.LivingResin), Is.False);
            camp.DeveloperSetLevel(12); camp.DeveloperCreditBoss(1);
            Assert.That(camp.PotionUnlocked(PotionKind.LivingResin), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.LavidiumSurge), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.Mixed), Is.False);
            camp.DeveloperSetLevel(18); camp.DeveloperCreditBoss(2);
            Assert.That(camp.PotionUnlocked(PotionKind.Mixed), Is.True);
            Assert.That(camp.PotionUnlocked(PotionKind.Clear), Is.True);
            var sandbox = CampWithFunds();
            for (int i = 0; i < Camp.PotionKindCount; i++) Assert.That(sandbox.PotionUnlocked((PotionKind)i), Is.True);
        }

        [Test]
        public void EffectsRefreshWithoutStackingAndExpireOnSimulationTicks()
        {
            var camp = CampWithFunds();
            for (int i = 0; i < 2; i++)
            {
                Assert.That(camp.BuyPotion(PotionKind.LivingResin), Is.True);
                Assert.That(camp.BuyPotion(PotionKind.LavidiumSurge), Is.True);
            }
            var sim = new Simulation(71); sim.SetupTestArena(1);
            sim.Entities.NextAttackTick[1] = int.MaxValue;
            var baseMoveSpeed = sim.Entities.Stats[0].Get(StatType.MoveSpeed);
            sim.Entities.Health[0] = 1;
            sim.Entities.Lavidium[0] = Fix64.Zero;
            Assert.That(camp.ConsumePotion(PotionKind.LivingResin, sim), Is.True);
            Assert.That(camp.ConsumePotion(PotionKind.LavidiumSurge, sim), Is.True);
            sim.Step(InputFrame.Empty);
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
            for (int i = 0; i < 30; i++) sim.Step(InputFrame.Empty);
            Assert.That(camp.ConsumePotion(PotionKind.LavidiumSurge, sim), Is.True);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Ratio(20, 100)));
            Assert.That(sim.SurgeTicksLeft, Is.GreaterThan(170));
            for (int i = 0; i < Simulation.PotionEffectTicks; i++) sim.Step(InputFrame.Empty);
            Assert.That(sim.SurgeTicksLeft, Is.Zero);
            Assert.That(sim.Entities.Stats[0].Get(StatType.AbilitySpeed), Is.EqualTo(Fix64.Zero));
            Assert.That(camp.PotionCount(PotionKind.LavidiumSurge), Is.Zero);
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
