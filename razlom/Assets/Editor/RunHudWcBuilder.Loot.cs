using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using LootLayout = Game.View.RunLootLedger.Layout;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class RunHudWcBuilder
    {
        const string LootName = "Добыча забега";
        /// <summary>Строка — чуть ниже нижней кромки панели состояния: там огненная нить панели.</summary>
        const float LootBelowStatus = 4f;
        /// <summary>Зазор между строкой и таймером выживания под ней.</summary>
        const float SurvivalGap = 12f;
        const float LootTipWidth = 300f;

        static readonly Vector2 TopLeftCorner = new Vector2(0f, 1f);
        static readonly Vector2 MiddleLeft = new Vector2(0f, .5f);

        /// <summary>
        /// Строка добычи (выбор владельца 30.09, кадр ART/UI/concepts-2026-09-30-hud-final/b1-loot-strip-under-panel):
        /// под панелью забега, по её левому краю — полоса дыма, тающая вправо (как всплывашки и кошелёк
        /// лагеря), знак золота в краске монет и число Nunito, тонкая кремовая черта и ряд кругов вещей
        /// «Дыма и света» (UiInkKit.SlotOrb — тот же круг, что надетая вещь в окнах лагеря: кольцо и свет
        /// редкости). Первое место ряда при нехватке места — круг «+K»; за последним кругом — «+1»
        /// оранжевым акцентом у новой вещи. Под строкой — малая подсказка, как у зелий боевого HUD.
        ///
        /// Ширину строки, число кругов и их картинки ведёт RunHud.Loot (правила — RunLootLedger).
        /// Строка появляется с панелью один раз за запуск (FirstTimeOnly: «HUD проявляется раз»); круг
        /// проявляется дымом только у новой вещи — своей группой без показа при включении.
        /// Мышь строка не ловит: наведение считает RunHud, клик уходит в мир.
        /// </summary>
        static RectTransform BuildLoot(RectTransform root, RunHudView view)
        {
            Vector2 at = new Vector2(24f, -138f);
            RectTransform status = view.Status;
            if (status != null && status.anchorMin == TopLeftCorner && status.anchorMax == TopLeftCorner)
            {
                float left = status.anchoredPosition.x - status.pivot.x * status.sizeDelta.x;
                float bottom = status.anchoredPosition.y - status.pivot.y * status.sizeDelta.y;
                at = new Vector2(left, bottom - LootBelowStatus);
            }
            else Debug.LogWarning("[ui-kit] Панель состояния забега привязана не к левому верхнему углу: строка добычи — на месте по умолчанию, проверить руками");

            RectTransform strip = Node(LootName, root);
            view.Loot = strip;
            strip.anchorMin = strip.anchorMax = TopLeftCorner;
            strip.pivot = TopLeftCorner;
            strip.anchoredPosition = at;
            strip.sizeDelta = new Vector2(LootLayout.Width(0f, 0), LootLayout.Height);
            // После панели и таймера, до полосы босса и экранов выбора: подсказка строки ложится поверх таймера.
            int after = Mathf.Max(status != null ? status.GetSiblingIndex() : -1, view.Survival != null ? view.Survival.GetSiblingIndex() : -1);
            if (after >= 0) strip.SetSiblingIndex(after + 1);

            UiInkKit.SmokeLayer(strip, "Дым", "smoke_band_2", 1f, 40f, 18f, origin: new Vector2(0f, .5f));
            UiInkGroup appear = UiInkKit.Group(strip, UiInkGroup.Sweep.LeftToRight, .45f, .18f);
            // Появляется с панелью один раз за запуск; огонь по кромке спокойный.
            appear.FirstTimeOnly = true;
            appear.Burn = .5f;

            view.LootCoin = Pic(strip, "Золото", "gold", TopLeftCorner, new Vector2(LootLayout.CoinX, -LootLayout.Height * .5f), LootLayout.Coin);
            Tint(view.LootCoin, Role.Coins);
            view.LootCoin.material = UiInkKit.Plain;
            UiInkKit.Inked(view.LootCoin, delay: .05f);

            RectTransform number = Node("Число", strip);
            number.anchorMin = number.anchorMax = TopLeftCorner;
            number.pivot = MiddleLeft;
            number.anchoredPosition = new Vector2(LootLayout.GoldX, -LootLayout.Height * .5f);
            number.sizeDelta = new Vector2(120f, 30f);
            view.LootGold = UiInkKit.Label(number, "Надпись", "0", FontRole.Body, 20f, Role.Text, TextAlignmentOptions.MidlineLeft, 0f, 1f, .12f);
            view.LootGold.fontStyle = FontStyles.Bold;
            view.LootGold.textWrappingMode = TextWrappingModes.NoWrap;

            // Черта между золотом и вещами — тонкая кремовая, как нити под карточками выбора.
            Image divider = UiInkKit.LightAt(strip, "Черта", "soft_blot", TopLeftCorner,
                new Vector2(LootLayout.DividerX(0f), -LootLayout.Height * .5f), new Vector2(4f, 30f), 1f, delay: .2f);
            divider.color = Warm(.45f);
            view.LootDivider = divider.rectTransform;
            view.LootDivider.gameObject.SetActive(false);

            RectTransform row = Node("Ряд", strip);
            row.anchorMin = row.anchorMax = TopLeftCorner;
            row.pivot = TopLeftCorner;
            row.anchoredPosition = new Vector2(LootLayout.RowStart(0f), 0f);
            row.sizeDelta = new Vector2(LootLayout.PoolSlots * LootLayout.Pitch, LootLayout.Height);
            view.LootRow = row;
            view.LootSlots = new RunHudView.LootSlot[LootLayout.PoolSlots];
            for (int i = 0; i < LootLayout.PoolSlots; i++) view.LootSlots[i] = LootSlot(row, i);

            // «+K» — тот же круг без картинки, число внутри; стоит на первом месте, ряд едет за ним.
            RectTransform more = LootOrb(strip, "Ещё", new Vector2(LootLayout.RowStart(0f) + LootLayout.Icon * .5f, 0f));
            TMP_Text moreLabel = UiInkKit.Label(more, "Число", "+3", FontRole.Body, 15f, Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .1f);
            moreLabel.fontStyle = FontStyles.Bold;
            moreLabel.textWrappingMode = TextWrappingModes.NoWrap;
            view.LootMore = more;
            view.LootMoreLabel = moreLabel;
            view.LootMoreInk = LootInk(more);
            more.gameObject.SetActive(false);

            // «+1» новой вещи — в ряду, за последним кругом; прозрачность и подъём ведёт RunHud.
            RectTransform arrival = Node("Новая", row);
            arrival.anchorMin = arrival.anchorMax = MiddleLeft;
            arrival.pivot = MiddleLeft;
            arrival.anchoredPosition = new Vector2(LootLayout.Icon + 4f, 0f);
            arrival.sizeDelta = new Vector2(LootLayout.ArrivalReserve + 14f, 28f);
            view.LootArrival = Label(arrival, "Надпись", "+1", FontRole.Body, 18f, Role.Accent, TextAlignmentOptions.MidlineLeft);
            view.LootArrival.fontStyle = FontStyles.Bold;
            view.LootArrival.textWrappingMode = TextWrappingModes.NoWrap;
            view.LootArrival.gameObject.SetActive(false);

            view.LootFallbackIcon = Icon("items");
            BuildLootTip(strip, view);
            strip.gameObject.SetActive(false);
            return strip;
        }

        /// <summary>Круг «Дыма и света» в ряду: мышь не ловит (наведение считает RunHud).</summary>
        static RectTransform LootOrb(RectTransform parent, string name, Vector2 position)
        {
            RectTransform orb = UiInkKit.SlotOrb(parent, name, LootLayout.Icon);
            orb.anchorMin = orb.anchorMax = MiddleLeft;
            orb.pivot = new Vector2(.5f, .5f);
            orb.anchoredPosition = position;
            Transform catcher = orb.Find("Ловец");
            Image hit = catcher != null ? catcher.GetComponent<Image>() : null;
            if (hit != null) hit.raycastTarget = false;
            return orb;
        }

        /// <summary>Своя группа круга: проявление дымом по вызову RunHud (новая вещь), не при каждом показе строки.</summary>
        static UiInkGroup LootInk(RectTransform orb)
        {
            UiInkGroup ink = UiInkKit.Group(orb, UiInkGroup.Sweep.FromCenter, .45f, .08f);
            ink.PlayOnEnable = false;
            ink.Burn = .3f;
            return ink;
        }

        static RunHudView.LootSlot LootSlot(RectTransform row, int index)
        {
            RectTransform orb = LootOrb(row, "Вещь " + (index + 1), new Vector2(LootLayout.Icon * .5f + index * LootLayout.Pitch, 0f));
            var group = orb.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Вспышка цвета редкости у новой вещи: мягкое круглое сияние за диском (цвет и силу ставит RunHud).
            RectTransform flashRect = Stretch(Node("Вспышка", orb), -LootLayout.Icon * .35f);
            var flash = flashRect.gameObject.AddComponent<Image>();
            flash.sprite = RoundGlow;
            flash.raycastTarget = false;
            flash.color = new Color(1f, 1f, 1f, 0f);
            flash.enabled = false;
            Transform smoke = orb.Find("Дым");
            flashRect.SetSiblingIndex(smoke != null ? smoke.GetSiblingIndex() + 1 : 0);

            var slot = new RunHudView.LootSlot
            {
                Rect = orb,
                Group = group,
                Art = orb.Find("Предмет")?.GetComponent<RawImage>(),
                State = orb.GetComponent<WcSlotState>(),
                Ink = LootInk(orb),
                Flash = flash,
            };
            orb.gameObject.SetActive(false);
            return slot;
        }

        /// <summary>
        /// Подсказка строки: малая подложка «Дыма и света», как у зелий боевого HUD (без огня по кромке:
        /// она всплывает на каждом наведении). Имя антиквой цвета редкости, строка «Редкая · ур. 4»,
        /// под ней при нужде примечание красным.
        /// </summary>
        static void BuildLootTip(RectTransform strip, RunHudView view)
        {
            RectTransform tip = Node("Подсказка добычи", strip);
            tip.anchorMin = tip.anchorMax = TopLeftCorner;
            tip.pivot = new Vector2(.5f, 1f);
            tip.anchoredPosition = new Vector2(LootTipWidth * .5f, -LootLayout.Height - 6f);
            tip.sizeDelta = new Vector2(LootTipWidth, 80f);
            UiInkKit.Plate(tip, small: true);
            UiInkKit.Group(tip, UiInkGroup.Sweep.FromCenter, .3f, .08f).Burn = 0f;
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(18, 18, 10, 12);
            column.spacing = 2f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view.LootTipTitle = TipLabel(tip, "Название", "Кожаная куртка", FontRole.Heading, 20f, Role.Text, .04f);
            view.LootTipLine = TipLabel(tip, "Строка", "Редкая · ур. 4", FontRole.Body, 15f, Role.TextMuted, .1f);
            view.LootTipNote = TipLabel(tip, "Примечание", "В сумке лагеря нет места — вещь пропадёт", FontRole.Body, 14f, Role.Bad, .14f);
            view.LootTipNote.gameObject.SetActive(false);
            view.LootTip = tip;
            tip.gameObject.SetActive(false);
        }

        /// <summary>Надпись прямо на узле подсказки: раскладка берёт высоту строки у самого текста.</summary>
        static TMP_Text TipLabel(RectTransform tip, string name, string text, FontRole font, float size, Role role, float delay)
        {
            RectTransform rect = Node(name, tip);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            UiInkKit.Revealed(label, delay);
            return label;
        }

        /// <summary>
        /// Таймер выживания стоял сразу под панелью — теперь там строка добычи: таймер опускается под неё.
        /// Уже ниже строки (правка руками) или привязан иначе — не трогаем.
        /// </summary>
        static void MakeRoomUnderLoot(RunHudView view, RectTransform strip)
        {
            RectTransform survival = view.Survival;
            if (survival == null || strip == null) return;
            if (survival.anchorMin != strip.anchorMin || survival.anchorMax != strip.anchorMax)
            {
                Debug.LogWarning("[ui-kit] Таймер выживания привязан не так, как строка добычи: не сдвинут, проверить руками");
                return;
            }
            float stripBottom = strip.anchoredPosition.y - strip.pivot.y * strip.sizeDelta.y;
            float top = survival.anchoredPosition.y + (1f - survival.pivot.y) * survival.sizeDelta.y;
            float want = stripBottom - SurvivalGap;
            if (top <= want) return;
            survival.anchoredPosition += new Vector2(0f, want - top);
        }
    }
}
