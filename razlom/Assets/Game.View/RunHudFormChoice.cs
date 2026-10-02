using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Экран «Выбери форму» без Unity (план форм 02.10, шаг 3): что пишется на карточке формы и видна ли она.
    /// Заполняет экран RunHud.FillFormChoice по образцу экрана артефакта; проверяют тесты представления
    /// (RunHudFormChoiceTests) на настоящем забеге из Game.Sim.
    ///
    /// Карточка: «Вихрь · Буря», строка вида «Форма навыка», описание в одну строку, главное число формы и
    /// иконка навыка с меткой формы (AbilityIconRules). Пустое место экрана (форма None, чужая линия) не
    /// показывается — как недостающий артефакт. Отказа и переброса нет: форма выбирается одна и навсегда.
    /// </summary>
    public static class RunHudFormChoice
    {
        /// <summary>Тексты одной карточки формы.</summary>
        public struct Card
        {
            public bool Shown;
            public int Line;
            public PelagForm Form;
            /// <summary>Способность линии для иконки; у сабли (своей иконки ещё нет) — −1.</summary>
            public int DefinitionId;
            public string Title, Kind, Body, ValueLabel, Value, Tip;
        }

        /// <summary>Карточка по награде экрана формы; не форма или пустое место — <see cref="Card.Shown"/> = false.</summary>
        public static Card Describe(in RewardOffer offer)
        {
            var card = new Card { Line = offer.PoolIndex, DefinitionId = -1 };
            PelagForm form = offer.Form;
            if (offer.Kind != RewardKind.Form || !PelagForms.IsValid(form) || PelagForms.LineOf(form) != offer.PoolIndex) return card;
            AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
            card.Shown = true;
            card.Form = form;
            card.DefinitionId = definition != null ? definition.Id : -1;
            card.Title = PelagFormTexts.Title(form);
            card.Kind = PelagFormTexts.Kind;
            card.Body = PelagFormTexts.Description(form);
            card.ValueLabel = PelagFormTexts.ValueLabel(form);
            card.Value = PelagFormTexts.Value(form);
            card.Tip = PelagFormTexts.Tip(form);
            return card;
        }

        /// <summary>Сколько карточек экрана формы видно (пустые места не считаются).</summary>
        public static int ShownCount(RiftRun run)
        {
            if (run == null || !run.ChoosingForm) return 0;
            int shown = 0;
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (Describe(run.GetOffer(i)).Shown) shown++;
            return shown;
        }

        /// <summary>Подсказка клавиш — один формат на все окна (лист 5): «[1] [2] [3] Выбрать   ·   [L] Уйти с добычей».</summary>
        public static string Hint(string key1, string key2, string key3, string leave)
            => UiKeyHint.Join(UiKeyHint.Hint("выбрать", key1, key2, key3), UiKeyHint.Hint("уйти с добычей", leave));
    }
}
