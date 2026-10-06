using Game.Sim;

namespace Game.View
{
    /// <summary>Вкладки кузницы Эни (temper-a.png, 06.10). Порядок — порядок вкладок окна; первые четыре совпадают с EniAction.</summary>
    public enum EniTab { Temper, Remelt, Add, Heart, Dismantle }

    /// <summary>Что сейчас делает основная кнопка вкладки (и почему она спит).</summary>
    public enum TemperStep
    {
        /// <summary>Вещь не выбрана: наковальня пустая, кнопки спят.</summary>
        NoItem,
        /// <summary>Вкладка закрыта рангом лагеря: замок и «Эни · ранг N».</summary>
        Locked,
        /// <summary>Действие нельзя: причина в Status (расколота, шедевр, предел, ждёт выбор…).</summary>
        Blocked,
        /// <summary>Первый удар закалки: оплата, риск 0%.</summary>
        Strike,
        /// <summary>Следующий удар той же закалки: бесплатно, риск растёт; «Взять» рядом.</summary>
        NextStrike,
        /// <summary>Свойство на пределе в открытой закалке: удар спит, «Взять» горит.</summary>
        AtMaximum,
        /// <summary>Попытки кончились: рискованный удар (шедевр или осколки) — только вторым нажатием.</summary>
        Risky,
        /// <summary>Переплавка или добавление до оплаты.</summary>
        Pay,
        /// <summary>Оплаченные три варианта: выбор обязателен.</summary>
        Choose,
        /// <summary>Сердце босса: выбрать грань и вплавить.</summary>
        Heart,
        /// <summary>Разбор вещи из сумки — только вторым нажатием.</summary>
        Dismantle,
    }

    /// <summary>Отметка попытки закалки под вещью.</summary>
    public enum TemperPip { Hidden, Free, Spent, Crack }

    /// <summary>
    /// Правила вкладок кузницы Эни без Unity (проверяются в Combat.Presentation.Tests): какая вкладка открыта рангом, что
    /// делает основная кнопка, цена и нехватка по каждой валюте, риск для дуги, отметки попыток. Цена и допуск берутся из
    /// Camp.Quote — окно и кузница не могут разойтись. Сами действия делает Camp, здесь только предпросмотр.
    /// </summary>
    public static class CampTemperRules
    {
        public const int TabCount = 5;
        /// <summary>Ранг, с которого в вещь входит второе сердце (Camp.HeartSlots).</summary>
        public const int SecondHeartRank = 3;
        /// <summary>Сколько отметок попыток у окна: больше всех у эпической (Camp.TemperAttempts — 4).</summary>
        public const int MaxPips = 4;
        /// <summary>Защита от двойного удара: ввод спит, пока молот опускается (двойное E дало бы второй удар с риском).</summary>
        public const float StrikeLockSeconds = .35f;

        /// <summary>
        /// Ранг лагеря, с которого открыта вкладка (06.10): закалка и разбор — с начала, переплавка и сердце — 1,
        /// добавление — 2. Числа повторяют Camp.EniActionUnlocked; тест сверяет их на рангах 0–3, чтобы подпись замка не врала.
        /// </summary>
        public static int TabRank(EniTab tab) => tab == EniTab.Remelt || tab == EniTab.Heart ? 1 : tab == EniTab.Add ? 2 : 0;

        /// <summary>Вкладка открыта: действие Эни по рангу; разбор — пока у лагеря есть кузнец.</summary>
        public static bool TabOpen(Camp camp, EniTab tab)
            => tab == EniTab.Dismantle ? camp.Has(CampService.Smith) : camp.EniActionUnlocked((EniAction)tab);

        /// <summary>
        /// Заполнение огня дуги «Риск трещины»: дуга — верхняя половина круга (Filled Radial360 от левого края), поэтому
        /// 100% — это половина круга. Вне 0..100 — к краю.
        /// </summary>
        public static float RiskFill(int riskPercent) => .5f * System.Math.Max(0, System.Math.Min(100, riskPercent)) / 100f;

        /// <summary>С 50% риск красный: дальше вещь трескается или рассыпается так же часто, как выживает.</summary>
        public static bool RiskIsBad(int riskPercent) => riskPercent >= 50;

        /// <summary>
        /// Отметка <paramref name="index"/> под вещью: первые — трещины (они тоже съели попытку), за ними — потраченные
        /// удачные попытки, остальные до лимита редкости — свободные, сверх лимита — скрыты.
        /// </summary>
        public static TemperPip Pip(int index, int used, int limit, int cracks)
        {
            if (index < 0 || index >= limit || index >= MaxPips) return TemperPip.Hidden;
            if (index < cracks) return TemperPip.Crack;
            return index < used ? TemperPip.Spent : TemperPip.Free;
        }

