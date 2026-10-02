using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.View;
using NUnit.Framework;

// Меню разработчика (F8), переделка 02.10 по аудиту: масштаб и панель, колонки, подтверждения, подписи — правила без Unity.
public sealed class DeveloperMenuRulesTests
{
    [TestCase(1080f, 1f, 1f)]
    [TestCase(720f, 1f, .75f)]
    [TestCase(540f, 1f, .75f)]
    [TestCase(1440f, 1f, 1.3333f)]
    [TestCase(2160f, 1f, 2f)]
    [TestCase(4320f, 1f, 2f)]
    [TestCase(1080f, 1.2f, 1.2f)]
    [TestCase(1080f, .8f, .8f)]
    [TestCase(1080f, 0f, 1f)]
    public void ScaleFollowsScreenHeightAndUiScale(float height, float uiScale, float expected)
    {
        Assert.That(DevMenuRules.Scale(height, uiScale), Is.EqualTo(expected).Within(.001f));
    }

    [Test]
    public void PanelIsThousandByEightTwentyAt1080p()
    {
        DevMenuRules.PanelSize(1920f, 1080f, 1f, out float width, out float height);
        Assert.That(width, Is.EqualTo(1000f));
        Assert.That(height, Is.EqualTo(820f));
    }

    [Test]
    public void PanelFitsAt720p()
    {
        float scale = DevMenuRules.Scale(720f, 1f);
        DevMenuRules.PanelSize(1280f, 720f, scale, out float width, out float height);
        Assert.That(width, Is.EqualTo(750f));
        Assert.That(height, Is.EqualTo(615f));
    }

    [Test]
    public void PanelKeepsSixteenPixelMargins()
    {
        DevMenuRules.PanelSize(800f, 600f, .75f, out float width, out float height);
        Assert.That(width, Is.EqualTo(750f));
        Assert.That(height, Is.EqualTo(568f));
        DevMenuRules.PanelSize(1920f, 1080f, 1.2f * 1f, out width, out height);
        Assert.That(height, Is.EqualTo(984f).Within(.001f), "820 × 1,2 при 1080p ещё помещается");
        DevMenuRules.PanelSize(1366f, 768f, DevMenuRules.Scale(768f, 1.2f), out width, out height);
        Assert.That(height, Is.EqualTo(736f), "масштаб UI 1,2 на 768p упирается в поля");
    }

    [Test]
    public void TwoColumnsFoldBelowSevenSixty()
    {
        Assert.That(DevMenuRules.TwoColumns(946f, 1f), Is.True);
        Assert.That(DevMenuRules.TwoColumns(759f, 1f), Is.False);
        Assert.That(DevMenuRules.TwoColumns(700f, .75f), Is.True);
        Assert.That(DevMenuRules.ColumnWidth(968f, 1f), Is.EqualTo(476f));
        Assert.That(DevMenuRules.SingleWidth(946f, 1f), Is.EqualTo(640f));
        Assert.That(DevMenuRules.SingleWidth(500f, 1f), Is.EqualTo(500f));
    }

    [Test]
    public void ConfirmOnlyWhereTheAuditAsks()
    {
        Assert.That(DevMenuRules.NeedsConfirm(DevFlags.None, true), Is.False);
        Assert.That(DevMenuRules.NeedsConfirm(DevFlags.ConfirmIfRealRun, false), Is.False, "тестовый забег и лагерь — сразу");
        Assert.That(DevMenuRules.NeedsConfirm(DevFlags.ConfirmIfRealRun, true), Is.True, "обычный забег — второе нажатие");
        Assert.That(DevMenuRules.NeedsConfirm(DevFlags.Confirm | DevFlags.WritesSave, false), Is.True, "уровень героя — всегда");
    }

