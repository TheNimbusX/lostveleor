using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Какой свет арены просили: выключен, по номеру арены или принудительно.</summary>
    public enum ArenaMoodMode : byte
    {
        /// <summary>Свет как сегодня: настроение не применяется.</summary>
        Off = 0,
        /// <summary>По номеру арены из таблицы профиля (босс — последняя строка).</summary>
        Auto = 1,
        Day = 2,
        Mist = 3,
        Dusk = 4,
        /// <summary>Сумерки с акцентом босса — для съёмки и F8 на любой арене.</summary>
        Boss = 5,
        /// <summary>Своё число на оси «день 0 → туман 1 → сумерки 2».</summary>
        Axis = 6,
    }

    /// <summary>
    /// Веса трёх пресетов настроения: день, туман, сумерки. Сумма — 1, ненулевых не больше двух
    /// (соседних по оси), поэтому смесь — обычная кусочно-линейная интерполяция внутри своей трети.
    /// </summary>
    public readonly struct ArenaMoodWeights : IEquatable<ArenaMoodWeights>
    {
        public readonly float Day, Mist, Dusk;

        public ArenaMoodWeights(float day, float mist, float dusk)
        {
            Day = day;
            Mist = mist;
            Dusk = dusk;
        }

        /// <summary>Сумерки берут верх: светлячки, фонари, синяя тушь.</summary>
        public bool IsDusk => Dusk >= ArenaMoodRules.DuskThreshold;

        /// <summary>Смесь трёх чисел пресетов этими весами.</summary>
        public float Blend(float day, float mist, float dusk) => Day * day + Mist * mist + Dusk * dusk;

        public bool Equals(ArenaMoodWeights other) => Day == other.Day && Mist == other.Mist && Dusk == other.Dusk;
        public override bool Equals(object obj) => obj is ArenaMoodWeights other && Equals(other);
        public override int GetHashCode() => (Day.GetHashCode() * 397 ^ Mist.GetHashCode()) * 397 ^ Dusk.GetHashCode();
        public override string ToString() => $"{Day:0.##}/{Mist:0.##}/{Dusk:0.##}";
    }

    /// <summary>Числа пятна поляны и кружева листвы для запекания «прожектора» (свет солнца по земле).</summary>
    public struct ArenaGladeLightParams
    {
        /// <summary>Глубина тени за краем поляны: 0 — без пятна, 1 — за краем прямого солнца нет.</summary>
        public float SpotStrength;
        /// <summary>Сила кружева листвы: на сколько гаснет солнце в тени кроны.</summary>
        public float DappleStrength;
        /// <summary>Доля кружева внутри поляны (0 — пол боя без пятен, 1 — как в лесу).</summary>
        public float DappleInside;
        /// <summary>Размер пятна кружева, метры.</summary>
        public float DappleScale;
        /// <summary>Поле поляны (≤ 1 — внутри), с которого свет начинает гаснуть, и где он гаснет весь.</summary>
        public float SpotInnerField, SpotOuterField;
        /// <summary>Свой рисунок кружева у каждой арены.</summary>
        public uint Seed;
    }

    /// <summary>
    /// Световая арка акта I без UnityEngine (владелец 01.10: «рефы просто ахуенно сделаны… хочется добиться
    /// этого в нашей игре», ART/UI/concepts-2026-10-01-style-shift/8-light-arc-act1-labeled.jpg, план —
    /// light-arc-plan.md там же). Арены 1–3 — тёплый золотой день, 4–6 — туман сгущается, 7–8 и босс —
    /// сумерки. Здесь: положение арены на оси «день 0 → туман 1 → сумерки 2», веса пресетов, разбор ключа
    /// съёмки, смесь ручек комикс-рисовки и запекание «прожектора» поляны (пятно света по настоящей форме
    /// поляны и кружево листвы) — сначала в мировой сетке на рабочем потоке, потом в плоскости солнца.
    /// Проверки — tools/Combat.Presentation.Tests/ArenaMoodRulesTests.cs.
    /// </summary>
    public static class ArenaMoodRules
    {
        public const float DayAxis = 0f, MistAxis = 1f, DuskAxis = 2f, MaxAxis = 2f;

        /// <summary>С этого веса сумерек кадр считается сумеречным (светлячки, фонари).</summary>
        public const float DuskThreshold = .5f;

        /// <summary>
        /// Ось по аренам 1…8 и босс (последняя строка): туман сгущается внутри своей трети, шаг между
        /// третями не рывок. Ровные три ступени — 0 / 1 / 2 в таблице профиля.
        /// </summary>
        public static readonly float[] DefaultDepthBlend = { 0f, 0f, .15f, .7f, 1f, 1.3f, 1.85f, 2f, 2f };

        /// <summary>Поле поляны за рамкой её радиусов растёт на столько за каждый радиус наружу.</summary>
        public const float OutsideFieldSlope = 4f;

        /// <summary>Сколько направлений в полярном контуре поляны (шаг 5°).</summary>
        public const int ContourSamples = 72;

        // ---- Ось и веса ---------------------------------------------------------------------------

        /// <summary>Ось в пределах 0…2; NaN и бесконечности — день.</summary>
        public static float ClampAxis(float axis)
        {
            if (float.IsNaN(axis) || float.IsInfinity(axis)) return DayAxis;
            return axis < DayAxis ? DayAxis : axis > MaxAxis ? MaxAxis : axis;
        }

        /// <summary>Веса пресетов в точке оси: 0 — день, 1 — туман, 2 — сумерки, между ними — линейно.</summary>
        public static ArenaMoodWeights Weights(float axis)
        {
            float a = ClampAxis(axis);
            if (a <= MistAxis) return new ArenaMoodWeights(1f - a, a, 0f);
            float t = a - MistAxis;
            return new ArenaMoodWeights(0f, 1f - t, t);
        }

        /// <summary>
        /// Ось арены <paramref name="depth"/> (с единицы). Босс берёт последнюю строку таблицы, обычная
        /// арена — свою, а глубже таблицы — последнюю не-боссовую. Пустая таблица — умолчание.
        /// </summary>
        public static float AxisForDepth(int depth, bool boss, float[] table)
        {
            float[] t = table != null && table.Length > 0 ? table : DefaultDepthBlend;
            if (boss) return ClampAxis(t[t.Length - 1]);
            int last = t.Length >= 2 ? t.Length - 2 : 0;
            int index = depth - 1;
            if (index < 0) index = 0;
            if (index > last) index = last;
            return ClampAxis(t[index]);
        }

        /// <summary>Ось с учётом выбора F8 / ключа съёмки. Off — день (настроение всё равно не применяется).</summary>
        public static float AxisFor(ArenaMoodMode mode, float axisOverride, int depth, bool boss, float[] table)
        {
            switch (mode)
            {
                case ArenaMoodMode.Auto: return AxisForDepth(depth, boss, table);
                case ArenaMoodMode.Mist: return MistAxis;
                case ArenaMoodMode.Dusk:
                case ArenaMoodMode.Boss: return DuskAxis;
                case ArenaMoodMode.Axis: return ClampAxis(axisOverride);
                default: return DayAxis;
            }
        }

        /// <summary>Акцент босса: на арене босса по номеру или принудительно режимом Boss.</summary>
        public static bool BossAccentFor(ArenaMoodMode mode, bool boss) =>
            mode == ArenaMoodMode.Boss || (mode == ArenaMoodMode.Auto && boss);

        /// <summary>
        /// Значение ключа -capture-mood: off / auto / day / mist / dusk / boss или число оси (0…2, точка или
        /// запятая). Регистр не важен. Не разобрали — false.
        /// </summary>
        public static bool TryParse(string value, out ArenaMoodMode mode, out float axis)
        {
            mode = ArenaMoodMode.Off;
            axis = 0f;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            switch (v)
            {
                case "off": case "none": case "было": mode = ArenaMoodMode.Off; return true;
                case "auto": case "on": mode = ArenaMoodMode.Auto; return true;
                case "day": case "день": mode = ArenaMoodMode.Day; return true;
                case "mist": case "fog": case "туман": mode = ArenaMoodMode.Mist; return true;
                case "dusk": case "сумерки": mode = ArenaMoodMode.Dusk; return true;
                case "boss": case "босс": mode = ArenaMoodMode.Boss; return true;
            }
            if (float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
            {
                mode = ArenaMoodMode.Axis;
                axis = ClampAxis(parsed);
                return true;
            }
            return false;
        }

        /// <summary>Имя режима для журнала и меню.</summary>
        public static string Name(ArenaMoodMode mode)
        {
            switch (mode)
            {
                case ArenaMoodMode.Off: return "выкл";
                case ArenaMoodMode.Auto: return "авто";
                case ArenaMoodMode.Day: return "день";
                case ArenaMoodMode.Mist: return "туман";
                case ArenaMoodMode.Dusk: return "сумерки";
                case ArenaMoodMode.Boss: return "босс";
                default: return "ось";
            }
        }

        /// <summary>Имя точки оси: «день», «туман», «сумерки» или «день→туман 0,70».</summary>
        public static string AxisName(float axis)
        {
            float a = ClampAxis(axis);
            if (a <= 0f) return "день";
            if (a == MistAxis) return "туман";
            if (a >= MaxAxis) return "сумерки";
            return a < MistAxis ? $"день→туман {a:0.00}" : $"туман→сумерки {a - MistAxis:0.00}";
        }

        // ---- Смесь ручек ----------------------------------------------------------------------------

        /// <summary>Целое число пресетов (фонари, светлячки) этими весами — округлённое, не меньше нуля.</summary>
        public static int BlendCount(ArenaMoodWeights w, int day, int mist, int dusk)
        {
            float v = w.Blend(day, mist, dusk);
            return v <= 0f || float.IsNaN(v) ? 0 : (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Насколько настроение перекрывает ручку комикс-рисовки: веса × «доля» каждого пресета. День с долей 0
        /// оставляет ручку как в настройках рисовки — их умолчания этим не меняются.
        /// </summary>
        public static float OverlayAmount(ArenaMoodWeights w, float dayAmount, float mistAmount, float duskAmount) =>
            Saturate(w.Blend(Saturate(dayAmount), Saturate(mistAmount), Saturate(duskAmount)));

        /// <summary>
        /// Цель перекрытия: смесь значений пресетов, взвешенная их долями. Если ни один пресет не перекрывает —
        /// <paramref name="fallback"/>.
        /// </summary>
        public static float OverlayTarget(ArenaMoodWeights w, float dayAmount, float mistAmount, float duskAmount,
            float day, float mist, float dusk, float fallback)
        {
            float a = Saturate(dayAmount) * w.Day, b = Saturate(mistAmount) * w.Mist, c = Saturate(duskAmount) * w.Dusk;
            float sum = a + b + c;
            if (!(sum > 1e-5f)) return fallback;
            return (a * day + b * mist + c * dusk) / sum;
        }

        // ---- Поляна: поле, пятно света, кружево -----------------------------------------------------

        /// <summary>
        /// Поле поляны (то же, что у симуляции и покраски земли: ≤ 1 — внутри) в точке (x, z) мира, но гладкое
        /// и за рамкой её радиусов: там <see cref="GladeRegion.Field"/> скачком отдаёт 2, а пятну света нужен
        /// мягкий край. Снаружи рамки — поле на рамке плюс <see cref="OutsideFieldSlope"/> на каждый радиус.
        /// </summary>
        public static float GladeField(in GladeRegion glade, double x, double z)
        {
            double cx = glade.Center.X.ToDouble(), cz = glade.Center.Y.ToDouble();
            double rx = Math.Max(1e-3, glade.Radii.X.ToDouble()), rz = Math.Max(1e-3, glade.Radii.Y.ToDouble());
            double nx = (x - cx) / rx, nz = (z - cz) / rz;
            const double edge = 1 - 1e-6;
            double ex = Math.Max(0, Math.Abs(nx) - edge), ez = Math.Max(0, Math.Abs(nz) - edge);
            if (ex > 0) nx = Math.Sign(nx) * edge;
            if (ez > 0) nz = Math.Sign(nz) * edge;
            var point = new FixVec2(Fix64.FromDouble(cx + nx * rx), Fix64.FromDouble(cz + nz * rz));
            float field = glade.Field(point).ToFloat();
            if (ex > 0 || ez > 0) field += (float)(OutsideFieldSlope * Math.Sqrt(ex * ex + ez * ez));
            return field;
        }

        /// <summary>Доля «внутри поляны» для пятна света: 1 до <paramref name="inner"/>, 0 от <paramref name="outer"/>.</summary>
        public static float GladeInside(float field, float inner, float outer)
        {
            if (float.IsNaN(field)) return 0f;
            float a = Math.Min(inner, outer), b = Math.Max(inner, outer);
            if (b - a < 1e-4f) return field <= a ? 1f : 0f;
            return 1f - SmoothStep01(Saturate((field - a) / (b - a)));
        }

        /// <summary>
        /// Кружево листвы в точке мира: 1 — луч сквозь листву, 0 — тень кроны, мягкие пятна размером
        /// около <paramref name="scale"/> метров. Рисунок привязан к земле и зависит от <paramref name="seed"/>.
        /// </summary>
        public static float Dapple(double x, double z, uint seed, float scale)
        {
            double s = scale > .05f ? scale : .05;
            double u = x / s, v = z / s;
            float n = .60f * ValueNoise(u, v, seed)
                      + .30f * ValueNoise(u * 2.13 + 17.1, v * 2.13 - 3.7, seed + 0x9E37u)
                      + .10f * ValueNoise(u * 4.41 - 9.3, v * 4.41 + 5.9, seed + 0x79B9u);
            // Широкий переход: мягкие пятна, а не камуфляж с резкой кромкой.
            return SmoothStep01(Saturate((n - .30f) / .42f));
        }

        /// <summary>
        /// Свет солнца на земле в точке поляны: пятно (поляна светла, за краем — тень на
        /// <see cref="ArenaGladeLightParams.SpotStrength"/>) × кружево листвы (внутри поляны — только своя доля).
        /// </summary>
        public static float GroundLight(float field, float dapple, in ArenaGladeLightParams p)
        {
            float inside = GladeInside(field, p.SpotInnerField, p.SpotOuterField);
            float spot = 1f - Saturate(p.SpotStrength) * (1f - inside);
            float inShare = Saturate(p.DappleInside);
            float amount = Saturate(p.DappleStrength) * (inShare + (1f - inShare) * (1f - inside));
            float leaf = 1f - amount * (1f - Saturate(dapple));
            return Saturate(spot * leaf);
        }

        /// <summary>Свет далеко за поляной: тень края и среднее кружево (за сеткой и на рамке текстуры).</summary>
        public static float OutsideLight(in ArenaGladeLightParams p) =>
            Saturate((1f - Saturate(p.SpotStrength)) * (1f - Saturate(p.DappleStrength) * .5f));

        /// <summary>
        /// Мировая сетка света по земле: <paramref name="n"/>×<paramref name="n"/> узлов от
        /// (<paramref name="minX"/>, <paramref name="minZ"/>) с шагом <paramref name="step"/> метров, строка — z.
        /// Не зависит от солнца, поэтому считается на рабочем потоке, пока арена собирается под завесой.
        /// </summary>
        public static void BakeGround(float[] ground, int n, double minX, double minZ, double step,
            GladeRegion glade, in ArenaGladeLightParams p)
        {
            if (ground == null || n <= 0 || ground.Length < n * n) throw new ArgumentException("сетка мала", nameof(ground));
            for (int j = 0; j < n; j++)
            {
                double z = minZ + j * step;
                for (int i = 0; i < n; i++)
                {
                    double x = minX + i * step;
                    float field = GladeField(glade, x, z);
                    float dapple = p.DappleStrength > 0f ? Dapple(x, z, p.Seed, p.DappleScale) : 1f;
                    ground[j * n + i] = GroundLight(field, dapple, p);
                }
            }
        }

        /// <summary>Свет сетки в точке мира (билинейно); за сеткой — <paramref name="outside"/>.</summary>
        public static float SampleGround(float[] ground, int n, double minX, double minZ, double step, double x, double z,
            float outside)
        {
            double gx = (x - minX) / step, gz = (z - minZ) / step;
            if (!(gx >= 0) || !(gz >= 0) || gx > n - 1 || gz > n - 1) return outside;
            int i = Math.Min((int)gx, n - 2), j = Math.Min((int)gz, n - 2);
            if (n < 2) return ground[0];
            float fx = (float)(gx - i), fz = (float)(gz - j);
            int k = j * n + i;
            float a = ground[k] + (ground[k + 1] - ground[k]) * fx;
            float b = ground[k + n] + (ground[k + n + 1] - ground[k + n]) * fx;
            return a + (b - a) * fz;
        }

        /// <summary>
        /// Текстура «прожектора» в плоскости солнца (cookie направленного света URP): тексель (i, j) —
        /// точка плоскости света offset + (uv − ½)·size, луч вдоль солнца до земли y = 0, свет сетки там.
        /// <paramref name="lightToWorld"/> — 12 чисел матрицы света 3×4 по строкам (m00…m23). Рамка в два
        /// текселя — ровный свет «далеко за поляной»: у основного света URP повтор не включён, край
        /// растягивается дальше. Строка 0 — низ текстуры (v = 0).
        /// </summary>
        public static void BakeCookie(byte[] cookie, int resolution, float[] lightToWorld, double offsetX, double offsetY,
            double size, float[] ground, int n, double minX, double minZ, double step, float outside)
        {
            if (cookie == null || resolution <= 0 || cookie.Length < resolution * resolution)
                throw new ArgumentException("текстура мала", nameof(cookie));
            if (lightToWorld == null || lightToWorld.Length < 12) throw new ArgumentException("нужна матрица 3×4", nameof(lightToWorld));
            float m00 = lightToWorld[0], m01 = lightToWorld[1], m02 = lightToWorld[2], m03 = lightToWorld[3];
            float m10 = lightToWorld[4], m11 = lightToWorld[5], m12 = lightToWorld[6], m13 = lightToWorld[7];
            float m20 = lightToWorld[8], m21 = lightToWorld[9], m22 = lightToWorld[10], m23 = lightToWorld[11];
            byte rim = ToByte(outside);
            bool grazing = Math.Abs(m12) < 1e-4f;
            for (int j = 0; j < resolution; j++)
            {
                double ly = offsetY + ((j + .5) / resolution - .5) * size;
                for (int i = 0; i < resolution; i++)
                {
                    int index = j * resolution + i;
                    if (grazing || i < 2 || j < 2 || i >= resolution - 2 || j >= resolution - 2)
                    {
                        cookie[index] = rim;
                        continue;
                    }
                    double lx = offsetX + ((i + .5) / resolution - .5) * size;
                    // Глубина вдоль солнца, на которой луч этой точки плоскости света касается земли.
                    double depth = -(m10 * lx + m11 * ly + m13) / m12;
                    double x = m00 * lx + m01 * ly + m02 * depth + m03;
                    double z = m20 * lx + m21 * ly + m22 * depth + m23;
                    cookie[index] = ToByte(SampleGround(ground, n, minX, minZ, step, x, z, outside));
                }
            }
        }

        /// <summary>
        /// Полярный контур поляны: расстояние от центра до края (поле = 1) по <see cref="ContourSamples"/>
        /// направлениям, угол 0 — +x мира, π/2 — +z. Для дымки и светлячков у кромки.
        /// </summary>
        public static void Contour(float[] radii, GladeRegion glade)
        {
            if (radii == null || radii.Length < ContourSamples) throw new ArgumentException("мало направлений", nameof(radii));
            double cx = glade.Center.X.ToDouble(), cz = glade.Center.Y.ToDouble();
            double reach = Math.Max(glade.Radii.X.ToDouble(), glade.Radii.Y.ToDouble()) * 1.6 + 1;
            double step = Math.Max(.05, reach / 160);
            for (int a = 0; a < ContourSamples; a++)
            {
                double angle = a * 2 * Math.PI / ContourSamples;
                double dx = Math.Cos(angle), dz = Math.Sin(angle);
                double previous = 0, previousField = GladeField(glade, cx, cz);
                double found = reach;
                for (double r = step; r <= reach; r += step)
                {
                    double f = GladeField(glade, cx + dx * r, cz + dz * r);
                    if (f >= 1)
                    {
                        double t = f - previousField > 1e-6 ? (1 - previousField) / (f - previousField) : 0;
                        found = previous + (r - previous) * Math.Max(0, Math.Min(1, t));
                        break;
                    }
                    previous = r;
                    previousField = f;
                }
                radii[a] = (float)found;
            }
        }

        /// <summary>Расстояние до края поляны по направлению <paramref name="angle"/> (радианы) из полярного контура.</summary>
        public static float ContourRadius(float[] radii, float angle)
        {
            if (radii == null || radii.Length == 0) return 0f;
            int count = Math.Min(radii.Length, ContourSamples);
            double turns = angle / (2 * Math.PI);
            double f = (turns - Math.Floor(turns)) * count;
            if (double.IsNaN(f)) return radii[0];
            int i = (int)f % count, k = (i + 1) % count;
            float t = (float)(f - Math.Floor(f));
            return radii[i] + (radii[k] - radii[i]) * t;
        }

        // ---- мелочи --------------------------------------------------------------------------------

        static byte ToByte(float v)
        {
            if (!(v > 0f)) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + .5f);
        }

        static float ValueNoise(double x, double y, uint seed)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int ix = (int)fx, iy = (int)fy;
            float tx = (float)(x - fx), ty = (float)(y - fy);
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            float top = a + (b - a) * tx, bottom = c + (d - c) * tx;
            return top + (bottom - top) * ty;
        }

        static float Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ seed * 0xCB1AB31Fu;
                h ^= h >> 13;
                h *= 0x5BD1E995u;
                h ^= h >> 15;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        static float Saturate(float x) => x < 0f ? 0f : x > 1f ? 1f : float.IsNaN(x) ? 0f : x;

        static float SmoothStep01(float t) => t * t * (3f - 2f * t);
    }
}
