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

        /// <summary>Лапа: серия П/Л/П (Stages 2 / 2–3 / 3 по фазе), сектор 100° от бьющего плеча на 3,58 м (4,14 от центра).</summary>
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

        /// <summary>
        /// «Терновник» (владелец 08.10, Simulation.ForestBoss.Seeds.cs; до него, 07.10 — «Веер шипов-семян»
        /// из кроны): жест каста, 2 / 3 куста в случайных местах пола, у каждого 4 шипа крестом по линиям на
        /// земле; кусты вянут и уходят. Имя значения прежнее (событие — EnemyActionKind.ThicketSeeds).
        /// </summary>
        Seeds = 10,
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

        /// <summary>
        /// Центр поводка: точка появления, а на поляне босса — её центр (сам босс
        /// встаёт в дальнем углу, GladeLayout.BossSpawnOffset; ревью 02.10, вечер).
        /// Буря и вид меряют поляну от него.
        /// </summary>
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

        /// <summary>
        /// Этап 2: срок нырка «под героя» (03.10, владелец: «босс за игру ни разу не залез под
        /// землю. надо участить») — не раньше этого тика: конец рёва вступления +
        /// ThicketDiveFirstTicks (10 с; сближение первой атакой в тот же тик ставит те же 10 с от себя),
        /// потом начало любого нырка + ThicketDiveEvery* (24 / 6 /
        /// 6 с по фазе, со множителями); 0 — вступления ещё не было. С ревью 02.10 (ночь) до 03.10
        /// поле было всегда 0 (нырок — только по дальнему герою).
        /// </summary>
        public int DiveNextTick;

        /// <summary>Свой поток решений: Pcg32(_encounterSeed ^ k, ThicketStream), заводится при пробуждении.</summary>
        public Pcg32 Rng;
        public bool RngSeeded;

        /// <summary>
        /// Окно ответа (темп 02.10): после последнего удара серии лапы или
        /// кольца топота ни один удар босса не ляжет раньше этого тика
        /// (удар + ThicketWindowOf: 30 / 26 / 22 по фазе). Следующий замах может начаться и
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

        /// <summary>
        /// Сколько последних тиков ПОДРЯД герой за спиной (дальше 100° от взгляда) и
        /// не дальше 5,2 м между центрами — правило топота «за спиной»: ≥ 20
        /// (ThicketRearStompTicks). Под землёй и после топота — с нуля.
        /// </summary>
        public int RearTicks;

        /// <summary>
        /// Ревью 02.10, ночь («лапа — основа»). HugTicks — сколько тиков ПОДРЯД герой
        /// прижат (центр ближе ThicketStompRadius), что бы босс ни делал (баланс 02.10, ночь;
        /// было — только пока у босса нет действия); топот «прижался» — от ThicketStompHugTicks
        /// (240). FarTicks — сколько тиков подряд герой в дальней полосе (ThicketHeroBand == Far):
        /// нырок — от ThicketDiveFarTicks. OutOfReachTicks — сколько тиков подряд лапа героя не
        /// достаёт: касты средней полосы и быстрый ход — от ThicketKiteTicks.
        /// </summary>
        public int HugTicks, FarTicks, OutOfReachTicks;
    }

    /// <summary>
    /// Полоса дистанции героя до Хозяина Чащи — от кромки корпуса до центра героя
    /// (ThicketHullGap; ревью 02.10, ночь). Значение идёт в отчёты стенда — новые только в конец.
    /// </summary>
    public enum ThicketBand : byte
    {
        /// <summary>Ближняя: до ThicketNearGap (1,4 м) — досягаемость лапы.</summary>
        Near = 0,

        /// <summary>Средняя: босс идёт к герою и бьёт лапой.</summary>
        Mid = 1,

        /// <summary>Дальняя: от ThicketFarGap (6,5 м) — нырок, быстрый ход, касты издали.</summary>
        Far = 2,
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

        /// <summary>
        /// Быстрый ход ×1,4 (2,8 м/с; ревью 02.10, ночь, «дальники»): герой в дальней полосе
        /// или лапа не достаёт его ThicketKiteTicks подряд — босс сокращает дистанцию, а не
        /// плетётся за кайтящим. Вид играет Walk по пройденному пути — клип ускоряется сам.
        /// </summary>
        public const int ThicketFarWalkPercent = 140;

        // ---- полосы дистанции (ревью 02.10, ночь: «лапа — основа, темп, дальники») ----

        /// <summary>
        /// Полосы — от кромки корпуса до ЦЕНТРА героя (ThicketHullGap): ближняя — до
        /// ThicketNearGap (1,4 м: лапа прямо вперёд достаёт 4,14 м от центра, грудь кончается
        /// в 2,74), средняя — до ThicketFarGap, дальняя — от ThicketFarGap (6,5 м: ≈ 9,2 м от
        /// центра перед мордой, ≈ 8,0 сбоку, ≈ 8,5 за хвостом). Будущие дальники (7–9 м от
        /// тела) — в дальней. Ближняя и средняя — серии лапы (в средней босс подходит),
        /// дальняя — нырок-сближение (ThicketDiveFarTicks), быстрый ход и касты издали.
        /// </summary>
        public static readonly Fix64 ThicketNearGap = Fix64.Ratio(7, 5);
        public static readonly Fix64 ThicketFarGap = Fix64.Ratio(13, 2);

        /// <summary>
        /// Счёт дальней полосы для нырка (FarTicks) не рвётся, пока герой не ближе
        /// ThicketFarGap − 1 м (5,5 м): дальник у края полосы (босс идёт на него, он отходит)
        /// иначе обнулял бы счёт каждые полсекунды.
        /// </summary>
        public static readonly Fix64 ThicketFarKeepSlack = Fix64.One;

        /// <summary>
        /// «Кайтит»: лапа не достаёт героя 45 тиков подряд (1,5 с, OutOfReachTicks) — касты фаз
        /// 2–3 в средней полосе и быстрый ход. Ближе — босс просто подходит и бьёт лапой.
        /// </summary>
        public const int ThicketKiteTicks = 45;

        /// <summary>
        /// Поворот 2,5° за тик (полный круг — 144 тика, 4,8 с; ревью 02.10, вечер:
        /// «закруживаешь его и он тупо разворачивается на месте», было 4,5° — 80
        /// тиков): заход сбоку и сзади имеет смысл, а героя за спиной встречает
        /// топот (ThicketRearStompTicks), а не вращение. Вид играет поворот на месте
        /// клипами TurnL/TurnR по изменению Entities.Facing.
        /// </summary>
        public const int ThicketTurnTicksPerCircle = 144;
        private static readonly Fix64 ThicketTurnStep = Fix64.TwoPi / ThicketTurnTicksPerCircle;
        private static readonly Fix64 ThicketTurnCos = Fix64.Cos(ThicketTurnStep);
        private static readonly Fix64 ThicketTurnSin = Fix64.Sin(ThicketTurnStep);

        /// <summary>Шаг только вдоль взгляда: ноль при cos 0,6, как у Вендиго.</summary>
        private static readonly Fix64 ThicketWalkAlignFrom = Fix64.Ratio(6, 10);

        /// <summary>
        /// Поводок: дальше 10 м от Home не уходит. На поляне босса Home — её центр.
        /// Было (поляна 02.10, углы до 11,03 м): любой герой на полу в досягаемости лапы
        /// босса, стоящего в поводке. С поляной 07.10 (+30%, полуоси 12,83 × 9,62) в
        /// дальних углах герой стоит до ~14 м от центра — дальше поводка + лапы
        /// (13,68): там ThicketHeroBand зовёт его дальним и вплотную к корпусу (нырок,
        /// быстрый ход, касты издали); терновник меряет ближнюю полосу по самому
        /// зазору (ThicketBushWeightNow). Поводок под новую поляну не менялся.
        /// </summary>
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
        /// Серия лапы: первый замах ThicketPawWindupTicks (17) во всех фазах, каждый следующий
        /// удар — через промежуток своей фазы после прошлого (не ровные 10, а 10–12 / 10–11 /
        /// 10–11 тиков, ThicketPawGapPhase*, бросок своего потока в начале серии,
        /// ThicketPawGapOf), П/Л/П (чётный номер — правая). ThicketPawSeriesGapTicks (10) —
        /// промежуток ревью 02.10 (вечер) и нижняя граница бросков: для вида и тестов, Sim им
        /// не считает. Знак удара k (EnemyActionStarted, Amount k) и его сектор на земле встают
        /// в тик удара k−1 — за промежуток до своего. Действие кончается через
        /// ThicketPawStrikeTicks (кадр контакта) после последнего удара, дальше отдых по фазе;
        /// окно ответа (ThicketWindowTicks, 30) — следующий удар босса не раньше стольких тиков
        /// после последнего удара серии. ThicketPawRecoveryTicks — стойки сверху нет (0; строка
        /// вида в EnemyArchetypes).
        /// </summary>
        public const int ThicketPawWindupTicks = 17, ThicketPawStrikeTicks = 1, ThicketPawRecoveryTicks = 0;
        public const int ThicketPawSeriesGapTicks = 10;

        /// <summary>
        /// Промежутки между ударами серии по фазам (тиков, включительно): фаза 1 — 10–12, фазы 2–3 —
        /// 10–11. Не короче 10 — «замедлить тычку лапой на 10%» (владелец, ревью 02.10, вечер:
        /// 9 → 10); баланс 02.10 (ночь) сжал их до 8–11 / 8–10 и замах до 16 / 15 — проверка находок
        /// 03.10 вернула просьбу владельца: фазы растут длиной серии, связками, кастами и отдыхом,
        /// а не скоростью тычка. Броски — свой поток босса в начале серии (оба, даже если ударов 2:
        /// поток тратится ровно), лежат в Tag действия: биты 0–7 — после удара 0, 8–15 — после
        /// удара 1 (ThicketPawGapOf).
        /// </summary>
        public const int ThicketPawGapPhase1Min = 10, ThicketPawGapPhase1Max = 12;
        public const int ThicketPawGapPhase2Min = 10, ThicketPawGapPhase2Max = 11;
        public const int ThicketPawGapPhase3Min = 10, ThicketPawGapPhase3Max = 11;

        /// <summary>Промежуток после удара stage серии лапы a (из Tag; 0 в Tag — средний 10).</summary>
        public static int ThicketPawGapOf(in ThicketMasterState a, int stage)
        {
            int gap = stage <= 0 ? a.Tag & 0xFF : (a.Tag >> 8) & 0xFF;
            return gap > 0 ? gap : ThicketPawSeriesGapTicks;
        }

        /// <summary>
        /// Окно ответа после серии лапы и после кольца топота (темп 02.10, принятое владельцем):
        /// 30 тиков (1 с) во всех фазах без ударов босса (ThicketMasterMemory.QuietUntil,
        /// ThicketWindowOf; баланс 02.10, ночь, сжимал его до 26 / 22 — проверка находок 03.10
        /// вернула 30). Замахи следующего действия могут начаться внутри окна: серия лапы — за
        /// свой замах до его конца, топот — за 42. Отдых по фазе считается от конца действия.
        /// </summary>
        public const int ThicketWindowTicks = 30;

        /// <summary>Ударов в серии: фаза 1 — 2, фаза 2 — 2 или 3 (свой поток), фаза 3 — 3.</summary>
        public const int ThicketPawSeriesPhase1 = 2, ThicketPawSeriesPhase2Min = 2, ThicketPawSeriesPhase2Max = 3,
            ThicketPawSeriesPhase3 = 3;

        /// <summary>
        /// Правило дока «больше 60 урона — от 30 тиков, моб стоит» (DESIGN.md,
        /// бой рогалика 26.09): при уроне лапы больше 60 ПЕРВЫЙ замах серии 30 —
        /// удлиняется замах, урон не режется. С базой лапы 12 (проверка находок 03.10) на арене 9
        /// до порога не доходят и «Сложно» с яростью (20 → 25 → 32).
        /// </summary>
        public const int ThicketPawHeavyDamage = 60, ThicketPawHeavyWindupTicks = 30;

        /// <summary>
        /// «Мягче серии» (владелец 02.10): второй и третий удары серии бьют слабее —
        /// 35% удара лапы с округлением (20 → 7 на арене 9; проверка находок 03.10 — было 45%,
        /// до баланса 02.10, ночь — 60%: цена ошибки — первый удар, 7% здоровья героя, а не
        /// россыпь добивок, которые при вернувшемся темпе собирает средний игрок). Доля от
        /// лапы: глубина, «Сложно» и ярость растят и их. Первый удар — полный.
        /// </summary>
        public const int ThicketPawFollowUpDamagePercent = 35;

        /// <summary>Досягаемость лапы от ЦЕНТРА босса прямо вперёд, 4,14 м: начало серии (ThicketPawInReach) и знак на теле.</summary>
        public static readonly Fix64 ThicketPawRadius = Fix64.Ratio(414, 100);

        /// <summary>
        /// Сектор удара лапы (ревью 02.10, вечер: «иногда попадает по герою, хотя
        /// стоишь сбоку»): не от центра босса на 120°, а от БЬЮЩЕГО ПЛЕЧА —
        /// ThicketPawShoulderForward вперёд и ThicketPawShoulderSide вбок (правая
        /// лапа — вправо, левая — влево) по направлению удара, раствор 100° (±50°,
        /// ThicketPawArcCos = cos 50°), ThicketPawReach 3,58 м от плеча — прямо
        /// вперёд те же 4,14 м от центра. Покрывает всю зону начала серии (±40°,
        /// до 3,68 м и вплотную к корпусу до 4,14) обеими лапами; герой у корпуса
        /// (до 2 м от него) дальше 60° от взгляда на стороне бьющей лапы и дальше 45°
        /// на другой — вне взмаха (тест PawSector_FromTheStrikingShoulder_*). Метка —
        /// SharedView: общий красный сектор на земле (GroundTelegraphView) от знака
        /// удара до контакта, ровно фигура попадания.
        /// </summary>
        public static readonly Fix64 ThicketPawShoulderForward = Fix64.Ratio(3, 5), ThicketPawShoulderSide = Fix64.Ratio(1, 2);
        public static readonly Fix64 ThicketPawReach = Fix64.Ratio(358, 100);
        public static readonly Fix64 ThicketPawArcCos = Fix64.Ratio(6428, 10000);

        /// <summary>Лапа начинается, когда герой ближе 3,68 м между центрами и в ±40° от взгляда.</summary>
        public static readonly Fix64 ThicketPawStartRange = Fix64.Ratio(368, 100);
        private static readonly Fix64 ThicketPawFrontCos = Fix64.Ratio(766, 1000);

        /// <summary>Подходит к герою до 3,22 м и стоит.</summary>
        public static readonly Fix64 ThicketHoldDistance = Fix64.Ratio(322, 100);

        /// <summary>
        /// Поворот корпуса в серии лапы (баланс 02.10, ночь): 3,5° за тик — замах доворачивает
        /// корпус к удару быстрее хода (2,5°): первый удар — до 17 × 3,5 = 59,5°, следующие — до
        /// промежуток × 3,5°, но не больше 35° (ThicketPawRetargetMaxDecidegrees; при промежутках
        /// 10–12 — всегда 35°). Вне лапы — 2,5°.
        /// </summary>
        public const int ThicketPawTurnDecidegrees = 35;
        private static readonly Fix64 ThicketPawTurnStep = Fix64.Pi * ThicketPawTurnDecidegrees / 1800;
        private static readonly Fix64 ThicketPawTurnCos = Fix64.Cos(ThicketPawTurnStep);
        private static readonly Fix64 ThicketPawTurnSin = Fix64.Sin(ThicketPawTurnStep);

        /// <summary>
        /// Каждый следующий удар серии заново доворачивает к герою (баланс 02.10, ночь: было
        /// ≤ 25°) — не больше поворота корпуса за свой промежуток (3,5°/тик) и не больше 35°:
        /// корпус доходит до сектора ровно к контакту. Градусы ×10.
        /// </summary>
        public const int ThicketPawRetargetMaxDecidegrees = 350;

        /// <summary>Наибольший доворот удара серии при среднем промежутке 10, градусы ×10 (350 = 35°): для вида и тестов.</summary>
        public const int ThicketPawRetargetDecidegrees = ThicketPawSeriesGapTicks * ThicketPawTurnDecidegrees < ThicketPawRetargetMaxDecidegrees
            ? ThicketPawSeriesGapTicks * ThicketPawTurnDecidegrees : ThicketPawRetargetMaxDecidegrees;

        /// <summary>Доворот следующего удара серии после промежутка gap, радианы.</summary>
        private static Fix64 ThicketPawRetargetAngle(int gap)
            => Fix64.Pi * Math.Min(gap * ThicketPawTurnDecidegrees, ThicketPawRetargetMaxDecidegrees) / 1800;

        /// <summary>
        /// Первый удар серии доворачивает к герою не больше поворота корпуса за замах — 17 ×
        /// 3,5° = 59,5° во всех фазах (ThicketPawOpeningDecidegrees = 595; баланс 02.10, ночь — было
        /// 42,5° при 2,5°/тик, ревью 02.10, ночь — «вдоль взгляда»): так лапа достаёт и стоящего у
        /// бока «под мышкой» между лапой и бедром (67–80° от взгляда, 1,7–1,9 м от центра; раньше —
        /// только топот «прижался»). Замах не растёт.
        /// </summary>
        private static readonly Fix64 ThicketPawOpenCos = Fix64.Cos(ThicketPawTurnStep * ThicketPawWindupTicks);
        private static readonly Fix64 ThicketPawOpenSin = Fix64.Sin(ThicketPawTurnStep * ThicketPawWindupTicks);
        public const int ThicketPawOpeningDecidegrees = ThicketPawTurnDecidegrees * ThicketPawWindupTicks;

        // ---- топот: два кольца ----

        /// <summary>
        /// Замах 42, круг r5,2 (контакт 2 тика — для вида); второе кольцо
        /// 5,2–7,5 м — через ThicketStompRingDelayTicks (15) после первого,
        /// урон ×0,75, отброс тот же. Действие кончается через
        /// ThicketStompStrikeTicks после кольца, дальше отдых по фазе; окно
        /// ответа (ThicketWindowTicks, 30) после кольца. Кого первое кольцо ранило
        /// (и отбросило в полосу второго), второе не бьёт — одно попадание на топот.
        /// Кольцо щадит героя, чей ЦЕНТР внутри круга 5,2 (шагнул обратно внутрь),
        /// хоть тело и заходит на кромку (ThicketStompRingHits).
        /// Ответ ногами (ревью 02.10, ночь: «топот — либо рывок, либо попал»): замах
        /// 24 → 31 → 42 (проверка находок 03.10: при 31 из настоящего места «прижался» —
        /// вплотную к корпусу, «подмышка» между лапой и бедром в 1,73 м от центра — выйти
        /// пешком успевал только тот, кто тронулся за 2 тика; на стенде из 808 топотов
        /// по прижатому сильному — 0 ушли ногами). Прижатый к корпусу герой идёт прямо прочь
        /// и за 28 тиков хода с разгоном выходит из круга (5,2 + тело 0,45): с замахом 42 —
        /// при реакции до 15 тиков (0,5 с) с любой стороны (тест Stomp_FootAnswer_*);
        /// вернувшийся внутрь 5,2 до кольца кольцом не задет. Радиусы прежние; рывок —
        /// по-прежнему ответ на всё. Замах один у всех топотов («прижался», «за спиной», связка).
        /// </summary>
        public const int ThicketStompWindupTicks = 42, ThicketStompStrikeTicks = 2;
        public const int ThicketStompRingDelayTicks = 15, ThicketStompRingDamagePercent = 75;
        public static readonly Fix64 ThicketStompRadius = Fix64.Ratio(26, 5);
        public static readonly Fix64 ThicketStompRingOuterRadius = Fix64.Ratio(15, 2);

        /// <summary>Бит Tag топота: первое кольцо ранило героя.</summary>
        public const int ThicketStompRing1HitBit = 1;

        /// <summary>
        /// Окно «рядом» для отладки (ThicketMasterNearTicks / NearRunTicks): последние
        /// ThicketStompWindowTicks (90) тиков битами (NearLo/NearHi), 1 — герой ближе
        /// ThicketStompNearRange (4 м) между центрами. С ревью 02.10 (ночь) правило топота
        /// по нему не считается — см. ThicketStompHugTicks.
        /// </summary>
        public const int ThicketStompWindowTicks = 90, ThicketStompNearTicks = 60;
        public static readonly Fix64 ThicketStompNearRange = Fix64.FromInt(4);

        /// <summary>
        /// Топот «прижался» (ревью 02.10, ночь: «босс топотов, а не лап» — топот теперь
        /// наказание, а не основа): герой ThicketStompHugTicks тиков ПОДРЯД центром ближе
        /// ThicketStompRadius (5,2), что бы босс ни делал (HugTicks; баланс 02.10, ночь — было
        /// 45 тиков, пока у босса нет действия и лапа не достаёт: с лапой, достающей бок, такое
        /// не наступало, и прижатый эксперт стоял в теле весь бой). 240 тиков (8 с) — у того,
        /// кто не отходит, топот примерно раз на 2–3 серии лапы.
        /// </summary>
        public const int ThicketStompHugTicks = 240;

        /// <summary>
        /// Своя перезарядка топота «прижался» — 180 тиков (6 с) от начала прошлого такого
        /// топота (было 150 при правиле «60 подряд ближе 4 м»). Любые два топота (прижался,
        /// за спиной, связка нырка) — не ближе ThicketStompSpacingTicks (90, 3 с) от начала
        /// до начала: место ThicketRearStompSlot — «готов любой топот».
        /// </summary>
        public const int ThicketStompCooldownTicks = 180, ThicketStompSpacingTicks = 90;

        /// <summary>
        /// Топот по герою за спиной (ревью 02.10, вечер: «он должен делать раньше
        /// АоЕ-атаку, когда пытается развернуться ударить героя»): герой
        /// ThicketRearStompTicks (20) тиков ПОДРЯД за спиной — дальше 100° от взгляда
        /// (ThicketRearCos) — и не дальше ThicketRearStompRange (5,2 м = круг топота)
        /// между центрами: босс не докручивается к нему, а топчет — мимо правила
        /// «прижался» и его перезарядки 180, со своей перезарядкой
        /// ThicketRearStompCooldownTicks (90 = ThicketStompSpacingTicks) от начала прошлого
        /// топота (любого). Окно ответа, наслоение (фоновая опасность — только лапа), бюджет
        /// и такт — как у топота. Счёт — ThicketMasterMemory.RearTicks.
        /// </summary>
        public const int ThicketRearStompTicks = 20, ThicketRearStompCooldownTicks = ThicketStompSpacingTicks;
        public static readonly Fix64 ThicketRearStompRange = ThicketStompRadius;

        /// <summary>«За спиной» — дальше 100° от взгляда: cos 100° ≈ −0,174.</summary>
        public static readonly Fix64 ThicketRearCos = Fix64.Ratio(-174, 1000);

        /// <summary>Место перезарядки топота «за спиной» в ThicketReady (вне значений ThicketMasterAction).</summary>
        private const int ThicketRearStompSlot = ThicketActionSlots - 1;

        /// <summary>
        /// Топот в жребии рядом с лапой (03.10, владелец: «участить аое в ближнем бою, а то он
        /// только лапой машет на фазе 1 — скучно»): фаза 1, герой центром в круге топота (5,2 м)
        /// и лапа готова — во взвешенный выбор вместе с серией (вес лапы ThicketPawWeight) идёт и
        /// топот, ThicketStompPickWeight (10 к 10: монетка); своя перезарядка ThicketStompPickCooldownTicks
        /// (135, 4,5 с; перебаланс 03.10 — было 150) от начала любого топота (×1,25 при подмоге, не
        /// меньше 90 — ThicketStompSpacingTicks). Фазы 2–3 — как были (касты в жребии, связка
        /// «топот → лапа»); «прижался» (240), «за спиной» (20) — во всех фазах. Замах 42, круги,
        /// ответ ногами — тот же топот. На стенде у сильного ближника фаза 1 — лапа ≈ 58%, топот ≈ 29%,
        /// нырок ≈ 13% (до 03.10 — 73 / 18 / 9; с перезарядкой 150 и нырком через 15 с — 56 / 25 / 19).
        /// </summary>
        public const int ThicketPawWeight = 10, ThicketStompPickWeight = 10, ThicketStompPickCooldownTicks = 135;

        /// <summary>Место перезарядки топота в жребии в ThicketReady (вне значений ThicketMasterAction).</summary>
        private const int ThicketStompPickSlot = ThicketActionSlots - 2;

        // ---- рёв ----

        public const int ThicketRoarWindupTicks = 36, ThicketRoarRecoveryTicks = 15;
        public static readonly Fix64 ThicketRoarInnerRadius = Fix64.Ratio(23, 10);
        public static readonly Fix64 ThicketRoarOuterRadius = Fix64.Ratio(63, 10);
        public const int ThicketRoarIntroBit = 1, ThicketRoar66Bit = 2, ThicketRoar50Bit = 4, ThicketRoar33Bit = 8;

        /// <summary>
        /// Рёв порога (66/50/33) доворачивает к герою только столько тиков от начала
        /// (10 × 2,5° = 25°), дальше стоит: вид играет клип рёва без нижнего слоя, и
        /// корпус, крутящийся за кружащим героем все 51 тик (до 127°), вертелся на
        /// ногах рёва (ревью 02.10, вечер: «закруживаешь его… прокручивается на
        /// месте»). Пробуждение и рёв вступления доворачивают весь (герой стоит).
        /// </summary>
        public const int ThicketRoarTurnTicks = 10;

        // ---- отброс (топот и рёв) ----

        public const int ThicketKnockbackTicks = 10;
        public static readonly Fix64 ThicketKnockbackDistance = Fix64.FromInt(2);

        // ---- темп ----

        /// <summary>
        /// Отдых между действиями по фазам: 30 / 20 / 12 тиков (1,0 / 0,67 / 0,4 с; фаза 3 под
        /// яростью ×0,85 — 10) — от конца действия до начала следующего (серия лапы и топот
        /// кончаются через кадр контакта после последнего удара; нырок — после стойки 36; каст —
        /// после жеста). После серии и топота ещё и окно ответа ThicketWindowTicks (30): удар не
        /// раньше стольких тиков после последнего — так что после серии босс стоит не меньше 13
        /// тиков при любом отдыхе. Проверка находок 03.10 (владелец: «когда мы с Костей тестили,
        /// по темпу боя всё было отлично»): баланс 02.10 (ночь) поднял отдых до 46 / 34 / 36 (фаза 3
        /// длиннее фазы 2 — темп не рос), и босс между атаками стоял над землёй вдвое дольше
        /// сыгранного (медиана 35 тиков против 18, 42% боя против 30%). 30 / 20 / 12 возвращают ритм
        /// сыгранного вечером (медиана простоя ~21 тик, ~29% боя) без нырков вместо атак; темп
        /// растёт от фазы к фазе. Связки (нырок, топот → лапа) — без отдыха. Рёв и пробуждение —
        /// без отдыха. Отдых и перезарядки растут ×1,25 при живой подмоге и режутся ×0,85 с
        /// половины здоровья; замахи и окно — никогда.
        /// </summary>
        public const int ThicketRestPhase1Ticks = 30, ThicketRestPhase2Ticks = 20, ThicketRestPhase3Ticks = 12;
        public const int ThicketAddsCooldownPercent = 125, ThicketEnragedCooldownPercent = 85;

        /// <summary>Песочные Часы: не оглушают, сдвигают таймеры босса на 2 с.</summary>
        public const int ThicketHourglassShiftTicks = 60;

        // ---- урон: доли от лапы ----
        //
        // Таблица долей: числа спецификации на арене 9 при лапе 41 (база 25 × 164%).
        // Баланс 02.10 снизил базу лапы до 10 (EnemyArchetypes.ThicketMasterPawDamage,
        // весь урон босса ×0,4), после ревью 02.10 (вечер) — до 9. Баланс 02.10, ночь
        // («ошибка должна стоить») — база 15 при отдыхе 46 / 34 / 36; доли прорастания
        // 34 → 45, ливня 30 → 40 и пыльцы 5 → 6 подняты: издали ошибаются кругами, а не лапой.
        // Проверка находок 03.10: темп сыгранного вечером вернулся (отдых 30 / 20 / 12, окно 30,
        // тычок не быстрее 10) — база 12: на арене 9 лапа 20 (второй и третий удары серии 7),
        // топот 30, кольцо 22, нырок 32, прорастание 22, ливень 20, буря 34 (в фазе 3 всегда
        // ярость ×1,3 — 44), укус пыльцы 3 — ThicketShareOf от этой таблицы.
        // Ревью владельца 03.10 (нырок «под героя», топот в жребии фазы 1): база 12 → 14 — на
        // арене 9 лапа 23 (второй и третий удары 8), нырок 37, прорастание 25, ливень 22; доли
        // топота 62 → 53 и бури 70 → 60 — их урон прежний (топот 30, кольцо 22, буря 34, в фазе 3 —
        // 44): цена ошибки — удар лапы, а не круги, которые средний игрок ещё учится читать.

        public const int ThicketPawDamageA9 = 41, ThicketStompDamageA9 = 53, ThicketDiveDamageA9 = 66;
        public const int ThicketSproutDamageA9 = 45, ThicketRainDamageA9 = 40, ThicketStormDamageA9 = 60;
        public const int ThicketPollenDamageA9 = 6;

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

        /// <summary>После правил топота: нырок по герою 2 с в дальней полосе (ревью 02.10, ночь).</summary>
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

        /// <summary>Сколько последних тиков ПОДРЯД герой за спиной ближе 5,2 м — правило топота «за спиной»: ≥ 20.</summary>
        public int ThicketMasterRearTicks(int id) => TryGetThicketMasterMemory(id, out var m) ? m.RearTicks : 0;

        /// <summary>Сколько тиков подряд герой прижат (ближе 5,2 м), что бы босс ни делал, — топот «прижался»: ≥ 240.</summary>
        public int ThicketMasterHugTicks(int id) => TryGetThicketMasterMemory(id, out var m) ? m.HugTicks : 0;

        /// <summary>Сколько тиков подряд герой в дальней полосе — нырок-сближение: ≥ 60.</summary>
        public int ThicketMasterFarTicks(int id) => TryGetThicketMasterMemory(id, out var m) ? m.FarTicks : 0;

        /// <summary>
        /// Полоса героя сейчас (от кромки корпуса до его центра): ближняя до 1,4 м, средняя,
        /// дальняя от 6,5 м. Дальняя и тогда, когда герой за поводком дальше лапы — пешком
        /// босс его не достанет (стенд без поляны; на поляне 07.10 — и дальние углы пола, см. ThicketLeash).
        /// </summary>
        public ThicketBand ThicketHeroBand(int id)
        {
            if (!Entities.Alive[PlayerId] || (uint)id >= (uint)Entities.Count) return ThicketBand.Mid;
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 gap = ThicketHullGap(id, hero);
            if (gap >= ThicketFarGap) return ThicketBand.Far;
            if (_thicketMemory != null)
            {
                Fix64 foot = ThicketLeash + ThicketPawStartRange;
                if (FixVec2.DistanceSq(hero, _thicketMemory[id].Home) > foot * foot) return ThicketBand.Far;
            }
            return gap <= ThicketNearGap ? ThicketBand.Near : ThicketBand.Mid;
        }

        /// <summary>Когда топот «за спиной» снова готов (90 от начала прошлого топота); 0 — готов.</summary>
        public int ThicketRearStompReadyTick(int id)
            => _thicketReady != null && (uint)id < (uint)Entities.Capacity ? _thicketReady[id * ThicketActionSlots + ThicketRearStompSlot] : 0;

        /// <summary>Когда действие action снова готово (перезарядка); 0 — готово.</summary>
        public int ThicketReadyTick(int id, ThicketMasterAction action)
            => _thicketReady != null && (uint)id < (uint)Entities.Capacity ? _thicketReady[id * ThicketActionSlots + (int)action] : 0;

        /// <summary>Сдвигает перезарядку действия. Для стендов и тестов.</summary>
        public void SetThicketReadyTick(int id, ThicketMasterAction action, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketReady[id * ThicketActionSlots + (int)action] = tick;
        }

        /// <summary>Сдвигает перезарядку топота «за спиной». Для стендов и тестов.</summary>
        public void SetThicketRearStompReadyTick(int id, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketReady[id * ThicketActionSlots + ThicketRearStompSlot] = tick;
        }

        /// <summary>Срок нырка «под героя» (DiveNextTick); 0 — вступления ещё не было.</summary>
        public int ThicketDiveDueTick(int id) => TryGetThicketMasterMemory(id, out var m) ? m.DiveNextTick : 0;

        /// <summary>Сдвигает срок нырка «под героя». Для стендов и тестов (int.MaxValue / 2 — только сближение).</summary>
        public void SetThicketDiveDueTick(int id, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketMemory[id].DiveNextTick = tick;
        }

        /// <summary>Когда топот снова может выпасть в жребии рядом с лапой (135 от начала прошлого топота); 0 — готов.</summary>
        public int ThicketStompPickReadyTick(int id)
            => _thicketReady != null && (uint)id < (uint)Entities.Capacity ? _thicketReady[id * ThicketActionSlots + ThicketStompPickSlot] : 0;

        /// <summary>Сдвигает перезарядку топота в жребии. Для стендов и тестов (int.MaxValue / 2 — топот только по правилам).</summary>
        public void SetThicketStompPickReadyTick(int id, int tick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return;
            ThicketReady[id * ThicketActionSlots + ThicketStompPickSlot] = tick;
        }

        // ---- урон: всё — доля лапы (урона листа), глубина, «Сложно» и ярость растят разом ----

        public int ThicketPawDamageOf(int id) => Entities.Damage[id];

        /// <summary>Удар серии stage (с 0): первый — лапа целиком, второй и третий — 35% (7 на арене 9).</summary>
        public int ThicketPawStrikeDamageOf(int id, int stage)
            => stage == 0 ? ThicketPawDamageOf(id)
                : EnemyArchetypes.Share(ThicketPawDamageOf(id), ThicketPawFollowUpDamagePercent, 100);

        /// <summary>
        /// Первый замах серии: 17 во всех фазах (проверка находок 03.10 — баланс 02.10, ночь, делал
        /// 16 / 15 в фазах 2–3), а при уроне лапы больше 60 — 30 (ThicketPawHeavyDamage).
        /// </summary>
        public int ThicketPawWindupOf(int id)
            => ThicketPawDamageOf(id) > ThicketPawHeavyDamage ? ThicketPawHeavyWindupTicks : ThicketPawWindupTicks;
        public int ThicketShareOf(int id, int authoredA9) => EnemyArchetypes.Share(Entities.Damage[id], authoredA9, ThicketPawDamageA9);
        public int ThicketStompDamageOf(int id) => ThicketShareOf(id, ThicketStompDamageA9);

        /// <summary>Второе кольцо топота: ×0,75 топота (17 на арене 9).</summary>
        public int ThicketStompRingDamageOf(int id) => ThicketStompDamageOf(id) * ThicketStompRingDamagePercent / 100;

        // ---- фигуры: одна на метку и на попадание ----

        /// <summary>Сектор лапы с вершиной в origin (бьющее плечо) по direction: ThicketPawReach, ±50°.</summary>
        public static EnemyTelegraph ThicketPawSector(FixVec2 origin, FixVec2 direction)
            => EnemyTelegraph.Sector(origin, direction, ThicketPawReach, ThicketPawArcCos);

        /// <summary>Удар stage серии — правой лапой (чётный: П/Л/П).</summary>
        public static bool ThicketPawIsRight(int stage) => (stage & 1) == 0;

        /// <summary>
        /// Бьющее плечо удара stage: от тела body ThicketPawShoulderForward вперёд по
        /// направлению удара direction и ThicketPawShoulderSide вбок — вправо у
        /// правой лапы, влево у левой.
        /// </summary>
        public static FixVec2 ThicketPawShoulder(FixVec2 body, FixVec2 direction, int stage)
        {
            FixVec2 forward = direction.Normalized();
            var left = new FixVec2(-forward.Y, forward.X);
            Fix64 side = ThicketPawIsRight(stage) ? -ThicketPawShoulderSide : ThicketPawShoulderSide;
            return body + forward * ThicketPawShoulderForward + left * side;
        }

        /// <summary>Фигура удара stage серии (метка на земле и попадание): сектор от бьющего плеча.</summary>
        public static EnemyTelegraph ThicketPawStrikeSector(FixVec2 body, FixVec2 direction, int stage)
            => ThicketPawSector(ThicketPawShoulder(body, direction, stage), direction.Normalized());

        /// <summary>Середина сектора удара stage (ThicketMasterState.Target лапы).</summary>
        public static FixVec2 ThicketPawStrikeMiddle(FixVec2 body, FixVec2 direction, int stage)
            => ThicketPawShoulder(body, direction, stage) + direction.Normalized() * (ThicketPawReach / 2);

        public static EnemyTelegraph ThicketStompCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketStompRadius);

        public static EnemyTelegraph ThicketStompRing(FixVec2 center)
            => EnemyTelegraph.Ring(center, ThicketStompRadius, ThicketStompRingOuterRadius);

        /// <summary>
        /// Попало ли кольцо топота: тело героя заходит за внешний край 7,5, а ЦЕНТР — снаружи
        /// круга 5,2 (ревью 02.10, ночь: «шагнул обратно внутрь 5,2 до кольца — цел»; общая
        /// метка кольца щадила только тело целиком внутри, 4,75 м).
        /// </summary>
        public static bool ThicketStompRingHits(FixVec2 center, FixVec2 hero, Fix64 body)
        {
            Fix64 outer = ThicketStompRingOuterRadius + body, inner = ThicketStompRadius;
            Fix64 distanceSq = FixVec2.DistanceSq(hero, center);
            return distanceSq <= outer * outer && distanceSq > inner * inner;
        }

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
                case ThicketMasterAction.Seeds: return EnemyActionKind.ThicketSeeds;
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
                Hashing.Mix(ref hash, m.QuietUntil); Hashing.Mix(ref hash, m.RearTicks);
                Hashing.Mix(ref hash, m.HugTicks); Hashing.Mix(ref hash, m.FarTicks); Hashing.Mix(ref hash, m.OutOfReachTicks);
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
        /// база уже своя (14 — лапа 23 на арене 9). На своей поляне (ревью 02.10,
        /// вечер) он встаёт не посреди неё, а в дальнем верхнем правом углу
        /// (ThicketSpawnPoint) лицом ко входу; центр поляны остаётся Home — центром
        /// поводка.
        /// </summary>
        private EncounterPlan SetupThicketMasterArena(LayoutMap map, ulong spawnSeed, int healthPercent,
            EncounterSettings settings, int hardPercent, int arena)
        {
            SetupRift(map, spawnSeed, 0, 0, 1);
            int module = map.GetPlaced(map.GetExit(0)).Parent;
            var radius = EnemyArchetypes.ThicketMasterBodyRadius;
            // Центр поляны — центр поводка (Home): буря, вид и «К боссу» меряют поляну от него.
            var home = BossFloorPoint(map, map.CenterOf(module), radius);
            var center = ThicketSpawnPoint(map, home);
            var rng = new Pcg32(spawnSeed, 0x424F5353UL);
            var pack = settings.Pick(EncounterRole.ExitGuard, ref rng);
            int boss = Entities.Spawn(center,
                EnemyArchetypes.ScaleHealth(EnemyArchetypes.ThicketMasterHealth, healthPercent, hardPercent), Faction.Orvill);
            ConfigureEnemy(boss, EnemyKind.ForestThicketMaster);
            ThicketMemory[boss].Home = home;
            // Из угла смотрит на вход: герой поднимается по тропе снизу.
            if (!center.Equals(home))
            {
                FixVec2 look = map.EntryPoint - center;
                if (look.LengthSq.Raw != 0) Entities.Facing[boss] = look.Normalized();
            }
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

        /// <summary>
        /// Где встаёт босс (ревью 02.10, вечер: «босса надо ставить дальше в
        /// противоположный угол»): на неизменной поляне босса — её дальний верхний
        /// правый угол (GladeLayout.BossSpawnOffset от центра), напротив входа снизу
        /// по центру; весь корпус (ThicketHullReach) там на полу. Карта без этой
        /// поляны или угол не на полу — по-старому, home (центр комнаты перед выходом).
        /// </summary>
        private static FixVec2 ThicketSpawnPoint(LayoutMap map, FixVec2 home)
        {
            if (map == null || map.GladeCount != 1) return home;
            var glade = map.GetGlade(0);
            if (!glade.Radii.Equals(GladeLayout.BossClearingRadii) || glade.Shape != GladeShape.Rounded) return home;
            FixVec2 corner = glade.Center + GladeLayout.BossSpawnOffset;
            return glade.Field(corner) <= Fix64.One && map.IsWalkable(corner, ThicketHullReach) ? corner : home;
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
                // Свои ещё не сработавшие метки ждут вместе с боссом: заполнение тянется. Метка, чей удар
                // уже прошёл, а она ещё лежит (линия шипа терновника в полёте), удар не сдвигает — остаётся
                // полной, — а живёт на столько же дольше: шип висит в воздухе (ShiftThicketSeeds).
                for (int slot = 0; slot < _telegraphHighWater; slot++)
                {
                    var t = _telegraphs[slot];
                    if (t.Serial == 0 || t.Source != id || !t.IsActive) continue;
                    int impact = t.ImpactTick >= Tick ? t.ImpactTick + shift : t.ImpactTick;
                    _telegraphs[slot] = new EnemyTelegraph(t.Serial, t.Source, t.StartTick, impact,
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
            // Терновник: кусты растут и шипы летят после жеста босса (Simulation.ForestBoss.Seeds).
            if (ThicketSeedsHoldToken(id)) return true;
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
            // Терновник — 2 от жеста до последнего шипа (Simulation.ForestBoss.Seeds).
            int seeds = ThicketSeedsMarkWeight(id, out int seedStart, out int seedImpact);
            if (seeds > 0)
            {
                weight += seeds;
                if (seedStart > start) start = seedStart;
                if (seedImpact >= Tick && (impact == int.MinValue || seedImpact < impact)) impact = seedImpact;
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
                        for (int k = a.Stage; k < a.Stages; impact += ThicketPawGapOf(a, k), k++)
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
            AddThicketSeedContacts(id);
        }
    }
}
