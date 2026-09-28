using System;

namespace Game.Sim
{
    /// <summary>
    /// ПОИСК ПУТИ МОБОВ: поля расстояний до выбранной цели по клеткам арены.
    ///
    /// Раньше пути не было нигде: моб шёл к герою по прямой, скользил вдоль
    /// стены одной осью и за круглым камнем дёргался на месте. Теперь у
    /// каждой проходимой клетки LayoutRoutes (2 м) есть цена пути до клетки
    /// героя, и упёршийся моб идёт по убыванию цены, а не в камень.
    ///
    /// Сетка — чистая функция карты: проходимость клеток и рёбер считается
    /// один раз на карту (деревья SolidEnvironment ставятся после маршрута,
    /// поэтому своя проверка, а не та, что в LayoutRoutes). Поле — чистая
    /// функция клетки цели. До 32 последних полей сохраняются, чтобы подход
    /// к союзнику или точке захода не вытеснял путь остальных мобов к герою.
    /// Ни сетка, ни поля не состояние боя: пересчитай их в любой тик — выйдет
    /// то же самое, поэтому в хеш они не идут.
    ///
    /// Цены целые: 10 по прямой, 14 по диагонали. Диагональ открыта, только
    /// когда открыты обе прямые соседки — угол препятствия не срезается.
    /// Соседи обходятся в фиксированном порядке, ничьи — младшим индексом.
    /// </summary>
    internal sealed class EnemyNavGrid
    {
        /// <summary>Клетка открыта, если в её центре помещается тело такого радиуса.</summary>
        public static readonly Fix64 CellRadius = Fix64.Ratio(6, 10);

        /// <summary>Ребро открыто, если телом такого радиуса можно пройти между центрами.</summary>
        public static readonly Fix64 EdgeRadius = Fix64.Ratio(1, 2);

        private const int Unreached = int.MaxValue;
        private const int CachedFieldCount = 32;
        private static readonly int[] StepX = { 1, 0, -1, 0, 1, -1, -1, 1 };
        private static readonly int[] StepY = { 0, 1, 0, -1, 1, 1, -1, -1 };

        public readonly LayoutMap Map;
        public readonly int ObstacleCount;
        private readonly LayoutRoutes _routes;
        private readonly int _count;
        private readonly bool[] _open;
        private readonly int[] _next;     // _count × 8, −1 — ребра нет
        private readonly int[][] _fieldCosts = new int[CachedFieldCount][];
        private readonly int[] _fieldTargets = new int[CachedFieldCount];
        private readonly bool[] _done;
        private readonly int[] _heapNodes, _heapPositions;
        private int _nextField, _heapCount;

        public EnemyNavGrid(LayoutMap map)
        {
            Map = map;
            ObstacleCount = map.ObstacleCount;
            _routes = map.Routes;
            _count = _routes.CellCount;
            _open = new bool[_count];
            _next = new int[_count * 8];
            _done = new bool[_count];
            _heapNodes = new int[_count];
            _heapPositions = new int[_count];
            for (int slot = 0; slot < CachedFieldCount; slot++)
            {
                _fieldCosts[slot] = new int[_count];
                _fieldTargets[slot] = -1;
            }
            for (int i = 0; i < _count; i++) _open[i] = map.IsWalkable(_routes.GetCell(i).Center, CellRadius);
            for (int i = 0; i < _count; i++)
            {
                for (int d = 0; d < 8; d++) _next[i * 8 + d] = -1;
                if (!_open[i]) continue;
                var cell = _routes.GetCell(i);
                for (int d = 0; d < 4; d++) _next[i * 8 + d] = Link(cell, d);
            }
            // Диагонали — после прямых: им нужны обе прямые соседки.
            for (int i = 0; i < _count; i++)
            {
                if (!_open[i]) continue;
                var cell = _routes.GetCell(i);
                for (int d = 4; d < 8; d++)
                {
                    int a = d == 4 ? 0 : d == 5 ? 2 : d == 6 ? 2 : 0;   // прямая по X
                    int b = d == 4 ? 1 : d == 5 ? 1 : d == 6 ? 3 : 3;   // прямая по Y
                    if (_next[i * 8 + a] < 0 || _next[i * 8 + b] < 0) continue;
                    _next[i * 8 + d] = Link(cell, d);
                }
            }
        }

        private int Link(LayoutRoutes.Cell cell, int direction)
        {
            var center = cell.Center;
            var other = center + new FixVec2(LayoutMap.CellSize * StepX[direction], LayoutMap.CellSize * StepY[direction]);
            int index = _routes.CellAt(other);
            if (index < 0 || !_open[index]) return -1;
            return Map.CanTravel(center, _routes.GetCell(index).Center, EdgeRadius) ? index : -1;
        }

        /// <summary>Годится ли сетка для этой карты: та же карта и те же препятствия.</summary>
        public bool Matches(LayoutMap map) => ReferenceEquals(map, Map) && map.ObstacleCount == ObstacleCount
            && ReferenceEquals(map.Routes, _routes);

