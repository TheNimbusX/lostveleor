using System;
using System.Collections.Generic;
using System.Numerics;

namespace Wreck4Anchor
{
    /// <summary>
    /// УПРАВЛЯЕМЫЙ слой «тяжёлая булава на короткой цепи» (махи, выбор цепи после выпада).
    /// Состояние — направление цепи d (от выхода цепи к голове, единичное) и его угловая скорость Om.
    /// Пружина-демпфер на УГОЛ между цепью и линией рукояти: Ω̇ = ω²·e + 2ζω·(Ω_покоя − Ω) + (d × g)/R,
    /// e — вектор поворота d → r по кратчайшей дуге, r — продолжение оси рукояти, просевшее под весом (droop),
    /// плюс плавные поправки наведения на контакт (колокол вокруг тика удара, амплитуда — стрельбой).
    /// Цепь всегда натянута: голова = хват + d·(ℓ + кольцо), ℓ — длина цепи (0,45 м в махах, на выборе — по закону).
    /// Предел угловой скорости MaxW (вне кадра удара), отдача в тик контакта: Ω ← μ·Ω.
    /// </summary>
    public sealed class Mace
    {
        public float Omega = 16f, Zeta = .6f, Droop = .45f, MaxW = 20f, ImpactMaxW = 30f, Recoil = .35f, Eye = .381f, Feed = .6f;
        /// <summary>Пауза (окно нажатия прошло): доля «висит» 0..1 — направление покоя к отвесу, пружина к нулю,
        /// остаётся маятник (тяжесть) с сопротивлением HangDrag, 1/с.</summary>
        public float HangW, HangDrag;
        public Vector3 D, Om;
        /// <summary>Замах выпада: направление покоя ведётся по дуге через верх от покоя в OvT0 к OvDir к тику выпуска OvT1.</summary>
        public float OvT0 = -1, OvT1 = -1; public Vector3 OvDir, OvApex; Vector3 _ovFrom; bool _ovCached;
        public readonly List<(float Tc, float A, float B, Vector3 Delta)> Aims = new List<(float, float, float, Vector3)>();
        readonly Timeline _tl;

        public Mace(Timeline tl) { _tl = tl; }

        static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

        /// <summary>Вес поправки наведения: нарастает за A с до контакта, спадает за B с после.</summary>
        public static float Bump(float T, float tc, float a, float b)
            => T <= tc ? Smooth(1 - (tc - T) / a) : Smooth(1 - (T - tc) / b);

        /// <summary>Направление покоя: ось рукояти, просевшая на droop вниз, плюс поправки наведения.</summary>
        Vector3 Natural(float T) => Vector3.Normalize(_tl.Axis(T) - Droop * Vector3.UnitY);

        public Vector3 Rest(float T)
        {
            Vector3 r = Natural(T);
            if (OvT1 > 0 && T > OvT0)
            {
                if (!_ovCached) { _ovFrom = Natural(OvT0); _ovCached = true; }   // обычно задано BeginOverride (где голова)
                float u = Math.Clamp((T - OvT0) / (OvT1 - OvT0), 0, 1); u = u * u * u * (10 - 15 * u + 6 * u * u);
                // через верх: две дуги большого круга (к вершине над головой, от неё к направлению выпуска), по длине дуги
                float a1 = MathF.Acos(Math.Clamp(Vector3.Dot(_ovFrom, OvApex), -1, 1)), a2 = MathF.Acos(Math.Clamp(Vector3.Dot(OvApex, OvDir), -1, 1));
                float s = u * (a1 + a2);
                Vector3 path = s < a1 ? Vector3.Transform(_ovFrom, Q.Exp(Q.Log(Q.Arc(_ovFrom, OvApex)) * (s / Math.Max(1e-4f, a1))))
                                      : Vector3.Transform(OvApex, Q.Exp(Q.Log(Q.Arc(OvApex, OvDir)) * ((s - a1) / Math.Max(1e-4f, a2))));
                float w = Smooth((T - OvT0) * 15f);                       // вход за 2 тика
                r = Vector3.Normalize(Vector3.Lerp(r, path, w));
            }
            Vector3 rot = Vector3.Zero;
            foreach (var (tc, a, b, delta) in Aims) rot += delta * Bump(T, tc, a, b);
            return rot.LengthSquared() > 0 ? Vector3.Normalize(Vector3.Transform(r, Q.Exp(rot))) : r;
        }

        public Vector3 RestRate(float T, float h = 1f / 240f) => Q.Log(Q.Arc(Rest(T - h), Rest(T + h))) / (2 * h);

        /// <summary>Замах начинается оттуда, где голова реально есть (меньше пути — меньше угловая скорость).</summary>
        public void BeginOverride() { _ovFrom = D; _ovCached = true; }

        public void Start(Vector3 d, Vector3 om) { D = Vector3.Normalize(d); Om = om - Vector3.Dot(om, D) * D; }

        /// <summary>Шаг: R — расстояние центра головы от выхода цепи (ℓ + кольцо), omegaScale — мягче на выборе цепи.</summary>
        public void Step(float T, float dt, float R, bool impactWindow, float omegaScale = 1f, Vector3? restOverride = null)
        {
            Vector3 r = restOverride ?? Rest(T);
            Vector3 wr = restOverride.HasValue ? Vector3.Zero : RestRate(T);
            float w = Omega * omegaScale;
            if (HangW > 0) { r = Vector3.Normalize(Vector3.Lerp(r, -Vector3.UnitY, HangW)); wr *= 1 - HangW; w *= 1 - HangW; }
            Vector3 e = Q.Log(Q.Arc(D, r));
            Vector3 acc = w * w * e + 2 * Zeta * w * (Feed * wr - Om) + Vector3.Cross(D, new Vector3(0, -9.81f, 0)) / R - HangW * HangDrag * Om;
            Om += acc * dt;
            Om -= Vector3.Dot(Om, D) * D;
            float lim = impactWindow ? ImpactMaxW : MaxW, l = Om.Length();
            if (l > lim) Om *= lim / l;
            D = Vector3.Normalize(Vector3.Transform(D, Q.Exp(Om * dt)));
        }

        /// <summary>Удар: голова теряет большую часть угловой скорости (короткая отдача), пружина снова тянет её за рукоятью.</summary>
        public void Impact() => Om *= Recoil;

        /// <summary>Уложить голову на землю: центр не ниже yMin (поворот d вверх по кратчайшей дуге).</summary>
        public void KeepAbove(Vector3 grip, float R, float yMin)
        {
            Vector3 p = grip + D * R;
            if (p.Y >= yMin) return;
            float sy = Math.Clamp((yMin - grip.Y) / R, -1f, 1f);
            Vector3 h = new Vector3(D.X, 0, D.Z); float hl = h.Length();
            if (hl < 1e-5f) return;
            Vector3 nd = h / hl * MathF.Sqrt(1 - sy * sy) + Vector3.UnitY * sy;
            float omd = Vector3.Dot(Om, Vector3.Normalize(Vector3.Cross(D, Vector3.UnitY)));
            D = Vector3.Normalize(nd);
            if (omd < 0) Om -= omd * Vector3.Normalize(Vector3.Cross(D, Vector3.UnitY));   // гасим вращение в землю
            Om -= Vector3.Dot(Om, D) * D;
        }
    }
}
