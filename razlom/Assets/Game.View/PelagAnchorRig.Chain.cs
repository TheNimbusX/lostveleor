using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    public sealed partial class PelagAnchorRig
    {
        private const int LinkCapacity = 64;
        private readonly Transform[] _links = new Transform[LinkCapacity];
        private readonly NV[] _nodes = new NV[LinkCapacity + 1];
        private readonly AnchorSlackChain _slack = new AnchorSlackChain();
        private readonly AnchorChainDrawState _draw = new AnchorChainDrawState();
        private Transform _chainRoot;
        private bool _chainVisible;
        private Vector3 _previousRing;
        private bool _stretchLogged;

        // Капсулы тела — один набор с офлайн-запечкой (AnchorRigBody.Build): голове — корпус, кольцо, оболочка против
        // торса, головы, ног и рук; цепи — торс, голова, ноги и плечи (кисти и предплечья держат цепь).
        private readonly AnchorCapsule[] _capsules = new AnchorCapsule[AnchorRigBody.Capacity];
        private readonly AnchorCapsule[] _chainCapsules = new AnchorCapsule[AnchorRigBody.Capacity];
        private int _capsuleCount, _chainCapsuleCount, _nodeCount;
        private Transform _hips, _spine2, _headBone;
        private Transform _lUpLeg, _lLeg, _lFoot, _rUpLeg, _rLeg, _rFoot;
        private Transform _lArm, _lForeArm, _lHand, _rArm, _rForeArm, _rHand;

        private void FindBones()
        {
            foreach (var bone in GetComponentsInChildren<Transform>(true))
                switch (bone.name)
                {
                    case "mixamorig:Hips": _hips = bone; break;
                    case "mixamorig:Spine2": _spine2 = bone; break;
                    case "mixamorig:Head": _headBone = bone; break;
                    case "mixamorig:LeftUpLeg": _lUpLeg = bone; break;
                    case "mixamorig:LeftLeg": _lLeg = bone; break;
                    case "mixamorig:LeftFoot": _lFoot = bone; break;
                    case "mixamorig:RightUpLeg": _rUpLeg = bone; break;
                    case "mixamorig:RightLeg": _rLeg = bone; break;
                    case "mixamorig:RightFoot": _rFoot = bone; break;
                    case "mixamorig:LeftArm": _lArm = bone; break;
                    case "mixamorig:LeftForeArm": _lForeArm = bone; break;
                    case "mixamorig:LeftHand": _lHand = bone; break;
                    case "mixamorig:RightArm": _rArm = bone; break;
                    case "mixamorig:RightForeArm": _rForeArm = bone; break;
                    case "mixamorig:RightHand": _rHand = bone; break;
                }
        }

        private void UpdateCapsules()
        {
            var s = new AnchorSkeleton
            {
                Root = N(transform.position), Hips = B(_hips), Spine2 = B(_spine2), Head = B(_headBone),
                LUpLeg = B(_lUpLeg), LLeg = B(_lLeg), LFoot = B(_lFoot), RUpLeg = B(_rUpLeg), RLeg = B(_rLeg), RFoot = B(_rFoot),
                LArm = B(_lArm), LForeArm = B(_lForeArm), LHand = B(_lHand), RArm = B(_rArm), RForeArm = B(_rForeArm), RHand = B(_rHand),
            };
            AnchorRigBody.Build(s, _bodyScale, _capsules, out _capsuleCount, _chainCapsules, out _chainCapsuleCount);
        }

        private static NV B(Transform bone) => bone != null ? N(bone.position) : AnchorSkeleton.Missing;

        /// <summary>
        /// Правая рука на цепи (DESIGN §4.1): ближайшая точка цепи в первых 0,30 м от рукояти; кулак клипа ближе 6 см —
        /// лёгкий двухкостный IK прижимает его туда (дальше — без IK: это ошибка клипа, её видно в проверке §5 #11).
        /// </summary>
        private void PressSupportHand()
        {
            if (!AnchorRigSwitches.SupportHandOnChain || !_chainVisible || _nodeCount < 2 || _rArm == null || _rForeArm == null || _rHand == null) return;
            Vector3 fist = _equipment.ChainSupportPosition;
            float budget = .30f * _bodyScale, best = float.MaxValue;
            Vector3 target = fist;
            for (int i = _nodeCount - 1; i > 0 && budget > 0f; i--)
            {
                Vector3 a = U(_nodes[i]), b = U(_nodes[i - 1]);
                float len = Vector3.Distance(a, b);
                if (len < 1e-5f) continue;
                if (len > budget) { b = a + (b - a) * (budget / len); len = budget; }
                Vector3 p = a + Vector3.Project(fist - a, b - a);
                if (Vector3.Dot(p - a, b - a) < 0f) p = a; else if ((p - a).sqrMagnitude > len * len) p = b;
                float d = Vector3.Distance(fist, p);
                if (d < best) { best = d; target = p; }
                budget -= len;
            }
            if (best > .06f * _bodyScale || best < .003f) return;
            Vector3 delta = target - fist;
            Vector3 pole = _rForeArm.position - (_rArm.position + _rHand.position) * .5f;
            SolveArm(_rArm, _rForeArm, _rHand, _rHand.position + delta, pole);
        }

        private static void SolveArm(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 shoulder = upper.position, elbow = fore.position;
            float a = Vector3.Distance(shoulder, elbow), b = Vector3.Distance(elbow, hand.position);
            Vector3 delta = target - shoulder;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, (a + b) * .98f);
            if (delta.sqrMagnitude < 1e-6f) return;
            Vector3 direction = delta.normalized;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            Vector3 desiredElbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Quaternion handRotation = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(elbow - shoulder, desiredElbow - shoulder) * upper.rotation;
            fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, shoulder + direction * distance - fore.position) * fore.rotation;
            hand.rotation = handRotation;
        }

        private void PrepareChain()
        {
            if (_equipment == null) return;
            _chainRoot = new GameObject("Anchor rig chain").transform;
            _chainRoot.SetParent(transform, false);
            var mesh = Resources.Load<Mesh>("VFX/Pelag/Geometry/Pelag_ChainLink_Centered");
            for (int i = 0; i < _links.Length; i++)
            {
                var go = new GameObject("Rig link " + i);
                go.transform.SetParent(_chainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _equipment.AnchorMaterial;
                _links[i] = go.transform;
                go.SetActive(false);
            }
            // Прогрев JIT решателя до первого каста.
            _slack.Configure(1.6f);
            _slack.Seed(new NV(0, .4f, 1.5f), new NV(0, 1, 0), NV.Zero, NV.Zero, .02f);
            _slack.Advance(1f / 60f, new NV(0, .4f, 1.5f), new NV(0, .4f, 1.5f), new NV(0, 1, 0), new NV(0, 1, 0), null, _chainCapsules, 0);
        }

        private void HideChain()
        {
            for (int i = 0; i < _links.Length; i++)
                if (_links[i] != null && _links[i].gameObject.activeSelf) _links[i].gameObject.SetActive(false);
            _chainVisible = false;
            _draw.Reset();
        }

        /// <summary>
        /// Натяг (флаг запечки, бросок, |кольцо − хват| ≥ L − 2 см) — прямая со стрелой, без решателя;
        /// провис — один решатель длины L, засеянный этой же прямой. Звенья нумеруются от кольца.
        /// </summary>
        private void DrawChain(float dt, Vector3 grip, in Directive d)
        {
            Vector3 ring = Ring;
            float span = Vector3.Distance(ring, grip);
            bool onBack = _core.Mode == AnchorRigMode.OnBack && span < .25f * _bodyScale && !_equipment.GripInHand;
            if (onBack) { HideChain(); _nodeCount = 0; _previousRing = ring; return; }
            _chainVisible = true;
            bool thrown = _core.Mode == AnchorRigMode.Thrown || _core.Mode == AnchorRigMode.Yank;
            float length = thrown ? span : _core.CableLength;
            bool taut = thrown || d.Taut || span >= length - _core.Settings.TautTolerance;
            float tension = d.Tension > 0 ? d.Tension : _core.Body.LastTension;
            if (thrown) tension = 2000f;
            _draw.Update(taut, span, length, _draw.UseLine ? 0f : _slack.Deviation());
            int count;
            float sag = AnchorChainLine.Sag(span, tension, _core.Settings.Gravity);
            if (_draw.UseLine)
            {
                count = AnchorChainLine.Layout(N(grip), N(ring), sag, _nodes, out bool stretched);
                if (stretched && !_stretchLogged && CaptureRig.LiveSkill) { _stretchLogged = true; Debug.Log($"{Log} chain longer than reserve span={span:F2}"); }
            }
            else
            {
                if (_draw.SeedSolver)
                {
                    _slack.Configure(length);
                    Vector3 ringVelocity = dt > 1e-5f ? (ring - _previousRing) / dt : Vector3.zero;
                    Vector3 gripVelocity = dt > 1e-5f ? (grip - _previousGrip) / dt : Vector3.zero;
                    _slack.Seed(N(ring), N(grip), N(ringVelocity), N(gripVelocity), sag);
                }
                _slack.Advance(dt, N(_previousRing), N(ring), N(_previousGrip), N(grip), Ground, _chainCapsules, _chainCapsuleCount);
                count = Mathf.Min(_slack.Count, _nodes.Length);
                for (int i = 0; i < count; i++) _nodes[i] = _slack[i];
            }
            PlaceLinks(count);
            _nodeCount = count;
            _previousRing = ring;
        }

        private void PlaceLinks(int count)
        {
            float inverseScale = 1f / Mathf.Max(1e-4f, transform.lossyScale.x);
            for (int i = 0; i < _links.Length; i++)
            {
                bool visible = i < count - 1;
                if (visible)
                {
                    Vector3 a = U(_nodes[i]), b = U(_nodes[i + 1]);
                    // Неполное звено у рукояти короче 4 см прячется в намотке, а не сжимается.
                    if (i == count - 2 && (b - a).sqrMagnitude < .04f * .04f) visible = false;
                    else
                    {
                        _links[i].position = (a + b) * .5f;
                        if ((b - a).sqrMagnitude > 1e-8f)
                            _links[i].rotation = Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, 0f, (i & 1) * 90f);
                        _links[i].localScale = Vector3.one * inverseScale;
                    }
                }
                if (_links[i].gameObject.activeSelf != visible) _links[i].gameObject.SetActive(visible);
            }
        }
    }
}
