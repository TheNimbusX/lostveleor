using System;

namespace Game.View
{
    /// <summary>
    /// Детерминированный генератор чисел для расстановки эффектов света арены: одна и та же арена — одна и та
    /// же картина (съёмка «было → стало» сравнима). Структура без выделений, xorshift32.
    /// </summary>
    public struct ArenaMoodRandom
    {
        uint _state;

        public ArenaMoodRandom(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
            // Первые числа xorshift от близких зёрен похожи — прокрутка разводит их.
            NextUInt();
            NextUInt();
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Число в [0, 1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float from, float to) => from + (to - from) * Next01();

        /// <summary>Зерно из нескольких чисел (зерно раскладки, глубина, номер слоя).</summary>
        public static uint Mix(ulong seed, int a, int b = 0)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)(seed >> 32) * 0x85EBCA77u;
                h ^= (uint)a * 0xC2B2AE3Du;
                h = (h << 13 | h >> 19) * 5u + 0xE6546B64u;
                h ^= (uint)b * 0x27D4EB2Fu;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                return h == 0u ? 1u : h;
            }
        }
    }

    /// <summary>
    /// Полоса у кромки поляны, где живут пыльца и светлячки: «редко и по краю» (владелец отверг «конфетти»
    /// лагеря 16.09). Радиус меряется от края поляны в этом направлении: внутрь — долей радиуса, наружу — метрами.
    /// </summary>
    public struct ArenaMoodBand
    {
        /// <summary>Насколько полоса заходит внутрь поляны — доля радиуса края (0,3 — до 70 % радиуса).</summary>
        public float InsideShare;
        /// <summary>Насколько полоса уходит за край поляны, метры.</summary>
        public float Outside;
        /// <summary>Где частиц гуще: 0 — у внутренней границы, 1 — у внешней (треугольное распределение).</summary>
        public float Peak;
        public float MinHeight, MaxHeight;
    }

    /// <summary>Луч в кадре: верх и низ в долях экрана (0…1 снизу вверх) и толщина, метры.</summary>
    public struct ArenaMoodBeamSlot
    {
        public float TopX, TopY, BottomX, BottomY, Width;
    }

    /// <summary>
    /// Эффекты света арены без UnityEngine (световая арка акта I, план
    /// ART/UI/concepts-2026-10-01-style-shift/light-arc-plan.md, разделы 3.5–3.6 и 5): кольца дымки чащи,
    /// лучи в тумане, пыльца, искры, светлячки и фонари сумерек. Здесь только расстановка и числа: полоса
    /// у кромки, сдвиг дымки на ближней к камере стороне (иначе высокий слой ложится на пол боя), просвет
    /// у проходов к порталам, места лучей в верхней трети кадра, углы фонарей и мерцание. Проверки —
    /// tools/Combat.Presentation.Tests/ArenaMoodFxRulesTests.cs.
    /// </summary>
    public static class ArenaMoodFxRules
    {
        // ---- Дымка чащи ------------------------------------------------------------------------------

        /// <summary>Ряды кольца дымки от поляны наружу: доля ширины полосы и прозрачность.</summary>
        public static readonly float[] HazeRowT = { 0f, .14f, .36f, .68f, 1f };
        public static readonly float[] HazeRowAlpha = { 0f, .4f, .82f, 1f, .85f };
        public static int HazeRows => HazeRowT.Length;

        /// <summary>Слои дымки: у подножия, в середине крон, над кронами (метры) и их доля плотности.</summary>
        public static readonly float[] HazeHeights = { .6f, 2.5f, 5f };
        public static readonly float[] HazeLayerAlpha = { .55f, .4f, .3f };

        /// <summary>Просвет у прохода к порталу: дымка гаснет ближе этого к оси прохода и полная — дальше второго, метры.</summary>
        public const float PortalClearInner = 2.8f, PortalClearOuter = 6.5f;

        /// <summary>
        /// Насколько отодвинуть наружу слой на высоте <paramref name="height"/>, чтобы на экране он не заходил на
        /// поляну. Камера смотрит сверху под углом <paramref name="pitchDegrees"/>: точка на высоте h видна там же,
        /// где земля дальше от камеры на h / tg(наклона). На ближней к камере стороне это «дальше» — внутрь поляны.
        /// (<paramref name="dirX"/>, <paramref name="dirZ"/>) — направление от центра поляны, (<paramref name="forwardX"/>,
        /// <paramref name="forwardZ"/>) — куда смотрит камера по земле; оба единичные.
        /// </summary>
        public static float NearSidePush(float dirX, float dirZ, float forwardX, float forwardZ, float height, float pitchDegrees)
        {
            if (!(height > 0f)) return 0f;
            float pitch = Clamp(pitchDegrees, 5f, 89f) * (float)(Math.PI / 180.0);
            float shift = height / (float)Math.Tan(pitch);
            float radial = shift * (dirX * forwardX + dirZ * forwardZ);
            return radial < 0f ? -radial : 0f;
        }

        /// <summary>Расстояние от точки до отрезка (ax, az)–(bx, bz) на земле.</summary>
        public static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float length = dx * dx + dz * dz;
            float t = length > 1e-8f ? ((x - ax) * dx + (z - az) * dz) / length : 0f;
            t = Clamp(t, 0f, 1f);
            float px = ax + dx * t - x, pz = az + dz * t - z;
            return (float)Math.Sqrt(px * px + pz * pz);
        }

        /// <summary>
        /// Доля дымки у проходов к порталам: 0 на оси прохода, 1 — дальше <see cref="PortalClearOuter"/>.
        /// <paramref name="segments"/> — по 4 числа на проход (ax, az, bx, bz), <paramref name="count"/> проходов.
        /// Выход должен читаться: дымка не закрывает тропу к порталу.
        /// </summary>
        public static float PortalClearance(float x, float z, float[] segments, int count)
        {
            if (segments == null) return 1f;
            float clear = 1f;
            int n = Math.Min(count, segments.Length / 4);
            for (int i = 0; i < n; i++)
            {
                float d = SegmentDistance(x, z, segments[i * 4], segments[i * 4 + 1], segments[i * 4 + 2], segments[i * 4 + 3]);
                float t = SmoothStep(PortalClearInner, PortalClearOuter, d);
                if (t < clear) clear = t;
            }
            return clear;
        }

        public static int HazeVertexCount(int samples) => samples * HazeRows * HazeHeights.Length;
        public static int HazeIndexCount(int samples) => samples * (HazeRows - 1) * 6 * HazeHeights.Length;

        /// <summary>
        /// Кольца дымки вокруг поляны: <paramref name="samples"/> направлений (угол 0 — +x мира, π/2 — +z) ×
        /// <see cref="HazeRows"/> рядов × слои <see cref="HazeHeights"/>. Внутренний край — край поляны плюс
        /// <paramref name="inner"/> метров, внешний — плюс <paramref name="outer"/>; на ближней к камере стороне
        /// слой отодвигается на <see cref="NearSidePush"/>. Прозрачность — профиль рядов × доля слоя × просвет
        /// у проходов. Пишет xyz по 3 числа на вершину и прозрачность по одной; слои идут снизу вверх, чтобы
        /// ближний к камере верхний рисовался последним.
        /// </summary>
        public static void HazeRing(float[] contour, int samples, float centerX, float centerZ, float inner, float outer,
            float forwardX, float forwardZ, float pitchDegrees, float[] portalSegments, int portalCount,
            float[] xyz, float[] alpha)
        {
            if (contour == null || contour.Length < samples || samples < 3) throw new ArgumentException("мало направлений", nameof(contour));
            int vertices = HazeVertexCount(samples);
            if (xyz == null || xyz.Length < vertices * 3 || alpha == null || alpha.Length < vertices)
                throw new ArgumentException("массивы малы", nameof(xyz));
            if (!(outer > inner)) outer = inner + 1f;
            int rows = HazeRows, v = 0;
            for (int layer = 0; layer < HazeHeights.Length; layer++)
            {
                float height = HazeHeights[layer];
                for (int a = 0; a < samples; a++)
                {
                    double angle = a * 2 * Math.PI / samples;
                    float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                    float push = NearSidePush(dx, dz, forwardX, forwardZ, height, pitchDegrees);
                    float edge = Math.Max(0f, contour[a]);
                    float from = edge + inner + push, to = edge + outer + push;
                    for (int r = 0; r < rows; r++)
                    {
                        float radius = from + (to - from) * HazeRowT[r];
                        float x = centerX + dx * radius, z = centerZ + dz * radius;
                        xyz[v * 3] = x;
                        xyz[v * 3 + 1] = height;
                        xyz[v * 3 + 2] = z;
                        alpha[v] = HazeRowAlpha[r] * HazeLayerAlpha[layer] * PortalClearance(x, z, portalSegments, portalCount);
                        v++;
                    }
                }
            }
        }

        /// <summary>Треугольники колец <see cref="HazeRing"/>: ленты между соседними рядами, по кругу без шва.</summary>
        public static void HazeTriangles(int[] indices, int samples)
        {
            if (indices == null || indices.Length < HazeIndexCount(samples)) throw new ArgumentException("массив мал", nameof(indices));
            int rows = HazeRows, k = 0;
            for (int layer = 0; layer < HazeHeights.Length; layer++)
            {
                int start = layer * samples * rows;
                for (int a = 0; a < samples; a++)
                {
                    int b = (a + 1) % samples;
                    for (int r = 0; r < rows - 1; r++)
                    {
                        int i0 = start + a * rows + r, i1 = start + b * rows + r;
                        int i2 = start + b * rows + r + 1, i3 = start + a * rows + r + 1;
                        indices[k++] = i0; indices[k++] = i2; indices[k++] = i1;
                        indices[k++] = i0; indices[k++] = i3; indices[k++] = i2;
                    }
                }
            }
        }

        // ---- Пыльца, искры, светлячки --------------------------------------------------------------

        /// <summary>Пыльца дня: освещённая часть поляны у края и над кромкой.</summary>
        public static readonly ArenaMoodBand PollenBand = new ArenaMoodBand
            { InsideShare = .4f, Outside = 4f, Peak = .55f, MinHeight = .5f, MaxHeight = 2.4f };

        /// <summary>Светлячки сумерек: полоса у края и над кромкой, в середине боя их почти нет.</summary>
        public static readonly ArenaMoodBand FireflyBand = new ArenaMoodBand
            { InsideShare = .25f, Outside = 7f, Peak = .6f, MinHeight = .4f, MaxHeight = 2.1f };

        /// <summary>Треугольное распределение на [0, 1] с вершиной <paramref name="peak"/> (обратная функция).</summary>
        public static float Triangular(float u, float peak)
        {
            u = Clamp(u, 0f, 1f);
            peak = Clamp(peak, 0f, 1f);
            if (u < peak) return (float)Math.Sqrt(u * peak);
            return 1f - (float)Math.Sqrt((1f - u) * (1f - peak));
        }

        /// <summary>
        /// Расстояние от центра поляны для частицы в направлении, где край поляны в <paramref name="edgeRadius"/>
        /// метрах: от (1 − InsideShare)·край до край + Outside, гуще у <see cref="ArenaMoodBand.Peak"/>.
        /// </summary>
        public static float BandRadius(float edgeRadius, in ArenaMoodBand band, float u)
        {
            float edge = Math.Max(0f, edgeRadius);
            float from = edge * (1f - Clamp(band.InsideShare, 0f, 1f));
            float to = edge + Math.Max(0f, band.Outside);
            return from + (to - from) * Triangular(u, band.Peak);
        }

        public static float BandHeight(in ArenaMoodBand band, float u) =>
            band.MinHeight + (band.MaxHeight - band.MinHeight) * Clamp(u, 0f, 1f);

        /// <summary>Сколько частиц рождать в секунду, чтобы в среднем жило <paramref name="count"/>.</summary>
        public static float SteadyRate(int count, float meanLifetime) =>
            count <= 0 || !(meanLifetime > 0f) ? 0f : count / meanLifetime;

        /// <summary>
        /// Сколько из пыльцы настроения уходит искрами в лучи (туман), остальное — пыльца у кромки (день). Без
        /// лучей искр нет; иначе — доля тумана среди дня и тумана.
        /// </summary>
        public static int SparkCount(ArenaMoodWeights w, int pollenCount, int beamCount)
        {
            if (pollenCount <= 0 || beamCount <= 0) return 0;
            float light = w.Day + w.Mist;
            float share = light > 1e-5f ? w.Mist / light : 1f;
            int sparks = (int)Math.Round(pollenCount * Clamp(share, 0f, 1f), MidpointRounding.AwayFromZero);
            return Math.Min(pollenCount, Math.Max(0, sparks));
        }

        // ---- Лучи ------------------------------------------------------------------------------------

        /// <summary>Ниже этой доли высоты кадра лучей нет: середина кадра — бой и герой (аудит 23.09).</summary>
        public const float BeamFightLine = .62f;

        /// <summary>
        /// Место луча в кадре, как на рефе тумана: сверху-справа вниз-влево, верх за краем кадра, низ гаснет
        /// выше <see cref="BeamFightLine"/>. Лучи идут веером по правой половине, у каждой арены чуть по-своему.
        /// </summary>
        public static ArenaMoodBeamSlot BeamLayout(int index, int count, uint seed)
        {
            count = Math.Max(1, count);
            index = Math.Max(0, Math.Min(count - 1, index));
            var random = new ArenaMoodRandom(ArenaMoodRandom.Mix(seed, index, 0x6265616D));
            float span = .34f, start = .56f;
            float step = count > 1 ? span / (count - 1) : 0f;
            float topX = (count > 1 ? start + step * index : start + span * .5f) + random.Range(-.025f, .025f);
            float slant = random.Range(.14f, .2f);
            return new ArenaMoodBeamSlot
            {
                TopX = Clamp(topX, .5f, .95f),
                TopY = 1.12f + random.Range(0f, .05f),
                BottomX = Clamp(topX - slant, .3f, .9f),
                BottomY = BeamFightLine + random.Range(.01f, .05f),
                Width = random.Range(1f, 1.8f),
            };
        }

        /// <summary>«Дыхание» луча: доля яркости 0,75…1, у каждого луча своя фаза.</summary>
        public static float BeamBreath(float time, float phase) => .875f + .125f * (float)Math.Sin(time * 1.3f + phase);

        // ---- Фонари ----------------------------------------------------------------------------------

        /// <summary>Дуга, по которой стоят фонари: ± столько радиан от дальней от камеры стороны (ближняя — пустая).</summary>
        public const float LanternArc = (float)(150.0 * Math.PI / 180.0);

        /// <summary>Фонарь стоит за краем пола: метров за край поляны на первой попытке и шаг следующих.</summary>
        public const float LanternBeyond = 2.2f, LanternBeyondStep = .6f;

        /// <summary>Сколько мест пробовать для одного фонаря (стволы, вода, проходы к порталам).</summary>
        public const int LanternAttempts = 9;

        /// <summary>
        /// Угол фонаря <paramref name="slot"/> из <paramref name="count"/>, попытка <paramref name="attempt"/>:
        /// фонари поровну по дуге <see cref="LanternArc"/> вокруг дальней стороны <paramref name="farAngle"/>,
        /// с небольшим сдвигом <paramref name="jitter"/> (−1…1); следующие попытки — шаг вбок поочерёдно в обе
        /// стороны. Ближняя к камере сторона пустая: столб там закрыл бы пол боя.
        /// </summary>
        public static float LanternAngle(int slot, int count, int attempt, float farAngle, float jitter)
        {
            count = Math.Max(1, count);
            float spacing = 2f * LanternArc / count;
            float baseOffset = -LanternArc + spacing * (slot + .5f) + Clamp(jitter, -1f, 1f) * spacing * .2f;
            int step = (attempt + 1) / 2;
            float side = attempt % 2 == 1 ? 1f : -1f;
            float offset = baseOffset + side * step * spacing * .18f;
            offset = Clamp(offset, -LanternArc, LanternArc);
            return WrapAngle(farAngle + offset);
        }

        /// <summary>Сколько метров за край поляны на попытке <paramref name="attempt"/>.</summary>
        public static float LanternDistance(int attempt) => LanternBeyond + LanternBeyondStep * (Math.Max(0, attempt) % 3);

        /// <summary>Мерцание огня: множитель 1 ± <paramref name="amount"/>, два несвязанных колебания.</summary>
        public static float Flicker(float time, float phase, float amount)
        {
            float a = Clamp(amount, 0f, .5f);
            float wave = .6f * (float)Math.Sin(time * 2.3f + phase) + .4f * (float)Math.Sin(time * 5.7f + phase * 1.7f);
            return 1f + a * wave;
        }

        /// <summary>Наименьшая разница углов, радианы (0…π).</summary>
        public static float AngleBetween(float a, float b)
        {
            float d = Math.Abs(WrapAngle(a - b));
            return d > Math.PI ? (float)(2 * Math.PI) - d : d;
        }

        /// <summary>Угол в [0, 2π).</summary>
        public static float WrapAngle(float angle)
        {
            if (float.IsNaN(angle) || float.IsInfinity(angle)) return 0f;
            double turn = 2 * Math.PI;
            double a = angle % turn;
            if (a < 0) a += turn;
            return a >= turn ? 0f : (float)a;
        }

        // ---- мелочи ----------------------------------------------------------------------------------

        public static float SmoothStep(float from, float to, float x)
        {
            if (to - from < 1e-6f) return x < from ? 0f : 1f;
            float t = Clamp((x - from) / (to - from), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        static float Clamp(float x, float min, float max) => float.IsNaN(x) ? min : x < min ? min : x > max ? max : x;
    }
}
