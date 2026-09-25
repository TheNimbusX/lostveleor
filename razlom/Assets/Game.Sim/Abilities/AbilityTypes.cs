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
    ///
    /// 64 бита: с усилениями 6–8 (24 сентября) 32 не хватило. Хеш сборки
    /// подмешивает старшую половину, только если она не пуста, — прежние
    /// наборы хешируются как раньше.
    /// </summary>
    [System.Flags]
    public enum AbilityFlag : ulong
    {
        None = 0,

        // Бит 1 << 0 принадлежал удалённой «Печати пламени» и не переиспользуется.

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

        // Якорные таланты, утверждены владельцем 15 сентября.
        /// <summary>Удар якорем: +50% урона по оглушённым.</summary>
        AnchorSlamStunnedBonus = 1 << 16,
        /// <summary>Удар якорем: три полосы веером, прямо и ±30°.</summary>
        AnchorSlamThreeWays = 1 << 17,
        /// <summary>Крушение: +40% урона по элитам и боссам.</summary>
        WreckBigGame = 1 << 18,
        /// <summary>Крушение: завершающий удар возвращает 15 лавидия.</summary>
        WreckRefund = 1 << 19,
        /// <summary>Крушение: четвёртый удар по земле ×3 с оглушением 1 с.</summary>
        WreckFourthStrike = 1 << 20,
        /// <summary>Абордаж: удар по прибытии оглушает на 0,5 с.</summary>
        BoardingStun = 1 << 21,
        /// <summary>Абордаж: два применения подряд.</summary>
        BoardingTwoCharges = 1 << 22,
        /// <summary>Абордаж: удар сокращает перезарядку остальных способностей на 1 с.</summary>
        BoardingHilt = 1 << 23,
        /// <summary>Абордаж: кулак бьёт всех врагов в радиусе 2 м.</summary>
        BoardingSweep = 1 << 24,
        /// <summary>Взрывная смесь: враги в луже получают +20% урона от Пелага.</summary>
        FlaskFuel = 1 << 25,
        /// <summary>Взрывная смесь: бутылка в горящую лужу взрывается вдвое сильнее.</summary>
        FlaskOil = 1 << 26,
        /// <summary>Взрывная смесь: ещё три малые лужи вокруг взрыва.</summary>
        FlaskRing = 1 << 27,

        // ---- усиления 6–8, выбор владельца 24 сентября ----
        /// <summary>Вихрь: враги в 3,5 м подтягиваются к центру перед ударом.</summary>
        WhirlwindPull = 1UL << 28,
        /// <summary>Вихрь: после оборота кольцо расходится до 4 м, 40% урона.</summary>
        WhirlwindWave = 1UL << 29,
        /// <summary>Вихрь: во время вращения −30% входящего урона.</summary>
        WhirlwindCocoon = 1UL << 30,
        /// <summary>Рассекающий удар: следующий удар Пелага по цели +25%.</summary>
        CleaveSunder = 1UL << 31,
        /// <summary>Рассекающий удар: через 0,3 с второй удар, 50%.</summary>
        CleaveDouble = 1UL << 32,
        /// <summary>Рассекающий удар: удар уходит волной на 3 м вперёд, 50%.</summary>
        CleaveWave = 1UL << 33,
        /// <summary>«Ладно смазал»: при поджоге взрыв 60 огня вокруг, 2 м.</summary>
        BlazeFlare = 1UL << 34,
        /// <summary>«Ладно смазал»: пока горит, автоатаки на 20% быстрее.</summary>
        BlazeHaste = 1UL << 35,
        /// <summary>«Ладно смазал»: убийство пока горит +0,5 с горения, до +3 с.</summary>
        BlazeStoke = 1UL << 36,
        /// <summary>Шквал: после серии назад на место старта.</summary>
        SquallReturn = 1UL << 37,
        /// <summary>Шквал: повторный прыжок в ту же цель +50%.</summary>
        SquallRepeat = 1UL << 38,
        /// <summary>Шквал: первый прыжок ×2 по цели с полным здоровьем.</summary>
        SquallOpener = 1UL << 39,
        /// <summary>Удар якорем: полоса лежит 3 с, кто наступит — оглушение 0,3 с.</summary>
        AnchorSlamCrack = 1UL << 40,
        /// <summary>Удар якорем: каждый задетый враг −0,3 с перезарядки.</summary>
        AnchorSlamRecoil = 1UL << 41,
        /// <summary>Крушение: во время серии −25% входящего урона.</summary>
        WreckUnstoppable = 1UL << 42,
        /// <summary>Крушение: второй удар тоже оглушает, 0,3 с.</summary>
        WreckConcuss = 1UL << 43,
        /// <summary>Крушение: каждый следующий удар серии +15%.</summary>
        WreckMomentum = 1UL << 44,
        /// <summary>Абордаж: кулак +10% урона за каждый метр полёта.</summary>
        BoardingMomentum = 1UL << 45,
        /// <summary>Абордаж: зацеп сбивает замах врага.</summary>
        BoardingInterrupt = 1UL << 46,
        /// <summary>Абордаж: следующая автоатака за 2 с — крит.</summary>
        BoardingSureCrit = 1UL << 47,
        /// <summary>Взрывная смесь: два заряда.</summary>
        FlaskTwoCharges = 1UL << 48,
        /// <summary>Взрывная смесь: взрыв бросает 3 осколка во врагов до 4 м, по 30.</summary>
        FlaskShrapnel = 1UL << 49,
    }

    /// <summary>
    /// Эффекты, которые узлы типа EffectInsert вставляют в стадии.
    /// В отличие от флага, эффект — это самостоятельный кусок поведения,
    /// а не переключатель внутри существующего.
    /// </summary>
    public enum AbilityEffect : byte
    {
        None = 0,

        // Значение 1 принадлежало удалённой «Печати пламени» и не переиспользуется.
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
