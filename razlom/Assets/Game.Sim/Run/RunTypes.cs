namespace Game.Sim
{
    /// <summary>
    /// Команды забега, приходящие в потоке ввода.
    /// Значения попадают в реплей — не переставлять.
    /// </summary>
    public enum RunCommand : byte
    {
        None = 0,
        ChooseReward1 = 1,
        ChooseReward2 = 2,
        ChooseReward3 = 3,

        /// <summary>Уйти из Разлома по своей воле. Награды за пройденное остаются.</summary>
        Leave = 4,
    }

    /// <summary>Фаза забега. Одна за раз, переходы только по правилам RiftRun.</summary>
    public enum RunPhase : byte
    {
        /// <summary>Забег не начат.</summary>
        Idle = 0,

        /// <summary>Идёт зачистка Разлома.</summary>
        Clearing = 1,

        /// <summary>Разлом зачищен, выбирается одна награда из трёх.</summary>
        ChoosingReward = 2,

        /// <summary>Забег окончен.</summary>
        Ended = 3,
    }

    /// <summary>Чем закончился забег.</summary>
    public enum RunOutcome : byte
    {
        None = 0,

        /// <summary>Смерть в Разломе. Глубже в этот раз не пойдёшь.</summary>
        Died = 1,

        /// <summary>Ушёл сам, с добычей.</summary>
        Left = 2,
    }

    /// <summary>Что предлагается в награду.</summary>
    public enum RewardKind : byte
    {
        /// <summary>Предмет. Рецепт разворачивается через ItemGenerator.</summary>
        Item = 0,

        /// <summary>Узел дерева способности.</summary>
        AbilityNode = 1,

        /// <summary>Прибавка к стату персонажа.</summary>
        StatBoost = 2,
    }

    /// <summary>
    /// Одно из трёх предложений на экране награды.
    ///
    /// Структура, а не класс: предложения катаются заново при каждом входе
    /// в экран, а живут до одного нажатия.
    /// </summary>
    public readonly struct RewardOffer
    {
        public readonly RewardKind Kind;

        /// <summary>Заполнено при Kind == Item.</summary>
        public readonly ItemInstance Item;

        /// <summary>Заполнено при Kind == AbilityNode.</summary>
        public readonly AbilityNode Node;

        /// <summary>Заполнено при Kind == StatBoost.</summary>
        public readonly StatType Stat;
        public readonly ModifierOp Op;
        public readonly Fix64 Value;

        private RewardOffer(RewardKind kind, ItemInstance item, AbilityNode node,
            StatType stat, ModifierOp op, Fix64 value)
        {
            Kind = kind;
            Item = item;
            Node = node;
            Stat = stat;
            Op = op;
            Value = value;
        }

        public static RewardOffer OfItem(in ItemInstance item)
            => new RewardOffer(RewardKind.Item, item, default, default, default, Fix64.Zero);

        public static RewardOffer OfNode(in AbilityNode node)
            => new RewardOffer(RewardKind.AbilityNode, default, node, default, default, Fix64.Zero);

        public static RewardOffer OfStat(StatType stat, ModifierOp op, Fix64 value)
            => new RewardOffer(RewardKind.StatBoost, default, default, stat, op, value);

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Kind);

            switch (Kind)
            {
                case RewardKind.Item:
                    Item.HashInto(ref hash);
                    break;
                case RewardKind.AbilityNode:
                    Hashing.Mix(ref hash, Node.Id);
                    break;
                case RewardKind.StatBoost:
                    Hashing.Mix(ref hash, (int)Stat);
                    Hashing.Mix(ref hash, (int)Op);
                    Hashing.Mix(ref hash, Value);
                    break;
            }
        }
    }
}
