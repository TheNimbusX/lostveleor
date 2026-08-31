using System;
using System.Collections;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Единственный владелец системной UI-навигации: пауза, настройки и
    /// возврат в лагерь. Экран не пишет в боевую симуляцию и блокирует её ввод.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(-200)]
    public sealed class PauseMenu : MonoBehaviour
    {
        private const float DesignWidth = 1920f;
        private const float DesignHeight = 1080f;
        private const float DisplayConfirmationSeconds = 15f;
        private const string CaptureFlag = "-capture-pause-menu";

        private enum Page : byte { Main, Graphics, Audio, ConfirmCamp, ConfirmDisplay }

        private static readonly FullScreenMode[] DisplayModes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.Windowed,
            FullScreenMode.FullScreenWindow,
        };

        private TickDriver _driver;
        private CombatJuiceView _combatJuice;
        private bool _open;
        private Page _page;
        private float _previousTimeScale = 1f;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private Vector2Int[] _resolutions = Array.Empty<Vector2Int>();
        private int _resolutionIndex;
        private FullScreenMode _displayMode;
        private float _master;
        private float _effects;
        private float _music;
        private bool _audioDirty;
        private string _displayStatus = string.Empty;
        private bool _displayPreviewActive;
        private float _displayConfirmationDeadline;
        private GameUserSettings.DisplayConfiguration _displayBeforePreview;
        private GameUserSettings.DisplayConfiguration _displayPreview;

        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _label;
        private GUIStyle _value;
        private GUIStyle _button;
        private GUIStyle _dangerButton;
        private GUIStyle _tab;
        private GUIStyle _activeTab;
        private GUIStyle _sliderStyle;
        private GUIStyle _sliderThumbStyle;
        private Texture2D _white;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _activeTexture;
        private Texture2D _dangerTexture;
        private Texture2D _clearTexture;
        private Texture2D _sliderTrackTexture;
        private Texture2D _sliderThumbTexture;

        private static readonly Color Backdrop = new Color(0.015f, 0.018f, 0.028f, 0.82f);
        private static readonly Color Panel = new Color(0.035f, 0.041f, 0.058f, 0.985f);
        private static readonly Color PanelEdge = new Color(0.15f, 0.19f, 0.24f, 0.95f);
        private static readonly Color Coral = new Color(1.00f, 0.34f, 0.22f, 1f);
        private static readonly Color Cyan = new Color(0.22f, 0.76f, 0.84f, 1f);
        private static readonly Color Text = new Color(0.93f, 0.94f, 0.91f, 1f);
        private static readonly Color Muted = new Color(0.61f, 0.66f, 0.70f, 1f);

        public bool IsOpen => _open;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _combatJuice = GetComponent<CombatJuiceView>();
            GameUserSettings.Load();
            _master = GameUserSettings.MasterVolume;
            _effects = GameUserSettings.EffectsVolume;
            _music = GameUserSettings.MusicVolume;
            _displayMode = GameUserSettings.DisplayMode;
            BuildResolutionList();
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, CaptureFlag);
            bool requested = CaptureRig.PauseMenuCaptureRequested || flag >= 0;
            if (!requested) return;

            Page page = Page.Main;
            string rawPage = CaptureRig.PauseMenuCapturePage;
            if (string.IsNullOrEmpty(rawPage) && flag + 1 < args.Length)
                rawPage = args[flag + 1];
            if (!string.IsNullOrEmpty(rawPage))
            {
                if (string.Equals(rawPage, "graphics", StringComparison.OrdinalIgnoreCase))
                    page = Page.Graphics;
                else if (string.Equals(rawPage, "audio", StringComparison.OrdinalIgnoreCase))
                    page = Page.Audio;
            }
            Debug.Log($"[pause-menu] capture open requested: {page}");
            StartCoroutine(OpenForCapture(page));
        }

        private IEnumerator OpenForCapture(Page page)
        {
            yield return null;
            Open(page);
        }

        private void Update()
        {
            if (_displayPreviewActive && Time.unscaledTime >= _displayConfirmationDeadline)
                CancelDisplayPreview("Изменения экрана отменены: время подтверждения истекло.");

            if (!EscapePressed()) return;
            if (!_open) Open(Page.Main);
            else if (_displayPreviewActive)
                CancelDisplayPreview("Изменения экрана отменены.");
            else if (_page != Page.Main)
            {
                SaveAudioIfNeeded();
                _page = Page.Main;
            }
            else Close();
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void Open(Page page)
        {
            if (_open)
            {
                _page = page;
                return;
            }

            _open = true;
            _page = page;
            _displayStatus = string.Empty;
            _previousTimeScale = _combatJuice != null
                ? _combatJuice.CancelHitStopForPause()
                : Time.timeScale;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _driver.SetGameplayPaused(true);
            Time.timeScale = 0f;
            if (CaptureRig.PauseMenuCaptureRequested)
                Debug.Log($"[pause-menu] opened: {page}");
        }

        private void Close()
        {
            if (!_open) return;
            if (_displayPreviewActive)
                CancelDisplayPreview("Изменения экрана отменены.");
            SaveAudioIfNeeded();
            _open = false;
            _page = Page.Main;
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _driver.SetGameplayPaused(false);
        }

        private void OnDisable()
        {
            if (_open) Close();
        }

        private void OnDestroy()
        {
            DestroyTexture(_white);
            DestroyTexture(_buttonTexture);
            DestroyTexture(_buttonHoverTexture);
            DestroyTexture(_activeTexture);
            DestroyTexture(_dangerTexture);
            DestroyTexture(_clearTexture);
            DestroyTexture(_sliderTrackTexture);
            DestroyTexture(_sliderThumbTexture);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
        }

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();
            GUI.depth = -1000;

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), Backdrop);

            float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
            float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));

            if (_page == Page.Main) DrawMain();
            else if (_page == Page.ConfirmCamp) DrawCampConfirmation();
            else if (_page == Page.ConfirmDisplay) DrawDisplayConfirmation();
            else DrawSettings();
            GUI.matrix = previous;
        }

        private void DrawMain()
        {
            var panel = new Rect(680f, 205f, 560f, 670f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 54f, panel.y + 48f, 450f, 58f), "ПАУЗА", _title);
            string context = _driver.Session != null && _driver.Session.Mode == GameMode.Rift
                ? "РАЗЛОМ ПРИОСТАНОВЛЕН"
                : "СИСТЕМНОЕ МЕНЮ";
            GUI.Label(new Rect(panel.x + 56f, panel.y + 108f, 450f, 32f), context, _subtitle);

            float x = panel.x + 60f;
            float y = panel.y + 186f;
            const float width = 440f;
            const float height = 68f;
            const float gap = 18f;
            if (GUI.Button(new Rect(x, y, width, height), "ПРОДОЛЖИТЬ", _button)) Close();
            y += height + gap;
            if (GUI.Button(new Rect(x, y, width, height), "НАСТРОЙКИ", _button))
                _page = Page.Graphics;
            y += height + gap;
            if (_driver.CanReturnToCamp &&
                GUI.Button(new Rect(x, y, width, height), "ВЕРНУТЬСЯ В ЛАГЕРЬ", _dangerButton))
            {
                if (_driver.Session != null && _driver.Session.Mode == GameMode.Rift)
                    _page = Page.ConfirmCamp;
                else
                {
                    _driver.ReturnToCampFromMenu();
                    Close();
                }
            }

            GUI.Label(new Rect(panel.x + 56f, panel.yMax - 70f, 450f, 30f),
                "ESC — продолжить игру", _subtitle);
        }

        private void DrawCampConfirmation()
        {
            var panel = new Rect(630f, 310f, 660f, 460f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 54f, panel.y + 48f, 550f, 58f),
                "ПОКИНУТЬ РАЗЛОМ?", _title);
            GUI.Label(new Rect(panel.x + 56f, panel.y + 124f, 548f, 72f),
                "Текущий забег завершится. Невзятые награды будут потеряны.", _subtitle);

            float x = panel.x + 60f;
            if (GUI.Button(new Rect(x, panel.y + 238f, 540f, 64f),
                    "ВЕРНУТЬСЯ В ЛАГЕРЬ", _dangerButton))
            {
                _driver.ReturnToCampFromMenu();
                Close();
            }
            if (GUI.Button(new Rect(x, panel.y + 320f, 540f, 64f), "ОТМЕНА", _button))
                _page = Page.Main;
        }

        private void DrawSettings()
        {
            var panel = new Rect(550f, 145f, 820f, 790f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 48f, panel.y + 38f, 510f, 56f), "НАСТРОЙКИ", _title);
            if (GUI.Button(new Rect(panel.xMax - 174f, panel.y + 42f, 124f, 48f), "НАЗАД", _button))
            {
                SaveAudioIfNeeded();
                _page = Page.Main;
            }

            var graphicsTab = new Rect(panel.x + 48f, panel.y + 116f, 344f, 54f);
            var audioTab = new Rect(panel.x + 408f, panel.y + 116f, 344f, 54f);
            if (GUI.Button(graphicsTab, "ГРАФИКА", _page == Page.Graphics ? _activeTab : _tab))
                _page = Page.Graphics;
            if (GUI.Button(audioTab, "ЗВУК", _page == Page.Audio ? _activeTab : _tab))
                _page = Page.Audio;

            if (_page == Page.Graphics) DrawGraphics(panel);
            else DrawAudio(panel);
        }

        private void DrawGraphics(Rect panel)
        {
            float x = panel.x + 64f;
            float y = panel.y + 226f;
            DrawSectionTitle(x, y, "ЭКРАН");
            y += 68f;

            GUI.Label(new Rect(x, y, 260f, 38f), "Режим экрана", _label);
            int modeIndex = Array.IndexOf(DisplayModes, _displayMode);
            if (modeIndex < 0) modeIndex = 2;
            modeIndex = DrawChoice(new Rect(x + 282f, y - 4f, 410f, 50f),
                ModeName(DisplayModes[modeIndex]), modeIndex, DisplayModes.Length);
            _displayMode = DisplayModes[modeIndex];
            y += 86f;

            GUI.Label(new Rect(x, y, 260f, 38f), "Разрешение", _label);
            if (_displayMode == FullScreenMode.FullScreenWindow)
            {
                GameUserSettings.DisplayConfiguration native = GameUserSettings.ResolveDisplay(
                    _resolutions[_resolutionIndex].x, _resolutions[_resolutionIndex].y, _displayMode);
                GUI.Label(new Rect(x + 282f, y - 4f, 410f, 50f),
                    $"{ResolutionName(new Vector2Int(native.Width, native.Height))}  МОНИТОР",
                    _value);
            }
            else
            {
                _resolutionIndex = DrawChoice(new Rect(x + 282f, y - 4f, 410f, 50f),
                    ResolutionName(_resolutions[_resolutionIndex]), _resolutionIndex,
                    _resolutions.Length);
            }
            y += 110f;

            GUI.Label(new Rect(x, y, 690f, 52f),
                _displayMode == FullScreenMode.FullScreenWindow
                    ? "Без рамок использует нативное разрешение текущего монитора."
                    : "Новое разрешение можно безопасно проверить перед сохранением.",
                _subtitle);
            y += 92f;
            if (GUI.Button(new Rect(x, y, 320f, 62f), "ПРИМЕНИТЬ", _button))
            {
                Vector2Int resolution = _resolutions[_resolutionIndex];
                BeginDisplayPreview(resolution.x, resolution.y, _displayMode);
            }

            if (!string.IsNullOrEmpty(_displayStatus))
                GUI.Label(new Rect(x, y + 80f, 690f, 36f), _displayStatus, _subtitle);
        }

        private void DrawDisplayConfirmation()
        {
            var panel = new Rect(630f, 285f, 660f, 510f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 54f, panel.y + 48f, 550f, 58f),
                "СОХРАНИТЬ ЭКРАН?", _title);
            GUI.Label(new Rect(panel.x + 56f, panel.y + 124f, 548f, 72f),
                "Если изображение отображается правильно, подтвердите новый режим.",
                _subtitle);

            int seconds = Mathf.Max(0,
                Mathf.CeilToInt(_displayConfirmationDeadline - Time.unscaledTime));
            GUI.Label(new Rect(panel.x + 56f, panel.y + 202f, 548f, 44f),
                $"Автоматический возврат через {seconds} сек.", _label);

            float x = panel.x + 60f;
            if (GUI.Button(new Rect(x, panel.y + 282f, 540f, 64f),
                    "СОХРАНИТЬ", _button))
                ConfirmDisplayPreview();
            if (GUI.Button(new Rect(x, panel.y + 364f, 540f, 64f),
                    "ВЕРНУТЬ ПРЕЖНИЕ", _dangerButton))
                CancelDisplayPreview("Предыдущие настройки экрана восстановлены.");
        }

        private void BeginDisplayPreview(int width, int height, FullScreenMode mode)
        {
            _displayBeforePreview = GameUserSettings.CaptureCurrentDisplay();
            _displayPreview = GameUserSettings.PreviewDisplay(width, height, mode);
            _displayPreviewActive = true;
            _displayConfirmationDeadline = Time.unscaledTime + DisplayConfirmationSeconds;
            _displayStatus = string.Empty;
            _page = Page.ConfirmDisplay;
        }

        private void ConfirmDisplayPreview()
        {
            if (!_displayPreviewActive) return;
            GameUserSettings.ConfirmDisplay(_displayPreview);
            _displayPreviewActive = false;
            _displayMode = _displayPreview.Mode;
            SelectResolution(_displayPreview.Width, _displayPreview.Height);
            var confirmedResolution = new Vector2Int(
                _displayPreview.Width, _displayPreview.Height);
            _displayStatus = $"Сохранено: {ResolutionName(confirmedResolution)}, "
                             + ModeName(_displayPreview.Mode);
            _page = Page.Graphics;
        }

        private void CancelDisplayPreview(string status)
        {
            if (!_displayPreviewActive) return;
            GameUserSettings.PreviewDisplay(_displayBeforePreview.Width,
                _displayBeforePreview.Height, _displayBeforePreview.Mode);
            _displayPreviewActive = false;
            _displayMode = _displayBeforePreview.Mode;
            SelectResolution(_displayBeforePreview.Width, _displayBeforePreview.Height);
            _displayStatus = status;
            _page = Page.Graphics;
        }

        private void DrawAudio(Rect panel)
        {
            float x = panel.x + 64f;
            float y = panel.y + 226f;
            DrawSectionTitle(x, y, "ГРОМКОСТЬ");
            y += 86f;
            _master = DrawSlider(x, y, "Общая громкость", _master);
            y += 112f;
            _effects = DrawSlider(x, y, "Эффекты", _effects);
            y += 112f;
            _music = DrawSlider(x, y, "Музыка", _music);

            GameUserSettings.SetAudio(_master, _effects, _music);
            GUI.Label(new Rect(x, panel.yMax - 112f, 690f, 44f),
                "Изменения сохраняются при выходе из настроек.", _subtitle);
        }

        private void DrawSectionTitle(float x, float y, string text)
        {
            Fill(new Rect(x, y + 8f, 5f, 34f), Coral);
            GUI.Label(new Rect(x + 20f, y, 400f, 48f), text, _subtitle);
        }

        private int DrawChoice(Rect rect, string text, int current, int count)
        {
            if (GUI.Button(new Rect(rect.x, rect.y, 52f, rect.height), "<", _button))
                current = (current - 1 + count) % count;
            GUI.Label(new Rect(rect.x + 64f, rect.y, rect.width - 128f, rect.height), text, _value);
            if (GUI.Button(new Rect(rect.xMax - 52f, rect.y, 52f, rect.height), ">", _button))
                current = (current + 1) % count;
            return current;
        }

        private float DrawSlider(float x, float y, string title, float value)
        {
            GUI.Label(new Rect(x, y, 300f, 38f), title, _label);
            GUI.Label(new Rect(x + 612f, y, 80f, 38f), $"{Mathf.RoundToInt(value * 100f)}%", _value);
            var track = new Rect(x, y + 60f, 690f, 8f);
            GUI.DrawTexture(track, _sliderTrackTexture);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * value, track.height), _activeTexture);
            float next = GUI.HorizontalSlider(new Rect(x - 8f, y + 49f, 706f, 30f),
                value, 0f, 1f, _sliderStyle, _sliderThumbStyle);
            if (!Mathf.Approximately(next, value)) _audioDirty = true;
            return next;
        }

        private void SaveAudioIfNeeded()
        {
            if (!_audioDirty) return;
            GameUserSettings.SetAudio(_master, _effects, _music);
            GameUserSettings.SaveAudio();
            _audioDirty = false;
        }

        private void BuildResolutionList()
        {
            var values = new List<Vector2Int>(32);
            var seen = new HashSet<long>();
            Resolution[] available = Screen.resolutions;
            for (int i = 0; i < available.Length; i++)
                AddResolution(values, seen, available[i].width, available[i].height);
            AddResolution(values, seen, GameUserSettings.DisplayWidth, GameUserSettings.DisplayHeight);
            AddResolution(values, seen, Screen.width, Screen.height);
            if (Display.main != null)
                AddResolution(values, seen, Display.main.systemWidth, Display.main.systemHeight);
            if (values.Count == 0) values.Add(new Vector2Int(1920, 1080));

            values.Sort((a, b) =>
            {
                int width = a.x.CompareTo(b.x);
                return width != 0 ? width : a.y.CompareTo(b.y);
            });
            _resolutions = values.ToArray();
            _resolutionIndex = 0;
            for (int i = 0; i < _resolutions.Length; i++)
            {
                if (_resolutions[i].x != GameUserSettings.DisplayWidth ||
                    _resolutions[i].y != GameUserSettings.DisplayHeight) continue;
                _resolutionIndex = i;
                break;
            }
        }

        private void SelectResolution(int width, int height)
        {
            for (int i = 0; i < _resolutions.Length; i++)
            {
                if (_resolutions[i].x != width || _resolutions[i].y != height) continue;
                _resolutionIndex = i;
                return;
            }
        }

        private static void AddResolution(List<Vector2Int> values, HashSet<long> seen,
            int width, int height)
        {
            if (width < 640 || height < 360) return;
            long key = ((long)width << 32) | (uint)height;
            if (seen.Add(key)) values.Add(new Vector2Int(width, height));
        }

        private static string ResolutionName(Vector2Int value) => $"{value.x} × {value.y}";

        private static string ModeName(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return "На весь экран";
                case FullScreenMode.Windowed: return "В окне";
                default: return "Без рамок";
            }
        }

        private void DrawPanel(Rect rect)
        {
            Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), PanelEdge);
            Fill(rect, Panel);
            Fill(new Rect(rect.x, rect.y, 7f, rect.height), Coral);
        }

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _white = MakeTexture(Color.white);
            _buttonTexture = MakeTexture(new Color(0.09f, 0.115f, 0.145f, 1f));
            _buttonHoverTexture = MakeTexture(new Color(0.15f, 0.20f, 0.24f, 1f));
            _activeTexture = MakeTexture(new Color(0.12f, 0.34f, 0.39f, 1f));
            _dangerTexture = MakeTexture(new Color(0.31f, 0.105f, 0.08f, 1f));
            _clearTexture = MakeTexture(Color.clear);
            _sliderTrackTexture = MakeTexture(new Color(0.13f, 0.16f, 0.19f, 1f));
            _sliderThumbTexture = MakeTexture(Coral);

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            _title.normal.textColor = Text;
            _subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            _subtitle.normal.textColor = Muted;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = Text;
            _value = new GUIStyle(_label)
            {
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            _value.normal.textColor = Cyan;
            _button = MakeButtonStyle(_buttonTexture, _buttonHoverTexture, Text);
            _dangerButton = MakeButtonStyle(_dangerTexture, _buttonHoverTexture,
                new Color(1f, 0.78f, 0.72f));
            _tab = MakeButtonStyle(_buttonTexture, _buttonHoverTexture, Muted);
            _activeTab = MakeButtonStyle(_activeTexture, _activeTexture, Text);
            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 8f,
            };
            _sliderStyle.normal.background = _clearTexture;
            _sliderStyle.hover.background = _clearTexture;
            _sliderStyle.active.background = _clearTexture;
            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 18f,
                fixedHeight = 30f,
            };
            _sliderThumbStyle.normal.background = _sliderThumbTexture;
            _sliderThumbStyle.hover.background = _buttonHoverTexture;
            _sliderThumbStyle.active.background = _buttonHoverTexture;
        }

        private static GUIStyle MakeButtonStyle(Texture2D normal, Texture2D hover, Color text)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(16, 16, 8, 8),
            };
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = hover;
            style.focused.background = normal;
            style.normal.textColor = text;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = text;
            return style;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }
    }
}
