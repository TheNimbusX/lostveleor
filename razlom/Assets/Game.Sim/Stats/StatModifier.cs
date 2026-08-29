namespace Game.Sim
{
    /// <summary>
    /// Слой, в котором работает модификатор. Три слоя — это модель Path of Exile,
    /// проверенная жанром; менять её нельзя.
    ///
    /// Flat        прибавляется к базе
    /// Increased   суммируется между собой, множится ОДИН раз
    /// More        множится отдельно, каждый со всеми
    ///
    /// Ради чего разделение: Increased даёт затухающую отдачу — сотый процент
    /// прибавки стоит столько же, сколько первый, но даёт меньше. More не
    /// затухает, поэтому его раздают скупо. Слить их в один слой — значит
    /// потерять единственный рычаг, которым в жанре балансируют поздние билды.
    /// </summary>
    public enum ModifierOp : byte
    {
        Flat = 0,
        Increased = 1,
        More = 2,
    }

    /// <summary>
    /// Откуда взялся модификатор. Нужен, чтобы снять ровно свою пачку при
    /// смене снаряжения или окончании бафа, не трогая чужие.
    ///
    /// Новые источники добавлять в конец: значение попадёт в хеш состояния.
    /// </summary>
    public enum ModifierSource : byte
    {
        Equipment = 0,
        TreeNode = 1,
        Buff = 2,
        RacePassive = 3,
    }

    /// <summary>
    /// Одна прибавка к одному стату.
    ///
    /// Структура, а не класс: на позднем билде их две-три сотни на персонаже,
    /// и они пересчитываются пачкой — они обязаны лежать подряд в памяти.
    ///
    /// Value для Increased и More — это ДОЛЯ, а не множитель: «+20% урона»
    /// это Ratio(20, 100). Ноль тогда означает «ничего не делает», что верно
    /// для обоих слоёв. Множитель по умолчанию был бы нулём и обнулял бы стат.
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly StatType Stat;
        public readonly ModifierOp Op;
        public readonly ModifierSource Source;

        /// <summary>
        /// Идентификатор конкретного носителя: слот снаряжения, id узла дерева,
        /// id бафа. По паре (Source, SourceId) модификаторы снимаются пачкой.
        /// </summary>
        public readonly int SourceId;

        public readonly Fix64 Value;

        public StatModifier(StatType stat, ModifierOp op, Fix64 value, ModifierSource source, int sourceId)
        {
            Stat = stat;
            Op = op;
            Value = value;
            Source = source;
            SourceId = sourceId;
        }

        public static StatModifier Flat(StatType stat, Fix64 value, ModifierSource source, int sourceId)
            => new StatModifier(stat, ModifierOp.Flat, value, source, sourceId);

        public static StatModifier Increased(StatType stat, Fix64 value, ModifierSource source, int sourceId)
            => new StatModifier(stat, ModifierOp.Increased, value, source, sourceId);

        public static StatModifier More(StatType stat, Fix64 value, ModifierSource source, int sourceId)
            => new StatModifier(stat, ModifierOp.More, value, source, sourceId);

        /// <summary>
        /// Канонический порядок: стат, слой, источник, id источника, значение.
        ///
        /// ЗАЧЕМ. Умножение в Fix64 округляет, поэтому произведение слоя More
        /// зависит от порядка сомножителей — в младшем разряде, но зависит.
        /// Если бы порядок задавался тем, в какой последовательности игрок надел
        /// вещи, два одинаковых билда считали бы разный урон. Список держится
        /// отсортированным по этому ключу, и порядок действий игрока перестаёт
        /// влиять на результат.
        /// </summary>
        public int CompareTo(in StatModifier other)
        {
            if (Stat != other.Stat) return Stat < other.Stat ? -1 : 1;
            if (Op != other.Op) return Op < other.Op ? -1 : 1;
            if (Source != other.Source) return Source < other.Source ? -1 : 1;
            if (SourceId != other.SourceId) return SourceId < other.SourceId ? -1 : 1;
            if (Value != other.Value) return Value < other.Value ? -1 : 1;
            return 0;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Stat);
            Hashing.Mix(ref hash, (int)Op);
            Hashing.Mix(ref hash, (int)Source);
            Hashing.Mix(ref hash, SourceId);
            Hashing.Mix(ref hash, Value);
        }
    }
}
