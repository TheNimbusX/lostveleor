using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Sim;
using Game.View;
using NUnit.Framework;
using Id = Game.View.UiKeywords.Id;
using Tone = Game.View.UiKeywords.Tone;

// Ключевые слова описаний (план, этап 4, п. 6; владелец 29.09 — «как в Hades»): словарь, авторская
// разметка {kw:…} и автопометка старых строк. Здесь проверяется, что словарь цел и честен к симуляции,
// что пометка находит нужные формы и не находит лишнего, не ломает чужой rich text, не вкладывает
// ссылку в ссылку, а видимый текст после неё остаётся буква в букву прежним.
public sealed class UiKeywordsTests
{
    static readonly Regex Tags = new Regex("<[^<>]+>");
    static readonly Regex Nested = new Regex("<link[^>]*>(?:(?!</link>).)*<link", RegexOptions.Singleline);

    const string Control = "#3BF0F5";

    /// <summary>Видимый текст: rich text без тегов.</summary>
    static string Visible(string rich) => Tags.Replace(rich, "");

    static string Link(string key, string shown, string hex = Control) =>
        "<link=\"kw:" + key + "\"><color=" + hex + ">" + shown + "</color></link>";

    static List<Id> Found(string text, bool autoTag = true)
    {
        var found = new List<Id>();
        UiKeywords.Markup(text, found, null, autoTag);
        return found;
    }

