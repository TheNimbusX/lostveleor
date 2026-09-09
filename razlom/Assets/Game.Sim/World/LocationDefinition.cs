using System;

namespace Game.Sim
{
    /// <summary>A compiled snapshot of authoring data. No Unity objects cross this boundary.</summary>
    public sealed class LocationDefinition
    {
        public readonly int Id;
        public readonly ModuleSet Modules;
        public readonly int MaxModules;
        private readonly RiftLevelSettings[] _levels;
        public int LevelCount => _levels.Length;
        public readonly bool CompleteAtEnd;

        public LocationDefinition(int id, ModuleSet modules, RiftLevelSettings[] levels, int maxModules = 64, bool completeAtEnd = false)
        {
            if (modules == null || modules.Count == 0) throw new ArgumentException("A location needs modules.");
            if (levels == null || levels.Length == 0) throw new ArgumentException("A location needs levels.");
            if (maxModules < 2 || maxModules > 64) throw new ArgumentOutOfRangeException(nameof(maxModules));
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].TargetModules < 2 || levels[i].TargetModules + levels[i].MaxLoops > maxModules)
                    throw new ArgumentException("Reserve module capacity for loop bridges.");
            }
            Id = id;
            CompleteAtEnd = completeAtEnd;
            Modules = modules;
            MaxModules = maxModules;
            _levels = (RiftLevelSettings[])levels.Clone();
        }

        // Endless profiles repeat the last configuration; finite runs stop before requesting it again.
        public RiftLevelSettings GetLevel(int depth)
        {
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth));
            return _levels[Math.Min(depth, _levels.Length) - 1];
        }

        public void ValidateCapacity(int entityCapacity)
        {
            foreach (var level in _levels)
                if ((level.Encounters != null ? level.Encounters.CapacityNeeded(level.ExitCount, level.RewardBranches)
                    : 1L + (MaxModules - 1L) * level.MaxEnemies) > entityCapacity)
                    throw new ArgumentException("Simulation capacity cannot hold this location's enemies.");
        }
    }
}
