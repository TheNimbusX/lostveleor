using System;
using System.Numerics;

namespace Wreck4Anchor
{
    /// <summary>
    /// Вторичное движение поворота головы (только вид — путь центра головы и контакты не меняются).
    /// Показанный поворот qd догоняет управляемый qc пружиной-демпфером, отдельно по двум каналам:
    ///   веретено (наклон оси к кольцу) и крен (вокруг оси), в мировых осях, ошибка e = log(qc·qd⁻¹) по кратчайшей дуге:
    ///   ω̇d = ω²·e + 2ζω·(f·ωc − ωd),  неявный шаг: Δω = (…)·h / (1 + 2ζωh + ω²h²);  qd ← exp(ωd·h)·qd.
    /// f &lt; 1 — упреждается только часть скорости цели, поэтому на разгоне голова отстаёт на 2ζ(1−f)·|ωc|/ω рад,
    /// ζ ≈ 0,4–0,45 — при остановке перелёт ≈ e^(−ζπ/√(1−ζ²)) ≈ 20 %. Отклонение от qc ≤ maxDev (клин: лишнее
    /// срезается, скорость «от цели» гасится), |ωd| ≤ 35 рад/с — раскрутки нет, крен накапливаться не может (он всегда
    /// возвращается к крену qc, а у qc крен ограничен 3 рад/с).
    /// </summary>
    public sealed class Secondary
    {
        public Quaternion Qd = Quaternion.Identity; public Vector3 Wd;
        Quaternion _prev; bool _has;
        public float LastDev;

        public void Reset(Quaternion qc) { Qd = Quaternion.Normalize(qc); Wd = Vector3.Zero; _prev = Qd; _has = true; LastDev = 0; }

        public struct Tune { public float W, Z, F, RollW, RollZ, RollF, MaxDev, MaxW; }

        public void Step(Quaternion qc, float dt, in Tune k)
        {
            qc = Quaternion.Normalize(qc);
            Vector3 wc = _has ? Q.Log(qc * Quaternion.Conjugate(_prev)) / dt : Vector3.Zero;
            _prev = qc; _has = true;
            if (wc.Length() > 30f) wc *= 30f / wc.Length();
            Vector3 y = Q.Ax(qc, Vector3.UnitY);
            Vector3 e = Q.Log(qc * Quaternion.Conjugate(Qd));
            Vector3 eR = Vector3.Dot(e, y) * y, eS = e - eR;
            Vector3 wcR = Vector3.Dot(wc, y) * y, wcS = wc - wcR, wdR = Vector3.Dot(Wd, y) * y, wdS = Wd - wdR;
            wdS += (k.W * k.W * eS + 2 * k.Z * k.W * (k.F * wcS - wdS)) * (dt / (1 + 2 * k.Z * k.W * dt + k.W * k.W * dt * dt));
            wdR += (k.RollW * k.RollW * eR + 2 * k.RollZ * k.RollW * (k.RollF * wcR - wdR))
                   * (dt / (1 + 2 * k.RollZ * k.RollW * dt + k.RollW * k.RollW * dt * dt));
            Wd = wdS + wdR;
            float cap = k.MaxW > 0 ? k.MaxW : 35f;
            if (Wd.Length() > cap) Wd *= cap / Wd.Length();
            Qd = Quaternion.Normalize(Q.Exp(Wd * dt) * Qd);
            Vector3 e2 = Q.Log(qc * Quaternion.Conjugate(Qd)); float dev = e2.Length();
            if (dev > k.MaxDev)
            {   // на границе клина: остаток ошибки = maxDev, скорость от цели (вдоль −e2) гасится
                Vector3 n = e2 / dev;
                Qd = Quaternion.Normalize(Q.Exp(-n * k.MaxDev) * qc);
                float away = Vector3.Dot(Wd - wc, -n);
                if (away > 0) Wd += n * away;
                dev = k.MaxDev;
            }
            LastDev = dev;
        }

        /// <summary>Показ: доля вторичного движения w (0 — ровно управляемый поворот).</summary>
        public Quaternion Show(Quaternion qc, float w)
        {
            if (w >= 1f) return Qd;
            if (w <= 0f) return Quaternion.Normalize(qc);
            var d = Qd; if (Quaternion.Dot(d, qc) < 0) d = Quaternion.Negate(d);
            return Quaternion.Normalize(Quaternion.Slerp(qc, d, w));
        }
    }
}
