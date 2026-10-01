using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class CharacterAnimatorView
    {
        private static readonly int BasicComboPhase = Animator.StringToHash("BasicComboPhase");
        private static readonly int[] BasicComboUpperStates = {
            Animator.StringToHash("UpperBody Combat.ComboAttackA"),
            Animator.StringToHash("UpperBody Combat.ComboAttackB"),
            Animator.StringToHash("UpperBody Combat.ComboAttackC") };
        private static readonly int[] BasicComboLowerStates = {
            Animator.StringToHash("LowerBody Combat.LowerComboAttackA"),
            Animator.StringToHash("LowerBody Combat.LowerComboAttackB"),
            Animator.StringToHash("LowerBody Combat.LowerComboAttackC") };

        // Importer/authorer supplies actual contact markers when the three clips are approved.
        [SerializeField] private Vector3 _basicComboContactPhases = new Vector3(4f / 9f, 4f / 9f, .5f);
        private PelagBasicAttackState _basicComboAction;
        private bool _basicComboActive, _basicComboMissingWarning;

        public bool SupportsBasicComboClips
        {
            get
            {
                if (_animator == null || _faction != Faction.Wole || _spriteVisual != null
                    || !HasAnimatorParameter("BasicComboPhase") || _upperBodyLayer < 0 || _lowerBodyLayer < 0)
                    return false;
                for (int stage = 0; stage < 3; stage++)
                    if (!_animator.HasState(_upperBodyLayer, BasicComboUpperStates[stage])
                        || !_animator.HasState(_lowerBodyLayer, BasicComboLowerStates[stage])) return false;
                return true;
            }
        }

        /// <summary>Requires all three real imported clips. A missing finisher is never substituted with A/B.</summary>
        public bool PlayBasicComboAttack(in PelagBasicAttackState action)
        {
            if (action.Serial <= 0 || action.Stage < 0 || action.Stage > 2 || IsDead) return false;
            if (!SupportsBasicComboClips)
            {
                if (!_basicComboMissingWarning)
                {
                    _basicComboMissingWarning = true;
                    Debug.LogWarning("[Pelag combo] Three approved ComboAttack clips are not imported; animation preview remains unavailable.", this);
                }
                return false;
            }
            var sim = TempoSim;
            float tick = sim != null ? sim.Tick - 1 + _cycloneDriver.Alpha : action.StartTick;
            if (tick >= action.EndTick || action.Interrupted) return false;
            StopAttackWarp();
            ResetAbilityTriggers();
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _abilityUsesLowerBodyLayer = false;
            _basicComboAction = action;
            if (sim != null && sim.PelagBasicAttack.Serial == action.Serial && sim.PelagBasicAttack.ContactProcessed)
                _basicComboAction.ContactProcessed = true;
            _basicComboActive = _attackPresentationActive = true;
            SetCombatReady(true);
            float phase = BasicComboSample(_basicComboAction, tick);
            _animator.SetFloat(BasicComboPhase, phase);
            bool observedAtContact = _basicComboAction.ContactProcessed || tick >= action.ContactTick;
            if (observedAtContact)
            {
                // A caught-up hit must already show its blade pose, including the masked layer's weight.
                _animator.Play(BasicComboUpperStates[action.Stage], _upperBodyLayer, 0f);
                _animator.Play(BasicComboLowerStates[action.Stage], _lowerBodyLayer, 0f);
                _animator.SetLayerWeight(_upperBodyLayer, 1f);
                _animator.SetLayerWeight(_lowerBodyLayer, _locomotionMoving ? 0f : 1f);
            }
            else
            {
                _animator.CrossFadeInFixedTime(BasicComboUpperStates[action.Stage], .035f, _upperBodyLayer);
                _animator.CrossFadeInFixedTime(BasicComboLowerStates[action.Stage], .035f, _lowerBodyLayer);
            }
            UpdateBasicComboAnimation();
            // Events arrive after the Animator's normal Update. Evaluate the phase now, without advancing its clock.
            _animator.Update(0f);
            return true;
        }

        private float BasicComboSample(in PelagBasicAttackState action, float tick)
        {
            float marker = action.Stage == 0 ? _basicComboContactPhases.x
                : action.Stage == 1 ? _basicComboContactPhases.y : _basicComboContactPhases.z;
            return PelagBasicAttackTiming.Phase(action, PelagBasicAttackTiming.ObservedTick(action, tick), marker);
        }

        public void ConfirmBasicComboContact(in PelagBasicAttackState action)
        {
            if (!_basicComboActive || action.Serial != _basicComboAction.Serial) return;
            _basicComboAction.ContactProcessed = true;
            UpdateBasicComboAnimation();
            if (_animator != null) _animator.Update(0f);
        }

        private void UpdateBasicComboAnimation()
        {
            if (!_basicComboActive) return;
            var sim = TempoSim;
            float tick = sim != null ? sim.Tick - 1 + _cycloneDriver.Alpha : _basicComboAction.EndTick;
            bool interrupted = sim == null || sim.PelagBasicAttack.Serial == _basicComboAction.Serial
                && sim.PelagBasicAttack.Interrupted;
            if (IsDead || !_attackPresentationActive || interrupted || tick >= _basicComboAction.EndTick)
            {
                _basicComboActive = false;
                if (_attackPresentationActive) CancelUpperBodyAttack(.055f);
                return;
            }
            if (sim.PelagBasicAttack.Serial == _basicComboAction.Serial && sim.PelagBasicAttack.ContactProcessed)
                _basicComboAction.ContactProcessed = true;
            _animator.SetFloat(BasicComboPhase, BasicComboSample(_basicComboAction, tick));
            _attackPresentationUntil = Time.time + Mathf.Max(0f, _basicComboAction.EndTick - tick) / Simulation.TicksPerSecond + .001f;
            _actionProtectedUntil = Time.time + Mathf.Max(0f, _basicComboAction.ContactTick - tick) / Simulation.TicksPerSecond;
        }
    }
}
