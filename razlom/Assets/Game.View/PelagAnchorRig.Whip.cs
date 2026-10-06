using UnityEngine;
using NV = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

namespace Game.View
{
    /// <summary>
    /// Крушение v4: цепь-хлыст, вторичный поворот головы и рукоять в обеих руках (перенос слоя «цепь-хлыст» превью
    /// ART/characters/pelag/wreck-2026-10-03/animation-v4/tools/wreck4anchor: WhipChain, Secondary, Whip).
    /// Цепь ВСЕГДА симулируется: 16 звеньев, подшаги 1/480 с, капсулы тела рига + веретено головы, земля; концы прибиты
    /// к выходу цепи из рукояти и к кольцу показанной головы. Длина — из запечки (по фазам), в паузе — хорда + 1,5 см.
    /// Рукоять — по запечке (h0 у кисточки → h1 выход цепи), со спины в руки за кадр после хвата, обратно — на укладке.
    /// </summary>
    public sealed partial class PelagAnchorRig
    {
        private const float WhipSubstep = 1f / 480f;
        private const int WhipMaxSubsteps = 16;
        private readonly AnchorWhipChain _whip = new AnchorWhipChain(16);
        private readonly AnchorSecondary _secondary = new AnchorSecondary();
        private readonly AnchorCapsule[] _whipCaps = new AnchorCapsule[AnchorWhipChain.MaxCapsules];
        private float _whipLength;
        private Vector3 _whipGripPrev, _whipRingPrev, _handleAxisLocal;
        private bool _whipPrimed, _handleAxisReady, _handleBack;
        private NQ _shownRotation = NQ.Identity;
        private bool _shownValid;
        private bool _stowHandCue;

        private void ResetWhip()
        {
            _whip.Reset();
            _whipLength = 0f;
            _whipPrimed = false;
            _shownValid = false;
            _handleBack = false;
            _secondary.Reset(_core.Output.Rotation.LengthSquared() > .5f ? _core.Output.Rotation : NQ.Identity);
        }

        /// <summary>Кольцо показанной головы (вторичный поворот у v4) — к нему прибита цепь, его читают VFX.</summary>
        private Vector3 ShownRing => _shownValid
            ? U(_core.Output.Position + System.Numerics.Vector3.Transform(_core.Body.EyeLocal, _shownRotation)) : U(_core.Body.Eye);

        /// <summary>Поворот, который рисуется: у v4 — управляемый + вторичная пружина, иначе — ровно ядро.</summary>
        private NQ ShownRotation => _shownValid ? _shownRotation : _core.Output.Rotation;

        /// <summary>Вторичный поворот этого кадра (после Apply): доля и настройка — по клипу и кадру запечки.</summary>
        private void UpdateSecondary(float dt)
        {
            if (!WreckV4) { _shownValid = false; return; }
            string name = PelagWreckClipRules.ClipName(_wreckClip);
            NQ qc = _core.Output.Rotation;
            if (_core.Mode == AnchorRigMode.OnBack || !_secondary.Ready) _secondary.Reset(qc);
            else _secondary.Step(qc, dt, AnchorRigWreckPlan.SecondaryTune(name, _wreckFrame, _wreckBake.ContactFrame, _wreckBake.Phases));
            float w = _core.Mode == AnchorRigMode.OnBack ? 0f : AnchorRigWreckPlan.SecondaryWeight(name, _wreckFrame, _wreckBake.Phases);
            _shownRotation = _secondary.Show(qc, w);
            _shownValid = true;
        }

        /// <summary>Живой маятник паузы: гашение относительно хвата по возрасту паузы, веретено вдоль цепи.</summary>
        private void ConfigureLive(bool wreck)
        {
            _core.LiveAlignShank = wreck;
            _core.LiveExtraDrag = wreck ? AnchorRigWreckPlan.HangDrag(Time.time - _liveSince) : 0f;
        }

