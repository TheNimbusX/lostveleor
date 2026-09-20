using Game.Sim;
using UnityEngine;
using Numerics = System.Numerics.Vector3;

namespace Game.View
{
    /// <summary>Единственный владелец головы и выдаваемой цепи во время Удара якорем.</summary>
    [DefaultExecutionOrder(1020)]
    public sealed partial class PelagAnchorSlamView : MonoBehaviour
    {
        private TickDriver _driver;
        private LayoutView _layout;
        private PelagEquipmentView _equipment;
        private readonly AnchorChainSolver _chain = new AnchorChainSolver();
        private readonly AnchorChainSolver _feed = new AnchorChainSolver();
        private readonly AnchorChainSolver _bridge = new AnchorChainSolver();
        private readonly Transform[] _links = new Transform[96];
        private readonly Transform[] _feedLinks = new Transform[24];
        private readonly Transform[] _bridgeLinks = new Transform[16];
        private Transform _ring;
        private Transform _chainRoot;
        private Transform _bodyHips, _bodyChest;
        private Vector3 _startPosition, _forward, _right, _impact;
        private Quaternion _startRotation;
        private Vector3[] _headBounds;
        private int _startTick, _contactTick, _endTick;
        private float _accumulator;
        private Vector3 _previousGrip, _previousRing;
        private float _previousPayout;
        private bool _contact;
        private float _peakStrain, _peakSolveMilliseconds;
        private float _solveTotal;
        private int _solveFrames;
        public bool Active { get; private set; }
        public float ClipTime { get; private set; }
        public float MaxStrain => Mathf.Max(_chain.MaxStrain, Mathf.Max(_feed.MaxStrain, _bridge.MaxStrain));
        public float AttachmentError => _chain.AttachmentError;

