namespace Game.Sim
{
    /// <summary>
    /// Базовые числа способности до узлов дерева.
    ///
    /// Все длительности и кулдауны — В ТИКАХ, как и везде в симуляции.
    /// </summary>
    public sealed class AbilityDefinition
    {
        private readonly Fix64[] _base = new Fix64[(int)AbilityStatType.Count];

        public readonly int Id;
        public AbilityFlag BaseFlags { get; private set; }

        public AbilityDefinition(string stableKey)
        {
            Id = StableId.Of(stableKey);
            BaseFlags = AbilityFlag.None;
        }

        public Fix64 GetBase(AbilityStatType stat) => _base[(int)stat];

        public AbilityDefinition Set(AbilityStatType stat, Fix64 value)
        {
            _base[(int)stat] = value;
            return this;
        }

        public AbilityDefinition Set(AbilityStatType stat, int value)
            => Set(stat, Fix64.FromInt(value));

        public AbilityDefinition WithFlags(AbilityFlag flags)
        {
            BaseFlags = flags;
            return this;
        }

        /// <summary>
        /// «Печать пламени»: бросок знака в точку, вспышка по площади, поджиг.
        /// Числа — заглушка баланса, но структура настоящая.
        /// </summary>
        public static AbilityDefinition FlameSeal()
            => new AbilityDefinition("ability.flame_seal")
                .Set(AbilityStatType.Damage, 60)
                .Set(AbilityStatType.Radius, 3)
                .Set(AbilityStatType.CooldownTicks, 90)             // 3 секунды при 30 Гц
                .Set(AbilityStatType.ProjectileSpeed, Fix64.Ratio(12, Simulation.TicksPerSecond))
                .Set(AbilityStatType.BurnTicks, 60)                 // 2 секунды
                .Set(AbilityStatType.BurnDamagePercent, Fix64.Ratio(4, 100));

        // ---- узлы дерева «Печати пламени», по одному каждого типа ----

        /// <summary>StatMod: +20% урона огнём. Кода ноль.</summary>
        public static AbilityNode NodeHotter()
            => AbilityNode.StatMod("node.flame_seal.hotter",
                AbilityStatType.Damage, ModifierOp.Increased, Fix64.Ratio(20, 100));

        /// <summary>Flag: знак делится на три снаряда, урон каждого −45%.</summary>
        public static AbilityNode NodeSplit()
            => AbilityNode.Flag("node.flame_seal.split", AbilityFlag.Split);

        /// <summary>EffectInsert: горящий враг при смерти поджигает ближайшего в радиусе 3 м.</summary>
        public static AbilityNode NodeSpreads()
            => AbilityNode.EffectInsert("node.flame_seal.spreads",
                AbilityEffect.SpreadBurn, AbilityStage.OnKill);
    }
}