        /// <summary>
        /// Рукоять по запечке: выход цепи — h1, ось — от кисточки h0. Со спины — смешиванием с позой крепления
        /// (HandleWeight); на укладке, когда доля дошла до нуля, рукоять крепится на спину штатно (SlideGrip, 1 кадр).
        /// </summary>
        private void PlaceWreckHandle()
        {
            Transform grip = _equipment.AnchorGrip;
            if (!WreckV4 || grip == null) return;
            string name = PelagWreckClipRules.ClipName(_wreckClip);
            float w = AnchorRigWreckPlan.HandleWeight(name, _wreckFrame, _wreckBake.Phases);
            if (w <= 0f)
            {
                if (_wreckClip == PelagWreckClip.Stow && !_handleBack) { _handleBack = true; _equipment.SlideGrip(toHand: false, 1); }
                return;
            }
            if (!_wreckBake.HasHandle) return;
            if (_gripExitLocal == Vector3.zero) ComputeGripExit();
            if (!_handleAxisReady) ComputeHandleAxis(grip);
            _wreckBake.HandleAt(_wreckFrame, out NV h0, out NV h1);
            Vector3 tassel = _rootPosition + _rootRotation * (U(h0) * _bodyScale);
            Vector3 exit = _rootPosition + _rootRotation * (U(h1) * _bodyScale);
            Vector3 axis = exit - tassel;
            if (axis.sqrMagnitude < 1e-6f) return;
            _equipment.GripBackPose(out Vector3 backPosition, out Quaternion backRotation);
            Quaternion from = w < 1f ? backRotation : grip.rotation;
            Quaternion rotation = Quaternion.FromToRotation(from * _handleAxisLocal, axis.normalized) * from;
            Vector3 position = exit - rotation * Vector3.Scale(grip.lossyScale, _gripExitLocal);
            if (w < 1f)
            {
                position = Vector3.Lerp(backPosition, position, w);
                rotation = Quaternion.Slerp(backRotation, rotation, w);
            }
            grip.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Ось рукояти в её осях: от середины меша к выходу цепи (длинная ось границ).</summary>
        private void ComputeHandleAxis(Transform grip)
        {
            _handleAxisReady = true;
            Vector3 center = Vector3.zero;
            int n = 0;
            foreach (var filter in grip.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                center += grip.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center));
                n++;
            }
            if (n > 0) center /= n;
            Vector3 d = _gripExitLocal - center;
            _handleAxisLocal = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up;
        }

        /// <summary>Цепь-хлыст между выходом из рукояти и кольцом показанной головы; звенья — шагом 0,135 м от кольца.</summary>
        private void DrawWhip(float dt, Vector3 grip)
        {
            Vector3 ring = ShownRing;
            float chord = Vector3.Distance(ring, grip);
            float target = _core.Live ? chord + AnchorRigWreckPlan.HangSlack * _bodyScale
                : _core.Mode == AnchorRigMode.OnBack ? Mathf.Max(AnchorRigWreckPlan.WhipSwing * _bodyScale, chord * 1.1f + .02f)
                : _wreckBake != null && _wreckBake.HasCable ? _wreckBake.CableAt(_wreckFrame) * _bodyScale
                : Mathf.Max(AnchorRigWreckPlan.WhipSwing * _bodyScale, chord + .03f);
            _whipLength = AnchorRigWreckPlan.WhipLength(_whipLength, target, chord, dt, _bodyScale);
            int caps = Mathf.Min(_chainCapsuleCount, _whipCaps.Length - 1);
            for (int i = 0; i < caps; i++) _whipCaps[i] = _chainCapsules[i];
            NV y = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, ShownRotation), p = _core.Output.Position;
            _whipCaps[caps++] = new AnchorCapsule(p - y * (.33f * _bodyScale), p + y * (.2f * _bodyScale), .065f * _bodyScale);
            Vector3 axisWorld = Vector3.zero;
            if (_equipment.AnchorGrip != null && _handleAxisReady) axisWorld = _equipment.AnchorGrip.rotation * _handleAxisLocal;
            if (!_whipPrimed)
            {
                _whip.Seed(N(grip), N(ring), _whipLength);
                for (int k = 0; k < 60; k++)
                    _whip.Step(WhipSubstep * 2f, N(grip), N(ring), N(axisWorld), _whipCaps, caps, _whip.Inside(N(grip), _whipCaps, caps),
                        _whip.Inside(N(ring), _whipCaps, caps), Ground);
                _whipPrimed = true;
                _whipGripPrev = grip; _whipRingPrev = ring;
            }
            uint maskA = _whip.Inside(N(grip), _whipCaps, caps), maskB = _whip.Inside(N(ring), _whipCaps, caps);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / WhipSubstep), 1, WhipMaxSubsteps);
            for (int s = 1; s <= steps; s++)
            {
                float u = s / (float)steps;
                _whip.Length = _whipLength;
                _whip.Step(dt / steps, N(Vector3.Lerp(_whipGripPrev, grip, u)), N(Vector3.Lerp(_whipRingPrev, ring, u)), N(axisWorld),
                    _whipCaps, caps, maskA, maskB, Ground);
            }
            _whipGripPrev = grip; _whipRingPrev = ring;
            _chainVisible = true;
            int count = _whip.Resample(AnchorChainLine.Pitch, _nodes);
            PlaceLinks(count);
            _nodeCount = count;
            _previousRing = ring;
        }
    }
}
