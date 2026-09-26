using System;

namespace Game.Sim
{
    /// <summary>
    /// Тип встречи арены — по документу владельца «Локация 1 — Лес».
    /// Значения сериализуются в хеш плана забега: новые только в конец.
    /// </summary>
    public enum ArenaEncounterType : byte
    {
        /// <summary>Обычная: 5–6 из 8 арен, с А2 — две волны.</summary>
        Normal = 0,

        /// <summary>Засада: 2–3 короткие волны без долгих пауз, не больше одной за забег.</summary>
        Ambush = 1,

        /// <summary>Выживание: ~60 с, волны по таймеру, добивать всех не обязательно.</summary>
        Survival = 2,

        /// <summary>Элитная: одна элита и небольшая поддержка.</summary>
        Elite = 3,

        /// <summary>Босс: временный Хранитель и волны подмоги на 66% и 33%.</summary>
        Boss = 4,
    }

    /// <summary>
    /// Где встаёт группа волны. Направления считаются по оси арены — от входа
    /// к выходу — от места героя в миг появления волны: «спереди» — ближе к
    /// выходу, «сзади» — к входу, «с фланга» — поперёк оси.
    /// </summary>
    public enum WavePlacement : byte
    {
        Front = 0,
        Back = 1,
        Flank = 2,
        Center = 3,
    }

    public enum WaveTriggerKind : byte
    {
        /// <summary>Волна стоит на арене с самого начала. Только первая.</summary>
        Start = 0,

        /// <summary>Живых врагов арены осталось не больше порога.</summary>
        AliveAtMost = 1,

        /// <summary>Прошло столько тиков от начала встречи.</summary>
        AtTick = 2,
    }

    /// <summary>
    /// Условие выхода волны. Любая волна, кроме стартовой, выходит не раньше
    /// чем через Simulation.EmergeTicks после предыдущей — чтобы две волны не
    /// слипались в одну; и сразу, если на арене не осталось живых, — чтобы
    /// между волнами не было пустых пауз.
    /// </summary>
    public readonly struct WaveTrigger
    {
        public readonly WaveTriggerKind Kind;

        /// <summary>AliveAtMost: порог живых.</summary>
        public readonly int Alive;

        /// <summary>
        /// AtTick: тик от начала встречи. AliveAtMost: крайний срок в тиках
        /// после прошлой волны (засада не ждёт, пока добьют всех); 0 — без срока.
        /// </summary>
        public readonly int Ticks;

        private WaveTrigger(WaveTriggerKind kind, int alive, int ticks)
        {
            if (alive < 0 || alive > 64 || ticks < 0 || ticks > 100000)
                throw new ArgumentException("Invalid wave trigger.");
            Kind = kind; Alive = alive; Ticks = ticks;
        }

        public static WaveTrigger Start => new WaveTrigger(WaveTriggerKind.Start, 0, 0);

        public static WaveTrigger AliveAtMost(int alive, int orAfterTicks = 0)
            => new WaveTrigger(WaveTriggerKind.AliveAtMost, alive, orAfterTicks);

        public static WaveTrigger AtTick(int tick) => new WaveTrigger(WaveTriggerKind.AtTick, 0, tick);
    }

    /// <summary>
    /// Группа одного вида в волне. Здоровье и урон — строка вида в
    /// EnemyArchetypes, выросшая с глубиной; подстройки группы нет.
    ///
    /// FILL — ДОБОР ДО БЮДЖЕТА. Число такой группы не бросается, а считается:
    /// сколько её угроз не хватает остальной волне до цели, выпавшей из
    /// бюджета арены, в рамках Min..Max. Так одна строка шаблона живёт и на
    /// А5, и на А8 — с разным числом Корнеползов.
    /// </summary>
    public readonly struct WaveGroup
    {
        public readonly EnemyKind Kind;
        public readonly int Min, Max;
        public readonly WavePlacement Placement;

        /// <summary>Элита встречи: опыт и добыча элиты. Сейчас это только Вендиго.</summary>
        public readonly bool Elite;

        public readonly bool Fill;

        public WaveGroup(EnemyKind kind, int min, int max, WavePlacement placement,
            bool elite = false, bool fill = false)
        {
            // Детёныш Расщепеня в волны не ставится: он встаёт только из распада.
            if (!EnemyArchetypes.IsPlaceable(kind) || min < 0 || max < min || max > 16 || (elite && max != 1))
                throw new ArgumentException("Invalid wave group.");
            Kind = kind; Min = min; Max = max; Placement = placement; Elite = elite; Fill = fill;
        }

        public int Threat => EnemyArchetypes.Get(Kind).Threat;
    }

