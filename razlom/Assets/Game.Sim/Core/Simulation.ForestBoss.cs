using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// Действие Хозяина Чащи. Значение идёт в хеш — новые только в конец.
    /// Этап 1 («Ядро»): вступление, рёв, лапа, топот. Нырок, прорастание,
    /// пыльца, ливень и буря — значения зарезервированы под этапы 2–3
    /// (Simulation.ForestBoss.Attacks.cs / .Storm.cs), ядро их не начинает.
    /// </summary>
    public enum ThicketMasterAction : byte
    {
        None = 0,

        /// <summary>Вырывает лапы из земли после сна. Без контакта; за ним сразу рёв.</summary>
        Wake = 1,

        /// <summary>Рёв: кольцо 2–5,5 м, 45 тиков, без урона, отброс 2 м.</summary>
        Roar = 2,

        /// <summary>Лапа: сектор 120°, 3,6 м. В фазе 3 — двойная (Stages = 2).</summary>
        Paw = 3,

        /// <summary>Дыбом и топот: круг 4,5 м вокруг себя, отброс 2 м.</summary>
        Stomp = 4,

        /// <summary>Этап 2: нырок в корни.</summary>
        Dive = 5,

        /// <summary>Этап 2: прорастание (6 кругов по следам героя).</summary>
        Sprout = 6,

        /// <summary>Этап 2: облака пыльцы.</summary>
        Pollen = 7,

        /// <summary>Этап 2: ягодный ливень.</summary>
        Rain = 8,

        /// <summary>Этап 3: буря цветения (Simulation.ForestBoss.Storm.cs).</summary>
        Storm = 9,
    }

    /// <summary>
    /// Текущее действие Хозяина Чащи. Изменяемая структура: каждое новое поле
    /// обязано попасть в HashThicketMasters. Действие из нескольких контактов
    /// (двойная лапа, прорастание, ливень) идёт шагами: Stage — номер текущего
    /// шага, Stages — сколько всего, ImpactTick — контакт текущего шага,
    /// LastImpactTick — последнего (до него босс держит крупный жетон).
    /// </summary>
    public struct ThicketMasterState
    {
        public int Serial;
        public ThicketMasterAction Action;

        /// <summary>Начало всего действия (первого замаха).</summary>
        public int StartTick;

        /// <summary>Начало замаха текущего шага.</summary>
        public int StageStartTick;

        /// <summary>Контакт текущего шага.</summary>
        public int ImpactTick;

        /// <summary>Контакт последнего шага.</summary>
        public int LastImpactTick;

        /// <summary>Конец стойки: в этот тик действие снимается.</summary>
        public int EndTick;

        public int Stage, Stages;

        /// <summary>Свободное число действия. Рёв — биты порогов, которые он закрыл (ThicketRoar*Bit).</summary>
        public int Tag;

        /// <summary>Откуда бьёт (тело босса в начале шага), куда смотрит, куда целит (центр фигуры).</summary>
        public FixVec2 Origin, Direction, Target;

        /// <summary>Номер метки текущего шага; 0 — метки нет (пул полон или у действия её нет).</summary>
        public int TelegraphSerial;

        /// <summary>Контакт текущего шага разрешён (попал или мимо).</summary>
        public bool HitResolved;
    }

    /// <summary>
    /// Память Хозяина Чащи между действиями: сон, фазы, рёвы, отдых, Часы,
    /// окно топота и свой поток решений. Поля под этапы 2–3 заведены сразу,
    /// чтобы их файлы не трогали структуру ядра.
    /// </summary>
    public struct ThicketMasterMemory
    {
        public int SpawnTick;

        /// <summary>Точка появления — центр поводка.</summary>
        public FixVec2 Home;

        public bool Awake;
        public int WakeTick;

        /// <summary>Фаза 1–3 по здоровью (0 — ещё спит). Назад не возвращается.</summary>
        public int Phase;

        /// <summary>Биты рёвов: прозвучавшие и ждущие конца текущего действия.</summary>
        public int RoarsDone, RoarsPending;

        /// <summary>Отдых после действия: раньше нового не начинает.</summary>
        public int NextActionTick;

        /// <summary>Песочные Часы: до этого тика стоит, таймеры сдвинуты.</summary>
        public int FrozenUntil;

        /// <summary>Окно топота, 90 бит: 1 — герой в этот тик ближе 3,5 м. Младший — текущий тик.</summary>
        public ulong NearLo, NearHi;

        /// <summary>
        /// Этап 3: связка фазы 3. ChainNext — её следующее действие (None —
        /// связки нет), ChainStep — тик, с которого оно ждёт старта.
        /// </summary>
        public int ChainStep;
        public ThicketMasterAction ChainNext;

        /// <summary>Этап 3: когда следующая буря.</summary>
        public int StormNextTick;

        /// <summary>Этап 2: когда нырок «раз в ~10 с».</summary>
        public int DiveNextTick;

        /// <summary>Свой поток решений: Pcg32(_encounterSeed ^ k, ThicketStream), заводится при пробуждении.</summary>
        public Pcg32 Rng;
        public bool RngSeeded;
    }

    /// <summary>
    /// ХОЗЯИН ЧАЩИ — босс леса (план 01.10, этап 4 «Sim», стадия 1 «Ядро»).
    ///
    /// Спит укоренённым, пока не прошло 90 тиков с появления и герой не
    /// подошёл на 9 м (или уже ранил его); потом 30 тиков вырывает лапы и
    /// ревёт. Фазы по здоровью: 66/50/33 — рёв после текущего действия, на
    /// 66 — одна волна подмоги (UpdateBossAdds). Пока подмога жива,
    /// перезарядки и отдых ×1,25; с 50% здоровья — ×0,85. Замахи не
    /// сокращаются никогда.
    ///
    /// Не оглушается и не двигается чужой волей: вес 0 (волок и толчок его
    /// не берут), ход — до проверки оглушения в MoveEnemies, оглушение и
    /// принудительное движение снимаются в конце каждого его тика. Песочные
    /// Часы не оглушают — сдвигают таймеры на ThicketHourglassShiftTicks.
    ///
    /// РАСШИРЕНИЕ (этапы 2–3). Ядро зовёт частичные методы ниже; их тела —
    /// в Simulation.ForestBoss.Attacks.cs / .Storm.cs. Пустой частичный
    /// метод компилятор выбрасывает, ядро от них не зависит.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- включение ----

        /// <summary>
        /// Переключатель владельца: true — SetupBossArena ставит Хозяина Чащи
        /// вместо временного босса-Хранителя. Включён 02.10, когда собрались клипы и вид.
        /// </summary>
        public const bool UseThicketMasterBoss = true;

        /// <summary>Переключатель этой симуляции (по умолчанию — константа). Тесты включают его у себя.</summary>
        public bool ThicketMasterBossEnabled { get; set; } = UseThicketMasterBoss;

        // ---- тело и ход ----

        public static readonly Fix64 ThicketMasterMoveSpeed = Fix64.Ratio(13, 5);

        /// <summary>Поворот 4,5° за тик: заход сбоку и сзади имеет смысл.</summary>
        private static readonly Fix64 ThicketTurnStep = Fix64.TwoPi / 80;
        private static readonly Fix64 ThicketTurnCos = Fix64.Cos(ThicketTurnStep);
        private static readonly Fix64 ThicketTurnSin = Fix64.Sin(ThicketTurnStep);

        /// <summary>Шаг только вдоль взгляда: ноль при cos 0,6, как у Вендиго.</summary>
        private static readonly Fix64 ThicketWalkAlignFrom = Fix64.Ratio(6, 10);

        /// <summary>Поводок: дальше 10 м от точки появления не уходит.</summary>
        public static readonly Fix64 ThicketLeash = Fix64.FromInt(10);

        /// <summary>Второй круг корпуса: центр в 1,1 м за телом, радиус 0,85 — выталкивает героя из-под крупа.</summary>
        public static readonly Fix64 ThicketRumpOffset = Fix64.Ratio(11, 10);
        public static readonly Fix64 ThicketRumpRadius = Fix64.Ratio(85, 100);
        private static readonly Fix64 ThicketRumpMaxPush = Fix64.Ratio(3, 10);

        // ---- вступление ----

        public const int ThicketMinSleepTicks = 90;
        public const int ThicketWakeTicks = 30;
        public static readonly Fix64 ThicketWakeRange = Fix64.FromInt(9);

        // ---- лапа ----

        public const int ThicketPawWindupTicks = 24, ThicketPawStrikeTicks = 1, ThicketPawRecoveryTicks = 24;

        /// <summary>
        /// Правило дока «больше 60 урона — от 30 тиков, моб стоит» (DESIGN.md,
        /// бой рогалика 26.09). «Сложно» и ярость поднимают лапу на арене 9 до
        /// 66 — тогда замах 30: удлиняется замах, урон не режется.
        /// </summary>
        public const int ThicketPawHeavyDamage = 60, ThicketPawHeavyWindupTicks = 30;
        public static readonly Fix64 ThicketPawRadius = Fix64.Ratio(18, 5);
        public static readonly Fix64 ThicketPawArcCos = Fix64.Ratio(1, 2);

        /// <summary>Лапа начинается, когда герой ближе 3,2 м между центрами и в ±40° от взгляда.</summary>
        public static readonly Fix64 ThicketPawStartRange = Fix64.Ratio(16, 5);
        private static readonly Fix64 ThicketPawFrontCos = Fix64.Ratio(766, 1000);

        /// <summary>Подходит к герою до 2,8 м и стоит.</summary>
        public static readonly Fix64 ThicketHoldDistance = Fix64.Ratio(14, 5);

        /// <summary>Вторая лапа двойной доворачивает к герою не больше чем на 30°.</summary>
        private static readonly Fix64 ThicketPawRetargetCos = Fix64.Ratio(86603, 100000);
        private static readonly Fix64 ThicketPawRetargetSin = Fix64.Ratio(1, 2);

        // ---- топот ----

        public const int ThicketStompWindupTicks = 33, ThicketStompStrikeTicks = 2, ThicketStompRecoveryTicks = 36;
        public static readonly Fix64 ThicketStompRadius = Fix64.Ratio(9, 2);

        /// <summary>Топот — если из последних 90 тиков герой был ближе 3,5 м (между центрами) не меньше 60.</summary>
        public const int ThicketStompWindowTicks = 90, ThicketStompNearTicks = 60;
        public static readonly Fix64 ThicketStompNearRange = Fix64.Ratio(7, 2);

        /// <summary>
        /// Топот по правилу — не чаще раза за окно (90 тиков от начала прошлого):
        /// правило спецификации «≥60 из ~3 с» остаётся главным, перезарядка лишь
        /// не даёт двух топотов в одно окно. Окно после топота начинается
        /// заново, стойка 71 тик короче перезарядки — прижатого героя между
        /// топотами бьёт лапа (стенд темпа: топот и лапа по очереди).
        /// Топот связки фазы 3 перезарядку не ждёт, но ставит её.
        /// </summary>
        public const int ThicketStompCooldownTicks = ThicketStompWindowTicks;

        // ---- рёв ----

        public const int ThicketRoarWindupTicks = 45, ThicketRoarRecoveryTicks = 15;
        public static readonly Fix64 ThicketRoarInnerRadius = Fix64.FromInt(2);
        public static readonly Fix64 ThicketRoarOuterRadius = Fix64.Ratio(11, 2);
        public const int ThicketRoarIntroBit = 1, ThicketRoar66Bit = 2, ThicketRoar50Bit = 4, ThicketRoar33Bit = 8;

        // ---- отброс (топот и рёв) ----

        public const int ThicketKnockbackTicks = 10;
        public static readonly Fix64 ThicketKnockbackDistance = Fix64.FromInt(2);

        // ---- темп ----

        /// <summary>
        /// Отдых после действия по фазам — режется только он и перезарядки, не
        /// замахи. Подобран стендом темпа (ThicketMasterTests.Cadence_*) под
        /// цель 2,6 / 2,3 / 2,0 с между атаками вместе со стойками фаз 2–3 ниже:
        /// замер 2,59 / 2,16–2,32 / 2,15 с (волна бури — своя атака).
        /// </summary>
        public const int ThicketRestPhase1Ticks = 3, ThicketRestPhase2Ticks = 0, ThicketRestPhase3Ticks = 0;
        public const int ThicketAddsCooldownPercent = 125, ThicketEnragedCooldownPercent = 85;

        /// <summary>
        /// Фазы 2 и 3: стойка после последнего контакта (столбец «отдых»
        /// таблицы атак спецификации — лапа 24, топот 36, нырок 36, ливень 60,
        /// остаток прорастания, стойка пыльцы) — этот процент; фаза 1 — как в
        /// таблице. Замахи не трогаются; отдых после связки — те же 45 тиков
        /// от удара топота. Окно «бить» после удара в фазах 2–3 короче.
        /// </summary>
        public const int ThicketPhase2RecoveryPercent = 75, ThicketPhase3RecoveryPercent = 50;

        /// <summary>Песочные Часы: не оглушают, сдвигают таймеры босса на 2 с.</summary>
        public const int ThicketHourglassShiftTicks = 60;

        // ---- урон: доли от лапы, числа на арене 9 (EnemyArchetypes.ThicketMasterPawDamage × 164%) ----

        public const int ThicketPawDamageA9 = 41, ThicketStompDamageA9 = 62, ThicketDiveDamageA9 = 66;
        public const int ThicketSproutDamageA9 = 34, ThicketRainDamageA9 = 30, ThicketStormDamageA9 = 70;
        public const int ThicketPollenDamageA9 = 5;

        /// <summary>Мест в таблице перезарядок на одного босса: по значению ThicketMasterAction.</summary>
        public const int ThicketActionSlots = 16;

        private const ulong ThicketStream = 0x5448494B4D535452UL;   // "THIKMSTR"
        private const ulong ThicketNearHiMask = (1UL << (ThicketStompWindowTicks - 64)) - 1;

        // ---- состояние (лениво, как у Корнехвата) ----

        private ThicketMasterState[] _thicketMasters;
        private ThicketMasterMemory[] _thicketMemory;
        private int[] _thicketReady;
        private int _thicketSerial;
        private ThicketMasterAction[] _thicketCandidates;
        private int[] _thicketWeights;
        private int _thicketCandidateCount;

        private ThicketMasterState[] ThicketMasters => _thicketMasters ??= new ThicketMasterState[Entities.Capacity];
        private ThicketMasterMemory[] ThicketMemory => _thicketMemory ??= new ThicketMasterMemory[Entities.Capacity];
        private int[] ThicketReady => _thicketReady ??= new int[Entities.Capacity * ThicketActionSlots];

        // ---- точки расширения этапов 2–3 ----

        /// <summary>Перед правилом топота: буря по расписанию, связки фазы 3. Выбор — в choice.</summary>
        partial void ThicketChooseForced(int id, ref ThicketMasterAction choice);

        /// <summary>После правила топота: нырок, если герой дальше 7 м или раз в ~10 с.</summary>
        partial void ThicketChooseRule(int id, ref ThicketMasterAction choice);

        /// <summary>Взвешенный выбор: добавить свои варианты через AddThicketCandidate.</summary>
        partial void ThicketAddCandidates(int id);

        /// <summary>Начать действие не из ядра. started = false — не сейчас (бюджет, такт), попробует в следующий тик.</summary>
        partial void ThicketStartExtra(int id, ThicketMasterAction action, ref bool started);

        /// <summary>Тик идущего действия не из ядра: контакты, шаги, конец (FinishThicketAction).</summary>
        partial void ThicketAdvanceExtra(int id);

        /// <summary>Действие снято (смерть, смерть героя): снять свои метки, зоны, тело под землёй.</summary>
        partial void ThicketCancelExtra(int id);

        /// <summary>Действие закончилось: связки ставят ChainNext, перезарядки и т. п.</summary>
        partial void ThicketFinishedExtra(int id, ThicketMasterAction finished);

        /// <summary>Связка: оборвать стойку после последнего контакта (cut = true) — следующее действие встанет сразу.</summary>
        partial void ThicketChainCutExtra(int id, ref bool cut);

        /// <summary>Рёв закончился; thresholds — биты порогов, которые он закрыл (на 33 — сразу буря).</summary>
        partial void ThicketRoarDone(int id, int thresholds);

        /// <summary>Раз в тик до боссов — и без живого босса: облака пыльцы и прочие зоны.</summary>
        partial void ThicketZonesTick();

        /// <summary>Ход во время своего действия (нырок под землёй). handled = true — ядро ход не делает.</summary>
        partial void ThicketMoveExtra(int id, ref bool handled);

        /// <summary>Часы: сдвинуть свои таймеры и зоны на ticks.</summary>
        partial void ThicketHourglassExtra(int id, int ticks);

        /// <summary>Вес крупных меток своего действия в бюджете (≤2, буря — 4), начало и ближайший удар.</summary>
        partial void ThicketMarkWeightExtra(int id, ref int weight, ref int start, ref int impact);

        /// <summary>Контакты своего действия в такт ударов (AddHeroContact).</summary>
        partial void ThicketContactsExtra(int id);

        partial void ThicketResetExtra();
        partial void ThicketHashExtra(ref ulong hash);

        // ---- чтение для вида, тестов и стенда ----

        public bool TryGetThicketMasterAction(int id, out ThicketMasterState state)
        {
            state = _thicketMasters != null && (uint)id < (uint)_thicketMasters.Length ? _thicketMasters[id] : default;
            return state.Serial != 0;
        }

        public bool TryGetThicketMasterMemory(int id, out ThicketMasterMemory memory)
        {
            bool known = _thicketMemory != null && (uint)id < (uint)Entities.Count
                && Entities.Kind[id] == EnemyKind.ForestThicketMaster;
            memory = known ? _thicketMemory[id] : default;
            return known;
        }

        /// <summary>Фаза босса 1–3; 0 — спит или это не босс.</summary>
        public int ThicketMasterPhase(int id) => TryGetThicketMasterMemory(id, out var m) ? m.Phase : 0;

        public bool ThicketMasterAwake(int id) => TryGetThicketMasterMemory(id, out var m) && m.Awake;

        /// <summary>Сколько ещё тиков босс стоит под Песочными Часами.</summary>
        public int ThicketMasterFrozenTicksLeft(int id)
            => TryGetThicketMasterMemory(id, out var m) && m.FrozenUntil > Tick ? m.FrozenUntil - Tick : 0;

        /// <summary>Сколько из последних 90 тиков герой был ближе 3,5 м (правило топота).</summary>
        public int ThicketMasterNearTicks(int id)
            => TryGetThicketMasterMemory(id, out var m) ? CountBits(m.NearLo) + CountBits(m.NearHi) : 0;

        /// <summary>Когда действие action снова готово (перезарядка); 0 — готово.</summary>
        public int ThicketReadyTick(int id, ThicketMasterAction action)
            => _thicketReady != null && (uint)id < (uint)Entities.Capacity ? _thicketReady[id * ThicketActionSlots + (int)action] : 0;

        /// <summary>Сдвигает перезарядку действия. Для стендов и тестов.</summary>
        public void SetThicketReadyTick(int id, ThicketMasterAction action, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketReady[id * ThicketActionSlots + (int)action] = tick;
        }

        /// <summary>Центр второго круга корпуса (за крупом).</summary>
        public FixVec2 ThicketRumpCenter(int id)
            => Entities.Position[id] - Entities.Facing[id].Normalized() * ThicketRumpOffset;

        // ---- урон: всё — доля лапы (урона листа), глубина, «Сложно» и ярость растят разом ----

        public int ThicketPawDamageOf(int id) => Entities.Damage[id];

        /// <summary>Замах лапы: 24, а при уроне больше 60 — 30 (ThicketPawHeavyDamage).</summary>
        public int ThicketPawWindupOf(int id)
            => ThicketPawDamageOf(id) > ThicketPawHeavyDamage ? ThicketPawHeavyWindupTicks : ThicketPawWindupTicks;
        public int ThicketShareOf(int id, int authoredA9) => EnemyArchetypes.Share(Entities.Damage[id], authoredA9, ThicketPawDamageA9);
        public int ThicketStompDamageOf(int id) => ThicketShareOf(id, ThicketStompDamageA9);

        // ---- фигуры: одна на метку и на попадание ----

        public static EnemyTelegraph ThicketPawSector(FixVec2 origin, FixVec2 direction)
            => EnemyTelegraph.Sector(origin, direction, ThicketPawRadius, ThicketPawArcCos);

        public static EnemyTelegraph ThicketStompCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketStompRadius);

        public static EnemyTelegraph ThicketRoarRing(FixVec2 center)
            => EnemyTelegraph.Ring(center, ThicketRoarInnerRadius, ThicketRoarOuterRadius);

        /// <summary>Событийный вид действия.</summary>
        public static EnemyActionKind ThicketActionKind(ThicketMasterAction action)
        {
            switch (action)
            {
                case ThicketMasterAction.Wake: return EnemyActionKind.ThicketWake;
                case ThicketMasterAction.Roar: return EnemyActionKind.ThicketRoar;
                case ThicketMasterAction.Paw: return EnemyActionKind.ThicketPaw;
                case ThicketMasterAction.Stomp: return EnemyActionKind.ThicketStomp;
                case ThicketMasterAction.Dive: return EnemyActionKind.ThicketDive;
                case ThicketMasterAction.Sprout: return EnemyActionKind.ThicketSprout;
                case ThicketMasterAction.Pollen: return EnemyActionKind.ThicketPollen;
                case ThicketMasterAction.Rain: return EnemyActionKind.ThicketRain;
                case ThicketMasterAction.Storm: return EnemyActionKind.ThicketStorm;
                default: return EnemyActionKind.None;
            }
        }

        // ---- настройка и сброс ----

        private void ConfigureThicketMaster(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestThicketMaster);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            // Вес 0: расталкивание его не сдвигает, волок и толчок сабли не берут.
            Entities.PushWeight[id] = Fix64.Zero;
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, ThicketMasterMoveSpeed);
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.One);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = Progression.BossKillXp;
            ThicketMasters[id] = default;
            ThicketMemory[id] = new ThicketMasterMemory { SpawnTick = Tick, Home = Entities.Position[id] };
            Array.Clear(ThicketReady, id * ThicketActionSlots, ThicketActionSlots);
        }

        private void ResetThicketMasters()
        {
            if (_thicketMasters != null) Array.Clear(_thicketMasters, 0, _thicketMasters.Length);
            if (_thicketMemory != null) Array.Clear(_thicketMemory, 0, _thicketMemory.Length);
            if (_thicketReady != null) Array.Clear(_thicketReady, 0, _thicketReady.Length);
            _thicketSerial = 0;
            _thicketCandidateCount = 0;
            ThicketResetExtra();
        }

        /// <summary>Есть ли на арене Хозяин Чащи (живой или мёртвый) или он уже действовал.</summary>
        private bool ThicketMasterPresent()
        {
            if (_thicketSerial != 0) return true;
            for (int id = 1; id < Entities.Count; id++)
                if (Entities.Kind[id] == EnemyKind.ForestThicketMaster) return true;
            return false;
        }

        private void HashThicketMasters(ref ulong hash)
        {
            if (!ThicketMasterPresent()) return;
            Hashing.Mix(ref hash, 0x54484B4D); Hashing.Mix(ref hash, _thicketSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                var a = ThicketMasters[id]; var m = ThicketMemory[id];
                Hashing.Mix(ref hash, id);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Action);
                Hashing.Mix(ref hash, a.StartTick); Hashing.Mix(ref hash, a.StageStartTick);
                Hashing.Mix(ref hash, a.ImpactTick); Hashing.Mix(ref hash, a.LastImpactTick); Hashing.Mix(ref hash, a.EndTick);
                Hashing.Mix(ref hash, a.Stage); Hashing.Mix(ref hash, a.Stages); Hashing.Mix(ref hash, a.Tag);
                Hashing.Mix(ref hash, a.Origin.X); Hashing.Mix(ref hash, a.Origin.Y);
                Hashing.Mix(ref hash, a.Direction.X); Hashing.Mix(ref hash, a.Direction.Y);
                Hashing.Mix(ref hash, a.Target.X); Hashing.Mix(ref hash, a.Target.Y);
                Hashing.Mix(ref hash, a.TelegraphSerial); Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
                Hashing.Mix(ref hash, m.SpawnTick); Hashing.Mix(ref hash, m.Home.X); Hashing.Mix(ref hash, m.Home.Y);
                Hashing.Mix(ref hash, m.Awake ? 1 : 0); Hashing.Mix(ref hash, m.WakeTick); Hashing.Mix(ref hash, m.Phase);
                Hashing.Mix(ref hash, m.RoarsDone); Hashing.Mix(ref hash, m.RoarsPending);
                Hashing.Mix(ref hash, m.NextActionTick); Hashing.Mix(ref hash, m.FrozenUntil);
                Hashing.Mix(ref hash, m.NearLo); Hashing.Mix(ref hash, m.NearHi);
                Hashing.Mix(ref hash, m.ChainStep); Hashing.Mix(ref hash, (int)m.ChainNext);
                Hashing.Mix(ref hash, m.StormNextTick); Hashing.Mix(ref hash, m.DiveNextTick);
                Hashing.Mix(ref hash, m.RngSeeded ? 1 : 0);
                Hashing.Mix(ref hash, m.Rng.State); Hashing.Mix(ref hash, m.Rng.Increment);
                for (int k = 0; k < ThicketActionSlots; k++) Hashing.Mix(ref hash, ThicketReady[id * ThicketActionSlots + k]);
            }
            ThicketHashExtra(ref hash);
        }

        // ---- арена босса (переключатель) ----

        /// <summary>
        /// SetupBossArena с включённым переключателем: то же, что у временного
        /// босса (комната перед выходом, пачка выхода в плане, подмога), но
        /// Хозяин Чащи с ThicketMasterHealth и без надбавки ×1,5 к удару: его
        /// база уже своя (25 — лапа 41 на арене 9).
        /// </summary>
        private EncounterPlan SetupThicketMasterArena(LayoutMap map, ulong spawnSeed, int healthPercent,
            EncounterSettings settings, int hardPercent, int arena)
        {
            SetupRift(map, spawnSeed, 0, 0, 1);
            int module = map.GetPlaced(map.GetExit(0)).Parent;
            var radius = EnemyArchetypes.ThicketMasterBodyRadius;
            var center = BossFloorPoint(map, map.CenterOf(module), radius);
            var rng = new Pcg32(spawnSeed, 0x424F5353UL);
            var pack = settings.Pick(EncounterRole.ExitGuard, ref rng);
            int boss = Entities.Spawn(center,
                EnemyArchetypes.ScaleHealth(EnemyArchetypes.ThicketMasterHealth, healthPercent, hardPercent), Faction.Orvill);
            ConfigureEnemy(boss, EnemyKind.ForestThicketMaster);
            Entities.Stats[boss].SetBase(StatType.Damage, Entities.Damage[boss]
                * Fix64.Ratio(settings.DamagePercent * hardPercent, 10000));
            Entities.RefreshStats(boss);
            _events.Add(SimEvent.Spawn(boss, center));
            var elite = new bool[Entities.Capacity]; elite[boss] = true;
            _eliteMask = elite;
            var sites = new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.ExitGuard,
                module, -1, pack.Id, center, boss, 1) };
            Grid.Rebuild(Entities);
            var plan = new EncounterPlan(sites, elite, Fix64.FromInt(9), 0) { BossId = boss };
            if (map.Routes != null)
            {
                PrepareBossAdds(map, spawnSeed, arena, healthPercent, settings.DamagePercent, hardPercent, boss);
                _encounterPlan = plan;
            }
            return plan;
        }

        // ---- Песочные Часы ----

        /// <summary>
        /// Зовёт StartHourglass после оглушения всех врагов: с босса оглушение
        /// снимается, а все его таймеры (действие, метки, отдых, перезарядки)
        /// сдвигаются на ThicketHourglassShiftTicks; столько же он стоит.
        /// </summary>
        private void ShiftThicketMastersForHourglass()
        {
            if (_thicketMemory == null) return;
            const int shift = ThicketHourglassShiftTicks;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThicketMaster || !Entities.Alive[id]) continue;
                Statuses.StunUntilTick[id] = 0;
                ref var m = ref ThicketMemory[id];
                m.FrozenUntil = Math.Max(m.FrozenUntil, Tick) + shift;
                if (m.NextActionTick > Tick) m.NextActionTick += shift;
                if (m.StormNextTick > Tick) m.StormNextTick += shift;
                if (m.DiveNextTick > Tick) m.DiveNextTick += shift;
                for (int k = 0; k < ThicketActionSlots; k++)
                    if (ThicketReady[id * ThicketActionSlots + k] > Tick) ThicketReady[id * ThicketActionSlots + k] += shift;
                ref var a = ref ThicketMasters[id];
                if (a.Serial != 0)
                {
                    // Сдвигаются только контакты впереди: прошедший удар в стойке
                    // не должен снова держать крупный жетон 60 тиков.
                    a.StageStartTick += shift;
                    if (a.ImpactTick >= Tick) a.ImpactTick += shift;
                    if (a.LastImpactTick >= Tick) a.LastImpactTick += shift;
                    a.EndTick += shift;
                }
                // Свои ещё не сработавшие метки ждут вместе с боссом: заполнение тянется.
                for (int slot = 0; slot < _telegraphHighWater; slot++)
                {
                    var t = _telegraphs[slot];
                    if (t.Serial == 0 || t.Source != id || !t.IsActive) continue;
                    _telegraphs[slot] = new EnemyTelegraph(t.Serial, t.Source, t.StartTick, t.ImpactTick + shift,
                        t.EndTick + shift, t.Shape, t.State, t.Flags, t.Origin, t.Direction, t.Radius,
                        t.InnerRadius, t.ArcCos, t.Width, t.Length);
                }
                ThicketHourglassExtra(id, shift);
            }
        }

        // ---- бюджет меток, жетон, такт ----

        /// <summary>
        /// Держит ли босс крупный жетон: от начала любого замаха (кроме
        /// пробуждения) до последнего контакта. Пока держит, подмога крупных
        /// меток не начинает: BigAttackTokenFree считает его за весь лимит.
        /// </summary>
        internal bool ThicketMasterHoldsBigToken(int id)
        {
            if (_thicketMasters == null || !Entities.Alive[id]) return false;
            var a = _thicketMasters[id];
            if (a.Serial != 0 && a.Action != ThicketMasterAction.Wake && Tick <= a.LastImpactTick) return true;
            bool holds = false;
            ThicketHoldsTokenExtra(id, ref holds);
            return holds;
        }

        /// <summary>Жетон без идущего действия: буря пора, а земля ещё не пуста — подмога новых крупных не начинает.</summary>
        partial void ThicketHoldsTokenExtra(int id, ref bool holds);

        /// <summary>Крупные метки босса в бюджете: топот и рёв — 1, лапа — 0 (знак на теле), прочее — этапы 2–3.</summary>
        private int ThicketMasterMarkWeight(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            if (_thicketMasters == null) return 0;
            var a = _thicketMasters[id];
            if (a.Serial == 0) return 0;
            int weight = 0;
            switch (a.Action)
            {
                case ThicketMasterAction.Stomp:
                case ThicketMasterAction.Roar:
                    if (a.HitResolved || Tick > a.ImpactTick) return 0;
                    start = a.StartTick; impact = a.ImpactTick;
                    return 1;
                case ThicketMasterAction.Wake:
                case ThicketMasterAction.Paw:
                    return 0;
            }
            ThicketMarkWeightExtra(id, ref weight, ref start, ref impact);
            return weight;
        }

        /// <summary>Контакты босса в такт ударов по герою: лапы (ближние), топот, рёв, своё у этапов 2–3.</summary>
        private void AddThicketMasterContacts(int id)
        {
            if (_thicketMasters == null) return;
            var a = _thicketMasters[id];
            if (a.Serial == 0) return;
            switch (a.Action)
            {
                case ThicketMasterAction.Paw:
                {
                    int impact = a.ImpactTick;
                    for (int k = a.Stage; k < a.Stages; k++, impact += ThicketPawWindupOf(id) + ThicketPawStrikeTicks)
                    {
                        if (k == a.Stage && a.HitResolved) continue;
                        if (impact >= Tick) AddHeroContact(id, impact, impact, melee: true);
                    }
                    return;
                }
                case ThicketMasterAction.Stomp:
                case ThicketMasterAction.Roar:
                    if (!a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                    return;
                case ThicketMasterAction.Wake:
                    return;
            }
            ThicketContactsExtra(id);
        }
    }
}
