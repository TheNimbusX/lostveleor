using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Витрина пака «Ночная акварель»: те же разделы, что на эталонном листе
    /// ART/UI/concepts-2026-09-22/final-2/kit-sheet-2.png, собранные из префабов пака.
    /// Префаб витрины — Assets/UI/Kit/Watercolor/Showcase.prefab: его удобно открыть и
    /// рассмотреть, как будет выглядеть любой элемент в любом состоянии.
    ///
    /// Снимок (<see cref="Capture"/>) рендерится в скрытой сцене предпросмотра —
    /// открытая сцена владельца не трогается.
    /// </summary>
    public static partial class UiKitShowcase
    {
        public const string PrefabPath = UiKitImport.KitRoot + "/Watercolor/Showcase.prefab";

        [MenuItem("Разлом/UI/Пак «Ночная акварель» — пересобрать витрину")]
        public static void RebuildFromMenu() => Rebuild();

        public static string Rebuild()
        {
            EnsurePrefabs();
            RectTransform root = Build();
            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath); }
            finally { Object.DestroyImmediate(root.gameObject); }
            AssetDatabase.SaveAssets();
            return PrefabPath;
        }

        static Texture Item(string key) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + key + ".png");
        static Texture Ability(string key) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_" + key + ".png");

        static void SetText(RectTransform root, string path, string text)
        {
            var label = root.Find(path)?.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
        }

        /// <summary>Корень страницы витрины: Canvas 1920×1080, тёмный сине-угольный фон и виньетка, как у эталона.</summary>
        static RectTransform Page(string name, UiTheme t)
        {
            RectTransform root = Node(name, null);
            root.sizeDelta = new Vector2(1920f, 1080f);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            root.gameObject.AddComponent<GraphicRaycaster>();
            Image bg = Layer(root, "Фон", t.Pixel, Role.Panel, 1f);
            bg.GetComponent<ThemeColor>().enabled = false;
            bg.color = new Color32(14, 18, 25, 255);
            Layer(root, "Виньетка", t.VeilRadial, Role.Veil, .9f);
            // Зерно только на панелях: на фоне оно читалось как шум, эталон гладкий.
            return root;
        }

        static RectTransform Build()
        {
            UiTheme t = UiThemeBuilder.Ensure(false) ?? UiTheme.Current;
            RectTransform root = Page("Витрина пака", t);

            // ---------------- верхний ряд
            TopLeft(SectionHeader(root, "Заголовок окна", "Окно", 510f), 46f, 44f, 510f, 30f);
            TopLeft(SectionHeader(root, "Заголовок навигации", "Навигация", 440f), 600f, 44f, 440f, 30f);
            TopLeft(SectionHeader(root, "Заголовок кнопок", "Кнопки", 280f), 1085f, 44f, 280f, 30f);
            TopLeft(SectionHeader(root, "Заголовок ячеек", "Ячейки", 470f), 1405f, 44f, 470f, 30f);

            // Окно: заголовок, разделитель, кнопка «закрыть».
            RectTransform window = TopLeft(Place("Panel", root, "Окно"), 46f, 104f, 515f, 335f);
            RectTransform content = (RectTransform)window.Find("Содержимое");
            TMP_Text title = Label(content, "Заголовок", "Палатка", FontRole.Heading, 46f, Role.Text, TextAlignmentOptions.Top, 2f);
            title.rectTransform.offsetMax = new Vector2(0f, -22f);
            RectTransform divider = Place("Divider", content, "Разделитель");
            At(divider, new Vector2(.5f, 1f), new Vector2(0f, -96f), new Vector2(410f, 16f));
            RectTransform close = Place("CloseButton", content, "Закрыть");
            At(close, new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(44f, 44f));

            // Навигация: вкладки в полосе и список.
            RectTransform tabs = TopLeft(Place("Panel", root, "Вкладки"), 600f, 108f, 440f, 64f);
            RectTransform tabsContent = (RectTransform)tabs.Find("Содержимое");
            string[] tabNames = { "Обычная", "При наведении", "Выбрано" };
            float[] tabX = { 10f, 132f, 296f }, tabW = { 118f, 160f, 130f };
            for (int i = 0; i < 3; i++)
            {
                RectTransform tab = Place("Tab", tabsContent, "Вкладка " + (i + 1));
                TopLeft(tab, tabX[i], 8f, tabW[i], 48f);
                SetText(tab, "Надпись", tabNames[i]);
                if (i == 2)
                {
                    tab.Find("Подчёркивание").gameObject.SetActive(true);
                    tab.Find("Надпись").GetComponent<ThemeColor>().SetRole(Role.Accent, 1f);
                }
                if (i == 1) tab.Find("Надпись").GetComponent<ThemeColor>().SetRole(Role.Text, 1f);
            }
            RectTransform list = TopLeft(Place("Panel", root, "Список"), 600f, 196f, 440f, 243f);
            RectTransform listContent = (RectTransform)list.Find("Содержимое");
            string[] items = { "Журнал", "Задания", "Инвентарь", "Карта" };
            for (int i = 0; i < items.Length; i++)
            {
                RectTransform row = Place("ListItem", listContent, "Строка " + items[i]);
                TopLeft(row, 10f, 12f + i * 56f, 420f, 52f);
                SetText(row, "Надпись", items[i]);
                if (i == 1)
                {
                    row.Find("Подложка").gameObject.SetActive(true);
                    row.Find("Рамка").gameObject.SetActive(true);
                    row.Find("Маркер").GetComponent<ThemeColor>().SetRole(Role.Accent);
                }
            }

            // Кнопки: основная, вторичная, недоступная, круглая со значком.
            TopLeft(Place("ButtonPrimary", root, "Основная"), 1085f, 110f, 280f, 56f);
            RectTransform secondary = TopLeft(Place("ButtonSecondary", root, "Вторичная"), 1085f, 196f, 280f, 56f);
            RectTransform disabled = TopLeft(Place("ButtonSecondary", root, "Недоступная"), 1085f, 282f, 280f, 56f);
            SetText(disabled, "Надпись", "Недоступно");
            disabled.GetComponent<Button>().interactable = false;
            disabled.Find("Заливка").GetComponent<ThemeColor>().SetRole(Role.Disabled, 1f);
            disabled.GetComponent<ThemeStates>().Apply();
            RectTransform gear = TopLeft(Place("Panel", root, "Кнопка-значок"), 1085f, 372f, 68f, 68f);
            Mark((RectTransform)gear.Find("Содержимое"), "Значок", CombatHudBuilder.Icon("menu_settings"), Role.Text, .9f, new Vector2(.5f, .5f), Vector2.zero, 34f);

            // Ячейки: пустая, обычная, редкая; выбранная, заблокированная.
            TopLeft(Place("CellEmpty", root, "Пустая"), 1405f, 106f, 138f, 138f);
            RectTransform common = TopLeft(Place("Cell", root, "Обычная"), 1567f, 106f, 138f, 138f);
            common.Find("Предмет").GetComponent<RawImage>().texture = Item("copper_ring");
            common.Find("Предмет").GetComponent<RawImage>().enabled = true;
            RectTransform rare = TopLeft(Place("Cell", root, "Редкая"), 1729f, 106f, 138f, 138f);
            rare.Find("Предмет").GetComponent<RawImage>().texture = Item("potion_health_large");
            rare.Find("Предмет").GetComponent<RawImage>().enabled = true;
            rare.GetComponent<WcRarity>().Set(WcRarity.Tier.Rare);
            RectTransform selected = TopLeft(Place("Cell", root, "Выбранная"), 1405f, 300f, 138f, 138f);
            selected.Find("Предмет").GetComponent<RawImage>().texture = Item("duelist_sabre");
            selected.Find("Предмет").GetComponent<RawImage>().enabled = true;
            selected.GetComponent<WcSlotState>().Set(0, true);
            RectTransform locked = TopLeft(Place("CellEmpty", root, "Заблокирована"), 1567f, 300f, 138f, 138f);
            locked.Find("Ромб").GetComponent<Image>().sprite = CombatHudBuilder.Icon("lock");
            locked.Find("Ромб").GetComponent<ThemeColor>().SetRole(Role.TextMuted, 1f);
            RectTransform lockRect = (RectTransform)locked.Find("Ромб");
            lockRect.sizeDelta = new Vector2(40f, 40f);
            Caption(root, "Пустая", 1405f, 250f); Caption(root, "Обычная", 1567f, 250f); Caption(root, "Редкая", 1729f, 250f, Role.Rare);
            Caption(root, "Выбрана", 1405f, 444f, Role.Accent); Caption(root, "Заблокировано", 1567f, 444f);

            // ---------------- нижний ряд
            TopLeft(SectionHeader(root, "Заголовок карт", "Карты улучшений", 715f), 46f, 500f, 715f, 30f);
            TopLeft(SectionHeader(root, "Заголовок полосок", "Полоски", 510f), 805f, 500f, 510f, 30f);
            TopLeft(SectionHeader(root, "Заголовок текста", "Текст", 500f), 1375f, 500f, 500f, 30f);

            RectTransform card1 = TopLeft(Place("Card", root, "Карта обычная"), 46f, 560f, 715f, 195f);
            SetCard(card1, "Рассекающий удар", "Сильный удар саблей сверху перед собой.\nДля применения не требуется выбранная цель.", "Урон", "+15", Ability("Cleave"), WcRarity.Tier.Common);
            RectTransform card2 = TopLeft(Place("Card", root, "Карта редкая"), 46f, 790f, 715f, 195f);
            SetCard(card2, "Абордажный крюк", "Выстрелите крюком, чтобы притянуться\nк ближайшему врагу.", "Урон", "+25", Ability("AnchorLeap"), WcRarity.Tier.Rare);

            BarRow(root, "Здоровье", "heart", Role.Health, 120f / 144f, "120 / 144", 575f);
            BarRow(root, "Лавидий", "lavidium", Role.Lavidium, 36f / 60f, "36 / 60", 675f);
            BarRow(root, "Опыт", "menu_level_up", Role.Experience, 243f / 500f, "243 / 500", 775f);

            // Подсказка предмета.
            RectTransform tip = TopLeft(Place("Panel", root, "Подсказка"), 1375f, 560f, 500f, 440f);
            RectTransform tipContent = (RectTransform)tip.Find("Содержимое");
            TMP_Text tipTitle = Label(tipContent, "Название", "Сабля дуэлянта", FontRole.Heading, 36f, Role.Text, TextAlignmentOptions.TopLeft, 1.5f);
            tipTitle.rectTransform.offsetMin = new Vector2(30f, 0f); tipTitle.rectTransform.offsetMax = new Vector2(-20f, -22f);
            Mark(tipContent, "Камень", t.Gem, Role.Rare, 1f, new Vector2(0f, 1f), new Vector2(42f, -94f), 20f);
            TMP_Text tipRarity = Label(tipContent, "Редкость", "Редкий", FontRole.Body, 22f, Role.Rare, TextAlignmentOptions.TopLeft);
            tipRarity.rectTransform.offsetMin = new Vector2(64f, 0f); tipRarity.rectTransform.offsetMax = new Vector2(0f, -80f);
            StatRow(tipContent, "Урон", "stat_damage", "14", "3", true, 128f);
            StatRow(tipContent, "Шанс крита", "stat_crit", "8%", "2%", true, 168f);
            RectTransform tipDivider = Place("DividerPlain", tipContent, "Разделитель");
            TopLeft(tipDivider, 30f, 212f, 440f, 16f);
            TMP_Text desc = Label(tipContent, "Описание", "Лёгкая и быстрая сабля, созданная для дуэлей. Отлично сбалансирована, позволяет наносить точные удары.", FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.TopLeft);
            desc.rectTransform.offsetMin = new Vector2(30f, 0f); desc.rectTransform.offsetMax = new Vector2(-30f, -236f);
            RectTransform tipDivider2 = Place("DividerPlain", tipContent, "Разделитель 2");
            TopLeft(tipDivider2, 30f, 322f, 440f, 16f);
            StatRow(tipContent, "Броня", "stat_armor", "+5", "1", true, 344f);
            StatRow(tipContent, "Скорость атаки", "stat_attack_speed", "−2%", "1%", false, 384f);
            return root;
        }

        static void Caption(RectTransform root, string text, float x, float y, Role role = Role.TextMuted)
        {
            RectTransform box = TopLeft(Node("Подпись " + text, root), x - 20f, y, 178f, 28f);
            Label(box, "Надпись", text, FontRole.Body, 19f, role, TextAlignmentOptions.Center);
        }

        static void SetCard(RectTransform card, string title, string desc, string valueLabel, string value, Texture icon, WcRarity.Tier tier)
        {
            SetText(card, "Текст/Название", title);
            SetText(card, "Текст/Описание", desc);
            SetText(card, "Текст/Значение/Подпись", valueLabel);
            SetText(card, "Текст/Значение/Число", value);
            SetText(card, "Текст/Плашка/Надпись", tier == WcRarity.Tier.Rare ? "Редкое" : "Обычное");
            var raw = card.Find("Значок/Диск/Картинка").GetComponent<RawImage>();
            raw.texture = icon;
            raw.enabled = icon != null;
            card.GetComponent<WcRarity>().Set(tier);
        }

        static void BarRow(RectTransform root, string name, string icon, Role role, float value, string numbers, float y)
        {
            UiTheme t = UiTheme.Current;
            Mark(root, "Значок " + name, CombatHudBuilder.Icon(icon), role == Role.Experience ? Role.Text : role, 1f, new Vector2(0f, 1f), new Vector2(826f, -y - 14f), 30f);
            RectTransform label = TopLeft(Node("Подпись " + name, root), 862f, y, 300f, 30f);
            Label(label, "Надпись", name, FontRole.Body, 22f, Role.Text);
            RectTransform bar = TopLeft(Place("Bar", root, "Полоса " + name), 862f, y + 44f, 345f, 14f);
            bar.Find("Заполнение").GetComponent<ThemeColor>().SetRole(role);
            bar.GetComponent<WcBar>().Set(value);
            RectTransform pill = TopLeft(Place("Pill", root, "Значение " + name), 1222f, y + 31f, 118f, 40f);
            SetText(pill, "Надпись", numbers);
        }

        static void StatRow(RectTransform parent, string name, string icon, string value, string delta, bool up, float y)
        {
            UiTheme t = UiTheme.Current;
            Mark(parent, "Значок " + name, CombatHudBuilder.Icon(icon), Role.Text, .85f, new Vector2(0f, 1f), new Vector2(46f, -y - 16f), 26f);
            RectTransform row = TopLeft(Node("Стат " + name, parent), 74f, y, 400f, 32f);
            Label(row, "Название", name, FontRole.Body, 21f, Role.Text);
            TMP_Text number = Label(row, "Значение", value, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.MidlineRight);
            number.rectTransform.offsetMax = new Vector2(-110f, 0f);
            Mark(row, "Стрелка", up ? t.TriangleUp : t.TriangleDown, up ? Role.Good : Role.Bad, 1f, new Vector2(1f, .5f), new Vector2(-90f, 0f), 12f);
            TMP_Text d = Label(row, "Разница", delta, FontRole.Body, 21f, up ? Role.Good : Role.Bad, TextAlignmentOptions.MidlineLeft);
            d.rectTransform.offsetMin = new Vector2(320f, 0f);
        }

        /// <summary>Снимок витрины в PNG через скрытую сцену предпросмотра. Возвращает путь.</summary>
        public static string Capture(string outPath, string prefabPath = PrefabPath, int width = 1920, int height = 1080,
            System.Action<GameObject> prepare = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Rebuild(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); }
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var camGo = new GameObject("Снимок витрины");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.orthographic = true;
                cam.nearClipPlane = .1f;
                cam.farClipPlane = 100f;
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var canvas = inst.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;
                // Необязательная подготовка экземпляра: пример данных, подложка из кадра игры.
                prepare?.Invoke(inst);
                Canvas.ForceUpdateCanvases();
                foreach (var label in inst.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true);
                foreach (var layout in inst.GetComponentsInChildren<LayoutGroup>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
                Canvas.ForceUpdateCanvases();
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                return outPath;
            }
            finally
            {
                if (tex != null) Object.DestroyImmediate(tex);
                foreach (var go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out Camera c)) c.targetTexture = null;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
