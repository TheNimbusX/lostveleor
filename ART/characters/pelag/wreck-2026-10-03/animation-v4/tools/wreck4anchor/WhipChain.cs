using System;
using System.Numerics;
using AnchorBake;

namespace Wreck4Anchor
{
    /// <summary>
    /// Цепь-хлыст между кулаком и кольцом якоря — всегда симулируется (как цепи Клинков Хаоса: путь головы задан,
    /// физику даёт цепь). Чистая математика на System.Numerics — переносится в AnchorRigCore как есть.
    ///
    /// Узлы 0…N (N звеньев): 0 — выход цепи из кулака, N — кольцо головы; оба прибиты (кинематика клипа и головы).
    /// Длина L задаётся снаружи по фазам (Sim.WhipLength); длина звена ℓ = L/N — выдача и выбор идут по всей цепи
    /// (скорость материала вдоль цепи линейна от кулака к кольцу), звенья рисуются от кольца с шагом 0,135 м.
    ///
    /// Подшаг h (1/240 с):
    ///  1) Верле: x ← x + (x − x₋)·(1 − c·h) + g·h²  (инерция, вязкое сопротивление c, тяжесть);
    ///  2) K итераций Гаусса — Зейделя:
    ///     • длины звеньев |x_{i+1} − x_i| = ℓ (проход попеременно от кулака и от кольца);
    ///     • дальность от пинов (LRA): |x_i − A| ≤ i·ℓ, |x_i − B| ≤ (N − i)·ℓ — натяг без растяжения за одну итерацию;
    ///     • запрет складки: |x_{i+1} − x_{i−1}| ≥ fold·2ℓ (без зигзага);
    ///     • выход из кулака вдоль рукояти: узел 1 мягко к A + ось·ℓ;
    ///     • капсулы тела (+ капсула веретена головы) и земля: проекция наружу на радиус звена;
    ///  3) трение о землю: касательная скорость узла на земле × e^(−μ·h).
    /// </summary>
    public sealed class WhipChain
    {
        public readonly int N;
        public readonly Vector3[] X, Prev;
        public float Length, Drag = 1.2f, Friction = 18f, Radius = .018f, Fold = .55f, GripAim = .3f, SkipA = .25f;
        public int Iterations = 12;
        public static readonly Vector3 G = new Vector3(0, -9.81f, 0);
        public float MaxStrain, MaxPen;                 // за последний шаг: растяжение звена, глубина в капсулах
        public int MaxPenCap = -1, MaxPenNode = -1;
        public int Substeps, CapsuleTests;              // счётчики стоимости

        public WhipChain(int n) { N = n; X = new Vector3[n + 1]; Prev = new Vector3[n + 1]; }

        /// <summary>Засев: парабола провиса по длине (стрела h = √(3·c·(L−c)/8)), скорость ноль.</summary>
        public void Seed(Vector3 a, Vector3 b, float length)
        {
            Length = length;
            float c = Vector3.Distance(a, b), sag = c < length ? MathF.Sqrt(3 * c * (length - c) / 8) : 0;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                X[i] = Vector3.Lerp(a, b, t) - Vector3.UnitY * (4 * sag * t * (1 - t));
                Prev[i] = X[i];
            }
        }

