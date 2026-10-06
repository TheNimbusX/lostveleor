using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    /// <summary>
    /// Серп маха Крушения V2 (06.10, swing1.png / swing2.png): сплошная тяжёлая полоса от кулака до носа головы якоря
    /// по последним PelagWreckSwingRules.WindowSeconds её хода. Ряды — из PelagWreckSwingSweep (миги между кадрами
    /// показа, Эрмит в цилиндрических осях вокруг героя): при любом FPS полоса не рвётся на штрихи (V1). Поперёк ряда —
    /// отрезок «рукоять → голова»: внутренняя основа на InnerFromGrip от кулака, наружная кромка за головой на
    /// OuterPastHead; внутренний край по PelagWreckSwingRules.InnerShare — у головы полоса толстая, к хвосту сходит на
    /// нет (серп). Шейдер Razlom/Wreck Swing (режим «серп»):
    ///   uv0 = (u: 0 голова … 1 хвост, v: 0 внутренний край … 1 наружная кромка, метры наружной кромки от головы,
    ///          ширина полосы в ряду, м);
    ///   uv1 = (непрозрачность: скорость головы × хвост, распад 0…1, зерно, яркость маха).
    /// Меш в осях корня объекта пула (без поворота и масштаба). Ряды кромки и ширины открыты — по ним едут звенья.
    /// </summary>
    public sealed class PelagWreckSwingRibbon : FormWaterMesh
    {
        public const string MeshName = "Крушение: серп маха";
        public const int Rows = 41, Columns = 3;
        private static int[] _triangles;
        private readonly NV[] _grips = new NV[Rows], _heads = new NV[Rows];
        private readonly float[] _times = new float[Rows];

        /// <summary>Внутренний край и наружная кромка ряда (мир), ширина полосы, м, непрозрачность — ряды 0 … Count − 1.</summary>
        public readonly Vector3[] Inner = new Vector3[Rows], Outer = new Vector3[Rows];
        public readonly float[] Band = new float[Rows], Alpha = new float[Rows];

        /// <summary>Рядов в последней сборке (0 — серпа нет).</summary>
        public int Count { get; private set; }

        public void Begin()
        {
            if (_triangles == null) _triangles = StripTriangles(1, Rows, Columns, false);
            Allocate(Rows * Columns, _triangles);
            Count = 0;
        }

        private static Vector3 U(NV v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>
        /// Серп по пути <paramref name="sweep"/> к тику показа <paramref name="newest"/> (замерший — к последнему кадру).
        /// <paramref name="erode"/> — распад 0…1, <paramref name="seed"/> — зерно шума.
        /// </summary>
        public void Build(Mesh mesh, Vector3 root, PelagWreckSwingSweep sweep, float newest, WreckSwingWeight weight, float erode, float seed)
        {
            int n = sweep.Rows(newest, PelagWreckSwingRules.WindowTicks(weight), _grips, _heads, _times);
            Count = n;
            float glow = PelagWreckSwingRules.Glow(weight);
            float outerPast = PelagWreckSwingRules.OuterPastHead(weight);
            float meters = 0f;
            for (int k = 0; k < Rows; k++)
            {
                if (n < 2)
                {
                    for (int c = 0; c < Columns; c++)
                    {
                        int at = k * Columns + c;
                        Vertices[at] = Vector3.zero;
                        Uv0[at] = Vector4.zero;
                        Uv1[at] = new Vector4(0f, 1f, seed, glow);
                        Colors[at] = new Color32(255, 255, 255, 0);
                    }
                    continue;
                }
                Vector3 grip = U(_grips[k]), head = U(_heads[k]);
                Vector3 chain = head - grip;
                float length = chain.magnitude;
                Vector3 dir = length > 1e-4f ? chain / length : Vector3.forward;
                Vector3 inBase = grip + dir * PelagWreckSwingRules.InnerFromGrip;
                Vector3 outer = head + dir * outerPast;
                float u = k / (float)(n - 1);
                Vector3 inner = Vector3.Lerp(inBase, outer, PelagWreckSwingRules.InnerShare(u, weight));
                if (k > 0) meters += (outer - Outer[k - 1]).magnitude;
                float band = (outer - inner).magnitude;
                // Скорость головы в ряду — по соседним рядам (ряды уже гладкие), м/с.
                int a = Mathf.Max(0, k - 1), b = Mathf.Min(n - 1, k + 1);
                float span = PelagWreckSwingRules.Seconds(_times[a] - _times[b]);
                float speed = span > 1e-5f ? (U(_heads[a]) - U(_heads[b])).magnitude / span : 0f;
                float alpha = PelagWreckSwingRules.SpeedAlpha(speed) * PelagWreckSwingRules.TailAlpha(u);
                Inner[k] = inner;
                Outer[k] = outer;
                Band[k] = band;
                Alpha[k] = alpha;
                for (int c = 0; c < Columns; c++)
                {
                    float v = c / (Columns - 1f);
                    int at = k * Columns + c;
                    Vertices[at] = Vector3.Lerp(inner, outer, v) - root;
                    Uv0[at] = new Vector4(u, v, meters, band);
                    Uv1[at] = new Vector4(alpha, erode, seed, glow);
                    Colors[at] = new Color32(255, 255, 255, 255);
                }
            }
            Write(mesh);
        }

        /// <summary>
        /// Точка на серпе для звена: доля <paramref name="u"/> вдоль, ось звена на <paramref name="inset"/> м внутрь от
        /// наружной кромки; ход (к голове) и поперёк (к кромке); ширина полосы и непрозрачность там. False — серпа нет.
        /// </summary>
        public bool At(float u, float inset, out Vector3 point, out Vector3 along, out Vector3 across, out float band, out float alpha)
        {
            point = along = across = Vector3.zero;
            band = alpha = 0f;
            if (Count < 2) return false;
            float f = Mathf.Clamp01(u) * (Count - 1);
            int i = Mathf.Min(Count - 2, (int)f);
            float s = f - i;
            Vector3 outer = Vector3.Lerp(Outer[i], Outer[i + 1], s);
            Vector3 inner = Vector3.Lerp(Inner[i], Inner[i + 1], s);
            band = Mathf.Lerp(Band[i], Band[i + 1], s);
            alpha = Mathf.Lerp(Alpha[i], Alpha[i + 1], s);
            across = outer - inner;
            across = across.sqrMagnitude > 1e-8f ? across.normalized : Vector3.up;
            along = Outer[Mathf.Max(0, i - 1)] - Outer[Mathf.Min(Count - 1, i + 2)];
            along = along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.Cross(across, Vector3.up);
            point = outer - across * Mathf.Min(inset, band * .5f);
            return true;
        }
    }
}
