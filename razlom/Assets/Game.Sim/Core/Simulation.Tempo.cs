namespace Game.Sim
{
    public sealed partial class Simulation
    {
        public const int AbilityBufferTicks = 6;
        private PlayerActionState _playerAction;
        private InputFrame _bufferedAbility;
        private int _bufferedUntil = -1;
        public PlayerActionState PlayerAction => _playerAction;

        public int AbilityExecutionTicks(int ticks, int minimum = 2)
            => TempoTicks(ticks, StatType.AbilitySpeed, minimum);
        public int AbilityCooldownTicks(AbilityBuild build)
            => build == null ? 0 : TempoTicks(build.CooldownTicks, StatType.CooldownRecovery, 6);
        private int TempoTicks(int ticks, StatType stat, int minimum)
        {
            Fix64 bonus = Fix64.Max(Fix64.Zero, Fix64.Min(Fix64.One, Entities.Stats[PlayerId].Get(stat)));
            return System.Math.Max(minimum, (Fix64.FromInt(ticks) / (Fix64.One + bonus)).ToInt());
        }
        public int PlayerAttackWindupTicks => System.Math.Max(2,
            (Fix64.FromInt(AttackWindupTicks) * PlayerBaseAttackSpeed
            / Fix64.Max(Fix64.Ratio(1, 100), Entities.Stats[PlayerId].Get(StatType.AttackSpeed))).ToInt());

        private static bool IsEvade(int id) => id == AbilityDefinition.DashId
            || id == AbilityDefinition.SkewerId || id == AbilityDefinition.BackblastId;

        private bool CastAvailable(int slot, in InputFrame input)
        {
            var build = _abilityBuilds[slot];
            if (build == null) return false;
            if (build.DefinitionId == AbilityDefinition.WreckId && _wreckSlot == slot && WreckComboOpen) return true;
            return Tick >= _abilityReadyTick[slot] && CanAffordAbility(build)
                && (build.DefinitionId != AbilityDefinition.ChainStepId || ValidAbilityTarget(input.AbilityTarget, build));
        }

        private InputFrame PrepareCombatInput(in InputFrame raw)
        {
            var result = raw;
            result.AbilityMask = 0;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))
            {
                CancelPlayerAction();
                _bufferedUntil = -1;
                result.Flags = 0;
                result.AbilityHoldMask = 0;
                return result;
            }
            // Доступный уход имеет приоритет даже при двух нажатиях в одном тике.
            for (int slot = AbilitySlots - 1; slot >= 0; slot--)
                if (raw.Ability(slot) && _abilityBuilds[slot] != null
                    && IsEvade(_abilityBuilds[slot].DefinitionId) && CastAvailable(slot, raw))
                {
                    result.AbilityMask = (byte)(1 << slot);
                    _bufferedUntil = -1;
                    CancelPlayerAction();
                    return result;
                }
            for (int slot = 0; slot < AbilitySlots; slot++)
            {
                if (!raw.Ability(slot) || _abilityBuilds[slot] == null || IsEvade(_abilityBuilds[slot].DefinitionId)) continue;
                // Последнее нажатие заменяет предыдущее даже при недоступном навыке.
                _bufferedAbility = raw;
                _bufferedAbility.AbilityMask = (byte)(1 << slot);
                _bufferedUntil = Tick + AbilityBufferTicks;
            }
            if (_bufferedUntil < Tick || !_playerAction.CanChainAt(Tick)) return result;
            for (int slot = 0; slot < AbilitySlots; slot++)
                if (_bufferedAbility.Ability(slot) && CastAvailable(slot, _bufferedAbility))
                {
                    result.AbilityMask = (byte)(1 << slot);
                    result.Aim = _bufferedAbility.Aim;
                    result.AbilityTarget = _bufferedAbility.AbilityTarget;
                    _bufferedUntil = -1;
                    break;
                }
            return result;
        }

        private void SetActionClock(int slot, int id, int contact, int end)
        {
            _playerAction = new PlayerActionState { Serial = _playerAction.Serial + 1,
                Slot = slot, DefinitionId = id, StartTick = Tick, ContactTick = contact,
                EndTick = System.Math.Max(contact + 1, end) };
            _abilityMovePenaltyUntilTick = slot >= 0 ? contact : 0;
        }
        private void CaptureAbilityClock(int slot)
        {
            var b = _abilityBuilds[slot]; int id = b.DefinitionId;
            int contact = Tick + AbilityExecutionTicks(2), end = contact + AbilityExecutionTicks(6, 1);
            if (id == AbilityDefinition.AnchorSlamId) { contact = _slamImpactTick; end = _slamEndTick; }
            else if (id == AbilityDefinition.CleaveId) { contact = _cleaveImpactTick; end = _cleaveEndTick; }
            else if (id == AbilityDefinition.BlazeId) { contact = _blazeIgniteTick; end = _blazeEndTick; }
            else if (id == AbilityDefinition.WhirlwindId) { contact = _whirlwindImpactTick; end = contact + AbilityExecutionTicks(12, 1); }
            else if (id == AbilityDefinition.WreckId) { contact = _wreckImpactTick; end = Tick + AbilityExecutionTicks(b.Get(AbilityStatType.DurationTicks).ToInt()); }
            else if (id == AbilityDefinition.AnchorLeapId) { contact = _leapLaunchTick + AnchorKit.LeapTicks; end = contact + AbilityExecutionTicks(6, 1); }
            else if (id == AbilityDefinition.ChainStepId) { contact = Tick + AnchorKit.ChainTicksPerHop; end = Tick + AnchorKit.ChainTicksPerHop * _chainHopsLeft; }
            else if (id == AbilityDefinition.FireFlaskId) { contact = _flaskLandTick; end = contact + AbilityExecutionTicks(6, 1); }
            else if (id == AbilityDefinition.DashId) { contact = Tick + Entities.ForcedTicksLeft[PlayerId]; end = contact; }
            else if (id == AbilityDefinition.SkewerId) { contact = _mobilityEndTick; end = contact; }
            else if (id == AbilityDefinition.BackblastId) { contact = _backblastTick; end = _mobilityEndTick; }
            SetActionClock(slot, id, contact, end);
        }
        private void CancelPlayerAction()
        {
            StopAnchorSlam(); StopWreck(); StopCleave(); StopFlask();
            CancelBlazeGesture(); StopWhirlwindChannel();
            _leapLaunchTick = _leapPunchTick = -1;
            _whirlwindImpactTick = _whirlwindImpactSlot = -1;
            _chainHopsLeft = _chainVisitedCount = 0;
            _mobilitySlot = _backblastTick = -1;
            ForcedMotion.Clear(Entities, PlayerId);
            Entities.PendingAttackTarget[PlayerId] = -1;
            Entities.AttackImpactTick[PlayerId] = 0;
            Entities.PendingAttackVariant[PlayerId] = 0;
            _abilityMovePenaltyUntilTick = 0;
            _playerAction.Interrupted = true;
            _playerAction.EndTick = Tick;
        }
        private void ResetTempo()
        {
            _playerAction = default;
            _bufferedAbility = InputFrame.Empty; _bufferedUntil = -1;
            _mobilitySlot = _backblastTick = -1;
            _mobilityEndTick = 0;
            _mobilityOrigin = _mobilityPrevious = FixVec2.Zero;
            if (_mobilityHits != null) System.Array.Clear(_mobilityHits, 0, _mobilityHits.Length);
        }
        private void HashTempo(ref ulong hash)
        {
            _playerAction.HashInto(ref hash);
            _bufferedAbility.HashInto(ref hash); Hashing.Mix(ref hash, _bufferedUntil);
            Hashing.Mix(ref hash, _mobilitySlot); Hashing.Mix(ref hash, _mobilityEndTick);
            Hashing.Mix(ref hash, _backblastTick);
            Hashing.Mix(ref hash, _mobilityOrigin.X); Hashing.Mix(ref hash, _mobilityOrigin.Y);
            Hashing.Mix(ref hash, _mobilityPrevious.X); Hashing.Mix(ref hash, _mobilityPrevious.Y);
            if (_mobilityHits != null) for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _mobilityHits[i] ? 1 : 0);
        }
    }
}
