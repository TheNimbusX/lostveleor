using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// Встреча арены по шаблону (стадия 6 плана «Мобы леса»): волны, выход
    /// из-под земли, выживание по таймеру и подмога временного босса.
    ///
    /// ПЕРВАЯ ВОЛНА стоит на арене с начала, как прежние пачки: ждёт, пока
    /// герой подойдёт на радиус обнаружения. ПОЗДНИЕ ВОЛНЫ встают из земли в
    /// заранее отобранных точках арены (проходимые клетки маршрута внутри
    /// поляны, куда от входа можно дойти) не ближе WaveHeroClearance к герою
    /// и EmergeTicks тиков бездействуют — ни шага, ни замаха; их видно и по
    /// ним можно бить, но сами они ещё корни. Потом сразу идут на героя.
    ///
    /// Арена зачищена, только когда вышли все волны и все мертвы
    /// (EncounterWavesPending — для RiftRun). Выживание кончается по таймеру:
    /// оставшиеся уходят в землю (SimEventType.Burrowed), арена засчитана.
    ///
    /// Всё здесь — часть состояния: хеш в HashEncounterWaves. Потоки случайности
    /// свои, от сида расстановки и номера волны: когда бы волна ни вышла, её
    /// состав тот же, а место зависит только от того, где стоит герой.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Сколько тиков вставший из земли моб бездействует: 0,8 с.</summary>
        public const int EmergeTicks = 24;

        /// <summary>Поздняя волна не встаёт ближе этого к герою, метры.</summary>
        public static readonly Fix64 WaveHeroClearance = Fix64.FromInt(6);

        /// <summary>Подмога босса — на этих долях его здоровья, %, по разу.</summary>
        public const int BossAddFirstPercent = 66, BossAddSecondPercent = 33;

        // Желаемая дальность поздней волны от героя: в кадре, но не в упор.
        private static readonly Fix64 WavePreferredDistance = Fix64.FromInt(9);
        // Стартовая волна — чуть дальше круга без врагов у входа.
        private static readonly Fix64 StartWaveExtraDistance = Fix64.FromInt(6);
        // Точки отбираются под самое толстое тело — любое из видов туда встанет.
        private static readonly Fix64 WaveSpawnBodyRadius = EntityStore.MaxBodyRadius;
        // Открытое место под точкой появления, метры, и сколько таких нужно,
        // чтобы не отступать к любым проходимым.
        private static readonly Fix64 WaveSpawnClearance = Fix64.Ratio(17, 10);
        private const int MinOpenSpawnPoints = 24;
        // Связность точек со входом — телом Хранителя: так ходят и мобы, и герой.
        private static readonly Fix64 WaveConnectRadius = EnemyArchetypes.GuardianBodyRadius;
        // Из скольких лучших точек бросается центр группы.
        private const int WaveAnchorChoices = 4;
        private const int WaveAnchorCandidates = 12;

        private const ulong WaveStream = 0x57415645454E4355UL;      // "WAVEENCU"
        private const ulong BossAddStream = 0x424F535341444453UL;   // "BOSSADDS"

        private int[] _emergeUntil;
        private ArenaEncounterTemplate _encounter;
        private EncounterPlan _encounterPlan;
        private ulong _encounterSeed;
        private int _encounterArena, _encounterHealthPercent, _encounterDamagePercent, _encounterHardPercent;
        private int _encounterStartTick, _wavesSpawned, _lastWaveTick, _survivalEndTick;
        private bool _survivalEnded;
        private int _encounterBoss = -1, _bossAddsSpawned;
        private FixVec2 _arenaCenter, _arenaAxis;
        private readonly List<FixVec2> _spawnPoints = new List<FixVec2>();
        private long[] _spawnScore = new long[0];
        private bool[] _spawnTaken = new bool[0];
        private int[] _waveCounts = new int[8];
        private readonly int[] _anchorScratch = new int[WaveAnchorCandidates];

        /// <summary>Шаблон встречи этой арены; null — встреча не по шаблону.</summary>
        public ArenaEncounterTemplate ActiveEncounter => _encounter;

        /// <summary>Сколько волн встречи уже вышло (стартовая — первая).</summary>
        public int EncounterWavesSpawned => _encounter != null ? _wavesSpawned : 0;

        /// <summary>Остались ли невышедшие волны. Арена не зачищена, пока true.</summary>
        public bool EncounterWavesPending
            => _encounter != null && !_survivalEnded && _wavesSpawned < _encounter.WaveCount;

        /// <summary>Тиков до конца выживания для HUD; 0 — не выживание или уже кончилось.</summary>
        public int SurvivalTicksLeft
            => _encounter != null && _encounter.SurvivalTicks > 0 && !_survivalEnded
                ? Math.Max(0, _survivalEndTick - Tick) : 0;

        /// <summary>Моб ещё встаёт из земли: не ходит и не бьёт.</summary>
        public bool IsEmerging(int entity)
            => _emergeUntil != null && (uint)entity < (uint)_emergeUntil.Length && Tick < _emergeUntil[entity];

        /// <summary>Сколько тиков моб ещё встаёт из земли; 0 — уже на ногах.</summary>
        public int EmergeTicksLeft(int entity) => IsEmerging(entity) ? _emergeUntil[entity] - Tick : 0;

        /// <summary>Сколько волн подмоги босса уже вышло: 0, 1 или 2.</summary>
        public int BossAddWavesSpawned => _encounterBoss >= 0 ? _bossAddsSpawned : 0;

        /// <summary>Сброс при любой новой расстановке (SetupRift).</summary>
        private void ResetEncounterWaves()
        {
            _encounter = null;
            _encounterPlan = null;
            _encounterBoss = -1;
            _bossAddsSpawned = 0;
            _wavesSpawned = 0;
            _survivalEnded = false;
            _spawnPoints.Clear();
            if (_emergeUntil == null || _emergeUntil.Length != Entities.Capacity) _emergeUntil = new int[Entities.Capacity];
            else Array.Clear(_emergeUntil, 0, _emergeUntil.Length);
        }

        /// <summary>
        /// Встреча арены по шаблону. Здоровье — строка вида × healthPercent
        /// уровня × hardPercent («Сложно»: 125); урон — строка вида ×
        /// damagePercent профиля × hardPercent. Подстройки групп у шаблонов нет.
        /// arena — номер арены (глубина забега): по нему бюджет угроз волн.
        /// </summary>
        public EncounterPlan SetupArenaEncounter(LayoutMap map, ulong spawnSeed, int arena, int healthPercent,
            int damagePercent, ArenaEncounterTemplate template, int hardPercent = 100, int entryClearance = 14)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (map.Routes == null || map.ExitCount == 0) throw new ArgumentException("Arena encounters require walking routes and an exit.");
            // MaxEnemies — места в пуле: дети Расщепеня встают в новые слоты.
            if (template.MaxEnemies + 1 > Entities.Capacity) throw new ArgumentException("Encounter population exceeds entity capacity.");
            SetupRift(map, spawnSeed, 0, 0, 1);
            var elite = new bool[Entities.Capacity];
            _eliteMask = elite;
            _encounterPlan = new EncounterPlan(new List<EncounterPlacement>(), elite, Fix64.FromInt(5), 0);
            PrepareArena(map, spawnSeed, arena, healthPercent, damagePercent, hardPercent);
            _encounter = template;
            _encounterStartTick = Tick;
            _survivalEndTick = template.SurvivalTicks > 0 ? Tick + template.SurvivalTicks : 0;
            Fix64 clearance = Fix64.FromInt(entryClearance);
            SpawnWave(0, map.EntryPoint, clearance, clearance + StartWaveExtraDistance, emerge: false);
            Grid.Rebuild(Entities);
            return _encounterPlan;
        }

        /// <summary>Точки появления арены и числа врагов встречи. Общее у шаблона и босса.</summary>
        private void PrepareArena(LayoutMap map, ulong spawnSeed, int arena, int healthPercent, int damagePercent, int hardPercent)
        {
            if (healthPercent < 1 || damagePercent < 1 || hardPercent < 1)
                throw new ArgumentException("Encounter percents must be positive.");
            _encounterSeed = spawnSeed;
            _encounterArena = arena;
            _encounterHealthPercent = healthPercent;
            _encounterDamagePercent = damagePercent;
            _encounterHardPercent = hardPercent;
            _wavesSpawned = 0;
            _lastWaveTick = Tick;
            _survivalEnded = false;

            FixVec2 entry = map.EntryPoint, exit = map.ExitPoint(0);
            _arenaAxis = (exit - entry).Normalized();
            if (_arenaAxis.LengthSq.Raw == 0) _arenaAxis = new FixVec2(Fix64.Zero, Fix64.One);
            _arenaCenter = map.GladeCount > 0 ? map.GetGlade(0).Center : (entry + exit) / Fix64.FromInt(2);

            // Заранее отобранные точки: клетки маршрута внутри поляны под самое
            // толстое тело, до которых от входа можно ДОЙТИ. Маршрут считается до
            // деревьев (SolidEnvironment ставит их после), поэтому связность —
            // своя волна по клеткам с проверкой хода: иначе волна вставала в
            // кармане за стволами, и ни она к герою, ни герой к ней (стенд, 26.09).
            // Порядок — порядок клеток, он и держит детерминизм выбора.
            //
            // Точка ещё и на открытом месте: круг WaveSpawnClearance вокруг неё
            // проходим. У края поляны, между стволами, волна утаскивала бой в
            // угол, откуда до выхода не видно ни одной клетки. Открытых мало
            // (тесная арена) — берутся все проходимые.
            _spawnPoints.Clear();
            bool[] reached = ReachableCells(map, WaveConnectRadius);
            for (int pass = 0; pass < 2 && _spawnPoints.Count < MinOpenSpawnPoints; pass++)
            {
                _spawnPoints.Clear();
                Fix64 clearance = pass == 0 ? WaveSpawnClearance : WaveSpawnBodyRadius;
                for (int c = 0; c < map.Routes.CellCount; c++)
                {
                    if (!reached[c]) continue;
                    FixVec2 point = map.Routes.GetCell(c).Center;
                    if (map.GladeCount > 0 && !InsideGlade(map, point)) continue;
                    if (!map.IsWalkable(point, clearance)) continue;
                    _spawnPoints.Add(point);
                }
            }
            if (_spawnScore.Length < _spawnPoints.Count)
            {
                _spawnScore = new long[_spawnPoints.Count];
                _spawnTaken = new bool[_spawnPoints.Count];
            }
        }

        /// <summary>
        /// Клетки маршрута, до которых от входа можно дойти телом radius: волна
        /// по четырём соседям, переход — только если по прямой между центрами
        /// клеток нет ни берега, ни ствола. Зовётся раз на расстановку.
        /// </summary>
        private static bool[] ReachableCells(LayoutMap map, Fix64 radius)
        {
            var routes = map.Routes;
            var reached = new bool[routes.CellCount];
            int start = routes.CellAt(map.EntryPoint);
            if (start < 0) return reached;
            var queue = new int[routes.CellCount];
            int head = 0, tail = 0;
            queue[tail++] = start;
            reached[start] = true;
            Fix64 size = LayoutMap.CellSize;
            var steps = new[]
            {
                new FixVec2(size, Fix64.Zero), new FixVec2(-size, Fix64.Zero),
                new FixVec2(Fix64.Zero, size), new FixVec2(Fix64.Zero, -size),
            };
            while (head < tail)
            {
                int cell = queue[head++];
                FixVec2 center = routes.GetCell(cell).Center;
                for (int d = 0; d < steps.Length; d++)
                {
                    int next = routes.CellAt(center + steps[d]);
                    if (next < 0 || reached[next] || !map.CanTravel(center, routes.GetCell(next).Center, radius)) continue;
                    reached[next] = true;
                    queue[tail++] = next;
                }
            }
            return reached;
        }

        private static bool InsideGlade(LayoutMap map, FixVec2 point)
        {
            for (int g = 0; g < map.GladeCount; g++)
                if (map.GetGlade(g).Field(point) <= Fix64.One) return true;
            return false;
        }

        /// <summary>
        /// Волны встречи и подмога босса. Зовётся в конце тика, после всех
        /// обновлений врагов, — счёт живых видит смерти этого тика.
        /// </summary>
        private void UpdateEncounterWaves()
        {
            if (_encounterBoss >= 0) UpdateBossAdds();
            if (_encounter == null || _survivalEnded) return;

            if (_encounter.SurvivalTicks > 0 && Tick + 1 >= _survivalEndTick)
            {
                EndSurvival();
                return;
            }
            if (_wavesSpawned >= _encounter.WaveCount)
            {
                // Все волны выживания вышли и легли раньше таймера — выживание
                // окончено: HUD больше не тикает над пустой ареной.
                if (_encounter.SurvivalTicks > 0 && CountAliveEnemies() == 0) _survivalEnded = true;
                return;
            }
            if (!Entities.Alive[PlayerId]) return;
            // Две волны не встают друг на друга: следующая — не раньше, чем
            // предыдущая поднялась из земли. Стартовая из земли не встаёт.
            if (_wavesSpawned > 1 && Tick - _lastWaveTick < EmergeTicks) return;

            int alive = CountAliveEnemies();
            var trigger = _encounter.GetWave(_wavesSpawned).Trigger;
            bool due;
            switch (trigger.Kind)
            {
                case WaveTriggerKind.AliveAtMost:
                    due = alive <= trigger.Alive
                        || (trigger.Ticks > 0 && Tick - _lastWaveTick >= trigger.Ticks);
                    break;
                case WaveTriggerKind.AtTick:
                    // Пустая арена волну не ждёт: пауза без врагов — пустая пауза.
                    due = alive == 0 || Tick + 1 - _encounterStartTick >= trigger.Ticks;
                    break;
                default:
                    due = false;
                    break;
            }
            if (!due) return;
            SpawnWave(_wavesSpawned, Entities.Position[PlayerId], WaveHeroClearance, WavePreferredDistance, emerge: true);
            Grid.Rebuild(Entities);
        }

        /// <summary>
        /// Одна волна шаблона: цель по бюджету угроз арены, числа групп,
        /// группы по местам. Сущности волны идут подряд — одно размещение плана.
        /// </summary>
        private void SpawnWave(int index, FixVec2 hero, Fix64 minDistance, Fix64 preferred, bool emerge)
        {
            var wave = _encounter.GetWave(index);
            var rng = new Pcg32(_encounterSeed ^ unchecked((ulong)(index + 1) * 0x9E3779B97F4A7C15UL), WaveStream);
            _encounter.WaveBudget(_encounterArena, out int low, out int high);
            int target = rng.NextInt(low, high + 1);
            if (_waveCounts.Length < wave.GroupCount) _waveCounts = new int[wave.GroupCount];
            wave.RollCounts(target, ref rng, _waveCounts);
            // Выживание — бой на время: и стартовая волна сразу идёт на героя.
            bool aggro = emerge || _encounter.Type == ArenaEncounterType.Survival;
            SpawnGroups(wave, hero, minDistance, preferred, emerge, aggro, ref rng, out FixVec2 center, out int first);
            _wavesSpawned = index + 1;
            _lastWaveTick = Tick;
            _events.Add(new SimEvent(SimEventType.EncounterWave, -1, -1, index,
                _encounter.Type == ArenaEncounterType.Survival, center, actionVariant: _encounter.WaveCount));
            _encounterPlan?.Add(new EncounterPlacement(index == 0 ? EncounterRole.Introduction : EncounterRole.MainPath,
                ModuleAt(center), -1, _encounter.Id, center, first, Entities.Count - first));
        }

        private void SpawnGroups(EncounterWave wave, FixVec2 hero, Fix64 minDistance, Fix64 preferred,
            bool emerge, bool aggro, ref Pcg32 rng, out FixVec2 center, out int first)
        {
            first = Entities.Count;
            center = hero;
            bool placed = false;
            for (int i = 0; i < _spawnPoints.Count; i++) _spawnTaken[i] = false;
            for (int g = 0; g < wave.GroupCount; g++)
            {
                var group = wave.GetGroup(g);
                int count = _waveCounts[g];
                if (count <= 0) continue;
                FixVec2 anchor = PlaceWaveGroup(group, count, hero, minDistance, preferred, emerge, aggro, ref rng);
                if (!placed) { center = anchor; placed = true; }
            }
        }

        /// <summary>
        /// Одна группа: точка-центр по месту группы (лучшие по счёту точки,
        /// бросок среди первых), члены — в ближайших к нему свободных точках.
        /// Не поместившиеся считаются в OmittedEnemies плана.
        /// </summary>
        private FixVec2 PlaceWaveGroup(in WaveGroup group, int count, FixVec2 hero, Fix64 minDistance,
            Fix64 preferred, bool emerge, bool aggro, ref Pcg32 rng)
        {
            int points = _spawnPoints.Count;
            FixVec2 direction = FixVec2.Zero;
            if (group.Placement == WavePlacement.Front) direction = _arenaAxis;
            else if (group.Placement == WavePlacement.Back) direction = -_arenaAxis;
            else if (group.Placement == WavePlacement.Flank)
            {
                var side = new FixVec2(-_arenaAxis.Y, _arenaAxis.X);
                direction = rng.NextInt(0, 2) == 0 ? side : -side;
            }
            Fix64 minSq = minDistance * minDistance;
            for (int i = 0; i < points; i++)
            {
                FixVec2 d = _spawnPoints[i] - hero;
                Fix64 distanceSq = d.LengthSq;
                if (_spawnTaken[i] || distanceSq < minSq) { _spawnScore[i] = long.MinValue; continue; }
                Fix64 distance = Fix64.Sqrt(distanceSq);
                Fix64 score = -Fix64.Abs(distance - preferred) / 4;
                if (group.Placement == WavePlacement.Center)
                    score -= FixVec2.Distance(_spawnPoints[i], _arenaCenter) / 2;
                else if (distance.Raw > 0)
                    score += FixVec2.Dot(d, direction) / distance * 4;
                _spawnScore[i] = score.Raw;
            }

            // Лучшие кандидаты в центр группы. Поздней волне нужна прямая дорога
            // к герою: враги идут на него по прямой, и волна за прудом стояла бы.
            Fix64 radius = ArchetypeBodyRadius(group.Kind);
            int anchor = -1, choices;
            int[] best = _anchorScratch;
            int bestCount = TopSpawnPoints(best);
            if (emerge && _layout != null)
            {
                int kept = 0;
                for (int k = 0; k < bestCount; k++)
                    if (_layout.CanTravel(_spawnPoints[best[k]], hero, radius)) best[kept++] = best[k];
                if (kept > 0) bestCount = kept;
            }
            choices = Math.Min(WaveAnchorChoices, bestCount);
            if (choices > 0) anchor = best[rng.NextInt(0, choices)];
            if (anchor < 0)
            {
                _encounterPlan.OmittedEnemies += count;
                return hero;
            }

            FixVec2 anchorPoint = _spawnPoints[anchor];
            FixVec2 face = hero;
            Fix64 jitter = Fix64.Ratio(1, 4);
            for (int n = 0; n < count; n++)
            {
                bool done = false;
                while (!done)
                {
                    // Ближайшая к центру ещё не занятая точка, при равенстве — младшая.
                    int next = -1;
                    Fix64 nearest = Fix64.MaxValue;
                    for (int i = 0; i < points; i++)
                    {
                        if (_spawnScore[i] == long.MinValue) continue;
                        Fix64 d = FixVec2.DistanceSq(_spawnPoints[i], anchorPoint);
                        if (d < nearest) { nearest = d; next = i; }
                    }
                    if (next < 0) break;
                    _spawnScore[next] = long.MinValue;
                    FixVec2 spot = _spawnPoints[next]
                        + new FixVec2(rng.NextFix(-jitter, jitter), rng.NextFix(-jitter, jitter));
                    if (_layout != null && !_layout.IsWalkable(spot, radius)) spot = _spawnPoints[next];
                    if (FixVec2.DistanceSq(spot, hero) < minSq || !WaveSpotFree(spot, radius)) continue;
                    _spawnTaken[next] = true;
                    SpawnEncounterEnemy(spot, group.Kind, group.Elite, emerge, aggro, face);
                    done = true;
                }
                if (!done) { _encounterPlan.OmittedEnemies += count - n; break; }
            }
            return anchorPoint;
        }

        /// <summary>Лучшие по счёту точки, по убыванию; при равенстве — младшая. Возвращает сколько.</summary>
        private int TopSpawnPoints(int[] best)
        {
            int count = 0;
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                if (_spawnScore[i] == long.MinValue) continue;
                int at = count < best.Length ? count : best.Length;
                while (at > 0 && _spawnScore[best[at - 1]] < _spawnScore[i]) at--;
                if (at >= best.Length) continue;
                int last = Math.Min(count, best.Length - 1);
                for (int k = last; k > at; k--) best[k] = best[k - 1];
                best[at] = i;
                if (count < best.Length) count++;
            }
            return count;
        }

        /// <summary>Не налезает ли тело на живых: тот же зазор, что у прежней расстановки.</summary>
        private bool WaveSpotFree(FixVec2 spot, Fix64 radius)
        {
            for (int i = 0; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i]) continue;
                Fix64 spacing = Entities.BodyRadius[i] + radius + Fix64.Ratio(1, 10);
                if (FixVec2.DistanceSq(spot, Entities.Position[i]) < spacing * spacing) return false;
            }
            return true;
        }

        /// <summary>
        /// Один враг встречи: строка вида, глубина и «Сложно». Вставший из
        /// земли сразу заметил героя, но EmergeTicks не ходит и не бьёт.
        /// </summary>
        private int SpawnEncounterEnemy(FixVec2 spot, EnemyKind kind, bool elite, bool emerge, bool aggro, FixVec2 face)
        {
            int id = SpawnScaledEnemy(spot, kind, _encounterHealthPercent, _encounterDamagePercent, _encounterHardPercent);
            FixVec2 look = (face - spot).Normalized();
            if (look.LengthSq.Raw != 0) Entities.Facing[id] = look;
            if (elite)
            {
                _eliteMask[id] = true;
                Entities.XpReward[id] = Progression.EliteKillXp;
            }
            if (aggro) Entities.Aggro[id] = true;
            if (emerge)
            {
                // Тик выхода уже идёт: бездействие считается со следующего.
                _emergeUntil[id] = Tick + 1 + EmergeTicks;
                if (Entities.NextAttackTick[id] < _emergeUntil[id]) Entities.NextAttackTick[id] = _emergeUntil[id];
                _events.Add(SimEvent.Emerge(id, spot, EmergeTicks));
            }
            else _events.Add(SimEvent.Spawn(id, spot));
            return id;
        }

        /// <summary>
        /// Враг вида kind со строкой таблицы, выросшей до арены: здоровье ×
        /// healthPercent × hardPercent, урон × damagePercent × hardPercent.
        /// Без события появления, агро и взгляда — их ставит вызывающий.
        /// Общий у волн встречи и стенда одного вида (SetupKindTestArena).
        /// </summary>
        private int SpawnScaledEnemy(FixVec2 spot, EnemyKind kind, int healthPercent, int damagePercent, int hardPercent)
        {
            int health = EnemyArchetypes.ScaleHealth(ArchetypeHealth(kind), healthPercent, hardPercent);
            int id = Entities.Spawn(spot, health, Faction.Orvill);
            ConfigureEnemy(id, kind);
            var sheet = Entities.Stats[id];
            sheet.SetBase(StatType.Damage, Entities.Damage[id]
                * Fix64.Ratio(damagePercent * hardPercent, 10000));
            Entities.RefreshStats(id);
            Entities.Health[id] = Entities.MaxHealth[id];
            return id;
        }

        private int ModuleAt(FixVec2 point)
        {
            if (_layout == null) return -1;
            for (int i = 0; i < _layout.PlacedCount; i++)
                if (_layout.ContainsWorld(i, point)) return i;
            return -1;
        }

        /// <summary>Таймер выживания вышел: оставшиеся уходят в землю, волны больше не выходят.</summary>
        private void EndSurvival()
        {
            _survivalEnded = true;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Faction.Wole) continue;
                Entities.Alive[i] = false;
                Entities.Velocity[i] = FixVec2.Zero;
                Entities.ForcedTicksLeft[i] = 0;
                // Метки гаснут в этот же тик, как у смерти; ни опыта, ни добычи.
                CancelEnemySwing(i);
                CancelTelegraphsOf(i);
                Statuses.ClearBurn(i);
                Statuses.StunUntilTick[i] = 0;
                if (_eliteMask != null && i < _eliteMask.Length) _eliteMask[i] = false;
                if (_attackTarget == i) _attackTarget = -1;
                _events.Add(SimEvent.Burrow(i, Entities.Position[i]));
            }
        }

        // ---- временный босс ----

        /// <summary>Босс арены: подмога на 66% и 33% его здоровья. Зовёт SetupBossArena.</summary>
        private void PrepareBossAdds(LayoutMap map, ulong spawnSeed, int arena, int healthPercent,
            int damagePercent, int hardPercent, int boss)
        {
            PrepareArena(map, spawnSeed, arena, healthPercent, damagePercent, hardPercent);
            _encounterBoss = boss;
            _bossAddsSpawned = 0;
        }

        private void UpdateBossAdds()
        {
            int boss = _encounterBoss;
            if (_bossAddsSpawned >= 2 || !Entities.Alive[boss] || !Entities.Alive[PlayerId]) return;
            int percent = _bossAddsSpawned == 0 ? BossAddFirstPercent : BossAddSecondPercent;
            if ((long)Entities.Health[boss] * 100 > (long)Entities.MaxHealth[boss] * percent) return;
            // Обе отметки за один тик — вторая волна выходит тиком позже, не разом.
            if (_bossAddsSpawned > 0 && Tick - _lastWaveTick < EmergeTicks) return;
            var wave = ForestEncounterTemplates.BossAdds;
            var rng = new Pcg32(_encounterSeed ^ unchecked((ulong)(_bossAddsSpawned + 1) * 0xC2B2AE3D27D4EB4FUL), BossAddStream);
            if (_waveCounts.Length < wave.GroupCount) _waveCounts = new int[wave.GroupCount];
            wave.RollCounts(0, ref rng, _waveCounts);
            SpawnGroups(wave, Entities.Position[PlayerId], WaveHeroClearance, WavePreferredDistance,
                emerge: true, aggro: true, ref rng, out FixVec2 center, out int first);
            _bossAddsSpawned++;
            _lastWaveTick = Tick;
            _events.Add(new SimEvent(SimEventType.EncounterWave, boss, -1, -1, false, center,
                actionVariant: _bossAddsSpawned));
            _encounterPlan?.Add(new EncounterPlacement(EncounterRole.MainPath, ModuleAt(center), -1,
                StableId.Of("encounter.forest.boss_adds"), center, first, Entities.Count - first));
            Grid.Rebuild(Entities);
        }

        private void HashEncounterWaves(ref ulong hash)
        {
            if (_encounter == null && _encounterBoss < 0) return;
            Hashing.Mix(ref hash, _encounter != null ? _encounter.Id : 0);
            Hashing.Mix(ref hash, _encounterBoss);
            Hashing.Mix(ref hash, _bossAddsSpawned);
            Hashing.Mix(ref hash, _encounterSeed);
            Hashing.Mix(ref hash, _encounterArena);
            Hashing.Mix(ref hash, _encounterHealthPercent);
            Hashing.Mix(ref hash, _encounterDamagePercent);
            Hashing.Mix(ref hash, _encounterHardPercent);
            Hashing.Mix(ref hash, _encounterStartTick);
            Hashing.Mix(ref hash, _wavesSpawned);
            Hashing.Mix(ref hash, _lastWaveTick);
            Hashing.Mix(ref hash, _survivalEndTick);
            Hashing.Mix(ref hash, _survivalEnded ? 1 : 0);
            Hashing.Mix(ref hash, _spawnPoints.Count);
            for (int i = 0; i < Entities.Count; i++)
                if (_emergeUntil[i] > Tick) { Hashing.Mix(ref hash, i); Hashing.Mix(ref hash, _emergeUntil[i]); }
        }
    }
}
