using System;
using Game.View;
using NUnit.Framework;

// Комикс-рисовка мира (проба 01.10, кадр ART/UI/concepts-2026-10-01-style-shift/6-mix-with-our-hud.jpg):
// умолчания под кадр, толщина туши по разрешению, ступени тона без затемнения, вес диорамы, кисть.
// Кривые повторены в Hidden/Razlom/Comic Style и Comic Tilt Shift — ComicStyleRules.
public sealed class ComicStyleRulesTests
{
    [Test]
    public void DefaultsFollowTheReferenceFrame()
    {
        // Тушь #2A1B12 — тёплая: красный > зелёный > синий, не серая.
        Assert.That(ComicStyleRules.InkR * 255f, Is.EqualTo(42f).Within(1e-3f));
        Assert.That(ComicStyleRules.InkG * 255f, Is.EqualTo(27f).Within(1e-3f));
        Assert.That(ComicStyleRules.InkB * 255f, Is.EqualTo(18f).Within(1e-3f));
        Assert.That(ComicStyleRules.InkR, Is.GreaterThan(ComicStyleRules.InkG));
        Assert.That(ComicStyleRules.InkG, Is.GreaterThan(ComicStyleRules.InkB));
        // Средняя толщина 1,5–2 px при 1080p, не «Borderlands».
        Assert.That(ComicStyleRules.DefaultOutlineThickness, Is.InRange(1.5f, 2f));
        // Героев не высветлять (ревью 24.09): мир светлеет умеренно и целиком, белое — под плечом ниже
        // порога Bloom; насыщенность — небольшой подъём.
        Assert.That(ComicStyleRules.DefaultBrightness, Is.InRange(1f, 1.5f));
        Assert.That(ComicStyleRules.BrightenPeak(1f, ComicStyleRules.DefaultBrightness, ComicStyleRules.DefaultHighlightKnee),
            Is.LessThanOrEqualTo(1f));
        Assert.That(ComicStyleRules.DefaultSaturation, Is.GreaterThan(1f).And.LessThanOrEqualTo(1.3f));
        // Тени тёплые: оттенок тени красный > зелёный > синий.
        Assert.That(ComicStyleRules.ShadowTintR, Is.GreaterThan(ComicStyleRules.ShadowTintG));
        Assert.That(ComicStyleRules.ShadowTintG, Is.GreaterThan(ComicStyleRules.ShadowTintB));
        // Диорама трогает только кромки: больше половины кадра по центру резкое.
        Assert.That(ComicStyleRules.DefaultTiltSharpBand, Is.GreaterThan(.5f));
        // Кисть — малым радиусом.
        Assert.That(ComicStyleRules.DefaultPainterlyRadius, Is.InRange(2f, 4f));
    }

    [Test]
    public void GrassTuftsStayWithoutInkButRootsRocksAndHeroesGetIt()
    {
        // Камера боя ортографическая, наклон 48°: предмет высотой h даёт разрыв глубины h / sin 48°.
        float sin48 = (float)Math.Sin(48.0 * Math.PI / 180.0);
        float tallestTuft = .49f * .694f * 1.45f * 1.15f; // меш × масштаб префаба × размер × вытяжка
        Assert.That(tallestTuft / sin48, Is.LessThan(ComicStyleRules.DefaultDepthThreshold),
            "самый высокий пучок травы не должен обводиться");
        float root = 1f;
        Assert.That(root / sin48, Is.GreaterThanOrEqualTo(ComicStyleRules.DefaultDepthThreshold + ComicStyleRules.DefaultDepthSoftness * .5f),
            "корень, камень и герой (≥ 1 м) обведены почти полной тушью");
    }

