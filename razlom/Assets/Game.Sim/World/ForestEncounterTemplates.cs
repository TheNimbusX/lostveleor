using System;

namespace Game.Sim
{
    /// <summary>
    /// Шаблоны встреч леса (стадия 6 плана «Мобы леса»), таблицей в коде.
    ///
    /// По документу владельца «Локация 1 — Лес»: 8 арен и босс; обычных 5–6
    /// из 8, засада и выживание — не больше одной за забег, одна элитная
    /// встреча наверняка (А5–А7, см. ArenaRunPlan) и вторая на А7–А8 с шансом
    /// 30%. Цена видов — Threat в EnemyArchetypes (рой 1, хранитель 2,
    /// плюй-плод 2, камнекопыт 3, Корнехват 3, Расщепень 4 вместе с детьми,
    /// вендиго и Шипомёт 6); бюджет угроз читается на ВОЛНУ (решение от
    /// 26.09), в обычной арене с А2 — не меньше двух волн. Обычная волна
    /// держит бюджет целиком (от нижней границы до верхней), засада, выживание
    /// и элита — только потолок: у них свой процент бюджета и своя поддержка.
    ///
    /// ЛЕСТНИЦА ПАЧЕК (решение владельца от 29.09, «лестница с весами»). У
    /// каждого шаблона уровень — Tier: лёгкие (4) на А1–А2, средние (10) на
    /// А2–А6, тяжёлые (6) на А6–А8, элитные (6) — в своих слотах. Какой
    /// уровень выпадает арене, решает ArenaRunPlan (А1 лёгкая; А2 60/40
    /// лёгкая/средняя; А3–А5 средние; А6 50/50 средняя/тяжёлая; А7–А8
    /// тяжёлые). Окно арен шаблона лежит там, где лестница его уровень
    /// допускает. Засада и выживание — средние: засада на А4–А6, выживание
    /// на А5–А6 (его 60 с не влезают в окно А4).
    ///
    /// НОВЫЙ ВИД — СНАЧАЛА ОДИН. Урок (Lesson) — единственный путь вида в
    /// забег, и уроков у вида столько, на скольких уровнях он может впервые
    /// встретиться (первая встреча мягче — владелец, 29.09):
    ///   рой — E01, E16 (лёгкие, А1);
    ///   хранитель — E02, E17 (лёгкие, А2), E18 (средний, А2);
    ///   плюй-плод — E04, E19 (средние, А3–А4);
    ///   камнекопыт — E03 (средний, А3–А5), E05 (тяжёлый, А6–А8);
    ///   Корнехват — E06 (А4–А6), E20 (А5–А6) — средние, E22 (тяжёлый, А6–А8);
    ///   Расщепень — E07 (А4–А6), E21 (А5–А6) — средние, E23 (тяжёлый, А6–А8);
    ///   вендиго — E08, Шипомёт — E08T (элитные, А5–А7).
    /// Урок уже знакомого вида план ставит как обычную пачку. Сочетания (E09,
    /// E10, E15, засада, выживание, элиты E13, E14, E24, E25) план ставит только
    /// после уроков всех своих видов — см. ArenaRunPlan. Тяжёлые уроки
    /// держат лестницу: без них вид, не встреченный до А6, не встречался бы
    /// вовсе, а тяжёлой арене не хватало бы пачек.
    ///
    /// ОТСТУП ПО ВОЛНАМ: E01 — семь волн по четыре Корнеполза, а не одна.
    /// Одной волной в 4–6 угроз А1 шла 7 с при цели 25–35, тремя — 15 с,
    /// шестью по 4–6 — 25 с: время А1 — это выход волн из земли и подход к
    /// ним, а не удары. Волна по нижней границе бюджета — меньше ртов разом:
    /// семь по четыре дольше шести по 4–6 и кусают реже (стенд,
    /// проход 2: 84 урона против 97 у героя 5-го уровня, 114 против 132 у 1-го).
    ///
    /// ОТСТУП ПО БЮДЖЕТУ: E15 берёт 70% бюджета на волну (11–13 угроз на А8
    /// вместо 15–18). Двумя полными волнами А8 шла 84 с при цели 60–80, с 80%
    /// — 78–81 с: время держат хранители, камнекопыты и плоды ядра, а добор
    /// режет в основном рой.
    ///
    /// ЧИСЛА ЛЕСТНИЦЫ — ПОДОГНАНЫ (29.09, план «Мобы леса v2», поток B) на
    /// всей механике: герой без статов за уровни (270/54), корни, оглушение
    /// Камнекопыта, круг Вендиго, такт атак. Стенд — ArenaBalanceBench, 1200
    /// сидов на подгонку и 1500 свежих на проверку: бот выигрывает ~70%
    /// забегов, на обычной арене теряет 19% здоровья (p50), 96% смертей — на
    /// элитах, А7–А8 и боссе, время каждой арены — в окне документа.
    /// Правки чисел — в комментариях шаблонов с пометкой «подгонка 29.09».
    ///
    /// Ключи 'forest.E01'… стабильны: из них Id шаблона, а он — в хеше плана.
    /// </summary>
    public static class ForestEncounterTemplates
    {
        /// <summary>Сколько арен в лесу до босса.</summary>
        public const int ArenaCount = 8;

