using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря, правка анимации (animfix 07.10, владелец: «провисание якоря в конце»): якорь в правом кулаке лежит
    /// ВДОЛЬ оси кулака «мизинец → указательный» венцом по ней — замах (ось вниз, венец у земли, поза 1 листа) и ловля
    /// (ось вверх, венец у головы, поза 6). Прежде голова стояла по предплечью (AnchorThrowPosition), а после ловли
    /// маятник/сматывание цепи уносили её на спину ~1,4 с с провисом. Уборка теперь — закидка через плечо по клипу:
    /// кадр Catch 3 (кулак над плечом, timing.json keys.stow_handoff) → голова ещё в кулаке ThrowStowHoldSeconds (кулак
    /// уходит за плечо, кисть отпускает в Catch 3,75) → бросок на крепление спины за ThrowStowTossSeconds (smootherstep по
    /// положению и повороту) → посадка (OnBack) и отдача. Рукоять из левой — на спину скольжением с того же кадра.
    /// </summary>
    public sealed partial class PelagAnchorRig
    {
        /// <summary>false — прежний хват по предплечью и уборка физикой (сматывание цепи).</summary>
        public static bool ThrowFistGrip = true;

        /// <summary>Центр головы от центра кулака по оси кулака, доля |кольцо − центр| (хват у кольца: веретено в кулаке).</summary>
        private const float ThrowGripCenterReach = .63f;
        private const float ThrowStowHoldSeconds = .025f, ThrowStowTossSeconds = .07f, ThrowGripSlideSeconds = .12f;

        private Transform _rIndex1, _rPinky1;
        private Transform[] _rKnuckles;
        private bool _knucklesLooked, _spanReady, _throwStow, _throwTossing;
        private Vector3 _spanLocal, _previousFistHead;
        private bool _hasFistHead;
        private const float ThrowDrawSeconds = .06f;
        private float _drawAge;
        private Vector3 _drawFromPosition;
        private Quaternion _drawFromRotation = Quaternion.identity;
        private float _throwStowAge, _tossAge;
        private Vector3 _tossFromPosition;
        private Quaternion _tossFromRotation;

        private void FindKnuckles()
        {
            _knucklesLooked = true;
            if (_rHand == null) return;
            var found = new System.Collections.Generic.List<Transform>();
            foreach (var t in _rHand.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!n.StartsWith("mixamorig:RightHand") || !(n.EndsWith("1") || n.EndsWith("2")) || n.Contains("Thumb")) continue;
                found.Add(t);
                if (n == "mixamorig:RightHandIndex1") _rIndex1 = t;
                if (n == "mixamorig:RightHandPinky1") _rPinky1 = t;
            }
            _rKnuckles = found.ToArray();
        }

        /// <summary>Центр обхвата правого кулака и ось «мизинец → указательный» (как wk_grip клипов).</summary>
        private bool RightFist(out Vector3 center, out Vector3 axis)
        {
            center = axis = Vector3.zero;
            if (!_knucklesLooked) FindKnuckles();
            if (_rIndex1 == null || _rPinky1 == null || _rKnuckles == null || _rKnuckles.Length < 4) return false;
            Vector3 all = Vector3.zero, first = Vector3.zero;
            int nFirst = 0;
            foreach (var k in _rKnuckles)
            {
                all += k.position;
                if (k.name.EndsWith("1")) { first += k.position; nFirst++; }
            }
            all /= _rKnuckles.Length;
            first /= Mathf.Max(1, nFirst);
            center = Vector3.Lerp(all, first, .25f);
            axis = _rIndex1.position - _rPinky1.position;
            if (axis.sqrMagnitude < 1e-8f) return false;
            axis.Normalize();
            return true;
        }

        /// <summary>Поперёк веретена в осях головы — к самой дальней от веретена точке оболочки (лапа): плоскость лап.</summary>
        private void EnsureSpan()
        {
            if (_spanReady) return;
            _spanReady = true;
            NV eye = _core.Body.EyeLocal;
            Vector3 shank = eye.LengthSquared() > 1e-8f ? -U(eye).normalized : Vector3.down;
            Vector3 best = Vector3.zero;
            if (_core.Body.Hull != null)
                foreach (NV h in _core.Body.Hull)
                {
                    Vector3 p = U(h), perp = p - shank * Vector3.Dot(p, shank);
                    if (perp.sqrMagnitude > best.sqrMagnitude) best = perp;
                }
            if (best.sqrMagnitude < 1e-8f) best = Vector3.Cross(shank, Vector3.right);
            _spanLocal = best.normalized;
        }

        /// <summary>Поза головы в кулаке: веретено (кольцо → венец) по оси кулака, лапы — поперёк тела (вправо корня).</summary>
        private bool FistHeadPose(out Vector3 center, out Quaternion rotation)
        {
            center = Vector3.zero; rotation = Quaternion.identity;
            if (!RightFist(out Vector3 fist, out Vector3 axis)) return false;
            EnsureSpan();
            NV eye = _core.Body.EyeLocal;
            float eyeDistance = Mathf.Sqrt(eye.LengthSquared());
            Vector3 shankLocal = eyeDistance > 1e-4f ? -U(eye) / eyeDistance : Vector3.down;
            Vector3 side = Vector3.ProjectOnPlane(transform.right, axis);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(transform.forward, axis);
            Quaternion local = Quaternion.LookRotation(shankLocal, _spanLocal);
            Quaternion world = Quaternion.LookRotation(axis, side.normalized);
            rotation = world * Quaternion.Inverse(local);
            center = fist + axis * (ThrowGripCenterReach * eyeDistance);
            return true;
        }

        /// <summary>Голова в правом кулаке (замах и ловля Броска). Нет костей пальцев — прежний хват по предплечью.</summary>
        private Directive ThrowFistDirective(float dt, string note, bool draw = false, bool settle = false)
        {
            if (!ThrowFistGrip || !FistHeadPose(out Vector3 center, out Quaternion rotation))
            {
                Directive old = FistDirective(dt);
                old.Note = note;
                return old;
            }
            if (draw)
            {
                // Замах: голова со спины в кулак ровной дугой за ThrowDrawSeconds от того места, где она есть (без сшивки
                // с ограничением ускорения: та тянула её за кулаком 0,3–0,8 м весь замах, замер 07.10).
                if (!_hasFistHead) { _drawFromPosition = U(_core.Output.Position); _drawFromRotation = UQ(_core.Output.Rotation); _drawAge = 0f; }
                _drawAge += dt;
                float u = Mathf.Clamp01(_drawAge / ThrowDrawSeconds), w = u * u * (3f - 2f * u);
                Vector3 lift = Vector3.up * (.25f * _bodyScale * Mathf.Sin(Mathf.PI * u));
                center = Vector3.Lerp(_drawFromPosition, center, w) + lift * (1f - w);
                rotation = Quaternion.Slerp(_drawFromRotation, rotation, w);
            }
            Vector3 velocity = _hasFistHead && dt > 1e-5f ? (center - _previousFistHead) / dt : Vector3.zero;
            _previousFistHead = center; _hasFistHead = true;
            return new Directive
            {
                Mode = AnchorRigMode.Baked, Segment = -1001, Taut = false, Deadline = -1f, Note = note, Settle = settle,
                Target = new AnchorPose(N(center), NQ_(rotation), N(velocity), NV.Zero),
            };
        }

        /// <summary>
        /// Последние ThrowCatchApproach м возврата (Along → 0 к тику ловли): голова сходит с линии в правый кулак — в тик ловли
        /// она уже в хвате (поза 6), а не догоняет кулак сшивкой (замер 07.10: зазор 0,5–0,9 м всё удержание).
        /// </summary>
        private const float ThrowCatchApproach = 2.5f;

        private AnchorPose CatchApproach(in AnchorLineFrame f, AnchorPose line)
        {
            if (!ThrowFistGrip) return line;
            float reach = ThrowCatchApproach * _bodyScale;
            if (f.Along >= reach || !FistHeadPose(out Vector3 center, out Quaternion rotation)) return line;
            float u = 1f - Mathf.Clamp01(f.Along / reach);
            float w = u * u * (3f - 2f * u);
            // Скорость цели гаснет к кулаку: иначе стык ловли унёс бы 30–50 м/с возврата дальше кулака (замер 07.10: перелёт ~1 м).
            return new AnchorPose(N(Vector3.Lerp(U(line.Position), center, w)), NQ_(Quaternion.Slerp(UQ(line.Rotation), rotation, w)),
                line.Velocity * (1f - w), NV.Zero);
        }

        /// <summary>Кадр «якорь на спину» (CueThrowStow): закидка по клипу — кулак ещё держит, потом бросок на крепление.</summary>
        private void BeginThrowStow()
        {
            _throwStow = true; _throwTossing = false; _throwStowAge = 0f; _tossAge = 0f;
            _equipment.SlideGripSeconds(toHand: false, ThrowGripSlideSeconds);
        }

        private Directive ThrowStowDirective(float dt)
        {
            _throwStowAge += dt;
            if (_throwStowAge < ThrowStowHoldSeconds) return ThrowFistDirective(dt, "throw-stow-hold");
            if (!_throwTossing)
            {
                // Бросок — от показанной головы (стык без зазора), скорость прошлого кадра не переносится (Settle).
                _throwTossing = true; _tossAge = 0f;
                _tossFromPosition = U(_core.Output.Position);
                _tossFromRotation = UQ(_core.Output.Rotation);
                PrimeBackTarget();
            }
            _tossAge += dt;
            float u = Mathf.Clamp01(_tossAge / ThrowStowTossSeconds);
            float w = u * u * u * (u * (6f * u - 15f) + 10f);
            AnchorPose back = BackTarget(dt);
            if (u >= 1f)
            {
                _throwStow = false; _catching = false;
                _stow = StowStep.Handoff; _stowAge = 0f;
                if (CaptureRig.LiveSkill) Debug.Log($"{Log} throw stow landed caughtAge={_caughtAge:F2}");
                return new Directive { Mode = AnchorRigMode.OnBack, Target = back, Segment = -2, Deadline = -1f, Note = "stow-back", Settle = true };
            }
            Vector3 from = _tossFromPosition, to = U(back.Position);
            Vector3 position = Vector3.Lerp(from, to, w);
            Quaternion rotation = Quaternion.Slerp(_tossFromRotation, UQ(back.Rotation), w);
            float slope = 30f * u * u * (u - 1f) * (u - 1f) / ThrowStowTossSeconds;
            return new Directive
            {
                Mode = AnchorRigMode.Baked, Segment = -1006, Taut = false, Deadline = -1f, Note = "throw-stow-toss", Settle = true,
                Target = new AnchorPose(N(position), NQ_(rotation), N((to - from) * slope), NV.Zero),
            };
        }
    }
}
