using System;
using System.Numerics;

namespace Wreck4Anchor
{
    /// <summary>Ориентация головы — только кватернионы: лог/эксп по кратчайшему пути, рамка из осей, критически
    /// демпфированный следящий поворот с упреждением скорости цели, эрмитова дуга поворота. Эйлеровых углов нет нигде.
    /// Оси головы: +Y — веретено к кольцу, X — размах лап, Z — нормаль плоскости якоря.</summary>
    public static class Q
    {
        public static Quaternion Canon(Quaternion q) => q.W < 0 ? new Quaternion(-q.X, -q.Y, -q.Z, -q.W) : q;

        /// <summary>Вектор поворота (ось·угол), угол ≤ π — кратчайший путь.</summary>
        public static Vector3 Log(Quaternion q)
        {
            q = Canon(Quaternion.Normalize(q));
            var v = new Vector3(q.X, q.Y, q.Z); float s = v.Length();
            if (s < 1e-7f) return v * 2f;
            float a = 2f * MathF.Atan2(s, q.W);
            return v / s * a;
        }

        public static Quaternion Exp(Vector3 r)
        {
            float a = r.Length();
            return a < 1e-7f ? Quaternion.Normalize(new Quaternion(r * .5f, 1f)) : Quaternion.CreateFromAxisAngle(r / a, a);
        }

        public static float Angle(Quaternion a, Quaternion b)
            => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f));

        public static Quaternion FromAxes(Vector3 x, Vector3 y, Vector3 z)
            => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));

        public static Vector3 Ax(Quaternion q, Vector3 local) => Vector3.Transform(local, q);

        /// <summary>Рамка: веретено Y точно, Z — по подсказке (проекция на плоскость ⟂ Y), знак Z — ближе к прошлому Z
        /// (лапы симметричны: перевернуть плоскость = не крутить голову на 180°).</summary>
        public static Quaternion Frame(Vector3 y, Vector3 zHint, Vector3 zPrev)
        {
            y = Vector3.Normalize(y);
            Vector3 z = zHint - Vector3.Dot(zHint, y) * y;
            if (z.LengthSquared() < 1e-6f) z = zPrev - Vector3.Dot(zPrev, y) * y;
            if (z.LengthSquared() < 1e-6f) z = MathF.Abs(y.Y) < .9f ? Vector3.Cross(Vector3.UnitY, y) : Vector3.UnitX;
            z = Vector3.Normalize(z);
            if (zPrev.LengthSquared() > .5f && Vector3.Dot(z, zPrev) < 0) z = -z;
            return FromAxes(Vector3.Cross(y, z), y, z);
        }

        /// <summary>Поворот, переводящий вектор a в b по кратчайшей дуге.</summary>
        public static Quaternion Arc(Vector3 a, Vector3 b)
        {
            a = Vector3.Normalize(a); b = Vector3.Normalize(b);
            Vector3 c = Vector3.Cross(a, b); float d = Vector3.Dot(a, b);
            if (d < -.99999f)
            {
                Vector3 p = MathF.Abs(a.X) < .9f ? Vector3.Cross(a, Vector3.UnitX) : Vector3.Cross(a, Vector3.UnitY);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(p), MathF.PI);
            }
            return Quaternion.Normalize(new Quaternion(c, 1f + d));
        }

        /// <summary>Эрмитова дуга поворота в касательном пространстве q0: концы и угловые скорости (мир, рад/с) совпадают.</summary>
        public static Quaternion Hermite(Quaternion q0, Vector3 w0, Quaternion q1, Vector3 w1, float duration, float u, out Vector3 w)
        {
            var inv = Quaternion.Conjugate(q0);
            Vector3 r1 = Log(inv * q1);
            Vector3 m0 = Vector3.Transform(w0, inv) * duration, m1 = Vector3.Transform(w1, inv) * duration;
            float u2 = u * u, u3 = u2 * u;
            Vector3 r = (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * r1 + (u3 - u2) * m1;
            Vector3 dr = (3 * u2 - 4 * u + 1) * m0 + (-6 * u2 + 6 * u) * r1 + (3 * u2 - 2 * u) * m1;
            w = Vector3.Transform(dr / duration, q0);
            return Quaternion.Normalize(q0 * Exp(r));
        }
    }

    /// <summary>Следящий поворот: критическое демпфирование к цели плюс упреждение её угловой скорости (без отставания на
    /// равномерном повороте, без перелёта, без раскрутки — ошибка всегда по кратчайшему пути).</summary>
    public sealed class Follower
    {
        public Quaternion Rot = Quaternion.Identity, PrevTarget;
        public Vector3 W;                       // угловая скорость, мир, рад/с
        bool _hasPrev;

        public void Reset(Quaternion q, Vector3 w) { Rot = Quaternion.Normalize(q); W = w; _hasPrev = false; _roll = 0; }

        float _roll;                            // скорость крена вокруг веретена, рад/с

        /// <summary>Веретено точно на цели (перенос без закрутки — кратчайшая дуга от прошлого веретена), крен — отдельно:
        /// критически демпфированно к нормали zTarget (знак любой — лапы симметричны) с пределом скорости rollRate.</summary>
        public void Transport(Vector3 yTarget, Vector3 zTarget, float rollRate, float dt, float k = 8f)
        {
            var old = Rot; yTarget = Vector3.Normalize(yTarget);
            Rot = Quaternion.Normalize(Q.Arc(Q.Ax(Rot, Vector3.UnitY), yTarget) * Rot);
            float phi = 0;
            Vector3 zt = zTarget - Vector3.Dot(zTarget, yTarget) * yTarget;
            if (zt.LengthSquared() > 1e-6f)
            {
                zt = Vector3.Normalize(zt); Vector3 z = Q.Ax(Rot, Vector3.UnitZ);
                if (Vector3.Dot(zt, z) < 0) zt = -zt;
                phi = MathF.Atan2(Vector3.Dot(Vector3.Cross(z, zt), yTarget), Vector3.Dot(z, zt));
            }
            _roll += (k * k * phi - 2 * k * _roll) * dt / (1 + 2 * k * dt + k * k * dt * dt);
            _roll = Math.Clamp(_roll, -rollRate, rollRate);
            Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(yTarget, _roll * dt) * Rot);
            W = Q.Log(Rot * Quaternion.Conjugate(old)) / dt; _hasPrev = false;
        }

        public void Step(Quaternion target, float omega, float dt, float maxW = float.PositiveInfinity)
        {
            target = Quaternion.Normalize(target);
            if (Quaternion.Dot(target, Rot) < 0) target = -target;
            Vector3 wt = _hasPrev ? Q.Log(target * Quaternion.Conjugate(PrevTarget)) / dt : Vector3.Zero;
            PrevTarget = target; _hasPrev = true;
            Vector3 e = Q.Log(target * Quaternion.Conjugate(Rot));
            Vector3 acc = omega * omega * e + 2 * omega * (wt - W);
            W += acc * (dt / (1 + 2 * omega * dt + omega * omega * dt * dt));
            float wl = W.Length();
            if (wl > maxW) W *= maxW / wl;
            Rot = Quaternion.Normalize(Q.Exp(W * dt) * Rot);
        }
    }
}
