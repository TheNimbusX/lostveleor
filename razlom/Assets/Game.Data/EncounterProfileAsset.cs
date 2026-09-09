using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Data
{
    [Serializable]
    public sealed class EncounterGroupAsset
    {
        public EnemyKind Kind = EnemyKind.ForestGuardian;
        [Min(0)] public int Min = 1;
        [Min(0)] public int Max = 1;
        [Range(1, 1000)] public int HealthPercent = 100;
        [Range(1, 1000)] public int DamagePercent = 100;
        public bool Elite;
        public bool GrowWithDepth;
        public EncounterGroup ToDefinition() => new EncounterGroup(Kind, Min, Max, HealthPercent, DamagePercent, Elite, GrowWithDepth);
    }

    [Serializable]
    public sealed class EncounterPackAsset
    {
        public string StableKey;
        [Min(1)] public int Weight = 100;
        [Min(1)] public int MinLevel = 1;
        [Tooltip("0 — без ограничения сверху.")]
        [Min(0)] public int MaxLevel;
        public EncounterGroupAsset[] Groups = Array.Empty<EncounterGroupAsset>();
        public EncounterPack ToDefinition()
        {
            if (string.IsNullOrWhiteSpace(StableKey) || Groups == null) throw new ArgumentException("Specify a pack key and groups.");
            var groups = new EncounterGroup[Groups.Length];
            for (int i = 0; i < groups.Length; i++)
                groups[i] = Groups[i]?.ToDefinition() ?? throw new ArgumentException("Missing enemy group.");
            return new EncounterPack(StableId.Of(StableKey), Weight, groups);
        }
    }

    [CreateAssetMenu(fileName = "Encounters", menuName = "Разлом/Локации/Боевые встречи")]
    public sealed class EncounterProfileAsset : ScriptableObject
    {
        [Header("Встречи по маршруту")]
        [Range(1, 12)] public int MainCount = 3;
        [Range(1, 12)] public int MaxMainCount = 5;
        [Min(1)] public int AddMainEveryLevels = 3;
        [Tooltip("Радиус расстановки группы и свободного от декора места, метры.")]
        [Range(2f, 12f)] public float FormationRadius = 4f;
        [Header("Рост сложности")]
        [Min(1)] public int AddEnemyEveryLevels = 4;
        [Range(0, 8)] public int MaxExtraEnemies = 2;
        [Range(0, 50)] public int DamagePerLevelPercent = 5;
        [Tooltip("Здоровье берётся из Enemy Health соответствующего уровня. Здесь задаются проценты от него.")]
        public EncounterPackAsset[] Introduction = { Pack("encounter.meadow.intro", Guardian(1, 1)) };
        public EncounterPackAsset[] MainPath = {
            Pack("encounter.meadow.guardians", Guardian(2, 3)),
            Pack("encounter.meadow.swarm", Swarm(4, 5)),
            Pack("encounter.meadow.mixed", Guardian(1, 2), Swarm(2, 3)) };
        public EncounterPackAsset[] RewardBranch = {
            Pack("encounter.meadow.cache", Guardian(2, 2), Swarm(2, 3)) };
        public EncounterPackAsset[] ExitGuard = {
            Pack("encounter.meadow.exit", new EncounterGroupAsset { Min = 1, Max = 1,
                Elite = true, HealthPercent = 240, DamagePercent = 160 }, Swarm(2, 3)) };

        private static EncounterGroupAsset Guardian(int min, int max)
            => new EncounterGroupAsset { Min = min, Max = max };
        private static EncounterGroupAsset Swarm(int min, int max)
            => new EncounterGroupAsset { Kind = EnemyKind.ForestRootSwarm, Min = min, Max = max,
                HealthPercent = 30, GrowWithDepth = true };
        private static EncounterPackAsset Pack(string key, params EncounterGroupAsset[] groups)
            => new EncounterPackAsset { StableKey = key, Groups = groups };
        private static EncounterPack[] Compile(EncounterPackAsset[] packs, int level)
        {
            if (packs == null) throw new ArgumentException("Missing encounter pack list.");
            var result = new List<EncounterPack>();
            for (int i = 0; i < packs.Length; i++)
            {
                var pack = packs[i] ?? throw new ArgumentException("Missing encounter pack.");
                if (pack.MinLevel < 1 || pack.MaxLevel < 0 || (pack.MaxLevel > 0 && pack.MaxLevel < pack.MinLevel))
                    throw new ArgumentException("Invalid encounter level range.");
                var compiled = pack.ToDefinition();
                if (level >= pack.MinLevel && (pack.MaxLevel == 0 || level <= pack.MaxLevel)) result.Add(compiled);
            }
            return result.ToArray();
        }
        public EncounterSettings ToDefinition(int level)
        {
            if (level < 1 || level > 100 || MainCount < 1 || MaxMainCount < MainCount || MaxMainCount > 12 ||
                AddMainEveryLevels < 1 || AddEnemyEveryLevels < 1 || MaxExtraEnemies < 0 || MaxExtraEnemies > 8 ||
                DamagePerLevelPercent < 0 || DamagePerLevelPercent > 50 || float.IsNaN(FormationRadius) || float.IsInfinity(FormationRadius))
                throw new ArgumentException("Invalid encounter growth settings.");
            return new EncounterSettings(Compile(Introduction, level), Compile(MainPath, level), Compile(RewardBranch, level), Compile(ExitGuard, level),
                Math.Min(MaxMainCount, MainCount + (level - 1) / AddMainEveryLevels),
                Math.Min(MaxExtraEnemies, (level - 1) / AddEnemyEveryLevels),
                Math.Min(1000, 100 + (level - 1) * DamagePerLevelPercent), Fix64.FromDouble(FormationRadius));
        }
    }
}
