namespace Game.Sim
{
    /// <summary>Неизменные сроки и направление конкретного сабельного взмаха.</summary>
    public struct PelagBasicAttackState
    {
        public int Serial, Stage, StartTick, ContactTick, EndTick;
        public FixVec2 Direction;
        public bool ContactProcessed, Interrupted;
        public bool ActiveAt(int tick) => Serial > 0 && !Interrupted && tick < EndTick;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Stage);
            Hashing.Mix(ref hash, StartTick); Hashing.Mix(ref hash, ContactTick);
            Hashing.Mix(ref hash, EndTick);
            Hashing.Mix(ref hash, Direction.X); Hashing.Mix(ref hash, Direction.Y);
            Hashing.Mix(ref hash, ContactProcessed ? 1 : 0);
            Hashing.Mix(ref hash, Interrupted ? 1 : 0);
        }
    }
}
