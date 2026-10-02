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

        /// <summary>Рёв: кольцо 2,3–6,3 м, 36 тиков, без урона, отброс 2 м.</summary>
        Roar = 2,

        /// <summary>Лапа: серия П/Л/П (Stages 2 / 2–3 / 3 по фазе), сектор 120° на 4,14 м.</summary>
        Paw = 3,

        /// <summary>Дыбом и топот: круг 5,2 м, через 15 тиков кольцо 5,2–7,5 м (Stages = 2), отброс 2 м.</summary>
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
    /// (серия лапы, топот с кольцом, нырок, буря) идёт шагами: Stage — номер
    /// текущего шага, Stages — сколько всего, ImpactTick — контакт текущего
    /// шага, LastImpactTick — последнего (до него босс держит крупный жетон).
    /// Касты (прорастание, пыльца, ливень) здесь — только жест; их круги идут
    /// сами (ThicketHazardState, облака пыльцы). Полный контракт для вида —
    /// artifacts/tools/wf/boss-tempo-contract.md.
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

        /// <summary>Окно топота, 90 бит: 1 — герой в этот тик ближе 4 м. Младший — текущий тик; топот — 60 единиц подряд с младшего.</summary>
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

        /// <summary>
        /// Окно ответа (темп 02.10): после последнего удара серии лапы или
        /// кольца топота ни один удар босса не ляжет раньше этого тика
        /// (удар + ThicketWindowTicks). Следующий замах может начаться и
        /// раньше — ударит не раньше окна.
        /// </summary>
        public int QuietUntil;

        /// <summary>
        /// Вступление (кат-сцена 02.10, Simulation.ForestBoss.Intro). Clearing —
        /// поляна босса (LayoutMap.GetGlade, номер + 1; 0 — стенд без поляны:
        /// прежнее пробуждение по 9 м). IntroStartTick — герой ступил на пол
        /// поляны, IntroWakeTick — пробуждение, IntroEndTick — конец рёва: герой
        /// снова слушается, босс в этот тик бьёт. IntroEndTick 0 — ещё не было.
        /// В хеш — HashThicketIntro.
        /// </summary>
        public int Clearing, IntroStartTick, IntroWakeTick, IntroEndTick;
    }

    /// <summary>
    /// ХОЗЯИН ЧАЩИ — босс леса (план 01.10, этап 4 «Sim», стадия 1 «Ядро»).
    ///
    /// На своей поляне спит укоренённым, пока герой не ступит на её пол (или
    /// не ранит его) — вступление-кат-сцена, Simulation.ForestBoss.Intro; на
    /// стенде без поляны — пока не прошло 90 тиков с появления и герой не
    /// подошёл на 9 м (или ранил); потом 30 тиков вырывает лапы и
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

        /// <summary>Ход 2,0 м/с (владелец 02.10: «темп ходьбы босса в целом надо замедлить», было 2,6).</summary>
        public static readonly Fix64 ThicketMasterMoveSpeed = Fix64.FromInt(2);

        /// <summary>Поворот 4,5° за тик: заход сбоку и сзади имеет смысл.</summary>
        private static readonly Fix64 ThicketTurnStep = Fix64.TwoPi / 80;
        private static readonly Fix64 ThicketTurnCos = Fix64.Cos(ThicketTurnStep);
        private static readonly Fix64 ThicketTurnSin = Fix64.Sin(ThicketTurnStep);

        /// <summary>Шаг только вдоль взгляда: ноль при cos 0,6, как у Вендиго.</summary>
        private static readonly Fix64 ThicketWalkAlignFrom = Fix64.Ratio(6, 10);

        /// <summary>Поводок: дальше 10 м от точки появления не уходит.</summary>
        public static readonly Fix64 ThicketLeash = Fix64.FromInt(10);

        /// <summary>
        /// Рост +15% (владелец, 02.10): модель 3,6 → 4,14 м. Всё, что идёт ОТ
        /// ТЕЛА, ×1,15: корпус (Simulation.ForestBoss.Hull), лапа 4,14, топот 5,2 и кольцо
        /// 5,2–7,5, рёв 2,3–6,3, круг выхода из нырка 4,0, подход и дальность
        /// начала лапы, окно топота. Круги прорастания, ягод, пыльцы и бури —
        /// прежние. Тело в Sim остаётся 0,95: это потолок EntityStore.MaxBodyRadius
        /// (по нему меряется сетка расталкивания) — проход держит корпус.
        /// </summary>
        public const int ThicketSizePercent = 115;
        public static readonly Fix64 ThicketModelHeight = Fix64.Ratio(414, 100);

        // ---- вступление ----

        /// <summary>Стенд без поляны: сон не короче 90 тиков и пробуждение по 9 м. На поляне — вступление (Simulation.ForestBoss.Intro).</summary>
        public const int ThicketMinSleepTicks = 90;
        public const int ThicketWakeTicks = 30;
        public static readonly Fix64 ThicketWakeRange = Fix64.FromInt(9);

        // ---- лапа: серия (темп 02.10) ----

        /// <summary>
        /// Серия лапы: первый замах ThicketPawWindupTicks (15), каждый следующий
        /// удар — через ThicketPawSeriesGapTicks (9) после прошлого, П/Л/П
        /// (чётный номер — правая). Знак удара k (EnemyActionStarted, Amount k)
        /// встаёт в тик удара k−1 — за 9 тиков до своего. Действие кончается
        /// через ThicketPawStrikeTicks (кадр контакта) после последнего удара,
        /// дальше отдых по фазе; окно ответа ThicketWindowTicks (30) — следующий
        /// удар босса не раньше чем через 30 после последнего удара серии.
        /// ThicketPawRecoveryTicks — стойки сверху нет (0; строка вида в EnemyArchetypes).
        /// </summary>
        public const int ThicketPawWindupTicks = 15, ThicketPawStrikeTicks = 1, ThicketPawRecoveryTicks = 0;
        public const int ThicketPawSeriesGapTicks = 9;

        /// <summary>
        /// Окно ответа после серии лапы и после кольца топота (темп 02.10): 30 тиков
        /// (1 с) без ударов босса (ThicketMasterMemory.QuietUntil). Замахи следующего
        /// действия могут начаться внутри окна: серия лапы — за 15 до его конца,
        /// топот — за 24. Отдых по фазе считается от конца действия.
        /// </summary>
        public const int ThicketWindowTicks = 30;

        /// <summary>Ударов в серии: фаза 1 — 2, фаза 2 — 2 или 3 (свой поток), фаза 3 — 3.</summary>
        public const int ThicketPawSeriesPhase1 = 2, ThicketPawSeriesPhase2Min = 2, ThicketPawSeriesPhase2Max = 3,
            ThicketPawSeriesPhase3 = 3;

        /// <summary>
        /// Правило дока «больше 60 урона — от 30 тиков, моб стоит» (DESIGN.md,
        /// бой рогалика 26.09): при уроне лапы больше 60 ПЕРВЫЙ замах серии 30 —
        /// удлиняется замах, урон не режется. С лапой 11 (баланс 02.10) на арене 9
        /// до порога не доходят и «Сложно» с яростью (16 → 20 → 26).
        /// </summary>
        public const int ThicketPawHeavyDamage = 60, ThicketPawHeavyWindupTicks = 30;

        /// <summary>
        /// «Мягче серии» (владелец 02.10): второй и третий удары серии бьют на 40%
        /// слабее — 60% удара лапы с округлением (16 → 10 на арене 9). Доля от
        /// лапы: глубина, «Сложно» и ярость растят и их. Первый удар — полный.
        /// </summary>
        public const int ThicketPawFollowUpDamagePercent = 60;
        public static readonly Fix64 ThicketPawRadius = Fix64.Ratio(414, 100);
        public static readonly Fix64 ThicketPawArcCos = Fix64.Ratio(1, 2);

        /// <summary>Лапа начинается, когда герой ближе 3,68 м между центрами и в ±40° от взгляда.</summary>
        public static readonly Fix64 ThicketPawStartRange = Fix64.Ratio(368, 100);
        private static readonly Fix64 ThicketPawFrontCos = Fix64.Ratio(766, 1000);

        /// <summary>Подходит к герою до 3,22 м и стоит.</summary>
        public static readonly Fix64 ThicketHoldDistance = Fix64.Ratio(322, 100);

        /// <summary>Каждый удар серии заново доворачивает к герою — не больше поворота за 9 тиков (40,5°).</summary>
        private static readonly Fix64 ThicketPawRetargetCos = Fix64.Cos(ThicketTurnStep * ThicketPawSeriesGapTicks);
        private static readonly Fix64 ThicketPawRetargetSin = Fix64.Sin(ThicketTurnStep * ThicketPawSeriesGapTicks);

        // ---- топот: два кольца ----

        /// <summary>
        /// Замах 24, круг r5,2 (контакт 2 тика — для вида); второе кольцо
        /// 5,2–7,5 м — через ThicketStompRingDelayTicks (15) после первого,
        /// урон ×0,75, отброс тот же. Действие кончается через
        /// ThicketStompStrikeTicks после кольца, дальше отдых по фазе; окно
        /// ответа ThicketWindowTicks (30) после кольца. Кого первое кольцо ранило
        /// (и отбросило в полосу второго), второе не бьёт — одно попадание на топот.
        /// </summary>
        public const int ThicketStompWindupTicks = 24, ThicketStompStrikeTicks = 2;
        public const int ThicketStompRingDelayTicks = 15, ThicketStompRingDamagePercent = 75;
        public static readonly Fix64 ThicketStompRadius = Fix64.Ratio(26, 5);
        public static readonly Fix64 ThicketStompRingOuterRadius = Fix64.Ratio(15, 2);

        /// <summary>Бит Tag топота: первое кольцо ранило героя.</summary>
        public const int ThicketStompRing1HitBit = 1;

        /// <summary>
        /// Топот — если герой ПОДРЯД ThicketStompNearTicks (60, 2 с) тиков был ближе
        /// 4 м между центрами («мягче топот», владелец 02.10): один тик дальше 4 м
        /// начинает счёт заново, мимо пробежавшего не топчет. Память — последние
        /// ThicketStompWindowTicks (90) тиков битами (NearLo/NearHi).
        /// </summary>
        public const int ThicketStompWindowTicks = 90, ThicketStompNearTicks = 60;
        public static readonly Fix64 ThicketStompNearRange = Fix64.FromInt(4);

        /// <summary>
        /// Топот по правилу — не чаще раза в 150 тиков (5 с) от начала прошлого
        /// (баланс 02.10, было 90 = окно): правило «60 тиков подряд ближе 4 м»
        /// остаётся главным, перезарядка лишь разносит топоты. Окно после топота
        /// начинается заново; прижатого героя между топотами бьёт лапа.
        /// Топот связки нырка перезарядку не ждёт, но ставит её.
        /// </summary>
        public const int ThicketStompCooldownTicks = 150;

        // ---- рёв ----

        public const int ThicketRoarWindupTicks = 36, ThicketRoarRecoveryTicks = 15;
        public static readonly Fix64 ThicketRoarInnerRadius = Fix64.Ratio(23, 10);
        public static readonly Fix64 ThicketRoarOuterRadius = Fix64.Ratio(63, 10);
        public const int ThicketRoarIntroBit = 1, ThicketRoar66Bit = 2, ThicketRoar50Bit = 4, ThicketRoar33Bit = 8;

        // ---- отброс (топот и рёв) ----

        public const int ThicketKnockbackTicks = 10;
        public static readonly Fix64 ThicketKnockbackDistance = Fix64.FromInt(2);

        // ---- темп ----

        /// <summary>
        /// Отдых между действиями по фазам (темп 02.10): 0,6 / 0,4 / 0,2 с — от
        /// конца действия до начала следующего (серия лапы и топот кончаются
        /// через кадр контакта после последнего удара; нырок — после стойки 36;
        /// каст — после жеста). После серии и топота ещё и окно ответа
        /// ThicketWindowTicks: удар не раньше 30 после последнего. Связка нырка —
        /// без отдыха. Рёв и пробуждение — без отдыха. Отдых и перезарядки растут
        /// ×1,25 при живой подмоге и режутся ×0,85 с половины здоровья; замахи и
        /// окно — никогда.
        /// </summary>
        public const int ThicketRestPhase1Ticks = 18, ThicketRestPhase2Ticks = 12, ThicketRestPhase3Ticks = 6;
        public const int ThicketAddsCooldownPercent = 125, ThicketEnragedCooldownPercent = 85;

        /// <summary>Песочные Часы: не оглушают, сдвигают таймеры босса на 2 с.</summary>
        public const int ThicketHourglassShiftTicks = 60;

        // ---- урон: доли от лапы ----
        //
        // Таблица долей: числа спецификации на арене 9 при лапе 41 (база 25 × 164%).
        // Баланс 02.10 снизил базу лапы до 10 (EnemyArchetypes.ThicketMasterPawDamage,
        // весь урон босса ×0,4): на арене 9 лапа 16 (второй и третий удары серии 10),
        // топот 24, кольцо 18, нырок 26, прорастание 13, ливень 12, буря 27, укус пыльцы 2 —
        // ThicketShareOf от этой таблицы.

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

        /// <summary>
        /// Фаза, которую уже ОБЪЯВИЛ рёв (для внешности по фазам): 0 — спит,
        /// 1 — до рёва 66, 2 — с начала рёва 66, 3 — с начала рёва 33. Рёв
        /// ставит биты в RoarsDone в тик своего начала (Tag рёва — те же биты).
        /// </summary>
        public int ThicketMasterRoaredPhase(int id)
        {
            if (!TryGetThicketMasterMemory(id, out var m) || !m.Awake) return 0;
            if ((m.RoarsDone & ThicketRoar33Bit) != 0) return 3;
            return (m.RoarsDone & ThicketRoar66Bit) != 0 ? 2 : 1;
        }

        /// <summary>
        /// Под землёй в нырке — от начала ухода (Stage 0) до выхода (контакт):
        /// неуязвим и не цель. Урон (ApplyAttack, ApplyAbilityDamage, отложенный
        /// Часами) не проходит, цель способности (ValidAbilityTarget) не
        /// принимается, тело не расталкивается и не держит проход, круп не
        /// выталкивает, горение гаснет.
        /// </summary>
        public bool ThicketShielded(int id)
        {
            var masters = _thicketMasters;
            if (masters == null || (uint)id >= (uint)masters.Length) return false;
            ref var a = ref masters[id];
            return a.Serial != 0 && a.Action == ThicketMasterAction.Dive && !a.HitResolved;
        }

        /// <summary>Сколько ещё тиков босс стоит под Песочными Часами.</summary>
        public int ThicketMasterFrozenTicksLeft(int id)
            => TryGetThicketMasterMemory(id, out var m) && m.FrozenUntil > Tick ? m.FrozenUntil - Tick : 0;

        /// <summary>Сколько из последних 90 тиков герой был ближе 4 м.</summary>
        public int ThicketMasterNearTicks(int id)
            => TryGetThicketMasterMemory(id, out var m) ? CountBits(m.NearLo) + CountBits(m.NearHi) : 0;

        /// <summary>Сколько последних тиков ПОДРЯД герой ближе 4 м (до 90) — правило топота: ≥ 60.</summary>
        public int ThicketMasterNearRunTicks(int id)
        {
            if (!TryGetThicketMasterMemory(id, out var m)) return 0;
            int run = 0;
            for (ulong bits = m.NearLo; (bits & 1UL) != 0 && run < 64; bits >>= 1) run++;
            if (run < 64) return run;
            for (ulong bits = m.NearHi; (bits & 1UL) != 0 && run < ThicketStompWindowTicks; bits >>= 1) run++;
            return run;
        }

        /// <summary>Когда действие action снова готово (перезарядка); 0 — готово.</summary>
        public int ThicketReadyTick(int id, ThicketMasterAction action)
            => _thicketReady != null && (uint)id < (uint)Entities.Capacity ? _thicketReady[id * ThicketActionSlots + (int)action] : 0;

        /// <summary>Сдвигает перезарядку действия. Для стендов и тестов.</summary>
        public void SetThicketReadyTick(int id, ThicketMasterAction action, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketReady[id * ThicketActionSlots + (int)action] = tick;
        }

        // ---- урон: всё — доля лапы (урона листа), глубина, «Сложно» и ярость растят разом ----

        public int ThicketPawDamageOf(int id) => Entities.Damage[id];

        /// <summary>Удар серии stage (с 0): первый — лапа целиком, второй и третий — 60% (25 на арене 9).</summary>
        public int ThicketPawStrikeDamageOf(int id, int stage)
            => stage == 0 ? ThicketPawDamageOf(id)
                : EnemyArchetypes.Share(ThicketPawDamageOf(id), ThicketPawFollowUpDamagePercent, 100);

        /// <summary>Замах лапы: 24, а при уроне больше 60 — 30 (ThicketPawHeavyDamage).</summary>
        public int ThicketPawWindupOf(int id)
            => ThicketPawDamageOf(id) > ThicketPawHeavyDamage ? ThicketPawHeavyWindupTicks : ThicketPawWindupTicks;
        public int ThicketShareOf(int id, int authoredA9) => EnemyArchetypes.Share(Entities.Damage[id], authoredA9, ThicketPawDamageA9);
        public int ThicketStompDamageOf(int id) => ThicketShareOf(id, ThicketStompDamageA9);

        /// <summary>Второе кольцо топота: ×0,75 топота (46 на арене 9).</summary>
        public int ThicketStompRingDamageOf(int id) => ThicketStompDamageOf(id) * ThicketStompRingDamagePercent / 100;

        // ---- фигуры: одна на метку и на попадание ----

        public static EnemyTelegraph ThicketPawSector(FixVec2 origin, FixVec2 direction)
            => EnemyTelegraph.Sector(origin, direction, ThicketPawRadius, ThicketPawArcCos);

        public static EnemyTelegraph ThicketStompCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketStompRadius);

        public static EnemyTelegraph ThicketStompRing(FixVec2 center)
            => EnemyTelegraph.Ring(center, ThicketStompRadius, ThicketStompRingOuterRadius);

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
            ResetThicketIntro();
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
                Hashing.Mix(ref hash, m.QuietUntil);
                for (int k = 0; k < ThicketActionSlots; k++) Hashing.Mix(ref hash, ThicketReady[id * ThicketActionSlots + k]);
            }
            ThicketHashExtra(ref hash);
            HashThicketIntro(ref hash);
        }

        // ---- арена босса (переключатель) ----

        /// <summary>
        /// SetupBossArena с включённым переключателем: то же, что у временного
        /// босса (комната перед выходом, пачка выхода в плане, подмога), но
        /// Хозяин Чащи с ThicketMasterHealth и без надбавки ×1,5 к удару: его
        /// база уже своя (10 — лапа 16 на арене 9).
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
            // Спит, пока герой не ступит на пол этой поляны — вступление (Simulation.ForestBoss.Intro).
            MarkThicketClearing(boss, map);
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
                if (m.QuietUntil > Tick) m.QuietUntil += shift;
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
                ShiftThicketIntro(id, shift);
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
            // Фоновая опасность (круги прорастания и ливня, падающая пыльца) — тоже его крупная атака.
            if (ThicketHazardHoldsToken(id)) return true;
            bool holds = false;
            ThicketHoldsTokenExtra(id, ref holds);
            return holds;
        }

        /// <summary>Жетон без идущего действия: буря пора, а земля ещё не пуста — подмога новых крупных не начинает.</summary>
        partial void ThicketHoldsTokenExtra(int id, ref bool holds);

        /// <summary>
        /// Крупные метки босса в бюджете: топот (оба кольца) и рёв — 1, лапа — 0
        /// (знак на теле), нырок и буря — этапы 2–3; плюс фоновая опасность
        /// (прорастание и ливень — 2, падающая пыльца — 1). Начало — самое
        /// позднее, удар — ближайший.
        /// </summary>
        private int ThicketMasterMarkWeight(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            if (_thicketMasters == null) return 0;
            var a = _thicketMasters[id];
            int weight = 0;
            if (a.Serial != 0)
            {
                switch (a.Action)
                {
                    case ThicketMasterAction.Stomp:
                        if (!(a.HitResolved && a.Stage + 1 >= a.Stages) && Tick <= a.LastImpactTick)
                        { weight = 1; start = a.StartTick; impact = a.ImpactTick; }
                        break;
                    case ThicketMasterAction.Roar:
                        if (!a.HitResolved && Tick <= a.ImpactTick) { weight = 1; start = a.StartTick; impact = a.ImpactTick; }
                        break;
                    case ThicketMasterAction.Wake:
                    case ThicketMasterAction.Paw:
                        break;
                    default:
                        ThicketMarkWeightExtra(id, ref weight, ref start, ref impact);
                        break;
                }
            }
            int hazard = ThicketHazardMarkWeight(id, out int hazardStart, out int hazardImpact);
            if (hazard > 0)
            {
                weight += hazard;
                if (hazardStart > start) start = hazardStart;
                if (impact == int.MinValue || (hazardImpact != int.MinValue && hazardImpact < impact)) impact = hazardImpact;
            }
            return weight;
        }

        /// <summary>
        /// Контакты босса в такт ударов по герою: каждый удар серии лапы
        /// (ближние), оба кольца топота, рёв, своё у этапов 2–3 и удары фоновой
        /// опасности.
        /// </summary>
        private void AddThicketMasterContacts(int id)
        {
            if (_thicketMasters == null) return;
            var a = _thicketMasters[id];
            if (a.Serial != 0)
            {
                switch (a.Action)
                {
                    case ThicketMasterAction.Paw:
                    {
                        int impact = a.ImpactTick;
                        for (int k = a.Stage; k < a.Stages; k++, impact += ThicketPawSeriesGapTicks)
                        {
                            if (k == a.Stage && a.HitResolved) continue;
                            if (impact >= Tick) AddHeroContact(id, impact, impact, melee: true);
                        }
                        break;
                    }
                    case ThicketMasterAction.Stomp:
                        if (!a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                        if (a.Stage + 1 < a.Stages && a.LastImpactTick >= Tick) AddHeroContact(id, a.LastImpactTick, a.LastImpactTick);
                        break;
                    case ThicketMasterAction.Roar:
                        if (!a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                        break;
                    case ThicketMasterAction.Wake:
                        break;
                    default:
                        ThicketContactsExtra(id);
                        break;
                }
            }
            AddThicketHazardContacts(id);
        }
    }
}