    public sealed class EncounterWave
    {
        public readonly WaveTrigger Trigger;
        private readonly WaveGroup[] _groups;
        public int GroupCount => _groups.Length;
        public WaveGroup GetGroup(int index) => _groups[index];

        public EncounterWave(WaveTrigger trigger, params WaveGroup[] groups)
        {
            if (groups == null || groups.Length == 0 || groups.Length > 6)
                throw new ArgumentException("A wave needs 1–6 groups.");
            int guaranteed = 0, guardians = 0;
            foreach (var g in groups)
            {
                guaranteed += g.Max;
                if (g.Kind == EnemyKind.ForestGuardian) guardians += g.Max;
            }
            if (guaranteed == 0) throw new ArgumentException("A wave must be able to hold an enemy.");
            // Правило владельца: не больше двух Хранителей в одной пачке — в волне.
            if (guardians > MaxGuardians) throw new ArgumentException("More than two Forest Guardians in a wave.");
            Trigger = trigger;
            _groups = (WaveGroup[])groups.Clone();
        }

        /// <summary>Правило владельца: не больше двух Лесных хранителей в одной волне.</summary>
        public const int MaxGuardians = 2;

        /// <summary>Мест в пуле сущностей на волну: особи × тела (Расщепень — сам и дети).</summary>
        public int MaxEnemies
        {
            get
            {
                int count = 0;
                foreach (var g in _groups) count += g.Max * EnemyArchetypes.BodiesPerSpawn(g.Kind);
                return count;
            }
        }

        /// <summary>
        /// Числа групп волны: сначала бросаются обычные группы (по порядку),
        /// потом добор считает, сколько угроз не хватает до цели. Возвращает
        /// угрозу волны. Бросков ровно столько, сколько обычных групп, — цель
        /// бросает вызывающий.
        /// </summary>
        public int RollCounts(int targetThreat, ref Pcg32 rng, int[] counts)
        {
            int threat = 0;
            for (int g = 0; g < _groups.Length; g++)
            {
                if (_groups[g].Fill) continue;
                counts[g] = rng.NextInt(_groups[g].Min, _groups[g].Max + 1);
                threat += counts[g] * _groups[g].Threat;
            }
            for (int g = 0; g < _groups.Length; g++)
            {
                if (!_groups[g].Fill) continue;
                counts[g] = FillCount(_groups[g], targetThreat - threat);
                threat += counts[g] * _groups[g].Threat;
            }
            return threat;
        }

        /// <summary>Сколько особей добора закрывают недостачу угроз, в рамках группы.</summary>
        public static int FillCount(in WaveGroup group, int missingThreat)
        {
            int count = missingThreat > 0 ? missingThreat / group.Threat : 0;
            return count < group.Min ? group.Min : count > group.Max ? group.Max : count;
        }
    }

    /// <summary>
    /// Шаблон встречи арены: тип, где он допустим, и волны. Таблица шаблонов
    /// леса — ForestEncounterTemplates; что на какой арене — ArenaRunPlan.
    /// </summary>
    public sealed class ArenaEncounterTemplate
    {
        /// <summary>Стабильный ключ ('forest.E01'); из него же Id для хеша.</summary>
        public readonly string Key;
        public readonly int Id;
        public readonly ArenaEncounterType Type;

        /// <summary>Номера арен, на которых шаблон допустим (А1 = 1).</summary>
        public readonly int MinArena, MaxArena;

