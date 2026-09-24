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
    /// Вопрос у арки «Отправиться в забег?» на паке «Ночная акварель»:
    /// Resources/UI/Prefabs/CampRiftConfirmWc.prefab. Затемнение, карточка пака (как
    /// подтверждение в паузе) с медальоном разлома, вопрос, «Отправиться»,
    /// «Остаться» (Enter и Esc работают без подписей). Прежняя IMGUI-плашка остаётся запасной.
    /// </summary>
    public static class CampRiftConfirmWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampRiftConfirmWc.prefab";
        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);

        [MenuItem("Разлом/UI/Собрать вопрос у арки «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Вопрос у арки", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampRiftConfirmWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над HUD и окнами лагеря, под паузой (300).
            canvas.sortingOrder = 150;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<CampRiftConfirmPanel>();
            panel.Group = root.AddComponent<CanvasGroup>();
            var rect = (RectTransform)root.transform;

            RectTransform veil = Stretch(Node("Затемнение", rect));
            Image veilImage = Layer(veil, "Вуаль", T.Pixel, Role.Veil, .7f);
            veilImage.raycastTarget = true;
            Layer(veil, "Виньетка", T.VeilRadial, Role.Veil, .9f);

            RectTransform card = Place("Panel", rect, "Карточка");
            // Без пояснения мелким текстом (владелец 24 сентября): вопрос и две кнопки.
            Box(card, Center, Center, Vector2.zero, new Vector2(620f, 280f));
            panel.Card = card;
            var content = (RectTransform)card.Find("Содержимое");

            // Медальон разлома над карточкой, как камень на кромке ячеек пака.
            RectTransform medal = Box(Node("Медальон", card), new Vector2(.5f, 1f), Center, new Vector2(0f, 0f), new Vector2(88f, 88f));
            Image glow = Layer(medal, "Свечение", T.Glow, Role.Lavidium, .45f, 22f);
            glow.raycastTarget = false;
            Layer(medal, "Круг", T.CircleFill, Role.Panel, 1f);
            Layer(medal, "Ободок", T.CircleFrame, Role.PanelLine, .95f);
            var art = Stretch(Node("Разлом", medal), 6f).gameObject.AddComponent<RawImage>();
            art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/RunIcons/rift.png");
            art.raycastTarget = false;

            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -58f), new Vector2(580f, 50f));
            panel.Title = Label(title, "Надпись", "Отправиться в забег?", FontRole.Heading, 36f, Role.Text, TextAlignmentOptions.Center, 1f);
            Box(Place("Divider", content, "Линия"), new Vector2(.5f, 1f), Center, new Vector2(0f, -122f), new Vector2(380f, 16f));

            panel.Enter = KitButton(content, "ButtonPrimary", "Отправиться", -145f);
            panel.Stay = KitButton(content, "ButtonSecondary", "Остаться", 145f);
            panel.Stay.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            // Мелких подписей нет (владелец 24 сентября): Esc и Enter работают, кнопки говорят сами.

            root.SetActive(false);
            return root;
        }

        static Button KitButton(RectTransform content, string prefab, string text, float x)
        {
            RectTransform button = Place(prefab, content, text);
            Box(button, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(x, 32f), new Vector2(250f, 58f));
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = 24f;
            button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            var result = button.GetComponent<Button>();
            Navigation navigation = result.navigation;
            navigation.mode = Navigation.Mode.None;
            result.navigation = navigation;
            return result;
        }

        public static string Capture(string outPath)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                inst.SetActive(true);
                var panel = inst.GetComponent<CampRiftConfirmPanel>();
                panel.Group.alpha = 1f;
                const string backdrop = "../artifacts/camp-prod-audit-20260923/review-full/02-smith.png";
                if (File.Exists(backdrop))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(backdrop));
                    var back = Stretch(Node("Кадр игры", (RectTransform)inst.transform)).gameObject.AddComponent<RawImage>();
                    back.texture = tex;
                    back.transform.SetSiblingIndex(0);
                }
            });
        }
    }
}
