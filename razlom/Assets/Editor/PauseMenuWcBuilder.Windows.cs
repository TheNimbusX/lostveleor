using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class PauseMenuWcBuilder
    {
        // «Настройки Б» (выбор владельца 30.09, ART/UI/concepts-2026-09-30-hud-final/e2-settings-tabs-left.png):
        // одно окно во всю ширину — вкладки столбцом слева со значками, опции в центре, описание справа.
        // 1380 × 800 влезает в холст 1440 × 900 — это 16:10 при масштабе интерфейса 120%, самый тесный случай.
        const float WindowW = 1380f, WindowH = 800f;
        const float TabsX = 24f, TabsTop = 150f, TabStep = 82f, TabW = 300f, TabH = 70f;
        const float PageX = 372f, PageTop = 36f, PageW = 640f, PageH = 650f;
        const float RowH = 60f, RowStep = 74f, ControlW = 330f;
        const float InfoX = 1056f, InfoW = 290f;
        /// <summary>Подсказка под вкладками — формат листа 5 «[Клавиша] Действие» (UiKeyHint).</summary>
        const string TabsHintText = "[Tab] [Shift+Tab] Вкладки";

        /// <summary>
        /// Окно «Дыма и света»: глубокий дым шире окна вместо панели пака, под ним мягкое глубокое пятно,
        /// прозрачный ловец мыши, своя группа появления в темпе паузы. Дети кладутся в «Содержимое».
        /// </summary>
        static RectTransform InkWindow(RectTransform root, string name, Vector2 position, Vector2 size)
        {
            RectTransform panel = Box(Node(name, root), Center, Center, position, size);
            panel.gameObject.AddComponent<CanvasGroup>();
            RectTransform content = Stretch(Node("Содержимое", panel));
            // Нить по низу подложки не нужна: у окна своя линия футера, лишний огонь владелец просил убрать.
            Object.DestroyImmediate(UiInkKit.Plate(panel).gameObject);
            // Под рваным дымом подложки — мягкое глубокое пятно без краёв: у краёв между клубами иначе
            // просвечивал мир.
            UiInkKit.SmokeLayer(panel, "Глубина", "soft_blot", .85f, size.x * .2f, size.y * .28f, deep: true).transform.SetAsFirstSibling();
            UiInkKit.HitArea(panel);
            Pace(UiInkKit.Group(panel, UiInkGroup.Sweep.TopToBottom));
            return content;
        }

        /// <summary>Нить света по низу узла, во всю ширину без <paramref name="inset"/> с краёв.</summary>
        static Image Thread(RectTransform parent, string name, float inset, float y, float height, float strength, float delay = .15f)
        {
            RectTransform rect = Node(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(inset, y - height * .5f);
            rect.offsetMax = new Vector2(-inset, y + height * .5f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiInkKit.Sprite("light_thread");
            image.raycastTarget = false;
            image.material = UiInkKit.Light;
            image.color = new Color(1f, 1f, 1f, strength);
            UiInkKit.Inked(image, delay: delay);
            return image;
        }

        /// <summary>Картинка пака (круг, шеврон) в материале «Дыма и света»: проявляется вместе с окном.</summary>
        static Image Plain(Image image)
        {
            image.material = UiInkKit.Plain;
            UiInkKit.Inked(image, delay: .1f);
            return image;
        }

        /// <summary>Узел от левого верхнего угла родителя.</summary>
        static RectTransform Corner(RectTransform parent, string name, float x, float y, float w, float h) => TopLeft(Node(name, parent), x, y, w, h);

        /// <summary>Тонкая вертикальная нить-разделитель колонок, как на кадре «Настройки Б».</summary>
        static void ColumnLine(RectTransform content, string name, float x)
        {
            RectTransform line = UiInkKit.Divider(content, name, PageH - 10f, false, .3f);
            line.anchorMin = line.anchorMax = new Vector2(0f, 1f);
            line.pivot = new Vector2(.5f, .5f);
            line.anchoredPosition = new Vector2(x, -(PageTop + PageH * .5f));
            line.localEulerAngles = new Vector3(0f, 0f, 90f);
        }

        // ---------------------------------------------------------------- настройки

        /// <summary>
        /// Окно настроек «Настройки Б»: столбец вкладок, опции, описание, футер «Esc Назад · F Сбросить ·
        /// Enter Применить». Строки подписаны из SettingsCatalog — одно место для названий и описаний.
        /// </summary>
        static RectTransform BuildSettings(RectTransform root, PauseMenuView view)
        {
            RectTransform content = InkWindow(root, "Настройки", Vector2.zero, new Vector2(WindowW, WindowH));
            view.SettingsPanel = (RectTransform)content.parent;
            view.HidePauseUnderWindow = true;

            // Заголовок окна и линия под ним — как на кадре.
            TMP_Text title = UiInkKit.Label(Corner(content, "Заголовок", 40f, 30f, 300f, 76f), "Надпись", "Настройки", FontRole.Heading, 58f, Role.Text,
                TextAlignmentOptions.MidlineLeft, 1f, 1f, .05f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Box(UiInkKit.Divider(content, "Линия под заголовком", 280f, true, .6f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(44f, -120f), new Vector2(280f, 16f));

            // Вкладки столбцом: выбранную отмечает переезжающая полоса дыма, огненное кольцо вокруг значка и огонёк слева.
            RectTransform indicator = Corner(content, "Выбранная вкладка", TabsX, TabsTop, TabW, TabH);
            UiInkKit.SmokeLayer(indicator, "Подложка", "smoke_plate", .85f, 22f, 6f);
            UiInkKit.LightAt(indicator, "Кольцо", "light_ring", new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(78f, 78f), .9f);
            UiInkKit.LightAt(indicator, "Свет огонька", "light_glow", new Vector2(0f, .5f), new Vector2(2f, 0f), new Vector2(30f, 30f), .8f);
            Plain(Mark(indicator, "Огонёк", T.CircleFill, Role.Accent, 1f, new Vector2(0f, .5f), new Vector2(2f, 0f), 10f));
            view.TabIndicator = indicator;
            view.TabGraphics = TabButton(content, 0, CombatHudBuilder.Icon("menu_screen"));
            view.TabAudio = TabButton(content, 1, CombatHudBuilder.Icon("menu_sound"));
            view.TabGame = TabButton(content, 2, CombatHudBuilder.Icon("menu_play"));
            view.TabInterface = TabButton(content, 3, CombatHudBuilder.Icon("menu_ui_scale"));
            view.TabControls = TabButton(content, 4, CombatHudBuilder.Icon("menu_settings"));
            TMP_Text tabsHint = UiInkKit.Label(Corner(content, "Подсказка вкладок", 40f, TabsTop + 5f * TabStep + 4f, 290f, 30f), "Надпись",
                TabsHintText, FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.TextMuted, TextAlignmentOptions.MidlineLeft, 0f, .8f);
            tabsHint.textWrappingMode = TextWrappingModes.NoWrap;
            // Заметка футера — в столбце вкладок: справа ей тесно рядом с тремя кнопками.
            view.FooterNote = UiInkKit.Label(Corner(content, "Заметка", 40f, WindowH - 104f, 290f, 70f), "Надпись", "Изменения сохраняются сразу",
                FontRole.Body, 15f, Role.TextMuted, TextAlignmentOptions.BottomLeft);

            ColumnLine(content, "Линия слева", PageX - 20f);
            ColumnLine(content, "Линия справа", InfoX - 22f);

            RectTransform options = Corner(content, "Опции", PageX, PageTop, PageW, PageH);
            BuildDisplayPage(options, view);
            BuildAudioPage(options, view);
            BuildGamePage(options, view);
            BuildInterfacePage(options, view);
            BuildControlsPage(options, view);

            BuildDescription(content, view);
            BuildFooter(content, view);
            view.SettingsPanel.gameObject.SetActive(false);
            return view.SettingsPanel;
        }

        static void BuildDisplayPage(RectTransform options, PauseMenuView view)
        {
            view.GraphicsPage = Page(options, "Изображение");
            RectTransform page = (RectTransform)view.GraphicsPage.transform;
            view.DisplayMode = Segmented(Control(Row(page, 0, SettingId.DisplayMode, ControlW), ControlW), 3);
            view.Resolution = Dropdown(Control(Row(page, 1, SettingId.Resolution, ControlW), ControlW));
            view.Quality = Segmented(Control(Row(page, 2, SettingId.Quality, ControlW), ControlW), 3);
            view.VSync = Toggle(Row(page, 3, SettingId.VSync, 48f));
            RectTransform limitRow = Row(page, 4, SettingId.FrameLimit, ControlW);
            view.FrameLimitRow = limitRow.gameObject.AddComponent<CanvasGroup>();
            view.FrameLimit = SliderControl(limitRow, out view.FrameLimitValue);
            view.Shadows = Segmented(Control(Row(page, 5, SettingId.Shadows, ControlW), ControlW), 3);
            view.Brightness = SliderControl(Row(page, 6, SettingId.Brightness, ControlW), out view.BrightnessValue);
        }

        static void BuildAudioPage(RectTransform options, PauseMenuView view)
        {
            view.AudioPage = Page(options, "Звук");
            RectTransform page = (RectTransform)view.AudioPage.transform;
            view.Master = SliderControl(Row(page, 0, SettingId.MasterVolume, ControlW), out view.MasterValue);
            view.Effects = SliderControl(Row(page, 1, SettingId.EffectsVolume, ControlW), out view.EffectsValue);
            view.Music = SliderControl(Row(page, 2, SettingId.MusicVolume, ControlW), out view.MusicValue);
            view.InterfaceVolume = SliderControl(Row(page, 3, SettingId.InterfaceVolume, ControlW), out view.InterfaceVolumeValue);
            view.SoundInBackground = Toggle(Row(page, 4, SettingId.SoundInBackground, 48f));
        }

        static void BuildGamePage(RectTransform options, PauseMenuView view)
        {
            view.GamePage = Page(options, "Игра");
            RectTransform page = (RectTransform)view.GamePage.transform;
            view.Language = Dropdown(Control(Row(page, 0, SettingId.Language, ControlW), ControlW));
            view.PauseOnFocusLoss = Toggle(Row(page, 1, SettingId.PauseOnFocusLoss, 48f));
            view.DamageNumbers = Toggle(Row(page, 2, SettingId.DamageNumbers, 48f));
            view.EnemyBars = Segmented(Control(Row(page, 3, SettingId.EnemyBars, ControlW), ControlW), 3);
        }

        static void BuildInterfacePage(RectTransform options, PauseMenuView view)
        {
            view.InterfacePage = Page(options, "Интерфейс и доступность");
            RectTransform page = (RectTransform)view.InterfacePage.transform;
            view.UiScale = SliderControl(Row(page, 0, SettingId.UiScale, ControlW), out view.UiScaleValue);
            view.ScreenShake = SliderControl(Row(page, 1, SettingId.ScreenShake, ControlW), out view.ScreenShakeValue);
            view.Flashes = Segmented(Control(Row(page, 2, SettingId.Flashes, ControlW), ControlW), 2);
        }

        /// <summary>
        /// Вкладка столбца: значок в круге дыма с тонким кольцом и подпись антиквой (длинная — в две строки).
        /// Цвет подписи и значка ведёт PauseMenuView (TabTextOn/Off); тема его не перебивает.
        /// </summary>
        static Button TabButton(RectTransform content, int index, Sprite icon)
        {
            string text = SettingsCatalog.TabTitle((SettingsTab)index);
            RectTransform tab = Corner(content, "Вкладка " + text, TabsX, TabsTop + index * TabStep, TabW, TabH);
            Button button = HitButton(tab);
            RectTransform circle = At(Node("Круг", tab), new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(58f, 58f));
            UiInkKit.SmokeLayer(circle, "Дым", "smoke_blot_2", 1f, 8f, 8f);
            Plain(Layer(circle, "Кольцо", T.CircleFrame, Role.PanelLine, .5f));
            Image mark = Plain(Mark(tab, PauseMenuView.TabIconName, icon, Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(40f, 0f), 30f));
            Object.DestroyImmediate(mark.GetComponent<ThemeColor>());
            mark.color = T.TextMuted;

            RectTransform caption = Corner(tab, "Подпись", 84f, 0f, TabW - 88f, TabH);
            TMP_Text label = UiInkKit.Label(caption, "Надпись", text, FontRole.Heading, 26f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, .5f, 1f, .1f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 26f;
            label.lineSpacing = -12f;
            Object.DestroyImmediate(label.GetComponent<ThemeColor>());
            label.color = T.TextMuted;
            var motion = tab.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.03f;
            motion.SilentClick = true;
            return button;
        }

        static CanvasGroup Page(RectTransform area, string name)
        {
            RectTransform page = Stretch(Node("Страница " + name, area));
            return page.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>
        /// Строка опции: подпись слева, элемент справа (<paramref name="controlWidth"/>); в покое без подложки.
        /// Наведение — полоса дыма светлее окна и нить света по низу (UiHoverMotion); выбранную строку —
        /// ту, что описана справа, — отмечает огонёк-круг слева (UiSettingRow.Selected). Подпись и описание —
        /// из SettingsCatalog.
        /// </summary>
        static RectTransform Row(RectTransform page, int index, SettingId id, float controlWidth)
        {
            string title = SettingsCatalog.Title(id);
            RectTransform row = Box(Node(title, page), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -index * RowStep), new Vector2(PageW, RowH));
            UiInkKit.HitArea(row);
            RectTransform hover = Stretch(Node("Наведение", row));
            var group = hover.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            UiInkKit.SmokeLayer(hover, "Дым", "smoke_plate", .85f, 24f, 6f);
            Thread(hover, "Нить", 50f, 0f, 26f, .5f);
            var motion = row.gameObject.AddComponent<UiHoverMotion>();
            motion.HighlightGroup = group;
            motion.PulseMin = .7f;
            motion.HoverScale = 1f;
            motion.PressScale = 1f;
            motion.HoverSound = false;
            motion.SilentClick = true;

            RectTransform selected = Node("Выбрано", row);
            selected.anchorMin = selected.anchorMax = new Vector2(0f, .5f);
            selected.sizeDelta = new Vector2(30f, 30f);
            selected.anchoredPosition = new Vector2(16f, 0f);
            UiInkKit.LightAt(selected, "Свет", "light_glow", Center, Vector2.zero, new Vector2(30f, 30f), .8f, delay: .05f);
            Plain(Mark(selected, "Огонёк", T.CircleFill, Role.Accent, 1f, Center, Vector2.zero, 10f));
            selected.gameObject.SetActive(false);

            float labelWidth = PageW - 40f - controlWidth - 28f;
            RectTransform label = Box(Node("Подпись", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(labelWidth, 40f));
            TMP_Text text = UiInkKit.Label(label, "Надпись", title, FontRole.Body, 21f, Role.Text);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 15f;
            text.fontSizeMax = 21f;

            var setting = row.gameObject.AddComponent<UiSettingRow>();
            setting.Setting = id;
            setting.Selected = selected.gameObject;
            return row;
        }

        static RectTransform Control(RectTransform row, float width)
            => Box(Node("Элемент", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-16f, 0f), new Vector2(width, 42f));

        // ---------------------------------------------------------------- описание и футер

        /// <summary>
        /// Панель описания справа: заголовок опции антиквой, линия с огоньком, одна мысль про опцию,
        /// «Стандартно: …», образец яркости (только у «Яркости») и строка состояния — она вне группы
        /// описания, чтобы не мигать при каждой смене опции.
        /// </summary>
        static void BuildDescription(RectTransform content, PauseMenuView view)
        {
            RectTransform info = Corner(content, "Описание", InfoX, PageTop, InfoW, 480f);
            view.Description = info.gameObject.AddComponent<CanvasGroup>();
            view.Description.blocksRaycasts = false;
            view.DescriptionTitle = UiInkKit.Label(Corner(info, "Заголовок", 0f, 0f, InfoW, 88f), "Надпись", "Качество графики", FontRole.Heading, 34f, Role.Text,
                TextAlignmentOptions.BottomLeft, .5f, 1f, .1f);
            view.DescriptionTitle.enableAutoSizing = true;
            view.DescriptionTitle.fontSizeMin = 24f;
            view.DescriptionTitle.fontSizeMax = 34f;
            Box(UiInkKit.Divider(info, "Линия", InfoW, true, .55f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(0f, -104f), new Vector2(InfoW, 16f));
            view.DescriptionText = UiInkKit.Label(Corner(info, "Текст", 0f, 124f, InfoW, 196f), "Надпись", "", FontRole.Body, 19f, Role.TextMuted,
                TextAlignmentOptions.TopLeft);
            view.DescriptionText.enableAutoSizing = true;
            view.DescriptionText.fontSizeMin = 15f;
            view.DescriptionText.fontSizeMax = 19f;
            view.DescriptionDefault = UiInkKit.Label(Corner(info, "Стандартно", 0f, 328f, InfoW, 30f), "Надпись", "", FontRole.Body, 16f, Role.TextMuted,
                TextAlignmentOptions.MidlineLeft, 0f, .75f);
            view.DescriptionDefault.textWrappingMode = TextWrappingModes.NoWrap;

            // Образец яркости: круги на чёрном — от почти чёрного к тёмно-серому. Цвет ставит PauseMenu
            // по выбранной яркости (интерфейс рисуется после постобработки, сам образец она не трогает).
            RectTransform sample = Corner(info, "Образец яркости", 0f, 372f, InfoW, 100f);
            RectTransform plate = Corner(sample, "Подложка", 0f, 0f, InfoW, 64f);
            var back = plate.gameObject.AddComponent<Image>();
            back.sprite = T.PillFill;
            back.type = T.PillFill != null && T.PillFill.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            back.color = Color.black;
            back.raycastTarget = false;
            view.BrightnessSwatches = new Image[5];
            for (int i = 0; i < view.BrightnessSwatches.Length; i++)
            {
                Image swatch = Mark(plate, "Круг " + (i + 1), T.CircleFill, Role.Text, 1f, new Vector2(0f, .5f), new Vector2(37f + i * 54f, 0f), 40f);
                Object.DestroyImmediate(swatch.GetComponent<ThemeColor>());
                float level = new[] { .035f, .06f, .1f, .15f, .23f }[i];
                swatch.color = new Color(level, level, level, 1f);
                view.BrightnessSwatches[i] = swatch;
            }
            TMP_Text caption = UiInkKit.Label(Corner(sample, "Подпись", 0f, 70f, InfoW, 26f), "Надпись", "Левый круг — едва различим", FontRole.Body, 15f,
                Role.TextMuted, TextAlignmentOptions.MidlineLeft, 0f, .8f);
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            view.BrightnessSample = sample;
            sample.gameObject.SetActive(false);

            view.DescriptionStatus = UiInkKit.Label(Corner(content, "Состояние", InfoX, PageTop + 500f, InfoW, 140f), "Надпись", "", FontRole.Body, 17f, Role.Accent,
                TextAlignmentOptions.TopLeft);
            view.DescriptionStatus.enableAutoSizing = true;
            view.DescriptionStatus.fontSizeMin = 14f;
            view.DescriptionStatus.fontSizeMax = 17f;
        }

        /// <summary>
        /// Футер как на кадре и как подсказки паузы: «Esc Назад», «F Сбросить», «Enter Применить» —
        /// клавиша капсулой прямо в кнопке. «Назад» видна всегда: паузы рядом нет.
        /// </summary>
        static void BuildFooter(RectTransform content, PauseMenuView view)
        {
            float lineWidth = WindowW - PageX - 40f;
            Box(UiInkKit.Divider(content, "Линия футера", lineWidth, false, .35f), new Vector2(0f, 0f), new Vector2(0f, .5f), new Vector2(PageX, 106f), new Vector2(lineWidth, 16f));

            RectTransform footer = Box(Node("Кнопки", content), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 26f), new Vector2(WindowW - PageX - 40f, 56f));
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            // Мазки кнопок шире самих кнопок: зазор, чтобы соседние мазки не слипались.
            row.spacing = 44f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            view.SettingsBack = FooterButton(footer, false, "Назад", "Esc", 210f);
            view.SettingsBack.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            view.SettingsBack.gameObject.AddComponent<UiHint>().Text = "Назад к паузе, из главного меню — в меню. Клавиша Esc.";
            view.Reset = FooterButton(footer, false, "Сбросить", "F", 230f);
            view.Reset.gameObject.AddComponent<UiHint>().Text = "Вернуть стандартные значения этой вкладки — после вопроса. Клавиша F.";
            view.Apply = FooterButton(footer, true, "Применить", "Enter", 260f);
            view.Apply.gameObject.AddComponent<UiHint>().Text = "Применить режим экрана и разрешение — с проверкой 15 секунд. Клавиша Enter.";
        }

        /// <summary>Кнопка футера с клавишей-капсулой слева от подписи; подпись центрируется в оставшемся месте.</summary>
        static Button FooterButton(RectTransform footer, bool primary, string text, string key, float width)
        {
            RectTransform button = UiInkKit.Button(footer, text, text, primary, new Vector2(width, T.ButtonHeight), T.Size(UiTheme.TextStep.Heading));
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = T.ButtonHeight;
            // Кейкап на кнопке — общий (лист 5): «[Esc] Назад».
            UiInkKit.ButtonKey(button, key, 30f);
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }

        // ---------------------------------------------------------------- элементы

        /// <summary>
        /// Переключатель положений: дорожка — тёмный дым, выбранное положение — оранжевый мазок кистью
        /// (как основная кнопка); мазок переезжает под выбор (UiSegmented).
        /// </summary>
        static UiSegmented Segmented(RectTransform control, int count)
        {
            UiInkKit.SmokeLayer(control, "Дорожка", "smoke_plate", .95f, 40f, 16f, Role.Track);
            RectTransform indicator = Node("Выбор", control);
            indicator.sizeDelta = new Vector2(120f, 34f);
            // Мазок сужается к правому краю: сдвинут вправо, чтобы надпись легла на толстую часть.
            Image paint = UiInkKit.StrokeLayer(indicator, "Мазок", "brush_stroke_2", Role.Accent, .92f, .08f);
            paint.rectTransform.offsetMin = new Vector2(-16f, -16f);
            paint.rectTransform.offsetMax = new Vector2(24f, 16f);
            indicator.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var row = control.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(4, 4, 4, 4);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            foreach (Transform child in control)
                if (child.GetComponent<LayoutElement>() == null) child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var segmented = control.gameObject.AddComponent<UiSegmented>();
            segmented.Indicator = indicator;
            segmented.SelectedText = T.TextOnAccent;
            segmented.IdleText = T.TextMuted;
            segmented.Options = new Button[count];
            segmented.Labels = new TMP_Text[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform option = Node("Вариант " + (i + 1), control);
                segmented.Options[i] = HitButton(option);
                segmented.Labels[i] = UiInkKit.Label(option, "Надпись", "—", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .15f);
                segmented.Labels[i].textWrappingMode = TextWrappingModes.NoWrap;
                segmented.Labels[i].enableAutoSizing = true;
                segmented.Labels[i].fontSizeMin = 13f;
                segmented.Labels[i].fontSizeMax = 17f;
                // Цвет подписи ведёт UiSegmented; тема его не перебивает.
                Object.DestroyImmediate(segmented.Labels[i].GetComponent<ThemeColor>());
            }
            return segmented;
        }

        /// <summary>
        /// Выпадающий список: поле — тёмный дым с шевроном, наведение — нить света по низу. Список —
        /// вложенный Canvas поверх строк ниже: глубокий дым (UiInkKit.Plate) и своя быстрая группа
        /// появления. Выбранный пункт — полоса дыма и огонёк («Выбрано», его включает UiDropdown).
        /// </summary>
        static UiDropdown Dropdown(RectTransform control)
        {
            float width = control.sizeDelta.x;
            var dropdown = control.gameObject.AddComponent<UiDropdown>();
            Image hit = UiInkKit.HitArea(control);
            UiInkKit.SmokeLayer(control, "Поле", "smoke_plate", .95f, 40f, 16f, Role.Track);
            var button = control.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            dropdown.Field = button;
            var motion = control.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = Thread(control, "Наведение", 30f, -2f, 26f, .7f);
            motion.HoverScale = 1f;
            dropdown.Value = UiInkKit.Label(control, "Значение", "1920 × 1080", FontRole.Body, 19f, Role.Text);
            dropdown.Value.margin = new Vector4(18f, 0f, 44f, 0f);
            dropdown.Value.textWrappingMode = TextWrappingModes.NoWrap;
            Plain(Mark(control, "Шеврон", T.ChevronDown, Role.Text, .8f, new Vector2(1f, .5f), new Vector2(-22f, 0f), 16f));

            // Список — вложенный Canvas: рисуется поверх строк ниже. Каналы uv1/uv2 — и ему: шейдер дыма
            // берёт из них данные элемента. Ниже поля на 30: дым списка шире его самого.
            RectTransform list = Box(Node("Список", control), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(width, 280f));
            var listCanvas = list.gameObject.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 320;
            listCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            list.gameObject.AddComponent<GraphicRaycaster>();
            list.gameObject.AddComponent<CanvasGroup>();
            UiInkKit.Plate(list, small: true);
            // Клик по дыму списка не должен проваливаться в строки под ним.
            UiInkKit.HitArea(list);
            UiInkGroup listGroup = UiInkKit.Group(list, UiInkGroup.Sweep.TopToBottom, .2f, .1f);
            listGroup.Burn = 0f;
            dropdown.List = list;

            RectTransform scroll = Stretch(Node("Прокрутка", list), 8f);
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            RectTransform viewport = Stretch(Node("Окно", scroll));
            viewport.gameObject.AddComponent<RectMask2D>();
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(1f, 1f, 1f, 0f);
            RectTransform listContent = Node("Пункты", viewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            var column = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = listContent;
            dropdown.Content = listContent;
            dropdown.Scroll = scrollRect;

            RectTransform template = Node("Шаблон пункта", listContent);
            var element = template.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 40f;
            Image back = UiInkKit.HitArea(template);
            var option = template.gameObject.AddComponent<UnityEngine.UI.Button>();
            option.targetGraphic = back;
            option.transition = Selectable.Transition.None;
            NoNavigation(option);
            // Внутри маски прокрутки дым не шире пункта: клубы соседних пунктов не должны резаться краем.
            RectTransform chosen = Stretch(Node("Выбрано", template));
            UiInkKit.SmokeLayer(chosen, "Дым", "smoke_plate", .9f, 0f, 2f);
            UiInkKit.LightAt(chosen, "Огонёк", "light_glow", new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(22f, 22f), .9f, delay: .1f);
            chosen.gameObject.SetActive(false);
            UiInkKit.Label(template, "Надпись", "1920 × 1080", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text, TextAlignmentOptions.Center, 0f, 1f, .08f);
            var optionMotion = template.gameObject.AddComponent<UiHoverMotion>();
            optionMotion.Highlight = Thread(template, "Наведение", 40f, 0f, 22f, .6f, .1f);
            optionMotion.HoverScale = 1f;
            dropdown.OptionTemplate = option;
            // Выбор показывает «Выбрано»; ловец мыши остаётся прозрачным.
            dropdown.OptionSelected = new Color(1f, 1f, 1f, 0f);
            dropdown.OptionIdle = new Color(1f, 1f, 1f, 0f);
            list.gameObject.SetActive(false);
            return dropdown;
        }

        /// <summary>
        /// Тумблер — круг (владелец 26 сентября: одна фигура — круг): тонкое кольцо в клубе дыма; включённый —
        /// внутри разгорается тёплый огонёк со светом (группа «Вкл», её ведёт UiToggle), выключенный — тёмная точка.
        /// Щелчок по всей строке тоже переключает (UiSettingRow.Toggle).
        /// </summary>
        static UiToggle Toggle(RectTransform row)
        {
            RectTransform rect = Box(Node("Переключатель", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-14f, 0f), new Vector2(48f, 48f));
            var toggle = rect.gameObject.AddComponent<UiToggle>();
            UiInkKit.HitArea(rect);
            UiInkKit.SmokeLayer(rect, "Дым", "smoke_blot_2", 1f, 10f, 10f, Role.Track);
            RectTransform circle = At(Node("Круг", rect), Center, Vector2.zero, new Vector2(30f, 30f));
            Image off = Plain(Mark(circle, "Выкл", T.CircleFill, Role.Veil, .9f, Center, Vector2.zero, 12f));
            RectTransform on = Stretch(Node("Вкл", circle));
            var onGroup = on.gameObject.AddComponent<CanvasGroup>();
            // В префабе — «выкл»; настоящее значение ставит первый SetValue.
            onGroup.alpha = 0f;
            onGroup.blocksRaycasts = false;
            UiInkKit.LightAt(on, "Свет", "light_glow", Center, Vector2.zero, new Vector2(46f, 46f), .75f, delay: .12f);
            Image dot = Plain(Mark(on, "Огонёк", T.CircleFill, Role.Accent, 1f, Center, Vector2.zero, 14f));
            Plain(Layer(circle, "Кольцо", T.CircleFrame, Role.PanelLine, .55f));
            toggle.Off = off;
            toggle.On = dot;
            toggle.OnGroup = onGroup;
            toggle.Knob = null;
            UiSettingRow setting = row.GetComponent<UiSettingRow>();
            if (setting != null) setting.Toggle = toggle;
            return toggle;
        }

        /// <summary>
        /// Ползунок: дорожка — тёмный мазок кистью, заливка — тот же мазок оранжевой краской, обрезанный по
        /// значению (Filled: мазок не сжимается), ручка — маленький светлый огонёк в тёплом свечении.
        /// Число справа — в своей капсуле места, как «50%» на кадре.
        /// </summary>
        static Slider SliderControl(RectTransform row, out TMP_Text value)
        {
            RectTransform rect = Box(Node("Ползунок", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-100f, 0f), new Vector2(230f, 40f));
            // Ловец на весь ползунок: клик в любом месте дорожки двигает значение.
            UiInkKit.HitArea(rect);
            Image track = UiInkKit.StrokeLayer(rect, "Дорожка", "brush_stroke_1", Role.Track, .95f);
            track.rectTransform.offsetMin = new Vector2(-10f, 0f);
            track.rectTransform.offsetMax = new Vector2(10f, 0f);
            RectTransform fillArea = Stretch(Node("Область заливки", rect));
            fillArea.offsetMin = new Vector2(-10f, 0f);
            fillArea.offsetMax = new Vector2(10f, 0f);
            RectTransform fill = Stretch(Node("Заливка", fillArea));
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = UiInkKit.Sprite("brush_stroke_1");
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.raycastTarget = false;
            fillImage.material = UiInkKit.Stroke;
            Tint(fillImage, Role.Accent, .92f);
            UiInkKit.Inked(fillImage, new Vector2(0f, .5f), .06f);
            RectTransform area = Stretch(Node("Область ручки", rect));
            RectTransform handle = Node("Ручка", area);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(28f, 0f);
            // Свой ловец у ручки: наведение на неё увеличивает огонёк.
            UiInkKit.HitArea(handle);
            UiInkKit.LightAt(handle, "Свечение", "light_glow", Center, Vector2.zero, new Vector2(40f, 40f), .6f, delay: .15f);
            Image dot = Plain(Mark(handle, "Огонёк", T.CircleFill, Role.Text, 1f, Center, Vector2.zero, 12f));

            var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = dot;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;
            NoNavigation(slider);
            var grow = handle.gameObject.AddComponent<UiHoverMotion>();
            grow.HoverScale = 1.2f;
            grow.PressScale = 1.3f;

            RectTransform number = Box(Node("Значение", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-14f, 0f), new Vector2(70f, 40f));
            value = UiInkKit.Label(number, "Надпись", "100%", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.MidlineRight);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            return slider;
        }
    }
}
