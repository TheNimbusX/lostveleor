using System;
using System.Collections;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.EventSystems;

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

        // Подсказки вкладки «Управление» в формате листа 5 «[Клавиша] Действие» (UiKeyHint); константы — без строки на кадр.
        private const string WasdControlsHint = "[WASD] Движение" + UiKeyHint.Separator + "[Мышь] Прицел" + UiKeyHint.Separator + "[1–4] Способности";
        private const string MouseControlsHint = "[ПКМ] Движение" + UiKeyHint.Separator + "[ЛКМ] Атака. Нажми на клавишу справа, чтобы переназначить.";

        private enum Page : byte { Main, Graphics, Controls, Audio, ConfirmCamp, ConfirmDisplay, ConfirmQuit, Game, Interface, ConfirmReset }

        private static readonly GameUserSettings.QualityLevel[] QualityLevels =
        {
            GameUserSettings.QualityLevel.Low,
            GameUserSettings.QualityLevel.Medium,
            GameUserSettings.QualityLevel.High,
        };

        private static readonly GameUserSettings.AbilityLayout[] AbilityLayouts =
        {
            GameUserSettings.AbilityLayout.Qwer,
            GameUserSettings.AbilityLayout.Digits,
        };

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

        // Меню на Canvas (префаб правится в Unity). Нет префаба — прежний IMGUI ниже.
        private PauseMenuView _view;
        // В Canvas-меню панель настроек видна и на главной странице паузы —
        // с последней открытой вкладкой, как в концепте v3-pause.
        private Page _settingsTab = Page.Graphics;

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

            // С 23 сентября первым берётся меню на паке «Ночная акварель» (PauseMenuWc);
            // прежнее PauseMenu — запасное: удалить новый префаб, и игра вернётся к нему.
            var prefab = Resources.Load<GameObject>("UI/Prefabs/PauseMenuWc") ?? Resources.Load<GameObject>("UI/Prefabs/PauseMenu");
            if (prefab != null)
            {
                _view = Instantiate(prefab, transform).GetComponentInChildren<PauseMenuView>(true);
                if (_view != null)
                {
                    _view.gameObject.SetActive(false);
                    BindView();
                }
            }
        }

        /// <summary>
        /// Окно «Настройки Б» (30.09): пять вкладок столбцом, «Управление» — вкладка, а не своё окно.
        /// Старый префаб без вкладки управления живёт по-прежнему.
        /// </summary>
        private bool ColumnLayout => _view != null && _view.ControlsPage != null;

        private bool IsSettingsTab(Page page) => page == Page.Graphics || page == Page.Audio || page == Page.Game
            || page == Page.Interface || page == Page.Controls && ColumnLayout;
        private static bool IsConfirmPage(Page page) => page == Page.ConfirmCamp || page == Page.ConfirmDisplay || page == Page.ConfirmQuit
            || page == Page.ConfirmReset;

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
                else if (string.Equals(rawPage, "controls", StringComparison.OrdinalIgnoreCase))
                    page = Page.Controls;
                else if (string.Equals(rawPage, "audio", StringComparison.OrdinalIgnoreCase))
                    page = Page.Audio;
                else if (string.Equals(rawPage, "game", StringComparison.OrdinalIgnoreCase))
                    page = Page.Game;
                else if (string.Equals(rawPage, "interface", StringComparison.OrdinalIgnoreCase))
                    page = Page.Interface;
                else if (string.Equals(rawPage, "tour", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[pause-menu] capture tour requested");
                    StartCoroutine(TourForCapture());
                    return;
                }
            }
            Debug.Log($"[pause-menu] capture open requested: {page}");
            StartCoroutine(OpenForCapture(page));
        }

        /// <summary>
        /// Съёмка анимаций: проходит меню теми же вызовами, что и кнопки, на
        /// реальном времени (в паузе игровое время стоит). Только под -capture-pause-menu tour.
        /// </summary>
        private IEnumerator TourForCapture()
        {
            yield return null;
            void Step(string what) => Debug.Log($"[pause-menu] tour {UiMotion.Now:0.00}: {what}");
            // Ждём по часам интерфейса: при записи видео они идут шагом кадра записи,
            // а по настоящим часам тур обгонял запись в 1,7 раза (кадры пишутся медленнее 30 в секунду).
            IEnumerator Wait(float seconds)
            {
                float until = UiMotion.Now + seconds;
                while (UiMotion.Now < until) yield return null;
            }
            Open(Page.Main); Step("pause");
            yield return Wait(1.6f);
            ShowTab(Page.Graphics); Step("settings · graphics");
            yield return Wait(1.8f);
            ShowTab(Page.Audio); Step("audio");
            yield return Wait(1.4f);
            ShowTab(Page.Game); Step("game");
            yield return Wait(1.4f);
            ShowTab(Page.Interface); Step("interface");
            yield return Wait(1.4f);
            ShowTab(Page.Controls); Step("controls");
            yield return Wait(1.4f);
            _waitingBinding = 0; Step("waiting for key");
            yield return Wait(2f);
            // Вспышка строки как при обмене — без записи клавиш: у съёмки общие настройки с игрой.
            _waitingBinding = -1;
            if (_bindingRows.Length > 1) PauseMenuView.Flash(_bindingRows[1]);
            Step("swap flash");
            yield return Wait(1.2f);
            BackToPause(); Step("back to pause");
            yield return Wait(1.4f);
            _page = Page.ConfirmQuit; Step("confirm quit");
            yield return Wait(1.4f);
            ConfirmNo(); Step("cancel");
            yield return Wait(1.2f);
            ShowTab(Page.Graphics); Step("graphics again");
            yield return Wait(1.2f);
            Hover(_view?.TabAudio, true); Step("hover tab frame");
            yield return Wait(1.6f);
            Hover(_view?.TabAudio, false);
            Hover(RowOf(_view?.Shadows), true); Step("hover row · hint");
            yield return Wait(1.6f);
            Hover(RowOf(_view?.Shadows), false);
            Hover(_view?.Continue, true); Step("hover coral · shine");
            yield return Wait(1.2f);
            Hover(_view?.Continue, false);
            if (_view != null && _view.Resolution != null) { _view.Resolution.Open(); Step("resolution list"); }
            yield return Wait(1.6f);
            if (_view != null && _view.Resolution != null) _view.Resolution.Close();
            yield return Wait(0.6f);
            Close(); Step("closed");
        }

        private IEnumerator OpenForCapture(Page page)
        {
            yield return null;
            Open(page);
        }

        /// <summary>
        /// Открыть настройки из главного меню.
        ///
        /// Отдельный вход, потому что меню — не пауза: игра ещё не началась, и
        /// Escape там ничего не приостанавливает. Страница графики выбрана
        /// первой намеренно: до запуска игрок настраивает экран, а не звук.
        /// </summary>
        public void OpenSettings() => Open(Page.Graphics);

        private void Update()
        {
            if (CaptureRig.ForestBudShowcase) return;
            // Под дымной завесой перехода (в лагерь, в забег, на новую арену) пауза не открывается:
            // смена идёт в неигровом времени и увела бы открытую паузу в другой мир.
            if (!_open && CampTransition.Covering) return;
            if (CampPlayerView.Instance?.EntranceOpen == true || CampRiftEntrance.ClosedFrame == Time.frameCount) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DeveloperMenu.BlocksPause) return;
