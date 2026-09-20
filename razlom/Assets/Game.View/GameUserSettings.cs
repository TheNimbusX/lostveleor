using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Единственный владелец пользовательских настроек. UI меняет значения
    /// здесь; звук и будущая музыка читают категорийные коэффициенты отсюда.
    /// </summary>
    public static class GameUserSettings
    {
        /// <summary>
        /// Какой ряд клавиш держит четыре способности.
        ///
        /// Ряд, а не список из четырёх привязок: полноценный ремап клавиш —
        /// это отдельный экран с конфликтами, дефолтами и сбросом, а весь
        /// спор здесь ровно один — рука на QWER против руки на цифрах.
        /// Значения пишутся в PlayerPrefs как числа, поэтому порядок менять
        /// нельзя: сохранённая единица должна остаться цифровым рядом.
        /// </summary>
        public enum AbilityLayout : byte
        {
            Qwer = 0,
            Digits = 1,
        }

        public readonly struct DisplayConfiguration
        {
            public DisplayConfiguration(int width, int height, FullScreenMode mode)
            {
                Width = width;
                Height = height;
                Mode = mode;
            }

            public int Width { get; }
            public int Height { get; }
            public FullScreenMode Mode { get; }
        }

        private const string MasterKey = "settings.audio.master";
        private const string EffectsKey = "settings.audio.effects";
        private const string MusicKey = "settings.audio.music";
        private const string WidthKey = "settings.display.width";
        private const string HeightKey = "settings.display.height";
        private const string ModeKey = "settings.display.mode";
        public static bool WasdMovement { get; private set; }
        private const string MovementKey = "settings.controls.wasd";
        private const string AbilityLayoutKey = "settings.controls.abilityLayout";
        private const string FrameCapKey = "settings.display.frameCap";
        private const string QualityKey = "settings.display.quality";
        private const string FrameLimitKey = "settings.display.frameLimit";
        private const string ShadowsKey = "settings.display.shadows";
        private const string UiScaleKey = "settings.ui.scale";

        /// <summary>
        /// Пределы кадров для слайдера при выключенной синхронизации (−1 — без предела).
        /// Все они есть в <see cref="FrameCaps"/>: тумблер и слайдер — только другой
        /// вид одной настройки FrameCap, противоречивой пары у игрока не бывает.
        /// </summary>
        public static readonly int[] FrameLimits = { 60, 120, 144, 240, -1 };

        /// <summary>Дальность и разрешение теней; уровни совпадают с пресетами качества.</summary>
        public enum ShadowLevel : byte
        {
            Low = 0,
            Medium = 1,
            High = 2,
        }

        public const float UiScaleMin = 0.8f;
        public const float UiScaleMax = 1.2f;

        /// <summary>
        /// Потолок кадров. 0 — вертикальная синхронизация, -1 — без ограничения,
        /// иначе это сам предел в кадрах.
        ///
        /// Одна настройка, а не две. Unity ИГНОРИРУЕТ targetFrameRate, пока
        /// vSyncCount больше нуля, поэтому раздельные «синхронизация» и «предел»
        /// давали бы игроку выставить пару, в которой одно молча не работает.
        /// </summary>
        public static readonly int[] FrameCaps = { 0, 60, 120, 144, 240, -1 };

        /// <summary>
        /// Уровень качества картинки.
        ///
        /// Существует ради одной заявленной цели: 60 кадров на встроенной
        /// графике пятилетней давности. Достигается она не тем, что игра
        /// «оптимизирована», а тем, что у игрока есть ручка. Пресет крутит
        /// ровно те четыре величины, которые на слабой видеокарте решают:
        /// масштаб рендера, сглаживание, разрешение и дальность теней.
        /// </summary>
        public enum QualityLevel : byte
        {
            Low = 0,
            Medium = 1,
            High = 2,
        }

        private static bool _loaded;

        public static float MasterVolume { get; private set; } = 1f;
        public static float EffectsVolume { get; private set; } = 1f;
        public static float MusicVolume { get; private set; } = 0.75f;
        public static int DisplayWidth { get; private set; }
        public static int DisplayHeight { get; private set; }
        public static FullScreenMode DisplayMode { get; private set; }

        /// <summary>
        /// По умолчанию QWER: рука лежит на нём не сходя с WASD-позиции, и до
        /// четвёртого слота не нужно тянуться через полклавиатуры.
        /// </summary>
        public static AbilityLayout Abilities { get; private set; } = AbilityLayout.Qwer;

        /// <summary>
        /// Правда только для QWER. Живое пересечение с командами режимов
        /// ровно одно — E на Полигоне, — и на цифровом ряду его нет вовсе.
        /// </summary>
        public static bool AbilityRowUsesLetters => !WasdMovement && Abilities == AbilityLayout.Qwer;

        /// <summary>
        /// По умолчанию вертикальная синхронизация: ровный такт кадров читается
        /// как плавность сильнее, чем большее среднее число кадров с рваным
        /// шагом. Разрывы кадра она снимает заодно.
        /// </summary>
        public static int FrameCap { get; private set; }

        public static QualityLevel Quality { get; private set; } = QualityLevel.High;

        /// <summary>Тумблер «Вертикальная синхронизация» — это FrameCap == 0.</summary>
        public static bool VSync => FrameCap == 0;

        /// <summary>Предел, который включится при выключении синхронизации.</summary>
        public static int FrameLimit { get; private set; } = 144;

        /// <summary>
        /// Тени отдельно от качества. Смена «Качества» ставит тени того же уровня
        /// (пресет остаётся рецептом), после этого игрок может поменять только тени.
        /// Без сохранённого значения — уровень качества: вид у игрока не меняется.
        /// </summary>
        public static ShadowLevel Shadows { get; private set; } = ShadowLevel.High;

        /// <summary>Масштаб HUD и меню, 0.8–1.2.</summary>
        public static float UiScale { get; private set; } = 1f;

        /// <summary>Сменился масштаб интерфейса — Canvas'ы пересчитывают CanvasScaler.</summary>
        public static event Action UiScaleChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeLoad() => Load();

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            WasdMovement = PlayerPrefs.GetInt(MovementKey, 0) == 1;
            FrameCap = PlayerPrefs.GetInt(FrameCapKey, 0);
            if (Array.IndexOf(FrameCaps, FrameCap) < 0) FrameCap = 0;
            int limit = PlayerPrefs.GetInt(FrameLimitKey, FrameCap != 0 ? FrameCap : 144);
            FrameLimit = Array.IndexOf(FrameLimits, limit) >= 0 ? limit : 144;
            ApplyFrameCap();

            int qualityValue = PlayerPrefs.GetInt(QualityKey, (int)QualityLevel.High);
            Quality = Enum.IsDefined(typeof(QualityLevel), (byte)qualityValue)
                ? (QualityLevel)qualityValue
                : QualityLevel.High;
            int shadowValue = PlayerPrefs.GetInt(ShadowsKey, (int)Quality);
            Shadows = Enum.IsDefined(typeof(ShadowLevel), (byte)shadowValue) ? (ShadowLevel)shadowValue : (ShadowLevel)Quality;
            ApplyQuality();

            UiScale = Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, 1f), UiScaleMin, UiScaleMax);

            int layoutValue = PlayerPrefs.GetInt(AbilityLayoutKey, (int)AbilityLayout.Qwer);
            Abilities = Enum.IsDefined(typeof(AbilityLayout), (byte)layoutValue)
                ? (AbilityLayout)layoutValue
                : AbilityLayout.Qwer;

            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.75f));
            AudioListener.volume = MasterVolume;

            DisplayWidth = Mathf.Max(640, PlayerPrefs.GetInt(WidthKey, Screen.width));
            DisplayHeight = Mathf.Max(360, PlayerPrefs.GetInt(HeightKey, Screen.height));
            int modeValue = PlayerPrefs.GetInt(ModeKey, (int)Screen.fullScreenMode);
            DisplayMode = Enum.IsDefined(typeof(FullScreenMode), modeValue)
                ? (FullScreenMode)modeValue
                : FullScreenMode.FullScreenWindow;

            if (PlayerPrefs.HasKey(WidthKey))
            {
                DisplayConfiguration resolved = ResolveDisplay(
                    DisplayWidth, DisplayHeight, DisplayMode);
                DisplayWidth = resolved.Width;
                DisplayHeight = resolved.Height;
                Screen.SetResolution(resolved.Width, resolved.Height, resolved.Mode);
            }
        }

        /// <summary>
        /// Кладёт выбранный потолок в движок. Вызывается и при загрузке, и при
        /// каждой смене: настройка, которая применяется только после
        /// перезапуска, читается игроком как сломанная.
        /// </summary>
        public static void ApplyFrameCap()
        {
            if (FrameCap == 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            }

            // Строка в лог, а не «должно работать»: настройка потолка кадров
            // проверяется только тем, что движок реально принял оба значения.
            Debug.Log($"[Разлом] Потолок кадров: {FrameCapName(FrameCap)} → "
                      + $"vSyncCount={QualitySettings.vSyncCount}, "
                      + $"targetFrameRate={Application.targetFrameRate}");
        }

        /// <summary>
        /// Кладёт пресет в живой URP-ассет.
        ///
        /// В собранной игре ассет конвейера — это загруженная копия, поэтому
        /// правка живёт до конца сессии и обратно в файл не течёт. Сохраняем мы
        /// не значения, а выбранный уровень: пресет — это рецепт, а не набор
        /// чисел, и менять его надо в одном месте.
        /// </summary>
        public static void ApplyQuality()
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UniversalRenderPipelineAsset;
            if (pipeline == null) return;

            switch (Quality)
            {
                case QualityLevel.Low:
                    // Масштаб рендера — единственная ручка, которая на встройке
                    // даёт кратный выигрыш: 0.75 это 56 % пикселей.
                    pipeline.renderScale = 0.75f;
                    pipeline.msaaSampleCount = 1;
                    pipeline.shadowDistance = 22f;
                    pipeline.mainLightShadowmapResolution = 1024;
                    QualitySettings.softParticles = false;
                    break;

                case QualityLevel.Medium:
                    pipeline.renderScale = 1f;
                    pipeline.msaaSampleCount = 2;
                    pipeline.shadowDistance = 100f;
                    pipeline.mainLightShadowmapResolution = 2048;
                    break;

                default:
                    // Высокое — ровно то, что настроено в ассете руками. Здесь
                    // числа повторены, чтобы возврат с низкого их восстановил.
                    pipeline.renderScale = 1f;
                    pipeline.msaaSampleCount = 4;
                    pipeline.shadowDistance = 150f;
                    pipeline.mainLightShadowmapResolution = 4096;
                    break;
            }
            ApplyShadows(pipeline);
        }

        /// <summary>Тени поверх пресета: те же три пары чисел, что у уровней качества.</summary>
        private static void ApplyShadows(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;
            switch (Shadows)
            {
                case ShadowLevel.Low:
                    pipeline.shadowDistance = 22f;
                    pipeline.mainLightShadowmapResolution = 1024;
                    break;
                case ShadowLevel.Medium:
                    pipeline.shadowDistance = 100f;
                    pipeline.mainLightShadowmapResolution = 2048;
                    break;
                default:
                    pipeline.shadowDistance = 150f;
                    pipeline.mainLightShadowmapResolution = 4096;
                    break;
            }
        }

        public static void SetShadows(ShadowLevel level)
        {
            Load();
            if (!Enum.IsDefined(typeof(ShadowLevel), level) || Shadows == level) return;
            Shadows = level;
            ApplyShadows(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset);
            PlayerPrefs.SetInt(ShadowsKey, (int)level);
            PlayerPrefs.Save();
        }

        public static string ShadowName(ShadowLevel level)
            => level == ShadowLevel.Low ? "Низкие" : level == ShadowLevel.Medium ? "Средние" : "Высокие";

        public static void SetVSync(bool on)
        {
            Load();
            SetFrameCap(on ? 0 : FrameLimit);
        }

        public static void SetFrameLimit(int limit)
        {
            Load();
            if (Array.IndexOf(FrameLimits, limit) < 0) return;
            FrameLimit = limit;
            PlayerPrefs.SetInt(FrameLimitKey, limit);
            if (!VSync) SetFrameCap(limit);
            else PlayerPrefs.Save();
        }

        public static void SetUiScale(float scale)
        {
            Load();
            scale = Mathf.Clamp(Mathf.Round(scale * 20f) / 20f, UiScaleMin, UiScaleMax);
            if (Mathf.Approximately(UiScale, scale)) return;
            UiScale = scale;
            PlayerPrefs.SetFloat(UiScaleKey, scale);
            PlayerPrefs.Save();
            UiScaleChanged?.Invoke();
        }

        /// <summary>«Сбросить» на вкладке графики. Экран не трогает: его смена идёт через подтверждение.</summary>
        public static void ResetGraphics()
        {
            Load();
            SetQuality(QualityLevel.High);
            SetShadows(ShadowLevel.High);
            FrameLimit = 144;
            PlayerPrefs.SetInt(FrameLimitKey, FrameLimit);
            SetFrameCap(0);
            SetUiScale(1f);
            PlayerPrefs.Save();
        }

        public static void ResetAudio()
        {
            SetAudio(1f, 1f, 0.75f);
            SaveAudio();
        }

        public static void ResetControls()
        {
            SetAbilityLayout(AbilityLayout.Qwer);
            GameKeyBindings.ResetAll();
        }

        public static void SetQuality(QualityLevel level)
        {
            Load();
            if (!Enum.IsDefined(typeof(QualityLevel), level)) level = QualityLevel.High;
            if (Quality == level) return;

            Quality = level;
            // Пресет качества ставит и тени своего уровня; отдельная строка «Тени» — после.
            Shadows = (ShadowLevel)level;
            ApplyQuality();
            PlayerPrefs.SetInt(QualityKey, (int)level);
            PlayerPrefs.SetInt(ShadowsKey, (int)Shadows);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Тот же пресет, но без записи в настройки. Нужен съёмке: замер трёх
        /// уровней подряд не должен оставлять после себя чужой выбор качества.
        /// </summary>
        public static void OverrideQuality(QualityLevel level)
        {
            Load();
            if (!Enum.IsDefined(typeof(QualityLevel), level)) return;
            Quality = level;
            // Замер уровня качества — вместе с его тенями, как до отдельной строки «Тени».
            Shadows = (ShadowLevel)level;
            ApplyQuality();
        }

        public static string QualityName(QualityLevel level)
        {
            switch (level)
            {
                case QualityLevel.Low: return "Низкое";
                case QualityLevel.Medium: return "Среднее";
                default: return "Высокое";
            }
        }

        public static void SetFrameCap(int cap)
        {
            Load();
            if (Array.IndexOf(FrameCaps, cap) < 0) return;
            if (FrameCap == cap) return;

            FrameCap = cap;
            ApplyFrameCap();
            PlayerPrefs.SetInt(FrameCapKey, cap);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Потолок без записи в настройки — для съёмки, как и с качеством.
        /// </summary>
        public static void OverrideFrameCap(int cap)
        {
            Load();
            if (Array.IndexOf(FrameCaps, cap) < 0) return;
            FrameCap = cap;
            ApplyFrameCap();
        }

        public static string FrameCapName(int cap)
        {
            if (cap == 0) return "Синхронизация";
            if (cap < 0) return "Без ограничения";
            return cap + " кадров";
        }

        /// <summary>
        /// Раскладка сохраняется сразу, а не при выходе из настроек, как звук.
        /// Переключение мгновенно видно на панели способностей и в подсказках,
        /// отменять его нечем и незачем — сохранять на выходе значило бы дать
        /// шанс потерять уже применённый выбор.
        /// </summary>
        public static void SetWasdMovement(bool value)
        {
            Load();
            if (WasdMovement == value) return;
            WasdMovement = value;
            PlayerPrefs.SetInt(MovementKey, value ? 1 : 0);
            PlayerPrefs.Save();
            GameKeyBindings.NotifyLayoutChanged();
        }

        public static void SetAbilityLayout(AbilityLayout layout)
        {
            Load();
            if (!Enum.IsDefined(typeof(AbilityLayout), layout)) layout = AbilityLayout.Qwer;
            if (Abilities == layout) return;

            Abilities = layout;
            PlayerPrefs.SetInt(AbilityLayoutKey, (int)layout);
            PlayerPrefs.Save();
            GameKeyBindings.NotifyLayoutChanged();
        }

        public static void SetAudio(float master, float effects, float music)
        {
            Load();
            MasterVolume = Mathf.Clamp01(master);
            EffectsVolume = Mathf.Clamp01(effects);
            MusicVolume = Mathf.Clamp01(music);
            AudioListener.volume = MasterVolume;
        }

        public static void SaveAudio()
        {
            PlayerPrefs.SetFloat(MasterKey, MasterVolume);
            PlayerPrefs.SetFloat(EffectsKey, EffectsVolume);
            PlayerPrefs.SetFloat(MusicKey, MusicVolume);
            PlayerPrefs.Save();
        }

        public static DisplayConfiguration CaptureCurrentDisplay()
        {
            return new DisplayConfiguration(
                Mathf.Max(640, Screen.width),
                Mathf.Max(360, Screen.height),
                Screen.fullScreenMode);
        }

        public static DisplayConfiguration PreviewDisplay(int width, int height,
            FullScreenMode mode)
        {
            DisplayConfiguration resolved = ResolveDisplay(width, height, mode);
            Screen.SetResolution(resolved.Width, resolved.Height, resolved.Mode);
            return resolved;
        }

        public static void ConfirmDisplay(DisplayConfiguration configuration)
        {
            Load();
            DisplayWidth = Mathf.Max(640, configuration.Width);
            DisplayHeight = Mathf.Max(360, configuration.Height);
            DisplayMode = configuration.Mode;

            PlayerPrefs.SetInt(WidthKey, DisplayWidth);
            PlayerPrefs.SetInt(HeightKey, DisplayHeight);
            PlayerPrefs.SetInt(ModeKey, (int)DisplayMode);
            PlayerPrefs.Save();
        }

        public static DisplayConfiguration ResolveDisplay(int width, int height,
            FullScreenMode mode)
        {
            int resolvedWidth = Mathf.Max(640, width);
            int resolvedHeight = Mathf.Max(360, height);
            if (mode == FullScreenMode.FullScreenWindow)
            {
                int nativeWidth = Display.main != null ? Display.main.systemWidth : 0;
                int nativeHeight = Display.main != null ? Display.main.systemHeight : 0;
                if (nativeWidth >= 640 && nativeHeight >= 360)
                {
                    resolvedWidth = nativeWidth;
                    resolvedHeight = nativeHeight;
                }
            }

            return new DisplayConfiguration(resolvedWidth, resolvedHeight, mode);
        }

        /// <summary>Громкость для AudioSource с музыкой после его authored volume.</summary>
        public static float MusicGain => MusicVolume;
    }
}