        /// <summary>
        /// Центр отметки <paramref name="index"/> из <paramref name="count"/> видимых: ряд по центру <paramref name="center"/>
        /// с шагом <paramref name="step"/> — у обычной вещи две отметки стоят посередине, а не слева от четырёх мест.
        /// </summary>
        public static float PipX(int index, int count, float center, float step) => center + (index - (count - 1) * .5f) * step;

        /// <summary>Сердце, которое предлагает вкладка: первый босс акта с гранями и сердцем в лагере, иначе Хозяин Чащи.</summary>
        public static int HeartBoss(Camp camp)
        {
            for (int i = 0; i < RunBossKeys.Count; i++)
            {
                int key = RunBossKeys.At(i);
                if (camp.HeartCount(key) > 0 && Camp.HeartFacetCount(key) > 0) return key;
            }
            return RunBossKeys.ThicketMaster;
        }

        /// <summary>Открытая закалка этой вещи (удачные удары есть): вкладка держит её свойство и предлагает «Взять».</summary>
        public static bool TemperOpenOn(Camp camp, ForgeTarget target)
        {
            var session = camp.Session;
            return session.IsOpen && session.Kind == ForgeSessionKind.Temper && session.Target.Same(target);
        }

        /// <summary>Оплаченная переплавка или добавление ждёт выбора (на любой вещи): вся кузница ждёт его.</summary>
        public static bool ChoicePending(Camp camp)
        {
            var session = camp.Session;
            return session.IsOpen && (session.Kind == ForgeSessionKind.Remelt || session.Kind == ForgeSessionKind.Add);
        }

        /// <summary>Вкладка, на которой ждёт оплаченный выбор (переплавка или добавление).</summary>
        public static EniTab PendingTab(Camp camp) => camp.Session.Kind == ForgeSessionKind.Add ? EniTab.Add : EniTab.Remelt;

        /// <summary>
        /// Вещь при открытии окна: вещь ждущего выбора (окно сразу на нём), иначе открытая закалка, иначе надетое оружие,
        /// иначе ничего (−1).
        /// </summary>
        public static int DefaultTarget(Camp camp, out bool worn)
        {
            var session = camp.Session;
            if (session.IsOpen) { worn = session.Target.IsWorn; return session.Target.Slot; }
            worn = true;
            if (!camp.Worn.Worn(EquipSlot.Weapon).IsEmpty) return (int)EquipSlot.Weapon;
            worn = false;
            return -1;
        }

        /// <summary>Предпросмотр вкладки: что сделает основная кнопка, сколько стоит и чего не хватает.</summary>
        public struct Plan
        {
            public EniTab Tab;
            public TemperStep Step;
            /// <summary>Ответ Quote (или Locked/SessionOpen окна); Success — можно.</summary>
            public SmithResult Status;
            /// <summary>Разбор: почему нельзя (надето, «беречь», вещь сессии).</summary>
            public CampShopDeals.Block DismantleBlock;
            public int Gold, Shards, Steel, Hearts;
            public int GoldShort, ShardsShort, SteelShort, HeartsShort;
            /// <summary>Риск этого нажатия для дуги: трещина удара, рассыпаться, перелив.</summary>
            public int RiskPercent;
            public bool Overflow;
            /// <summary>Удачных ударов открытой закалки этой вещи.</summary>
            public int Strikes;
            public int AttemptsUsed, AttemptLimit, Cracks;
            /// <summary>Осколков за разбор (или за неудачный рискованный удар).</summary>
            public int Yield;
            /// <summary>Основная кнопка нажимается.</summary>
            public bool CanAct;
            /// <summary>«Взять [Esc]»: открыта закалка этой вещи.</summary>
            public bool CanTake;
            /// <summary>Вещь исчезнет — первое нажатие только спрашивает (рискованный удар, разбор).</summary>
            public bool NeedsConfirm;
            public bool Short => GoldShort > 0 || ShardsShort > 0 || SteelShort > 0 || HeartsShort > 0;
            /// <summary>Строку цены показывать: действие возможно, пусть и не по карману, и что-то стоит.</summary>
            public bool ShowsPrice => (Step == TemperStep.Strike || Step == TemperStep.Risky || Step == TemperStep.Pay || Step == TemperStep.Heart)
                && Gold + Shards + Steel + Hearts > 0;
        }

