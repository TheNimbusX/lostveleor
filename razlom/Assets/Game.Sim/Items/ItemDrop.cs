namespace Game.Sim
{
    /// <summary>
    /// Рождение предмета: здесь и только здесь расходуется поток Rng.Affix.
    ///
    /// РАЗДЕЛЕНИЕ, РАДИ КОТОРОГО ВСЁ ЗАТЕЯНО:
    ///   дроп   — недетерминированная точка забега, берёт из потока сид;
    ///   разворот — чистая функция от сида, потока не касается.
    ///
    /// Поэтому предмет можно развернуть заново когда угодно: при загрузке
    /// сейва, в окне сравнения, на сервере — и получить ровно тот же предмет,
    /// не зная ничего про состояние боя, в котором он выпал.
    /// </summary>
    public static class ItemDrop
    {
        /// <summary>
        /// Порог редкости в сотых долях. Заглушка баланса: реальные числа
        /// приедут из таблиц вместе с модификаторами Разлома.
        /// </summary>
        private const int MagicChancePercent = 30;
        private const int RareChancePercent = 8;

        /// <summary>
        /// Катит новый предмет. Расходует из потока Affix РОВНО три броска —
        /// один на редкость и два на 64-битный сид, — сколько бы аффиксов
        /// потом ни развернулось. Расход не должен зависеть от результата:
        /// иначе один удачный предмет сдвинул бы всю дальнейшую
        /// последовательность лута.
        /// </summary>
        public static ItemInstance Roll(ref Pcg32 affixStream, int baseId, short itemLevel)
        {
            int rarityRoll = affixStream.NextInt(0, 100);
            ulong seed = NextSeed(ref affixStream);

            ItemRarity rarity =
                rarityRoll < RareChancePercent ? ItemRarity.Rare :
                rarityRoll < RareChancePercent + MagicChancePercent ? ItemRarity.Magic :
                ItemRarity.Normal;

            return new ItemInstance(baseId, itemLevel, rarity, seed);
        }

        /// <summary>Катит предмет заданной редкости — для наград и гарантированных дропов.</summary>
        public static ItemInstance RollOfRarity(ref Pcg32 affixStream, int baseId, short itemLevel, ItemRarity rarity)
        {
            // Бросок на редкость всё равно делается: расход потока обязан быть
            // одинаковым, иначе гарантированная награда сдвигала бы обычный лут.
            affixStream.NextInt(0, 100);
            ulong seed = NextSeed(ref affixStream);
            return new ItemInstance(baseId, itemLevel, rarity, seed);
        }

        /// <summary>
        /// Шестьдесят четыре бита из двух бросков по тридцать два.
        /// Порядок вызовов задан явно, а не оставлен на порядок вычисления
        /// операндов в выражении: читателю не должно приходиться о нём помнить.
        /// </summary>
        private static ulong NextSeed(ref Pcg32 stream)
        {
            ulong high = stream.NextUInt();
            ulong low = stream.NextUInt();
            return (high << 32) | low;
        }
    }
}
