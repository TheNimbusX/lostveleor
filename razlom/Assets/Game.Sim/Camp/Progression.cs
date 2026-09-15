namespace Game.Sim
{
    /// <summary>
    /// Постоянная прокачка героя: опыт за убийства и уровень, дающий статы.
    ///
    /// Решено владельцем: опыт идёт за убийства, уровень без потолка, и с
    /// разворота в роглайк (15 сентября) каждый уровень даёт базовые статы,
    /// а не очки талантов — таланты теперь берутся внутри забега. Награды за
    /// убийство и кривая уровня — ЗАГЛУШКИ БАЛАНСА; прибавки за уровень —
    /// числа владельца. Всё здесь, чтобы крутить одним заходом.
    /// </summary>
    public static class Progression
    {
        public const int NormalKillXp = 10;
        public const int EliteKillXp = 60;
        public const int BossKillXp = 400;

        /// <summary>Прибавки за каждый уровень выше первого. Решение владельца от 15 сентября.</summary>
        public const int HealthPerLevel = 30;
        public const int DamagePerLevel = 5;
        public const int LavidiumPerLevel = 10;

        /// <summary>
        /// Сколько опыта нужно, чтобы с уровня level перейти на следующий.
        ///
        /// Линейный рост, а не степень: потолка уровня нет, и экспонента
        /// через два десятка уровней превратила бы каждый следующий в стену.
        /// </summary>
        public static int XpToNextLevel(int level)
        {
            if (level < 1) level = 1;
            return 100 + 50 * (level - 1);
        }
    }
}
