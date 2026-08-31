using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Единственный владелец пользовательских настроек. UI меняет значения
    /// здесь; звук и будущая музыка читают категорийные коэффициенты отсюда.
    /// </summary>
    public static class GameUserSettings
    {
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

        private static bool _loaded;

        public static float MasterVolume { get; private set; } = 1f;
        public static float EffectsVolume { get; private set; } = 1f;
        public static float MusicVolume { get; private set; } = 0.75f;
        public static int DisplayWidth { get; private set; }
        public static int DisplayHeight { get; private set; }
        public static FullScreenMode DisplayMode { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeLoad() => Load();

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

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
