namespace Game.Sim
{
    /// <summary>Категория базы. Аффиксы разрешены не на всём подряд.</summary>
    public enum ItemCategory : byte
    {
        Weapon = 0,
        Armor = 1,
        Jewellery = 2,
        Talisman = 3,
        Artifact = 4,
    }

    /// <summary>
    /// Редкость. Определяет, сколько аффиксов развернётся из сида.
    /// Значения попадают в сейв — не переставлять.
    /// </summary>
    public enum ItemRarity : byte
    {
        Normal = 0,
        Magic = 1,
        Rare = 2,
        /// <summary>
        /// Уникальная (16 сентября, владелец: «обычная, редкая, эпическая, уникальная»).
        /// В интерфейсе Normal — обычная, Magic — редкая, Rare — эпическая.
        /// Из врагов пока не выпадает: добавить её в розыгрыш — значит сдвинуть все сиды.
        /// </summary>
        Unique = 3,
    }

    /// <summary>
    /// РЕЦЕПТ предмета, а не его статы. Это ключевое решение всей системы лута,
    /// и менять его нельзя.
    ///
    /// В сейве лежит вот это — пятнадцать байт, — а не список посчитанных
    /// модификаторов. Из рецепта предмет разворачивается заново при каждой
    /// загрузке через ItemGenerator.Generate.
    ///
    /// ЧТО ЭТО ДАЁТ. Ребаланс аффиксов применяется ко всем предметам всех
    /// игроков сам собой, без единой миграции сейвов: поменялся диапазон
    /// в данных — при следующей загрузке из того же сида развернётся предмет
    /// с новыми числами. Хранили бы посчитанное — пришлось бы писать миграцию
    /// на каждый патч баланса и надеяться, что она нигде не ошиблась.
    ///
    /// Структура, а не класс: предметы копируются как значения, за забег их
    /// падают тысячи, и ни один не должен стоить аллокации.
    /// </summary>
    public readonly struct ItemInstance
    {
        /// <summary>Хеш стабильного строкового id базы, см. StableId.</summary>
        public readonly int BaseId;

        public readonly short ItemLevel;
        public readonly ItemRarity Rarity;

        /// <summary>Из него разворачиваются аффиксы. Берётся из потока Rng.Affix при дропе.</summary>
        public readonly ulong Seed;
        // По четыре бита на выбранный аффикс каждой из трёх перековок; ноль — попытки не было.
        public readonly ushort ForgeRecipe;
        public int ReforgeCount => ForgeRecipe==0?0:(ForgeRecipe>>8)!=0?3:(ForgeRecipe>>4)!=0?2:1;
        public short OriginalLevel => (short)(ItemLevel-ReforgeCount*(1+(int)Rarity));

        public ItemInstance(int baseId, short itemLevel, ItemRarity rarity, ulong seed, ushort forgeRecipe=0)
        {
            BaseId = baseId;
            ItemLevel = itemLevel;
            Rarity = rarity;
            Seed = seed;
            ForgeRecipe = forgeRecipe;
        }

        public bool IsEmpty => BaseId == 0 && Seed == 0UL;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, BaseId);
            Hashing.Mix(ref hash, (int)ItemLevel);
            Hashing.Mix(ref hash, (int)Rarity);
            Hashing.Mix(ref hash, Seed);
            if(ForgeRecipe!=0)Hashing.Mix(ref hash,(int)ForgeRecipe);
        }
    }
}
