using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// МЕТРИКИ «ОЩУЩЕНИЯ» БОЯ (поток D плана «Мобы леса v2»). Владелец не может
    /// сказать словами, чем ИИ «не Hades 2», поэтому бой меряется числами —
    /// по аренам и по уровням пачек, против полос из плана (их утверждает
    /// владелец, выбор G2):
    ///
    ///   надо уходить (MustMove)   20–40% — герой в фигуре удара, до которого
    ///                             ≤ 12 тиков (метка на земле или замах без метки:
    ///                             укус Корнеполза, клыки кабана, шип, плод);
    ///   свободная атака           ≥ 45% — враг в досягаемости сабли (2,5 м),
    ///                             и ни одна фигура не накроет героя за 24 тика;
    ///   погоня                    ≤ 25% — никого в досягаемости, герой идёт к
    ///                             ближайшему и ему не надо уходить;
    ///   атак в секунду на героя   лёгкая 0,5–1,0 / средняя 0,8–1,5 / тяжёлая 1,2–2,2;
    ///   одновременные замахи p90  ≤ 3 — разных мобов с незакрытым ударом;
    ///   моб без дела              ≤ 30% — заметил героя, не встаёт из земли, не
    ///                             оглушён, не двигается и ничего не делает;
    ///                             подряд ≤ 3 с;
    ///   паузы без атак, p90       ≤ 2,5 с — промежутки между началами атак,
    ///                             пока на поле есть враги.
    /// Плюс время убийства по видам (от первого удара героя до смерти) и
    /// худшие моменты: секунды с самым большим уроном по герою.
    ///
    /// Все доли считаются по «тикам боя» — фаза Clearing, на поле есть живой
    /// враг, не встающий из земли; «пусто» (между волнами) — отдельной долей.
    /// Проба только читает Simulation и события тика. Подключение:
    /// ARENA_BENCH_PROBES=ArenaFeelProbe к ArenaBalanceBench (отчёт — после
    /// таблиц стенда); ARENA_FEEL_OUT=папка — ещё и CSV по аренам и моментам.
    /// </summary>
    public sealed class ArenaFeelProbe : IArenaProbe
    {
        /// <summary>За сколько тиков до удара герой уже должен уходить: реакция живого игрока, как у бота.</summary>
        public const int MustMoveTicks = 12;

        /// <summary>Горизонт «нет угрозы» для свободной атаки: взмах сабли короче, но выйти потом надо успеть.</summary>
        public const int ThreatWatchTicks = 24;

        /// <summary>Окно худшего момента — секунда урона по герою.</summary>
        public const int BurstWindowTicks = Simulation.TicksPerSecond;

        private const int WindupHistogram = 16;

        /// <summary>Ячеек на вид в разбивках: EnemyKind — byte, видов меньше шестнадцати.</summary>
        public const int KindSlots = 16;

        /// <summary>
        /// Удар «внахлёст»: урон от другого моба ложится на героя не позже чем
        /// через столько тиков после предыдущего (0,2 с — быстрее, чем игрок
        /// успевает понять, откуда пришло).
        /// </summary>
        public const int StackWindowTicks = 6;

        /// <summary>Без дела ближе этого к герою — ждёт у кольца (жетон, место), дальше — отстал.</summary>
        private static readonly Fix64 IdleNearSq = Fix64.FromInt(16);

        /// <summary>Сдвиг меньше 2 см за тик — стоит.</summary>
        private static readonly Fix64 MovedSq = Fix64.Ratio(1, 2500);
        private static readonly Fix64 ChaseCos = Fix64.Ratio(1, 2);

        // ---------- итог одной арены ----------

        public sealed class ArenaFeel
        {
            public string Mode, Template, Tier, Type;
            public ulong Seed;
            public int Arena, CampLevel;
            public bool Boss;
            public ArenaProbeOutcome Outcome;
            public int ClearTicks, FightTicks, EmptyTicks, MustMove, FreeAttack, Chase, AttackStarts;
            public readonly int[] Windups = new int[WindupHistogram];
            public int EnemyTicks, IdleTicks, LongestIdle = 0;
            public string LongestIdleKind = "-";
            public int LongestIdleAt;
            public readonly List<int> Lulls = new List<int>();
            public int LongestLull, LongestLullAt;
            public readonly Dictionary<EnemyKind, List<int>> KillTicks = new Dictionary<EnemyKind, List<int>>();
            public readonly Dictionary<EnemyKind, int> StartsByKind = new Dictionary<EnemyKind, int>();
            public int HeroMaxHealth, DamageTaken;
            public int WorstBurst, WorstBurstAt, WorstBurstWindups;
            public string WorstBurstKinds = "";

            // Разбивки по виду моба (индекс — (int)EnemyKind): кто атакует, кто
            // стоит (и где), чей урон, чья угроза отнимает свободный удар,
            // за кем герой бегает. Ради них и строится список правок ИИ.
            public readonly int[] StartsBy = new int[KindSlots], EnemyTicksBy = new int[KindSlots];
            public readonly int[] IdleBy = new int[KindSlots], IdleNearBy = new int[KindSlots];
            public readonly int[] DamageBy = new int[KindSlots], BlockedBy = new int[KindSlots], ChaseBy = new int[KindSlots];

            /// <summary>Прямых попаданий мобов по герою; из них внахлёст с чужим (≤ StackWindowTicks) — и их урон.</summary>
            public int EnemyHits, StackedHits, StackedDamage, HitDamage;

            /// <summary>
            /// Тиков боя, когда герой уже связан корнями или оглушён (с прошлого
            /// тика), и урон, пришедший в эти тики, — без удара, который связал.
            /// </summary>
            public int ControlTicks, ControlDamage;

            public double FightSeconds => FightTicks / (double)Simulation.TicksPerSecond;
        }

        private readonly List<ArenaFeel> _arenas = new List<ArenaFeel>();
        private ArenaFeel _now;

        // ---------- по мобам текущей арены ----------

        private FixVec2[] _lastPosition = Array.Empty<FixVec2>();
        private bool[] _seen = Array.Empty<bool>();
        private int[] _idleStreak = Array.Empty<int>(), _firstHit = Array.Empty<int>();
        private FixVec2 _heroLast;
        private bool _controlledLast;
        private int _lastAttackTick;

        // Урон по герою за последнюю секунду: по тику и по виду источника.
        private readonly int[] _burst = new int[BurstWindowTicks];
        private readonly List<KeyValuePair<int, EnemyKind>> _burstHits = new List<KeyValuePair<int, EnemyKind>>();
        private int _burstSum;

        // Последние прямые попадания мобов по герою (тик, источник) — для «внахлёст».
        private readonly List<KeyValuePair<int, int>> _recentHits = new List<KeyValuePair<int, int>>();

        // Угрозы этого тика: фигура, сколько тиков до удара, чья.
        private readonly List<EnemyTelegraph> _threats = new List<EnemyTelegraph>();
        private readonly List<int> _threatLeft = new List<int>(), _threatSource = new List<int>();
        private readonly HashSet<int> _windupSources = new HashSet<int>();

        public IReadOnlyList<ArenaFeel> Arenas => _arenas;

        public void ArenaStarted(Simulation sim, in ArenaProbeContext context)
        {
            EntityStore e = sim.Entities;
            ArenaEncounterTemplate template = context.Run.CurrentEncounter;
            _now = new ArenaFeel
            {
                Mode = context.Immortal ? "immortal" : "carry", Seed = context.Seed, Arena = context.Arena,
                CampLevel = context.CampLevel, Boss = context.Boss, Template = context.Template,
                Tier = context.Boss ? "Boss" : template == null ? "-"
                    : template.Type == ArenaEncounterType.Elite ? "Elite" : template.Tier.ToString(),
                Type = context.Boss ? "Boss" : template != null ? template.Type.ToString() : "-",
                HeroMaxHealth = e.MaxHealth[Simulation.PlayerId],
            };
            _arenas.Add(_now);
            int capacity = e.Capacity;
            if (_lastPosition.Length < capacity)
            {
                _lastPosition = new FixVec2[capacity];
                _seen = new bool[capacity];
                _idleStreak = new int[capacity];
                _firstHit = new int[capacity];
            }
            Array.Clear(_seen, 0, _seen.Length);
            Array.Clear(_idleStreak, 0, _idleStreak.Length);
            for (int i = 0; i < _firstHit.Length; i++) _firstHit[i] = -1;
            Array.Clear(_burst, 0, _burst.Length);
            _burstHits.Clear();
            _burstSum = 0;
            _recentHits.Clear();
            _heroLast = e.Position[Simulation.PlayerId];
            _controlledLast = false;
            _lastAttackTick = -1;
        }

        public void Tick(Simulation sim, in ArenaProbeContext context)
        {
            if (_now == null || context.PhaseBefore != RunPhase.Clearing) return;
            EntityStore e = sim.Entities;
            const int hero = Simulation.PlayerId;
            int tick = context.ClearTicks;
            _now.ClearTicks = tick;
            FixVec2 heroAt = e.Position[hero];
            Fix64 heroBody = e.BodyRadius[hero];

            int damageBefore = _now.DamageTaken;
            ReadEvents(sim, tick);
            // Связан или оглушён уже с прошлого тика: увернуться нельзя, урон в этот
            // тик — «наказание без выбора». Сам удар, который связал, сюда не входит.
            bool controlled = _controlledLast;
            _controlledLast = sim.HeroRooted || sim.HeroStunned;

            int alive = 0, nearest = -1;
            bool inReach = false;
            Fix64 nearestSq = Fix64.MaxValue, reachSq = Simulation.AutoAttackRange * Simulation.AutoAttackRange;
            for (int i = 1; i < e.Count; i++)
            {
                if (!Hostile(sim, i)) continue;
                alive++;
                Fix64 d = FixVec2.DistanceSq(heroAt, e.Position[i]);
                if (d <= reachSq) inReach = true;
                if (d < nearestSq) { nearestSq = d; nearest = i; }
            }
            FixVec2 heroMove = heroAt - _heroLast;
            _heroLast = heroAt;
            if (alive == 0 || !e.Alive[hero])
            {
                _now.EmptyTicks++;
                // Пауза между волнами — не «пауза без атак»: враги ещё не пришли.
                _lastAttackTick = -1;
                ShiftBurst();
                return;
            }
            _now.FightTicks++;
            if (controlled)
            {
                _now.ControlTicks++;
                _now.ControlDamage += _now.DamageTaken - damageBefore;
            }

            CollectThreats(sim);
            bool mustMove = false, threatened = false;
            int soonest = int.MaxValue, soonestSource = -1;
            _windupSources.Clear();
            for (int k = 0; k < _threats.Count; k++)
            {
                int left = _threatLeft[k];
                if (left < 0) continue;
                _windupSources.Add(_threatSource[k]);
                if (left > ThreatWatchTicks || !Simulation.TelegraphContains(_threats[k], heroAt, heroBody)) continue;
                threatened = true;
                if (left <= MustMoveTicks) mustMove = true;
                if (left < soonest) { soonest = left; soonestSource = _threatSource[k]; }
            }
            if (mustMove) _now.MustMove++;
            if (inReach && !threatened) _now.FreeAttack++;
            // Враг в досягаемости, но ударить некогда: чья угроза мешает (ближайшая по времени).
            if (inReach && threatened) _now.BlockedBy[KindSlot(sim, soonestSource)]++;
            if (!inReach && !mustMove && nearest > 0 && heroMove.LengthSq > MovedSq)
            {
                FixVec2 toward = e.Position[nearest] - heroAt;
                Fix64 along = FixVec2.Dot(heroMove, toward);
                if (along.Raw > 0 && along * along >= ChaseCos * ChaseCos * heroMove.LengthSq * toward.LengthSq)
                {
                    _now.Chase++;
                    _now.ChaseBy[KindSlot(sim, nearest)]++;
                }
            }
            _now.Windups[Math.Min(WindupHistogram - 1, _windupSources.Count)]++;

            // Мобы без дела.
            for (int i = 1; i < e.Count; i++)
            {
                if (!Hostile(sim, i)) { _seen[i] = false; _idleStreak[i] = 0; continue; }
                FixVec2 at = e.Position[i];
                bool moved = _seen[i] && FixVec2.DistanceSq(at, _lastPosition[i]) > MovedSq;
                bool first = !_seen[i];
                _seen[i] = true;
                _lastPosition[i] = at;
                if (first || !e.Aggro[i] || sim.Statuses.IsStunned(i, sim.Tick)) { _idleStreak[i] = 0; continue; }
                int kindSlot = KindSlot(sim, i);
                _now.EnemyTicks++;
                _now.EnemyTicksBy[kindSlot]++;
                if (moved || Busy(sim, i)) { _idleStreak[i] = 0; continue; }
                _now.IdleTicks++;
                _now.IdleBy[kindSlot]++;
                if (FixVec2.DistanceSq(at, heroAt) <= IdleNearSq) _now.IdleNearBy[kindSlot]++;
                if (++_idleStreak[i] > _now.LongestIdle)
                {
                    _now.LongestIdle = _idleStreak[i];
                    _now.LongestIdleKind = e.Kind[i].ToString();
                    _now.LongestIdleAt = tick;
                }
            }

            if (_burstSum > _now.WorstBurst)
            {
                _now.WorstBurst = _burstSum;
                _now.WorstBurstAt = tick;
                _now.WorstBurstWindups = _windupSources.Count;
                _now.WorstBurstKinds = BurstKinds();
            }
            ShiftBurst();
        }

        public void ArenaEnded(Simulation sim, in ArenaProbeContext context)
        {
            if (_now == null) return;
            _now.Outcome = context.Outcome;
            // Хвост: враги ещё были, а атак после последней не случилось.
            if (_lastAttackTick >= 0 && context.Outcome != ArenaProbeOutcome.Cleared)
                AddLull(_now.ClearTicks - _lastAttackTick, _now.ClearTicks);
            _now = null;
        }

        // ---------- события тика ----------

        private void ReadEvents(Simulation sim, int tick)
        {
            EntityStore e = sim.Entities;
            var events = sim.Events;
            int slot = tick % BurstWindowTicks;
            for (int n = 0; n < events.Count; n++)
            {
                SimEvent ev = events[n];
                if (IsAttackStart(in ev) && ev.Source > 0 && ev.Source < e.Count)
                {
                    _now.AttackStarts++;
                    EnemyKind kind = e.Kind[ev.Source];
                    _now.StartsByKind[kind] = (_now.StartsByKind.TryGetValue(kind, out int c) ? c : 0) + 1;
                    _now.StartsBy[KindSlot(sim, ev.Source)]++;
                    if (_lastAttackTick >= 0) AddLull(tick - _lastAttackTick, tick);
                    _lastAttackTick = tick;
                }
                if ((ev.Type == SimEventType.Damage || ev.Type == SimEventType.DamageOverTime) && ev.Amount > 0)
                {
                    if (ev.Target == Simulation.PlayerId)
                    {
                        _now.DamageTaken += ev.Amount;
                        _burst[slot] += ev.Amount;
                        _burstSum += ev.Amount;
                        _burstHits.Add(new KeyValuePair<int, EnemyKind>(tick,
                            ev.Source > 0 && ev.Source < e.Count ? e.Kind[ev.Source] : EnemyKind.None));
                        _now.DamageBy[KindSlot(sim, ev.Source)] += ev.Amount;
                        if (ev.Type == SimEventType.Damage && ev.Source > 0 && ev.Source < e.Count) CountStack(ev.Source, ev.Amount, tick);
                    }
                    else if (ev.Source == Simulation.PlayerId && ev.Target > 0 && ev.Target < _firstHit.Length
                             && _firstHit[ev.Target] < 0)
                        _firstHit[ev.Target] = tick;
                }
                if (ev.Type == SimEventType.Death && ev.Target > 0 && ev.Target < _firstHit.Length && _firstHit[ev.Target] >= 0)
                {
                    EnemyKind kind = e.Kind[ev.Target];
                    if (!_now.KillTicks.TryGetValue(kind, out var list)) _now.KillTicks[kind] = list = new List<int>();
                    list.Add(tick - _firstHit[ev.Target]);
                    _firstHit[ev.Target] = -1;
                }
            }
        }

        /// <summary>
        /// Начало атаки на героя: ближний замах, залп Плюй-плода, действие
        /// Вендиго и Камнекопыта, действия новых мобов. Лечение Корнехвата —
        /// не атака.
        /// </summary>
        private static bool IsAttackStart(in SimEvent ev)
        {
            switch (ev.Type)
            {
                case SimEventType.Attack:
                    return ev.Target == Simulation.PlayerId;
                case SimEventType.ForestBudVolleyStarted:
                case SimEventType.WendigoStarted:
                case SimEventType.StonehoofStarted:
                    return true;
                case SimEventType.EnemyActionStarted:
                    return ev.ActionVariant != (int)EnemyActionKind.SnarerMend;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Прямое попадание моба source по герою: легло ли оно внахлёст — не
        /// позже StackWindowTicks после попадания ДРУГОГО моба. Такие пары
        /// игрок не разводит перекатом: два удара читаются как один.
        /// </summary>
        private void CountStack(int source, int amount, int tick)
        {
            int drop = 0;
            while (drop < _recentHits.Count && tick - _recentHits[drop].Key > StackWindowTicks) drop++;
            if (drop > 0) _recentHits.RemoveRange(0, drop);
            bool stacked = false;
            foreach (var hit in _recentHits)
                if (hit.Value != source) { stacked = true; break; }
            _now.EnemyHits++;
            _now.HitDamage += amount;
            if (stacked) { _now.StackedHits++; _now.StackedDamage += amount; }
            _recentHits.Add(new KeyValuePair<int, int>(tick, source));
        }

        /// <summary>Ячейка вида для разбивок; 0 (None) — не моб или вне стора.</summary>
        private static int KindSlot(Simulation sim, int id)
        {
            if (id <= 0 || id >= sim.Entities.Count) return 0;
            int kind = (int)sim.Entities.Kind[id];
            return kind < KindSlots ? kind : 0;
        }

        private void AddLull(int ticks, int at)
        {
            if (ticks <= 0) return;
            _now.Lulls.Add(ticks);
            if (ticks > _now.LongestLull) { _now.LongestLull = ticks; _now.LongestLullAt = at; }
        }

        private void ShiftBurst()
        {
            // Слот следующего тика освобождается: окно — ровно секунда.
            int next = (_now.ClearTicks + 1) % BurstWindowTicks;
            _burstSum -= _burst[next];
            _burst[next] = 0;
            // Попадания лежат по порядку тиков: вышедшие из окна — в начале списка.
            int oldest = _now.ClearTicks + 1 - BurstWindowTicks, drop = 0;
            while (drop < _burstHits.Count && _burstHits[drop].Key <= oldest) drop++;
            if (drop > 0) _burstHits.RemoveRange(0, drop);
        }

        private string BurstKinds()
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var hit in _burstHits)
            {
                string name = Short(hit.Value);
                counts[name] = (counts.TryGetValue(name, out int c) ? c : 0) + 1;
            }
            var sb = new StringBuilder();
            foreach (var pair in counts) sb.Append(sb.Length > 0 ? " " : "").Append(pair.Key).Append('×').Append(pair.Value);
            return sb.ToString();
        }

        // ---------- состояние мобов ----------

        private static bool Hostile(Simulation sim, int i)
        {
            EntityStore e = sim.Entities;
            return e.Alive[i] && e.Side[i] != Faction.Wole && !sim.IsEmerging(i);
        }

        /// <summary>Моб занят: замах или восстановление, своё действие, снаряд в полёте.</summary>
        private static bool Busy(Simulation sim, int i)
        {
            if (sim.TryGetEnemySwing(i, out EnemySwingState swing) && sim.Tick < swing.RecoverUntil) return true;
            switch (sim.Entities.Kind[i])
            {
                case EnemyKind.ForestWendigo:
                    return sim.TryGetWendigoAction(i, out _);
                case EnemyKind.ForestStonehoof:
                    return sim.TryGetStonehoofAction(i, out _) || sim.TryGetStonehoofTusk(i, out _);
                case EnemyKind.ForestThorncaster:
                    return sim.TryGetThorncasterAction(i, out ThorncasterState thorn) && thorn.Action != ThornAction.None
                        && sim.Tick <= thorn.EndTick || sim.TryGetThornShot(i, out _);
                case EnemyKind.ForestRootSnarer:
                    return sim.TryGetRootSnarerAction(i, out RootSnarerState snarer) && sim.Tick <= snarer.EndTick;
                case EnemyKind.ForestSplitter:
                    return sim.TryGetSplitterRoll(i, out _);
                case EnemyKind.ForestBud:
                    return sim.TryGetForestBudAttack(i, out _);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Всё, что ударит героя, если он останется: общий список меток (там и
        /// секторы Хранителя с Расщепенем), укус Корнеполза и детёныша (метки
        /// нет), шип Шипомёта в замахе и в полёте, клыки кабана, падающие плоды.
        /// Так же видит угрозы бот стенда (ArenaBot.CollectThreats).
        /// </summary>
        private void CollectThreats(Simulation sim)
        {
            _threats.Clear(); _threatLeft.Clear(); _threatSource.Clear();
            EntityStore e = sim.Entities;
            int now = sim.Tick;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out EnemyTelegraph t) && t.IsActive)
                    AddThreat(t, t.ImpactTick - now, t.Source);
            FixVec2 heroAt = e.Position[Simulation.PlayerId];
            Fix64 body = e.BodyRadius[Simulation.PlayerId];
            for (int id = 1; id < e.Count; id++)
            {
                EnemyKind kind = e.Kind[id];
                if (e.Alive[id] && sim.TryGetEnemySwing(id, out EnemySwingState swing) && !swing.HitResolved
                    && swing.Telegraph < 0 && now <= swing.ImpactTick)
                    AddThreat(sim.EnemySwingShape(id, e.Position[id], swing.Direction), swing.ImpactTick - now, id);
                if (kind == EnemyKind.ForestStonehoof && e.Alive[id] && sim.TryGetStonehoofTusk(id, out StonehoofTuskState tusk)
                    && !tusk.HitResolved)
                    AddThreat(Simulation.StonehoofTuskShape(e.Position[id], tusk.Direction), tusk.ImpactTick - now, id);
                if (kind != EnemyKind.ForestThorncaster) continue;
                ThornShotState shot;
                if (!sim.TryGetThornShot(id, out shot))
                {
                    if (!sim.TryGetThorncasterAction(id, out ThorncasterState a) || a.Action != ThornAction.Shot
                        || a.Erupted > 0) continue;
                    shot = new ThornShotState
                    {
                        Serial = a.Serial, ReleaseTick = a.ImpactTick,
                        Origin = a.Origin + a.Direction * Simulation.ThornShotStartOffset,
                        Direction = a.Direction, Length = sim.ThornShotFlightLength(a.Origin, a.Direction),
                    };
                }
                Fix64 along = FixVec2.Dot(heroAt - shot.Origin, shot.Direction) - body;
                int arrive = shot.ReleaseTick + Math.Max(0, (along / Simulation.ThornShotSpeed).ToInt());
                AddThreat(Simulation.ThornShotSweep(in shot, shot.Travelled, shot.Length), arrive - now, id);
            }
            if (sim.ForestFruitActiveCount == 0) return;
            for (int slot = 0; slot < sim.ForestFruitCapacity; slot++)
                if (sim.TryGetForestFruit(slot, out ForestFruitState fruit))
                    AddThreat(EnemyTelegraph.Circle(fruit.Target, fruit.Radius), fruit.ImpactTick - now, fruit.Source);
        }

        private void AddThreat(in EnemyTelegraph shape, int left, int source)
        {
            _threats.Add(shape);
            _threatLeft.Add(left);
            _threatSource.Add(source);
        }

        // ---------- отчёт ----------

        /// <summary>Полосы плана по уровню пачки: атак в секунду.</summary>
        private static bool PressureBand(string tier, out double low, out double high)
        {
            switch (tier)
            {
                case "Easy": low = 0.5; high = 1.0; return true;
                case "Medium": low = 0.8; high = 1.5; return true;
                case "Hard":
                case "Elite":
                case "Boss": low = 1.2; high = 2.2; return true;
                default: low = high = 0; return false;
            }
        }

        public void AppendReport(StringBuilder report)
        {
            report.AppendLine("ARENA FEEL (ArenaFeelProbe): " + _arenas.Count + " arenas. Bands: MustMove 20-40%, FreeAttack >=45%, "
                + "Chase <=25%, attacks/s easy 0.5-1.0 / medium 0.8-1.5 / hard 1.2-2.2, windups p90 <=3, "
                + "idle <=30% & streak <=3 s, lull p90 <=2.5 s");
            foreach (string mode in new[] { "carry", "immortal" })
            {
                var rows = _arenas.FindAll(a => a.Mode == mode && a.FightTicks > 0);
                if (rows.Count == 0) continue;
                report.AppendLine();
                report.AppendLine(mode.ToUpperInvariant() + " - by arena");
                AppendHeader(report, "Arena");
                for (int arena = 1; arena <= 9; arena++)
                    AppendRow(report, arena == 9 ? "Boss" : "A" + arena, rows.FindAll(a => a.Arena == arena), null);
                report.AppendLine(mode.ToUpperInvariant() + " - by tier");
                AppendHeader(report, "Tier");
                foreach (string tier in new[] { "Easy", "Medium", "Hard", "Elite", "Boss" })
                    AppendRow(report, tier, rows.FindAll(a => a.Tier == tier), tier);
                report.AppendLine(mode.ToUpperInvariant() + " - by type");
                AppendHeader(report, "Type");
                foreach (string type in new[] { "Normal", "Ambush", "Survival", "Elite", "Boss" })
                    AppendRow(report, type, rows.FindAll(a => a.Type == type), null);
                AppendKills(report, rows);
                AppendWho(report, rows);
                AppendMoments(report, rows);
            }
            WriteCsv();
        }

        private static void AppendHeader(StringBuilder report, string first)
            => report.AppendLine(first.PadRight(7) + "   n  Fight s  Empty%  MustMove%  Free%  Chase%  Atk/s [p25-p75]   Band      "
                + "Windups p90  Idle%  IdleRun p50/p90 s  Gap p90 s  LongLull p50/p90 s  Died");

        private static void AppendRow(StringBuilder report, string name, List<ArenaFeel> rows, string tier)
        {
            if (rows.Count == 0) { report.AppendLine(name.PadRight(7) + "   0"); return; }
            double fight = 0, empty = 0, must = 0, free = 0, chase = 0, enemy = 0, idle = 0;
            var windups = new int[WindupHistogram];
            var lulls = new List<double>();
            foreach (var a in rows)
            {
                fight += a.FightTicks; empty += a.EmptyTicks; must += a.MustMove; free += a.FreeAttack; chase += a.Chase;
                enemy += a.EnemyTicks; idle += a.IdleTicks;
                for (int k = 0; k < WindupHistogram; k++) windups[k] += a.Windups[k];
                foreach (int l in a.Lulls) lulls.Add(l / (double)Simulation.TicksPerSecond);
            }
            var pressure = Values(rows, a => a.AttackStarts / Math.Max(1e-9, a.FightSeconds));
            double atk = Percentile(pressure, 0.5);
            string band = "-";
            if (tier != null && PressureBand(tier, out double low, out double high))
                band = (atk < low ? "LOW" : atk > high ? "HIGH" : "ok") + " " + F(low) + "-" + F(high);
            var line = new StringBuilder(name.PadRight(7));
            line.Append(rows.Count.ToString().PadLeft(4))
                .Append(F(Percentile(Values(rows, a => a.FightSeconds), 0.5)).PadLeft(9))
                .Append(Pct(empty, fight + empty).PadLeft(8))
                .Append(Pct(must, fight).PadLeft(11))
                .Append(Pct(free, fight).PadLeft(7))
                .Append(Pct(chase, fight).PadLeft(8))
                .Append((F(atk) + " [" + F(Percentile(pressure, 0.25)) + "-" + F(Percentile(pressure, 0.75)) + "]").PadLeft(18))
                .Append("   ").Append(band.PadRight(10))
                .Append(HistogramPercentile(windups, 0.9).ToString().PadLeft(11))
                .Append(Pct(idle, enemy).PadLeft(7))
                .Append((F(Percentile(Values(rows, a => a.LongestIdle / (double)Simulation.TicksPerSecond), 0.5)) + "/"
                    + F(Percentile(Values(rows, a => a.LongestIdle / (double)Simulation.TicksPerSecond), 0.9))).PadLeft(19))
                .Append(F(Percentile(lulls, 0.9)).PadLeft(11))
                .Append((F(Percentile(Values(rows, a => a.LongestLull / (double)Simulation.TicksPerSecond), 0.5)) + "/"
                    + F(Percentile(Values(rows, a => a.LongestLull / (double)Simulation.TicksPerSecond), 0.9))).PadLeft(20))
                .Append(rows.FindAll(a => a.Outcome == ArenaProbeOutcome.Died).Count.ToString().PadLeft(6));
            report.AppendLine(line.ToString());
        }

        private static void AppendKills(StringBuilder report, List<ArenaFeel> rows)
        {
            var byKind = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);
            var starts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            double fight = 0;
            foreach (var a in rows)
            {
                fight += a.FightSeconds;
                foreach (var pair in a.KillTicks)
                {
                    string name = pair.Key.ToString();
                    if (!byKind.TryGetValue(name, out var list)) byKind[name] = list = new List<double>();
                    foreach (int t in pair.Value) list.Add(t / (double)Simulation.TicksPerSecond);
                }
                foreach (var pair in a.StartsByKind)
                    starts[pair.Key.ToString()] = (starts.TryGetValue(pair.Key.ToString(), out int c) ? c : 0) + pair.Value;
            }
            report.AppendLine("Time to kill (first hero hit -> death), s:  kind  n  p50  p90");
            foreach (var pair in byKind)
                report.AppendLine("  " + pair.Key.PadRight(18) + pair.Value.Count.ToString().PadLeft(6)
                    + F(Percentile(pair.Value, 0.5)).PadLeft(7) + F(Percentile(pair.Value, 0.9)).PadLeft(7));
            var split = new StringBuilder("Attack starts by kind (per fight minute): ");
            foreach (var pair in starts) split.Append(pair.Key).Append(' ').Append(F(pair.Value * 60 / Math.Max(1e-9, fight))).Append("  ");
            report.AppendLine(split.ToString());
        }

        /// <summary>
        /// По уровням пачек: удары внахлёст и разбивка по видам — атак в минуту,
        /// без дела (и доля «ждёт у героя» ≤ 4 м), доля урона, чья угроза отнимает
        /// свободный удар, за кем герой бегает.
        /// </summary>
        private static void AppendWho(StringBuilder report, List<ArenaFeel> rows)
        {
            report.AppendLine("Who, by tier: kind  atk/min  idle%  idleNear%  dmg%  blocksFree%  chased%");
            foreach (string tier in new[] { "Easy", "Medium", "Hard", "Elite", "Boss" })
            {
                var group = rows.FindAll(a => a.Tier == tier);
                if (group.Count == 0) continue;
                var starts = new long[KindSlots]; var enemy = new long[KindSlots]; var idle = new long[KindSlots];
                var near = new long[KindSlots]; var dmg = new long[KindSlots]; var blocked = new long[KindSlots]; var chase = new long[KindSlots];
                long hits = 0, stacked = 0, stackedDamage = 0, hitDamage = 0, controlTicks = 0, controlDamage = 0, taken = 0, fightTicks = 0;
                double fight = 0;
                foreach (var a in group)
                {
                    fight += a.FightSeconds;
                    hits += a.EnemyHits; stacked += a.StackedHits; stackedDamage += a.StackedDamage; hitDamage += a.HitDamage;
                    controlTicks += a.ControlTicks; controlDamage += a.ControlDamage; taken += a.DamageTaken; fightTicks += a.FightTicks;
                    for (int k = 0; k < KindSlots; k++)
                    {
                        starts[k] += a.StartsBy[k]; enemy[k] += a.EnemyTicksBy[k]; idle[k] += a.IdleBy[k];
                        near[k] += a.IdleNearBy[k]; dmg[k] += a.DamageBy[k]; blocked[k] += a.BlockedBy[k]; chase[k] += a.ChaseBy[k];
                    }
                }
                long dmgAll = 0, blockedAll = 0, chaseAll = 0;
                for (int k = 0; k < KindSlots; k++) { dmgAll += dmg[k]; blockedAll += blocked[k]; chaseAll += chase[k]; }
                report.AppendLine("  " + tier + ": direct hits " + hits + ", stacked (<= " + StackWindowTicks + " ticks after another mob) "
                    + Pct(stacked, hits) + "% of hits, " + Pct(stackedDamage, hitDamage) + "% of hit damage; rooted/stunned "
                    + Pct(controlTicks, fightTicks) + "% of fight, " + Pct(controlDamage, taken) + "% of damage taken then");
                for (int k = 0; k < KindSlots; k++)
                {
                    if (enemy[k] == 0 && dmg[k] == 0 && starts[k] == 0) continue;
                    report.AppendLine("    " + Short((EnemyKind)k).PadRight(12)
                        + F(starts[k] * 60 / Math.Max(1e-9, fight)).PadLeft(8)
                        + Pct(idle[k], enemy[k]).PadLeft(7) + Pct(near[k], idle[k]).PadLeft(11)
                        + Pct(dmg[k], dmgAll).PadLeft(6) + Pct(blocked[k], blockedAll).PadLeft(13) + Pct(chase[k], chaseAll).PadLeft(9));
                }
            }
        }

        private static void AppendMoments(StringBuilder report, List<ArenaFeel> rows)
        {
            var bursts = new List<ArenaFeel>(rows);
            bursts.Sort((x, y) => (y.WorstBurst / (double)y.HeroMaxHealth).CompareTo(x.WorstBurst / (double)x.HeroMaxHealth));
            report.AppendLine("Worst moments (1 s of damage to the hero, % max HP): seed arena template tier @tick (s) dmg% windups sources");
            for (int k = 0; k < Math.Min(10, bursts.Count); k++)
            {
                var a = bursts[k];
                report.AppendLine("  seed " + a.Seed + " A" + a.Arena + " " + a.Template + " " + a.Tier + " @" + a.WorstBurstAt
                    + " (" + F(a.WorstBurstAt / (double)Simulation.TicksPerSecond) + " s) "
                    + F(100.0 * a.WorstBurst / a.HeroMaxHealth) + "% windups " + a.WorstBurstWindups + " [" + a.WorstBurstKinds + "]"
                    + (a.Outcome == ArenaProbeOutcome.Died ? " DIED" : ""));
            }
            var lulls = new List<ArenaFeel>(rows);
            lulls.Sort((x, y) => y.LongestLull.CompareTo(x.LongestLull));
            report.AppendLine("Longest lulls without attack starts: seed arena template @tick length s");
            for (int k = 0; k < Math.Min(5, lulls.Count); k++)
                report.AppendLine("  seed " + lulls[k].Seed + " A" + lulls[k].Arena + " " + lulls[k].Template + " @" + lulls[k].LongestLullAt
                    + " " + F(lulls[k].LongestLull / (double)Simulation.TicksPerSecond));
            var idle = new List<ArenaFeel>(rows);
            idle.Sort((x, y) => y.LongestIdle.CompareTo(x.LongestIdle));
            report.AppendLine("Longest idle mob streaks: seed arena template @tick kind length s");
            for (int k = 0; k < Math.Min(5, idle.Count); k++)
                report.AppendLine("  seed " + idle[k].Seed + " A" + idle[k].Arena + " " + idle[k].Template + " @" + idle[k].LongestIdleAt
                    + " " + idle[k].LongestIdleKind + " " + F(idle[k].LongestIdle / (double)Simulation.TicksPerSecond));
        }

        /// <summary>ARENA_FEEL_OUT — папка: строка на арену (сырые суммы, чтобы сводить процессы).</summary>
        private void WriteCsv()
        {
            string folder = Environment.GetEnvironmentVariable("ARENA_FEEL_OUT");
            if (string.IsNullOrEmpty(folder) || _arenas.Count == 0) return;
            Directory.CreateDirectory(folder);
            var csv = new StringBuilder("mode,seed,arena,template,tier,type,outcome,clear_ticks,fight_ticks,empty_ticks,must_move,"
                + "free_attack,chase,attack_starts,enemy_ticks,idle_ticks,longest_idle,longest_idle_kind,longest_idle_at,"
                + "longest_lull,longest_lull_at,hero_max_hp,damage_taken,worst_burst,worst_burst_at,worst_burst_windups,"
                + "worst_burst_kinds,windups,lulls,kills,starts_by,enemy_by,idle_by,idle_near_by,dmg_by,blocked_by,chase_by,"
                + "enemy_hits,stacked_hits,stacked_damage,hit_damage,control_ticks,control_damage\n");
            foreach (var a in _arenas)
            {
                var kills = new StringBuilder();
                foreach (var pair in a.KillTicks)
                    foreach (int t in pair.Value) kills.Append(kills.Length > 0 ? ";" : "").Append(pair.Key).Append(':').Append(t);
                csv.Append(string.Join(",", new[]
                {
                    a.Mode, a.Seed.ToString(CultureInfo.InvariantCulture), I(a.Arena), a.Template, a.Tier, a.Type, a.Outcome.ToString(),
                    I(a.ClearTicks), I(a.FightTicks), I(a.EmptyTicks), I(a.MustMove), I(a.FreeAttack), I(a.Chase),
                    I(a.AttackStarts), I(a.EnemyTicks), I(a.IdleTicks), I(a.LongestIdle), a.LongestIdleKind, I(a.LongestIdleAt),
                    I(a.LongestLull), I(a.LongestLullAt), I(a.HeroMaxHealth), I(a.DamageTaken), I(a.WorstBurst),
                    I(a.WorstBurstAt), I(a.WorstBurstWindups), a.WorstBurstKinds.Replace(',', ' '),
                    string.Join(";", Array.ConvertAll(a.Windups, I)), string.Join(";", a.Lulls.ConvertAll(I)), kills.ToString(),
                    Join(a.StartsBy), Join(a.EnemyTicksBy), Join(a.IdleBy), Join(a.IdleNearBy), Join(a.DamageBy),
                    Join(a.BlockedBy), Join(a.ChaseBy), I(a.EnemyHits), I(a.StackedHits), I(a.StackedDamage), I(a.HitDamage),
                    I(a.ControlTicks), I(a.ControlDamage),
                })).Append('\n');
            }
            string first = _arenas[0].Seed.ToString(CultureInfo.InvariantCulture);
            string last = _arenas[_arenas.Count - 1].Seed.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(folder, "feel-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "-s" + first + "-" + last + ".csv"), csv.ToString());
        }

        // ---------- мелочи ----------

        private static string Short(EnemyKind kind) => kind.ToString().Replace("Forest", "");

        private static int HistogramPercentile(int[] histogram, double p)
        {
            long total = 0;
            foreach (int c in histogram) total += c;
            if (total == 0) return 0;
            long need = (long)Math.Ceiling(p * total), seen = 0;
            for (int k = 0; k < histogram.Length; k++)
            {
                seen += histogram[k];
                if (seen >= need) return k;
            }
            return histogram.Length - 1;
        }

        private static List<double> Values(List<ArenaFeel> rows, Func<ArenaFeel, double> pick)
        {
            var result = new List<double>(rows.Count);
            foreach (var r in rows) result.Add(pick(r));
            return result;
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = new List<double>(values);
            sorted.Sort();
            double rank = p * (sorted.Count - 1);
            int low = (int)Math.Floor(rank), high = (int)Math.Ceiling(rank);
            return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
        }

        private static string Pct(double part, double whole) => whole > 0 ? (100 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) : "-";

        private static string F(double value)
            => double.IsNaN(value) ? "-" : value.ToString(Math.Abs(value) >= 100 ? "0" : "0.##", CultureInfo.InvariantCulture);

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Join(int[] values) => string.Join(";", Array.ConvertAll(values, I));
    }
}
