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
    /// Интерфейс в мире забега на паке «Ночная акварель» (владелец, 24 сентября, концепт
    /// 3-rift-world-*): Resources/UI/Prefabs/RunWorldWc. Метка (плашка, значок, надпись, у элиты —
    /// ромб и полоска здоровья), мини-меню способности при полной панели и подсказка выбора цели.
    /// Логика — RunWorldView.
    /// </summary>
    public static class RunWorldWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/RunWorldWc.prefab";
        static UiTheme T => UiTheme.Current;

        [MenuItem("Разлом/UI/Собрать метки мира забега «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Метки мира", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static Texture Icon(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/RunIcons/" + name + ".png");

        static GameObject Layout()
        {
            var root = new GameObject("RunWorldWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Под боевым HUD (10): метки мира не перекрывают полосу способностей.
            canvas.sortingOrder = 8;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var view = root.AddComponent<RunWorldView>();
            var rect = (RectTransform)root.transform;

            view.ExitIcon = Icon("exit");
            view.CacheIcon = Icon("cache");
            view.EntryIcon = Icon("rift");
            view.EliteIcon = Icon("encounter");
            view.ItemIcon = Icon("items");

            view.MarkerTemplate = Marker(rect);
            view.MarkerTemplate.gameObject.SetActive(false);
            BuildDropMenu(rect, view);
            BuildAimHint(rect, view);
            return root;
        }

        // ---------------------------------------------------------------- метка
        /// <summary>
        /// Плашка по размеру текста, низ по центру в точке мира. Значок слева; у элиты вместо значка —
        /// оранжевый ромб, под именем — полоска здоровья.
        /// </summary>
        static RunWorldMarker Marker(RectTransform root)
        {
            RectTransform marker = Node("Метка", root);
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.pivot = new Vector2(.5f, 0f);
            var column = marker.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(12, 14, 5, 6);
            column.spacing = 3f;
            column.childAlignment = TextAnchor.MiddleCenter;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
            var fitter = marker.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var group = marker.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Image fill = Layer(marker, "Заливка", T.PillFill, Role.Panel, .88f);
            Image frame = Layer(marker, "Ободок", T.PillFrame, Role.PanelLine, .55f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var view = marker.gameObject.AddComponent<RunWorldMarker>();
            RectTransform row = Node("Строка", marker);
            var line = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 7f;
            line.childAlignment = TextAnchor.MiddleCenter;
            line.childControlWidth = line.childControlHeight = true;
            line.childForceExpandWidth = line.childForceExpandHeight = false;

            RectTransform iconBox = Node("Значок", row);
            Size(iconBox, 22f, 22f);
            view.Icon = iconBox.gameObject.AddComponent<RawImage>();
            view.Icon.raycastTarget = false;
            RectTransform eliteBox = Node("Ромб элиты", row);
            Size(eliteBox, 14f, 14f);
            var elite = eliteBox.gameObject.AddComponent<Image>();
            elite.sprite = T.DiamondFill;
            elite.preserveAspect = true;
            elite.raycastTarget = false;
            Tint(elite, Role.Accent);
            view.EliteMark = eliteBox.gameObject;
            eliteBox.gameObject.SetActive(false);

            RectTransform textBox = Node("Надпись", row);
            view.Text = textBox.gameObject.AddComponent<TextMeshProUGUI>();
            view.Text.text = "Выход";
            view.Text.fontSize = 16f;
            view.Text.alignment = TextAlignmentOptions.MidlineLeft;
            view.Text.textWrappingMode = TextWrappingModes.NoWrap;
            view.Text.raycastTarget = false;
            var font = textBox.gameObject.AddComponent<ThemeFont>();
            font.Role = FontRole.Body;
            font.Apply();
            Tint(view.Text, Role.Text);

            // Полоска здоровья элиты под именем.
            RectTransform bar = Node("Здоровье", marker);
            Size(bar, 150f, 7f);
            Layer(bar, "Дорожка", T.BarFill, Role.Track, .9f);
            Image health = Layer(bar, "Заполнение", T.Pixel, Role.Health, 1f);
            health.type = Image.Type.Filled;
            health.fillMethod = Image.FillMethod.Horizontal;
            health.fillAmount = .6f;
            view.HealthFill = health;
            view.HealthRow = bar.gameObject;
            bar.gameObject.SetActive(false);
            return view;
        }

        // ---------------------------------------------------------------- мини-меню добычи
        /// <summary>
        /// Одна карточка над способностью при полной панели (концепт 3-rift-world): значок и имя,
        /// «Разобрать · +N» и «Заменить» с четырьмя клавишами-слотами (на клавишах — что уйдёт).
        /// </summary>
        static void BuildDropMenu(RectTransform root, RunWorldView view)
        {
            RectTransform card = Box(Node("Мини-меню добычи", root), Vector2.zero, new Vector2(.5f, 0f), Vector2.zero, new Vector2(380f, 132f));
            Image shadow = Layer(card, "Тень", T.Glow, Role.Veil, .8f, 20f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -5f);
            Layer(card, "Заливка", T.FillSmall, Role.Panel, .97f);
            Layer(card, "Рамка", T.FrameSmall, Role.PanelLine, .9f);
            Image catcher = Layer(card, "Ловец", T.Pixel, Role.Panel, 0f);
            catcher.raycastTarget = true;
            view.DropMenu = card;

            RectTransform cell = TopLeft(Node("Значок", card), 14f, 14f, 52f, 52f);
            Layer(cell, "Заливка", T.FillSmall, Role.Panel, 1f);
            view.DropIcon = Stretch(Node("Картинка", cell), 2f).gameObject.AddComponent<RawImage>();
            view.DropIcon.raycastTarget = false;
            Layer(cell, "Рамка", T.FrameSmall, Role.Accent, .9f);
            RectTransform caption = TopLeft(Node("Панель полна", card), 78f, 12f, 288f, 18f);
            Label(caption, "Надпись", "Панель полна", FontRole.Body, 13f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            RectTransform title = TopLeft(Node("Имя", card), 78f, 30f, 288f, 30f);
            view.DropTitle = Label(title, "Надпись", "Шквал", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.MidlineLeft);
            view.DropTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.DropTitle.enableAutoSizing = true;
            view.DropTitle.fontSizeMin = 14f;
            view.DropTitle.fontSizeMax = 22f;

            RectTransform salvage = Place("ButtonSecondary", card, "Разобрать");
            TopLeft(salvage, 14f, 76f, 132f, 42f);
            view.DropSalvage = salvage.GetComponent<Button>();
            view.DropSalvageLabel = salvage.GetComponentInChildren<TMP_Text>();
            view.DropSalvageLabel.text = "Разобрать · +40";
            view.DropSalvageLabel.fontSize = 15f;
            salvage.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;

            RectTransform replaceCaption = TopLeft(Node("Заменить", card), 156f, 76f, 70f, 42f);
            Label(replaceCaption, "Надпись", "Заменить", FontRole.Body, 14f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            view.DropReplace = new Button[4];
            view.DropReplaceIcons = new RawImage[4];
            for (int slot = 0; slot < 4; slot++)
            {
                RectTransform key = TopLeft(Node("Слот " + (slot + 1), card), 226f + slot * 36f, 78f, 33f, 40f);
                Image keyFill = Layer(key, "Заливка", T.FillSmall, Role.Panel, 1f);
                keyFill.raycastTarget = true;
                RawImage icon = Stretch(Node("Уйдёт", key), 2f).gameObject.AddComponent<RawImage>();
                icon.color = new Color(1f, 1f, 1f, .32f);
                icon.raycastTarget = false;
                view.DropReplaceIcons[slot] = icon;
                RectTransform digit = Stretch(Node("Цифра", key));
                TMP_Text label = Label(digit, "Надпись", (slot + 1).ToString(), FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
                label.fontStyle = FontStyles.Bold;
                Layer(key, "Рамка", T.FrameSmall, Role.PanelLine, .9f);
                var button = key.gameObject.AddComponent<Button>();
                button.targetGraphic = keyFill;
                button.transition = Selectable.Transition.None;
                var motion = key.gameObject.AddComponent<UiHoverMotion>();
                motion.Highlight = Layer(key, "Наведение", T.FrameBoldSmall, Role.Accent, 1f);
                motion.HoverScale = 1.08f;
                view.DropReplace[slot] = button;
            }
            card.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- подсказка цели
        static void BuildAimHint(RectTransform root, RunWorldView view)
        {
            RectTransform pill = Node("Подсказка цели", root);
            pill.anchorMin = pill.anchorMax = Vector2.zero;
            pill.pivot = new Vector2(0f, 1f);
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(14, 16, 7, 8);
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pill.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            Image fill = Layer(pill, "Заливка", T.FillSmall, Role.Panel, .92f);
            Image frame = Layer(pill, "Рамка", T.FrameSmall, Role.Accent, .8f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            RectTransform mark = Node("Прицел", pill);
            Size(mark, 14f, 14f);
            var diamond = mark.gameObject.AddComponent<Image>();
            diamond.sprite = T.DiamondFill;
            diamond.preserveAspect = true;
            diamond.raycastTarget = false;
            Tint(diamond, Role.Accent);

            RectTransform textBox = Node("Надпись", pill);
            view.AimHintText = textBox.gameObject.AddComponent<TextMeshProUGUI>();
            view.AimHintText.text = "Выбери врага\n<size=80%>ЛКМ — применить · ПКМ / Esc — отмена</size>";
            view.AimHintText.fontSize = 17f;
            view.AimHintText.alignment = TextAlignmentOptions.MidlineLeft;
            view.AimHintText.textWrappingMode = TextWrappingModes.NoWrap;
            view.AimHintText.raycastTarget = false;
            var font = textBox.gameObject.AddComponent<ThemeFont>();
            font.Role = FontRole.Body;
            font.Apply();
            Tint(view.AimHintText, Role.Text);
            view.AimHint = pill;
            pill.gameObject.SetActive(false);
        }

        static LayoutElement Size(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) { element.preferredWidth = width; element.minWidth = width; }
            if (height >= 0f) { element.preferredHeight = height; element.minHeight = height; }
            return element;
        }

        static RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // ---------------------------------------------------------------- кадр для владельца
        /// <summary>Кадр меток и мини-меню поверх кадра разлома (без запуска игры).</summary>
        public static string Capture(string outPath)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var view = inst.GetComponent<RunWorldView>();
                var root = (RectTransform)inst.transform;
                const string backdrop = "../artifacts/editor-whirl/c1/f020.png";
                if (File.Exists(backdrop))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(backdrop));
                    var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                    back.texture = tex;
                    back.transform.SetSiblingIndex(0);
                }
                void Show(Texture icon, string text, Vector2 at, float health = -1f, bool elite = false)
                {
                    RunWorldMarker marker = Object.Instantiate(view.MarkerTemplate, view.MarkerTemplate.transform.parent);
                    marker.gameObject.SetActive(true);
                    marker.Show(icon, text, health, elite);
                    ((RectTransform)marker.transform).anchoredPosition = at;
                }
                Show(view.ExitIcon, "Выход", new Vector2(1330f, 930f));
                Show(view.CacheIcon, "Тайник · охрана 4", new Vector2(1320f, 690f));
                Show(null, "Усиленный хранитель", new Vector2(930f, 700f), .64f, true);
                view.DropMenu.gameObject.SetActive(true);
                view.DropMenu.anchoredPosition = new Vector2(560f, 520f);
                view.DropIcon.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_Squall.png");
                view.DropTitle.text = "Шквал";
                string[] slots = { "Whirlwind", "Cleave", "FireFlask", "Skewer" };
                for (int i = 0; i < 4; i++) view.DropReplaceIcons[i].texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_" + slots[i] + ".png");
                view.AimHint.gameObject.SetActive(true);
                view.AimHint.anchoredPosition = new Vector2(1080f, 420f);
                foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                    if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
            });
        }
    }
}
