namespace Game.Sim
{
    /// <summary>
    /// Итоговые статы одной сущности.
    ///
    /// Формула, модель Path of Exile, менять её нельзя:
    ///
    ///     final = (base + flat) * (1 + sumIncreased) * product(1 + more_i)
    ///
    /// Пересчёт идёт по грязному флагу — при смене снаряжения, узлов, бафов
    /// или стаков, — а не каждый кадр. Это не микрооптимизация: на позднем
    /// билде на персонаже висит две-три сотни модификаторов, и пересчёт
    /// «на всякий случай» каждый кадр на каждой сущности — главная причина,
    /// по которой ARPG начинают тормозить именно там, где играют дольше всего.
    ///
    /// Get стоит одно обращение к массиву.
    /// </summary>
    public sealed class StatSheet
    {
        private const int StatCount = (int)StatType.Count;

        private readonly Fix64[] _base = new Fix64[StatCount];
        private readonly Fix64[] _final = new Fix64[StatCount];

        // Рабочие буферы пересчёта. Выделяются один раз: пересчёт не должен
        // порождать мусор, сколько бы раз за забег он ни случился.
        private readonly Fix64[] _flat = new Fix64[StatCount];
        private readonly Fix64[] _increased = new Fix64[StatCount];
        private readonly Fix64[] _more = new Fix64[StatCount];

        // Модификаторы держатся отсортированными по StatModifier.CompareTo.
        // Массив, а не список объектов: они пересчитываются пачкой и обязаны
        // лежать подряд. Порядок канонический, а не порядок действий игрока.
        private StatModifier[] _mods;
        private int _modCount;

        private bool _dirty = true;

        /// <summary>
        /// Сколько раз пересчитывались итоговые значения. Только для тестов
        /// и профилировки: доказывает, что Get не считает ничего, пока лист
        /// не испачкан.
        /// </summary>
        public int RecalculateCount { get; private set; }

        public int ModifierCount => _modCount;
        public bool IsDirty => _dirty;

        public StatSheet(int modifierCapacity = 32)
        {
            _mods = new StatModifier[modifierCapacity < 1 ? 1 : modifierCapacity];
        }

        // ---- база ----

        /// <summary>Базовое значение до модификаторов: народ, класс, тип врага.</summary>
        public void SetBase(StatType stat, Fix64 value)
        {
            if (_base[(int)stat] == value) return;
            _base[(int)stat] = value;
            _dirty = true;
        }

        public Fix64 GetBase(StatType stat) => _base[(int)stat];

        // ---- модификаторы ----

        /// <summary>
        /// Добавляет модификатор, сохраняя канонический порядок списка.
        /// Вставка стоит сдвига хвоста, но случается только при смене
        /// снаряжения или бафа, а не в горячем пути.
        /// </summary>
        public void Add(in StatModifier mod)
        {
            if (_modCount == _mods.Length)
            {
                var grown = new StatModifier[_mods.Length * 2];
                System.Array.Copy(_mods, grown, _modCount);
                _mods = grown;
            }

            int at = UpperBound(in mod);
            if (at < _modCount)
                System.Array.Copy(_mods, at, _mods, at + 1, _modCount - at);

            _mods[at] = mod;
            _modCount++;
            _dirty = true;
        }

        /// <summary>
        /// Первый индекс, где элемент строго больше mod. Вставка сюда держит
        /// одинаковые модификаторы в порядке добавления, но их порядок между
        /// собой на результат не влияет: они равны побитово.
        /// </summary>
        private int UpperBound(in StatModifier mod)
        {
            int lo = 0, hi = _modCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_mods[mid].CompareTo(in mod) <= 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Снимает все модификаторы одного носителя: сняли кольцо — ушли ровно
        /// его прибавки. Возвращает, сколько снято.
        /// </summary>
        public int RemoveSource(ModifierSource source, int sourceId)
        {
            int write = 0;
            for (int read = 0; read < _modCount; read++)
            {
                if (_mods[read].Source == source && _mods[read].SourceId == sourceId) continue;
                if (write != read) _mods[write] = _mods[read];
                write++;
            }

            int removed = _modCount - write;
            if (removed > 0)
            {
                _modCount = write;
                _dirty = true;
            }
            return removed;
        }

        public void ClearModifiers()
        {
            if (_modCount == 0) return;
            _modCount = 0;
            _dirty = true;
        }

        public StatModifier GetModifier(int index) => _mods[index];

        // ---- чтение ----

        /// <summary>Итоговое значение стата. Одно обращение к массиву, если лист чист.</summary>
        public Fix64 Get(StatType stat)
        {
            if (_dirty) Recalculate();
            return _final[(int)stat];
        }

        /// <summary>
        /// Считает все статы за один проход по модификаторам.
        ///
        /// Публичный, чтобы пересчёт можно было провести в заранее выбранной
        /// точке кадра, а не поймать его случайным первым Get посреди боя.
        /// </summary>
        public void Recalculate()
        {
            for (int i = 0; i < StatCount; i++)
            {
                _flat[i] = Fix64.Zero;
                _increased[i] = Fix64.Zero;
                _more[i] = Fix64.One;
            }

            // Один проход: список отсортирован по стату и слою, поэтому
            // сомножители слоя More перемножаются в каноническом порядке.
            // Умножение в Fix64 округляет и не ассоциативно — порядок
            // обязан зависеть только от состава билда, но не от того,
            // в какой последовательности игрок его собрал.
            for (int i = 0; i < _modCount; i++)
            {
                int s = (int)_mods[i].Stat;
                Fix64 v = _mods[i].Value;

                switch (_mods[i].Op)
                {
                    case ModifierOp.Flat:
                        _flat[s] += v;
                        break;
                    case ModifierOp.Increased:
                        _increased[s] += v;
                        break;
                    case ModifierOp.More:
                        _more[s] *= Fix64.One + v;
                        break;
                }
            }

            for (int i = 0; i < StatCount; i++)
                _final[i] = StatMath.Combine(_base[i], _flat[i], _increased[i], _more[i]);

            RecalculateCount++;
            _dirty = false;
        }

        /// <summary>
        /// Лист статов — часть состояния сущности, значит и часть хеша.
        /// Хешируются база и модификаторы, а не итог: итог из них выводится,
        /// а расхождение в исходных данных так видно раньше.
        /// </summary>
        public void HashInto(ref ulong hash)
        {
            for (int i = 0; i < StatCount; i++)
                Hashing.Mix(ref hash, _base[i]);

            Hashing.Mix(ref hash, _modCount);
            for (int i = 0; i < _modCount; i++)
                _mods[i].HashInto(ref hash);
        }
    }
}
