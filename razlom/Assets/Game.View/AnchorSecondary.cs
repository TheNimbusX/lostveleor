using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Повороты головы якоря — только кватернионы (перенос Orient.cs превью Крушения v4).</summary>
    public static class AnchorQuat
    {
        public static Quaternion Canon(Quaternion q) => q.W < 0 ? new Quaternion(-q.X, -q.Y, -q.Z, -q.W) : q;

        /// <summary>Вектор поворота (ось·угол), угол ≤ π — кратчайший путь.</summary>
        public static Vector3 Log(Quaternion q)
        {
            q = Canon(Quaternion.Normalize(q));
            var v = new Vector3(q.X, q.Y, q.Z);
            float s = v.Length();
            if (s < 1e-7f) return v * 2f;
            return v / s * (2f * MathF.Atan2(s, q.W));
        }

        public static Quaternion Exp(Vector3 r)
        {
            float a = r.Length();
            return a < 1e-7f ? Quaternion.Normalize(new Quaternion(r * .5f, 1f)) : Quaternion.CreateFromAxisAngle(r / a, a);
        }

        public static float Angle(Quaternion a, Quaternion b)
            => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f));

        public static Vector3 Ax(Quaternion q, Vector3 local) => Vector3.Transform(local, q);

        /// <summary>Поворот, переводящий вектор a в b по кратчайшей дуге.</summary>
        public static Quaternion Arc(Vector3 a, Vector3 b)
        {
            a = Vector3.Normalize(a); b = Vector3.Normalize(b);
            Vector3 c = Vector3.Cross(a, b);
            float d = Vector3.Dot(a, b);
            if (d < -.99999f)
            {
                Vector3 p = MathF.Abs(a.X) < .9f ? Vector3.Cross(a, Vector3.UnitX) : Vector3.Cross(a, Vector3.UnitY);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(p), MathF.PI);
            }
            return Quaternion.Normalize(new Quaternion(c, 1f + d));
        }
    }

    /// <summary>
    /// Вторичное движение поворота головы якоря (только вид: путь центра головы и контакты не меняются). Перенос
    /// Secondary.cs превью Крушения v4 (timing.json whip_chain_0610.secondary). Показанный поворот qd догоняет
    /// управляемый qc пружиной-демпфером отдельно по двум каналам — веретено (наклон оси к кольцу) и крен (вокруг оси):
    /// ω̇d = ω²·e + 2ζω·(f·ωc − ωd) неявным шагом; отклонение ≤ MaxDev (клин), |ωd| ≤ MaxW — раскрутки нет, крен не копится.
    /// </summary>
    public sealed class AnchorSecondary
    {
        public struct Tune
        {
            public float W, Z, F, RollW, RollZ, RollF, MaxDev, MaxW;
        }

        private const float Deg = MathF.PI / 180f;
        /// <summary>Махи и пауза (булава): ω 20, ζ 0,5, f 0,85; крен ω 14, ζ 0,75, f 0,8; клин 22°, ≤ 22 рад/с.</summary>
        public static readonly Tune Mace = new Tune { W = 20, Z = .5f, F = .85f, RollW = 14, RollZ = .75f, RollF = .8f, MaxDev = 22 * Deg, MaxW = 22 };
        /// <summary>Полёт выпада: ω 20 → к удару 45 (венец входит в грунт как у управляемого).</summary>
        public static readonly Tune Flight = new Tune { W = 20, Z = .45f, F = .8f, RollW = 14, RollZ = .75f, RollF = .85f, MaxDev = 20 * Deg, MaxW = 22 };
        /// <summary>Укус в воронке: ω 45, ζ 0,3, ≤ 8°.</summary>
        public static readonly Tune Bite = new Tune { W = 45, Z = .3f, F = .9f, RollW = 30, RollZ = .4f, RollF = .9f, MaxDev = 8 * Deg, MaxW = 35 };

        public Quaternion Qd = Quaternion.Identity;
        public Vector3 Wd;
        public float LastDev;
        private Quaternion _prev;
        private bool _has;

        public bool Ready => _has;

        public void Reset(Quaternion qc)
        {
            Qd = Quaternion.Normalize(qc); Wd = Vector3.Zero; _prev = Qd; _has = true; LastDev = 0;
        }

        /// <summary>Полёт: к удару жёстче (k 0…1 за 0,06 с до контакта).</summary>
        public static Tune Blend(in Tune a, in Tune b, float k)
        {
            k = Math.Clamp(k, 0f, 1f);
            var t = a;
            t.W += (b.W - a.W) * k;
            t.MaxDev += (b.MaxDev - a.MaxDev) * k;
            return t;
        }

        public void Step(Quaternion qc, float dt, in Tune k)
        {
            qc = Quaternion.Normalize(qc);
            if (!_has || dt <= 1e-6f) { Reset(qc); return; }
            Vector3 wc = AnchorQuat.Log(qc * Quaternion.Conjugate(_prev)) / dt;
            _prev = qc;
            if (wc.Length() > 30f) wc *= 30f / wc.Length();
            Vector3 y = AnchorQuat.Ax(qc, Vector3.UnitY);
            Vector3 e = AnchorQuat.Log(qc * Quaternion.Conjugate(Qd));
            Vector3 eR = Vector3.Dot(e, y) * y, eS = e - eR;
            Vector3 wcR = Vector3.Dot(wc, y) * y, wcS = wc - wcR, wdR = Vector3.Dot(Wd, y) * y, wdS = Wd - wdR;
            wdS += (k.W * k.W * eS + 2 * k.Z * k.W * (k.F * wcS - wdS)) * (dt / (1 + 2 * k.Z * k.W * dt + k.W * k.W * dt * dt));
            wdR += (k.RollW * k.RollW * eR + 2 * k.RollZ * k.RollW * (k.RollF * wcR - wdR))
                   * (dt / (1 + 2 * k.RollZ * k.RollW * dt + k.RollW * k.RollW * dt * dt));
            Wd = wdS + wdR;
            float cap = k.MaxW > 0 ? k.MaxW : 35f;
            if (Wd.Length() > cap) Wd *= cap / Wd.Length();
            Qd = Quaternion.Normalize(AnchorQuat.Exp(Wd * dt) * Qd);
            Vector3 e2 = AnchorQuat.Log(qc * Quaternion.Conjugate(Qd));
            float dev = e2.Length();
            if (dev > k.MaxDev)
            {
                // на границе клина: остаток ошибки = MaxDev, скорость «от цели» гасится
                Vector3 n = e2 / dev;
                Qd = Quaternion.Normalize(AnchorQuat.Exp(-n * k.MaxDev) * qc);
                float away = Vector3.Dot(Wd - wc, -n);
                if (away > 0) Wd += n * away;
                dev = k.MaxDev;
            }
            LastDev = dev;
        }

        /// <summary>Показ: доля вторичного движения w (0 — ровно управляемый поворот).</summary>
        public Quaternion Show(Quaternion qc, float w)
        {
            if (!_has || w <= 0f) return Quaternion.Normalize(qc);
            if (w >= 1f) return Qd;
            var d = Qd;
            if (Quaternion.Dot(d, qc) < 0) d = Quaternion.Negate(d);
            return Quaternion.Normalize(Quaternion.Slerp(qc, d, w));
        }
    }
}
