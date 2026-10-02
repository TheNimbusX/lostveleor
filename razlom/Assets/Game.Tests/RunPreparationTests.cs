using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class RunPreparationTests
    {
        static Camp OpenCamp()
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3);
            camp.DeveloperSetLevel(10);
            for (int resident = 0; resident < 3; resident++) for (int rank = 0; rank < 3; rank++)
                Assert.AreEqual(CampUpgradeResult.Success, camp.TryUpgradeResident((CampResident)resident));
            return camp;
        }
        static GameSession Prepared(CampGift gift)
        {
            var camp = OpenCamp(); var session = new GameSession(1701, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            for (int attempt = 0; attempt < 100; attempt++)
            {
                for (int i = 0; i < camp.GiftOfferCount; i++)
                    if (camp.GiftOfferAt(i) == gift) { Assert.True(session.SetPreparedGift(gift)); return session; }
                camp.RecordRealAttemptEnded(1, 0);
            }
            Assert.Fail("Дар не встретился в предложениях"); return session;
        }
        static void DisableFoes(Simulation sim)
        { for (int i = 1; i < sim.Entities.Count; i++) sim.Entities.Alive[i] = false; }

        [Test] public void OffersAreDistinctAndSurviveCancelSaveAndReloadWithoutFreeReroll()
        {
            var s = Prepared(CampGift.SeaKnot); var camp = s.Camp; Assert.True(s.SetPreparedStarter(3));
            var offers = new[] { camp.GiftOfferAt(0), camp.GiftOfferAt(1), camp.GiftOfferAt(2) };
            Assert.AreNotEqual(offers[0], offers[1]); Assert.AreNotEqual(offers[1], offers[2]); Assert.AreNotEqual(offers[0], offers[2]);
            var bytes = CampSaveCodec.Encode(camp); var restored = CampSaveCodec.Decode(bytes, camp.Items);
            var loaded = new GameSession(9999, restored, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            Assert.AreEqual(3, loaded.CampLoadout.PoolIndexAt(0));
            for (int i = 0; i < 3; i++) Assert.AreEqual(offers[i], restored.GiftOfferAt(i));
            Assert.AreEqual(camp.PreparedGift, restored.PreparedGift);
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(restored));
        }
        [Test] public void ChosenStarterIsAloneHasNoTalentsAndIsFrozenDuringAttempt()
        {
            var s = Prepared(CampGift.DryRation); Assert.True(s.SetPreparedStarter(3)); s.EnterRift();
            Assert.AreEqual(3, s.Run.Loadout.PoolIndexAt(0)); Assert.AreEqual(0, s.Run.Loadout.TalentCount(3));
            for (int i = 1; i < RunLoadout.Slots; i++) Assert.True(s.Run.Loadout.IsEmpty(i));
            Assert.AreEqual(AbilityDefinition.DashId, s.ActiveSim.GetAbility(PelagKit.DashSlot).DefinitionId);
            Assert.False(s.SetPreparedStarter(2)); Assert.False(s.SetPreparedGift(CampGift.LightPack));
            Assert.False(s.SetPreparedPotion(0, PotionKind.LargeHealth));
        }
        [Test] public void StarterUnlocksFollowSmithRanksAndNewProfileHasNoLevelStatGrowth()
        {
            var camp = new Camp(PrototypeContent.Items(), progressive: true); camp.DeveloperSetLevel(10);
            camp.RecordRealAttemptEnded(1, 0); camp.RecordRealAttemptEnded(1, 0);
            Assert.False(camp.StarterSkillUnlocked(1)); camp.TryUpgradeResident(CampResident.Smith);
            Assert.True(camp.StarterSkillUnlocked(1)); Assert.False(camp.StarterSkillUnlocked(3));
            camp.TryUpgradeResident(CampResident.Smith); Assert.True(camp.StarterSkillUnlocked(3)); Assert.False(camp.StarterSkillUnlocked(2));
            camp.TryUpgradeResident(CampResident.Smith); Assert.True(camp.StarterSkillUnlocked(2));
            var low = new GameSession(41, new Camp(PrototypeContent.Items()), PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            var high = new GameSession(41, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            Assert.AreEqual(low.CampSim.Entities.MaxHealth[0], high.CampSim.Entities.MaxHealth[0]);
            Assert.AreEqual(low.CampSim.Entities.Damage[0], high.CampSim.Entities.Damage[0]);
        }
        [Test] public void SpareFlaskNeedsStockAndOnlyFirstSuccessfulUseIsFree()
        {
            var s = Prepared(CampGift.SpareFlask); s.EnterRift(); DisableFoes(s.ActiveSim); s.ActiveSim.Entities.Health[0] = 1;
            s.Step(new InputFrame { PotionSlotMask = 1 }); Assert.False(s.Run.SpareFlaskUsed);
            s.Camp.GrantPotions(PotionKind.SmallHealth, 3); s.Step(new InputFrame { PotionSlotMask = 1 });
            Assert.True(s.Run.SpareFlaskUsed); Assert.AreEqual(3, s.Camp.PotionCount(PotionKind.SmallHealth));
            while (s.PotionCooldownTicksLeft > 0) s.Step(InputFrame.Empty);
            s.ActiveSim.Entities.Health[0] = 1; s.Step(new InputFrame { PotionSlotMask = 1 });
            Assert.AreEqual(2, s.Camp.PotionCount(PotionKind.SmallHealth));
        }
        [Test] public void MixedUsesOneBottleRestoresBothBarsAndEightRecipesUseTwoSlotActions()
        {
            var s = Prepared(CampGift.DryRation); Assert.True(s.SetPreparedPotion(0, PotionKind.Mixed));
            Assert.True(s.SetPreparedPotion(1, PotionKind.Clear)); Assert.False(s.SetPreparedPotion(1, PotionKind.Mixed));
            s.Camp.GrantPotions(PotionKind.Mixed, 2); s.EnterRift(); DisableFoes(s.ActiveSim);
            var e = s.ActiveSim.Entities; e.MaxHealth[0] = 200; e.Health[0] = 1; e.MaxLavidium[0] = 100; e.Lavidium[0] = Fix64.Zero;
            s.Step(new InputFrame { PotionSlotMask = 1 }); Assert.AreEqual(37, e.Health[0]);
            Assert.True(e.Lavidium[0] >= Fix64.FromInt(18)); Assert.AreEqual(1, s.Camp.PotionCount(PotionKind.Mixed));
            Assert.AreEqual(0, Camp.PotionInputBit(PotionKind.Mixed)); Assert.AreEqual(0, Camp.PotionInputBit(PotionKind.Clear));
            var frame = InputFrame.Empty; ulong baseline = Hashing.Offset, chosen = baseline; frame.HashInto(ref baseline);
            frame.PotionSlotMask = 1; frame.HashInto(ref chosen); Assert.AreNotEqual(baseline, chosen);
        }
        [Test] public void ClearRemovesRootsAndSlowProtectsForTwoSecondsAndDoesNotRemoveStun()
        {
            var sim = new Simulation(411); sim.SetupTestArena(0);
            sim.ApplyHeroSlow(40, 100); Assert.True(sim.ApplyHeroRoot(90)); sim.ApplyClearPotion();
            Assert.False(sim.HeroRooted); Assert.AreEqual(0, sim.HeroSlowTicksLeft); Assert.False(sim.ApplyHeroRoot(30));
            sim.ApplyHeroSlow(50, 60); Assert.AreEqual(0, sim.HeroSlowTicksLeft);
            for (int i = 0; i < 60; i++) sim.Step(InputFrame.Empty);
            Assert.True(sim.ApplyHeroRoot(30));
            var stunned = new Simulation(412); stunned.SetupTestArena(0); Assert.True(stunned.ApplyHeroStun(30));
            stunned.ApplyClearPotion(); Assert.True(stunned.HeroStunned);
        }
        [Test] public void WhetstoneExpiresAfterThirdArenaWhileDryRationIsAChoiceWithCost()
        {
            var sim = new Simulation(413); sim.SetupTestArena(0);
            var damage = sim.Entities.Stats[0].Get(StatType.Damage); var health = sim.Entities.Stats[0].Get(StatType.MaxHealth);
            var move = sim.Entities.Stats[0].Get(StatType.MoveSpeed);
            sim.ApplyPreparedGift(CampGift.EniWhetstone, 3); Assert.True(sim.Entities.Stats[0].Get(StatType.Damage) > damage);
            sim.ApplyPreparedGift(CampGift.EniWhetstone, 4); Assert.AreEqual(damage, sim.Entities.Stats[0].Get(StatType.Damage));
            sim.ApplyPreparedGift(CampGift.DryRation, 4); Assert.True(sim.Entities.Stats[0].Get(StatType.MaxHealth) > health);
            Assert.True(sim.Entities.Stats[0].Get(StatType.MoveSpeed) < move);
        }
        [Test] public void BackupPlanRerollsOnlyOnceAndDoesNotProduceCurrencyOrTakeAReward()
        {
            var s = Prepared(CampGift.BackupPlan); s.EnterRift(); DisableFoes(s.ActiveSim);
            s.Step(InputFrame.Empty); s.ActiveSim.Entities.Position[0] = s.Run.Map.ExitPoint(0); s.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, s.Run.Phase); Assert.True(s.Run.CanRerollReward);
            int gold = s.Run.Gold, taken = s.Run.TakenRewardCount;
            s.Step(new InputFrame { Command = (byte)RunCommand.RerollReward });
            Assert.False(s.Run.CanRerollReward); Assert.AreEqual(gold, s.Run.Gold); Assert.AreEqual(taken, s.Run.TakenRewardCount);
            ulong hash = s.Run.Hash(); s.Step(new InputFrame { Command = (byte)RunCommand.RerollReward }); Assert.AreEqual(hash, s.Run.Hash());
        }
        [Test] public void DefaultUnpreparedRunKeepsSimulationAndLootSequence()
        {
            var a = new RiftRun(new Simulation(555), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            var b = new RiftRun(new Simulation(555), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            var preparation = RunPreparation.Default; b.SetPreparation(in preparation); a.StartRun(); b.StartRun();
            Assert.AreEqual(a.Hash(), b.Hash());
            for (int i = 0; i < 30; i++) { a.Step(InputFrame.Empty); b.Step(InputFrame.Empty); Assert.AreEqual(a.Hash(), b.Hash()); }
        }
        [Test] public void FinishedRealAttemptCountsOnceWhileDeveloperAndMenuAbortDoNot()
        {
            var camp = new Camp(PrototypeContent.Items(), progressive: true);
            var s = new GameSession(556, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            s.EnterRift(); s.ReturnToCamp(); Assert.AreEqual(0, camp.AttemptCount);
            s.EnterRift(); s.ActiveSim.Entities.Health[0] = 0; s.ActiveSim.Entities.Alive[0] = false; s.Step(InputFrame.Empty);
            Assert.AreEqual(1, camp.AttemptCount); s.Step(InputFrame.Empty); Assert.AreEqual(1, camp.AttemptCount);
        }
        [TestCase(false)] [TestCase(true)] public void RepeatedDummyDeathsNeverGiveCampExperienceOrUpgradePoints(bool legacyGround)
        {
            var s = PrototypeContent.NewSession(847);
            if (legacyGround) s.EnterProvingGround(1);
            else s.ConfigureCampTraining(new[] { new CampDummyDefinition(new FixVec2(Fix64.One, Fix64.Zero), 1, Fix64.Zero, Fix64.Zero) });
            var sim = s.ActiveSim;
            for (int repetition = 0; repetition < 12; repetition++)
            {
                Assert.True(sim.Entities.Alive[1]);
                sim.Statuses.ApplyBurn(1, Fix64.FromInt(100000), 1, 0, -1); s.Step(InputFrame.Empty);
                Assert.True(sim.Entities.Alive[1], "Мишень должна восстановиться после настоящей смерти");
            }
            Assert.AreEqual(1, s.Camp.Level); Assert.AreEqual(0, s.Camp.Experience); Assert.AreEqual(0, s.Camp.AvailableCampPoints);
        }
        [Test] public void DeveloperTrialClearsPendingEntryAndCannotSpendPotionsOrChangeCampProgress()
        {
            var s = Prepared(CampGift.DryRation); s.Camp.GrantPotions(PotionKind.SmallHealth, 1);
            s.RequestRiftEntry(); Assert.True(s.PreparationRequested);
            ulong campHash = 0; s.Camp.HashInto(ref campHash);
            s.StartForestBudTest(null, 848); Assert.False(s.PreparationRequested); Assert.True(s.IsDeveloperRun);
            var sim = s.ActiveSim; sim.Entities.Health[0] = 1;
            for (int id = 1; id < sim.Entities.Count; id++) sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, 0, -1);
            s.Step(new InputFrame { PotionSlotMask = 1 });
            Assert.AreEqual(1, sim.Entities.Health[0]);
            sim.Entities.Alive[0] = false; sim.Entities.Health[0] = 0; s.Step(InputFrame.Empty);
            ulong after = 0; s.Camp.HashInto(ref after); Assert.AreEqual(campHash, after);
        }
        [Test] public void NewProfileStartsWithThreeSmallPotionsWithoutAnAlchemistAndReloadDoesNotRefill()
        {
            var s = PrototypeContent.NewSession(844); var camp = s.Camp;
            Assert.False(camp.HasResident(CampResident.Alchemist));
            Assert.AreEqual(PrototypeContent.StartingPotionStockPerKind, camp.PotionCount(PotionKind.SmallHealth));
            Assert.AreEqual(PrototypeContent.StartingPotionStockPerKind, camp.PotionCount(PotionKind.SmallLavidium));
            Assert.AreEqual(PotionPurchaseResult.AlchemistUnavailable, camp.TryBuyPotion(PotionKind.SmallHealth));
            s.EnterRift(); DisableFoes(s.ActiveSim); s.ActiveSim.Entities.Health[0] = 1;
            int heal = s.ActiveSim.Entities.MaxHealth[0] / 10;
            s.Step(new InputFrame { PotionSlotMask = 1 });
            Assert.AreEqual(1 + heal, s.ActiveSim.Entities.Health[0]);
            Assert.AreEqual(PrototypeContent.StartingPotionStockPerKind - 1, camp.PotionCount(PotionKind.SmallHealth));
            var restored = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.False(restored.HasResident(CampResident.Alchemist));
            Assert.AreEqual(camp.PotionCount(PotionKind.SmallHealth), restored.PotionCount(PotionKind.SmallHealth));
        }
        [Test] public void EntryAndRepeatCommandsRequestPreparationAfterSecondEndedAttempt()
        {
            var s = PrototypeContent.NewSession(845);
            s.Step(new InputFrame { Command = (byte)CampCommand.EnterRift });
            Assert.AreEqual(GameMode.Rift, s.Mode);
            s.ActiveSim.Entities.Alive[0] = false; s.ActiveSim.Entities.Health[0] = 0; s.Step(InputFrame.Empty);
            s.Step(new InputFrame { Command = (byte)CampCommand.RepeatRift });
            Assert.AreEqual(GameMode.Rift, s.Mode, "Первый повтор ещё не требует стола");
            s.ActiveSim.Entities.Alive[0] = false; s.ActiveSim.Entities.Health[0] = 0; s.Step(InputFrame.Empty);
            Assert.AreEqual(2, s.Camp.AttemptCount); int runs = s.RunNumber;
            s.Step(new InputFrame { Command = (byte)CampCommand.RepeatRift });
            Assert.AreEqual(GameMode.Camp, s.Mode); Assert.True(s.PreparationRequested); Assert.IsNull(s.Run);
            s.Step(new InputFrame { Command = (byte)CampCommand.EnterRift });
            Assert.AreEqual(runs, s.RunNumber, "Повтор команды не подтверждает подготовку");
            int generation = s.Generation;
            s.CancelRiftEntryRequest(); Assert.False(s.PreparationRequested);
            Assert.AreEqual(GameMode.Camp, s.Mode); Assert.AreEqual(generation, s.Generation);
            s.RequestRiftEntry(); Assert.True(s.PreparationRequested);
            Assert.True(s.SetPreparedGift(s.Camp.GiftOfferAt(0))); s.EnterRift();
            Assert.AreEqual(GameMode.Rift, s.Mode); Assert.False(s.PreparationRequested); Assert.AreEqual(runs + 1, s.RunNumber);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void StaticEliteEncounterPaysOneSteelOnlyAfterAllSplitChildrenAndSuccessfulReturn(int finish)
        {
            var normal = new EncounterPack(1, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 1) });
            var elite = new EncounterPack(2, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2, elite: true),
                new EncounterGroup(EnemyKind.ForestSplitter, 1, 1) });
            var settings = new EncounterSettings(new[] { normal }, new[] { normal }, new[] { normal }, new[] { elite }, 1, 0, 100, Fix64.FromInt(5));
            var modules = PrototypeContent.Modules();
            var location = new LocationDefinition(StableId.Of("location.test-camp-materials"), modules,
                new[] { new RiftLevelSettings(12, 1, 0, 0, 0, 0, 100, settings) });
            var camp = new Camp(PrototypeContent.Items(), act: 3);
            var s = new GameSession(846, camp, modules, PrototypeContent.ItemBaseIds(), location: location);
            s.EnterRift(); var sim = s.ActiveSim; var plan = s.Run.Encounters;
            EncounterPlacement encounter = default; bool found = false;
            for (int i = 0; i < plan.Count; i++) if (plan.Get(i).Role == EncounterRole.ExitGuard) { encounter = plan.Get(i); found = true; }
            Assert.True(found); Assert.AreEqual(3, encounter.EnemyCount);
            int end = encounter.FirstEntity + encounter.EnemyCount;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                sim.Entities.NextAttackTick[id] = int.MaxValue;
                if (id >= encounter.FirstEntity && id < end) sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, 0, -1);
                else sim.Entities.Alive[id] = false;
            }
            s.Step(InputFrame.Empty); Assert.True(sim.HasPendingSplits);
            int deadline = sim.Tick + Simulation.SplitterDeathReleaseTicks + 1;
            while (sim.HasPendingSplits && sim.Tick <= deadline) s.Step(InputFrame.Empty);
            Assert.False(sim.HasPendingSplits); int children = 0;
            for (int id = end; id < sim.Entities.Count; id++) if (sim.SplitParentOf(id) >= encounter.FirstEntity && sim.Entities.Alive[id])
            {
                children++;
                if (finish != 0) sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, 0, -1);
            }
            Assert.AreEqual(2, children);
            if (finish != 0) s.Step(InputFrame.Empty);
            if (finish == 2) { sim.Entities.Alive[0] = false; sim.Entities.Health[0] = 0; s.Step(InputFrame.Empty); }
            else if (finish == 3) s.ReturnToCamp();
            else s.Step(new InputFrame { Command = (byte)RunCommand.Leave });
            Assert.AreEqual(finish == 1 ? 1 : 0, camp.MaterialCount(ForgeMaterial.Steel));
        }
        static Simulation RollArena(CampGift gift)
        {
            var sim = new Simulation(733, 32); sim.SetupTestArena(0); new RunLoadout().ApplyTo(sim);
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Zero); sim.Entities.RefreshStats(0);
            sim.ApplyPreparedGift(gift, 1);
            var roll = InputFrame.Empty; roll.AbilityMask = (byte)(1 << PelagKit.DashSlot);
            roll.Aim = new FixVec2(Fix64.FromInt(3), Fix64.Zero); sim.Step(in roll);
            int contact = sim.PlayerAction.ContactTick;
            Assert.AreEqual(AbilityDefinition.DashId, sim.PlayerAction.DefinitionId);
            while (sim.Tick <= contact) sim.Step(InputFrame.Empty);
            while (sim.Tick < sim.Entities.NextAttackTick[0]) sim.Step(InputFrame.Empty);
            return sim;
        }
        static int[] AttackPair(Simulation sim)
        {
            for (int i = 0; i < 2; i++)
            {
                int id = sim.Entities.Spawn(sim.Entities.Position[0] + new FixVec2(Fix64.One, Fix64.Ratio(i * 3, 10)), 10000, Faction.Orvill);
                sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero); sim.Entities.RefreshStats(id);
                sim.Entities.NextAttackTick[id] = int.MaxValue;
            }
            sim.Entities.Facing[0] = new FixVec2(Fix64.One, Fix64.Zero);
            int[] damage = new int[3]; var input = InputFrame.Empty;
            input.AttackTarget = 1; input.Flags = (byte)InputFlags.Attack; input.Aim = sim.Entities.Position[1];
            int previousSerial = sim.PlayerAction.Serial;
            sim.Step(in input);
            Assert.AreEqual(previousSerial + 1, sim.PlayerAction.Serial, "Обычная атака должна действительно начаться");
            for (int tick = 0; tick < Simulation.SabreBaseContactTicks(0) + 3; tick++)
            {
                foreach (var e in sim.Events) if (e.Type == SimEventType.Damage && e.Source == 0 && e.Target < 3) damage[e.Target] += e.Amount;
                sim.Step(InputFrame.Empty);
            }
            return damage;
        }
        [Test] public void SeaKnotBoostsEveryTargetOfNextAttackStartedAfterCompletedRoll()
        {
            var plain = RollArena(CampGift.None); var knot = RollArena(CampGift.SeaKnot);
            // Удар серии сабли бьёт всех в секторе — дар должен лечь на каждую цель.
            var baseline = AttackPair(plain); var gifted = AttackPair(knot);
            Assert.Greater(baseline[1], 0); Assert.Greater(baseline[2], 0);
            for (int target = 1; target < 3; target++)
                Assert.AreEqual(CombatStats.RoundToInt(Fix64.FromInt(baseline[target]) * Fix64.Ratio(125, 100)), gifted[target]);
        }
        [Test] public void SeaKnotMissConsumesWindowInsteadOfSavingItForNextHit()
        {
            var a = RollArena(CampGift.None); var b = RollArena(CampGift.SeaKnot);
            var miss = InputFrame.Empty; miss.Flags = (byte)InputFlags.Attack;
            a.Step(in miss); b.Step(in miss);
            bool startedMiss = false;
            foreach (var e in b.Events) if (e.Type == SimEventType.Attack && e.Source == 0 && e.Target < 0) startedMiss = true;
            Assert.True(startedMiss, "Пустой взмах должен начаться и потратить окно дара");
            int ready = a.Entities.NextAttackTick[0]; while (a.Tick < ready) { a.Step(InputFrame.Empty); b.Step(InputFrame.Empty); }
            CollectionAssert.AreEqual(AttackPair(a), AttackPair(b));
        }
        [Test] public void SeaKnotWindowExpiresTwoSecondsAfterRoll()
        {
            var a = RollArena(CampGift.None); var b = RollArena(CampGift.SeaKnot);
            for (int i = 0; i < 2 * Simulation.TicksPerSecond + 1; i++) { a.Step(InputFrame.Empty); b.Step(InputFrame.Empty); }
            CollectionAssert.AreEqual(AttackPair(a), AttackPair(b));
        }
        [Test] public void SharedPotionCooldownSurvivesArenaTransitionAndQueriesDoNotAdvanceIt()
        {
            var s = Prepared(CampGift.DryRation); s.Camp.GrantPotions(PotionKind.SmallHealth, 3);
            s.EnterRift(); DisableFoes(s.ActiveSim); s.ActiveSim.Entities.Health[0] = 1;
            s.Step(new InputFrame { PotionSlotMask = 1 }); s.ActiveSim.Entities.Position[0] = s.Run.Map.ExitPoint(0); s.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, s.Run.Phase); int before = s.PotionCooldownTicksLeft;
            for (int i = 0; i < 30; i++) s.CanUsePotionSlot(0);
            Assert.AreEqual(before, s.PotionCooldownTicksLeft);
            s.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward1 });
            Assert.AreEqual(2, s.Run.Depth); Assert.AreEqual(before, s.PotionCooldownTicksLeft);
        }
    }
}
