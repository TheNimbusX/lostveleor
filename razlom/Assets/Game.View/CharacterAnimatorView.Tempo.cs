using UnityEngine;
using Game.Sim;

namespace Game.View
{
    public sealed partial class CharacterAnimatorView
    {
        private int _tempoSerial = -1;
        private float _tempoContactPhase;
        private bool _tempoAbility;
        private int _tempoReleaseTick;
        private int _recoveryFootworkLayer = -1;

        private void UpdateRecoveryFootwork()
        {
            if (_animator == null || _recoveryFootworkLayer < 0) return;
            var sim = TempoSim;
            bool supported = _abilityDefinitionId == AbilityDefinition.AnchorSlamId || _abilityDefinitionId == AbilityDefinition.WreckId
                || _abilityDefinitionId == AbilityDefinition.FireFlaskId || _abilityDefinitionId == AbilityDefinition.AnchorLeapId;
            bool recovering = !IsDead && _abilityPresentationActive && supported && sim != null
                && !sim.PlayerAction.Interrupted && sim.PlayerAction.DefinitionId == _abilityDefinitionId;
            if (!recovering) { _animator.SetLayerWeight(_recoveryFootworkLayer,0); return; }
            // После контакта ноги следуют разрешённому Sim бегу, верх завершает удар и выбирает цепь.
            float tick = sim.Tick-1+_cycloneDriver.Alpha;
            int settle = _abilityDefinitionId == AbilityDefinition.AnchorLeapId ? 3 : 1;
            float target = _locomotionMoving && tick >= sim.PlayerAction.ContactTick + settle ? 1 : 0;
            _animator.SetLayerWeight(_recoveryFootworkLayer,Mathf.MoveTowards(_animator.GetLayerWeight(_recoveryFootworkLayer),target,Time.deltaTime/.08f));
        }

        private Simulation TempoSim
        {
            get
            {
                if (_cycloneDriver == null) _cycloneDriver = FindAnyObjectByType<TickDriver>();
                return _cycloneDriver != null ? _cycloneDriver.Sim : null;
            }
        }
        private float CurrentAttackSpeed => TempoSim == null ? 1f :
            Simulation.AttackWindupTicks / (float)Mathf.Max(2, TempoSim.PlayerAttackWindupTicks);

        public float AuthoredLeapTime => TempoSim != null && TempoSim.PlayerAction.DefinitionId == AbilityDefinition.AnchorLeapId
            ? PelagAbilityTiming.SampleLeap(TempoSim.PlayerAction, TempoSim.Tick - 1 + _cycloneDriver.Alpha) : -1f;

        private bool TryPlayTempoAbility(int id)
        {
            bool wreck = id == AbilityDefinition.WreckId;
            if (id != AbilityDefinition.SkewerId && id != AbilityDefinition.BackblastId
                && id != AbilityDefinition.FireFlaskId && !wreck)
            { _tempoAbility = false; return false; }
            var sim = TempoSim;
            if (_animator == null || IsDead || sim == null) return true;
            GetComponent<PelagAnchorSlamView>()?.Release();
            StopAttackWarp(); CancelUpperBodyAttack(.02f); ResetAbilityTriggers();
            _leapLocomotion = false; _attackPresentationActive = false;
            _abilityDefinitionId = id; _abilityPresentationActive = true;
            _abilityUsesLowerBodyLayer = false;
            _tempoAbility = true; _tempoSerial = sim.PlayerAction.Serial;
            _tempoReleaseTick = sim.FlaskReleaseTick;
            string name = id == AbilityDefinition.SkewerId ? "Skewer"
                : id == AbilityDefinition.BackblastId ? "Backblast"
                : wreck ? (sim.WreckStage == 0 ? "WreckA" : sim.WreckStage == 1 ? "WreckB" : "WreckFinish")
                : "FireFlask";
            _tempoContactPhase = wreck ? (sim.WreckStage >= 2 ? 6f / 18f : 6f / 15f)
                : id == AbilityDefinition.BackblastId ? .25f : 1f;
            SetCombatReady(true);
            if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
            if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);
            _animator.SetFloat("TempoPhase", 0f);
            EnterCommittedAbilityState(Animator.StringToHash("Base Layer." + name + "_v5"), .025f);
            if (wreck) GetComponent<PelagAnchorSlamView>()?.BeginWreck();
            UpdateTempoAnimation();
            return true;
        }

        private void UpdateTempoAnimation()
        {
            if (_faction != Faction.Wole || _animator == null) return;
            var sim = TempoSim; if (sim == null) return;
            var action = sim.PlayerAction;
            float tick = sim.Tick - 1 + _cycloneDriver.Alpha;
            if (_abilityPresentationActive && !action.Interrupted && action.DefinitionId == _abilityDefinitionId)
            {
                if (action.DefinitionId == AbilityDefinition.AnchorLeapId)
                    _animator.SetFloat("LeapPhase", PelagAbilityTiming.SampleLeap(action, tick) / PelagAbilityTiming.LeapRecovery);
                if (action.DefinitionId == AbilityDefinition.WhirlwindId)
                {
                    float contact = WhirlwindContactTime / WhirlwindClipDuration;
                    float whirl = tick <= action.ContactTick ? Mathf.Lerp(0, contact, Mathf.InverseLerp(action.StartTick, action.ContactTick, tick))
                        : Mathf.Lerp(contact, 1, Mathf.InverseLerp(action.ContactTick, action.EndTick, tick));
                    _animator.SetFloat("WhirlwindPhase", whirl);
                }
            }
            if (_abilityPresentationActive && action.DefinitionId == _abilityDefinitionId && !action.Interrupted)
            {
                // Эти часы допускают новый каст сразу после контакта, даже при незавершённом возврате оружия.
                _abilityPresentationUntil = Time.time + Mathf.Max(0f, action.EndTick - tick) / Simulation.TicksPerSecond;
                _actionProtectedUntil = Time.time + Mathf.Max(0f, action.ContactTick - tick) / Simulation.TicksPerSecond;
            }
            if (!_tempoAbility) return;
            if (IsDead || action.Interrupted || action.Serial != _tempoSerial || tick >= action.EndTick)
            {
                _tempoAbility = false;
                if (_abilityPresentationActive && action.Serial == _tempoSerial)
                {
                    _abilityPresentationActive = false; _actionProtectedUntil = 0f;
                    _animator.CrossFadeInFixedTime(_locomotionMoving ? "Run_v5" : "CombatIdle_v5", .055f, 0);
                }
                return;
            }
            float phase = tick <= action.ContactTick
                ? Mathf.Lerp(0f, _tempoContactPhase, Mathf.InverseLerp(action.StartTick, action.ContactTick, tick))
                : Mathf.Lerp(_tempoContactPhase, 1f, Mathf.InverseLerp(action.ContactTick, action.EndTick, tick));
            if (_abilityDefinitionId == AbilityDefinition.FireFlaskId)
            {
                phase = tick <= _tempoReleaseTick
                    ? Mathf.Lerp(0f, 1f / 3f, Mathf.InverseLerp(action.StartTick, _tempoReleaseTick, tick))
                    : Mathf.Lerp(1f / 3f, 1f, Mathf.InverseLerp(_tempoReleaseTick, action.EndTick, tick));
            }
            _animator.SetFloat("TempoPhase", phase);
        }
    }
}
