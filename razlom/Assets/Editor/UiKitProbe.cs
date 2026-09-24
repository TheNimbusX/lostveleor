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
    /// Проба «рисованного» пака (23 сентября 2026): те же окно, кнопки и ячейки,
    /// но рамка, камни и кнопка — из генерации по эталону (Assets/UI/Kit/WatercolorPainted,
    /// tools/ui-kit/cut_painted.py), заголовки со свечением и мягкой тенью.
    /// Сверху — нынешний пак, снизу — проба. Префаб: Assets/UI/Kit/WatercolorPainted/Probe.prefab.
    /// </summary>
    public static class UiKitProbe
    {
        const string Folder = UiKitImport.KitRoot + "/WatercolorPainted";
        public const string PrefabPath = Folder + "/Probe.prefab";
        const string GlowMaterialPath = "Assets/UI/Fonts/Philosopher-Regular SDF Glow.mat";

        static Sprite P(string name)
        {
            string path = Folder + "/" + name + ".png";
            UiKitImport.Ensure(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static float FrameExpand()
        {
            string file = Folder + "/frame-offset.txt";
            return File.Exists(file) && float.TryParse(File.ReadAllText(file).Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 6f;
        }

        /// <summary>Материал заголовка: мягкая тень снизу и лёгкое свечение, как у надписей эталона.</summary>
        public static Material HeadingGlow(UiTheme t)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (mat != null || t.Heading == null) return mat;
            mat = new Material(t.Heading.material);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, .7f));
            mat.SetFloat("_UnderlayOffsetY", -.35f);
            mat.SetFloat("_UnderlayDilate", .25f);
            mat.SetFloat("_UnderlaySoftness", .75f);
            mat.EnableKeyword("GLOW_ON");
            mat.SetColor("_GlowColor", new Color(.85f, .92f, 1f, .22f));
            mat.SetFloat("_GlowOuter", .35f);
            mat.SetFloat("_GlowPower", .7f);
            AssetDatabase.CreateAsset(mat, GlowMaterialPath);
            return mat;
        }

        static void Glowing(TMP_Text text, Material mat)
        {
            if (mat != null) text.fontSharedMaterial = mat;
        }

        public static string Build()
        {
            UiTheme t = UiThemeBuilder.Ensure(false) ?? UiTheme.Current;
            EnsurePrefabs();
            Material glowMat = HeadingGlow(t);
            float fx = FrameExpand();

            RectTransform root = Node("Проба рисованного пака", null);
            root.sizeDelta = new Vector2(1920f, 1080f);
            var canvas = root.gameObject.AddComponent<Canvas>();
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            Image bg = Layer(root, "Фон", t.Pixel, Role.Panel);
            bg.GetComponent<ThemeColor>().enabled = false;
            bg.color = new Color32(14, 18, 25, 255);
            Layer(root, "Виньетка", t.VeilRadial, Role.Veil, .9f);

            Text(root, "Метка сверху", "Сейчас", 46f, 16f);
            Text(root, "Метка снизу", "Проба: рисованные детали, свет", 46f, 556f);

            // ------------- верхний ряд: нынешний пак
            RectTransform w1 = TopLeft(Place("Panel", root, "Окно сейчас"), 46f, 70f, 515f, 335f);
            var c1 = (RectTransform)w1.Find("Содержимое");
            Label(c1, "Заголовок", "Палатка", FontRole.Heading, 46f, Role.Text, TextAlignmentOptions.Top, 2f).rectTransform.offsetMax = new Vector2(0f, -22f);
            At(Place("Divider", c1, "Разделитель"), new Vector2(.5f, 1f), new Vector2(0f, -96f), new Vector2(410f, 16f));
            At(Place("CloseButton", c1, "Закрыть"), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(44f, 44f));
            TopLeft(Place("ButtonPrimary", root, "Основная сейчас"), 640f, 110f, 300f, 56f);
            TopLeft(Place("ButtonSecondary", root, "Вторичная сейчас"), 640f, 200f, 300f, 56f);
            RectTransform cc1 = TopLeft(Place("Cell", root, "Обычная сейчас"), 1040f, 100f, 150f, 150f);
            SetIcon(cc1, "copper_ring");
            RectTransform cr1 = TopLeft(Place("Cell", root, "Редкая сейчас"), 1240f, 100f, 150f, 150f);
            SetIcon(cr1, "potion_health_large");
            cr1.GetComponent<WcRarity>().Set(WcRarity.Tier.Rare);

            // ------------- нижний ряд: проба
            RectTransform w2 = PaintedPanel(root, "Окно проба", t, fx);
            TopLeft(w2, 46f, 610f, 515f, 335f);
            var c2 = (RectTransform)w2.Find("Содержимое");
            TMP_Text title = Label(c2, "Заголовок", "Палатка", FontRole.Heading, 46f, Role.Text, TextAlignmentOptions.Top, 2f);
            title.rectTransform.offsetMax = new Vector2(0f, -22f);
            Glowing(title, glowMat);
            PaintedDivider(c2, t, new Vector2(0f, -96f), 410f);
            RectTransform close = Node("Закрыть", c2);
            At(close, new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(44f, 44f));
            Layer(close, "Рамка", t.DiamondFrameSmall, Role.PanelLine);
            Mark(close, "Крест", t.Cross, Role.Text, .9f, new Vector2(.5f, .5f), Vector2.zero, 20f);

            RectTransform b1 = Node("Основная проба", root);
            TopLeft(b1, 640f, 650f, 300f, 56f);
            Layer(b1, "Свечение", t.ButtonGlow, Role.Accent, .45f, 24f);
            Layer(b1, "Заливка", P("wcp_button"), Role.Accent);
            TMP_Text l1 = Label(b1, "Надпись", "Основная", FontRole.Heading, 26f, Role.TextOnAccent, TextAlignmentOptions.Center, 2f);
            Glowing(l1, glowMat);
            RectTransform b2 = Node("Вторичная проба", root);
            TopLeft(b2, 640f, 740f, 300f, 56f);
            Layer(b2, "Заливка", t.ButtonFill, Role.Panel, .8f);
            Layer(b2, "Ободок", t.ButtonFrame, Role.PanelLine);
            Layer(b2, "Свечение ободка", t.ButtonGlow, Role.PanelLine, .12f, 24f);
            Glowing(Label(b2, "Надпись", "Вторичная", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.Center, 2f), glowMat);

            PaintedCell(root, "Обычная проба", t, fx, 1040f, 640f, "copper_ring", false);
            PaintedCell(root, "Редкая проба", t, fx, 1240f, 640f, "potion_health_large", true);

            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath); }
            finally { Object.DestroyImmediate(root.gameObject); }
            AssetDatabase.SaveAssets();
            return PrefabPath;
        }

        static void Text(RectTransform root, string name, string text, float x, float y)
        {
            RectTransform box = TopLeft(Node(name, root), x, y, 900f, 34f);
            Label(box, "Надпись", text, FontRole.Body, 22f, Role.TextMuted);
        }

        static void SetIcon(RectTransform cell, string item)
        {
            var raw = cell.Find("Предмет").GetComponent<RawImage>();
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + item + ".png");
            raw.enabled = true;
        }

        static RectTransform PaintedPanel(Transform parent, string name, UiTheme t, float fx)
        {
            RectTransform root = Node(name, parent);
            Image shadow = Layer(root, "Тень", t.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(root, "Заливка", t.Fill, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSprite, Role.Veil, .55f);
            Layer(root, "Подсветка края", t.InnerGlow, Role.Text, .035f);
            Layer(root, "Свет по кромке", t.HighlightSprite, Role.Highlight);
            Layer(root, "Рамка", P("wcp_frame"), Role.PanelLine, 1f, fx);
            Stretch(Node("Содержимое", root));
            return root;
        }

        static void PaintedDivider(RectTransform parent, UiTheme t, Vector2 pos, float w)
        {
            RectTransform root = Node("Разделитель", parent);
            At(root, new Vector2(.5f, 1f), pos, new Vector2(w, 20f));
            RectTransform line = Node("Линия", root);
            line.anchorMin = new Vector2(0f, .5f);
            line.anchorMax = new Vector2(1f, .5f);
            line.offsetMin = new Vector2(6f, -.75f);
            line.offsetMax = new Vector2(-6f, .75f);
            var img = line.gameObject.AddComponent<Image>();
            img.sprite = t.Pixel;
            img.raycastTarget = false;
            Tint(img, Role.PanelLine, .8f);
            Layer(root, "Свечение линии", t.Glow, Role.PanelLine, .08f, 0f).rectTransform.sizeDelta = new Vector2(0f, -12f);
            Mark(root, "Ромб слева", P("wcp_diamond_s"), Role.PanelLine, 1f, new Vector2(0f, .5f), Vector2.zero, 10f);
            Mark(root, "Ромб справа", P("wcp_diamond_s"), Role.PanelLine, 1f, new Vector2(1f, .5f), Vector2.zero, 10f);
            Mark(root, "Ромб в центре", P("wcp_diamond_m"), Role.PanelLine, 1f, new Vector2(.5f, .5f), Vector2.zero, 18f);
        }

        static void PaintedCell(RectTransform parent, string name, UiTheme t, float fx, float x, float y, string item, bool rare)
        {
            RectTransform root = TopLeft(Node(name, parent), x, y, 150f, 150f);
            if (rare) Layer(root, "Свечение", t.Glow, Role.Rare, .85f, 24f);
            Layer(root, "Заливка", t.Fill, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSprite, Role.Veil, .5f);
            Layer(root, "Свет по кромке", t.HighlightSprite, Role.Highlight, .7f);
            if (rare) Layer(root, "Подсветка изнутри", t.InnerGlow, Role.Rare, .32f);
            RectTransform art = Stretch(Node("Предмет", root), 14f);
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + item + ".png");
            raw.raycastTarget = false;
            Layer(root, "Рамка", P(rare ? "wcp_frame_bold" : "wcp_frame"), rare ? Role.Rare : Role.Common, 1f, fx);
            if (rare) Mark(root, "Камень", P("wcp_gem"), Role.Rare, 1f, new Vector2(.5f, 1f), Vector2.zero, 30f);
        }
    }
}