#endif
            // Escape в главном меню не открывает паузу: паузить нечего, а
            // закрытие этой паузы сняло бы паузу самого меню.
            if (MainMenuView.IsOpen && !_open) return;
            if ((CampPlayerView.Instance != null && CampPlayerView.Instance.InventoryOpen) || CampInventoryView.ClosedFrame == Time.frameCount || CampServicesView.Instance?.IsOpen == true || CampServicesView.ConsumedFrame==Time.frameCount || CampPreparationView.Instance?.IsOpen==true || CampForgeView.Instance?.IsOpen==true || CampPreparationView.ClosedFrame==Time.frameCount || CampForgeView.ClosedFrame==Time.frameCount) return;
            // Escape в вопросе «Заменить артефакт?» — «Оставить», а не пауза.
            if (!_open && (RunHud.ReplaceOpen || RunHud.ReplaceClosedFrame == Time.frameCount)) return;
            if (_displayPreviewActive && Time.unscaledTime >= _displayConfirmationDeadline)
                CancelDisplayPreview("Изменения экрана отменены: время подтверждения истекло.");

            // Окно «нажмите клавишу» забирает клавиатуру себе, Escape в нём — отмена.
            if (_open && _waitingBinding >= 0)
            {
                CaptureBinding();
                return;
            }

            if (_open && _view != null && !_viewOpening) SettingsKeys();

            if (!EscapePressed()) return;
            if (!_open) Open(Page.Main);
            else if (_displayPreviewActive)
                CancelDisplayPreview("Изменения экрана отменены.");
            // Открытый список (разрешение, язык) Esc сначала закрывает — окно остаётся.
            else if (CloseOpenList()) { }
            else if (_page == Page.ConfirmReset) ConfirmNo();
            // Escape из окна настроек или управления возвращает к паузе
            // (окно уезжает), из подтверждения — туда, откуда пришли.
            else if (_page != Page.Main && !MainMenuView.IsOpen)
            {
                SaveAudioIfNeeded();
                _page = Page.Main;
            }
            // Из главного меню открыты только настройки, и Escape закрывает их
            // сразу. Промежуточная страница паузы предлагала бы «продолжить» и
            // «вернуться в лагерь» игре, которая ещё не началась.
            else Close();
        }

        /// <summary>
        /// Клавиши окна настроек, как подписано в футере: Tab / Shift+Tab — вкладки, F — «Сбросить»
        /// (с подтверждением), Enter — «Применить» экран. Пока ждём клавишу или открыто
        /// подтверждение, окно клавиш не берёт.
        /// </summary>
        private void SettingsKeys()
        {
            if (_waitingBinding >= 0 || IsConfirmPage(_page) || !IsSettingsTab(_page)) return;
            bool shift;
            bool tab = KeyDown(out shift, KeyCode.Tab);
            if (tab && ColumnLayout)
            {
                ShowTab(NextTab(_settingsTab, shift ? -1 : 1));
                return;
            }
            if (KeyDown(out _, KeyCode.F) && ColumnLayout && _view.Reset != null && _view.Reset.gameObject.activeInHierarchy)
            {
                RequestReset();
                return;
            }
            if ((KeyDown(out _, KeyCode.Return) || KeyDown(out _, KeyCode.KeypadEnter)) && _settingsTab == Page.Graphics && DisplayChanged())
                ApplyDisplay();
        }

        private Page NextTab(Page from, int step)
        {
            Page[] order = { Page.Graphics, Page.Audio, Page.Game, Page.Interface, Page.Controls };
            int index = Mathf.Max(0, Array.IndexOf(order, from));
            return order[(index + step + order.Length) % order.Length];
        }

        private bool CloseOpenList()
        {
            if (_view == null) return false;
            foreach (UiDropdown list in new[] { _view.Resolution, _view.Language })
            {
                if (list == null || !list.IsOpen) continue;
                list.Close();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Открытый список (разрешение, язык) прячется сразу и молча перед вопросом «Сбросить?» и проверкой
        /// экрана: список — свой Canvas поверх окна (sortingOrder 320) и иначе висел бы над подтверждением.
        /// </summary>
        private void HideOpenLists()
        {
            if (_view == null) return;
            foreach (UiDropdown list in new[] { _view.Resolution, _view.Language })
                if (list != null && list.List != null && list.List.gameObject.activeSelf) list.List.gameObject.SetActive(false);
        }

        private static bool KeyDown(out bool shift, KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            shift = keyboard != null && keyboard.shiftKey.isPressed;
            if (keyboard == null) return false;
            switch (key)
            {
                case KeyCode.Tab: return keyboard.tabKey.wasPressedThisFrame;
                case KeyCode.F: return keyboard.fKey.wasPressedThisFrame;
                case KeyCode.Return: return keyboard.enterKey.wasPressedThisFrame;
                case KeyCode.KeypadEnter: return keyboard.numpadEnterKey.wasPressedThisFrame;
                default: return false;
            }
#else
            shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return Input.GetKeyDown(key);
#endif
        }

        /// <summary>
        /// «Пауза при сворачивании»: окно игры потеряло фокус — пауза, как по Esc. Там, где Esc паузу
        /// не открывает (главное меню, дым перехода, окна лагеря, вопрос артефакта), не открывает и фокус.
        /// </summary>
        private void OnApplicationFocus(bool focus)
        {
            if (focus || _open || !GameUserSettings.PauseOnFocusLoss || _driver == null) return;
            if (CaptureRig.ForestBudShowcase || CampTransition.Covering || MainMenuView.IsOpen) return;
            if (CampPlayerView.Instance?.EntranceOpen == true) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DeveloperMenu.BlocksPause) return;
#endif
            if ((CampPlayerView.Instance != null && CampPlayerView.Instance.InventoryOpen) || CampServicesView.Instance?.IsOpen == true || CampPreparationView.Instance?.IsOpen==true || CampForgeView.Instance?.IsOpen==true) return;
            if (RunHud.ReplaceOpen) return;
            Open(Page.Main);
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
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
            if (_view != null)
            {
                if (IsSettingsTab(page)) _settingsTab = page;
                _focused = null;
                // Сначала снимок кадра для размытого фона (меню ещё не видно), потом появление.
                _viewOpening = true;
                StartCoroutine(PresentAfterSnapshot());
            }
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
            _waitingBinding = -1;
            if (_view != null) _view.PlayClose();

            // Под настройками может лежать не игра, а главное меню — тогда
            // закрывать надо в него, а не в бой. Иначе выход из настроек
            // запускал бы забег за спиной у игрока.
            bool toMainMenu = MainMenuView.IsOpen;
            Time.timeScale = toMainMenu ? 0f
                : _previousTimeScale > 0f ? _previousTimeScale : 1f;
            if (!toMainMenu)
            {
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }
            _driver.SetGameplayPaused(toMainMenu);
        }

        private void OnDisable()
        {
            if (_open) Close();
        }

        private void OnDestroy()
        {
            UiSettingRow.Clicked -= OnRowClicked;
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

        // ---- Canvas-меню ----
        private int _waitingBinding = -1;
        private UiKeyBindingRow[] _bindingRows = Array.Empty<UiKeyBindingRow>();
        private string[] _resolutionLabels = Array.Empty<string>();
        private string _bindingStatus = string.Empty;
        private bool _frameRowDimmed;
        private bool _frameRowKnown;

        private bool _viewOpening;
        // Опция, с которой игрок работал последней (щелчок по строке, смена значения): её описание
        // держится справа, пока мышь не над другой строкой. null — первая опция вкладки.
        private SettingId? _focused;
        private string _resetStatus = string.Empty;
        private string _languageStatus = string.Empty;
        private float _resetCheckAt;
        private bool _tabIsDefault;
        private bool _resetKnown;
        private bool _resetDimmed;
        private bool _abilityRowKnown;
        private bool _abilityRowDimmed;
        private UiSettingRow[] _rows = Array.Empty<UiSettingRow>();

        private static readonly string[] LanguageLabels = Array.ConvertAll(UserPreferences.Languages, language => language.Label);
        private static readonly string[] EnemyBarLabels =
        {
            SettingsCatalog.EnemyBarsName(EnemyBarMode.All), SettingsCatalog.EnemyBarsName(EnemyBarMode.EliteOnly),
            SettingsCatalog.EnemyBarsName(EnemyBarMode.None),
        };
        private static readonly string[] FlashLabels = { SettingsCatalog.FlashesName(false), SettingsCatalog.FlashesName(true) };

        // Образец яркости: уровни sRGB кругов слева направо; левый при верной яркости едва различим.
        private static readonly float[] SwatchLevels = { .035f, .06f, .1f, .15f, .23f };

        private void LateUpdate()
        {
            if (_open && _view != null && !_viewOpening) SyncView();
        }

        /// <summary>
        /// Снимок игры для размытого фона берётся в конце кадра, пока меню не
        /// показано (боевой HUD в паузе уже скрыт), и только затем меню появляется.
        /// </summary>
        private IEnumerator PresentAfterSnapshot()
        {
            if (_view.BackdropBlur != null) yield return _view.BackdropBlur.Capture();
            _viewOpening = false;
            if (!_open) yield break;
            bool fromMainMenu = MainMenuView.IsOpen;
            _view.PlayOpen(WindowFor(_page, fromMainMenu), TabFor(_settingsTab), fromMainMenu);
            SyncView();
        }

        /// <summary>Выбранные режим и разрешение отличаются от того, что сейчас на экране.</summary>
        private bool DisplayChanged()
        {
            if (_resolutions.Length == 0) return false;
            Vector2Int resolution = _resolutions[_resolutionIndex];
            GameUserSettings.DisplayConfiguration target = GameUserSettings.ResolveDisplay(resolution.x, resolution.y, _displayMode);
            return target.Mode != Screen.fullScreenMode || target.Width != Screen.width || target.Height != Screen.height;
        }

        // Съёмка: наведение мыши без мыши — через те же обработчики, что у EventSystem.
        private static void Hover(Component target, bool enter)
        {
            if (target == null || EventSystem.current == null) return;
            var data = new PointerEventData(EventSystem.current);
            if (enter) ExecuteEvents.ExecuteHierarchy(target.gameObject, data, ExecuteEvents.pointerEnterHandler);
            else ExecuteEvents.ExecuteHierarchy(target.gameObject, data, ExecuteEvents.pointerExitHandler);
        }

        private static Component RowOf(Component control)
            => control != null && control.transform.parent != null ? control.transform.parent : null;

        /// <summary>Какое окно рядом с паузой видно на странице.</summary>
        private PauseMenuView.Window WindowFor(Page page, bool fromMainMenu)
        {
            if (page == Page.Controls && !ColumnLayout) return PauseMenuView.Window.Controls;
            if (IsSettingsTab(page) || page == Page.ConfirmDisplay || page == Page.ConfirmReset || fromMainMenu) return PauseMenuView.Window.Settings;
            return PauseMenuView.Window.None;
        }

        private static PauseMenuView.Tab TabFor(Page tab)
        {
            switch (tab)
            {
                case Page.Audio: return PauseMenuView.Tab.Audio;
                case Page.Game: return PauseMenuView.Tab.Game;
                case Page.Interface: return PauseMenuView.Tab.Interface;
                case Page.Controls: return PauseMenuView.Tab.Controls;
                default: return PauseMenuView.Tab.Graphics;
            }
        }

        private static SettingsTab CatalogTab(Page tab)
        {
            switch (tab)
            {
                case Page.Audio: return SettingsTab.Audio;
                case Page.Game: return SettingsTab.Game;
                case Page.Interface: return SettingsTab.Interface;
                case Page.Controls: return SettingsTab.Controls;
                default: return SettingsTab.Display;
            }
        }

        /// <summary>Кнопки, слайдеры и переключатели префаба ведут в ту же логику, что и IMGUI.</summary>
        private void BindView()
        {
            PauseMenuView v = _view;
            v.Continue?.onClick.AddListener(Close);
            // Повторное нажатие на пункт с открытым окном закрывает окно — пауза возвращается в центр.
            v.Settings?.onClick.AddListener(() => { if (IsSettingsTab(_page)) BackToPause(); else ShowTab(_settingsTab); });
            v.Controls?.onClick.AddListener(() => { if (_page == Page.Controls) BackToPause(); else ShowTab(Page.Controls); });
            v.Camp?.onClick.AddListener(RequestReturnToCamp);
            v.Quit?.onClick.AddListener(() => { SaveAudioIfNeeded(); _page = Page.ConfirmQuit; });
            v.TabGraphics?.onClick.AddListener(() => ShowTab(Page.Graphics));
            v.TabAudio?.onClick.AddListener(() => ShowTab(Page.Audio));
            v.TabGame?.onClick.AddListener(() => ShowTab(Page.Game));
            v.TabInterface?.onClick.AddListener(() => ShowTab(Page.Interface));
            v.TabControls?.onClick.AddListener(() => ShowTab(Page.Controls));
            v.SettingsBack?.onClick.AddListener(() => { if (MainMenuView.IsOpen) Close(); else BackToPause(); });
            v.ControlsBack?.onClick.AddListener(BackToPause);
            // Окно столбцом сбрасывает через подтверждение: «Сбросить» трогает всю вкладку.
            v.Reset?.onClick.AddListener(() => { if (ColumnLayout) RequestReset(); else ResetTab(); });
            v.ControlsReset?.onClick.AddListener(() =>
            {
                UiSound.Play(UiSoundEvent.Reset);
                _waitingBinding = -1;
                GameUserSettings.ResetControls();
                _bindingStatus = "Клавиши возвращены к стандартным.";
            });

            if (v.DisplayMode != null) v.DisplayMode.Chosen += index => { _focused = SettingId.DisplayMode; _displayMode = DisplayModes[Mathf.Clamp(index, 0, DisplayModes.Length - 1)]; };
            if (v.Resolution != null) v.Resolution.Chosen += index => { _focused = SettingId.Resolution; _resolutionIndex = Mathf.Clamp(index, 0, _resolutions.Length - 1); };
            if (v.Quality != null) v.Quality.Chosen += index => { _focused = SettingId.Quality; GameUserSettings.SetQuality(QualityLevels[Mathf.Clamp(index, 0, QualityLevels.Length - 1)]); };
            if (v.VSync != null) v.VSync.Changed += on => { _focused = SettingId.VSync; GameUserSettings.SetVSync(on); };
            if (v.FrameLimit != null)
            {
                v.FrameLimit.minValue = 0f;
                v.FrameLimit.maxValue = GameUserSettings.FrameLimits.Length - 1;
                v.FrameLimit.wholeNumbers = true;
                v.FrameLimit.onValueChanged.AddListener(value =>
                {
                    UiSound.Play(UiSoundEvent.SliderStep);
                    GameUserSettings.SetFrameLimit(GameUserSettings.FrameLimits[Mathf.Clamp(Mathf.RoundToInt(value), 0, GameUserSettings.FrameLimits.Length - 1)]);
                });
            }
            if (v.Shadows != null) v.Shadows.Chosen += index => { _focused = SettingId.Shadows; GameUserSettings.SetShadows((GameUserSettings.ShadowLevel)Mathf.Clamp(index, 0, 2)); };
            if (v.UiScale != null)
            {
                // Шаг 5%: 16..24 — это 80..120%.
                v.UiScale.minValue = GameUserSettings.UiScaleMin * 20f;
                v.UiScale.maxValue = GameUserSettings.UiScaleMax * 20f;
                v.UiScale.wholeNumbers = true;
                v.UiScale.onValueChanged.AddListener(value =>
                {
                    UiSound.Play(UiSoundEvent.SliderStep);
                    GameUserSettings.SetUiScale(value / 20f);
                });
            }
            v.Apply?.onClick.AddListener(ApplyDisplay);

            v.Master?.onValueChanged.AddListener(value => { _master = value; ChangeAudio(); });
            v.Effects?.onValueChanged.AddListener(value => { _effects = value; ChangeAudio(); });
            v.Music?.onValueChanged.AddListener(value => { _music = value; ChangeAudio(); });

            if (v.AbilityLayout != null)
                v.AbilityLayout.Chosen += index => { _focused = SettingId.Movement; GameUserSettings.SetWasdMovement(index == 1); };
            BindColumnSettings(v);
            _bindingRows = v.BuildBindingRows(GameKeyBindings.Count, row =>
            {
                UiSound.Play(UiSoundEvent.KeyWaiting);
                _waitingBinding = (int)row.Binding;
                _bindingStatus = string.Empty;
            });

            v.ConfirmYes?.onClick.AddListener(ConfirmYes);
            v.ConfirmNo?.onClick.AddListener(ConfirmNo);
        }

        /// <summary>
        /// Опции окна «Настройки Б» (30.09). Каждая применяется и сохраняется сразу (GameUserSettings),
        /// кроме режима и разрешения экрана — они по-прежнему через «Применить» с проверкой.
        /// Звуков здесь не добавлено: звук интерфейса — отдельным проходом (тумблер звучит сам, UiToggle).
        /// </summary>
        private void BindColumnSettings(PauseMenuView v)
        {
            _rows = v.GetComponentsInChildren<UiSettingRow>(true);
            UiSettingRow.Clicked -= OnRowClicked;
            UiSettingRow.Clicked += OnRowClicked;

            StepSlider(v.Brightness, value => { _focused = SettingId.Brightness; GameUserSettings.SetBrightness(value); });
            StepSlider(v.ScreenShake, value => { _focused = SettingId.ScreenShake; GameUserSettings.SetScreenShake(value); });
            if (v.InterfaceVolume != null)
            {
                v.InterfaceVolume.minValue = 0f;
                v.InterfaceVolume.maxValue = 1f;
                v.InterfaceVolume.wholeNumbers = false;
                // Пишется сразу, на диск — вместе с остальной громкостью при уходе со вкладки (SaveAudioIfNeeded).
                v.InterfaceVolume.onValueChanged.AddListener(value => { _focused = SettingId.InterfaceVolume; _audioDirty = true; GameUserSettings.SetInterfaceVolume(value); });
            }
            if (v.SoundInBackground != null) v.SoundInBackground.Changed += on => { _focused = SettingId.SoundInBackground; GameUserSettings.SetSoundInBackground(on); };
            if (v.PauseOnFocusLoss != null) v.PauseOnFocusLoss.Changed += on => { _focused = SettingId.PauseOnFocusLoss; GameUserSettings.SetPauseOnFocusLoss(on); };
            if (v.DamageNumbers != null) v.DamageNumbers.Changed += on => { _focused = SettingId.DamageNumbers; GameUserSettings.SetDamageNumbers(on); };
            if (v.EnemyBars != null) v.EnemyBars.Chosen += index => { _focused = SettingId.EnemyBars; GameUserSettings.SetEnemyBars((EnemyBarMode)Mathf.Clamp(index, 0, 2)); };
            if (v.Flashes != null) v.Flashes.Chosen += index => { _focused = SettingId.Flashes; GameUserSettings.SetReduceFlashes(index == 1); };
            if (v.AbilityRow != null)
                v.AbilityRow.Chosen += index =>
                {
                    _focused = SettingId.AbilityRow;
                    if (GameUserSettings.WasdMovement) return;
                    GameUserSettings.SetAbilityLayout(index == 1 ? GameUserSettings.AbilityLayout.Digits : GameUserSettings.AbilityLayout.Qwer);
                };
            if (v.Language != null)
                v.Language.Chosen += index =>
                {
                    _focused = SettingId.Language;
                    LanguageOption option = UserPreferences.Languages[Mathf.Clamp(index, 0, UserPreferences.Languages.Length - 1)];
                    // Неготовый перевод не выбирается: остаётся русский, и сказано почему.
                    _languageStatus = GameUserSettings.SetLanguage(option.Code) || option.Ready ? string.Empty
                        : option.Name + " — перевод ещё готовится. Остаётся " + UserPreferences.Languages[0].Name.ToLowerInvariant() + ".";
                };
            if (v.UiScale != null) v.UiScale.onValueChanged.AddListener(_ => _focused = SettingId.UiScale);
            if (v.FrameLimit != null) v.FrameLimit.onValueChanged.AddListener(_ => _focused = SettingId.FrameLimit);
            v.Master?.onValueChanged.AddListener(_ => _focused = SettingId.MasterVolume);
            v.Effects?.onValueChanged.AddListener(_ => _focused = SettingId.EffectsVolume);
            v.Music?.onValueChanged.AddListener(_ => _focused = SettingId.MusicVolume);
        }

        /// <summary>Ползунок с шагом 5%: 0–20 целыми, значение — доля 0–1.</summary>
        private static void StepSlider(UnityEngine.UI.Slider slider, Action<float> changed)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 20f;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(value => changed(value / 20f));
        }

        private void OnRowClicked(UiSettingRow row)
        {
            if (row != null && _open) _focused = row.Setting;
        }

        /// <summary>
        /// Общее окна столбцом: описание справа (наведённая строка, иначе последняя тронутая, иначе
        /// первая во вкладке), огонёк у описываемой строки, строка состояния, заметка футера и
        /// «Сбросить», притухшая, когда сбрасывать нечего.
        /// </summary>
        private void SyncColumnWindow(PauseMenuView v)
        {
            SettingsTab tab = CatalogTab(_settingsTab);
            SettingId[] ids = SettingsCatalog.Ids(tab);
            UiSettingRow hovered = UiSettingRow.Hovered;
            SettingId described = hovered != null && SettingsCatalog.TabOf(hovered.Setting) == tab ? hovered.Setting
                : _focused.HasValue && SettingsCatalog.TabOf(_focused.Value) == tab ? _focused.Value
                : ids[0];
            v.ShowDescription(SettingsCatalog.Title(described), SettingsCatalog.Description(described),
                SettingsCatalog.DefaultLine(described), described == SettingId.Brightness);
            if (described == SettingId.Brightness)
            {
                float brightness = GameUserSettings.Preferences.Brightness;
                v.SetBrightnessSwatches(i =>
                {
                    float level = UserPreferences.ApplyToSrgb(SwatchLevels[Mathf.Clamp(i, 0, SwatchLevels.Length - 1)], brightness);
                    return new Color(level, level, level, 1f);
                });
            }
            foreach (UiSettingRow row in _rows)
                if (row != null) row.SetSelected(row.Setting == described && row.isActiveAndEnabled);

            PauseMenuView.SetText(v.DescriptionStatus, ColumnStatus());
            PauseMenuView.SetText(v.FooterNote, UiHint.Current ?? (_settingsTab == Page.Graphics
                ? "Экран — кнопкой «Применить», остальное сохраняется сразу"
                : "Изменения сохраняются сразу"));

            // Стандартные ли значения вкладки — раз в пятую долю секунды: проверка читает сохранённые клавиши.
            if (Time.unscaledTime >= _resetCheckAt)
            {
                _resetCheckAt = Time.unscaledTime + .2f;
                _tabIsDefault = TabIsDefault(_settingsTab);
            }
            if (v.Reset != null && (!_resetKnown || _resetDimmed != _tabIsDefault))
            {
                _resetKnown = true;
                _resetDimmed = _tabIsDefault;
                CanvasGroup group = v.Reset.GetComponent<CanvasGroup>();
                if (group == null) group = v.Reset.gameObject.AddComponent<CanvasGroup>();
                UiMotion.FadeTo(group, _tabIsDefault ? .5f : 1f, .15f);
            }
        }

        /// <summary>Строка состояния под описанием: что ждёт действия игрока или чем кончилось последнее.</summary>
        private string ColumnStatus()
        {
            if (_settingsTab == Page.Graphics)
            {
                if (DisplayChanged()) return "Режим или разрешение изменены — нажми «Применить» (Enter).";
                if (!string.IsNullOrEmpty(_displayStatus)) return _displayStatus;
            }
            // У «Управления» ожидание клавиши и итог обмена пишет пояснение над колонками — у глаз, не дублируем.
            if (_settingsTab == Page.Game && !string.IsNullOrEmpty(_languageStatus)) return _languageStatus;
            return _resetStatus;
        }

        private void ShowTab(Page tab)
        {
            if (_page == Page.Audio && tab != Page.Audio) SaveAudioIfNeeded();
            if (IsSettingsTab(tab))
            {
                if (tab != _settingsTab)
                {
                    _focused = null;
                    _resetStatus = string.Empty;
                    _languageStatus = string.Empty;
                    _resetCheckAt = 0f;
                    // Список прежней вкладки не висит над новой, пока та проявляется.
                    HideOpenLists();
                }
                _settingsTab = tab;
            }
            _waitingBinding = -1;
            _page = tab;
        }

        private void ApplyDisplay()
        {
            if (!DisplayChanged()) { UiSound.Play(UiSoundEvent.Denied); return; }
            UiSound.Play(UiSoundEvent.Apply);
            HideOpenLists();
            Vector2Int resolution = _resolutions[_resolutionIndex];
            BeginDisplayPreview(resolution.x, resolution.y, _displayMode);
        }

        /// <summary>«Сбросить» (кнопка или F): сначала вопрос — сброс трогает всю вкладку. Сбрасывать нечего — только подсказка.</summary>
        private void RequestReset()
        {
            if (TabIsDefault(_settingsTab))
            {
                _resetStatus = "Здесь уже всё стандартное.";
                return;
            }
            _waitingBinding = -1;
            HideOpenLists();
            _page = Page.ConfirmReset;
        }

        private bool TabIsDefault(Page tab)
        {
            switch (tab)
            {
                case Page.Audio: return GameUserSettings.AudioIsDefault && Mathf.Approximately(_master, 1f) && Mathf.Approximately(_effects, 1f) && Mathf.Approximately(_music, .75f);
                case Page.Game: return GameUserSettings.GameIsDefault;
                case Page.Interface: return GameUserSettings.InterfaceIsDefault;
                case Page.Controls: return GameUserSettings.ControlsIsDefault;
                default: return GameUserSettings.GraphicsIsDefault;
            }
        }

        private void BackToPause()
        {
            SaveAudioIfNeeded();
            _waitingBinding = -1;
            _page = Page.Main;
        }

        private void ResetTab()
        {
            UiSound.Play(UiSoundEvent.Reset);
            if (_settingsTab == Page.Graphics)
            {
                GameUserSettings.ResetGraphics();
                // В старом окне на вкладке графики был и масштаб интерфейса — там он сбрасывается вместе с ней.
                if (!ColumnLayout) GameUserSettings.SetUiScale(1f);
                _displayStatus = "Изображение возвращено к стандартному. Экран не менялся.";
            }
            else if (_settingsTab == Page.Audio)
            {
                GameUserSettings.ResetAudio();
                _master = GameUserSettings.MasterVolume;
                _effects = GameUserSettings.EffectsVolume;
                _music = GameUserSettings.MusicVolume;
                _audioDirty = false;
            }
            else if (_settingsTab == Page.Game) GameUserSettings.ResetGame();
            else if (_settingsTab == Page.Interface) GameUserSettings.ResetInterface();
            else if (_settingsTab == Page.Controls)
            {
                _waitingBinding = -1;
                GameUserSettings.ResetControls();
                _bindingStatus = "Клавиши и схема управления возвращены к стандартным.";
            }
            _languageStatus = string.Empty;
            _resetStatus = "«" + SettingsCatalog.TabTitle(CatalogTab(_settingsTab)) + "» — стандартные значения.";
            _resetCheckAt = 0f;
        }

        /// <summary>Ждём клавишу для строки назначения: любая поддерживаемая назначается, Escape отменяет.</summary>
        private void CaptureBinding()
        {
            KeyCode key = GameKeyBindings.PressedThisFrame();
            if (key == KeyCode.None) return;
            var action = (GameAction)_waitingBinding;
            _waitingBinding = -1;
            if (key == KeyCode.Escape) { UiSound.Play(UiSoundEvent.Back); return; }
            if (GameKeyBindings.Rebind(action, key, out int swapped))
            {
                UiSound.Play(UiSoundEvent.KeyAssigned);
                _bindingStatus = GameKeyBindings.ActionName(action) + " — " + GameKeyBindings.Label(key);
                if (swapped >= 0 && swapped < _bindingRows.Length)
                {
                    // Строка, отдавшая клавишу, вспыхивает: обмен виден, а не молча случился.
                    PauseMenuView.Flash(_bindingRows[swapped]);
                    _bindingStatus += " · обмен с «" + GameKeyBindings.ActionName((GameAction)swapped) + "»";
                }
            }
            else
            {
                UiSound.Play(UiSoundEvent.Denied);
                _bindingStatus = "Эту клавишу назначить нельзя.";
            }
        }

        private void ChangeAudio()
        {
            _audioDirty = true;
            GameUserSettings.SetAudio(_master, _effects, _music);
        }

        private void RequestReturnToCamp()
        {
            if (!_driver.CanReturnToCamp) return;
            if (_driver.Session != null && _driver.Session.Mode == GameMode.Rift)
                _page = Page.ConfirmCamp;
            else
            {
                _driver.ReturnToCampFromMenu();
                Close();
            }
        }

        private void ConfirmYes()
        {
            if (_page == Page.ConfirmCamp)
            {
                _driver.ReturnToCampFromMenu();
                Close();
            }
            else if (_page == Page.ConfirmDisplay) ConfirmDisplayPreview();
            else if (_page == Page.ConfirmReset)
            {
                ResetTab();
                _page = _settingsTab;
            }
            else if (_page == Page.ConfirmQuit)
            {
                SaveAudioIfNeeded();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        private void ConfirmNo()
        {
            if (_page == Page.ConfirmDisplay) CancelDisplayPreview("Предыдущие настройки экрана восстановлены.");
            else if (_page == Page.ConfirmReset) _page = _settingsTab;
            else _page = Page.Main;
        }

        /// <summary>Состояние меню → префаб. Анимирует вид только изменившееся; тексты ставятся, если изменились.</summary>
        private void SyncView()
        {
            PauseMenuView v = _view;
            bool fromMainMenu = MainMenuView.IsOpen;
            bool confirming = IsConfirmPage(_page);
            if (IsSettingsTab(_page)) _settingsTab = _page;
            PauseMenuView.Window window = WindowFor(_page, fromMainMenu);

            v.Present(window, TabFor(_settingsTab), confirming, fromMainMenu);
            PauseMenuView.Show(v.Camp, _driver.CanReturnToCamp);

            if (window == PauseMenuView.Window.Settings)
            {
                if (_settingsTab == Page.Graphics) SyncGraphics(v);
                else if (_settingsTab == Page.Audio) SyncAudio(v);
                else if (_settingsTab == Page.Interface) SyncInterface(v);
                else if (_settingsTab == Page.Controls) SyncControls(v);
                else if (ColumnLayout) SyncGame(v);
                else PauseMenuView.SetText(v.GameText, "В разработке");
                if (ColumnLayout) SyncColumnWindow(v);
            }
            else if (window == PauseMenuView.Window.Controls) SyncControls(v);
            if (confirming) SyncConfirmation(v);
        }

        private static readonly string[] DisplayModeLabels = { "Весь экран", "Окно", "Без рамок" };

        private void SyncGraphics(PauseMenuView v)
        {
            if (v.DisplayMode != null)
            {
                v.DisplayMode.SetLabels(DisplayModeLabels);
                v.DisplayMode.SetSelected(Mathf.Max(0, Array.IndexOf(DisplayModes, _displayMode)));
            }
            if (v.Resolution != null)
            {
                bool monitor = _displayMode == FullScreenMode.FullScreenWindow;
                Vector2Int resolution = _resolutions[_resolutionIndex];
                if (monitor)
                {
                    GameUserSettings.DisplayConfiguration native = GameUserSettings.ResolveDisplay(resolution.x, resolution.y, _displayMode);
                    resolution = new Vector2Int(native.Width, native.Height);
                }
                v.Resolution.SetOptions(_resolutionLabels);
                v.Resolution.SetSelected(_resolutionIndex, monitor ? ResolutionName(resolution) + " · монитор" : ResolutionName(resolution));
                v.Resolution.Interactable = !monitor;
            }
            if (v.Quality != null)
            {
                v.Quality.SetLabels(new[]
                {
                    GameUserSettings.QualityName(QualityLevels[0]), GameUserSettings.QualityName(QualityLevels[1]), GameUserSettings.QualityName(QualityLevels[2]),
                });
                v.Quality.SetSelected(Mathf.Max(0, Array.IndexOf(QualityLevels, GameUserSettings.Quality)));
            }

            v.VSync?.SetValue(GameUserSettings.VSync);
            int limit = Mathf.Max(0, Array.IndexOf(GameUserSettings.FrameLimits, GameUserSettings.FrameLimit));
            if (v.FrameLimit != null)
            {
                if (Mathf.RoundToInt(v.FrameLimit.value) != limit) v.FrameLimit.SetValueWithoutNotify(limit);
                v.FrameLimit.interactable = !GameUserSettings.VSync;
            }
            int limitValue = GameUserSettings.FrameLimits[limit];
            PauseMenuView.SetText(v.FrameLimitValue, limitValue < 0 ? "Без предела" : limitValue.ToString());
            // Пока включена синхронизация, предел кадров не действует — строка притухает.
            if (v.FrameLimitRow != null && (!_frameRowKnown || _frameRowDimmed != GameUserSettings.VSync))
            {
                _frameRowKnown = true;
                _frameRowDimmed = GameUserSettings.VSync;
                UiMotion.FadeTo(v.FrameLimitRow, _frameRowDimmed ? 0.45f : 1f, 0.18f);
            }

            if (v.Shadows != null)
            {
                v.Shadows.SetLabels(new[]
                {
                    GameUserSettings.ShadowName(GameUserSettings.ShadowLevel.Low), GameUserSettings.ShadowName(GameUserSettings.ShadowLevel.Medium),
                    GameUserSettings.ShadowName(GameUserSettings.ShadowLevel.High),
                });
                v.Shadows.SetSelected((int)GameUserSettings.Shadows);
            }
            SyncUiScale(v);
            SyncStepSlider(v.Brightness, v.BrightnessValue, GameUserSettings.Preferences.Brightness);

            // «Применить» — только когда режим или разрешение действительно отличаются от экрана.
            bool pending = DisplayChanged();
            if (v.Apply != null)
            {
                v.Apply.interactable = pending;
                CanvasGroup applyGroup = v.Apply.GetComponent<CanvasGroup>();
                if (applyGroup == null) applyGroup = v.Apply.gameObject.AddComponent<CanvasGroup>();
                float target = pending ? 1f : 0.5f;
                if (!Mathf.Approximately(applyGroup.alpha, target)) UiMotion.FadeTo(applyGroup, target, 0.15f);
            }
            PauseMenuView.SetText(v.Status, UiHint.Current
                ?? (pending ? "Есть изменения экрана — нажмите «Применить»." : _displayStatus));
        }

        private static void SyncUiScale(PauseMenuView v)
        {
            int scale = Mathf.RoundToInt(GameUserSettings.UiScale * 20f);
            if (v.UiScale != null && Mathf.RoundToInt(v.UiScale.value) != scale) v.UiScale.SetValueWithoutNotify(scale);
            PauseMenuView.SetText(v.UiScaleValue, Mathf.RoundToInt(GameUserSettings.UiScale * 100f) + "%");
        }

        /// <summary>Ползунок с шагом 5% (0–20): яркость, тряска.</summary>
        private static void SyncStepSlider(UnityEngine.UI.Slider slider, TMPro.TMP_Text label, float value)
        {
            int steps = Mathf.RoundToInt(value * 20f);
            if (slider != null && Mathf.RoundToInt(slider.value) != steps) slider.SetValueWithoutNotify(steps);
            PauseMenuView.SetText(label, UserPreferences.Percent(value));
        }

        private void SyncAudio(PauseMenuView v)
        {
            SyncSlider(v.Master, v.MasterValue, _master);
            SyncSlider(v.Effects, v.EffectsValue, _effects);
            SyncSlider(v.Music, v.MusicValue, _music);
            SyncSlider(v.InterfaceVolume, v.InterfaceVolumeValue, GameUserSettings.Preferences.InterfaceVolume);
            v.SoundInBackground?.SetValue(GameUserSettings.Preferences.SoundInBackground);
            PauseMenuView.SetText(v.Status, UiHint.Current ?? string.Empty);
        }

        private void SyncGame(PauseMenuView v)
        {
            UserPreferences prefs = GameUserSettings.Preferences;
            if (v.Language != null)
            {
                v.Language.SetOptions(LanguageLabels);
                int index = UserPreferences.LanguageIndex(prefs.Language);
                v.Language.SetSelected(index, UserPreferences.Languages[index].Name);
            }
            v.PauseOnFocusLoss?.SetValue(prefs.PauseOnFocusLoss);
            v.DamageNumbers?.SetValue(prefs.DamageNumbers);
            if (v.EnemyBars != null)
            {
                v.EnemyBars.SetLabels(EnemyBarLabels);
                v.EnemyBars.SetSelected((int)prefs.EnemyBars);
            }
        }

        private void SyncInterface(PauseMenuView v)
        {
            UserPreferences prefs = GameUserSettings.Preferences;
            SyncUiScale(v);
            SyncStepSlider(v.ScreenShake, v.ScreenShakeValue, prefs.ScreenShake);
            if (v.Flashes != null)
            {
                v.Flashes.SetLabels(FlashLabels);
                v.Flashes.SetSelected(prefs.ReduceFlashes ? 1 : 0);
            }
        }

        private static void SyncSlider(UnityEngine.UI.Slider slider, TMPro.TMP_Text label, float value)
        {
            if (slider != null && !Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
            PauseMenuView.SetText(label, Mathf.RoundToInt(value * 100f) + "%");
        }

        private void SyncControls(PauseMenuView v)
        {
            if (v.AbilityLayout != null)
            {
                v.AbilityLayout.SetLabels(new[] { "Мышь", "WASD" });
                v.AbilityLayout.SetSelected(GameUserSettings.WasdMovement ? 1 : 0);
            }
            if (v.AbilityRow != null)
            {
                v.AbilityRow.SetLabels(new[] { "Q W E R", "1 2 3 4" });
                // При WASD ряд всегда цифровой — показываем то, что действует, а строку притушаем.
                bool wasd = GameUserSettings.WasdMovement;
                v.AbilityRow.SetSelected(wasd || GameUserSettings.Abilities == GameUserSettings.AbilityLayout.Digits ? 1 : 0);
                CanvasGroup row = v.AbilityRowGroup;
                if (row != null && (!_abilityRowKnown || _abilityRowDimmed != wasd))
                {
                    _abilityRowKnown = true;
                    _abilityRowDimmed = wasd;
                    row.interactable = !wasd;
                    UiMotion.FadeTo(row, wasd ? .45f : 1f, .18f);
                }
            }
            // Подсказки клавиш — один формат на все окна: «[Esc] Отмена» (лист 5, UiKeyHint).
            PauseMenuView.SetText(v.ControlsHint, _waitingBinding >= 0
                ? "Нажми клавишу для «" + GameKeyBindings.ActionName((GameAction)_waitingBinding) + "»" + UiKeyHint.Separator + UiKeyHint.EscCancel
                : UiHint.Current ?? (!string.IsNullOrEmpty(_bindingStatus) ? _bindingStatus
                : (GameUserSettings.WasdMovement ? WasdControlsHint + UiKeyHint.Separator + UiKeyHint.Hint("рывок", GameKeyBindings.Label(GameAction.Dash)) + ". Нажми на клавишу, чтобы переназначить." : MouseControlsHint)));
            for (int i = 0; i < _bindingRows.Length; i++)
            {
                var action = (GameAction)i;
                _bindingRows[i]?.Show(GameKeyBindings.ActionName(action), GameKeyBindings.Label(action),
                    _waitingBinding == i, GameKeyBindings.IsCustom(action));
            }
        }

        private void SyncConfirmation(PauseMenuView v)
        {
            if (_page == Page.ConfirmCamp)
            {
                PauseMenuView.SetText(v.ConfirmTitle, "Покинуть разлом?");
                PauseMenuView.SetText(v.ConfirmText, "Текущий забег завершится. Невзятые награды будут потеряны.");
                PauseMenuView.SetText(v.ConfirmCountdown, string.Empty);
                PauseMenuView.SetText(v.ConfirmYesLabel, "В лагерь");
                PauseMenuView.SetText(v.ConfirmNoLabel, "Отмена");
            }
            else if (_page == Page.ConfirmReset)
            {
                SettingsTab tab = CatalogTab(_settingsTab);
                PauseMenuView.SetText(v.ConfirmTitle, "Сбросить «" + SettingsCatalog.TabTitle(tab) + "»?");
                PauseMenuView.SetText(v.ConfirmText, SettingsCatalog.ResetSummary(tab));
                PauseMenuView.SetText(v.ConfirmCountdown, "Остальные вкладки не изменятся.");
                PauseMenuView.SetText(v.ConfirmYesLabel, "Сбросить");
                PauseMenuView.SetText(v.ConfirmNoLabel, "Отмена");
            }
            else if (_page == Page.ConfirmQuit)
            {
                PauseMenuView.SetText(v.ConfirmTitle, "Выйти из игры?");
                PauseMenuView.SetText(v.ConfirmText, _driver.Session != null && _driver.Session.Mode == GameMode.Rift
                    ? "Текущий забег не сохранится." : "Прогресс лагеря уже сохранён.");
                PauseMenuView.SetText(v.ConfirmCountdown, string.Empty);
                PauseMenuView.SetText(v.ConfirmYesLabel, "Выйти");
                PauseMenuView.SetText(v.ConfirmNoLabel, "Отмена");
            }
            else
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(_displayConfirmationDeadline - Time.unscaledTime));
                PauseMenuView.SetText(v.ConfirmTitle, "Сохранить экран?");
                PauseMenuView.SetText(v.ConfirmText, "Если изображение отображается правильно, подтвердите новый режим.");
                PauseMenuView.SetText(v.ConfirmCountdown, "Автоматический возврат через " + seconds + " с");
                PauseMenuView.SetText(v.ConfirmYesLabel, "Сохранить");
                PauseMenuView.SetText(v.ConfirmNoLabel, "Вернуть прежние");
            }
        }

        private void OnGUI()
        {
            if (!_open || _view != null) return;
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
                "[Esc] Продолжить игру", _subtitle);
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
            // Панель подросла вместе с содержимым: во вкладке экрана теперь
            // четыре строки вместо двух, и кнопка «Применить» со строкой
            // состояния обязана помещаться под ними, а не уезжать за край.
            var panel = new Rect(550f, 118f, 820f, 850f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 48f, panel.y + 38f, 510f, 56f), "НАСТРОЙКИ", _title);
            // «Назад» ведёт туда, откуда пришли: из паузы — на её главную
            // страницу, из главного меню — обратно в меню. Страница паузы над
            // незапущенной игрой предлагала бы продолжить несуществующий забег.
            if (GUI.Button(new Rect(panel.xMax - 174f, panel.y + 42f, 124f, 48f), "НАЗАД", _button))
            {
                SaveAudioIfNeeded();
                if (MainMenuView.IsOpen) Close();
                else _page = Page.Main;
            }

            // Три вкладки вместо двух: 224 в ширину при зазоре 16 ровно
            // укладываются в те же 724 полезных пикселя панели.
            const float tabWidth = 224f;
            const float tabGap = 16f;
            float tabX = panel.x + 48f;
            float tabY = panel.y + 116f;
            if (GUI.Button(new Rect(tabX, tabY, tabWidth, 54f), "ГРАФИКА",
                    _page == Page.Graphics ? _activeTab : _tab))
                _page = Page.Graphics;
            tabX += tabWidth + tabGap;
            if (GUI.Button(new Rect(tabX, tabY, tabWidth, 54f), "УПРАВЛЕНИЕ",
                    _page == Page.Controls ? _activeTab : _tab))
                _page = Page.Controls;
            tabX += tabWidth + tabGap;
            if (GUI.Button(new Rect(tabX, tabY, tabWidth, 54f), "ЗВУК",
                    _page == Page.Audio ? _activeTab : _tab))
                _page = Page.Audio;

            if (_page == Page.Graphics) DrawGraphics(panel);
            else if (_page == Page.Controls) DrawControls(panel);
            else DrawAudio(panel);
        }

        /// <summary>
        /// Ряд способностей и напоминание об остальных клавишах.
        ///
        /// Полного ремапа здесь нет намеренно: это отдельный экран с
        /// конфликтами, дефолтами и сбросом, а спор ровно один — QWER против
        /// цифр. Остальные клавиши перечислены как справка, не как поля ввода.
        /// </summary>
        private void DrawControls(Rect panel)
        {
            bool letters = GameUserSettings.AbilityRowUsesLetters;
            float x = panel.x + 64f;
            float y = panel.y + 226f;
            DrawSectionTitle(x, y, "СПОСОБНОСТИ");
            y += 64f;

            GUI.Label(new Rect(x, y, 260f, 38f), "Движение", _label);
            int layoutIndex = GameUserSettings.WasdMovement ? 1 : 0;
            if (layoutIndex < 0) layoutIndex = 0;
            layoutIndex = DrawChoice(new Rect(x + 282f, y - 4f, 410f, 50f),
                (layoutIndex == 1 ? "WASD" : "Мышь"), layoutIndex, 2);
            GameUserSettings.SetWasdMovement(layoutIndex == 1);
            y += 76f;

            // Две строки — потолок: ниже начинается список клавиш, и он обязан
            // уместиться в панель целиком. Длинное объяснение уводило «Паузу»
            // за нижнюю кромку.
            GUI.Label(new Rect(x, y, 690f, 60f),
                letters
                    ? "Рука не сходит с позиции WASD. Способности работают и на манекенах в лагере."
                    : "Классический ряд ARPG. Ни одна клавиша не спорит с командами лагеря.",
                _subtitle);
            y += 78f;

            DrawSectionTitle(x, y, "ОСТАЛЬНОЕ");
            y += 60f;
            DrawKeyRow(x, ref y, "Идти · атаковать",
                GameUserSettings.WasdMovement ? "WASD · ЛКМ" : "ПКМ · ЛКМ");
            DrawKeyRow(x, ref y, "Выбрать награду",
                GameKeyBindings.Label(GameAction.Ability1) + " · "
                + GameKeyBindings.Label(GameAction.Ability2) + " · "
                + GameKeyBindings.Label(GameAction.Ability3));
            DrawKeyRow(x, ref y, "Уйти из Разлома с добычей", "L");
            DrawKeyRow(x, ref y, "Войти в забег", "Зайти в арку");
            DrawKeyRow(x, ref y, "Повторить забег · вернуться в лагерь", "R · C");
            DrawKeyRow(x, ref y, "Пауза", "ESC");
        }

        private void DrawKeyRow(float x, ref float y, string action, string keys)
        {
            GUI.Label(new Rect(x, y, 430f, 34f), action, _label);
            GUI.Label(new Rect(x + 430f, y, 262f, 34f), keys, _value);
            y += 40f;
        }

        private static string AbilityLayoutName(GameUserSettings.AbilityLayout layout)
            => layout == GameUserSettings.AbilityLayout.Digits ? "1  2  3  4" : "Q  W  E  R";

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
            y += 86f;

            GUI.Label(new Rect(x, y, 260f, 38f), "Качество", _label);
            int qualityIndex = Array.IndexOf(QualityLevels, GameUserSettings.Quality);
            if (qualityIndex < 0) qualityIndex = QualityLevels.Length - 1;
            qualityIndex = DrawChoice(new Rect(x + 282f, y - 4f, 410f, 50f),
                GameUserSettings.QualityName(QualityLevels[qualityIndex]),
                qualityIndex, QualityLevels.Length);
            GameUserSettings.SetQuality(QualityLevels[qualityIndex]);
            y += 86f;

            GUI.Label(new Rect(x, y, 260f, 38f), "Частота кадров", _label);
            int capIndex = Array.IndexOf(GameUserSettings.FrameCaps, GameUserSettings.FrameCap);
            if (capIndex < 0) capIndex = 0;
            capIndex = DrawChoice(new Rect(x + 282f, y - 4f, 410f, 50f),
                GameUserSettings.FrameCapName(GameUserSettings.FrameCaps[capIndex]),
                capIndex, GameUserSettings.FrameCaps.Length);
            GameUserSettings.SetFrameCap(GameUserSettings.FrameCaps[capIndex]);
            y += 78f;

            GUI.Label(new Rect(x, y, 690f, 70f),
                GameUserSettings.Quality == GameUserSettings.QualityLevel.Low
                    ? "Низкое рисует кадр в три четверти разрешения и снимает "
                      + "сглаживание — картинка мягче, кадров заметно больше."
                    : GameUserSettings.FrameCap == 0
                        ? "Кадры идут в такт монитору: разрывов нет, шаг ровный. "
                          + "Самый плавный вариант, если видеокарта успевает."
                        : "Разрешение и режим экрана применяются кнопкой ниже "
                          + "и их можно безопасно проверить перед сохранением.",
                _subtitle);
            y += 84f;
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
            _resolutionLabels = new string[_resolutions.Length];
            for (int i = 0; i < _resolutions.Length; i++) _resolutionLabels[i] = ResolutionName(_resolutions[i]);
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

            _title = new GUIStyle(GameTypography.Label)
            {
                fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            _title.normal.textColor = Text;
            _subtitle = new GUIStyle(GameTypography.Label)
            {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            _subtitle.normal.textColor = Muted;
            _label = new GUIStyle(GameTypography.Label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
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
            var style = new GUIStyle(GameTypography.Button)
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