        // Бюджет угроз арены из документа владельца — на одну волну.
        private static readonly int[] BudgetLow = { 4, 6, 7, 9, 10, 12, 13, 15 };
        private static readonly int[] BudgetHigh = { 6, 8, 9, 11, 12, 14, 16, 18 };

        /// <summary>Нижняя граница бюджета угроз волны на арене. Дальше восьмой — как на восьмой.</summary>
        public static int BudgetMin(int arena) => BudgetLow[ClampArena(arena) - 1];

        /// <summary>Верхняя граница бюджета угроз волны на арене.</summary>
        public static int BudgetMax(int arena) => BudgetHigh[ClampArena(arena) - 1];

        public static int ClampArena(int arena) => arena < 1 ? 1 : arena > ArenaCount ? ArenaCount : arena;

        /// <summary>Цена вида в бюджете угроз.</summary>
        public static int Threat(EnemyKind kind) => EnemyArchetypes.Get(kind).Threat;

        // Засада и выживание по таймеру и без долгих пауз. Тики — 30 в секунду.
        private const int AmbushNextWaveTicks = 8 * Simulation.TicksPerSecond;

        /// <summary>Выживание: 60 секунд; волны каждые 11 секунд, по таймеру оставшиеся уходят в землю.</summary>
        public const int SurvivalTicks = 60 * Simulation.TicksPerSecond;
        public const int SurvivalWaveTicks = 11 * Simulation.TicksPerSecond;

        /// <summary>
        /// Вес тяжёлых сочетаний (E09, E10, E15) против тяжёлых уроков: когда
        /// виды знакомы, конец леса чаще собирает их вместе, чем учит заново.
        /// </summary>
        private const int HardMixWeight = 200;

        private static WaveGroup Swarm(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestRootSwarm, min, max, at);
        private static WaveGroup SwarmFill(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestRootSwarm, min, max, at, fill: true);
        private static WaveGroup Guardian(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestGuardian, min, max, at);
        private static WaveGroup Bud(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestBud, min, max, at);
        private static WaveGroup BudFill(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestBud, min, max, at, fill: true);
        private static WaveGroup Stonehoof(int count, WavePlacement at) => new WaveGroup(EnemyKind.ForestStonehoof, count, count, at);
        private static WaveGroup Wendigo(WavePlacement at) => new WaveGroup(EnemyKind.ForestWendigo, 1, 1, at, elite: true);
        private static WaveGroup Thorncaster(WavePlacement at) => new WaveGroup(EnemyKind.ForestThorncaster, 1, 1, at, elite: true);
        private static WaveGroup Snarer(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestRootSnarer, min, max, at);
        private static WaveGroup Splitter(int min, int max, WavePlacement at) => new WaveGroup(EnemyKind.ForestSplitter, min, max, at);
        private static EncounterWave Wave(WaveTrigger trigger, params WaveGroup[] groups) => new EncounterWave(trigger, groups);

        private const WavePlacement Front = WavePlacement.Front, Back = WavePlacement.Back,
            Flank = WavePlacement.Flank, Center = WavePlacement.Center;

        private const EncounterTier Easy = EncounterTier.Easy, Medium = EncounterTier.Medium, Hard = EncounterTier.Hard;

        // СОСТАВ ПО СТЕНДУ (26.09). Первый прогон добирал бюджет одним роем —
        // и арены шли вдвое быстрее целей, а рой давал 78% урона по герою: укус
        // без метки не обходится, и с переносом здоровья до босса дожили 4 из 20.
        // Теперь бюджет набирают сначала хранители (до двух на волну, 275
        // здоровья на угрозу, от сектора бот уходит в 97% замахов), камнекопыт и
        // плюй-плод (150 на угрозу и почти без урона), а рой — только остаток.
        // Время арены держится здоровьем и числом волн, а не толпой.

        // ---------- лёгкие: А1–А2 ----------