    [Test]
    public void ConfirmLabelSaysWhatHappens()
    {
        Assert.That(DevMenuRules.ConfirmLabel(DevFlags.Confirm | DevFlags.WritesSave, false), Does.Contain("сохранение"));
        Assert.That(DevMenuRules.ConfirmLabel(DevFlags.ConfirmIfRealRun | DevFlags.Danger, true), Does.Contain("обычный забег пропадёт"));
        Assert.That(DevMenuRules.ConfirmLabel(DevFlags.Confirm, false), Does.StartWith("Ещё раз"));
    }

    [Test]
    public void ArmLastsThreeSecondsOfUnscaledTime()
    {
        Assert.That(DevMenuRules.Armed(-1f, 5f), Is.False);
        Assert.That(DevMenuRules.Armed(10f, 10f), Is.True);
        Assert.That(DevMenuRules.Armed(10f, 12.9f), Is.True);
        Assert.That(DevMenuRules.Armed(10f, 13f), Is.False);
        Assert.That(DevMenuRules.Armed(10f, 9f), Is.False, "часы не идут назад: взвод из прошлого запуска не действует");
    }

    [Test]
    public void TabsWrapBothWays()
    {
        Assert.That(DevMenuRules.NextTab(3, 1), Is.EqualTo(0));
        Assert.That(DevMenuRules.NextTab(0, -1), Is.EqualTo(3));
        Assert.That(DevMenuRules.NextTab(1, 1), Is.EqualTo(2));
    }

    [Test]
    public void TabTitlesFollowTheAudit()
    {
        Assert.That(DevMenuRules.TabTitle(DevTab.Run, 0), Is.EqualTo("Забег"));
        Assert.That(DevMenuRules.TabTitle(DevTab.Combat, 0), Is.EqualTo("Бой"));
        Assert.That(DevMenuRules.TabTitle(DevTab.Pelag, 0), Is.EqualTo("Пелаг"));
        Assert.That(DevMenuRules.TabTitle(DevTab.Pelag, 3), Is.EqualTo("Пелаг · 3"));
        Assert.That(DevMenuRules.TabTitle(DevTab.Visual, 5), Is.EqualTo("Визуал"));
    }

    [Test]
    public void ArenaWordsDoNotClashWithHeroLevel()
    {
        Assert.That(DevMenuRules.ArenaCell(4, false), Is.EqualTo("4"));
        Assert.That(DevMenuRules.ArenaCell(9, true), Is.EqualTo("Б"));
        Assert.That(DevMenuRules.ArenaTitle(9, true), Is.EqualTo("арена 9 (босс)"));
        Assert.That(DevMenuRules.ClampArena(0, 9), Is.EqualTo(1));
        Assert.That(DevMenuRules.ClampArena(12, 9), Is.EqualTo(9));
        Assert.That(DevMenuRules.ClampArena(3, 0), Is.EqualTo(1));
    }

    [TestCase("42", true, 42UL)]
    [TestCase(" 7 ", true, 7UL)]
    [TestCase("18446744073709551615", true, ulong.MaxValue)]
    [TestCase("-1", false, 0UL)]
    [TestCase("4.2", false, 0UL)]
    [TestCase("", false, 0UL)]
    [TestCase(null, false, 0UL)]
    public void SeedIsANonNegativeInteger(string text, bool ok, ulong expected)
    {
        Assert.That(DevMenuRules.TryParseSeed(text, out ulong seed), Is.EqualTo(ok));
        if (ok) Assert.That(seed, Is.EqualTo(expected));
    }

    [Test]
    public void TalentLinesReadInSentenceCase()
    {
        Assert.That(DevMenuRules.SentenceCase("ВИХРЬ"), Is.EqualTo("Вихрь"));
        Assert.That(DevMenuRules.SentenceCase("УДАР ЯКОРЕМ"), Is.EqualTo("Удар якорем"));
        Assert.That(DevMenuRules.SentenceCase(""), Is.EqualTo(""));
    }

