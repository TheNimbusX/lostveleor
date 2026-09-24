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
    /// Значок «здесь есть дело» над жителем лагеря на паке «Ночная акварель» (владелец 24 сентября):
    /// Resources/UI/Prefabs/CampGuideWc. Шаблон — круглый медальон с картинкой и ромбом-хвостиком вниз.
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

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static GameObject Layout()
        {
            var root = new GameObject("CampGuideWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над HUD (10) и тренировкой (12), под окнами лагеря (100).
            canvas.sortingOrder = 14;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<CampGuidePanel>();
            var rect = (RectTransform)root.transform;

            // Шаблон значка над целью: медальон и хвостик-ромб вниз.
            RectTransform marker = Node("Значок цели", rect);
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.pivot = new Vector2(.5f, 0f);
            marker.sizeDelta = new Vector2(58f, 70f);
            marker.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            RectTransform medal = TopLeft(Node("Медальон", marker), 0f, 0f, 58f, 58f);
            Layer(medal, "Свечение", T.Glow, Role.Accent, .55f, 18f);
            // Два слоя заливки: акварельный круг сам по себе просвечивает, а значок должен читаться на земле.
            Layer(medal, "Подложка", T.CircleFill, Role.Panel, 1f);
            Layer(medal, "Круг", T.CircleFill, Role.Panel, 1f);
            Layer(medal, "Ободок", T.CircleFrame, Role.Accent, .95f);
            var art = Stretch(Node("Картинка", medal), 7f).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            RectTransform tail = TopLeft(Node("Хвостик", marker), 21f, 47f, 16f, 16f);
            Layer(tail, "Заливка", T.DiamondFill, Role.Accent, 1f);
            marker.gameObject.SetActive(false);
            panel.MarkerTemplate = marker;
            panel.Icons = new Texture[]
            {
                Tex("Assets/UI/CampShops/reforge.png"),
                Tex("Assets/UI/RunIcons/gold.png"),
                Tex("Assets/Resources/UI/Items/potion_health_small.png"),
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
