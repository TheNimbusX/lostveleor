using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Экран «Выбери форму» (план форм 02.10, шаг 3): тексты форм и заполнение карточек — на настоящем забеге из
// Game.Sim (предпросмотр F8 — RiftRun.DebugOpenFormScreen). Включатель форм обычных забегов не трогается.
public sealed class RunHudFormChoiceTests
{
    static IEnumerable<PelagForm> TableForms()
    {
        for (int f = 1; f <= PelagForms.Count; f++) yield return (PelagForm)f;
    }

    static RiftRun NewRun(ulong seed)
    {
        var run = new RiftRun(new Simulation(seed, 1024), PrototypeContent.Modules(), PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
        run.StartRun();
        return run;
    }

    [Test]
    public void EveryFormOfTheTable_HasNameAndOneLineDescription()
    {
        var titles = new HashSet<string>();
        foreach (PelagForm form in TableForms())
        {
            Assert.That(PelagFormTexts.Name(form), Is.Not.Empty, form + " без имени");
            string body = PelagFormTexts.Description(form);
            Assert.That(body, Is.Not.Empty, form + " без описания");
            Assert.That(body, Does.Not.Contain("\n"), "описание формы — одна строка");
            // Строка описания карточки — 712 единиц при 17 pt: около 70 знаков; с запасом на широкие буквы.
            Assert.That(body.Length, Is.LessThanOrEqualTo(60), form + ": описание не влезет в строку карточки");
            Assert.That(PelagFormTexts.ValueLabel(form), Is.Not.Empty);
            Assert.That(PelagFormTexts.Value(form), Is.Not.Empty);
            Assert.That(titles.Add(PelagFormTexts.Title(form)), Is.True, "две формы с одним названием");
        }
    }

    [Test]
    public void Title_IsSkillDotForm_WithoutRepeatingTheSkill()
    {
        Assert.That(PelagFormTexts.Title(PelagForm.WhirlwindStorm), Is.EqualTo("Вихрь · Буря"));
        Assert.That(PelagFormTexts.Title(PelagForm.WhirlwindMaelstrom), Is.EqualTo("Вихрь · Водоворот"));
        Assert.That(PelagFormTexts.Title(PelagForm.WhirlwindFoamWaves), Is.EqualTo("Вихрь · Пенные волны"));
        // Утверждённое имя уже называет навык — не «Вихрь · Вихрь на ходу».
        Assert.That(PelagFormTexts.Title(PelagForm.WhirlwindOnTheMove), Is.EqualTo("Вихрь на ходу"));
        Assert.That(PelagFormTexts.TooltipTitle("ВИХРЬ", PelagForm.WhirlwindStorm), Is.EqualTo("ВИХРЬ · БУРЯ"));
        Assert.That(PelagFormTexts.TooltipTitle("ВИХРЬ", PelagForm.None), Is.EqualTo("ВИХРЬ"), "без формы подсказка прежняя");
    }

    [Test]
    public void TooltipBody_PutsTheFormBeforeTheSkill_AndIsUnchangedWithoutForm()
    {
        const string skill = "Пелаг крутится с саблей.";
        Assert.That(PelagFormTexts.WithForm(skill, PelagForm.None), Is.EqualTo(skill));
        string withForm = PelagFormTexts.WithForm(skill, PelagForm.WhirlwindStorm);
        Assert.That(withForm, Does.StartWith("Форма «Буря». Держи клавишу"));
        Assert.That(withForm, Does.EndWith("\n" + skill));
    }

    [Test]
    public void DeveloperPreview_FillsThreeMarkedWhirlwindCards()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            RiftRun run = NewRun(seed);
            Assert.That(run.SkillFormsEnabled, Is.False, "предпросмотр F8 не включает формы обычных забегов");
            Assert.That(RunHudFormChoice.ShownCount(run), Is.EqualTo(0), "в бою экрана формы нет");
            Assert.That(run.DebugOpenFormScreen(), Is.True);
            Assert.That(run.ChoosingForm, Is.True);
            Assert.That(RunHudFormChoice.ShownCount(run), Is.EqualTo(3), "у Вихря четыре формы — экран полон");
            var forms = new HashSet<PelagForm>();
            for (int i = 0; i < RiftRun.RewardChoices; i++)
            {
                RunHudFormChoice.Card card = RunHudFormChoice.Describe(run.GetOffer(i));
                Assert.That(card.Shown, Is.True);
                Assert.That(card.Line, Is.EqualTo(PelagKit.StarterPoolIndex));
                Assert.That(card.DefinitionId, Is.EqualTo(AbilityDefinition.WhirlwindId), "иконка — Вихрь с меткой формы");
                Assert.That(card.Title, Does.StartWith("Вихрь"));
                Assert.That(card.Kind, Is.EqualTo("Форма навыка"));
                Assert.That(card.Body, Is.EqualTo(PelagFormTexts.Description(card.Form)));
                Assert.That(card.Tip, Does.Contain(card.Body).And.Contain("сменить нельзя"));
                Assert.That(forms.Add(card.Form), Is.True, "одна форма дважды на экране");
            }
            Assert.That(run.CanRerollReward, Is.False, "переброса на экране формы нет");
        }
    }

    [Test]
    public void ChosenForm_ReachesTheSlotBuild_ThatTheHudIconReads()
    {
        RiftRun run = NewRun(7);
        Assert.That(run.DebugOpenFormScreen(), Is.True);
        PelagForm chosen = run.GetOffer(2).Form;
        run.Step(new InputFrame { Command = (byte)RunCommand.ChooseReward3 });
        Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "после выбора — обратно в бой");
        AbilityBuild build = run.Sim.GetAbility(0);
        // Плитка HUD и подсказка берут иконку по (DefinitionId, Form) сборки слота.
        Assert.That(build.Form, Is.EqualTo(chosen));
        Assert.That(build.DefinitionId, Is.EqualTo(AbilityDefinition.WhirlwindId), "DefinitionId у формы прежний");
        Assert.That(AbilityIconRules.CacheKey(build.DefinitionId, build.Form),
            Is.Not.EqualTo(AbilityIconRules.CacheKey(build.DefinitionId, PelagForm.None)));
        Assert.That(run.Loadout.FormOf(0), Is.EqualTo(chosen), "экраны забега берут форму из набора");
    }

    [Test]
    public void Describe_HidesEmptyAndForeignPlaces()
    {
        Assert.That(RunHudFormChoice.Describe(RewardOffer.OfForm(0, PelagForm.None)).Shown, Is.False, "пустое место экрана");
        Assert.That(RunHudFormChoice.Describe(RewardOffer.OfForm(RunLoadout.EmptySlot, PelagForm.None)).Shown, Is.False);
        Assert.That(RunHudFormChoice.Describe(RewardOffer.OfForm(1, PelagForm.WhirlwindStorm)).Shown, Is.False, "форма Вихря на чужой линии");
        Assert.That(RunHudFormChoice.Describe(RewardOffer.OfSpring(40)).Shown, Is.False, "не форма");
        Assert.That(RunHudFormChoice.Describe(RewardOffer.OfForm(0, (PelagForm)200)).Shown, Is.False, "номер вне таблицы");
    }

    [Test]
    public void Hint_SameFormatAsTheRewardScreen()
    {
        Assert.That(RunHudFormChoice.Hint("1", "2", "3", "L"),
            Is.EqualTo(UiKeyHint.Join(UiKeyHint.Hint("выбрать", "1", "2", "3"), UiKeyHint.Hint("уйти с добычей", "L"))));
        Assert.That(PelagFormTexts.ScreenTitle, Is.EqualTo("Выбери форму"));
        Assert.That(PelagFormTexts.ScreenSubtitle, Is.EqualTo("Навык меняется до конца забега · сменить нельзя"));
    }

    [Test]
    public void FormTalentCard_HasTextsInsteadOfFallingOffTheTalentTable()
    {
        // Карточка Talent с номером от FormTalentBase не должна идти в таблицу талантов линии (там 8 номеров).
        Assert.That(PelagForms.FormTalentBase, Is.GreaterThanOrEqualTo(SabreTalents.TalentsPerLine));
        Assert.That(PelagFormTexts.TalentName(PelagForm.WhirlwindStorm, 1), Is.EqualTo("Буря · талант 2"));
        Assert.That(PelagFormTexts.TalentPending, Is.Not.Empty);
    }
}
