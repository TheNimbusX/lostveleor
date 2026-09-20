using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Пул способностей Пелага и набор на время забега.
    ///
    /// Решение владельца от 15 сентября: веток нет, забег начинается с
    /// автоатаки и Вихря, остальное находится по пути из пула в восемь
    /// способностей; таланты берутся в забеге строго по порядку.
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
            };
            Assert.AreEqual(expected.Length, PelagKit.PoolSize);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], PelagKit.PoolDefinition(i).Id, $"пул {i}");
                Assert.AreEqual(i, PelagKit.PoolIndexOf(expected[i]), $"обратный поиск {i}");
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
        public void TalentsAreTakenInOrderUpToFive()
        {
            var loadout = new RunLoadout();
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++)
                Assert.IsTrue(loadout.TakeTalent(0), $"талант {i + 1}");
            Assert.IsFalse(loadout.TakeTalent(0), "шестой талант");
            Assert.AreEqual(SabreTalents.TalentsPerLine, loadout.TalentRank(0));
            Assert.IsFalse(loadout.HasTalentToTake, "все таланты взяты, а доступный остался");
        }

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
