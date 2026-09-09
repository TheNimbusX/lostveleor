using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public readonly struct EncounterPlacement
    {
        public readonly EncounterRole Role;
        public readonly int Module, Branch, PackId, FirstEntity, EnemyCount;
        public readonly FixVec2 Center;
        public EncounterPlacement(EncounterRole role, int module, int branch, int packId,
            FixVec2 center, int firstEntity, int enemyCount)
        {
            Role = role; Module = module; Branch = branch; PackId = packId;
            Center = center; FirstEntity = firstEntity; EnemyCount = enemyCount;
        }
    }

    /// <summary>Spawned encounters shared by the run, HUD and authoring preview.</summary>
    public sealed class EncounterPlan
    {
        private readonly EncounterPlacement[] _entries;
        private readonly bool[] _elite;
        public readonly Fix64 FormationRadius;
        public readonly int OmittedEnemies;
        public int Count => _entries.Length;
        public int BossId { get; internal set; } = -1;
        public EncounterPlacement Get(int index) => _entries[index];
        public bool IsElite(int entity) => entity >= 0 && entity < _elite.Length && _elite[entity];
        internal EncounterPlan(List<EncounterPlacement> entries, bool[] elite, Fix64 radius, int omitted)
        { _entries = entries.ToArray(); _elite = elite; FormationRadius = radius; OmittedEnemies = omitted; }
        public int ForEntity(int entity)
        {
            for (int i = 0; i < Count; i++)
                if (entity >= _entries[i].FirstEntity && entity < _entries[i].FirstEntity + _entries[i].EnemyCount) return i;
            return -1;
        }
        public int Alive(int index, EntityStore entities)
        {
            var e = _entries[index];
            int count = 0;
            for (int i = e.FirstEntity; i < e.FirstEntity + e.EnemyCount; i++)
                if (entities.Alive[i]) count++;
            return count;
        }
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Count);
            if (BossId >= 0) Hashing.Mix(ref hash, BossId);
            foreach (var e in _entries)
            {
                Hashing.Mix(ref hash, (int)e.Role); Hashing.Mix(ref hash, e.Module);
                Hashing.Mix(ref hash, e.Branch); Hashing.Mix(ref hash, e.PackId);
                Hashing.Mix(ref hash, e.Center.X.Raw); Hashing.Mix(ref hash, e.Center.Y.Raw);
                Hashing.Mix(ref hash, e.FirstEntity); Hashing.Mix(ref hash, e.EnemyCount);
            }
            for (int i = 0; i < _elite.Length; i++) if (_elite[i]) Hashing.Mix(ref hash, i);
        }
    }
}
