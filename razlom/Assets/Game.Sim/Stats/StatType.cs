namespace Game.Sim
{
    /// <summary>
    /// Статы персонажа. Значение элемента — это индекс в плоском массиве
    /// StatSheet, поэтому НОВЫЕ СТАТЫ ДОБАВЛЯТЬ ТОЛЬКО ПЕРЕД Count,
    /// а существующие не переставлять: порядок войдёт в сейвы и реплеи.
    ///
    /// Начинаем с минимума. Стат, который никто не читает, — это строчка
    /// в таблице баланса, которую всё равно придётся заполнять.
    /// </summary>
    public enum StatType : byte
    {
        MaxHealth = 0,
        Damage = 1,
        AttackSpeed = 2,
        MoveSpeed = 3,
        CritChance = 4,
        CritMultiplier = 5,
        Armor = 6,
        FireResist = 7,

        /// <summary>Не стат. Размер массива значений.</summary>
        Count = 8,
    }
}
