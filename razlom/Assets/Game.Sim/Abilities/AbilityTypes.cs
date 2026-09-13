namespace Game.Sim
{
    /// <summary>
    /// Стадии конвейера. ОДИНАКОВЫ ДЛЯ ВСЕХ ДВАДЦАТИ СПОСОБНОСТЕЙ ИГРЫ —
    /// в этом весь смысл: узлы дерева вмешиваются в стадии, а не пишут
    /// новый код на каждую способность.
    ///
    /// Порядок значений — это порядок выполнения. Не переставлять.
    /// </summary>
    public enum AbilityStage : byte
    {
        Cast = 0,              // проверка кулдауна, расход, событие каста
        SelectTargets = 1,     // кого или куда бьём
        SpawnProjectiles = 2,  // что полетело
        OnHit = 3,             // что случилось при попадании
        OnKill = 4,            // что случилось при убийстве

        Count = 5,
    }

    /// <summary>
    /// Статы способности. Отдельно от StatType: у способности свои числа,
    /// и мешать их со статами персонажа в одном массиве значило бы платить
    /// размером за каждую способность.
    ///
    /// Новые добавлять только перед Count.
    /// </summary>
    public enum AbilityStatType : byte
    {
        Damage = 0,
        Radius = 1,
        CooldownTicks = 2,
        ProjectileSpeed = 3,

        /// <summary>Сколько тиков горит цель.</summary>
        BurnTicks = 4,

        /// <summary>Урон горения за тик как доля от урона попадания.</summary>
        BurnDamagePercent = 5,

        MinimumRadius = 6,
        DurationTicks = 7,
        StartTurnsPerSecond = 8,
        EndTurnsPerSecond = 9,
        StartMoveMultiplier = 10,
        EndMoveMultiplier = 11,
        WeaponRadius = 12,
        Width = 13,
        StunTicks = 14,
        WindupTicks = 15,

        /// <summary>На сколько метров отбрасывает. «За борт!».</summary>
        KnockbackDistance = 16,

        /// <summary>Сколько тиков открыто окно следующего нажатия комбо. «Крушение».</summary>
        ComboWindowTicks = 17,

        /// <summary>
        /// Полуширина дуги удара, заданная ПОРОГОМ КОСИНУСА, а не углом.
        ///
        /// Тригонометрии в симуляции нет и не будет: угол пришлось бы считать
        /// через Atan2, а это источник расхождения на фиксированной точке.
        /// Скалярное произведение направления удара и направления на цель
        /// сравнивается с этим порогом напрямую — 0 даёт полукруг, 1/2 —
        /// сектор в 120 градусов, 9/10 — узкий клин.
        /// </summary>
        ArcCosine = 18,

        SwingLeadTicks = 19,
        ContactWindowTicks = 20,
        TurnRadiansPerTick = 21,

        /// <summary>
        /// Добавка к попаданию как ДОЛЯ ОТ СИЛЫ УДАРА. «Ладно смазал».
        ///
        /// Отдельный стат, а не Damage: тот задаёт урон самой способности
        /// плоским числом, а здесь усиление чужого удара, и оно обязано расти
        /// вместе с оружием.
        /// </summary>
        BonusDamagePercent = 22,

        /// <summary>
        /// Сколько лавидия стоит каст. Не хватает — способность не срабатывает
        /// и кулдаун не тратится. Ноль — бесплатно (кувырок).
        /// </summary>
        LavidiumCost = 23,

        Count = 24,
    }

    /// <summary>
    /// Поведения, которые включаются узлами типа Flag. Код на каждый флаг
    /// пишется ОДИН РАЗ, дальше узел его только включает.
    /// </summary>
    [System.Flags]
    public enum AbilityFlag : uint
    {
        None = 0,

        /// <summary>Знак делится на три снаряда. Узел «Раскол».</summary>
        Split = 1 << 0,

        // ---- таланты сабельной ветки, решение владельца от 13 сентября ----

        /// <summary>Вихрь: +10% урона за каждого задетого, до +50%.</summary>
        WhirlwindCrowd = 1 << 1,
        /// <summary>Вихрь: +3 лавидия за задетого, до 15 за каст.</summary>
        WhirlwindRefund = 1 << 2,
        /// <summary>Вихрь: удержание до 2 с за 15 лавидия в секунду.</summary>
        WhirlwindChannel = 1 << 3,
        /// <summary>Рассекающий удар: можно бить на ходу.</summary>
        CleaveOnTheMove = 1 << 4,
        /// <summary>Рассекающий удар: +40% урона по элитам и боссам.</summary>
        CleaveBigGame = 1 << 5,
        /// <summary>Рассекающий удар: убийство возвращает стоимость.</summary>
        CleaveKillRefund = 1 << 6,
        /// <summary>Рассекающий удар: три направления перед героем.</summary>
        CleaveFan = 1 << 7,
        /// <summary>«Ладно смазал»: кувырок под огнём оставляет огненный след.</summary>
        BlazeTrail = 1 << 8,
        /// <summary>«Ладно смазал»: уклонение возвращает 5 лавидия.</summary>
        BlazeEvadeRefund = 1 << 9,
        /// <summary>«Ладно смазал»: обычные атаки поджигают.</summary>
        BlazeIgnite = 1 << 10,
        /// <summary>«Ладно смазал»: огненная добавка и на способности.</summary>
        BlazeAbilities = 1 << 11,
        /// <summary>Шквал: убийство во время серии −0,5 с перезарядки.</summary>
        SquallKillCooldown = 1 << 12,
        /// <summary>Шквал: неуязвимость во время прыжков.</summary>
        SquallInvulnerable = 1 << 13,
        /// <summary>Шквал: последний прыжок ×2 урона.</summary>
        SquallFinisher = 1 << 14,
        /// <summary>Шквал: пять прыжков вместо четырёх.</summary>
        SquallFiveHops = 1 << 15,
    }

    /// <summary>
    /// Эффекты, которые узлы типа EffectInsert вставляют в стадии.
    /// В отличие от флага, эффект — это самостоятельный кусок поведения,
    /// а не переключатель внутри существующего.
    /// </summary>
    public enum AbilityEffect : byte
    {
        None = 0,

        /// <summary>Горящий враг при смерти поджигает ближайшего. Узел «Перекидывается».</summary>
        SpreadBurn = 1,
    }

    /// <summary>Тип узла дерева. Доли взяты из архитектуры и держатся намеренно.</summary>
    public enum NodeKind : byte
    {
        /// <summary>Меняет число. Кода ноль, около 55% узлов.</summary>
        StatMod = 0,

        /// <summary>Включает написанное поведение. Код один раз на флаг, около 23%.</summary>
        Flag = 1,

        /// <summary>Вставляет новый эффект в стадию. Настоящий код, около 23%.</summary>
        EffectInsert = 2,
    }
}
