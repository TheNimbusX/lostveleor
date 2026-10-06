using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Чем голова касается капсулы тела: центром массы, кольцом или оболочкой (68 точек).</summary>
    public enum AnchorCapsuleKind : byte
    {
        /// <summary>Центр массы против капсулы (корпус r 0,38, как старый CollideBody).</summary>
        Center = 0,
        /// <summary>Кольцо против капсулы торса (CollideEyeBody).</summary>
        Ring = 1,
        /// <summary>Оболочка головы против капсулы конечности: выталкивание с гашением нормальной скорости, без трения.</summary>
        Hull = 2,
    }

    /// <summary>Капсула тела героя (мир, метры). Для цепи важны только A, B, Radius.</summary>
    public struct AnchorCapsule
    {
        public Vector3 A, B;
        public float Radius;
        public AnchorCapsuleKind Kind;
        /// <summary>Капсула корпуса: её не видит голова, пока не вышла из крепления спины (<see cref="AnchorRigCore.IgnoreTorso"/>).</summary>
        public bool Trunk;

        public AnchorCapsule(Vector3 a, Vector3 b, float radius, bool ring = false)
            : this(a, b, radius, ring ? AnchorCapsuleKind.Ring : AnchorCapsuleKind.Center, ring) { }

        public AnchorCapsule(Vector3 a, Vector3 b, float radius, AnchorCapsuleKind kind, bool trunk = false)
        {
            A = a; B = b; Radius = radius; Kind = kind; Trunk = trunk || kind != AnchorCapsuleKind.Hull;
        }

        public bool Ring => Kind == AnchorCapsuleKind.Ring;
        public bool Torso => Trunk;

        /// <summary>Расстояние от точки до оси капсулы и нормаль от оси.</summary>
        public float AxisDistance(Vector3 p, out Vector3 normal)
        {
            Vector3 ab = B - A;
            float u = Math.Clamp(Vector3.Dot(p - A, ab) / Math.Max(1e-9f, ab.LengthSquared()), 0f, 1f);
            Vector3 d = p - (A + ab * u);
            float len = d.Length();
            normal = len > 1e-6f ? d / len : Vector3.UnitX;
            return len;
        }
    }

    /// <summary>Кости тела для капсул (мир). Нет кости — <see cref="Missing"/> (капсула не строится).</summary>
    public struct AnchorSkeleton
    {
        public static readonly Vector3 Missing = new Vector3(float.NaN, float.NaN, float.NaN);
        public Vector3 Root, Hips, Spine2, Head;
        public Vector3 LUpLeg, LLeg, LFoot, RUpLeg, RLeg, RFoot;
        public Vector3 LArm, LForeArm, LHand, RArm, RForeArm, RHand;

        public static AnchorSkeleton Empty(Vector3 root) => new AnchorSkeleton
        {
            Root = root, Hips = Missing, Spine2 = Missing, Head = Missing,
            LUpLeg = Missing, LLeg = Missing, LFoot = Missing, RUpLeg = Missing, RLeg = Missing, RFoot = Missing,
            LArm = Missing, LForeArm = Missing, LHand = Missing, RArm = Missing, RForeArm = Missing, RHand = Missing,
        };

        public static bool Has(Vector3 v) => !float.IsNaN(v.X);
    }

    /// <summary>
    /// Тело героя для головы и цепи (DESIGN §1.5 п. 4) — ОДИН набор капсул для игры (PelagAnchorRig) и для офлайн-запечки
    /// (tools/anchorbake): корпус центром r 0,38 (корень +0,65…1,35, как старый CollideBody), кольцо — торс r 0,22,
    /// оболочка головы — торс таз…голова r 0,18 (проверка §5 #2 смотрит в тот же торс), голова r 0,11, бёдра и голени r 0,10,
    /// плечи и предплечья r 0,07. Цепи — торс r 0,20, голова,
    /// ноги и плечи; кисти и предплечья, которые держат цепь, цепь не выталкивают.
    /// </summary>
    public static class AnchorRigBody
    {
        public const int Capacity = 16;
        public const float CenterRadius = .38f, RingTorsoRadius = .22f, ChainTorsoRadius = .20f, HullTorsoRadius = .18f;
        public const float HeadRadius = .11f, ThighRadius = .10f, ShinRadius = .10f, ChainShinRadius = .08f, ArmRadius = .07f;

        public static void Build(in AnchorSkeleton s, float scale, AnchorCapsule[] head, out int headCount,
            AnchorCapsule[] chain, out int chainCount)
        {
            headCount = chainCount = 0;
            Vector3 up = Vector3.UnitY * scale;
            Vector3 bottom = AnchorSkeleton.Has(s.Hips) ? s.Hips + up * .03f : s.Root + up * .91f;
            Vector3 top = AnchorSkeleton.Has(s.Spine2) ? s.Spine2 : s.Root + up * 1.28f;
            Add(head, ref headCount, new AnchorCapsule(s.Root + up * .65f, s.Root + up * 1.35f, CenterRadius * scale, AnchorCapsuleKind.Center));
            Add(head, ref headCount, new AnchorCapsule(bottom, top, RingTorsoRadius * scale, AnchorCapsuleKind.Ring));
            Add(chain, ref chainCount, new AnchorCapsule(bottom, top, ChainTorsoRadius * scale, AnchorCapsuleKind.Center));
            if (AnchorSkeleton.Has(s.Hips))
                Add(head, ref headCount, new AnchorCapsule(s.Hips, AnchorSkeleton.Has(s.Head) ? s.Head : top, HullTorsoRadius * scale, AnchorCapsuleKind.Hull, trunk: true));
            if (AnchorSkeleton.Has(s.Head))
            {
                var c = new AnchorCapsule(s.Head, s.Head + up * .12f, HeadRadius * scale, AnchorCapsuleKind.Hull);
                Add(head, ref headCount, c); Add(chain, ref chainCount, c);
            }
            Limb(head, ref headCount, chain, ref chainCount, s.LUpLeg, s.LLeg, ThighRadius * scale, ThighRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.LLeg, s.LFoot, ShinRadius * scale, ChainShinRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.RUpLeg, s.RLeg, ThighRadius * scale, ThighRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.RLeg, s.RFoot, ShinRadius * scale, ChainShinRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.LArm, s.LForeArm, ArmRadius * scale, ArmRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.RArm, s.RForeArm, ArmRadius * scale, ArmRadius * scale);
            Limb(head, ref headCount, chain, ref chainCount, s.LForeArm, s.LHand, ArmRadius * scale, 0f);
            Limb(head, ref headCount, chain, ref chainCount, s.RForeArm, s.RHand, ArmRadius * scale, 0f);
        }

        private static void Limb(AnchorCapsule[] head, ref int headCount, AnchorCapsule[] chain, ref int chainCount,
            Vector3 a, Vector3 b, float headRadius, float chainRadius)
        {
            if (!AnchorSkeleton.Has(a) || !AnchorSkeleton.Has(b)) return;
            if (headRadius > 0) Add(head, ref headCount, new AnchorCapsule(a, b, headRadius, AnchorCapsuleKind.Hull));
            if (chainRadius > 0) Add(chain, ref chainCount, new AnchorCapsule(a, b, chainRadius, AnchorCapsuleKind.Hull));
        }

        private static void Add(AnchorCapsule[] list, ref int count, in AnchorCapsule c)
        {
            if (list != null && count < list.Length) list[count++] = c;
        }

        /// <summary>Радиус описанной сферы оболочки (широкая фаза: дальняя капсула не проверяется по 68 точкам).</summary>
        public static float HullRadius(Vector3[] hull)
        {
            float r = 0;
            if (hull != null) foreach (var p in hull) r = Math.Max(r, p.Length());
            return r;
        }

        /// <summary>
        /// Оболочка головы против капсулы: глубочайшая точка выталкивается по нормали с поворотом, нормальная скорость
        /// точки гасится импульсом (без трения). Возвращает разрешённую глубину (0 — касания нет).
        /// </summary>
        public static float PushHull(AnchorHeadDynamics h, in AnchorCapsule c, float hullRadius)
        {
            Vector3 n0;
            if (c.AxisDistance(h.Position, out n0) > c.Radius + hullRadius) return 0;
            float best = 0; Vector3 n = default, point = default;
            foreach (var local in h.Hull)
            {
                Vector3 p = h.Position + Vector3.Transform(local, h.Rotation);
                float d = c.Radius - c.AxisDistance(p, out var nn);
                if (d > best) { best = d; n = nn; point = p; }
            }
            if (best <= 0) return 0;
            Vector3 r = point - h.Position, lever = Vector3.Cross(r, n);
            Vector3 turnPerImpulse = InverseInertia(h, lever);
            float inverseMass = 1f / h.Mass + Vector3.Dot(lever, turnPerImpulse);
            float correction = best / inverseMass;
            h.Position += n * (correction / h.Mass);
            Vector3 turn = turnPerImpulse * correction;
            float angle = turn.Length();
            if (angle > .12f) { turn *= .12f / angle; angle = .12f; }
            if (angle > 1e-6f) h.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(turn / angle, angle) * h.Rotation);
            float inward = Vector3.Dot(h.Velocity + Vector3.Cross(h.AngularVelocity, r), n);
            if (inward < 0) h.Impulse(n * (-inward / inverseMass), point);
            return best;
        }

        private static Vector3 InverseInertia(AnchorHeadDynamics h, Vector3 torque)
        {
            var local = Vector3.Transform(torque, Quaternion.Conjugate(h.Rotation));
            return Vector3.Transform(local / h.Inertia, h.Rotation);
        }
    }
}
