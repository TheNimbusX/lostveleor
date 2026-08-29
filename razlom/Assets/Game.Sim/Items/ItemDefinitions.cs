namespace Game.Sim
{
    /// <summary>
    /// Описание аффикса в том виде, в каком его видит симуляция: плоская
    /// структура без единой ссылки на Unity. ScriptableObject, который правит
    /// человек, конвертируется сюда один раз при загрузке.
    /// </summary>
    public readonly struct AffixDefinition
    {
        /// <summary>Хеш стабильной строки, например «affix.fire_damage_t3».</summary>
        public readonly int Id;

        /// <summary>
        /// Группа взаимоисключения. Два аффикса одной группы на предмет не попадут:
        /// иначе «+урон огнём, тир 3» и «+урон огнём, тир 5» встанут вместе,
        /// и предмет получит вдвое больше того, что задумывалось.
        /// </summary>
        public readonly int Group;

        public readonly StatType Stat;
        public readonly ModifierOp Op;

        public readonly Fix64 MinValue;
        public readonly Fix64 MaxValue;

        /// <summary>Минимальный уровень предмета. Так тиры разводятся по глубине забега.</summary>
        public readonly short MinItemLevel;

        /// <summary>Вес во взвешенном выборе. Ноль — аффикс не выпадает никогда.</summary>
        public readonly int Weight;

        /// <summary>Битовая маска разрешённых категорий: 1 &lt;&lt; (int)ItemCategory.</summary>
        public readonly byte AllowedCategories;

        public AffixDefinition(int id, int group, StatType stat, ModifierOp op,
            Fix64 minValue, Fix64 maxValue, short minItemLevel, int weight, byte allowedCategories)
        {
            Id = id;
            Group = group;
            Stat = stat;
            Op = op;
            MinValue = minValue;
            MaxValue = maxValue;
            MinItemLevel = minItemLevel;
            Weight = weight;
            AllowedCategories = allowedCategories;
        }

        public bool AllowedOn(ItemCategory category)
            => (AllowedCategories & (1 << (int)category)) != 0;

        public static byte Mask(params ItemCategory[] categories)
        {
            byte mask = 0;
            for (int i = 0; i < categories.Length; i++) mask |= (byte)(1 << (int)categories[i]);
            return mask;
        }
    }

    /// <summary>
    /// База предмета: «ржавый меч», «кожаная куртка». Собственный модификатор
    /// базы (implicit) не роллится — он одинаков на всех экземплярах базы.
    /// </summary>
    public readonly struct ItemBaseDefinition
    {
        public readonly int Id;
        public readonly ItemCategory Category;

        public readonly StatType ImplicitStat;
        public readonly ModifierOp ImplicitOp;
        public readonly Fix64 ImplicitValue;
        public readonly bool HasImplicit;

        public ItemBaseDefinition(int id, ItemCategory category)
        {
            Id = id;
            Category = category;
            ImplicitStat = default;
            ImplicitOp = default;
            ImplicitValue = Fix64.Zero;
            HasImplicit = false;
        }

        public ItemBaseDefinition(int id, ItemCategory category,
            StatType implicitStat, ModifierOp implicitOp, Fix64 implicitValue)
        {
            Id = id;
            Category = category;
            ImplicitStat = implicitStat;
            ImplicitOp = implicitOp;
            ImplicitValue = implicitValue;
            HasImplicit = true;
        }
    }
}
