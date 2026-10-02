using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Награда «выбор формы» (план форм 02.10, тест 4) — с включённым включателем и
    /// разблокированными формами: готовых форм ещё нет, механика Вихря следующим
    /// шагом. Правила — заглушки FormRewardRules: не раньше А3, шанс А3–А5,
    /// гарантия после А5 при двух навыках, вторая ~25% на А7–А8, не на боссе и не на
    /// финальной арене, раненому — родник, а форма ждёт (кроме гарантии).
    /// </summary>
    public sealed class FormRewardTests
    {
        private const int Whirlwind = PelagKit.StarterPoolIndex;

        private static RiftRun FormRun(ulong seed, bool boss = true, Action<RiftRun> configure = null)
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(seed, boss);
            run.SkillFormsEnabled = true;
            run.DeveloperFormsUnlocked = true;
            // Временный босс-Хранитель: экран босса проверяется без механики Хозяина Чащи.
            run.Sim.ThicketMasterBossEnabled = false;
            configure?.Invoke(run);
            run.StartRun();
            return run;
        }

        /// <summary>Набор: Вихрь в слоте 0 и ещё skills−1 навыков без форм (пул 1, 2, 3).</summary>
        private static void KeepSkills(RiftRun run, int skills)
        {
            for (int slot = 1; slot < RunLoadout.Slots; slot++) run.Loadout.Put(slot, RunLoadout.EmptySlot);
            for (int slot = 1; slot < skills; slot++) Assert.IsTrue(run.Loadout.Put(slot, slot));
        }

        private static InputFrame Command(RunCommand command) => FormBaselineScenarios.Command(command);

        /// <summary>Не способность (набор держит KeepSkills), иначе первая; способность в полную панель — разбор.</summary>
        private static void TakeOrdinary(RiftRun run)
        {
            int card = 0;
            for (int i = RiftRun.RewardChoices - 1; i >= 0; i--)
                if (run.GetOffer(i).Kind != RewardKind.Ability) card = i;
            run.Step(FormBaselineScenarios.Choice(card));
            if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.SalvageAbility));
        }

        /// <summary>Экран формы по правилам: три карточки формы, разные пары (линия, форма), без родника.</summary>
        private static void AssertFormScreen(RiftRun run)
        {
            Assert.IsTrue(run.ChoosingForm);
            for (int i = 0; i < RiftRun.RewardChoices; i++)
            {
                RewardOffer offer = run.GetOffer(i);
                Assert.AreEqual(RewardKind.Form, offer.Kind, "карточка " + i);
                Assert.AreNotEqual(PelagForm.None, offer.Form, "у Вихря четыре формы — пустых мест нет");
                Assert.AreEqual(offer.PoolIndex, PelagForms.LineOf(offer.Form));
                Assert.IsTrue(run.Loadout.Owns(offer.PoolIndex));
                for (int j = 0; j < i; j++)
                    Assert.IsFalse(run.GetOffer(j).PoolIndex == offer.PoolIndex && run.GetOffer(j).Form == offer.Form,
                        "одна форма дважды на экране");
            }
            Assert.IsFalse(run.CanRerollReward);
        }

        /// <summary>
        /// Одна арена: набор, ранение, зачистка, экран. Экран формы — проверить и
        /// взять formCard; иначе — обычная награда. Потом первая ветка маршрута.
        /// Возвращает, был ли экран формы.
        /// </summary>
        private static bool PlayArena(RiftRun run, int skills, bool wound = false, int formCard = 0)
        {
            KeepSkills(run, skills);
            if (wound) FormBaselineScenarios.Wound(run);
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run), "экран награды арены " + run.Depth);
            bool form = run.ChoosingForm;
            if (form)
            {
                AssertFormScreen(run);
                RewardOffer chosen = run.GetOffer(formCard);
                run.Step(FormBaselineScenarios.Choice(formCard));
                Assert.AreEqual(chosen.Form, run.Loadout.FormOf(chosen.PoolIndex), "форма не встала");
                Assert.AreEqual(RewardKind.Form, run.GetTaken(run.TakenRewardCount - 1).Kind);
            }
            else
            {
                for (int i = 0; i < RiftRun.RewardChoices; i++) Assert.AreNotEqual(RewardKind.Form, run.GetOffer(i).Kind);
                TakeOrdinary(run);
            }
            if (run.Phase == RunPhase.ChoosingRoute) run.Step(Command(RunCommand.ChooseRoute1));
            return form;
        }

        // ---- когда ----

        [Test]
        public void SwitchedOff_NoFormScreenEver()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                RiftRun run = FormRun(seed, configure: r => r.SkillFormsEnabled = false);
                for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
                    Assert.IsFalse(PlayArena(run, skills: 2), "сид " + seed + ", арена " + depth);
                Assert.AreEqual(0, run.FormScreensShown);
            }
        }

        /// <summary>Не раньше А3; к А5 включительно — ровно один экран (шанс или гарантия), даже раненому на А5.</summary>
        [Test]
        public void TwoSkills_FirstFormComesOnArenaThreeToFive()
        {
            int byChance = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                RiftRun run = FormRun(seed);
                int first = 0;
                for (int depth = 1; depth <= 5; depth++)
                    if (PlayArena(run, skills: 2, wound: depth == 5) && first == 0) first = depth;
                Assert.That(first, Is.InRange(FormRewardRules.FormEarliestArena, FormRewardRules.FormGuaranteeArena - 1),
                    "сид " + seed);
                Assert.AreEqual(1, run.FormScreensShown, "сид " + seed);
                if (first < 5) byChance++;
            }
            // Шанс 20% на А3 и А4 — около 36% сидов до гарантии. Поток детерминирован, допуск широкий.
            Assert.That(byChance, Is.InRange(5, 25), "доля экранов по шансу");
        }

        [Test]
        public void OneSkill_NoGuarantee_OnlyTheChance()
        {
            int without = 0, with = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                RiftRun run = FormRun(seed);
                bool any = false;
                for (int depth = 1; depth <= 5; depth++)
                {
                    bool form = PlayArena(run, skills: 1);
                    if (depth < FormRewardRules.FormEarliestArena) Assert.IsFalse(form, "сид " + seed + ", арена " + depth);
                    any |= form;
                }
                if (any) with++; else without++;
            }
            Assert.Greater(without, 0, "один навык, а гарантия сработала на каждом сиде");
            Assert.Greater(with, 0, "шанс не сработал ни разу");
        }

        /// <summary>Раненому — родник, экран формы ждёт; гарантия после А5 — даже раненому.</summary>
        [Test]
        public void Wounded_SpringFirst_GuaranteeStillComes()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                RiftRun run = FormRun(seed);
                for (int depth = 1; depth <= 4; depth++)
                {
                    KeepSkills(run, 2);
                    FormBaselineScenarios.Wound(run);
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                    Assert.IsFalse(run.ChoosingForm, "раненому экран формы вместо родника, сид " + seed);
                    TakeOrdinary(run);
                    run.Step(Command(RunCommand.ChooseRoute1));
                }
                Assert.IsTrue(PlayArena(run, skills: 2, wound: true), "гарантия после А5, сид " + seed);
            }
        }

        /// <summary>Вторая форма — шанс на А7–А8; больше двух экранов нет; на финальной арене — никогда.</summary>
        [Test]
        public void SecondForm_OnlyOnArenaSevenToEight_NeverOnTheFinalArena()
        {
            int seventh = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                RiftRun run = FormRun(seed, boss: false);
                for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
                {
                    bool form = PlayArena(run, skills: 2);
                    if (depth == 6) Assert.IsFalse(form, "между гарантией и второй формой, сид " + seed);
                    if (depth == FormBaselineScenarios.ArenaCount) Assert.IsFalse(form, "финальная арена, сид " + seed);
                    if (depth == 7 && form) seventh++;
                    // Формы есть только у Вихря: сняв выбранную, проверяем лимит и окно второй.
                    if (form) run.Loadout.DebugSetForm(Whirlwind, PelagForm.None);
                    if (run.Phase == RunPhase.Ended) break;
                }
                Assert.That(run.FormScreensShown, Is.InRange(1, FormRewardRules.MaxFormScreensPerRun), "сид " + seed);
                Assert.AreEqual(RunOutcome.Completed, run.Outcome);
            }
            Assert.That(seventh, Is.InRange(3, 20), "25% на А7");
        }

        [Test]
        public void NeverOnTheBossArena()
        {
            for (ulong seed = 1; seed <= 4; seed++)
            {
                RiftRun run = FormRun(seed);
                for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
                {
                    if (PlayArena(run, skills: 2)) run.Loadout.DebugSetForm(Whirlwind, PelagForm.None);
                }
                Assert.AreEqual(FormBaselineScenarios.ArenaCount + 1, run.Depth);
                Assert.Greater(run.BossId, 0);
                KeepSkills(run, 2);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                Assert.IsTrue(run.ChoosingArtifact, "на боссе — артефакт");
                Assert.IsFalse(run.ChoosingForm);
            }
        }

        [Test]
        public void NoEligibleLine_NoScreen()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                RiftRun run = FormRun(seed);
                for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
                {
                    // Без Вихря: у остальных навыков и у сабли форм пока нет.
                    run.Loadout.Put(0, 4);
                    for (int slot = 1; slot < RunLoadout.Slots; slot++) run.Loadout.Put(slot, slot);
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                    Assert.IsFalse(run.ChoosingForm, "сид " + seed + ", арена " + depth);
                    TakeOrdinary(run);
                    if (run.Phase == RunPhase.ChoosingRoute) run.Step(Command(RunCommand.ChooseRoute1));
                }
            }
        }

        // ---- что на экране ----

        /// <summary>Подставная таблица: линия 0 — 4 формы, линия 3 — 3, линия 6 — 4, линия 9 — одна.</summary>
        private readonly struct FakeForms : IFormSource
        {
            public int Count(int line) => line == 0 ? 4 : line == 3 ? 3 : line == 6 ? 4 : line == 9 ? 1 : 0;
            public PelagForm At(int line, int i) => (PelagForm)(line * 10 + i + 1);
        }

        private static RewardOffer[] Compose(ulong seed, params int[] lines)
        {
            var offers = new RewardOffer[RiftRun.RewardChoices];
            var rng = new Pcg32(seed, 0x464F524D4FUL);
            RiftRun.ComposeFormScreen(ref rng, lines.AsSpan(), lines.Length, new FakeForms(), offers);
            return offers;
        }

        [Test]
        public void Screen_ThreeSkills_ThreeDifferentSkills()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                RewardOffer[] offers = Compose(seed, 0, 3, 6);
                bool[] seen = new bool[10];
                foreach (RewardOffer offer in offers)
                {
                    Assert.AreEqual(RewardKind.Form, offer.Kind);
                    Assert.IsFalse(seen[offer.PoolIndex], "навык дважды на экране");
                    seen[offer.PoolIndex] = true;
                    Assert.AreEqual(offer.PoolIndex, (int)offer.Form / 10, "форма чужой линии");
                }
            }
        }

        [Test]
        public void Screen_OneSkill_ThreeOfItsForms_AndTwoSkillsFillTheThird()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                RewardOffer[] one = Compose(seed, 0);
                Assert.AreEqual(0, one[0].PoolIndex);
                Assert.AreEqual(0, one[1].PoolIndex);
                Assert.AreEqual(0, one[2].PoolIndex);
                Assert.AreNotEqual(one[0].Form, one[1].Form);
                Assert.AreNotEqual(one[0].Form, one[2].Form);
                Assert.AreNotEqual(one[1].Form, one[2].Form);

                RewardOffer[] two = Compose(seed, 3, 6);
                Assert.AreNotEqual(two[0].PoolIndex, two[1].PoolIndex, "две линии — сначала разные");
                Assert.AreEqual(two[0].PoolIndex, two[2].PoolIndex, "третья карточка — снова первая линия");
                Assert.AreNotEqual(two[0].Form, two[2].Form);
            }
            RewardOffer[] lonely = Compose(7, 9);
            Assert.AreEqual((PelagForm)91, lonely[0].Form);
            Assert.AreEqual(PelagForm.None, lonely[1].Form, "форм меньше трёх — пустое место");
            Assert.AreEqual(PelagForm.None, lonely[2].Form);
            Assert.AreEqual(RunLoadout.EmptySlot, lonely[1].PoolIndex);
            Assert.AreEqual(Compose(11, 0, 3, 6)[1].Form, Compose(11, 0, 3, 6)[1].Form, "состав — функция потока");
        }

        // ---- взятие ----

        [Test]
        public void Choosing_PutsTheFormIntoTheSlotBuild_FromTheNextArena()
        {
            RiftRun run = FormRun(3);
            PelagForm chosen = PelagForm.None;
            for (int depth = 1; depth <= 5 && chosen == PelagForm.None; depth++)
            {
                KeepSkills(run, 2);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                if (!run.ChoosingForm) { TakeOrdinary(run); run.Step(Command(RunCommand.ChooseRoute1)); continue; }
                chosen = run.GetOffer(2).Form;
                int before = run.TakenRewardCount;
                run.Step(FormBaselineScenarios.Choice(2));
                Assert.AreEqual(before + 1, run.TakenRewardCount);
                Assert.AreEqual(RunPhase.ChoosingRoute, run.Phase);
                run.Step(Command(RunCommand.ChooseRoute1));
            }
            Assert.AreNotEqual(PelagForm.None, chosen, "к А5 экрана формы не было");
            Assert.AreEqual(chosen, run.Loadout.FormOf(Whirlwind));
            AbilityBuild build = run.Sim.GetAbility(0);
            Assert.AreEqual(chosen, build.Form, "иконка и механика берут форму из сборки слота");
            Assert.AreEqual(AbilityDefinition.WhirlwindId, build.DefinitionId);
            Assert.IsTrue(run.Sim.FormIs(0, chosen));
        }

        /// <summary>Отказа и переброса на экране формы нет; устаревшая карточка не берётся.</summary>
        [Test]
        public void FormScreen_HasNoSkipNoReroll_AndIgnoresAStaleCard()
        {
            RiftRun run = FormRun(1, configure: r => r.SetPreparation(new RunPreparation(AbilityDefinition.WhirlwindId,
                CampGift.BackupPlan, PotionKind.SmallHealth, PotionKind.SmallLavidium)));
            for (int depth = 1; depth <= 5 && !run.ChoosingForm; depth++)
            {
                KeepSkills(run, 2);
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                if (run.ChoosingForm) break;
                TakeOrdinary(run);
                run.Step(Command(RunCommand.ChooseRoute1));
            }
            Assert.IsTrue(run.ChoosingForm);
            ulong screen = run.Hash();
            Assert.IsFalse(run.CanRerollReward, "переброс «Запасного плана» на экране формы");
            run.Step(Command(RunCommand.RerollReward));
            run.Step(Command(RunCommand.SkipReward));
            Assert.AreEqual(screen, run.Hash(), "отказ или переброс тронули экран формы");
            Assert.IsFalse(run.GiftRerollUsed);

            // Форму успели поставить иначе (F8) — карточка устарела, нажатие ничего не делает.
            run.Loadout.DebugSetForm(Whirlwind, run.GetOffer(1).Form);
            int taken = run.TakenRewardCount;
            run.Step(FormBaselineScenarios.Choice(0));
            Assert.IsTrue(run.ChoosingForm);
            Assert.AreEqual(taken, run.TakenRewardCount);
        }

        /// <summary>Экран формы перезаписывает брошенные карточки: Loot и остальные потоки — как без него.</summary>
        [Test]
        public void FormScreen_LeavesEveryRngStreamAsWithoutIt()
        {
            int screens = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                RiftRun on = FormRun(seed);
                RiftRun off = FormRun(seed, configure: r => r.SkillFormsEnabled = false);
                for (int depth = 1; depth <= 5; depth++)
                {
                    KeepSkills(on, 2); KeepSkills(off, 2);
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(on));
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(off));
                    RngStreams a = on.Sim.Rng, b = off.Sim.Rng;
                    Assert.AreEqual(b.Loot.State, a.Loot.State, "Loot, сид " + seed);
                    Assert.AreEqual(b.Affix.State, a.Affix.State, "Affix, сид " + seed);
                    Assert.AreEqual(b.Layout.State, a.Layout.State, "Layout, сид " + seed);
                    Assert.AreEqual(b.Spawns.State, a.Spawns.State, "Spawns, сид " + seed);
                    if (on.ChoosingForm) { screens++; break; }
                    for (int i = 0; i < RiftRun.RewardChoices; i++)
                        Assert.AreEqual(OfferHash(off.GetOffer(i)), OfferHash(on.GetOffer(i)), "сид " + seed);
                    TakeOrdinary(on); TakeOrdinary(off);
                    on.Step(Command(RunCommand.ChooseRoute1)); off.Step(Command(RunCommand.ChooseRoute1));
                }
            }
            Assert.AreEqual(12, screens, "к А5 у каждого сида с двумя навыками был экран формы");
        }

        private static ulong OfferHash(in RewardOffer offer)
        {
            ulong hash = Hashing.Offset;
            offer.HashInto(ref hash);
            return hash;
        }

        /// <summary>Два прогона одного сида с одним вводом — один хеш забега на каждом шаге петли.</summary>
        [Test]
        public void SameSeedSameInput_SameHashAtEveryStep()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                RiftRun a = FormRun(seed), b = FormRun(seed);
                Assert.AreEqual(a.Hash(), b.Hash());
                for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
                {
                    KeepSkills(a, 2); KeepSkills(b, 2);
                    for (int guard = 0; guard < 4000 && a.Phase == RunPhase.Clearing; guard++)
                    {
                        for (int i = 0; i < a.Sim.Entities.Count; i++)
                            if (a.Sim.Entities.Side[i] != Faction.Wole) a.Sim.Entities.Alive[i] = b.Sim.Entities.Alive[i] = false;
                        a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                        Assert.AreEqual(a.Hash(), b.Hash(), "сид " + seed + ", арена " + depth);
                    }
                    a.Sim.Entities.Position[Simulation.PlayerId] = a.Map.ExitPoint(0);
                    b.Sim.Entities.Position[Simulation.PlayerId] = b.Map.ExitPoint(0);
                    a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                    Assert.AreEqual(RunPhase.ChoosingReward, a.Phase);
                    Assert.AreEqual(a.Hash(), b.Hash(), "экран, сид " + seed + ", арена " + depth);
                    var choice = FormBaselineScenarios.Choice((int)((seed + (ulong)depth) % RiftRun.RewardChoices));
                    a.Step(choice); b.Step(choice);
                    if (a.Phase == RunPhase.ReplacingAbility)
                    {
                        a.Step(Command(RunCommand.SalvageAbility)); b.Step(Command(RunCommand.SalvageAbility));
                    }
                    Assert.AreEqual(a.Hash(), b.Hash(), "выбор, сид " + seed + ", арена " + depth);
                    if (a.Loadout.FormOf(Whirlwind) != PelagForm.None && depth < 6)
                    {
                        a.Loadout.DebugSetForm(Whirlwind, PelagForm.None); b.Loadout.DebugSetForm(Whirlwind, PelagForm.None);
                    }
                    if (depth == FormBaselineScenarios.ArenaCount) break;
                    a.Step(Command(RunCommand.ChooseRoute1)); b.Step(Command(RunCommand.ChooseRoute1));
                    Assert.AreEqual(a.Hash(), b.Hash(), "маршрут, сид " + seed + ", арена " + depth);
                }
                Assert.Greater(a.FormScreensShown, 0, "сид " + seed + " прошёл без экрана формы");
            }
        }

        /// <summary>Шаг записи: ввод петли или правка стенда (снять врагов, герой к выходу, набор).</summary>
        private struct ScriptStep
        {
            public int Kind;   // 0 — ввод, 1 — снять врагов, 2 — герой у выхода, 3 — два навыка
            public InputFrame Input;
        }

        private static void Apply(RiftRun run, in ScriptStep step)
        {
            switch (step.Kind)
            {
                case 1:
                    for (int i = 0; i < run.Sim.Entities.Count; i++)
                        if (run.Sim.Entities.Side[i] != Faction.Wole) run.Sim.Entities.Alive[i] = false;
                    break;
                case 2: run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0); break;
                case 3: KeepSkills(run, 2); break;
                default: run.Step(step.Input); break;
            }
        }

        /// <summary>
        /// Реплей — сид и поток ввода (новых команд у форм нет, выбор формы — ChooseReward).
        /// Забег с экраном формы, записанный целиком и проигранный позже на свежем
        /// забеге того же сида, приходит в тот же хеш и с той же формой.
        /// </summary>
        [Test]
        public void RecordedStream_ReplaysAFormRun()
        {
            const ulong seed = 9;
            var script = new System.Collections.Generic.List<ScriptStep>();
            RiftRun a = FormRun(seed);
            void Do(int kind, InputFrame input = default)
            {
                var step = new ScriptStep { Kind = kind, Input = input };
                script.Add(step);
                Apply(a, in step);
            }

            var screens = new System.Collections.Generic.List<ulong>();
            for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
            {
                Do(3);
                for (int guard = 0; guard < 4000 && a.Phase == RunPhase.Clearing; guard++) { Do(1); Do(0, InputFrame.Empty); }
                Do(2);
                Do(0, InputFrame.Empty);
                Assert.AreEqual(RunPhase.ChoosingReward, a.Phase);
                screens.Add(a.Hash());
                Do(0, FormBaselineScenarios.Choice((int)((seed + (ulong)depth) % RiftRun.RewardChoices)));
                if (a.Phase == RunPhase.ReplacingAbility) Do(0, Command(RunCommand.SalvageAbility));
                if (depth < FormBaselineScenarios.ArenaCount) Do(0, Command(RunCommand.ChooseRoute1));
            }
            Assert.Greater(a.FormScreensShown, 0, "в записи нет экрана формы");

            RiftRun b = FormRun(seed);
            int screen = 0;
            foreach (ScriptStep step in script)
            {
                bool reward = b.Phase == RunPhase.ChoosingReward;
                Apply(b, in step);
                if (!reward && b.Phase == RunPhase.ChoosingReward) Assert.AreEqual(screens[screen++], b.Hash(), "экран " + screen);
            }
            Assert.AreEqual(screens.Count, screen);
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.AreEqual(a.Loadout.FormOf(Whirlwind), b.Loadout.FormOf(Whirlwind));
            Assert.AreNotEqual(PelagForm.None, b.Loadout.FormOf(Whirlwind));
        }

        // ---- меню разработчика ----

        [Test]
        public void DeveloperPreview_OpensMidFight_ChoosingReturnsToTheFight()
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(5);
            run.StartRun();
            Assert.IsFalse(run.SkillFormsEnabled, "предпросмотр не требует включателя");
            int tick = run.Sim.Tick;
            ulong before = run.Hash();
            Assert.IsTrue(run.DebugOpenFormScreen());
            Assert.IsTrue(run.FormPreviewOpen);
            AssertFormScreen(run);
            Assert.AreNotEqual(before, run.Hash(), "предпросмотр не виден хешу");
            run.Step(InputFrame.Empty);
            Assert.AreEqual(tick, run.Sim.Tick, "бой шагал, пока открыт экран формы");
            Assert.IsFalse(run.DebugOpenFormScreen(), "второй экран поверх первого");

            PelagForm chosen = run.GetOffer(1).Form;
            run.Step(FormBaselineScenarios.Choice(1));
            Assert.AreEqual(RunPhase.Clearing, run.Phase, "после выбора — обратно в бой");
            Assert.IsFalse(run.FormPreviewOpen);
            Assert.AreEqual(chosen, run.Loadout.FormOf(Whirlwind));
            Assert.AreEqual(chosen, run.Sim.GetAbility(0).Form, "форма в сборке сразу, бой идёт");
            Assert.AreEqual(0, run.FormScreensShown, "предпросмотр съел лимит забега");
            Assert.AreEqual(1, run.Depth);
            Assert.IsFalse(run.DebugOpenFormScreen(), "у Вихря уже форма — других линий с формами нет");
        }

        [Test]
        public void DeveloperPreview_CanBeClosed_AndNeedsAFormLine()
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(6);
            run.StartRun();
            ulong before = run.Hash();
            Assert.IsTrue(run.DebugOpenFormScreen());
            Assert.IsTrue(run.DebugCloseFormScreen());
            Assert.AreEqual(RunPhase.Clearing, run.Phase);
            Assert.AreEqual(before, run.Hash(), "закрытый предпросмотр оставил след");
            Assert.IsFalse(run.DebugCloseFormScreen());

            run.Loadout.Put(0, 1);
            Assert.IsFalse(run.DebugOpenFormScreen(), "без Вихря форм нет");
            run.Loadout.Put(0, Whirlwind);
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            Assert.IsFalse(run.DebugOpenFormScreen(), "на обычном экране награды предпросмотр не открывается");
        }
    }
}
