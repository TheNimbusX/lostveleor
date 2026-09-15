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
                .Set(AbilityStatType.LavidiumCost, 30)
                .Set(AbilityStatType.CooldownTicks, 72);

        public static int AnchorLeapId => StableId.Of("ability.anchor_leap");
        public static int AnchorSlamId => StableId.Of("ability.anchor_slam");

        /// <summary>Удар якорем через плечо по узкой полосе перед героем.</summary>
        public static AbilityDefinition AnchorSlam()
            => new AbilityDefinition("ability.anchor_slam")
                .Set(AbilityStatType.Damage, 100)
                .Set(AbilityStatType.LavidiumCost, 20)
                .Set(AbilityStatType.Radius, Fix64.Ratio(9, 2))
                .Set(AbilityStatType.Width, Fix64.Ratio(6, 5))
                .Set(AbilityStatType.StunTicks, 15)
                .Set(AbilityStatType.WindupTicks, 15)
                .Set(AbilityStatType.DurationTicks, 27)
                .Set(AbilityStatType.CooldownTicks, 108);

        public static int ChainStepId => StableId.Of("ability.chain_step");

        /// <summary>
        /// «АБОРДАЖ»: цепляет врага якорем и подтягивает СЕБЯ к нему, завершая
        /// сближение ударом свободного кулака.
        ///
        /// БЫЛ «БРОСКОМ ЯКОРЯ» — броском в пустую точку и с нулевым уроном.
        /// Тот ноль был осознанным: способность целиком состояла из
        /// перемещения, и урон сделал бы перемещение бесплатным приложением к
        /// атаке. С переходом на диздок Абордажа условие изменилось — цель
        /// теперь враг, а не точка, и удар по прибытии есть в описании. Ноль
        /// сохранил бы прежнюю осторожность ценой способности, которая цепляет
        /// врага и ничего с ним не делает.
        ///
        /// СТАБИЛЬНЫЙ КЛЮЧ ОСТАЁТСЯ ПРЕЖНИМ. На «ability.anchor_leap» завязаны
        /// анимация прыжка, звук и VFX в десятке файлов представления; смена
        /// ключа ради названия разрушила бы работу, которую он обозначает.
        /// </summary>
        public static AbilityDefinition AnchorLeap()
            => new AbilityDefinition("ability.anchor_leap")
                .Set(AbilityStatType.Damage, 75)
                .Set(AbilityStatType.LavidiumCost, 15)
                .Set(AbilityStatType.Radius, AnchorKit.LeapRange)
                .Set(AbilityStatType.CooldownTicks, 54);          // 1.8 с

        /// <summary>
        /// «Подсечка»: якорь уходит за спины врагов, рывок волочит их к игроку.
        ///
        /// Урон низкий намеренно: ценность способности в том, что разбросанная
        /// толпа становится одной кучей под Вихрь, а не в самом уроне.
        /// </summary>
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
                .Set(AbilityStatType.LavidiumCost, 40)
                .Set(AbilityStatType.CooldownTicks, 126);         // 4.2 с

        public static int DashId => StableId.Of("ability.dash");

        /// <summary>
        /// «БАЗОВЫЙ РЫВОК»: короткий рывок в выбранном направлении.
        ///
        /// Пятая кнопка ЯКОРНОЙ ветки и её единственный инструмент побега.
        /// Сабельный Пелаг уходит из-под удара Выпадом и Шквалом; якорный
        /// стоит в замахе, и без отдельного рывка у него нет ответа на
        /// направленную атаку босса вовсе.
        ///
        /// НИ УРОНА, НИ НЕУЯЗВИМОСТИ. Диздок говорит об этом прямо: «дополни-
        /// тельный урон и другие эффекты в его описание пока не входят»,
        /// «неуязвимость не зафиксирована». Кадры неуязвимости — это отдельное
        /// решение о том, как в игре вообще уклоняются, и принимать его молча
        /// внутри правки нельзя: с ними рывок перестаёт быть перемещением и
        /// становится защитой.
        ///
        /// Цели не требует — только направление.
        /// </summary>
        public static AbilityDefinition Dash()
            => new AbilityDefinition("ability.dash")
                .Set(AbilityStatType.Damage, 0)
                .Set(AbilityStatType.Radius, 3)
                .Set(AbilityStatType.DurationTicks, 10)            // 0.33 с — быстрый кувырок
                .Set(AbilityStatType.CooldownTicks, 150);          // 5 с

        public static int WreckId => StableId.Of("ability.wreck");
        public static int CleaveId => StableId.Of("ability.cleaving_strike");
        public static int BlazeId => StableId.Of("ability.blaze_oil");
        public static int FireFlaskId => StableId.Of("ability.fire_flask");

        /// <summary>
        /// «РАССЕКАЮЩИЙ УДАР»: сильный удар саблей сверху по одной цели.
        ///
        /// ЭТО НЕ РЫВОК И НЕ АОЕ. Три остальные сабельные кнопки бьют по
        /// площади или собирают серию; эта существует ровно для того, ради
        /// чего у сабельной ветки не было инструмента вовсе, — для одного
        /// важного тела. Отсюда и число: урон крупнее Вихря вдвое, потому что
        /// он достаётся одному, а не всем вокруг.
        ///
        /// Замах длинный намеренно. Удар такой силы обязан быть видимым
        /// решением, а не нажатием между делом.
        ///
        /// Числа — ЗАГЛУШКА БАЛАНСА: диздок оставляет их открытыми.
        /// </summary>
        public static AbilityDefinition Cleave()
            => new AbilityDefinition("ability.cleaving_strike")
                .Set(AbilityStatType.Damage, 260)
                .Set(AbilityStatType.LavidiumCost, 15)
                .Set(AbilityStatType.Radius, Fix64.Ratio(15, 10))
                .Set(AbilityStatType.Width, Fix64.Ratio(15, 100))
                .Set(AbilityStatType.ArcCosine, Fix64.Ratio(7071, 10000))
                .Set(AbilityStatType.SwingLeadTicks, 3)
                .Set(AbilityStatType.ContactWindowTicks, 2)
                .Set(AbilityStatType.TurnRadiansPerTick, Fix64.Pi / 60)
                .Set(AbilityStatType.DurationTicks, 27)
                .Set(AbilityStatType.WindupTicks, 12)              // 0.4 с
                .Set(AbilityStatType.CooldownTicks, 90);           // 3 с

        /// <summary>
        /// «ЛАДНО СМАЗАЛ»: три секунды горящей сабли.
        ///
        /// Не наносит урона сама и ничего не двигает — она меняет ЦЕНУ всего
        /// остального. Длительность взята из диздока — три секунды; уклонение
        /// и добавку огнём владелец назвал 12 сентября: по пятой части каждая.
        ///
        /// BonusDamagePercent — это ДОБАВКА ЗА ПОПАДАНИЕ долей от силы удара,
        /// а не урон способности, и достаётся она ТОЛЬКО обычным атакам.
        /// </summary>
        public static AbilityDefinition Blaze()
            => new AbilityDefinition("ability.blaze_oil")
                .Set(AbilityStatType.BonusDamagePercent, Fix64.Ratio(1, 5))
                .Set(AbilityStatType.LavidiumCost, 10)
                .Set(AbilityStatType.DurationTicks, 90)            // 3 с
                .Set(AbilityStatType.CooldownTicks, 360);          // 12 с

        /// <summary>
        /// «ВЗРЫВНАЯ СМЕСЬ»: бутылка, взрыв и горящая лужа.
        ///
        /// Единственная способность Пелага, которая удерживает урон НА МЕСТЕ,
        /// а не на теле. Якорный герой подолгу стоит в замахе, и площадка,
        /// продолжающая бить, пока он заносит якорь, — ровно та валюта,
        /// которой ему не хватает.
        ///
        /// ПЕРЕИСПОЛЬЗОВАННЫЕ СТАТЫ. BurnTicks здесь означает ПЕРИОД между
        /// тиками лужи, а BurnDamagePercent — урон одного тика. Заводить два
        /// новых стата ради одной способности дороже, чем объяснить два уже
        /// существующих; смысл «через сколько тиков жжёт и на сколько» у них
        /// тот же.
        ///
        /// Числа — ЗАГЛУШКА БАЛАНСА: в диздоке все открыты.
        /// </summary>
        public static AbilityDefinition FireFlask()
            => new AbilityDefinition("ability.fire_flask")
                .Set(AbilityStatType.Damage, 70)                   // взрыв
                .Set(AbilityStatType.LavidiumCost, 30)
                .Set(AbilityStatType.Radius, 7)                    // дальность броска
                .Set(AbilityStatType.Width, Fix64.Ratio(5, 2))     // диаметр лужи
                .Set(AbilityStatType.ProjectileSpeed, Fix64.Ratio(14, Simulation.TicksPerSecond))
                .Set(AbilityStatType.DurationTicks, 150)           // лужа горит 5 с
                .Set(AbilityStatType.BurnTicks, 10)                // тик трижды в секунду
                .Set(AbilityStatType.BurnDamagePercent, 18)        // урон одного тика
                .Set(AbilityStatType.CooldownTicks, 180);          // 6 с

        /// <summary>
        /// «КРУШЕНИЕ»: комбо из трёх ударов якорем, каждый по своему нажатию.
        ///
        /// ТРИ НАЖАТИЯ, А НЕ ОДНО. Автоматическая серия сыграла бы сама и
        /// отобрала бы у игрока единственное решение внутри способности —
        /// продолжать или разорвать и уйти. Ориентир диздока: около половины
        /// секунды на этап, то есть 15 тиков.
        ///
        /// Окно следующего нажатия шире этапа: комбо, которое рвётся от
        /// опоздания на кадр, читается как поломка ввода, а не как требование
        /// к точности.
        ///
        /// Урон растёт к третьему удару: завершение обязано ощущаться тяжелее
        /// первых двух, иначе прерывать комбо никогда не жалко.
        /// </summary>
        public static AbilityDefinition Wreck()
            => new AbilityDefinition("ability.wreck")
                .Set(AbilityStatType.Damage, 70)                   // первый и второй удар
                // Цена за всё комбо: списывается на первом нажатии, продолжения бесплатны.
                .Set(AbilityStatType.LavidiumCost, 25)
                .Set(AbilityStatType.Radius, Fix64.Ratio(28, 10))
                .Set(AbilityStatType.ArcCosine, Fix64.Ratio(3, 10))
                .Set(AbilityStatType.WindupTicks, 6)
                .Set(AbilityStatType.DurationTicks, 15)            // этап, 0.5 с
                .Set(AbilityStatType.ComboWindowTicks, 24)
                .Set(AbilityStatType.StunTicks, 9)                 // только завершающий удар
                .Set(AbilityStatType.CooldownTicks, 96);
    }
}
