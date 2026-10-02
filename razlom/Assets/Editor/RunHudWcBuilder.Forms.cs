using System.Collections.Generic;
using Game.View;
using UnityEngine;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграция v5 экранов забега — карточка «выбор формы навыка» (план форм 02.10, шаг 3). Владелец 01.10:
    /// форма — «особая, заметно отмеченная награда». Без нового арта, деталями набора «Дым и свет»: у формы
    /// редкости нет (лут 02.10), поэтому отметка — единственный оранжевый акцент, а не цвет редкости.
    ///
    /// На каждую карточку награды — скрытая группа «Отметка формы» (её показывает WcRarity.MarkedOnly, когда
    /// RunHud отмечает карточку): огненная нить с камнем под карточкой вместо тусклой кремовой и тлеющий
    /// отсвет за медальоном. Вторая нить над верхней кромкой была в первом кадре — между карточками вышли
    /// двойные линии, убрана до записи v5. Кольцо медальона, мягкое сияние и
    /// строка вида красит акцентом сама WcRarity; перелив раз в несколько секунд — RunHud.Forms.
    /// Огонь здесь уместен: экран формы бывает раз-два за забег, а «успокоить огонь» относилось к частому экрану.
    /// Своя рамка и долгий дым — по листу концептов лута, только после «да» владельца.
    /// </summary>
    public static partial class RunHudWcBuilder
    {
        public const string FormMarkName = "Отметка формы";

        /// <summary>v5 — отметка формы навыка на трёх карточках награды; повторный запуск ничего не дублирует.</summary>
        static void MigrateTo5(RunHudView view)
        {
            int built = 0;
            foreach (RunOfferCard card in view.Offers)
            {
                if (card == null || card.Rarity == null) continue;
                if (FormMark(card)) built++;
            }
            Debug.Log("[ui-kit] Экраны забега: отметка формы навыка на карточках — " + built + ".");
        }

        static bool FormMark(RunOfferCard card)
        {
            var rect = (RectTransform)card.transform;
            Transform existing = rect.Find(FormMarkName);
            GameObject mark = existing != null ? existing.gameObject : BuildFormMark(rect);
            var marked = new List<GameObject>(card.Rarity.MarkedOnly ?? new GameObject[0]);
            if (!marked.Contains(mark)) marked.Add(mark);
            card.Rarity.MarkedOnly = marked.ToArray();
            mark.SetActive(card.Rarity.Marked);
            return existing == null;
        }

        static GameObject BuildFormMark(RectTransform card)
        {
            RectTransform mark = Stretch(Node(FormMarkName, card));
            // Сразу за подложкой карточки (дым и кремовая нить): под медальоном, текстом и светом наведения.
            Transform plateThread = card.Find("Нить");
            mark.SetSiblingIndex(plateThread != null ? plateThread.GetSiblingIndex() + 1 : 0);

            // Нить с камнем — та же, что под заголовком экрана, но под самой карточкой и ярче кремовой.
            float width = Mathf.Max(600f, card.sizeDelta.x * .74f);
            UiInkKit.LightAt(mark, "Огненная нить", "light_thread_gem", new Vector2(.5f, 0f), new Vector2(0f, -4f), new Vector2(width, 44f), .75f,
                delay: .2f);
            // Тлеющий отсвет только за медальоном (на всю карточку огонь ложился пятном под текст — 26.09).
            Transform icon = card.Find("Значок");
            Vector2 at = icon is RectTransform iconRect ? iconRect.anchoredPosition : new Vector2(100f, 0f);
            UiInkKit.LightAt(mark, "Отсвет", "light_glow", new Vector2(0f, .5f), at, new Vector2(200f, 200f), .3f, delay: .15f);

            mark.gameObject.SetActive(false);
            return mark.gameObject;
        }

        /// <summary>
        /// Кадр экрана формы (Shot.Form): три формы Вихря, как их бросает забег с одним Вихрем; карточки
        /// заполняет та же RunHud.FillFormCard, что в игре. Вторая — под мышью с подсказкой справа, по первой
        /// бежит перелив акцентом, у третьей ещё наполняется кольцо блокировки ввода.
        /// </summary>
        static void PreviewForm(RunHudView view)
        {
            view.ChoiceTitle.text = PelagFormTexts.ScreenTitle;
            view.ChoiceSubtitle.text = PelagFormTexts.ScreenSubtitle;
            view.ChoiceHint.text = RunHudFormChoice.Hint("1", "2", "3", "L");
            Game.Sim.PelagForm[] forms =
                { Game.Sim.PelagForm.WhirlwindStorm, Game.Sim.PelagForm.WhirlwindMaelstrom, Game.Sim.PelagForm.WhirlwindFoamWaves };
            for (int i = 0; i < view.Offers.Length && i < forms.Length; i++)
            {
                RunOfferCard card = view.Offers[i];
                RunHudFormChoice.Card form = RunHudFormChoice.Describe(Game.Sim.RewardOffer.OfForm(0, forms[i]));
                RunHud.FillFormCard(card, form, (i + 1).ToString(), view.KindIcons.Length > 0 ? view.KindIcons[0] : null);
                card.Description.text = UiKeywords.Themed(form.Body);
                RunHudView.KeyLock key = card.KeyLock;
                if (key?.Edge == null) continue;
                bool locked = i == 2;
                Color edge = UiTheme.Current.Get(locked ? UiTheme.Role.PanelLine : UiTheme.Role.Accent);
                edge.a = locked ? .3f : .85f;
                key.Edge.color = edge;
                if (key.Ring != null)
                {
                    key.Ring.gameObject.SetActive(locked);
                    key.Ring.fillAmount = .6f;
                }
            }
            // Перелив по первой карточке на середине пробега (в игре — раз в несколько секунд).
            RunOfferCard first = view.Offers[0];
            if (first.Glint != null)
            {
                first.Glint.Peak = .32f;
                first.Glint.Apply(.45f);
            }

            // Вторая — под мышью: приподнята, свет наведения, подсказка справа.
            RunOfferCard hoveredCard = view.Offers[1];
            var hovered = (RectTransform)hoveredCard.transform;
            hovered.anchoredPosition += new Vector2(0f, 8f);
            hovered.localRotation = Quaternion.Euler(0f, 0f, .8f);
            var motion = hoveredCard.GetComponent<UiHoverMotion>();
            if (motion != null && motion.HighlightGroup != null) motion.HighlightGroup.alpha = 1f;
            if (view.OfferTip == null) return;
            RunHudFormChoice.Card tip = RunHudFormChoice.Describe(Game.Sim.RewardOffer.OfForm(0, forms[1]));
            view.OfferTip.gameObject.SetActive(true);
            view.OfferTipTitle.text = tip.Title;
            view.OfferTipTitle.color = UiTheme.Current.Get(UiTheme.Role.Accent);
            view.OfferTipBody.text = UiKeywords.Themed(tip.Tip);
            view.OfferTip.anchoredPosition = new Vector2(510f, -(CardTop + CardPitch));
            if (view.KeywordTip == null) return;
            view.KeywordTip.gameObject.SetActive(true);
            UiKeywords.Entry pull = UiKeywords.Get(UiKeywords.Id.Pull);
            view.KeywordTipTitle.text = pull.Title;
            view.KeywordTipTitle.color = UiTheme.Current.Get(UiKeywords.ThemeRole(pull.Tone));
            view.KeywordTipBody.text = UiKeywords.ThemedDefinition(UiKeywords.Id.Pull);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.OfferTip);
            view.KeywordTip.anchoredPosition = new Vector2(546f, view.OfferTip.anchoredPosition.y - view.OfferTip.rect.height - 12f);
        }
    }
}
