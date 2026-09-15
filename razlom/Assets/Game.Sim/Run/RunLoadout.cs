namespace Game.Sim
{
    /// <summary>
    /// Способности и таланты на время одного забега.
    ///
    /// Решение владельца от 15 сентября: всё, что герой нашёл в Разломе,
    /// живёт до конца забега. Забег начинается с автоатаки и Вихря, три слота
    /// пусты; способности приходят с карточек и с элит, таланты берутся строго
    /// по порядку внутри способности.
    ///
    /// ХРАНИТСЯ ИНДЕКС В ПУЛЕ, А НЕ ОПРЕДЕЛЕНИЕ: так набор хешируется и
    /// сравнивается числами, а ролл карточек видит, чего у игрока ещё нет.
    /// Ранг таланта — число, как и раньше: раз брать можно только следующий,
    /// «сколько взято» и есть полный список взятого.
    /// </summary>
    public sealed class RunLoadout
    {
        public const int Slots = PelagKit.MainSlots;
        public const int EmptySlot = -1;

        private readonly int[] _slots = new int[Slots];
        private readonly int[] _ranks = new int[PelagKit.PoolSize];
        private readonly AbilityNode[] _nodes = new AbilityNode[SabreTalents.TalentsPerLine];

        /// <summary>Растёт при каждой смене набора или ранга.</summary>
        public int Version { get; private set; }

        /// <summary>Растёт при каждом ApplyTo — представление по нему накладывает отладочные узлы заново.</summary>
        public int Applications { get; private set; }

        public RunLoadout() => ResetToStarter();

        /// <summary>Стартовый набор: Вихрь в первом слоте, остальное пусто, талантов нет.</summary>
        public void ResetToStarter()
        {
            for (int i = 0; i < Slots; i++) _slots[i] = EmptySlot;
            System.Array.Clear(_ranks, 0, _ranks.Length);
            _slots[0] = PelagKit.StarterPoolIndex;
            Version++;
        }

        public void CopyFrom(RunLoadout other)
        {
            System.Array.Copy(other._slots, _slots, Slots);
            System.Array.Copy(other._ranks, _ranks, _ranks.Length);
            Version++;
        }

        // ---- слоты ----

        public int PoolIndexAt(int slot) => (uint)slot < Slots ? _slots[slot] : EmptySlot;
        public AbilityDefinition DefinitionAt(int slot) => PelagKit.PoolDefinition(PoolIndexAt(slot));
        public bool IsEmpty(int slot) => PoolIndexAt(slot) == EmptySlot;

        /// <summary>Первый пустой слот или −1.</summary>
        public int FreeSlot()
        {
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == EmptySlot) return i;
            return -1;
        }

        public bool IsFull => FreeSlot() < 0;

        /// <summary>Слот, где лежит способность пула, или −1.</summary>
        public int SlotOf(int poolIndex)
        {
            if (poolIndex == EmptySlot) return -1;
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == poolIndex) return i;
            return -1;
        }

        public bool Owns(int poolIndex) => SlotOf(poolIndex) >= 0;

        /// <summary>
        /// Кладёт способность в слот (EmptySlot — освобождает). Прежняя способность
        /// уходит вместе со своими талантами. Способность, которая уже лежит в
        /// другом слоте, положить нельзя: две одинаковые кнопки — не выбор.
        /// </summary>
        public bool Put(int slot, int poolIndex)
        {
            if ((uint)slot >= Slots) return false;
            if (poolIndex != EmptySlot && (uint)poolIndex >= PelagKit.PoolSize) return false;
            if (_slots[slot] == poolIndex) return true;
            if (Owns(poolIndex)) return false;

            int old = _slots[slot];
            if (old != EmptySlot) _ranks[old] = 0;
            _slots[slot] = poolIndex;
            Version++;
            return true;
        }

        /// <summary>Кладёт новую способность в первый пустой слот. False — панель полна или способность уже есть.</summary>
        public bool Add(int poolIndex)
        {
            int free = FreeSlot();
            return free >= 0 && Put(free, poolIndex);
        }

        // ---- таланты ----

        public int TalentRank(int poolIndex) => (uint)poolIndex < PelagKit.PoolSize ? _ranks[poolIndex] : 0;

        /// <summary>Способность в руках, у неё есть направление талантов, и оно не взято целиком.</summary>
        public bool CanTakeTalent(int poolIndex)
            => Owns(poolIndex)
               && SabreTalents.TryLineOf(poolIndex, out _)
               && _ranks[poolIndex] < SabreTalents.TalentsPerLine;

        /// <summary>Берёт следующий по порядку талант способности.</summary>
        public bool TakeTalent(int poolIndex)
        {
            if (!CanTakeTalent(poolIndex)) return false;
            _ranks[poolIndex]++;
            Version++;
            return true;
        }

        /// <summary>Есть ли у игрока хоть один доступный талант — от этого зависят веса карточек.</summary>
        public bool HasTalentToTake
        {
            get
            {
                for (int i = 0; i < Slots; i++)
                    if (_slots[i] != EmptySlot && CanTakeTalent(_slots[i])) return true;
                return false;
            }
        }

        /// <summary>Дописывает узлы взятых талантов способности в слоте.</summary>
        public int AppendTalentNodes(int slot, AbilityNode[] buffer, int count)
        {
            int pool = PoolIndexAt(slot);
            if (!SabreTalents.TryLineOf(pool, out SabreTalentLine line)) return count;
            return SabreTalents.AppendNodes(line, _ranks[pool], buffer, count);
        }

        /// <summary>Ставит набор в симуляцию: четыре слота и общий кувырок.</summary>
        public void ApplyTo(Simulation sim)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                int count = AppendTalentNodes(slot, _nodes, 0);
                sim.SetAbility(slot, DefinitionAt(slot), _nodes, count);
            }
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), _nodes, 0);
            Applications++;
        }

        public void HashInto(ref ulong hash)
        {
            for (int i = 0; i < Slots; i++) Hashing.Mix(ref hash, _slots[i]);
            for (int i = 0; i < _ranks.Length; i++) Hashing.Mix(ref hash, _ranks[i]);
        }
    }
}
