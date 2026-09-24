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
    /// Панель тренировки у манекенов на паке «Ночная акварель» (концепт training, владелец
    /// 23 сентября): Resources/UI/Prefabs/CampTrainingWc.prefab. Карточка под миникартой —
    /// «Тренировка», шесть строк замера со значками, кнопка «Сбросить замер»; подписи
    /// «Манекен» над полосками. Прежняя IMGUI-панель остаётся запасной, если префаба нет.
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

        static GameObject Layout()
        {
            var root = new GameObject("CampTrainingWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над боевым HUD (10), под палаткой и окнами NPC (100).
            canvas.sortingOrder = 12;
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

            // Карточка справа под миникартой и её подписью.
            RectTransform card = Place("Panel", rect, "Тренировка");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(-24f, -318f);
            card.sizeDelta = new Vector2(Width, 426f);
            panel.Card = card;
            var content = (RectTransform)card.Find("Содержимое");

            RectTransform title = TopLeft(Node("Заголовок", content), 0f, 18f, Width, 40f);
            TMP_Text titleLabel = Label(title, "Надпись", "ТРЕНИРОВКА", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.Center);
            titleLabel.characterSpacing = 6f;
            TopLeft(Place("Divider", content, "Линия"), 40f, 62f, Width - 80f, 16f);

            for (int i = 0; i < Rows.Length; i++)
            {
                float y = 88f + i * 40f;
                RectTransform row = TopLeft(Node("Строка " + Rows[i], content), 24f, y, Width - 48f, 36f);
                Mark(row, "Значок", WcSprite(Icons[i]), Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(12f, 0f), 22f);
                TMP_Text label = Label(TopLeft(Node("Подпись", row), 38f, 0f, 150f, 36f), "Надпись", Rows[i], FontRole.Body, 19f, Role.TextMuted);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                TMP_Text value = Label(TopLeft(Node("Число", row), Width - 48f - 120f, 0f, 116f, 36f), "Надпись", "0", FontRole.Body, 21f,
                    Accent[i] ? Role.Accent : Role.Text, TextAlignmentOptions.MidlineRight);
                value.fontStyle = FontStyles.Bold;
                value.textWrappingMode = TextWrappingModes.NoWrap;
                panel.Values[i] = value;
                if (i < Rows.Length - 1)
                {
                    RectTransform line = Node("Черта", row);
                    line.anchorMin = new Vector2(0f, 0f);
                    line.anchorMax = new Vector2(1f, 0f);
                    line.pivot = new Vector2(.5f, .5f);
                    line.offsetMin = new Vector2(4f, -2.5f);
                    line.offsetMax = new Vector2(-4f, -1.5f);
                    Tint(line.gameObject.AddComponent<Image>(), Role.PanelLine, .12f).GetComponent<Image>().sprite = T.Pixel;
                }
            }
            TopLeft(Place("DividerPlain", content, "Линия снизу"), 40f, 334f, Width - 80f, 16f);

            RectTransform button = Place("ButtonSecondary", content, "Сбросить замер");
            TopLeft(button, 50f, 356f, Width - 100f, 50f);
            TMP_Text buttonLabel = button.Find("Надпись").GetComponent<TMP_Text>();
            buttonLabel.text = "Сбросить замер";
            buttonLabel.fontSize = 19f;
            panel.Reset = button.GetComponent<Button>() ?? button.gameObject.AddComponent<Button>();
            if (panel.Reset.targetGraphic == null) panel.Reset.targetGraphic = button.Find("Заливка")?.GetComponent<Image>();
            if (button.GetComponent<UiHoverMotion>() == null) button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;

            // Подписи над полосками манекенов: имя без чисел (владелец: «Манекен · 10 000» лишнее).
            panel.Names = new RectTransform[4];
            for (int i = 0; i < panel.Names.Length; i++)
            {
                RectTransform name = Node("Подпись манекена " + (i + 1), rect);
                name.anchorMin = name.anchorMax = Vector2.zero;
                name.pivot = new Vector2(.5f, 0f);
                name.sizeDelta = new Vector2(200f, 26f);
                TMP_Text text = Label(name, "Надпись", "Манекен", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
                text.fontStyle = FontStyles.Bold;
                Material shadow = CombatHudWcBuilder.ShadowMaterial(text.font);
                if (shadow != null) text.fontSharedMaterial = shadow;
                name.gameObject.SetActive(false);
                panel.Names[i] = name;
            }
            return root;
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
