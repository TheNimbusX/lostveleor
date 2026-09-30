namespace Game.Sim
{
    public sealed partial class Simulation
    {
        const int PreparedGiftModifierId = 0x47494654;
        CampGift _preparedRunGift;
        int _giftRollEndTick = -1, _giftRollAttackUntil = -1;
        bool _giftOrdinaryAttackBoost;
        public CampGift PreparedRunGift => _preparedRunGift;

        public void ApplyPreparedGift(CampGift gift, int arenaDepth)
        {
            _preparedRunGift = gift; ResetPreparedGiftTiming();
            var sheet = Entities.Stats[PlayerId]; sheet.RemoveSource(ModifierSource.Buff, PreparedGiftModifierId);
            if (gift == CampGift.DryRation)
            {
                sheet.Add(StatModifier.Increased(StatType.MaxHealth, Fix64.Ratio(12, 100), ModifierSource.Buff, PreparedGiftModifierId));
                sheet.Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(-6, 100), ModifierSource.Buff, PreparedGiftModifierId));
            }
            else if (gift == CampGift.LightPack)
            {
                sheet.Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(10, 100), ModifierSource.Buff, PreparedGiftModifierId));
                sheet.Add(StatModifier.Increased(StatType.Damage, Fix64.Ratio(-8, 100), ModifierSource.Buff, PreparedGiftModifierId));
            }
            else if (gift == CampGift.EniWhetstone && arenaDepth <= 3)
                sheet.Add(StatModifier.Increased(StatType.Damage, Fix64.Ratio(12, 100), ModifierSource.Buff, PreparedGiftModifierId));
        }
        void ResetPreparedGiftTiming()
        { _giftRollEndTick = _giftRollAttackUntil = -1; _giftOrdinaryAttackBoost = false; }
        void PreparedGiftRollStarted(int endTick)
        {
            if (_preparedRunGift != CampGift.SeaKnot) return;
            _giftRollEndTick = endTick; _giftRollAttackUntil = endTick + 2 * TicksPerSecond;
        }
        void PreparedGiftActionCancelled()
        {
            if (_giftRollEndTick > Tick) _giftRollEndTick = _giftRollAttackUntil = -1;
        }
        void PreparedGiftOrdinaryAttackStarted()
        {
            // Бонус закреплён за начатым взмахом, включая все цели тяжёлого удара.
            // Промах тоже тратит возможность: дар не ждёт первого удобного попадания.
            _giftOrdinaryAttackBoost = _preparedRunGift == CampGift.SeaKnot
                && _giftRollEndTick >= 0 && Tick >= _giftRollEndTick && Tick <= _giftRollAttackUntil;
            if (_giftOrdinaryAttackBoost) _giftRollEndTick = _giftRollAttackUntil = -1;
        }
        int PreparedGiftAttackDamage(int source, int damage)
            => source == PlayerId && _giftOrdinaryAttackBoost
                ? CombatStats.RoundToInt(Fix64.FromInt(damage) * Fix64.Ratio(125, 100)) : damage;
        void HashPreparedGift(ref ulong hash)
        {
            if (_preparedRunGift == CampGift.None) return;
            Hashing.Mix(ref hash, 0x47494654); Hashing.Mix(ref hash, (int)_preparedRunGift);
            Hashing.Mix(ref hash, _giftRollEndTick); Hashing.Mix(ref hash, _giftRollAttackUntil);
            Hashing.Mix(ref hash, _giftOrdinaryAttackBoost ? 1 : 0);
        }
    }
}
