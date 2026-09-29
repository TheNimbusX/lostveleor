namespace Game.Sim
{
    /// <summary>
    /// Равномерная сетка для поиска соседей. Заменяет наивный перебор O(n²),
    /// который взрывается ровно на тех сорока врагах, под которые свёрстан
    /// бюджет производительности.
    ///
    /// Раскладка «счётная сортировка по корзинам»: считаем, сколько сущностей
    /// в каждой ячейке, делаем префиксные суммы, раскладываем индексы в один
    /// плоский массив. Ноль аллокаций в бою, данные лежат подряд, и это
    /// та же схема, которую потом переносят в Job с Burst без переписывания.
    ///
    /// ДЕТЕРМИНИЗМ. Порядок обхода ячеек фиксирован (сначала Y, потом X),
    /// внутри ячейки сущности лежат по возрастанию индекса, а при равном
    /// расстоянии побеждает меньший индекс. Без явного разрыва ничьих
    /// результат зависел бы от раскладки по ячейкам, и симуляция разъехалась бы.
    /// </summary>
    public sealed class SpatialHash
    {
        private readonly int _cellsX;
        private readonly int _cellsY;
        private readonly FixVec2 _origin;
        private readonly Fix64 _invCellSize;
        public readonly Fix64 CellSize;

        private readonly int[] _cellCount;   // сколько сущностей в ячейке
        private readonly int[] _cellStart;   // смещение ячейки в _entries
        private readonly int[] _entries;     // индексы сущностей, сгруппированные по ячейкам

        private int _entryCount;

        // Ячейки с кем-то после прошлой пересборки лежат в [_usedLow, _usedHigh]:
        // вне диапазона счётчики — нули, и пересборке не нужно трижды обходить
        // все 4096 ячеек ради полусотни тел (поток D, 29.09). Ячейка каждого
        // тела запоминается в первом проходе и не считается второй раз.
        private int _usedLow, _usedHigh = -1;
        private readonly int[] _entityCell;

        public int CellCount => _cellsX * _cellsY;

        // Раскладка для обхода пар соседей по ячейкам (Simulation.SeparateBodies):
        // массивы отдаются как есть, чтобы горячий цикл не звал метод на каждое
        // тело. Только чтение. Начало пустой ячейки не значит ничего — смотреть
        // сначала на счётчик.
        internal int CellsX => _cellsX;
        internal int CellsY => _cellsY;
        internal int UsedLow => _usedLow;
        internal int UsedHigh => _usedHigh;
        internal int[] CellStarts => _cellStart;
        internal int[] CellCounts => _cellCount;
        internal int[] Entries => _entries;

        /// <param name="origin">Левый нижний угол сетки в мировых координатах.</param>
        /// <param name="cellSize">Сторона ячейки. Брать не меньше самого большого радиуса запроса.</param>
        public SpatialHash(FixVec2 origin, Fix64 cellSize, int cellsX, int cellsY, int capacity)
        {
            _origin = origin;
            CellSize = cellSize;
            _invCellSize = Fix64.One / cellSize;
            _cellsX = cellsX;
            _cellsY = cellsY;

            _cellCount = new int[cellsX * cellsY];
            _cellStart = new int[cellsX * cellsY + 1];
            _entries = new int[capacity];
            _entityCell = new int[capacity];
        }

        /// <summary>Ячейка точки. Координаты за пределами сетки прижимаются к краю:
        /// это не портит результат, потому что расстояние всё равно проверяется явно.</summary>
        private void CellOf(FixVec2 p, out int cx, out int cy)
        {
            cx = ((p.X - _origin.X) * _invCellSize).ToInt();
            cy = ((p.Y - _origin.Y) * _invCellSize).ToInt();
            if (cx < 0) cx = 0; else if (cx >= _cellsX) cx = _cellsX - 1;
            if (cy < 0) cy = 0; else if (cy >= _cellsY) cy = _cellsY - 1;
        }

        /// <summary>
        /// Полная пересборка. Для наших объёмов это дешевле инкрементального
        /// обновления и не оставляет места рассинхрону между сеткой и миром.
        /// </summary>
        public void Rebuild(EntityStore entities)
        {
            int cells = _cellsX * _cellsY;
            // Вне прошлого занятого диапазона счётчики и так нули.
            for (int c = _usedLow; c <= _usedHigh; c++) _cellCount[c] = 0;

            // 1. Счёт
            int low = cells, high = -1;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                CellOf(entities.Position[i], out int cx, out int cy);
                int c = cy * _cellsX + cx;
                _entityCell[i] = c;
                _cellCount[c]++;
                if (c < low) low = c;
                if (c > high) high = c;
            }

            // 2. Префиксные суммы — по занятому диапазону. Начало пустой ячейки
            // запросы не читают: при нулевом счётчике обход её не заходит.
            int running = 0;
            for (int c = low; c <= high; c++)
            {
                _cellStart[c] = running;
                running += _cellCount[c];
            }
            _cellStart[cells] = running;
            _entryCount = running;

            // 3. Раскладка. Идём по возрастанию индекса сущности, поэтому
            // внутри каждой ячейки индексы тоже возрастают.
            for (int c = low; c <= high; c++) _cellCount[c] = 0;

            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                int c = _entityCell[i];
                _entries[_cellStart[c] + _cellCount[c]] = i;
                _cellCount[c]++;
            }
            _usedLow = high >= 0 ? low : 0;
            _usedHigh = high;
        }

        /// <summary>
        /// Ближайшая живая сущность чужой стороны в радиусе И во фронтальном
        /// секторе. Возвращает -1, если никого нет.
        ///
        /// minFacingDot — косинус половины сектора: −1 отключает фильтр,
        /// 0 даёт полусферу перед собой, 0.5 — сектор в 120°.
        /// Бить за спину нельзя, поэтому сектор входит в САМ ПОИСК цели:
        /// иначе персонаж выбирал бы ближайшего врага у себя за спиной
        /// и не бил бы вообще, стоя лицом к другому.
        /// </summary>
        public int FindNearestEnemy(EntityStore entities, int from, Fix64 radius, Fix64 minFacingDot)
        {
            FixVec2 origin = entities.Position[from];
            FixVec2 facing = entities.Facing[from];
            Faction mySide = entities.Side[from];
            Fix64 radiusSq = radius * radius;

            CellOf(new FixVec2(origin.X - radius, origin.Y - radius), out int minX, out int minY);
            CellOf(new FixVec2(origin.X + radius, origin.Y + radius), out int maxX, out int maxY);

            int best = -1;
            Fix64 bestDistSq = Fix64.MaxValue;

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    int c = cy * _cellsX + cx;
                    int start = _cellStart[c];
                    int end = start + _cellCount[c];

                    for (int k = start; k < end; k++)
                    {
                        int i = _entries[k];
                        if (i == from) continue;

                        // ОБЯЗАТЕЛЬНО. Сетка — это снимок на момент пересборки,
                        // а сущности умирают в течение того же тика. Без этой
                        // проверки уже убитая цель остаётся выбираемой, и поиск
                        // по сетке расходится с прямым перебором.
                        if (!entities.Alive[i]) continue;

                        if (entities.Side[i] == mySide) continue;

                        FixVec2 toTarget = entities.Position[i] - origin;
                        Fix64 distSq = toTarget.LengthSq;
                        if (distSq > radiusSq) continue;
                        if (!FixVec2.WithinArc(facing, toTarget, minFacingDot)) continue;

                        // Разрыв ничьей по индексу обязателен: без него результат
                        // зависел бы от того, в какие ячейки легли сущности.
                        if (distSq < bestDistSq || (distSq == bestDistSq && i < best))
                        {
                            bestDistSq = distSq;
                            best = i;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Все живые сущности в радиусе, кроме самой запрашивающей.
        /// Результат пишется в переданный буфер; возвращается количество.
        /// Буфер выделяется один раз и переиспользуется — в бою аллокаций быть не должно.
        /// </summary>
        public int QueryRadius(EntityStore entities, FixVec2 center, Fix64 radius, int exclude, int[] results)
        {
            Fix64 radiusSq = radius * radius;

            CellOf(new FixVec2(center.X - radius, center.Y - radius), out int minX, out int minY);
            CellOf(new FixVec2(center.X + radius, center.Y + radius), out int maxX, out int maxY);

            int count = 0;
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    int c = cy * _cellsX + cx;
                    int start = _cellStart[c];
                    int end = start + _cellCount[c];

                    for (int k = start; k < end && count < results.Length; k++)
                    {
                        int i = _entries[k];
                        if (i == exclude) continue;
                        if (!entities.Alive[i]) continue; // см. комментарий в FindNearestEnemy
                        if (FixVec2.DistanceSq(center, entities.Position[i]) > radiusSq) continue;
                        results[count++] = i;
                    }
                }
            }
            return count;
        }

        /// <summary>Диагностика: сколько сущностей разложено и какая ячейка самая забитая.</summary>
        public void GetStats(out int entries, out int busiestCell)
        {
            entries = _entryCount;
            busiestCell = 0;
            int cells = _cellsX * _cellsY;
            for (int c = 0; c < cells; c++)
                if (_cellCount[c] > busiestCell) busiestCell = _cellCount[c];
        }
    }
}
