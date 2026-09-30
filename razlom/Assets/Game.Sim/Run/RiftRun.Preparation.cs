namespace Game.Sim
{
    public sealed partial class RiftRun
    {
        public RunPreparation Preparation { get; private set; } = RunPreparation.Default;
        public int PotionCooldownTicksLeft { get; private set; }
        public bool GiftRerollUsed { get; private set; }
        public bool SpareFlaskUsed { get; private set; }
        public bool CanRerollReward => Preparation.Gift == CampGift.BackupPlan && !GiftRerollUsed
            && Phase == RunPhase.ChoosingReward && !ChoosingArtifact && BossId < 0;
        public void SetPreparation(in RunPreparation preparation)
        {
            if (Phase != RunPhase.Idle) throw new System.InvalidOperationException("Подготовка уже закреплена за походом.");
            if (preparation.StarterPoolIndex < 0 || preparation.Potion1 == preparation.Potion2
                || (uint)preparation.Potion1 >= Camp.PotionKindCount || (uint)preparation.Potion2 >= Camp.PotionKindCount)
                throw new System.ArgumentException("Некорректная подготовка похода.");
            Preparation = preparation;
        }
        public bool TryRerollReward()
        {
            if (!CanRerollReward) return false;
            GiftRerollUsed = true; RollOffers(); return true;
        }
        internal bool FirstPotionFree => Preparation.Gift == CampGift.SpareFlask && !SpareFlaskUsed;
        internal void PotionWasUsed()
        {
            if (FirstPotionFree) SpareFlaskUsed = true;
            PotionCooldownTicksLeft = 8 * Simulation.TicksPerSecond;
        }
        internal void AdvancePotionCooldown()
        { if (PotionCooldownTicksLeft > 0) PotionCooldownTicksLeft--; }
        void ResetPreparationUsage()
        { PotionCooldownTicksLeft = 0; GiftRerollUsed = SpareFlaskUsed = false; }
        void HashRunPreparation(ref ulong hash)
        {
            if (Preparation.StarterId != AbilityDefinition.WhirlwindId || Preparation.Gift != CampGift.None
                || Preparation.Potion1 != PotionKind.SmallHealth || Preparation.Potion2 != PotionKind.SmallLavidium)
            { Hashing.Mix(ref hash, 0x50524550); Preparation.HashInto(ref hash); }
            if (PotionCooldownTicksLeft != 0 || GiftRerollUsed || SpareFlaskUsed)
            {
                Hashing.Mix(ref hash, 0x50555345); Hashing.Mix(ref hash, PotionCooldownTicksLeft);
                Hashing.Mix(ref hash, GiftRerollUsed ? 1 : 0); Hashing.Mix(ref hash, SpareFlaskUsed ? 1 : 0);
            }
        }
    }
}
