using System;

namespace Game.View
{
    /// <summary>
    /// Хранилище настроек. В игре — PlayerPrefs (<see cref="GameUserSettings"/>), в тестах —
    /// словарь. Отдельный интерфейс нужен ради тестов: логика значений, умолчаний и сохранения
    /// проверяется без Unity (tools/Combat.Presentation.Tests).
    /// </summary>
    public interface ISettingsStore
    {
        bool HasKey(string key);
        int GetInt(string key, int fallback);
        float GetFloat(string key, float fallback);
        string GetString(string key, string fallback);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
        void Save();
    }

    /// <summary>Полоски здоровья над врагами. Номера лежат в сохранённых настройках — не менять.</summary>
    public enum EnemyBarMode : byte
    {
        /// <summary>Как было: у задетых ударом и у элиты рядом.</summary>
        All = 0,
        EliteOnly = 1,
        None = 2,
    }

    /// <summary>Вкладки окна настроек (выбор владельца 30.09, «Настройки Б»: столбец слева со значками).</summary>
    public enum SettingsTab : byte
    {
        Display = 0,
        Audio = 1,
        Game = 2,
        Interface = 3,
        Controls = 4,
    }

    /// <summary>
    /// Опция окна настроек: по ней панель справа берёт заголовок, описание и стандартное значение.
    /// ТОЛЬКО ДОПИСЫВАТЬ В КОНЕЦ: номер лежит в префабе (UiSettingRow.Setting).
    /// </summary>
    public enum SettingId : byte
    {
        DisplayMode = 0,
        Resolution = 1,
        Quality = 2,
        VSync = 3,
        FrameLimit = 4,
        Shadows = 5,
        Brightness = 6,
        MasterVolume = 7,
        EffectsVolume = 8,
        MusicVolume = 9,
        InterfaceVolume = 10,
        SoundInBackground = 11,
        Language = 12,
        PauseOnFocusLoss = 13,
        DamageNumbers = 14,
        EnemyBars = 15,
        UiScale = 16,
        ScreenShake = 17,
        Flashes = 18,
        Movement = 19,
        AbilityRow = 20,
        KeyBindings = 21,
    }

    /// <summary>Язык из списка: готов ли перевод. Неготовые показываются с пометкой «скоро» и не выбираются.</summary>
    public readonly struct LanguageOption
    {
        public LanguageOption(string code, string name, bool ready)
        {
            Code = code;
            Name = name;
            Ready = ready;
        }

        public string Code { get; }
        public string Name { get; }
        public bool Ready { get; }
        public string Label => Ready ? Name : Name + " · скоро";
    }

    /// <summary>
    /// Настройки, добавленные окном «Настройки Б» (30.09): яркость, громкость интерфейса, звук в фоне,
    /// язык, пауза при сворачивании, цифры урона, полоски врагов, тряска экрана, вспышки. Без Unity:
    /// значения, пределы, умолчания и запись. Применяет их к движку <see cref="GameUserSettings"/>.
    ///
    /// Каждый сеттер сохраняет сразу: настройка, которая живёт до выхода из меню, теряется при вылете,
    /// а применённое на глазах игрока значение отменять незачем.
    /// </summary>
    public sealed class UserPreferences
    {
        public const string BrightnessKey = "settings.display.brightness";
        public const string InterfaceVolumeKey = "settings.audio.interface";
        public const string BackgroundKey = "settings.audio.background";
        public const string LanguageKey = "settings.game.language";
        public const string PauseOnFocusLossKey = "settings.game.pauseOnFocusLoss";
        public const string DamageNumbersKey = "settings.ui.damageNumbers";
        public const string EnemyBarsKey = "settings.ui.enemyBars";
        public const string ScreenShakeKey = "settings.access.screenShake";
        public const string ReduceFlashesKey = "settings.access.reduceFlashes";

