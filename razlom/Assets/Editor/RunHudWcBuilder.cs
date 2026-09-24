using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Экраны забега на паке «Ночная акварель» (23 сентября 2026):
    /// Resources/UI/Prefabs/RunHudWc.prefab — выбор награды (по концепту
    /// final-P4/5-choice.png: три карточки пака стопкой, редкость — рамкой и камнем),
    /// замена способности, панель состояния забега, полоса босса. RunHud берёт
    /// префаб, если он есть; без него — прежний IMGUI. Префаб создаётся, только если его нет.
    /// </summary>
    public static partial class RunHudWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/RunHudWc.prefab";

        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);

        [MenuItem("Разлом/UI/Собрать экраны забега «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Экраны забега", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        public static string Build(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return PrefabPath;
            UiThemeBuilder.Ensure(false);
            EnsurePrefabs();
            GameObject root = Layout();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return PrefabPath;
        }

        static RectTransform Box(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = position;
            r.sizeDelta = size;
            return r;
        }

        static GameObject Layout()
        {
            var root = new GameObject("RunHudWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            var view = root.AddComponent<RunHudView>();
            var rect = (RectTransform)root.transform;

            BuildStatus(rect, view);
            BuildBoss(rect, view);
            BuildChoice(rect, view);
            BuildReplace(rect, view);
            BuildSummary(rect, view);
            return root;
        }

        /// <summary>Экран поверх игры: вуаль и виньетка, свой CanvasGroup (проявляет RunHudView).</summary>
        static RectTransform Screen(RectTransform root, string name, out CanvasGroup group)
        {
            RectTransform screen = Stretch(Node(name, root));
            group = screen.gameObject.AddComponent<CanvasGroup>();
            Image veil = Layer(screen, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            Layer(screen, "Глубина", T.Pixel, Role.Panel, .35f);
            Layer(screen, "Виньетка", T.VeilRadial, Role.Veil, 1f);
            return screen;
        }

        static TMP_Text Title(RectTransform screen, string text, float y, float size)
        {
            RectTransform box = Box(Node("Заголовок", screen), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -y), new Vector2(1400f, 80f));
            TMP_Text label = Label(box, "Надпись", text, FontRole.Heading, size, Role.Text, TextAlignmentOptions.Center, 4f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        static TMP_Text Line(RectTransform screen, string name, string text, float y, float size, Role role, bool fromBottom = false)
        {
            Vector2 anchor = fromBottom ? new Vector2(.5f, 0f) : new Vector2(.5f, 1f);
            RectTransform box = Box(Node(name, screen), anchor, new Vector2(.5f, .5f), new Vector2(0f, fromBottom ? y : -y), new Vector2(1200f, 34f));
            return Label(box, "Надпись", text, FontRole.Body, size, role, TextAlignmentOptions.Center);
        }

        const string IconFolder = "Assets/UI/RunIcons/";

        static Texture Icon(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + name + ".png");

        /// <summary>Рисованный значок (ART/UI/icons-run-2026-09-23, 256 px): центр в точке якоря.</summary>
        static RawImage Pic(RectTransform parent, string name, string icon, Vector2 anchor, Vector2 position, float size)
        {
            RectTransform rect = At(Node(name, parent), anchor, position, new Vector2(size, size));
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = Icon(icon);
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>Значок слева на кнопке пака; надпись сдвигается вправо.</summary>
        static void ButtonIcon(RectTransform button, string icon, float size)
        {
            Pic(button, "Значок", icon, new Vector2(0f, .5f), new Vector2(26f + size * .5f, 0f), size);
            var label = button.GetComponentInChildren<TMP_Text>();
            label.rectTransform.offsetMin = new Vector2(size + 22f, label.rectTransform.offsetMin.y);
        }

        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>Кнопка на весь элемент пака: ловит мышь его заливка, наведение — оранжевая жирная рамка.</summary>
        static Button Clickable(RectTransform rect, Sprite frame, float grow)
        {
            Image fill = rect.Find("Заливка")?.GetComponent<Image>();
            if (fill == null) fill = Layer(rect, "Ловец", T.Pixel, Role.Panel, 0f);
            fill.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            var motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = Layer(rect, "Наведение", frame, Role.Accent, 1f);
            motion.HoverScale = grow;
            return button;
        }

        // ---------------------------------------------------------------- выбор награды
        static void BuildChoice(RectTransform root, RunHudView view)
        {
            RectTransform screen = Screen(root, "Выбор награды", out view.Choice);
            Pic(screen, "Значок", "rift", new Vector2(.5f, 1f), new Vector2(0f, -66f), 92f);
            view.ChoiceTitle = Title(screen, "Выберите награду", 150f, 58f);
            Box(Place("Divider", screen, "Линия"), new Vector2(.5f, 1f), Center, new Vector2(0f, -212f), new Vector2(860f, 16f));
            view.ChoiceSubtitle = Line(screen, "Пояснение", "Разлом зачищен", 246f, 19f, Role.TextMuted);
            view.KindIcons = new[] { Icon("ability"), Icon("talent"), Icon("items"), Icon("rift") };

            view.Offers = new RunOfferCard[3];
            for (int i = 0; i < 3; i++)
                view.Offers[i] = OfferCard(screen, i);

            Box(Place("DividerPlain", screen, "Линия снизу"), new Vector2(.5f, 0f), Center, new Vector2(0f, 104f), new Vector2(700f, 16f));
            view.ChoiceHint = Line(screen, "Подсказка", "1  2  3 — выбрать    ·    L — уйти с добычей", 70f, 18f, Role.TextMuted, true);
        }

        static RunOfferCard OfferCard(RectTransform screen, int index)
        {
            RectTransform card = Place("Card", screen, "Карточка " + (index + 1));
            Box(card, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -286f - index * 220f), new Vector2(980f, 200f));
            var offer = card.gameObject.AddComponent<RunOfferCard>();
            offer.Button = Clickable(card, T.FrameBold, 1.015f);
            offer.Icon = card.Find("Значок/Диск/Картинка").GetComponent<RawImage>();
            offer.Title = card.Find("Текст/Название").GetComponent<TMP_Text>();
            offer.Kind = card.Find("Текст/Плашка/Надпись").GetComponent<TMP_Text>();
            offer.Description = card.Find("Текст/Описание").GetComponent<TMP_Text>();
            offer.ValueLabel = card.Find("Текст/Значение/Подпись").GetComponent<TMP_Text>();
            offer.Value = card.Find("Текст/Значение/Число").GetComponent<TMP_Text>();
            offer.Rarity = card.GetComponent<WcRarity>();
            // Плашка шире («Предмет · ур. 14»), число правее длинной подписи («Лавидий»).
            ((RectTransform)card.Find("Текст/Плашка")).sizeDelta = new Vector2(210f, 30f);
            // Значок вида награды в плашке (свиток, кристалл, сумка) — ставит RunHud.
            offer.KindIcon = Pic((RectTransform)card.Find("Текст/Плашка"), "Значок", "ability", new Vector2(0f, .5f), new Vector2(22f, 0f), 30f);
            offer.Kind.rectTransform.offsetMin = new Vector2(30f, 0f);
            offer.Value.rectTransform.offsetMin = new Vector2(150f, 0f);
            offer.Description.rectTransform.offsetMax = new Vector2(-60f, -80f);

            RectTransform cap = Keycap(card, "Клавиша", (index + 1).ToString(), 38f);
            cap.anchorMin = cap.anchorMax = new Vector2(1f, 1f);
            cap.pivot = new Vector2(1f, 1f);
            cap.anchoredPosition = new Vector2(-24f, -22f);
            offer.Key = cap.Find("Буква").GetComponent<TMP_Text>();
            return offer;
        }

        // ---------------------------------------------------------------- замена способности
        static void BuildReplace(RectTransform root, RunHudView view)
        {
            RectTransform screen = Screen(root, "Замена способности", out view.Replace);
            Pic(screen, "Значок", "ability", new Vector2(.5f, 1f), new Vector2(0f, -128f), 100f);
            view.ReplaceTitle = Title(screen, "Новая способность", 222f, 52f);
            Box(Place("Divider", screen, "Линия"), new Vector2(.5f, 1f), Center, new Vector2(0f, -284f), new Vector2(860f, 16f));
            view.ReplaceSubtitle = Line(screen, "Пояснение", "Панель полна.", 320f, 19f, Role.TextMuted);

            view.Slots = new RunSlotTile[4];
            for (int i = 0; i < 4; i++) view.Slots[i] = SlotTile(screen, i);

            RectTransform salvage = Place("ButtonPrimary", screen, "Разобрать");
            Box(salvage, new Vector2(.5f, 1f), Center, new Vector2(0f, -722f), new Vector2(460f, 58f));
            view.Salvage = salvage.GetComponent<UnityEngine.UI.Button>();
            NoNavigation(view.Salvage);
            salvage.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            view.SalvageLabel = salvage.GetComponentInChildren<TMP_Text>();
            view.SalvageLabel.text = "Разобрать на 0 золота";
            ButtonIcon(salvage, "salvage", 44f);

            Box(Place("DividerPlain", screen, "Линия снизу"), new Vector2(.5f, 0f), Center, new Vector2(0f, 104f), new Vector2(700f, 16f));
            view.ReplaceHint = Line(screen, "Подсказка", "1  2  3  4 — заменить слот    ·    L — уйти с добычей", 70f, 18f, Role.TextMuted, true);
        }

        static RunSlotTile SlotTile(RectTransform screen, int index)
        {
            const float w = 230f, h = 250f, gap = 24f;
            RectTransform tile = Node("Слот " + (index + 1), screen);
            Box(tile, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2((index - 1.5f) * (w + gap), -372f), new Vector2(w, h));
            Image shadow = Layer(tile, "Тень", T.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(tile, "Заливка", T.Fill, Role.Panel);
            Layer(tile, "Тень снизу", T.ShadeSprite, Role.Veil, .5f);
            Layer(tile, "Свет по кромке", T.HighlightSprite, Role.Highlight);
            Layer(tile, "Рамка", T.Frame, Role.PanelLine, .9f);
            var slot = tile.gameObject.AddComponent<RunSlotTile>();
            slot.Button = Clickable(tile, T.FrameBold, 1.03f);

            RectTransform cell = Place("Cell", tile, "Ячейка");
            Box(cell, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(112f, 112f));
            slot.Icon = cell.Find("Предмет").GetComponent<RawImage>();
            RectTransform name = Box(Node("Название", tile), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -156f), new Vector2(w - 24f, 34f));
            slot.Name = Label(name, "Надпись", "Способность", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.Center);
            slot.Name.enableAutoSizing = true;
            slot.Name.fontSizeMin = 14f;
            slot.Name.fontSizeMax = 22f;
            slot.Name.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform note = Box(Node("Таланты", tile), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -194f), new Vector2(w - 24f, 28f));
            slot.Note = Label(note, "Надпись", "Талантов нет", FontRole.Body, 16f, Role.TextMuted, TextAlignmentOptions.Center);

            RectTransform cap = Keycap(tile, "Клавиша", (index + 1).ToString(), 34f);
            cap.anchorMin = cap.anchorMax = new Vector2(1f, 1f);
            cap.pivot = new Vector2(1f, 1f);
            cap.anchoredPosition = new Vector2(-14f, -14f);
            slot.Key = cap.Find("Буква").GetComponent<TMP_Text>();
            return slot;
        }

        // ---------------------------------------------------------------- состояние и босс
        static void BuildStatus(RectTransform root, RunHudView view)
        {
            RectTransform panel = Box(Node("Состояние забега", root), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(370f, 110f));
            Image shadow = Layer(panel, "Тень", T.GlowSmall, Role.Veil, .8f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(panel, "Заливка", T.FillSmall, Role.Panel, .9f);
            Layer(panel, "Свет по кромке", T.HighlightSmall, Role.Highlight);
            Layer(panel, "Рамка", T.FrameSmall, Role.PanelLine, .85f);
            view.Status = panel;
            Pic(panel, "Значок разлома", "rift", new Vector2(0f, 1f), new Vector2(38f, -30f), 50f);
            Pic(panel, "Значок встреч", "encounter", new Vector2(0f, 1f), new Vector2(86f, -62f), 30f);
            Pic(panel, "Значок тайников", "cache", new Vector2(0f, 1f), new Vector2(86f, -88f), 30f);
            RectTransform title = Box(Node("Заголовок", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(72f, -12f), new Vector2(290f, 34f));
            view.StatusTitle = Label(title, "Надпись", "Разлом 1 / 5", FontRole.Heading, 26f, Role.Text);
            RectTransform line = Box(Node("Строка", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -50f), new Vector2(256f, 24f));
            view.StatusLine = Label(line, "Надпись", "Встречи 0 / 3 · целей 12", FontRole.Body, 16f, Role.Text);
            RectTransform extra = Box(Node("Ещё строка", panel), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -76f), new Vector2(256f, 24f));
            view.StatusExtra = Label(extra, "Надпись", "Тайники 0 / 2 · золото 0", FontRole.Body, 16f, Role.TextMuted);
            panel.gameObject.SetActive(false);
        }

        static void BuildBoss(RectTransform root, RunHudView view)
        {
            RectTransform boss = Place("BossBar", root, "Босс");
            Box(boss, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -22f), new Vector2(680f, 104f));
            view.Boss = boss;
            view.BossName = boss.Find("Рамка/Имя/Надпись").GetComponent<TMP_Text>();
            view.BossBar = boss.Find("Рамка/Здоровье").GetComponent<WcBar>();
            boss.gameObject.SetActive(false);
        }
    }
}
