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

        public const int PoolSize = 8;

        /// <summary>Вихрь — с него начинается каждый забег.</summary>
        public const int StarterPoolIndex = 0;

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
                default: return null;
            }
        }

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
                default: return AbilityDefinition.FireFlaskId;
            }
        }
    }
}
