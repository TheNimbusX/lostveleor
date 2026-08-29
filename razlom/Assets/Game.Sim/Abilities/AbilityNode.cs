namespace Game.Sim
{
    /// <summary>
    /// Узел дерева способности. Один тип структуры на все три вида узлов:
    /// поля, не относящиеся к виду, просто не читаются.
    ///
    /// ЗАЧЕМ ОДНА СТРУКТУРА, А НЕ ТРИ КЛАССА. Узлы применяются пачкой, в строгом
    /// порядке и очень часто — при каждой пересборке билда. Массив структур
    /// обходится подряд; массив ссылок на три разных класса даёт виртуальный
    /// вызов и прыжок по памяти на каждом узле, ради экономии трёх полей.
    /// </summary>
    public readonly struct AbilityNode
    {
        /// <summary>
        /// Хеш стабильной строки, например «node.flame_seal.hotter».
        ///
        /// ПО ВОЗРАСТАНИЮ ЭТОГО ЧИСЛА УЗЛЫ И ПРИМЕНЯЮТСЯ — не в порядке взятия
        /// игроком и не в порядке обхода коллекции. Иначе два одинаковых билда
        /// посчитают урон по-разному, потому что умножение в Fix64 округляет.
        /// </summary>
        public readonly int Id;

        public readonly NodeKind Kind;

        // ---- StatMod ----
        public readonly AbilityStatType Stat;
        public readonly ModifierOp Op;
        public readonly Fix64 Value;

        // ---- Flag ----
        public readonly AbilityFlag EnabledFlag;

        // ---- EffectInsert ----
        public readonly AbilityEffect Effect;
        public readonly AbilityStage Stage;

        private AbilityNode(int id, NodeKind kind, AbilityStatType stat, ModifierOp op, Fix64 value,
            AbilityFlag flag, AbilityEffect effect, AbilityStage stage)
        {
            Id = id;
            Kind = kind;
            Stat = stat;
            Op = op;
            Value = value;
            EnabledFlag = flag;
            Effect = effect;
            Stage = stage;
        }

        public static AbilityNode StatMod(string key, AbilityStatType stat, ModifierOp op, Fix64 value)
            => new AbilityNode(StableId.Of(key), NodeKind.StatMod, stat, op, value,
                AbilityFlag.None, AbilityEffect.None, AbilityStage.Cast);

        public static AbilityNode Flag(string key, AbilityFlag flag)
            => new AbilityNode(StableId.Of(key), NodeKind.Flag, default, default, Fix64.Zero,
                flag, AbilityEffect.None, AbilityStage.Cast);

        public static AbilityNode EffectInsert(string key, AbilityEffect effect, AbilityStage stage)
            => new AbilityNode(StableId.Of(key), NodeKind.EffectInsert, default, default, Fix64.Zero,
                AbilityFlag.None, effect, stage);
    }
}
