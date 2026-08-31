namespace Game.Sim
{
    public enum Faction : byte
    {
        Wole = 0,     // игрок и союзники
        Orvill = 1,   // аппарат
        Rift = 2,     // сущности Разлома, враждебны всем
    }

    /// <summary>
    /// Структуры массивами, а не массив структур. Кэш процессора любит такую раскладку,
    /// и это единственное, что нужно, чтобы позже перевести горячие циклы на Jobs и Burst
    /// без переписывания логики.
    ///
    /// Пока обычные массивы. Переход на NativeArray делается в один заход, когда
    /// профилировщик покажет, что пора — не раньше.
    /// </summary>
    public sealed class EntityStore
    {
        public readonly int Capacity;

        public readonly FixVec2[] Position;
        public readonly FixVec2[] Velocity;

        /// <summary>
        /// Куда сущность смотрит: единичный вектор. Часть состояния симуляции,
        /// а не отрисовки — от него будет зависеть направление конуса способности
        /// и удара, поэтому решать его на стороне анимации нельзя.
        /// </summary>
        public readonly FixVec2[] Facing;
        public readonly int[] Health;
        public readonly int[] MaxHealth;
        public readonly Faction[] Side;
        public readonly bool[] Alive;

        /// <summary>Тик, когда сущность сможет атаковать снова.</summary>
        public readonly int[] NextAttackTick;

        /// <summary>
        /// Цель уже начатого замаха и тик контакта. Урон не может происходить
        /// в кадре старта анимации: иначе вспышка и смерть опережают клинок.
        /// </summary>
        public readonly int[] PendingAttackTarget;
        public readonly int[] AttackImpactTick;
        public readonly int[] PendingAttackVariant;

        /// <summary>
        /// Радиус тела. Тела не проходят сквозь друг друга: без этого сорок
        /// врагов сливаются в одну точку и толпа перестаёт читаться как толпа.
        ///
        /// Он же — «хитбокс» для отрисовки: кольцо под ногами рисуется ровно
        /// по этому числу, поэтому нарисованное и настоящее не могут разъехаться.
        /// </summary>
        public readonly Fix64[] BodyRadius;

        /// <summary>
        /// Насколько легко тело сдвигают чужие тела: 0 — не сдвинуть вовсе.
        ///
        /// Игрок намеренно тяжелее толпы. Будь веса равными, сорок врагов
        /// вынесли бы его с позиции, и он потерял бы контроль над собственным
        /// положением — а положение здесь единственное, чем он управляет.
        /// </summary>
        public readonly Fix64[] PushWeight;

        // ---- статы ----

        /// <summary>
        /// Лист статов каждой сущности. Индекс тот же, что у позиции: лист
        /// принадлежит слоту, а не живому существу, и переиспользуется вместе
        /// со слотом. Выделяются все разом при создании — аллокация в бою
        /// запрещена, а сущности не досоздаются.
        ///
        /// Ёмкость под модификаторы взята небольшой намеренно. Врагу хватит
        /// нескольких, а лист игрока дорастёт сам: StatSheet удваивает массив,
        /// и случается это при надевании вещи, а не в горячем пути.
        /// </summary>
        public readonly StatSheet[] Stats;

        // Ниже — то, что лист уже посчитал, разложенное по плоским массивам.
        // Бой читает ТОЛЬКО их: лазить в лист за каждым числом на каждом ударе
        // стоило бы дороже, чем весь остальной тик.
        //
        // Значения обновляет RefreshStats и только он.

        /// <summary>Урон автоатаки, уже целым числом.</summary>
        public readonly int[] Damage;

        /// <summary>Кулдаун автоатаки в тиках. Выведен из StatType.AttackSpeed.</summary>
        public readonly int[] AttackCooldown;

        /// <summary>Шаг за тик. Выведен из StatType.MoveSpeed.</summary>
        public readonly Fix64[] MoveStep;

        public readonly Fix64[] CritChance;
        public readonly Fix64[] CritMultiplier;

        /// <summary>
        /// Броня гасит физический урон по кривой Path of Exile, сопротивление
        /// огню — множитель с потолком 75%. Формулы живут в CombatStats.
        /// </summary>
        public readonly Fix64[] Armor;
        public readonly Fix64[] FireResist;

        public int Count { get; private set; }

        /// <summary>Взгляд по умолчанию. Ненулевой: нулевой вектор не задаёт направления.</summary>
        private static readonly FixVec2 FacingDefault = new FixVec2(Fix64.One, Fix64.Zero);

        /// <summary>Полметра. Тело примерно метр в поперечнике — как и рисованный силуэт.</summary>
        public static readonly Fix64 DefaultBodyRadius = Fix64.Ratio(45, 100);

        /// <summary>Потолок радиуса. По нему считается радиус запроса при расталкивании.</summary>
        public static readonly Fix64 MaxBodyRadius = Fix64.Ratio(80, 100);

        public EntityStore(int capacity)
        {
            Capacity = capacity;
            Position = new FixVec2[capacity];
            Velocity = new FixVec2[capacity];
            Facing = new FixVec2[capacity];
            Health = new int[capacity];
            MaxHealth = new int[capacity];
            Side = new Faction[capacity];
            Alive = new bool[capacity];
            NextAttackTick = new int[capacity];
            PendingAttackTarget = new int[capacity];
            AttackImpactTick = new int[capacity];
            PendingAttackVariant = new int[capacity];
            BodyRadius = new Fix64[capacity];
            PushWeight = new Fix64[capacity];

            Stats = new StatSheet[capacity];
            for (int i = 0; i < capacity; i++) Stats[i] = new StatSheet(8);

            Damage = new int[capacity];
            AttackCooldown = new int[capacity];
            MoveStep = new Fix64[capacity];
            CritChance = new Fix64[capacity];
            CritMultiplier = new Fix64[capacity];
            Armor = new Fix64[capacity];
            FireResist = new Fix64[capacity];
        }

        /// <summary>
        /// Сущности НИКОГДА не удаляются и не переупорядочиваются: индекс — это identity.
        /// Мёртвые остаются в массиве с Alive = false. Порядок обхода обязан быть
        /// одинаковым всегда, иначе детерминизм ломается.
        ///
        /// Лист статов слота сбрасывается здесь целиком, а базы ставятся нейтральные:
        /// EntityStore не знает баланса и знать его не должен. Настоящие числа
        /// ставит поверх тот, кто эту сущность создаёт.
        /// </summary>
        public int Spawn(FixVec2 position, int health, Faction side)
        {
            int id = Count++;
            Position[id] = position;
            Velocity[id] = FixVec2.Zero;
            Facing[id] = FacingDefault;
            Side[id] = side;
            Alive[id] = true;
            NextAttackTick[id] = 0;
            PendingAttackTarget[id] = -1;
            AttackImpactTick[id] = 0;
            PendingAttackVariant[id] = 0;
            BodyRadius[id] = DefaultBodyRadius;
            PushWeight[id] = Fix64.One;

            StatSheet sheet = Stats[id];
            sheet.ClearModifiers();
            sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(health));
            sheet.SetBase(StatType.Damage, Fix64.Zero);

            // Единица, а не ноль: нулевая скорость атаки — это деление на ноль
            // при переводе в кулдаун.
            sheet.SetBase(StatType.AttackSpeed, Fix64.One);
            sheet.SetBase(StatType.MoveSpeed, Fix64.Zero);
            sheet.SetBase(StatType.CritChance, Fix64.Zero);

            // Тоже единица: множитель крита — множитель, и по умолчанию он
            // обязан ничего не менять.
            sheet.SetBase(StatType.CritMultiplier, Fix64.One);
            sheet.SetBase(StatType.Armor, Fix64.Zero);
            sheet.SetBase(StatType.FireResist, Fix64.Zero);

            RefreshStats(id);
            Health[id] = MaxHealth[id];
            return id;
        }

        /// <summary>
        /// Переносит посчитанное листом в плоские массивы, которые читает бой.
        ///
        /// Зовётся при рождении и при смене снаряжения, узлов или бафов — то есть
        /// редко. Каждый тик звать не надо и нельзя: пересчёт «на всякий случай»
        /// на каждой сущности — главная причина, по которой ARPG начинают
        /// тормозить именно на поздних билдах.
        /// </summary>
        public void RefreshStats(int id)
        {
            StatSheet sheet = Stats[id];
            if (sheet.IsDirty) sheet.Recalculate();

            Damage[id] = CombatStats.RoundToInt(sheet.Get(StatType.Damage));
            AttackCooldown[id] = CombatStats.AttackCooldownTicks(sheet.Get(StatType.AttackSpeed));
            MoveStep[id] = CombatStats.MoveStepPerTick(sheet.Get(StatType.MoveSpeed));
            CritChance[id] = sheet.Get(StatType.CritChance);
            CritMultiplier[id] = sheet.Get(StatType.CritMultiplier);
            Armor[id] = sheet.Get(StatType.Armor);
            FireResist[id] = sheet.Get(StatType.FireResist);

            int maxHealth = CombatStats.RoundToInt(sheet.Get(StatType.MaxHealth));
            if (maxHealth < 1) maxHealth = 1;
            MaxHealth[id] = maxHealth;

            // Текущее здоровье только ПОДРЕЗАЕТСЯ под новый максимум и никогда
            // не растёт само: надетая посреди боя вещь не должна работать зельем,
            // а снятая — отнимать больше, чем давала.
            if (Health[id] > maxHealth) Health[id] = maxHealth;
        }

        public void Clear()
        {
            Count = 0;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Count);
            for (int i = 0; i < Count; i++)
            {
                Hashing.Mix(ref hash, Position[i].X);
                Hashing.Mix(ref hash, Position[i].Y);
                Hashing.Mix(ref hash, Velocity[i].X);
                Hashing.Mix(ref hash, Velocity[i].Y);
                Hashing.Mix(ref hash, Facing[i].X);
                Hashing.Mix(ref hash, Facing[i].Y);
                Hashing.Mix(ref hash, Health[i]);
                Hashing.Mix(ref hash, MaxHealth[i]);
                Hashing.Mix(ref hash, (int)Side[i]);
                Hashing.Mix(ref hash, Alive[i] ? 1 : 0);
                Hashing.Mix(ref hash, NextAttackTick[i]);
                Hashing.Mix(ref hash, PendingAttackTarget[i]);
                Hashing.Mix(ref hash, AttackImpactTick[i]);
                Hashing.Mix(ref hash, PendingAttackVariant[i]);
                Hashing.Mix(ref hash, BodyRadius[i]);
                Hashing.Mix(ref hash, PushWeight[i]);

                // Лист статов — такая же часть состояния, как позиция. Не попади
                // он в хеш, расхождение в снаряжении жило бы незамеченным до тех
                // пор, пока не разъедется здоровье.
                Stats[i].HashInto(ref hash);
            }
        }
    }
}
