using System.Globalization;
using Game.View;
using NUnit.Framework;
using TextStep = Game.View.UiTheme.TextStep;

// Единый набор (этап 4, лист 5 «один язык», выбор владельца 30.09: «5 ок» — эталон для единообразия): живые токены
// UiTheme (шкала текста 56/32/24/18/14, кейкап, кнопки, отступы, цвета по умолчанию) и один формат подсказки
// клавиши «[Esc] Закрыть» вместо шести прежних.
public sealed class UiConsistencyTests
{
    // ---------------------------------------------------------------- шкала текста

    [Test]
    public void TextScaleIsSheetFive()
    {
        Assert.That(UiTheme.DefaultSize(TextStep.Display), Is.EqualTo(56f));
        Assert.That(UiTheme.DefaultSize(TextStep.Title), Is.EqualTo(32f));
        Assert.That(UiTheme.DefaultSize(TextStep.Heading), Is.EqualTo(24f));
        Assert.That(UiTheme.DefaultSize(TextStep.Body), Is.EqualTo(18f));
        Assert.That(UiTheme.DefaultSize(TextStep.Caption), Is.EqualTo(14f));
    }

    [Test]
    public void StepsGoFromLargestToSmallest()
    {
        for (int i = 1; i <= (int)TextStep.Caption; i++)
            Assert.That(UiTheme.DefaultSize((TextStep)i), Is.LessThan(UiTheme.DefaultSize((TextStep)(i - 1))), ((TextStep)i).ToString());
    }

    [TestCase(56f, TextStep.Display)]
    [TestCase(58f, TextStep.Display)]
    [TestCase(100f, TextStep.Display)]
    [TestCase(32f, TextStep.Title)]
    [TestCase(26f, TextStep.Heading)]
    [TestCase(22f, TextStep.Heading)]
    [TestCase(20f, TextStep.Body)]
    [TestCase(17f, TextStep.Body)]
    [TestCase(15f, TextStep.Caption)]
    [TestCase(0f, TextStep.Caption)]
    public void NearestStepPicksClosestSize(float size, TextStep expected)
    {
        Assert.That(UiTheme.NearestStep(size), Is.EqualTo(expected));
    }

    /// <summary>Ровно посередине — меньшая ступень: текст, приведённый к шкале, не вылезает из рамки.</summary>
    [TestCase(16f, TextStep.Caption)]
    [TestCase(21f, TextStep.Body)]
    [TestCase(28f, TextStep.Heading)]
    [TestCase(44f, TextStep.Title)]
    public void TieGoesToSmallerStep(float size, TextStep expected)
    {
        Assert.That(UiTheme.NearestStep(size), Is.EqualTo(expected));
    }

