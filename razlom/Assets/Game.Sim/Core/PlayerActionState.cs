namespace Game.Sim
{
    /// <summary>Единые часы применения; представление не восстанавливает сроки из констант.</summary>
    public struct PlayerActionState
    {
        public int Serial, DefinitionId, Slot, StartTick, ContactTick, EndTick;
        public bool Interrupted;
        public bool ActiveAt(int tick) => Serial > 0 && !Interrupted && tick < EndTick;
        public bool CanChainAt(int tick) => !ActiveAt(tick) || tick > ContactTick;
        public bool CanEvadeAt(int tick) => ActiveAt(tick);
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, DefinitionId);
            Hashing.Mix(ref hash, Slot); Hashing.Mix(ref hash, StartTick);
            Hashing.Mix(ref hash, ContactTick); Hashing.Mix(ref hash, EndTick);
            Hashing.Mix(ref hash, Interrupted ? 1 : 0);
        }
    }
}
