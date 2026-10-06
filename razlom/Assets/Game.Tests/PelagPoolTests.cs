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
        /// Убранные индексы (03.10): 4 — Удар якорем (влит в Крушение), 8 — «На вылет»
        /// (убран владельцем). Номер держат (PoolDefinition прежний), но наградой не приходят.
        /// </summary>
        [Test]
        public void RetiredPoolIndices_AreNeverOffered()
        {
            for (int i = 0; i < PelagKit.PoolSize; i++)
                // 10 — Бросок якоря (03.10): не убран, но в награды — только по слову владельца (пока F8).
                Assert.AreEqual(i != 4 && i != 8 && i != 10, PelagKit.InRewardPool(i), "пул " + i);
            Assert.AreEqual(AbilityDefinition.AnchorSlamId, PelagKit.PoolDefinition(4).Id, "номер не переиспользован");
            Assert.AreEqual(AbilityDefinition.SkewerId, PelagKit.PoolDefinition(8).Id);

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
                        Assert.IsTrue(offer.PoolIndex != 4 && offer.PoolIndex != 8 && offer.PoolIndex != 10, "сид " + seed + ": предложен убранный " + offer.PoolIndex);
                    }
                    FormBaselineScenarios.Choose(run, 0, screen);
                }
            }
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
