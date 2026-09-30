namespace Game.Sim
{
    public sealed partial class Simulation
    {
        public const int PotionEffectTicks = 6 * TicksPerSecond;
        const int SurgeModifierId = 0x5054;
        int _resinUntilTick;
        int _surgeUntilTick;
        int _clearUntilTick;

        public int ResinTicksLeft => System.Math.Max(0, _resinUntilTick - Tick);
        public int SurgeTicksLeft => System.Math.Max(0, _surgeUntilTick - Tick);
        public int ClearTicksLeft => System.Math.Max(0, _clearUntilTick - Tick);
        public void ApplyClearPotion()
        {
            bool wasRooted = HeroRooted;
            _clearUntilTick = Tick + 2 * TicksPerSecond;
            _heroSlowUntil = _heroRootUntil = _heroSlowPercent = 0;
            if (wasRooted && !HeroStunned) _heroControlImmuneUntil = 0;
            var sheet = Entities.Stats[PlayerId];
            sheet.RemoveSource(ModifierSource.Buff, HeroSlowId);
            sheet.RemoveSource(ModifierSource.Buff, HeroRootId);
        }

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
            if (_clearUntilTick > 0 && Tick >= _clearUntilTick) _clearUntilTick = 0;
        }
        private int ApplyResinReduction(int target, int damage)
            => target == PlayerId && ResinTicksLeft > 0
                ? CombatStats.RoundToInt(Fix64.FromInt(damage) * Fix64.Ratio(75, 100)) : damage;
        private void ResetPotionEffects()
        {
            _resinUntilTick = _surgeUntilTick = _clearUntilTick = 0;
            if (Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, SurgeModifierId);
        }
        private void HashPotionEffects(ref ulong hash)
        {
            Hashing.Mix(ref hash, _resinUntilTick);
            Hashing.Mix(ref hash, _surgeUntilTick);
            if (_clearUntilTick != 0) { Hashing.Mix(ref hash, 0x434C4541); Hashing.Mix(ref hash, _clearUntilTick); }
        }
    }
}
