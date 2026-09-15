using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public sealed class NaturalOutline
    {
        public static readonly Fix64 Step = Fix64.Ratio(1, 2);
        private readonly HashSet<long> _cells = new HashSet<long>();
        private readonly long[] _ordered;
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
        }
        public bool ContainsCell(int x, int y) => _cells.Contains(Key(x, y));
        public bool Contains(FixVec2 p) => ContainsCell(Floor(p.X), Floor(p.Y));
        public void MixHash(ref ulong hash)
        {
            Hashing.Mix(ref hash, _ordered.Length);
            foreach (long key in _ordered) { Hashing.Mix(ref hash, (int)(key >> 32)); Hashing.Mix(ref hash, (int)key); }
        }
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        private static int Floor(Fix64 value) => (int)(value.Raw / Step.Raw - (value.Raw < 0 && value.Raw % Step.Raw != 0 ? 1 : 0));
    }
}
