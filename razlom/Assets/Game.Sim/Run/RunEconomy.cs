namespace Game.Sim
{
    /// <summary>
    /// Экономика забега (решение владельца 06.10, план «Лагерь 06–10.10», §3).
    ///
    /// Все числа забега в одном месте, чтобы общий проход баланса крутил их одним
    /// заходом. Числа — ЗАГЛУШКИ БАЛАНСА: цель ~150 золота за удачный лес.
    /// Округление везде вниз и от суммы, а не от каждой порции: иначе десяток
    /// мелких начислений терял бы по единице на каждом.
    /// </summary>
    public static class RunEconomy
    {
        /// <summary>Золото за зачистку арены: 2 × её номер. Глубже — богаче.</summary>
        public const int ArenaClearGoldPerArena = 2;

        /// <summary>Множитель золота на маршруте «Сложно».</summary>
        public const int HardRouteMultiplier = 2;

        /// <summary>Золото за арену босса вместо 2 × N: босс — главный куш леса.</summary>
        public const int BossLevelGold = 30;

        /// <summary>Золото за элитную встречу.</summary>
        public const int EliteGold = 25;

        /// <summary>Разбор навыка при полных слотах — плоская сумма.</summary>
        public const int SalvageGold = 10;

        /// <summary>Сколько процентов найденного золота доезжает при смерти. Вещи теряются.</summary>
        public const int DeathGoldKeptPercent = 50;

        /// <summary>Пепел за обычного врага.</summary>
        public const int AshNormal = 1;

        /// <summary>Пепел за элиту.</summary>
        public const int AshElite = 5;

        /// <summary>Пепел за босса.</summary>
        public const int AshBoss = 20;

        /// <summary>Сталь за полностью зачищенную элитную встречу.</summary>
        public const int EliteEncounterSteel = 1;

        /// <summary>Сталь за победу над боссом.</summary>
        public const int BossSteel = 1;

        /// <summary>Золото за зачистку арены с номером arenaNumber (с единицы).</summary>
        public static int ArenaClearGold(int arenaNumber, bool hard, bool bossLevel)
            => (bossLevel ? BossLevelGold : ArenaClearGoldPerArena * arenaNumber) * (hard ? HardRouteMultiplier : 1);

        /// <summary>
        /// Процент от суммы, вниз. Через long: золото к int.MaxValue не подходит,
        /// но переполнение в умножении молча дало бы отрицательную долю.
        /// </summary>
        public static int Percent(int total, int percent) => (int)((long)total * percent / 100);
    }
}
