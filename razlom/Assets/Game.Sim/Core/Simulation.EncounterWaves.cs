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
    /// и EmergeTicks тиков (с разносом — до EmergeTicks + EmergeStaggerMaxTicks)
    /// бездействуют — ни шага, ни замаха; их видно и по ним можно бить, но
    /// сами они ещё корни. Потом сразу идут на героя.
    ///
    /// ПАЧКИ РАЗНЕСЕНЫ (владелец, 26.09: «по пачкам их чуть правильнее надо
    /// рассредоточить»). Центры групп одной волны не ближе WaveAnchorSpread
    /// друг к другу (точка у занятого центра теряет очки), второй «центр»
    /// волны ставится как фланг. Члены группы крупных видов — не плечом к
    /// плечу, а через WaveMemberSpacing; рой стоит кучей, как стоял. Стрелки
    /// (плюй-плод, Корнехват, Шипомёт) тянутся дальше от героя — как у
    /// прежних пачек. Волна встаёт из земли и бьёт впервые вразнобой: k-й
    /// член волны встаёт на EmergeStaggerTicks·k позже (не больше
    /// EmergeStaggerMaxTicks), первые удары — по кругу из трёх через
    /// FirstAttackStaggerTicks; стартовая волна — только удары.
    ///
    /// Арена зачищена, только когда вышли все волны и все мертвы
    /// (EncounterWavesPending — для RiftRun). Выживание кончается по таймеру:
    /// оставшиеся уходят в землю (SimEventType.Burrowed), арена засчитана.
    ///
    /// Всё здесь — часть состояния: хеш в HashEncounterWaves (кроме черновика
    /// одной расстановки — центров групп и мест членов: после неё он ни на что
    /// не влияет, а разнос ложится в _emergeUntil и NextAttackTick). Потоки случайности
    /// свои, от сида расстановки и номера волны: когда бы волна ни вышла, её
    /// состав тот же, а место зависит только от того, где стоит герой.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Сколько тиков вставший из земли моб бездействует: 0,8 с. Это первый
        /// член волны; следующие — дольше на разнос (EmergeStaggerTicks).
        /// </summary>
        public const int EmergeTicks = 24;

        /// <summary>k-й член волны (с нуля) встаёт на столько тиков позже первого за каждый номер…</summary>
        public const int EmergeStaggerTicks = 4;

        /// <summary>…но не позже этого: волна целиком на ногах за EmergeTicks + 12 тиков.</summary>
        public const int EmergeStaggerMaxTicks = 12;

        /// <summary>
        /// Первые удары волны вразнобой: член k бьёт не раньше чем через
        /// (k mod FirstAttackStaggerGroups) · FirstAttackStaggerTicks после выхода
        /// (у стартовой волны — после появления).
        /// </summary>
        public const int FirstAttackStaggerTicks = 10, FirstAttackStaggerGroups = 3;

        /// <summary>Центры групп одной волны не ближе этого друг к другу, метры (если арена позволяет).</summary>
        public static readonly Fix64 WaveAnchorSpread = Fix64.FromInt(5);

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
        // Точка ближе WaveAnchorSpread к центру уже вставшей группы волны теряет
        // столько очков за каждый такой центр: больше разброса по направлению
        // (±4), и соседний центр почти всегда проигрывает свободному месту.
        private static readonly Fix64 WaveAnchorCrowdPenalty = Fix64.FromInt(8);
        // Групп в волне не больше шести (EncounterWave), членов группы — 16 (WaveGroup).
        private const int MaxWaveGroups = 6, MaxWaveGroupMembers = 16;

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
        // Сегменты арены (владелец, 2 октября): сегмент каждой точки появления и каждой волны.
        // Волна, открывающая сегмент, выходит, когда герой вступил на его поляну; волны ставятся
        // только на точки своего сегмента. Карта из одной поляны — всё в сегменте 0, как раньше.
        private int[] _spawnSegment = new int[0];
        private int[] _waveSegment = new int[0];
        private int _activeSegment;
        private int _segmentCount = 1;

        /// <summary>Сегмент арены, в котором выходят волны сейчас (0 — первый).</summary>
        public int ActiveArenaSegment => _activeSegment;

        /// <summary>Сегмент арены, к которому относится волна index шаблона.</summary>
        public int WaveSegment(int index) => index < _waveSegment.Length ? _waveSegment[index] : 0;
        private long[] _spawnScore = new long[0];
        private bool[] _spawnTaken = new bool[0];
        private int[] _waveCounts = new int[8];
        private readonly int[] _anchorScratch = new int[WaveAnchorCandidates];
        // Черновик одной расстановки волны (SpawnGroups), не состояние: центры
        // уже вставших групп и их сущности, был ли «центр», места членов
        // текущей группы и номер следующего члена волны. Ни на что после
        // расстановки не влияет — в хеш не идёт; тесты читают последнюю волну.
        private readonly FixVec2[] _waveAnchors = new FixVec2[MaxWaveGroups];
        private readonly int[] _waveGroupFirst = new int[MaxWaveGroups], _waveGroupEnd = new int[MaxWaveGroups];
        private int _waveAnchorCount, _waveMember;
        private bool _waveCenterTaken;
        private readonly FixVec2[] _groupSpots = new FixVec2[MaxWaveGroupMembers];

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

        /// <summary>Для тестов разноса: сколько групп встало в последней расставленной волне (или подмоге).</summary>
        internal int LastWaveGroupCount => _waveAnchorCount;

        /// <summary>Для тестов разноса: центр группы последней волны и её сущности [first, end).</summary>
        internal FixVec2 LastWaveGroup(int group, out int first, out int end)
        {
            first = _waveGroupFirst[group];
            end = _waveGroupEnd[group];
            return _waveAnchors[group];
        }

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
            AssignWaveSegments(map, template);
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
            _activeSegment = 0;
            _segmentCount = map.IsArena ? Math.Max(1, map.GladeCount) : 1;
            if (_spawnSegment.Length < _spawnPoints.Count) _spawnSegment = new int[_spawnPoints.Count];
            for (int i = 0; i < _spawnPoints.Count; i++)
                _spawnSegment[i] = _segmentCount > 1 ? Math.Max(0, map.SegmentAt(_spawnPoints[i])) : 0;
        }

        /// <summary>
        /// Волны шаблона по сегментам арены, по порядку: волна w — в сегменте w·S/W. Каждый сегмент
        /// получает хотя бы одну волну, если волн не меньше сегментов; бюджет угроз волн прежний —
        /// враги встречи делятся между сегментами, а не умножаются. Выживание — бой на время,
        /// он остаётся целиком в первом сегменте.
        /// </summary>
        private void AssignWaveSegments(LayoutMap map, ArenaEncounterTemplate template)
        {
            int waves = template.WaveCount;
            if (_waveSegment.Length < waves) _waveSegment = new int[waves];
            int segments = template.Type == ArenaEncounterType.Survival ? 1 : _segmentCount;
            for (int w = 0; w < waves; w++) _waveSegment[w] = Math.Min(segments - 1, w * segments / Math.Max(1, waves));
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

        /// <summary>
        /// Для тестов зачистки: следующая волна ждёт входа в новый сегмент — герой ставится на его
        /// точку появления, ближайшую к центру поляны (ходьба проверяется движковыми тестами).
        /// </summary>
        internal void StepHeroIntoPendingSegment()
        {
            if (!EncounterWavesPending || _layout == null) return;
            int segment = _waveSegment[_wavesSpawned];
            if (segment == _activeSegment || segment >= _layout.GladeCount) return;
            FixVec2 center = _layout.GetGlade(segment).Center;
            int best = -1;
            for (int i = 0; i < _spawnPoints.Count; i++)
                if (_spawnSegment[i] == segment
                    && (best < 0 || FixVec2.DistanceSq(_spawnPoints[i], center) < FixVec2.DistanceSq(_spawnPoints[best], center)))
                    best = i;
            if (best < 0) return;
            Entities.Position[PlayerId] = _spawnPoints[best];
            StopPlayerMovement();
            Grid.Rebuild(Entities);
        }

        // Центр поляны сегмента, где выходят волны; у карты из одной поляны — центр арены.
        private FixVec2 SegmentCenter()
            => _segmentCount > 1 && _layout != null && _activeSegment < _layout.GladeCount
                ? _layout.GetGlade(_activeSegment).Center : _arenaCenter;

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
                if (_encounter.SurvivalTicks > 0 && CountAliveEnemies() == 0 && !HasPendingSplits) _survivalEnded = true;
                return;
            }
            if (!Entities.Alive[PlayerId]) return;
            // Две волны не встают друг на друга: следующая — не раньше, чем
            // из земли поднялся первый член предыдущей (остальные встают следом,
            // до EmergeStaggerMaxTicks позже). Стартовая из земли не встаёт.
            if (_wavesSpawned > 1 && Tick - _lastWaveTick < EmergeTicks) return;

            int alive = CountAliveEnemies() + PendingSplitCount * SplitChildren;
            // Волна нового сегмента выходит, когда герой ступил на его поляну, — что бы ни
            // осталось живым позади: до того сегмент пуст, и герой идёт к нему по проходу.
            int segment = _waveSegment[_wavesSpawned];
            if (segment != _activeSegment)
            {
                if (_layout == null || _layout.SegmentAt(Entities.Position[PlayerId]) != segment) return;
                _activeSegment = segment;
                SpawnWave(_wavesSpawned, Entities.Position[PlayerId], WaveHeroClearance, WavePreferredDistance, emerge: true);
                Grid.Rebuild(Entities);
                return;
            }
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
            _waveAnchorCount = 0;
            _waveMember = 0;
            _waveCenterTaken = false;
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
        /// бросок среди первых), члены — в ближайших к нему свободных точках
        /// не ближе WaveMemberSpacing друг к другу. Центр не садится у центров
        /// уже вставших групп волны; стрелки тянутся от героя. Не
        /// поместившиеся считаются в OmittedEnemies плана.
        /// </summary>
        private FixVec2 PlaceWaveGroup(in WaveGroup group, int count, FixVec2 hero, Fix64 minDistance,
            Fix64 preferred, bool emerge, bool aggro, ref Pcg32 rng)
        {
            int points = _spawnPoints.Count;
            // Второй «центр» волны — уже фланг: два центра садились в один ком
            // посреди поляны (E04: хранитель и рой).
            WavePlacement placement = group.Placement;
            if (placement == WavePlacement.Center && _waveCenterTaken) placement = WavePlacement.Flank;
            FixVec2 direction = FixVec2.Zero;
            if (placement == WavePlacement.Front) direction = _arenaAxis;
            else if (placement == WavePlacement.Back) direction = -_arenaAxis;
            else if (placement == WavePlacement.Flank)
            {
                var side = new FixVec2(-_arenaAxis.Y, _arenaAxis.X);
                direction = rng.NextInt(0, 2) == 0 ? side : -side;
            }
            // Стрелки — в глубину, как у прежних пачек (SetupEncounters): дальше
            // желаемой дальности счёт не падает, ближе — падает вдвое быстрее.
            bool keepBack = WaveKeepsBack(group.Kind);
            Fix64 minSq = minDistance * minDistance;
            Fix64 spreadSq = WaveAnchorSpread * WaveAnchorSpread;
            for (int i = 0; i < points; i++)
            {
                FixVec2 d = _spawnPoints[i] - hero;
                Fix64 distanceSq = d.LengthSq;
                if (_spawnTaken[i] || distanceSq < minSq || _spawnSegment[i] != _activeSegment) { _spawnScore[i] = long.MinValue; continue; }
                Fix64 distance = Fix64.Sqrt(distanceSq);
                Fix64 score = -Fix64.Abs(distance - preferred) / 4;
                if (keepBack) score += distance / 4;
                if (placement == WavePlacement.Center)
                    score -= FixVec2.Distance(_spawnPoints[i], SegmentCenter()) / 2;
                else if (distance.Raw > 0)
                    score += FixVec2.Dot(d, direction) / distance * 4;
                for (int a = 0; a < _waveAnchorCount; a++)
                    if (FixVec2.DistanceSq(_spawnPoints[i], _waveAnchors[a]) < spreadSq) score -= WaveAnchorCrowdPenalty;
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
            int slot = _waveAnchorCount < _waveAnchors.Length ? _waveAnchorCount++ : -1;
            if (slot >= 0)
            {
                _waveAnchors[slot] = anchorPoint;
                _waveGroupFirst[slot] = _waveGroupEnd[slot] = Entities.Count;
            }
            if (placement == WavePlacement.Center) _waveCenterTaken = true;
            FixVec2 face = hero;
            Fix64 jitter = Fix64.Ratio(1, 4);
            Fix64 spacing = WaveMemberSpacing(group.Kind);
            Fix64 spacingSq = spacing * spacing;
            int placed = 0;
            for (int n = 0; n < count; n++)
            {
                bool done = false;
                while (!done)
                {
                    // Ближайшая к центру свободная точка не ближе spacing к уже
                    // вставшим членам группы; такой нет — просто ближайшая.
                    int next = NearestWavePoint(anchorPoint, placed, spacingSq);
                    bool spaced = next >= 0 && placed > 0 && spacing.Raw > 0;
                    if (next < 0 && placed > 0 && spacing.Raw > 0) next = NearestWavePoint(anchorPoint, 0, spacingSq);
                    if (next < 0) break;
                    _spawnScore[next] = long.MinValue;
                    FixVec2 spot = _spawnPoints[next]
                        + new FixVec2(rng.NextFix(-jitter, jitter), rng.NextFix(-jitter, jitter));
                    if (_layout != null && !_layout.IsWalkable(spot, radius)) spot = _spawnPoints[next];
                    // Сдвиг не должен съедать разнос: точка разнос держит, она и берётся.
                    if (spaced && !SpacedFromGroup(spot, placed, spacingSq)) spot = _spawnPoints[next];
                    if (FixVec2.DistanceSq(spot, hero) < minSq || !WaveSpotFree(spot, radius)) continue;
                    _spawnTaken[next] = true;
                    if (placed < _groupSpots.Length) _groupSpots[placed++] = spot;
                    SpawnEncounterEnemy(spot, group.Kind, group.Elite, emerge, aggro, face, _waveMember++);
                    done = true;
                }
                if (!done) { _encounterPlan.OmittedEnemies += count - n; break; }
            }
            if (slot >= 0) _waveGroupEnd[slot] = Entities.Count;
            return anchorPoint;
        }

        /// <summary>
        /// Ближайшая к anchor ещё не отброшенная точка (счёт не MinValue), не
        /// ближе spacingSq к первым spaced местам группы; при равенстве — младшая.
        /// spaced = 0 — без разноса. -1 — такой нет.
        /// </summary>
        private int NearestWavePoint(FixVec2 anchor, int spaced, Fix64 spacingSq)
        {
            int next = -1;
            Fix64 nearest = Fix64.MaxValue;
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                if (_spawnScore[i] == long.MinValue) continue;
                Fix64 d = FixVec2.DistanceSq(_spawnPoints[i], anchor);
                if (d >= nearest || !SpacedFromGroup(_spawnPoints[i], spaced, spacingSq)) continue;
                nearest = d;
                next = i;
            }
            return next;
        }

        /// <summary>Не ближе ли spacingSq точка к первым count местам текущей группы.</summary>
        private bool SpacedFromGroup(FixVec2 point, int count, Fix64 spacingSq)
        {
            for (int m = 0; m < count; m++)
                if (FixVec2.DistanceSq(point, _groupSpots[m]) < spacingSq) return false;
            return true;
        }

        /// <summary>
        /// Разнос членов одной группы волны, метры: крупные и стрелки не встают
        /// плечом к плечу. Рой и дети Расщепеня — 0: им куча к лицу.
        /// </summary>
        public static Fix64 WaveMemberSpacing(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestGuardian: return Fix64.FromInt(3);
                case EnemyKind.ForestBud:
                case EnemyKind.ForestRootSnarer:
                case EnemyKind.ForestStonehoof:
                case EnemyKind.ForestThorncaster:
                case EnemyKind.ForestWendigo: return Fix64.FromInt(4);
                case EnemyKind.ForestSplitter: return Fix64.Ratio(5, 2);
                default: return Fix64.Zero;
            }
        }

        /// <summary>Стрелки волны — плюй-плод, Корнехват, Шипомёт: их группа встаёт дальше от героя.</summary>
        public static bool WaveKeepsBack(EnemyKind kind)
            => kind == EnemyKind.ForestBud || kind == EnemyKind.ForestRootSnarer || kind == EnemyKind.ForestThorncaster;

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
        /// земли сразу заметил героя, но не ходит и не бьёт EmergeTicks тиков
        /// и ещё разнос по номеру order в волне (EmergeStaggerTicks·order, не
        /// больше EmergeStaggerMaxTicks). Первый удар — ещё позже на
        /// (order mod FirstAttackStaggerGroups)·FirstAttackStaggerTicks; у
        /// стартовой волны — столько же от появления.
        /// </summary>
        private int SpawnEncounterEnemy(FixVec2 spot, EnemyKind kind, bool elite, bool emerge, bool aggro, FixVec2 face,
            int order)
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
            // Волна не бьёт залпом: первые удары по кругу из трёх — +0, +10, +20 тиков.
            int attackDelay = order % FirstAttackStaggerGroups * FirstAttackStaggerTicks;
            int firstAttack;
            if (emerge)
            {
                // Тик выхода уже идёт: бездействие считается со следующего.
                // Члены волны встают по одному, а не разом (4 тика на номер).
                int dormant = EmergeTicks + Math.Min(order * EmergeStaggerTicks, EmergeStaggerMaxTicks);
                _emergeUntil[id] = Tick + 1 + dormant;
                firstAttack = _emergeUntil[id] + attackDelay;
                _events.Add(SimEvent.Emerge(id, spot, dormant));
            }
            else
            {
                firstAttack = Tick + attackDelay;
                _events.Add(SimEvent.Spawn(id, spot));
            }
            if (Entities.NextAttackTick[id] < firstAttack) Entities.NextAttackTick[id] = firstAttack;
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
            if (_segmentCount > 1) { Hashing.Mix(ref hash, _segmentCount); Hashing.Mix(ref hash, _activeSegment); }
            for (int i = 0; i < Entities.Count; i++)
                if (_emergeUntil[i] > Tick) { Hashing.Mix(ref hash, i); Hashing.Mix(ref hash, _emergeUntil[i]); }
        }
    }
}
