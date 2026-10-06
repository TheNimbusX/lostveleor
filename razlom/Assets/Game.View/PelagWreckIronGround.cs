using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Меш разбитой земли Крушения «холодное железо» (шейдер Razlom/Wreck Iron Ground): круг удара
    /// (тёмная воронка Crater40 и рваное пятно Crater2 Hovl, сеть трещин, светящиеся трещины из-под якоря,
    /// рисованная «звезда» трещин Hovl Crack — V7), полоса (тёмная земля и трещины Crack4; свет — у оси)
    /// и паз по форме звена со светом из дыры звена под каждым звеном. V7 (разбор владельца 06.10 «земля
    /// по краям и форма слишком чёткая»): ни одного ровного края — край полосы ходит тем же шумом, что и
    /// камни (PelagWreckIronRules.LaneEdge, в цвете вершины g) и рвётся шумом в шейдере, концы полосы рваные
    /// (r — доля от конца); поверх — рваные пятна разбитой земли Hovl Crater2 вдоль неровного края и по кромке
    /// круга и ветвистые трещины Hovl Crater6, бегущие из полосы и круга наружу (Decals.cs). Пишется один раз
    /// в удар: каждая вершина лежит на земле (высота пола под ней) и знает, когда до неё дошёл удар (uv1.x,
    /// секунды от удара; полоса — по фронту Sim, круг — по раскрытию кольца). Координаты — от корня объекта
    /// (точка удара на земле, без поворота и масштаба).
    /// </summary>
    public sealed partial class PelagWreckIronGround
    {
        public const string MeshName = "WreckIronGround";

        private const int CraterRings = 10, CraterSectors = 36;
        /// <summary>Сетка круга — до 1,35 R (трещины выбегают за кромку), тёмное — до ~R по фактуре.</summary>
        private const float CraterReach = 1.35f;
        private const int LaneColumns = 13;
        private const float LaneRowStep = .2f;
        /// <summary>Сетка полосы шире полуширины: туда выбегают рваные края и трещины, гаснут к краю сетки.</summary>
        private const float LaneOverhang = .65f;
        private const int SpotGrid = 5;
        /// <summary>Сетка следа под звеном — с запасом за эллипс (свет гаснет к краю эллипса).</summary>
        private const float SpotReach = 1.15f;
        private const float Lift = .035f;

        private Vector3[] _vertices = new Vector3[0];
        private Vector4[] _uv0 = new Vector4[0], _uv1 = new Vector4[0];
        private Color32[] _colors = new Color32[0];
        private int[] _triangles = new int[0];
        private int _v, _t;

        /// <summary>Удар оземь в числах вида: всё, что нужно меху земли.</summary>
        public struct Slam
        {
            public Vector3 Root, Impact, Origin, Dir;
            public float Radius, Start, End, HalfWidth, Step;
            public int SlamTick, Serial, Links;
            public bool Lane;
        }

        public void Build(Mesh mesh, in Slam s, Func<float, float, float> ground)
        {
            int laneRows = s.Lane ? Math.Max(2, (int)Math.Ceiling((s.End + .1f - (s.Start - .35f)) / LaneRowStep) + 1) : 0;
            int vertices = (CraterRings + 1) * (CraterSectors + 1) + laneRows * LaneColumns + s.Links * SpotGrid * SpotGrid + MaxDecals * 4;
            int triangles = CraterRings * CraterSectors * 6 + Math.Max(0, laneRows - 1) * (LaneColumns - 1) * 6
                            + s.Links * (SpotGrid - 1) * (SpotGrid - 1) * 6 + MaxDecals * 6;
            if (_vertices.Length < vertices)
            {
                _vertices = new Vector3[vertices];
                _uv0 = new Vector4[vertices];
                _uv1 = new Vector4[vertices];
                _colors = new Color32[vertices];
            }
            if (_triangles.Length < triangles) _triangles = new int[triangles];
            _v = _t = 0;
            var perp = new Vector3(-s.Dir.z, 0f, s.Dir.x);
            float spin = 6.2831853f * PelagWreckIronRules.Hash01(s.Serial, 900);
            float splatSpin = PelagWreckIronRules.Hash01(s.Serial, 901);

            // Круг удара: полярная сетка до 1,35 R; фактуры воронки — 2,2 R поперёк (uv0.xy), рваное пятно и
            // звезда трещин считает шейдер из того же uv со своим поворотом (uv1.z) и размером.
            int craterBase = _v;
            for (int ring = 0; ring <= CraterRings; ring++)
            {
                float r = s.Radius * CraterReach * ring / CraterRings;
                for (int k = 0; k <= CraterSectors; k++)
                {
                    float a = 6.2831853f * k / CraterSectors;
                    Vector3 flat = s.Impact + (s.Dir * Mathf.Cos(a) + perp * Mathf.Sin(a)) * r;
                    float tu = Mathf.Cos(a + spin) * r / (s.Radius * 2.2f), tv = Mathf.Sin(a + spin) * r / (s.Radius * 2.2f);
                    float edge = 1f - PelagWreckIronRules.Smooth01((r - s.Radius * 1.12f) / (s.Radius * (CraterReach - 1.12f)));
                    float arrival = PelagWreckIronRules.Seconds(PelagWreckIronRules.CraterArrivalTick(Mathf.Min(r, s.Radius), s.SlamTick, s.Radius) - s.SlamTick);
                    Add(s, flat, ground, new Vector4(.5f + tu, .5f + tv, Mathf.Sin(a) * r, Mathf.Cos(a) * r), new Vector4(arrival, 1f, splatSpin, 1f), edge, 1f, 1f);
                }
            }
            Grid(craterBase, CraterRings + 1, CraterSectors + 1);

            if (s.Lane)
            {
                int laneBase = _v;
                float from = s.Start - .35f, width = s.HalfWidth + LaneOverhang;
                for (int row = 0; row < laneRows; row++)
                {
                    float along = Mathf.Min(s.End + .1f, from + row * LaneRowStep);
                    float arrival = PelagWreckIronRules.Seconds(PelagWreckIronRules.LaneArrivalTick(along, s.SlamTick, s.Start, s.Step) - s.SlamTick);
                    // Доля от концов полосы (r): шейдер рвёт концы шумом по ней — не ровная поперечная линия.
                    float ends = PelagWreckIronRules.Smooth01((along - from) / .5f) * (1f - PelagWreckIronRules.Smooth01((along - (s.End - .45f)) / .5f));
                    float edgeLeft = PelagWreckIronRules.LaneEdge(s.Serial, 1, along), edgeRight = PelagWreckIronRules.LaneEdge(s.Serial, -1, along);
                    for (int c = 0; c < LaneColumns; c++)
                    {
                        float across = -width + 2f * width * c / (LaneColumns - 1);
                        Vector3 flat = s.Origin + s.Dir * along + perp * across;
                        // Неровный край (g) — тот же шум, что у камней края; за полушириной сетка гаснет (a).
                        float edgeAt = Mathf.Abs(across) < .05f ? (edgeLeft + edgeRight) * .5f : across > 0f ? edgeLeft : edgeRight;
                        float outer = 1f - PelagWreckIronRules.Smooth01((Mathf.Abs(across) - (s.HalfWidth + .3f)) / (LaneOverhang - .3f));
                        Add(s, flat, ground, new Vector4(across, along - s.Start, across, along - s.Start), new Vector4(arrival, 0f, s.HalfWidth, 1f),
                            outer, ends, edgeAt);
                    }
                }
                Grid(laneBase, laneRows, LaneColumns);
            }

            // Пятна и трещины — до пазов звеньев: свет из дыры звена ложится поверх тёмного пятна.
            AddDecals(s, perp, ground);

            // Под звеньями: паз по форме звена и свет из дыры звена и его внутренней кромки (шейдер считает
            // осевую линию звена); uv0.xy — метры от центра звена (x поперёк, y вдоль), uv1.z — 1, если звено
            // лежит ребром (PelagWreckIronRules: нечётные — ребром, как в PlaceWreckIronPieces).
            float halfAlong = PelagWreckIronRules.LinkLength * .62f;
            float halfAcross = PelagWreckIronRules.LinkLength * PelagWreckIronRules.LinkWidthOfLength * .78f;
            for (int i = 0; i < s.Links; i++)
            {
                int spotBase = _v;
                float along = PelagWreckIronRules.LinkAlong(i, s.Start);
                float arrival = PelagWreckIronRules.Seconds(PelagWreckIronRules.LinkArrivalTick(i, s.SlamTick, s.Start, s.Step) - s.SlamTick);
                Vector3 centre = s.Origin + s.Dir * along;
                for (int j = 0; j < SpotGrid; j++)
                    for (int k = 0; k < SpotGrid; k++)
                    {
                        float u = SpotReach * (2f * k / (SpotGrid - 1) - 1f), v = SpotReach * (2f * j / (SpotGrid - 1) - 1f);
                        Vector3 flat = centre + s.Dir * (v * halfAlong) + perp * (u * halfAcross);
                        Add(s, flat, ground, new Vector4(u * halfAcross, v * halfAlong, u * halfAcross, along - s.Start + v * halfAlong),
                            new Vector4(arrival, 2f, PelagWreckIronRules.LinkOnEdge(i) ? 1f : 0f, 1.3f), 1f, 1f, 1f);
                    }
                Grid(spotBase, SpotGrid, SpotGrid);
            }

            mesh.Clear();
            mesh.SetVertices(_vertices, 0, _v);
            mesh.SetUVs(0, _uv0, 0, _v);
            mesh.SetUVs(1, _uv1, 0, _v);
            mesh.SetColors(_colors, 0, _v);
            mesh.SetTriangles(_triangles, 0, _t, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Вершина: a — гаснет к краю сетки, r — доля от концов полосы / «внутри» у трещины, g — неровный край полосы.</summary>
        private void Add(in Slam s, Vector3 flat, Func<float, float, float> ground, Vector4 uv0, Vector4 uv1, float edge, float r, float g)
        {
            float y = ground != null ? ground(flat.x, flat.z) : s.Root.y;
            _vertices[_v] = new Vector3(flat.x - s.Root.x, y + Lift - s.Root.y, flat.z - s.Root.z);
            _uv0[_v] = uv0;
            _uv1[_v] = uv1;
            _colors[_v] = new Color32(Byte(r), Byte(g), 255, Byte(edge));
            _v++;
        }

        private static byte Byte(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);

        /// <summary>Сетка rows × columns вершин от base подряд по рядам.</summary>
        private void Grid(int start, int rows, int columns)
        {
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < columns - 1; c++)
                {
                    int a = start + r * columns + c, b = a + columns;
                    _triangles[_t++] = a; _triangles[_t++] = b; _triangles[_t++] = a + 1;
                    _triangles[_t++] = a + 1; _triangles[_t++] = b; _triangles[_t++] = b + 1;
                }
        }
    }
}
