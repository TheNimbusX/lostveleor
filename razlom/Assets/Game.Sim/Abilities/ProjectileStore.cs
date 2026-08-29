namespace Game.Sim
{
    /// <summary>
    /// Снаряды. Те же структуры массивами, что и у сущностей, но с одним
    /// отличием: снаряды ПЕРЕИСПОЛЬЗУЮТСЯ. За минуту боя их рождаются тысячи,
    /// держать каждый до конца забега нельзя.
    ///
    /// Свободные слоты лежат в стеке. Стек детерминирован ровно потому, что
    /// детерминирована последовательность рождений и смертей: одинаковый забег
    /// раздаёт одинаковые слоты. Обход при этом всегда по возрастанию индекса.
    /// </summary>
    public sealed class ProjectileStore
    {
        public readonly int Capacity;

        public readonly FixVec2[] Position;
        public readonly FixVec2[] Target;
        public readonly FixVec2[] Velocity;
        public readonly bool[] Alive;

        /// <summary>Кто пустил. Нужно на попадании: урон засчитывается ему.</summary>
        public readonly int[] Owner;

        /// <summary>Слот способности владельца — по нему находится билд на стадии попадания.</summary>
        public readonly int[] Slot;

        /// <summary>Урон этого конкретного снаряда: у расколотых он свой.</summary>
        public readonly Fix64[] Damage;

        /// <summary>Страховка от снаряда, который почему-то не долетел.</summary>
        public readonly int[] TicksLeft;

        /// <summary>Верхняя граница живых слотов. Обход идёт до неё.</summary>
        public int HighWater { get; private set; }

        private readonly int[] _freeStack;
        private int _freeCount;

        public ProjectileStore(int capacity)
        {
            Capacity = capacity;

            Position = new FixVec2[capacity];
            Target = new FixVec2[capacity];
            Velocity = new FixVec2[capacity];
            Alive = new bool[capacity];
            Owner = new int[capacity];
            Slot = new int[capacity];
            Damage = new Fix64[capacity];
            TicksLeft = new int[capacity];

            _freeStack = new int[capacity];
            Clear();
        }

        public void Clear()
        {
            HighWater = 0;
            _freeCount = 0;

            // Свободные слоты кладутся с конца, чтобы первым снялся нулевой:
            // порядок раздачи должен быть предсказуемым при чтении логов.
            for (int i = Capacity - 1; i >= 0; i--)
            {
                Alive[i] = false;
                _freeStack[_freeCount++] = i;
            }
        }

        /// <summary>Заводит снаряд. Возвращает -1, если пул исчерпан.</summary>
        public int Spawn(FixVec2 origin, FixVec2 target, FixVec2 velocity,
            int owner, int slot, Fix64 damage, int ticksLeft)
        {
            if (_freeCount == 0) return -1;

            int id = _freeStack[--_freeCount];

            Position[id] = origin;
            Target[id] = target;
            Velocity[id] = velocity;
            Owner[id] = owner;
            Slot[id] = slot;
            Damage[id] = damage;
            TicksLeft[id] = ticksLeft;
            Alive[id] = true;

            if (id >= HighWater) HighWater = id + 1;
            return id;
        }

        public void Despawn(int id)
        {
            if (!Alive[id]) return;
            Alive[id] = false;
            _freeStack[_freeCount++] = id;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, HighWater);
            for (int i = 0; i < HighWater; i++)
            {
                Hashing.Mix(ref hash, Alive[i] ? 1 : 0);
                if (!Alive[i]) continue;

                Hashing.Mix(ref hash, Position[i].X);
                Hashing.Mix(ref hash, Position[i].Y);
                Hashing.Mix(ref hash, Target[i].X);
                Hashing.Mix(ref hash, Target[i].Y);
                Hashing.Mix(ref hash, Owner[i]);
                Hashing.Mix(ref hash, Slot[i]);
                Hashing.Mix(ref hash, Damage[i]);
                Hashing.Mix(ref hash, TicksLeft[i]);
            }
        }
    }
}
