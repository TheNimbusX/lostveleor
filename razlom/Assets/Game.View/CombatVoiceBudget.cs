namespace Game.View
{
    // Политика отделена от AudioSource: занятый отложенный голос тоже защищён,
    // а выбор при перегрузке можно проверить без звукового устройства.
    public sealed class CombatVoiceBudget
    {
        private readonly double[] _until;
        private readonly int[] _priority;
        public int Dropped { get; private set; }
        public int Stolen { get; private set; }

        public CombatVoiceBudget(int capacity)
        {
            _until = new double[capacity];
            _priority = new int[capacity];
        }

        public int Acquire(double now, double duration, int priority)
        {
            int candidate = -1;
            for (int i = 0; i < _until.Length; i++)
            {
                if (_until[i] <= now) { candidate = i; break; }
                if (_priority[i] >= priority) continue;
                if (candidate < 0 || _priority[i] < _priority[candidate]
                    || (_priority[i] == _priority[candidate] && _until[i] < _until[candidate]))
                    candidate = i;
            }
            if (candidate < 0) { Dropped++; return -1; }
            if (_until[candidate] > now) Stolen++;
            _until[candidate] = now + System.Math.Max(0.001, duration);
            _priority[candidate] = priority;
            return candidate;
        }

        public void Clear()
        {
            System.Array.Clear(_until, 0, _until.Length);
            Dropped = Stolen = 0;
        }

        public void Release(int slot) => _until[slot] = 0;
    }
}
