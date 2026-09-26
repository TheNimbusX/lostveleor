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
        /// полоса дыма с нитью света, слева круг значка с кольцом и светом цвета события, справа имя
        /// и строка. Столбик растёт вверх над эффектами зелий. Белые значки забега (золото)
        /// HudToasts красит кремовым, картинки вещей и заказов — как есть.
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
            // «Дым и свет»: вместо пилюли — полоса дыма, тающая вправо; по низу огненная нить.
            UiInkKit.SmokeLayer(pill, "Дым", "smoke_band_2", 1f, 44f, 22f, origin: new Vector2(0f, .5f));
            UiInkKit.LightAt(pill, "Нить", "light_thread", new Vector2(0f, 0f), new Vector2(120f, 0f), new Vector2(240f, 26f), .4f,
                origin: new Vector2(0f, .5f), delay: .2f);

            RectTransform circle = Box(Node("Круг", toast), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(ToastCircle * .5f + 4f, 0f),
                new Vector2(ToastCircle, ToastCircle));
            Image light = Layer(circle, "Свет", RoundGlow, Role.Rare, .5f, 12f);
            light.raycastTarget = false;
            UiInkKit.SmokeLayer(circle, "Клякса", "smoke_ring", 1f, 10f, 10f);
            Layer(circle, "Подложка", T.CircleFill, Role.Panel);
            var icon = Stretch(Node("Значок", circle), 6f).gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;
            Layer(circle, "Кольцо", T.CircleFrameBold, Role.Rare);

            float textX = ToastCircle + 14f;
            // Высота с запасом: у «многоточия» TMP строка выше коробки пропадает целиком.
            RectTransform titleBox = Box(Node("Имя", toast), new Vector2(0f, .5f), new Vector2(0f, 0f), new Vector2(textX, -4f), new Vector2(ToastWidth - textX - 20f, 28f));
            TMP_Text title = LabelOn(titleBox, "Кожаная куртка", FontRole.Body, 17f, Role.Text, TextAlignmentOptions.BottomLeft);
            UiInkKit.Revealed(title, .1f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform lineBox = Box(Node("Строка", toast), new Vector2(0f, .5f), new Vector2(0f, 1f), new Vector2(textX, -1f), new Vector2(ToastWidth - textX - 20f, 22f));
            TMP_Text line = LabelOn(lineBox, "Редкая · ур. 4", FontRole.Body, 13f, Role.Rare, TextAlignmentOptions.TopLeft);
            UiInkKit.Revealed(line, .2f);
            line.textWrappingMode = TextWrappingModes.NoWrap;
            line.overflowMode = TextOverflowModes.Ellipsis;

            // Всплывашки частые (золото с каждого врага) — без тлеющей кромки, как подсказки.
            UiInkKit.Group(toast, UiInkGroup.Sweep.LeftToRight, .4f, .15f).Burn = 0f;
            toast.gameObject.SetActive(false);
            toasts.Template = toast;
            toasts.Pitch = ToastHeight + 10f;
        }

        /// <summary>
        /// Узкий баннер сверху по центру (концепт 2Б, «Дым и свет»): клуб дыма, «РАЗЛОМ ЗАЧИЩЕН» антиквой
        /// с огоньками-ромбами по бокам, под ним строка и нить света (ромб — только мелкий свет).
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

            // «Дым и свет»: широкий клуб дыма, растекается от середины; под надписью — нить света с ромбом.
            UiInkKit.SmokeLayer(banner, "Дым", "smoke_band_1", 1f, 110f, 46f);
            UiInkKit.LightAt(banner, "Нить с ромбом", "light_thread_gem", new Vector2(.5f, .5f), new Vector2(0f, -30f), new Vector2(520f, 50f), .8f,
                delay: .25f);

            RectTransform titleBox = Box(Node("Надпись", banner), new Vector2(.5f, .5f), new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(440f, 34f));
            TMP_Text title = LabelOn(titleBox, "РАЗЛОМ ЗАЧИЩЕН", FontRole.Heading, 24f, Role.Text, TextAlignmentOptions.Bottom);
            UiInkKit.Revealed(title, .08f);
            title.characterSpacing = 4f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            announce.Title = title;
            // Ромбы у надписи — спрайтом пака: знака ◆ нет ни в Philosopher, ни в Nunito, ни в запасном шрифте.
            announce.LeftMark = UiInkKit.LightAt(banner, "Ромб у надписи слева", "light_gem", new Vector2(.5f, .5f), new Vector2(-120f, 12f), new Vector2(16f, 18f), .95f,
                delay: .3f).rectTransform;
            announce.RightMark = UiInkKit.LightAt(banner, "Ромб у надписи справа", "light_gem", new Vector2(.5f, .5f), new Vector2(120f, 12f), new Vector2(16f, 18f), .95f,
                delay: .3f).rectTransform;
            RectTransform lineBox = Box(Node("Строка", banner), new Vector2(.5f, .5f), new Vector2(.5f, 1f), new Vector2(0f, -6f), new Vector2(360f, 22f));
            TMP_Text line = LabelOn(lineBox, "Путь к выходу открыт", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Top);
            line.textWrappingMode = TextWrappingModes.NoWrap;
            UiInkKit.Revealed(line, .25f);
            announce.Line = line;
            // Большой момент — огонь можно, но медленный и мягкий (владелец 26 сентября: красная кромка
            // «очень-очень быстрая»): дым тлеет ровно почти секунду, кромка шире и тусклее.
            UiInkGroup group = UiInkKit.Group(banner, UiInkGroup.Sweep.FromCenter, .5f, .2f);
            group.InkDuration = .9f;
            group.Curve = UiInkGroup.Easing.Smooth;
            group.EdgeScale = 1.6f;
            group.Burn = .7f;
            banner.gameObject.SetActive(false);
        }
    }
}
