using System;

namespace Game.View
{
    /// <summary>
    /// Числа и кривые «комикс-рисовки» мира без UnityEngine (владелец 01.10: «шифтануть общий визуал
    /// в другую рисовку, не меняя модели» → выбран микс, кадры ART/UI/concepts-2026-10-01-style-shift/
    /// 6-mix-with-our-hud.jpg и 4-mix-fight-comic-painted-diorama.jpg). Умолчания подобраны под эти кадры:
    /// тёплая тёмно-коричневая тушь средней толщины, 2–3 тона с тёплыми тенями, живописное сглаживание
    /// малым радиусом, лёгкое размытие диорамы только у верхнего и нижнего края.
    ///
    /// Шейдеры Hidden/Razlom/Comic Style и Comic Tilt Shift повторяют ToneLuminance, ToneTerrace,
    /// ShadowAmount, TonePivotFromKey и TiltShiftWeight строка в строку — при правке кривой менять обе
    /// стороны. Проверки —
    /// tools/Combat.Presentation.Tests/ComicStyleRulesTests.cs.
    /// </summary>
    public static class ComicStyleRules
    {
        /// <summary>Высота кадра, для которой заданы толщина туши, радиус кисти и размытие (1080p).</summary>
        public const float ReferenceHeight = 1080f;

        // ---- Тушь ------------------------------------------------------------------------------
        /// <summary>Цвет туши #2A1B12 — тёплый коричнево-чёрный, не серый (sRGB 0..1).</summary>
        public const float InkR = 42f / 255f, InkG = 27f / 255f, InkB = 18f / 255f;
        /// <summary>Тушь кроющая: на съёмке 01.10 линия 0,9 на тёмной земле арены почти терялась.</summary>
        public const float DefaultInkOpacity = 1f;
        /// <summary>
        /// Толщина линии в пикселях при 1080p: средняя, не «Borderlands». 2 px — верх диапазона 1,5–2:
        /// при 1,75 тушь на съёмке арены занимала 0,2 % кадра и читалась как случайная тень.
        /// </summary>
        public const float DefaultOutlineThickness = 2f, MinOutlineThickness = 0f, MaxOutlineThickness = 4f;
        /// <summary>Самое большое число колец выборки в шейдере (4K при толщине 3 → 6 px).</summary>
        public const int MaxInkRings = 6;
        /// <summary>
        /// Порог разрыва глубины, метры. Камера боя ортографическая, наклон 48°: пучок травы до 0,56 м
        /// даёт разрыв до ~0,75 м и тушью не обводится; корни, камни, герой и мобы (≥ 1,3 м) — обводятся.
        /// </summary>
        public const float DefaultDepthThreshold = .8f, DefaultDepthSoftness = .4f;
        /// <summary>
        /// Малый разрыв глубины, метры: гриб, куст, мелкий камень, ступня на земле. Такой разрыв обводится,
        /// только если рядом меняется и цвет (<see cref="DefaultColourGateLow"/>…High): пучок травы на траве
        /// остаётся чистым, а шляпка гриба на земле — с тушью. Порог не ниже крупного — малый выключен.
        /// </summary>
        public const float DefaultSmallDepthThreshold = .2f, DefaultSmallDepthSoftness = .1f;
        /// <summary>
        /// Порог излома нормали, градусы: контакт тела с землёй и рёбра камней. 35°, а не 50°: корень,
        /// лежащий на земле, под камерой 48° даёт на кромке излом ~42° и при 50° оставался без линии.
        /// </summary>
        public const float DefaultNormalThreshold = 35f, DefaultNormalSoftness = 10f;
        /// <summary>
        /// Излом нормали рисуется тушью только там, где рядом меняется и цвет: трава на траве остаётся
        /// чистой, камень и герой на траве — обведены. Разница цвета — в перцептивном пространстве 0..1.
        /// Съёмка 01.10: без ворот тушь ложится на пучки травы и камешки (2,1 % кадра, «грязь»), с
        /// воротами .08–.22 — почти только силуэты (0,35 %); .05–.15 даёт и внутренние линии мобов.
        /// </summary>
        public const float DefaultColourGateLow = .05f, DefaultColourGateHigh = .15f;

