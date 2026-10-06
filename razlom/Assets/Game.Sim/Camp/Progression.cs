namespace Game.Sim
{
    /// <summary>
    /// Постоянная прокачка героя: опыт за убийства и уровень лагеря.
    ///
    /// Решено владельцем: опыт идёт за убийства, уровень без потолка. С 15
    /// сентября уровень давал базовые статы (+30 здоровья, +5 урона, +10
    /// лавидия за каждый выше первого), но 29 сентября владелец их убрал:
    /// статов уровень не даёт. С 06.10 уровень открывает лагерь: жителей
    /// (ур. 3) и ранги вместе с боссами (ур. 6/12/18), а база героя зависит
    /// не от уровня, а от профиля (HeroBaseline: эталон 270/54 или новый 200/40).
    ///
    /// Награды за убийство и кривая уровня — ЗАГЛУШКИ БАЛАНСА. Всё здесь,
    /// чтобы крутить одним заходом.
    /// </summary>
    public static class Progression
    {
        public const int NormalKillXp = 10;
        public const int EliteKillXp = 60;
        public const int BossKillXp = 400;

        /// <summary>
        /// Какой прежний уровень лагеря стал героем для всех (владелец, 29 сентября).
        /// Прибавки ниже — ровно четыре прежних уровня выше первого.
        /// </summary>
        public const int ReferenceHeroLevel = 5;

        /// <summary>Прибавка базы героя к здоровью: 4 × прежние 30 за уровень.</summary>
        public const int HeroBaselineHealth = 120;

        /// <summary>Прибавка базы героя к урону: 4 × прежние 5 за уровень.</summary>
        public const int HeroBaselineDamage = 20;

        /// <summary>Прибавка базы героя к пулу лавидия: 4 × прежние 10 за уровень (200 → 240).</summary>
        public const int HeroBaselineLavidium = 40;

        /// <summary>
        /// Здоровье эталонного героя баланса: 150 базы локации (MeadowGameplay и
        /// лес тестов) + HeroBaselineHealth. По нему меряют окна урона мобов.
        /// </summary>
        public const int ReferenceHeroHealth = 150 + HeroBaselineHealth;

        /// <summary>Урон эталонного героя: 34 базы героя (Simulation) + HeroBaselineDamage.</summary>
        public const int ReferenceHeroDamage = 34 + HeroBaselineDamage;

        /// <summary>
        /// Сколько опыта нужно, чтобы с уровня level перейти на следующий (06.10):
        /// 1→2 — 200, 2→3 — 300, дальше +100 за уровень. Первый забег (≈600–850 опыта
        /// до гибели на А3–А5) приводит к уровню 3 и жителям; три забега — к 7–8.
        ///
        /// Линейный рост, а не степень: потолка уровня нет, и экспонента
        /// через два десятка уровней превратила бы каждый следующий в стену.
        /// </summary>
        public static int XpToNextLevel(int level)
        {
            if (level < 1) level = 1;
            return 200 + 100 * (level - 1);
        }
    }
}
