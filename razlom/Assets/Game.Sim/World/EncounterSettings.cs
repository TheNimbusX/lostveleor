using System;

namespace Game.Sim
{
    public enum EncounterRole : byte { Introduction, MainPath, RewardBranch, ExitGuard }

    public readonly struct EncounterGroup
    {
        public readonly EnemyKind Kind;
        public readonly int Min, Max, HealthPercent, DamagePercent;
        public readonly bool Elite, GrowWithDepth;
        public EncounterGroup(EnemyKind kind, int min, int max, int healthPercent = 100,
            int damagePercent = 100, bool elite = false, bool growWithDepth = false)
        {
            if ((kind != EnemyKind.ForestGuardian && kind != EnemyKind.ForestRootSwarm) ||
                min < 0 || max < min || max > 16 || healthPercent < 1 || healthPercent > 1000 ||
                damagePercent < 1 || damagePercent > 1000)
                throw new ArgumentException("Invalid encounter enemy group.");
            Kind = kind; Min = min; Max = max; HealthPercent = healthPercent;
            DamagePercent = damagePercent; Elite = elite; GrowWithDepth = growWithDepth;
        }
    }

    public sealed class EncounterPack
    {
        public readonly int Id, Weight;
        private readonly EncounterGroup[] _groups;
        public int GroupCount => _groups.Length;
        public EncounterGroup GetGroup(int index) => _groups[index];
        public EncounterPack(int id, int weight, EncounterGroup[] groups)
        {
            if (weight < 1 || weight > 10000 || groups == null || groups.Length == 0 || groups.Length > 8)
                throw new ArgumentException("A pack needs a positive weight and 1–8 groups.");
            int guaranteed = 0;
            foreach (var g in groups)
            {
                if (g.HealthPercent <= 0 || g.DamagePercent <= 0) throw new ArgumentException("Uninitialized group.");
                guaranteed += g.Min;
            }
            if (guaranteed == 0) throw new ArgumentException("A pack must guarantee at least one enemy.");
            Id = id; Weight = weight; _groups = (EncounterGroup[])groups.Clone();
        }
        public int MaxEnemies(int bonus)
        {
            int count = 0;
            foreach (var g in _groups) count += g.Max + (g.GrowWithDepth ? bonus : 0);
            return count;
        }
    }

    /// <summary>Compiled per-level encounter balance. Contains no authoring or Unity objects.</summary>
    public sealed class EncounterSettings
    {
        private readonly EncounterPack[][] _packs;
        public readonly int MainCount, CountBonus, DamagePercent;
        public readonly Fix64 FormationRadius;
        public EncounterSettings(EncounterPack[] introduction, EncounterPack[] main, EncounterPack[] reward,
            EncounterPack[] exit, int mainCount, int countBonus, int damagePercent, Fix64 formationRadius)
        {
            if (mainCount < 1 || mainCount > 12 || countBonus < 0 || countBonus > 8 ||
                damagePercent < 1 || damagePercent > 1000 || formationRadius < Fix64.FromInt(2) ||
                formationRadius > Fix64.FromInt(12)) throw new ArgumentException("Invalid encounter progression.");
            _packs = new[] { Copy(introduction), Copy(main), Copy(reward), Copy(exit) };
            foreach (var pack in _packs[(int)EncounterRole.ExitGuard])
            {
                bool guardian = false;
                for (int g = 0; g < pack.GroupCount; g++)
                {
                    var group = pack.GetGroup(g);
                    guardian |= group.Kind == EnemyKind.ForestGuardian && group.Elite && group.Min > 0;
                }
                if (!guardian) throw new ArgumentException("Each exit pack must guarantee an elite Guardian.");
            }
            MainCount = mainCount; CountBonus = countBonus; DamagePercent = damagePercent;
            FormationRadius = formationRadius;
        }
        private static EncounterPack[] Copy(EncounterPack[] source)
        {
            if (source == null || source.Length == 0 || source.Length > 32)
                throw new ArgumentException("Supply 1–32 packs for each encounter role.");
            var result = (EncounterPack[])source.Clone();
            foreach (var p in result) if (p == null) throw new ArgumentException("Missing encounter pack.");
            Array.Sort(result, (a, b) => a.Id.CompareTo(b.Id));
            for (int i = 1; i < result.Length; i++)
                if (result[i].Id == result[i - 1].Id) throw new ArgumentException("Duplicate encounter pack key.");
            return result;
        }
        public EncounterPack Pick(EncounterRole role, ref Pcg32 rng)
        {
            var packs = _packs[(int)role];
            int total = 0;
            foreach (var p in packs) total += p.Weight;
            int roll = rng.NextInt(0, total);
            foreach (var p in packs) { roll -= p.Weight; if (roll < 0) return p; }
            return packs[packs.Length - 1];
        }
        private int MaxFor(EncounterRole role)
        {
            int count = 0;
            foreach (var p in _packs[(int)role]) count = Math.Max(count, p.MaxEnemies(CountBonus));
            return count;
        }
        public int CapacityNeeded(int exits, int branches)
            => 1 + MaxFor(EncounterRole.Introduction) + MainCount * MaxFor(EncounterRole.MainPath)
                + exits * MaxFor(EncounterRole.ExitGuard) + branches * MaxFor(EncounterRole.RewardBranch);
    }
}
