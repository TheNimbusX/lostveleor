using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.View;

// Окно «Настройки Б» (30.09): новые настройки сохраняются, переживают перезапуск, порченое значение
// становится стандартным; у каждой опции есть заголовок, описание и строка про стандартное значение.
public sealed class SettingsModelTests
{
    /// <summary>PlayerPrefs в памяти: «перезапуск» — новый UserPreferences над тем же хранилищем.</summary>
    sealed class MemoryStore : ISettingsStore
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public int Saves;
        public bool HasKey(string key) => Values.ContainsKey(key);
        public int GetInt(string key, int fallback) => Values.TryGetValue(key, out object v) && v is int i ? i : fallback;
        public float GetFloat(string key, float fallback) => Values.TryGetValue(key, out object v) && v is float f ? f : fallback;
        public string GetString(string key, string fallback) => Values.TryGetValue(key, out object v) && v is string s ? s : fallback;
        public void SetInt(string key, int value) => Values[key] = value;
        public void SetFloat(string key, float value) => Values[key] = value;
        public void SetString(string key, string value) => Values[key] = value;
        public void Save() => Saves++;
    }

    static UserPreferences Loaded(MemoryStore store)
    {
        var prefs = new UserPreferences(store);
        prefs.Load();
        return prefs;
    }

    [Test]
    public void EmptyStoreGivesDefaultsThatLeaveTheGameAsItWas()
    {
        UserPreferences prefs = Loaded(new MemoryStore());
        Assert.That(prefs.Brightness, Is.EqualTo(.5f));
        Assert.That(prefs.InterfaceVolume, Is.EqualTo(1f));
        Assert.That(prefs.SoundInBackground, Is.True);
        Assert.That(prefs.Language, Is.EqualTo("ru"));
        Assert.That(prefs.PauseOnFocusLoss, Is.False);
        Assert.That(prefs.DamageNumbers, Is.True);
        Assert.That(prefs.EnemyBars, Is.EqualTo(EnemyBarMode.All));
        Assert.That(prefs.ScreenShake, Is.EqualTo(1f));
        Assert.That(prefs.ReduceFlashes, Is.False);
        Assert.That(prefs.FlashScale, Is.EqualTo(1f));
        Assert.That(prefs.DisplayIsDefault && prefs.AudioIsDefault && prefs.GameIsDefault && prefs.InterfaceIsDefault, Is.True);
    }

    [Test]
    public void EverySettingSurvivesARestart()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        prefs.SetBrightness(.7f);
        prefs.SetInterfaceVolume(.33f);
        prefs.SetSoundInBackground(false);
        prefs.SetPauseOnFocusLoss(true);
        prefs.SetDamageNumbers(false);
        prefs.SetEnemyBars(EnemyBarMode.EliteOnly);
        prefs.SetScreenShake(.25f);
        prefs.SetReduceFlashes(true);

        UserPreferences restarted = Loaded(store);
        Assert.That(restarted.Brightness, Is.EqualTo(.7f).Within(1e-5f));
        Assert.That(restarted.InterfaceVolume, Is.EqualTo(.33f).Within(1e-5f));
        Assert.That(restarted.SoundInBackground, Is.False);
        Assert.That(restarted.PauseOnFocusLoss, Is.True);
        Assert.That(restarted.DamageNumbers, Is.False);
        Assert.That(restarted.EnemyBars, Is.EqualTo(EnemyBarMode.EliteOnly));
        Assert.That(restarted.ScreenShake, Is.EqualTo(.25f).Within(1e-5f));
        Assert.That(restarted.ReduceFlashes, Is.True);
        Assert.That(restarted.FlashScale, Is.EqualTo(UserPreferences.ReducedFlashScale));
    }

    [Test]
    public void TogglesAndStepsAreFlushedToDiskAtOnce()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        int before = store.Saves;
        prefs.SetDamageNumbers(false);
        prefs.SetBrightness(.6f);
        Assert.That(store.Saves, Is.EqualTo(before + 2));
    }

    [Test]
    public void InterfaceVolumeDragDoesNotHitTheDiskEveryStep()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        int before = store.Saves;
        for (int i = 1; i <= 30; i++) prefs.SetInterfaceVolume(i / 30f);
        Assert.That(store.Saves, Is.EqualTo(before));
        Assert.That(store.GetFloat(UserPreferences.InterfaceVolumeKey, -1f), Is.EqualTo(1f));
    }

    [Test]
    public void UnchangedValueWritesNothing()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        Assert.That(prefs.SetScreenShake(1f), Is.False);
        Assert.That(prefs.SetEnemyBars(EnemyBarMode.All), Is.False);
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public void SlidersSnapToFivePercentAndClamp()
    {
        UserPreferences prefs = Loaded(new MemoryStore());
        prefs.SetBrightness(.62f);
        Assert.That(prefs.Brightness, Is.EqualTo(.6f).Within(1e-5f));
        prefs.SetScreenShake(7f);
        Assert.That(prefs.ScreenShake, Is.EqualTo(1f));
        prefs.SetScreenShake(-2f);
        Assert.That(prefs.ScreenShake, Is.EqualTo(0f));
        prefs.SetInterfaceVolume(float.NaN);
        Assert.That(prefs.InterfaceVolume, Is.EqualTo(0f));
    }

    [Test]
    public void CorruptStoredValuesFallBackToDefaults()
    {
        var store = new MemoryStore();
        store.SetInt(UserPreferences.EnemyBarsKey, 9);
        store.SetString(UserPreferences.LanguageKey, "en");
        store.SetFloat(UserPreferences.BrightnessKey, 4f);
        store.SetFloat(UserPreferences.ScreenShakeKey, -1f);
        UserPreferences prefs = Loaded(store);
        Assert.That(prefs.EnemyBars, Is.EqualTo(EnemyBarMode.All));
        Assert.That(prefs.Language, Is.EqualTo("ru"), "у английского нет строк — после перезапуска остаётся русский");
        Assert.That(prefs.Brightness, Is.EqualTo(1f));
        Assert.That(prefs.ScreenShake, Is.EqualTo(0f));
    }

    [Test]
    public void OnlyLanguagesWithStringsCanBeChosen()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        Assert.That(prefs.SetLanguage("en"), Is.False);
        Assert.That(prefs.SetLanguage("xx"), Is.False);
        Assert.That(prefs.Language, Is.EqualTo("ru"));
        Assert.That(store.Values.ContainsKey(UserPreferences.LanguageKey), Is.False);
        foreach (LanguageOption language in UserPreferences.Languages)
            Assert.That(language.Label.EndsWith("скоро"), Is.EqualTo(!language.Ready), language.Code);
        Assert.That(UserPreferences.Languages[0].Code, Is.EqualTo("ru"));
        Assert.That(UserPreferences.Languages[0].Ready, Is.True);
    }

    [Test]
    public void ResetReturnsEachTabToDefaultsOnly()
    {
        var store = new MemoryStore();
        UserPreferences prefs = Loaded(store);
        prefs.SetBrightness(.2f);
        prefs.SetInterfaceVolume(.1f);
        prefs.SetSoundInBackground(false);
        prefs.SetDamageNumbers(false);
        prefs.SetEnemyBars(EnemyBarMode.None);
        prefs.SetScreenShake(0f);
        prefs.SetReduceFlashes(true);

        prefs.ResetInterface();
        Assert.That(prefs.InterfaceIsDefault, Is.True);
        Assert.That(prefs.GameIsDefault, Is.False, "сброс одной вкладки не трогает другую");
        prefs.ResetGame();
        Assert.That(prefs.GameIsDefault, Is.True);
        Assert.That(prefs.AudioIsDefault, Is.False);
        prefs.ResetAudio();
        Assert.That(prefs.AudioIsDefault, Is.True);
        Assert.That(prefs.DisplayIsDefault, Is.False);
        prefs.ResetDisplay();
        Assert.That(prefs.DisplayIsDefault, Is.True);

        UserPreferences restarted = Loaded(store);
        Assert.That(restarted.DisplayIsDefault && restarted.AudioIsDefault && restarted.GameIsDefault && restarted.InterfaceIsDefault, Is.True);
    }

    [Test]
    public void DefaultBrightnessLeavesThePictureUntouched()
    {
        Assert.That(UserPreferences.GammaOffset(.5f), Is.EqualTo(0f));
        foreach (float level in new[] { 0f, .04f, .2f, .5f, 1f })
            Assert.That(UserPreferences.ApplyToSrgb(level, .5f), Is.EqualTo(level).Within(1e-4f));
    }

    [Test]
    public void BrightnessMovesMidtonesMonotonicallyAndKeepsBlackAndWhite()
    {
        float previous = -1f;
        for (int step = 0; step <= 20; step++)
        {
            float value = UserPreferences.ApplyToSrgb(.1f, step / 20f);
            Assert.That(value, Is.GreaterThan(previous), "ярче ползунок — светлее образец");
            previous = value;
        }
        Assert.That(UserPreferences.ApplyToSrgb(0f, 1f), Is.EqualTo(0f));
        Assert.That(UserPreferences.ApplyToSrgb(1f, 0f), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(Math.Abs(UserPreferences.GammaOffset(1f)), Is.LessThanOrEqualTo(.3f + 1e-5f), "шире ±0,3 картинка теряет контраст");
    }

    [Test]
    public void EveryOptionIsDescribedOnItsTab()
    {
        var seen = new HashSet<SettingId>();
        for (int t = 0; t < SettingsCatalog.TabCount; t++)
        {
            var tab = (SettingsTab)t;
            Assert.That(SettingsCatalog.TabTitle(tab), Is.Not.Empty);
            SettingId[] ids = SettingsCatalog.Ids(tab);
            Assert.That(ids, Is.Not.Empty, tab.ToString());
            foreach (SettingId id in ids)
            {
                Assert.That(seen.Add(id), Is.True, id + " стоит на двух вкладках");
                Assert.That(SettingsCatalog.TabOf(id), Is.EqualTo(tab));
                Assert.That(SettingsCatalog.Title(id), Is.Not.Empty, id.ToString());
                string description = SettingsCatalog.Description(id);
                Assert.That(description, Is.Not.Empty, id.ToString());
                Assert.That(description.Length, Is.LessThanOrEqualTo(120), id + ": описание — одна мысль, панель справа узкая");
                Assert.That(SettingsCatalog.DefaultLine(id), Is.Not.Empty, id.ToString());
            }
            Assert.That(SettingsCatalog.ResetSummary(tab), Is.Not.Empty);
        }
        foreach (SettingId id in (SettingId[])Enum.GetValues(typeof(SettingId)))
            Assert.That(seen.Contains(id), Is.True, id + " не попала ни на одну вкладку");
    }

    [Test]
    public void TitlesAreUniqueSoRowsAreNotConfused()
    {
        var titles = new HashSet<string>();
        foreach (SettingId id in (SettingId[])Enum.GetValues(typeof(SettingId)))
            Assert.That(titles.Add(SettingsCatalog.Title(id)), Is.True, SettingsCatalog.Title(id));
    }

    [Test]
    public void DefaultLinesMatchTheModelDefaults()
    {
        Assert.That(SettingsCatalog.DefaultText(SettingId.Brightness), Is.EqualTo(UserPreferences.Percent(UserPreferences.DefaultBrightness)));
        Assert.That(SettingsCatalog.DefaultText(SettingId.ScreenShake), Is.EqualTo("100%"));
        Assert.That(SettingsCatalog.DefaultText(SettingId.EnemyBars), Is.EqualTo(SettingsCatalog.EnemyBarsName(UserPreferences.DefaultEnemyBars)));
        Assert.That(SettingsCatalog.DefaultText(SettingId.Language), Is.EqualTo("Русский"));
        Assert.That(SettingsCatalog.DefaultLine(SettingId.Resolution), Does.Contain("экран не меняет"), "сброс не трогает экран — так и сказано");
        Assert.That(SettingsCatalog.DefaultLine(SettingId.Quality), Is.EqualTo("Стандартно: Высокое"));
    }

    [Test]
    public void SettingIdNumbersStayPutForPrefabs()
    {
        // Номер опции лежит в префабе (UiSettingRow.Setting): новые — только в конец.
        Assert.That((int)SettingId.DisplayMode, Is.EqualTo(0));
        Assert.That((int)SettingId.Brightness, Is.EqualTo(6));
        Assert.That((int)SettingId.EnemyBars, Is.EqualTo(15));
        Assert.That((int)SettingId.KeyBindings, Is.EqualTo(21));
        Assert.That((int)EnemyBarMode.None, Is.EqualTo(2));
    }
}
