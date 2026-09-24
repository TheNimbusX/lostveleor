using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class RunHudWcBuilder
    {
        /// <summary>
        /// Итог забега: исход крупной антиквой (цвет задаёт RunHud: гибель — красный,
        /// победа — оранжевый), четыре плашки со счётом, потери, что ждёт в лагере,
        /// кнопки «Повторить» и «В лагерь» с их клавишами.
        /// </summary>
        static void BuildSummary(RectTransform root, RunHudView view)
        {
            RectTransform screen = Screen(root, "Итог забега", out view.Summary);
            // Исход крупным рисованным значком: сломанная сабля, сабля в лучах, мешок с добычей.
            Image halo = Mark(screen, "Свечение значка", T.Blob, Role.Accent, .18f, new Vector2(.5f, 1f), new Vector2(0f, -118f), 260f);
            halo.preserveAspect = false;
            view.OutcomeIcon = Pic(screen, "Значок исхода", "victory", new Vector2(.5f, 1f), new Vector2(0f, -118f), 164f);
            view.OutcomeIcons = new[] { Icon("death"), Icon("victory"), Icon("exit") };
            view.SummaryTitle = Title(screen, "Победа", 250f, 80f);
            Box(Place("Divider", screen, "Линия"), new Vector2(.5f, 1f), Center, new Vector2(0f, -314f), new Vector2(860f, 16f));
            view.SummarySubtitle = Line(screen, "Пояснение", "Локация пройдена", 350f, 21f, Role.TextMuted);

            string[] captions = { "Разломов зачищено", "Глубина", "Предметов", "Золота" };
            string[] icons = { "rift", "depth", "items", "gold" };
            view.SummaryValues = new TMP_Text[captions.Length];
            const float w = 230f, gap = 24f;
            for (int i = 0; i < captions.Length; i++)
            {
                RectTransform plate = Box(Node(captions[i], screen), new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                    new Vector2((i - 1.5f) * (w + gap), -392f), new Vector2(w, 176f));
                Image shadow = Layer(plate, "Тень", T.GlowSmall, Role.Veil, .85f, 24f);
                shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
                Layer(plate, "Заливка", T.FillSmall, Role.Panel);
                Layer(plate, "Тень снизу", T.ShadeSmall, Role.Veil, .5f);
                Layer(plate, "Свет по кромке", T.HighlightSmall, Role.Highlight);
                Layer(plate, "Рамка", T.FrameSmall, Role.PanelLine, .85f);
                Pic(plate, "Значок", icons[i], new Vector2(.5f, 1f), new Vector2(0f, -40f), 62f);
                RectTransform number = Box(Node("Число", plate), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -72f), new Vector2(w, 60f));
                view.SummaryValues[i] = Label(number, "Надпись", "0", FontRole.Heading, 54f, i == 3 ? Role.Coins : Role.Text, TextAlignmentOptions.Center);
                RectTransform caption = Box(Node("Подпись", plate), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 14f), new Vector2(w - 16f, 28f));
                Label(caption, "Надпись", captions[i], FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center);
            }

            view.SummaryLoss = Line(screen, "Потери", "", 606f, 18f, Role.Bad);
            view.SummaryCampTitle = Line(screen, "В лагере", "В лагере ждёт", 656f, 26f, Role.Text);
            view.SummaryCampTitle.GetComponent<ThemeFont>().Role = FontRole.Heading;
            view.SummaryCampTitle.GetComponent<ThemeFont>().Apply();
            RectTransform campBox = Box(Node("Что ждёт", screen), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -678f), new Vector2(1000f, 70f));
            view.SummaryCamp = Label(campBox, "Надпись", "Ничего. Значит, повторяй.", FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.Top);

            view.Repeat = SummaryButton(screen, "ButtonPrimary", "Повторить · R", -180f, out view.RepeatLabel);
            ButtonIcon((RectTransform)view.Repeat.transform, "repeat", 44f);
            view.ToCamp = SummaryButton(screen, "ButtonSecondary", "В лагерь · C", 180f, out view.ToCampLabel);
            ButtonIcon((RectTransform)view.ToCamp.transform, "camp", 44f);
        }

        static Button SummaryButton(RectTransform screen, string prefab, string text, float x, out TMP_Text label)
        {
            RectTransform button = Place(prefab, screen, text);
            Box(button, new Vector2(.5f, 1f), Center, new Vector2(x, -812f), new Vector2(320f, 64f));
            label = button.GetComponentInChildren<TMP_Text>();
            label.text = text;
            button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }
    }
}