        public const float DefaultBrightness = .5f;
        public const float DefaultInterfaceVolume = 1f;
        public const bool DefaultSoundInBackground = true;
        public const string DefaultLanguage = "ru";
        public const bool DefaultPauseOnFocusLoss = false;
        public const bool DefaultDamageNumbers = true;
        public const EnemyBarMode DefaultEnemyBars = EnemyBarMode.All;
        public const float DefaultScreenShake = 1f;
        public const bool DefaultReduceFlashes = false;

        /// <summary>
        /// Яркость меняет средние тона через LiftGammaGain URP: gamma.w = (яркость − 0,5) × диапазон.
        /// ±0,3 — от «ночь читается» до «тени светлее»: шире картинка теряет контраст.
        /// </summary>
        public const float BrightnessRange = .6f;

        /// <summary>Во сколько раз гаснут вспышки в режиме «Мягче».</summary>
        public const float ReducedFlashScale = .35f;

        /// <summary>
        /// Языки, на которые готовят перевод (CampServiceText.SupportedLocales). Строки есть только
        /// у русского — остальные в списке помечены «скоро» и не выбираются.
        /// </summary>
        public static readonly LanguageOption[] Languages =
        {
            new LanguageOption("ru", "Русский", true),
            new LanguageOption("en", "Английский", false),
            new LanguageOption("de", "Немецкий", false),
            new LanguageOption("es", "Испанский", false),
            new LanguageOption("pt", "Португальский", false),
            new LanguageOption("it", "Итальянский", false),
            new LanguageOption("zh", "Китайский", false),
        };

        readonly ISettingsStore _store;

