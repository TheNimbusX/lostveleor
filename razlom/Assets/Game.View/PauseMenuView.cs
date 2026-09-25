using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Game.View
{
    /// <summary>
    /// Меню паузы на Canvas: префаб Resources/UI/Prefabs/PauseMenu.
    ///
    /// Ссылки на части префаба и все переходы между состояниями. Что показано
    /// и что делает кнопка — решает <see cref="PauseMenu"/>; как это выглядит и
    /// как быстро двигается — правится в префабе (позиции окон берутся из
    /// префаба при запуске, длительности — поля ниже).
    ///
    /// Состояния: одна пауза по центру → «Настройки»/«Управление»: пауза
    /// уезжает влево, окно выезжает справа → подтверждение поверх всего.
    /// </summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        public enum Window { None = 0, Settings = 1, Controls = 2 }
        public enum Tab { Graphics = 0, Audio = 1, Game = 2 }

        /// <summary>Версия раскладки сборщика; см. PauseMenuBuilder.LayoutVersion.</summary>
        [HideInInspector] public int LayoutVersion;

        [Header("Движение")]
        [Tooltip("X паузы, когда окно закрыто")] public float PauseCenterX = 0f;
        [Tooltip("X паузы, когда открыто окно настроек или управления")] public float PauseShiftedX = -520f;
        [Tooltip("На сколько окно выезжает справа")] public float WindowSlide = 80f;
        public float OpenDuration = 0.22f;
        public float WindowDuration = 0.3f;
        public float TabDuration = 0.2f;
        public float CloseDuration = 0.16f;

        [Header("Появление лесенкой")]
        [Tooltip("Задержка между кнопками паузы при открытии, секунд")] public float ButtonStagger = 0.035f;
        [Tooltip("Задержка между строками страницы, секунд")] public float RowStagger = 0.025f;
        [Tooltip("Откуда приезжает элемент (сдвиг от места)")] public Vector2 ArriveOffset = new Vector2(0f, -14f);
        public float ArriveDuration = 0.22f;

        [Header("Общее")]
        public CanvasGroup Backdrop;
        [Tooltip("Размытый снимок игры под меню")] public UiBackdropBlur BackdropBlur;
        public TMP_Text Hint;

        [Header("Пауза")]
        public RectTransform PausePanel;
        public Button Continue;
        public Button Settings;
        public Button Controls;
        public Button Camp;
        public Button Quit;
        [Tooltip("Кольцо свечения у «Настройки», пока окно настроек открыто")] public GameObject SettingsGlow;
        [Tooltip("Кольцо свечения у «Управление», пока окно управления открыто")] public GameObject ControlsGlow;

        [Header("Настройки")]
        public RectTransform SettingsPanel;
        public Button TabGraphics;
        public Button TabAudio;
        public Button TabGame;
        [Tooltip("Коралловая подложка выбранной вкладки; переезжает")] public RectTransform TabIndicator;
        public Color TabTextOn = Color.white;
        public Color TabTextOff = new Color32(0xCF, 0xDD, 0xEB, 0xFF);
        public CanvasGroup GraphicsPage;
        public CanvasGroup AudioPage;
        public CanvasGroup GamePage;
        public Button Reset;
        public Button Apply;
        public TMP_Text Status;
        [Tooltip("«Назад» — только когда настройки открыты из главного меню")] public Button SettingsBack;

        [Header("Графика")]
        public UiSegmented DisplayMode;
        public UiDropdown Resolution;
        public UiSegmented Quality;
        public UiToggle VSync;
        public Slider FrameLimit;
        public TMP_Text FrameLimitValue;
        [Tooltip("Строка предела кадров: притухает, пока включена синхронизация")] public CanvasGroup FrameLimitRow;
        public UiSegmented Shadows;
        public Slider UiScale;
        public TMP_Text UiScaleValue;

        [Header("Звук")]
        public Slider Master;
        public Slider Effects;
        public Slider Music;
        public TMP_Text MasterValue;
        public TMP_Text EffectsValue;
        public TMP_Text MusicValue;

        [Header("Игра")]
        public TMP_Text GameText;

        [Header("Управление")]
        public RectTransform ControlsPanel;
        public UiSegmented AbilityLayout;
        public TMP_Text ControlsHint;
        [Tooltip("Контейнер строк клавиш внутри прокрутки")] public RectTransform BindingsContent;
        [Tooltip("Шаблон строки клавиши; сам остаётся выключенным")] public UiKeyBindingRow BindingTemplate;
        [Tooltip("Колонка «Бой»: способности и кувырок")] public RectTransform BindingsCombat;
        [Tooltip("Колонка «Мир»: команды лагеря и Разлома")] public RectTransform BindingsWorld;
        public Button ControlsReset;
        public Button ControlsBack;

        [Header("Подтверждение")]
        public RectTransform ConfirmPanel;
        public TMP_Text ConfirmTitle;
        public TMP_Text ConfirmText;
        public TMP_Text ConfirmCountdown;
        public Button ConfirmYes;
        public Button ConfirmNo;
        public TMP_Text ConfirmYesLabel;
        public TMP_Text ConfirmNoLabel;

        Vector2 _settingsRest, _controlsRest;
        bool _presented;
        Window _window;
        Tab _tab;
        bool _confirming, _fromMainMenu;

        void Awake()
        {
            if (SettingsPanel != null) _settingsRest = SettingsPanel.anchoredPosition;
            if (ControlsPanel != null) _controlsRest = ControlsPanel.anchoredPosition;
            if (BindingTemplate != null) BindingTemplate.gameObject.SetActive(false);
            if (GetComponent<UiScaleFollower>() == null && GetComponent<CanvasScaler>() != null) gameObject.AddComponent<UiScaleFollower>();
            EnsureEventSystem();
        }

        /// <summary>Кнопкам Canvas нужен EventSystem; в сцене его может ещё не быть.</summary>
        internal static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var events = new GameObject("UI EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
            DontDestroyOnLoad(events);
        }

        // ---- открытие и закрытие ----

        /// <summary>Меню появилось: фон темнеет, панели проявляются с лёгким увеличением.</summary>
        internal void PlayOpen(Window window, Tab tab, bool fromMainMenu)
        {
            UiMotion.Stop(this);
            gameObject.SetActive(true);
            _presented = false;
            Present(window, tab, false, fromMainMenu);
            if (Backdrop != null) { Backdrop.alpha = 0f; UiMotion.FadeTo(Backdrop, 1f, OpenDuration); }
            PopIn(PausePanel);
            if (window != Window.None) { PopIn(Panel(window)); StaggerRows(ActivePage(window, tab)); }
            // Кнопки паузы приезжают по очереди снизу вверх.
            Button[] buttons = { Continue, Settings, Controls, Camp, Quit };
            int shown = 0;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                if (buttons[i] == null || !buttons[i].gameObject.activeSelf) continue;
                Arrive((RectTransform)buttons[i].transform, OpenDuration * 0.3f + shown++ * ButtonStagger, true);
            }
            UiSound.Play(fromMainMenu ? UiSoundEvent.WindowOpen : UiSoundEvent.PauseOpen);
            FocusGamepad(window, tab, false);
        }

        /// <summary>Меню гаснет и выключается после анимации.</summary>
        internal void PlayClose()
        {
            if (!gameObject.activeSelf) return;
            UiSound.Play(UiSoundEvent.PauseClose);
            if (Backdrop != null) UiMotion.FadeTo(Backdrop, 0f, CloseDuration);
            foreach (RectTransform panel in new[] { PausePanel, SettingsPanel, ControlsPanel, ConfirmPanel })
                if (panel != null && panel.gameObject.activeSelf) UiMotion.FadeTo(Group(panel), 0f, CloseDuration);
            UiMotion.Play(this, 99, CloseDuration, _ => { }, null, () => gameObject.SetActive(false));
        }

        void PopIn(RectTransform panel)
        {
            if (panel == null) return;
            CanvasGroup group = Group(panel);
            group.alpha = 0f;
            panel.localScale = Vector3.one * 0.96f;
            UiMotion.FadeTo(group, 1f, OpenDuration);
            UiMotion.ScaleTo(panel, 1f, OpenDuration);
        }

        // ---- состояние ----

        /// <summary>
        /// Показать состояние меню. Анимирует только то, что изменилось с
        /// прошлого вызова; вызывается каждый кадр, пока меню открыто.
        /// </summary>
        internal void Present(Window window, Tab tab, bool confirming, bool fromMainMenu)
        {
            bool first = !_presented;
            bool windowChanged = first || window != _window || fromMainMenu != _fromMainMenu;
            bool tabChanged = first || tab != _tab;
            bool confirmChanged = first || confirming != _confirming;

            if (windowChanged) ShowWindows(window, fromMainMenu, first);
            if (tabChanged) ShowTab(tab, first);
            if (confirmChanged) ShowConfirmation(confirming, first);
            if (!first)
            {
                if (windowChanged)
                {
                    UiSound.Play(window == Window.None ? UiSoundEvent.WindowClose : UiSoundEvent.WindowOpen);
                    if (window != Window.None) StaggerRows(ActivePage(window, tab));
                }
                else if (tabChanged && window == Window.Settings)
                {
                    UiSound.Play(UiSoundEvent.Tab);
                    StaggerRows(ActivePage(window, tab));
                }
                if (confirmChanged && confirming) UiSound.Play(UiSoundEvent.WindowOpen);
            }

            _window = window; _tab = tab; _confirming = confirming; _fromMainMenu = fromMainMenu;
            _presented = true;
            if (windowChanged || confirmChanged || tabChanged) FocusGamepad(window, tab, confirming);
        }

        private void FocusGamepad(Window window, Tab tab, bool confirming)
        {
            if (!TickDriver.GamepadLastUsed || EventSystem.current == null) return;
            Selectable preferred = confirming ? ConfirmYes
                : window == Window.None ? Continue
                : window == Window.Settings
                    ? tab == Tab.Graphics ? TabGraphics : tab == Tab.Audio ? TabAudio : TabGame
                    : ControlsBack;
            if (preferred != null && preferred.gameObject.activeInHierarchy && preferred.IsInteractable())
                EventSystem.current.SetSelectedGameObject(preferred.gameObject);
        }

        void ShowWindows(Window window, bool fromMainMenu, bool instant)
        {
            float duration = instant ? 0f : WindowDuration;

            // Пауза: из главного меню её нет; иначе по центру или сдвинута к окну.
            if (PausePanel != null)
            {
                bool shown = !fromMainMenu;
                SetShown(PausePanel, shown, instant);
                if (shown)
                {
                    var target = new Vector2(window == Window.None ? PauseCenterX : PauseShiftedX, PausePanel.anchoredPosition.y);
                    if (instant) PausePanel.anchoredPosition = target; else UiMotion.MoveTo(PausePanel, target, duration);
                }
            }
            Slide(SettingsPanel, _settingsRest, window == Window.Settings, instant);
            Slide(ControlsPanel, _controlsRest, window == Window.Controls, instant);
            SetGlow(SettingsGlow, window == Window.Settings);
            SetGlow(ControlsGlow, window == Window.Controls);
            Show(SettingsBack, fromMainMenu);
            if (Hint != null) Hint.text = fromMainMenu ? "Esc — назад" : window == Window.None ? "Esc — продолжить игру" : "Esc — назад к паузе";
        }

        void Slide(RectTransform panel, Vector2 rest, bool shown, bool instant)
        {
            if (panel == null) return;
            CanvasGroup group = Group(panel);
            UiMotion.Stop(panel);
            UiMotion.Stop(group);
            if (instant)
            {
                panel.gameObject.SetActive(shown);
                panel.anchoredPosition = rest;
                group.alpha = shown ? 1f : 0f;
                return;
            }
            if (shown)
            {
                if (!panel.gameObject.activeSelf) { panel.gameObject.SetActive(true); group.alpha = 0f; panel.anchoredPosition = rest + new Vector2(WindowSlide, 0f); }
                UiMotion.MoveTo(panel, rest, WindowDuration);
                UiMotion.FadeTo(group, 1f, WindowDuration);
            }
            else if (panel.gameObject.activeSelf)
            {
                UiMotion.MoveTo(panel, rest + new Vector2(WindowSlide, 0f), WindowDuration * 0.7f);
                UiMotion.FadeTo(group, 0f, WindowDuration * 0.7f, () => { panel.gameObject.SetActive(false); panel.anchoredPosition = rest; });
            }
        }

        void SetShown(RectTransform panel, bool shown, bool instant)
        {
            CanvasGroup group = Group(panel);
            if (shown == panel.gameObject.activeSelf && (!shown || group.alpha > 0.99f)) return;
            if (instant) { panel.gameObject.SetActive(shown); group.alpha = shown ? 1f : 0f; return; }
            if (shown) { panel.gameObject.SetActive(true); UiMotion.FadeTo(group, 1f, WindowDuration); }
            else UiMotion.FadeTo(group, 0f, WindowDuration * 0.6f, () => panel.gameObject.SetActive(false));
        }

        static void SetGlow(GameObject glow, bool on)
        {
            if (glow != null && glow.activeSelf != on) glow.SetActive(on);
        }

        void ShowTab(Tab tab, bool instant)
        {
            Button[] tabs = { TabGraphics, TabAudio, TabGame };
            CanvasGroup[] pages = { GraphicsPage, AudioPage, GamePage };
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == (int)tab;
                // Синий фон выбранной вкладки уходит — под ним видна переезжающая коралловая подложка.
                if (tabs[i] != null && tabs[i].image != null) tabs[i].image.CrossFadeAlpha(on ? 0f : 1f, instant ? 0f : TabDuration, true);
                var label = tabs[i] != null ? tabs[i].GetComponentInChildren<TMP_Text>(true) : null;
                if (label != null) { if (instant) label.color = on ? TabTextOn : TabTextOff; else UiMotion.ColorTo(label, on ? TabTextOn : TabTextOff, TabDuration); }

                CanvasGroup page = pages[i];
                if (page == null) continue;
                UiMotion.Stop(page);
                if (on)
                {
                    page.gameObject.SetActive(true);
                    if (instant) page.alpha = 1f;
                    else
                    {
                        page.alpha = 0f;
                        var rect = (RectTransform)page.transform;
                        Vector2 rest = rect.anchoredPosition;
                        rect.anchoredPosition = rest + new Vector2(0f, -12f);
                        UiMotion.FadeTo(page, 1f, TabDuration);
                        UiMotion.MoveTo(rect, rest, TabDuration);
                    }
                }
                else page.gameObject.SetActive(false);
            }
            Show(Apply, tab == Tab.Graphics);
            Show(Reset, tab != Tab.Game);

            Button selected = tabs[(int)tab];
            if (TabIndicator == null || selected == null) return;
            var target = (RectTransform)selected.transform;
            Vector2 position = target.anchoredPosition, size = target.sizeDelta;
            if (instant) { TabIndicator.anchoredPosition = position; TabIndicator.sizeDelta = size; return; }
            Vector2 fromPosition = TabIndicator.anchoredPosition, fromSize = TabIndicator.sizeDelta;
            UiMotion.Play(TabIndicator, 10, TabDuration, t =>
            {
                TabIndicator.anchoredPosition = Vector2.LerpUnclamped(fromPosition, position, t);
                TabIndicator.sizeDelta = Vector2.LerpUnclamped(fromSize, size, t);
            });
        }

        void ShowConfirmation(bool confirming, bool instant)
        {
            if (ConfirmPanel == null) return;
            CanvasGroup group = Group(ConfirmPanel);
            // Окна под подтверждением притухают и не ловят мышь.
            foreach (RectTransform panel in new[] { PausePanel, SettingsPanel, ControlsPanel })
            {
                if (panel == null) continue;
                CanvasGroup under = Group(panel);
                under.interactable = under.blocksRaycasts = !confirming;
                if (panel.gameObject.activeSelf && !instant) UiMotion.FadeTo(under, confirming ? 0.35f : 1f, OpenDuration);
            }
            if (confirming)
            {
                ConfirmPanel.gameObject.SetActive(true);
                group.alpha = instant ? 1f : 0f;
                ConfirmPanel.localScale = Vector3.one * (instant ? 1f : 0.9f);
                if (!instant) { UiMotion.FadeTo(group, 1f, OpenDuration); UiMotion.ScaleTo(ConfirmPanel, 1f, OpenDuration); }
            }
            else if (ConfirmPanel.gameObject.activeSelf)
            {
                if (instant) ConfirmPanel.gameObject.SetActive(false);
                else UiMotion.FadeTo(group, 0f, CloseDuration, () => ConfirmPanel.gameObject.SetActive(false));
            }
        }

        RectTransform Panel(Window window) => window == Window.Settings ? SettingsPanel : window == Window.Controls ? ControlsPanel : null;

        // ---- появление лесенкой ----
        readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _rest = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        Transform ActivePage(Window window, Tab tab)
        {
            if (window == Window.Controls) return ControlsPanel;
            CanvasGroup page = tab == Tab.Audio ? AudioPage : tab == Tab.Game ? GamePage : GraphicsPage;
            return page != null ? page.transform : null;
        }

        /// <summary>Строки страницы (элементы с откликом на мышь) приезжают сверху вниз.</summary>
        void StaggerRows(Transform page)
        {
            if (page == null) return;
            int index = 0;
            foreach (Transform child in page)
            {
                if (!child.gameObject.activeSelf || !(child is RectTransform rect)) continue;
                if (child.GetComponent<UiHoverMotion>() != null) Arrive(rect, WindowDuration * 0.25f + index++ * RowStagger, rect.GetComponent<CanvasGroup>() != FrameLimitRow);
                else if (rect == BindingsCombat || rect == BindingsWorld)
                    foreach (Transform binding in rect)
                        if (binding.gameObject.activeSelf && binding is RectTransform row)
                            FadeIn(row, WindowDuration * 0.25f + index++ * RowStagger);
            }
        }

        /// <summary>Приезд на место покоя; позиция покоя запоминается один раз — повторы не сдвигают элемент.</summary>
        void Arrive(RectTransform rect, float delay, bool fade)
        {
            if (!_rest.TryGetValue(rect, out Vector2 rest)) _rest[rect] = rest = rect.anchoredPosition;
            Vector2 from = rest + ArriveOffset;
            rect.anchoredPosition = from;
            UiMotion.Play(rect, 1, ArriveDuration, t => rect.anchoredPosition = Vector2.LerpUnclamped(from, rest, t), null, null, delay);
            if (fade) FadeIn(rect, delay);
        }

        /// <summary>Проявление без сдвига: строки внутри раскладки двигать нельзя.</summary>
        void FadeIn(RectTransform rect, float delay)
        {
            CanvasGroup group = Group(rect);
            UiMotion.Play(group, 2, ArriveDuration, t => group.alpha = t, null, null, delay);
        }

        /// <summary>Строка, отдавшая клавишу при обмене, вспыхивает — обмен виден глазом.</summary>
        internal static void Flash(UiKeyBindingRow row) => row?.Flash();

        static CanvasGroup Group(Component panel)
        {
            var group = panel.GetComponent<CanvasGroup>();
            return group != null ? group : panel.gameObject.AddComponent<CanvasGroup>();
        }

        // ---- строки клавиш ----

        /// <summary>Строки назначений создаются из шаблона один раз; вид правится на шаблоне.</summary>
        internal UiKeyBindingRow[] BuildBindingRows(int count, Action<UiKeyBindingRow> clicked)
        {
            RectTransform fallback = BindingsContent != null ? BindingsContent : BindingsCombat;
            if (BindingTemplate == null || fallback == null) return Array.Empty<UiKeyBindingRow>();
            var rows = new UiKeyBindingRow[count];
            for (int i = 0; i < count; i++)
            {
                // Две колонки по группам обмена клавиш: «Бой» до кувырка включительно, дальше «Мир».
                RectTransform column = BindingsCombat != null && BindingsWorld != null
                    ? ((GameAction)i <= GameAction.Dash ? BindingsCombat : BindingsWorld)
                    : fallback;
                UiKeyBindingRow row = Instantiate(BindingTemplate, column);
                row.gameObject.SetActive(true);
                row.name = "Binding " + (GameAction)i;
                row.Binding = (GameAction)i;
                row.Clicked += clicked;
                rows[i] = row;
            }
            return rows;
        }

        // ---- помощники для PauseMenu ----
        internal static void Show(Component part, bool shown)
        {
            if (part != null && part.gameObject.activeSelf != shown) part.gameObject.SetActive(shown);
        }

        internal static void SetText(TMP_Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }
    }
}
