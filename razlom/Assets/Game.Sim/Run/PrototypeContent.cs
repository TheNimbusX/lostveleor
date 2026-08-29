namespace Game.Sim
{
    /// <summary>
    /// Данные прототипа: модули локаций и справочник предметов, зашитые в код.
    ///
    /// ЭТО ЗАГЛУШКА, как и числа баланса в Simulation. Настоящие модули и базы
    /// приедут из Game.Data как ScriptableObject, которые правит человек;
    /// сюда они попадать перестанут. Пока же петля забега должна на чём-то
    /// запускаться, а заводить редактор модулей раньше, чем доказано, что
    /// в забег интересно играть, — это работа не в ту сторону.
    /// </summary>
    public static class PrototypeContent
    {
        public static ModuleSet Modules()
        {
            var entrance = new ModuleDefinition("module.entrance", 5, 5, new[]
            {
                new ModuleConnector(2, 4, Direction.North),
                new ModuleConnector(4, 2, Direction.East),
                new ModuleConnector(2, 0, Direction.South),
                new ModuleConnector(0, 2, Direction.West),
            }, weight: 0, isEntrance: true);

            var hall = new ModuleDefinition("module.hall", 7, 6, new[]
            {
                new ModuleConnector(0, 3, Direction.West),
                new ModuleConnector(6, 2, Direction.East),
                new ModuleConnector(3, 5, Direction.North),
                new ModuleConnector(4, 0, Direction.South),
            }, weight: 100);

            var corridor = new ModuleDefinition("module.corridor", 6, 3, new[]
            {
                new ModuleConnector(0, 1, Direction.West),
                new ModuleConnector(5, 1, Direction.East),
            }, weight: 130);

            var junction = new ModuleDefinition("module.junction", 5, 5, new[]
            {
                new ModuleConnector(0, 2, Direction.West),
                new ModuleConnector(4, 2, Direction.East),
                new ModuleConnector(2, 4, Direction.North),
                new ModuleConnector(2, 0, Direction.South),
            }, weight: 90);

            var chamber = new ModuleDefinition("module.chamber", 4, 4, new[]
            {
                new ModuleConnector(0, 2, Direction.West),
                new ModuleConnector(3, 1, Direction.East),
            }, weight: 70);

            return new ModuleSet(new[] { entrance, hall, corridor, junction, chamber });
        }

        /// <summary>
        /// Лагерь прототипа.
        ///
        /// Третий акт, потому что кампании ещё нет, а без третьего акта закрыт
        /// портал в Разлом — то есть закрыто всё, что сейчас можно потрогать.
        /// Когда появятся акты, стартовым станет первый.
        /// </summary>
        public static Camp NewCamp() => new Camp(Items(), act: 3);

        /// <summary>Игра целиком на данных прототипа: лагерь, портал, забег.</summary>
        public static GameSession NewSession(ulong sessionSeed)
            => new GameSession(sessionSeed, NewCamp(), Modules(), ItemBaseIds());

        /// <summary>Идентификаторы баз, которые может предложить награда.</summary>
        public static int[] ItemBaseIds() => new[]
        {
            StableId.Of("base.rusty_sword"),
            StableId.Of("base.leather_jacket"),
        };

        public static ItemDatabase Items()
        {
            var bases = new[]
            {
                new ItemBaseDefinition(StableId.Of("base.rusty_sword"), ItemCategory.Weapon,
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(5)),

                new ItemBaseDefinition(StableId.Of("base.leather_jacket"), ItemCategory.Armor,
                    StatType.Armor, ModifierOp.Flat, Fix64.FromInt(8)),
            };

            byte weapon = AffixDefinition.Mask(ItemCategory.Weapon);
            byte armor = AffixDefinition.Mask(ItemCategory.Armor);
            byte both = AffixDefinition.Mask(ItemCategory.Weapon, ItemCategory.Armor);

            var affixes = new[]
            {
                new AffixDefinition(StableId.Of("affix.flat_damage_t1"), StableId.Of("group.flat_damage"),
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(2), Fix64.FromInt(6), 1, 100, weapon),

                new AffixDefinition(StableId.Of("affix.flat_damage_t2"), StableId.Of("group.flat_damage"),
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(7), Fix64.FromInt(15), 15, 70, weapon),

                new AffixDefinition(StableId.Of("affix.increased_damage"), StableId.Of("group.increased_damage"),
                    StatType.Damage, ModifierOp.Increased, Fix64.Ratio(10, 100), Fix64.Ratio(35, 100),
                    1, 90, weapon),

                new AffixDefinition(StableId.Of("affix.attack_speed"), StableId.Of("group.attack_speed"),
                    StatType.AttackSpeed, ModifierOp.Increased, Fix64.Ratio(5, 100), Fix64.Ratio(20, 100),
                    10, 60, weapon),

                new AffixDefinition(StableId.Of("affix.armor_flat"), StableId.Of("group.armor"),
                    StatType.Armor, ModifierOp.Flat, Fix64.FromInt(10), Fix64.FromInt(45), 1, 100, armor),

                new AffixDefinition(StableId.Of("affix.max_health"), StableId.Of("group.health"),
                    StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(15), Fix64.FromInt(60), 1, 95, both),

                new AffixDefinition(StableId.Of("affix.crit_chance"), StableId.Of("group.crit"),
                    StatType.CritChance, ModifierOp.Increased, Fix64.Ratio(5, 100), Fix64.Ratio(25, 100),
                    20, 55, both),

                new AffixDefinition(StableId.Of("affix.fire_resist"), StableId.Of("group.fire_resist"),
                    StatType.FireResist, ModifierOp.Flat, Fix64.Ratio(5, 100), Fix64.Ratio(30, 100),
                    5, 80, both),
            };

            return new ItemDatabase(bases, affixes);
        }
    }
}
