using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// План встреч забега: какой шаблон на какой арене. Бросается ОДИН РАЗ на
    /// старте забега из мастер-сида симуляции собственным потоком — Layout,
    /// Spawns и Loot не сдвигаются, сиды уровней те же (RiftLevelSeeds), —
    /// и входит в хеш забега.
    ///
    /// ЛЕСТНИЦА С ВЕСАМИ (решение владельца от 29.09). Сначала бросается
    /// «форма» забега, потом она заполняется шаблонами:
    ///  1. элитные арены: первая — ровно одна на А5–А7 (веса 30/30/40), вторая —
    ///     с шансом 30%, после первой и на А7–А8 (окна делят А7, но одна
    ///     арена — одна встреча), иначе элит больше нет;
    ///  2. уровень каждой прочей арены по лестнице (TierWeight): А1 лёгкая;
    ///     А2 лёгкая 60 / средняя 40; А3–А5 средние; А6 средняя 50 / тяжёлая 50;
    ///     А7–А8 тяжёлые;
    ///  3. особые арены — засада или выживание: при одной элите одна (80%) или
    ///     две (20%), при двух элитах ни одной или одна (50/50); встают на
    ///     арены, уровню которых в пуле есть особый шаблон. В лесу из 8 арен
    ///     обычных встреч так всегда 5–6, как в документе.
    /// Заполнение — перебор с возвратом: элитной арене — элитный шаблон,
    /// особой — засада или выживание её уровня, обычной — обычный шаблон её
    /// уровня. Правила (документ владельца «Локация 1 — Лес», стадия 6 плана):
    ///  • шаблон стоит только на своих аренах, шаблоны не повторяются;
    ///  • новый вид приходит только уроком, сочетания — после уроков своих
    ///    видов. Уроков у вида может быть несколько, по уровням: кто впервые
    ///    встретил Корнехвата на тяжёлой арене, встретил его в тяжёлом уроке;
    ///  • Вендиго и Шипомёт не встречаются в одном забеге;
    ///  • засада и выживание — не больше чем по одной.
    /// Порядок кандидатов арены — перестановка по весам шаблонов. Если форма не
    /// заполняется (короткая тестовая локация, чужой пул), правила снимаются
    /// по очереди: сначала выпавшие уровни и особые арены (элитные слоты
    /// остаются, уровни — в пределах лестницы), потом и элитные слоты и
    /// лестница, но не повтор, урок и арены шаблона.
    ///
    /// До лестницы (27.09–29.09) элитная арена и особые встречи выходили из
    /// самой перестановки: первая элита вставала на А5/А6/А7 в 31/14/55%
    /// забегов, засада — в 77%. Теперь их места бросаются явно.
    /// </summary>
    public sealed class ArenaRunPlan
    {
        /// <summary>Шанс второй элитной встречи на А7–А8, %.</summary>
        public const int SecondEliteChancePercent = 30;

        /// <summary>Первая элита — ровно одна на этих аренах.</summary>
        public const int FirstEliteMinArena = 5, FirstEliteMaxArena = 7;

        /// <summary>Вторая элита — только здесь и только после первой.</summary>
        public const int SecondEliteMinArena = 7, SecondEliteMaxArena = 8;

        /// <summary>Обычных встреч в лесу из восьми арен.</summary>
        public const int MinNormal = 5, MaxNormal = 6;

        // Лестница владельца: веса уровней пачки по аренам — {лёгкая, средняя, тяжёлая}.
        private static readonly int[][] Ladder =
        {
            new[] { 100,   0,   0 },   // А1
            new[] {  60,  40,   0 },   // А2
            new[] {   0, 100,   0 },   // А3
            new[] {   0, 100,   0 },   // А4
            new[] {   0, 100,   0 },   // А5
            new[] {   0,  50,  50 },   // А6
            new[] {   0,   0, 100 },   // А7
            new[] {   0,   0, 100 },   // А8
        };

        /// <summary>Веса арены первой элиты: А5, А6, А7.</summary>
        private static readonly int[] FirstEliteWeights = { 30, 30, 40 };

        // Веса числа особых арен (засада, выживание) по числу элит забега:
        // [элит][особых 0, 1, 2]. Без элит — короткая локация.
        private static readonly int[][] SpecialCounts =
        {
            new[] { 50, 50,  0 },
            new[] {  0, 80, 20 },
            new[] { 50, 50,  0 },
        };

        private const ulong PlanStream = 0x504C414E454E43UL;   // "PLANENC"
        private const ulong ExtraStream = 0x504C414E585452UL;  // "PLANXTR"

        private readonly ArenaEncounterTemplate[] _levels;
        private readonly bool[] _boss;
        private readonly ArenaEncounterTemplate[] _pool;
        private readonly ulong _seed;

        /// <summary>Выпал ли шанс второй элиты (план мог её и не вместить).</summary>
        public readonly bool SecondEliteRolled;

        /// <summary>
        /// Сколько правил сняли, чтобы план сложился: 0 — ни одного (лес всегда
        /// так), 1 — без выпавших уровней и особых арен, 2 — и без элитных
        /// слотов, 3 — последняя страховка без правил.
        /// </summary>
        public readonly int Relaxed;

        public int LevelCount => _levels.Length;

        /// <summary>Уровень босса: шаблона нет, встречу ставит SetupBossArena.</summary>
        public bool IsBoss(int depth) => depth >= 1 && depth <= _boss.Length && _boss[depth - 1];

        private ArenaRunPlan(ArenaEncounterTemplate[] levels, bool[] boss, ArenaEncounterTemplate[] pool,
            ulong seed, bool secondElite, int relaxed)
        {
            _levels = levels; _boss = boss; _pool = pool; _seed = seed; SecondEliteRolled = secondElite;
            Relaxed = relaxed;
        }

        /// <summary>Вес уровня пачки на арене по лестнице, %; дальше восьмой — как на восьмой.</summary>
        public static int TierWeight(int arena, EncounterTier tier)
            => Ladder[ForestEncounterTemplates.ClampArena(arena) - 1][(int)tier];

        /// <summary>Вес арены первой элиты; вне окна А5–А7 — ноль.</summary>
        public static int FirstEliteWeight(int arena)
            => arena >= FirstEliteMinArena && arena <= FirstEliteMaxArena ? FirstEliteWeights[arena - FirstEliteMinArena] : 0;

        /// <summary>Вес числа особых арен count при elites элитах забега.</summary>
        public static int SpecialCountWeight(int elites, int count)
            => elites >= 0 && elites < SpecialCounts.Length && count >= 0 && count < SpecialCounts[elites].Length
                ? SpecialCounts[elites][count] : 0;

        /// <summary>Особая встреча — засада или выживание: не больше одной каждой за забег.</summary>
        public static bool IsSpecial(ArenaEncounterType type)
            => type == ArenaEncounterType.Ambush || type == ArenaEncounterType.Survival;

        /// <summary>
        /// Вендиго и Шипомёт — соперники: шаблоны с ними не стоят в одном
        /// забеге (и один шаблон не держит обоих).
        /// </summary>
        public static bool Rivals(ArenaEncounterTemplate a, ArenaEncounterTemplate b)
            => (a.Uses(EnemyKind.ForestWendigo) && b.Uses(EnemyKind.ForestThorncaster))
               || (a.Uses(EnemyKind.ForestThorncaster) && b.Uses(EnemyKind.ForestWendigo));

        /// <summary>
        /// Шаблон арены глубины depth (с единицы); null — уровень босса.
        /// Дальше конца плана (бесконечная локация) — взвешенный бросок среди
        /// обычных и элитных шаблонов восьмой арены своим потоком от глубины.
        /// </summary>
        public ArenaEncounterTemplate TemplateFor(int depth)
        {
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth));
            if (depth <= _levels.Length) return _levels[depth - 1];
            var rng = new Pcg32(_seed ^ unchecked((ulong)depth * 0x9E3779B97F4A7C15UL), ExtraStream);
            int arena = ForestEncounterTemplates.ClampArena(depth), total = 0;
            foreach (var t in _pool) if (Endless(t, arena)) total += t.Weight;
            if (total == 0) return _pool[0];
            int roll = rng.NextInt(0, total);
            foreach (var t in _pool)
            {
                if (!Endless(t, arena)) continue;
                roll -= t.Weight;
                if (roll < 0) return t;
            }
            return _pool[0];
        }

        private static bool Endless(ArenaEncounterTemplate t, int arena)
            => t.AllowsArena(arena) && (t.Type == ArenaEncounterType.Normal || t.Type == ArenaEncounterType.Elite);

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, _levels.Length);
            Hashing.Mix(ref hash, SecondEliteRolled ? 1 : 0);
            for (int i = 0; i < _levels.Length; i++)
                Hashing.Mix(ref hash, _boss[i] ? -1 : _levels[i] != null ? _levels[i].Id : 0);
        }

        /// <summary>
        /// План леса для локации: уровни с боссом — босс, прочие — арены по
        /// порядку, шаблоны — ForestEncounterTemplates.All.
        /// </summary>
        public static ArenaRunPlan Roll(ulong seed, LocationDefinition location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            var boss = new bool[location.LevelCount];
            for (int i = 0; i < boss.Length; i++) boss[i] = location.GetLevel(i + 1).Boss;
            return Roll(seed, boss, ForestEncounterTemplates.All);
        }

        /// <summary>
        /// Бросок плана: boss[i] — уровень i+1 с боссом. Номер арены шаблона —
        /// номер уровня, как глубина забега; пул задаёт вызывающий.
        /// </summary>
        public static ArenaRunPlan Roll(ulong seed, bool[] boss, ArenaEncounterTemplate[] pool)
        {
            if (boss == null || pool == null || pool.Length == 0) throw new ArgumentException("A plan needs levels and templates.");
            var rng = new Pcg32(seed, PlanStream);
            bool secondElite = rng.NextInt(0, 100) < SecondEliteChancePercent;
            var shape = new Shape(boss, pool, secondElite, ref rng);
            var levels = new ArenaEncounterTemplate[boss.Length];
            // Правила снимаются по очереди, пока план не сложится: 0 — все,
            // 1 — без выпавших уровней и особых арен, 2 — ещё и без элитных слотов.
            for (int relax = 0; relax <= 2; relax++)
            {
                var search = new Search(pool, boss, shape, relax, rng);
                if (search.Run(levels))
                    return new ArenaRunPlan(levels, (bool[])boss.Clone(), pool, seed, secondElite, relax);
            }
            // Сюда не доходит ни один лес с уроком на А1; последняя страховка —
            // первый допустимый шаблон на каждой арене без прочих правил.
            for (int i = 0; i < boss.Length; i++)
            {
                levels[i] = null;
                if (boss[i]) continue;
                foreach (var t in pool)
                    if (t.AllowsArena(ForestEncounterTemplates.ClampArena(i + 1))) { levels[i] = t; break; }
                if (levels[i] == null) levels[i] = pool[0];
            }
            return new ArenaRunPlan(levels, (bool[])boss.Clone(), pool, seed, secondElite, 3);
        }

        /// <summary>Форма забега: элитные и особые арены и уровень каждой прочей.</summary>
        private sealed class Shape
        {
            public readonly bool[] Elite, Special;
            public readonly EncounterTier[] Tier;

            public Shape(bool[] boss, ArenaEncounterTemplate[] pool, bool secondElite, ref Pcg32 rng)
            {
                int count = boss.Length, elites = 0, arenas = 0;
                Elite = new bool[count]; Special = new bool[count]; Tier = new EncounterTier[count];
                for (int i = 0; i < count; i++) if (!boss[i]) arenas++;

                // Первая элита — по весам среди арен окна, что есть в локации;
                // вторая — поровну среди арен А7–А8 после неё.
                int first = PickArena(boss, FirstEliteMinArena, FirstEliteMaxArena, true, ref rng);
                if (first > 0)
                {
                    Elite[first - 1] = true; elites++;
                    int second = secondElite
                        ? PickArena(boss, Math.Max(first + 1, SecondEliteMinArena), SecondEliteMaxArena, false, ref rng) : 0;
                    if (second > 0) { Elite[second - 1] = true; elites++; }
                }

                // Уровень каждой прочей арены — по лестнице.
                for (int i = 0; i < count; i++)
                {
                    if (boss[i] || Elite[i]) continue;
                    int total = 0;
                    for (int t = 0; t <= (int)EncounterTier.Hard; t++) total += TierWeight(i + 1, (EncounterTier)t);
                    int roll = rng.NextInt(0, total);
                    for (int t = 0; t <= (int)EncounterTier.Hard; t++)
                    {
                        roll -= TierWeight(i + 1, (EncounterTier)t);
                        if (roll < 0) { Tier[i] = (EncounterTier)t; break; }
                    }
                }

                // Особые арены: число — по весам, в лесу из восьми арен ещё и в
                // пределах «обычных 5–6»; места — поровну среди арен, уровню
                // которых есть засада или выживание.
                var eligible = new List<int>();
                for (int i = 0; i < count; i++)
                    if (!boss[i] && !Elite[i] && HasSpecial(pool, i + 1, Tier[i])) eligible.Add(i);
                int specials = 0, weights = 0;
                for (int n = 0; n <= 2; n++) weights += SpecialCountWeight(elites, n);
                if (weights > 0)
                {
                    int roll = rng.NextInt(0, weights);
                    for (int n = 0; n <= 2; n++)
                    {
                        roll -= SpecialCountWeight(elites, n);
                        if (roll < 0) { specials = n; break; }
                    }
                }
                if (arenas == ForestEncounterTemplates.ArenaCount)
                {
                    int ordinary = arenas - elites;
                    specials = Math.Max(specials, ordinary - MaxNormal);
                    specials = Math.Min(specials, ordinary - MinNormal);
                }
                specials = Math.Max(0, Math.Min(specials, eligible.Count));
                for (int k = 0; k < specials; k++)
                {
                    int j = rng.NextInt(k, eligible.Count);
                    int pick = eligible[j];
                    eligible[j] = eligible[k];
                    eligible[k] = pick;
                    Special[pick] = true;
                }
            }

            /// <summary>Арена окна min..max без босса: по весам первой элиты или поровну; 0 — нет такой.</summary>
            private static int PickArena(bool[] boss, int min, int max, bool weighted, ref Pcg32 rng)
            {
                int total = 0;
                for (int a = min; a <= max && a <= boss.Length; a++)
                    if (!boss[a - 1]) total += weighted ? FirstEliteWeight(a) : 1;
                if (total == 0) return 0;
                int roll = rng.NextInt(0, total);
                for (int a = min; a <= max && a <= boss.Length; a++)
                {
                    if (boss[a - 1]) continue;
                    roll -= weighted ? FirstEliteWeight(a) : 1;
                    if (roll < 0) return a;
                }
                return 0;
            }

            private static bool HasSpecial(ArenaEncounterTemplate[] pool, int arena, EncounterTier tier)
            {
                foreach (var t in pool)
                    if (IsSpecial(t.Type) && t.Tier == tier && t.AllowsArena(arena)) return true;
                return false;
            }
        }

        /// <summary>Перебор с возвратом. Порядок кандидатов каждой арены — взвешенная перестановка.</summary>
        private sealed class Search
        {
            private readonly ArenaEncounterTemplate[] _pool;
            private readonly bool[] _boss;
            private readonly Shape _shape;
            private readonly int _relax;
            private Pcg32 _rng;
            private readonly bool[] _used;
            private readonly List<EnemyKind> _known = new List<EnemyKind>();
            private int _steps;

            // Предохранитель: перебор мал (форма заранее, лесу хватает пары
            // десятков шагов), но план не имеет права повесить старт забега
            // на плохих данных.
            private const int MaxSteps = 20000;

            public Search(ArenaEncounterTemplate[] pool, bool[] boss, Shape shape, int relax, Pcg32 rng)
            {
                _pool = pool; _boss = boss; _shape = shape; _relax = relax; _rng = rng;
                _used = new bool[pool.Length];
            }

            public bool Run(ArenaEncounterTemplate[] levels)
            {
                for (int i = 0; i < levels.Length; i++) levels[i] = null;
                return Place(levels, 0);
            }

            private bool Place(ArenaEncounterTemplate[] levels, int index)
            {
                if (++_steps > MaxSteps) return false;
                if (index == levels.Length) return true;
                if (_boss[index]) return Place(levels, index + 1);
                // Кандидаты арены: свой слот формы, диапазон, без повтора, урок и счётчики.
                var order = new List<int>();
                var weights = new List<int>();
                for (int t = 0; t < _pool.Length; t++)
                    if (!_used[t] && Allowed(levels, index, _pool[t])) { order.Add(t); weights.Add(_pool[t].Weight); }
                // Взвешенная перестановка: вытягиваем по весу без возврата.
                while (order.Count > 0)
                {
                    int total = 0;
                    for (int k = 0; k < weights.Count; k++) total += weights[k];
                    int roll = _rng.NextInt(0, total), pick = 0;
                    for (int k = 0; k < weights.Count; k++) { roll -= weights[k]; if (roll < 0) { pick = k; break; } }
                    int t = order[pick];
                    order.RemoveAt(pick); weights.RemoveAt(pick);
                    var template = _pool[t];
                    _used[t] = true;
                    levels[index] = template;
                    bool learned = template.Lesson != EnemyKind.None && !_known.Contains(template.Lesson);
                    if (learned) _known.Add(template.Lesson);
                    if (Place(levels, index + 1)) return true;
                    if (learned) _known.Remove(template.Lesson);
                    levels[index] = null;
                    _used[t] = false;
                    if (_steps > MaxSteps) return false;
                }
                return false;
            }

            private bool Allowed(ArenaEncounterTemplate[] levels, int index, ArenaEncounterTemplate template)
            {
                int arena = index + 1;
                if (!template.AllowsArena(arena)) return false;
                bool elite = template.Type == ArenaEncounterType.Elite;
                // Слот формы: элитной арене — элита, прочим — не элита.
                if (_relax < 2 && elite != _shape.Elite[index]) return false;
                if (!elite)
                {
                    // Выпавший уровень и особая арена; снятые — хотя бы в пределах лестницы.
                    if (_relax == 0 && (template.Tier != _shape.Tier[index]
                        || IsSpecial(template.Type) != _shape.Special[index])) return false;
                    if (_relax == 1 && TierWeight(arena, template.Tier) == 0) return false;
                }
                // Урок раньше сочетаний: каждый вид шаблона уже знаком, кроме его урока.
                for (int k = 0; k < EnemyArchetypes.Count; k++)
                {
                    EnemyKind kind = EnemyArchetypes.At(k).Kind;
                    if (template.Uses(kind) && kind != template.Lesson && !_known.Contains(kind)) return false;
                }
                if (Rivals(template, template)) return false;
                for (int i = 0; i < index; i++)
                {
                    if (levels[i] == null) continue;
                    // Вендиго и Шипомёт — не в одном забеге.
                    if (Rivals(levels[i], template)) return false;
                    // Засада и выживание — не больше чем по одной.
                    if (IsSpecial(template.Type) && levels[i].Type == template.Type) return false;
                }
                return true;
            }
        }
    }
}
