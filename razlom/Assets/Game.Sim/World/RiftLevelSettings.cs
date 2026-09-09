using System;

namespace Game.Sim
{
    /// <summary>Immutable parameters shared by the run and the editor preview.</summary>
    public readonly struct RiftLevelSettings
    {
        public readonly int TargetModules, ExitCount, MaxLoops, RewardBranches;
        public readonly int MinEnemies, MaxEnemies, EnemyHealth;
        public readonly EncounterSettings Encounters;
        public readonly bool Boss;
        public readonly int PlayerHealth;
        public readonly int EntryClearance;

        public RiftLevelSettings(int targetModules, int exitCount, int maxLoops, int rewardBranches,
            int minEnemies, int maxEnemies, int enemyHealth, EncounterSettings encounters = null, bool boss = false, int playerHealth = 1000, int entryClearance = 9)
        {
            if (targetModules < 2 || targetModules > 64 || exitCount < 1 || exitCount > 8 ||
                maxLoops < 0 || maxLoops > 8 || rewardBranches < 0 || rewardBranches > 8 ||
                minEnemies < 0 || maxEnemies < minEnemies || maxEnemies > 128 || enemyHealth < 1 || playerHealth < 1)
                throw new ArgumentException("Invalid rift level settings.");
            TargetModules = targetModules;
            ExitCount = exitCount;
            MaxLoops = maxLoops;
            RewardBranches = rewardBranches;
            MinEnemies = minEnemies;
            MaxEnemies = maxEnemies;
            EnemyHealth = enemyHealth;
            Encounters = encounters;
            if (boss && (encounters == null || exitCount != 1 || enemyHealth > int.MaxValue / 6))
                throw new ArgumentException("A boss level requires encounters and exactly one exit.");
            Boss = boss;
            PlayerHealth = playerHealth;
            if (entryClearance < 9 || entryClearance > 30) throw new ArgumentOutOfRangeException(nameof(entryClearance));
            EntryClearance = entryClearance;
        }

        // The existing prototype balance, without any additional RNG calls.
        public static RiftLevelSettings Prototype(int depth)
            => new RiftLevelSettings(Math.Min(64, 10 + depth), 1, 1, 2,
                1 + depth / 3, 3 + depth / 2, 100 + 100 * depth / 4);

        public void Generate(LayoutGenerator generator, ModuleSet modules, LayoutMap map, ulong layoutSeed)
        {
            if (Boss) generator.GenerateBossArena(modules, layoutSeed, map);
            else generator.Generate(modules, layoutSeed, map, TargetModules, ExitCount, MaxLoops, RewardBranches);
        }

        public EncounterPlan Spawn(Simulation sim, LayoutMap map, ulong spawnSeed)
        {
            if (Encounters != null)
            {
                var plan = Boss ? sim.SetupBossArena(map, spawnSeed, EnemyHealth, Encounters)
                    : sim.SetupEncounters(map, spawnSeed, EnemyHealth, Encounters, EntryClearance);
                ApplyPlayerHealth(sim);
                return plan;
            }
            sim.SetupRift(map, spawnSeed, MinEnemies, MaxEnemies, EnemyHealth);
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
