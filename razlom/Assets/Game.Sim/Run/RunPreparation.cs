namespace Game.Sim
{
    // Номера даров сохраняются: новые добавляются только в конец.
    public enum CampGift : byte
    {
        None = 0, DryRation = 1, EniWhetstone = 2, LightPack = 3,
        SeaKnot = 4, BackupPlan = 5, SpareFlask = 6
    }

    /// <summary>Снимок решений у стола. После входа в поход он не меняется.</summary>
    public readonly struct RunPreparation
    {
        public readonly int StarterId;
        public readonly CampGift Gift;
        public readonly PotionKind Potion1, Potion2;
        public RunPreparation(int starterId, CampGift gift, PotionKind potion1, PotionKind potion2)
        { StarterId = starterId; Gift = gift; Potion1 = potion1; Potion2 = potion2; }
        public int StarterPoolIndex => PelagKit.PoolIndexOf(StarterId);
        public PotionKind PotionAt(int slot) => slot == 0 ? Potion1 : Potion2;
        public static RunPreparation Default => new RunPreparation(AbilityDefinition.WhirlwindId,
            CampGift.None, PotionKind.SmallHealth, PotionKind.SmallLavidium);
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, StarterId); Hashing.Mix(ref hash, (int)Gift);
            Hashing.Mix(ref hash, (int)Potion1); Hashing.Mix(ref hash, (int)Potion2);
        }
    }
}
