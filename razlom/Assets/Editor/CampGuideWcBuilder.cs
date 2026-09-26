using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Значок «здесь есть дело» над жителем лагеря (владелец 24 сентября): Resources/UI/Prefabs/CampGuideWc.
    /// Материал — «Дым и свет» (26 сентября): круг тёмного дыма, тонкое кольцо света, кремовый знак и
    /// огонёк вниз к жителю; появляется без огня по кромке (частая всплывашка).
    /// Логика — CampGuideView. Карточки знакомства с лагерем нет: владелец её отверг.
    /// </summary>
    public static class CampGuideWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampGuideWc.prefab";
        static UiTheme T => UiTheme.Current;

        [MenuItem("Разлом/UI/Собрать подсказки лагеря «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Подсказки лагеря", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        /// <summary>Знак алхимика: белая колба (набор знаков 26 сентября); пока её нет — бутылка первого набора.</summary>
        static Texture2D Flask()
        {
            Texture2D flask = Tex("Assets/UI/RunIcons/alchemist.png");
            return flask != null ? flask : Tex("Assets/Resources/UI/Items/potion_health_small.png");
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampGuideWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над HUD (10) и тренировкой (12), под окнами лагеря (100).
            canvas.sortingOrder = 14;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<CampGuidePanel>();
            var rect = (RectTransform)root.transform;

            // Шаблон значка над целью «Дыма и света»: круг тёмного дыма, тонкое кольцо света, знак
            // кремовым; под кругом — огонёк-капля вниз к жителю (ромб остался только мелким светом).
            RectTransform marker = Node("Значок цели", rect);
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.pivot = new Vector2(.5f, 0f);
            marker.sizeDelta = new Vector2(58f, 70f);
            marker.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            RectTransform medal = TopLeft(Node("Медальон", marker), 0f, 0f, 58f, 58f);
            UiInkKit.SmokeLayer(medal, "Дым", "smoke_ring", 1f, 12f, 12f, deep: true);
            // Плотный диск под знаком: дым сам по себе просвечивает, а знак должен читаться на траве.
            Image disc = Layer(medal, "Круг", T.CircleFill, Role.SmokeDeep, .92f, -2f);
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            UiInkKit.LightLayer(medal, "Кольцо", "light_ring", .5f, 8f, delay: .15f);
            // Первая RawImage значка — знак: её картинку меняет CampGuideView.
            var art = Stretch(Node("Картинка", medal), 13f).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            Tint(art, Role.Text);
            art.material = UiInkKit.Art;
            UiInkKit.Inked(art, delay: .1f);
            UiInkKit.LightAt(marker, "Хвостик", "light_gem", new Vector2(.5f, 1f), new Vector2(0f, -64f), new Vector2(11f, 13f), .85f, delay: .2f);
            // Значок всплывает над жителем каждый раз, как появляется дело: без огня по кромке.
            UiInkKit.Group(marker, UiInkGroup.Sweep.FromCenter, .3f, .08f).Burn = 0f;
            marker.gameObject.SetActive(false);
            panel.MarkerTemplate = marker;
            panel.Icons = new Texture[]
            {
                Tex("Assets/UI/CampShops/reforge.png"),
                Tex("Assets/UI/RunIcons/gold.png"),
                Flask(),
            };
            return root;
        }

        public static string Capture(string outPath)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var panel = inst.GetComponent<CampGuidePanel>();
                const string backdrop = "../artifacts/camp-prod-audit-20260923/review-full/01-arrival.png";
                if (File.Exists(backdrop))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(backdrop));
                    var back = Stretch(Node("Кадр игры", (RectTransform)inst.transform)).gameObject.AddComponent<RawImage>();
                    back.texture = tex;
                    back.transform.SetSiblingIndex(0);
                }
                var marker = Object.Instantiate(panel.MarkerTemplate, panel.MarkerTemplate.parent);
                marker.gameObject.SetActive(true);
                marker.anchoredPosition = new Vector2(1265f, 560f);
                marker.GetComponentInChildren<RawImage>().texture = panel.Icons[1];
            });
        }
    }
}
