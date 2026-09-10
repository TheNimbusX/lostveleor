using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Removes residual horizontal stance drift in the in-place run. Runs after
    /// ArenaView places the body; never moves the simulation root or casts.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PelagFootPlantView : MonoBehaviour
    {
        private sealed class Leg
        {
            public Transform Hip, Knee, Ankle, Toe;
            public Vector3 Plant;
            public bool Planted;
            public int ContactFrame = -1;
            public float LastContactTime = -1f;
        }

        private Animator _animator;
        private CharacterAnimatorView _presentation;
        private Transform _hips;
        private bool _wasTurning;
        private bool _wasWhirlwind;
        private Leg _left, _right;

        public bool TryGetStepContact(bool left, out Vector3 position)
        {
            Leg leg = left ? _left : _right;
            position = leg != null ? leg.Plant : default;
            position.y = transform.position.y + 0.025f;
            return leg != null && leg.ContactFrame == Time.frameCount;
        }
        private int _lowerLayer;
        private float _attackPlantWeight;
        private Vector3 _attackLeft, _attackRight;
        private Quaternion _attackLeftRotation, _attackRightRotation;
        private Vector3 _stepFrom, _stepTo;
        private Quaternion _stepRotationFrom, _stepRotationTo;
        private int _attackState;
        private bool _stepLeft;
        private Transform _spine;
        private Transform _chest, _head;
        private float _idleWeight;
        private static readonly int AttackA = Animator.StringToHash("LowerBody Combat.Lower_Saber_A_v5");
        private static readonly int AttackB = Animator.StringToHash("LowerBody Combat.Lower_Saber_B_v5");
        private static readonly int Run = Animator.StringToHash("Base Layer.Run_v5");
        private static readonly int Relaxed = Animator.StringToHash("Base Layer.RelaxedIdle_v5");
        private static readonly int Combat = Animator.StringToHash("Base Layer.CombatIdle_v5");
        private static readonly int TurnLeft = Animator.StringToHash("Base Layer.TurnLeft_v5");
        private static readonly int TurnRight = Animator.StringToHash("Base Layer.TurnRight_v5");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _presentation = GetComponent<CharacterAnimatorView>();
            _left = FindLeg("Left");
            _right = FindLeg("Right");
            if (_left != null) _hips = _left.Hip.parent;
            foreach (Transform bone in GetComponentsInChildren<Transform>())
            {
                if (bone.name == "mixamorig:Spine") _spine = bone;
                if (bone.name == "mixamorig:Spine2") _chest = bone;
                if (bone.name == "mixamorig:Head") _head = bone;
            }
            _lowerLayer = _animator != null ? _animator.GetLayerIndex("LowerBody Combat") : -1;
            enabled = _animator != null && _left != null && _right != null;
        }

        private void OnEnable() => Release();
        private void OnDisable() => Release();

        private void LateUpdate()
        {
            AnimateIdle();
            // Якорные позы и опоры теперь записаны в клипы; бег хука остаётся бегом.
            if (PlantWhirlwind()) return;
            if (_wasWhirlwind) { Release(); _wasWhirlwind = false; }
            if (PlantAttack()) return;
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            bool running = state.fullPathHash == Run;
            bool turning = state.fullPathHash == TurnLeft || state.fullPathHash == TurnRight;
            bool blending = _animator.IsInTransition(0);
            int next = blending ? _animator.GetNextAnimatorStateInfo(0).fullPathHash : 0;
            turning |= blending && (next == TurnLeft || next == TurnRight);
            bool locomotionTransition = !blending || next == Run || next == Relaxed || next == Combat
                || next == TurnLeft || next == TurnRight;
            // Lower-body attacks and full-body abilities own their leg poses.
            if ((!running && !turning) || !locomotionTransition
                || (_lowerLayer >= 0 && _animator.GetLayerWeight(_lowerLayer) > 0.05f))
            {
                Release();
                return;
            }
            float scale = transform.lossyScale.y / 1.82f;
            if (running && !turning && _hips != null && _presentation != null && !_presentation.HasCommittedAction)
            {
                float lean = Mathf.Clamp(_presentation.TurnAngularSpeed / 600f, -1f, 1f) * -4f;
                _hips.localRotation = Quaternion.AngleAxis(lean, Vector3.forward) * _hips.localRotation;
            }
            float fade = blending && (next == Relaxed || next == Combat)
                ? 1f - _animator.GetAnimatorTransitionInfo(0).normalizedTime : 1f;
            if (turning && (_presentation == null || !_presentation.HasCommittedAction))
            {
                if (!_wasTurning) { Release(); _wasTurning = true; }
                float phase = Mathf.Clamp01(_animator.GetFloat("TurnPhase"));
                bool leftFirst = state.fullPathHash == TurnLeft || next == TurnLeft;
                bool firstHalf = phase < 0.5f;
                Leg swing = leftFirst == firstHalf ? _left : _right;
                Leg support = swing == _left ? _right : _left;
                float step = firstHalf ? phase * 2f : (phase - 0.5f) * 2f;
                // Two deliberate steps for each quarter-turn. The delivered
                // clips barely lift the toes; release one foot before asking
                // the other to pivot, instead of pinning both through the turn.
                swing.Planted = false;
                float lift = Mathf.Sin(step * Mathf.PI) * 0.055f * scale * fade;
                Solve(swing, swing.Ankle.position + Vector3.up * lift);
                Plant(support, scale, fade);
                return;
            }
            if (_wasTurning) { Release(); _wasTurning = false; }
            Plant(_left, scale, fade, true);
            Plant(_right, scale, fade, true);
        }

        private void AnimateIdle()
        {
            if (_presentation == null || _presentation.IsDead || _presentation.HasCommittedAction || _spine == null)
            { _idleWeight = 0f; return; }
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            bool idle = state.fullPathHash == Relaxed || state.fullPathHash == Combat;
            float target = idle && !_presentation.HasCommittedAction && !_presentation.LocomotionMoving ? 1f : 0f;
            _idleWeight = Mathf.MoveTowards(_idleWeight, target, Time.deltaTime / 0.24f);
            if (_idleWeight <= 0f) return;
            // Breathing and small attention shifts use independent periods,
            // so the hero does not repeat one mechanical rocking gesture.
            float breath = Mathf.Sin(Time.time * 2.05f);
            float sway = Mathf.Sin(Time.time * 0.79f);
            float strength = _idleWeight * (_presentation.CombatReady ? 0.75f : 1f);
            _spine.localRotation *= Quaternion.Euler(breath * 0.7f * strength, sway * 1.3f * strength, 0f);
            if (_chest != null) _chest.localRotation *= Quaternion.Euler(breath * 1.25f * strength, 0f,
                Mathf.Sin(Time.time * 1.13f) * 0.65f * strength);
            if (_head != null) _head.localRotation *= Quaternion.Euler(-breath * 0.6f * strength,
                Mathf.Sin(Time.time * 0.47f) * 3.5f * strength, 0f);
        }

        private bool PlantWhirlwind()
        {
            if (_presentation == null || !_presentation.WhirlwindActive || _lowerLayer < 0) return false;
            if (!_wasWhirlwind)
            {
                Release();
                _attackPlantWeight = 0f;
                _wasWhirlwind = true;
            }
            float time = _presentation.WhirlwindElapsed;
            // Alternate the support foot through the authored pivot. Pinning
            // both feet would twist the knees through a complete revolution.
            if (time < 0.09f || time > 0.61f) { Release(); return true; }
            bool middle = time >= 0.30f && time < 0.48f;
            Leg support = middle ? _right : _left;
            Leg swing = middle ? _left : _right;
            swing.Planted = false;
            float step = middle ? Mathf.InverseLerp(0.30f, 0.48f, time)
                : time < 0.30f ? Mathf.InverseLerp(0.09f, 0.30f, time)
                : Mathf.InverseLerp(0.48f, 0.61f, time);
            float scale = transform.lossyScale.y / 1.82f;
            float weight = _animator.GetLayerWeight(_lowerLayer);
            float lift = Mathf.Sin(step * Mathf.PI) * 0.055f * scale;
            float height = swing.Toe.position.y - transform.position.y;
            Solve(swing, swing.Ankle.position + Vector3.up * Mathf.Max(0f, lift - height) * weight);
            Plant(support, scale, weight);
            return true;
        }

        private bool PlantAttack()
        {
            if (_presentation == null || _presentation.IsDead || _lowerLayer < 0) return false;
            // Постановка стоп НЕ распространяется на прыжок.
            //
            // Пробовали 8 сентября: она решает IK ног и двигает таз под позу
            // базовой атаки, а на замахе броска это выворачивало ноги. Скольжение
            // стоп чинится в другом месте — переносом шага таза в сам клип.
            if (_presentation.HasCommittedAction && !_presentation.BasicAttackActive)
            {
                _attackPlantWeight = 0f;
                return false;
            }
            AnimatorStateInfo attackInfo = _animator.IsInTransition(_lowerLayer)
                ? _animator.GetNextAnimatorStateInfo(_lowerLayer)
                : _animator.GetCurrentAnimatorStateInfo(_lowerLayer);
            int state = _animator.GetCurrentAnimatorStateInfo(_lowerLayer).fullPathHash;
            int next = _animator.IsInTransition(_lowerLayer)
                ? _animator.GetNextAnimatorStateInfo(_lowerLayer).fullPathHash : 0;
            bool basic = state == AttackA || state == AttackB || next == AttackA || next == AttackB;
            basic &= _presentation.BasicAttackActive || !_presentation.HasCommittedAction;
            float layerWeight = _animator.GetLayerWeight(_lowerLayer);
            float target = basic && !_presentation.LocomotionMoving ? layerWeight : 0f;
            if (_attackPlantWeight <= 0f && target <= 0.001f) return false;
            if (_attackPlantWeight <= 0f)
            {
                _attackLeft = _left.Toe.position;
                _attackRight = _right.Toe.position;
                _attackLeftRotation = _left.Ankle.rotation;
                _attackRightRotation = _right.Ankle.rotation;
                _attackState = 0;
                _left.Planted = _right.Planted = false;
            }
            // При начале движения отпускаем опору постепенно, чтобы таз
            // не прыгал из исправленной стойки обратно в исходный клип.
            _attackPlantWeight = Mathf.MoveTowards(_attackPlantWeight, target, Time.deltaTime / 0.12f);
            if (_attackPlantWeight <= 0f) return false;
            float scale = transform.lossyScale.y / 1.82f;
            int activeState = attackInfo.fullPathHash;
            if (basic && activeState != _attackState && (activeState == AttackA || activeState == AttackB))
            {
                _attackState = activeState;
                _stepLeft = activeState == AttackB;
                Leg swing = _stepLeft ? _left : _right;
                _stepFrom = _stepLeft ? _attackLeft : _attackRight;
                _stepRotationFrom = _stepLeft ? _attackLeftRotation : _attackRightRotation;
                // Ширина и небольшой разнос стоп задают устойчивую стойку.
                // За один замах переставляется только одна нога, вторая держит вес.
                _stepTo = transform.position + (transform.right * (_stepLeft ? -0.21f : 0.21f)
                    + transform.forward * (_stepLeft ? 0.19f : -0.01f)) * scale;
                _stepTo.y = _stepFrom.y;
                Vector3 toeDirection = Vector3.ProjectOnPlane(swing.Toe.position - swing.Ankle.position, Vector3.up);
                Vector3 wantedDirection = Quaternion.AngleAxis(_stepLeft ? -8f : 8f, Vector3.up) * transform.forward;
                _stepRotationTo = Quaternion.FromToRotation(toeDirection, wantedDirection) * swing.Ankle.rotation;
            }
            float phase = Mathf.Clamp01(attackInfo.normalizedTime);
            float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.39f, phase));
            Vector3 stepPosition = Vector3.Lerp(_stepFrom, _stepTo, step);
            float travel = Vector3.Distance(_stepFrom, _stepTo);
            float lift = Mathf.Sin(step * Mathf.PI) * 0.065f * scale * Mathf.Clamp01(travel / (0.06f * scale));
            stepPosition += Vector3.up * lift;
            Quaternion stepRotation = Quaternion.Slerp(_stepRotationFrom, _stepRotationTo, step);
            if (_stepLeft) { _attackLeft = stepPosition; _attackLeftRotation = stepRotation; }
            else { _attackRight = stepPosition; _attackRightRotation = stepRotation; }

            // Не удерживаем таз развёрнутым на 60–90° над неподвижными стопами.
            // Верх сохраняет мировой поворот клипа: сабля продолжает полный мах.
            Quaternion spineRotation = _spine != null ? _spine.rotation : Quaternion.identity;
            float pelvisYaw = Vector3.SignedAngle(transform.forward,
                Vector3.ProjectOnPlane(_hips.forward, Vector3.up), Vector3.up);
            float yawCorrection = Mathf.Clamp(pelvisYaw, -25f, 25f) - pelvisYaw;
            _hips.rotation = Quaternion.AngleAxis(yawCorrection * _attackPlantWeight, Vector3.up) * _hips.rotation;
            if (_spine != null) _spine.rotation = spineRotation;
            Vector3 correction = (_attackLeft + _attackRight - _left.Toe.position - _right.Toe.position) * 0.5f;
            correction.y = 0f;
            _hips.position += Vector3.ClampMagnitude(correction, 0.65f * scale) * _attackPlantWeight;
            PoseAttackLeg(_left, _attackLeft, _attackLeftRotation, -1f);
            PoseAttackLeg(_right, _attackRight, _attackRightRotation, 1f);
            return true;
        }

        private void PoseAttackLeg(Leg leg, Vector3 toe, Quaternion rotation, float side)
        {
            leg.Ankle.rotation = Quaternion.Slerp(leg.Ankle.rotation, rotation, _attackPlantWeight);
            Vector3 target = leg.Ankle.position + (toe - leg.Toe.position) * _attackPlantWeight;
            // Колено направлено вперёд и немного наружу, а не следует
            // скрученному полюсу из исходного клипа после переноса стопы.
            Solve(leg, target, transform.forward + transform.right * (side * 0.22f), _attackPlantWeight);
        }

        private void Plant(Leg leg, float scale, float fade, bool footstep = false)
        {
            Vector3 toe = leg.Toe.position;
            float height = (toe.y - transform.position.y) / scale;
            if (height > 0.085f)
            {
                leg.Planted = false;
                return;
            }
            if (!leg.Planted)
            {
                if (height > 0.04f) return;
                leg.Plant = toe;
                leg.Planted = true;
                if (footstep && fade > 0.5f && _presentation != null
                    && _presentation.LocomotionMoving && !_presentation.IsDead
                    && Time.time - leg.LastContactTime >= 0.16f)
                {
                    leg.ContactFrame = Time.frameCount;
                    leg.LastContactTime = Time.time;
                }
            }

            Vector3 correction = leg.Plant - toe;
            correction.y = 0f;
            // Release excessive reach on abrupt turns/collisions rather than
            // stretching a leg to an old contact. The authored lift stays intact.
            float maxCorrection = 0.16f * scale;
            if (correction.magnitude > maxCorrection)
            {
                correction = Vector3.ClampMagnitude(correction, maxCorrection);
                leg.Plant = toe + correction;
            }
            float weight = (1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.035f, 0.085f, height))) * Mathf.Clamp01(fade);
            Solve(leg, leg.Ankle.position + correction * weight);
        }

        private static void Solve(Leg leg, Vector3 target, Vector3 pole = default, float poleWeight = 0f)
        {
            Vector3 hip = leg.Hip.position;
            Vector3 knee = leg.Knee.position;
            Vector3 ankle = leg.Ankle.position;
            Quaternion footRotation = leg.Ankle.rotation;
            float upper = Vector3.Distance(hip, knee);
            float lower = Vector3.Distance(knee, ankle);
            Vector3 direction = target - hip;
            float distance = direction.magnitude;
            if (distance < 0.0001f) return;
            direction /= distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 0.0001f,
                (upper + lower) * 0.999f);
            target = hip + direction * distance;
            Vector3 bend = Vector3.ProjectOnPlane(knee - hip, direction);
            if (poleWeight > 0f)
                bend = Vector3.Slerp(bend.normalized, Vector3.ProjectOnPlane(pole, direction).normalized, poleWeight);
            if (bend.sqrMagnitude < 0.000001f) return;
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            float outward = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 wantedKnee = hip + direction * along + bend.normalized * outward;
            leg.Hip.rotation = Quaternion.FromToRotation(knee - hip, wantedKnee - hip) * leg.Hip.rotation;
            leg.Knee.rotation = Quaternion.FromToRotation(leg.Ankle.position - leg.Knee.position,
                target - leg.Knee.position) * leg.Knee.rotation;
            leg.Ankle.rotation = footRotation;
        }

        private Leg FindLeg(string side)
        {
            var leg = new Leg();
            foreach (Transform bone in GetComponentsInChildren<Transform>())
            {
                if (bone.name == "mixamorig:" + side + "UpLeg") leg.Hip = bone;
                else if (bone.name == "mixamorig:" + side + "Leg") leg.Knee = bone;
                else if (bone.name == "mixamorig:" + side + "Foot") leg.Ankle = bone;
                else if (bone.name == "mixamorig:" + side + "ToeBase") leg.Toe = bone;
            }
            return leg.Hip != null && leg.Knee != null && leg.Ankle != null && leg.Toe != null ? leg : null;
        }

        private void Release()
        {
            _attackPlantWeight = 0f;
            _attackState = 0;
            _wasTurning = false;
            if (_left != null) _left.Planted = false;
            if (_right != null) _right.Planted = false;
        }
    }
}
