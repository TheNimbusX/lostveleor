using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Положение головы якоря: центр массы, поворот, линейная и угловая скорость (мир, м и м/с).</summary>
    public struct AnchorPose
    {
        public Vector3 Position, Velocity, AngularVelocity;
        public Quaternion Rotation;

        public AnchorPose(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            Position = position; Rotation = rotation; Velocity = velocity; AngularVelocity = angularVelocity;
        }

        public static AnchorPose At(Vector3 position, Quaternion rotation)
            => new AnchorPose(position, rotation, Vector3.Zero, Vector3.Zero);
    }

    /// <summary>
    /// Сшивка режимов рига (DESIGN §1.3): на стыке голова сохраняет положение, поворот и обе скорости,
    /// а разница с новой целью гаснет критически задемпфированным звеном второго порядка
    /// e'' = −ω²e − 2ωe'. Это не Lerp позиции: смещение затухает со своей скоростью,
    /// и голова наследует скорость прежнего режима. Решение точное (замкнутая форма), шаг кадра не важен.
    /// ω ограничено так, чтобы ускорение поправки не превышало <see cref="MaxAccel"/> (проверка §5 #8: 400 м/с²):
    /// метровая ошибка стыка гаснет медленнее, а не «защёлкивается» за 2 тика; чего не успели к сроку — в лог.
    /// Исключения §5 #8 — бросок (выпуск и рывок — это и есть удар руки) и толчок натяга: Start(limitAccel: false), Kick.
    /// </summary>
    public sealed class AnchorBlend
    {
        /// <summary>ω по умолчанию: 2π / 4 тика при 30 Гц ≈ 47 рад/с (DESIGN §1.3).</summary>
        public const float DefaultOmega = 2f * (float)Math.PI * 30f / 4f;
        public const float MaxOmega = 260f;
        /// <summary>Предел ускорения поправки стыка, м/с² (§5 #8). Толчок броска (Kick) — исключение.</summary>
        public const float MaxAccel = 400f;

        private Vector3 _p0, _v0, _r0, _w0;
        private float _age;
        private bool _limited = true;
        public float Omega { get; private set; } = DefaultOmega;

        public Vector3 Offset { get; private set; }
        public Vector3 OffsetVelocity { get; private set; }
        public Vector3 TurnOffset { get; private set; }
        public Vector3 TurnVelocity { get; private set; }
        public float Magnitude => Offset.Length();
        /// <summary>Ускорение смещения сейчас, м/с².</summary>
        public float Acceleration { get; private set; }
        /// <summary>Остаток к сроку, если предел ускорения не дал успеть (0 — успевает).</summary>
        public float DeadlineResidual { get; private set; }

        public void Clear()
        {
            _p0 = _v0 = _r0 = _w0 = Vector3.Zero; _age = 0;
            Offset = OffsetVelocity = TurnOffset = TurnVelocity = Vector3.Zero;
            Omega = DefaultOmega; Acceleration = 0; DeadlineResidual = 0;
        }

        /// <summary>Начать сшивку: текущее показанное состояние <paramref name="from"/> против новой цели <paramref name="to"/>.</summary>
        public void Start(in AnchorPose from, in AnchorPose to, float omega = DefaultOmega, bool limitAccel = true)
        {
            _limited = limitAccel;
            _p0 = from.Position - to.Position;
            _v0 = from.Velocity - to.Velocity;
            _r0 = RotationVector(Quaternion.Normalize(from.Rotation * Quaternion.Conjugate(to.Rotation)));
            _w0 = from.AngularVelocity - to.AngularVelocity;
            _age = 0; DeadlineResidual = 0;
            Omega = _limited ? LimitOmega(_p0, _v0, Math.Clamp(omega, 1f, MaxOmega)) : Math.Clamp(omega, 1f, MaxOmega);
            Evaluate();
        }

        /// <summary>
        /// Поднять ω так, чтобы смещение стало меньше <paramref name="tolerance"/> к сроку (тик контакта − 1).
        /// Ниже текущего ω не опускается; выше <see cref="MaxOmega"/> и выше предела ускорения не поднимается —
        /// тогда <see cref="DeadlineResidual"/> &gt; 0 (контакт мимо, это пишется в лог, а не прячется рывком).
        /// </summary>
        public void MeetDeadline(float secondsLeft, float tolerance = .02f)
        {
            if (secondsLeft <= 0) return;
            Vector3 p = Offset, v = OffsetVelocity;
            float omega = Omega;
            while (omega < MaxOmega && Residual(p, v, omega, secondsLeft) > tolerance)
            {
                float next = Math.Min(MaxOmega, omega * 1.12f);
                if (_limited && PeakAccel(p, v, next) > MaxAccel) break;
                omega = next;
            }
            float left = Residual(p, v, omega, secondsLeft);
            DeadlineResidual = left > tolerance ? left : 0f;
            if (omega <= Omega) return;
            // Пересев с тем же состоянием: положение и скорость стыка не меняются.
            _p0 = p; _v0 = v; _r0 = TurnOffset; _w0 = TurnVelocity; _age = 0;
            Omega = omega;
            Evaluate();
        }

        /// <summary>Наибольшее ускорение смещения на всём затухании из (p, v) при ω (численно, 32 точки до 8/ω).</summary>
        public static float PeakAccel(Vector3 p, Vector3 v, float omega)
        {
            Vector3 b = v + omega * p;
            float peak = 0;
            for (int k = 0; k <= 32; k++)
            {
                float t = k * (8f / 32f) / omega;
                Vector3 a = (-2f * omega * b + omega * omega * (p + b * t)) * (float)Math.Exp(-omega * t);
                peak = Math.Max(peak, a.Length());
            }
            return peak;
        }

        /// <summary>Наибольшее ω ≤ <paramref name="omega"/>, при котором поправка не превышает <see cref="MaxAccel"/>.</summary>
        public static float LimitOmega(Vector3 p, Vector3 v, float omega)
        {
            if (PeakAccel(p, v, omega) <= MaxAccel) return omega;
            float lo = 1f, hi = omega;
            for (int i = 0; i < 24; i++)
            {
                float mid = .5f * (lo + hi);
                if (PeakAccel(p, v, mid) <= MaxAccel) lo = mid; else hi = mid;
            }
            return lo;
        }

        public static float Residual(Vector3 p, Vector3 v, float omega, float t)
        {
            float decay = (float)Math.Exp(-omega * t);
            return ((p + (v + omega * p) * t) * decay).Length();
        }

        /// <summary>Добавить скорость к смещению (дёрг натянутой цепи в конце броска).</summary>
        public void Kick(Vector3 velocity)
        {
            _p0 = Offset; _v0 = OffsetVelocity + velocity; _r0 = TurnOffset; _w0 = TurnVelocity; _age = 0;
            Evaluate();
        }

        /// <summary>Скорость толчка, после которого смещение достигнет <paramref name="amplitude"/> (пик e(t) при e0 = 0).</summary>
        public static float KickSpeedFor(float amplitude, float omega) => amplitude * omega * (float)Math.E;

        public void Advance(float dt)
        {
            if (dt <= 0) return;
            _age += dt;
            Evaluate();
        }

        private void Evaluate()
        {
            float w = Omega, t = _age, decay = (float)Math.Exp(-w * t);
            Vector3 b = _v0 + w * _p0;
            Offset = (_p0 + b * t) * decay;
            OffsetVelocity = (_v0 - w * b * t) * decay;
            Acceleration = ((-2f * w * b + w * w * (_p0 + b * t)) * decay).Length();
            Vector3 rb = _w0 + w * _r0;
            TurnOffset = (_r0 + rb * t) * decay;
            TurnVelocity = (_w0 - w * rb * t) * decay;
        }

        /// <summary>Показанное состояние = цель + затухающее смещение (положение, скорость, поворот, угловая скорость).</summary>
        public AnchorPose Apply(in AnchorPose target)
        {
            return new AnchorPose(
                target.Position + Offset,
                Quaternion.Normalize(FromRotationVector(TurnOffset) * target.Rotation),
                target.Velocity + OffsetVelocity,
                target.AngularVelocity + TurnVelocity);
        }

        public static Vector3 RotationVector(Quaternion q)
        {
            if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            Vector3 axis = new Vector3(q.X, q.Y, q.Z);
            float s = axis.Length();
            if (s < 1e-7f) return axis * 2f;
            float angle = 2f * (float)Math.Atan2(s, q.W);
            return axis / s * angle;
        }

        public static Quaternion FromRotationVector(Vector3 r)
        {
            float angle = r.Length();
            return angle < 1e-7f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(r / angle, angle);
        }
    }

    /// <summary>Мелкая геометрия рига без Unity.</summary>
    public static class AnchorRigMath
    {
        /// <summary>Пятистепенный smoothstep: h, h′, h″ = 0 на концах (поправка точки удара, DESIGN §2.3).</summary>
        public static float Quintic(float u)
        {
            u = Math.Clamp(u, 0f, 1f);
            return u * u * u * (u * (u * 6f - 15f) + 10f);
        }

        /// <summary>Производная <see cref="Quintic"/> по u.</summary>
        public static float QuinticSlope(float u)
        {
            if (u <= 0f || u >= 1f) return 0f;
            return 30f * u * u * (u - 1f) * (u - 1f);
        }

        /// <summary>
        /// Поворот, который переводит локальную ось <paramref name="localAxis"/> в мировое направление
        /// <paramref name="worldAxis"/>, а локальную <paramref name="localSecondary"/> — как можно ближе к <paramref name="worldHint"/>.
        /// </summary>
        public static Quaternion Align(Vector3 localAxis, Vector3 worldAxis, Vector3 localSecondary, Vector3 worldHint)
        {
            Vector3 a = SafeNormalize(localAxis, Vector3.UnitY), b = SafeNormalize(worldAxis, Vector3.UnitY);
            Vector3 la = SafeNormalize(Vector3.Cross(a, localSecondary), Vector3.UnitX);
            Vector3 lb = Vector3.Cross(la, a);
            Vector3 wa = SafeNormalize(Vector3.Cross(b, worldHint), Math.Abs(b.Y) < .9f ? Vector3.Cross(b, Vector3.UnitY) : Vector3.UnitX);
            wa = SafeNormalize(wa, Vector3.UnitX);
            Vector3 wb = Vector3.Cross(wa, b);
            Quaternion local = Basis(a, la, lb), world = Basis(b, wa, wb);
            return Quaternion.Normalize(world * Quaternion.Conjugate(local));
        }

        private static Quaternion Basis(Vector3 axis, Vector3 side, Vector3 third)
        {
            var m = new Matrix4x4(
                side.X, side.Y, side.Z, 0,
                axis.X, axis.Y, axis.Z, 0,
                third.X, third.Y, third.Z, 0,
                0, 0, 0, 1);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float l = v.Length();
            return l > 1e-6f ? v / l : fallback;
        }
    }
}