        // ---- Тон -------------------------------------------------------------------------------
        /// <summary>Доля, на которую тон внутри полосы стягивается к её середине (0 — выкл.).</summary>
        public const float DefaultToneStrength = .45f;
        /// <summary>Ширина тоновой полосы в стопах экспозиции: 1,25 стопа → на кадре 2–3 тона.</summary>
        public const float DefaultToneBandStops = 1.25f, MinToneBandStops = .25f, MaxToneBandStops = 4f;
        /// <summary>
        /// Яркость середины основной полосы (линейная, до тонмаппинга), если она задана рукой. Замер сцены в
        /// редакторе 01.10: медиана 0,08, 95-й процентиль 0,27 — поэтому 0,1, а не «серые» 0,18.
        /// </summary>
        public const float DefaultTonePivot = .1f, MinTonePivot = .005f, MaxTonePivot = 4f;
        /// <summary>
        /// По умолчанию середина полосы берётся по кадру: среднее геометрическое яркости мира (до
        /// прозрачных, без фона) × множитель. Тогда ступени ложатся одинаково и в вечернем лагере, и на
        /// солнечной поляне.
        /// </summary>
        public const bool DefaultToneAutoPivot = true;
        public const float DefaultTonePivotScale = 1f, MinTonePivotScale = .25f, MaxTonePivotScale = 4f;
        /// <summary>Жёсткость ступени: 0 — плавно, 1 — рубленый постеризатор.</summary>
        public const float DefaultToneHardness = .65f;
        /// <summary>
        /// Тёплый сдвиг теней (0..1) и цвет этого сдвига — оранжево-коричневый, не серый. Немного: на
        /// бурой земле арены .35 вместе с насыщенностью съедали четверть синего — кадр уходил в сепию.
        /// </summary>
        public const float DefaultShadowWarmth = .15f;
        public const float ShadowTintR = 1f, ShadowTintG = .78f, ShadowTintB = .6f;
        /// <summary>Добавка насыщенности в тенях: тень остаётся цветной, не серой.</summary>
        public const float DefaultShadowSaturation = .2f;
        /// <summary>Общий подъём насыщенности — маленький: палитра и так солнечная, а бурая земля от 1,2+ рыжеет.</summary>
        public const float DefaultSaturation = 1.1f, MaxSaturation = 2f;
        /// <summary>
        /// Общая яркость мира (множитель до тонмаппинга). Подъём выше 1 идёт с «плечом»
        /// (<see cref="BrightenPeak"/>): свет выше колена мягко сворачивается к 1,0 — порогу Bloom, поэтому
        /// белая рубаха героя не выгорает и не начинает светиться (ревью 24.09: героев не высветлять).
        /// 1,3: весь мир светлее на ~16 % по яркости кадра (съёмка арены 01.10: 0,218 → 0,254) — тушь и
        /// ступени тона не делают его мрачнее, а
        /// герой светлеет вместе с миром, а не отдельно от него. Концепт 6-mix светлее игры вдвое — это уже
        /// свет уровня, а не рисовка.
        /// </summary>
        public const float DefaultBrightness = 1.3f, MinBrightness = .5f, MaxBrightness = 3f;
        /// <summary>Колено плеча яркости (линейная яркость самого яркого канала).</summary>
        public const float DefaultHighlightKnee = .6f, MinHighlightKnee = .3f, MaxHighlightKnee = 1f;

        // ---- Живописное сглаживание (обобщённый фильтр Кувахары) -------------------------------
        /// <summary>Радиус кисти в пикселях при 1080p: малый, чтобы лица и кромки не плыли.</summary>
        public const float DefaultPainterlyRadius = 3f, MaxPainterlyRadius = 8f;
        public const float DefaultPainterlyStrength = .8f;
        /// <summary>Резкость выбора сектора (показатель q) и жёсткость по дисперсии.</summary>
        public const float DefaultPainterlySharpness = 8f, MinPainterlySharpness = 1f, MaxPainterlySharpness = 18f;
        public const float DefaultPainterlyHardness = 8f, MinPainterlyHardness = 1f, MaxPainterlyHardness = 100f;
        /// <summary>Крупнее этого радиуса (в пикселях кадра) кисть сама уходит в половинное разрешение.</summary>
        public const float HalfResolutionAbove = 4.5f;
        /// <summary>Предел ядра в шейдере: (2·6+1)² = 169 выборок.</summary>
        public const int MaxKernelRadius = 6;

