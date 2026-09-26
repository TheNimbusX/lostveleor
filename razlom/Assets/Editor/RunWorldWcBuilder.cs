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
    /// Интерфейс в мире забега (владелец, 24 сентября, концепт 3-rift-world-*; раскладка та же) в
    /// материале «Дым и свет» боевого HUD (владелец 26 сентября: «перевести вообще всё на новую
    /// версию»): Resources/UI/Prefabs/RunWorldWc. Метка (клуб дыма, значок, надпись, у элиты —
    /// огонёк-круг и полоска здоровья), мини-меню способности при полной панели и подсказка выбора
    /// цели. Префабов пака нет — детали строятся на месте (UiInkKit). Логика — RunWorldView.
    /// </summary>
    public static class RunWorldWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/RunWorldWc.prefab";
        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);

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
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
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
        /// Клуб дыма по размеру текста, низ по центру в точке мира. Значок слева; у элиты вместо значка —
        /// светящийся огонёк-круг, под именем — полоска здоровья.
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

            // «Дым и свет» (25 сентября): вместо капсулы — клуб дыма, тающий в мир.
            Image smoke = UiInkKit.SmokeLayer(marker, "Дым", "smoke_band_2", 1f, 26f, 12f);
            smoke.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var view = marker.gameObject.AddComponent<RunWorldMarker>();
            RectTransform row = Node("Строка", marker);
            var line = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 7f;
            line.childAlignment = TextAnchor.MiddleCenter;
            line.childControlWidth = line.childControlHeight = true;
            line.childForceExpandWidth = line.childForceExpandHeight = false;

            // Значок: белый знак забега (Assets/UI/RunIcons) — кремовый от темы; цветной рисунок
            // способности у добычи — в круге (вместилища — круги, владелец 26 сентября). Что показать,
            // выбирает RunWorldMarker.Show.
            RectTransform iconBox = Node("Значок", row);
            Size(iconBox, 22f, 22f);
            view.Icon = Stretch(Node("Знак", iconBox)).gameObject.AddComponent<RawImage>();
            view.Icon.raycastTarget = false;
            Tint(view.Icon, Role.Text);
            view.Art = RoundArt(iconBox, "Рисунок", 0f);
            view.Art.enabled = false;

            // Элита — тёплый огонёк-круг вместо ромба: сияние света и светлое ядро.
            RectTransform eliteBox = Node("Огонёк элиты", row);
            Size(eliteBox, 14f, 14f);
            UiInkKit.LightAt(eliteBox, "Сияние", "light_glow", Center, Vector2.zero, new Vector2(30f, 30f), .95f, delay: 0f);
            Mark(eliteBox, "Ядро", T.CircleFill, Role.TextOnAccent, .95f, Center, Vector2.zero, 7f);
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
            UiKitBuilder.Stretch(UiInkKit.StrokeLayer(bar, "Дорожка", "brush_stroke_1", Role.Smoke, 1f).rectTransform, -3f);
            // Мазок цвета здоровья, обрезанный по доле (Filled режет спрайт, а не сжимает его).
            Image health = UiInkKit.StrokeLayer(bar, "Заполнение", "brush_stroke_2", Role.Health, 1f);
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
        /// Одна карточка над способностью при полной панели (концепт 3-rift-world, раскладка та же):
        /// значок и имя, «Разобрать · +N» и «Заменить» с четырьмя клавишами-слотами (на клавишах — что
        /// уйдёт). «Дым и свет»: клуб глубокого дыма вместо карточки, значок — круглый медальон, клавиши —
        /// круги UiInkKit.Keycap; наведение — тёплое свечение за клавишей и акцент её кольца.
        /// </summary>
        static void BuildDropMenu(RectTransform root, RunWorldView view)
        {
            RectTransform card = Box(Node("Мини-меню добычи", root), Vector2.zero, new Vector2(.5f, 0f), Vector2.zero, new Vector2(380f, 132f));
            UiInkKit.Plate(card, small: true);
            // Картинки «Дыма и света» мышь не ловят: без ловца клик по меню ушёл бы в мир ударом или
            // приказом идти (RunHud сверяет курсор с прямоугольником карточки — ловец ровно по нему).
            UiInkKit.HitArea(card);
            // Меню всплывает у каждой лежащей способности: быстро и без тлеющей кромки (красная вспышка
            // на частых всплывашках отвергнута владельцем 25 сентября).
            UiInkGroup appear = UiInkKit.Group(card, UiInkGroup.Sweep.LeftToRight, .34f, .14f);
            appear.Burn = 0f;
            appear.HideDuration = .16f;
            view.DropMenu = card;

            // Входящая способность — круглый медальон, как плитка HUD: клякса дыма, тёмный диск, рисунок
            // в круге и тонкое тёплое кольцо (спокойный огонь, не вспышка готовности).
            RectTransform cell = TopLeft(Node("Значок", card), 14f, 14f, 52f, 52f);
            UiInkKit.SmokeLayer(cell, "Клякса", "smoke_ring", 1f, 10f, 10f);
            Image disc = Layer(cell, "Диск", T.CircleFill, Role.Panel, .9f, -1f);
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            view.DropIcon = RoundArt(cell, "Маска", 2f);
            UiInkKit.LightLayer(cell, "Кольцо", "light_ring", .4f, 10f, delay: .2f);

            RectTransform caption = TopLeft(Node("Панель полна", card), 78f, 12f, 288f, 18f);
            UiInkKit.Label(caption, "Надпись", "Панель полна", FontRole.Body, 13f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, delay: .08f);
            RectTransform title = TopLeft(Node("Имя", card), 78f, 30f, 288f, 30f);
            view.DropTitle = UiInkKit.Label(title, "Надпись", "Шквал", FontRole.Heading, 22f, Role.Text, TextAlignmentOptions.MidlineLeft, delay: .1f);
            view.DropTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.DropTitle.enableAutoSizing = true;
            view.DropTitle.fontSizeMin = 14f;
            view.DropTitle.fontSizeMax = 22f;

            // Вторичная кнопка «Дыма и света»: тёмный мазок с нитью света, надпись разгорается при наведении.
            RectTransform salvage = UiInkKit.Button(card, "Разобрать", "Разобрать · +40", false, new Vector2(132f, 42f), 15f);
            TopLeft(salvage, 14f, 76f, 132f, 42f);
            view.DropSalvage = salvage.GetComponent<Button>();
            view.DropSalvageLabel = salvage.Find("Надпись").GetComponent<TMP_Text>();
            // Золото бывает четырёхзначным: подпись ужимается, а не вылезает за мазок.
            view.DropSalvageLabel.enableAutoSizing = true;
            view.DropSalvageLabel.fontSizeMin = 11f;
            view.DropSalvageLabel.fontSizeMax = 15f;

            RectTransform replaceCaption = TopLeft(Node("Заменить", card), 156f, 76f, 70f, 42f);
            UiInkKit.Label(replaceCaption, "Надпись", "Заменить", FontRole.Body, 14f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, delay: .15f);
            view.DropReplace = new Button[4];
            view.DropReplaceIcons = new RawImage[4];
            for (int slot = 0; slot < 4; slot++)
            {
                RectTransform key = TopLeft(Node("Слот " + (slot + 1), card), 226f + slot * 36f, 78f, 33f, 40f);
                Image hit = UiInkKit.HitArea(key);
                // Наведение — тёплое свечение за клавишей: UiHoverMotion держит его прозрачным без мыши.
                Image glow = UiInkKit.LightAt(key, "Свечение", "light_glow", Center, Vector2.zero, new Vector2(52f, 52f), .5f, delay: .2f);
                RectTransform cap = UiInkKit.Keycap(key, "Клавиша", (slot + 1).ToString(), 33f);
                // На клавише — способность, которая уйдёт: бледный рисунок в круге под цифрой.
                RawImage gone = RoundArt(cap, "Уйдёт", 3f);
                gone.transform.parent.SetSiblingIndex(1);
                gone.color = new Color(1f, 1f, 1f, .32f);
                view.DropReplaceIcons[slot] = gone;
                cap.Find("Буква").GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;

                var button = key.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                // Кольцо клавиши разгорается акцентом, как у кнопки «закрыть» «Дыма и света».
                var states = key.gameObject.AddComponent<ThemeStates>();
                states.Target = cap.Find("Кольцо").GetComponent<ThemeColor>();
                states.Normal = Role.PanelLine;
                states.Hover = Role.Accent;
                states.Pressed = Role.AccentPressed;
                states.Disabled = Role.Disabled;
                states.Apply();
                var motion = key.gameObject.AddComponent<UiHoverMotion>();
                motion.Highlight = glow;
                motion.HoverScale = 1.08f;
                view.DropReplace[slot] = button;
            }
            card.gameObject.SetActive(false);
        }

        /// <summary>
        /// Цветной рисунок способности в круге: узел <paramref name="name"/> — маска-круг (отступ
        /// <paramref name="inset"/>), внутри RawImage «Картинка» в материале Art «Дыма и света» (без
        /// дымки поверх цвета), проявляется с группой. Возвращает RawImage.
        /// </summary>
        static RawImage RoundArt(RectTransform parent, string name, float inset)
        {
            RectTransform mask = Stretch(Node(name, parent), inset);
            var shape = mask.gameObject.AddComponent<Image>();
            shape.sprite = T.CircleFill;
            shape.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = Stretch(Node("Картинка", mask)).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.material = UiInkKit.Art;
            UiInkKit.Inked(art, delay: .1f);
            return art;
        }

        // ---------------------------------------------------------------- подсказка цели
        /// <summary>
        /// Подсказка у курсора: капсула дыма (мягкое тёмное пятно под текстом и полоса дыма, края тают
        /// в мир), слева тёплый огонёк-круг вместо ромба, текст — две строки. Мышь не ловит.
        /// </summary>
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
            Image shade = UiInkKit.SmokeLayer(pill, "Тень под текстом", "soft_blot", .85f, 26f, 14f, deep: true);
            Image smoke = UiInkKit.SmokeLayer(pill, "Дым", "smoke_band_1", 1f, 44f, 22f);
            shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            smoke.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            // Подсказка встаёт на каждом прицеливании: быстро и без огня.
            UiInkGroup appear = UiInkKit.Group(pill, UiInkGroup.Sweep.LeftToRight, .3f, .1f);
            appear.Burn = 0f;
            appear.HideDuration = .14f;

            RectTransform mark = Node("Прицел", pill);
            Size(mark, 14f, 14f);
            UiInkKit.LightAt(mark, "Огонёк", "light_glow", Center, Vector2.zero, new Vector2(28f, 28f), .95f, delay: .05f);
            Image core = Mark(mark, "Ядро", T.CircleFill, Role.TextOnAccent, .9f, Center, Vector2.zero, 6f);
            core.material = UiInkKit.Plain;
            UiInkKit.Inked(core, delay: .05f);

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
            UiInkKit.Revealed(view.AimHintText, .06f);
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
                void Show(Texture icon, string text, Vector2 at, float health = -1f, bool elite = false, bool art = false)
                {
                    RunWorldMarker marker = Object.Instantiate(view.MarkerTemplate, view.MarkerTemplate.transform.parent);
                    marker.gameObject.SetActive(true);
                    marker.Show(icon, text, health, elite, art);
                    ((RectTransform)marker.transform).anchoredPosition = at;
                }
                Show(view.ExitIcon, "Выход", new Vector2(1330f, 930f));
                Show(view.CacheIcon, "Тайник · охрана 4", new Vector2(1320f, 690f));
                Show(null, "Усиленный хранитель", new Vector2(930f, 700f), .64f, true);
                Show(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_Wreck.png"), "КРУШЕНИЕ", new Vector2(1500f, 480f), art: true);
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
