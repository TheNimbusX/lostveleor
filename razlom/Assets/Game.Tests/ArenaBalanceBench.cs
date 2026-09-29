using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// СТЕНД БАЛАНСА АРЕН (план «Мобы леса», стадии 0 и 2).
    ///
    /// Бот в настоящей симуляции проходит забег Meadow аренами 1…9, девятая —
    /// временный босс. Встречи арен — шаблоны из плана забега (стадия 6):
    /// волны, засада, выживание, элита. Бот бьёт ближайшего, жмёт способности из слотов по
    /// готовности, выходит из метки на земле, которая упадёт в ближайшие
    /// 12 тиков (реакция живого игрока), берёт родник, если здоровья меньше
    /// 60%, иначе способность или усиление с экрана награды и обычную ветку
    /// (не «Сложно»). Всё идёт через GameSession, как в игре: здоровье
    /// переносится между аренами, опыт растит уровень лагеря по ходу забега.
    ///
    /// ДВА ПРОХОДА НА СИД. «Перенос» — честный забег до смерти. «Бессмертный» —
    /// здоровье доливается после каждого тика: иначе время и урон поздних арен
    /// мерились бы только на выживших, и чем хуже баланс, тем меньше строк.
    ///
    /// Это замер, а не проверка: [Explicit], запускается руками. Таблица — в
    /// вывод теста, строки по аренам — в artifacts/balance/bench-&lt;дата&gt;.csv.
    /// Переменные окружения: ARENA_BENCH_SEEDS (20), ARENA_BENCH_LEVELS (1,5,10),
    /// ARENA_BENCH_MODES (carry,immortal), ARENA_BENCH_OUT — папка для CSV,
    /// ARENA_BENCH_DODGE_TICKS (12) — за сколько тиков до удара бот видит метку
    /// (и шип выстрела Шипомёта: метки у него нет, бот уходит с его пути).
    /// Корнехват, Расщепень и Шипомёт в игре с 27.09 — план забега тот же, что
    /// у игры. Урон по герою делится по видам, и у Шипомёта, Корнехвата,
    /// Расщепня и его детёнышей — свои столбцы; с этапа 0 «Мобов леса v2» —
    /// и у Камнекопыта с Вендиго (раньше шли в «прочее»).
    ///
    /// ПАРАЛЛЕЛЬНО. ARENA_BENCH_SEED_FROM (1) — номер первого сида: процессы
    /// с SEED_FROM=1, 76, 151, 226 и SEEDS=75 вместе дают 300 сидов, а CSV
    /// каждого ложится в свой файл bench-&lt;дата&gt;-s&lt;первый&gt;-&lt;последний&gt;.csv.
    /// Бот — в ArenaBot.cs. Замеры сверх таблицы (метрики «ощущения» боя)
    /// подключаются через IArenaProbe: ARENA_BENCH_PROBES — имена классов
    /// через запятую (с конструктором без параметров), или свой тест зовёт
    /// PlaySeed с пробами напрямую.
    /// </summary>
    public class ArenaBalanceBench
    {
        /// <summary>Восемь арен и босс — лес по документу владельца (стадия 6 плана).</summary>
        private const int ArenaCount = 9;

        /// <summary>Цели владельца, секунды боя: А1…А8 и босс.</summary>
        private static readonly int[,] TargetSeconds =
        {
            { 25, 35 }, { 30, 40 }, { 40, 50 }, { 45, 55 }, { 50, 70 },
            { 50, 65 }, { 60, 75 }, { 60, 80 }, { 150, 195 },
        };

        // Застрявший бот не должен вешать прогон: арена дольше этого — «время вышло».
        private const int ArenaTimeoutTicks = 360 * Simulation.TicksPerSecond;
        private const int BossTimeoutTicks = 600 * Simulation.TicksPerSecond;
        private const int ExitTimeoutTicks = 90 * Simulation.TicksPerSecond;

        [Test, Explicit("Замер баланса арен: время, урон, перенос здоровья, смерти")]
        public void RecordArenaClearTimesAndDamage()
        {
            int seeds = EnvInt("ARENA_BENCH_SEEDS", 20);
            int seedFrom = EnvInt("ARENA_BENCH_SEED_FROM", 1);
            int[] levels = EnvInts("ARENA_BENCH_LEVELS", new[] { 1, 5, 10 });
            string modes = Environment.GetEnvironmentVariable("ARENA_BENCH_MODES") ?? "carry,immortal";
            IArenaProbe[] probes = EnvProbes("ARENA_BENCH_PROBES");
            var meadow = Meadow();
            var records = new List<ArenaRecord>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (string mode in new[] { "carry", "immortal" })
            {
                if (modes.IndexOf(mode, StringComparison.Ordinal) < 0) continue;
                foreach (int level in levels)
                    for (int s = 0; s < seeds; s++)
                        records.AddRange(PlayRun(meadow, level, (ulong)(seedFrom + s), mode == "immortal", probes));
            }

            var report = new StringBuilder();
            report.AppendLine("ArenaBalanceBench: " + seeds + " seeds"
                + (seedFrom != 1 ? " (" + seedFrom + "-" + (seedFrom + seeds - 1) + ")" : "")
                + ", camp levels " + string.Join("/", levels)
                + ", " + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s wall");
            foreach (string mode in new[] { "carry", "immortal" })
                foreach (int level in levels)
                    AppendTable(report, records, mode, level, seeds);
            foreach (int level in levels)
                AppendTemplates(report, records, level);
            foreach (IArenaProbe probe in probes)
            {
                report.AppendLine();
                probe.AppendReport(report);
            }
            string path = WriteCsv(records, seedFrom == 1 ? "" : "-s" + seedFrom + "-" + (seedFrom + seeds - 1));
            report.AppendLine("CSV: " + path);
            TestContext.WriteLine(report.ToString());
            Assert.That(records.Count, Is.GreaterThan(0));
        }

        // ---------- забег ----------

        private sealed class ArenaRecord
        {
            public string Mode, Template = "-";
            public int CampLevel, Arena, RouteSize, HeroLevel, HeroMaxHealth, HealthStart, HealthEnd = -1;
            public ulong Seed;
            public bool Boss, Cleared, Died, TimedOut, SpringOffered, SpringTaken;
            public int ClearTicks, ExitTicks, Enemies, Elites, EnemyHealth, Abilities, Talents;
            public int DamageTaken, Hits, DamageGuardian, DamageSwarm, DamageBud, DamageBoss, DamageOther;
            public int DamageThorncaster, DamageSnarer, DamageSplitter, DamageSplitling;
            public int DamageStonehoof, DamageWendigo;
            public int Casts, Dodges, Dashes, GuardianSwings, GuardianHits;
            public double ClearSeconds => ClearTicks / (double)Simulation.TicksPerSecond;
        }

        /// <summary>
        /// Один забег бота на лесе стенда с пробами — вход для своих замеров
        /// (метрики «ощущения» боя и т. п.) без таблиц и CSV стенда. Бот, лес,
        /// сиды и ход забега — ровно те же, что в RecordArenaClearTimesAndDamage.
        /// </summary>
        public static void PlaySeed(int campLevel, ulong seed, bool immortal, params IArenaProbe[] probes)
            => PlayRun(Meadow(), campLevel, seed, immortal, probes ?? Array.Empty<IArenaProbe>());

        private static List<ArenaRecord> PlayRun(LocationDefinition meadow, int campLevel, ulong seed, bool immortal,
            IArenaProbe[] probes)
        {
            var session = new GameSession(seed, PrototypeContent.NewCamp(), meadow.Modules,
                PrototypeContent.ItemBaseIds(), location: meadow);
            session.Camp.DeveloperSetLevel(campLevel);
            session.SyncPlayerLevel();
            session.EnterRift();
            var bot = new ArenaBot(session);
            var records = new List<ArenaRecord>();
            ArenaRecord arena = null;
            // Бой арены ещё идёт для проб: ArenaEnded ровно один раз на ArenaStarted.
            bool probeOpen = false;
            RiftRun lastRun = null;
            // Предохранитель от петли на экранах выбора: команды там тиков боя не тратят.
            for (int guard = 0; session.Mode == GameMode.Rift && guard < 2000000; guard++)
            {
                RiftRun run = session.Run;
                Simulation sim = run.Sim;
                EntityStore e = sim.Entities;
                if (run.Phase == RunPhase.Clearing && (arena == null || arena.Arena != run.Depth))
                {
                    if (probeOpen) EndProbes(probes, session, lastRun ?? run, bot, arena, ArenaProbeOutcome.Left);
                    arena = StartRecord(session, campLevel, seed, immortal);
                    records.Add(arena);
                    bot.EnterArena();
                    probeOpen = probes.Length > 0;
                    if (probeOpen)
                    {
                        var start = ProbeContext(session, run, bot, arena, RunPhase.Clearing, InputFrame.Empty,
                            ArenaProbeOutcome.Running);
                        foreach (IArenaProbe probe in probes) probe.ArenaStarted(sim, in start);
                    }
                }
                lastRun = run;

                RunPhase before = run.Phase;
                int taken = run.TakenRewardCount;
                InputFrame input = bot.Decide();
                bool fighting = before == RunPhase.Clearing || before == RunPhase.SeekingExit;
                if (fighting && arena != null && (before == RunPhase.Clearing
                        ? arena.ClearTicks >= (arena.Boss ? BossTimeoutTicks : ArenaTimeoutTicks)
                        : arena.ExitTicks >= ExitTimeoutTicks))
                {
                    arena.TimedOut = true;
                    // Застрявший бот виден в выводе: кто остался жив, стоит ли он на полу
                    // и видна ли до него дорога.
                    var stuck = new StringBuilder("timeout: " + (immortal ? "immortal" : "carry") + " level " + campLevel
                        + " seed " + seed + " arena " + arena.Arena + " " + before + ", hero at " + e.Position[Simulation.PlayerId] + ";");
                    Fix64 heroRadius = e.BodyRadius[Simulation.PlayerId];
                    FixVec2 heroAt = e.Position[Simulation.PlayerId];
                    int heroCell = run.Map.Routes != null ? run.Map.Routes.CellAt(heroAt) : -1;
                    stuck.Append(" walkable " + run.Map.IsWalkable(heroAt, heroRadius)
                        + ", to exit " + FixVec2.Distance(heroAt, run.Map.ExitPoint(0))
                        + (run.Map.CanTravel(heroAt, run.Map.ExitPoint(0), heroRadius) ? " (clear)" : " (blocked)")
                        + ", route cell " + heroCell + (heroCell >= 0 ? " dist " + run.Map.Routes.DistanceFromEntry(heroCell) : "")
                        + ", drops " + run.DropCount + ";");
                    {
                        // Застрял ли сам герой (куда он может шагнуть на метр) или только
                        // граф клеток бота не видит от его клетки дороги к выходу.
                        int free = 0;
                        for (int k = 0; k < 16; k++)
                            if (run.Map.CanTravel(heroAt, heroAt + FixVec2.FromAngle(Fix64.TwoPi * k / 16), heroRadius)) free++;
                        var routes = run.Map.Routes;
                        int exitCell = routes.CellAt(run.Map.ExitPoint(0));
                        var seen = new bool[routes.CellCount];
                        var queue = new Queue<int>();
                        int from = heroCell >= 0 ? heroCell : 0;
                        queue.Enqueue(from); seen[from] = true;
                        while (queue.Count > 0)
                        {
                            int c = queue.Dequeue();
                            var center = routes.GetCell(c).Center;
                            foreach (var step in new[] { new FixVec2(Fix64.FromInt(2), Fix64.Zero), new FixVec2(Fix64.FromInt(-2), Fix64.Zero), new FixVec2(Fix64.Zero, Fix64.FromInt(2)), new FixVec2(Fix64.Zero, Fix64.FromInt(-2)) })
                            {
                                int n = routes.CellAt(center + step);
                                if (n < 0 || seen[n] || !run.Map.CanTravel(center, routes.GetCell(n).Center, heroRadius)) continue;
                                seen[n] = true; queue.Enqueue(n);
                            }
                        }
                        int reach = 0; foreach (bool b in seen) if (b) reach++;
                        stuck.Append(" free dirs " + free + "/16, cell graph reaches exit " + (exitCell >= 0 && seen[exitCell]) + " (" + reach + "/" + routes.CellCount + " cells);");
                    }
                    for (int i = 1; i < e.Count; i++)
                    {
                        if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                        stuck.Append(" #" + i + " " + e.Kind[i] + " at " + e.Position[i] + " hp " + e.Health[i] + "/" + e.MaxHealth[i]);
                        if (!run.Map.IsWalkable(e.Position[i], e.BodyRadius[i])) stuck.Append(" (off floor)");
                        if (!run.Map.CanTravel(e.Position[Simulation.PlayerId], e.Position[i], heroRadius)) stuck.Append(" (blocked)");
                    }
                    TestContext.WriteLine(stuck.ToString());
                    input = new InputFrame { AttackTarget = -1, AbilityTarget = -1, Command = (byte)RunCommand.Leave };
                }
                session.Step(input);
                if (arena != null && before == RunPhase.SeekingExit && run.Phase == RunPhase.ChoosingReward)
                    for (int i = 0; i < RiftRun.RewardChoices; i++)
                        arena.SpringOffered |= run.GetOffer(i).Kind == RewardKind.Spring;
                if (arena != null && before == RunPhase.ChoosingReward && run.TakenRewardCount > taken
                    && run.GetTaken(run.TakenRewardCount - 1).Kind == RewardKind.Spring) arena.SpringTaken = true;
                if (!fighting || arena == null) continue;

                if (before == RunPhase.Clearing) arena.ClearTicks++;
                else arena.ExitTicks++;
                // События читаются только после настоящего шага симуляции: на
                // экранах выбора в списке лежат события прошлого тика.
                var events = sim.Events;
                for (int i = 0; i < events.Count; i++) Count(arena, run, events[i]);
                arena.Dodges = bot.Dodges;
                arena.Dashes = bot.Dashes;
                if (before == RunPhase.Clearing && run.Phase != RunPhase.Clearing)
                {
                    arena.Cleared = run.Phase == RunPhase.SeekingExit;
                    arena.HealthEnd = e.Alive[Simulation.PlayerId] ? e.Health[Simulation.PlayerId] : 0;
                }
                if (run.Outcome == RunOutcome.Died) { arena.Died = true; arena.HealthEnd = 0; }
                if (probes.Length > 0)
                {
                    // До долива бессмертного: проба видит здоровье после ударов этого тика.
                    var tick = ProbeContext(session, run, bot, arena, before, input, ArenaProbeOutcome.Running);
                    foreach (IArenaProbe probe in probes) probe.Tick(sim, in tick);
                    if (probeOpen && (arena.Died || (before == RunPhase.Clearing && run.Phase != RunPhase.Clearing)))
                    {
                        probeOpen = false;
                        EndProbes(probes, session, run, bot, arena, arena.Died ? ArenaProbeOutcome.Died
                            : arena.Cleared ? ArenaProbeOutcome.Cleared
                            : arena.TimedOut ? ArenaProbeOutcome.TimedOut : ArenaProbeOutcome.Left);
                    }
                }
                if (immortal && e.Alive[Simulation.PlayerId]) e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId];
            }
            if (probeOpen) EndProbes(probes, session, lastRun, bot, arena, ArenaProbeOutcome.Left);
            return records;
        }

        private static ArenaProbeContext ProbeContext(GameSession session, RiftRun run, ArenaBot bot, ArenaRecord arena,
            RunPhase before, InputFrame input, ArenaProbeOutcome outcome)
            => new ArenaProbeContext(session, run, bot, arena.Mode == "immortal", arena.CampLevel, arena.Seed,
                arena.Arena, arena.Template, arena.Boss, before, arena.ClearTicks, arena.ExitTicks, input, outcome);

        private static void EndProbes(IArenaProbe[] probes, GameSession session, RiftRun run, ArenaBot bot, ArenaRecord arena,
            ArenaProbeOutcome outcome)
        {
            var end = ProbeContext(session, run, bot, arena, RunPhase.Clearing, InputFrame.Empty, outcome);
            foreach (IArenaProbe probe in probes) probe.ArenaEnded(run.Sim, in end);
        }

        private static ArenaRecord StartRecord(GameSession session, int campLevel, ulong seed, bool immortal)
        {
            RiftRun run = session.Run;
            EntityStore e = run.Sim.Entities;
            var record = new ArenaRecord
            {
                Mode = immortal ? "immortal" : "carry", CampLevel = campLevel, Seed = seed,
                Arena = run.Depth, Boss = run.BossId >= 0, RouteSize = run.CurrentRoute.Size,
                Template = run.CurrentEncounter != null ? run.CurrentEncounter.Key.Replace("forest.", "") : run.BossId >= 0 ? "Boss" : "-",
                HeroLevel = session.Camp.Level, HeroMaxHealth = e.MaxHealth[Simulation.PlayerId],
                HealthStart = e.Health[Simulation.PlayerId],
            };
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                record.Enemies++;
                record.EnemyHealth += e.MaxHealth[i];
                if (run.Encounters != null && run.Encounters.IsElite(i) && i != run.BossId) record.Elites++;
            }
            for (int slot = 0; slot < RunLoadout.Slots; slot++)
            {
                if (run.Loadout.IsEmpty(slot)) continue;
                record.Abilities++;
                record.Talents += run.Loadout.TalentCount(run.Loadout.PoolIndexAt(slot));
            }
            return record;
        }

        private static void Count(ArenaRecord arena, RiftRun run, in SimEvent ev)
        {
            EntityStore e = run.Sim.Entities;
            // Поздние волны и подмога босса встают из земли по ходу боя.
            if (ev.Type == SimEventType.Spawn && ev.Flag && ev.Target > 0 && ev.Target < e.Count)
            {
                arena.Enemies++;
                arena.EnemyHealth += e.MaxHealth[ev.Target];
                if (run.Encounters != null && run.Encounters.IsElite(ev.Target)) arena.Elites++;
                return;
            }
            if ((ev.Type == SimEventType.Damage || ev.Type == SimEventType.DamageOverTime)
                && ev.Target == Simulation.PlayerId)
            {
                arena.DamageTaken += ev.Amount;
                if (ev.Type == SimEventType.Damage) arena.Hits++;
                EnemyKind kind = ev.Source > 0 && ev.Source < e.Count ? e.Kind[ev.Source] : EnemyKind.None;
                if (kind == EnemyKind.ForestGuardian && ev.Type == SimEventType.Damage) arena.GuardianHits++;
                if (ev.Source == run.BossId) arena.DamageBoss += ev.Amount;
                else if (kind == EnemyKind.ForestGuardian) arena.DamageGuardian += ev.Amount;
                else if (kind == EnemyKind.ForestRootSwarm) arena.DamageSwarm += ev.Amount;
                else if (kind == EnemyKind.ForestBud) arena.DamageBud += ev.Amount;
                else if (kind == EnemyKind.ForestThorncaster) arena.DamageThorncaster += ev.Amount;
                else if (kind == EnemyKind.ForestRootSnarer) arena.DamageSnarer += ev.Amount;
                else if (kind == EnemyKind.ForestSplitter) arena.DamageSplitter += ev.Amount;
                else if (kind == EnemyKind.ForestSplitling) arena.DamageSplitling += ev.Amount;
                else if (kind == EnemyKind.ForestStonehoof) arena.DamageStonehoof += ev.Amount;
                else if (kind == EnemyKind.ForestWendigo) arena.DamageWendigo += ev.Amount;
                else arena.DamageOther += ev.Amount;
            }
            else if (ev.Type == SimEventType.Attack && ev.Target == Simulation.PlayerId
                     && ev.Source > 0 && ev.Source < e.Count && e.Kind[ev.Source] == EnemyKind.ForestGuardian)
                arena.GuardianSwings++;
            else if (ev.Type == SimEventType.AbilityCast && ev.Source == Simulation.PlayerId
                     && ev.Amount != PelagKit.DashSlot) arena.Casts++;
        }

        // ---------- локация ----------

        /// <summary>
        /// Meadow без Unity: Resources.Load вне редактора недоступен, поэтому
        /// здесь копия MeadowGameplay.asset и MeadowEncounters.asset (как их
        /// компилирует LocationProfileAsset.ToDefinition). Модули — те же шесть
        /// с теми же весами: PrototypeContent.Modules() совпадает с
        /// Resources/Locations/Modules. Девять строк — девять уровней ассета:
        /// восемь арен и босс (стадия 6). Пачки профиля в потоке арен больше не
        /// выбираются — встречу ставит шаблон плана, — но профиль даёт рост
        /// урона и ключ пачки босса в хеше. МЕНЯЕШЬ АССЕТ — МЕНЯЙ И ЭТУ КОПИЮ.
        /// </summary>
        private static LocationDefinition Meadow()
        {
            int[] rooms = { 11, 12, 13, 14, 15, 16, 17, 18, 20 };
            int[] minEnemies = { 1, 1, 2, 2, 2, 3, 3, 3, 4 };
            int[] maxEnemies = { 3, 4, 4, 5, 5, 6, 6, 7, 8 };
            var levels = new RiftLevelSettings[ArenaCount];
            for (int i = 0; i < ArenaCount; i++)
            {
                int arena = i + 1;
                bool boss = arena == ArenaCount;
                levels[i] = new RiftLevelSettings(rooms[i], 1, 1, 2, minEnemies[i], maxEnemies[i],
                    EnemyArchetypes.DepthHealthPercent(arena), MeadowEncounters(arena), boss,
                    playerHealth: 150, entryClearance: 14, solidEnvironment: true, naturalGlade: true)
                    .WithArenaSize(boss ? 4 : 3);
            }
            return new LocationDefinition(StableId.Of("location.meadow"), PrototypeContent.Modules(), levels,
                64, completeAtEnd: true);
        }

        // MeadowEncounters.asset построчно: ключ, вес, MinLevel, MaxLevel (0 — без
        // потолка), группы. Порядок строк не важен: EncounterSettings сортирует
        // пачки по ключу. Не больше двух Хранителей в пачке (правило владельца).
        private const int MainCount = 2, MaxMainCount = 2, AddMainEveryLevels = 2;
        private const int AddEnemyEveryLevels = 2, MaxExtraEnemies = 2, DamagePerLevelPercent = 8;

        private static readonly PackRow[] Introduction =
        {
            Row("encounter.meadow.intro_swarm", 100, 1, 1, Swarm(2, 2)),
            Row("encounter.meadow.intro", 100, 2, 4, Guardian(1, 1)),
            Row("encounter.meadow.intro_pair", 100, 5, 0, Guardian(2, 2)),
        };

        private static readonly PackRow[] MainPath =
        {
            Row("encounter.meadow.swarm", 100, 1, 2, Swarm(3, 4)),
            Row("encounter.meadow.mixed", 100, 1, 2, Guardian(1, 1), Swarm(1, 2)),
            Row("encounter.meadow.guardians", 100, 2, 2, Guardian(2, 2)),
            Row("encounter.meadow.forest_bud", 75, 2, 0, Bud(1, 2), Swarm(2, 3)),
            Row("encounter.meadow.swarm_large", 100, 3, 3, Swarm(6, 7)),
            Row("encounter.meadow.mixed_large", 100, 3, 3, Guardian(2, 2), Swarm(3, 4)),
            Row("encounter.meadow.guardians_escort", 100, 3, 3, Guardian(2, 2), Swarm(3, 4)),
            Row("encounter.meadow.patrol", 100, 4, 6, Guardian(1, 2), Swarm(3, 4)),
            Row("encounter.meadow.guardian_pair", 100, 4, 0, Guardian(2, 2), Swarm(2, 3)),
            Row("encounter.meadow.crossfire", 75, 5, 0, Bud(2, 2), Guardian(2, 2)),
            Row("encounter.meadow.veteran_patrol", 100, 7, 0, Guardian(2, 2), Bud(1, 1), Swarm(3, 4)),
        };

        private static readonly PackRow[] RewardBranch =
        {
            Row("encounter.meadow.cache", 100, 1, 0, Guardian(2, 2), Swarm(2, 3)),
        };

        private static readonly PackRow[] ExitGuard =
        {
            Row("encounter.meadow.exit", 100, 1, 6, Guardian(1, 1, true), Swarm(2, 3)),
            Row("encounter.meadow.exit_escort", 100, 7, 0, Guardian(1, 1, true), Guardian(1, 1), Swarm(2, 3)),
        };

        /// <summary>MeadowEncounters.asset на уровне level — как EncounterProfileAsset.ToDefinition.</summary>
        private static EncounterSettings MeadowEncounters(int level)
            => new EncounterSettings(Compile(Introduction, level), Compile(MainPath, level),
                Compile(RewardBranch, level), Compile(ExitGuard, level),
                Math.Min(MaxMainCount, MainCount + (level - 1) / AddMainEveryLevels),
                Math.Min(MaxExtraEnemies, (level - 1) / AddEnemyEveryLevels),
                Math.Min(1000, 100 + (level - 1) * DamagePerLevelPercent), Fix64.FromInt(5));

        private readonly struct PackRow
        {
            public readonly EncounterPack Pack;
            public readonly int MinLevel, MaxLevel;
            public PackRow(EncounterPack pack, int minLevel, int maxLevel) { Pack = pack; MinLevel = minLevel; MaxLevel = maxLevel; }
        }

        private static EncounterPack[] Compile(PackRow[] rows, int level)
        {
            var result = new List<EncounterPack>();
            foreach (var row in rows)
                if (level >= row.MinLevel && (row.MaxLevel == 0 || level <= row.MaxLevel)) result.Add(row.Pack);
            return result.ToArray();
        }

        private static PackRow Row(string key, int weight, int minLevel, int maxLevel, params EncounterGroup[] groups)
        {
            int guardians = 0;
            foreach (var group in groups) if (group.Kind == EnemyKind.ForestGuardian) guardians += group.Max;
            if (guardians > 2) throw new ArgumentException(key + ": больше двух Хранителей в пачке");
            return new PackRow(new EncounterPack(StableId.Of(key), weight, groups), minLevel, maxLevel);
        }

        private static EncounterGroup Guardian(int min, int max, bool elite = false)
            => new EncounterGroup(EnemyKind.ForestGuardian, min, max, elite: elite);

        private static EncounterGroup Swarm(int min, int max)
            => new EncounterGroup(EnemyKind.ForestRootSwarm, min, max, growWithDepth: true);

        private static EncounterGroup Bud(int min, int max)
            => new EncounterGroup(EnemyKind.ForestBud, min, max);

        // ---------- отчёт ----------

        private static void AppendTable(StringBuilder report, List<ArenaRecord> all, string mode, int level, int seeds)
        {
            var rows = all.FindAll(r => r.Mode == mode && r.CampLevel == level);
            if (rows.Count == 0) return;
            bool carry = mode == "carry";
            report.AppendLine();
            report.AppendLine((carry ? "CARRY (HP carries over, death ends the run)" : "IMMORTAL (HP topped up every tick)")
                + " - camp level " + level + ", " + seeds + " runs");
            report.AppendLine(carry
                ? "Arena  Target   Runs  Clear s p50 [p25-p75]  p90    Verdict  Dmg p50 (%maxHP)  GHit%  HP start%  HP end%  Died  Lvl  Enemies  EnemyHP  Spring"
                : "Arena  Target   Runs  Clear s p50 [p25-p75]  p90    Verdict  Dmg p50 (%maxHP)  GHit%  Dmg p90  Hits  Dodges  Dashes  Casts  Timeouts");
            for (int arena = 1; arena <= ArenaCount; arena++)
            {
                var here = rows.FindAll(r => r.Arena == arena);
                string name = arena == ArenaCount ? "Boss " : "A" + arena + "   ";
                string target = TargetSeconds[arena - 1, 0] + "-" + TargetSeconds[arena - 1, 1];
                if (here.Count == 0) { report.AppendLine(name + "  " + target.PadRight(7) + "  0"); continue; }
                var clear = Values(here.FindAll(r => r.Cleared), r => r.ClearSeconds);
                var damage = Values(here, r => r.DamageTaken);
                var damageShare = Values(here, r => 100.0 * r.DamageTaken / r.HeroMaxHealth);
                double p50 = Percentile(clear, 0.5);
                string verdict = clear.Count == 0 ? "-" : p50 < TargetSeconds[arena - 1, 0] ? "FAST"
                    : p50 > TargetSeconds[arena - 1, 1] ? "SLOW" : "ok";
                var line = new StringBuilder();
                line.Append(name).Append("  ").Append(target.PadRight(7)).Append("  ")
                    .Append(here.Count.ToString().PadLeft(4)).Append("  ")
                    .Append((F(p50) + " [" + F(Percentile(clear, 0.25)) + "-" + F(Percentile(clear, 0.75)) + "]").PadRight(20))
                    .Append(F(Percentile(clear, 0.9)).PadLeft(6)).Append("  ").Append(verdict.PadRight(7)).Append("  ")
                    .Append((F(Percentile(damage, 0.5)) + " (" + F(Percentile(damageShare, 0.5)) + "%)").PadRight(16));
                // Доля замахов Хранителя (и босса), что дошли до героя: мера того, успевает ли бот выйти из сектора.
                double swings = 0, landed = 0;
                foreach (var r in here) { swings += r.GuardianSwings; landed += r.GuardianHits; }
                line.Append("  ").Append(F(swings > 0 ? 100 * landed / swings : double.NaN).PadLeft(5));
                if (carry)
                {
                    var start = Values(here, r => 100.0 * r.HealthStart / r.HeroMaxHealth);
                    var end = Values(here.FindAll(r => r.Cleared), r => 100.0 * r.HealthEnd / r.HeroMaxHealth);
                    line.Append("  ").Append(F(Percentile(start, 0.5)).PadLeft(9))
                        .Append("  ").Append(F(Percentile(end, 0.5)).PadLeft(7))
                        .Append("  ").Append(here.FindAll(r => r.Died).Count.ToString().PadLeft(4))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.HeroLevel), 0.5)).PadLeft(3))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.Enemies), 0.5)).PadLeft(7))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.EnemyHealth), 0.5)).PadLeft(7))
                        // Родник: взят / предложен (герой ниже 75% у выхода).
                        .Append("  ").Append((here.FindAll(r => r.SpringTaken).Count + "/"
                            + here.FindAll(r => r.SpringOffered).Count).PadLeft(6));
                }
                else
                {
                    line.Append("  ").Append(F(Percentile(damage, 0.9)).PadLeft(7))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.Hits), 0.5)).PadLeft(4))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.Dodges), 0.5)).PadLeft(6))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.Dashes), 0.5)).PadLeft(6))
                        .Append("  ").Append(F(Percentile(Values(here, r => r.Casts), 0.5)).PadLeft(5))
                        .Append("  ").Append(here.FindAll(r => r.TimedOut).Count.ToString().PadLeft(8));
                }
                report.AppendLine(line.ToString());
            }
            var bossRows = rows.FindAll(r => r.Arena == ArenaCount && r.Cleared);
            int died = rows.FindAll(r => r.Died).Count, timeouts = rows.FindAll(r => r.TimedOut).Count;
            // Новые виды — отдельными долями, и только если они вообще били: в
            // забеге без них строка та же, что была.
            var sources = new[] { "guardian", "swarm", "bud", "boss", "other", "thorncaster", "snarer", "splitter", "splitling",
                "stonehoof", "wendigo" };
            var shares = new double[sources.Length];
            foreach (var r in rows)
            {
                shares[0] += r.DamageGuardian; shares[1] += r.DamageSwarm; shares[2] += r.DamageBud;
                shares[3] += r.DamageBoss; shares[4] += r.DamageOther; shares[5] += r.DamageThorncaster;
                shares[6] += r.DamageSnarer; shares[7] += r.DamageSplitter; shares[8] += r.DamageSplitling;
                shares[9] += r.DamageStonehoof; shares[10] += r.DamageWendigo;
            }
            double total = 0;
            foreach (double share in shares) total += share;
            var split = new StringBuilder();
            for (int i = 0; i < sources.Length; i++)
                if (i < 5 || shares[i] > 0)
                    split.Append(sources[i]).Append(' ').Append(F(total > 0 ? 100 * shares[i] / total : 0)).Append("% ");
            int reached = rows.FindAll(r => r.Arena == ArenaCount).Count;
            report.AppendLine("Runs: " + seeds + ", reached boss " + reached + ", boss killed " + bossRows.Count + ", died " + died
                + ", timeouts " + timeouts + ". Damage by source: " + split);
        }

        /// <summary>
        /// Бессмертный проход по шаблонам: медиана зачистки каждого шаблона на
        /// каждой арене, где он стоял, против цели этой арены.
        /// </summary>
        private static void AppendTemplates(StringBuilder report, List<ArenaRecord> all, int level)
        {
            var rows = all.FindAll(r => r.Mode == "immortal" && r.CampLevel == level && r.Cleared);
            if (rows.Count == 0) return;
            report.AppendLine();
            report.AppendLine("BY TEMPLATE (immortal) - camp level " + level + ": clear s p50 [n] per arena");
            var keys = new List<string>();
            foreach (var r in rows) if (!keys.Contains(r.Template)) keys.Add(r.Template);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                var line = new StringBuilder(key.PadRight(5));
                for (int arena = 1; arena <= ArenaCount; arena++)
                {
                    var here = rows.FindAll(r => r.Template == key && r.Arena == arena);
                    if (here.Count == 0) continue;
                    double p50 = Percentile(Values(here, r => r.ClearSeconds), 0.5);
                    string verdict = p50 < TargetSeconds[arena - 1, 0] ? "FAST" : p50 > TargetSeconds[arena - 1, 1] ? "SLOW" : "ok";
                    line.Append("  A").Append(arena).Append(' ').Append(F(p50)).Append(" [").Append(here.Count).Append("] ")
                        .Append(verdict).Append(" (").Append(F(Percentile(Values(here, r => r.Enemies), 0.5))).Append(" mobs)");
                }
                report.AppendLine(line.ToString());
            }
        }

        private static List<double> Values(List<ArenaRecord> rows, Func<ArenaRecord, double> pick)
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

        private static string F(double value)
            => double.IsNaN(value) ? "-" : value.ToString(Math.Abs(value) >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);

        private static string WriteCsv(List<ArenaRecord> records, string suffix)
        {
            string folder = Environment.GetEnvironmentVariable("ARENA_BENCH_OUT");
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(RepositoryRoot(), "artifacts", "balance");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "bench-"
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + suffix + ".csv");
            var csv = new StringBuilder();
            // Столбцы новых видов — в конце: прежние стоят на своих местах.
            csv.AppendLine("mode,camp_level,seed,arena,template,boss,route_size,hero_level,hero_max_hp,hp_start,hp_end,"
                + "clear_s,exit_s,cleared,died,timed_out,damage_taken,hits,dmg_guardian,dmg_swarm,dmg_bud,dmg_boss,"
                + "dmg_other,enemies,elites,enemy_hp,abilities,talents,casts,dodges,dashes,guardian_swings,guardian_hits,"
                + "spring_offered,spring_taken,dmg_thorncaster,dmg_snarer,dmg_splitter,dmg_splitling,"
                + "dmg_stonehoof,dmg_wendigo");
            foreach (var r in records)
                csv.AppendLine(string.Join(",", new[]
                {
                    r.Mode, I(r.CampLevel), r.Seed.ToString(CultureInfo.InvariantCulture), I(r.Arena), r.Template, B(r.Boss),
                    I(r.RouteSize), I(r.HeroLevel), I(r.HeroMaxHealth), I(r.HealthStart), I(r.HealthEnd),
                    r.ClearSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    (r.ExitTicks / (double)Simulation.TicksPerSecond).ToString("0.###", CultureInfo.InvariantCulture),
                    B(r.Cleared), B(r.Died), B(r.TimedOut), I(r.DamageTaken), I(r.Hits), I(r.DamageGuardian),
                    I(r.DamageSwarm), I(r.DamageBud), I(r.DamageBoss), I(r.DamageOther), I(r.Enemies), I(r.Elites),
                    I(r.EnemyHealth), I(r.Abilities), I(r.Talents), I(r.Casts), I(r.Dodges), I(r.Dashes),
                    I(r.GuardianSwings), I(r.GuardianHits), B(r.SpringOffered), B(r.SpringTaken),
                    I(r.DamageThorncaster), I(r.DamageSnarer), I(r.DamageSplitter), I(r.DamageSplitling),
                    I(r.DamageStonehoof), I(r.DamageWendigo),
                }));
            File.WriteAllText(path, csv.ToString());
            return path;
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string B(bool value) => value ? "1" : "0";

        /// <summary>Корень репозитория: из Unity рабочая папка — razlom/, из dotnet — bin/…</summary>
        private static string RepositoryRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "razlom", "Assets"))) return dir.FullName;
            return Directory.GetCurrentDirectory();
        }

        internal static int EnvInt(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) && value > 0 ? value : fallback;

        /// <summary>
        /// Пробы по именам классов через запятую: полное имя или короткое в
        /// Game.Tests. Нет такого класса — стенд падает сразу, а не через час замера.
        /// </summary>
        private static IArenaProbe[] EnvProbes(string name)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(text)) return Array.Empty<IArenaProbe>();
            var result = new List<IArenaProbe>();
            foreach (string part in text.Split(','))
            {
                string typeName = part.Trim();
                if (typeName.Length == 0) continue;
                Type type = typeof(ArenaBalanceBench).Assembly.GetType(typeName)
                    ?? typeof(ArenaBalanceBench).Assembly.GetType("Game.Tests." + typeName);
                if (type == null || !typeof(IArenaProbe).IsAssignableFrom(type))
                    throw new ArgumentException(name + ": нет пробы " + typeName);
                result.Add((IArenaProbe)Activator.CreateInstance(type));
            }
            return result.ToArray();
        }

        private static int[] EnvInts(string name, int[] fallback)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(text)) return fallback;
            var result = new List<int>();
            foreach (string part in text.Split(','))
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
                    result.Add(value);
            return result.Count > 0 ? result.ToArray() : fallback;
        }
    }
}