        /// <summary>Один подшаг. a — кулак, b — кольцо, axis — ось рукояти к выходу цепи, caps[0..capCount) — капсулы
        /// (тело + веретено головы), skipA/skipB — маски капсул, внутри которых сидит пин (не толкать узлы у пина),
        /// ground — высота земли.</summary>
        public void Step(float h, Vector3 a, Vector3 b, Vector3 axis, Capsule[] caps, int capCount, uint skipA, uint skipB,
                         Func<Vector3, float> ground)
        {
            Substeps++;
            float keep = 1 - Drag * h;
            for (int i = 1; i < N; i++)
            {
                Vector3 x = X[i], v = (x - Prev[i]) * keep;
                Prev[i] = x; X[i] = x + v + G * (h * h);
            }
            Prev[0] = X[0]; Prev[N] = X[N]; X[0] = a; X[N] = b;
            float l = Length / N;
            // широкая фаза: только капсулы, чей бокс задевает бокс цепи (+ запас на ход за подшаг)
            Vector3 lo = X[0], hi = X[0];
            for (int i = 1; i <= N; i++) { lo = Vector3.Min(lo, X[i]); hi = Vector3.Max(hi, X[i]); }
            Vector3 pad = new Vector3(Radius + .05f);
            int near = 0;
            for (int k = 0; k < capCount; k++)
            {
                var c = caps[k]; Vector3 r = new Vector3(c.R);
                if (Vector3.Min(c.A, c.B).X - r.X > hi.X + pad.X || Vector3.Max(c.A, c.B).X + r.X < lo.X - pad.X ||
                    Vector3.Min(c.A, c.B).Y - r.Y > hi.Y + pad.Y || Vector3.Max(c.A, c.B).Y + r.Y < lo.Y - pad.Y ||
                    Vector3.Min(c.A, c.B).Z - r.Z > hi.Z + pad.Z || Vector3.Max(c.A, c.B).Z + r.Z < lo.Z - pad.Z) continue;
                _near[near++] = k;
            }
            if (axis.LengthSquared() > 1e-6f) X[1] = Vector3.Lerp(X[1], a + axis * l, GripAim);
            for (int it = 0; it < Iterations; it++)
            {
                for (int i = 1; i < N; i++) Unfold(i, l);
                if ((it & 1) == 0) for (int i = 0; i < N; i++) Link(i, l);
                else for (int i = N - 1; i >= 0; i--) Link(i, l);
                for (int i = 1; i < N; i++)
                {
                    X[i] = Within(X[i], a, i * l);
                    X[i] = Within(X[i], b, (N - i) * l);
                }
                if (it % 3 == 2 || it == Iterations - 1) Collide(l, caps, near, skipA, skipB, ground, it == Iterations - 1);
            }
            // последней идёт длина: в споре «капсула против длины» цепь не рвётся (остаток проникновения — в MaxPen)
            for (int i = 0; i < N; i++) Link(i, l);
            for (int i = N - 1; i >= 0; i--) Link(i, l);
            for (int i = 1; i < N; i++)
            {
                float gy = ground(X[i]) + Radius;
                if (X[i].Y > gy + 1e-3f) continue;
                Vector3 v = X[i] - Prev[i]; float k = MathF.Exp(-Friction * h);
                Prev[i] = X[i] - new Vector3(v.X * k, Math.Max(0, v.Y), v.Z * k);
            }
            MaxStrain = 0;
            for (int i = 0; i < N; i++) MaxStrain = Math.Max(MaxStrain, MathF.Abs(Vector3.Distance(X[i], X[i + 1]) - l) / l);
        }

        static float W(int i, int n) => i == 0 || i == n ? 0f : 1f;

        void Link(int i, float l)
        {
            float wi = W(i, N), wj = W(i + 1, N), ws = wi + wj;
            if (ws <= 0) return;
            Vector3 d = X[i + 1] - X[i]; float len = d.Length();
            if (len < 1e-7f) return;
            Vector3 c = d * ((len - l) / (len * ws));
            X[i] += c * wi; X[i + 1] -= c * wj;
        }

        static Vector3 Within(Vector3 x, Vector3 pin, float r)
        {
            Vector3 d = x - pin; float len = d.Length();
            return len > r && len > 1e-7f ? pin + d * (r / len) : x;
        }

        void Unfold(int i, float l)
        {
            float min = Fold * 2 * l, wa = W(i - 1, N), wb = W(i + 1, N), ws = wa + wb;
            if (ws <= 0) return;
            Vector3 d = X[i + 1] - X[i - 1]; float len = d.Length();
            if (len >= min || len < 1e-7f) return;
            Vector3 c = d * ((len - min) / (len * ws));
            X[i - 1] += c * wa; X[i + 1] -= c * wb;
        }

        readonly int[] _near = new int[32];

        void Collide(float l, Capsule[] caps, int near, uint skipA, uint skipB, Func<Vector3, float> ground, bool last)
        {
            if (last) { MaxPen = 0; MaxPenCap = MaxPenNode = -1; }
            for (int i = 1; i < N; i++)
            {
                Vector3 x = X[i];
                bool nearA = i * l < SkipA, nearB = (N - i) * l < .08f;
                for (int j = 0; j < near; j++)
                {
                    int k = _near[j]; uint bit = 1u << k;
                    if ((nearA && (skipA & bit) != 0) || (nearB && (skipB & bit) != 0)) continue;
                    CapsuleTests++;
                    float depth = caps[k].Depth(x, out Vector3 n) + Radius;
                    if (depth > 0) { x += n * depth; if (last && depth > MaxPen) { MaxPen = depth; MaxPenCap = k; MaxPenNode = i; } }
                }
                float gy = ground(x) + Radius;
                if (x.Y < gy) x.Y = gy;
                X[i] = x;
            }
        }

        /// <summary>Маска капсул, внутри которых (с радиусом звена) сидит точка p.</summary>
        public uint Inside(Vector3 p, Capsule[] caps, int capCount)
        {
            uint m = 0;
            for (int k = 0; k < capCount; k++) if (caps[k].Depth(p, out _) + Radius > 0) m |= 1u << k;
            return m;
        }

        /// <summary>Провис: наибольшее расстояние узла от хорды кулак–кольцо (м).</summary>
        public float Bow()
        {
            Vector3 a = X[0], d = X[N] - X[0]; float dl = d.LengthSquared(), best = 0;
            for (int i = 1; i < N; i++)
            {
                float u = dl > 1e-8f ? Math.Clamp(Vector3.Dot(X[i] - a, d) / dl, 0, 1) : 0;
                best = Math.Max(best, Vector3.Distance(X[i], a + d * u));
            }
            return best;
        }
    }
}
