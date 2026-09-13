using System;

namespace Game.Sim
{
    public readonly struct CampDummyDefinition
    {
        public readonly FixVec2 Position;
        public readonly int Health;
        public readonly Fix64 Armor, FireResist;
        public CampDummyDefinition(FixVec2 position, int health, Fix64 armor, Fix64 fireResist)
        {
            if (health <= 0) throw new ArgumentOutOfRangeException(nameof(health));
            Position = position; Health = health; Armor = armor; FireResist = fireResist;
        }
    }

    // Мишени живут в общей симуляции лагеря: экипировка и способности те же, что в забеге.
    public sealed class CampTraining
    {
        readonly CampDummyDefinition[] _definitions;
        readonly int[] _ids;
        public long DamageTotal { get; private set; }
        public long FireDamage { get; private set; }
        public long Hits { get; private set; }
        public long Crits { get; private set; }
        public int LastDamage { get; private set; }
        public int Ticks { get; private set; }
        public int Count => _ids.Length;
        public int DamagePerSecond => Ticks == 0 ? 0 : (int)Math.Min(int.MaxValue, DamageTotal * Simulation.TicksPerSecond / Ticks);
        public int EntityId(int index) => _ids[index];
        public bool Contains(int id) => Array.IndexOf(_ids, id) >= 0;

        public CampTraining(CampDummyDefinition[] definitions)
        {
            _definitions = (CampDummyDefinition[])definitions.Clone();
            _ids = new int[definitions.Length];
            Array.Fill(_ids, -1);
        }
        public void Populate(Simulation sim)
        {
            for (int i = 0; i < Count; i++)
                _ids[i] = sim.SpawnCampDummy(_definitions[i]);
            ResetCounters();
        }
        public void ResetCounters()
        {
            DamageTotal = FireDamage = Hits = Crits = 0;
            Ticks = LastDamage = 0;
        }
        public void AfterStep(Simulation sim)
        {
            foreach (var e in sim.Events)
            {
                if ((e.Type != SimEventType.Damage && e.Type != SimEventType.DamageOverTime)
                    || e.Source != Simulation.PlayerId || !Contains(e.Target)) continue;
                DamageTotal += e.Amount;
                LastDamage = e.Amount;
                if (e.DamageKind == DamageType.Fire) FireDamage += e.Amount;
                if (e.Type == SimEventType.Damage) { Hits++; if (e.Flag) Crits++; }
            }
            if (DamageTotal > 0) Ticks++;
            for (int i = 0; i < Count; i++)
            {
                int id = _ids[i];
                if (!sim.Entities.Alive[id]) sim.ReviveDummy(id);
                sim.Entities.Position[id] = _definitions[i].Position;
                sim.Entities.Velocity[id] = FixVec2.Zero;
                ForcedMotion.Clear(sim.Entities, id);
            }
        }
    }
}
