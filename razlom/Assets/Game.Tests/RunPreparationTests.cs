using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class RunPreparationTests
    {
        /// <summary>Sandbox стоит на ранге 3 (06.10): все жители, дары, навыки и зелья открыты.</summary>
        static Camp OpenCamp()
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3);
            Assert.AreEqual(3, camp.CampRank);
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

        static int[] SkillOffers(Camp camp)
        { var offers = new int[3]; for (int i = 0; i < 3; i++) offers[i] = camp.SkillOfferAt(i); return offers; }
        static CarryChoice[] CarryOffers(Camp camp)
        { var offers = new CarryChoice[3]; for (int i = 0; i < 3; i++) offers[i] = camp.CarryOfferAt(i); return offers; }
        static bool Among(int[] offers, int pool) => System.Array.IndexOf(offers, pool) >= 0;

        /// <summary>
        /// Навык стола (06.10): 1 из 3 случайных среди когда-либо взятых, меньше трёх — все по
        /// порядку пула. Ранги Эни навыков не открывают; снятые из наград не предлагаются.
        /// Набор попытки не перебрасывается, пока она не закончилась.
        /// </summary>
        [Test] public void SkillOffersComeFromEverTakenAndShowAllWhenFewerThanThree()
        {
            int whirlwind = PelagKit.PoolIndexOf(AbilityDefinition.WhirlwindId);
            var camp = new Camp(PrototypeContent.Items(), progressive: true); camp.DeveloperSetLevel(18);
            for (int boss = 0; boss < RunBossKeys.Count; boss++) camp.DeveloperCreditBoss(boss);
            Assert.AreEqual(0, camp.SkillOfferCount, "без стола предложений нет");
            Assert.AreEqual(AbilityDefinition.WhirlwindId, camp.PreparedStarterId);
            camp.RecordRealAttemptEnded(1, 0); Assert.AreEqual(3, camp.Rank(CampResident.Smith));
            Assert.AreEqual(1, camp.SkillOfferCount, "взятых нет — один Вихрь, ранг навыков не даёт");
            Assert.AreEqual(whirlwind, camp.SkillOfferAt(0)); Assert.AreEqual(-1, camp.SkillOfferAt(1));
            Assert.False(camp.SelectStarterSkill(1));

            camp.RecordSkillTaken(PelagKit.PoolDefinition(3).Id); camp.RecordSkillTaken(PelagKit.PoolDefinition(1).Id);
            Assert.False(PelagKit.InRewardPool(10)); camp.RecordSkillTaken(PelagKit.PoolDefinition(10).Id);
            Assert.AreEqual(1, camp.SkillOfferCount, "набор попытки не перебрасывается");
            camp.RecordRealAttemptEnded(1, 0);
            CollectionAssert.AreEqual(new[] { 1, 3, -1 }, SkillOffers(camp), "меньше трёх — все, по порядку пула");
            Assert.AreEqual(PelagKit.PoolDefinition(1).Id, camp.PreparedStarterId, "Вихря нет среди новых — первое предложение");
            Assert.False(camp.SelectStarterSkill(10)); Assert.False(camp.SelectStarterSkill(whirlwind)); Assert.False(camp.SelectStarterSkill(-1));
            Assert.True(camp.SelectStarterSkill(3)); Assert.AreEqual(PelagKit.PoolDefinition(3).Id, camp.CreateRunPreparation().StarterId);

            int[] taken = { 0, 1, 3, 5, 7 };
            foreach (int pool in taken) camp.RecordSkillTaken(PelagKit.PoolDefinition(pool).Id);
            var seen = new bool[PelagKit.PoolSize];
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int previous = camp.PreparedStarterPoolIndex;
                camp.RecordRealAttemptEnded(1, 0);
                var offers = SkillOffers(camp);
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(Among(taken, offers[i]), "только когда-либо взятые и идущие в награды");
                    for (int j = 0; j < i; j++) Assert.AreNotEqual(offers[j], offers[i]);
                    seen[offers[i]] = true;
                }
                Assert.True(Among(offers, camp.PreparedStarterPoolIndex));
                if (Among(offers, previous)) Assert.AreEqual(previous, camp.PreparedStarterPoolIndex, "прежний выбор остаётся, если предложен");
                Assert.True(camp.SelectStarterSkill(offers[2]));
            }
            foreach (int pool in taken) Assert.True(seen[pool], "за попытки встречается каждый взятый");

            var sandbox = new Camp(PrototypeContent.Items());
            Assert.AreEqual(3, sandbox.SkillOfferCount, "Sandbox открывает весь пул наград");
            for (int i = 0; i < 3; i++) Assert.True(PelagKit.InRewardPool(sandbox.SkillOfferAt(i)));
        }

        [Test] public void OffersAreDistinctAndSurviveCancelSaveAndReloadWithoutFreeReroll()
        {
            var camp = PrototypeContent.NewCamp();
            foreach (int pool in new[] { 0, 1, 2, 3, 5 }) camp.RecordSkillTaken(PelagKit.PoolDefinition(pool).Id);
            camp.RecordRealAttemptEnded(1, 0); camp.DeveloperCreditBoss(0);
            camp.OpenArtifact(RunArtifacts.At(1)); camp.OpenArtifact(RunArtifacts.At(4));
            camp.RecordRealAttemptEnded(1, 0);
            var s = new GameSession(1701, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            var skills = SkillOffers(camp); var carry = CarryOffers(camp);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < i; j++) { Assert.AreNotEqual(skills[j], skills[i]); Assert.False(carry[j].SameAs(carry[i])); }
            Assert.True(s.SetPreparedStarter(skills[2])); Assert.True(s.SetPreparedCarry(carry[1]));

            s.RequestRiftEntry(); Assert.True(s.PreparationRequested); s.CancelRiftEntryRequest();
            CollectionAssert.AreEqual(skills, SkillOffers(camp), "отмена не перебрасывает");
            var bytes = CampSaveCodec.Encode(camp); var restored = CampSaveCodec.Decode(bytes, camp.Items);
            // Другой сид сессии: сид стола уже лежит в файле, набор тот же.
            var loaded = new GameSession(9999, restored, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            Assert.AreEqual(skills[2], loaded.CampLoadout.PoolIndexAt(0));
            CollectionAssert.AreEqual(skills, SkillOffers(restored));
            for (int i = 0; i < 3; i++) Assert.True(carry[i].SameAs(restored.CarryOfferAt(i)));
            Assert.True(camp.PreparedCarry.SameAs(restored.PreparedCarry)); Assert.AreEqual(camp.PreparedStarterId, restored.PreparedStarterId);
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(restored));
            ulong a = 0, b = 0; camp.HashInto(ref a); restored.HashInto(ref b); Assert.AreEqual(a, b);

            // Сохранение посреди забега: навык и артефакт уже взяты, попытка ещё не кончилась.
            restored.RecordSkillTaken(PelagKit.PoolDefinition(7).Id); restored.OpenArtifact(RunArtifacts.At(6));
            var midRun = CampSaveCodec.Decode(CampSaveCodec.Encode(restored), camp.Items);
            CollectionAssert.AreEqual(skills, SkillOffers(midRun));
            for (int i = 0; i < 3; i++) Assert.True(carry[i].SameAs(midRun.CarryOfferAt(i)));

            // Файл дня 1 (секция 8 без хвоста T3): до босса дары выходят те же — тот же поток.
            var early = PrototypeContent.NewCamp(); early.RecordRealAttemptEnded(1, 0); early.RecordRealAttemptEnded(1, 0);
            new GameSession(1703, early, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            Assert.True(early.SelectCarry(early.CarryOfferAt(1)));
            var earlyBytes = CampSaveCodec.Encode(early);
            var section = CampSaveFile.Payload(earlyBytes, CampSaveFile.PreparationTag);
            var head = new byte[4 + 1 + 8 + 1 + 4 + 3]; System.Array.Copy(section, head, head.Length);
            var dayOne = CampSaveCodec.Decode(CampSaveFile.Replace(earlyBytes, CampSaveFile.PreparationTag, head), early.Items);
            for (int i = 0; i < 3; i++) Assert.True(early.CarryOfferAt(i).SameAs(dayOne.CarryOfferAt(i)));
            Assert.True(dayOne.PreparedCarry.SameAs(dayOne.CarryOfferAt(0)), "выбор дня 1 переносится на первое предложение");
        }
        [Test] public void ChosenStarterIsAloneHasNoTalentsAndIsFrozenDuringAttempt()
        {
            var s = Prepared(CampGift.DryRation); int pool = s.Camp.SkillOfferAt(1);
            Assert.True(s.SetPreparedStarter(pool)); s.EnterRift();
            Assert.AreEqual(pool, s.Run.Loadout.PoolIndexAt(0)); Assert.AreEqual(0, s.Run.Loadout.TalentCount(pool));
            for (int i = 1; i < RunLoadout.Slots; i++) Assert.True(s.Run.Loadout.IsEmpty(i));
            Assert.AreEqual(AbilityDefinition.DashId, s.ActiveSim.GetAbility(PelagKit.DashSlot).DefinitionId);
            Assert.False(s.SetPreparedStarter(s.Camp.SkillOfferAt(0))); Assert.False(s.SetPreparedGift(CampGift.LightPack));
            Assert.False(s.SetPreparedCarry(s.Camp.CarryOfferAt(0)));
            Assert.False(s.SetPreparedPotion(0, PotionKind.LargeHealth));
        }
        /// <summary>
        /// Ячейка «с собой» (06.10): до первого босса — 1 из 3 даров, даже если тайник уже открыл
        /// артефакт; после — 1 из 3 среди даров и открытых артефактов. Победа посреди попытки
        /// набор не перебрасывает. В снимке забега — либо дар, либо артефакт.
        /// </summary>
        [Test] public void CarryIsGiftBeforeFirstBossThenGiftsOrArtifacts()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.AreEqual(0, camp.CarryOfferCount); Assert.False(camp.SelectGift(CampGift.DryRation), "до стола ячейки нет");
            camp.OpenArtifact(RunArtifacts.At(0)); camp.OpenArtifact(RunArtifacts.At(3));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                camp.RecordRealAttemptEnded(1, 0);
                Assert.AreEqual(3, camp.CarryOfferCount);
                for (int i = 0; i < 3; i++)
                {
                    var offer = camp.CarryOfferAt(i);
                    Assert.AreEqual(CarryKind.Gift, offer.Kind);
                    Assert.True(offer.Gift == CampGift.DryRation || offer.Gift == CampGift.EniWhetstone || offer.Gift == CampGift.LightPack,
                        "до ранга 1 — три базовых дара");
                }
                Assert.AreEqual(CarryKind.None, camp.PreparedCarry.Kind, "без выбора ячейка пуста");
            }
            Assert.False(camp.SelectCarry(CarryChoice.Of(RunArtifacts.At(0))));
            var gift = camp.CarryOfferAt(2); Assert.True(camp.SelectCarry(gift));
            Assert.AreEqual(gift.Gift, camp.CreateRunPreparation().Gift); Assert.AreEqual(RunArtifact.None, camp.CreateRunPreparation().Carried);

            var before = CarryOffers(camp); camp.DeveloperCreditBoss(0);
            for (int i = 0; i < 3; i++) Assert.True(before[i].SameAs(camp.CarryOfferAt(i)), "победа посреди попытки не перебрасывает");

            bool artifactTaken = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                camp.RecordRealAttemptEnded(1, 0);
                Assert.True(camp.PreparedCarry.SameAs(camp.CarryOfferAt(0)), "прежний выбор переносится на первое предложение");
                var offers = CarryOffers(camp);
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < i; j++) Assert.False(offers[j].SameAs(offers[i]));
                    if (offers[i].Kind == CarryKind.Gift) { Assert.True(camp.GiftUnlocked(offers[i].Gift)); continue; }
                    Assert.AreEqual(CarryKind.Artifact, offers[i].Kind);
                    Assert.True(offers[i].Artifact == RunArtifacts.At(0) || offers[i].Artifact == RunArtifacts.At(3), "только открытые");
                    if (artifactTaken) continue;
                    artifactTaken = true; Assert.True(camp.SelectCarry(offers[i]));
                    var preparation = camp.CreateRunPreparation();
                    Assert.AreEqual(offers[i].Artifact, preparation.Carried); Assert.AreEqual(CampGift.None, preparation.Gift);
                    Assert.AreEqual(CampGift.None, camp.PreparedGift);
                }
            }
            Assert.True(artifactTaken);
            Assert.False(camp.SelectCarry(CarryChoice.Of(RunArtifacts.At(5))), "закрытый артефакт не берётся");
        }
        /// <summary>
        /// Артефакт «с собой» стоит в слоте с первого тика этого забега, наградой не считается и
        /// в лагерь не возвращается: следующий забег с даром идёт без него.
        /// </summary>
        [Test] public void CarriedArtifactActsThisRunOnly()
        {
            var camp = PrototypeContent.NewCamp(); camp.RecordRealAttemptEnded(1, 0); camp.DeveloperCreditBoss(0);
            camp.OpenArtifact(RunArtifacts.At(2));
            var s = new GameSession(1702, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
            CarryChoice artifact = default;
            for (int attempt = 0; attempt < 40 && artifact.Kind == CarryKind.None; attempt++)
            {
                camp.RecordRealAttemptEnded(1, 0);
                for (int i = 0; i < camp.CarryOfferCount; i++) if (camp.CarryOfferAt(i).Kind == CarryKind.Artifact) artifact = camp.CarryOfferAt(i);
            }
            Assert.AreEqual(RunArtifacts.At(2), artifact.Artifact);
            Assert.True(s.SetPreparedCarry(artifact)); int opened = camp.OpenedArtifactCount;
            s.EnterRift();
            Assert.AreEqual(artifact.Artifact, s.Run.Artifact); Assert.AreEqual(artifact.Artifact, s.ActiveSim.Artifact);
            Assert.AreEqual(artifact.Artifact, s.Run.Preparation.Carried); Assert.AreEqual(CampGift.None, s.Run.Preparation.Gift);
            Assert.AreEqual(0, s.Run.TakenRewardCount, "артефакт «с собой» — не награда забега");
            Assert.False(s.SetPreparedCarry(camp.CarryOfferAt(0)), "в Разломе ячейка не меняется");
            s.ActiveSim.Entities.Health[0] = 0; s.ActiveSim.Entities.Alive[0] = false; s.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Summary, s.Mode); s.ReturnToCamp();
            Assert.AreEqual(opened, camp.OpenedArtifactCount);

            CarryChoice gift = default;
            for (int i = 0; i < camp.CarryOfferCount; i++) if (camp.CarryOfferAt(i).Kind == CarryKind.Gift) gift = camp.CarryOfferAt(i);
            Assert.True(s.SetPreparedCarry(gift)); s.EnterRift();
            Assert.AreEqual(RunArtifact.None, s.Run.Artifact); Assert.AreEqual(RunArtifact.None, s.ActiveSim.Artifact);

            // Уровень RiftRun: артефакт встаёт в StartRun, хеш подготовки его различает, дар и артефакт вместе — ошибка.
            var plain = new RiftRun(new Simulation(557), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            var carried = new RiftRun(new Simulation(557), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            var withArtifact = new RunPreparation(AbilityDefinition.WhirlwindId, CampGift.None, PotionKind.SmallHealth, PotionKind.SmallLavidium, RunArtifacts.At(2));
            carried.SetPreparation(in withArtifact); plain.StartRun(); carried.StartRun();
            Assert.AreEqual(RunArtifacts.At(2), carried.Artifact); Assert.AreEqual(RunArtifact.None, plain.Artifact);
            Assert.AreNotEqual(plain.Hash(), carried.Hash());
            var both = new RunPreparation(AbilityDefinition.WhirlwindId, CampGift.DryRation, PotionKind.SmallHealth, PotionKind.SmallLavidium, RunArtifacts.At(2));
            var invalid = new RunPreparation(AbilityDefinition.WhirlwindId, CampGift.None, PotionKind.SmallHealth, PotionKind.SmallLavidium, (RunArtifact)1);
            var fresh = new RiftRun(new Simulation(558), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            Assert.Throws<System.ArgumentException>(() => fresh.SetPreparation(in both));
            Assert.Throws<System.ArgumentException>(() => fresh.SetPreparation(in invalid));
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
        [TestCase(false)] [TestCase(true)] public void RepeatedDummyDeathsNeverGiveCampExperienceOrAsh(bool legacyGround)
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
            Assert.AreEqual(1, s.Camp.Level); Assert.AreEqual(0, s.Camp.Experience); Assert.AreEqual(0, s.Camp.Money(CurrencyType.Ash));
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
        /// <summary>Стол сборов открывается после первого завершённого забега (06.10), и повтор ведёт к нему.</summary>
        [Test] public void EntryAndRepeatCommandsRequestPreparationAfterFirstEndedAttempt()
        {
            var s = PrototypeContent.NewSession(845);
            s.Step(new InputFrame { Command = (byte)CampCommand.EnterRift });
            Assert.AreEqual(GameMode.Rift, s.Mode, "Первый вход ещё без стола");
            s.ActiveSim.Entities.Alive[0] = false; s.ActiveSim.Entities.Health[0] = 0; s.Step(InputFrame.Empty);
            Assert.AreEqual(1, s.Camp.AttemptCount); Assert.True(s.Camp.HasTravelTable); int runs = s.RunNumber;
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
