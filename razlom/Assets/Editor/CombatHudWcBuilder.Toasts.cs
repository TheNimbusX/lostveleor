using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class CombatHudWcBuilder
    {
        const float ToastWidth = 300f, ToastHeight = 44f, ToastCircle = 48f;

        /// <summary>
        /// Всплывашки над портретом (концепт 2Б, `ART/UI/concepts-2026-09-25-audit/2-toasts-B.png`):
        /// тёмная «пилюля» с серебряной кромкой и ромбами на концах, слева круг значка с кольцом
        /// и светом цвета события, справа имя и строка. Столбик растёт вверх над эффектами зелий.
        /// </summary>
        static void BuildToasts(RectTransform root, CombatHudView view)
        {
            RectTransform column = Box(Node("Всплывашки", root), BottomCenter, Vector2.zero,
                new Vector2(StripLeft + 6f, Bottom + StripHeight + 104f), new Vector2(ToastWidth, ToastHeight));
            var toasts = column.gameObject.AddComponent<HudToasts>();
            view.Toasts = toasts;

            RectTransform toast = Box(Node("Образец", column), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(ToastWidth, ToastHeight));
            var group = toast.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform pill = Box(Node("Плашка", toast), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(ToastCircle * .5f, 0f),
                new Vector2(ToastWidth - ToastCircle * .5f, ToastHeight));
            Image shadow = Layer(pill, "Тень", T.PillFill, Role.Veil, .5f, 3f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(pill, "Заливка", T.PillFill, Role.Panel, .95f);
            Layer(pill, "Рамка", T.PillFrame, Role.PanelLine, .7f);
            Mark(pill, "Ромб справа", T.DiamondSmall, Role.PanelLine, .95f, new Vector2(1f, .5f), new Vector2(-1f, 0f), 10f);
            Mark(toast, "Ромб слева", T.DiamondSmall, Role.PanelLine, .95f, new Vector2(0f, .5f), new Vector2(-4f, 0f), 10f);

            RectTransform circle = Box(Node("Круг", toast), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(ToastCircle * .5f + 4f, 0f),
                new Vector2(ToastCircle, ToastCircle));
            Image light = Layer(circle, "Свет", RoundGlow, Role.Rare, .5f, 12f);
            light.raycastTarget = false;
            Layer(circle, "Подложка", T.CircleFill, Role.Panel);
            var icon = Stretch(Node("Значок", circle), 6f).gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;
            Layer(circle, "Кольцо", T.CircleFrameBold, Role.Rare);

            float textX = ToastCircle + 14f;
            // Высота с запасом: у «многоточия» TMP строка выше коробки пропадает целиком.
            RectTransform titleBox = Box(Node("Имя", toast), new Vector2(0f, .5f), new Vector2(0f, 0f), new Vector2(textX, -4f), new Vector2(ToastWidth - textX - 20f, 28f));
            TMP_Text title = LabelOn(titleBox, "Кожаная куртка", FontRole.Body, 17f, Role.Text, TextAlignmentOptions.BottomLeft);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform lineBox = Box(Node("Строка", toast), new Vector2(0f, .5f), new Vector2(0f, 1f), new Vector2(textX, -1f), new Vector2(ToastWidth - textX - 20f, 22f));
            TMP_Text line = LabelOn(lineBox, "Редкая · ур. 4", FontRole.Body, 13f, Role.Rare, TextAlignmentOptions.TopLeft);
            line.textWrappingMode = TextWrappingModes.NoWrap;
            line.overflowMode = TextOverflowModes.Ellipsis;

            toast.gameObject.SetActive(false);
            toasts.Template = toast;
            toasts.Pitch = ToastHeight + 10f;
        }

        /// <summary>
        /// Узкий баннер сверху по центру (концепт 2Б): скошенная плашка пака, «◆ РАЗЛОМ ЗАЧИЩЕН ◆»
        /// антиквой, под ним строка и две короткие линейки с ромбами.
        /// </summary>
        static void BuildAnnounce(RectTransform root, CombatHudView view)
        {
            var top = new Vector2(.5f, 1f);
            RectTransform banner = Box(Node("Объявление", root), top, top, new Vector2(0f, -22f), new Vector2(540f, 74f));
            var announce = banner.gameObject.AddComponent<HudAnnounce>();
            announce.Group = banner.gameObject.AddComponent<CanvasGroup>();
            announce.Group.blocksRaycasts = false;
            announce.Group.interactable = false;
            view.Announce = announce;

            Image shadow = Layer(banner, "Тень", Kit("wc_plate_fill"), Role.Veil, .55f, 6f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(banner, "Заливка", Kit("wc_plate_fill"), Role.Panel, .96f);
            Layer(banner, "Свет по кромке", T.InnerGlowSmall, Role.PanelLine, .08f, -6f);
            Layer(banner, "Рамка", Kit("wc_plate_frame"), Role.PanelLine, .85f);
            Mark(banner, "Ромб слева", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(0f, .5f), new Vector2(2f, 0f), 12f);
            Mark(banner, "Ромб справа", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(1f, .5f), new Vector2(-2f, 0f), 12f);

            RectTransform titleBox = Box(Node("Надпись", banner), new Vector2(.5f, .5f), new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(440f, 34f));
            TMP_Text title = LabelOn(titleBox, "РАЗЛОМ ЗАЧИЩЕН", FontRole.Heading, 24f, Role.Text, TextAlignmentOptions.Bottom);
            title.characterSpacing = 4f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            announce.Title = title;
            // Ромбы у надписи — спрайтом пака: знака ◆ нет ни в Philosopher, ни в Nunito, ни в запасном шрифте.
            announce.LeftMark = Mark(banner, "Ромб у надписи слева", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(.5f, .5f), new Vector2(-120f, 12f), 10f).rectTransform;
            announce.RightMark = Mark(banner, "Ромб у надписи справа", T.DiamondSmall, Role.PanelLine, 1f, new Vector2(.5f, .5f), new Vector2(120f, 12f), 10f).rectTransform;
            RectTransform lineBox = Box(Node("Строка", banner), new Vector2(.5f, .5f), new Vector2(.5f, 1f), new Vector2(0f, -6f), new Vector2(360f, 22f));
            TMP_Text line = LabelOn(lineBox, "Путь к выходу открыт", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Top);
            line.textWrappingMode = TextWrappingModes.NoWrap;
            announce.Line = line;
            foreach (float side in new[] { -1f, 1f })
            {
                RectTransform rule = Box(Node(side < 0f ? "Линейка слева" : "Линейка справа", banner), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(side * 168f, -16f), new Vector2(96f, 1.5f));
                var image = rule.gameObject.AddComponent<Image>();
                image.sprite = T.Pixel;
                image.raycastTarget = false;
                Tint(image, Role.PanelLine, .5f);
                Mark(rule, "Ромб", T.DiamondSmall, Role.PanelLine, .9f, new Vector2(side < 0f ? 1f : 0f, .5f), Vector2.zero, 8f);
            }
            banner.gameObject.SetActive(false);
        }
    }
}
