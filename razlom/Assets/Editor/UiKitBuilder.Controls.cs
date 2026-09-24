using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Элементы управления пака «Ночная акварель» по листу
    /// ART/UI/concepts-2026-09-22/final-2/kit-sheet-3-controls.png: ползунок, тумблер,
    /// флажок, радио, поле списка, прокрутка, окно подтверждения, уведомление,
    /// подсказка клавиши, счётчик валюты, бейджи, полоса загрузки.
    /// Всё — на тех же спрайтах и ролях темы, что и основа пака.
    /// </summary>
    public static partial class UiKitBuilder
    {
        static void EnsureControlPrefabs()
        {
            Save("Switch", () => Switch(null, "Switch", true));
            Save("Checkbox", () => Checkbox(null, "Checkbox", true));
            Save("Radio", () => Radio(null, "Radio", true));
            Save("Slider", () => Slider(null, "Slider", .7f));
            Save("Scrollbar", () => ScrollbarVertical(null, "Scrollbar"));
            Save("DropdownField", () => DropdownField(null, "DropdownField", "Разрешение", "1920 × 1080"));
            Save("KeyHint", () => KeyHint(null, "KeyHint", "E", "Действие"));
            Save("Currency", () => Currency(null, "Currency", "coin", "Монеты", "1 240", Role.Coins));
            Save("LevelBadge", () => LevelBadge(null, "LevelBadge", "12"));
            Save("Tag", () => Tag(null, "Tag", "ур. 4", false));
            Save("Dialog", () => Dialog(null, "Dialog", "Разобрать предмет?", "Вы получите материалы для крафта."));
            Save("Toast", () => Toast(null, "Toast", "Новый предмет", "Вы получили: Лавидий"));
            Save("LoadingBar", () => LoadingBar(null, "LoadingBar", .4f));
        }

        // ---------------------------------------------------------------- переключатели

        /// <summary>Тумблер: капсула-дорожка (оранжевая во «вкл»), ручка-шар ездит влево-вправо.</summary>
        public static RectTransform Switch(Transform parent, string name, bool on)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, 60f, 30f);
            Image track = Layer(root, "Дорожка", t.TagFill, on ? Role.Accent : Role.Track);
            Layer(root, "Ободок", t.TagFrame, Role.PanelLine, .45f);
            Image knob = Mark(root, "Ручка", t.Knob, Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, 24f);
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = track;
            track.raycastTarget = true;
            toggle.isOn = on;
            var visual = root.gameObject.AddComponent<WcToggleVisual>();
            visual.Target = track.GetComponent<ThemeColor>();
            visual.Knob = knob.rectTransform;
            visual.KnobTravel = 15f;
            visual.KnobColor = knob.GetComponent<ThemeColor>();
            visual.Apply();
            return root;
        }

        /// <summary>Флажок: квадрат со скруглением пака, во «вкл» — оранжевая заливка и галочка.</summary>
        public static RectTransform Checkbox(Transform parent, string name, bool on)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, 30f, 30f);
            Image box = Layer(root, "Квадрат", t.FillSmall, on ? Role.Accent : Role.Panel);
            Layer(root, "Рамка", t.FrameSmall, Role.PanelLine, .8f);
            Image check = Mark(root, "Галочка", t.Check, Role.TextOnAccent, 1f, new Vector2(.5f, .5f), Vector2.zero, 22f);
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = box;
            box.raycastTarget = true;
            toggle.isOn = on;
            var visual = root.gameObject.AddComponent<WcToggleVisual>();
            visual.Target = box.GetComponent<ThemeColor>();
            visual.Off = Role.Panel;
            visual.OnlyWhenOn = check.gameObject;
            visual.Apply();
            return root;
        }

        /// <summary>Радио: серебряное кольцо (оранжевое во «вкл») и точка.</summary>
        public static RectTransform Radio(Transform parent, string name, bool on)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, 30f, 30f);
            Image back = Layer(root, "Диск", t.CircleFill, Role.Panel, .8f);
            back.raycastTarget = true;
            Image ring = Layer(root, "Кольцо", t.CircleFrameBold, on ? Role.Accent : Role.PanelLine);
            Image dot = Mark(root, "Точка", t.CircleFill, Role.Accent, 1f, new Vector2(.5f, .5f), Vector2.zero, 14f);
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = back;
            toggle.isOn = on;
            var visual = root.gameObject.AddComponent<WcToggleVisual>();
            visual.Target = ring.GetComponent<ThemeColor>();
            visual.Off = Role.PanelLine;
            visual.OnlyWhenOn = dot.gameObject;
            visual.Apply();
            return root;
        }

        // ---------------------------------------------------------------- ползунок и прокрутка

        /// <summary>Ползунок: дорожка-капсула, оранжевое заполнение, серебряный контур, гранёный камень-ручка.</summary>
        public static RectTransform Slider(Transform parent, string name, float value, float w = 300f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 28f);
            RectTransform bg = Node("Дорожка", root);
            bg.anchorMin = new Vector2(0f, .5f);
            bg.anchorMax = new Vector2(1f, .5f);
            bg.sizeDelta = new Vector2(0f, 14f);
            Layer(bg, "Фон", t.BarFill, Role.Track);

            RectTransform fillArea = Node("Область заполнения", root);
            fillArea.anchorMin = new Vector2(0f, .5f);
            fillArea.anchorMax = new Vector2(1f, .5f);
            fillArea.sizeDelta = new Vector2(0f, 14f);
            RectTransform fill = Node("Заполнение", fillArea);
            fill.sizeDelta = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = t.BarFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.raycastTarget = false;
            Tint(fillImage, Role.Accent);
            Layer(fill, "Свет", t.HighlightSmall, Role.Text, .3f);
            Layer(bg, "Контур", t.BarFrame, Role.PanelLine, .55f);

            RectTransform handleArea = Stretch(Node("Область ручки", root));
            RectTransform handle = Node("Ручка", handleArea);
            handle.sizeDelta = new Vector2(26f, 26f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = t.Gem;
            handleImage.preserveAspect = true;
            Tint(handleImage, Role.Text);

            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.value = value;
            return root;
        }

        /// <summary>Вертикальная прокрутка: дорожка-капсула с шевронами внутри, тонкая линия, ручка — ромб.</summary>
        public static RectTransform ScrollbarVertical(Transform parent, string name, float h = 240f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, 24f, h);
            Image back = Layer(root, "Касание", t.TagFill, Role.Track, .7f);
            back.raycastTarget = true;
            Layer(root, "Ободок", t.TagFrame, Role.PanelLine, .55f);
            RectTransform line = Node("Линия", root);
            line.anchorMin = new Vector2(.5f, 0f);
            line.anchorMax = new Vector2(.5f, 1f);
            line.offsetMin = new Vector2(-1.5f, 30f);
            line.offsetMax = new Vector2(1.5f, -30f);
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.sprite = t.BarFill;
            lineImage.type = Image.Type.Sliced;
            lineImage.raycastTarget = false;
            Tint(lineImage, Role.PanelLine, .25f);
            Image up = Mark(root, "Вверх", t.ChevronDown, Role.TextMuted, 1f, new Vector2(.5f, 1f), new Vector2(0f, -15f), 14f);
            up.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            Mark(root, "Вниз", t.ChevronDown, Role.TextMuted, 1f, new Vector2(.5f, 0f), new Vector2(0f, 15f), 14f);

            RectTransform area = Stretch(Node("Область ручки", root));
            area.offsetMin = new Vector2(0f, 36f);
            area.offsetMax = new Vector2(0f, -36f);
            RectTransform handle = Node("Ручка", area);
            handle.sizeDelta = new Vector2(20f, 20f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = t.DiamondLarge;
            handleImage.preserveAspect = true;
            Tint(handleImage, Role.PanelLine);

            var bar = root.gameObject.AddComponent<Scrollbar>();
            bar.transition = Selectable.Transition.None;
            bar.handleRect = handle;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.TopToBottom;
            bar.size = 0f;
            bar.value = .15f;
            return root;
        }

        // ---------------------------------------------------------------- поля и подсказки

        /// <summary>
        /// Поле выпадающего списка: подпись, значение, шеврон. Раскрытый список собирается
        /// из ListItem; рабочий TMP_Dropdown подключается при пересборке окна настроек.
        /// </summary>
        public static RectTransform DropdownField(Transform parent, string name, string caption, string value, float w = 420f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 66f);
            Image fill = Layer(root, "Заливка", t.FillSmall, Role.Panel, .85f);
            fill.raycastTarget = true;
            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight);
            Layer(root, "Рамка", t.FrameSmall, Role.PanelLine);
            TMP_Text cap = Label(root, "Подпись", caption, FontRole.Body, 20f, Role.Text, TextAlignmentOptions.TopLeft);
            cap.rectTransform.offsetMin = new Vector2(20f, 0f);
            cap.rectTransform.offsetMax = new Vector2(-50f, -8f);
            TMP_Text val = Label(root, "Значение", value, FontRole.Body, 20f, Role.TextMuted, TextAlignmentOptions.BottomLeft);
            val.rectTransform.offsetMin = new Vector2(20f, 8f);
            val.rectTransform.offsetMax = new Vector2(-50f, 0f);
            Mark(root, "Шеврон", t.ChevronDown, Role.Text, .9f, new Vector2(1f, .5f), new Vector2(-26f, 0f), 20f);
            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;
            return root;
        }

        /// <summary>Подсказка клавиши: клавиша-плашка с буквой и подпись под ней.</summary>
        public static RectTransform KeyHint(Transform parent, string name, string key, string caption)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, 110f, 96f);
            RectTransform cap = At(Node("Клавиша", root), new Vector2(.5f, 1f), new Vector2(0f, -27f), new Vector2(Mathf.Max(54f, 20f + key.Length * 18f), 54f));
            Image shadow = Layer(cap, "Тень", t.Glow, Role.Veil, .7f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(cap, "Заливка", t.FillSmall, Role.Panel);
            Layer(cap, "Свет по кромке", t.HighlightSmall, Role.Highlight, 1f);
            Layer(cap, "Рамка", t.FrameSmall, Role.PanelLine);
            Label(cap, "Буква", key, FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.Center);
            RectTransform label = Node("Подпись", root);
            label.anchorMin = new Vector2(0f, 0f);
            label.anchorMax = new Vector2(1f, 0f);
            label.pivot = new Vector2(.5f, 0f);
            label.sizeDelta = new Vector2(0f, 28f);
            Label(label, "Надпись", caption, FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center);
            return root;
        }

        /// <summary>Счётчик валюты: значок, подпись, число цветом валюты (монеты — медь, не золото).</summary>
        public static RectTransform Currency(Transform parent, string name, string icon, string caption, string value, Role color)
        {
            RectTransform root = Root(parent, name, 220f, 70f);
            Mark(root, "Значок", CombatHudBuilder.Icon(icon), color, 1f, new Vector2(0f, .5f), new Vector2(30f, 0f), 54f);
            TMP_Text cap = Label(root, "Подпись", caption, FontRole.Body, 19f, Role.Text, TextAlignmentOptions.TopLeft);
            cap.rectTransform.offsetMin = new Vector2(70f, 0f);
            TMP_Text number = Label(root, "Число", value, FontRole.Heading, 36f, color, TextAlignmentOptions.BottomLeft);
            number.rectTransform.offsetMin = new Vector2(70f, -4f);
            return root;
        }

        /// <summary>Бейдж уровня: двойной серебряный ромб и число (бирюза в паке — только редкость).</summary>
        public static RectTransform LevelBadge(Transform parent, string name, string level, float size = 84f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Layer(root, "Свечение", t.DiamondFill, Role.PanelLine, .05f, 8f);
            Layer(root, "Заливка", t.DiamondFill, Role.Panel, .95f);
            Layer(root, "Рамка", t.DiamondFrame, Role.PanelLine);
            Image inner = Layer(root, "Рамка внутри", t.DiamondFrame, Role.PanelLine, .45f);
            Stretch(inner.rectTransform, 9f);
            Label(root, "Число", level, FontRole.Heading, 32f, Role.Text, TextAlignmentOptions.Center);
            return root;
        }

        /// <summary>Ярлык: уровень предмета, «новый» (с ромбом в цвете редкости). Форма — малое скругление, как у ячеек.</summary>
        public static RectTransform Tag(Transform parent, string name, string text, bool withGem, float w = 110f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 36f);
            Layer(root, "Заливка", t.FillSmall, Role.Panel, .9f);
            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight);
            Layer(root, "Ободок", t.FrameSmall, Role.PanelLine, .8f);
            Image gem = Mark(root, "Ромб", t.DiamondMedium, Role.Rare, 1f, new Vector2(0f, .5f), new Vector2(20f, 0f), 12f);
            gem.gameObject.SetActive(withGem);
            TMP_Text label = Label(root, "Надпись", text, FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            if (withGem) label.rectTransform.offsetMin = new Vector2(18f, 0f);
            return root;
        }

        // ---------------------------------------------------------------- составные

        /// <summary>Окно подтверждения: заголовок, пояснение, основная и вторичная кнопки, «закрыть».</summary>
        public static RectTransform Dialog(Transform parent, string name, string title, string body)
        {
            RectTransform root = Panel(parent, name, 540f, 230f);
            var content = (RectTransform)root.Find("Содержимое");
            TMP_Text head = Label(content, "Заголовок", title, FontRole.Heading, 32f, Role.Text, TextAlignmentOptions.TopLeft, 1f);
            head.rectTransform.offsetMin = new Vector2(34f, 0f);
            head.rectTransform.offsetMax = new Vector2(-70f, -26f);
            TMP_Text text = Label(content, "Пояснение", body, FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            text.rectTransform.offsetMin = new Vector2(34f, 0f);
            text.rectTransform.offsetMax = new Vector2(-34f, -80f);
            RectTransform ok = Place("ButtonPrimary", content, "Подтвердить");
            TopLeft(ok, 34f, 150f, 220f, 56f);
            SetLabel(ok, "Разобрать");
            RectTransform cancel = Place("ButtonSecondary", content, "Отмена");
            TopLeft(cancel, 270f, 150f, 220f, 56f);
            SetLabel(cancel, "Отмена");
            RectTransform close = Place("CloseButton", content, "Закрыть");
            At(close, new Vector2(1f, 1f), new Vector2(-38f, -38f), new Vector2(44f, 44f));
            return root;
        }

        /// <summary>Уведомление: бирюзовая рамка со свечением, ячейка с предметом, заголовок, текст, крест.</summary>
        public static RectTransform Toast(Transform parent, string name, string title, string body)
        {
            UiTheme t = Theme;
            RectTransform root = Panel(parent, name, 520f, 120f);
            Image glow = Layer(root, "Свечение", t.Glow, Role.Rare, .45f, 24f);
            glow.transform.SetSiblingIndex(0);
            root.Find("Рамка").GetComponent<ThemeColor>().SetRole(Role.Rare, 1f);
            var content = (RectTransform)root.Find("Содержимое");
            RectTransform cell = Place("Cell", content, "Предмет");
            At(cell, new Vector2(0f, .5f), new Vector2(62f, 0f), new Vector2(84f, 84f));
            TMP_Text head = Label(content, "Заголовок", title, FontRole.Heading, 28f, Role.Text, TextAlignmentOptions.TopLeft, 1f);
            head.rectTransform.offsetMin = new Vector2(126f, 0f);
            head.rectTransform.offsetMax = new Vector2(-56f, -20f);
            TMP_Text text = Label(content, "Текст", body, FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.BottomLeft);
            text.rectTransform.offsetMin = new Vector2(126f, 22f);
            text.rectTransform.offsetMax = new Vector2(-40f, 0f);
            Mark(content, "Крест", t.Cross, Role.TextMuted, 1f, new Vector2(1f, 1f), new Vector2(-28f, -28f), 22f);
            return root;
        }

        /// <summary>Полоса загрузки: полоса-капсула, гранёный камень на конце заполнения.</summary>
        public static RectTransform LoadingBar(Transform parent, string name, float value, float w = 360f)
        {
            UiTheme t = Theme;
            RectTransform root = Bar(parent, name, Role.Accent, value, w, 14f);
            Image head = Mark(root, "Камень", t.Gem, Role.Text, 1f, new Vector2(value, .5f), Vector2.zero, 26f);
            var bar = root.GetComponent<WcBar>();
            bar.Head = head.rectTransform;
            bar.Apply();
            return root;
        }

        static void SetLabel(RectTransform root, string text)
        {
            var label = root.Find("Надпись")?.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
        }
    }
}