        /// <summary>Ближайшая открытая клетка к точке: своя, если открыта, иначе перебор по порядку.</summary>
        private int OpenCellNear(FixVec2 point)
        {
            int cell = _routes.CellAt(point);
            if (cell >= 0 && _open[cell]) return cell;
            int best = -1;
            Fix64 bestDistance = Fix64.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                if (!_open[i]) continue;
                Fix64 distance = FixVec2.DistanceSq(point, _routes.GetCell(i).Center);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Поле цен до клетки цели. Дейкстра с двоичной очередью за O(n log n),
        /// только при промахе кеша; массивы очереди и полей выделены заранее.
        /// Вытеснение по кругу меняет лишь стоимость расчёта, а не его результат.
        /// </summary>
        private int[] FieldFor(int target)
        {
            for (int slot = 0; slot < CachedFieldCount; slot++)
                if (_fieldTargets[slot] == target) return _fieldCosts[slot];
            int field = _nextField;
            _nextField = (_nextField + 1) % CachedFieldCount;
            int[] costs = _fieldCosts[field];
            _fieldTargets[field] = target;
            for (int i = 0; i < _count; i++)
            { costs[i] = Unreached; _done[i] = false; _heapPositions[i] = -1; }
            _heapCount = 0;
            costs[target] = 0;
            QueueOrDecrease(target, costs);
            while (_heapCount > 0)
            {
                int current = PopCheapest(costs), best = costs[current];
                _done[current] = true;
                for (int d = 0; d < 8; d++)
                {
                    int next = _next[current * 8 + d];
                    if (next < 0 || _done[next]) continue;
                    int cost = best + (d < 4 ? 10 : 14);
                    if (cost >= costs[next]) continue;
                    costs[next] = cost;
                    QueueOrDecrease(next, costs);
                }
            }
            return costs;
        }

        // Равные цены разрешаются меньшим индексом, как в прежнем полном переборе.
        private static bool ComesBefore(int left, int right, int[] costs)
            => costs[left] < costs[right] || (costs[left] == costs[right] && left < right);

        private void QueueOrDecrease(int node, int[] costs)
        {
            int position = _heapPositions[node];
            if (position < 0) position = _heapCount++;
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentNode = _heapNodes[parent];
                if (!ComesBefore(node, parentNode, costs)) break;
                _heapNodes[position] = parentNode;
                _heapPositions[parentNode] = position;
                position = parent;
            }
            _heapNodes[position] = node;
            _heapPositions[node] = position;
        }

        private int PopCheapest(int[] costs)
        {
            int result = _heapNodes[0];
            int last = _heapNodes[--_heapCount];
            _heapPositions[result] = -1;
            if (_heapCount == 0) return result;
            int position = 0;
            while (true)
            {
                int child = position * 2 + 1;
                if (child >= _heapCount) break;
                if (child + 1 < _heapCount && ComesBefore(_heapNodes[child + 1], _heapNodes[child], costs)) child++;
                int childNode = _heapNodes[child];
                if (!ComesBefore(childNode, last, costs)) break;
                _heapNodes[position] = childNode;
                _heapPositions[childNode] = position;
                position = child;
            }
            _heapNodes[position] = last;
            _heapPositions[last] = position;
            return result;
        }

        /// <summary>
        /// Куда идти от from к цели: к самой дальней клетке по убыванию цены
        /// (до трёх шагов вперёд), до которой телом radius видно по прямой.
        /// false — пути нет: цель в отрезанном кармане или моб вне сетки.
        /// </summary>
        public bool TryHeading(FixVec2 from, FixVec2 goal, Fix64 radius, out FixVec2 heading)
        {
            heading = FixVec2.Zero;
            int target = OpenCellNear(goal);
            if (target < 0) return false;
            int[] costs = FieldFor(target);
            int own = _routes.CellAt(from);
            if (own >= 0 && _open[own] && costs[own] == 0)
            {
                heading = (goal - from).Normalized();
                return heading.LengthSq.Raw != 0;
            }
            // Лучшая по цене из восьми соседних клеток, до центра которой видно
            // телом radius. Своя клетка может быть закрыта (моб прижат к стволу)
            // — тогда соседи всё равно ищутся от точки, где он стоит, и он не
            // бегает туда-обратно к ближайшей открытой.
            int best = -1, bestCost = Unreached;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int next = _routes.CellAt(from + new FixVec2(LayoutMap.CellSize * dx, LayoutMap.CellSize * dy));
                    if (next < 0 || next == own || !_open[next] || costs[next] >= bestCost) continue;
                    if (!Map.CanTravel(from, _routes.GetCell(next).Center, radius)) continue;
                    best = next; bestCost = costs[next];
                }
            if (best < 0)
            {
                // Ни одного видимого соседа: к ближайшей открытой клетке.
                int near = OpenCellNear(from);
                if (near < 0 || near == own) return false;
                heading = (_routes.GetCell(near).Center - from).Normalized();
                return heading.LengthSq.Raw != 0;
            }
            // Заглядывание вперёд по убыванию цены: срезаем углы там, где видно.
            int aim = best, walk = best;
            for (int step = 0; step < 2 && costs[walk] > 0; step++)
            {
                int down = Downhill(walk, costs);
                if (down < 0) break;
                walk = down;
                if (Map.CanTravel(from, _routes.GetCell(walk).Center, radius)) aim = walk;
            }
            heading = (_routes.GetCell(aim).Center - from).Normalized();
            return heading.LengthSq.Raw != 0;
        }

        private int Downhill(int cell, int[] costs)
        {
            int best = -1, bestCost = costs[cell];
            for (int d = 0; d < 8; d++)
            {
                int next = _next[cell * 8 + d];
                if (next >= 0 && costs[next] < bestCost) { bestCost = costs[next]; best = next; }
            }
            return best;
        }
    }
}
