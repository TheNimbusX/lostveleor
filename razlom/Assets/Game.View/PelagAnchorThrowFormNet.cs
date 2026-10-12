using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// НЕВОД (кадр form-net-v1): плоская сеть, СПЛЕТЁННАЯ ИЗ ЦЕПЕЙ, чуть над землёй по полосе Sim (3 м, от руки до конца
    /// полосы): ромб из двух семейств цепей-диагоналей (звенья — процедурно в шейдере «Razlom/Anchor Throw Form Chain»,
    /// свечение морской зелени вдоль швов), боковые цепи сходятся к горлу у руки, хвостовой край рваный (концы цепей разной
    /// длины гаснут), над пойманными телами — горб (цепи облегают тело). Узлы — светящиеся точки (во вспышке — звезда, «Form Glint»).
    /// Решётка задана в покое (полоса в натяг); в возврате сеть ВЫБИРАЮТ: рисунок едет к руке и уходит в горло, хвостовой
    /// край гребёт по кривой тяги Sim (08.10 V7: прежнее сжатие всей решётки в пролёт [near, far] давало к ловле мятное
    /// пятно у героя). Ячейки не сжимаются. Все числа — PelagThrowFormLook; меши свои у объекта пула, вершины в мире.
    /// </summary>
    public sealed class AnchorThrowChainNet
    {
        public const string StrandMeshName = "AnchorThrowFormNetStrands", KnotMeshName = "AnchorThrowFormNetKnots";
        private const float PointStep = .2f;
        private const int MaxBumps = 8;

        public struct Frame
        {
            public Vector3 Origin, Dir, ViewForward, ViewUp;
            public float Near, Far, RestFar, FullHalf, Open, Collapse, Opacity, Clock, Pulse;
            public int Serial;
        }

        private readonly List<Vector3> _v = new List<Vector3>(4096);
        private readonly List<Vector4> _uv0 = new List<Vector4>(4096), _uv1 = new List<Vector4>(4096);
        private readonly List<Color32> _c = new List<Color32>(4096);
        private readonly List<int> _t = new List<int>(8192);
        private readonly List<Vector3> _kv = new List<Vector3>(1024);
        private readonly List<Vector4> _kuv = new List<Vector4>(1024);
        private readonly List<Color32> _kc = new List<Color32>(1024);
        private readonly List<int> _kt = new List<int>(1536);
        private readonly Vector4[] _bumps = new Vector4[MaxBumps];
        private int _bumpCount;
        private readonly List<Vector3> _line = new List<Vector3>(64);
        private readonly List<float> _lineU = new List<float>(64), _lineFade = new List<float>(64);
        private Frame _f;
        private AnchorThrowLaneGround _ground;
        private Vector3 _right;
        private float _restLength, _shift, _diag;

        /// <summary>Тела под сетью: x, z — точка на полу, w — вспышка узлов рядом (0…1).</summary>
        public void ClearBumps() => _bumpCount = 0;
        public void AddBump(Vector3 body, float flare)
        {
            if (_bumpCount < MaxBumps) _bumps[_bumpCount++] = new Vector4(body.x, 0f, body.z, flare);
        }

        public void Build(Mesh strands, Mesh knots, in Frame f, AnchorThrowLaneGround ground)
        {
            _f = f; _ground = ground;
            _v.Clear(); _uv0.Clear(); _uv1.Clear(); _c.Clear(); _t.Clear();
            _kv.Clear(); _kuv.Clear(); _kc.Clear(); _kt.Clear();
            _right = new Vector3(f.Dir.z, 0f, -f.Dir.x);
            float near0 = PelagThrowFormLook.NetNear;
            _restLength = Mathf.Max(.5f, f.RestFar - near0);
            // Выбирание: рисунок сдвинут к руке на столько, на сколько хвост ушёл от покоя (ячейки того же размера).
            _shift = Mathf.Max(0f, f.RestFar - f.Far);
            _diag = Mathf.Sqrt(1f + Sq(PelagThrowFormLook.NetCellAcross / PelagThrowFormLook.NetCellAlong));
            if (f.Opacity > .001f && f.Open > .001f && f.Far > f.Near + .1f)
            {
                float slope = PelagThrowFormLook.NetCellAcross / PelagThrowFormLook.NetCellAlong;
                float span = f.FullHalf + _restLength * slope + .5f;
                int m = Mathf.CeilToInt(span / PelagThrowFormLook.NetCellAcross);
                for (int family = -1; family <= 1; family += 2)
                    for (int i = -m; i <= m; i++)
                        Diagonal(family, i * PelagThrowFormLook.NetCellAcross, slope);
                Rope(1f);
                Rope(-1f);
                Knots(slope);
            }
            Write(strands, _v, _uv0, _uv1, _c, _t);
            knots.Clear();
            if (_kv.Count > 0)
            {
                knots.SetVertices(_kv);
                knots.SetUVs(0, _kuv);
                knots.SetColors(_kc);
                knots.SetTriangles(_kt, 0, true);
            }
        }

        private static void Write(Mesh mesh, List<Vector3> v, List<Vector4> uv0, List<Vector4> uv1, List<Color32> c, List<int> t)
        {
            mesh.Clear();
            if (v.Count < 4) return;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0, true);
        }

        private static float Sq(float x) => x * x;

        /// <summary>Полуширина воронки на <paramref name="x"/> м от центра героя (горло у руки стоит на месте, рисунок едет сквозь него).</summary>
        private float HalfAt(float x) => PelagThrowFormLook.NetHalfAt(x - _f.Near, _f.FullHalf);

        /// <summary>Хвостовой край в покое для поперечного <paramref name="y"/> (рваный).</summary>
        private float RestEnd(float y) => _f.RestFar + PelagThrowFormLook.NetRagAt(y / Mathf.Max(.1f, _f.FullHalf), _f.Serial);

        /// <summary>Точка мира (x м от героя вдоль, y поперёк) → раскрытие, стяжка, земля, горбы тел.</summary>
        private Vector3 Place(float x, float y)
        {
            float along = x;
            float across = y * Mathf.Pow(_f.Open, .7f) * (1f - .45f * _f.Collapse);
            Vector3 p = _f.Origin + _f.Dir * along + _right * across;
            p.y = _ground.At(along, across) + PelagThrowFormLook.NetLift * (1f - _f.Collapse);
            for (int b = 0; b < _bumpCount; b++)
            {
                float dx = p.x - _bumps[b].x, dz = p.z - _bumps[b].z;
                float d2 = (dx * dx + dz * dz) / (PelagThrowFormLook.NetBumpRadius * PelagThrowFormLook.NetBumpRadius);
                p.y += PelagThrowFormLook.NetBumpHeight * Mathf.Exp(-d2) * (1f - _f.Collapse);
            }
            return p;
        }

        /// <summary>Цепь-диагональ: x — метры мира от героя, a = x + сдвиг — место в рисунке (звенья едут с сетью).</summary>
        private void Diagonal(int family, float c, float slope)
        {
            _line.Clear(); _lineU.Clear(); _lineFade.Clear();
            float near0 = PelagThrowFormLook.NetNear;
            float end = _f.RestFar + PelagThrowFormLook.NetRag - _shift;
            bool open = false;
            // Начало шага привязано к рисунку: точки не «плывут» по цепи, пока сеть едет.
            float x0 = _f.Near + PointStep - Mathf.Repeat(_f.Near + _shift, PointStep);
            for (float x = Mathf.Min(_f.Near, x0); x <= end + 1e-3f; x = x < x0 ? x0 : x + PointStep)
            {
                float a = x + _shift;
                float y = family * (a - near0) * slope + c;
                bool inside = Mathf.Abs(y) <= HalfAt(x) + .02f && a <= RestEnd(y);
                if (!inside)
                {
                    if (open) { Emit(); _line.Clear(); _lineU.Clear(); _lineFade.Clear(); open = false; }
                    continue;
                }
                open = true;
                _line.Add(Place(x, y));
                _lineU.Add((a - near0) * _diag + Mathf.Abs(c) * 1.7f);
                // Рваный хвост: последние 0,6 м цепи гаснут к своему концу.
                _lineFade.Add(Mathf.Clamp01((RestEnd(y) - a) / .6f));
            }
            if (open) Emit();
        }

        /// <summary>Боковая цепь: по краю сети от хвоста к горлу (сходится к цепи у руки, как на кадре).</summary>
        private void Rope(float side)
        {
            _line.Clear(); _lineU.Clear(); _lineFade.Clear();
            float near = _f.Near;
            for (float x = near - .35f; x <= _f.RestFar + .05f - _shift; x += PointStep)
            {
                float y = x < near ? side * PelagThrowFormLook.NetThroatHalf * Mathf.Clamp01((x - near + .35f) / .35f) : side * HalfAt(x);
                float a = x + _shift;
                if (a > RestEnd(y)) break;
                _line.Add(Place(x, y));
                _lineU.Add(a);
                _lineFade.Add(Mathf.Clamp01((RestEnd(y) - a) / .6f));
            }
            Emit();
        }

        private void Emit()
        {
            int n = _line.Count;
            if (n < 2) return;
            float half = PelagThrowFormLook.StrandHalf;
            int start = _v.Count;
            float breakS = _f.Collapse * .30f;
            var heat = (byte)(Mathf.Clamp01(.55f + .45f * _f.Pulse) * 255f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _line[i];
                Vector3 t = (_line[Mathf.Min(n - 1, i + 1)] - _line[Mathf.Max(0, i - 1)]);
                Vector3 side = Vector3.Cross(t, Vector3.up);
                if (side.sqrMagnitude < 1e-8f) side = _right;
                side.Normalize();
                float fade = _lineFade[i];
                byte alpha = (byte)(Mathf.Clamp01(_f.Opacity * Mathf.Lerp(.15f, 1f, fade)) * 255f);
                var color = new Color32(heat, 0, (byte)(Mathf.Clamp01(_f.Pulse) * 255f), alpha);
                // Распад стяжки: хвост рассыпается первым.
                float b = Mathf.Max(0f, breakS - .06f * fade);
                for (int s = -1; s <= 1; s += 2)
                {
                    _v.Add(p + side * (half * s));
                    _uv0.Add(new Vector4(_lineU[i], s, b, _f.Clock));
                    _uv1.Add(new Vector4(half * s, Mathf.Lerp(.35f, 1f, fade), 0f, 0f));
                    _c.Add(color);
                }
                if (i == 0) continue;
                int v0 = start + (i - 1) * 2;
                _t.Add(v0); _t.Add(v0 + 2); _t.Add(v0 + 1);
                _t.Add(v0 + 1); _t.Add(v0 + 2); _t.Add(v0 + 3);
            }
        }

        /// <summary>Узлы — пересечения диагоналей: блёстки к камере, вспыхивают в раскрытие, у пойманных тел и в стяжку.</summary>
        private void Knots(float slope)
        {
            float near0 = PelagThrowFormLook.NetNear;
            float stepA = PelagThrowFormLook.NetCellAlong * .5f, stepY = PelagThrowFormLook.NetCellAcross * .5f;
            Vector3 right = Vector3.Cross(_f.ViewUp, _f.ViewForward).normalized, up = _f.ViewUp;
            int rows = Mathf.CeilToInt((_f.RestFar + PelagThrowFormLook.NetRag - near0) / stepA);
            for (int i = 1; i <= rows; i++)
            {
                float a = near0 + i * stepA;
                float x = a - _shift;
                if (x < _f.Near + .08f) continue;
                int cols = Mathf.CeilToInt(_f.FullHalf / stepY) + 1;
                for (int j = -cols; j <= cols; j++)
                {
                    if (((i + j) & 1) != 0) continue;
                    float y = j * stepY;
                    if (Mathf.Abs(y) > HalfAt(x) - .05f || a > RestEnd(y) - .15f) continue;
                    Vector3 p = Place(x, y) + Vector3.up * .02f;
                    float flare = KnotFlare(p, a);
                    float size = Mathf.Lerp(PelagThrowFormLook.KnotSize, PelagThrowFormLook.KnotFlareSize, flare);
                    size *= Mathf.Clamp01((RestEnd(y) - a) / .5f) * .7f + .3f;
                    var color = new Color32((byte)(Mathf.Clamp01(flare) * 255f), 0, 0, (byte)(Mathf.Clamp01(_f.Opacity * (1f - _f.Collapse * .5f)) * 255f));
                    int b = _kv.Count;
                    float rot = (i * 7 + j * 13) * .37f;
                    Vector3 rx = (right * Mathf.Cos(rot) + up * Mathf.Sin(rot)) * size * .5f;
                    Vector3 ry = (up * Mathf.Cos(rot) - right * Mathf.Sin(rot)) * size * .5f;
                    _kv.Add(p - rx - ry); _kv.Add(p + rx - ry); _kv.Add(p + rx + ry); _kv.Add(p - rx + ry);
                    _kuv.Add(new Vector4(0, 0, flare, _f.Clock)); _kuv.Add(new Vector4(1, 0, flare, _f.Clock));
                    _kuv.Add(new Vector4(1, 1, flare, _f.Clock)); _kuv.Add(new Vector4(0, 1, flare, _f.Clock));
                    _kc.Add(color); _kc.Add(color); _kc.Add(color); _kc.Add(color);
                    _kt.Add(b); _kt.Add(b + 2); _kt.Add(b + 1);
                    _kt.Add(b); _kt.Add(b + 3); _kt.Add(b + 2);
                }
            }
        }

        /// <summary>Вспышка узла: узкая волна раскрытия от горла к хвосту, тела рядом, стяжка.</summary>
        private float KnotFlare(Vector3 p, float a)
        {
            float rel = (a - PelagThrowFormLook.NetNear) / _restLength;
            // V7: волна узкая и вполсилы (V6: широкая волна белых звёзд по всей сети в «держит» читалась конфетти).
            float wave = .6f * Mathf.Clamp01(1f - Mathf.Abs(_f.Open * 1.3f - rel - .15f) * 7f);
            float flare = wave * (_f.Open < .999f ? 1f : 0f);
            for (int b = 0; b < _bumpCount; b++)
            {
                float dx = p.x - _bumps[b].x, dz = p.z - _bumps[b].z;
                flare = Mathf.Max(flare, _bumps[b].w * Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / 1.1f));
            }
            return Mathf.Max(flare, _f.Collapse * 1.2f);
        }
    }
}
