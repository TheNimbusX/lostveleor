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
    /// Меню паузы на паке «Ночная акварель» (23 сентября 2026):
    /// Resources/UI/Prefabs/PauseMenuWc.prefab. PauseMenu берёт его первым,
    /// прежний PauseMenu.prefab остаётся запасным.
    ///
    /// Пауза — по концепту ART/UI/concepts-2026-09-22/final-P4/6-pause.png:
    /// без плашки, заголовок антиквой, линии с ромбами, пункты текстом; наведение
    /// и открытое окно — оранжевая надпись с ромбами по бокам. Окна настроек,
    /// управления и подтверждения — панели пака. Смысл — в PauseMenu и
    /// PauseMenuView, вид — здесь. Префаб создаётся, только если его нет.
    /// </summary>
    public static partial class PauseMenuWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/PauseMenuWc.prefab";

        static UiTheme T => UiTheme.Current;

        [MenuItem("Разлом/UI/Собрать меню паузы «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Меню паузы", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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
            var root = new GameObject("PauseMenuWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var view = root.AddComponent<PauseMenuView>();
            view.TabTextOn = T.Text;
            view.TabTextOff = T.TextMuted;
            var rect = (RectTransform)root.transform;

            // Фон: размытый снимок игры и вуаль поверх; сам фон — прозрачный ловец мыши.
            RectTransform backdrop = Stretch(Node("Фон", rect));
            var catcher = backdrop.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            view.Backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();
            RectTransform blur = Stretch(Node("Размытие", backdrop));
            var raw = blur.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            view.BackdropBlur = blur.gameObject.AddComponent<UiBackdropBlur>();
            view.BackdropBlur.Target = raw;
            // Вуаль плотнее, чем под окнами лагеря: на концепте паузы мир едва читается.
            Layer(backdrop, "Вуаль", T.Pixel, Role.Veil, 1f);
            Layer(backdrop, "Глубина", T.Pixel, Role.Panel, .45f);
            Layer(backdrop, "Виньетка", T.VeilRadial, Role.Veil, 1f);

            BuildPause(rect, view);
            BuildSettings(rect, view);
            BuildControls(rect, view);
            BuildConfirm(rect, view);
            return root;
        }

        static RectTransform Box(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = position;
            r.sizeDelta = size;
            return r;
        }

        static readonly Vector2 Center = new Vector2(.5f, .5f);

        /// <summary>Без клавиатурной навигации: стрелки и Esc у игры свои.</summary>
        static void NoNavigation(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        /// <summary>Прозрачная кнопка на весь узел: ловит мышь, вид дают слои.</summary>
        static Button HitButton(RectTransform rect)
        {
            var hit = rect.GetComponent<Image>();
            if (hit == null) hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            return button;
        }

        // ---------------------------------------------------------------- пауза
        static void BuildPause(RectTransform root, PauseMenuView view)
        {
            RectTransform panel = Box(Node("Пауза", root), Center, Center, Vector2.zero, new Vector2(480f, 760f));
            panel.gameObject.AddComponent<CanvasGroup>();
            view.PausePanel = panel;
            view.PauseCenterX = 0f;
            view.PauseShiftedX = -560f;

            RectTransform title = Box(Node("Заголовок", panel), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -40f), new Vector2(480f, 96f));
            TMP_Text titleLabel = Label(title, "Надпись", "Пауза", FontRole.Heading, 76f, Role.Text, TextAlignmentOptions.Center, 1f);
            titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Box(Place("DividerPlain", panel, "Линия сверху"), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -164f), new Vector2(340f, 16f));

            view.Continue = MenuItem(panel, "Продолжить", 0, true, out _);
            view.Settings = MenuItem(panel, "Настройки", 1, false, out view.SettingsGlow);
            view.Controls = MenuItem(panel, "Управление", 2, false, out view.ControlsGlow);
            view.Camp = MenuItem(panel, "В лагерь", 3, false, out _);
            view.Quit = MenuItem(panel, "Выйти из игры", 4, false, out _);

            Box(Place("DividerPlain", panel, "Линия снизу"), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -596f), new Vector2(340f, 16f));
            RectTransform hint = Box(Node("Подсказка", panel), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 20f), new Vector2(460f, 30f));
            view.Hint = Label(hint, "Надпись", "Esc — продолжить игру", FontRole.Body, 18f, Role.TextMuted, TextAlignmentOptions.Center, 1f);
        }

        /// <summary>
        /// Пункт паузы: надпись антиквой. Наведение — оранжевая копия надписи, ромбы
        /// по бокам и мягкое свечение (одна группа, её проявляет UiHoverMotion).
        /// «Продолжить» оранжевая всегда, как на концепте. Открытое окно отмечает
        /// та же подсветка, включённая постоянно (glow).
        /// </summary>
        static Button MenuItem(RectTransform panel, string text, int index, bool accent, out GameObject glow)
        {
            RectTransform item = Box(Node(text, panel), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -232f - index * 72f), new Vector2(420f, 60f));
            Button button = HitButton(item);

            TMP_Text label = Label(item, "Надпись", text, FontRole.Heading, 36f, accent ? Role.Accent : Role.Text, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float half = label.GetPreferredValues(text).x * .5f + 30f;

            RectTransform hover = Stretch(Node("Наведение", item));
            var group = hover.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            HoverParts(hover, text, half, 1f);

            var motion = item.gameObject.AddComponent<UiHoverMotion>();
            motion.HighlightGroup = group;
            motion.HoverScale = 1.02f;
            motion.PulseMin = .75f;

            glow = null;
            if (!accent)
            {
                RectTransform open = Stretch(Node("Открыто", item));
                HoverParts(open, text, half, 1f);
                open.gameObject.SetActive(false);
                glow = open.gameObject;
            }
            else
            {
                // У «Продолжить» ромбы видны всегда.
                Mark(item, "Ромб слева", T.DiamondSmall, Role.Accent, 1f, Center, new Vector2(-half, 0f), 14f);
                Mark(item, "Ромб справа", T.DiamondSmall, Role.Accent, 1f, Center, new Vector2(half, 0f), 14f);
            }
            return button;
        }

        static void HoverParts(RectTransform parent, string text, float half, float alpha)
        {
            Image glow = Mark(parent, "Свечение", T.Blob, Role.Accent, .16f * alpha, Center, Vector2.zero, 60f);
            glow.preserveAspect = false;
            glow.rectTransform.sizeDelta = new Vector2(half * 2f + 40f, 70f);
            TMP_Text label = Label(parent, "Надпись", text, FontRole.Heading, 36f, Role.Accent, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Mark(parent, "Ромб слева", T.DiamondSmall, Role.Accent, alpha, Center, new Vector2(-half, 0f), 14f);
            Mark(parent, "Ромб справа", T.DiamondSmall, Role.Accent, alpha, Center, new Vector2(half, 0f), 14f);
        }
    }
}
