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
        private float _whirlHoldTicks;
        private bool _poseHeld;
        private float _poseHoldUntil;

        /// <summary>
        /// Стоп-кадр героя на контакте Вихря. Держится только фаза клипа:
        /// движение, ввод и Sim идут дальше.
        /// </summary>
        public void HoldWhirlwindPose(float seconds)
        {
            if (!WhirlwindActive || seconds <= 0f) return;
            if (_whirlLooping) { _whirlLoopHoldUntil = Mathf.Max(_whirlLoopHoldUntil, Time.time + seconds); return; }
            _whirlHoldTicks = Mathf.Max(_whirlHoldTicks, seconds * Simulation.TicksPerSecond);
        }

        /// <summary>
        /// Стоп-кадр моба: аниматор стоит, пока длится удержание, затем
        /// возвращается к скорости удара или бега. Sim не ждёт.
        /// </summary>
        public void HoldPose(float seconds)
        {
            if (_faction == Faction.Wole || IsDead || _animator == null || seconds <= 0f) return;
            _poseHeld = true;
            _poseHoldUntil = Mathf.Max(_poseHoldUntil, Time.time + seconds);
            _animator.speed = 0f;
            _contactPose?.Hold(seconds);
        }

        private void UpdatePoseHold()
        {
            if (!_poseHeld) return;
            if (IsDead || _animator == null) { _poseHeld = false; return; }
            if (Time.time < _poseHoldUntil)
            {
                // Переутверждается каждый кадр: реакция на удар ставит speed = 1 позже нас.
                _animator.speed = 0f;
                return;
            }
            _poseHeld = false;
            _animator.speed = _attackPresentationActive || Time.time < _orvillHitPresentationUntil
                ? 1f : _orvillLocomotionPlaybackSpeed;
        }

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

        // ---- Вихрь: фаза клипа, стоп-кадр и цикл удержания ----

        // Один авторский оборот сабли лежит в фазах 0.15–0.65 клипа (34° → 365°).
        // При удержании этот отрезок крутится по кругу, один оборот на импульс
        // Sim (полсекунды); перемотка приходится на кадр-вспышку импульса.
        private const float WhirlwindLoopStart = .15f;
        private const float WhirlwindLoopEnd = .65f;
        private const float WhirlwindLoopSeconds = Simulation.WhirlwindPulseTicks / (float)Simulation.TicksPerSecond;
        private const float WhirlwindLoopRate = (WhirlwindLoopEnd - WhirlwindLoopStart) / WhirlwindLoopSeconds;
        private const float WhirlwindExitRecoverySeconds = .16f;
        /// <summary>После конца Вихря взгляд героя догоняет Sim плавно, а не рывком в один кадр.</summary>
        public const float WhirlwindFacingRecoverySeconds = .28f;

        private float _whirlPhase;
        private bool _whirlLooping, _whirlExiting;
        private float _whirlLoopTime, _whirlLoopHoldUntil, _whirlExitPhase, _whirlExitAt, _whirlExitSeconds;
        private float _whirlwindEndedAt = -10f;

        public bool WhirlwindChannelLooping => _whirlLooping;
        public bool WhirlwindFacingRecovery => !WhirlwindActive && Time.time < _whirlwindEndedAt + WhirlwindFacingRecoverySeconds;

        /// <summary>Время клипа текущей позы: по фазе в цикле и на выходе, −1 в обычном касте (там считают часы).</summary>
        internal float WhirlwindPoseTime => _whirlLooping || _whirlExiting ? _whirlPhase * WhirlwindClipDuration : -1f;

        private float WhirlwindPhaseFor(Simulation sim, in PlayerActionState action, float tick)
        {
            float contact = WhirlwindContactTime / WhirlwindClipDuration;
            bool channeling = sim.WhirlwindChanneling && !IsDead && tick >= action.ContactTick;
            if (channeling)
            {
                if (!_whirlLooping) { _whirlLooping = true; _whirlExiting = false; _whirlLoopTime = 0f; _whirlLoopHoldUntil = 0f; }
                else if (Time.time >= _whirlLoopHoldUntil) _whirlLoopTime += Time.deltaTime;
                // Слои и опора ног считают, что оборот в разгаре: часы держатся до начала возврата.
                _abilityPresentationUntil = Time.time + WhirlwindClipDuration * (1f - WhirlwindLoopEnd);
                _actionProtectedUntil = Time.time;
                return Mathf.Lerp(WhirlwindLoopStart, WhirlwindLoopEnd, Mathf.Repeat(_whirlLoopTime / WhirlwindLoopSeconds, 1f));
            }
            if (_whirlLooping)
            {
                // Удержание кончилось: дойти до конца оборота на скорости цикла, потом возврат клипа.
                _whirlLooping = false; _whirlExiting = true;
                _whirlExitPhase = _whirlPhase; _whirlExitAt = Time.time;
                _whirlExitSeconds = Mathf.Max(0f, WhirlwindLoopEnd - _whirlExitPhase) / WhirlwindLoopRate + WhirlwindExitRecoverySeconds;
                _abilityPresentationUntil = Time.time + _whirlExitSeconds;
                _actionProtectedUntil = Time.time;
            }
            if (_whirlExiting)
            {
                float elapsed = Time.time - _whirlExitAt;
                float turn = Mathf.Max(0f, WhirlwindLoopEnd - _whirlExitPhase) / WhirlwindLoopRate;
                if (elapsed >= _whirlExitSeconds) { _whirlExiting = false; return 1f; }
                return elapsed < turn
                    ? Mathf.Lerp(_whirlExitPhase, WhirlwindLoopEnd, turn > 0f ? elapsed / turn : 1f)
                    : Mathf.Lerp(WhirlwindLoopEnd, 1f, (elapsed - turn) / WhirlwindExitRecoverySeconds);
            }
            // Стоп-кадр: после контакта поза держится _whirlHoldTicks, затем
            // остаток клипа сжимается к прежнему EndTick — как у Рассекающего.
            // Sim и управление не ждут.
            float whirlTick = tick;
            if (_whirlHoldTicks > 0f && tick > action.ContactTick)
                whirlTick = tick < action.ContactTick + _whirlHoldTicks ? action.ContactTick
                    : Mathf.Lerp(action.ContactTick, action.EndTick,
                        Mathf.InverseLerp(action.ContactTick + _whirlHoldTicks, action.EndTick, tick));
            return whirlTick <= action.ContactTick ? Mathf.Lerp(0, contact, Mathf.InverseLerp(action.StartTick, action.ContactTick, whirlTick))
                : Mathf.Lerp(contact, 1, Mathf.InverseLerp(action.ContactTick, action.EndTick, whirlTick));
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
                    _animator.SetFloat("WhirlwindPhase", _whirlPhase = WhirlwindPhaseFor(sim, action, tick));
            }
            if (_abilityPresentationActive && action.DefinitionId == _abilityDefinitionId && !action.Interrupted
                && !_whirlLooping && !_whirlExiting)
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
