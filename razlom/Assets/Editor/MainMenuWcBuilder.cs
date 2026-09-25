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
    /// Главное меню на паке «Ночная акварель» (владелец, 24 сентября): Resources/UI/Prefabs/MainMenuWc.
    /// Компоновка варианта 4 (концепт 5-menu-arch-continue) на рисованной панораме (выбран фон 2,
    /// 6-menu-art-panorama; чистая подложка — Resources/UI/MainMenu/menu_panorama). Лого «расписной
    /// камень» сверху по центру, слева кнопки, «Новая игра» — через подтверждение. Логика — MainMenuView.
    /// </summary>
    public static class MainMenuWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/MainMenuWc.prefab";
        const string Art = "Assets/Resources/UI/MainMenu/";
        static UiTheme T => UiTheme.Current;

        [MenuItem("Разлом/UI/Собрать главное меню «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Главное меню", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
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

        static Texture Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Art + file);

        static GameObject Layout()
        {
            var root = new GameObject("MainMenuWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над игрой и HUD, под настройками паузы (300): «Настройки» открываются поверх меню.
            canvas.sortingOrder = 200;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<MainMenuPanel>();
            panel.Group = root.AddComponent<CanvasGroup>();
            var rect = (RectTransform)root.transform;

            // Рисованная панорама на весь экран, без полей на любых пропорциях.
            RectTransform back = Stretch(Node("Панорама", rect));
            var backImage = back.gameObject.AddComponent<RawImage>();
            backImage.texture = Tex("menu_panorama.png");
            backImage.raycastTarget = true;
            var fitter = back.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            // Мягкая тень слева — кнопки читаются на светлом лесе.
            RectTransform shade = Node("Тень слева", rect);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(0f, 1f);
            shade.pivot = new Vector2(0f, .5f);
            shade.sizeDelta = new Vector2(1100f, 0f);
            var shadeImage = shade.gameObject.AddComponent<RawImage>();
            shadeImage.texture = Tex("menu_left_shade.png");
            shadeImage.raycastTarget = false;

            // Лого слева сверху, над колонкой кнопок — как в концепте 5-menu-arch-continue.
            RectTransform logo = TopLeft(Node("Лого", rect), 64f, 64f, 820f, 820f * 636f / 2048f);
            var logoImage = logo.gameObject.AddComponent<RawImage>();
            logoImage.texture = Tex("logo_painted_stone.png");
            logoImage.raycastTarget = false;

            // Колонка кнопок: тёмные плашки с ромбом, «Продолжить» — с оранжевым ободком и строкой о сохранении.
            RectTransform menu = TopLeft(Node("Меню", rect), 76f, 452f, MenuWidth, 380f);
            RectTransform continueButton = MenuButton(menu, "Продолжить", 0f, 100f, true);
            panel.Continue = continueButton.GetComponent<Button>();
            panel.ContinueLabel = continueButton.Find("Надпись").GetComponent<TMP_Text>();
            panel.ContinueBullet = (RectTransform)continueButton.Find("Ромб");
            RectTransform line = Node("Сохранение", continueButton);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0f, 0f);
            line.offsetMin = new Vector2(TextInset, 16f);
            line.offsetMax = new Vector2(-24f, 42f);
            panel.ContinueLine = Label(line, "Надпись", "", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            panel.SetContinueLine("Пелаг · уровень 5");

            panel.NewGame = MenuButton(menu, "Новая игра", 116f, 68f, false).GetComponent<Button>();
            panel.Settings = MenuButton(menu, "Настройки", 200f, 68f, false).GetComponent<Button>();
            panel.Exit = MenuButton(menu, "Выход", 284f, 68f, false).GetComponent<Button>();

            BuildConfirm(rect, panel);
            return root;
        }

        const float MenuWidth = 480f;
        const float TextInset = 66f;

        /// <summary>
        /// Плашка меню из вторичной кнопки пака: текст влево, ромб перед ним. featured — «Продолжить»:
        /// ободок и надпись оранжевые, мягкое свечение (в концепте это выбранная строка).
        /// </summary>
        static RectTransform MenuButton(RectTransform menu, string text, float y, float h, bool featured)
        {
            RectTransform button = Place("ButtonSecondary", menu, text);
            TopLeft(button, 0f, y, MenuWidth, h);
            var label = button.Find("Надпись").GetComponent<TMP_Text>();
            label.text = text;
            label.fontSize = featured ? 30f : 26f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.characterSpacing = 1f;
            label.rectTransform.offsetMin = new Vector2(TextInset, label.rectTransform.offsetMin.y);
            label.rectTransform.offsetMax = new Vector2(-24f, label.rectTransform.offsetMax.y);

            RectTransform bullet = Node("Ромб", button);
            bullet.anchorMin = bullet.anchorMax = new Vector2(0f, .5f);
            bullet.pivot = new Vector2(.5f, .5f);
            bullet.anchoredPosition = new Vector2(36f, 0f);
            bullet.sizeDelta = new Vector2(16f, 16f);
            var diamond = bullet.gameObject.AddComponent<Image>();
            diamond.sprite = T.DiamondSmall;
            diamond.preserveAspect = true;
            diamond.raycastTarget = false;
            Tint(diamond, featured ? Role.Accent : Role.PanelLine);

            if (featured)
            {
                var states = button.GetComponent<ThemeStates>();
                states.Normal = Role.Accent;
                states.Hover = Role.AccentHover;
                states.Apply();
                Tint(label, Role.Accent);
                Layer(button, "Свечение", T.ButtonGlow, Role.Accent, .14f, 24f).transform.SetAsFirstSibling();
            }
            button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.02f;
            return button;
        }

        /// <summary>«Начать новую игру?» — стирание лагеря только после явного согласия.</summary>
        static void BuildConfirm(RectTransform root, MainMenuPanel panel)
        {
            RectTransform shade = Stretch(Node("Подтверждение", root));
            panel.Confirm = shade.gameObject.AddComponent<CanvasGroup>();
            Image veil = Layer(shade, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            RectTransform backing = Box(Node("Подложка", shade), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(652f, 312f));
            Layer(backing, "Заливка", T.Fill, Role.Panel, 1f);
            RectTransform card = Place("Panel", shade, "Карточка");
            Box(card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(660f, 320f));
            var content = (RectTransform)card.Find("Содержимое");
            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -54f), new Vector2(600f, 50f));
            Label(title, "Надпись", "Начать новую игру?", FontRole.Heading, 32f, Role.Text, TextAlignmentOptions.Center);
            RectTransform text = Box(Node("Пояснение", content), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -134f), new Vector2(560f, 80f));
            TMP_Text body = Label(text, "Надпись", "Лагерь начнётся заново: уровень Пелага, вещи, деньги и заказы жителей будут стёрты.",
                FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;

            RectTransform yes = Place("ButtonPrimary", content, "Начать заново");
            Box(yes, new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(-140f, 52f), new Vector2(256f, 56f));
            yes.GetComponentInChildren<TMP_Text>().text = "Начать заново";
            yes.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            panel.ConfirmYes = yes.GetComponent<Button>();
            RectTransform no = Place("ButtonSecondary", content, "Отмена");
            Box(no, new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(140f, 52f), new Vector2(256f, 56f));
            no.GetComponentInChildren<TMP_Text>().text = "Отмена";
            no.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            panel.ConfirmNo = no.GetComponent<Button>();
            shade.gameObject.SetActive(false);
        }

        static RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Кадр меню без запуска игры; confirm — показать подтверждение новой игры.</summary>
        public static string Capture(string outPath, bool confirm = false)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var panel = inst.GetComponent<MainMenuPanel>();
                panel.SetContinueLine("Пелаг · уровень 5");
                if (confirm)
                {
                    panel.Confirm.gameObject.SetActive(true);
                    panel.Confirm.alpha = 1f;
                }
                foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                    if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
            });
        }
    }
}
