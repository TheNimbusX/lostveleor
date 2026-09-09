using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>Shortest walking paths on the actual union of floor cells, including loop shortcuts.</summary>
    public sealed class LayoutRoutes
    {
        public readonly struct Cell
        {
            public readonly int X, Y, Module;
            public Cell(int x, int y, int module) { X = x; Y = y; Module = module; }
            public FixVec2 Center => new FixVec2(LayoutMap.CellSize * Fix64.Ratio(2 * X + 1, 2),
                LayoutMap.CellSize * Fix64.Ratio(2 * Y + 1, 2));
        }

        private readonly Cell[] _cells;
        private readonly Dictionary<long, int> _indices = new Dictionary<long, int>();
        private readonly int[] _distance, _parent, _targets;
        private readonly bool[] _road, _mainModules;
        private readonly int _start;
        public int CellCount => _cells.Length;
        public FixVec2 EntryPoint => _cells[_start].Center;
        public FixVec2 EntryFacing
        {
            get
            {
                for (int i = 0; i < _cells.Length; i++)
                    if (_road[i] && _parent[i] == _start) return (_cells[i].Center - EntryPoint).Normalized();
                return new FixVec2(Fix64.One, Fix64.Zero);
            }
        }
        public static readonly Fix64 ExitRadius = Fix64.Ratio(85, 100);
        public static readonly Fix64 SafeSpawnRadius = Fix64.FromInt(9);
        public int ExitDistanceCells { get; private set; }

        public Cell GetCell(int index) => _cells[index];
        public int ParentCell(int index) => _parent[index];
        public int DistanceFromEntry(int index) => _distance[index];
        public bool IsRoadCell(int index) => _road[index];
        public bool IsMainModule(int module) => _mainModules[module];
        public int DistanceToModule(int module) => _distance[_targets[module]];
        public FixVec2 Endpoint(int module) => _cells[_targets[module]].Center;

        public LayoutRoutes(LayoutMap map)
        {
            if (map.PlacedCount == 0) throw new ArgumentException("Routes need a placed entrance.");
            var cells = new List<Cell>();
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var p = map.GetPlaced(m);
                for (int y = p.OriginY; y < p.OriginY + p.Height; y++)
                    for (int x = p.OriginX; x < p.OriginX + p.Width; x++)
                    {
                        _indices.Add(Key(x, y), cells.Count);
                        cells.Add(new Cell(x, y, m));
                    }
            }
            _cells = cells.ToArray();
            _distance = new int[cells.Count];
            _parent = new int[cells.Count];
            _road = new bool[cells.Count];
            _targets = new int[map.PlacedCount];
            _mainModules = new bool[map.PlacedCount];
            for (int i = 0; i < cells.Count; i++) { _distance[i] = -1; _parent[i] = -1; }
            _start = ChooseStart(map);
            var queue = new int[cells.Count];
            int head = 0, tail = 0;
            queue[tail++] = _start;
            _distance[_start] = 0;
            while (head < tail)
            {
                int current = queue[head++];
                var c = _cells[current];
                // Stable neighbor order; hash table enumeration never affects paths.
                for (int d = 0; d < 4; d++)
                {
                    Directions.Step((Direction)d, out int dx, out int dy);
                    if (!_indices.TryGetValue(Key(c.X + dx, c.Y + dy), out int next) || _distance[next] >= 0) continue;
                    _distance[next] = _distance[current] + 1;
                    _parent[next] = current;
                    queue[tail++] = next;
                }
            }
            for (int m = 0; m < map.PlacedCount; m++)
            {
                int best = -1;
                for (int i = 0; i < _cells.Length; i++)
                    if (_cells[i].Module == m && _distance[i] >= 0 && IsBoundary(_cells[i]) &&
                        (best < 0 || _distance[i] > _distance[best])) best = i;
                // Enclosed modules still have a valid destination, but are not ideal exits.
                if (best < 0)
                    for (int i = 0; i < _cells.Length; i++)
                        if (_cells[i].Module == m && (best < 0 || _distance[i] > _distance[best])) best = i;
                _targets[m] = best;
            }
        }

        private int ChooseStart(LayoutMap map)
        {
            int best = 0, bestSeparation = -1;
            Fix64 centerDistance = Fix64.MaxValue;
            for (int i = 0; i < _cells.Length && _cells[i].Module == 0; i++)
            {
                var c = _cells[i];
                if (!IsBoundary(c)) continue;
                int separation = int.MaxValue;
                for (int m = 1; m < map.PlacedCount; m++)
                {
                    var p = map.GetPlaced(m);
                    int dx = Math.Max(0, Math.Max(p.OriginX - c.X, c.X - (p.OriginX + p.Width - 1)));
                    int dy = Math.Max(0, Math.Max(p.OriginY - c.Y, c.Y - (p.OriginY + p.Height - 1)));
                    separation = Math.Min(separation, dx + dy);
                }
                var distance = FixVec2.DistanceSq(c.Center, map.CenterOf(0));
                if (separation > bestSeparation || (separation == bestSeparation && distance < centerDistance))
                { best = i; bestSeparation = separation; centerDistance = distance; }
            }
            return best;
        }

        private bool IsBoundary(Cell c)
        {
            for (int d = 0; d < 4; d++)
            {
                Directions.Step((Direction)d, out int dx, out int dy);
                if (!_indices.ContainsKey(Key(c.X + dx, c.Y + dy))) return true;
            }
            return false;
        }

        public void MarkMainRoutes(LayoutMap map)
        {
            for (int e = 0; e < map.ExitCount; e++)
            {
                int target = _targets[map.GetExit(e)];
                if (e == 0) ExitDistanceCells = _distance[target];
                for (int i = target; i >= 0; i = _parent[i])
                { _road[i] = true; _mainModules[_cells[i].Module] = true; }
            }
        }

        public void MarkBranchRoutes(LayoutMap map)
        {
            for (int b = 0; b < map.RewardBranchCount; b++)
            {
                var point = map.CenterOf(map.GetRewardBranch(b));
                int index = CellAt(point);
                for (int i = index; i >= 0 && !_road[i]; i = _parent[i]) _road[i] = true;
            }
        }

        public int CellAt(FixVec2 point)
        {
            int x = FloorCell(point.X.Raw);
            int y = FloorCell(point.Y.Raw);
            return _indices.TryGetValue(Key(x, y), out int index) ? index : -1;
        }

        public bool NearRoad(FixVec2 point, Fix64 clearance)
        {
            var radiusSq = clearance * clearance;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_road[i]) continue;
                var a = _cells[i].Center;
                var b = _parent[i] >= 0 ? _cells[_parent[i]].Center : a;
                var closest = new FixVec2(Fix64.Clamp(point.X, Fix64.Min(a.X, b.X), Fix64.Max(a.X, b.X)),
                    Fix64.Clamp(point.Y, Fix64.Min(a.Y, b.Y), Fix64.Max(a.Y, b.Y)));
                if (FixVec2.DistanceSq(point, closest) < radiusSq) return true;
            }
            return false;
        }

        // Deterministic relocation without new spawn rolls. Omit enemies only
        // when the entire module lies in the safe starting area.
        public bool TrySafeSpawn(int module, FixVec2 candidate, out FixVec2 result)
        {
            var safeSq = SafeSpawnRadius * SafeSpawnRadius;
            result = candidate;
            if (FixVec2.DistanceSq(candidate, EntryPoint) >= safeSq) return true;
            Fix64 nearest = Fix64.MaxValue;
            bool found = false;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Module != module || FixVec2.DistanceSq(_cells[i].Center, EntryPoint) < safeSq) continue;
                var distance = FixVec2.DistanceSq(candidate, _cells[i].Center);
                if (distance >= nearest) continue;
                nearest = distance; result = _cells[i].Center; found = true;
            }
            return found;
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        private static int FloorCell(long raw)
        {
            long size = LayoutMap.CellSize.Raw;
            return (int)(raw / size - (raw < 0 && raw % size != 0 ? 1 : 0));
        }
    }
}
