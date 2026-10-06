using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Цепь-хлыст Крушения v4 (06.10, реф — цепи Клинков Хаоса): ВСЕГДА симулируется между выходом цепи из кулака и
    /// кольцом головы. Путь головы задан (запечка рига), физику даёт цепь: отстаёт, выгибается, на ударах натягивается,
    /// в паузах провисает. Перенос решателя превью ART/characters/pelag/wreck-2026-10-03/animation-v4/tools/wreck4anchor/
    /// WhipChain.cs (те же числа: timing.json whip_chain_0610) на капсулы рига <see cref="AnchorCapsule"/>.
    ///
    /// Узлы 0…N: 0 — выход из кулака, N — кольцо; оба прибиты. Длина L задаётся снаружи (запечка, по фазам);
    /// звено ℓ = L/N — выдача и выбор идут по всей цепи. Подшаг h: Верле (инерция, вязкость, тяжесть) → K итераций
    /// (складка, длины попеременно, дальность от обоих пинов, капсулы и земля каждую 3-ю и последнюю) → длины ещё раз
    /// в обе стороны → трение о землю. Рисуется звеньями с шагом <see cref="AnchorChainLine.Pitch"/> от кольца.
    /// </summary>
    public sealed class AnchorWhipChain
    {
        public const int MaxCapsules = 24;
        public readonly int N;
        public readonly Vector3[] X, Prev;
        public float Length, Drag = 1.2f, Friction = 18f, Radius = .018f, Fold = .55f, GripAim = .3f, SkipA = .25f;
        public int Iterations = 8;
        public static readonly Vector3 G = new Vector3(0, -9.81f, 0);
        /// <summary>За последний шаг: наибольшее растяжение звена (доля) и глубина звена в капсуле, м.</summary>
        public float MaxStrain, MaxPen;
        public int Substeps;
        public bool Seeded { get; private set; }
        private readonly int[] _near = new int[MaxCapsules];

        public AnchorWhipChain(int links = 16)
        {
            N = Math.Max(2, links);
            X = new Vector3[N + 1];
            Prev = new Vector3[N + 1];
        }

        public void Reset() => Seeded = false;

        /// <summary>Засев: парабола провиса по длине (стрела √(3·c·(L−c)/8)), скорость ноль.</summary>
        public void Seed(Vector3 a, Vector3 b, float length)
        {
            Length = Math.Max(1e-3f, length);
            float c = Vector3.Distance(a, b), sag = c < Length ? MathF.Sqrt(3 * c * (Length - c) / 8) : 0;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                X[i] = Vector3.Lerp(a, b, t) - Vector3.UnitY * (4 * sag * t * (1 - t));
                Prev[i] = X[i];
            }
            Seeded = true;
        }

        /// <summary>
        /// Один подшаг. a — кулак, b — кольцо, axis — ось рукояти к выходу цепи (ноль — без прицела), caps — капсулы
        /// (тело + веретено головы), skipA/skipB — маски капсул, внутри которых сидит пин, ground — высота земли.
        /// </summary>
        public void Step(float h, Vector3 a, Vector3 b, Vector3 axis, AnchorCapsule[] caps, int capCount, uint skipA, uint skipB,
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
            // широкая фаза: капсулы, чей бокс задевает бокс цепи (+ запас на ход за подшаг)
            Vector3 lo = X[0], hi = X[0];
            for (int i = 1; i <= N; i++) { lo = Vector3.Min(lo, X[i]); hi = Vector3.Max(hi, X[i]); }
            float pad = Radius + .05f;
            int near = 0;
            capCount = Math.Min(capCount, Math.Min(caps != null ? caps.Length : 0, MaxCapsules));
            for (int k = 0; k < capCount; k++)
            {
                var c = caps[k];
                Vector3 cmin = Vector3.Min(c.A, c.B) - new Vector3(c.Radius), cmax = Vector3.Max(c.A, c.B) + new Vector3(c.Radius);
                if (cmin.X > hi.X + pad || cmax.X < lo.X - pad || cmin.Y > hi.Y + pad || cmax.Y < lo.Y - pad
                    || cmin.Z > hi.Z + pad || cmax.Z < lo.Z - pad) continue;
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
            // последней идёт длина: в споре «капсула против длины» цепь не рвётся (остаток — в MaxPen)
            for (int i = 0; i < N; i++) Link(i, l);
            for (int i = N - 1; i >= 0; i--) Link(i, l);
            if (ground != null)
            {
                float k = MathF.Exp(-Friction * h);
                for (int i = 1; i < N; i++)
                {
                    float gy = ground(X[i]) + Radius;
                    if (X[i].Y > gy + 1e-3f) continue;
                    Vector3 v = X[i] - Prev[i];
                    Prev[i] = X[i] - new Vector3(v.X * k, Math.Max(0, v.Y), v.Z * k);
                }
            }
            MaxStrain = 0;
            for (int i = 0; i < N; i++) MaxStrain = Math.Max(MaxStrain, MathF.Abs(Vector3.Distance(X[i], X[i + 1]) - l) / l);
        }

        private static float W(int i, int n) => i == 0 || i == n ? 0f : 1f;

        private void Link(int i, float l)
        {
            float wi = W(i, N), wj = W(i + 1, N), ws = wi + wj;
            if (ws <= 0) return;
            Vector3 d = X[i + 1] - X[i];
            float len = d.Length();
            if (len < 1e-7f) return;
            Vector3 c = d * ((len - l) / (len * ws));
            X[i] += c * wi; X[i + 1] -= c * wj;
        }

        private static Vector3 Within(Vector3 x, Vector3 pin, float r)
        {
            Vector3 d = x - pin;
            float len = d.Length();
            return len > r && len > 1e-7f ? pin + d * (r / len) : x;
        }

        private void Unfold(int i, float l)
        {
            float min = Fold * 2 * l, wa = W(i - 1, N), wb = W(i + 1, N), ws = wa + wb;
            if (ws <= 0) return;
            Vector3 d = X[i + 1] - X[i - 1];
            float len = d.Length();
            if (len >= min || len < 1e-7f) return;
            Vector3 c = d * ((len - min) / (len * ws));
            X[i - 1] += c * wa; X[i + 1] -= c * wb;
        }

        /// <summary>Глубина точки в капсуле с радиусом звена (&gt; 0 — внутри) и нормаль наружу.</summary>
        public float Depth(in AnchorCapsule c, Vector3 p, out Vector3 n) => c.Radius + Radius - c.AxisDistance(p, out n);

        private void Collide(float l, AnchorCapsule[] caps, int near, uint skipA, uint skipB, Func<Vector3, float> ground, bool last)
        {
            if (last) MaxPen = 0;
            for (int i = 1; i < N; i++)
            {
                Vector3 x = X[i];
                bool nearA = i * l < SkipA, nearB = (N - i) * l < .08f;
                for (int j = 0; j < near; j++)
                {
                    int k = _near[j];
                    uint bit = 1u << k;
                    if ((nearA && (skipA & bit) != 0) || (nearB && (skipB & bit) != 0)) continue;
                    float depth = Depth(caps[k], x, out Vector3 n);
                    if (depth > 0) { x += n * depth; if (last && depth > MaxPen) MaxPen = depth; }
                }
                if (ground != null)
                {
                    float gy = ground(x) + Radius;
                    if (x.Y < gy) x.Y = gy;
                }
                X[i] = x;
            }
        }

        /// <summary>Маска капсул, внутри которых (с радиусом звена) сидит точка p.</summary>
        public uint Inside(Vector3 p, AnchorCapsule[] caps, int capCount)
        {
            uint m = 0;
            capCount = Math.Min(capCount, Math.Min(caps != null ? caps.Length : 0, MaxCapsules));
            for (int k = 0; k < capCount; k++) if (Depth(caps[k], p, out _) > 0) m |= 1u << k;
            return m;
        }

        /// <summary>Провис: наибольшее расстояние узла от хорды кулак–кольцо, м.</summary>
        public float Bow()
        {
            Vector3 a = X[0], d = X[N] - X[0];
            float dl = d.LengthSquared(), best = 0;
            for (int i = 1; i < N; i++)
            {
                float u = dl > 1e-8f ? Math.Clamp(Vector3.Dot(X[i] - a, d) / dl, 0, 1) : 0;
                best = Math.Max(best, Vector3.Distance(X[i], a + d * u));
            }
            return best;
        }

        /// <summary>
        /// Узлы для рисования звеньями: от кольца (out[0]) вдоль ломаной с шагом <paramref name="pitch"/>, последний —
        /// выход из кулака (неполное звено у рукояти). Возвращает число узлов (звеньев на одно меньше).
        /// </summary>
        public int Resample(float pitch, Vector3[] into)
        {
            if (into == null || into.Length < 2) return 0;
            int count = 0;
            into[count++] = X[N];
            float carry = 0f;
            for (int i = N; i > 0 && count < into.Length - 1; i--)
            {
                Vector3 a = X[i], b = X[i - 1];
                float seg = Vector3.Distance(a, b);
                if (seg < 1e-7f) continue;
                float at = pitch - carry;
                while (at <= seg && count < into.Length - 1)
                {
                    into[count++] = Vector3.Lerp(a, b, at / seg);
                    at += pitch;
                }
                carry = seg - (at - pitch);
            }
            if (Vector3.DistanceSquared(into[count - 1], X[0]) > 1e-8f) into[count++] = X[0];
            return count;
        }
    }
}
