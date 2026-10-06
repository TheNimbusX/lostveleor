using Game.Sim;
using UnityEngine;
using NV = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

namespace Game.View
{
    /// <summary>Переключатели рига (DESIGN §7). Где владелец уже принял показ — по умолчанию прежний путь.</summary>
    public static class AnchorRigSwitches
    {
        /// <summary>Крушение на риге, если есть снимок Sim и запечки Swing1/Swing2/Slam; иначе прежний PelagAnchorSlamView.BeginWreck.</summary>
        public static bool Wreck = true;
        /// <summary>
        /// Абордаж: false (по умолчанию) — прежний путь, принятый владельцем (ReturnFlyingAnchor, на спину за 0,14 с).
        /// true — возврат головы ловит риг (Caught → Stow ≤ 0,25 с). Остальной Абордаж риг не трогает (External).
        /// </summary>
        public static bool AbordageReturn = false;
        /// <summary>Сабля и рукоять переходят между сокетами скольжением за столько кадров (DESIGN §4.3).</summary>
        public static int PropSlideFrames = 2;
        /// <summary>Рукоять на спину без кисти у крепления (клипа Stow ещё нет): скольжение за столько секунд, а не за 2 кадра.</summary>
        public static float StowSlideSeconds = .22f;
        /// <summary>Правый кулак прижимается к цепи у рукояти, если клип держит его ближе 6 см (DESIGN §4.1).</summary>
        public static bool SupportHandOnChain = true;
        /// <summary>Бросок якоря на риге (DESIGN §7.3): действует, только когда положен PelagAnchorRig.ThrowSnapshot.cs (Sim Броска влит).</summary>
        public static bool AnchorThrow = true;
    }

    /// <summary>
    /// Один владелец головы якоря и цепи на навыках с якорем (DESIGN §1). Голова — та же «Stored anchor head»
    /// (SlamHead), других копий риг не создаёт. Удары — запечённый путь по часам Sim, всё остальное —
    /// живая физика без пружин; стыки режимов — по положению и скорости (<see cref="AnchorRigCore"/>).
    /// Порядок 1021: после аниматора, FootPlant 1000, Equipment 1005, VFX 1010 и старого PelagAnchorSlamView 1020.
    /// </summary>
    [DefaultExecutionOrder(1021)]
    [DisallowMultipleComponent]
    public sealed partial class PelagAnchorRig : MonoBehaviour
    {
        private const string Log = "[anchor-rig]";
        private readonly AnchorRigCore _core = new AnchorRigCore();
        private TickDriver _driver;
        private LayoutView _layout;
        private PelagEquipmentView _equipment;
        private PelagAnchorSlamView _slam;
        private CharacterAnimatorView _presentation;
        private Animator _animator;
        private PelagVfxController _vfx;
        private AnchorBakeLibrary _library;
        private bool _started, _owned, _geometry;
        private Vector3 _headCenterLocal, _gripExitLocal;
        private Vector3 _socketLocal = new Vector3(0f, .045f, .012f);
        private bool _socketMeters;
        private Vector3 _previousRoot, _previousGrip, _previousBackCenter;
        private Quaternion _previousRootRotation = Quaternion.identity;
        private bool _hasPrevious;
        private Vector3 _rootPosition, _rootVelocity, _rootAngularVelocity;
        private Quaternion _rootRotation = Quaternion.identity;
        private float _bodyScale = 1f;
        private float _shown;

        public bool Owning => _owned;
        public AnchorRigMode Mode => _core.Mode;
        /// <summary>Точка выхода цепи из рукояти и кольцо головы этого кадра (для VFX, как ChainGrip/Ring у Абордажа).</summary>
        public Vector3 ChainGrip { get; private set; }
        public Vector3 Ring => U(_core.Body.Eye);
        public Vector3 HeadVelocity => U(_core.Output.Velocity);

        private void Start()
        {
            _driver = FindAnyObjectByType<TickDriver>();
            _layout = FindAnyObjectByType<LayoutView>();
            _equipment = GetComponent<PelagEquipmentView>();
            _slam = GetComponent<PelagAnchorSlamView>();
            _presentation = GetComponent<CharacterAnimatorView>();
            _animator = GetComponentInChildren<Animator>();
            _vfx = FindAnyObjectByType<PelagVfxController>();
            _library = AnchorBakeLibrary.Load();
            LoadGripSocket();
            FindBones();
            PrepareChain();
            _core.MarkExternal();
            _started = _equipment != null && _equipment.SlamHead != null;
            if (CaptureRig.LiveSkill) Debug.Log($"{Log} ready bakes={_library.Count} socketMeters={_socketMeters} started={_started}");
        }

        /// <summary>
        /// grip_socket.json v2 (тот же файл, что читает anchor_grip_export.py): кольцо рукояти в осях LeftHand, метры.
        /// Нет файла или не v2 — прежняя точка ладони (0; 0,045; 0,012) в единицах кости, это пишется в лог.
        /// </summary>
        private void LoadGripSocket()
        {
            var asset = Resources.Load<TextAsset>(AnchorBakeLibrary.Folder + "/grip_socket");
            if (asset == null) return;
            var data = JsonUtility.FromJson<AnchorGripSocketData>(asset.text);
            if (data == null || !data.Valid)
            {
                Debug.LogWarning($"{Log} grip_socket.json не v2 (метры): точка ладони прежняя");
                return;
            }
            _socketLocal = new Vector3(data.position[0], data.position[1], data.position[2]);
            _socketMeters = true;
        }

