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

        /// <summary>Панель полна: новая способность встаёт в слот 1–4, прежняя уходит с талантами.</summary>
        ReplaceSlot1 = 5,
        ReplaceSlot2 = 6,
        ReplaceSlot3 = 7,
        ReplaceSlot4 = 8,

        /// <summary>Панель полна: новая способность разбирается на золото забега.</summary>
        SalvageAbility = 9,

        /// <summary>Мини-меню над способностью с элиты: заменить ею слот 1–4. Бой при этом идёт.</summary>
        PickupReplaceSlot1 = 10,
        PickupReplaceSlot2 = 11,
        PickupReplaceSlot3 = 12,
        PickupReplaceSlot4 = 13,

        /// <summary>Мини-меню над способностью с элиты: разобрать её на золото забега.</summary>
        PickupSalvage = 14,
        ChooseRoute1 = 15,
        ChooseRoute2 = 16,
        ChooseRoute3 = 17,
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

        /// <summary>Враги зачищены, игрок идёт к выходу из Разлома.</summary>
        SeekingExit = 4,

        /// <summary>Взята способность при полной панели: заменить одну из четырёх или разобрать на золото.</summary>
        ReplacingAbility = 5,
        ChoosingRoute = 6,
    }

    public enum ArenaReward : byte { Upgrade, Shop }

    /// <summary>Обещание следующей ветки. Бонус выдаётся только за её зачистку.</summary>
    public readonly struct ArenaRouteOffer
    {
        public readonly ArenaReward Reward;
        public readonly int Size, BonusGold;
        public readonly bool Hard;
        public ArenaRouteOffer(ArenaReward reward, int size, bool hard, int bonusGold)
        { Reward = reward; Size = size; Hard = hard; BonusGold = bonusGold; }
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Reward); Hashing.Mix(ref hash, Size);
            Hashing.Mix(ref hash, Hard ? 1 : 0); Hashing.Mix(ref hash, BonusGold);
        }
    }

    /// <summary>Чем закончился забег.</summary>
    public enum RunOutcome : byte
    {
        None = 0,

        /// <summary>Смерть в Разломе. Глубже в этот раз не пойдёшь.</summary>
        Died = 1,

        /// <summary>Ушёл сам, с добычей.</summary>
        Left = 2,
        /// <summary>Все уровни локации пройдены, финальная награда выбрана.</summary>
        Completed = 3,
    }

    /// <summary>Что предлагается в награду.</summary>
    public enum RewardKind : byte
    {
        /// <summary>Предмет. Рецепт разворачивается через ItemGenerator.</summary>
        Item = 0,

        /// <summary>Узел удалённой «Печати пламени». Не выпадает; значение хранится ради реплеев.</summary>
        AbilityNode = 1,

        /// <summary>Прибавка к стату персонажа. С 15 сентября не выпадает, значение хранится ради реплеев.</summary>
        StatBoost = 2,

        /// <summary>Способность из пула Пелага, которой у игрока нет.</summary>
        Ability = 3,

        /// <summary>Следующий по порядку талант имеющейся способности.</summary>
        Talent = 4,
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

        /// <summary>Заполнено при Kind == StatBoost.</summary>
        public readonly StatType Stat;
        public readonly ModifierOp Op;
        public readonly Fix64 Value;

        /// <summary>Индекс способности в пуле Пелага. Заполнено при Kind == Ability и Talent.</summary>
        public readonly int PoolIndex;

        /// <summary>Номер предлагаемого таланта, с нуля. Заполнено при Kind == Talent.</summary>
        public readonly int TalentIndex;

        private RewardOffer(RewardKind kind, ItemInstance item,
            StatType stat, ModifierOp op, Fix64 value, int poolIndex = -1, int talentIndex = -1)
        {
            Kind = kind;
            Item = item;
            Stat = stat;
            Op = op;
            Value = value;
            PoolIndex = poolIndex;
            TalentIndex = talentIndex;
        }

        public static RewardOffer OfItem(in ItemInstance item)
            => new RewardOffer(RewardKind.Item, item, default, default, Fix64.Zero);

        public static RewardOffer OfStat(StatType stat, ModifierOp op, Fix64 value)
            => new RewardOffer(RewardKind.StatBoost, default, stat, op, value);

        public static RewardOffer OfAbility(int poolIndex)
            => new RewardOffer(RewardKind.Ability, default, default, default, Fix64.Zero, poolIndex);

        public static RewardOffer OfTalent(int poolIndex, int talentIndex)
            => new RewardOffer(RewardKind.Talent, default, default, default, Fix64.Zero, poolIndex, talentIndex);

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Kind);

            switch (Kind)
            {
                case RewardKind.Item:
                    Item.HashInto(ref hash);
                    break;
                case RewardKind.StatBoost:
                    Hashing.Mix(ref hash, (int)Stat);
                    Hashing.Mix(ref hash, (int)Op);
                    Hashing.Mix(ref hash, Value);
                    break;
                case RewardKind.Ability:
                    Hashing.Mix(ref hash, PoolIndex);
                    break;
                case RewardKind.Talent:
                    Hashing.Mix(ref hash, PoolIndex);
                    Hashing.Mix(ref hash, TalentIndex);
                    break;
            }
        }
    }
}
