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

        public static int WhirlwindId => StableId.Of("ability.whirlwind");

        /// <summary>«Вихрь»: один физический круговой удар вокруг героя.</summary>
        public static AbilityDefinition Whirlwind()
            => new AbilityDefinition("ability.whirlwind")
                .Set(AbilityStatType.Damage, 120)
                .Set(AbilityStatType.Radius, Fix64.Ratio(23, 10))
                .Set(AbilityStatType.CooldownTicks, 72);

        public static int AnchorLeapId => StableId.Of("ability.anchor_leap");
        public static int AnchorSweepId => StableId.Of("ability.anchor_sweep");
        public static int ChainStepId => StableId.Of("ability.chain_step");

        /// <summary>
        /// «Бросок якоря»: швыряет якорь в точку и подтягивает туда себя.
        ///
        /// Урона нет вовсе, и это не заглушка. Это единственный способ
        /// мгновенно сменить позицию во всей игре — отдельной кнопки рывка
        /// не будет, решено 1 сентября. Добавить сюда урон значило бы сделать
        /// перемещение бесплатным приложением к атаке, а оно и есть смысл.
        /// </summary>
        public static AbilityDefinition AnchorLeap()
            => new AbilityDefinition("ability.anchor_leap")
                .Set(AbilityStatType.Damage, 0)
                .Set(AbilityStatType.Radius, AnchorKit.LeapRange)
                .Set(AbilityStatType.CooldownTicks, 54);          // 1.8 с

        /// <summary>
        /// «Подсечка»: якорь уходит за спины врагов, рывок волочит их к игроку.
        ///
        /// Урон низкий намеренно: ценность способности в том, что разбросанная
        /// толпа становится одной кучей под Вихрь, а не в самом уроне.
        /// </summary>
        public static AbilityDefinition AnchorSweep()
            => new AbilityDefinition("ability.anchor_sweep")
                .Set(AbilityStatType.Damage, 35)
                .Set(AbilityStatType.Radius, AnchorKit.SweepRadius)
                .Set(AbilityStatType.CooldownTicks, 108);         // 3.6 с

        /// <summary>
        /// «Шаг по цепи»: серия прыжков от врага к врагу с ударом на каждом.
        ///
        /// Урон на прыжок средний, но прыжков до четырёх — это выход из
        /// окружения, который по дороге собирает добивания.
        /// </summary>
        public static AbilityDefinition ChainStep()
            => new AbilityDefinition("ability.chain_step")
                .Set(AbilityStatType.Damage, 85)
                .Set(AbilityStatType.Radius, AnchorKit.ChainRange)
                .Set(AbilityStatType.CooldownTicks, 126);         // 4.2 с

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
