namespace Game.Sim
{
    public sealed partial class RiftRun
    {
        public RunPreparation Preparation { get; private set; } = RunPreparation.Default;
        public int PotionCooldownTicksLeft { get; private set; }
        public bool GiftRerollUsed { get; private set; }
        public bool SpareFlaskUsed { get; private set; }
        public bool CanRerollReward => Preparation.Gift == CampGift.BackupPlan && !GiftRerollUsed
            && Phase == RunPhase.ChoosingReward && !ChoosingArtifact && !ChoosingForm && BossId < 0;
        public void SetPreparation(in RunPreparation preparation)
        {
            if (Phase != RunPhase.Idle) throw new System.InvalidOperationException("Подготовка уже закреплена за походом.");
            if (preparation.StarterPoolIndex < 0 || preparation.Potion1 == preparation.Potion2
                || (uint)preparation.Potion1 >= Camp.PotionKindCount || (uint)preparation.Potion2 >= Camp.PotionKindCount)
                throw new System.ArgumentException("Некорректная подготовка похода.");
            // Ячейка «с собой» одна: дар и артефакт вместе — ошибка вызывающего, а не выбор игрока.
            if (preparation.Carried != RunArtifact.None
                && (!RunArtifacts.IsValid(preparation.Carried) || preparation.Gift != CampGift.None))
                throw new System.ArgumentException("Некорректная ячейка «с собой».");
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
        /// <summary>
        /// Артефакт из ячейки «с собой» встаёт в слот артефакта на этот забег (StartRun, сразу
        /// после сброса слота). В список взятого он не пишется: это не награда забега, итоги
        /// не должны показывать его находкой, а в атласе он уже открыт.
        /// </summary>
        void ApplyCarriedArtifact()
        {
            if (RunArtifacts.IsValid(Preparation.Carried)) TakeArtifact(Preparation.Carried);
        }
        void HashRunPreparation(ref ulong hash)
        {
            // Carried в условии: забег с артефактом «с собой» и без него не должны совпасть по хешу
            // подготовки; без артефакта условие прежнее и хеш тот же бит в бит.
            if (Preparation.StarterId != AbilityDefinition.WhirlwindId || Preparation.Gift != CampGift.None
                || Preparation.Potion1 != PotionKind.SmallHealth || Preparation.Potion2 != PotionKind.SmallLavidium
                || Preparation.Carried != RunArtifact.None)
            { Hashing.Mix(ref hash, 0x50524550); Preparation.HashInto(ref hash); }
            if (PotionCooldownTicksLeft != 0 || GiftRerollUsed || SpareFlaskUsed)
            {
                Hashing.Mix(ref hash, 0x50555345); Hashing.Mix(ref hash, PotionCooldownTicksLeft);
                Hashing.Mix(ref hash, GiftRerollUsed ? 1 : 0); Hashing.Mix(ref hash, SpareFlaskUsed ? 1 : 0);
            }
        }
    }
}
