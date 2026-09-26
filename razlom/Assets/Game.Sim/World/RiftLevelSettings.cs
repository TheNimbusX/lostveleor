using System;

namespace Game.Sim
{
    /// <summary>Immutable parameters shared by the run and the editor preview.</summary>
    public readonly struct RiftLevelSettings
    {
        public readonly int TargetModules, ExitCount, MaxLoops, RewardBranches;
        public readonly int MinEnemies, MaxEnemies;

        /// <summary>
        /// ПРОЦЕНТ здоровья врагов уровня от таблицы видов, а не очки: 100 на
        /// первой арене, +7 за каждую следующую (EnemyArchetypes.DepthHealthPercent).
        /// Одно число растит всех — Хранителя, рой, стрелка, босса, — и каждого
        /// от его собственного здоровья.
        /// </summary>
        public readonly int EnemyHealth;
        public readonly EncounterSettings Encounters;
        public readonly bool Boss;
        public readonly int PlayerHealth;
        public readonly int EntryClearance;
        public readonly bool SolidEnvironment, NaturalGlade;
        public readonly int ArenaSize;

        public RiftLevelSettings(int targetModules, int exitCount, int maxLoops, int rewardBranches,
            int minEnemies, int maxEnemies, int enemyHealth, EncounterSettings encounters = null, bool boss = false, int playerHealth = 1000, int entryClearance = 9, bool solidEnvironment = false, bool naturalGlade = false, int arenaSize = 0)
        {
            if (targetModules < 2 || targetModules > 64 || exitCount < 1 || exitCount > 8 ||
                maxLoops < 0 || maxLoops > 8 || rewardBranches < 0 || rewardBranches > 8 ||
                minEnemies < 0 || maxEnemies < minEnemies || maxEnemies > 128 || enemyHealth < 1 ||
                enemyHealth > MaxEnemyHealthPercent || playerHealth < 1)
                throw new ArgumentException("Invalid rift level settings.");
            TargetModules = targetModules;
            ExitCount = exitCount;
            MaxLoops = maxLoops;
            RewardBranches = rewardBranches;
            MinEnemies = minEnemies;
            MaxEnemies = maxEnemies;
            EnemyHealth = enemyHealth;
            Encounters = encounters;
            if (boss && (encounters == null || exitCount != 1))
                throw new ArgumentException("A boss level requires encounters and exactly one exit.");
            Boss = boss;
            PlayerHealth = playerHealth;
            if (entryClearance < 9 || entryClearance > 30) throw new ArgumentOutOfRangeException(nameof(entryClearance));
            EntryClearance = entryClearance;
            SolidEnvironment = solidEnvironment; NaturalGlade = naturalGlade;
            if (arenaSize != 0 && (arenaSize < 2 || arenaSize > 4)) throw new ArgumentOutOfRangeException(nameof(arenaSize));
            ArenaSize = arenaSize;
        }

        public RiftLevelSettings WithArenaSize(int size) => new RiftLevelSettings(TargetModules, ExitCount,
            MaxLoops, RewardBranches, MinEnemies, MaxEnemies, EnemyHealth, Encounters, Boss,
            PlayerHealth, EntryClearance, SolidEnvironment, NaturalGlade, size);

        /// <summary>Потолок процента здоровья: стократ. Выше — опечатка, а не баланс.</summary>
        public const int MaxEnemyHealthPercent = 10000;

        // The existing prototype balance, without any additional RNG calls.
        // Здоровье — тот же рост с глубиной, что у авторских уровней.
        public static RiftLevelSettings Prototype(int depth)
            => new RiftLevelSettings(Math.Min(64, 10 + depth), 1, 1, 2,
                1 + depth / 3, 3 + depth / 2, EnemyArchetypes.DepthHealthPercent(depth));

        public void Generate(LayoutGenerator generator, ModuleSet modules, LayoutMap map, ulong layoutSeed)
        {
            if (ArenaSize > 0) GladeLayout.Generate(modules, map, layoutSeed, TargetModules, Boss, ArenaSize);
            else if (NaturalGlade) GladeLayout.Generate(modules, map, layoutSeed, TargetModules, Boss);
            else if (Boss) generator.GenerateBossArena(modules, layoutSeed, map);
            else generator.Generate(modules, layoutSeed, map, TargetModules, ExitCount, MaxLoops, RewardBranches);
            if (SolidEnvironment && !Boss) map.BuildObstacles(layoutSeed);
        }

        public EncounterPlan Spawn(Simulation sim, LayoutMap map, ulong spawnSeed)
            => Spawn(sim, map, spawnSeed, null, 0);

        /// <summary>
        /// Расстановка уровня. С шаблоном встречи (поток арен, стадия 6 плана)
        /// пачки профиля не выбираются: встречу ставит шаблон, а от профиля
        /// берётся только рост урона. arena — номер арены для бюджета угроз;
        /// hardPercent — маршрут «Сложно» (125), его ставит сама расстановка,
        /// потому что поздние волны выходят уже после неё.
        /// </summary>
        public EncounterPlan Spawn(Simulation sim, LayoutMap map, ulong spawnSeed, ArenaEncounterTemplate template,
            int arena, int hardPercent = 100)
        {
            if (Encounters != null)
            {
                var plan = Boss ? sim.SetupBossArena(map, spawnSeed, EnemyHealth, Encounters, hardPercent,
                        arena > 0 ? arena : ForestEncounterTemplates.ArenaCount + 1)
                    : template != null ? sim.SetupArenaEncounter(map, spawnSeed, arena > 0 ? arena : 1, EnemyHealth,
                        Encounters.DamagePercent, template, hardPercent, EntryClearance)
                    : sim.SetupEncounters(map, spawnSeed, EnemyHealth, Encounters, EntryClearance);
                ApplyPlayerHealth(sim);
                return plan;
            }
            // Прежняя случайная расстановка ставит одних Хранителей.
            sim.SetupRift(map, spawnSeed, MinEnemies, MaxEnemies,
                EnemyArchetypes.ScaleHealth(sim.ArchetypeHealth(EnemyKind.ForestGuardian), EnemyHealth));
            ApplyPlayerHealth(sim);
            return null;
        }

        private void ApplyPlayerHealth(Simulation sim)
        {
            sim.Entities.Stats[Simulation.PlayerId].SetBase(StatType.MaxHealth, Fix64.FromInt(PlayerHealth));
            sim.RefreshPlayerStats(heal: true);
        }
    }

    /// <summary>Replay a level's seed allocation without advancing a live simulation.</summary>
    public readonly struct RiftLevelSeeds
    {
        public readonly ulong Layout, Spawns;
        public RiftLevelSeeds(ulong layout, ulong spawns) { Layout = layout; Spawns = spawns; }

        public static RiftLevelSeeds ForLevel(ulong runSeed, int level)
        {
            if (level < 1 || level > 10000) throw new ArgumentOutOfRangeException(nameof(level));
            var streams = new RngStreams(runSeed);
            ulong layout = 0, spawns = 0;
            for (int i = 0; i < level; i++)
            {
                layout = LayoutGenerator.RollSeed(ref streams.Layout);
                spawns = LayoutGenerator.RollSeed(ref streams.Spawns);
            }
            return new RiftLevelSeeds(layout, spawns);
        }
    }
}
