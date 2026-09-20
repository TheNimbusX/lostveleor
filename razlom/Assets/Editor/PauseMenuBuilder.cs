using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.CombatHudBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Собирает префаб меню паузы по ART/UI/concepts-2026-09-15/v3-pause.png
    /// из нарисованного пака Assets/UI/Kit/Pause (tools/ui-kit/compose-pause-kit.ps1).
    ///
    /// Поведение по согласованному плану (15 сентября): по Escape — одна пауза
    /// по центру; «Настройки»/«Управление» сдвигают её влево и выдвигают окно.
    /// Настройки: Графика · Звук · Игра («в разработке»); управление — своё окно
    /// с назначением клавиш. Всё анимировано компонентами Ui* и PauseMenuView.
    ///
    /// Сборщик создаёт префаб, только если его нет или он старше
    /// <see cref="RebuildBelow"/>; дальше префаб правится руками.
    /// </summary>
    [InitializeOnLoad]
    public static partial class PauseMenuBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/PauseMenu.prefab";

        /// <summary>
        /// Версия раскладки. v3 (15 сентября) — нарисованный пак, анимации, окна
        /// по плану владельца; префабы v1–v2 он не правил, они пересобираются.
        /// Дальнейшие изменения — только миграциями поверх ручных правок.
        /// </summary>
        public const int LayoutVersion = 7;
        const int RebuildBelow = 3;

        /// <summary>
        /// v4: длинные подписи кнопок меню («ВЫЙТИ ИЗ ИГРЫ») заходили на ромб справа —
        /// отступ справа больше, подпись ужимается по ширине. Поверх ручных правок.
        /// </summary>
        static void Migrate(PauseMenuView view)
        {
            if (view.LayoutVersion < 4)
                foreach (Button button in new[] { view.Continue, view.Settings, view.Controls, view.Camp, view.Quit })
                    if (button != null && button.transform.Find("Label") is RectTransform rect && rect.GetComponent<TMP_Text>() is TMP_Text label)
                        FitMenuLabel(label);
            if (view.LayoutVersion < 5) MigrateTo5(view);
            if (view.LayoutVersion < 6) MigrateTo6(view);
            if (view.LayoutVersion < 7 && view.ControlsPanel != null)
                foreach (var label in view.ControlsPanel.GetComponentsInChildren<TMP_Text>(true))
                    if (label.text == "Раскладка способностей") label.text = "Движение";
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        static void FitMenuLabel(TMP_Text label)
        {
            label.margin = new Vector4(label.margin.x, label.margin.y, 56f, label.margin.w);
            label.fontSizeMax = Mathf.Max(label.fontSize, 14f);
            label.fontSizeMin = 16f;
            label.enableAutoSizing = true;
        }

        const string PauseKit = UiKitImport.KitRoot + "/Pause";

        static readonly Color Cream = Hex(0xF3E8D9);
        static readonly Color NavyInk = Hex(0x1C3A5E);
        static readonly Color Muted = Hex(0x9DB6CB);
        static readonly Color Idle = Hex(0xCFDDEB);

        static readonly Vector2 Center = new Vector2(.5f, .5f);
        static readonly Vector2 TopCenter = new Vector2(.5f, 1f);
        static readonly Vector2 BottomCenter = new Vector2(.5f, 0f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 BottomLeft = Vector2.zero;
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        static readonly Vector2 LeftMiddle = new Vector2(0f, .5f);
        static readonly Vector2 RightMiddle = new Vector2(1f, .5f);

        static TMP_FontAsset _regular, _semibold, _bold;

        static PauseMenuBuilder()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EnsureBuilt(false);
            };
        }

        [MenuItem("Разлом/UI/Собрать меню паузы")]
        static void RebuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Меню паузы",
                    "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".", "Пересобрать", "Отмена"))
                return;
            EnsureBuilt(true);
        }

        public static bool EnsureBuilt(bool force)
        {
            EnsureSoundBank();
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!force && existing != null)
            {
                var built = existing.GetComponentInChildren<PauseMenuView>(true);
                if (built != null && built.LayoutVersion >= LayoutVersion) return true;
                if (built != null && built.LayoutVersion >= RebuildBelow)
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                    try
                    {
                        var view = contents.GetComponentInChildren<PauseMenuView>(true);
                        int from = view.LayoutVersion;
                        Migrate(view);
                        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                        Debug.Log("[ui-kit] Меню паузы доработано поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    return true;
                }
                Debug.Log("[ui-kit] Меню паузы v" + (built != null ? built.LayoutVersion : 0) + " пересобирается по плану v" + LayoutVersion + ".");
            }
            if (!EnsureEssentials()) return false;
            _regular = EnsureFont("Regular");
            _semibold = EnsureFont("SemiBold");
            _bold = EnsureFont("Bold");
            if (_regular == null || _semibold == null || _bold == null || Kit("pause_panel") == null)
            {
                Debug.LogError("[ui-kit] Нет шрифтов или пака Pause — меню паузы не собрано.");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject root = Build();
            Migrate(root.GetComponent<PauseMenuView>());
            try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ui-kit] Меню паузы собрано: " + PrefabPath);
            return true;
        }

        static Sprite Kit(string name)
        {
            string path = PauseKit + "/" + name + ".png";
            UiKitImport.Ensure(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[ui-kit] Нет спрайта " + path);
            return sprite;
        }

        static GameObject Build()
        {
            var root = new GameObject("PauseMenu", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Высота 1200 при раскладке под 1080: меню занимает 90% кадра.
            scaler.referenceResolution = new Vector2(2133f, 1200f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var view = root.AddComponent<PauseMenuView>();
            // Чистая сборка = раскладка v3 плюс те же миграции, что дорабатывают ручной префаб.
            view.LayoutVersion = 3;

            RectTransform backdrop = Stretch("Backdrop", root.transform, 0f);
            Img(backdrop, null, new Color(.01f, .03f, .06f, .62f)).raycastTarget = true;
            view.Backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();

            BuildPause(root.transform, view);
            BuildSettings(root.transform, view);
            BuildControls(root.transform, view);
            BuildConfirm(root.transform, view);
            return root;
        }

        // ---- пауза ----
        static void BuildPause(Transform parent, PauseMenuView view)
        {
            RectTransform panel = Panel("Pause Panel", parent, new Vector2(0f, 396f), new Vector2(424f, 796f));
            view.PausePanel = panel;
            view.PauseCenterX = 0f;
            view.PauseShiftedX = -520f;

            RectTransform ribbon = Node("Title Ribbon", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -34f), new Vector2(480f, 94f));
            Img(ribbon, Kit("pause_ribbon"), Color.white);
            Label(Stretch("Title", ribbon, 0f), "Пауза", _bold, 40f, Color.white, TextAlignmentOptions.Center, 30f, true).margin = new Vector4(100f, 0f, 100f, 6f);

            view.Continue = MenuButton(panel, "Continue", 168f, "Продолжить", "menu_play", true, out _);
            view.Settings = MenuButton(panel, "Settings", 278f, "Настройки", "menu_settings", false, out view.SettingsGlow);
            view.Controls = MenuButton(panel, "Controls", 388f, "Управление", "menu_controls", false, out view.ControlsGlow);
            view.Camp = MenuButton(panel, "Camp", 498f, "В лагерь", "menu_camp", false, out _);
            view.Quit = MenuButton(panel, "Quit", 608f, "Выйти из игры", "menu_exit", false, out _);

            view.Hint = Label(Node("Hint", panel, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 34f), new Vector2(380f, 28f)),
                "ESC — продолжить игру", _regular, 18f, Muted, TextAlignmentOptions.Center);
        }

        static Button MenuButton(RectTransform panel, string name, float top, string text, string icon, bool coral, out GameObject glow)
        {
            RectTransform rect = Node(name, panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -top), new Vector2(360f, 86f));
            glow = null;
            if (!coral)
            {
                // Кольцо свечения открытого пункта: за кнопкой, «дышит».
                RectTransform ring = Stretch("Glow", rect, -30f);
                var glowImage = Img(ring, Kit("pause_glow_ring"), Color.white);
                var pulse = ring.gameObject.AddComponent<UiPulse>();
                pulse.Graphic = glowImage;
                ring.gameObject.SetActive(false);
                glow = ring.gameObject;
            }
            Button button = coral
                ? MakeButton(rect, Kit("pause_button_coral"), Kit("pause_button_coral_hover"), Kit("pause_button_coral_pressed"))
                : MakeButton(rect, Kit("pause_button"), Kit("pause_button_hover"), Kit("pause_button_pressed"));
            if (glow != null) glow.transform.SetAsLastSibling();
            IconImage(Node("Icon", rect, LeftMiddle, LeftMiddle, Center, new Vector2(64f, 0f), new Vector2(42f, 42f)), icon, Color.white);
            TextMeshProUGUI label = Label(Stretch("Label", rect, 0f), text, _bold, 23f, coral ? Color.white : Cream, TextAlignmentOptions.MidlineLeft, 12f, true);
            label.margin = new Vector4(106f, 0f, 26f, 0f);
            FitMenuLabel(label);
            rect.gameObject.AddComponent<UiHoverMotion>();
            return button;
        }

        // ---- настройки ----
        static void BuildSettings(Transform parent, PauseMenuView view)
        {
            RectTransform panel = Panel("Settings Panel", parent, new Vector2(248f, 396f), new Vector2(1016f, 836f));
            view.SettingsPanel = panel;

            RectTransform indicator = Node("Tab Indicator", panel, TopLeft, TopLeft, TopLeft, new Vector2(58f, -30f), new Vector2(300f, 62f));
            Img(indicator, Kit("pause_tab_on"), Color.white);
            view.TabIndicator = indicator;
            view.TabGraphics = Tab(panel, "Tab Graphics", 58f, "Графика");
            view.TabAudio = Tab(panel, "Tab Audio", 358f, "Звук");
            view.TabGame = Tab(panel, "Tab Game", 658f, "Игра");

            view.GraphicsPage = Page(panel, "Graphics Page", true);
            RectTransform graphics = (RectTransform)view.GraphicsPage.transform;
            view.DisplayMode = Segmented(Control(Row(graphics, 0, "Режим экрана", "menu_screen"), 460f), 3);
            view.Resolution = Dropdown(Control(Row(graphics, 1, "Разрешение", "menu_ui_scale"), 460f));
            view.Quality = Segmented(Control(Row(graphics, 2, "Качество", "menu_screen"), 460f), 3);
            view.VSync = Toggle(Row(graphics, 3, "Вертикальная синхронизация", "menu_apply"));
            RectTransform limitRow = Row(graphics, 4, "Ограничение кадров", "menu_framerate");
            view.FrameLimitRow = limitRow.gameObject.AddComponent<CanvasGroup>();
            view.FrameLimit = SliderControl(limitRow, out view.FrameLimitValue);
            view.Shadows = Segmented(Control(Row(graphics, 5, "Тени", "menu_shadows"), 460f), 3);
            view.UiScale = SliderControl(Row(graphics, 6, "Масштаб интерфейса", "menu_ui_scale"), out view.UiScaleValue);
            // Список разрешений рисуется поверх нижних строк: выносим строку разрешения выше соседей.
            ((RectTransform)view.Resolution.transform).parent.SetAsLastSibling();

            view.AudioPage = Page(panel, "Audio Page", false);
            RectTransform audio = (RectTransform)view.AudioPage.transform;
            view.Master = SliderControl(Row(audio, 0, "Общая громкость", "menu_sound"), out view.MasterValue);
            view.Effects = SliderControl(Row(audio, 1, "Эффекты", "menu_sound"), out view.EffectsValue);
            view.Music = SliderControl(Row(audio, 2, "Музыка", "menu_music"), out view.MusicValue);

            view.GamePage = Page(panel, "Game Page", false);
            view.GameText = Label(Stretch("Text", view.GamePage.transform, 0f), "В разработке", _semibold, 32f, Idle, TextAlignmentOptions.Center, 12f, true);

            RectTransform divider = Node("Divider", panel, BottomLeft, BottomLeft, BottomLeft, new Vector2(40f, 62f), new Vector2(420f, 22f));
            Img(divider, Kit("pause_divider"), Color.white, false).preserveAspect = true;
            view.Status = Label(Node("Status", panel, BottomLeft, BottomLeft, BottomLeft, new Vector2(44f, 26f), new Vector2(430f, 32f)),
                "", _regular, 18f, Muted, TextAlignmentOptions.MidlineLeft);
            view.Reset = FooterButton(panel, "Reset", new Vector2(-300f, 24f), "Сбросить", "menu_reset", false);
            view.Apply = FooterButton(panel, "Apply", new Vector2(-27f, 24f), "Применить", "menu_apply", true);
            view.SettingsBack = BackButton(panel);
            panel.gameObject.SetActive(false);
        }

        static Button Tab(RectTransform panel, string name, float left, string text)
        {
            RectTransform rect = Node(name, panel, TopLeft, TopLeft, TopLeft, new Vector2(left, -30f), new Vector2(300f, 62f));
            // Выбранную вкладку показывает переезжающая коралловая подложка, сама вкладка — только синяя.
            Button button = MakeButton(rect, Kit("pause_tab_off"), Kit("pause_tab_hover"), Kit("pause_tab_hover"));
            Label(Stretch("Label", rect, 0f), text, _bold, 23f, Idle, TextAlignmentOptions.Center, 20f, true);
            var hover = rect.gameObject.AddComponent<UiHoverMotion>();
            hover.HoverScale = 1.02f;
            return button;
        }

        static CanvasGroup Page(RectTransform panel, string name, bool shown)
        {
            RectTransform page = Stretch(name, panel, 0f);
            page.gameObject.SetActive(shown);
            return page.gameObject.AddComponent<CanvasGroup>();
        }

        static RectTransform Row(RectTransform page, int index, string title, string icon)
        {
            RectTransform row = Node(title, page, TopCenter, TopCenter, TopCenter, new Vector2(0f, -112f - index * 86f), new Vector2(930f, 76f));
            Img(row, Kit("pause_row"), Color.white).raycastTarget = true;
            RectTransform highlight = Stretch("Hover", row, -5f);
            var glow = Img(highlight, Kit("pause_row_hover"), Color.white);
            var hover = row.gameObject.AddComponent<UiHoverMotion>();
            hover.Highlight = glow;
            hover.HoverScale = 1.004f;
            hover.PressScale = 1f;
            IconImage(Node("Icon", row, LeftMiddle, LeftMiddle, Center, new Vector2(52f, 0f), new Vector2(38f, 38f)), icon, NavyInk);
            Label(Node("Label", row, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(88f, 0f), new Vector2(380f, 44f)),
                title, _semibold, 22f, NavyInk, TextAlignmentOptions.MidlineLeft);
            return row;
        }

        static RectTransform Control(RectTransform row, float width)
            => Node("Control", row, RightMiddle, RightMiddle, RightMiddle, new Vector2(-24f, 0f), new Vector2(width, 50f));

        // ---- управление ----
        static void BuildControls(Transform parent, PauseMenuView view)
        {
            RectTransform panel = Panel("Controls Panel", parent, new Vector2(248f, 396f), new Vector2(1016f, 836f));
            view.ControlsPanel = panel;

            RectTransform title = Node("Title", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -30f), new Vector2(320f, 62f));
            Img(title, Kit("pause_tab_on"), Color.white);
            Label(Stretch("Label", title, 0f), "Управление", _bold, 23f, Color.white, TextAlignmentOptions.Center, 20f, true);

            view.AbilityLayout = Segmented(Control(Row(panel, 0, "Движение", "menu_controls"), 340f), 2);
            view.ControlsHint = Label(Node("Hint", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -198f), new Vector2(920f, 52f)),
                "", _regular, 19f, Idle, TextAlignmentOptions.MidlineLeft);
            view.ControlsHint.textWrappingMode = TextWrappingModes.Normal;

            RectTransform scroll = Node("Bindings", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -258f), new Vector2(950f, 460f));
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 40f;
            RectTransform viewport = Stretch("Viewport", scroll, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Img(viewport, null, new Color(1f, 1f, 1f, 0f)).raycastTarget = true;
            RectTransform content = Node("Content", viewport, TopCenter, TopCenter, TopCenter, Vector2.zero, new Vector2(930f, 0f));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.sizeDelta = new Vector2(-20f, 0f);
            var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.padding = new RectOffset(0, 0, 6, 6);
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            view.BindingsContent = content;
            view.BindingTemplate = BindingRow(content);

            view.ControlsReset = FooterButton(panel, "Reset", new Vector2(-27f, 24f), "Сбросить", "menu_reset", false);
            view.ControlsBack = BackButton(panel);
            view.ControlsBack.gameObject.SetActive(true);
            panel.gameObject.SetActive(false);
        }

        static UiKeyBindingRow BindingRow(RectTransform content)
        {
            RectTransform row = Node("Binding Template", content, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(930f, 60f));
            Img(row, Kit("pause_row"), Color.white).raycastTarget = true;
            Layout(row, -1f, 60f);
            var binding = row.gameObject.AddComponent<UiKeyBindingRow>();
            binding.Action = Label(Node("Action", row, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(40f, 0f), new Vector2(600f, 40f)),
                "Действие", _semibold, 21f, NavyInk, TextAlignmentOptions.MidlineLeft);

            RectTransform key = Node("Key", row, RightMiddle, RightMiddle, RightMiddle, new Vector2(-24f, 0f), new Vector2(170f, 48f));
            RectTransform waiting = Stretch("Waiting", key, -16f);
            var glow = Img(waiting, Kit("pause_keycap_glow"), Color.white);
            waiting.gameObject.AddComponent<UiPulse>().Graphic = glow;
            waiting.gameObject.SetActive(false);
            binding.Waiting = waiting.gameObject;
            binding.Key = MakeButton(key, Kit("pause_keycap"), Kit("pause_keycap_glow"), Kit("pause_keycap"));
            waiting.SetAsLastSibling();
            binding.KeyLabel = Label(Stretch("Label", key, 0f), "Q", _bold, 21f, Color.white, TextAlignmentOptions.Center, 4f);
            key.gameObject.AddComponent<UiHoverMotion>();
            return binding;
        }

        // ---- подтверждение ----
        static void BuildConfirm(Transform parent, PauseMenuView view)
        {
            RectTransform panel = Node("Confirm Panel", parent, Center, Center, Center, Vector2.zero, new Vector2(720f, 440f));
            Img(panel, Kit("pause_panel"), Color.white);
            panel.gameObject.AddComponent<CanvasGroup>();
            view.ConfirmPanel = panel;
            RectTransform ribbon = Node("Title Ribbon", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -30f), new Vector2(560f, 98f));
            Img(ribbon, Kit("pause_ribbon"), Color.white);
            view.ConfirmTitle = Label(Stretch("Title", ribbon, 0f), "Выйти из игры?", _bold, 30f, Color.white, TextAlignmentOptions.Center, 12f, true);
            view.ConfirmTitle.enableAutoSizing = true;
            view.ConfirmTitle.fontSizeMin = 18f;
            view.ConfirmTitle.fontSizeMax = 30f;
            view.ConfirmTitle.margin = new Vector4(110f, 0f, 110f, 6f);
            view.ConfirmText = Label(Node("Text", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -150f), new Vector2(600f, 96f)),
                "", _semibold, 24f, Cream, TextAlignmentOptions.Center);
            view.ConfirmText.textWrappingMode = TextWrappingModes.Normal;
            view.ConfirmCountdown = Label(Node("Countdown", panel, TopCenter, TopCenter, TopCenter, new Vector2(0f, -256f), new Vector2(600f, 34f)),
                "", _regular, 20f, Muted, TextAlignmentOptions.Center);

            view.ConfirmYes = FooterButton(panel, "Yes", Vector2.zero, "Выйти", "menu_apply", true);
            Place((RectTransform)view.ConfirmYes.transform, BottomCenter, new Vector2(-150f, 40f), new Vector2(270f, 76f));
            view.ConfirmYesLabel = view.ConfirmYes.GetComponentInChildren<TMP_Text>();
            view.ConfirmNo = FooterButton(panel, "No", Vector2.zero, "Отмена", "menu_close", false);
            Place((RectTransform)view.ConfirmNo.transform, BottomCenter, new Vector2(150f, 40f), new Vector2(270f, 76f));
            view.ConfirmNoLabel = view.ConfirmNo.GetComponentInChildren<TMP_Text>();
            view.ConfirmNoLabel.enableAutoSizing = true;
            view.ConfirmNoLabel.fontSizeMin = 14f;
            view.ConfirmNoLabel.fontSizeMax = 22f;
            panel.gameObject.SetActive(false);
        }

        // ---- элементы управления ----
        static UiSegmented Segmented(RectTransform control, int count)
        {
            Img(control, Kit("pause_segmented"), Color.white);
            var row = control.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(16, 16, 5, 5);
            row.spacing = 0f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            var segmented = control.gameObject.AddComponent<UiSegmented>();

            RectTransform indicator = Node("Indicator", control, Center, Center, Center, Vector2.zero, new Vector2(140f, 40f));
            Img(indicator, Kit("pause_segment_on"), Color.white);
            indicator.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            segmented.Indicator = indicator;

            segmented.Options = new Button[count];
            segmented.Labels = new TMP_Text[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform option = Node("Option " + (i + 1), control, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(100f, 40f));
                var hit = Img(option, null, new Color(1f, 1f, 1f, 0f));
                hit.raycastTarget = true;
                var button = option.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                NoNavigation(button);
                segmented.Options[i] = button;
                segmented.Labels[i] = Label(Stretch("Label", option, 0f), "—", _semibold, 19f, Idle, TextAlignmentOptions.Center);
            }
            return segmented;
        }

        static UiDropdown Dropdown(RectTransform control)
        {
            var dropdown = control.gameObject.AddComponent<UiDropdown>();
            dropdown.Field = MakeButton(control, Kit("pause_dropdown"), Kit("pause_dropdown"), Kit("pause_dropdown"));
            dropdown.Field.transition = Selectable.Transition.ColorTint;
            dropdown.Value = Label(Stretch("Value", control, 0f), "1920 × 1080", _semibold, 22f, Cream, TextAlignmentOptions.MidlineLeft);
            dropdown.Value.margin = new Vector4(44f, 0f, 90f, 0f);

            // Список — отдельный вложенный Canvas: рисуется поверх строк ниже.
            RectTransform list = Node("List", control, BottomCenter, BottomCenter, TopCenter, new Vector2(0f, -6f), new Vector2(460f, 300f));
            var listCanvas = list.gameObject.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 320;
            list.gameObject.AddComponent<GraphicRaycaster>();
            list.gameObject.AddComponent<CanvasGroup>();
            Img(list, Kit("pause_tile"), Color.white).raycastTarget = true;
            dropdown.List = list;

            RectTransform scroll = Stretch("Scroll", list, 18f);
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            RectTransform viewport = Stretch("Viewport", scroll, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Img(viewport, null, new Color(1f, 1f, 1f, 0f)).raycastTarget = true;
            RectTransform content = Node("Content", viewport, TopCenter, TopCenter, TopCenter, Vector2.zero, new Vector2(0f, 0f));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            var column = content.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            dropdown.Content = content;
            dropdown.Scroll = scrollRect;

            RectTransform template = Node("Option Template", content, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(400f, 44f));
            Layout(template, -1f, 44f);
            var back = Img(template, null, new Color(1f, 1f, 1f, 0f));
            back.raycastTarget = true;
            var option = template.gameObject.AddComponent<Button>();
            option.targetGraphic = back;
            option.transition = Selectable.Transition.None;
            NoNavigation(option);
            Label(Stretch("Label", template, 0f), "1920 × 1080", _semibold, 21f, Cream, TextAlignmentOptions.Center);
            template.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            dropdown.OptionTemplate = option;
            list.gameObject.SetActive(false);
            return dropdown;
        }

        static UiToggle Toggle(RectTransform row)
        {
            RectTransform rect = Node("Toggle", row, RightMiddle, RightMiddle, RightMiddle, new Vector2(-24f, 0f), new Vector2(118f, 52f));
            var toggle = rect.gameObject.AddComponent<UiToggle>();
            var off = Img(Stretch("Off", rect, 0f), Kit("pause_toggle_off"), Color.white, false);
            off.preserveAspect = true;
            off.raycastTarget = true;
            var on = Img(Stretch("On", rect, 0f), Kit("pause_toggle_on"), Color.white, false);
            on.preserveAspect = true;
            toggle.Off = off;
            toggle.On = on;
            return toggle;
        }

        static Slider SliderControl(RectTransform row, out TMP_Text value)
        {
            RectTransform rect = Node("Slider", row, RightMiddle, RightMiddle, RightMiddle, new Vector2(-170f, 0f), new Vector2(300f, 44f));
            RectTransform background = Node("Background", rect, LeftMiddle, RightMiddle, Center, Vector2.zero, new Vector2(0f, 22f));
            Img(background, Kit("pause_slider_track"), Color.white).raycastTarget = true;
            RectTransform fillArea = Node("Fill Area", rect, LeftMiddle, RightMiddle, Center, Vector2.zero, new Vector2(-8f, 16f));
            RectTransform fill = Stretch("Fill", fillArea, 0f);
            Img(fill, Kit("pause_slider_fill"), Color.white);
            RectTransform slideArea = Stretch("Handle Slide Area", rect, 0f);
            slideArea.offsetMin = new Vector2(18f, 0f);
            slideArea.offsetMax = new Vector2(-18f, 0f);
            RectTransform handle = Node("Handle", slideArea, Center, Center, Center, Vector2.zero, new Vector2(40f, 0f));
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var knob = Img(handle, Kit("pause_knob"), Color.white, false);
            knob.raycastTarget = true;
            knob.preserveAspect = true;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;
            NoNavigation(slider);
            var grow = handle.gameObject.AddComponent<UiHoverMotion>();
            grow.HoverScale = 1.12f;
            grow.PressScale = 1.2f;

            value = Label(Node("Value", row, RightMiddle, RightMiddle, RightMiddle, new Vector2(-24f, 0f), new Vector2(130f, 40f)),
                "100%", _semibold, 22f, NavyInk, TextAlignmentOptions.MidlineRight);
            return slider;
        }

        // ---- общие части ----
        static RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform panel = Node(name, parent, Center, Center, TopCenter, position, size);
            Img(panel, Kit("pause_panel"), Color.white).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            return panel;
        }

        static Button FooterButton(RectTransform panel, string name, Vector2 position, string text, string icon, bool coral)
        {
            RectTransform rect = Node(name, panel, BottomRight, BottomRight, BottomRight, position, new Vector2(coral ? 264f : 250f, 72f));
            Button button = coral
                ? MakeButton(rect, Kit("pause_button_coral"), Kit("pause_button_coral_hover"), Kit("pause_button_coral_pressed"))
                : MakeButton(rect, Kit("pause_button"), Kit("pause_button_hover"), Kit("pause_button_pressed"));
            IconImage(Node("Icon", rect, LeftMiddle, LeftMiddle, Center, new Vector2(54f, 0f), new Vector2(32f, 32f)), icon, coral ? Color.white : Cream);
            Label(Stretch("Label", rect, 0f), text, _bold, 21f, coral ? Color.white : Cream, TextAlignmentOptions.MidlineLeft, 10f, true)
                .margin = new Vector4(86f, 0f, 20f, 0f);
            rect.gameObject.AddComponent<UiHoverMotion>();
            return button;
        }

        static Button BackButton(RectTransform panel)
        {
            RectTransform rect = Node("Back", panel, BottomLeft, BottomLeft, BottomLeft, new Vector2(27f, 24f), new Vector2(220f, 72f));
            Button button = MakeButton(rect, Kit("pause_button"), Kit("pause_button_hover"), Kit("pause_button_pressed"));
            RectTransform arrow = Node("Icon", rect, LeftMiddle, LeftMiddle, Center, new Vector2(52f, 0f), new Vector2(30f, 30f));
            IconImage(arrow, "menu_level_up", Cream);
            arrow.localEulerAngles = new Vector3(0f, 0f, 90f);
            Label(Stretch("Label", rect, 0f), "Назад", _bold, 21f, Cream, TextAlignmentOptions.MidlineLeft, 10f, true).margin = new Vector4(82f, 0f, 16f, 0f);
            rect.gameObject.AddComponent<UiHoverMotion>();
            rect.gameObject.SetActive(false);
            return button;
        }

        static Button MakeButton(RectTransform rect, Sprite normal, Sprite hover, Sprite pressed)
        {
            // Явная проверка: в редакторе GetComponent отдаёт «фальшивый null», и ?? его не видит.
            Image image = rect.GetComponent<Image>();
            if (image == null) image = Img(rect, normal, Color.white);
            image.sprite = normal;
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = pressed, selectedSprite = normal, disabledSprite = normal };
            NoNavigation(button);
            return button;
        }

        /// <summary>Без клавиатурной навигации: стрелки и Escape у игры свои.</summary>
        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        static Image IconImage(RectTransform rect, string icon, Color tint)
        {
            Image image = Img(rect, Icon(icon), tint, false);
            image.preserveAspect = true;
            return image;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static TextMeshProUGUI Label(RectTransform rect, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment, float spacing = 0f, bool upper = false)
        {
            TextMeshProUGUI label = Text(rect, text, font, size, color, alignment, spacing);
            if (upper) label.fontStyle = FontStyles.UpperCase;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }
    }
}