        public UserPreferences(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public float Brightness { get; private set; } = DefaultBrightness;
        public float InterfaceVolume { get; private set; } = DefaultInterfaceVolume;
        public bool SoundInBackground { get; private set; } = DefaultSoundInBackground;
        public string Language { get; private set; } = DefaultLanguage;
        public bool PauseOnFocusLoss { get; private set; } = DefaultPauseOnFocusLoss;
        public bool DamageNumbers { get; private set; } = DefaultDamageNumbers;
        public EnemyBarMode EnemyBars { get; private set; } = DefaultEnemyBars;
        public float ScreenShake { get; private set; } = DefaultScreenShake;
        public bool ReduceFlashes { get; private set; } = DefaultReduceFlashes;

        /// <summary>Множитель вспышек для тех, кто их рисует: 1 или <see cref="ReducedFlashScale"/>.</summary>
        public float FlashScale => ReduceFlashes ? ReducedFlashScale : 1f;

        /// <summary>
        /// Читает всё из хранилища. Порченое значение (чужой номер, вне пределов, неготовый язык) —
        /// стандартное: игра не должна стартовать с невозможной настройкой.
        /// </summary>
        public void Load()
        {
            Brightness = Step(_store.GetFloat(BrightnessKey, DefaultBrightness));
            InterfaceVolume = Clamp01(_store.GetFloat(InterfaceVolumeKey, DefaultInterfaceVolume));
            SoundInBackground = _store.GetInt(BackgroundKey, DefaultSoundInBackground ? 1 : 0) != 0;
            string language = _store.GetString(LanguageKey, DefaultLanguage);
            Language = IsReady(language) ? language : DefaultLanguage;
            PauseOnFocusLoss = _store.GetInt(PauseOnFocusLossKey, DefaultPauseOnFocusLoss ? 1 : 0) != 0;
            DamageNumbers = _store.GetInt(DamageNumbersKey, DefaultDamageNumbers ? 1 : 0) != 0;
            int bars = _store.GetInt(EnemyBarsKey, (int)DefaultEnemyBars);
            EnemyBars = bars >= 0 && bars <= (int)EnemyBarMode.None ? (EnemyBarMode)bars : DefaultEnemyBars;
            ScreenShake = Step(_store.GetFloat(ScreenShakeKey, DefaultScreenShake));
            ReduceFlashes = _store.GetInt(ReduceFlashesKey, DefaultReduceFlashes ? 1 : 0) != 0;
        }

        // ---- сеттеры: true — значение поменялось и записано ----

        public bool SetBrightness(float value)
        {
            value = Step(value);
            if (Same(Brightness, value)) return false;
            Brightness = value;
            _store.SetFloat(BrightnessKey, value);
            _store.Save();
            return true;
        }

        /// <summary>
        /// Громкость интерфейса без шага: ползунок звука плавный, как у других каналов. Пишется в хранилище
        /// сразу, а на диск — вместе с остальной громкостью (GameUserSettings.SaveAudio при уходе со вкладки):
        /// запись на каждом шаге перетаскивания — десятки сохранений в секунду.
        /// </summary>
        public bool SetInterfaceVolume(float value)
        {
            value = Clamp01(value);
            if (Same(InterfaceVolume, value)) return false;
            InterfaceVolume = value;
            _store.SetFloat(InterfaceVolumeKey, value);
            return true;
        }

        public bool SetSoundInBackground(bool value)
        {
            if (SoundInBackground == value) return false;
            SoundInBackground = value;
            WriteBool(BackgroundKey, value);
            return true;
        }

        /// <summary>Только язык с готовым переводом; неготовый — false, выбор не меняется.</summary>
        public bool SetLanguage(string code)
        {
            if (!IsReady(code) || Language == code) return false;
            Language = code;
            _store.SetString(LanguageKey, code);
            _store.Save();
            return true;
        }

        public bool SetPauseOnFocusLoss(bool value)
        {
            if (PauseOnFocusLoss == value) return false;
            PauseOnFocusLoss = value;
            WriteBool(PauseOnFocusLossKey, value);
            return true;
        }

        public bool SetDamageNumbers(bool value)
        {
            if (DamageNumbers == value) return false;
            DamageNumbers = value;
            WriteBool(DamageNumbersKey, value);
            return true;
        }

        public bool SetEnemyBars(EnemyBarMode mode)
        {
            if ((int)mode < 0 || (int)mode > (int)EnemyBarMode.None) mode = DefaultEnemyBars;
            if (EnemyBars == mode) return false;
            EnemyBars = mode;
            _store.SetInt(EnemyBarsKey, (int)mode);
            _store.Save();
            return true;
        }

        public bool SetScreenShake(float value)
        {
            value = Step(value);
            if (Same(ScreenShake, value)) return false;
            ScreenShake = value;
            _store.SetFloat(ScreenShakeKey, value);
            _store.Save();
            return true;
        }

        public bool SetReduceFlashes(bool value)
        {
            if (ReduceFlashes == value) return false;
            ReduceFlashes = value;
            WriteBool(ReduceFlashesKey, value);
            return true;
        }

        // ---- сброс по вкладкам (то, что из этих настроек лежит на вкладке) ----

        public void ResetDisplay() => SetBrightness(DefaultBrightness);

        public void ResetAudio()
        {
            SetInterfaceVolume(DefaultInterfaceVolume);
            SetSoundInBackground(DefaultSoundInBackground);
        }

        public void ResetGame()
        {
            SetLanguage(DefaultLanguage);
            SetPauseOnFocusLoss(DefaultPauseOnFocusLoss);
            SetDamageNumbers(DefaultDamageNumbers);
            SetEnemyBars(DefaultEnemyBars);
        }

        public void ResetInterface()
        {
            SetScreenShake(DefaultScreenShake);
            SetReduceFlashes(DefaultReduceFlashes);
        }

        public bool DisplayIsDefault => Same(Brightness, DefaultBrightness);
        public bool AudioIsDefault => Same(InterfaceVolume, DefaultInterfaceVolume) && SoundInBackground == DefaultSoundInBackground;
        public bool GameIsDefault => Language == DefaultLanguage && PauseOnFocusLoss == DefaultPauseOnFocusLoss
            && DamageNumbers == DefaultDamageNumbers && EnemyBars == DefaultEnemyBars;
        public bool InterfaceIsDefault => Same(ScreenShake, DefaultScreenShake) && ReduceFlashes == DefaultReduceFlashes;

        // ---- чистые помощники ----

        public static bool IsReady(string code)
        {
            foreach (LanguageOption option in Languages)
                if (option.Code == code) return option.Ready;
            return false;
        }

        public static int LanguageIndex(string code)
        {
            for (int i = 0; i < Languages.Length; i++)
                if (Languages[i].Code == code) return i;
            return 0;
        }

        /// <summary>Шаг ползунков яркости и тряски — 5%: подпись всегда круглая, сохранённое значение тоже.</summary>
        public static float Step(float value) => (float)Math.Round(Clamp01(value) * 20f) / 20f;

        public static string Percent(float value) => (int)Math.Round(Clamp01(value) * 100f) + "%";

        /// <summary>Смещение gamma.w для LiftGammaGain; 0 при стандартной яркости.</summary>
        public static float GammaOffset(float brightness) => (Step(brightness) - DefaultBrightness) * BrightnessRange;

        /// <summary>
        /// Что делает LiftGammaGain URP с линейным цветом при нейтральном балансе: pow(c, 1 / (1 + w))
        /// (ColorUtils.PrepareLiftGammaGain). Тот же сдвиг — у образца яркости в окне настроек:
        /// интерфейс рисуется после постобработки, и образец красится вручную.
        /// </summary>
        public static float ApplyToLinear(float linear, float brightness)
        {
            double exponent = 1.0 / Math.Max(1e-3, 1.0 + GammaOffset(brightness));
            return (float)Math.Pow(Math.Max(0.0, linear), exponent);
        }

        /// <summary>Тот же сдвиг для цвета sRGB (0–1): образец яркости в панели описания.</summary>
        public static float ApplyToSrgb(float srgb, float brightness) => LinearToSrgb(ApplyToLinear(SrgbToLinear(srgb), brightness));

        public static float SrgbToLinear(float c)
        {
            c = Clamp01(c);
            return c <= .04045f ? c / 12.92f : (float)Math.Pow((c + .055) / 1.055, 2.4);
        }

        public static float LinearToSrgb(float c)
        {
            c = Clamp01(c);
            return c <= .0031308f ? c * 12.92f : (float)(1.055 * Math.Pow(c, 1.0 / 2.4) - .055);
        }

        static float Clamp01(float value) => float.IsNaN(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
        static bool Same(float a, float b) => Math.Abs(a - b) < 1e-4f;

        void WriteBool(string key, bool value)
        {
            _store.SetInt(key, value ? 1 : 0);
            _store.Save();
        }
    }

    /// <summary>
    /// Что показывает окно настроек: вкладки, порядок опций, заголовок и описание в панели справа,
    /// стандартное значение и что сбрасывает «Сбросить». Без Unity — проверяется тестами.
    /// </summary>
    public static class SettingsCatalog
    {
        static readonly SettingId[] DisplayIds =
        {
            SettingId.DisplayMode, SettingId.Resolution, SettingId.Quality, SettingId.VSync,
            SettingId.FrameLimit, SettingId.Shadows, SettingId.Brightness,
        };

        static readonly SettingId[] AudioIds =
        {
            SettingId.MasterVolume, SettingId.EffectsVolume, SettingId.MusicVolume,
            SettingId.InterfaceVolume, SettingId.SoundInBackground,
        };

        static readonly SettingId[] GameIds =
        {
            SettingId.Language, SettingId.PauseOnFocusLoss, SettingId.DamageNumbers, SettingId.EnemyBars,
        };

        static readonly SettingId[] InterfaceIds = { SettingId.UiScale, SettingId.ScreenShake, SettingId.Flashes };

        static readonly SettingId[] ControlsIds = { SettingId.Movement, SettingId.AbilityRow, SettingId.KeyBindings };

        public const int TabCount = 5;

        public static SettingId[] Ids(SettingsTab tab)
        {
            switch (tab)
            {
                case SettingsTab.Audio: return AudioIds;
                case SettingsTab.Game: return GameIds;
                case SettingsTab.Interface: return InterfaceIds;
                case SettingsTab.Controls: return ControlsIds;
                default: return DisplayIds;
            }
        }

        public static SettingsTab TabOf(SettingId id)
        {
            for (int t = 0; t < TabCount; t++)
                if (Array.IndexOf(Ids((SettingsTab)t), id) >= 0) return (SettingsTab)t;
            return SettingsTab.Display;
        }

        public static string TabTitle(SettingsTab tab)
        {
            switch (tab)
            {
                case SettingsTab.Audio: return "Звук";
                case SettingsTab.Game: return "Игра";
                case SettingsTab.Interface: return "Интерфейс и доступность";
                case SettingsTab.Controls: return "Управление";
                default: return "Изображение";
            }
        }

        public static string Title(SettingId id)
        {
            switch (id)
            {
                case SettingId.DisplayMode: return "Режим экрана";
                case SettingId.Resolution: return "Разрешение";
                case SettingId.Quality: return "Качество графики";
                case SettingId.VSync: return "Вертикальная синхронизация";
                case SettingId.FrameLimit: return "Ограничение кадров";
                case SettingId.Shadows: return "Тени";
                case SettingId.Brightness: return "Яркость";
                case SettingId.MasterVolume: return "Общая громкость";
                case SettingId.EffectsVolume: return "Эффекты";
                case SettingId.MusicVolume: return "Музыка";
                case SettingId.InterfaceVolume: return "Интерфейс";
                case SettingId.SoundInBackground: return "Звук в фоне";
                case SettingId.Language: return "Язык";
                case SettingId.PauseOnFocusLoss: return "Пауза при сворачивании";
                case SettingId.DamageNumbers: return "Цифры урона";
                case SettingId.EnemyBars: return "Полоски здоровья врагов";
                case SettingId.UiScale: return "Масштаб интерфейса";
                case SettingId.ScreenShake: return "Тряска экрана";
                case SettingId.Flashes: return "Вспышки и мерцание";
                case SettingId.Movement: return "Движение";
                case SettingId.AbilityRow: return "Способности";
                default: return "Клавиши";
            }
        }

        /// <summary>Одна мысль на опцию: что меняет и когда применяется.</summary>
        public static string Description(SettingId id)
        {
            switch (id)
            {
                case SettingId.DisplayMode: return "Весь экран, окно с рамкой или окно без рамок размером с монитор. Меняется кнопкой «Применить».";
                case SettingId.Resolution: return "Размер кадра. Меняется кнопкой «Применить»: 15 секунд на проверку, потом возврат сам.";
                case SettingId.Quality: return "Масштаб кадра, сглаживание и тени разом. На слабом компьютере выбери «Среднее».";
                case SettingId.VSync: return "Кадры в такт монитору, без разрывов картинки. Пока включена, ограничение кадров не действует.";
                case SettingId.FrameLimit: return "Потолок кадров в секунду без синхронизации. Ниже — тише и холоднее компьютер.";
                case SettingId.Shadows: return "Дальность и чёткость теней отдельно от качества графики.";
                case SettingId.Brightness: return "Средние тона картинки. Двигай, пока левый круг образца не станет едва различим.";
                case SettingId.MasterVolume: return "Громкость всей игры.";
                case SettingId.EffectsVolume: return "Бой, шаги, окружение и голоса.";
                case SettingId.MusicVolume: return "Музыка лагеря, Разлома и главного меню.";
                case SettingId.InterfaceVolume: return "Щелчки, наведение и окна интерфейса.";
                case SettingId.SoundInBackground: return "Звучать, когда окно игры не в фокусе. Выключи — игра замолчит при переключении окон.";
                case SettingId.Language: return "Язык текста игры. Пока готов только русский, остальные переводы в работе.";
                case SettingId.PauseOnFocusLoss: return "Свернул окно или нажал Alt+Tab — игра сама встаёт на паузу.";
                case SettingId.DamageNumbers: return "Всплывающие цифры урона над врагами и героем.";
                case SettingId.EnemyBars: return "Полоски над врагами: у всех задетых, только у элиты или ни у кого.";
                case SettingId.UiScale: return "Размер HUD и меню, 80–120%. Меняется сразу.";
                case SettingId.ScreenShake: return "Тряска и толчок камеры от сильных ударов. 0% — камера стоит на месте.";
                case SettingId.Flashes: return "«Мягче» приглушает вспышки попаданий и всполохи интерфейса.";
                case SettingId.Movement: return "Мышь: ПКМ — идти, ЛКМ — бить. WASD: ходьба клавишами, мышь — прицел, способности на 1–4.";
                case SettingId.AbilityRow: return "Ряд способностей при управлении мышью. При WASD способности всегда на цифрах.";
                default: return "Нажми на клавишу действия, затем новую. Занятая клавиша той же группы меняется местами. " + UiKeyHint.EscCancel + ".";
            }
        }

        /// <summary>Что вернёт «Сбросить»; null — сброс эту опцию не трогает.</summary>
        public static string DefaultText(SettingId id)
        {
            switch (id)
            {
                case SettingId.Quality: return "Высокое";
                case SettingId.VSync: return "Вкл";
                case SettingId.FrameLimit: return "144";
                case SettingId.Shadows: return "Высокие";
                case SettingId.Brightness: return UserPreferences.Percent(UserPreferences.DefaultBrightness);
                case SettingId.MasterVolume: return "100%";
                case SettingId.EffectsVolume: return "100%";
                case SettingId.MusicVolume: return "75%";
                case SettingId.InterfaceVolume: return UserPreferences.Percent(UserPreferences.DefaultInterfaceVolume);
                case SettingId.SoundInBackground: return OnOff(UserPreferences.DefaultSoundInBackground);
                case SettingId.Language: return UserPreferences.Languages[UserPreferences.LanguageIndex(UserPreferences.DefaultLanguage)].Name;
                case SettingId.PauseOnFocusLoss: return OnOff(UserPreferences.DefaultPauseOnFocusLoss);
                case SettingId.DamageNumbers: return OnOff(UserPreferences.DefaultDamageNumbers);
                case SettingId.EnemyBars: return EnemyBarsName(UserPreferences.DefaultEnemyBars);
                case SettingId.UiScale: return "100%";
                case SettingId.ScreenShake: return UserPreferences.Percent(UserPreferences.DefaultScreenShake);
                case SettingId.Flashes: return FlashesName(UserPreferences.DefaultReduceFlashes);
                case SettingId.Movement: return "Мышь";
                case SettingId.AbilityRow: return "Q W E R";
                case SettingId.KeyBindings: return "стандартные";
                default: return null;
            }
        }

        /// <summary>Строка под описанием: «Стандартно: …» или что «Сбросить» опцию не трогает.</summary>
        public static string DefaultLine(SettingId id)
        {
            string text = DefaultText(id);
            return text != null ? "Стандартно: " + text : "«Сбросить» экран не меняет";
        }

        /// <summary>Текст окна «Сбросить …?»: что именно вернётся и что останется.</summary>
        public static string ResetSummary(SettingsTab tab)
        {
            switch (tab)
            {
                case SettingsTab.Audio: return "Громкость всех каналов и звук в фоне вернутся к стандартным.";
                case SettingsTab.Game: return "Язык, пауза при сворачивании, цифры урона и полоски врагов вернутся к стандартным.";
                case SettingsTab.Interface: return "Масштаб интерфейса, тряска экрана и вспышки вернутся к стандартным.";
                case SettingsTab.Controls: return "Схема движения, ряд способностей и все назначенные клавиши вернутся к стандартным.";
                default: return "Качество, тени, синхронизация, предел кадров и яркость вернутся к стандартным. Экран не изменится.";
            }
        }

        public static string OnOff(bool on) => on ? "Вкл" : "Выкл";

        public static string EnemyBarsName(EnemyBarMode mode)
            => mode == EnemyBarMode.EliteOnly ? "Элита" : mode == EnemyBarMode.None ? "Нет" : "У всех";

        public static string FlashesName(bool reduced) => reduced ? "Мягче" : "Как есть";
    }
}