    [Test]
    public void SmallObjectsGetInkOnlyThroughTheColourGate()
    {
        // Малый разрыв (гриб, куст, камешек) ниже крупного порога — его пропускают только ворота цвета,
        // поэтому трава на траве (почти тот же цвет) остаётся чистой.
        Assert.That(ComicStyleRules.DefaultSmallDepthThreshold + ComicStyleRules.DefaultSmallDepthSoftness,
            Is.LessThan(ComicStyleRules.DefaultDepthThreshold));
        float sin48 = (float)Math.Sin(48.0 * Math.PI / 180.0);
        float mushroom = .3f;
        Assert.That(mushroom / sin48, Is.GreaterThanOrEqualTo(ComicStyleRules.DefaultSmallDepthThreshold + ComicStyleRules.DefaultSmallDepthSoftness),
            "гриб высотой 0,3 м проходит малый порог целиком");
        Assert.That(ComicStyleRules.DefaultColourGateLow, Is.GreaterThan(0f), "без ворот тушь ложится на всю траву");
        Assert.That(ComicStyleRules.DefaultColourGateHigh, Is.GreaterThan(ComicStyleRules.DefaultColourGateLow));
    }

    [Test]
    public void InkThicknessScalesWithResolution()
    {
        Assert.That(ComicStyleRules.OutlinePixels(1.75f, 1080), Is.EqualTo(1.75f).Within(1e-5f));
        Assert.That(ComicStyleRules.OutlinePixels(1.75f, 2160), Is.EqualTo(3.5f).Within(1e-5f));
        Assert.That(ComicStyleRules.OutlinePixels(1.75f, 1440), Is.EqualTo(1.75f * 1440f / 1080f).Within(1e-5f));
        Assert.That(ComicStyleRules.OutlinePixels(1.75f, 720), Is.EqualTo(1.75f * 720f / 1080f).Within(1e-5f));
        // Не больше, чем колец у шейдера.
        Assert.That(ComicStyleRules.OutlinePixels(4f, 4320), Is.EqualTo((float)ComicStyleRules.MaxInkRings));
        Assert.That(ComicStyleRules.OutlinePixels(float.NaN, 1080), Is.EqualTo(ComicStyleRules.DefaultOutlineThickness).Within(1e-5f));

        Assert.That(ComicStyleRules.InkRings(0f), Is.EqualTo(0));
        Assert.That(ComicStyleRules.InkRings(1f), Is.EqualTo(1));
        Assert.That(ComicStyleRules.InkRings(1.75f), Is.EqualTo(2));
        Assert.That(ComicStyleRules.InkRings(3.5f), Is.EqualTo(4));
        Assert.That(ComicStyleRules.InkRings(100f), Is.EqualTo(ComicStyleRules.MaxInkRings));
    }

    [Test]
    public void RingWeightsAddUpToTheLineThickness()
    {
        foreach (float pixels in new[] { .5f, 1f, 1.75f, 2.33f, 3.5f, 6f })
        {
            int rings = ComicStyleRules.InkRings(pixels);
            float depth = 0f, normal = 0f;
            for (int ring = 1; ring <= rings; ring++)
            {
                depth += ComicStyleRules.DepthRingWeight(pixels, ring);
                normal += ComicStyleRules.NormalRingWeight(pixels, ring);
            }
            // Разрыв глубины — линия на одной стороне во всю толщину; излом — по половине на сторону.
            Assert.That(depth, Is.EqualTo(pixels).Within(1e-4f), "глубина, " + pixels);
            Assert.That(normal * 2f, Is.EqualTo(pixels).Within(1e-4f), "излом, " + pixels);
        }
        Assert.That(ComicStyleRules.DepthRingWeight(1.75f, 0), Is.EqualTo(0f));
    }

