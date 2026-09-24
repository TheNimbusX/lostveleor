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
    public static partial class CombatHudWcBuilder
    {
        // ---------------------------------------------------------------- зелья
        static void BuildPotions(RectTransform root, CombatHudView view)
        {
            // Вариант B: два зелья за разделителем, размером со способность (владелец: «мелковаты»).
            // Бутылку по выбранному размеру ставит CombatHudView (Resources/UI/Items/potion_*).
            RectTransform panel = Box(Node("Зелья", root), BottomCenter, Vector2.zero, new Vector2(PotionsX, RowBottom),
                new Vector2(PotionPitch + Slot, Slot));
            view.PotionPanel = panel;
            string[] art = { "potion_health_small", "potion_lavidium_small" };
            for (int i = 0; i < 2; i++)
            {
                // Имена частей — те, что ищет CombatHudView.Potions: «Art», «Count», «Key».
                RectTransform tileRect = Box(Node(i == 0 ? "Health Potion" : "Lavidium Potion", panel), Vector2.zero, Vector2.zero,
                    new Vector2(i * PotionPitch, 0f), new Vector2(Slot, Slot));
                Image shadow = Layer(tileRect, "Тень", T.GlowSmall, Role.Veil, .8f, 20f);
                shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
                Layer(tileRect, "Заливка", T.FillSmall, Role.Panel);
                Layer(tileRect, "Тень снизу", T.ShadeSmall, Role.Veil, .5f);
                Image halo = Mark(tileRect, "Свет", T.Blob, i == 0 ? Role.Health : Role.Lavidium, .18f, new Vector2(.5f, .5f), Vector2.zero, Slot);
                halo.preserveAspect = false;
                var raw = Stretch(Node("Art", tileRect), 6f).gameObject.AddComponent<RawImage>();
                raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + art[i] + ".png");
                raw.raycastTarget = false;
                Layer(tileRect, "Свет по кромке", T.HighlightSmall, Role.Highlight, .8f);
                Layer(tileRect, "Рамка", T.FrameSmall, Role.PanelLine, .9f);
                Mark(tileRect, "Камень", T.Gem, i == 0 ? Role.Health : Role.Lavidium, 1f, new Vector2(.5f, 1f), new Vector2(0f, 1f), 18f);
                RectTransform count = Box(Node("Count", tileRect), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-5f, 12f), new Vector2(40f, 24f));
                TMP_Text countLabel = LabelOn(count, "0", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.BottomRight);
                countLabel.fontStyle = FontStyles.Bold;
                Shadowed(countLabel);
                // Клавиша — плашкой на нижней кромке, как у способностей.
                RectTransform cap = Keycap(tileRect, "Клавиша", "5", 26f);
                cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
                cap.pivot = new Vector2(.5f, .5f);
                cap.anchoredPosition = new Vector2(0f, -4f);
            }

            // Подсказка зелья: малая карточка пака, высота по тексту.
            RectTransform tip = Box(Node("Подсказка зелья", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 200f), new Vector2(320f, 90f));
            Card(tip, 8f);
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(18, 18, 12, 12);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Текст", tip);
            view.PotionTooltipText = LabelOn(text, "Зелье здоровья · 10%\nЛКМ — выпить\nПКМ — сменить размер", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
            view.PotionTooltip = tip;
            tip.gameObject.SetActive(false);
        }

        /// <summary>
        /// Надпись прямо на узле. Label пака кладёт текст дочерним объектом, а здесь
        /// текст нужен на самом узле: CombatHudView ищет «Count» и «Key» с TMP_Text,
        /// а раскладка подсказки берёт высоту строки у самого текста.
        /// </summary>
        static TMP_Text LabelOn(RectTransform rect, string text, FontRole font, float size, Role role, TextAlignmentOptions align)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            return label;
        }

        /// <summary>Фон карточки пака слоями, которые не участвуют в раскладке.</summary>
        static void Card(RectTransform rect, float radius)
        {
            bool small = radius < 12f;
            Image shadow = Layer(rect, "Тень", small ? T.GlowSmall : T.Glow, Role.Veil, .85f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(rect, "Заливка", small ? T.FillSmall : T.Fill, Role.Panel, .96f);
            Layer(rect, "Тень снизу", small ? T.ShadeSmall : T.ShadeSprite, Role.Veil, .5f);
            Layer(rect, "Свет по кромке", small ? T.HighlightSmall : T.HighlightSprite, Role.Highlight);
            Layer(rect, "Рамка", small ? T.FrameSmall : T.Frame, Role.PanelLine, .9f);
            foreach (Transform child in rect)
                child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        // ---------------------------------------------------------------- подсказка способности
        static void BuildTooltip(RectTransform root, CombatHudView view)
        {
            RectTransform tip = Box(Node("Подсказка", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 180f), new Vector2(420f, 200f));
            Card(tip, 14f);
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20, 20, 16, 18);
            column.spacing = 10f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Tooltip = tip;

            // Шапка: иконка в ячейке, название антиквой, клавиша.
            RectTransform header = Node("Шапка", tip);
            var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Size(header, -1f, 56f);
            RectTransform cell = Node("Иконка", header);
            Size(cell, 56f, 56f);
            Layer(cell, "Заливка", T.FillSmall, Role.Panel);
            RectTransform mask = Stretch(Node("Маска", cell), 1.5f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = T.FillSmall;
            maskImage.type = Image.Type.Sliced;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            view.TooltipIcon = Stretch(Node("Картинка", mask)).gameObject.AddComponent<RawImage>();
            view.TooltipIcon.raycastTarget = false;
            Layer(cell, "Рамка", T.FrameSmall, Role.PanelLine, .9f);

            RectTransform title = Node("Название", header);
            Size(title, -1f, 40f).flexibleWidth = 1f;
            view.TooltipTitle = LabelOn(title, "Способность", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.MidlineLeft);
            view.TooltipTitle.characterSpacing = 1f;
            view.TooltipTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.TooltipTitle.enableAutoSizing = true;
            view.TooltipTitle.fontSizeMin = 16f;
            view.TooltipTitle.fontSizeMax = 26f;
            RectTransform keyBox = Node("Клавиша", header);
            Size(keyBox, 34f, 34f);
            RectTransform cap = Keycap(keyBox, "Плашка", "Q", 34f);
            Stretch(cap);
            view.TooltipKey = cap.Find("Буква").GetComponent<TMP_Text>();

            RectTransform divider = Place("DividerPlain", tip, "Разделитель");
            Size(divider, -1f, 16f);

            RectTransform body = Node("Описание", tip);
            view.TooltipBody = LabelOn(body, "Описание способности.", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.TopLeft);
            view.TooltipBody.lineSpacing = 2f;

            // Параметры: сетка в три столбца, черта только между столбцами.
            RectTransform metrics = Node("Параметры", tip);
            var grid = metrics.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(126f, 32f);
            grid.spacing = new Vector2(0f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            view.TooltipMetricColumns = 3;
            view.TooltipMetrics = new HudTooltipMetric[6];
            for (int i = 0; i < 6; i++)
            {
                RectTransform item = Node("Параметр " + (i + 1), metrics);
                var metric = item.gameObject.AddComponent<HudTooltipMetric>();
                RectTransform sep = Box(Node("Черта", item), new Vector2(0f, .5f), new Vector2(0f, .5f), Vector2.zero, new Vector2(1.5f, 22f));
                var sepImage = sep.gameObject.AddComponent<Image>();
                sepImage.sprite = T.Pixel;
                Tint(sepImage, Role.PanelLine, .3f);
                metric.Separator = sep.gameObject;
                metric.Icon = Mark(item, "Значок", T.Pixel, Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(24f, 0f), 22f);
                RectTransform value = Box(Node("Значение", item), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(42f, 0f), new Vector2(80f, 30f));
                metric.Value = Label(value, "Надпись", "0", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineLeft);
                metric.Value.textWrappingMode = TextWrappingModes.NoWrap;
                metric.Value.enableAutoSizing = true;
                metric.Value.fontSizeMin = 12f;
                metric.Value.fontSizeMax = 18f;
                view.TooltipMetrics[i] = metric;
            }

            // Состояние («Перезарядка 2,1 с»): CombatHudView включает и прячет сам текст.
            RectTransform status = Node("Состояние", tip);
            Size(status, -1f, 24f);
            view.TooltipStatus = LabelOn(status, "", FontRole.Body, 16f, Role.Bad, TextAlignmentOptions.MidlineLeft);
            status.gameObject.SetActive(false);

            // Хвостик — гранёный ромб на нижней кромке, напротив плитки.
            RectTransform tail = Box(Node("Хвостик", tip), new Vector2(.5f, 0f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(18f, 18f));
            tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Layer(tail, "Заливка", T.DiamondFill, Role.Panel);
            Layer(tail, "Оправа", T.DiamondFrameSmall, Role.PanelLine);
            view.TooltipTail = tail;
            tip.gameObject.SetActive(false);
        }

        static LayoutElement Size(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = element.minWidth = width;
            if (height >= 0f) element.preferredHeight = element.minHeight = height;
            return element;
        }

        // ---------------------------------------------------------------- отказ при нажатии
        static void BuildFeedback(RectTransform root, CombatHudView view)
        {
            RectTransform pill = Box(Node("Отказ", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, Bottom + StripHeight + 22f), new Vector2(240f, 40f));
            Image shadow = Layer(pill, "Тень", T.ButtonGlow, Role.Veil, .7f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(pill, "Заливка", T.PillFill, Role.Panel, .95f);
            Layer(pill, "Ободок", T.PillFrame, Role.Accent, .9f);
            foreach (Transform child in pill) child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(26, 26, 6, 6);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Сообщение", pill);
            view.FeedbackText = LabelOn(text, "Перезарядка", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            view.FeedbackText.textWrappingMode = TextWrappingModes.NoWrap;
            view.Feedback = pill;
            pill.gameObject.SetActive(false);
        }
    }
}
