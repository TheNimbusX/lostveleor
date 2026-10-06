using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Тексты форм навыков Пелага (план форм 02.10). Как у талантов (SabreTalentTexts): бой знает только
    /// номера PelagForm, формулировки живут здесь. Формы и их суть — AGENTS/DESIGN.md «Пелаг — новая структура
    /// набора»: Вихрь — Буря (держать до 3 с), Водоворот (стягивает на 4 м и сбивает), Пенные волны (2 кольца
    /// до 5 м), Вихрь на ходу (убрана). Шквал (02.10) — Охота, Пенный след, Неуловимый. Абордаж (02.10) — Обвал,
    /// Гейзер, Пробоина. Крушение (03.10; 06.10 вечером) — Волнорез, Девятый вал, Призрачный якорь (вместо «Якорной брони»). Бросок якоря (03.10) — Невод,
    /// Веер, Гарпун. Остальные формы не утверждены.
    ///
    /// Описание — одна строка простым языком: на карточке экрана формы оно не переносится. Число на карточке —
    /// из утверждённого описания; когда у формы будет механика, его даст сборка.
    /// </summary>
    public static class PelagFormTexts
    {
        /// <summary>Экран «выбор формы»: заголовок и пояснение (как у экрана артефакта).</summary>
        public const string ScreenTitle = "Выбери форму";
        public const string ScreenSubtitle = "Навык меняется до конца забега · сменить нельзя";

        /// <summary>Строка вида на карточке формы.</summary>
        public const string Kind = "Форма навыка";

        /// <summary>Правила формы — в подсказке карточки, под описанием.</summary>
        public const string Rules = "Одна форма на навык за забег, сменить нельзя. Уберёшь навык из панели — форма и её таланты пропадут.";

        /// <summary>Таланты формы, пока у формы нет механики: карточка честно говорит, что числа ещё нет.</summary>
        public const string TalentPending = "Талант этой формы: число и действие придут вместе с механикой формы.";

        /// <summary>Утверждённое имя формы.</summary>
        public static string Name(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm: return "Буря";
                case PelagForm.WhirlwindMaelstrom: return "Водоворот";
                case PelagForm.WhirlwindFoamWaves: return "Пенные волны";
                case PelagForm.WhirlwindOnTheMove: return "Вихрь на ходу";
                case PelagForm.SquallHunt: return "Охота";
                case PelagForm.SquallFoamTrail: return "Пенный след";
                case PelagForm.SquallElusive: return "Неуловимый";
                case PelagForm.AbordageQuake: return "Обвал";
                case PelagForm.AbordageGeyser: return "Гейзер";
                case PelagForm.AbordageBreach: return "Пробоина";
                case PelagForm.WreckBreakwater: return "Волнорез";
                case PelagForm.WreckNinthWave: return "Девятый вал";
                case PelagForm.WreckGhostAnchor: return "Призрачный якорь";
                case PelagForm.AnchorThrowNet: return "Невод";
                case PelagForm.AnchorThrowFan: return "Веер";
                case PelagForm.AnchorThrowHarpoon: return "Гарпун";
                case PelagForm.None: return string.Empty;
                default: return "Форма " + (int)form;
            }
        }

        /// <summary>Что делает форма — одна строка.</summary>
        public static string Description(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm: return "Держи клавишу — Вихрь крутится до 3 секунд.";
                case PelagForm.WhirlwindMaelstrom: return "Вихрь подтягивает врагов с 4 м и сбивает их с ног.";
                case PelagForm.WhirlwindFoamWaves: return "Вихрь пускает два кольца пены до 5 м.";
                case PelagForm.WhirlwindOnTheMove: return "Вихрь не тормозит шаг — крутишься на полном ходу.";
                case PelagForm.SquallHunt: return "Прыжки к самым раненым; убийство даёт лишний прыжок.";
                case PelagForm.SquallFoamTrail: return "Прыжки оставляют пену: она бьёт и замедляет врагов.";
                case PelagForm.SquallElusive: return "Неуязвим в прыжках, последний возвращает на место.";
                case PelagForm.AbordageQuake: return "Кулак в землю: волна до 3 м сбивает с ног.";
                case PelagForm.AbordageGeyser: return "Апперкот: столб воды подбрасывает, вода падает.";
                case PelagForm.AbordageBreach: return "Удар насквозь: за целью бьёт струя воды на 4 м.";
                case PelagForm.WreckBreakwater: return "Стена воды на 8 м несёт врагов и обрушивается.";
                case PelagForm.WreckNinthWave: return "Мах по врагу заряжает выпад: полоса и урон до ×2, +2 м.";
                case PelagForm.WreckGhostAnchor: return "За выпадом падает призрачный якорь: круг 3 м, оглушение.";
                case PelagForm.AnchorThrowNet: return "За якорем сеть пены 3 м: ловит и тянет всю полосу.";
                case PelagForm.AnchorThrowFan: return "Три якоря веером: настоящий и два водяных под ±30°.";
                case PelagForm.AnchorThrowHarpoon: return "Вонзается в первого: ×2 урона, тянет даже элиту.";
                default: return string.Empty;
            }
        }

        /// <summary>Главное число формы на карточке: подпись…</summary>
        public static string ValueLabel(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm: return "Удержание";
                case PelagForm.WhirlwindMaelstrom: return "Подтягивает";
                case PelagForm.WhirlwindFoamWaves: return "Кольца пены";
                case PelagForm.WhirlwindOnTheMove: return "Скорость";
                case PelagForm.SquallHunt: return "Лишние прыжки";
                case PelagForm.SquallFoamTrail: return "Замедление";
                case PelagForm.SquallElusive: return "Неуязвимость";
                case PelagForm.AbordageQuake: return "Волна";
                case PelagForm.AbordageGeyser: return "В воздухе";
                case PelagForm.AbordageBreach: return "Струя";
                case PelagForm.WreckBreakwater: return "Стена";
                case PelagForm.WreckNinthWave: return "Заряд";
                case PelagForm.WreckGhostAnchor: return "Якорь";
                case PelagForm.AnchorThrowNet: return "Сеть";
                case PelagForm.AnchorThrowFan: return "Полосы";
                case PelagForm.AnchorThrowHarpoon: return "Укус";
                default: return string.Empty;
            }
        }

        /// <summary>…и само число (акцентом, как цена способности).</summary>
        public static string Value(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WhirlwindStorm: return "до 3 с";
                case PelagForm.WhirlwindMaelstrom: return "с 4 м";
                case PelagForm.WhirlwindFoamWaves: return "2 · до 5 м";
                case PelagForm.WhirlwindOnTheMove: return "полная";
                case PelagForm.SquallHunt: return "до 4";
                case PelagForm.SquallFoamTrail: return "−30% · 3 с";
                case PelagForm.SquallElusive: return "все прыжки";
                case PelagForm.AbordageQuake: return "3 м";
                case PelagForm.AbordageGeyser: return "0,8 с";
                case PelagForm.AbordageBreach: return "4 м";
                case PelagForm.WreckBreakwater: return "8 м · до 6";
                case PelagForm.WreckNinthWave: return "до ×2 · +2 м";
                case PelagForm.WreckGhostAnchor: return "3 м · ×1,5";
                case PelagForm.AnchorThrowNet: return "3 м · 50%";
                case PelagForm.AnchorThrowFan: return "3 · ±30°";
                case PelagForm.AnchorThrowHarpoon: return "×2";
                default: return string.Empty;
            }
        }

        /// <summary>Имя навыка формы с заглавной: «Вихрь». Линия без талантов (сабля) — «Сабля».</summary>
        public static string SkillName(PelagForm form)
        {
            int line = PelagForms.LineOf(form);
            if (line == PelagKit.SabreLine) return "Сабля";
            // У Броска якоря линии талантов пока нет (вторая очередь) — имя навыка своё.
            if (line == PelagKit.PoolIndexOf(AbilityDefinition.AnchorThrowId)) return "Бросок якоря";
            if (!SabreTalents.TryLineOf(line, out SabreTalentLine talents)) return "Навык";
            return Capitalized(SabreTalentTexts.LineName(talents));
        }

        /// <summary>
        /// Название на карточке: «Вихрь · Буря». Имя, в котором навык уже назван («Вихрь на ходу»), — как есть,
        /// без «Вихрь · Вихрь на ходу».
        /// </summary>
        public static string Title(PelagForm form) => Joined(SkillName(form), form);

        /// <summary>Заголовок подсказки плитки HUD: «ВИХРЬ · БУРЯ»; без формы — имя способности как было.</summary>
        public static string TooltipTitle(string abilityName, PelagForm form)
            => form == PelagForm.None || string.IsNullOrEmpty(abilityName) ? abilityName : Joined(abilityName, form).ToUpperInvariant();

        /// <summary>Описание способности с формой: сначала что даёт форма, потом сама способность. Без формы — как было.</summary>
        public static string WithForm(string abilityDescription, PelagForm form)
        {
            string formLine = Description(form);
            if (formLine.Length == 0) return abilityDescription;
            return "Форма «" + Name(form) + "». " + formLine + (string.IsNullOrEmpty(abilityDescription) ? "" : "\n" + abilityDescription);
        }

        /// <summary>Подсказка карточки формы: описание и правила.</summary>
        public static string Tip(PelagForm form) => Description(form) + "\n\n" + Rules;

        /// <summary>Таланты формы, пока без своих текстов: «Буря · талант 2».</summary>
        public static string TalentName(PelagForm form, int index) => Name(form) + " · талант " + (index + 1);

        static string Joined(string skill, PelagForm form)
        {
            string name = Name(form);
            if (name.Length == 0) return skill;
            return name.StartsWith(skill, System.StringComparison.OrdinalIgnoreCase) ? name : skill + " · " + name;
        }

        static string Capitalized(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string lower = text.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }
    }
}