        /// <summary>
        /// E01 — разминка: только рой, урок роя. Семь волн по четыре подряд,
        /// следующая — когда арена пуста (см. «отступ по волнам» выше).
        /// </summary>
        public static readonly ArenaEncounterTemplate E01 = new ArenaEncounterTemplate("forest.E01",
            ArenaEncounterType.Normal, 1, 1, 2, EnemyKind.ForestRootSwarm, new[]
            {
                Wave(WaveTrigger.Start, Swarm(2, 2, Center), SwarmFill(2, 2, Front)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Front)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Back)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Front)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 4, Back)),
            }, tier: Easy);

        /// <summary>
        /// E16 — рой по кругу, второй урок роя на А1: шесть горстей по 4–5,
        /// каждая с новой стороны; следующая встаёт, когда живых не больше
        /// одного, — учит оборачиваться, а не стоять лицом к одной стороне.
        /// </summary>
        public static readonly ArenaEncounterTemplate E16 = new ArenaEncounterTemplate("forest.E16",
            ArenaEncounterType.Normal, 1, 1, 2, EnemyKind.ForestRootSwarm, new[]
            {
                Wave(WaveTrigger.Start, Swarm(2, 2, Front), SwarmFill(2, 3, Flank)),
                Wave(WaveTrigger.AliveAtMost(1), SwarmFill(4, 5, Back)),
                Wave(WaveTrigger.AliveAtMost(1), SwarmFill(4, 5, Flank)),
                Wave(WaveTrigger.AliveAtMost(1), SwarmFill(4, 5, Front)),
                Wave(WaveTrigger.AliveAtMost(1), SwarmFill(4, 5, Back)),
                Wave(WaveTrigger.AliveAtMost(0), SwarmFill(4, 5, Flank)),
            }, tier: Easy);

        /// <summary>E02 — рой и хранитель. Урок хранителя: сначала один, потом два, потом один.</summary>
        public static readonly ArenaEncounterTemplate E02 = new ArenaEncounterTemplate("forest.E02",
            ArenaEncounterType.Normal, 2, 2, 2, EnemyKind.ForestGuardian, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Front), SwarmFill(4, 6, Center)),
                Wave(WaveTrigger.AliveAtMost(2), Guardian(2, 2, Flank), SwarmFill(2, 4, Front)),
                Wave(WaveTrigger.AliveAtMost(2), Guardian(1, 1, Back), SwarmFill(4, 6, Flank)),
            }, tier: Easy);

        /// <summary>
        /// E17 — хранитель за хранителем, мягкий урок хранителя: всегда один,
        /// каждый раз с новой стороны, и следующий — когда арена пуста. Двух
        /// хранителей разом тут не бывает (у E02 — во второй волне). Подгонка
        /// 29.09: порог «один живой» → «пусто» — с Хранителем 500 здоровья
        /// E17 шла 29,8 с, ниже окна А2 (30–40), и вся А2 — 31,8 с у самой
        /// кромки; теперь E17 — 35 с, А2 — 33,6 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E17 = new ArenaEncounterTemplate("forest.E17",
            ArenaEncounterType.Normal, 2, 2, 2, EnemyKind.ForestGuardian, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Center), SwarmFill(4, 6, Front)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 1, Flank), SwarmFill(4, 6, Back)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 1, Back), SwarmFill(4, 6, Flank)),
            }, tier: Easy);

        // ---------- средние: А2–А6 ----------

        /// <summary>
        /// E18 — клин хранителей: урок хранителя для средней А2. Хранителей
        /// столько же, сколько в E02 (1–2–1), но волны наезжают друг на друга:
        /// следующая встаёт, когда живых не больше трёх, — вторая выходит к
        /// недобитому первому. Стенд 29.09: с 1–2–2 и порогом два А2 шла 45 с
        /// при цели 30–40 и не больнее E02 — дольше, а не труднее.
        /// </summary>
        public static readonly ArenaEncounterTemplate E18 = new ArenaEncounterTemplate("forest.E18",
            ArenaEncounterType.Normal, 2, 2, 2, EnemyKind.ForestGuardian, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Front), SwarmFill(4, 6, Center)),
                Wave(WaveTrigger.AliveAtMost(3), Guardian(2, 2, Flank), SwarmFill(2, 4, Front)),
                Wave(WaveTrigger.AliveAtMost(3), Guardian(1, 1, Back), SwarmFill(4, 6, Flank)),
            }, tier: Medium);

        /// <summary>
        /// E03 — урок тарана: сначала один камнекопыт и немного роя; когда
        /// арена пуста — он же с парой хранителей. А3–А5: окно шире дока (А3),
        /// иначе средние арены не вмещали бы его рядом с другими уроками.
        /// </summary>
        public static readonly ArenaEncounterTemplate E03 = new ArenaEncounterTemplate("forest.E03",
            ArenaEncounterType.Normal, 3, 5, 3, EnemyKind.ForestStonehoof, new[]
            {
                Wave(WaveTrigger.Start, Stonehoof(1, Front), SwarmFill(4, 8, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Stonehoof(1, Front), Guardian(2, 2, Flank), SwarmFill(0, 4, Center)),
            }, tier: Medium);

        /// <summary>
        /// E04 — урок стрелка: плюй-плод с хранителем и роем, три волны. А3–А4:
        /// хранитель в нём должен быть знаком, а на А2 знаком только рой.
        /// Подгонка 29.09: 85% бюджета — полным А3 шла 51 с при окне 40–50
        /// (плод теперь переходит между залпами, и догнать его дольше).
        /// </summary>
        public static readonly ArenaEncounterTemplate E04 = new ArenaEncounterTemplate("forest.E04",
            ArenaEncounterType.Normal, 3, 4, 2, EnemyKind.ForestBud, new[]
            {
                Wave(WaveTrigger.Start, Bud(1, 1, Front), Guardian(1, 1, Center), SwarmFill(3, 5, Center)),
                Wave(WaveTrigger.AliveAtMost(3), Bud(1, 1, Flank), Guardian(1, 1, Front), BudFill(0, 1, Back), SwarmFill(1, 5, Front)),
                Wave(WaveTrigger.AliveAtMost(3), Bud(1, 1, Back), Guardian(1, 1, Flank), SwarmFill(3, 5, Flank)),
            }, budgetPercent: 85, tier: Medium);

        /// <summary>
        /// E19 — плоды за стеной, второй урок стрелка: плод сзади, пока
        /// хранитель держит перёд; потом пара хранителей стеной перед плодом,
        /// потом плоды с фланга. 80% бюджета — стенд 29.09: полным бюджетом
        /// А3–А4 шла 60 с при цели 40–55. Подгонка 29.09: в третьей волне —
        /// только плоды и рой, без хранителя: четыре хранителя за урок вели
        /// А3 к 57 с, А4 — к 64 с; теперь 45 и 53 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E19 = new ArenaEncounterTemplate("forest.E19",
            ArenaEncounterType.Normal, 3, 4, 2, EnemyKind.ForestBud, new[]
            {
                Wave(WaveTrigger.Start, Bud(1, 1, Back), Guardian(1, 1, Front), SwarmFill(3, 6, Center)),
                Wave(WaveTrigger.AliveAtMost(2), Guardian(2, 2, Front), Bud(1, 1, Back), SwarmFill(1, 4, Flank)),
                Wave(WaveTrigger.AliveAtMost(2), Bud(1, 1, Flank), BudFill(0, 1, Back),
                    SwarmFill(1, 5, Front)),
            }, budgetPercent: 80, tier: Medium);

        /// <summary>
        /// E06 — урок корней: Корнехват с хранителем и роем; когда живых не
        /// больше одного — второй Корнехват с парой хранителей; потом пара
        /// хранителей без Корнехвата. Стенд 27.09 (на А5): двумя полными
        /// волнами А5 шла 31 с при цели 50–70 (рой добора гибнет с двух ударов);
        /// тремя полными — 65 с, но пятнадцать Корнеползов снимали герою 5-го
        /// уровня половину здоровья, а третий Корнехват под замедлением добивал
        /// тех, кто пришёл на А5 с половиной. Три волны по 80% бюджета: время
        /// держат Корнехваты и хранители, Корнехват в волне — один. А4–А6 с
        /// лестницей (было только А5). Подгонка 29.09: хранитель во второй и
        /// третьей волне один, а не пара, и 70% бюджета. С корнями, что держат
        /// героя секунду, урок шёл 74–89 с при окнах 45–65 и на А6 снимал
        /// половину здоровья (p50); теперь 53–61 с и 26–37%.
        /// </summary>
        public static readonly ArenaEncounterTemplate E06 = new ArenaEncounterTemplate("forest.E06",
            ArenaEncounterType.Normal, 4, 6, 2, EnemyKind.ForestRootSnarer, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Front), Guardian(1, 1, Center), SwarmFill(3, 5, Center)),
                Wave(WaveTrigger.AliveAtMost(1), Snarer(1, 1, Back), Guardian(1, 1, Front), SwarmFill(1, 3, Flank)),
                Wave(WaveTrigger.AliveAtMost(1), Guardian(1, 1, Back), SwarmFill(4, 6, Front)),
            }, budgetPercent: 70, tier: Medium);

        /// <summary>
        /// E20 — корни и плоды, второй урок корней: Корнехват с плодом и роем,
        /// без хранителя — круг под ногами и выстрел сверху; потом Корнехват с
        /// хранителем и плодом. Две волны — стенд 29.09: с третьей (пара
        /// хранителей с плодом) А5–А6 шла 76 с при цели 50–65.
        /// </summary>
        public static readonly ArenaEncounterTemplate E20 = new ArenaEncounterTemplate("forest.E20",
            ArenaEncounterType.Normal, 5, 6, 3, EnemyKind.ForestRootSnarer, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Front), Bud(1, 1, Back), SwarmFill(2, 5, Center)),
                Wave(WaveTrigger.AliveAtMost(1), Snarer(1, 1, Flank), Guardian(1, 1, Front), Bud(1, 1, Back),
                    SwarmFill(0, 3, Center)),
            }, budgetPercent: 80, tier: Medium);

        /// <summary>
        /// E07 — урок раскола: Расщепень с хранителем и роем, 70% бюджета —
        /// распад добавляет двух детей на каждого, и урок не тонет в толпе.
        /// Следующая волна — только на пустой арене: дети считаются живыми, и
        /// порог выше нуля выпускал бы её прямо на детей прошлой. Три волны —
        /// стенд 27.09: двумя А6 шла 31 с при цели 50–65. А4–А6 с лестницей
        /// (было только А6). Подгонка 29.09: 50% бюджета — с 70% урок шёл на
        /// А4 и А6 61 и 70 с при окнах 45–55 и 50–65 и на А6 снимал 53%
        /// здоровья (p50); теперь 55 и 60 с, 39%.
        /// </summary>
        public static readonly ArenaEncounterTemplate E07 = new ArenaEncounterTemplate("forest.E07",
            ArenaEncounterType.Normal, 4, 6, 2, EnemyKind.ForestSplitter, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Guardian(1, 1, Center), SwarmFill(0, 4, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Splitter(1, 1, Flank), Guardian(0, 1, Front), SwarmFill(0, 4, Back)),
                Wave(WaveTrigger.AliveAtMost(0), Splitter(1, 1, Back), Guardian(0, 1, Flank), SwarmFill(0, 4, Front)),
            }, budgetPercent: 50, tier: Medium);

        /// <summary>
        /// E21 — раскол у плодов, второй урок раскола: Расщепень с плодом за
        /// спиной; на пустой арене — он же с хранителем. Как у E07 — 70%
        /// бюджета и волна только на пустой арене. Две волны — стенд 29.09: с
        /// третьей (пара хранителей с плодом) А5–А6 шла 76 с при цели 50–65.
        /// </summary>
        public static readonly ArenaEncounterTemplate E21 = new ArenaEncounterTemplate("forest.E21",
            ArenaEncounterType.Normal, 5, 6, 3, EnemyKind.ForestSplitter, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Bud(1, 1, Back), SwarmFill(0, 3, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Splitter(1, 1, Flank), Guardian(1, 1, Front), Bud(0, 1, Back),
                    SwarmFill(0, 3, Center)),
            }, budgetPercent: 70, tier: Medium);

        /// <summary>
        /// E11 — засада: приманка (рой и хранитель), потом волна с парой
        /// хранителей за спиной и волна с хранителем спереди, рой — с флангов.
        /// Срок волны — 8 с. Средняя, А4–А6 (было А4–А7). Подгонка 29.09: в
        /// третьей волне хранитель один, а не пара — пять хранителей вели А4
        /// к 59 с при окне 45–55; теперь 48 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E11 = new ArenaEncounterTemplate("forest.E11",
            ArenaEncounterType.Ambush, 4, 6, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Swarm(3, 3, Center), Guardian(1, 1, Front)),
                Wave(WaveTrigger.AliveAtMost(2, AmbushNextWaveTicks), Guardian(2, 2, Back), Bud(1, 1, Front), SwarmFill(1, 6, Flank)),
                Wave(WaveTrigger.AliveAtMost(2, AmbushNextWaveTicks), Guardian(1, 1, Front), Bud(1, 1, Back), SwarmFill(1, 6, Flank)),
            }, budgetPercent: 70, tier: Medium);

        /// <summary>
        /// E12 — выживание 60 с: волна каждые 11 с (раньше, если арена
        /// пуста), по таймеру оставшиеся уходят в землю. 40% бюджета на волну,
        /// и в каждой — кто-то плотный: иначе минута — это минута роя. Средняя,
        /// А5–А6 (было А5–А7); в четвёртой волне вместо камнекопыта — пара
        /// хранителей: выживание встаёт и в забег, где тарана ещё не учили.
        /// Подгонка 29.09: не на А4 (лестница ставила А4–А6) — выживание идёт
        /// ровно 60 с, а окно А4 — 45–55: каждый четвёртый забег держал А4 на
        /// 60 с, и медиана А4 была 60 с. На А5–А6 60 с — в окне.
        /// </summary>
        public static readonly ArenaEncounterTemplate E12 = new ArenaEncounterTemplate("forest.E12",
            ArenaEncounterType.Survival, 5, 6, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Front), SwarmFill(2, 4, Center)),
                Wave(WaveTrigger.AtTick(SurvivalWaveTicks), Bud(1, 1, Back), Guardian(1, 1, Flank), SwarmFill(0, 2, Flank)),
                Wave(WaveTrigger.AtTick(2 * SurvivalWaveTicks), Guardian(1, 1, Front), SwarmFill(2, 4, Center)),
                Wave(WaveTrigger.AtTick(3 * SurvivalWaveTicks), Guardian(2, 2, Front), SwarmFill(0, 3, Back)),
                Wave(WaveTrigger.AtTick(4 * SurvivalWaveTicks), Guardian(1, 1, Front), Bud(1, 1, Flank),
                    SwarmFill(0, 2, Center)),
            }, budgetPercent: 40, survivalTicks: SurvivalTicks, tier: Medium);

        // ---------- тяжёлые: А6–А8 ----------

        /// <summary>
        /// E05 — перекрёстное давление и тяжёлый урок тарана: сначала
        /// камнекопыт с парой хранителей и роем, без стрелков; потом таран,
        /// плоды с фланга и хранитель, рой — остаток. Был А5–А8 без урока;
        /// с лестницей — А6–А8 и 70% бюджета, как другие тяжёлые уроки (E22,
        /// E23): с 80% урок клал 8 забегов из 300 — больше сочетаний.
        /// Подгонка 29.09: вторая волна — только на пустой арене (таран по
        /// одному, как в E03), 60% бюджета и рой первой волны от одного (было
        /// от трёх — с 60% три Корнеполза не влезали в бюджет А6). С оглушением
        /// разбега урок стал самой смертной пачкой леса: 27 смертей из 300
        /// забегов, на А8 — 60% здоровья (p50); теперь ~8 из 300 и 37%.
        /// </summary>
        public static readonly ArenaEncounterTemplate E05 = new ArenaEncounterTemplate("forest.E05",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.ForestStonehoof, new[]
            {
                Wave(WaveTrigger.Start, Stonehoof(1, Front), Guardian(2, 2, Center), SwarmFill(1, 7, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Stonehoof(1, Flank), Bud(1, 1, Back), Guardian(1, 1, Front),
                    BudFill(0, 2, Flank), SwarmFill(0, 7, Front)),
            }, budgetPercent: 60, tier: Hard);

        /// <summary>
        /// E22 — корни в чаще, тяжёлый урок корней: Корнехват с хранителем и
        /// роем; когда живых не больше одного — второй Корнехват, пара
        /// хранителей и плод. 70% бюджета и порог «один живой» — стенд 29.09:
        /// с 80% и порогом «двое» два Корнехвата сходились в одном бою, и урок
        /// клал 10 забегов из 300 — больше любой тяжёлой пачки.
        /// </summary>
        public static readonly ArenaEncounterTemplate E22 = new ArenaEncounterTemplate("forest.E22",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.ForestRootSnarer, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Front), Guardian(1, 1, Center), SwarmFill(3, 8, Flank)),
                Wave(WaveTrigger.AliveAtMost(1), Snarer(1, 1, Back), Guardian(2, 2, Front), Bud(1, 1, Flank),
                    BudFill(0, 1, Back), SwarmFill(0, 6, Center)),
            }, budgetPercent: 70, tier: Hard);

        /// <summary>
        /// E23 — раскол на тропе, тяжёлый урок раскола: Расщепень с хранителем
        /// и роем; на пустой арене — он же с плодом и хранителем. 70% бюджета и
        /// волна на пустой арене — как у E07. Две волны — стенд 29.09: с
        /// третьей (пара хранителей с плодом) А6–А8 шла 91 с при цели 50–80 и
        /// стала самой смертной пачкой леса — урок так не должен.
        /// </summary>
        public static readonly ArenaEncounterTemplate E23 = new ArenaEncounterTemplate("forest.E23",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.ForestSplitter, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Guardian(1, 1, Center), SwarmFill(2, 8, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Splitter(1, 1, Back), Guardian(1, 1, Front), Bud(1, 1, Flank),
                    BudFill(0, 1, Back), SwarmFill(0, 6, Center)),
            }, budgetPercent: 70, tier: Hard);

        /// <summary>
        /// E09 — точка и линия: круг Корнехвата под героем, пока камнекопыт
        /// целит таран, а плод бьёт сверху; во второй волне вместо тарана —
        /// хранители. Рой — остаток бюджета; 80% бюджета, как у E10 (стенд
        /// 27.09): полным бюджетом на А8 добор давал по 7–9 Корнеползов на волну,
        /// и рой снимал герою больше, чем у него было.
        /// </summary>
        public static readonly ArenaEncounterTemplate E09 = new ArenaEncounterTemplate("forest.E09",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Flank), Stonehoof(1, Front), Bud(1, 1, Back), SwarmFill(2, 10, Center)),
                Wave(WaveTrigger.AliveAtMost(2), Snarer(1, 1, Back), Guardian(1, 2, Front), Bud(1, 1, Flank),
                    SwarmFill(1, 11, Center)),
            }, weight: HardMixWeight, budgetPercent: 80, tier: Hard);

        /// <summary>
        /// E10 — раскол под давлением: Расщепень под плодом и с хранителем,
        /// рой — остаток. Дети распада считаются живыми и держат порог второй
        /// волны: она не выходит, пока их не добили. 80% бюджета и хранитель в
        /// обеих волнах — стенд 27.09: полным бюджетом добор давал на А8 по
        /// 7–10 Корнеползов на волну, рой снимал герою 5-го уровня две трети
        /// здоровья, и 4 забега из 9 с переносом кончились здесь.
        /// </summary>
        public static readonly ArenaEncounterTemplate E10 = new ArenaEncounterTemplate("forest.E10",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Bud(1, 1, Back), Guardian(1, 1, Center), SwarmFill(2, 10, Flank)),
                Wave(WaveTrigger.AliveAtMost(2), Splitter(1, 1, Flank), Bud(1, 1, Front), Guardian(1, 1, Back),
                    SwarmFill(2, 10, Center)),
            }, weight: HardMixWeight, budgetPercent: 80, tier: Hard);

        /// <summary>
        /// E15 — перед боссом: четыре знакомых вида, ничего нового. Бюджет
        /// добирают плоды, а не рой: к А8 герой приходит с тем, что осталось.
        /// 65% бюджета на волну — см. «отступ по бюджету» выше. Подгонка 29.09:
        /// 70% → 65% — плоды теперь переходят между залпами, и с 70% А8 шла
        /// 81–83 с при окне 60–80; теперь 73 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E15 = new ArenaEncounterTemplate("forest.E15",
            ArenaEncounterType.Normal, 8, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Guardian(2, 2, Front), Bud(1, 1, Back), Stonehoof(1, Flank),
                    BudFill(0, 3, Back), SwarmFill(0, 9, Center)),
                Wave(WaveTrigger.AliveAtMost(4), Guardian(0, 1, Flank), Bud(1, 1, Front), Stonehoof(1, Back),
                    BudFill(0, 3, Front), SwarmFill(0, 9, Center)),
            }, weight: HardMixWeight, budgetPercent: 65, tier: Hard);

        // ---------- элитные: свои слоты, А5–А8 ----------
        //
        // Уровень элиты — Hard: лестница её не ставит, а стенд считает тяжёлой.

        /// <summary>
        /// E08 — первая элита: вендиго с парой роя — урок вендиго; когда арена
        /// пуста, из земли встаёт небольшая подмога. Подмога ПОСЛЕ вендиго, а не
        /// к нему: с ней в бою вендиго на А5–А6 клал половину забегов с переносом
        /// здоровья (стенд, 26.09). А5–А7 — окно первой элиты.
        /// </summary>
        public static readonly ArenaEncounterTemplate E08 = new ArenaEncounterTemplate("forest.E08",
            ArenaEncounterType.Elite, 5, 7, 3, EnemyKind.ForestWendigo, new[]
            {
                Wave(WaveTrigger.Start, Wendigo(Center), Swarm(2, 3, Front)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(2, 2, Flank), Swarm(2, 3, Back)),
            }, tier: Hard);

        /// <summary>
        /// E08T — первая элита, Шипомёт: он с парой роя — урок Шипомёта; когда
        /// арена пуста, встаёт подмога, как у вендиго в E08. Гарантированную
        /// элиту забега берёт E08 или E08T, и никогда обе (ArenaRunPlan).
        /// </summary>
        public static readonly ArenaEncounterTemplate E08T = new ArenaEncounterTemplate("forest.E08T",
            ArenaEncounterType.Elite, 5, 7, 3, EnemyKind.ForestThorncaster, new[]
            {
                Wave(WaveTrigger.Start, Thorncaster(Center), Swarm(2, 3, Front)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(2, 2, Flank), Swarm(2, 3, Back)),
            }, tier: Hard);

        /// <summary>
        /// E13 — охота вендиго: вендиго со стрелками; подмога — только когда
        /// арена пуста: на поздней арене удар вендиго и без толпы крупный.
        /// </summary>
        public static readonly ArenaEncounterTemplate E13 = new ArenaEncounterTemplate("forest.E13",
            ArenaEncounterType.Elite, 7, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Wendigo(Center), Bud(1, 2, Front), Swarm(2, 3, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 2, Flank), Swarm(2, 3, Back)),
            }, tier: Hard);

        /// <summary>
        /// E14 — поле шипов: Шипомёт с хранителями и, может быть, плодом —
        /// линия шипов режет арену, пока хранители жмут; когда арена пуста —
        /// хранители и рой. Урока нет: только после E08T, и значит, без вендиго.
        /// </summary>
        public static readonly ArenaEncounterTemplate E14 = new ArenaEncounterTemplate("forest.E14",
            ArenaEncounterType.Elite, 7, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Thorncaster(Center), Guardian(1, 2, Front), Bud(0, 1, Back)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 2, Flank), Swarm(2, 4, Back)),
            }, tier: Hard);

        /// <summary>
        /// E24 — шипы и корни, вторая элита Шипомёта: линия шипов режет арену,
        /// пока круг Корнехвата держит героя на месте; когда арена пуста —
        /// хранитель, может быть, ещё Корнехват и рой. Только после уроков
        /// Шипомёта и Корнехвата. Подгонка 29.09: во второй волне хранитель
        /// один (было 1–2) — А8 шла 87 с при окне 60–80; теперь 78 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E24 = new ArenaEncounterTemplate("forest.E24",
            ArenaEncounterType.Elite, 7, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Thorncaster(Center), Snarer(1, 1, Flank), Swarm(2, 3, Front)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 1, Flank), Snarer(0, 1, Back), Swarm(2, 3, Back)),
            }, tier: Hard);

        /// <summary>
        /// E25 — вендиго и раскол, вторая элита вендиго: Расщепень впереди, и
        /// его дети путаются под ногами, пока вендиго заходит сбоку; когда
        /// арена пуста — хранитель, плод и рой. Только после уроков вендиго и
        /// Расщепеня. Подгонка 29.09: во второй волне хранитель один (было
        /// 1–2) — А7–А8 шли 80–88 с при окнах 60–80; теперь 72–81 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E25 = new ArenaEncounterTemplate("forest.E25",
            ArenaEncounterType.Elite, 7, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Wendigo(Center), Splitter(1, 1, Front), Swarm(0, 2, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 1, Flank), Bud(1, 1, Back), Swarm(2, 3, Front)),
            }, tier: Hard);

        /// <summary>
        /// Подмога временного босса на 66% и 33% его здоровья, по разу:
        /// 4–5 роя и хранитель, встают из земли. Подгонка 29.09: было 2–3
        /// роя — с ними бой с боссом клал 1 забег из 100 дошедших (p50 — 24%
        /// здоровья), и финал ничего не решал; теперь 11 из 100, 39%.
        /// </summary>
        public static readonly EncounterWave BossAdds = Wave(WaveTrigger.AliveAtMost(64),
            Swarm(4, 5, Flank), Guardian(1, 1, Front));

        /// <summary>
        /// Пул шаблонов леса в порядке ключей. Порядок входит в бросок плана:
        /// новые ключи — только в конец.
        /// </summary>
        public static readonly ArenaEncounterTemplate[] All =
        {
            E01, E02, E03, E04, E05, E06, E07, E08, E08T, E09, E10, E11, E12, E13, E14, E15,
            E16, E17, E18, E19, E20, E21, E22, E23, E24, E25,
        };

        /// <summary>
        /// Шаблоны видов, которых ещё нет в игре (ждут арта): в All не входят,
        /// игра их не ставит, а тесты и стенд проверяют. Сейчас пусто — все
        /// мобы леса в игре; следующий новый вид (босс «Хозяин Чащи» и его
        /// свита) заводит свои шаблоны сначала здесь.
        /// </summary>
        public static readonly ArenaEncounterTemplate[] Staged = new ArenaEncounterTemplate[0];

        /// <summary>Шаблон по стабильному ключу или null: сначала All, потом Staged.</summary>
        public static ArenaEncounterTemplate Find(string key)
        {
            foreach (var template in All) if (template.Key == key) return template;
            foreach (var template in Staged) if (template.Key == key) return template;
            return null;
        }

        /// <summary>
        /// Пределы угрозы волны на арене по всем броскам: обычные группы — от
        /// Min до Max, цель — весь бюджет. Для проверки таблицы и тестов.
        /// </summary>
        public static void WaveThreatRange(ArenaEncounterTemplate template, int wave, int arena, out int min, out int max)
        {
            template.WaveBudget(arena, out int low, out int high);
            var w = template.GetWave(wave);
            min = int.MaxValue; max = 0;
            var counts = new int[w.GroupCount];
            EnumerateCore(w, 0, 0, counts, low, high, ref min, ref max);
        }

        private static void EnumerateCore(EncounterWave wave, int group, int core, int[] counts,
            int low, int high, ref int min, ref int max)
        {
            if (group == wave.GroupCount)
            {
                for (int target = low; target <= high; target++)
                {
                    int threat = core;
                    for (int g = 0; g < wave.GroupCount; g++)
                        if (wave.GetGroup(g).Fill)
                        {
                            int fill = EncounterWave.FillCount(wave.GetGroup(g), target - threat);
                            threat += fill * wave.GetGroup(g).Threat;
                        }
                    min = Math.Min(min, threat);
                    max = Math.Max(max, threat);
                }
                return;
            }
            var current = wave.GetGroup(group);
            if (current.Fill) { EnumerateCore(wave, group + 1, core, counts, low, high, ref min, ref max); return; }
            for (int n = current.Min; n <= current.Max; n++)
                EnumerateCore(wave, group + 1, core + n * current.Threat, counts, low, high, ref min, ref max);
        }
    }
}
