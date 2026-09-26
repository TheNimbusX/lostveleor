using System;

namespace Game.Sim
{
    /// <summary>
    /// Шаблоны встреч леса (стадия 6 плана «Мобы леса»), таблицей в коде.
    ///
    /// По документу владельца «Локация 1 — Лес»: 8 арен и босс; обычных 5–6
    /// из 8, засада и выживание — не больше одной за забег, одна элитная
    /// встреча на А4–А6 наверняка и вторая на А7–А8 с шансом 30%. Цена
    /// видов — Threat в EnemyArchetypes (рой 1, хранитель 2, плюй-плод 2,
    /// камнекопыт 3, вендиго 6); бюджет угроз читается на ВОЛНУ (решение от
    /// 26.09), в обычной арене с А2 — не меньше двух волн. Обычная волна
    /// держит бюджет целиком (от нижней границы до верхней), засада, выживание
    /// и элита — только потолок: у них свой процент бюджета и своя поддержка.
    ///
    /// НОВЫЙ ВИД — СНАЧАЛА ОДИН. Урок (Lesson) — единственный путь вида в
    /// забег: E01 — рой, E02 — хранитель, E04 — плюй-плод, E03 — камнекопыт,
    /// E08 — вендиго; в Staged — E06 Корнехват, E07 Расщепень, E08T Шипомёт.
    /// Сочетания (E05, E09, E10, E13, E14, E15, засада, выживание) план
    /// ставит только после уроков всех своих видов — см. ArenaRunPlan.
    ///
    /// ОТСТУП ОТ ДОКА В ДИАПАЗОНАХ. С нынешним составом (без Корнехвата,
    /// Расщепня и Шипомёта: E06/E07/E09/E10/E14) на А2–А3 три урока — E02,
    /// E03, E04 — на два места, и хотя бы один вид оставался бы без урока.
    /// Поэтому E03 — А3–А4 (в доке А3), и на деле А1–А4 всегда E01, E02,
    /// E04, E03. E04 — только А3 (в доке А2–А3): после E02 в нём можно
    /// хранителя. E05 — до А7 (в доке «А5+»): на А8 он шёл 96 с.
    ///
    /// С НОВЫМИ ВИДАМИ (пул Release, план с staged) диапазоны дока вернулись
    /// у E04 (А2–А3; на деле всё равно А3 — хранитель в нём знаком только
    /// после E02) и E05 (А5–А8), а E08 встал в новое окно первой элиты,
    /// А5–А7. E03 остался на А3–А4 — запасной вариант плана от 26.09: при А3
    /// на А4 не остаётся ни одной обычной встречи (элита с новыми видами — с
    /// А5), А3 всегда брал бы E04, А4 — засаду, и камнекопыт не встречался
    /// бы ни в одном забеге. План на 1000 сидах (26.09): рой, хранитель и
    /// плюй-плод — в каждом забеге, Расщепень — в 60%, камнекопыт и Корнехват —
    /// в 49–50%, вендиго и Шипомёт — поровну, по 49–51%; засада — в 77%.
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

