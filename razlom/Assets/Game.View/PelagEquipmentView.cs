using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Рука берёт саблю с пояса и возвращает её тем же путём. Пропс меняет
    /// сокет в точке захвата; игровая атака не ждёт завершения жеста.
    /// </summary>
    [DefaultExecutionOrder(1005)]
    [DisallowMultipleComponent]
    public sealed class PelagEquipmentView : MonoBehaviour
    {
        public readonly struct MountPoint
        {
            public readonly Transform Socket;
            public readonly Vector3 LocalPosition;
            public readonly Vector3 LocalEuler;
            public readonly Vector3 LocalScale;

            public MountPoint(Transform socket, Vector3 localPosition,
                Vector3 localEuler, Vector3 localScale)
            {
                Socket = socket;
                LocalPosition = localPosition;
                LocalEuler = localEuler;
                LocalScale = localScale;
            }
        }

        private Transform _saber;
        private Transform _anchor;
        private MountPoint _saberStored;
        private MountPoint _saberEquipped;
        private MountPoint _anchorStored;
        private MountPoint _anchorEquipped;
        private bool _combatReady;
        private bool _anchorInHand;
        private bool _configured;
        private Transform _leftHand, _leftForearm, _leftUpperArm;
        private Transform _anchorHead;
        private float _anchorStartedAt;
        private bool _anchorLeap;
        public Vector3 AnchorHeadPosition => _anchorHead != null ? _anchorHead.position : ChainHand.position;
        private CharacterAnimatorView _presentation;
        private Transform _upperArm, _forearm, _hand, _chest;
        // Одна обратимая шкала: 0 — пояс, 1 — боевая стойка.
        private const float GripPhase = 0.35f;
        private float _drawPhase;
        public float DrawPhase => _drawPhase;
        public float GripError { get; private set; }
        public float TransferDistance { get; private set; }

        public bool CombatReady => _combatReady;
        public bool AnchorInHand => _anchorInHand;
        public bool AnchorHeadVisible => _anchorHead != null && _anchorHead.gameObject.activeInHierarchy;
        public bool Configured => _configured;
        public Transform ChainHand => _anchorEquipped.Socket;
        public bool SaberInHand => _saber != null
                                   && _saberEquipped.Socket != null
                                   && _saber.parent == _saberEquipped.Socket;

        public void Configure(Transform saber, MountPoint saberStored,
            MountPoint saberEquipped, Transform anchor, MountPoint anchorStored,
            MountPoint anchorEquipped)
        {
            _saber = saber;
            _anchor = anchor;
            _saberStored = saberStored;
            _saberEquipped = saberEquipped;
            _anchorStored = anchorStored;
            _anchorEquipped = anchorEquipped;
            _presentation = GetComponent<CharacterAnimatorView>();
            _hand = saberEquipped.Socket;
            _forearm = _hand != null ? _hand.parent : null;
            _upperArm = _forearm != null ? _forearm.parent : null;
            foreach (Transform bone in GetComponentsInChildren<Transform>())
                if (bone.name == "mixamorig:Spine2") _chest = bone;
            _leftHand = anchorEquipped.Socket;
            _leftForearm = _leftHand != null ? _leftHand.parent : null;
            _leftUpperArm = _leftForearm != null ? _leftForearm.parent : null;
            // The legacy grip prefab includes a complete anchor mesh. Keep
            // its socket transform, but render only the matching held/VFX head.
            if (_anchor != null)
                foreach (Renderer renderer in _anchor.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            CreateHeldAnchorHead();
            _configured = true;
            ResetForSpawn();
        }

        /// <summary>
        /// Pooled bodies retain their hierarchy between uses. Reset both props
        /// explicitly so a body released during an anchor throw cannot respawn
        /// with the anchor in its hand or the saber already drawn.
        /// </summary>
        public void ResetForSpawn()
        {
            _presentation = GetComponent<CharacterAnimatorView>();
            _drawPhase = 0f;
            _combatReady = false;
            _anchorInHand = false;
            ApplyAnchor();
            ApplySaber();
        }

        public void SetCombatReady(bool combatReady)
        {
            // Повторное событие боя не должно начинать захват заново.
            _combatReady = combatReady;
        }

        public void BeginAnchorUse(bool leap = false)
        {
            if (_anchorInHand) return;
            _anchorInHand = true;
            _anchorStartedAt = Time.time;
            _anchorLeap = leap;
            _drawPhase = 0f;
            Mount(_saber, _saberStored);
            Mount(_anchor, _anchorStored);
        }

        public void EndAnchorUse()
        {
            if (!_anchorInHand) return;
            _anchorInHand = false;
            ApplyAnchor();
        }

        private void LateUpdate()
        {
            if (!_configured || _saber == null || _hand == null || _upperArm == null
                || _saberStored.Socket == null) return;
            GripError = 0f;
            TransferDistance = 0f;
            if (_presentation != null && _presentation.IsDead) { PlaceAnchorHead(); return; }
            if (!_anchorInHand) PlaceAnchorHead();
            if (_anchorInHand)
            {
                _drawPhase = 0f;
                Mount(_saber, _saberStored);
                AnimateAnchor();
                return;
            }
            // Принятый Sim удар важнее бытового жеста. Клинок уже в ладони
            // на первом кадре замаха; IK не переписывает позу способности.
            if (_presentation != null && _presentation.HasCommittedAction)
            {
                _drawPhase = _combatReady ? 1f : 0f;
                ApplySaber();
                return;
            }

            float previousPhase = _drawPhase;
            _drawPhase = Mathf.MoveTowards(_drawPhase, _combatReady ? 1f : 0f,
                Time.deltaTime / (_combatReady ? 0.62f : 0.70f));
            // Даже длинный кадр обязан показать точку захвата до смены сокета.
            if ((previousPhase < GripPhase && _drawPhase > GripPhase)
                || (previousPhase > GripPhase && _drawPhase < GripPhase)) _drawPhase = GripPhase;
            if (_drawPhase <= 0f || _drawPhase >= 1f)
            {
                ApplySaber();
                return;
            }
            float reach = Smooth(_drawPhase / GripPhase);
            float release = Smooth((_drawPhase - GripPhase) / (1f - GripPhase));
            float weight = reach * (1f - release);
            if (_chest != null && !_anchorLeap && _presentation != null && _presentation.LocomotionMoving)
                _chest.localRotation *= Quaternion.Euler(2f * weight, -7f * weight, 0f);

            // Решаем обратный сокет: где должна быть кисть, чтобы её штатный
            // хват точно совпал с рукоятью сабли на поясе, включая поворот.
            Vector3 storedPosition = _saberStored.Socket.TransformPoint(_saberStored.LocalPosition);
            Quaternion storedRotation = _saberStored.Socket.rotation * Quaternion.Euler(_saberStored.LocalEuler);
            Quaternion gripRotation = storedRotation * Quaternion.Inverse(Quaternion.Euler(_saberEquipped.LocalEuler));
            Vector3 gripPosition = storedPosition - gripRotation * Vector3.Scale(_hand.lossyScale, _saberEquipped.LocalPosition);
            Vector3 authoredPosition = _hand.position;
            Quaternion authoredRotation = _hand.rotation;
            float extraction = Mathf.Clamp01((_drawPhase - GripPhase) / (1f - GripPhase));
            // После захвата сначала приподнимаем клинок от пояса, затем
            // раскрываем руку в исходную стойку. На уборке путь обратный.
            float bodyScale = transform.lossyScale.y / 1.82f;
            // Keep the blade's belt orientation until it has cleared the torso.
            // Only then rotate the wrist on the outside of the body.
            Vector3 clearPosition = gripPosition + (transform.forward * 0.42f
                + transform.up * 0.24f + transform.right * 0.12f) * bodyScale;
            Vector3 target;
            Quaternion targetRotation;
            if (_drawPhase <= GripPhase)
            {
                target = Vector3.Lerp(authoredPosition, gripPosition, reach);
                targetRotation = Quaternion.Slerp(authoredRotation, gripRotation, reach);
            }
            else
            {
                float lift = Smooth(extraction / 0.55f);
                float open = Smooth((extraction - 0.55f) / 0.45f);
                target = Vector3.Lerp(Vector3.Lerp(gripPosition, clearPosition, lift), authoredPosition, open);
                targetRotation = Quaternion.Slerp(gripRotation, authoredRotation, open);
            }
            SolveArm(target, _drawPhase <= GripPhase ? reach : 1f - Smooth((extraction - 0.7f) / 0.3f));
            _hand.rotation = targetRotation;
            GripError = Vector3.Distance(_hand.position, target);
            MountPoint mount = _drawPhase > GripPhase || (_drawPhase == GripPhase && _combatReady)
                ? _saberEquipped : _saberStored;
            bool transfer = _saber.parent != mount.Socket;
            Vector3 beforeMount = _saber.position;
            Mount(_saber, mount);
            if (transfer) TransferDistance = Vector3.Distance(beforeMount, _saber.position);
        }

        private void SolveArm(Vector3 target, float weight)
        {
            Vector3 shoulder = _upperArm.position;
            Vector3 elbow = _forearm.position;
            float upper = Vector3.Distance(shoulder, elbow);
            float lower = Vector3.Distance(elbow, _hand.position);
            // Небольшое движение ключицы даёт достать противоположный бок
            // без растяжения предплечья. Нужен только недостающий запас длины.
            Transform clavicle = _upperArm.parent;
            float missing = Vector3.Distance(shoulder, target) - (upper + lower) * 0.995f;
            if (missing > 0f && clavicle != null)
            {
                Quaternion toward = Quaternion.FromToRotation(shoulder - clavicle.position,
                    target - clavicle.position);
                float degrees = Mathf.Min(35f * weight,
                    missing / Mathf.Max(0.01f, Vector3.Distance(shoulder, clavicle.position)) * Mathf.Rad2Deg * 1.3f);
                clavicle.rotation = Quaternion.RotateTowards(Quaternion.identity, toward, degrees) * clavicle.rotation;
                shoulder = _upperArm.position;
                elbow = _forearm.position;
            }
            Vector3 direction = target - shoulder;
            float distance = direction.magnitude;
            if (distance < 0.0001f) return;
            direction /= distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 0.0001f, (upper + lower) * 0.999f);
            target = shoulder + direction * distance;
            // Постоянный локальный полюс не даёт локтю переворачиваться при
            // пересечении рукой средней линии тела или развороте героя.
            Vector3 bend = Vector3.ProjectOnPlane(transform.right - transform.forward * 0.35f, direction).normalized;
            Vector3 authoredBend = Vector3.ProjectOnPlane(elbow - shoulder, direction).normalized;
            bend = Vector3.Slerp(authoredBend, bend, weight).normalized;
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            float outward = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 desiredElbow = shoulder + direction * along + bend * outward;
            _upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, desiredElbow - shoulder) * _upperArm.rotation;
            _forearm.rotation = Quaternion.FromToRotation(_hand.position - _forearm.position,
                target - _forearm.position) * _forearm.rotation;
        }

        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

        private void CreateHeldAnchorHead()
        {
            if (_anchorHead != null || _anchor == null) return;
            var source = Resources.Load<GameObject>("Weapons/Pelag/AnchorChain/Pelag_AnchorHead");
            if (source == null) return;
            var root = new GameObject("Pelag held anchor head");
            var model = Instantiate(source, root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = 0.56f / Mathf.Max(0.001f, size);
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = -bounds.center * scale;
                var metal = Resources.Load<Material>("VFX/Pelag/Materials/M_AnchorMetal");
                if (metal != null) foreach (var renderer in renderers) renderer.sharedMaterial = metal;
            }
            _anchorHead = root.transform;
            _anchorHead.SetParent(transform, true);
        }

        private void AnimateAnchor()
        {
            if (_leftHand == null || _leftUpperArm == null) return;
            float age = Time.time - _anchorStartedAt;
            float duration = _anchorLeap ? PelagAbilityTiming.LeapRecovery : PelagAbilityTiming.SweepRecovery;
            float pullStart = _anchorLeap ? PelagAbilityTiming.LeapWindup : PelagAbilityTiming.SweepWindup;
            float pullEnd = _anchorLeap ? PelagAbilityTiming.LeapArrival : pullStart + PelagAbilityTiming.SweepTravel;
            float scale = transform.lossyScale.y / 1.82f;
            float throwPhase = Smooth((age - 0.12f) / 0.20f);
            float haul = Smooth((age - pullStart) / (pullEnd - pullStart));
            float settle = Smooth((age - pullEnd) / (duration - pullEnd));
            if (_chest != null)
            {
                float twist = Mathf.Sin(throwPhase * Mathf.PI) * -18f + (1f - settle) * haul * 12f;
                _chest.rotation = Quaternion.AngleAxis(twist, transform.up)
                    * Quaternion.AngleAxis((haul * -9f + throwPhase * 5f) * (1f - settle), transform.right)
                    * _chest.rotation;
            }
            Quaternion storedRotation = _anchorStored.Socket.rotation * Quaternion.Euler(_anchorStored.LocalEuler);
            Quaternion gripRotation = storedRotation * Quaternion.Inverse(Quaternion.Euler(_anchorEquipped.LocalEuler));
            Vector3 grip = _anchorStored.Socket.TransformPoint(_anchorStored.LocalPosition)
                - gripRotation * Vector3.Scale(_leftHand.lossyScale, _anchorEquipped.LocalPosition);
            Vector3 chamber = transform.position + (Vector3.up * 1.25f - transform.right * 0.32f
                - transform.forward * 0.15f) * scale;
            Vector3 extended = transform.position + (Vector3.up * 1.12f - transform.right * 0.20f
                + transform.forward * 0.64f) * scale;
            Vector3 brace = transform.position + (Vector3.up * 0.96f - transform.right * 0.28f
                + transform.forward * 0.16f) * scale;
            Vector3 target;
            float reach = Smooth(age / 0.12f);
            if (age < 0.12f) target = Vector3.Lerp(_leftHand.position, grip, reach);
            else if (age < PelagAbilityTiming.AnchorDraw)
                target = Vector3.Lerp(grip, chamber, Smooth((age - 0.12f) / 0.08f));
            else target = Vector3.Lerp(Vector3.Lerp(chamber, extended,
                Smooth((age - PelagAbilityTiming.AnchorDraw) / (pullStart - PelagAbilityTiming.AnchorDraw))), brace, haul);
            if (_anchorLeap && age >= pullStart)
            {
                // The taut chain pulls the hands forward throughout flight.
                // Do not haul both hands back into the torso halfway through.
                float flight = Mathf.Sin(haul * Mathf.PI);
                target = extended + transform.up * (0.10f * flight * scale)
                    - transform.forward * (0.10f * haul * scale);
            }
            // Возврат проходит через захват у пояса, затем рука освобождает клип.
            // Иначе последний кадр IK сменяется совершенно другой позой idle.
            float returnToBelt = Smooth((age - pullEnd) / Mathf.Max(.01f, duration - .06f - pullEnd));
            target = Vector3.Lerp(target, grip, returnToBelt);
            float releaseHand = Smooth((age - duration + .06f) / .06f);
            target = Vector3.Lerp(target, _leftHand.position, releaseHand);
            Quaternion authoredRotation = _leftHand.rotation;
            SolveChain(_leftUpperArm, _leftForearm, _leftHand, target,
                -transform.right + transform.forward * 0.15f);
            _leftHand.rotation = Quaternion.Slerp(authoredRotation, gripRotation,
                (age < 0.20f ? reach : returnToBelt) * (1f - releaseHand));
            Mount(_anchor, age >= 0.12f && age < duration - .06f ? _anchorEquipped : _anchorStored);
            if (_anchorHead != null)
            {
                _anchorHead.gameObject.SetActive(age < PelagAbilityTiming.AnchorDraw || age >= duration - .06f);
                PlaceAnchorHead();
            }
            // The free hand joins the haul without a second weapon appearing.
            float support = Smooth((age - 0.24f) / 0.18f) * (1f - settle);
            Vector3 supportTarget = _leftHand.position + transform.right * (0.20f * scale)
                + transform.forward * (0.12f * scale);
            SolveChain(_upperArm, _forearm, _hand, Vector3.Lerp(_hand.position, supportTarget, support),
                transform.right - transform.forward * 0.15f);
        }

        private void PlaceAnchorHead()
        {
            if (_anchorHead == null || _anchor == null) return;
            float age = Time.time - _anchorStartedAt;
            float duration = _anchorLeap ? PelagAbilityTiming.LeapRecovery : PelagAbilityTiming.SweepRecovery;
            float take = _anchorInHand ? Smooth((age - .12f) / .08f)
                * (1f - Smooth((age - duration + .16f) / .10f)) : 0f;
            _anchorHead.position = _anchor.position + Vector3.Lerp(Vector3.down * .23f, transform.forward * .17f, take);
            _anchorHead.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
        }

        private static void SolveChain(Transform upperArm, Transform forearm, Transform hand,
            Vector3 target, Vector3 pole)
        {
            if (upperArm == null || forearm == null || hand == null) return;
            Vector3 shoulder = upperArm.position;
            Vector3 elbow = forearm.position;
            float a = Vector3.Distance(shoulder, elbow), b = Vector3.Distance(elbow, hand.position);
            Vector3 delta = target - shoulder;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + 0.001f, (a + b) * 0.98f);
            Vector3 direction = delta.normalized;
            if (direction.sqrMagnitude < 0.001f) return;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            Vector3 desiredElbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, desiredElbow - shoulder) * upperArm.rotation;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position,
                shoulder + direction * distance - forearm.position) * forearm.rotation;
        }

        private void ApplySaber() => Mount(_saber, _drawPhase >= GripPhase && !_anchorInHand
            ? _saberEquipped : _saberStored);

        private void ApplyAnchor()
        {
            Mount(_anchor, _anchorInHand ? _anchorEquipped : _anchorStored);
            if (_anchor != null) _anchor.gameObject.SetActive(true);
            if (_anchorHead != null) { _anchorHead.gameObject.SetActive(true); PlaceAnchorHead(); }
        }

        private static void Mount(Transform prop, MountPoint point)
        {
            if (prop == null || point.Socket == null) return;
            prop.SetParent(point.Socket, false);
            prop.localPosition = point.LocalPosition;
            prop.localRotation = Quaternion.Euler(point.LocalEuler);
            prop.localScale = point.LocalScale;
            if (!prop.gameObject.activeSelf) prop.gameObject.SetActive(true);
        }

    }
}