        // ---- Диорама (tilt-shift) --------------------------------------------------------------
        /// <summary>Доля высоты кадра по центру, которая остаётся полностью резкой.</summary>
        public const float DefaultTiltSharpBand = .62f, MaxTiltSharpBand = .95f;
        /// <summary>Сила размытия у самой кромки кадра (0..1).</summary>
        public const float DefaultTiltStrength = .9f;
        /// <summary>Размытие кромки в пикселях при 1080p.</summary>
        public const float DefaultTiltBlur = 10f, MaxTiltBlur = 32f;
        /// <summary>Размытие считается в четверти разрешения.</summary>
        public const int TiltDownsample = 4;

        /// <summary>Значение в диапазоне; NaN и бесконечности — умолчание, а не мусор в шейдере.</summary>
        public static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }

        public static float Clamp01(float value, float fallback) => Clamp(value, 0f, 1f, fallback);

        /// <summary>Во сколько раз кадр выше 1080p. Толщина туши, радиус кисти и размытие растут с ним.</summary>
        public static float ResolutionScale(int height) => height > 0 ? Clamp(height / ReferenceHeight, .25f, 4f, 1f) : 1f;

        /// <summary>Толщина туши в пикселях этого кадра.</summary>
        public static float OutlinePixels(float thickness, int height)
        {
            float px = Clamp(thickness, MinOutlineThickness, MaxOutlineThickness, DefaultOutlineThickness) * ResolutionScale(height);
            return Math.Min(px, MaxInkRings);
        }

        /// <summary>Сколько колец выборки нужно шейдеру для линии толщиной <paramref name="pixels"/>.</summary>
        public static int InkRings(float pixels)
        {
            if (!(pixels > 0f)) return 0;
            return Math.Min(MaxInkRings, (int)Math.Ceiling(pixels - 1e-4f));
        }

        /// <summary>
        /// Вес кольца <paramref name="ring"/> (1..) у разрыва глубины. Линия ложится только на ближний
        /// предмет, поэтому кольца идут до полной толщины; дробный остаток — сглаженный край линии.
        /// </summary>
        public static float DepthRingWeight(float pixels, int ring) => ring < 1 ? 0f : Saturate(pixels - (ring - 1));

        /// <summary>Вес кольца у излома нормали: излом метится с обеих сторон, поэтому половина толщины.</summary>
        public static float NormalRingWeight(float pixels, int ring) => ring < 1 ? 0f : Saturate(pixels * .5f - (ring - 1));

        /// <summary>
        /// Вес размытия диорамы по высоте кадра <paramref name="v"/> (0 — низ, 1 — верх): ноль во всей
        /// резкой полосе по центру, плавный рост к кромкам, у самой кромки — <paramref name="strength"/>.
        /// Симметричен: верх и низ размыты одинаково.
        /// </summary>
        public static float TiltShiftWeight(float v, float sharpBand, float strength)
        {
            float band = Clamp(sharpBand, 0f, MaxTiltSharpBand, DefaultTiltSharpBand);
            float d = Math.Abs(Saturate(v) * 2f - 1f);
            float t = Saturate((d - band) / Math.Max(1e-4f, 1f - band));
            return SmoothStep01(t) * Clamp01(strength, DefaultTiltStrength);
        }

        /// <summary>Сигма размытия диорамы в текселях четвертного разрешения.</summary>
        public static float TiltBlurSigma(float blurPixels, int height) =>
            Clamp(blurPixels, 0f, MaxTiltBlur, DefaultTiltBlur) * ResolutionScale(height) / TiltDownsample;

