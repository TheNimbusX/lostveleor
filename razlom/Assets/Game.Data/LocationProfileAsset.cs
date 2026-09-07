using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Data
{
    [Serializable]
    public struct LevelSettingsAsset
    {
        [Range(2, 56)] public int Rooms;
        [Range(1, 8)] public int Exits;
        [Range(0, 8)] public int Loops;
        [Range(0, 8)] public int RewardBranches;
        [Min(0)] public int MinEnemies;
        [Min(0)] public int MaxEnemies;
        [Min(1)] public int EnemyHealth;

        public RiftLevelSettings ToDefinition()
            => new RiftLevelSettings(Rooms, Exits, Loops, RewardBranches, MinEnemies, MaxEnemies, EnemyHealth);
    }

    [CreateAssetMenu(fileName = "Location", menuName = "Разлом/Локации/Игровой профиль")]
    public sealed class LocationProfileAsset : ScriptableObject
    {
        public string DisplayName = "Луговая окраина";
        public string StableKey = "location.meadow";
        [Range(2, 64)] public int MaxModules = 64;
        public ModuleAsset[] Modules = Array.Empty<ModuleAsset>();
        [Tooltip("Element 0 = level 1. Above this list the last configuration repeats; boss progression is not implemented here.")]
        public LevelSettingsAsset[] Levels = new LevelSettingsAsset[10];

        public LocationDefinition ToDefinition()
        {
            if (string.IsNullOrWhiteSpace(StableKey)) throw new ArgumentException("Specify a location stable key.");
            if (Modules == null || Modules.Length == 0 || Modules.Length > 64)
                throw new ArgumentException("Supply 1–64 modules.");
            if (Levels == null || Levels.Length == 0 || Levels.Length > 100)
                throw new ArgumentException("Supply 1–100 level configurations.");
            int entrances = 0, expandable = 0;
            var ids = new HashSet<int>();
            var modules = new ModuleDefinition[Modules.Length];
            for (int i = 0; i < Modules.Length; i++)
            {
                if (Modules[i] == null) throw new ArgumentException("Missing module at index " + i);
                modules[i] = Modules[i].ToDefinition();
                if (!ids.Add(modules[i].Id)) throw new ArgumentException("Duplicate module key: " + Modules[i].StableKey);
                if (modules[i].IsEntrance) entrances++;
                else if (modules[i].Weight > 0) expandable++;
            }
            if (entrances != 1 || expandable == 0)
                throw new ArgumentException("The set needs exactly one entrance and at least one weighted regular module.");
            var levels = new RiftLevelSettings[Levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                try { levels[i] = Levels[i].ToDefinition(); }
                catch (ArgumentException e) { throw new ArgumentException("Level " + (i + 1) + ": " + e.Message); }
            }
            var result = new LocationDefinition(StableId.Of(StableKey), new ModuleSet(modules), levels, MaxModules);
            result.ValidateCapacity(512);
            return result;
        }
    }
}