    [Test]
    public void TiltShiftBlursOnlyTheTopAndBottomEdges()
    {
        const float band = .62f, strength = .9f;
        // Вся резкая полоса — ровно ноль.
        for (int i = 0; i <= 100; i++)
        {
            float v = .5f - band * .5f + band * i / 100f;
            Assert.That(ComicStyleRules.TiltShiftWeight(v, band, strength), Is.EqualTo(0f).Within(1e-6f), "v=" + v);
        }
        // У самых кромок — полная сила, и верх равен низу.
        Assert.That(ComicStyleRules.TiltShiftWeight(0f, band, strength), Is.EqualTo(strength).Within(1e-5f));
        Assert.That(ComicStyleRules.TiltShiftWeight(1f, band, strength), Is.EqualTo(strength).Within(1e-5f));
        float previous = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float v = .5f + i * .005f;
            float up = ComicStyleRules.TiltShiftWeight(v, band, strength);
            float down = ComicStyleRules.TiltShiftWeight(1f - v, band, strength);
            Assert.That(up, Is.EqualTo(down).Within(1e-5f), "симметрия, v=" + v);
            Assert.That(up, Is.GreaterThanOrEqualTo(previous - 1e-6f), "растёт к кромке, v=" + v);
            previous = up;
        }
        // Сила 0 выключает диораму, выход v за 0..1 не ломает кривую.
        Assert.That(ComicStyleRules.TiltShiftWeight(0f, band, 0f), Is.EqualTo(0f));
        Assert.That(ComicStyleRules.TiltShiftWeight(-3f, band, strength), Is.EqualTo(strength).Within(1e-5f));
    }

    [Test]
    public void TiltBlurIsMeasuredInQuarterResolutionTexels()
    {
        Assert.That(ComicStyleRules.TiltBlurSigma(8f, 1080), Is.EqualTo(2f).Within(1e-5f));
        Assert.That(ComicStyleRules.TiltBlurSigma(8f, 2160), Is.EqualTo(4f).Within(1e-5f));
        Assert.That(ComicStyleRules.TiltBlurSigma(-1f, 1080), Is.EqualTo(0f));
    }

    [Test]
    public void ToneTerraceIsFlatInTheMiddleAndContinuousAtTheEdges()
    {
        const float hardness = .65f;
        Assert.That(ComicStyleRules.ToneTerrace(0f, hardness), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(ComicStyleRules.ToneTerrace(1f, hardness), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(ComicStyleRules.ToneTerrace(.5f, hardness), Is.EqualTo(.5f).Within(1e-6f));
        Assert.That(ComicStyleRules.ToneTerrace(.4f, hardness), Is.EqualTo(.5f).Within(1e-6f), "середина полосы плоская");
        Assert.That(ComicStyleRules.ToneTerrace(.6f, hardness), Is.EqualTo(.5f).Within(1e-6f), "середина полосы плоская");
        float previous = 0f;
        for (int i = 0; i <= 200; i++)
        {
            float f = i / 200f;
            float s = ComicStyleRules.ToneTerrace(f, hardness);
            Assert.That(s, Is.GreaterThanOrEqualTo(previous - 1e-6f), "монотонна, f=" + f);
            // Симметрия: полоса в среднем не темнеет и не светлеет.
            Assert.That(ComicStyleRules.ToneTerrace(1f - f, hardness), Is.EqualTo(1f - s).Within(1e-5f), "симметрия, f=" + f);
            previous = s;
        }
    }

    [Test]
    public void ToneStepsNeitherDarkenNorBrightenTheFrameOnAverage()
    {
        const float pivot = ComicStyleRules.DefaultTonePivot, stops = ComicStyleRules.DefaultToneBandStops;
        const float hardness = ComicStyleRules.DefaultToneHardness, strength = ComicStyleRules.DefaultToneStrength;
        // Сила 0 — ступеней нет.
        Assert.That(ComicStyleRules.ToneLuminance(.37f, pivot, stops, hardness, 0f), Is.EqualTo(.37f));
        // Середина основной полосы (освещённая трава) остаётся как была.
        Assert.That(ComicStyleRules.ToneLuminance(pivot, pivot, stops, hardness, strength), Is.EqualTo(pivot).Within(1e-5f));
        Assert.That(ComicStyleRules.ToneLuminance(0f, pivot, stops, hardness, strength), Is.EqualTo(0f));

        // Средний логарифм яркости по целой полосе сохраняется — кадр не уходит в «гримдарк».
        for (int band = -2; band <= 1; band++)
        {
            double input = 0, output = 0;
            const int samples = 4000;
            for (int i = 0; i < samples; i++)
            {
                double p = band + (i + .5) / samples;
                float l = (float)(pivot * Math.Pow(2.0, (p - .5) * stops));
                input += Math.Log(l, 2.0);
                output += Math.Log(ComicStyleRules.ToneLuminance(l, pivot, stops, hardness, strength), 2.0);
            }
            Assert.That(output / samples, Is.EqualTo(input / samples).Within(1e-3), "полоса " + band);
        }
    }

    [Test]
    public void ToneStepsKeepOrderAndNeverJumpFarFromTheOriginal()
    {
        const float pivot = ComicStyleRules.DefaultTonePivot, stops = ComicStyleRules.DefaultToneBandStops;
        const float hardness = ComicStyleRules.DefaultToneHardness, strength = ComicStyleRules.DefaultToneStrength;
        float previous = 0f;
        // Самый большой сдвиг — половина полосы, умноженная на силу.
        double limit = Math.Pow(2.0, stops * strength * .5) + 1e-4;
        for (int i = 0; i <= 2000; i++)
        {
            float l = (float)(.002 * Math.Pow(2.0, i * 12.0 / 2000.0)); // от 0,002 до ~8 (HDR)
            float toned = ComicStyleRules.ToneLuminance(l, pivot, stops, hardness, strength);
            Assert.That(toned, Is.GreaterThan(0f), "чёрных пятен нет, l=" + l);
            Assert.That(toned, Is.GreaterThanOrEqualTo(previous * (1f - 1e-5f)), "порядок яркостей сохранён, l=" + l);
            double ratio = toned / l;
            Assert.That(ratio, Is.LessThanOrEqualTo(limit).And.GreaterThanOrEqualTo(1.0 / limit), "l=" + l);
            previous = toned;
        }
    }

    [Test]
    public void ShadowAmountStartsBelowTheMainBand()
    {
        const float pivot = .1f, stops = 1.25f;
        Assert.That(ComicStyleRules.ShadowAmount(pivot, pivot, stops), Is.EqualTo(0f));
        Assert.That(ComicStyleRules.ShadowAmount(pivot * 3f, pivot, stops), Is.EqualTo(0f), "свет не теплеет как тень");
        // Вся основная полоса (до полуполосы ниже середины) — без тёплого сдвига: теплеет только настоящая тень.
        Assert.That(ComicStyleRules.ShadowAmount(pivot / (float)Math.Pow(2.0, stops * .5), pivot, stops), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(ComicStyleRules.ShadowAmount(pivot / (float)Math.Pow(2.0, stops * .75), pivot, stops), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(ComicStyleRules.ShadowAmount(pivot / (float)Math.Pow(2.0, stops), pivot, stops), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(ComicStyleRules.ShadowAmount(0f, pivot, stops), Is.EqualTo(1f));
        float previous = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float l = pivot * (float)Math.Pow(2.0, -i * .03);
            float amount = ComicStyleRules.ShadowAmount(l, pivot, stops);
            Assert.That(amount, Is.GreaterThanOrEqualTo(previous - 1e-6f), "темнее — теплее, l=" + l);
            previous = amount;
        }
    }

    [Test]
    public void TonePivotFollowsTheFrameKey()
    {
        // Средний log2 яркости мира −3,6 (≈ 0,082, замер сцены 01.10) × 1 → середина полосы ≈ 0,082.
        Assert.That(ComicStyleRules.TonePivotFromKey(-3.6f, 1f), Is.EqualTo((float)Math.Pow(2.0, -3.6)).Within(1e-5f));
        Assert.That(ComicStyleRules.TonePivotFromKey(-3.6f, 2f), Is.EqualTo(2f * (float)Math.Pow(2.0, -3.6)).Within(1e-5f));
        // Пустой кадр и крайности — в пределах.
        Assert.That(ComicStyleRules.TonePivotFromKey(float.NaN, 1f), Is.EqualTo(ComicStyleRules.DefaultTonePivot));
        Assert.That(ComicStyleRules.TonePivotFromKey(-40f, 1f), Is.EqualTo(ComicStyleRules.MinTonePivot));
        Assert.That(ComicStyleRules.TonePivotFromKey(40f, 1f), Is.EqualTo(ComicStyleRules.MaxTonePivot));
        Assert.That(ComicStyleRules.TonePivotFromKey(-3f, 100f), Is.EqualTo(.125f * ComicStyleRules.MaxTonePivotScale).Within(1e-5f));
        Assert.That(ComicStyleRules.DefaultToneAutoPivot, Is.True);
    }

    [Test]
    public void PainterlyKernelStaysSmallAndMovesToHalfResolutionWhenLarge()
    {
        Assert.That(ComicStyleRules.PainterlyKernel(3f, .8f, 1080, false, out bool half), Is.EqualTo(3));
        Assert.That(half, Is.False);
        // 1440p: 4 px — ещё полное разрешение.
        Assert.That(ComicStyleRules.PainterlyKernel(3f, .8f, 1440, false, out half), Is.EqualTo(4));
        Assert.That(half, Is.False);
        // 4K: 6 px → половинное разрешение с тем же ядром, что у 1080p.
        Assert.That(ComicStyleRules.PainterlyKernel(3f, .8f, 2160, false, out half), Is.EqualTo(3));
        Assert.That(half, Is.True);
        Assert.That(ComicStyleRules.PainterlyKernel(3f, .8f, 1080, true, out half), Is.EqualTo(2));
        Assert.That(half, Is.True);
        // Выключено: радиус 0 или доля 0.
        Assert.That(ComicStyleRules.PainterlyKernel(0f, .8f, 1080, false, out _), Is.EqualTo(0));
        Assert.That(ComicStyleRules.PainterlyKernel(3f, 0f, 1080, false, out _), Is.EqualTo(0));
        // Предел ядра шейдера.
        Assert.That(ComicStyleRules.PainterlyKernel(100f, 1f, 4320, false, out _), Is.LessThanOrEqualTo(ComicStyleRules.MaxKernelRadius));
    }

    [Test]
    public void PainterlyTapWeightsMatchTheShaderFormula()
    {
        var weights = new float[ComicStyleRules.MaxKernelTaps * 8];
        Assert.That(ComicStyleRules.PainterlyTapWeights(ComicStyleRules.MaxKernelRadius, weights),
            Is.EqualTo(ComicStyleRules.MaxKernelTaps));
        for (int r = 1; r <= ComicStyleRules.MaxKernelRadius; r++)
        {
            int taps = ComicStyleRules.PainterlyTapWeights(r, weights);
            Assert.That(taps, Is.EqualTo((2 * r + 1) * (2 * r + 1)));
            int side = 2 * r + 1, centre = r * side + r;
            // В центре все восемь секторов равны, а вместе дают гауссиану exp(0) = 1.
            for (int k = 0; k < 8; k++)
                Assert.That(weights[centre * 8 + k], Is.EqualTo(.125f).Within(1e-5f), "r " + r + " сектор " + k);
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int tap = (y + r) * side + (x + r);
                float sum = 0f;
                for (int k = 0; k < 8; k++)
                {
                    Assert.That(weights[tap * 8 + k], Is.GreaterThanOrEqualTo(0f));
                    sum += weights[tap * 8 + k];
                }
                float vx = (float)x / r, vy = (float)y / r;
                float gauss = (float)Math.Exp(-3.125 * (vx * vx + vy * vy));
                // Сумма секторов = гауссиана (веса нормированы), если хоть один сектор видит выборку.
                if (sum > 0f) Assert.That(sum, Is.EqualTo(gauss).Within(1e-4f), $"r {r} ({x},{y})");
                // Зеркало по x меняет сектор 0 (вверх) на себя же, а 2 (влево) на 6 (вправо).
                int mirror = (y + r) * side + (-x + r);
                Assert.That(weights[tap * 8 + 2], Is.EqualTo(weights[mirror * 8 + 6]).Within(1e-5f));
                Assert.That(weights[tap * 8 + 0], Is.EqualTo(weights[mirror * 8 + 0]).Within(1e-5f));
            }
        }
        Assert.Throws<ArgumentException>(() => ComicStyleRules.PainterlyTapWeights(3, new float[8]));
    }

    [Test]
    public void BrightnessLiftRollsOffBelowTheBloomThreshold()
    {
        const float knee = ComicStyleRules.DefaultHighlightKnee;
        // Без подъёма и с понижением — чистый множитель.
        Assert.That(ComicStyleRules.BrightenPeak(.4f, 1f, knee), Is.EqualTo(.4f).Within(1e-6f));
        Assert.That(ComicStyleRules.BrightenPeak(.4f, .8f, knee), Is.EqualTo(.32f).Within(1e-6f));
        // Тёмное и среднее до колена поднимается как есть.
        Assert.That(ComicStyleRules.BrightenPeak(.1f, 2f, knee), Is.EqualTo(.2f).Within(1e-6f));
        // Светлое сворачивается ниже 1,0 (порог Bloom): белая рубаха не выгорает и не светится.
        for (float peak = .05f; peak < 1f; peak += .05f)
        {
            float lifted = ComicStyleRules.BrightenPeak(peak, 3f, knee);
            Assert.That(lifted, Is.LessThan(1f), "peak " + peak);
            Assert.That(lifted, Is.GreaterThanOrEqualTo(peak), "подъём не темнит, peak " + peak);
        }
        // Монотонно: порядок яркостей сохраняется.
        float previous = 0f;
        for (float peak = .01f; peak < 2f; peak += .01f)
        {
            float lifted = ComicStyleRules.BrightenPeak(peak, 2.2f, knee);
            Assert.That(lifted, Is.GreaterThanOrEqualTo(previous - 1e-6f), "peak " + peak);
            previous = lifted;
        }
        // Эмиссия ярче порога не гасится.
        Assert.That(ComicStyleRules.BrightenPeak(3f, 2f, knee), Is.EqualTo(3f).Within(1e-6f));
        Assert.That(ComicStyleRules.BrightenPeak(float.NaN, 2f, knee), Is.EqualTo(0f));
    }

    [Test]
    public void ClampRejectsNaNAndOutOfRangeValues()
    {
        Assert.That(ComicStyleRules.Clamp(float.NaN, 0f, 1f, .3f), Is.EqualTo(.3f));
        Assert.That(ComicStyleRules.Clamp(float.PositiveInfinity, 0f, 1f, .3f), Is.EqualTo(.3f));
        Assert.That(ComicStyleRules.Clamp(-2f, 0f, 1f, .3f), Is.EqualTo(0f));
        Assert.That(ComicStyleRules.Clamp(7f, 0f, 1f, .3f), Is.EqualTo(1f));
        Assert.That(ComicStyleRules.Clamp01(.42f, .3f), Is.EqualTo(.42f));
        Assert.That(ComicStyleRules.ResolutionScale(0), Is.EqualTo(1f));
        Assert.That(ComicStyleRules.ResolutionScale(1080), Is.EqualTo(1f));
        // Кривая тона с мусором на входе даёт конечное число.
        float toned = ComicStyleRules.ToneLuminance(.3f, float.NaN, float.NaN, float.NaN, float.NaN);
        Assert.That(float.IsNaN(toned) || float.IsInfinity(toned), Is.False);
    }
}