        /// <summary>
        /// Ступень внутри одной тоновой полосы: <paramref name="f"/> 0..1 → 0..1. Середина полосы плоская
        /// (0,5), переход к соседней — у её краёв; на краях кривая непрерывна (0 и 1).
        /// </summary>
        public static float ToneTerrace(float f, float hardness)
        {
            float w = Math.Max(1e-3f, 1f - Clamp01(hardness, DefaultToneHardness));
            float t = Saturate(f) - .5f;
            float s = SmoothStep(.5f - .5f * w, .5f, Math.Abs(t));
            return .5f + Math.Sign(t) * .5f * s;
        }

        /// <summary>
        /// Новая яркость пикселя: полосы шириной <paramref name="stops"/> стопа в логарифме яркости,
        /// середина основной полосы — <paramref name="pivot"/>. Тон стягивается к середине своей полосы на
        /// <paramref name="strength"/>. Полоса симметрична в стопах: в среднем кадр не темнеет и не светлеет,
        /// чёрного и белого пятна ступень не даёт.
        /// </summary>
        public static float ToneLuminance(float luminance, float pivot, float stops, float hardness, float strength)
        {
            if (!(luminance > 0f)) return 0f;
            float k = Clamp01(strength, DefaultToneStrength);
            if (k <= 0f) return luminance;
            float p0 = Clamp(pivot, MinTonePivot, MaxTonePivot, DefaultTonePivot);
            float s = Clamp(stops, MinToneBandStops, MaxToneBandStops, DefaultToneBandStops);
            float l = Math.Max(luminance, 1e-4f);
            float p = (float)(Math.Log(l / p0, 2.0) / s) + .5f;
            float i = (float)Math.Floor(p);
            float q = i + ToneTerrace(p - i, hardness);
            float stepped = p + (q - p) * k;
            return p0 * (float)Math.Pow(2.0, (stepped - .5f) * s);
        }

        /// <summary>
        /// Насколько пиксель в тени: 0 — вся основная полоса и светлее (тёплым становится только настоящая
        /// тень, а не всё, что чуть темнее середины), 1 — от середины полосы ниже.
        /// </summary>
        public static float ShadowAmount(float luminance, float pivot, float stops)
        {
            if (!(luminance > 0f)) return 1f;
            float p0 = Clamp(pivot, MinTonePivot, MaxTonePivot, DefaultTonePivot);
            float s = Clamp(stops, MinToneBandStops, MaxToneBandStops, DefaultToneBandStops);
            float below = (float)(-Math.Log(luminance / p0, 2.0) / s);
            return Saturate((below - .5f) * 2f);
        }

        /// <summary>
        /// Яркость самого яркого канала после подъёма в <paramref name="gain"/> раз. До колена — просто
        /// множитель; выше — экспоненциальное плечо к 1,0 (порог Bloom). Свет, что и без подъёма был ярче
        /// (эмиссия, блики), не гасится. При <paramref name="gain"/> ≤ 1 — чистый множитель.
        /// </summary>
        public static float BrightenPeak(float peak, float gain, float knee)
        {
            if (!(peak > 0f)) return 0f;
            float g = Clamp(gain, MinBrightness, MaxBrightness, DefaultBrightness);
            float lifted = peak * g;
            float k = Clamp(knee, MinHighlightKnee, MaxHighlightKnee, DefaultHighlightKnee);
            if (g <= 1f || lifted <= k) return lifted;
            float head = Math.Max(1e-3f, 1f - k);
            float rolled = k + head * (1f - (float)Math.Exp(-(lifted - k) / head));
            return Math.Max(rolled, peak);
        }

        /// <summary>
        /// Середина основной полосы по кадру: <paramref name="logKey"/> — средний log2 яркости мира,
        /// <paramref name="scale"/> — множитель. Пустой кадр (NaN) — ручное умолчание.
        /// </summary>
        public static float TonePivotFromKey(float logKey, float scale)
        {
            if (float.IsNaN(logKey) || float.IsInfinity(logKey)) return DefaultTonePivot;
            float k = Clamp(scale, MinTonePivotScale, MaxTonePivotScale, DefaultTonePivotScale);
            return Clamp((float)Math.Pow(2.0, logKey) * k, MinTonePivot, MaxTonePivot, DefaultTonePivot);
        }

