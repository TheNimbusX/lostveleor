namespace Game.Sim
{
    /// <summary>
    /// Наложенные состояния, по одному массиву на сущность. Пока только горение.
    ///
    /// Отдельно от EntityStore намеренно: статусов со временем станет много,
    /// а хеш состояния сущности не должен разрастаться каждым новым эффектом.
    /// </summary>
    public sealed class StatusStore
    {
        public readonly int Capacity;

        /// <summary>Сколько тиков ещё горит. Ноль — не горит.</summary>
        public readonly int[] BurnTicksLeft;

        /// <summary>Урон горения за тик.</summary>
        public readonly Fix64[] BurnDamage;

        /// <summary>Кто поджёг. Убийство горением засчитывается ему.</summary>
        public readonly int[] BurnSource;

        /// <summary>Слот способности, которой подожгли: по нему берётся билд на стадии убийства.</summary>
        public readonly int[] BurnSlot;

        public StatusStore(int capacity)
        {
            Capacity = capacity;
            BurnTicksLeft = new int[capacity];
            BurnDamage = new Fix64[capacity];
            BurnSource = new int[capacity];
            BurnSlot = new int[capacity];
        }

        public bool IsBurning(int entity) => BurnTicksLeft[entity] > 0;

        /// <summary>
        /// Поджиг не складывается, а обновляется по силе: более слабый поджиг
        /// не затирает более сильный, но продлевает его. Правило простое
        /// и объяснимое игроку, в отличие от стаков, которые придут позже.
        /// </summary>
        public void ApplyBurn(int entity, Fix64 damagePerTick, int ticks, int source, int slot)
        {
            if (damagePerTick >= BurnDamage[entity])
            {
                BurnDamage[entity] = damagePerTick;
                BurnSource[entity] = source;
                BurnSlot[entity] = slot;
            }

            if (ticks > BurnTicksLeft[entity]) BurnTicksLeft[entity] = ticks;
        }

        public void ClearBurn(int entity)
        {
            BurnTicksLeft[entity] = 0;
            BurnDamage[entity] = Fix64.Zero;
            BurnSource[entity] = -1;
            BurnSlot[entity] = -1;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) ClearBurn(i);
        }

        public void HashInto(ref ulong hash, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Hashing.Mix(ref hash, BurnTicksLeft[i]);
                if (BurnTicksLeft[i] == 0) continue;

                Hashing.Mix(ref hash, BurnDamage[i]);
                Hashing.Mix(ref hash, BurnSource[i]);
                Hashing.Mix(ref hash, BurnSlot[i]);
            }
        }
    }
}