        /// <summary>Наименьшая арена (ArenaRouteOffer.Size): таран и кольцо воя тесны на малой.</summary>
        public readonly int MinArenaSize;
        public readonly int Weight;

        /// <summary>
        /// Урок: вид, который этот шаблон показывает впервые, одним и среди
        /// знакомых. Шаблон с уроком — единственный путь нового вида в забег;
        /// None — шаблон только из уже знакомых видов.
        /// </summary>
        public readonly EnemyKind Lesson;

        /// <summary>
        /// Доля бюджета угроз арены на одну волну, %. Обычные и элитные — 100;
        /// у засады и выживания волн больше и они короче.
        /// </summary>
        public readonly int BudgetPercent;

        /// <summary>Длина выживания в тиках; 0 — не выживание.</summary>
        public readonly int SurvivalTicks;

        private readonly EncounterWave[] _waves;
        public int WaveCount => _waves.Length;
        public EncounterWave GetWave(int index) => _waves[index];

        public ArenaEncounterTemplate(string key, ArenaEncounterType type, int minArena, int maxArena,
            int minArenaSize, EnemyKind lesson, EncounterWave[] waves, int weight = 100,
            int budgetPercent = 100, int survivalTicks = 0)
        {
            if (string.IsNullOrWhiteSpace(key) || type == ArenaEncounterType.Boss || minArena < 1
                || maxArena < minArena || minArenaSize < 2 || minArenaSize > 4 || weight < 1 || weight > 10000
                || budgetPercent < 10 || budgetPercent > 200 || survivalTicks < 0
                || (survivalTicks > 0) != (type == ArenaEncounterType.Survival)
                || waves == null || waves.Length == 0 || waves.Length > 8)
                throw new ArgumentException("Invalid arena encounter template: " + key);
            for (int w = 0; w < waves.Length; w++)
            {
                if (waves[w] == null) throw new ArgumentException(key + ": missing wave.");
                if ((waves[w].Trigger.Kind == WaveTriggerKind.Start) != (w == 0))
                    throw new ArgumentException(key + ": only the first wave starts with the arena.");
            }
            Key = key; Id = StableId.Of(key); Type = type; MinArena = minArena; MaxArena = maxArena;
            MinArenaSize = minArenaSize; Weight = weight; Lesson = lesson; BudgetPercent = budgetPercent;
            SurvivalTicks = survivalTicks; _waves = (EncounterWave[])waves.Clone();
            if (lesson != EnemyKind.None && !Uses(lesson)) throw new ArgumentException(key + ": the lesson kind is absent.");
        }

        public bool AllowsArena(int arena) => arena >= MinArena && arena <= MaxArena;

        /// <summary>Встречается ли вид хоть в одной волне — для урока и прогрева пулов вида.</summary>
        public bool Uses(EnemyKind kind)
        {
            foreach (var wave in _waves)
                for (int g = 0; g < wave.GroupCount; g++)
                    if (wave.GetGroup(g).Kind == kind && wave.GetGroup(g).Max > 0) return true;
            return false;
        }

        public bool HasElite
        {
            get
            {
                foreach (var wave in _waves)
                    for (int g = 0; g < wave.GroupCount; g++)
                        if (wave.GetGroup(g).Elite) return true;
                return false;
            }
        }

        public int MaxEnemies
        {
            get
            {
                int count = 0;
                foreach (var wave in _waves) count += wave.MaxEnemies;
                return count;
            }
        }

        /// <summary>
        /// Бюджет угроз одной волны на арене arena: бюджет леса × BudgetPercent,
        /// с округлением до ближайшего и не меньше единицы.
        /// </summary>
        public void WaveBudget(int arena, out int min, out int max)
        {
            min = Math.Max(1, (ForestEncounterTemplates.BudgetMin(arena) * BudgetPercent + 50) / 100);
            max = Math.Max(min, (ForestEncounterTemplates.BudgetMax(arena) * BudgetPercent + 50) / 100);
        }
    }
}