        /// <summary>
        /// Как считать кисть: радиус ядра в текселях и нужно ли половинное разрешение. Радиус 0 — кисть
        /// выключена. На 4K радиус 3 → 6 px кадра → половинное разрешение с ядром 3: цена как у 1080p.
        /// </summary>
        public static int PainterlyKernel(float radius, float strength, int height, bool forceHalf, out bool half)
        {
            half = false;
            float r = Clamp(radius, 0f, MaxPainterlyRadius, DefaultPainterlyRadius);
            if (r <= 0f || !(Clamp01(strength, DefaultPainterlyStrength) > 0f)) return 0;
            float pixels = r * ResolutionScale(height);
            half = forceHalf || pixels > HalfResolutionAbove;
            int kernel = (int)Math.Round(half ? pixels * .5f : pixels, MidpointRounding.AwayFromZero);
            return Math.Max(1, Math.Min(MaxKernelRadius, kernel));
        }

        /// <summary>Выборок ядра кисти в самом большом радиусе: (2·6+1)² = 169.</summary>
        public const int MaxKernelTaps = (2 * MaxKernelRadius + 1) * (2 * MaxKernelRadius + 1);

        /// <summary>
        /// Веса восьми секторов обобщённого Кувахары для каждой выборки ядра радиуса
        /// <paramref name="radius"/>, уже умноженные на гауссиану: восемь чисел на выборку, строки y от −r
        /// до r, внутри — x от −r до r. Веса зависят только от смещения, а не от картинки, поэтому
        /// считаются здесь один раз, а шейдер только умножает. Формула — та, что была в шейдере
        /// (полиномиальные секторы, нуль на 0,58 рад): кадр от переноса не меняется. Возвращает число выборок.
        /// </summary>
        public static int PainterlyTapWeights(int radius, float[] weights)
        {
            int r = Math.Max(1, Math.Min(MaxKernelRadius, radius));
            int taps = (2 * r + 1) * (2 * r + 1);
            if (weights == null || weights.Length < taps * 8) throw new ArgumentException("нужно 8 весов на выборку", nameof(weights));
            float zeta = 2f / r;
            const float zeroCross = .58f;
            float sinZero = (float)Math.Sin(zeroCross);
            float eta = (zeta + (float)Math.Cos(zeroCross)) / (sinZero * sinZero);
            var w = new float[8];
            int tap = 0;
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++, tap++)
            {
                float vx = (float)x / r, vy = (float)y / r;
                float vxx = zeta - eta * vx * vx, vyy = zeta - eta * vy * vy;
                w[0] = Sq(Math.Max(0f, vy + vxx));
                w[2] = Sq(Math.Max(0f, -vx + vyy));
                w[4] = Sq(Math.Max(0f, -vy + vxx));
                w[6] = Sq(Math.Max(0f, vx + vyy));
                float rx = .70710678f * (vx - vy), ry = .70710678f * (vx + vy);
                vxx = zeta - eta * rx * rx;
                vyy = zeta - eta * ry * ry;
                w[1] = Sq(Math.Max(0f, ry + vxx));
                w[3] = Sq(Math.Max(0f, -rx + vyy));
                w[5] = Sq(Math.Max(0f, -ry + vxx));
                w[7] = Sq(Math.Max(0f, rx + vyy));
                float sum = 0f;
                for (int i = 0; i < 8; i++) sum += w[i];
                float g = (float)Math.Exp(-3.125f * (rx * rx + ry * ry)) / Math.Max(sum, 1e-6f);
                for (int i = 0; i < 8; i++) weights[tap * 8 + i] = w[i] * g;
            }
            return taps;
        }

        static float Sq(float x) => x * x;

        static float Saturate(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        static float SmoothStep01(float t) => t * t * (3f - 2f * t);

        static float SmoothStep(float a, float b, float x)
        {
            if (b <= a) return x >= b ? 1f : 0f;
            return SmoothStep01(Saturate((x - a) / (b - a)));
        }
    }
}