        // СОСТАВ ПО СТЕНДУ (26.09). Первый прогон добирал бюджет одним роем —
        // и арены шли вдвое быстрее целей, а рой давал 78% урона по герою: укус
        // без метки не обходится, и с переносом здоровья до босса дожили 4 из 20.
        // Теперь бюджет набирают сначала хранители (до двух на волну, 275
        // здоровья на угрозу, от сектора бот уходит в 97% замахов), камнекопыт и
        // плюй-плод (150 на угрозу и почти без урона), а рой — только остаток.
        // Время арены держится здоровьем и числом волн, а не толпой.

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
            });

        /// <summary>E02 — рой и хранитель. Урок хранителя: сначала один, потом два, потом один.</summary>
        public static readonly ArenaEncounterTemplate E02 = new ArenaEncounterTemplate("forest.E02",
            ArenaEncounterType.Normal, 2, 2, 2, EnemyKind.ForestGuardian, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Front), SwarmFill(4, 6, Center)),
                Wave(WaveTrigger.AliveAtMost(2), Guardian(2, 2, Flank), SwarmFill(2, 4, Front)),
                Wave(WaveTrigger.AliveAtMost(2), Guardian(1, 1, Back), SwarmFill(4, 6, Flank)),
            });

        /// <summary>
        /// E03 — урок тарана: сначала один камнекопыт и немного роя; когда
        /// арена пуста — он же с парой хранителей.
        /// </summary>
        public static readonly ArenaEncounterTemplate E03 = new ArenaEncounterTemplate("forest.E03",
            ArenaEncounterType.Normal, 3, 4, 3, EnemyKind.ForestStonehoof, new[]
            {
                Wave(WaveTrigger.Start, Stonehoof(1, Front), SwarmFill(4, 8, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Stonehoof(1, Front), Guardian(2, 2, Flank), SwarmFill(0, 4, Center)),
            });

        // Волны E04, E05 и E08 — общие у шаблона игры и его копии для пула
        // Release: ключ, урок и состав у копии те же, другие только арены.
        private static readonly EncounterWave[] E08Waves =
        {
            Wave(WaveTrigger.Start, Wendigo(Center), Swarm(2, 3, Front)),
            Wave(WaveTrigger.AliveAtMost(0), Guardian(2, 2, Flank), Swarm(2, 3, Back)),
        };

        private static readonly EncounterWave[] E04Waves =
        {
            Wave(WaveTrigger.Start, Bud(1, 1, Front), Guardian(1, 1, Center), SwarmFill(3, 5, Center)),
            Wave(WaveTrigger.AliveAtMost(3), Bud(1, 1, Flank), Guardian(1, 1, Front), BudFill(0, 1, Back), SwarmFill(1, 5, Front)),
            Wave(WaveTrigger.AliveAtMost(3), Bud(1, 1, Back), Guardian(1, 1, Flank), SwarmFill(3, 5, Flank)),
        };

        private static readonly EncounterWave[] E05Waves =
        {
            Wave(WaveTrigger.Start, Stonehoof(1, Front), Bud(1, 1, Flank), Guardian(1, 1, Center),
                BudFill(0, 2, Flank), SwarmFill(0, 7, Center)),
            Wave(WaveTrigger.AliveAtMost(2), Stonehoof(1, Flank), Bud(1, 1, Back), Guardian(1, 1, Front),
                BudFill(0, 2, Flank), SwarmFill(0, 7, Front)),
        };

        /// <summary>
        /// E04 — урок стрелка: плюй-плод с хранителем и роем, три волны. Только
        /// А3 (в доке А2–А3): так он всегда после E02, и хранитель в нём знаком.
        /// </summary>
        public static readonly ArenaEncounterTemplate E04 = new ArenaEncounterTemplate("forest.E04",
            ArenaEncounterType.Normal, 3, 3, 2, EnemyKind.ForestBud, E04Waves);

        /// <summary>
        /// E05 — перекрёстное давление: таран, плоды с фланга, хранители, рой —
        /// остаток. До А7: на А8 две такие волны шли 96 с при цели 60–80.
        /// </summary>
        public static readonly ArenaEncounterTemplate E05 = new ArenaEncounterTemplate("forest.E05",
            ArenaEncounterType.Normal, 5, 7, 3, EnemyKind.None, E05Waves);

        /// <summary>E04 с диапазоном дока, А2–А3, — только в пуле Release.</summary>
        public static readonly ArenaEncounterTemplate E04Release = new ArenaEncounterTemplate("forest.E04",
            ArenaEncounterType.Normal, 2, 3, 2, EnemyKind.ForestBud, E04Waves);

        /// <summary>E05 с диапазоном дока, А5–А8, — только в пуле Release.</summary>
        public static readonly ArenaEncounterTemplate E05Release = new ArenaEncounterTemplate("forest.E05",
            ArenaEncounterType.Normal, 5, 8, 3, EnemyKind.None, E05Waves);

        /// <summary>
        /// E08 — первая элита: вендиго с парой роя — урок вендиго; когда арена
        /// пуста, из земли встаёт небольшая подмога. Подмога ПОСЛЕ вендиго, а не
        /// к нему: с ней в бою вендиго на А5–А6 клал половину забегов с переносом
        /// здоровья (стенд, 26.09).
        /// </summary>
        public static readonly ArenaEncounterTemplate E08 = new ArenaEncounterTemplate("forest.E08",
            ArenaEncounterType.Elite, 4, 6, 3, EnemyKind.ForestWendigo, E08Waves);

        /// <summary>
        /// E08 в окне первой элиты релиза, А5–А7, — только в пуле Release. С
        /// А4–А6 на А7 из первых элит вставал бы один E08T, и Шипомёт выпадал
        /// бы втрое чаще вендиго (1000 сидов: 78% против 22%); с А5–А7 — поровну.
        /// </summary>
        public static readonly ArenaEncounterTemplate E08Release = new ArenaEncounterTemplate("forest.E08",
            ArenaEncounterType.Elite, 5, 7, 3, EnemyKind.ForestWendigo, E08Waves);

        /// <summary>
        /// E11 — засада: приманка (рой и хранитель), потом две волны с парой
        /// хранителей за спиной и спереди, рой — с флангов. Срок волны — 8 с.
        /// </summary>
        public static readonly ArenaEncounterTemplate E11 = new ArenaEncounterTemplate("forest.E11",
            ArenaEncounterType.Ambush, 4, 7, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Swarm(3, 3, Center), Guardian(1, 1, Front)),
                Wave(WaveTrigger.AliveAtMost(2, AmbushNextWaveTicks), Guardian(2, 2, Back), Bud(1, 1, Front), SwarmFill(1, 6, Flank)),
                Wave(WaveTrigger.AliveAtMost(2, AmbushNextWaveTicks), Guardian(2, 2, Front), Bud(1, 1, Back), SwarmFill(1, 6, Flank)),
            }, budgetPercent: 70);

        /// <summary>
        /// E12 — выживание 60 с: волна каждые 11 с (раньше, если арена
        /// пуста), по таймеру оставшиеся уходят в землю. 40% бюджета на волну,
        /// и в каждой — кто-то плотный: иначе минута — это минута роя.
        /// </summary>
        public static readonly ArenaEncounterTemplate E12 = new ArenaEncounterTemplate("forest.E12",
            ArenaEncounterType.Survival, 5, 7, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Guardian(1, 1, Front), SwarmFill(2, 4, Center)),
                Wave(WaveTrigger.AtTick(SurvivalWaveTicks), Bud(1, 1, Back), Guardian(1, 1, Flank), SwarmFill(0, 2, Flank)),
                Wave(WaveTrigger.AtTick(2 * SurvivalWaveTicks), Guardian(1, 1, Front), SwarmFill(2, 4, Center)),
                Wave(WaveTrigger.AtTick(3 * SurvivalWaveTicks), Stonehoof(1, Front), SwarmFill(1, 3, Back)),
                Wave(WaveTrigger.AtTick(4 * SurvivalWaveTicks), Guardian(1, 1, Front), Bud(1, 1, Flank),
                    SwarmFill(0, 2, Center)),
            }, budgetPercent: 40, survivalTicks: SurvivalTicks);

        /// <summary>
        /// E13 — охота вендиго: вендиго со стрелками; подмога — только когда
        /// арена пуста: на поздней арене удар вендиго и без толпы крупный.
        /// </summary>
        public static readonly ArenaEncounterTemplate E13 = new ArenaEncounterTemplate("forest.E13",
            ArenaEncounterType.Elite, 7, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Wendigo(Center), Bud(1, 2, Front), Swarm(2, 3, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Guardian(1, 2, Flank), Swarm(2, 3, Back)),
            });

        /// <summary>
        /// E15 — перед боссом: четыре знакомых вида, ничего нового. Бюджет
        /// добирают плоды, а не рой: к А8 герой приходит с тем, что осталось.
        /// 70% бюджета на волну — см. «отступ по бюджету» выше.
        /// </summary>
        public static readonly ArenaEncounterTemplate E15 = new ArenaEncounterTemplate("forest.E15",
            ArenaEncounterType.Normal, 8, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Guardian(2, 2, Front), Bud(1, 1, Back), Stonehoof(1, Flank),
                    BudFill(0, 3, Back), SwarmFill(0, 9, Center)),
                Wave(WaveTrigger.AliveAtMost(4), Guardian(0, 1, Flank), Bud(1, 1, Front), Stonehoof(1, Back),
                    BudFill(0, 3, Front), SwarmFill(0, 9, Center)),
            }, budgetPercent: 70);

        // ---------- Staged: новые мобы леса (план от 26.09) ----------
        //
        // Шаблоны Корнехвата, Расщепня и Шипомёта ждут арта своих мобов и в All
        // не входят: игра их пока не ставит. Тесты и стенд баланса
        // (ARENA_BENCH_STAGED=1) гоняют All + Staged, план с ними — пул Release
        // и ArenaRunPlan.Roll(..., staged: true): там же правила элит релиза
        // (первая элита на А5–А7, Вендиго и Шипомёт не в одном забеге). Моб
        // получил арт и сыгран владельцем — его шаблоны переезжают в All.
        //
        // Угроза видов: Корнехват 3, Расщепень 4 вместе с детьми распада,
        // Шипомёт 6 (элита, как вендиго). Не больше двух хранителей на волну —
        // то же правило владельца.

        /// <summary>
        /// E06 — урок корней: Корнехват с хранителем и роем; когда живых не
        /// больше одного — второй Корнехват с хранителями. Только А5.
        /// </summary>
        public static readonly ArenaEncounterTemplate E06 = new ArenaEncounterTemplate("forest.E06",
            ArenaEncounterType.Normal, 5, 5, 2, EnemyKind.ForestRootSnarer, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Front), Guardian(1, 1, Center), SwarmFill(5, 7, Center)),
                Wave(WaveTrigger.AliveAtMost(1), Snarer(1, 1, Back), Guardian(1, 2, Front), SwarmFill(3, 7, Flank)),
            });

        /// <summary>
        /// E07 — урок раскола: Расщепень с хранителем и роем, 70% бюджета —
        /// распад добавляет двух детей на каждого, и урок не тонет в толпе.
        /// Вторая волна — только на пустой арене: дети считаются живыми, и
        /// порог выше нуля выпускал бы её прямо на детей первой. Только А6.
        /// </summary>
        public static readonly ArenaEncounterTemplate E07 = new ArenaEncounterTemplate("forest.E07",
            ArenaEncounterType.Normal, 6, 6, 2, EnemyKind.ForestSplitter, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Guardian(1, 1, Center), SwarmFill(0, 4, Flank)),
                Wave(WaveTrigger.AliveAtMost(0), Splitter(1, 1, Flank), Guardian(0, 1, Front), SwarmFill(0, 4, Back)),
            }, budgetPercent: 70);

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
            });

        /// <summary>
        /// E09 — точка и линия: круг Корнехвата под героем, пока камнекопыт
        /// целит таран, а плод бьёт сверху; во второй волне вместо тарана —
        /// хранители. Рой — остаток бюджета.
        /// </summary>
        public static readonly ArenaEncounterTemplate E09 = new ArenaEncounterTemplate("forest.E09",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Snarer(1, 1, Flank), Stonehoof(1, Front), Bud(1, 1, Back), SwarmFill(4, 10, Center)),
                Wave(WaveTrigger.AliveAtMost(2), Snarer(1, 1, Back), Guardian(1, 2, Front), Bud(1, 1, Flank),
                    SwarmFill(3, 11, Center)),
            });

        /// <summary>
        /// E10 — раскол под давлением: Расщепень под плодом и с хранителем,
        /// рой — остаток. Дети распада считаются живыми и держат порог второй
        /// волны: она не выходит, пока их не добили.
        /// </summary>
        public static readonly ArenaEncounterTemplate E10 = new ArenaEncounterTemplate("forest.E10",
            ArenaEncounterType.Normal, 6, 8, 3, EnemyKind.None, new[]
            {
                Wave(WaveTrigger.Start, Splitter(1, 1, Front), Bud(1, 1, Back), Guardian(1, 1, Center), SwarmFill(4, 10, Flank)),
                Wave(WaveTrigger.AliveAtMost(2), Splitter(1, 1, Flank), Bud(1, 1, Front), Guardian(0, 1, Back),
                    SwarmFill(4, 12, Center)),
            });

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
            });

        /// <summary>
        /// Подмога временного босса на 66% и 33% его здоровья, по разу:
        /// 2–3 роя и хранитель, встают из земли.
        /// </summary>
        public static readonly EncounterWave BossAdds = Wave(WaveTrigger.AliveAtMost(64),
            Swarm(2, 3, Flank), Guardian(1, 1, Front));

        /// <summary>Пул шаблонов леса в порядке ключей. Порядок входит в бросок плана.</summary>
        public static readonly ArenaEncounterTemplate[] All = { E01, E02, E03, E04, E05, E08, E11, E12, E13, E15 };

        /// <summary>
        /// Шаблоны новых видов, которых ещё нет в игре: в All не входят. У
        /// каждого вида — ровно один урок по All + Staged.
        /// </summary>
        public static readonly ArenaEncounterTemplate[] Staged = { E06, E07, E08T, E09, E10, E14 };

        /// <summary>
        /// Пул плана с новыми видами (ArenaRunPlan.Roll(..., staged: true)): All +
        /// Staged в порядке ключей; E04, E05 и E08 — копии с теми же ключами и
        /// другими аренами, см. «с новыми видами» выше.
        /// </summary>
        public static readonly ArenaEncounterTemplate[] Release =
        {
            E01, E02, E03, E04Release, E05Release, E06, E07, E08Release, E08T, E09, E10, E11, E12, E13, E14, E15,
        };

        /// <summary>Пул плана: игры (All) или с новыми видами (Release).</summary>
        public static ArenaEncounterTemplate[] Pool(bool staged) => staged ? Release : All;

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