    static int Count(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ---- словарь ----

    [Test]
    public void EveryWordHasExactlyOneEntry()
    {
        var keys = new HashSet<string>();
        foreach (Id id in Enum.GetValues(typeof(Id)))
        {
            if (id == Id.None) { Assert.That(UiKeywords.Get(id), Is.Null); continue; }
            UiKeywords.Entry entry = UiKeywords.Get(id);
            Assert.That(entry, Is.Not.Null, id.ToString());
            Assert.That(entry.Id, Is.EqualTo(id));
            Assert.That(Regex.IsMatch(entry.Key, "^[a-z_]+$"), Is.True, "ключ разметки — строчная латиница: " + entry.Key);
            Assert.That(keys.Add(entry.Key), Is.True, "ключ повторяется: " + entry.Key);
            Assert.That(entry.Icon, Is.EqualTo("kw_" + entry.Key));
            Assert.That(entry.LinkId, Is.EqualTo("kw:" + entry.Key));
        }
        Assert.That(UiKeywords.All.Count, Is.EqualTo(Enum.GetValues(typeof(Id)).Length - 1));
    }

    [Test]
    public void DefinitionIsOneSentence()
    {
        foreach (UiKeywords.Entry entry in UiKeywords.All)
        {
            Assert.That(entry.Title, Is.Not.Empty, entry.Key);
            Assert.That(entry.Definition, Does.EndWith("."), entry.Key);
            Assert.That(Regex.IsMatch(entry.Definition, @"[.!?]\s+[А-ЯЁA-Z]"), Is.False,
                "в определении одно предложение: " + entry.Definition);
        }
    }

    [Test]
    public void TitleTagsAsItsOwnWord()
    {
        // Название — тоже словоформа: «Защита от контроля» в тексте находится целиком.
        foreach (UiKeywords.Entry entry in UiKeywords.All)
        {
            var found = new List<Id>();
            string tagged = UiKeywords.Markup(entry.Title, found);
            Assert.That(tagged, Is.EqualTo(Link(entry.Key, entry.Title, UiKeywords.DefaultHex(entry.Tone))), entry.Key);
            Assert.That(found, Is.EqualTo(new[] { entry.Id }));
        }
    }

    [Test]
    public void EveryFormBelongsToOneWord()
    {
        var owner = new Dictionary<string, Id>();
        foreach (UiKeywords.Entry entry in UiKeywords.All)
            foreach (string form in entry.Forms)
            {
                Assert.That(form, Is.Not.Empty, entry.Key);
                Assert.That(form, Is.EqualTo(form.ToLowerInvariant()), "формы строчные: " + form);
                Assert.That(form.IndexOf('ё'), Is.EqualTo(-1), "ё в формах уже заменена: " + form);
                Assert.That(form, Does.Not.Contain("  "));
                Assert.That(form.Trim(), Is.EqualTo(form));
                Assert.That(owner.ContainsKey(form), Is.False,
                    "форма «" + form + "» у двух слов: " + (owner.ContainsKey(form) ? owner[form].ToString() : "") + " и " + entry.Id);
                owner[form] = entry.Id;
            }
    }

    [Test]
    public void NumbersComeFromTheSim()
    {
        Assert.That(UiKeywords.Seconds(30), Is.EqualTo("1 с"));
        Assert.That(UiKeywords.Seconds(45), Is.EqualTo("1,5 с"));
        Assert.That(UiKeywords.Seconds(180), Is.EqualTo("6 с"));
        Assert.That(UiKeywords.Seconds(9), Is.EqualTo("0,3 с"));

        // Корни 1 с, защита 1,5 с — решение владельца 29.09 (Simulation.HeroSlow).
        Assert.That(UiKeywords.Get(Id.Roots).Definition, Does.Contain(UiKeywords.Seconds(Simulation.RootSnarerRootTicks)));
        Assert.That(UiKeywords.Get(Id.ControlImmunity).Definition, Does.Contain(UiKeywords.Seconds(Simulation.HeroControlImmunityTicks)));
        Assert.That(UiKeywords.Get(Id.ControlImmunity).Definition, Does.Contain("1,5 с"));
        Assert.That(UiKeywords.Get(Id.Stun).Definition, Does.Contain(UiKeywords.Seconds(Simulation.StonehoofChargeStunTicks)));
        Assert.That(UiKeywords.Get(Id.Slow).Definition, Does.Contain(Simulation.WendigoHowlSlowPercent + "%"));
        Assert.That(UiKeywords.Get(Id.Slow).Definition, Does.Contain(UiKeywords.Seconds(Simulation.WendigoHowlSlowTicks)));
        Assert.That(UiKeywords.Get(Id.Freeze).Definition, Does.Contain("+" + Simulation.WinterShatterDamage + " урона"));
        Assert.That(UiKeywords.Get(Id.Resin).Definition, Does.Contain(UiKeywords.Seconds(Simulation.PotionEffectTicks)));
        Assert.That(UiKeywords.Get(Id.Surge).Definition, Does.Contain(UiKeywords.Seconds(Simulation.PotionEffectTicks)));
        Assert.That(UiKeywords.Get(Id.FireResist).Definition, Does.Contain("75%"));
        Assert.That(UiKeywords.Get(Id.Boss).Definition, Does.Contain(Simulation.BossAddFirstPercent + "%"));
        Assert.That(UiKeywords.Get(Id.Boss).Definition, Does.Contain(Simulation.BossAddSecondPercent + "%"));
    }

    [Test]
    public void RootsDefinitionNamesEveryAbilityRootsHold()
    {
        // MovesHero (Simulation.Tempo): кувырок, Выпад, Отскок, Абордаж, Шаг по цепи — в игре их имена такие.
        string definition = UiKeywords.Get(Id.Roots).Definition;
        foreach (int id in new[] { AbilityDefinition.DashId, AbilityDefinition.SkewerId, AbilityDefinition.BackblastId,
                     AbilityDefinition.AnchorLeapId, AbilityDefinition.ChainStepId })
            Assert.That(Simulation.MovesHero(id), Is.True);
        foreach (string name in new[] { "рывок", "«На вылет»", "«Отбой»", "«Абордаж»", "«Шквал»" })
            Assert.That(definition, Does.Contain(name));
        foreach (int id in new[] { AbilityDefinition.WhirlwindId, AbilityDefinition.CleaveId, AbilityDefinition.BlazeId,
                     AbilityDefinition.AnchorSlamId, AbilityDefinition.WreckId, AbilityDefinition.FireFlaskId })
            Assert.That(Simulation.MovesHero(id), Is.False, "остальные корни не держат");
    }

    // ---- автопометка ----

    [Test]
    public void TagsWordInsideDescription()
    {
        var found = new List<Id>();
        string tagged = UiKeywords.Markup("Удар якорем перед собой с коротким оглушением.", found);
        Assert.That(tagged, Is.EqualTo("Удар якорем перед собой с коротким " + Link("stun", "оглушением") + "."));
        Assert.That(found, Is.EqualTo(new[] { Id.Stun }));
    }

    [Test]
    public void KeepsAuthorCaseAtSentenceStart()
    {
        string tagged = UiKeywords.Markup("Оглушение длится 1 секунду вместо 0,5.");
        Assert.That(tagged, Does.StartWith(Link("stun", "Оглушение")));
        Assert.That(UiKeywords.Markup("КОНЦЕНТРАЦИИ НЕ ХВАТАЕТ"), Does.StartWith(Link("lavidium", "КОНЦЕНТРАЦИИ", "#FA883C")));
    }

    [Test]
    public void YoAndYeAreTheSameLetter()
    {
        Assert.That(Found("+50% урона по оглушенным врагам."), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(Found("по замерзшему"), Is.EqualTo(new[] { Id.Freeze }));
        Assert.That(UiKeywords.Markup("по оглушённым"), Does.Contain(">оглушённым<"), "написание автора сохраняется");
    }

    [Test]
    public void OnlyWholeWords()
    {
        Assert.That(Found("Корнехват бьёт корнями."), Is.EqualTo(new[] { Id.Roots }));
        Assert.That(UiKeywords.Markup("Корнехват бьёт корнями."), Does.StartWith("Корнехват бьёт <link"));
        Assert.That(Found("Раскритиковал критерий кританского стана."), Is.Empty);
        Assert.That(Found("Не хватает концентрации: 15"), Is.EqualTo(new[] { Id.Lavidium }));
        Assert.That(Found("Отбой: Пелаг отскакивает."), Is.Empty, "«Отбой» — способность, не отброс");
    }

    [Test]
    public void LongestFormWins()
    {
        // Зелье «Порыв» (с 01.10 без «Лавидиевого»): имя зелья — слово словаря целиком.
        string surge = UiKeywords.Markup("Выпей Порыв.");
        Assert.That(surge, Is.EqualTo("Выпей " + Link("surge", "Порыв", "#A765FF") + "."));
        Assert.That(Found("Удар под горящей саблей."), Is.EqualTo(new[] { Id.Blaze }));
        Assert.That(Found("Бутылка, упавшая в горящую лужу, взрывается."), Is.EqualTo(new[] { Id.Burn }));
        Assert.That(Found("Под защитой от контроля."), Is.EqualTo(new[] { Id.ControlImmunity }));
        Assert.That(Found("Под защитой от контроля."), Is.EqualTo(new[] { Id.ControlImmunity }), "неразрывный пробел");
        Assert.That(Found("Обычные атаки под огнём поджигают цель: 3 секунды."), Is.EqualTo(new[] { Id.Blaze, Id.Burn }));
    }

    [Test]
    public void AmbiguousVerbsNeedAPhrase()
    {
        // «Поджигает саблю» — это «Ладно смазал», а не горение цели; «горит» — и сабля, и лужа.
        Assert.That(Found("Поджигает саблю и повышает уклонение."), Is.EqualTo(new[] { Id.Evasion }));
        Assert.That(Found("Лужа горит 8 секунд вместо 5."), Is.Empty);
        Assert.That(Found("Сабля горит 5 секунд вместо 3."), Is.EqualTo(new[] { Id.Blaze }));
        Assert.That(Found("Замах Удара якорем короче на 40%."), Is.Empty, "свой замах Пелага — не срыв замаха");
        Assert.That(Found("Сколько урона Пелаг выдержит. Растёт с уровнем и от брони и талисманов."), Is.Empty,
            "«от брони» — надетые вещи, а не характеристика");
    }

    [Test]
    public void LeavesExistingRichTextAlone()
    {
        string colored = UiKeywords.Markup("<b>Рука</b>  <color=#93A2BC>оглушает на 0,5 с</color>");
        Assert.That(colored, Is.EqualTo("<b>Рука</b>  <color=#93A2BC>" + Link("stun", "оглушает") + " на 0,5 с</color>"));

        string linked = UiKeywords.Markup("<link=\"x\">оглушение</link> и оглушение");
        Assert.That(linked, Is.EqualTo("<link=\"x\">оглушение</link> и " + Link("stun", "оглушение")));

        const string noparse = "<noparse><b>оглушение</b></noparse>";
        Assert.That(UiKeywords.Markup(noparse), Is.SameAs(noparse));

        string sprite = UiKeywords.Markup("<sprite name=\"крит\"> крит");
        Assert.That(sprite, Is.EqualTo("<sprite name=\"крит\"> " + Link("crit", "крит", "#A765FF")));

        string less = UiKeywords.Markup("урон < 5 и оглушение");
        Assert.That(less, Is.EqualTo("урон < 5 и " + Link("stun", "оглушение")));
    }

    [Test]
    public void SecondPassChangesNothing()
    {
        foreach (string text in Samples)
        {
            string once = UiKeywords.Markup(text);
            Assert.That(UiKeywords.Markup(once), Is.EqualTo(once), text);
            Assert.That(Nested.IsMatch(once), Is.False, "ссылка в ссылке: " + once);
            Assert.That(Count(once, "<link"), Is.EqualTo(Count(once, "</link>")));
            // Авторская разметка раскрывается в свою форму; остальной видимый текст — буква в букву.
            if (!text.Contains("{kw:")) Assert.That(Visible(once), Is.EqualTo(Visible(text)), "видимый текст не меняется");
        }
    }

    [Test]
    public void NamesInGuillemetsStayPlain()
    {
        string tagged = UiKeywords.Markup("Усиление «Неуязвимость»: 3 секунды неуязвим.");
        Assert.That(tagged, Is.EqualTo("Усиление «Неуязвимость»: 3 секунды " + Link("invulnerability", "неуязвим", "#8FE3A8") + "."));
        Assert.That(Found("После «Ладно смазал» и «Долгий стан»"), Is.Empty);
    }

    [Test]
    public void EveryOccurrenceTaggedButListedOnce()
    {
        var found = new List<Id>();
        string tagged = UiKeywords.Markup("Корни, оглушение и снова корни.", found);
        Assert.That(Count(tagged, "<link"), Is.EqualTo(3));
        Assert.That(found, Is.EqualTo(new[] { Id.Roots, Id.Stun }));
    }

    [Test]
    public void NothingToTagReturnsSameString()
    {
        const string plain = "Мощный удар саблей перед собой.";
        Assert.That(UiKeywords.Markup(plain), Is.SameAs(plain));
        Assert.That(UiKeywords.Markup(null), Is.Null);
        Assert.That(UiKeywords.Markup(string.Empty), Is.Empty);
    }

    // ---- авторская разметка ----

    [Test]
    public void AuthoredMarkupExpands()
    {
        Assert.That(UiKeywords.Markup("{kw:roots} держат 1 с."), Is.EqualTo(Link("roots", "Корни") + " держат 1 с."));
        Assert.That(UiKeywords.Markup("Пелаг скован {kw:roots|корнями}."), Is.EqualTo("Пелаг скован " + Link("roots", "корнями") + "."));
        Assert.That(UiKeywords.Markup("{kw:roots|}"), Is.EqualTo(Link("roots", "Корни")), "пустая форма — название");
        Assert.That(Found("{kw:crit|верный удар}"), Is.EqualTo(new[] { Id.Crit }), "своя форма может быть любой");
    }

    [Test]
    public void BrokenMarkupStaysVisible()
    {
        // Опечатку в ключе должно быть видно в игре, а не пустое место.
        foreach (string text in new[] { "{kw:nope|слово}", "{kw:roots", "{0} и {kw}", "{kw:roots\n}", "{KW:roots}" })
        {
            var found = new List<Id>();
            Assert.That(UiKeywords.Markup(text, found, null, autoTag: false), Is.SameAs(text), text);
            Assert.That(found, Is.Empty);
        }
        Assert.That(Found("{kw:nope|оглушение}"), Is.Empty, "внутри битой разметки автопометки нет");
    }

    [Test]
    public void MarkupWithoutAutoTag()
    {
        var found = new List<Id>();
        string tagged = UiKeywords.Markup("{kw:stun|Оглушение} и корни", found, null, autoTag: false);
        Assert.That(tagged, Is.EqualTo(Link("stun", "Оглушение") + " и корни"));
        Assert.That(found, Is.EqualTo(new[] { Id.Stun }));
    }

    [Test]
    public void MarkupInsideExistingLinkIsPlain()
    {
        Assert.That(UiKeywords.Markup("<link=\"x\">{kw:roots|корнями}</link>"), Is.EqualTo("<link=\"x\">корнями</link>"));
    }

    [Test]
    public void ColourComesFromTheResolver()
    {
        string tagged = UiKeywords.Markup("крит и корни", null, tone => tone == Tone.Offense ? "#123456" : null);
        Assert.That(tagged, Is.EqualTo(Link("crit", "крит", "#123456") + " и " + Link("roots", "корни")),
            "пустой ответ — запасной цвет");
    }

    [Test]
    public void LinkIdsRoundTrip()
    {
        foreach (UiKeywords.Entry entry in UiKeywords.All)
        {
            Assert.That(UiKeywords.TryParseLink(entry.LinkId, out Id id), Is.True, entry.Key);
            Assert.That(id, Is.EqualTo(entry.Id));
            Assert.That(UiKeywords.TryGet(entry.Key, out UiKeywords.Entry byKey), Is.True);
            Assert.That(byKey, Is.SameAs(entry));
        }
        foreach (string bad in new[] { null, "", "kw:", "roots", "kw:nope", "KW:roots", "kw:Roots" })
        {
            Assert.That(UiKeywords.TryParseLink(bad, out Id id), Is.False, bad ?? "null");
            Assert.That(id, Is.EqualTo(Id.None));
        }
    }

    // ---- вложенная подсказка ----

    [Test]
    public void DefinitionLinksOtherWordsButNotItself()
    {
        foreach (UiKeywords.Entry entry in UiKeywords.All)
        {
            var found = new List<Id>();
            string tagged = UiKeywords.DefinitionMarkup(entry.Id, found);
            Assert.That(found, Does.Not.Contain(entry.Id), entry.Key);
            Assert.That(tagged, Does.Not.Contain("\"" + entry.LinkId + "\""), entry.Key);
            Assert.That(Visible(tagged), Is.EqualTo(entry.Definition));
        }
        Assert.That(DefinitionFound(Id.ControlImmunity), Is.EqualTo(new[] { Id.Roots, Id.Stun, Id.Slow, Id.Knockback }));
        Assert.That(DefinitionFound(Id.Stun), Does.Contain(Id.Interrupt));
        Assert.That(DefinitionFound(Id.Freeze), Is.EquivalentTo(new[] { Id.Stun, Id.Boss, Id.Elite, Id.Slow }));
        Assert.That(DefinitionFound(Id.Evasion), Does.Contain(Id.Blaze));
        Assert.That(DefinitionFound(Id.Surge), Is.Empty, "своё имя «Порыв» себя не метит");
        Assert.That(UiKeywords.DefinitionMarkup(Id.None), Is.Empty);
    }

    static List<Id> DefinitionFound(Id id)
    {
        var found = new List<Id>();
        UiKeywords.DefinitionMarkup(id, found);
        return found;
    }

    // ---- настоящие описания ----

    [Test]
    public void EveryTalentDescriptionKeepsItsText()
    {
        foreach (SabreTalentLine line in Enum.GetValues(typeof(SabreTalentLine)))
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++)
            {
                string text = SabreTalentTexts.Description(line, i);
                string tagged = UiKeywords.Markup(text);
                Assert.That(Visible(tagged), Is.EqualTo(text), line + " " + i);
                Assert.That(Nested.IsMatch(tagged), Is.False);
            }
    }

    [Test]
    public void TalentDescriptionsFindTheirTerms()
    {
        Assert.That(TalentFound(SabreTalentLine.AnchorSlam, 1), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(TalentFound(SabreTalentLine.AnchorSlam, 3), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(TalentFound(SabreTalentLine.AnchorSlam, 5), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(TalentFound(SabreTalentLine.Wreck, 2), Is.EqualTo(new[] { Id.Elite, Id.Boss }));
        Assert.That(TalentFound(SabreTalentLine.Wreck, 4), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(TalentFound(SabreTalentLine.Blaze, 0), Is.EqualTo(new[] { Id.Burn }), "след жжёт огнём по времени");
        Assert.That(TalentFound(SabreTalentLine.Blaze, 2), Is.EqualTo(new[] { Id.Evasion, Id.Blaze, Id.Lavidium }));
        Assert.That(TalentFound(SabreTalentLine.Blaze, 3), Is.EqualTo(new[] { Id.Blaze, Id.Burn }));
        Assert.That(TalentFound(SabreTalentLine.Blaze, 4), Is.EqualTo(new[] { Id.Blaze }));
        Assert.That(TalentFound(SabreTalentLine.Blaze, 5), Is.Empty, "«поджог сабли» — момент каста, не горение");
        Assert.That(TalentFound(SabreTalentLine.Boarding, 6), Is.EqualTo(new[] { Id.Interrupt }));
        Assert.That(TalentFound(SabreTalentLine.Boarding, 7), Is.EqualTo(new[] { Id.Crit }));
        Assert.That(TalentFound(SabreTalentLine.Whirlwind, 3), Is.EqualTo(new[] { Id.Lavidium }));
        Assert.That(TalentFound(SabreTalentLine.Whirlwind, 5), Is.EqualTo(new[] { Id.Pull }));
        Assert.That(TalentFound(SabreTalentLine.Flask, 3), Is.EqualTo(new[] { Id.Burn }));
    }

    static List<Id> TalentFound(SabreTalentLine line, int index) => Found(SabreTalentTexts.Description(line, index));

    [Test]
    public void OtherTablesFindTheirTerms()
    {
        // Копии строк PlayerHud.AbilityDescription, RunArtifactTexts.Effect и CampServiceText: сами таблицы
        // тянут Unity и в эту сборку не входят.
        Assert.That(Found("Удар якорем перед собой с коротким оглушением."), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(Found("Три удара якорем: нажимай повторно. Последний оглушает."), Is.EqualTo(new[] { Id.Stun }));
        Assert.That(Found("Взрыв в выбранной точке оставляет горящую область."), Is.EqualTo(new[] { Id.Burn }));
        Assert.That(Found("Поджигает саблю и повышает уклонение. Можно применять на бегу."), Is.EqualTo(new[] { Id.Evasion }));
        Assert.That(Found("4 секунды полной неуязвимости. Атаковать можно."), Is.EqualTo(new[] { Id.Invulnerability }));
        Assert.That(Found("Враги в 8 м замерзают на 5 секунд; удар способностью по замёрзшему раскалывает лёд: +60 урона. "
            + "Босс и элита — замедление на 40%."), Is.EqualTo(new[] { Id.Freeze, Id.Boss, Id.Elite, Id.Slow }));
        Assert.That(Found("Смертельный удар не убивает: Пелаг встаёт с половиной здоровья, 3 секунды неуязвим, "
            + "враги рядом отброшены. Один раз за забег."), Is.EqualTo(new[] { Id.Invulnerability, Id.Knockback }));
        Assert.That(Found("Шанс крита"), Is.EqualTo(new[] { Id.Crit }));
        Assert.That(Found("Во сколько раз критический удар сильнее обычного: 150% — в полтора раза."), Is.EqualTo(new[] { Id.Crit }));
        Assert.That(Found("Запас концентрации — ресурса способностей. Растёт с уровнем."), Is.EqualTo(new[] { Id.Lavidium }));
        Assert.That(Found("Сопротивление огню"), Is.EqualTo(new[] { Id.FireResist }));
        Assert.That(Found("Снижает физический урон. Чем сильнее удар, тем меньше броня от него спасает."), Is.EqualTo(new[] { Id.Armor }));
        Assert.That(Found("Живица"), Is.EqualTo(new[] { Id.Resin }));
        Assert.That(Found("20% концентрации и +20% скорости на 6 с"), Is.EqualTo(new[] { Id.Lavidium }));
    }

    // ---- ресурс способностей: «Концентрация» ----

    [Test]
    public void ResourceWordIsConcentration()
    {
        // Владелец 01.10: «лавидий у Пелага звучит глупо» — ресурс способностей игроку «Концентрация».
        // Id.Lavidium и ключ разметки «lavidium» — стабильные id, их не меняем.
        UiKeywords.Entry entry = UiKeywords.Get(Id.Lavidium);
        Assert.That(entry.Title, Is.EqualTo("Концентрация"));
        Assert.That(entry.Key, Is.EqualTo("lavidium"));
        Assert.That(entry.Definition, Does.Contain("концентрацию"));
        foreach (string form in new[] { "Концентрация", "концентрации", "концентрацию", "концентрацией" })
            Assert.That(Found(form), Is.EqualTo(new[] { Id.Lavidium }), form);
    }

    [Test]
    public void LavidiumIsNoLongerTheResource()
    {
        // Лавидий остаётся металлом и валютой мира («Кольцо с лавидием»): подсказку ресурса он не открывает,
        // и ни одно определение, усиление или отказ слота его больше не называют.
        Assert.That(Found("Кольцо с лавидием"), Is.Empty);
        Assert.That(Found("Лавидий, лавидия, лавидию, лавидием"), Is.Empty);
        foreach (UiKeywords.Entry entry in UiKeywords.All)
            Assert.That(entry.Definition.ToLowerInvariant(), Does.Not.Contain("лавиди"), entry.Key);
        foreach (SabreTalentLine line in Enum.GetValues(typeof(SabreTalentLine)))
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++)
            {
                Assert.That(SabreTalentTexts.Name(line, i).ToLowerInvariant(), Does.Not.Contain("лавиди"), line + " " + i);
                Assert.That(SabreTalentTexts.Description(line, i).ToLowerInvariant(), Does.Not.Contain("лавиди"), line + " " + i);
            }
        Assert.That(SabreTalentTexts.Name(SabreTalentLine.Whirlwind, 3), Is.EqualTo("Возврат концентрации"));
        Assert.That(HudAbilityAvailability.Evaluate(true, false, 0, 10, 25, false).Text, Is.EqualTo("Не хватает концентрации: 15"));
        Assert.That(RunHudCompare.AbilityStatName(SabreTalentLine.Whirlwind, AbilityStatType.LavidiumCost), Is.EqualTo("Концентрация"));
    }

    static readonly string[] Samples =
    {
        "Удар якорем перед собой с коротким оглушением.",
        "Оглушение длится 1 секунду вместо 0,5.",
        "Обычные атаки под огнём поджигают цель: 3 секунды, 10% урона удара в секунду.",
        "Враги в 8 м замерзают на 5 секунд; удар способностью по замёрзшему раскалывает лёд: +60 урона. Босс и элита — замедление на 40%.",
        "<b>Долгий стан</b>  <color=#93A2BC>Оглушение длится 1 секунду вместо 0,5.</color>",
        "Пелаг скован {kw:roots|корнями}, а {kw:control_immunity} потом держит 1,5 с.",
        "Выпей Порыв под защитой от контроля.",
        "Усиление «Неуязвимость»: 3 секунды неуязвим.",
        "урон < 5 и оглушение",
    };
}