        /// <summary>Голова: центр массы, кольцо и оболочка в осях SlamHead с мировым масштабом (как BeginHeadPhysics).</summary>
        private void EnsureGeometry()
        {
            Transform head = _equipment.SlamHead;
            Vector3 scale = head.lossyScale;
            var filters = head.GetComponentsInChildren<MeshFilter>(true);
            var corners = new Vector3[Mathf.Max(1, filters.Length * 8)];
            Vector3 center = Vector3.zero;
            int n = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    corners[n] = Vector3.Scale(head.InverseTransformPoint(filter.transform.TransformPoint(corner)), scale);
                    center += corners[n++];
                }
            }
            _headCenterLocal = n > 0 ? center / n : Vector3.zero;
            Transform attachment = null;
            foreach (var t in head.GetComponentsInChildren<Transform>(true)) if (t.name == "Anchor_Attachment") attachment = t;
            Vector3 eye = attachment != null ? Vector3.Scale(head.InverseTransformPoint(attachment.position), scale)
                : _headCenterLocal + Vector3.up * (.38f * _bodyScale);
            _core.Body.EyeLocal = N(eye - _headCenterLocal);
            Vector3[] points = _equipment.AnchorShape != null && _equipment.AnchorShape.Points != null ? _equipment.AnchorShape.Points : null;
            int count = points != null ? points.Length : n;
            var hull = new NV[count];
            for (int i = 0; i < count; i++)
                hull[i] = N((points != null ? Vector3.Scale(points[i], scale) : corners[i]) - _headCenterLocal);
            _core.Body.Hull = hull;
            _core.Settings.ChainLength = 1.60f * _bodyScale;
            _geometry = true;
        }

        private void SampleRoot(float dt)
        {
            _bodyScale = transform.lossyScale.y / 1.82f;
            _rootPosition = transform.position;
            Vector3 forward = transform.forward; forward.y = 0f;
            _rootRotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            if (_hasPrevious && dt > 1e-5f)
            {
                _rootVelocity = (_rootPosition - _previousRoot) / dt;
                (_rootRotation * Quaternion.Inverse(_previousRootRotation)).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                _rootAngularVelocity = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            }
            else { _rootVelocity = Vector3.zero; _rootAngularVelocity = Vector3.zero; }
            _previousRoot = _rootPosition; _previousRootRotation = _rootRotation;
        }

        /// <summary>Выход цепи — точка рукояти в её же осях: в руке, на спине и в скольжении одна и та же (без скачка хвата).</summary>
        private Vector3 GripWorld()
        {
            Transform grip = _equipment.AnchorGrip;
            if (grip == null) return _equipment.ChainGripPosition;
            if (_gripExitLocal == Vector3.zero) ComputeGripExit();
            return grip.TransformPoint(_gripExitLocal);
        }

        private void ComputeGripExit()
        {
            var mount = _equipment.AnchorEquippedMount;
            Transform hand = mount.Socket;
            if (hand == null) return;
            Vector3 handLocal = _socketMeters
                ? new Vector3(_socketLocal.x / Mathf.Max(1e-5f, hand.lossyScale.x), _socketLocal.y / Mathf.Max(1e-5f, hand.lossyScale.y),
                    _socketLocal.z / Mathf.Max(1e-5f, hand.lossyScale.z))
                : _socketLocal;
            Matrix4x4 trs = Matrix4x4.TRS(mount.LocalPosition, Quaternion.Euler(mount.LocalEuler), mount.LocalScale);
            _gripExitLocal = trs.inverse.MultiplyPoint3x4(handLocal);
            if (_gripExitLocal == Vector3.zero) _gripExitLocal = new Vector3(0f, 0f, 1e-4f);
        }

        private AnchorPose BackTarget(float dt)
        {
            Quaternion rotation = _equipment.SlamBeltRotation;
            Vector3 center = _equipment.SlamBeltPosition + rotation * _headCenterLocal;
            Vector3 velocity = _hasPrevious && dt > 1e-5f ? (center - _previousBackCenter) / dt : Vector3.zero;
            _previousBackCenter = center;
            return new AnchorPose(N(center), NQ_(rotation), N(velocity), NV.Zero);
        }

        private AnchorPose CurrentHeadPose()
        {
            Transform head = _equipment.SlamHead;
            return AnchorPose.At(N(head.position + head.rotation * _headCenterLocal), NQ_(head.rotation));
        }

        private void WriteHead()
        {
            Quaternion rotation = UQ(_core.Output.Rotation);
            _equipment.SlamHead.SetPositionAndRotation(U(_core.Output.Position) - rotation * _headCenterLocal, rotation);
        }

        private float Ground(NV p) => _layout != null ? _layout.WeaponGroundHeight(p.X, p.Z) : transform.position.y;

        private static NV N(Vector3 v) => new NV(v.x, v.y, v.z);
        private static Vector3 U(NV v) => new Vector3(v.X, v.Y, v.Z);
        private static NQ NQ_(Quaternion q) => new NQ(q.x, q.y, q.z, q.w);
        private static Quaternion UQ(NQ q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
