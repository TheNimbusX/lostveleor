namespace Game.Sim
{
    public sealed partial class Simulation
    {
        private int _mobilitySlot = -1, _mobilityEndTick, _backblastTick = -1;
        private FixVec2 _mobilityOrigin, _mobilityPrevious;
        private bool[] _mobilityHits;
        public FixVec2 MobilityOrigin => _mobilityOrigin;

        private void BeginMobility(int slot, FixVec2 aim)
        {
            var build = _abilityBuilds[slot];
            bool back = build.DefinitionId == AbilityDefinition.BackblastId;
            _mobilityOrigin = _mobilityPrevious = Entities.Position[PlayerId];
            FixVec2 direction = (aim - _mobilityOrigin).Normalized();
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            Entities.Facing[PlayerId] = direction;
            _mobilitySlot = slot;
            int ticks = build.Get(AbilityStatType.DurationTicks).ToInt();
            _mobilityEndTick = Tick + ticks;
            _backblastTick = back ? Tick + 2 : -1;
            System.Array.Clear(_mobilityHits, 0, _mobilityHits.Length);
            FixVec2 travel = direction * build.Get(AbilityStatType.Radius);
            ForcedMotion.Begin(Entities, PlayerId, back ? _mobilityOrigin - travel : _mobilityOrigin + travel,
                ticks, back ? ForcedMotionKind.Backblast : ForcedMotionKind.Skewer);
        }

        private void UpdateMobility()
        {
            if (_mobilitySlot < 0) return;
            var b = _abilityBuilds[_mobilitySlot];
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick) || b == null)
            { _mobilitySlot = _backblastTick = -1; return; }
            FixVec2 at = Entities.Position[PlayerId];
            bool skewer = b.DefinitionId == AbilityDefinition.SkewerId;
            bool burst = !skewer && _backblastTick >= 0 && Tick >= _backblastTick;
            if (burst)
            {
                _backblastTick = -1;
                _events.Add(new SimEvent(SimEventType.BackblastBurst, PlayerId, -1, _mobilitySlot, false, _mobilityOrigin, DamageType.Fire));
            }
            if (skewer || burst)
                for (int i = 1; i < Entities.Count; i++)
                {
                    if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId] || _mobilityHits[i]) continue;
                    Fix64 reach = Entities.BodyRadius[i] + (skewer ? b.Get(AbilityStatType.Width) / 2 : b.Get(AbilityStatType.Width));
                    Fix64 distance = skewer ? CleavePointSegmentSq(Entities.Position[i], _mobilityPrevious, at)
                        : (Entities.Position[i] - _mobilityOrigin).LengthSq;
                    if (distance > reach * reach) continue;
                    _mobilityHits[i] = true;
                    ApplyAbilityDamage(PlayerId, i, b.Get(AbilityStatType.Damage).ToInt(), _mobilitySlot,
                        skewer ? DamageType.Physical : DamageType.Fire);
                }
            _mobilityPrevious = at;
            if (Tick >= _mobilityEndTick) _mobilitySlot = -1;
        }
    }
}
