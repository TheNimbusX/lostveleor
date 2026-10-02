using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Экран «Выбери форму» (план форм 02.10, шаг 3) — та же стопка из трёх карточек, что у награды и
    /// артефакта босса. Тексты — RunHudFormChoice и PelagFormTexts, иконка — AbilityIconRules (база навыка
    /// с меткой формы, пока нет арта).
    ///
    /// ОСОБАЯ НАГРАДА («как-то красиво отмечено», без нового арта): карточка формы отмечена единственным
    /// оранжевым акцентом набора, а не цветом редкости — у форм редкости нет (лут 02.10). Акцентом — кольцо
    /// медальона и мягкое сияние за ним, строка вида «Форма навыка», огненная нить с камнем под карточкой и
    /// отсвет за медальоном (узлы миграции v5, WcRarity.MarkedOnly), перелив по карточке раз в несколько секунд и без мыши.
    /// Отказа и переброса нет; «Уйти с добычей» — как на обычной награде.
    /// </summary>
    public sealed partial class RunHud
    {
        /// <summary>Перелив акцентом по карточке формы, секунды между пробегами.</summary>
        const float FormGlintEvery = 3.4f;

        private void FillFormChoice(RiftRun run, bool letters)
        {
            RunHudView.SetActive(_view.Skip, false);
            RunHudView.SetText(_view.ChoiceTitle, PelagFormTexts.ScreenTitle);
            RunHudView.SetText(_view.ChoiceSubtitle, PelagFormTexts.ScreenSubtitle);
            RunHudView.SetText(_view.ChoiceHint, RunHudFormChoice.Hint(KeyLabel(0, letters), KeyLabel(1, letters), KeyLabel(2, letters),
                GameKeyBindings.Label(GameAction.LeaveRift)));
            Texture kindIcon = _view.KindIcons != null && _view.KindIcons.Length > 0 ? _view.KindIcons[0] : null;
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                RunHudFormChoice.Card form = i < RiftRun.RewardChoices ? RunHudFormChoice.Describe(run.GetOffer(i)) : default;
                // Пустое место (форм меньше трёх) не показывается — как недостающий артефакт.
                card.gameObject.SetActive(form.Shown);
                if (!form.Shown) continue;
                FillFormCard(card, form, KeyLabel(i, letters), kindIcon);
                PolishForm(i, card, form);
            }
        }

        /// <summary>
        /// Карточка формы: тексты, иконка с меткой формы, отметка акцентом и перелив. Одна на игру и кадр
        /// редактора (RunHudWcBuilder.Preview, Shot.Form) — кадр не расходится с экраном.
        /// </summary>
        public static void FillFormCard(RunOfferCard card, in RunHudFormChoice.Card form, string key, Texture kindIcon)
        {
            if (card == null) return;
            RunHudView.SetText(card.Title, form.Title);
            RunHudView.SetText(card.Kind, form.Kind);
            if (card.KindIcon != null && kindIcon != null)
            {
                card.KindIcon.texture = kindIcon;
                card.KindIcon.enabled = true;
            }
            RunHudView.SetText(card.Description, form.Body);
            RunHudView.SetText(card.ValueLabel, form.ValueLabel);
            RunHudView.SetText(card.Value, form.Value);
            RunHudView.SetKey(card.Key, key);
            card.SetIcon(form.DefinitionId >= 0 ? AbilityIcons.Get(form.DefinitionId, form.Form) : null, false);
            if (card.Rarity != null) card.Rarity.SetMarked(true);
            FormGlint(card, true);
        }

        /// <summary>Перелив акцентом раз в <see cref="FormGlintEvery"/> с у карточки формы; у остальных — только под мышью.</summary>
        static void FormGlint(RunOfferCard card, bool marked)
        {
            if (card == null || card.Glint == null) return;
            card.Glint.Repeat = marked;
            card.Glint.Every = marked ? FormGlintEvery : 0f;
            if (!marked || card.Glint.Stripe == null) return;
            Color colour = UiTheme.Current.Get(UiTheme.Role.Accent);
            colour.a = 0f;
            card.Glint.Stripe.color = colour;
            card.Glint.Peak = GlintPeak * GameUserSettings.FlashScale;
        }

        /// <summary>Обычные экраны: перелив формы снят (отметку акцентом снимает сама Rarity.Set).</summary>
        private void FormCardsOff()
        {
            for (int i = 0; i < _view.Offers.Length; i++) FormGlint(_view.Offers[i], false);
        }

        /// <summary>Описание на карточке с ключевыми словами; в подсказке справа — описание и правила формы.</summary>
        private void PolishForm(int index, RunOfferCard card, in RunHudFormChoice.Card form)
        {
            if (index >= Cards || card == null) return;
            _tipsEnabled = true;
            SetOfferBody(index, card, UiKeywords.Themed(form.Body), false);
            SetTip(index, card, form.Tip);
        }
    }
}
