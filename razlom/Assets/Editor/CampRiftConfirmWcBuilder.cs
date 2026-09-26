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
    /// Вопрос у арки «Отправиться в забег?»: Resources/UI/Prefabs/CampRiftConfirmWc.prefab.
    /// Затемнение, окно «Дыма и света» (26 сентября, вместо карточки пака; раскладка прежняя):
    /// клуб глубокого дыма с нитью света, круглый медальон разлома, вопрос, разделитель
    /// с огоньком, кнопки-мазки «Отправиться» и «Остаться» (Enter и Esc работают без подписей).
    /// Прежняя IMGUI-плашка остаётся запасной.
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
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
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

            // Окно «Дыма и света» на месте карточки пака: клуб глубокого дыма шире окна, огненная нить
            // по низу. Мышь ловит вуаль (весь экран) и прозрачный ловец окна — клик не уходит в лагерь.
            RectTransform card = Box(Node("Карточка", rect), Center, Center, Vector2.zero, new Vector2(620f, 280f));
            UiInkKit.Plate(card);
            UiInkKit.HitArea(card);
            // Проявление от центра, быстро; огня по кромке чуть-чуть — вопрос у арки не частая
            // всплывашка, но и не большой момент. Показ запускает включение окна (CampRiftConfirmPanel.Show),
            // подъём, масштаб и прозрачность карточки панель ведёт сама, поверх проявления.
            UiInkKit.Group(card, UiInkGroup.Sweep.FromCenter, .35f, .12f).Burn = .25f;
            panel.Card = card;
            RectTransform content = Stretch(Node("Содержимое", card));

            Medal(card);

            // Без пояснения мелким текстом (владелец 24 сентября): вопрос и две кнопки.
            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -58f), new Vector2(580f, 50f));
            panel.Title = UiInkKit.Label(title, "Надпись", "Отправиться в забег?", FontRole.Heading, 36f, Role.Text, TextAlignmentOptions.Center, 1f, 1f, .05f);
            panel.Title.textWrappingMode = TextWrappingModes.NoWrap;
            Box(UiInkKit.Divider(content, "Линия", 380f), new Vector2(.5f, 1f), Center, new Vector2(0f, -122f), new Vector2(380f, 16f));

            panel.Enter = InkButton(content, true, "Отправиться", -145f);
            panel.Stay = InkButton(content, false, "Остаться", 145f);
            panel.Stay.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            // Мелких подписей нет (владелец 24 сентября): Esc и Enter работают, кнопки говорят сами.

            root.SetActive(false);
            return root;
        }

        /// <summary>
        /// Медальон разлома на верхней кромке окна: круг, а не камень пака. Клуб дыма кругом, тёплое
        /// свечение за диском, тёмный диск, тонкое тёплое кольцо света (тихое, как кольцо способности
        /// в покое) и знак разлома кремовым — знаки забега теперь белые силуэты, краску даёт тема.
        /// </summary>
        static void Medal(RectTransform card)
        {
            const float size = 88f;
            RectTransform medal = Box(Node("Медальон", card), new Vector2(.5f, 1f), Center, Vector2.zero, new Vector2(size, size));
            UiInkKit.SmokeLayer(medal, "Дым", "smoke_ring", 1f, size * .2f, size * .2f, deep: true);
            UiInkKit.LightAt(medal, "Свечение", "light_glow", Center, Vector2.zero, new Vector2(size * 1.7f, size * 1.7f), .3f, delay: .2f);
            Image disc = Layer(medal, "Круг", T.CircleFill, Role.Panel, 1f);
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            UiInkKit.LightLayer(medal, "Кольцо", "light_ring", .32f, size * .2f, delay: .25f);
            var art = Stretch(Node("Разлом", medal), 12f).gameObject.AddComponent<RawImage>();
            art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/RunIcons/rift.png");
            art.raycastTarget = false;
            Tint(art, Role.Text);
            art.material = UiInkKit.Art;
            UiInkKit.Inked(art, delay: .15f);
        }

        /// <summary>Кнопка-мазок «Дыма и света» по нижней кромке окна; наведение и звук — в UiInkKit.Button.</summary>
        static Button InkButton(RectTransform content, bool primary, string text, float x)
        {
            var size = new Vector2(250f, 58f);
            RectTransform button = UiInkKit.Button(content, text, text, primary, size, 24f);
            Box(button, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(x, 32f), size);
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
