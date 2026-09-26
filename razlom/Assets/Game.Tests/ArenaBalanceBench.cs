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
    /// ARENA_BENCH_DODGE_TICKS (12) — за сколько тиков до удара бот видит метку,
    /// ARENA_BENCH_STAGED=1 — план с новыми видами (All + Staged и правила элит
    /// релиза, ArenaRunPlan.Roll(..., staged: true)); CSV тогда bench-staged-*.
    /// Урон по герою делится по видам, и у Шипомёта, Корнехвата, Расщепня и его
    /// детёнышей — свои столбцы.
    /// </summary>
    public class ArenaBalanceBench
    {
        /// <summary>Восемь арен и босс — лес по документу владельца (стадия 6 плана).</summary>
        private const int ArenaCount = 9;

        /// <summary>ARENA_BENCH_STAGED=1: встречи из пула с новыми видами.</summary>
        private static readonly bool Staged = Environment.GetEnvironmentVariable("ARENA_BENCH_STAGED") == "1";

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
            int[] levels = EnvInts("ARENA_BENCH_LEVELS", new[] { 1, 5, 10 });
            string modes = Environment.GetEnvironmentVariable("ARENA_BENCH_MODES") ?? "carry,immortal";
            var meadow = Meadow();
            var records = new List<ArenaRecord>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (string mode in new[] { "carry", "immortal" })
            {
                if (modes.IndexOf(mode, StringComparison.Ordinal) < 0) continue;
                foreach (int level in levels)
                    for (int s = 0; s < seeds; s++)
                        records.AddRange(PlayRun(meadow, level, (ulong)(s + 1), mode == "immortal"));
            }

            var report = new StringBuilder();
            report.AppendLine("ArenaBalanceBench: " + seeds + " seeds, camp levels " + string.Join("/", levels)
                + ", " + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s wall"
                + (Staged ? ", STAGED plan (All + Staged)" : ""));
            foreach (string mode in new[] { "carry", "immortal" })
                foreach (int level in levels)
                    AppendTable(report, records, mode, level, seeds);
            foreach (int level in levels)
                AppendTemplates(report, records, level);
            string path = WriteCsv(records);
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
            public int Casts, Dodges, Dashes, GuardianSwings, GuardianHits;
            public double ClearSeconds => ClearTicks / (double)Simulation.TicksPerSecond;
        }

        private static List<ArenaRecord> PlayRun(LocationDefinition meadow, int campLevel, ulong seed, bool immortal)
        {
            var session = new GameSession(seed, PrototypeContent.NewCamp(), meadow.Modules,
                PrototypeContent.ItemBaseIds(), location: meadow);
            session.Camp.DeveloperSetLevel(campLevel);
            session.SyncPlayerLevel();
            session.EnterRift();
            if (Staged) UseStagedPlan(session.Run, meadow);
            var bot = new ArenaBot(session);
            var records = new List<ArenaRecord>();
            ArenaRecord arena = null;
            // Предохранитель от петли на экранах выбора: команды там тиков боя не тратят.
            for (int guard = 0; session.Mode == GameMode.Rift && guard < 2000000; guard++)
            {
                RiftRun run = session.Run;
                Simulation sim = run.Sim;
                EntityStore e = sim.Entities;
                if (run.Phase == RunPhase.Clearing && (arena == null || arena.Arena != run.Depth))
                {
                    arena = StartRecord(session, campLevel, seed, immortal);
                    records.Add(arena);
                    bot.EnterArena();
                }

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
                if (immortal && e.Alive[Simulation.PlayerId]) e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId];
            }
            return records;
        }

        /// <summary>
        /// Игра бросает план без новых видов, и RiftRun своего переключателя не
        /// имеет. Бенч подменяет план сразу после входа тем же сидом, но с
        /// staged: на А1 оба плана ставят E01, так что первая арена уже та, а
        /// дальше встречи, размер арен в маршрутах и хеш забега идут от нового.
        /// </summary>
        private static void UseStagedPlan(RiftRun run, LocationDefinition meadow)
        {
            var plan = ArenaRunPlan.Roll(run.Sim.Rng.MasterSeed, meadow, staged: true);
            if (run.Plan == null || run.Depth != 1 || !ReferenceEquals(plan.TemplateFor(1), run.CurrentEncounter))
                throw new InvalidOperationException("The staged plan must keep the first arena of the run.");
            typeof(RiftRun).GetProperty(nameof(RiftRun.Plan)).SetValue(run, plan);
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
            // забеге без Staged строка та же, что была.
            var sources = new[] { "guardian", "swarm", "bud", "boss", "other", "thorncaster", "snarer", "splitter", "splitling" };
            var shares = new double[sources.Length];
            foreach (var r in rows)
            {
                shares[0] += r.DamageGuardian; shares[1] += r.DamageSwarm; shares[2] += r.DamageBud;
                shares[3] += r.DamageBoss; shares[4] += r.DamageOther; shares[5] += r.DamageThorncaster;
                shares[6] += r.DamageSnarer; shares[7] += r.DamageSplitter; shares[8] += r.DamageSplitling;
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

        private static string WriteCsv(List<ArenaRecord> records)
        {
            string folder = Environment.GetEnvironmentVariable("ARENA_BENCH_OUT");
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(RepositoryRoot(), "artifacts", "balance");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, (Staged ? "bench-staged-" : "bench-")
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            var csv = new StringBuilder();
            // Столбцы новых видов — в конце: прежние стоят на своих местах.
            csv.AppendLine("mode,camp_level,seed,arena,template,boss,route_size,hero_level,hero_max_hp,hp_start,hp_end,"
                + "clear_s,exit_s,cleared,died,timed_out,damage_taken,hits,dmg_guardian,dmg_swarm,dmg_bud,dmg_boss,"
                + "dmg_other,enemies,elites,enemy_hp,abilities,talents,casts,dodges,dashes,guardian_swings,guardian_hits,"
                + "spring_offered,spring_taken,dmg_thorncaster,dmg_snarer,dmg_splitter,dmg_splitling");
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

        private static int EnvInt(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) && value > 0 ? value : fallback;

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

        // ---------- бот ----------

        /// <summary>
        /// Игрок «средней руки». Ввод — тот же InputFrame, что собирает вид:
        /// ЛКМ по врагу (Attack + AttackTarget), ПКМ по земле (MoveOrder),
        /// кнопки способностей с точкой прицела. Решения только по тому, что
        /// видно на экране: позиции, метки на земле, плоды, кулдауны, лавидий.
        /// </summary>
        private sealed class ArenaBot
        {
            /// <summary>
            /// Уходит из метки, которая упадёт не позже чем через столько тиков.
            /// 12 — реакция живого игрока (0,4 с); первый прогон шёл с 8, и бот
            /// уворачивался лучше человека. ARENA_BENCH_DODGE_TICKS — для сравнения.
            /// </summary>
            private static readonly int DodgeTicks = EnvInt("ARENA_BENCH_DODGE_TICKS", 12);

            /// <summary>Ниже этой доли здоровья бот берёт родник, если он на экране.</summary>
            private const int SpringBelowPercent = 60;

            /// <summary>Метки ближе этого тоже учитываются, чтобы не отступить в соседнюю.</summary>
            private const int WatchTicks = 24;

            private static readonly Fix64 DodgeMargin = Fix64.Ratio(15, 100);
            private static readonly Fix64 DodgeStep = Fix64.Ratio(15, 100);
            private static readonly Fix64 DodgeMaxDistance = Fix64.Ratio(39, 10);
            private static readonly Fix64 DodgeOvershoot = Fix64.Ratio(3, 10);

            // Приказ идти ставится дальше края: у точки приказа герой тормозит
            // (PlayerTravelStep), и точка у самой кромки съедала бы разгон.
            private static readonly Fix64 DodgeClickBeyond = Fix64.Ratio(3, 2);
            private static readonly Fix64 DirectRange = Fix64.FromInt(3);

            /// <summary>Докуда герой подходит к цели сам: чуть ближе его AttackReach (1,875 м).</summary>
            private static readonly Fix64 StopDistance = Fix64.Ratio(3, 2);
            private static readonly Fix64 SteerStep = Fix64.Ratio(6, 5);
            private static readonly FixVec2[] Directions = BuildDirections(16);

            private readonly GameSession _session;
            private readonly List<EnemyTelegraph> _threats = new List<EnemyTelegraph>();
            private readonly List<int> _threatLeft = new List<int>();

            private int _target = -1;
            private bool _dodging;
            private FixVec2 _dodgePoint;
            private int _dodgeUntil = -1;
            private int _pathUntil = -1, _progressTick = -1;
            private FixVec2 _progressFrom;
            private int _reachTarget = -1, _reachUntil = -1;
            private bool _reachValue;

            // Граф клеток маршрута текущей арены: четыре соседа на клетку.
            private int[] _links, _distance, _queue;
            private int _graphDepth = -1, _graphRun = -1;

            public int Dodges { get; private set; }
            public int Dashes { get; private set; }

            public ArenaBot(GameSession session) => _session = session;

            public void EnterArena()
            {
                _target = _reachTarget = -1;
                _dodging = false;
                _dodgeUntil = _pathUntil = _progressTick = _reachUntil = -1;
                Dodges = Dashes = 0;
            }

            public InputFrame Decide()
            {
                RiftRun run = _session.Run;
                switch (run.Phase)
                {
                    case RunPhase.Clearing: return Fight(run);
                    case RunPhase.SeekingExit: return WalkOut(run);
                    case RunPhase.ChoosingReward: return Command(PickReward(run));
                    case RunPhase.ReplacingAbility: return Command(RunCommand.SalvageAbility);
                    // Первая ветка — «усиление» обычной сложности; «Сложно» бот не берёт.
                    case RunPhase.ChoosingRoute: return Command(RunCommand.ChooseRoute1);
                    default: return InputFrame.Empty;
                }
            }

            private static InputFrame Command(RunCommand command)
            {
                var input = InputFrame.Empty;
                input.Command = (byte)command;
                return input;
            }

            /// <summary>
            /// Родник, если здоровья меньше 60%; иначе способность, пока есть
            /// пустой слот; иначе усиление; иначе первое, что не родник.
            /// </summary>
            private static RunCommand PickReward(RiftRun run)
            {
                if (run.ChoosingArtifact) return RunCommand.ChooseReward1;
                EntityStore e = run.Sim.Entities;
                if ((long)e.Health[Simulation.PlayerId] * 100 < (long)e.MaxHealth[Simulation.PlayerId] * SpringBelowPercent)
                    for (int i = 0; i < RiftRun.RewardChoices; i++)
                        if (run.GetOffer(i).Kind == RewardKind.Spring) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
                bool free = run.Loadout.FreeSlot() >= 0;
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (free && run.GetOffer(i).Kind == RewardKind.Ability) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (run.GetOffer(i).Kind == RewardKind.Talent) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (run.GetOffer(i).Kind != RewardKind.Spring) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
                return RunCommand.ChooseReward1;
            }

            private InputFrame Fight(RiftRun run)
            {
                Simulation sim = run.Sim;
                EntityStore e = sim.Entities;
                FixVec2 pos = e.Position[Simulation.PlayerId];
                CollectThreats(sim);
                if (Dodge(run, pos, out InputFrame dodge)) return dodge;

                var input = InputFrame.Empty;
                int target = PickTarget(e, pos);
                if (target < 0) return input;
                FixVec2 at = e.Position[target];
                HoldWhirlwind(sim, pos, ref input);
                if (!Stuck(sim, pos, at) && Reachable(run, pos, target))
                {
                    input.Flags = (byte)InputFlags.Attack;
                    input.AttackTarget = target;
                    input.Aim = at;
                    TryCast(sim, pos, target, ref input);
                }
                else
                {
                    // Дерево или берег между героем и целью: ПКМ по клеткам маршрута,
                    // ЛКМ зажата — бьёт того, кто окажется перед носом.
                    input.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
                    input.Aim = Waypoint(run, pos, at);
                }
                return input;
            }

            /// <summary>Добежать до способности с элиты (если есть куда положить), потом к выходу.</summary>
            private InputFrame WalkOut(RiftRun run)
            {
                FixVec2 pos = run.Sim.Entities.Position[Simulation.PlayerId];
                FixVec2 goal = run.Map.ExitPoint(0);
                if (run.Loadout.FreeSlot() >= 0)
                {
                    Fix64 best = Fix64.MaxValue;
                    for (int d = 0; d < run.DropCount; d++)
                    {
                        RunDrop drop = run.GetDrop(d);
                        if (drop.Claimed || drop.Offer.Kind != RewardKind.Ability) continue;
                        Fix64 distance = FixVec2.DistanceSq(pos, drop.Position);
                        if (distance < best) { best = distance; goal = drop.Position; }
                    }
                }
                var input = InputFrame.Empty;
                input.Flags = (byte)InputFlags.MoveOrder;
                input.Aim = run.Map.CanTravel(pos, goal, run.Sim.Entities.BodyRadius[Simulation.PlayerId])
                    ? goal : Waypoint(run, pos, goal);
                return input;
            }

            /// <summary>Ближайший живой враг; прежнюю цель не бросает ради того, кто ближе на метр.</summary>
            private int PickTarget(EntityStore e, FixVec2 pos)
            {
                int best = -1;
                Fix64 bestDistance = Fix64.MaxValue;
                for (int i = 1; i < e.Count; i++)
                {
                    if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                    Fix64 distance = FixVec2.DistanceSq(pos, e.Position[i]);
                    if (distance < bestDistance) { bestDistance = distance; best = i; }
                }
                if (_target > 0 && _target < e.Count && e.Alive[_target] && e.Side[_target] != Faction.Wole && best >= 0
                    && FixVec2.Distance(pos, e.Position[_target]) <= Fix64.Sqrt(bestDistance) + Fix64.One)
                    return _target;
                _target = best;
                return best;
            }

            // ---- метки на земле ----

            /// <summary>
            /// Всё, что нарисовано на земле и ещё не упало: общий список меток
            /// (сектор Хранителя и метки Вендиго/Камнекопыта — у тех свой вид, но
            /// игрок их тоже видит) и точки падения плодов.
            /// </summary>
            private void CollectThreats(Simulation sim)
            {
                _threats.Clear();
                _threatLeft.Clear();
                for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                {
                    if (!sim.TryGetTelegraph(slot, out EnemyTelegraph t) || !t.IsActive) continue;
                    AddThreat(t, t.ImpactTick - sim.Tick);
                }
                if (sim.ForestFruitActiveCount == 0) return;
                for (int slot = 0; slot < sim.ForestFruitCapacity; slot++)
                    if (sim.TryGetForestFruit(slot, out ForestFruitState fruit))
                        AddThreat(EnemyTelegraph.Circle(fruit.Target, fruit.Radius), fruit.ImpactTick - sim.Tick);
            }

            private void AddThreat(in EnemyTelegraph shape, int left)
            {
                if (left < 0 || left > WatchTicks) return;
                _threats.Add(shape);
                _threatLeft.Add(left);
            }

            private bool Unsafe(FixVec2 point, Fix64 body)
            {
                for (int k = 0; k < _threats.Count; k++)
                    if (Simulation.TelegraphContains(_threats[k], point, body)) return true;
                return false;
            }

            /// <summary>
            /// Метка накроет героя через DodgeTicks или раньше — шаг наружу по
            /// кратчайшему пути. Снаружи герой стоит, пока метка не упадёт: иначе
            /// автоатака на следующем тике завела бы его обратно под удар.
            /// </summary>
            private bool Dodge(RiftRun run, FixVec2 pos, out InputFrame input)
            {
                input = InputFrame.Empty;
                Simulation sim = run.Sim;
                EntityStore e = sim.Entities;
                Fix64 body = e.BodyRadius[Simulation.PlayerId] + DodgeMargin;
                int soonest = int.MaxValue;
                for (int k = 0; k < _threats.Count; k++)
                {
                    if (_threatLeft[k] > DodgeTicks || !Simulation.TelegraphContains(_threats[k], pos, body)) continue;
                    soonest = Math.Min(soonest, _threatLeft[k]);
                    _dodgeUntil = Math.Max(_dodgeUntil, sim.Tick + _threatLeft[k] + 1);
                }
                bool danger = soonest != int.MaxValue;
                if (!danger && sim.Tick > _dodgeUntil) { _dodging = false; return false; }
                if (!danger)
                {
                    // Уже снаружи: приказ идти в точку выхода живёт в симуляции сам,
                    // а зажатая атака бьёт того, кто стоит перед носом.
                    input.Flags = (byte)InputFlags.Attack;
                    int near = PickTarget(e, pos);
                    input.Aim = near >= 0 ? e.Position[near] : pos + e.Facing[Simulation.PlayerId];
                    return true;
                }

                if (!_dodging || Unsafe(_dodgePoint, body))
                {
                    if (!_dodging) Dodges++;
                    _dodging = true;
                    int target = PickTarget(e, pos);
                    _dodgePoint = Escape(run, pos, body, target >= 0 ? e.Position[target] : pos, soonest,
                        out FixVec2 direction, out bool onFoot);
                    // Пешком не успеть — кувырок, если готов. Рывок общий у всех героев.
                    AbilityBuild dash = sim.GetAbility(PelagKit.DashSlot);
                    if (!onFoot && dash != null && sim.Tick >= sim.AbilityReadyTick(PelagKit.DashSlot))
                    {
                        input.AbilityMask = (byte)(1 << PelagKit.DashSlot);
                        input.Flags = (byte)InputFlags.MoveOrder;
                        input.Aim = pos + direction * dash.Get(AbilityStatType.Radius);
                        _dodgePoint = input.Aim;
                        Dashes++;
                        return true;
                    }
                }
                input.Flags = (byte)InputFlags.MoveOrder;
                input.Aim = _dodgePoint;
                return true;
            }

            /// <summary>
            /// Сколько герой пройдёт вдоль direction за ticks шагов с текущей
            /// скоростью: разгон и разворот ограничены так же, как в Approach
            /// (треть полной скорости за тик), начатый замах режет скорость на четверть.
            /// </summary>
            private static Fix64 ReachAlong(Simulation sim, FixVec2 direction, int ticks)
            {
                EntityStore e = sim.Entities;
                Fix64 full = e.MoveStep[Simulation.PlayerId];
                Fix64 cap = e.PendingAttackTarget[Simulation.PlayerId] > 0 || sim.PlayerAction.ActiveAt(sim.Tick)
                    ? full * Fix64.Ratio(3, 4) : full;
                Fix64 change = full / 3, speed = FixVec2.Dot(e.Velocity[Simulation.PlayerId], direction), total = Fix64.Zero;
                // Ход идёт и в тик удара: движение в шаге раньше проверки попадания.
                for (int t = 0; t <= ticks; t++)
                {
                    speed = Fix64.Min(speed + change, cap);
                    total += speed;
                }
                return total;
            }

            /// <summary>
            /// Безопасная точка по шестнадцати направлениям. Сначала те, куда
            /// герой успевает дойти с учётом нынешней скорости; среди них — с
            /// кратчайшим путём, при равном — ближе к цели (после удара бот сразу
            /// наказывает). Не успевает никуда — направление с наименьшей
            /// нехваткой, и onFoot = false: пора кувыркаться.
            /// </summary>
            private FixVec2 Escape(RiftRun run, FixVec2 pos, Fix64 body, FixVec2 focus, int ticks,
                out FixVec2 direction, out bool onFoot)
            {
                Fix64 radius = run.Sim.Entities.BodyRadius[Simulation.PlayerId];
                int bestDir = -1;
                bool bestFeasible = false;
                Fix64 bestKey = Fix64.MaxValue, bestFocus = Fix64.MaxValue;
                FixVec2 bestPoint = pos;
                for (int k = 0; k < Directions.Length; k++)
                {
                    for (Fix64 d = DodgeStep; d <= DodgeMaxDistance; d += DodgeStep)
                    {
                        if (Unsafe(pos + Directions[k] * d, body)) continue;
                        Fix64 margin = ReachAlong(run.Sim, Directions[k], ticks) - d;
                        bool feasible = margin.Raw >= 0;
                        Fix64 key = feasible ? d : -margin;
                        FixVec2 point = pos + Directions[k] * (d + DodgeOvershoot);
                        Fix64 focus2 = FixVec2.DistanceSq(point, focus);
                        bool better = feasible != bestFeasible ? feasible
                            : key != bestKey ? key < bestKey : focus2 < bestFocus;
                        if (!better || !run.Map.IsWalkable(point, radius) || !run.Map.CanTravel(pos, point, radius)) break;
                        bestDir = k; bestFeasible = feasible; bestKey = key; bestFocus = focus2; bestPoint = point;
                        FixVec2 click = pos + Directions[k] * (d + DodgeClickBeyond);
                        if (run.Map.CanTravel(pos, click, radius)) bestPoint = click;
                        break;
                    }
                }
                if (bestDir >= 0)
                {
                    direction = Directions[bestDir];
                    onFoot = bestFeasible;
                    return bestPoint;
                }
                // Выхода нет (угол, стена) — прочь от начала ближайшей фигуры.
                FixVec2 away = pos - _threats[0].Origin;
                direction = away.LengthSq.Raw == 0 ? Directions[0] : away.Normalized();
                onFoot = false;
                return run.Map.ClampToWalkable(pos + direction * Fix64.FromInt(2), radius);
            }

            // ---- способности ----

            /// <summary>
            /// Одна кнопка за тик, по порядку слотов, когда готова, хватает лавидия
            /// и цель в её дальности. Стоя в метке, не кастует: длинный замах
            /// способности не даёт потом из неё выйти.
            /// </summary>
            private void TryCast(Simulation sim, FixVec2 pos, int target, ref InputFrame input)
            {
                EntityStore e = sim.Entities;
                if (Unsafe(pos, e.BodyRadius[Simulation.PlayerId] + DodgeMargin)) return;
                // Дальность — до края тела цели, как бьёт и сама способность.
                Fix64 reach = FixVec2.Distance(pos, e.Position[target]) - e.BodyRadius[target];
                for (int slot = 0; slot < PelagKit.MainSlots; slot++)
                {
                    AbilityBuild build = sim.GetAbility(slot);
                    if (build == null) continue;
                    int id = build.DefinitionId;
                    bool combo = id == AbilityDefinition.WreckId && sim.WreckComboOpen;
                    if (!combo && (sim.Tick < sim.AbilityReadyTick(slot)
                        || e.Lavidium[Simulation.PlayerId] < Fix64.FromInt(Simulation.LavidiumCostOf(build)))) continue;
                    Fix64 radius = build.Get(AbilityStatType.Radius);
                    int abilityTarget = -1;
                    bool use;
                    if (id == AbilityDefinition.WhirlwindId) use = reach <= radius;
                    else if (id == AbilityDefinition.BlazeId) use = reach <= Simulation.AutoAttackRange;
                    else if (id == AbilityDefinition.AnchorLeapId) { use = reach <= radius && reach >= DirectRange; abilityTarget = target; }
                    else if (id == AbilityDefinition.ChainStepId) { use = reach <= radius; abilityTarget = target; }
                    else use = id != AbilityDefinition.DashId && reach <= radius;
                    if (!use) continue;
                    input.AbilityMask = (byte)(1 << slot);
                    input.AbilityTarget = abilityTarget;
                    return;
                }
            }

            /// <summary>Вихрь с талантом «канал» крутится, пока кнопка зажата, — держим, пока рядом враги.</summary>
            private static void HoldWhirlwind(Simulation sim, FixVec2 pos, ref InputFrame input)
            {
                EntityStore e = sim.Entities;
                for (int slot = 0; slot < PelagKit.MainSlots; slot++)
                {
                    AbilityBuild build = sim.GetAbility(slot);
                    if (build == null || build.DefinitionId != AbilityDefinition.WhirlwindId) continue;
                    Fix64 radius = build.Get(AbilityStatType.Radius) + Fix64.One;
                    for (int i = 1; i < e.Count; i++)
                        if (e.Alive[i] && e.Side[i] != Faction.Wole && FixVec2.DistanceSq(pos, e.Position[i]) <= radius * radius)
                        {
                            input.AbilityHoldMask |= (byte)(1 << slot);
                            return;
                        }
                }
            }

            // ---- дорога ----

            /// <summary>Полсекунды без продвижения вдали от цели — полторы секунды по клеткам маршрута.</summary>
            private bool Stuck(Simulation sim, FixVec2 pos, FixVec2 at)
            {
                if (_progressTick < 0 || sim.Tick - _progressTick >= 15)
                {
                    Fix64 reach = Simulation.AutoAttackRange - Fix64.Ratio(1, 2);
                    bool far = FixVec2.DistanceSq(pos, at) > reach * reach;
                    if (_progressTick >= 0 && far && FixVec2.DistanceSq(pos, _progressFrom) < Fix64.Ratio(1, 25))
                        _pathUntil = sim.Tick + 45;
                    _progressTick = sim.Tick;
                    _progressFrom = pos;
                }
                return sim.Tick < _pathUntil;
            }

            /// <summary>
            /// Видна ли точка остановки у цели по прямой. Проверяется и вплотную:
            /// дерево между героем и роем в двух метрах держало обоих навсегда.
            /// Кэш на треть секунды.
            /// </summary>
            private bool Reachable(RiftRun run, FixVec2 pos, int target)
            {
                EntityStore e = run.Sim.Entities;
                FixVec2 at = e.Position[target];
                Fix64 distance = FixVec2.Distance(pos, at);
                if (distance <= StopDistance) return true;
                if (_reachTarget == target && run.Sim.Tick < _reachUntil) return _reachValue;
                FixVec2 stop = at - (at - pos) / distance * StopDistance;
                _reachValue = run.Map.CanTravel(pos, stop, e.BodyRadius[Simulation.PlayerId]);
                _reachTarget = target;
                _reachUntil = run.Sim.Tick + 10;
                return _reachValue;
            }

            /// <summary>
            /// Следующая точка пути по клеткам маршрута: волна от клетки цели,
            /// спуск по ней от клетки героя, и самая дальняя клетка, видная по прямой.
            /// </summary>
            private FixVec2 Waypoint(RiftRun run, FixVec2 from, FixVec2 goal)
            {
                Fix64 radius = run.Sim.Entities.BodyRadius[Simulation.PlayerId];
                FixVec2 point = RouteWaypoint(run, from, goal, radius);
                // Клетки маршрута не довели дальше своей: герой уже стоит в ней, и
                // приказ «иди в центр своей клетки» держал его у ствола до таймера.
                bool progress = FixVec2.DistanceSq(from, point) > Fix64.Ratio(9, 16);
                if (progress && run.Map.CanTravel(from, point, radius)) return point;
                FixVec2 toward = progress ? point : goal;
                return LocalWaypoint(run, from, toward, radius, out FixVec2 local) ? local : Steer(run, from, toward, radius);
            }

            // ---- обход вблизи ----
            //
            // Клетки маршрута — по два метра, и дерево между героем и мобом в
            // трёх метрах рвёт их граф: оба упирались в ствол с разных сторон
            // до конца таймера (стенд с реакцией 12 тиков насчитал 3–5 таких
            // «время вышло» на сорок забегов). Здесь волна по решётке в полметра
            // вокруг обоих, пересчёт раз в треть секунды.

            private const int LocalCells = 48;
            private static readonly Fix64 LocalStep = Fix64.Ratio(1, 2);
            private readonly int[] _localDistance = new int[LocalCells * LocalCells];
            private readonly int[] _localQueue = new int[LocalCells * LocalCells];
            private readonly bool[] _localOpen = new bool[LocalCells * LocalCells];
            private FixVec2 _localGoal, _localPoint;
            private int _localUntil = -1;
            private bool _localFound;

            private bool LocalWaypoint(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius, out FixVec2 waypoint)
            {
                int tick = run.Sim.Tick;
                if (tick < _localUntil && FixVec2.DistanceSq(goal, _localGoal) < Fix64.One
                    && (!_localFound || run.Map.CanTravel(from, _localPoint, radius)))
                {
                    waypoint = _localPoint;
                    return _localFound;
                }
                _localUntil = tick + 10;
                _localGoal = goal;
                _localFound = SearchLocal(run, from, goal, radius, out _localPoint);
                waypoint = _localPoint;
                return _localFound;
            }

            private bool SearchLocal(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius, out FixVec2 waypoint)
            {
                waypoint = from;
                Fix64 half = LocalStep * (LocalCells / 2);
                FixVec2 origin = (from + goal) * Fix64.Ratio(1, 2) - new FixVec2(half, half);
                FixVec2 Center(int cell) => origin + new FixVec2(LocalStep * (cell % LocalCells) + LocalStep / 2,
                    LocalStep * (cell / LocalCells) + LocalStep / 2);
                int CellOf(FixVec2 p)
                {
                    int x = ((p.X - origin.X) / LocalStep).ToInt(), y = ((p.Y - origin.Y) / LocalStep).ToInt();
                    return x < 0 || y < 0 || x >= LocalCells || y >= LocalCells ? -1 : y * LocalCells + x;
                }
                int start = CellOf(from), end = CellOf(goal);
                if (start < 0 || end < 0) return false;
                for (int i = 0; i < _localDistance.Length; i++)
                {
                    _localDistance[i] = -1;
                    _localOpen[i] = run.Map.IsWalkable(Center(i), radius);
                }
                // Цель стоит на месте моба — сама клетка может быть занята деревом рядом; ищем от неё.
                int head = 0, tail = 0;
                _localQueue[tail++] = end;
                _localDistance[end] = 0;
                while (head < tail && _localDistance[start] < 0)
                {
                    int cell = _localQueue[head++];
                    int cx = cell % LocalCells, cy = cell / LocalCells;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= LocalCells || ny >= LocalCells) continue;
                            int next = ny * LocalCells + nx;
                            if (_localDistance[next] >= 0) continue;
                            if (next != start && !_localOpen[next]) continue;
                            if (next != start && !run.Map.CanTravel(Center(cell), Center(next), radius)) continue;
                            _localDistance[next] = _localDistance[cell] + 1;
                            _localQueue[tail++] = next;
                        }
                }
                if (_localDistance[start] < 0) return false;
                // Спуск по волне от героя; берём самую дальнюю клетку пути, видную по прямой.
                bool found = false;
                for (int cell = start, guard = 0; cell != end && guard < _localDistance.Length; guard++)
                {
                    int next = -1, cx = cell % LocalCells, cy = cell / LocalCells;
                    for (int dy = -1; dy <= 1 && next < 0; dy++)
                        for (int dx = -1; dx <= 1 && next < 0; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= LocalCells || ny >= LocalCells) continue;
                            int candidate = ny * LocalCells + nx;
                            if (_localDistance[candidate] >= 0 && _localDistance[candidate] < _localDistance[cell]) next = candidate;
                        }
                    if (next < 0) break;
                    cell = next;
                    FixVec2 center = Center(cell);
                    if (!run.Map.CanTravel(from, center, radius)) break;
                    waypoint = center;
                    found = true;
                }
                return found;
            }

            /// <summary>
            /// Обход вблизи, внутри одной клетки маршрута: шаг на 1,2 м в ту из
            /// шестнадцати сторон, что проходима и ближе всего к цели.
            /// </summary>
            private static FixVec2 Steer(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius)
            {
                FixVec2 best = goal;
                Fix64 bestDistance = Fix64.MaxValue;
                for (int k = 0; k < Directions.Length; k++)
                {
                    FixVec2 point = from + Directions[k] * SteerStep;
                    if (!run.Map.CanTravel(from, point, radius)) continue;
                    Fix64 distance = FixVec2.DistanceSq(point, goal);
                    if (distance < bestDistance) { bestDistance = distance; best = point; }
                }
                return best;
            }

            private FixVec2 RouteWaypoint(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius)
            {
                LayoutRoutes routes = run.Map.Routes;
                if (routes == null) return goal;
                EnsureGraph(run, radius);
                int start = NearestCell(routes, from), end = NearestCell(routes, goal);
                if (start < 0 || end < 0 || start == end) return goal;
                for (int i = 0; i < _distance.Length; i++) _distance[i] = -1;
                int head = 0, tail = 0;
                _queue[tail++] = end;
                _distance[end] = 0;
                while (head < tail && _distance[start] < 0)
                {
                    int cell = _queue[head++];
                    for (int d = 0; d < 4; d++)
                    {
                        int next = _links[cell * 4 + d];
                        if (next < 0 || _distance[next] >= 0) continue;
                        _distance[next] = _distance[cell] + 1;
                        _queue[tail++] = next;
                    }
                }
                // Своя клетка героя отрезана от графа (центр под деревом, отброс
                // тарана загнал к стволу): волна прошла все связные клетки, и путь
                // начинается с ближайшей из них, видной по прямой. Без этого бот
                // упирался в ствол до «время вышло» (проход 2 стенда: 7 из 13).
                if (_distance[start] < 0)
                {
                    int entry = -1;
                    Fix64 nearest = Fix64.MaxValue;
                    for (int i = 0; i < routes.CellCount; i++)
                    {
                        if (_distance[i] < 0) continue;
                        Fix64 d = FixVec2.DistanceSq(from, routes.GetCell(i).Center);
                        if (d < nearest && run.Map.CanTravel(from, routes.GetCell(i).Center, radius)) { nearest = d; entry = i; }
                    }
                    if (entry < 0) return goal;
                    start = entry;
                    if (start == end) return goal;
                }
                FixVec2 best = routes.GetCell(start).Center;
                for (int cell = start, guard = 0; cell != end && guard < _distance.Length; guard++)
                {
                    int next = -1;
                    for (int d = 0; d < 4 && next < 0; d++)
                    {
                        int candidate = _links[cell * 4 + d];
                        if (candidate >= 0 && _distance[candidate] >= 0 && _distance[candidate] < _distance[cell]) next = candidate;
                    }
                    if (next < 0) break;
                    cell = next;
                    FixVec2 center = routes.GetCell(cell).Center;
                    if (!run.Map.CanTravel(from, center, radius)) break;
                    best = center;
                    if (cell == end && run.Map.CanTravel(from, goal, radius)) return goal;
                }
                return best;
            }

            private void EnsureGraph(RiftRun run, Fix64 radius)
            {
                if (_graphDepth == run.Depth && _graphRun == _session.RunNumber && _links != null) return;
                _graphDepth = run.Depth;
                _graphRun = _session.RunNumber;
                LayoutRoutes routes = run.Map.Routes;
                int count = routes.CellCount;
                _links = new int[count * 4];
                _distance = new int[count];
                _queue = new int[count];
                Fix64 size = LayoutMap.CellSize;
                var steps = new[]
                {
                    new FixVec2(size, Fix64.Zero), new FixVec2(-size, Fix64.Zero),
                    new FixVec2(Fix64.Zero, size), new FixVec2(Fix64.Zero, -size),
                };
                for (int i = 0; i < count; i++)
                {
                    FixVec2 center = routes.GetCell(i).Center;
                    for (int d = 0; d < 4; d++)
                    {
                        int next = routes.CellAt(center + steps[d]);
                        _links[i * 4 + d] = next >= 0 && run.Map.CanTravel(center, routes.GetCell(next).Center, radius) ? next : -1;
                    }
                }
            }

            private static int NearestCell(LayoutRoutes routes, FixVec2 point)
            {
                int cell = routes.CellAt(point);
                if (cell >= 0) return cell;
                Fix64 best = Fix64.MaxValue;
                for (int i = 0; i < routes.CellCount; i++)
                {
                    Fix64 distance = FixVec2.DistanceSq(point, routes.GetCell(i).Center);
                    if (distance < best) { best = distance; cell = i; }
                }
                return cell;
            }

            private static FixVec2[] BuildDirections(int count)
            {
                var result = new FixVec2[count];
                for (int k = 0; k < count; k++) result[k] = FixVec2.FromAngle(Fix64.TwoPi * k / count);
                return result;
            }
        }
    }
}
