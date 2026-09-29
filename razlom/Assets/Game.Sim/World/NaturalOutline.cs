using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public sealed class NaturalOutline
    {
        public static readonly Fix64 Step = Fix64.Ratio(1, 2);
        private readonly HashSet<long> _cells = new HashSet<long>();
        private readonly long[] _ordered;

        // Те же клетки плотным массивом по охватывающему прямоугольнику.
        // Contains зовётся девять раз на каждую проверку IsWalkable, а
        // CanTravel проверяет точку через каждые полметра пути: поиск в
        // HashSet под Mono был самой дорогой частью шага толпы (поток D,
        // 29.09: на 48 мобах — больше трети тика). Состав клеток не меняется.
        private readonly int _minX, _minY, _width, _height;
        private readonly bool[] _dense;

        /// <summary>Шаг 0,5 м — степень двойки в Raw: округление вниз при делении — это сдвиг.</summary>
        private static readonly int StepShift = PowerOfTwo(Step.Raw);
        public NaturalOutline(LayoutMap map, Func<FixVec2, bool> inside)
        {
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var p = map.GetPlaced(m);
                for (int y = p.OriginY * 4; y < (p.OriginY + p.Height) * 4; y++)
                    for (int x = p.OriginX * 4; x < (p.OriginX + p.Width) * 4; x++)
                        if (inside(new FixVec2(Step * x + Step / 2, Step * y + Step / 2))) _cells.Add(Key(x, y));
            }
            _ordered = new long[_cells.Count]; _cells.CopyTo(_ordered); Array.Sort(_ordered);
            if (_ordered.Length == 0) { _dense = Array.Empty<bool>(); return; }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (long key in _ordered)
            {
                int x = (int)(key >> 32), y = (int)key;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            _minX = minX; _minY = minY; _width = maxX - minX + 1; _height = maxY - minY + 1;
            _dense = new bool[_width * _height];
            foreach (long key in _ordered) _dense[((int)key - minY) * _width + (int)(key >> 32) - minX] = true;
        }
        public bool ContainsCell(int x, int y)
        {
            int dx = x - _minX, dy = y - _minY;
            return (uint)dx < (uint)_width && (uint)dy < (uint)_height && _dense[dy * _width + dx];
        }
        public bool Contains(FixVec2 p) => ContainsCell(Floor(p.X), Floor(p.Y));

        /// <summary>
        /// Опорные точки тела из LayoutMap.IsWalkable — центр, четыре на radius
        /// по осям и четыре на diagonal по диагоналям — все ли на полу. Те же
        /// точки и тот же ответ, что девять Contains, но сложением Raw без
        /// сборки векторов: это самая частая проверка шага толпы.
        /// </summary>
        internal bool ContainsBody(FixVec2 center, Fix64 radius, Fix64 diagonal)
        {
            long x = center.X.Raw, y = center.Y.Raw, r = radius.Raw, d = diagonal.Raw;
            return ContainsRaw(x, y) && ContainsRaw(x + r, y) && ContainsRaw(x - r, y)
                && ContainsRaw(x, y + r) && ContainsRaw(x, y - r)
                && ContainsRaw(x + d, y + d) && ContainsRaw(x - d, y + d)
                && ContainsRaw(x + d, y - d) && ContainsRaw(x - d, y - d);
        }

        /// <summary>
        /// Contains по сырым координатам. Шаг — степень двойки (всегда: 0,5 м),
        /// и клетка — просто сдвиг; одним методом, без цепочки вызовов:
        /// в редакторе (отладочная сборка) каждый вызов стоит времени.
        /// </summary>
        private bool ContainsRaw(long x, long y)
        {
            if (StepShift < 0) return ContainsCell(Floor(Fix64.FromRaw(x)), Floor(Fix64.FromRaw(y)));
            int dx = (int)(x >> StepShift) - _minX, dy = (int)(y >> StepShift) - _minY;
            return (uint)dx < (uint)_width && (uint)dy < (uint)_height && _dense[dy * _width + dx];
        }
        public void MixHash(ref ulong hash)
        {
            Hashing.Mix(ref hash, _ordered.Length);
            foreach (long key in _ordered) { Hashing.Mix(ref hash, (int)(key >> 32)); Hashing.Mix(ref hash, (int)key); }
        }
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        private static int Floor(Fix64 value) => StepShift >= 0 ? (int)(value.Raw >> StepShift)
            : (int)(value.Raw / Step.Raw - (value.Raw < 0 && value.Raw % Step.Raw != 0 ? 1 : 0));

        private static int PowerOfTwo(long raw)
        {
            for (int shift = 0; shift < 62; shift++)
                if (1L << shift == raw) return shift;
            return -1;
        }
    }
}
