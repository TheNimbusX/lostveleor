namespace Game.Sim
{
    /// <summary>
    /// Инвентарь СЛОТАМИ, без тетриса. Решение принято и меняться не должно:
    /// возня с укладкой рюкзака — это не то решение, ради которого играют
    /// в гриндилку, а налог на каждый поднятый предмет.
    ///
    /// Хранит рецепты. Числа предмета разворачиваются при показе и не
    /// запоминаются, поэтому ребаланс аффиксов применяется сам.
    ///
    /// Метка «беречь» — единственное, что отличает предметы друг от друга
    /// с точки зрения инвентаря: по ней и работает автразбор. Игрок помечает
    /// то, что оставляет, а не то, что выбрасывает — помеченного всегда меньше.
    /// </summary>
    public sealed class Inventory
    {
        private readonly ItemInstance[] _slots;
        private readonly bool[] _keep;

        public int Capacity => _slots.Length;

        /// <summary>Сколько слотов занято. Считается на месте: инвентарь мал.</summary>
        public int Used
        {
            get
            {
                int used = 0;
                for (int i = 0; i < _slots.Length; i++)
                    if (!_slots[i].IsEmpty) used++;
                return used;
            }
        }

        public int Free => Capacity - Used;
        public bool IsFull => Free == 0;

        /// <summary>
        /// Сколько лежит непомеченного, то есть уйдёт в осколки при автразборе.
        /// Читается экраном итогов забега: это одна из причин зайти в лагерь,
        /// а не жать «повторить».
        /// </summary>
        public int UnkeptCount
        {
            get
            {
                int junk = 0;
                for (int i = 0; i < _slots.Length; i++)
                    if (!_slots[i].IsEmpty && !_keep[i]) junk++;
                return junk;
            }
        }

        public Inventory(int capacity)
        {
            _slots = new ItemInstance[capacity];
            _keep = new bool[capacity];
        }

        public ItemInstance At(int slot) => _slots[slot];
        public bool IsKept(int slot) => _keep[slot];
        public bool IsEmpty(int slot) => _slots[slot].IsEmpty;

        /// <summary>
        /// Кладёт предмет в первый свободный слот. Возвращает номер слота
        /// или -1, если места нет.
        ///
        /// Первый свободный, а не в конец: слоты освобождаются в середине,
        /// и дырки обязаны заполняться, иначе инвентарь «кончится» при
        /// половине занятых слотов.
        /// </summary>
        public int Add(in ItemInstance item)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].IsEmpty) continue;

                _slots[i] = item;
                _keep[i] = false;
                return i;
            }
            return -1;
        }

        public ItemInstance Remove(int slot)
        {
            ItemInstance was = _slots[slot];
            _slots[slot] = default;
            _keep[slot] = false;
            return was;
        }

        public void SetKeep(int slot, bool keep)
        {
            if (_slots[slot].IsEmpty) return;
            _keep[slot] = keep;
        }

        /// <summary>
        /// Автразбор по фильтру: всё непомеченное уходит в осколки.
        /// Возвращает, сколько осколков вышло.
        ///
        /// Разбор — это то, ради чего инвентарь вообще может переполниться
        /// и остаться играбельным: место освобождается одним нажатием, а не
        /// двадцатью.
        /// </summary>
        public int SalvageUnkept()
        {
            int shards = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty || _keep[i]) continue;

                shards += ShardsFor(_slots[i]);
                _slots[i] = default;
            }
            return shards;
        }

        /// <summary>
        /// Сколько осколков даёт предмет.
        ///
        /// ЗАГЛУШКА БАЛАНСА, того же рода, что числа в PrototypeContent:
        /// настоящая шкала настраивается вместе со стоимостью перековки,
        /// а она в открытых вопросах. Важно здесь одно — что редкость
        /// и уровень предмета вообще влияют на выход.
        /// </summary>
        public static int ShardsFor(in ItemInstance item)
        {
            int byRarity;
            switch (item.Rarity)
            {
                case ItemRarity.Rare: byRarity = 8; break;
                case ItemRarity.Magic: byRarity = 3; break;
                default: byRarity = 1; break;
            }

            return byRarity + item.ItemLevel / 5;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = default;
                _keep[i] = false;
            }
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, _slots.Length);
            for (int i = 0; i < _slots.Length; i++)
            {
                Hashing.Mix(ref hash, _slots[i].IsEmpty ? 0 : 1);
                if (_slots[i].IsEmpty) continue;

                _slots[i].HashInto(ref hash);
                Hashing.Mix(ref hash, _keep[i] ? 1 : 0);
            }
        }
    }
}
