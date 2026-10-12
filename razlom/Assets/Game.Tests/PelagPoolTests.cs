using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Пул способностей Пелага и набор на время забега.
    ///
    /// Решение владельца от 15 сентября: веток нет, забег начинается с
    /// автоатаки и Вихря, остальное находится по пути из пула в восемь
    /// способностей. Порядок и потолок усилений — в UpgradeOrderTests
    /// (владелец, 24.09: любое усиление может быть первым).
    /// </summary>
    public class PelagPoolTests
    {
        private static RiftRun NewRun(ulong seed = 42)
        {
            var run = new RiftRun(new Simulation(seed, 1024), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            return run;
        }

        private static Simulation Arena(RunLoadout loadout)
        {
            var sim = new Simulation(1234, 64);
            sim.SetupTestArena(0);
            loadout.ApplyTo(sim);
            return sim;
        }

        private static InputFrame Press(int slot)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            return input;
        }

        // ---- пул ----

        /// <summary>
        /// Пин порядка: индекс пула хранится в состоянии забега и участвует в
        /// роллах карточек, поэтому молчаливая перестановка сдвинула бы сиды.
        /// </summary>
        [Test]
        public void PoolPreservesOldOrderAndAppendsMobility()
        {
            int[] expected =
            {
                AbilityDefinition.WhirlwindId, AbilityDefinition.CleaveId, AbilityDefinition.BlazeId,
                AbilityDefinition.ChainStepId, AbilityDefinition.AnchorSlamId, AbilityDefinition.WreckId,
                AbilityDefinition.AnchorLeapId, AbilityDefinition.FireFlaskId, AbilityDefinition.SkewerId, AbilityDefinition.BackblastId,
                AbilityDefinition.AnchorThrowId,   // 03.10, в конец пула
            };
            Assert.AreEqual(expected.Length, PelagKit.PoolSize);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], PelagKit.PoolDefinition(i).Id, $"пул {i}");
                Assert.AreEqual(i, PelagKit.PoolIndexOf(expected[i]), $"обратный поиск {i}");
            }
        }

        /// <summary>
        /// Убранные индексы: 4 — Удар якорем (влит в Крушение, 03.10), 8 — «На вылет»
        /// (03.10), 2 — «Ладно смазал» и 9 — Отбой (07.10, идеи уйдут в таланты). Номер
        /// держат (PoolDefinition прежний), но наградой не приходят.
        /// </summary>
        [Test]
        public void RetiredPoolIndices_AreNeverOffered()
        {
            for (int i = 0; i < PelagKit.PoolSize; i++)
                // 10 — Бросок якоря (03.10): не убран, но в награды — только по слову владельца (пока F8).
                Assert.AreEqual(i != 2 && i != 4 && i != 8 && i != 9 && i != 10, PelagKit.InRewardPool(i), "пул " + i);
            Assert.AreEqual(AbilityDefinition.BlazeId, PelagKit.PoolDefinition(2).Id, "номер не переиспользован");
            Assert.AreEqual(AbilityDefinition.AnchorSlamId, PelagKit.PoolDefinition(4).Id, "номер не переиспользован");
            Assert.AreEqual(AbilityDefinition.SkewerId, PelagKit.PoolDefinition(8).Id);
            Assert.AreEqual(AbilityDefinition.BackblastId, PelagKit.PoolDefinition(9).Id);

            for (ulong seed = 1; seed <= 12; seed++)
            {
                RiftRun run = FormBaselineScenarios.NewPrototypeRun(seed);
                run.StartRun();
                for (int screen = 0; screen < 6; screen++)
                {
                    if (!FormBaselineScenarios.ClearToReward(run)) break;
                    for (int card = 0; card < RiftRun.RewardChoices; card++)
                    {
                        RewardOffer offer = run.GetOffer(card);
                        if (offer.Kind != RewardKind.Ability) continue;
                        Assert.IsTrue(PelagKit.InRewardPool(offer.PoolIndex), "сид " + seed + ": предложен убранный " + offer.PoolIndex);
                    }
                    FormBaselineScenarios.Choose(run, 0, screen);
                }
            }
        }

        /// <summary>
        /// Набор владельца 07.10 (DESIGN, «Навыки»): Вихрь, Шквал, Абордаж, Крушение,
        /// Взрывная смесь, Бросок якоря, Рассекающий. Награды дают шесть из них; Бросок
        /// якоря — пока только F8, в награды — по слову владельца.
        /// </summary>
        [Test]
        public void RewardPool_IsTheOwnersSevenSkills_AnchorThrowStillF8Only()
        {
            var owners = new[]
            {
                AbilityDefinition.WhirlwindId, AbilityDefinition.ChainStepId, AbilityDefinition.AnchorLeapId,
                AbilityDefinition.WreckId, AbilityDefinition.FireFlaskId, AbilityDefinition.AnchorThrowId,
                AbilityDefinition.CleaveId,
            };
            var offered = new System.Collections.Generic.List<int>();
            for (int i = 0; i < PelagKit.PoolSize; i++)
                if (PelagKit.InRewardPool(i)) offered.Add(PelagKit.PoolDefinition(i).Id);

            Assert.AreEqual(6, offered.Count, "в наградах шесть навыков");
            Assert.IsFalse(PelagKit.InRewardPool(PelagKit.PoolIndexOf(AbilityDefinition.AnchorThrowId)), "Бросок якоря — пока F8");
            offered.Add(AbilityDefinition.AnchorThrowId);
            CollectionAssert.AreEquivalent(owners, offered);
        }

        /// <summary>
        /// 07.10: сотни бросков карточек на аренах и в прототипе — ни «Ладно смазал», ни
        /// Отбоя, ни их усилений; зато каждый навык наград, кроме стартового, выпадает.
        /// </summary>
        [Test]
        public void SeededRewardRolls_NeverOfferBlazeOrBackblast()
        {
            var seen = new bool[PelagKit.PoolSize];
            int screens = 0;
            for (ulong seed = 1; seed <= 40; seed++)
                for (int kind = 0; kind < 2; kind++)
                {
                    RiftRun run = kind == 0 ? FormBaselineScenarios.NewArenaRun(seed) : FormBaselineScenarios.NewPrototypeRun(seed);
                    run.StartRun();
                    for (int depth = 1; depth <= 6; depth++)
                    {
                        if (run.Phase != RunPhase.Clearing || !FormBaselineScenarios.ClearToReward(run)) break;
                        screens++;
                        for (int card = 0; card < RiftRun.RewardChoices; card++)
                        {
                            RewardOffer offer = run.GetOffer(card);
                            if (offer.Kind != RewardKind.Ability && offer.Kind != RewardKind.Talent) continue;
                            Assert.IsTrue(offer.PoolIndex != 2 && offer.PoolIndex != 9,
                                "сид " + seed + ", арена " + depth + ": предложен убранный " + offer.PoolIndex + " (" + offer.Kind + ")");
                            if (offer.Kind == RewardKind.Ability) seen[offer.PoolIndex] = true;
                        }
                        FormBaselineScenarios.Choose(run, (int)((seed + (ulong)depth) % RiftRun.RewardChoices), depth);
                        if (run.Phase == RunPhase.ChoosingRoute)
                            run.Step(FormBaselineScenarios.Command((RunCommand)((int)RunCommand.ChooseRoute1 + depth % 3)));
                    }
                }

            Assert.Greater(screens, 200, "экранов наград слишком мало для проверки");
            for (int i = 0; i < PelagKit.PoolSize; i++)
                if (PelagKit.InRewardPool(i) && i != PelagKit.StarterPoolIndex)
                    Assert.IsTrue(seen[i], "навык наград " + i + " ни разу не выпал");
        }

        /// <summary>
        /// Набор, где «Ладно смазал» и Отбой уже лежат (старое состояние, F8): забег идёт,
        /// оба в слотах и в симуляции и жмутся; карточек их и их усилений нет, пустой слот
        /// заполняется обычной наградой.
        /// </summary>
        [Test]
        public void RunAlreadyHoldingBlazeAndBackblast_KeepsWorking()
        {
            for (ulong seed = 1; seed <= 12; seed++)
            {
                RiftRun run = FormBaselineScenarios.NewArenaRun(seed);
                run.StartRun();
                Assert.IsTrue(run.Loadout.Put(1, 2));
                Assert.IsTrue(run.Loadout.Put(2, 9));
                run.Loadout.TakeTalent(2, 0);
                run.ApplyLoadout();
                Assert.AreEqual(AbilityDefinition.BlazeId, run.Sim.GetAbility(1).DefinitionId);
                Assert.AreEqual(AbilityDefinition.BackblastId, run.Sim.GetAbility(2).DefinitionId);

                for (int depth = 1; depth <= 4; depth++)
                {
                    Assert.AreEqual(RunPhase.Clearing, run.Phase, "сид " + seed + ", арена " + depth);
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(run), "сид " + seed + ", арена " + depth);
                    int pick = 0;
                    for (int card = RiftRun.RewardChoices - 1; card >= 0; card--)
                    {
                        RewardOffer offer = run.GetOffer(card);
                        if (offer.Kind == RewardKind.Ability || offer.Kind == RewardKind.Talent)
                            Assert.IsTrue(offer.PoolIndex != 2 && offer.PoolIndex != 9,
                                "сид " + seed + ": предложен убранный " + offer.PoolIndex + " (" + offer.Kind + ")");
                        if (offer.Kind != RewardKind.Ability || !run.Loadout.IsFull) pick = card;
                    }
                    run.Step(FormBaselineScenarios.Choice(pick));
                    if (run.Phase == RunPhase.ReplacingAbility) run.Step(FormBaselineScenarios.Command(RunCommand.SalvageAbility));
                    Assert.AreEqual(RunPhase.ChoosingRoute, run.Phase, "сид " + seed + ", арена " + depth);
                    run.Step(FormBaselineScenarios.Command((RunCommand)((int)RunCommand.ChooseRoute1 + depth % 3)));

                    Assert.AreEqual(1, run.Loadout.SlotOf(2), "«Ладно смазал» ушёл из слота");
                    Assert.AreEqual(2, run.Loadout.SlotOf(9), "Отбой ушёл из слота");
                    Assert.IsTrue(run.Loadout.HasTalent(2, 0), "взятое усиление пропало");
                    Assert.AreEqual(AbilityDefinition.BlazeId, run.Sim.GetAbility(1).DefinitionId);
                    Assert.AreEqual(AbilityDefinition.BackblastId, run.Sim.GetAbility(2).DefinitionId);
                }
            }

            // Оба жмутся из слотов забега (враги сняты — бою не мешают).
            RiftRun cast = FormBaselineScenarios.NewArenaRun(1);
            cast.StartRun();
            cast.Loadout.Put(1, 2);
            cast.Loadout.Put(2, 9);
            cast.ApplyLoadout();
            for (int guard = 0; guard < 200 && cast.Phase == RunPhase.Clearing; guard++)
            {
                EntityStore e = cast.Sim.Entities;
                for (int i = 0; i < e.Count; i++)
                    if (e.Side[i] != Faction.Wole) e.Alive[i] = false;
                cast.Step(InputFrame.Empty);
            }
            Assert.AreEqual(RunPhase.SeekingExit, cast.Phase);
            cast.Step(Press(1));
            Assert.IsTrue(cast.Sim.BlazeCasting || cast.Sim.BlazeActive, "«Ладно смазал» в слоте не жмётся");
            for (int i = 0; i < 90; i++) cast.Step(InputFrame.Empty);
            bool burst = false;
            cast.Step(Press(2));
            for (int i = 0; i < 8 && !burst; i++)
            {
                foreach (SimEvent ev in cast.Sim.Events) burst |= ev.Type == SimEventType.BackblastBurst;
                cast.Step(InputFrame.Empty);
            }
            Assert.IsTrue(burst, "Отбой в слоте не жмётся");
        }

        // ---- набор забега ----

        [Test]
        public void RunStartsWithWhirlwindThreeEmptySlotsAndDash()
        {
            var sim = NewRun().Sim;

            Assert.AreEqual(AbilityDefinition.WhirlwindId, sim.GetAbility(0).DefinitionId);
            for (int slot = 1; slot < PelagKit.MainSlots; slot++)
                Assert.IsNull(sim.GetAbility(slot), $"слот {slot + 1} не пуст на старте");
            Assert.AreEqual(AbilityDefinition.DashId, sim.GetAbility(PelagKit.DashSlot).DefinitionId);
        }

        [Test]
        public void NewAbilityTakesTheFirstFreeSlotAndTheFifthDoesNotFit()
        {
            var loadout = new RunLoadout();
            Assert.IsTrue(loadout.Add(3));
            Assert.AreEqual(3, loadout.PoolIndexAt(1));
            Assert.IsTrue(loadout.Add(5));
            Assert.IsTrue(loadout.Add(7));

            Assert.IsTrue(loadout.IsFull);
            Assert.IsFalse(loadout.Add(1), "пятая способность при полной панели");
        }

        // ---- таланты ----

        [Test]
        public void ReplacedAbilityLosesItsTalents()
        {
            var loadout = new RunLoadout();
            loadout.Add(1);
            loadout.TakeTalent(1);
            loadout.TakeTalent(1);
            int slot = loadout.SlotOf(1);

            Assert.IsTrue(loadout.Put(slot, 5));
            Assert.IsTrue(loadout.Put(slot, 1));

            Assert.AreEqual(0, loadout.TalentRank(1), "вернувшаяся способность принесла старые таланты");
        }

        [Test]
        public void LoadoutIsPartOfTheRunState()
        {
            var a = NewRun();
            var b = NewRun();
            Assert.AreEqual(a.Hash(), b.Hash());

            b.Loadout.TakeTalent(0);

            Assert.AreNotEqual(a.Hash(), b.Hash());
        }
    }
}
