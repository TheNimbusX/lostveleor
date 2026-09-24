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
        const float WindowW = 1000f, WindowH = 780f, RowW = 920f, RowH = 62f, RowStep = 72f;

        /// <summary>Окно пака справа от сдвинутой паузы; дети кладутся в «Содержимое».</summary>
        static RectTransform Window(RectTransform root, string name)
        {
            RectTransform panel = Place("Panel", root, name);
            Box(panel, Center, Center, new Vector2(230f, 0f), new Vector2(WindowW, WindowH));
            panel.gameObject.AddComponent<CanvasGroup>();
            panel.GetComponentInChildren<Image>().raycastTarget = true;
            var fill = panel.Find("Заливка")?.GetComponent<Image>();
            if (fill != null) fill.raycastTarget = true;
            return (RectTransform)panel.Find("Содержимое");
        }

        // ---------------------------------------------------------------- настройки
        static void BuildSettings(RectTransform root, PauseMenuView view)
        {
            RectTransform content = Window(root, "Настройки");
            view.SettingsPanel = (RectTransform)content.parent;

            // Вкладки: подпись антиквой; выбранную отмечает переезжающая подложка с оранжевой чертой.
            RectTransform indicator = Box(Node("Выбранная вкладка", content), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(200f, 52f));
            Layer(indicator, "Подложка", T.FillSmall, Role.Accent, .1f);
            RectTransform line = Node("Черта", indicator);
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(.5f, 0f);
            line.offsetMin = new Vector2(14f, 0f);
            line.offsetMax = new Vector2(-14f, 3f);
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.sprite = T.Pixel;
            Tint(lineImage, Role.Accent);
            Image lineGlow = Mark(indicator, "Свечение черты", T.Blob, Role.Accent, .35f, new Vector2(.5f, 0f), Vector2.zero, 20f);
            lineGlow.preserveAspect = false;
            lineGlow.rectTransform.sizeDelta = new Vector2(180f, 18f);
            view.TabIndicator = indicator;
            view.TabGraphics = TabButton(content, "Графика", 40f);
            view.TabAudio = TabButton(content, "Звук", 250f);
            view.TabGame = TabButton(content, "Игра", 460f);
            RectTransform tabsLine = Box(Node("Линия под вкладками", content), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -86f), new Vector2(RowW, 1.5f));
            var tabsLineImage = tabsLine.gameObject.AddComponent<Image>();
            tabsLineImage.sprite = T.Pixel;
            Tint(tabsLineImage, Role.PanelLine, .25f);

            view.GraphicsPage = Page(content, "Графика");
            RectTransform graphics = (RectTransform)view.GraphicsPage.transform;
            view.DisplayMode = Segmented(Control(Row(graphics, 0, "Режим экрана", "Весь экран — эксклюзивный режим; Окно — с рамкой; Без рамок — окно размером с монитор."), 420f), 3);
            view.Resolution = Dropdown(Control(Row(graphics, 1, "Разрешение", "Размер кадра. В режиме «Без рамок» всегда равен монитору."), 420f));
            view.Quality = Segmented(Control(Row(graphics, 2, "Качество", "Пресет: масштаб рендера, сглаживание и тени. Низкое — для слабых видеокарт."), 420f), 3);
            view.VSync = Toggle(Row(graphics, 3, "Вертикальная синхронизация", "Кадры в такт монитору, без разрывов. Пока включена, ограничение кадров не действует."));
            RectTransform limitRow = Row(graphics, 4, "Ограничение кадров", "Верхний предел кадров, когда синхронизация выключена.");
            view.FrameLimitRow = limitRow.gameObject.AddComponent<CanvasGroup>();
            view.FrameLimit = SliderControl(limitRow, out view.FrameLimitValue);
            view.Shadows = Segmented(Control(Row(graphics, 5, "Тени", "Дальность и чёткость теней. На слабой видеокарте — Низкие."), 420f), 3);
            view.UiScale = SliderControl(Row(graphics, 6, "Масштаб интерфейса", "Размер HUD и меню, 80–120%."), out view.UiScaleValue);
            // Список разрешений рисуется поверх нижних строк.
            view.Resolution.transform.parent.SetAsLastSibling();

            view.AudioPage = Page(content, "Звук");
            RectTransform audio = (RectTransform)view.AudioPage.transform;
            view.Master = SliderControl(Row(audio, 0, "Общая громкость", "Громкость всей игры."), out view.MasterValue);
            view.Effects = SliderControl(Row(audio, 1, "Эффекты", "Бой, шаги и интерфейс."), out view.EffectsValue);
            view.Music = SliderControl(Row(audio, 2, "Музыка", "Музыка лагеря и Разлома."), out view.MusicValue);

            view.GamePage = Page(content, "Игра");
            view.GameText = Label((RectTransform)view.GamePage.transform, "Надпись", "В разработке", FontRole.Heading, 32f, Role.TextMuted, TextAlignmentOptions.Center, 2f);

            Footer(content, true, out view.Reset, out view.Apply, out view.Status, out view.SettingsBack);
            view.Reset.gameObject.AddComponent<UiHint>().Text = "Вернуть стандартные значения этой вкладки. Экран не меняется.";
            view.Apply.gameObject.AddComponent<UiHint>().Text = "Применить режим экрана и разрешение — с проверкой 15 секунд.";
            view.SettingsPanel.gameObject.SetActive(false);
        }

        static Button TabButton(RectTransform content, string text, float left)
        {
            RectTransform tab = Box(Node("Вкладка " + text, content), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -30f), new Vector2(200f, 52f));
            Button button = HitButton(tab);
            Label(tab, "Надпись", text, FontRole.Heading, 26f, Role.TextMuted, TextAlignmentOptions.Center, 1f).textWrappingMode = TextWrappingModes.NoWrap;
            var motion = tab.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.03f;
            motion.SilentClick = true;
            return button;
        }

        static CanvasGroup Page(RectTransform content, string name)
        {
            RectTransform page = Stretch(Node("Страница " + name, content));
            return page.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>Строка настройки: тёмная полоса, подпись слева, элемент справа; наведение — оранжевая рамка и описание внизу окна.</summary>
        static RectTransform Row(RectTransform page, int index, string title, string hint)
        {
            RectTransform row = Box(Node(title, page), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -110f - index * RowStep), new Vector2(RowW, RowH));
            Image back = Layer(row, "Полоса", T.FillSmall, Role.Track, .6f);
            back.raycastTarget = true;
            Image frame = Layer(row, "Наведение", T.FrameSmall, Role.Accent, .9f);
            var motion = row.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = frame;
            motion.HoverScale = 1f;
            motion.PressScale = 1f;
            motion.HoverSound = false;
            motion.SilentClick = true;
            RectTransform label = Box(Node("Подпись", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(28f, 0f), new Vector2(420f, 40f));
            Label(label, "Надпись", title, FontRole.Body, 21f, Role.Text).textWrappingMode = TextWrappingModes.NoWrap;
            row.gameObject.AddComponent<UiHint>().Text = hint;
            return row;
        }

        static RectTransform Control(RectTransform row, float width)
            => Box(Node("Элемент", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(width, 42f));

        // ---------------------------------------------------------------- элементы
        static UiSegmented Segmented(RectTransform control, int count)
        {
            Layer(control, "Дорожка", T.TagFill, Role.Track, .95f);
            Layer(control, "Ободок", T.TagFrame, Role.PanelLine, .35f);
            RectTransform indicator = Node("Выбор", control);
            indicator.sizeDelta = new Vector2(120f, 34f);
            Layer(indicator, "Свечение", T.ButtonGlow, Role.Accent, .25f, 14f);
            Layer(indicator, "Заливка", T.TagFill, Role.Accent);
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
                segmented.Labels[i] = Label(option, "Надпись", "—", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center);
                // Цвет подписи ведёт UiSegmented; тема его не перебивает.
                Object.DestroyImmediate(segmented.Labels[i].GetComponent<ThemeColor>());
            }
            return segmented;
        }

        static UiDropdown Dropdown(RectTransform control)
        {
            var dropdown = control.gameObject.AddComponent<UiDropdown>();
            Image field = Layer(control, "Поле", T.FillSmall, Role.Panel, .95f);
            field.raycastTarget = true;
            Layer(control, "Рамка", T.FrameSmall, Role.PanelLine, .7f);
            var button = control.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = field;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            dropdown.Field = button;
            var motion = control.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = Layer(control, "Наведение", T.FrameSmall, Role.Accent);
            motion.HoverScale = 1f;
            dropdown.Value = Label(control, "Значение", "1920 × 1080", FontRole.Body, 19f, Role.Text);
            dropdown.Value.margin = new Vector4(18f, 0f, 44f, 0f);
            Mark(control, "Шеврон", T.ChevronDown, Role.Text, .9f, new Vector2(1f, .5f), new Vector2(-22f, 0f), 18f);

            // Список — вложенный Canvas: рисуется поверх строк ниже.
            RectTransform list = Box(Node("Список", control), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(0f, -6f), new Vector2(420f, 280f));
            var listCanvas = list.gameObject.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 320;
            list.gameObject.AddComponent<GraphicRaycaster>();
            list.gameObject.AddComponent<CanvasGroup>();
            Image shadow = Layer(list, "Тень", T.GlowSmall, Role.Veil, .9f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(list, "Заливка", T.FillSmall, Role.Panel).raycastTarget = true;
            Layer(list, "Рамка", T.FrameSmall, Role.PanelLine, .8f);
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
            var back = template.gameObject.AddComponent<Image>();
            back.sprite = T.FillSmall;
            back.type = Image.Type.Sliced;
            back.color = new Color(1f, 1f, 1f, 0f);
            var option = template.gameObject.AddComponent<UnityEngine.UI.Button>();
            option.targetGraphic = back;
            option.transition = Selectable.Transition.None;
            NoNavigation(option);
            Label(template, "Надпись", "1920 × 1080", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            var optionMotion = template.gameObject.AddComponent<UiHoverMotion>();
            optionMotion.Highlight = Layer(template, "Наведение", T.FrameSmall, Role.Accent, .8f);
            optionMotion.HoverScale = 1f;
            dropdown.OptionTemplate = option;
            dropdown.OptionSelected = new Color(T.Accent.r, T.Accent.g, T.Accent.b, .28f);
            dropdown.OptionIdle = new Color(1f, 1f, 1f, 0f);
            list.gameObject.SetActive(false);
            return dropdown;
        }

        static UiToggle Toggle(RectTransform row)
        {
            RectTransform rect = Box(Node("Переключатель", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(68f, 34f));
            var toggle = rect.gameObject.AddComponent<UiToggle>();
            Image off = Layer(rect, "Выкл", T.TagFill, Role.Track);
            off.raycastTarget = true;
            Image on = Layer(rect, "Вкл", T.TagFill, Role.Accent);
            Layer(rect, "Ободок", T.TagFrame, Role.PanelLine, .45f);
            RectTransform knob = At(Node("Ручка", rect), Center, new Vector2(-17f, 0f), new Vector2(26f, 26f));
            var knobImage = knob.gameObject.AddComponent<Image>();
            knobImage.sprite = T.Knob;
            knobImage.raycastTarget = false;
            Tint(knobImage, Role.Text);
            toggle.Off = off;
            toggle.On = on;
            toggle.Knob = knob;
            toggle.KnobOff = -17f;
            toggle.KnobOn = 17f;
            return toggle;
        }

        static Slider SliderControl(RectTransform row, out TMP_Text value)
        {
            RectTransform rect = Box(Node("Ползунок", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-120f, 0f), new Vector2(320f, 30f));
            RectTransform track = Node("Дорожка", rect);
            track.anchorMin = new Vector2(0f, .5f);
            track.anchorMax = new Vector2(1f, .5f);
            track.sizeDelta = new Vector2(0f, 12f);
            Layer(track, "Заливка", T.BarFill, Role.Track).raycastTarget = true;
            Layer(track, "Контур", T.BarFrame, Role.PanelLine, .45f);
            RectTransform fillArea = Node("Область заливки", rect);
            fillArea.anchorMin = new Vector2(0f, .5f);
            fillArea.anchorMax = new Vector2(1f, .5f);
            fillArea.sizeDelta = new Vector2(-4f, 8f);
            RectTransform fill = Stretch(Node("Заливка", fillArea));
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = T.BarFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            Tint(fillImage, Role.Accent);
            RectTransform area = Stretch(Node("Область ручки", rect));
            area.offsetMin = new Vector2(12f, 0f);
            area.offsetMax = new Vector2(-12f, 0f);
            RectTransform handle = Node("Ручка", area);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(26f, 0f);
            var gem = handle.gameObject.AddComponent<Image>();
            gem.sprite = T.DiamondLarge;
            gem.preserveAspect = true;
            gem.raycastTarget = true;
            Tint(gem, Role.PanelLine);

            var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = gem;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;
            NoNavigation(slider);
            var grow = handle.gameObject.AddComponent<UiHoverMotion>();
            grow.HoverScale = 1.15f;
            grow.PressScale = 1.25f;

            RectTransform number = Box(Node("Значение", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(84f, 40f));
            value = Label(number, "Надпись", "100%", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.MidlineRight);
            return slider;
        }

        /// <summary>Нижняя кромка окна: «Назад» слева (только из главного меню), описание наведённой строки, кнопки справа.</summary>
        static void Footer(RectTransform content, bool withApply, out Button reset, out Button apply, out TMP_Text status, out Button back)
        {
            Box(Place("DividerPlain", content, "Линия футера"), new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(0f, 102f), new Vector2(RowW, 16f));
            // Описание наведённой строки — своей строкой над линией: рядом с кнопками ему тесно.
            RectTransform statusBox = Box(Node("Описание", content), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 116f), new Vector2(RowW, 40f));
            status = Label(statusBox, "Надпись", "", FontRole.Body, 16f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.enableAutoSizing = true;
            status.fontSizeMin = 13f;
            status.fontSizeMax = 16f;

            RectTransform footer = Box(Node("Кнопки", content), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(560f, 56f));
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.spacing = 18f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            reset = FooterButton(footer, "ButtonSecondary", "Сбросить");
            apply = withApply ? FooterButton(footer, "ButtonPrimary", "Применить") : null;
            back = BackButton(content);
        }

        static Button FooterButton(RectTransform footer, string prefab, string text)
        {
            RectTransform button = Place(prefab, footer, text);
            button.GetComponentInChildren<TMP_Text>().text = text;
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 220f;
            element.preferredHeight = 56f;
            button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }

        static Button BackButton(RectTransform content)
        {
            RectTransform button = Place("ButtonSecondary", content, "Назад");
            Box(button, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(180f, 56f));
            button.GetComponentInChildren<TMP_Text>().text = "Назад";
            var motion = button.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.03f;
            motion.ClickSound = UiSoundEvent.Back;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            button.gameObject.SetActive(false);
            return result;
        }
    }
}