        /// <summary>
        /// План вкладки для вещи (<paramref name="slot"/>, <paramref name="worn"/>): свойство <paramref name="property"/>
        /// (−1 — базовое обычной вещи), выбранный вариант <paramref name="choice"/> (−1 — не выбран) — для выбора и сердца.
        /// </summary>
        public static Plan PlanTab(Camp camp, EniTab tab, int slot, bool worn, int property, int choice)
        {
            var plan = new Plan { Tab = tab, Status = SmithResult.Success };
            var item = Pick(camp, slot, worn);
            if (item.IsEmpty) { plan.Step = TemperStep.NoItem; plan.Status = SmithResult.InvalidItem; return plan; }
            plan.AttemptsUsed = camp.AttemptsUsed(item);
            plan.AttemptLimit = Camp.TemperAttempts(item.Rarity);
            plan.Cracks = camp.CrackCount(item);
            if (!TabOpen(camp, tab)) { plan.Step = TemperStep.Locked; plan.Status = SmithResult.Locked; return plan; }
            var target = worn ? ForgeTarget.Worn((EquipSlot)slot) : ForgeTarget.Bag(slot);

            if (tab == EniTab.Dismantle)
            {
                var scrap = CampShopDeals.PlanDismantle(camp, slot, worn);
                plan.Step = TemperStep.Dismantle;
                plan.DismantleBlock = scrap.Block;
                plan.Yield = scrap.Shards;
                plan.CanAct = scrap.Allowed;
                plan.NeedsConfirm = true;
                return plan;
            }

            if (ChoicePending(camp))
            {
                // Оплаченный выбор запирает все действия Эни на всех вещах (Quote → SessionOpen): окно ведёт к нему.
                if (camp.Session.Target.Same(target) && tab == PendingTab(camp))
                {
                    plan.Step = TemperStep.Choose;
                    plan.CanAct = choice >= 0 && choice < camp.SessionCandidateCount;
                    return plan;
                }
                plan.Step = TemperStep.Blocked;
                plan.Status = SmithResult.SessionOpen;
                return plan;
            }

            int boss = HeartBoss(camp);
            HeartFacet facet = tab == EniTab.Heart ? Camp.HeartFacetAt(boss, choice) : HeartFacet.None;
            var quote = camp.Quote((EniAction)tab, target, tab == EniTab.Temper || tab == EniTab.Remelt ? property : 0, boss, facet);
            plan.Status = quote.Status;
            plan.RiskPercent = quote.RiskPercent;
            plan.Overflow = quote.Overflow;
            plan.Strikes = quote.Strikes;
            // Закалка этой вещи и этого свойства: другое свойство Strike начал бы новой оплатой.
            bool open = tab == EniTab.Temper && TemperOpenOn(camp, target) && camp.Session.Property == property;
            if (open) plan.Strikes = camp.Session.Strikes;
            plan.CanTake = open && plan.Strikes > 0;

            if (quote.Status == SmithResult.AtMaximum && plan.CanTake)
            {
                // Рост сессии уже упёрся в предел: удар спит, «Взять» — единственный ход.
                plan.Step = TemperStep.AtMaximum;
                plan.Status = SmithResult.AtMaximum;
                return plan;
            }
            if (quote.Status != SmithResult.Success) { plan.Step = TemperStep.Blocked; return plan; }

            plan.Gold = quote.Gold; plan.Shards = quote.Shards; plan.Steel = quote.Steel; plan.Hearts = quote.Hearts;
            plan.GoldShort = System.Math.Max(0, plan.Gold - camp.Money(CurrencyType.Gold));
            plan.ShardsShort = System.Math.Max(0, plan.Shards - camp.Money(CurrencyType.Shards));
            plan.SteelShort = System.Math.Max(0, plan.Steel - camp.Money(CurrencyType.Steel));
            plan.HeartsShort = System.Math.Max(0, plan.Hearts - camp.HeartCount(boss));
            switch (tab)
            {
                case EniTab.Temper:
                    if (quote.Risky)
                    {
                        plan.Step = TemperStep.Risky;
                        plan.NeedsConfirm = true;
                        // Неудача рассыпает вещь в осколки: выход показывается до решения.
                        plan.Yield = camp.SalvageShards(item);
                    }
                    else plan.Step = plan.Strikes > 0 ? TemperStep.NextStrike : TemperStep.Strike;
                    plan.CanAct = quote.Affordable;
                    break;
                case EniTab.Remelt:
                case EniTab.Add:
                    plan.Step = TemperStep.Pay;
                    plan.CanAct = quote.Affordable;
                    break;
                default:
                    plan.Step = TemperStep.Heart;
                    // Грань выбирается до вплавления: без неё кнопка спит.
                    plan.CanAct = quote.Affordable && facet != HeartFacet.None;
                    break;
            }
            return plan;
        }

        static ItemInstance Pick(Camp camp, int slot, bool worn)
        {
            if (slot < 0) return default;
            if (worn) return slot < (int)EquipSlot.Count ? camp.Worn.Worn((EquipSlot)slot) : default;
            return slot < camp.Bag.Capacity ? camp.Bag.At(slot) : default;
        }
    }
}
