namespace Game.Sim
{
    /// <summary>
    /// Постоянная прокачка героя: опыт за убийства, уровень, очки талантов.
    ///
    /// ВСЕ ЧИСЛА — ЗАГЛУШКИ БАЛАНСА. Решено владельцем только правило:
    /// опыт идёт за убийства, уровень без потолка, одно очко таланта за
    /// уровень. Награды по тиру и кривая здесь, в одном месте, чтобы крутить
    /// их одним заходом, а не искать по бою.
    /// </summary>
    public static class Progression
    {
        public const int NormalKillXp = 10;
        public const int EliteKillXp = 60;
        public const int BossKillXp = 400;

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

        /// <summary>Очков талантов на уровне: по одному за каждый, включая первый.</summary>
        public static int TalentPointsAtLevel(int level) => level < 1 ? 0 : level;
    }
}
