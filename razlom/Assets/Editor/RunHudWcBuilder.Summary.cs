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
        /// Итог забега в материале «Дым и свет» (владелец 25 сентября, концепт 6-summary-C-smoke):
        /// колонна чернильного дыма посреди потемневшего мира, плашек нет — значки, числа и подписи
        /// прямо на дыму, между столбцами тонкие черты. Исход крупной антиквой (цвет задаёт RunHud:
        /// гибель — красный, победа — оранжевый), под ним нить света с ромбом; потери и кнопки
        /// «Повторить» (оранжевый мазок) и «В лагерь» (тёмный мазок с нитью). Строк «В лагере ждёт»
        /// больше нет (владелец 26 сентября: «не нравятся эти строки вовсе») — кнопки поднялись.
        /// Появление — сверху вниз, после паузы RunEndBeat: дым ведёт и медленно тлеет, за ним
        /// проявляются буквы, числа досчитываются, когда цифры уже видны (RunHud.CountSummary).
        /// </summary>
        static void BuildSummary(RectTransform root, RunHudView view)
        {
            RectTransform screen = Screen(root, "Итог забега", out view.Summary);
            // Как в концепте: мир под итогами притемнён, но виден, а колонна дыма — почти чёрная.
            // Под плотной вуалью дым читался бы серым облаком светлее фона.
            screen.Find("Вуаль").GetComponent<ThemeColor>().SetRole(Role.Veil, .62f);
            screen.Find("Глубина").gameObject.SetActive(false);
            // Дым ведёт: отрицательная задержка снимает его место в лесенке — оба клуба встают первыми,
            // раньше значка и заголовка, которые на нём лежат.
            UiInkKit.SmokeAt(screen, "Дым", "smoke_blot_1", new Vector2(.5f, .5f), new Vector2(0f, 20f), new Vector2(1420f, 1180f), 1f,
                origin: new Vector2(.5f, .85f), delay: -.4f, deep: true);
            UiInkKit.SmokeAt(screen, "Дым плотнее", "smoke_plate", new Vector2(.5f, 1f), new Vector2(0f, -470f), new Vector2(1320f, 520f), 1f,
                delay: -.35f, deep: true);
            // Владелец 26 сентября: красное проявление перед «Ушёл с добычей» пролетало за треть секунды
            // и читалось вспышкой. Итоги — большой момент, огонь остаётся, но тлеет: дым встаёт дольше
            // текста и ровным ходом (фронт идёт всё проявление), кромка шире, тусклее и гаснет хвостом.
            UiInkGroup appear = UiInkKit.Group(screen, UiInkGroup.Sweep.TopToBottom, .7f, .8f);
            appear.StartDelay = .1f;
            appear.InkDuration = 1.2f;
            appear.Curve = UiInkGroup.Easing.Smooth;
            appear.EdgeScale = 2.2f;
            appear.Burn = .85f;
            UiEmbers embers = UiInkKit.Embers(screen, "Угли", new Vector2(.5f, 0f), new Vector2(0f, 200f), new Vector2(1400f, 400f), 3f);
            embers.Speed = new Vector2(30f, 70f);

            // Исход крупным знаком: сломанная сабля, сабля в лучах, мешок с добычей. Знаки белые, краску
            // ставит RunHud: гибель — приглушённый красный, победа и уход — тёплые; свечение за ним — тоже.
            Image halo = Mark(screen, "Свечение значка", T.Blob, Role.Accent, .18f, new Vector2(.5f, 1f), new Vector2(0f, -118f), 260f);
            halo.preserveAspect = false;
            view.OutcomeHalo = halo;
            view.OutcomeIcon = Pic(screen, "Значок исхода", "victory", new Vector2(.5f, 1f), new Vector2(0f, -118f), 164f);
            Tint(view.OutcomeIcon, Role.Accent);
            view.OutcomeIcon.material = UiInkKit.Plain;
            UiInkKit.Inked(view.OutcomeIcon);
            view.OutcomeIcons = new[] { Icon("death"), Icon("victory"), Icon("exit") };
            view.SummaryTitle = Title(screen, "Победа", 250f, 80f);
            UiInkKit.Revealed(view.SummaryTitle, .1f);
            UiInkKit.LightAt(screen, "Линия", "light_thread_gem", new Vector2(.5f, 1f), new Vector2(0f, -314f), new Vector2(900f, 60f), .8f, delay: .2f);
            view.SummarySubtitle = Line(screen, "Пояснение", "Локация пройдена", 350f, 21f, Role.TextMuted);
            UiInkKit.Revealed(view.SummarySubtitle, .15f);

            string[] captions = { "Арен зачищено", "Глубина", "Предметов", "Золота" };
            string[] icons = { "rift", "depth", "items", "gold" };
            view.SummaryValues = new TMP_Text[captions.Length];
            const float w = 230f, gap = 24f;
            for (int i = 0; i < captions.Length; i++)
            {
                RectTransform plate = Box(Node(captions[i], screen), new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                    new Vector2((i - 1.5f) * (w + gap), -392f), new Vector2(w, 176f));
                if (i > 0)
                {
                    // Тонкая черта между столбцами вместо плашек.
                    RectTransform rule = Box(Node("Черта", plate), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(-gap * .5f, 6f), new Vector2(1.5f, 120f));
                    var ruleImage = rule.gameObject.AddComponent<Image>();
                    ruleImage.sprite = T.Pixel;
                    ruleImage.raycastTarget = false;
                    Tint(ruleImage, Role.PanelLine, .22f);
                }
                RawImage pic = Pic(plate, "Значок", icons[i], new Vector2(.5f, 1f), new Vector2(0f, -40f), 62f);
                pic.material = UiInkKit.Plain;
                UiInkKit.Inked(pic);
                RectTransform number = Box(Node("Число", plate), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -72f), new Vector2(w, 60f));
                view.SummaryValues[i] = UiInkKit.Label(number, "Надпись", "0", FontRole.Heading, 54f, i == 3 ? Role.Coins : Role.Text, TextAlignmentOptions.Center);
                RectTransform caption = Box(Node("Подпись", plate), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 14f), new Vector2(w - 16f, 28f));
                UiInkKit.Label(caption, "Надпись", captions[i], FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center, delay: .2f);
            }

            view.SummaryLoss = Line(screen, "Потери", "", 606f, 18f, Role.Bad);
            UiInkKit.Revealed(view.SummaryLoss);

            // Кнопки сразу под потерями (раньше между ними стояли строки «В лагере ждёт»), шире разведены:
            // мазок под «Повторить» стал больше и при ±180 наезжал бы на соседний.
            view.Repeat = SummaryButton(screen, true, "Повторить · R", -200f, out view.RepeatLabel);
            ButtonIcon((RectTransform)view.Repeat.transform, "repeat", 44f, true, Role.TextOnAccent);
            view.ToCamp = SummaryButton(screen, false, "В лагерь · C", 200f, out view.ToCampLabel);
            ButtonIcon((RectTransform)view.ToCamp.transform, "camp", 44f, true);
        }

        /// <summary>Высота кнопок итогов: под строкой потерь (y −606), внутри плотного дыма (до −730).</summary>
        const float SummaryButtonsY = -700f;

        static Button SummaryButton(RectTransform screen, bool primary, string text, float x, out TMP_Text label)
        {
            RectTransform button = UiInkKit.Button(screen, text, text, primary, new Vector2(320f, 64f));
            Box(button, new Vector2(.5f, 1f), Center, new Vector2(x, SummaryButtonsY), new Vector2(320f, 64f));
            if (primary)
            {
                // Владелец 26 сентября: подложку под «Повторить» увеличить. Кисть сужается вправо: при
                // прежних полях буквы были вровень с полосой краски, а «· R» стояло на тонком хвосте.
                // Мазок выше и длиннее вправо; свет — по тому же прямоугольнику, а не своим.
                foreach (string part in new[] { "Мазок", "Свет" })
                {
                    var layer = button.Find(part) as RectTransform;
                    if (layer == null) continue;
                    layer.offsetMin = new Vector2(-32f, -22f);
                    layer.offsetMax = new Vector2(40f, 22f);
                }
            }
            label = button.GetComponentInChildren<TMP_Text>();
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }
    }
}
