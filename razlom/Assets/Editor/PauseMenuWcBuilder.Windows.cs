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

        /// <summary>
        /// Окно «Дыма и света» справа от сдвинутой паузы (владелец 26 сентября: «перевести всё на новую
        /// версию»): глубокий дым шире окна вместо панели пака, под ним мягкое глубокое пятно, прозрачный
        /// ловец мыши, своя группа появления в темпе паузы. Дети кладутся в «Содержимое».
        /// </summary>
        static RectTransform Window(RectTransform root, string name) => InkWindow(root, name, new Vector2(230f, 0f), new Vector2(WindowW, WindowH));

        static RectTransform InkWindow(RectTransform root, string name, Vector2 position, Vector2 size)
        {
            RectTransform panel = Box(Node(name, root), Center, Center, position, size);
            panel.gameObject.AddComponent<CanvasGroup>();
            RectTransform content = Stretch(Node("Содержимое", panel));
            // Нить по низу подложки не нужна: у окна своя линия футера, лишний огонь владелец просил убрать.
            Object.DestroyImmediate(UiInkKit.Plate(panel).gameObject);
            // Под рваным дымом подложки — мягкое глубокое пятно без краёв: плотное ядро подложки уже окна,
            // и у краёв между клубами просвечивали мир (клавиши справа) и пункты паузы (под кнопками
            // подтверждения, 26 сентября). Вбок шире мало — до колонны паузы слева не дотягивается.
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

        // ---------------------------------------------------------------- настройки
        static void BuildSettings(RectTransform root, PauseMenuView view)
        {
            RectTransform content = Window(root, "Настройки");
            view.SettingsPanel = (RectTransform)content.parent;

            // Вкладки: подпись антиквой; выбранную отмечает переезжающая полоса дыма с нитью света.
            RectTransform indicator = Box(Node("Выбранная вкладка", content), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(200f, 52f));
            UiInkKit.SmokeLayer(indicator, "Подложка", "smoke_plate", .9f, 26f, 8f);
            Thread(indicator, "Черта", 10f, 2f, 26f, .85f);
            view.TabIndicator = indicator;
            view.TabGraphics = TabButton(content, "Графика", 40f);
            view.TabAudio = TabButton(content, "Звук", 250f);
            view.TabGame = TabButton(content, "Игра", 460f);
            Box(UiInkKit.Divider(content, "Линия под вкладками", RowW, false, .22f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -88f), new Vector2(RowW, 16f));

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
            view.GameText = UiInkKit.Label((RectTransform)view.GamePage.transform, "Надпись", "В разработке", FontRole.Heading, 32f, Role.TextMuted, TextAlignmentOptions.Center, 2f);

            Footer(content, true, out view.Reset, out view.Apply, out view.Status, out view.SettingsBack);
            view.Reset.gameObject.AddComponent<UiHint>().Text = "Вернуть стандартные значения этой вкладки. Экран не меняется.";
            view.Apply.gameObject.AddComponent<UiHint>().Text = "Применить режим экрана и разрешение — с проверкой 15 секунд.";
            view.SettingsPanel.gameObject.SetActive(false);
        }

        static Button TabButton(RectTransform content, string text, float left)
        {
            RectTransform tab = Box(Node("Вкладка " + text, content), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -30f), new Vector2(200f, 52f));
            Button button = HitButton(tab);
            TMP_Text label = UiInkKit.Label(tab, "Надпись", text, FontRole.Heading, 26f, Role.TextMuted, TextAlignmentOptions.Center, 1f, 1f, .1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // Цвет подписи ведёт PauseMenuView (TabTextOn/Off); тема его не перебивает при каждом включении окна.
            Object.DestroyImmediate(label.GetComponent<ThemeColor>());
            label.color = T.TextMuted;
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

        /// <summary>
        /// Строка настройки: подпись слева, элемент справа; в покое без подложки. Наведение — полоса дыма
        /// светлее окна, нить света по низу и огонёк у подписи (одна группа, её проявляет UiHoverMotion);
        /// описание строки — внизу окна.
        /// </summary>
        static RectTransform Row(RectTransform page, int index, string title, string hint)
        {
            RectTransform row = Box(Node(title, page), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -110f - index * RowStep), new Vector2(RowW, RowH));
            UiInkKit.HitArea(row);
            RectTransform hover = Stretch(Node("Наведение", row));
            var group = hover.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            UiInkKit.SmokeLayer(hover, "Дым", "smoke_plate", .85f, 30f, 6f);
            Thread(hover, "Нить", 60f, 0f, 26f, .5f);
            UiInkKit.LightAt(hover, "Огонёк", "light_gem", new Vector2(0f, .5f), new Vector2(18f, 0f), new Vector2(14f, 16f), .9f);
            var motion = row.gameObject.AddComponent<UiHoverMotion>();
            motion.HighlightGroup = group;
            motion.PulseMin = .7f;
            motion.HoverScale = 1f;
            motion.PressScale = 1f;
            motion.HoverSound = false;
            motion.SilentClick = true;
            RectTransform label = Box(Node("Подпись", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(420f, 40f));
            UiInkKit.Label(label, "Надпись", title, FontRole.Body, 21f, Role.Text).textWrappingMode = TextWrappingModes.NoWrap;
            row.gameObject.AddComponent<UiHint>().Text = hint;
            return row;
        }

        static RectTransform Control(RectTransform row, float width)
            => Box(Node("Элемент", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(width, 42f));

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
                // Цвет подписи ведёт UiSegmented; тема его не перебивает.
                Object.DestroyImmediate(segmented.Labels[i].GetComponent<ThemeColor>());
            }
            return segmented;
        }

        /// <summary>
        /// Выпадающий список: поле — тёмный дым с шевроном, наведение — нить света по низу. Список —
        /// вложенный Canvas поверх строк ниже: глубокий дым (UiInkKit.Plate) и своя быстрая группа
        /// появления — дым списка растекается при каждом открытии. Выбранный пункт — полоса дыма
        /// и огонёк («Выбрано», его включает UiDropdown), наведённый — нить света.
        /// </summary>
        static UiDropdown Dropdown(RectTransform control)
        {
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
            // берёт из них данные элемента.
            // Ниже поля на 30: дым списка шире его самого и иначе лёг бы на поле (список на холсте поверх).
            RectTransform list = Box(Node("Список", control), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(420f, 280f));
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
            UiInkKit.LightAt(chosen, "Огонёк", "light_gem", new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(12f, 14f), .9f, delay: .1f);
            chosen.gameObject.SetActive(false);
            UiInkKit.Label(template, "Надпись", "1920 × 1080", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .08f);
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
            return toggle;
        }

        /// <summary>
        /// Ползунок: дорожка — тёмный мазок кистью, заливка — тот же мазок оранжевой краской, обрезанный по
        /// значению (Filled: мазок не сжимается), ручка — маленький светлый огонёк в тёплом свечении.
        /// </summary>
        static Slider SliderControl(RectTransform row, out TMP_Text value)
        {
            RectTransform rect = Box(Node("Ползунок", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-120f, 0f), new Vector2(320f, 40f));
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

            RectTransform number = Box(Node("Значение", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(84f, 40f));
            value = UiInkKit.Label(number, "Надпись", "100%", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.MidlineRight);
            return slider;
        }

        /// <summary>Нижняя кромка окна: «Назад» слева (только из главного меню), описание наведённой строки, кнопки справа.</summary>
        static void Footer(RectTransform content, bool withApply, out Button reset, out Button apply, out TMP_Text status, out Button back)
        {
            Box(UiInkKit.Divider(content, "Линия футера", RowW, false, .35f), new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(0f, 102f), new Vector2(RowW, 16f));
            // Описание наведённой строки — своей строкой над линией: рядом с кнопками ему тесно.
            RectTransform statusBox = Box(Node("Описание", content), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 116f), new Vector2(RowW, 40f));
            status = UiInkKit.Label(statusBox, "Надпись", "", FontRole.Body, 16f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.enableAutoSizing = true;
            status.fontSizeMin = 13f;
            status.fontSizeMax = 16f;

            RectTransform footer = Box(Node("Кнопки", content), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 28f), new Vector2(560f, 56f));
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            // Мазки кнопок шире самих кнопок: зазор, чтобы соседние мазки не слипались.
            row.spacing = 44f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            reset = FooterButton(footer, false, "Сбросить");
            apply = withApply ? FooterButton(footer, true, "Применить") : null;
            back = BackButton(content);
        }

        static Button FooterButton(RectTransform footer, bool primary, string text)
        {
            RectTransform button = UiInkKit.Button(footer, text, text, primary, new Vector2(220f, 56f), 24f);
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 220f;
            element.preferredHeight = 56f;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }

        static Button BackButton(RectTransform content)
        {
            RectTransform button = UiInkKit.Button(content, "Назад", "Назад", false, new Vector2(180f, 56f), 24f);
            Box(button, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 28f), new Vector2(180f, 56f));
            button.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            button.gameObject.SetActive(false);
            return result;
        }
    }
}