        private void Start()
        {
            _driver = FindAnyObjectByType<TickDriver>();
            _layout = FindAnyObjectByType<LayoutView>();
            _chain.GroundHeight = _feed.GroundHeight = _bridge.GroundHeight = SampleFloor;
            _equipment = GetComponent<PelagEquipmentView>();
            foreach (var bone in GetComponentsInChildren<Transform>())
            {
                if (bone.name == "mixamorig:Hips") _bodyHips = bone;
                if (bone.name == "mixamorig:Spine2") _bodyChest = bone;
            }
            PrepareWeightTrail();
            var filters = _equipment.SlamHead.GetComponentsInChildren<MeshFilter>();
            _headBounds = new Vector3[filters.Length * 8];
            for (int m = 0; m < filters.Length; m++)
            {
                Bounds b = filters[m].sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    _headBounds[m * 8 + c] = _equipment.SlamHead.InverseTransformPoint(filters[m].transform.TransformPoint(corner));
                }
            }
            _chainRoot = new GameObject("Anchor slam chain reserve").transform;
            _chainRoot.SetParent(transform, false);
            var mesh = Resources.Load<Mesh>("VFX/Pelag/Geometry/Pelag_ChainLink_Centered");
            _ring = new GameObject("Anchor chain ring").transform;
            _ring.SetParent(_equipment.SlamHead, false);
            _ring.localPosition = Vector3.up * (.035f / _equipment.SlamHead.lossyScale.x);
            _ring.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            _ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _equipment.AnchorMaterial;
            _ring.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            _ring.localScale = Vector3.one * (.72f / _equipment.SlamHead.lossyScale.x);
            _ring.gameObject.SetActive(false);
            for (int i = 0; i < _links.Length; i++)
            {
                var go = new GameObject("Slam link " + i);
                go.transform.SetParent(_chainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _equipment.AnchorMaterial;
                _links[i] = go.transform;
                go.SetActive(false);
            }
            for (int i = 0; i < _feedLinks.Length; i++)
            {
                var go = new GameObject("Belt feed link " + i);
                go.transform.SetParent(_chainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _equipment.AnchorMaterial;
                _feedLinks[i] = go.transform;
                go.SetActive(false);
            }
            for (int i = 0; i < _bridgeLinks.Length; i++)
            {
                var go = new GameObject("Hand bridge link " + i);
                go.transform.SetParent(_chainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _equipment.AnchorMaterial;
                _bridgeLinks[i] = go.transform;
                go.SetActive(false);
            }
            // JIT и ветви выдачи прогреваются при создании героя, до первого каста.
            foreach (var solver in new[] { _chain, _feed, _bridge })
            {
                for (int i = 0; i < 4; i++)
                    solver.Advance(new Numerics(0, 1, 0), new Numerics(0, .4f, 3),
                        3.4f + i * .08f, 0, new Numerics(-5, 1, 0), new Numerics(-5, 2, 0), .2f, true);
                solver.Clear();
            }
        }

        public void Begin()
        {
            if (_driver == null || _equipment == null || _equipment.SlamHead == null) return;
            var sim = _driver.Sim;
            if (sim == null || !sim.AnchorSlamActive) return;
            Vector3 inheritedPosition = _equipment.SlamHead.position;
            Quaternion inheritedRotation = _equipment.SlamHead.rotation;
            Release(true);
            _wreck = false;
            int windup = 15;
            float range = 4.5f;
            for (int i = 0; i < Simulation.AbilitySlots; i++)
            {
                var build = sim.GetAbility(i);
                if (build == null || build.DefinitionId != AbilityDefinition.AnchorSlamId) continue;
                windup = sim.AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt());
                range = (float)build.Get(AbilityStatType.Radius).ToDouble();
                break;
            }
            _contactTick = sim.AnchorSlamImpactTick;
            _startTick = _contactTick - windup;
            _endTick = sim.AnchorSlamEndTick;
            _forward = new Vector3((float)sim.AnchorSlamDirection.X.ToDouble(), 0,
                (float)sim.AnchorSlamDirection.Y.ToDouble());
            _right = Vector3.Cross(Vector3.up, _forward);
            _impact = new Vector3((float)sim.AnchorSlamOrigin.X.ToDouble(), transform.position.y,
                (float)sim.AnchorSlamOrigin.Y.ToDouble()) + _forward * range;
            if (Physics.Raycast(_impact + Vector3.up * 3f, Vector3.down, out var hit, 6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                _impact.y = hit.point.y;
            if (_layout != null) _impact.y = Mathf.Max(_impact.y, _layout.WeaponGroundHeight(_impact.x, _impact.z));
            if (CaptureRig.LiveSkill) Debug.Log($"[anchor-ground] impact={_impact.ToString("F4")}");
            _startPosition = inheritedPosition;
            _startRotation = inheritedRotation;
            _equipment.SetSlamOwnership(true);
            _ring.gameObject.SetActive(true);
            Active = true; _contact = false; _accumulator = 0; ClipTime = 0;
            _peakStrain = _peakSolveMilliseconds = 0;
            _solveTotal = 0; _solveFrames = 0;
            _previousGrip = _equipment.ChainGripPosition;
            _previousRing = _ring.position;
            _previousPayout = 1.05f;
            BeginHeadPhysics();
            if (CaptureRig.LiveSkill)
                foreach (var renderer in _equipment.SlamHead.GetComponentsInChildren<Renderer>(true))
                    Debug.Log($"[anchor-head] active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} bounds={renderer.bounds} scale={_equipment.SlamHead.lossyScale} local={renderer.transform.localEulerAngles}");
        }

        public float SampleClipTime()
        {
            if (!Active || _driver == null) return 0;
            float tick = _driver.Sim.Tick - 1 + _driver.Alpha;
            return tick <= _contactTick
                ? .5f * Mathf.InverseLerp(_startTick, _contactTick, tick)
                : Mathf.Lerp(.5f, .9f, Mathf.InverseLerp(_contactTick, _endTick, tick));
        }

        private void LateUpdate()
        {
            if (!Active) return;
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null || !sim.Entities.Alive[0]) { Release(true); return; }
            if (_driver.GameplayPaused) return;
            bool valid = _wreck ? sim.PlayerAction.Serial == _weaponSerial && !sim.PlayerAction.Interrupted
                && sim.PlayerAction.DefinitionId == AbilityDefinition.WreckId && sim.Tick <= _endTick
                : sim.AnchorSlamActive && sim.AnchorSlamEndTick == _endTick;
            if (!valid && !_returning) Release();
            if (_returning)
            {
                _returnAge += Time.deltaTime;
                if (_physicalSlam)
                {
                    if (ReturnPhysicalHead()) { Release(true); return; }
                }
                else
                {
                float blend = Smooth(_returnAge / .14f);
                _equipment.SlamHead.SetPositionAndRotation(Vector3.Lerp(_returnPosition, _equipment.SlamBeltPosition, blend),
                    Quaternion.Slerp(_returnRotation, _equipment.SlamBeltRotation, blend));
                if (_returnAge >= .14f) { Release(true); return; }
                }
            }
            else ClipTime = SampleClipTime();
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.AnchorSlamImpact)
                { _contact = true; ClipTime = Mathf.Max(.5f, ClipTime); }
            if (_wreck && sim.Tick > _contactTick) _contact = true;
            if (!_contact) ClipTime = Mathf.Min(.5f, ClipTime);
            if (!_returning) { if (_wreck) PoseWreck(ClipTime); else PosePhysicalHead(ClipTime); }
            UpdateWeightTrail();
            Vector3 grip = _returning ? _equipment.SlamBeltPosition : _equipment.ChainGripPosition;
            Vector3 ring = _ring.position;
            Vector3 support = _equipment.ChainSupportPosition;
            float bridgePayout = _physicalSlam ? 1.05f : ClipTime < .32f
                ? Mathf.Lerp(1.12f, .80f, Smooth(ClipTime / .09f))
                : Mathf.Lerp(.80f, .52f, Smooth((ClipTime - .32f) / .16f));
            float feedPayout = _physicalSlam ? Mathf.Lerp(1.30f,1.18f,Smooth(ClipTime/.2f))
                : Mathf.Lerp(1.05f, 1.15f, Smooth(ClipTime / .3f));
            float payout = _returning ? Mathf.Lerp(_returnPayout, .75f, Smooth(_returnAge / (_physicalSlam ? .5f : .14f)))
                : _wreck ? WreckPayout(ClipTime) : PhysicalPayout(ClipTime);
            float floor = Mathf.Min(transform.position.y, _impact.y);
            Vector3 bottom = _bodyHips != null ? _bodyHips.position + Vector3.up*.03f : transform.position + Vector3.up*.91f;
            Vector3 top = _bodyChest != null ? _bodyChest.position : transform.position + Vector3.up*1.28f;
            Vector3 belt = (_bodyHips != null ? _bodyHips.position - Vector3.up*.15f : transform.position + Vector3.up*.83f)
                - transform.right*.32f + transform.forward*.04f;
            long solveStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, .1f);
            while (_accumulator >= AnchorChainSolver.Step)
            {
                float alpha = Mathf.Clamp01(1f - (_accumulator - AnchorChainSolver.Step) / Mathf.Max(.0001f, Time.deltaTime));
                _chain.Advance(N(Vector3.Lerp(_previousGrip, grip, alpha)),
                    N(Vector3.Lerp(_previousRing, ring, alpha)), Mathf.Lerp(_previousPayout, payout, alpha),
                    floor, N(bottom), N(top), .20f, true);
                if (!_returning) _feed.Advance(N(belt), N(support), feedPayout,
                    floor, N(bottom), N(top), .20f, true);
                if (!_returning) _bridge.Advance(N(support), N(grip), bridgePayout, floor, N(bottom), N(top), .20f, true);
                _accumulator -= AnchorChainSolver.Step;
            }
            _chain.Advance(N(grip), N(ring), payout, floor, N(bottom), N(top), .20f, false);
            if (!_returning)
            {
                _feed.Advance(N(belt), N(support), feedPayout, floor, N(bottom), N(top), .20f, false);
                _bridge.Advance(N(support), N(grip), bridgePayout, floor, N(bottom), N(top), .20f, false);
            }
            else { _feed.Clear(); _bridge.Clear(); }
            _peakStrain = Mathf.Max(_peakStrain, MaxStrain);
            float solveMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - solveStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _peakSolveMilliseconds = Mathf.Max(_peakSolveMilliseconds, solveMilliseconds);
            _solveTotal += solveMilliseconds; _solveFrames++;
            _previousGrip = grip; _previousRing = ring; _previousPayout = payout;
            for (int i = 0; i < _links.Length; i++)
            {
                // Неполное звено выходит из запаса, но само не масштабируется.
                bool visible = i < _chain.Count - 1 && (i != 0 || _chain.SegmentLength(0) > .04f);
                _links[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 a = U(_chain[i]), b = U(_chain[i + 1]);
                _links[i].position = (a + b) * .5f;
                Vector3 direction = b - a;
                if (direction.sqrMagnitude > .000001f)
                    _links[i].rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 0, ((_chain.Count - 2 - i) & 1) * 90);
                _links[i].localScale = Vector3.one / transform.lossyScale.x;
            }
            for (int i = 0; i < _feedLinks.Length; i++)
            {
                bool visible = i < _feed.Count - 1;
                _feedLinks[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 a = U(_feed[i]), b = U(_feed[i + 1]);
                _feedLinks[i].position = (a + b) * .5f;
                if ((b - a).sqrMagnitude > .000001f)
                    _feedLinks[i].rotation = Quaternion.LookRotation(b - a) * Quaternion.Euler(0, 0, (i & 1) * 90);
                _feedLinks[i].localScale = Vector3.one / transform.lossyScale.x;
            }
            if (CaptureRig.LiveSkill)
                Debug.Log($"[anchor-slam] tick={sim.Tick} time={ClipTime:F4} strain={MaxStrain:F5} flight={_chain.MaxStrain:F5} feed={_feed.MaxStrain:F5} bridge={_bridge.MaxStrain:F5} feedSpan={Vector3.Distance(belt,support):F3} feedLength={feedPayout:F3} grip={AttachmentError:F6} span={Vector3.Distance(grip,ring):F3} payout={payout:F3} head={ring.ToString("F3")} hand={grip.ToString("F3")} solveMs={solveMilliseconds:F3}");
            DrawBridge();
        }

        private void DrawBridge()
        {
            for (int i = 0; i < _bridgeLinks.Length; i++)
            {
                bool visible = i < _bridge.Count - 1;
                _bridgeLinks[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 a = U(_bridge[i]), b = U(_bridge[i + 1]);
                _bridgeLinks[i].position = (a + b) * .5f;
                if ((b-a).sqrMagnitude > .000001f)
                    _bridgeLinks[i].rotation = Quaternion.LookRotation(b-a) * Quaternion.Euler(0,0,(i&1)*90);
                _bridgeLinks[i].localScale = Vector3.one / transform.lossyScale.x;
            }
        }

        public void Release(bool immediate = false)
        {
            StopWeightTrail();
            if (Active && !immediate)
            {
                if (!_returning)
                {
                    _returning = true; _returnAge = 0f;
                    _returnPosition = _equipment.SlamHead.position; _returnRotation = _equipment.SlamHead.rotation;
                    // При передаче из ладони к поясу используем запас уже выданной цепи.
                    // Длину выбираем однократно: новый наклон корпуса при уходе не растягивает звенья.
                    _returnPayout = Mathf.Min(7.5f, Mathf.Max(_previousPayout,
                        Vector3.Distance(_equipment.SlamBeltPosition, _ring.position) + .65f));
                    _equipment.ReleaseSlamHands();
                }
                return;
            }
            if (Active && CaptureRig.LiveSkill)
                Debug.Log($"[anchor-release] tick={(_driver != null && _driver.Sim != null ? _driver.Sim.Tick : -1)} end={_endTick} contact={_contact} peakStrain={_peakStrain:F5} peakSolveMs={_peakSolveMilliseconds:F3} meanSolveMs={_solveTotal / Mathf.Max(1, _solveFrames):F3} phase={ClipTime:F4} handoff={Vector3.Distance(_equipment.SlamHead.position, _equipment.SlamBeltPosition):F5}");
            if (Active && _equipment != null) _equipment.SetSlamOwnership(false);
            Active = false; _returning = false; _physicalSlam = false; _chain.Clear(); _feed.Clear(); _bridge.Clear(); _accumulator = 0;
            if (_ring != null) _ring.gameObject.SetActive(false);
            for (int i = 0; i < _links.Length; i++)
                if (_links[i] != null) _links[i].gameObject.SetActive(false);
            for (int i = 0; i < _feedLinks.Length; i++)
                if (_feedLinks[i] != null) _feedLinks[i].gameObject.SetActive(false);
            for (int i = 0; i < _bridgeLinks.Length; i++)
                if (_bridgeLinks[i] != null) _bridgeLinks[i].gameObject.SetActive(false);
        }
        private void OnDisable() => Release(true);
        private void OnDestroy() { if (_weightTrailMaterial != null) Destroy(_weightTrailMaterial); }
        private float SampleFloor(Numerics p) => _layout != null ? _layout.WeaponGroundHeight(p.X, p.Z) : transform.position.y;
        private static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        private static Numerics N(Vector3 p) => new Numerics(p.x, p.y, p.z);
        private static Vector3 U(Numerics p) => new Vector3(p.X, p.Y, p.Z);
    }
}
