using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Путь серпа маха Крушения V2 (06.10): рукоять (выход цепи из кулака) и голова якоря КАЖДЫЙ КАДР показа — их
    /// ставит риг якоря (PelagAnchorRig, запечка клипа), вид читает их в позднем кадре. Ряды серпа берутся в любой
    /// миг между кадрами кубикой Эрмита в цилиндрических осях вокруг героя (угол — развёрнутый, радиус, высота), а не
    /// по хордам: дуга остаётся круглой и сплошной при любом FPS (V1 ставил точку на кадр/тик — лента рвалась на
    /// штрихи). Время — тик показа (дробный). Без Unity (System.Numerics: X, Y — вверх, Z — оси мира Unity).
    /// </summary>
    public sealed class PelagWreckSwingSweep
    {
        public const int MaxSamples = 64;

        /// <summary>Ближе к оси героя угол точки не верен — берётся угол прошлого кадра, м.</summary>
        public const float MinRadius = .12f;

        private readonly float[] _time = new float[MaxSamples];
        private readonly float[] _px = new float[MaxSamples], _pz = new float[MaxSamples];
        private readonly float[] _ha = new float[MaxSamples], _hr = new float[MaxSamples], _hy = new float[MaxSamples];
        private readonly float[] _ga = new float[MaxSamples], _gr = new float[MaxSamples], _gy = new float[MaxSamples];
        private int _count;

        public int Count => _count;
        public float Newest => _count > 0 ? _time[_count - 1] : 0f;
        public float Oldest => _count > 0 ? _time[0] : 0f;

        public void Clear() => _count = 0;

        /// <summary>
        /// Кадр показа: тик показа, ось героя (центр вращения), рукоять и голова в мире. Тот же тик — переписывает
        /// последний кадр (два позднего кадра за один показ), более ранний — пропуск.
        /// </summary>
        public void Add(float time, Vector3 pivot, Vector3 grip, Vector3 head)
        {
            int i = _count;
            if (i > 0 && time <= _time[i - 1] + 1e-4f)
            {
                if (time < _time[i - 1] - 1e-4f) return;
                i--;
            }
            else if (i == MaxSamples)
            {
                Drop(MaxSamples / 4);
                i = _count;
            }
            _time[i] = time;
            _px[i] = pivot.X;
            _pz[i] = pivot.Z;
            Polar(head, pivot, i > 0 ? _ha[i - 1] : float.NaN, out _ha[i], out _hr[i]);
            _hy[i] = head.Y;
            Polar(grip, pivot, i > 0 ? _ga[i - 1] : float.NaN, out _ga[i], out _gr[i]);
            _gy[i] = grip.Y;
            _count = i + 1;
        }

        /// <summary>Угол точки вокруг оси (развёрнутый к прошлому: шаг не больше π) и радиус.</summary>
        private static void Polar(Vector3 p, Vector3 pivot, float previous, out float angle, out float radius)
        {
            float dx = p.X - pivot.X, dz = p.Z - pivot.Z;
            radius = (float)Math.Sqrt(dx * dx + dz * dz);
            if (radius < MinRadius && !float.IsNaN(previous)) { angle = previous; return; }
            angle = (float)Math.Atan2(dz, dx);
            if (float.IsNaN(previous)) return;
            while (angle - previous > Math.PI) angle -= (float)(2.0 * Math.PI);
            while (angle - previous < -Math.PI) angle += (float)(2.0 * Math.PI);
        }

        /// <summary>
        /// Угловая скорость головы вокруг героя по двум последним кадрам, рад/с: + — против часовой сверху (с правой
        /// стороны героя на левую через перёд). 0 — кадров меньше двух.
        /// </summary>
        public float HeadOmega(float ticksPerSecond)
        {
            if (_count < 2) return 0f;
            float dt = _time[_count - 1] - _time[_count - 2];
            return dt > 1e-4f ? (_ha[_count - 1] - _ha[_count - 2]) / dt * ticksPerSecond : 0f;
        }

        /// <summary>Отбросить кадры старше <paramref name="oldest"/>, оставив один на границе (ряд на краю окна).</summary>
        public void Trim(float oldest)
        {
            int drop = 0;
            while (drop < _count - 2 && _time[drop + 1] <= oldest) drop++;
            Drop(drop);
        }

        private void Drop(int drop)
        {
            if (drop <= 0) return;
            int n = _count - drop;
            Array.Copy(_time, drop, _time, 0, n);
            Array.Copy(_px, drop, _px, 0, n);
            Array.Copy(_pz, drop, _pz, 0, n);
            Array.Copy(_ha, drop, _ha, 0, n);
            Array.Copy(_hr, drop, _hr, 0, n);
            Array.Copy(_hy, drop, _hy, 0, n);
            Array.Copy(_ga, drop, _ga, 0, n);
            Array.Copy(_gr, drop, _gr, 0, n);
            Array.Copy(_gy, drop, _gy, 0, n);
            _count = n;
        }

        /// <summary>Рукоять и голова в тик показа <paramref name="time"/> (вне записанного — край). False — кадров нет.</summary>
        public bool Sample(float time, out Vector3 grip, out Vector3 head)
        {
            grip = head = Vector3.Zero;
            if (_count == 0) return false;
            int i;
            float s;
            if (_count == 1 || time <= _time[0]) { i = 0; s = 0f; }
            else if (time >= _time[_count - 1]) { i = _count - 2; s = 1f; }
            else
            {
                int lo = 0, hi = _count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (_time[mid] <= time) lo = mid; else hi = mid;
                }
                i = lo;
                s = (time - _time[i]) / Math.Max(1e-5f, _time[i + 1] - _time[i]);
            }
            if (_count == 1)
            {
                head = FromPolar(_px[0], _pz[0], _ha[0], _hr[0], _hy[0]);
                grip = FromPolar(_px[0], _pz[0], _ga[0], _gr[0], _gy[0]);
                return true;
            }
            float px = _px[i] + (_px[i + 1] - _px[i]) * s, pz = _pz[i] + (_pz[i + 1] - _pz[i]) * s;
            head = FromPolar(px, pz, Hermite(_ha, i, s), Math.Max(0f, Hermite(_hr, i, s)), Hermite(_hy, i, s));
            grip = FromPolar(px, pz, Hermite(_ga, i, s), Math.Max(0f, Hermite(_gr, i, s)), Hermite(_gy, i, s));
            return true;
        }

        private static Vector3 FromPolar(float px, float pz, float angle, float radius, float y)
            => new Vector3(px + radius * (float)Math.Cos(angle), y, pz + radius * (float)Math.Sin(angle));

        /// <summary>Кубика Эрмита на отрезке i…i+1, наклоны — центральные разности (неравные шаги кадров).</summary>
        private float Hermite(float[] v, int i, float s)
        {
            float h = _time[i + 1] - _time[i];
            float m0 = Slope(v, i) * h, m1 = Slope(v, i + 1) * h;
            float s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * v[i] + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * v[i + 1] + (s3 - s2) * m1;
        }

        private float Slope(float[] v, int i)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(_count - 1, i + 1);
            float dt = _time[b] - _time[a];
            return dt > 1e-5f ? (v[b] - v[a]) / dt : 0f;
        }

        /// <summary>
        /// Ряды серпа: до <paramref name="grips"/>.Length мигов от <paramref name="newest"/> назад на
        /// <paramref name="spanTicks"/> (не дальше записанного), ряд 0 — голова сейчас. Число рядов; 0 — серпа нет
        /// (кадров меньше двух или путь нулевой длины во времени).
        /// </summary>
        public int Rows(float newest, float spanTicks, Vector3[] grips, Vector3[] heads, float[] times)
        {
            int n = Math.Min(grips.Length, Math.Min(heads.Length, times.Length));
            if (_count < 2 || n < 2) return 0;
            float end = Math.Min(newest, _time[_count - 1]);
            float start = Math.Max(_time[0], end - spanTicks);
            if (end - start < 1e-4f) return 0;
            for (int k = 0; k < n; k++)
            {
                float t = end - (end - start) * k / (n - 1);
                Sample(t, out grips[k], out heads[k]);
                times[k] = t;
            }
            return n;
        }
    }
}