    [Test]
    public void HeaderNamesWhereTheGameIs()
    {
        Assert.That(DevMenuRules.ModeTitle(true, true, false, 1, 9, false), Is.EqualTo("Стенд мобов"));
        Assert.That(DevMenuRules.ModeTitle(false, true, false, 4, 9, false), Is.EqualTo("Забег · арена 4 из 9"));
        Assert.That(DevMenuRules.ModeTitle(false, true, false, 9, 9, true), Is.EqualTo("Забег · арена 9 (босс)"));
        Assert.That(DevMenuRules.ModeTitle(false, false, true, 0, 9, false), Is.EqualTo("Полигон"));
        Assert.That(DevMenuRules.ModeTitle(false, false, false, 0, 9, false), Is.EqualTo("Лагерь"));
    }
}

// Перенос всех 47 строк инвентаря аудита F8: каждый пункт зарегистрирован через DevMenu, якорные подписи —
// дословно, публичные имена, которые зовут съёмки и окна, — на месте. Читает исходники меню.
public sealed class DeveloperMenuInventoryTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot.Path, "razlom", "Assets", "Game.View", relative));

    private static string MenuSource()
        => string.Concat(Directory.GetFiles(Path.Combine(RepoRoot.Path, "razlom", "Assets", "Game.View"), "DeveloperMenu*.cs")
            .OrderBy(path => path).Select(File.ReadAllText));

    private static readonly Regex Registration = new Regex(@"DevMenu\.(Button|Toggle|Choice|Custom)\(\s*""([a-z\.\-]+)""");

    [TestCase("run.location")]
    [TestCase("run.arena")]
    [TestCase("run.seed")]
    [TestCase("run.start-arena")]
    [TestCase("run.boss")]
    [TestCase("camp.hero-level")]
    [TestCase("run.to-camp")]
    [TestCase("combat.immortal")]
    [TestCase("combat.bud")]
    [TestCase("combat.tempo-preset")]
    [TestCase("combat.tempo")]
    [TestCase("combat.sandbox")]
    [TestCase("pelag.slots")]
    [TestCase("pelag.preset-capture")]
    [TestCase("pelag.preset-starter")]
    [TestCase("pelag.artifact")]
    [TestCase("pelag.forms.preview")]
    [TestCase("pelag.forms.close")]
    [TestCase("pelag.talents")]
    [TestCase("pelag.talents-clear")]
    [TestCase("visual.comic")]
    [TestCase("visual.mood")]
    [TestCase("visual.mood-mode")]
    [TestCase("visual.mood-status")]
    public void EveryInventoryEntryIsRegistered(string id)
    {
        var ids = Registration.Matches(MenuSource()).Cast<Match>().Select(m => m.Groups[2].Value).ToList();
        Assert.That(ids, Has.Member(id));
        Assert.That(ids.Count(x => x == id), Is.EqualTo(1), "id регистрируется один раз");
    }

    [Test]
    public void BuiltInIdsListMatchesRegistrations()
    {
        string builtIn = string.Concat(new[] { "DeveloperMenu.Run.cs", "DeveloperMenu.Combat.cs", "DeveloperMenu.Pelag.cs", "DeveloperMenu.Visual.cs" }.Select(Read));
        var registered = new HashSet<string>(Registration.Matches(builtIn).Cast<Match>().Select(m => m.Groups[2].Value));
        string shell = Read("DeveloperMenu.cs");
        int start = shell.IndexOf("BuiltInIds =", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        string list = shell.Substring(start, shell.IndexOf("};", start, System.StringComparison.Ordinal) - start);
        var listed = new HashSet<string>(Regex.Matches(list, @"""([a-z\.\-]+)""").Cast<Match>().Select(m => m.Groups[1].Value));
        Assert.That(listed, Is.EquivalentTo(registered), "OnDisable снимает ровно встроенные пункты");
    }

    // Подписи, которые цитируют документы, память и окна стендов (аудит, раздел 4.2) — дословно.
    [TestCase("К боссу · свежий бой")]
    [TestCase("Бессмертие персонажа")]
    [TestCase("Лесной бутон · тестовый бой / повтор")]
    [TestCase("Визуал · проба рисовки")]
    [TestCase("Комикс-рисовка мира: тушь, кисть, диорама")]
    [TestCase("Свет арены по глубине: день → туман → сумерки")]
    [TestCase("Повторить бой с текущими навыками")]
    [TestCase("Снять все таланты")]
    [TestCase("Вернуться в лагерь")]
    [TestCase("Продолжить · F8 / Esc")]
    public void AnchorLabelsStayVerbatim(string label)
    {
        Assert.That(MenuSource(), Does.Contain("\"" + label + "\""));
    }

    [Test]
    public void PublicNamesThatCapturesCallStay()
    {
        Assert.That(Read("TickDriver.Developer.cs"), Does.Contain("public void StartDeveloperRift(LocationTheme theme, int level, bool nearBoss, ulong seed)"));
        Assert.That(Read("TickDriver.ForestBud.cs"), Does.Contain("public void StartForestBudTest(LocationTheme theme, ulong seed, int count = 1)"));
        Assert.That(Read("TickDriver.Tempo.cs"), Does.Contain("public void StartTempoTest(int[] pool, int preset, bool basicComboCandidate = false)"));
        Assert.That(Read("TickDriver.cs"), Does.Contain("public void ReturnToCampFromMenu()"));
        Assert.That(Read("TickDriver.cs"), Does.Contain("public void RefreshAbilityBuild()"));
        string shell = Read("DeveloperMenu.cs");
        Assert.That(shell, Does.Contain("public sealed partial class DeveloperMenu : MonoBehaviour"));
        Assert.That(shell, Does.Contain("public static bool CapturesEscape"));
        Assert.That(shell, Does.Contain("public static bool BlocksPause => CapturesEscape || _closedFrame == Time.frameCount;"));
    }

    [Test]
    public void ShellKeepsPauseSafety()
    {
        string shell = Read("DeveloperMenu.cs");
        Assert.That(shell, Does.Not.Contain("_request"), "магических номеров больше нет");
        Assert.That(Regex.IsMatch(shell, @"private void OnDisable\(\)\s*\{\s*Close\(\);"), Is.True, "выход из Play с открытым меню не оставляет timeScale 0");
        Assert.That(shell, Does.Contain("if (CaptureRig.ForestBudShowcase) return;"), "съёмка forest-bud не открывает F8");
        Assert.That(shell, Does.Contain("Time.timeScale = _previousTimeScale > 0 ? _previousTimeScale : 1;"));
        Assert.That(Regex.IsMatch(MenuSource(), @"hideFlags\s*="), Is.False, "текстуры скина без DontSave: уничтожаются в OnDestroy");
        Assert.That(Read("DeveloperMenu.Skin.cs"), Does.Contain("Object.Destroy(texture)"));
        Assert.That(MenuSource(), Does.Not.Contain("GUI.matrix"), "масштаб — размерами шрифтов, не матрицей");
    }

    [Test]
    public void RegistrationIsCompiledOutOfRelease()
    {
        string api = Read("DevMenu.cs");
        Assert.That(api, Does.Not.StartWith("#if"), "типы API нужны и в релизе");
        foreach (string method in new[] { "Section", "Button", "Toggle", "Choice", "Custom", "Unregister" })
            Assert.That(Regex.IsMatch(api, @"\[Conditional\(InEditor\), Conditional\(InDevBuild\)\]\s*public static void " + method + @"\("), Is.True, method);
        foreach (string file in new[] { "DeveloperMenu.cs", "DeveloperMenu.Run.cs", "DeveloperMenu.Combat.cs", "DeveloperMenu.Pelag.cs",
                     "DeveloperMenu.Visual.cs", "DeveloperMenu.Forms.cs", "DeveloperMenuCapture.cs" })
            Assert.That(Read(file), Does.StartWith("#if UNITY_EDITOR || DEVELOPMENT_BUILD"), file);
    }
}
