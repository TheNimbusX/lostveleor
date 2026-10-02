using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Таланты формы (план форм 02.10, тест 5): та же карточка Talent с номером от
    /// PelagForms.FormTalentBase, только после выбора формы; взятие кладёт узел;
    /// замена навыка их снимает. У линий без формы последовательность карточек —
    /// пин FormPinTests.TalentCards_….
    /// </summary>
    public sealed class FormTalentTests
    {
        private const int Whirlwind = PelagKit.StarterPoolIndex;
        private const PelagForm Storm = PelagForm.WhirlwindStorm;

        /// <summary>Полная панель 0–3, все свои усиления взяты: у линий больше нечего брать.</summary>
        private static RiftRun MaxedRun(ulong seed, bool form)
        {
            RiftRun run = FormBaselineScenarios.NewPrototypeRun(seed);
            run.StartRun();
            for (int slot = 1; slot < RunLoadout.Slots; slot++) run.Loadout.Put(slot, slot);
            for (int pool = 0; pool < RunLoadout.Slots; pool++)
                for (int i = 0; i < SabreTalents.TalentsPerLine; i++) run.Loadout.TakeTalent(pool, i);
            if (form) Assert.IsTrue(run.Loadout.ChooseForm(Whirlwind, Storm, unreadyAllowed: true));
            return run;
        }

        [Test]
        public void FormTalents_OnlyAfterTheForm()
        {
            int offered = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                RiftRun plain = MaxedRun(seed, form: false);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(plain));
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                {
                    Assert.AreNotEqual(RewardKind.Talent, plain.GetOffer(i).Kind, "усиление без кандидата, сид " + seed);
                    Assert.IsFalse(plain.GetOffer(i).IsFormTalent);
                }

                RiftRun formed = MaxedRun(seed, form: true);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(formed));
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                {
                    RewardOffer offer = formed.GetOffer(i);
                    if (offer.Kind != RewardKind.Talent) continue;
                    offered++;
                    Assert.AreEqual(Whirlwind, offer.PoolIndex, "талант формы чужой линии");
                    Assert.IsTrue(offer.IsFormTalent);
                    Assert.That(offer.TalentIndex, Is.InRange(PelagForms.FormTalentBase,
                        PelagForms.FormTalentBase + PelagForms.FormTalentCount(Storm) - 1));
                    Assert.AreEqual(offer.TalentIndex - PelagForms.FormTalentBase, offer.FormTalentIndex);
                }
            }
            // Полная панель: усиление — 55% карточки, кандидат — один Вихрь (по одной карточке на линию).
            Assert.That(offered, Is.InRange(20, 60), "таланты формы почти не выпадают");
        }

        [Test]
        public void TakingAFormTalent_PutsItsNodeIntoTheSlot_AndTheNextRiftBuild()
        {
            for (ulong seed = 1; seed <= 60; seed++)
            {
                RiftRun run = MaxedRun(seed, form: true);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                int card = -1;
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (run.GetOffer(i).IsFormTalent) card = i;
                if (card < 0) continue;

                int k = run.GetOffer(card).FormTalentIndex;
                run.Step(FormBaselineScenarios.Choice(card));
                Assert.IsTrue(run.Loadout.HasFormTalent(Whirlwind, k));
                Assert.AreEqual(RunPhase.Clearing, run.Phase, "прототипный забег — сразу следующий Разлом");
                Assert.AreEqual(2, run.Depth);

                var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
                int count = run.Loadout.AppendSlotNodes(0, nodes, 0);
                bool found = false;
                for (int n = 0; n < count; n++) found |= nodes[n].Id == StableId.Of(PelagForms.TalentKeyOf(Storm, k));
                Assert.IsTrue(found, "узел таланта формы не дошёл до слота");
                Assert.AreEqual(Storm, run.Sim.GetAbility(0).Form);

                // Взятый не предлагается снова.
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    Assert.IsFalse(run.GetOffer(i).IsFormTalent && run.GetOffer(i).FormTalentIndex == k, "взятый талант формы снова");
                return;
            }
            Assert.Fail("за 60 сидов не выпало таланта формы");
        }

        [Test]
        public void ReplacingTheSkill_TakesFormAndItsTalentsAway()
        {
            RiftRun run = MaxedRun(4, form: true);
            run.Loadout.TakeFormTalent(Whirlwind, 1);
            // Как мини-меню над навыком с элиты посреди боя: Вихрь уходит из слота.
            Assert.IsTrue(run.Loadout.Put(0, 7));
            run.ApplyLoadout();
            Assert.AreEqual(PelagForm.None, run.Loadout.FormOf(Whirlwind));
            Assert.AreEqual(0, run.Loadout.FormTalentMask(Whirlwind));
            Assert.AreEqual(PelagForm.None, run.Sim.GetAbility(0).Form);
            Assert.IsFalse(run.Sim.FormIs(0, Storm));
        }
    }
}