    [Test]
    public void OnScaleOnlyForSheetSizes()
    {
        foreach (float size in new[] { 56f, 32f, 24f, 18f, 14f }) Assert.That(UiTheme.OnScale(size), Is.True, size.ToString(CultureInfo.InvariantCulture));
        foreach (float size in new[] { 13f, 15f, 16f, 17f, 20f, 26f, 44f, 58f }) Assert.That(UiTheme.OnScale(size), Is.False, size.ToString(CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- кейкап

    [Test]
    public void OneLetterKeycapIsSquare()
    {
        Assert.That(UiTheme.KeycapWidth(12f, 38f, 1), Is.EqualTo(38f));
        Assert.That(UiTheme.KeycapWidth(0f, 30f, 0), Is.EqualTo(30f), "пустая подпись — тоже квадрат");
    }

    [Test]
    public void LongLabelWidensByTextAndPadding()
    {
        float height = UiTheme.KeycapSizeDefault;
        float width = UiTheme.KeycapWidth(40f, height, 3);
        Assert.That(width, Is.EqualTo(40f + height * UiTheme.KeycapPadRatio).Within(.001f));
    }

    [Test]
    public void ShortLongLabelIsNeverNarrowerThanSquare()
    {
        Assert.That(UiTheme.KeycapWidth(4f, 38f, 2), Is.EqualTo(38f));
    }

    [Test]
    public void VeryLongLabelStopsAtMaxWidth()
    {
        Assert.That(UiTheme.KeycapWidth(1000f, 30f, 20), Is.EqualTo(30f * UiTheme.KeycapMaxWidthRatio).Within(.001f));
        Assert.That(UiTheme.KeycapWidth(1000f, 30f, 20, 2f), Is.EqualTo(60f).Within(.001f), "свой предел строки назначения");
        Assert.That(UiTheme.KeycapWidth(1000f, 30f, 20, .2f), Is.EqualTo(30f).Within(.001f), "предел меньше квадрата — квадрат");
    }

    [Test]
    public void ZeroHeightKeycapHasNoWidth()
    {
        Assert.That(UiTheme.KeycapWidth(50f, 0f, 5), Is.EqualTo(0f));
    }

    [Test]
    public void KeycapCornerFollowsSheetAndKeepsMinimum()
    {
        Assert.That(UiTheme.KeycapCorner(38f), Is.EqualTo(8f).Within(.001f));
        Assert.That(UiTheme.KeycapCorner(76f), Is.EqualTo(16f).Within(.001f));
        Assert.That(UiTheme.KeycapCorner(10f), Is.EqualTo(4f), "мелкий кейкап не становится острым квадратом");
    }

    [Test]
    public void KeycapAndButtonTokensMatchSheet()
    {
        Assert.That(UiTheme.KeycapSizeDefault, Is.EqualTo(38f));
        Assert.That(UiTheme.KeycapLetterRatio * UiTheme.KeycapSizeDefault, Is.EqualTo(19f).Within(.001f), "буква кейкапа — Nunito 19 на 38");
        Assert.That(UiTheme.KeycapSmallDefault, Is.LessThan(UiTheme.KeycapSizeDefault));
        Assert.That(UiTheme.ButtonHeightDefault, Is.EqualTo(56f));
        Assert.That(UiTheme.ButtonHeightSmallDefault, Is.LessThan(UiTheme.ButtonHeightDefault));
        Assert.That(new[] { UiTheme.SpaceXSDefault, UiTheme.SpaceSDefault, UiTheme.SpaceMDefault, UiTheme.SpaceLDefault, UiTheme.SpaceXLDefault },
            Is.Ordered.Ascending);
    }

    // ---------------------------------------------------------------- цвета

    [Test]
    public void ThemeHexTokensAreSixDigits()
    {
        foreach (string hex in new[]
                 {
                     UiTheme.AccentHex, UiTheme.AccentHoverHex, UiTheme.AccentPressedHex, UiTheme.TextHex, UiTheme.TextMutedHex,
                     UiTheme.TextOnAccentHex, UiTheme.CommonHex, UiTheme.RareHex, UiTheme.EpicHex, UiTheme.UniqueHex,
                     UiTheme.LavidiumHex, UiTheme.HealthHex, UiTheme.CoinsHex, UiTheme.GoodHex, UiTheme.BadHex,
                 })
        {
            Assert.That(hex.Length, Is.EqualTo(6), hex);
            Assert.That(int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _), Is.True, hex);
        }
    }

    /// <summary>Один акцент и одна палитра редкости: ключевые слова без темы берут те же умолчания, что и тема.</summary>
    [Test]
    public void KeywordFallbackColoursComeFromThemeTokens()
    {
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Control), Is.EqualTo("#" + UiTheme.RareHex));
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Defense), Is.EqualTo("#" + UiTheme.GoodHex));
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Fire), Is.EqualTo("#" + UiTheme.UniqueHex));
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Resource), Is.EqualTo("#" + UiTheme.LavidiumHex));
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Offense), Is.EqualTo("#" + UiTheme.EpicHex));
        Assert.That(UiKeywords.DefaultHex(UiKeywords.Tone.Enemy), Is.EqualTo("#" + UiTheme.BadHex));
    }

    [Test]
    public void AccentIsNotARarityColour()
    {
        foreach (string rarity in new[] { UiTheme.CommonHex, UiTheme.RareHex, UiTheme.EpicHex, UiTheme.UniqueHex })
            Assert.That(rarity, Is.Not.EqualTo(UiTheme.AccentHex));
    }

    // ---------------------------------------------------------------- «[Esc] Закрыть»

    [Test]
    public void HintIsKeyInBracketsThenCapitalisedVerb()
    {
        Assert.That(UiKeyHint.Hint("закрыть", "Esc"), Is.EqualTo("[Esc] Закрыть"));
        Assert.That(UiKeyHint.Hint("Закрыть", "Esc"), Is.EqualTo("[Esc] Закрыть"));
        Assert.That(UiKeyHint.Hint("уйти с добычей", "L"), Is.EqualTo("[L] Уйти с добычей"), "остальные буквы не трогаются");
    }

    [Test]
    public void SeveralKeysOfOneActionEachGetBrackets()
    {
        Assert.That(UiKeyHint.Hint("отмена", "ПКМ", "Esc"), Is.EqualTo("[ПКМ] [Esc] Отмена"));
        Assert.That(UiKeyHint.Hint("выбрать", "1", "2", "3"), Is.EqualTo("[1] [2] [3] Выбрать"));
        Assert.That(UiKeyHint.Hint("заменить слот", "1", "2", "3", "4"), Is.EqualTo("[1] [2] [3] [4] Заменить слот"));
    }

    [Test]
    public void EmptyKeysAreSkipped()
    {
        Assert.That(UiKeyHint.Hint("закрыть", null, "", " ", "Esc"), Is.EqualTo("[Esc] Закрыть"));
        Assert.That(UiKeyHint.Hint("выбрать путь", null), Is.EqualTo("Выбрать путь"));
        Assert.That(UiKeyHint.Hint(null, "Esc"), Is.EqualTo("[Esc]"));
        Assert.That(UiKeyHint.Hint("", ""), Is.EqualTo(""));
    }

    [Test]
    public void KeyAndVerbTrimSpaces()
    {
        Assert.That(UiKeyHint.Key(" Esc "), Is.EqualTo("[Esc]"));
        Assert.That(UiKeyHint.Key(null), Is.EqualTo(""));
        Assert.That(UiKeyHint.Verb("  назад "), Is.EqualTo("Назад"));
        Assert.That(UiKeyHint.Verb("ёлка"), Is.EqualTo("Ёлка"));
        Assert.That(UiKeyHint.Verb("1–4"), Is.EqualTo("1–4"), "не буква — как есть");
    }

    [Test]
    public void ActionsJoinWithOneSeparator()
    {
        string apply = UiKeyHint.Hint("применить", "ЛКМ");
        string cancel = UiKeyHint.Hint("отмена", "ПКМ", "Esc");
        Assert.That(UiKeyHint.Join(apply, cancel), Is.EqualTo("[ЛКМ] Применить" + UiKeyHint.Separator + "[ПКМ] [Esc] Отмена"));
        Assert.That(UiKeyHint.Join(apply, null), Is.EqualTo(apply));
        Assert.That(UiKeyHint.Join("", cancel), Is.EqualTo(cancel));
        Assert.That(UiKeyHint.Join("a", "b", "c"), Is.EqualTo("a" + UiKeyHint.Separator + "b" + UiKeyHint.Separator + "c"));
        Assert.That(UiKeyHint.Join(null, null), Is.EqualTo(""));
    }

    [Test]
    public void ReadyHintsMatchTheFormat()
    {
        Assert.That(UiKeyHint.EscClose, Is.EqualTo(UiKeyHint.Hint("закрыть", "Esc")));
        Assert.That(UiKeyHint.EscCancel, Is.EqualTo(UiKeyHint.Hint("отмена", "Esc")));
        Assert.That(UiKeyHint.EscBack, Is.EqualTo(UiKeyHint.Hint("назад", "Esc")));
    }

    /// <summary>Подсказки прицела способности — те же клавиши и тот же формат, что собирает Hint/Join.</summary>
    [Test]
    public void AimHintsMatchTheFormat()
    {
        Assert.That(UiKeyHint.AimMouse, Is.EqualTo(UiKeyHint.Join(UiKeyHint.Hint("применить", "ЛКМ"), UiKeyHint.Hint("отмена", "ПКМ", "Esc"))));
        Assert.That(UiKeyHint.AimPad, Is.EqualTo(UiKeyHint.Join(UiKeyHint.Hint("прицел", "Правый стик"), UiKeyHint.Hint("применить", "RT", "A"),
            UiKeyHint.Hint("отмена", "B"))));
        Assert.That(UiKeyHint.AimStatus, Is.EqualTo(UiKeyHint.Join("Выбери цель", UiKeyHint.Hint("отмена", "ПКМ"))));
    }

    /// <summary>В готовых подсказках нет прежних записей «клавиша — глагол» и «клавиша / клавиша».</summary>
    [Test]
    public void ReadyHintsHaveNoOldDashOrSlashFormat()
    {
        foreach (string hint in new[] { UiKeyHint.EscClose, UiKeyHint.EscCancel, UiKeyHint.EscBack, UiKeyHint.AimMouse, UiKeyHint.AimPad, UiKeyHint.AimStatus })
        {
            Assert.That(hint.Contains(" — "), Is.False, hint);
            Assert.That(hint.Contains(" / "), Is.False, hint);
            Assert.That(hint.Contains("["), Is.True, hint);
        }
    }
}
