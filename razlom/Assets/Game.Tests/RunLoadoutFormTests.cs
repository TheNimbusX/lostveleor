using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Форма в наборе забега (план форм 02.10, тест 2): одна форма на линию во
    /// владении, сменить нельзя; убрал навык — форма и её таланты пропали;
    /// сабля — линия всегда во владении, но не слот; все узлы доходят до сборки.
    /// Хеш набора без форм прибит в FormPinTests.
    /// </summary>
    public sealed class RunLoadoutFormTests
    {
        private const int Whirlwind = PelagKit.StarterPoolIndex;
        private const PelagForm Storm = PelagForm.WhirlwindStorm;

        [Test]
        public void Form_OnlyOnAnOwnedLineOfItsOwn()
        {
            var loadout = new RunLoadout();
            Assert.AreEqual(PelagForm.None, loadout.FormOf(Whirlwind));
            Assert.IsFalse(loadout.ChooseForm(1, Storm, unreadyAllowed: true), "форма чужой линии");
            Assert.IsFalse(loadout.ChooseForm(Whirlwind, PelagForm.None, unreadyAllowed: true));
            if (!PelagForms.IsReady(Storm))
                Assert.IsFalse(loadout.ChooseForm(Whirlwind, Storm), "неготовая форма в обычном забеге");
            Assert.IsTrue(loadout.ChooseForm(Whirlwind, Storm, unreadyAllowed: true));
            Assert.AreEqual(Storm, loadout.FormOf(Whirlwind));
            Assert.AreEqual(1, loadout.FormsChosen);

            var other = new RunLoadout();
            other.Put(0, 1);
            Assert.IsFalse(other.Owns(Whirlwind));
            Assert.IsFalse(other.CanChooseForm(Whirlwind, Storm, unreadyAllowed: true), "форма навыка не во владении");
        }

        [Test]
        public void SecondForm_DoesNotReplaceTheFirst()
        {
            var loadout = new RunLoadout();
            loadout.ChooseForm(Whirlwind, Storm, true);
            int version = loadout.Version;
            Assert.IsFalse(loadout.ChooseForm(Whirlwind, PelagForm.WhirlwindMaelstrom, true));
            Assert.AreEqual(Storm, loadout.FormOf(Whirlwind));
            Assert.AreEqual(version, loadout.Version);
        }

        [Test]
        public void RemovingTheSkill_ClearsFormAndFormTalents_AndItReturnsBasic()
        {
            var loadout = new RunLoadout();
            loadout.TakeTalent(Whirlwind, 3);
            loadout.ChooseForm(Whirlwind, Storm, true);
            Assert.IsTrue(loadout.TakeFormTalent(Whirlwind, 0));
            Assert.IsTrue(loadout.TakeTalent(Whirlwind, PelagForms.FormTalentBase + 2), "талант формы карточкой Talent");
            Assert.AreEqual(0b101, loadout.FormTalentMask(Whirlwind));

            Assert.IsTrue(loadout.Put(0, 5));
            Assert.AreEqual(PelagForm.None, loadout.FormOf(Whirlwind));
            Assert.AreEqual(0, loadout.FormTalentMask(Whirlwind));
            Assert.AreEqual(0, loadout.TalentMask(Whirlwind));

            Assert.IsTrue(loadout.Put(1, Whirlwind));
            Assert.AreEqual(PelagForm.None, loadout.FormOf(Whirlwind), "вернувшийся навык принёс форму");
            Assert.AreEqual(0, loadout.FormTalentMask(Whirlwind));
            Assert.AreEqual(!FormRewardRules.FormLockedAfterRemoval,
                loadout.CanChooseForm(Whirlwind, Storm, true), "вопрос 3 плана: форма заново");
        }

        [Test]
        public void CopyFrom_AndResetToStarter_CarryAndClearForms()
        {
            var camp = new RunLoadout();
            camp.ChooseForm(Whirlwind, Storm, true);
            camp.TakeFormTalent(Whirlwind, 1);
            var run = new RunLoadout();
            run.CopyFrom(camp);
            Assert.AreEqual(Storm, run.FormOf(Whirlwind));
            Assert.IsTrue(run.HasFormTalent(Whirlwind, 1));
            Assert.AreEqual(FormBaselineScenarios.LoadoutHash(camp), FormBaselineScenarios.LoadoutHash(run));

            run.ResetToStarter();
            Assert.AreEqual(PelagForm.None, run.FormOf(Whirlwind));
            Assert.AreEqual(0, run.FormTalentMask(Whirlwind));
            Assert.AreEqual(FormBaselineScenarios.LoadoutHash(new RunLoadout()), FormBaselineScenarios.LoadoutHash(run));
        }

        [Test]
        public void SabreLine_IsAlwaysOwned_ButNeverSlotted()
        {
            var loadout = new RunLoadout();
            Assert.IsTrue(loadout.Owns(PelagKit.SabreLine));
            Assert.AreEqual(-1, loadout.SlotOf(PelagKit.SabreLine));
            Assert.IsFalse(loadout.Put(1, PelagKit.SabreLine));
            Assert.IsFalse(loadout.Add(PelagKit.SabreLine));
            for (int slot = 0; slot < RunLoadout.Slots; slot++) Assert.AreNotEqual(PelagKit.SabreLine, loadout.PoolIndexAt(slot));
            // Ни талантов, ни форм у сабли пока нет — она ничего не предлагает.
            Assert.AreEqual(0, RunLoadout.LineTalentCount(PelagKit.SabreLine));
            Assert.IsFalse(loadout.CanTakeTalent(PelagKit.SabreLine));
            Assert.IsFalse(loadout.TakeTalent(PelagKit.SabreLine, 0));
            Assert.AreEqual(0, loadout.AppendSabreNodes(new AbilityNode[RunLoadout.MaxNodesPerSlot], 0));
            Assert.AreEqual(1, loadout.SkillCount, "сабля не навык в счёте");
        }

        [Test]
        public void Version_GrowsOnFormChanges()
        {
            var loadout = new RunLoadout();
            int v = loadout.Version;
            loadout.ChooseForm(Whirlwind, Storm, true);
            Assert.Greater(loadout.Version, v);
            v = loadout.Version;
            loadout.TakeFormTalent(Whirlwind, 0);
            Assert.Greater(loadout.Version, v);
            v = loadout.Version;
            loadout.DebugSetForm(Whirlwind, PelagForm.WhirlwindFoamWaves);
            Assert.Greater(loadout.Version, v);
        }

        [Test]
        public void DebugSetForm_BypassesTheLock_AndNoneClears()
        {
            var loadout = new RunLoadout();
            loadout.ChooseForm(Whirlwind, Storm, true);
            loadout.TakeFormTalent(Whirlwind, 2);
            Assert.IsTrue(loadout.DebugSetForm(Whirlwind, PelagForm.WhirlwindMaelstrom));
            Assert.AreEqual(PelagForm.WhirlwindMaelstrom, loadout.FormOf(Whirlwind));
            Assert.AreEqual(0, loadout.FormTalentMask(Whirlwind), "таланты прежней формы остались");
            Assert.IsFalse(loadout.DebugSetForm(Whirlwind, (PelagForm)99), "несуществующая форма");
            Assert.IsFalse(loadout.DebugSetForm(Whirlwind, PelagForm.WhirlwindOnTheMove), "убранная форма (02.10)");
            Assert.AreEqual(PelagForm.WhirlwindMaelstrom, loadout.FormOf(Whirlwind));
            Assert.IsFalse(loadout.DebugSetForm(3, Storm), "форма чужой линии");
            Assert.IsTrue(loadout.DebugSetForm(Whirlwind, PelagForm.None));
            Assert.AreEqual(PelagForm.None, loadout.FormOf(Whirlwind));
            Assert.AreEqual(FormBaselineScenarios.LoadoutHash(new RunLoadout()), FormBaselineScenarios.LoadoutHash(loadout),
                "снятая форма оставила след в хеше");
        }

        [Test]
        public void FormTalents_OnlyAfterTheForm()
        {
            var loadout = new RunLoadout();
            Assert.IsFalse(loadout.CanTakeFormTalent(Whirlwind, 0));
            Assert.IsFalse(loadout.CanTakeTalent(Whirlwind, PelagForms.FormTalentBase));
            Assert.IsFalse(loadout.TakeTalent(Whirlwind, PelagForms.FormTalentBase));
            Assert.AreEqual(SabreTalents.TalentsPerLine, loadout.TalentsLeft(Whirlwind));

            loadout.ChooseForm(Whirlwind, Storm, true);
            int formTalents = PelagForms.FormTalentCount(Storm);
            Assert.AreEqual(SabreTalents.TalentsPerLine + formTalents, loadout.TalentsLeft(Whirlwind));
            Assert.IsTrue(loadout.CanTakeTalent(Whirlwind, PelagForms.FormTalentBase));
            Assert.IsFalse(loadout.CanTakeTalent(Whirlwind, PelagForms.FormTalentBase + formTalents), "талант сверх формы");
            Assert.AreEqual(PelagForms.FormTalentBase, loadout.UntakenTalentAt(Whirlwind, SabreTalents.TalentsPerLine),
                "таланты формы — после своих");
            Assert.IsTrue(loadout.TakeTalent(Whirlwind, PelagForms.FormTalentBase + 1));
            Assert.IsTrue(loadout.HasTalent(Whirlwind, PelagForms.FormTalentBase + 1));
            Assert.IsFalse(loadout.TakeTalent(Whirlwind, PelagForms.FormTalentBase + 1), "талант формы дважды");
            Assert.AreEqual(1, loadout.FormTalentCount(Whirlwind));
        }

        /// <summary>8 усилений + форма + таланты формы: все узлы доходят до сборки (буфер 32, а был 8).</summary>
        [Test]
        public void AllNodes_ReachTheBuild()
        {
            var loadout = new RunLoadout();
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++) Assert.IsTrue(loadout.TakeTalent(Whirlwind, i));
            loadout.ChooseForm(Whirlwind, Storm, true);
            int formTalents = PelagForms.FormTalentCount(Storm);
            for (int k = 0; k < formTalents; k++) Assert.IsTrue(loadout.TakeFormTalent(Whirlwind, k));

            var buffer = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = loadout.AppendSlotNodes(0, buffer, 0);
            // Буря кладёт два своих узла: форму и стат шага в удержании (02.10).
            int formOwn = PelagForms.AppendFormNodes(Storm, new AbilityNode[RunLoadout.MaxNodesPerSlot], 0);
            Assert.AreEqual(2, formOwn);
            Assert.AreEqual(SabreTalents.TalentsPerLine + formOwn + formTalents, count);
            int formNodes = 0, talentNodes = 0;
            for (int n = 0; n < count; n++)
            {
                if (buffer[n].Kind == NodeKind.Form) formNodes++;
                for (int k = 0; k < formTalents; k++)
                    if (buffer[n].Id == StableId.Of(PelagForms.TalentKeyOf(Storm, k))) talentNodes++;
            }
            Assert.AreEqual(1, formNodes);
            Assert.AreEqual(formTalents, talentNodes);

            var sim = new Simulation(5, 64);
            loadout.ApplyTo(sim);
            AbilityBuild build = sim.GetAbility(0);
            Assert.AreEqual(Storm, build.Form);
            Assert.AreEqual(AbilityDefinition.WhirlwindId, build.DefinitionId, "форма сменила определение");
            Assert.AreEqual(PelagForms.StormMoveMultiplier, build.Get(AbilityStatType.StartMoveMultiplier), "стат шага Бури");
            Assert.IsTrue(build.Has(AbilityFlag.WhirlwindCocoon), "восьмое усиление отрезал буфер");
            Assert.IsTrue(sim.FormIs(0, Storm));
            Assert.IsFalse(sim.FormIs(1, Storm));
            Assert.IsNull(sim.BasicAttackBuild, "у сабли нет узлов — билд не ставится");
        }

        [Test]
        public void Hash_SeesTheForm_ButNotAnEmptyOne()
        {
            var a = new RunLoadout();
            var b = new RunLoadout();
            Assert.AreEqual(FormBaselineScenarios.LoadoutHash(a), FormBaselineScenarios.LoadoutHash(b));
            b.ChooseForm(Whirlwind, Storm, true);
            Assert.AreNotEqual(FormBaselineScenarios.LoadoutHash(a), FormBaselineScenarios.LoadoutHash(b));
            a.ChooseForm(Whirlwind, PelagForm.WhirlwindMaelstrom, true);
            Assert.AreNotEqual(FormBaselineScenarios.LoadoutHash(a), FormBaselineScenarios.LoadoutHash(b));
            b.TakeFormTalent(Whirlwind, 0);
            ulong withTalent = FormBaselineScenarios.LoadoutHash(b);
            b.DebugSetForm(Whirlwind, Storm);
            Assert.AreEqual(withTalent, FormBaselineScenarios.LoadoutHash(b), "та же форма — тот же хеш");
        }
    }
}
