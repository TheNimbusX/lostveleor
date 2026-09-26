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
    /// Правила (документ владельца «Локация 1 — Лес», стадия 6 плана):
    ///  • шаблон стоит только на своих аренах, шаблоны не повторяются;
    ///  • новый вид приходит только уроком, сочетания — после уроков своих видов;
    ///  • ровно одна элитная встреча на А4–А6; вторая — на А7–А8 с шансом
    ///    30%, иначе элит больше нет;
    ///  • засада и выживание — не больше чем по одной;
    ///  • в лесу из 8 арен обычных встреч 5–6.
    /// Правила проверяются перебором с возвратом в порядке, перемешанном по
    /// весам шаблонов: план всегда допустим, если допустимый вообще есть.
    /// Если нет (короткая тестовая локация) — правила про число элит и
    /// обычных снимаются по очереди, но не повтор, урок и арены шаблона.
    ///
    /// С НОВЫМИ ВИДАМИ (staged: пул ForestEncounterTemplates.Release, решение
    /// владельца от 26.09) — правила релиза: первая элита — ровно одна на
    /// А5–А7, вторая по броску 30% — после неё и на А7–А8 (окна делят А7, но
    /// одна арена — одна встреча). Игра бросает без staged, и её планы те же,
    /// что до новых видов. В любом пуле Вендиго и Шипомёт не встречаются в
    /// одном забеге: охота вендиго (E13) ждёт урока вендиго, поле шипов (E14) —
    /// урока Шипомёта, а два урока-элиты исключают друг друга здесь.
    /// </summary>
    public sealed class ArenaRunPlan
    {
        /// <summary>Шанс второй элитной встречи на А7–А8, %.</summary>
        public const int SecondEliteChancePercent = 30;

        /// <summary>Первая элита — ровно одна на этих аренах.</summary>
        public const int FirstEliteMinArena = 4, FirstEliteMaxArena = 6;

        /// <summary>Вторая элита — только здесь.</summary>
        public const int SecondEliteMinArena = 7, SecondEliteMaxArena = 8;

        /// <summary>Первая элита плана с новыми видами (staged) — на этих аренах.</summary>
        public const int StagedFirstEliteMinArena = 5, StagedFirstEliteMaxArena = 7;

        /// <summary>Обычных встреч в лесу из восьми арен.</summary>
        public const int MinNormal = 5, MaxNormal = 6;

        private const ulong PlanStream = 0x504C414E454E43UL;   // "PLANENC"
        private const ulong ExtraStream = 0x504C414E585452UL;  // "PLANXTR"

        private readonly ArenaEncounterTemplate[] _levels;
        private readonly bool[] _boss;
        private readonly ArenaEncounterTemplate[] _pool;
        private readonly ulong _seed;

        /// <summary>Выпал ли шанс второй элиты (план мог её и не вместить).</summary>
        public readonly bool SecondEliteRolled;

        /// <summary>План брошен по правилам релиза с новыми видами (см. выше).</summary>
        public readonly bool Staged;

        public int LevelCount => _levels.Length;

        /// <summary>Уровень босса: шаблона нет, встречу ставит SetupBossArena.</summary>
        public bool IsBoss(int depth) => depth >= 1 && depth <= _boss.Length && _boss[depth - 1];

        private ArenaRunPlan(ArenaEncounterTemplate[] levels, bool[] boss, ArenaEncounterTemplate[] pool,
            ulong seed, bool secondElite, bool staged)
        {
            _levels = levels; _boss = boss; _pool = pool; _seed = seed; SecondEliteRolled = secondElite;
            Staged = staged;
        }

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
            // Пул за концом плана (бесконечная локация) зависит от staged. Метка —
            // только у плана с новыми видами: хеш плана игры тот же, что был.
            if (Staged) Hashing.Mix(ref hash, 0x5354474EL);   // "STGN"
            for (int i = 0; i < _levels.Length; i++)
                Hashing.Mix(ref hash, _boss[i] ? -1 : _levels[i] != null ? _levels[i].Id : 0);
        }

        /// <summary>
        /// План леса для локации: уровни с боссом — босс, прочие — арены по
        /// порядку. staged — с новыми видами: пул ForestEncounterTemplates.Release
        /// и правила элит релиза; игра (RiftRun) пока бросает без него.
        /// </summary>
        public static ArenaRunPlan Roll(ulong seed, LocationDefinition location, bool staged = false)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            var boss = new bool[location.LevelCount];
            for (int i = 0; i < boss.Length; i++) boss[i] = location.GetLevel(i + 1).Boss;
            return Roll(seed, boss, ForestEncounterTemplates.Pool(staged), staged);
        }

        /// <summary>
        /// Бросок плана: boss[i] — уровень i+1 с боссом. Номер арены шаблона —
        /// номер уровня, как глубина забега. staged — правила элит релиза
        /// (первая на А5–А7); пул задаёт вызывающий.
        /// </summary>
        public static ArenaRunPlan Roll(ulong seed, bool[] boss, ArenaEncounterTemplate[] pool, bool staged = false)
        {
            if (boss == null || pool == null || pool.Length == 0) throw new ArgumentException("A plan needs levels and templates.");
            var rng = new Pcg32(seed, PlanStream);
            bool secondElite = rng.NextInt(0, 100) < SecondEliteChancePercent;
            var levels = new ArenaEncounterTemplate[boss.Length];
            // Правила снимаются по очереди, пока план не сложится: 0 — все,
            // 1 — без числа обычных, 2 — ещё и без правила элит.
            for (int relax = 0; relax <= 2; relax++)
            {
                var search = new Search(pool, boss, secondElite, staged, relax, rng);
                if (search.Run(levels)) return new ArenaRunPlan(levels, (bool[])boss.Clone(), pool, seed, secondElite, staged);
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
            return new ArenaRunPlan(levels, (bool[])boss.Clone(), pool, seed, secondElite, staged);
        }

        /// <summary>Перебор с возвратом. Порядок кандидатов каждой арены — взвешенная перестановка.</summary>
        private sealed class Search
        {
            private readonly ArenaEncounterTemplate[] _pool;
            private readonly bool[] _boss;
            private readonly bool _secondElite, _staged;
            private readonly int _relax;
            private Pcg32 _rng;
            private readonly bool[] _used;
            private readonly List<EnemyKind> _known = new List<EnemyKind>();
            private readonly int _arenas;
            private int _steps;

            // Предохранитель: перебор мал (10 шаблонов игры или 16 с новыми
            // видами, 8 арен; леса хватает 30 и 130 шагов), но план не имеет
            // права повесить старт забега на плохих данных.
            private const int MaxSteps = 20000;

            public Search(ArenaEncounterTemplate[] pool, bool[] boss, bool secondElite, bool staged, int relax, Pcg32 rng)
            {
                _pool = pool; _boss = boss; _secondElite = secondElite; _staged = staged; _relax = relax; _rng = rng;
                _used = new bool[pool.Length];
                for (int i = 0; i < boss.Length; i++) if (!boss[i]) _arenas++;
            }

            public bool Run(ArenaEncounterTemplate[] levels)
            {
                for (int i = 0; i < levels.Length; i++) levels[i] = null;
                return Place(levels, 0);
            }

            private bool Place(ArenaEncounterTemplate[] levels, int index)
            {
                if (++_steps > MaxSteps) return false;
                if (index == levels.Length) return Complete(levels);
                if (_boss[index]) return Place(levels, index + 1);
                int arena = index + 1;
                // Кандидаты арены: свой диапазон, без повтора, урок и счётчики.
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
                // Урок раньше сочетаний: каждый вид шаблона уже знаком, кроме его урока.
                for (int k = 0; k < EnemyArchetypes.Count; k++)
                {
                    EnemyKind kind = EnemyArchetypes.At(k).Kind;
                    if (template.Uses(kind) && kind != template.Lesson && !_known.Contains(kind)) return false;
                }
                int ambush = 0, survival = 0;
                for (int i = 0; i < index; i++)
                {
                    if (levels[i] == null) continue;
                    // Вендиго и Шипомёт — не в одном забеге. В пуле игры Шипомёта нет,
                    // и её планы это правило не трогает.
                    if (Rivals(levels[i], template)) return false;
                    if (levels[i].Type == ArenaEncounterType.Ambush) ambush++;
                    if (levels[i].Type == ArenaEncounterType.Survival) survival++;
                }
                if (Rivals(template, template)) return false;
                if (template.Type == ArenaEncounterType.Ambush && ambush >= 1) return false;
                if (template.Type == ArenaEncounterType.Survival && survival >= 1) return false;
                if (_relax < 2 && template.Type == ArenaEncounterType.Elite && !EliteSlot(levels, index)) return false;
                return true;
            }

            /// <summary>
            /// Элита на этой арене не ломает правило «одна на А4–А6, вторая по
            /// броску на А7–А8» (staged — «одна на А5–А7, вторая после неё на А7–А8»).
            /// </summary>
            private bool EliteSlot(ArenaEncounterTemplate[] levels, int index)
            {
                int arena = index + 1;
                if (_staged)
                {
                    // Окна делят А7, поэтому первая — самая ранняя элита плана, а
                    // вторая считается от неё, а не от своего окна.
                    int before = CountElites(levels, index, 1, int.MaxValue);
                    if (before == 0) return arena >= StagedFirstEliteMinArena && arena <= StagedFirstEliteMaxArena;
                    return before == 1 && _secondElite && arena >= SecondEliteMinArena && arena <= SecondEliteMaxArena;
                }
                if (arena >= FirstEliteMinArena && arena <= FirstEliteMaxArena)
                    return CountElites(levels, index, FirstEliteMinArena, FirstEliteMaxArena) == 0;
                if (arena >= SecondEliteMinArena && arena <= SecondEliteMaxArena)
                    return _secondElite && CountElites(levels, index, SecondEliteMinArena, SecondEliteMaxArena) == 0;
                return false;
            }

            private static int CountElites(ArenaEncounterTemplate[] levels, int end, int minArena, int maxArena)
            {
                int count = 0;
                for (int i = 0; i < end; i++)
                    if (levels[i] != null && levels[i].Type == ArenaEncounterType.Elite
                        && i + 1 >= minArena && i + 1 <= maxArena) count++;
                return count;
            }

            private bool Complete(ArenaEncounterTemplate[] levels)
            {
                if (_relax < 2 && _staged)
                {
                    // Первая элита обязательна, если план доходит до А7; вторая —
                    // если выпала и план доходит до А8. Больше двух EliteSlot не пустит.
                    if (levels.Length >= StagedFirstEliteMaxArena && !_boss[StagedFirstEliteMaxArena - 1]
                        && CountElites(levels, levels.Length, StagedFirstEliteMinArena, StagedFirstEliteMaxArena) == 0) return false;
                    if (_secondElite && levels.Length >= SecondEliteMaxArena && !_boss[SecondEliteMaxArena - 1]
                        && CountElites(levels, levels.Length, 1, int.MaxValue) != 2) return false;
                }
                else if (_relax < 2)
                {
                    // Первая элита обязательна, если план доходит до А6; вторая —
                    // если выпала и план доходит до А8.
                    if (levels.Length >= FirstEliteMaxArena && !_boss[FirstEliteMaxArena - 1]
                        && CountElites(levels, levels.Length, FirstEliteMinArena, FirstEliteMaxArena) != 1) return false;
                    if (_secondElite && levels.Length >= SecondEliteMaxArena && !_boss[SecondEliteMaxArena - 1]
                        && CountElites(levels, levels.Length, SecondEliteMinArena, SecondEliteMaxArena) != 1) return false;
                }
                if (_relax < 1 && _arenas == ForestEncounterTemplates.ArenaCount)
                {
                    int normal = 0;
                    foreach (var t in levels) if (t != null && t.Type == ArenaEncounterType.Normal) normal++;
                    if (normal < MinNormal || normal > MaxNormal) return false;
                }
                return true;
            }
        }
    }
}
