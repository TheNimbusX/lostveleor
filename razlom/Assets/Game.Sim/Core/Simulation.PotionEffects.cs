namespace Game.Sim
{
    public sealed partial class Simulation
    {
        public const int PotionEffectTicks = 6 * TicksPerSecond;
        const int SurgeModifierId = 0x5054;
        int _resinUntilTick;
        int _surgeUntilTick;

        public int ResinTicksLeft => System.Math.Max(0, _resinUntilTick - Tick);
        public int SurgeTicksLeft => System.Math.Max(0, _surgeUntilTick - Tick);

        public void ApplyResinPotion() => _resinUntilTick = Tick + PotionEffectTicks;
        public void ApplySurgePotion()
        {
            _surgeUntilTick = Tick + PotionEffectTicks;
            var sheet = Entities.Stats[PlayerId];
            sheet.RemoveSource(ModifierSource.Buff, SurgeModifierId);
            sheet.Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(20, 100), ModifierSource.Buff, SurgeModifierId));
            sheet.Add(StatModifier.Flat(StatType.AbilitySpeed, Fix64.Ratio(20, 100), ModifierSource.Buff, SurgeModifierId));
        }
        private void UpdatePotionEffects()
        {
            if (_surgeUntilTick > 0 && Tick >= _surgeUntilTick)
            {
                _surgeUntilTick = 0;
                Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, SurgeModifierId);
            }
            if (_resinUntilTick > 0 && Tick >= _resinUntilTick) _resinUntilTick = 0;
        }
        private int ApplyResinReduction(int target, int damage)
            => target == PlayerId && ResinTicksLeft > 0
                ? CombatStats.RoundToInt(Fix64.FromInt(damage) * Fix64.Ratio(75, 100)) : damage;
        private void ResetPotionEffects()
        {
            _resinUntilTick = _surgeUntilTick = 0;
            if (Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, SurgeModifierId);
        }
        private void HashPotionEffects(ref ulong hash)
        {
            Hashing.Mix(ref hash, _resinUntilTick);
            Hashing.Mix(ref hash, _surgeUntilTick);
        }
    }
}
