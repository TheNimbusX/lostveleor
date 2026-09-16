namespace Game.Sim
{
    /// <summary>Русло и плоский мост используют одну геометрию для пола и представления.</summary>
    public readonly struct LayoutRiver
    {
        public readonly FixVec2 Center, Across;
        public readonly Fix64 HalfLength, HalfWidth, Bend;
        public static readonly Fix64 BridgeHalfWidth = Fix64.FromInt(2);
        public FixVec2 Along => new FixVec2(-Across.Y, Across.X);
        public LayoutRiver(FixVec2 center, FixVec2 across, Fix64 bend)
        {
            Center = center; Across = across; Bend = bend;
            HalfLength = Fix64.FromInt(42); HalfWidth = Fix64.Ratio(23, 10);
        }
        public FixVec2 Point(Fix64 t) => Center + Across * t
            + Along * (Fix64.Sin(t / Fix64.FromInt(9)) * Bend);
        public bool ContainsWater(FixVec2 point, Fix64 margin)
        {
            var delta = point - Center;
            var t = FixVec2.Dot(delta, Across);
            return Fix64.Abs(t) <= HalfLength + margin
                && Fix64.Abs(FixVec2.Dot(point - Point(t), Along)) <= HalfWidth + margin;
        }
        public bool ContainsBridge(FixVec2 point)
        {
            var delta = point - Center;
            return Fix64.Abs(FixVec2.Dot(delta, Across)) <= BridgeHalfWidth
                && Fix64.Abs(FixVec2.Dot(delta, Along)) <= HalfWidth + Fix64.FromInt(2);
        }
        public bool Blocks(FixVec2 point) => ContainsWater(point, Fix64.Zero) && !ContainsBridge(point);
        public void MixHash(ref ulong hash)
        {
            Hashing.Mix(ref hash, Center.X); Hashing.Mix(ref hash, Center.Y);
            Hashing.Mix(ref hash, Across.X); Hashing.Mix(ref hash, Across.Y);
            Hashing.Mix(ref hash, Bend);
        }
    }
}
