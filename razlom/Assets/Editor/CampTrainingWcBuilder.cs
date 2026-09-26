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
    /// Панель тренировки у манекенов (концепт training, владелец 23 сентября):
    /// Resources/UI/Prefabs/CampTrainingWc.prefab. Карточка под миникартой — «Тренировка»,
    /// шесть строк замера со значками, кнопка «Сбросить замер»; подписи «Манекен» над полосками.
    /// С 26 сентября в материале «Дым и свет» (раскладка прежняя): клуб глубокого дыма вместо
    /// карточки пака, нити света вместо серебряных линий, кнопка-мазок, подписи манекенов на
    /// полосе дыма. Прежняя IMGUI-панель остаётся запасной, если префаба нет.
    /// </summary>
    public static class CampTrainingWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTrainingWc.prefab";
        const float Width = 340f;

        static UiTheme T => UiTheme.Current;

        static readonly string[] Rows = { "Урон", "DPS", "Последний", "Попадания", "Криты", "Огонь" };
        static readonly string[] Icons = { "wc_stat_damage", "wc_stat_crit_power", "wc_stat_cooldown", "wc_stat_radius", "wc_stat_crit_chance", "wc_stat_lavidium" };
        /// <summary>Главные числа — оранжевым акцентом, остальные — белым (как в концепте).</summary>
        static readonly bool[] Accent = { true, true, false, false, false, true };

        [MenuItem("Разлом/UI/Собрать панель тренировки «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Тренировка", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static GameObject Layout()
        {
            var root = new GameObject("CampTrainingWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над боевым HUD (10), под палаткой и окнами NPC (100).
            canvas.sortingOrder = 12;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<CampTrainingPanel>();
            panel.Group = root.AddComponent<CanvasGroup>();
            var rect = (RectTransform)root.transform;

            // Карточка справа под миникартой и её подписью. Вместо панели пака — клуб глубокого дыма
            // (как у подсказок боевого HUD: много текста прямо над травой) и огненная нить по низу.
            RectTransform card = Node("Тренировка", rect);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(-24f, -318f);
            card.sizeDelta = new Vector2(Width, 426f);
            UiInkKit.Plate(card);
            TrimTop(card, 24f);
            // Картинки «Дыма и света» мышь не ловят: без ловца клик по панели уходил бы в ходьбу по лагерю.
            UiInkKit.HitArea(card);
            // Панель встаёт каждый раз, когда герой подходит к манекенам: быстро и без огня по кромке.
            UiInkKit.Group(card, UiInkGroup.Sweep.TopToBottom, .35f, .2f).Burn = 0f;
            panel.Card = card;
            RectTransform content = Stretch(Node("Содержимое", card));

            RectTransform title = TopLeft(Node("Заголовок", content), 0f, 18f, Width, 40f);
            UiInkKit.Label(title, "Надпись", "ТРЕНИРОВКА", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.Center, 6f, 1f, .05f);
            TopLeft(UiInkKit.Divider(content, "Линия", Width - 80f), 40f, 62f, Width - 80f, 16f);

            for (int i = 0; i < Rows.Length; i++)
            {
                float y = 88f + i * 40f;
                RectTransform row = TopLeft(Node("Строка " + Rows[i], content), 24f, y, Width - 48f, 36f);
                // Значки — белые силуэты: краску даёт тема, проявление — вместе с подписью.
                Image icon = Mark(row, "Значок", WcSprite(Icons[i]), Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(12f, 0f), 22f);
                icon.material = UiInkKit.Art;
                UiInkKit.Inked(icon, delay: .1f);
                TMP_Text label = UiInkKit.Label(TopLeft(Node("Подпись", row), 38f, 0f, 150f, 36f), "Надпись", Rows[i], FontRole.Body, 19f, Role.TextMuted);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                TMP_Text value = UiInkKit.Label(TopLeft(Node("Число", row), Width - 48f - 120f, 0f, 116f, 36f), "Надпись", "0", FontRole.Body, 21f,
                    Accent[i] ? Role.Accent : Role.Text, TextAlignmentOptions.MidlineRight, delay: .16f);
                value.fontStyle = FontStyles.Bold;
                value.textWrappingMode = TextWrappingModes.NoWrap;
                panel.Values[i] = value;
                if (i < Rows.Length - 1)
                {
                    // Тонкая черта между строками, как между столбцами итогов; прорисовывается слева направо.
                    RectTransform line = Node("Черта", row);
                    line.anchorMin = new Vector2(0f, 0f);
                    line.anchorMax = new Vector2(1f, 0f);
                    line.pivot = new Vector2(.5f, .5f);
                    line.offsetMin = new Vector2(4f, -2.5f);
                    line.offsetMax = new Vector2(-4f, -1.5f);
                    var rule = line.gameObject.AddComponent<Image>();
                    rule.sprite = T.Pixel;
                    rule.raycastTarget = false;
                    rule.material = UiInkKit.Plain;
                    Tint(rule, Role.PanelLine, .12f);
                    UiInkKit.Inked(rule, new Vector2(0f, .5f), .1f);
                }
            }
            TopLeft(UiInkKit.Divider(content, "Линия снизу", Width - 80f, false, .5f), 40f, 334f, Width - 80f, 16f);

            // Кнопка-мазок: мышь ловит её прозрачный прямоугольник (targetGraphic), наведение — в UiInkKit.Button.
            var buttonSize = new Vector2(Width - 100f, 50f);
            RectTransform button = UiInkKit.Button(content, "Сбросить замер", "Сбросить замер", false, buttonSize, 19f);
            At(button, new Vector2(0f, 1f), new Vector2(Width * .5f, -356f - buttonSize.y * .5f), buttonSize);
            panel.Reset = button.GetComponent<Button>();
            Navigation navigation = panel.Reset.navigation;
            navigation.mode = Navigation.Mode.None;
            panel.Reset.navigation = navigation;

            // Подписи над полосками манекенов: имя без чисел (владелец: «Манекен · 10 000» лишнее).
            // Имя на полосе дыма, как метки мира в разломе; своя группа — проявляется при каждом показе.
            panel.Names = new RectTransform[4];
            for (int i = 0; i < panel.Names.Length; i++)
            {
                RectTransform name = Node("Подпись манекена " + (i + 1), rect);
                name.anchorMin = name.anchorMax = Vector2.zero;
                name.pivot = new Vector2(.5f, 0f);
                name.sizeDelta = new Vector2(200f, 26f);
                TMP_Text text = UiInkKit.Label(name, "Надпись", "Манекен", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center, 0f, 1f, .1f);
                text.fontStyle = FontStyles.Bold;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                Material shadow = CombatHudWcBuilder.ShadowMaterial(text.font);
                if (shadow != null) text.fontSharedMaterial = shadow;
                float width = text.GetPreferredValues("Манекен").x;
                Image smoke = UiInkKit.SmokeAt(name, "Дым", "smoke_band_2", new Vector2(.5f, .5f), Vector2.zero, new Vector2(width + 56f, 42f), 1f);
                smoke.transform.SetAsFirstSibling();
                UiInkKit.Group(name, UiInkGroup.Sweep.FromCenter, .3f, .05f).Burn = 0f;
                name.gameObject.SetActive(false);
                panel.Names[i] = name;
            }
            return root;
        }

        /// <summary>
        /// Дым подложки не выше <paramref name="top"/> над карточкой: сразу над ней подпись миникарты,
        /// а холст тренировки выше HUD — клуб во всю высоту лёг бы на подпись.
        /// </summary>
        static void TrimTop(RectTransform card, float top)
        {
            foreach (string part in new[] { "Тень под текстом", "Дым", "Дым плотнее" })
                if (card.Find(part) is RectTransform layer && layer.offsetMax.y > top)
                    layer.offsetMax = new Vector2(layer.offsetMax.x, top);
        }

        public static string Capture(string outPath)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, Preview);
        }

        static void Preview(GameObject inst)
        {
            var panel = inst.GetComponent<CampTrainingPanel>();
            var root = (RectTransform)inst.transform;
            const string backdrop = "../artifacts/camp-prod-audit-20260923/review-full/06-dummy-2.png";
            string file = File.Exists(backdrop) ? backdrop : "../ART/no-ui.png";
            if (File.Exists(file))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(file));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.transform.SetSiblingIndex(0);
            }
            string[] values = { "1 240", "62", "38", "21", "4", "180" };
            for (int i = 0; i < panel.Values.Length; i++) panel.Values[i].text = values[i];
            panel.Names[0].gameObject.SetActive(true);
            panel.Names[0].anchoredPosition = new Vector2(800f, 600f);
        }
    }
}
