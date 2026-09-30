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
        // Раскладка итогов со статистикой (кадр 4-summary-stats, «ок»): от центра экрана, в единицах 1920×1080 —
        // блок ~850 единиц в высоту, так что при масштабе интерфейса 120% (холст 900) он ещё целиком на экране.
        // Шапка (знак, «Гибель», нить, пояснение) — по центру; слева стоп-кадр в тлеющем круге, справа столбцы
        // статистики, «Убито», полосы «Потеряно» / «Остаётся» и кнопки.

        /// <summary>Середина правой части (статистика, убитые, полосы) от центра экрана по x и её ширина.</summary>
        const float BodyX = 210f, BodyWidth = 820f;
        /// <summary>Стоп-кадр: центр от центра экрана и поперечник круга.</summary>
        static readonly Vector2 FreezeAt = new Vector2(-440f, 20f);
        const float FreezeSize = 360f;
        /// <summary>Строки статистики: верх первой (от верха холста 1080), шаг, ширина столбца.</summary>
        const float StatsTop = 350f, StatsPitch = 48f, StatsColumn = 380f;
        const float KillsHeaderTop = 612f, KillPortraitTop = 668f, KillPortrait = 72f;
        const float BandsTop = 772f, BandHeight = 100f, BandWidth = 390f, BandOrb = 40f;
        const float SummaryButtonsTop = 922f;

        /// <summary>y от центра холста для точки <paramref name="top"/> единиц от верха (холст 1080).</summary>
        static float FromTop(float top) => 540f - top;

        static void PolishSummary(RunHudView view)
        {
            if (view.Summary == null) return;
            var screen = (RectTransform)view.Summary.transform;
            RecentreHeader(view, screen);
            HideOldSummary(view, screen);
            BuildFreeze(screen, view);
            BuildStats(screen, view);
            BuildKills(screen, view);
            view.SummaryLost = Band(screen, "Потеряно", BodyX - BandWidth * .5f - 20f, Role.Bad, true);
            view.SummaryKept = Band(screen, "Остаётся", BodyX + BandWidth * .5f + 20f, Role.Accent, false);
            PlaceSummaryButton(view.Repeat, view.RepeatLabel, BodyX - 180f, -200f, out view.RepeatKey);
            PlaceSummaryButton(view.ToCamp, view.ToCampLabel, BodyX + 180f, 200f, out view.CampKey);
        }

        /// <summary>Узел на месте сборки 26 сентября (сверху по центру, y = −<paramref name="top"/>)?</summary>
        static bool AtBuildPlace(RectTransform rect, float top, float x = 0f)
            => rect != null && rect.anchorMin == new Vector2(.5f, 1f) && rect.anchorMax == new Vector2(.5f, 1f)
               && Mathf.Abs(rect.anchoredPosition.y + top) < 2f && Mathf.Abs(rect.anchoredPosition.x - x) < 2f;

        /// <summary>Перенести узел со сборочного места сверху в новую раскладку от центра; руками переставленный — не трогать.</summary>
        static void Recentre(RectTransform rect, float oldTop, float newTop, float oldX, float newX, Vector2? size, string what)
        {
            if (rect == null) return;
            if (!AtBuildPlace(rect, oldTop, oldX))
            {
                Debug.LogWarning("[ui-kit] Итоги забега: «" + what + "» переставлен руками — в раскладке со статистикой остаётся на своём месте, проверить");
                return;
            }
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(newX, FromTop(newTop));
            if (size.HasValue) rect.sizeDelta = size.Value;
        }

        /// <summary>Шапка плотнее и выше: знак 100 вместо 164, заголовок 64 вместо 80 — под ней встаёт статистика.</summary>
        static void RecentreHeader(RunHudView view, RectTransform screen)
        {
            if (view.OutcomeHalo != null) Recentre(view.OutcomeHalo.rectTransform, 118f, 150f, 0f, 0f, new Vector2(180f, 180f), "свечение знака");
            if (view.OutcomeIcon != null) Recentre(view.OutcomeIcon.rectTransform, 118f, 150f, 0f, 0f, new Vector2(100f, 100f), "знак исхода");
            if (view.SummaryTitle != null && view.SummaryTitle.transform.parent is RectTransform title)
            {
                Recentre(title, 250f, 238f, 0f, 0f, null, "заголовок");
                if (Mathf.Approximately(view.SummaryTitle.fontSize, 80f)) view.SummaryTitle.fontSize = T.Size(UiTheme.TextStep.Display);
            }
            Recentre(screen.Find("Линия") as RectTransform, 314f, 286f, 0f, 0f, new Vector2(760f, 50f), "нить под заголовком");
            if (view.SummarySubtitle != null && view.SummarySubtitle.transform.parent is RectTransform subtitle)
                Recentre(subtitle, 350f, 314f, 0f, 0f, null, "пояснение");
        }

        /// <summary>Прежние четыре числа и строка потерь гаснут: их смысл — в статистике и полосах.</summary>
        static void HideOldSummary(RunHudView view, RectTransform screen)
        {
            // Все дети с этими именами, а не первый: у экрана есть и слой «Глубина» (тень панели, уже выключен), и
            // плашка «Глубина» — Find находил слой, и плашка оставалась поверх статистики. В префабе владельца первая
            // плашка называется «Разломов зачищено».
            var plates = new[] { "Арен зачищено", "Разломов зачищено", "Глубина", "Предметов", "Золота" };
            foreach (Transform child in screen)
                if (System.Array.IndexOf(plates, child.name) >= 0) child.gameObject.SetActive(false);
            // Плашки прежних чисел под другими именами (переименованы руками) — по ссылкам вида: число → «Число» → плашка.
            if (view.SummaryValues != null)
                foreach (TMP_Text value in view.SummaryValues)
                {
                    Transform plate = value != null && value.transform.parent != null ? value.transform.parent.parent : null;
                    if (plate != null && plate.parent == screen && plate != (Transform)screen) plate.gameObject.SetActive(false);
                }
            if (view.SummaryLoss != null && view.SummaryLoss.transform.parent != screen)
                view.SummaryLoss.transform.parent.gameObject.SetActive(false);
        }

        static RectTransform CentreBox(RectTransform parent, string name, Vector2 at, Vector2 size, Vector2? pivot = null)
            => Box(Node(name, parent), Center, pivot ?? Center, at, size);

        /// <summary>
        /// Стоп-кадр последнего удара: клуб глубокого дыма, тёмный диск, кадр в круглой маске и тлеющее кольцо
        /// огня вокруг (свет спокойный — сила .55); под кругом — подпись удара.
        /// </summary>
        static void BuildFreeze(RectTransform screen, RunHudView view)
        {
            RectTransform circle = CentreBox(screen, "Стоп-кадр", FreezeAt, new Vector2(FreezeSize, FreezeSize));
            UiInkKit.SmokeLayer(circle, "Дым", "smoke_ring", 1f, FreezeSize * .18f, FreezeSize * .18f, deep: true);
            Image disc = Layer(circle, "Диск", T.CircleFill, Role.SmokeDeep, 1f);
            disc.type = Image.Type.Simple;
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            RectTransform maskRect = Stretch(Node("Маска", circle), 6f);
            var mask = maskRect.gameObject.AddComponent<Image>();
            mask.sprite = T.CircleFill;
            mask.raycastTarget = false;
            maskRect.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var frame = Stretch(Node("Кадр", maskRect)).gameObject.AddComponent<RawImage>();
            frame.raycastTarget = false;
            frame.material = UiInkKit.Art;
            UiInkKit.Inked(frame, delay: .15f);
            frame.enabled = false;
            view.SummaryFreeze = frame;
            // Кольцо огня: нарисованное кольцо внутри спрайта — примерно две трети его ширины.
            UiInkKit.LightAt(circle, "Кольцо", "light_ring", Center, Vector2.zero, new Vector2(FreezeSize / .67f, FreezeSize / .67f), .55f, delay: .3f);

            RectTransform caption = CentreBox(screen, "Подпись кадра", new Vector2(FreezeAt.x, FromTop(740f)), new Vector2(460f, 30f));
            view.SummaryFreezeCaption = UiInkKit.Label(caption, "Надпись", "Последний удар · Лесной вендиго · 64", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text,
                TextAlignmentOptions.Center, 0f, .85f, .25f);
            view.SummaryFreezeCaption.textWrappingMode = TextWrappingModes.NoWrap;
            view.SummaryFreezeCaption.enableAutoSizing = true;
            view.SummaryFreezeCaption.fontSizeMin = 13f;
            view.SummaryFreezeCaption.fontSizeMax = T.Size(UiTheme.TextStep.Body);
            Box(UiInkKit.Divider(screen, "Нить подписи", 380f, false, .3f), Center, Center, new Vector2(FreezeAt.x, FromTop(762f)), new Vector2(380f, 16f));
        }

        /// <summary>
        /// Две колонки строк (RunHudSummary.Rows): белый знак набора забега, подпись приглушённо, число Nunito
        /// крупно (кадр рисовал антиквой — владелец: числа Nunito). Между колонками — тонкая черта.
        /// </summary>
        static void BuildStats(RectTransform screen, RunHudView view)
        {
            RunHudSummary.Stat[] rows = RunHudSummary.Rows;
            view.SummaryStats = new TMP_Text[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                bool left = i < RunHudSummary.LeftColumn;
                int line = left ? i : i - RunHudSummary.LeftColumn;
                float x = left ? BodyX - BodyWidth * .5f : BodyX + 20f;
                float top = StatsTop + line * StatsPitch;
                RectTransform row = Box(Node("Строка · " + RunHudSummary.Caption(rows[i]), screen), Center, new Vector2(0f, 1f),
                    new Vector2(x, FromTop(top)), new Vector2(StatsColumn, StatsPitch - 2f));
                RawImage icon = Pic(row, "Значок", RunHudSummary.Icon(rows[i]), new Vector2(0f, .5f), new Vector2(18f, 0f), 30f);
                icon.material = UiInkKit.Plain;
                UiInkKit.Inked(icon, delay: .1f);
                RectTransform caption = TopRow(row, "Подпись", 0f, 18f);
                caption.offsetMin = new Vector2(46f, caption.offsetMin.y);
                UiInkKit.Label(caption, "Надпись", RunHudSummary.Caption(rows[i]), FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.TextMuted, TextAlignmentOptions.MidlineLeft, .5f, 1f, .15f)
                    .textWrappingMode = TextWrappingModes.NoWrap;
                RectTransform value = TopRow(row, "Число", 17f, 28f);
                value.offsetMin = new Vector2(46f, value.offsetMin.y);
                TMP_Text number = UiInkKit.Label(value, "Надпись", "0", FontRole.Body, T.Size(UiTheme.TextStep.Heading), Role.Text, TextAlignmentOptions.MidlineLeft, 0f, 1f, .2f);
                number.fontStyle = FontStyles.Bold;
                number.textWrappingMode = TextWrappingModes.NoWrap;
                view.SummaryStats[i] = number;
            }
            // Тонкая черта между столбцами.
            float height = RunHudSummary.LeftColumn * StatsPitch - 10f;
            RectTransform rule = CentreBox(screen, "Черта столбцов", new Vector2(BodyX, FromTop(StatsTop + height * .5f)), new Vector2(1.5f, height));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.sprite = T.Pixel;
            ruleImage.raycastTarget = false;
            Tint(ruleImage, Role.PanelLine, .2f);
        }

        /// <summary>«Убито»: подпись между двумя нитями и шесть кругов — портрет вида (снимок из боя), «×N», имя.</summary>
        static void BuildKills(RectTransform screen, RunHudView view)
        {
            RectTransform header = CentreBox(screen, "Убито", new Vector2(BodyX, FromTop(KillsHeaderTop)), new Vector2(BodyWidth, 30f));
            UiInkKit.Label(header, "Надпись", "Убито", FontRole.Heading, T.Size(UiTheme.TextStep.Heading), Role.Text, TextAlignmentOptions.Center, 1f, 1f, .1f);
            Box(UiInkKit.Divider(header, "Нить слева", 300f, false, .35f), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(300f, 16f));
            Box(UiInkKit.Divider(header, "Нить справа", 300f, false, .35f), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-40f, 0f), new Vector2(300f, 16f));
            view.SummaryKillsHeader = header;

            view.SummaryKills = new RunHudView.SummaryKill[RunHudSummary.KillSlots];
            float pitch = BodyWidth / RunHudSummary.KillSlots;
            for (int i = 0; i < view.SummaryKills.Length; i++)
            {
                float x = BodyX - BodyWidth * .5f + pitch * (i + .5f);
                RectTransform slot = CentreBox(screen, "Убит " + (i + 1), new Vector2(x, FromTop(KillPortraitTop + 30f)), new Vector2(pitch, 136f));
                RawImage portrait = Medallion(slot, "Портрет", new Vector2(.5f, 1f), new Vector2(0f, -KillPortrait * .5f - 4f), KillPortrait, Role.PanelLine, .6f,
                    out RectTransform disc, out _);
                portrait.enabled = false;
                RawImage glyph = Pic(disc, "Знак", "encounter", Center, Vector2.zero, KillPortrait * .5f);
                Tint(glyph, Role.TextMuted);
                glyph.material = UiInkKit.Plain;
                UiInkKit.Inked(glyph, delay: .15f);
                RectTransform count = TopRow(slot, "Счёт", KillPortrait + 10f, 26f);
                TMP_Text countLabel = UiInkKit.Label(count, "Надпись", "×0", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text, TextAlignmentOptions.Center, 0f, 1f, .2f);
                countLabel.fontStyle = FontStyles.Bold;
                countLabel.textWrappingMode = TextWrappingModes.NoWrap;
                RectTransform name = TopRow(slot, "Имя", KillPortrait + 36f, 20f);
                TMP_Text nameLabel = UiInkKit.Label(name, "Надпись", "Корнеполз", FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .25f);
                nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
                nameLabel.enableAutoSizing = true;
                nameLabel.fontSizeMin = 10f;
                nameLabel.fontSizeMax = 14f;
                slot.gameObject.SetActive(false);
                view.SummaryKills[i] = new RunHudView.SummaryKill { Rect = slot, Portrait = portrait, Glyph = glyph, Count = countLabel, Name = nameLabel };
            }
        }

        /// <summary>
        /// Полоса «Потеряно» или «Остаётся» (кадр 4 и старый концепт итогов): без жёсткой рамки, в дыму — малая
        /// подложка без огненной нити, заголовок малыми капсом с разрядкой цвета смысла, слева круги вещей (у
        /// потерянных — красный крест), справа строки.
        /// </summary>
        static RunHudView.SummaryBand Band(RectTransform screen, string title, float x, Role role, bool lost)
        {
            RectTransform band = CentreBox(screen, lost ? "Потеряно" : "Остаётся", new Vector2(x, FromTop(BandsTop + BandHeight * .5f)),
                new Vector2(BandWidth, BandHeight));
            Image thread = UiInkKit.Plate(band, small: true);
            if (thread != null) Object.DestroyImmediate(thread.gameObject);
            RectTransform head = TopRow(band, "Заголовок", 4f, 22f);
            TMP_Text titleLabel = UiInkKit.Label(head, "Надпись", title.ToUpperInvariant(), FontRole.Body, T.Size(UiTheme.TextStep.Caption), role, TextAlignmentOptions.Center, 6f, 1f, .1f);
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            UiInkKit.LightAt(band, "Нить заголовка", "soft_blot", new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(BandWidth * .7f, 4f), 1f, delay: .15f)
                .color = Warm(.3f);

            const int orbs = 4;
            var items = new RunHudView.BandItem[orbs];
            for (int i = 0; i < orbs; i++)
            {
                RectTransform orb = LootOrb(band, "Вещь " + (i + 1), Vector2.zero);
                orb.anchorMin = orb.anchorMax = new Vector2(0f, 1f);
                orb.anchoredPosition = new Vector2(18f + BandOrb * .5f + i * (BandOrb + 6f), -38f - BandOrb * .5f);
                orb.sizeDelta = new Vector2(BandOrb, BandOrb);
                Graphic cross = null;
                if (lost)
                {
                    Image mark = Plain(orb, "Крест", T.Cross, T.Get(Role.Bad), new Vector2(1f, 0f), new Vector2(-3f, 5f), new Vector2(15f, 15f));
                    cross = mark;
                }
                orb.gameObject.SetActive(false);
                items[i] = new RunHudView.BandItem
                {
                    Rect = orb,
                    Art = orb.Find("Предмет")?.GetComponent<RawImage>(),
                    State = orb.GetComponent<WcSlotState>(),
                    Cross = cross,
                };
            }
            RectTransform lines = Node("Строки", band);
            lines.anchorMin = new Vector2(0f, 0f);
            lines.anchorMax = new Vector2(1f, 1f);
            lines.offsetMin = new Vector2(18f, 8f);
            lines.offsetMax = new Vector2(-14f, -34f);
            TMP_Text linesLabel = UiInkKit.Label(lines, "Надпись", lost ? "−64 золота" : "Опыт сохранён", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text,
                TextAlignmentOptions.MidlineLeft, 0f, 1f, .2f);
            linesLabel.enableAutoSizing = true;
            linesLabel.fontSizeMin = 12f;
            linesLabel.fontSizeMax = T.Size(UiTheme.TextStep.Body);
            linesLabel.lineSpacing = -8f;
            return new RunHudView.SummaryBand { Rect = band, Title = titleLabel, Items = items, Lines = linesLabel };
        }

        /// <summary>
        /// Кнопки итогов — вниз, под полосы, в новую раскладку от центра; справа в мазке — кейкап набора с кольцом
        /// блокировки (кадр 4: «Повторить [R]», «В лагерь [C]»). Надпись без «· R».
        /// </summary>
        static void PlaceSummaryButton(Button button, TMP_Text label, float x, float oldX, out RunHudView.KeyLock key)
        {
            key = new RunHudView.KeyLock();
            if (button == null) return;
            var rect = (RectTransform)button.transform;
            Recentre(rect, -SummaryButtonsY, SummaryButtonsTop, oldX, x, null, "кнопка «" + (label != null ? label.text : button.name) + "»");
            RectTransform cap = UiInkKit.Keycap(rect, "Клавиша", "R", 34f);
            cap.anchorMin = cap.anchorMax = new Vector2(1f, .5f);
            cap.pivot = new Vector2(1f, .5f);
            cap.anchoredPosition = new Vector2(-14f, 0f);
            TMP_Text letter = cap.Find("Буква").GetComponent<TMP_Text>();
            key = KitKey(letter, 34f);
            if (label != null)
            {
                label.rectTransform.offsetMax = new Vector2(label.rectTransform.offsetMax.x - 40f, label.rectTransform.offsetMax.y);
                label.text = label.text.Replace(" · R", "").Replace(" · C", "");
            }
        }
    }
}
