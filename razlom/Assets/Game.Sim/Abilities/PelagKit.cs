namespace Game.Sim
{
    /// <summary>
    /// Пул способностей Пелага.
    ///
    /// С разворота в роглайк (15 сентября) сабельной и якорной веток нет:
    /// забег начинается с автоатаки и Вихря, а остальное находится по пути
    /// из общего пула. Что лежит в слотах, хранит RunLoadout.
    ///
    /// ПУЛ ЖИВЁТ В СИМУЛЯЦИИ, А НЕ В ПРЕДСТАВЛЕНИИ: «чем игрок бьёт» — правило
    /// игры, и съёмка, тесты и живой запуск обязаны видеть одно и то же.
    ///
    /// ПОРЯДОК ПУЛА ТОЛЬКО ДОПИСЫВАЕТСЯ. Индекс в пуле хранится в состоянии
    /// забега и участвует в роллах карточек: перестановка поменяет исход
    /// каждого сохранённого сида. Первые четыре — бывшая сабля, в порядке
    /// утверждённого листа; следующие четыре — бывший якорь.
    /// </summary>
    public static class PelagKit
    {
        /// <summary>Основных слотов — четыре. Пятый слот симуляции — общий кувырок.</summary>
        public const int MainSlots = Simulation.AbilitySlots - 1;

        /// <summary>
        /// Слот кувырка. Он не принадлежит Пелагу: диздок называет его базовым
        /// инструментом передвижения, общим для всех героев, и в забеге он есть всегда.
        /// </summary>
        public const int DashSlot = Simulation.AbilitySlots - 1;

        public const int PoolSize = 11;

        /// <summary>Вихрь — с него начинается каждый забег.</summary>
        public const int StarterPoolIndex = 0;

        /// <summary>
        /// Псевдолиния сабли (ЛКМ) в наборе забега: свои формы и таланты, как у
        /// навыков (DESIGN 01.10), но это НЕ индекс пула и НЕ слот — в слоты, броски
        /// способностей, дропы и меню пула не попадает никогда. Пул обязан
        /// оставаться меньше этого числа. План форм 02.10.
        /// </summary>
        public const int SabreLine = 100;

        /// <summary>Таланты сабли ещё не утверждены («Выпад из рывка» — будущий): пока ноль.</summary>
        public const int SabreTalentCount = 0;

        /// <summary>
        /// Линии набора для бросков талантов и форм: весь пул, затем сабля. Сабля
        /// ПОСЛЕДНЯЯ: пока у неё нет ни талантов, ни форм, число кандидатов и ход
        /// бросков те же, что до неё.
        /// </summary>
        public const int LineCount = PoolSize + 1;

        public static int LineAt(int i) => i < PoolSize ? i : SabreLine;

        /// <summary>Способность пула по индексу. null — индекс вне пула.</summary>
        public static AbilityDefinition PoolDefinition(int index)
        {
            switch (index)
            {
                case 0: return AbilityDefinition.Whirlwind();
                case 1: return AbilityDefinition.Cleave();
                case 2: return AbilityDefinition.Blaze();
                // «Шквал» — это и есть Шаг по цепи: механика уже написана и
                // анимирована, идентификатор сохранён.
                case 3: return AbilityDefinition.ChainStep();
                case 4: return AbilityDefinition.AnchorSlam();
                case 5: return AbilityDefinition.Wreck();
                // Абордаж — переделанный Бросок якоря, стабильный идентификатор
                // сохранён: на него завязаны анимация, звук и VFX.
                case 6: return AbilityDefinition.AnchorLeap();
                case 7: return AbilityDefinition.FireFlask();
                case 8: return AbilityDefinition.Skewer();
                case 9: return AbilityDefinition.Backblast();
                // Бросок якоря (03.10): новый навык, свой ключ ability.anchor_throw.
                case 10: return AbilityDefinition.AnchorThrow();
                default: return null;
            }
        }

        /// <summary>
        /// Способность пула ещё предлагается наградой (броски RiftRun: карточки и дропы).
        /// УБРАННЫЙ индекс номер держит навсегда (не переиспользуется, PoolDefinition
        /// его ещё отдаёт — старый код живёт до приёмки замены), но в броски не идёт:
        /// * 4 — Удар якорем: влит в Крушение (владелец 02.10, спека 03.10 «Крушение» 2.5);
        /// * 8 — «На вылет»: убран из набора владельцем 03.10, идея уйдёт в талант рывка.
        /// * 10 — Бросок якоря: пока только через F8, в награды — по слову владельца (тогда
        ///   пересъёмка FormPinTests с записью причины).
        /// Остальные индексы проход талантов уберёт этим же признаком.
        /// </summary>
        public static bool InRewardPool(int index) => (uint)index < PoolSize && index != 4 && index != 8 && index != 10;

        /// <summary>Индекс способности в пуле по её идентификатору. −1 — вне пула.</summary>
        public static int PoolIndexOf(int definitionId)
        {
            for (int i = 0; i < PoolSize; i++)
                if (PoolId(i) == definitionId) return i;
            return -1;
        }

        private static int PoolId(int index)
        {
            switch (index)
            {
                case 0: return AbilityDefinition.WhirlwindId;
                case 1: return AbilityDefinition.CleaveId;
                case 2: return AbilityDefinition.BlazeId;
                case 3: return AbilityDefinition.ChainStepId;
                case 4: return AbilityDefinition.AnchorSlamId;
                case 5: return AbilityDefinition.WreckId;
                case 6: return AbilityDefinition.AnchorLeapId;
                case 7: return AbilityDefinition.FireFlaskId;
                case 8: return AbilityDefinition.SkewerId;
                case 9: return AbilityDefinition.BackblastId;
                case 10: return AbilityDefinition.AnchorThrowId;
                default: return 0;
            }
        }
    }
}
