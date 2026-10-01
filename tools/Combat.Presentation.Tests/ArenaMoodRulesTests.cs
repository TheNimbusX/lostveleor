using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Свет арены по глубине (световая арка акта I, 01.10; цель — ART/UI/concepts-2026-10-01-style-shift/
// 8-light-arc-act1-labeled.jpg, план — light-arc-plan.md там же): арены 1–3 — день, 4–6 — туман сгущается,
// 7–8 и босс — сумерки. Ось и веса пресетов, выбор F8 / ключа съёмки, доля перекрытия комикс-рисовки,
// «прожектор» поляны (поле, кружево, сетка по земле, cookie в плоскости солнца) и полярный контур поляны.
public sealed class ArenaMoodRulesTests
{
    [Test]
    public void WeightsInterpolateInsideEachThirdAndSumToOne()
    {
        AssertWeights(ArenaMoodRules.Weights(0f), 1f, 0f, 0f);
        AssertWeights(ArenaMoodRules.Weights(.5f), .5f, .5f, 0f);
        AssertWeights(ArenaMoodRules.Weights(1f), 0f, 1f, 0f);
        AssertWeights(ArenaMoodRules.Weights(1.3f), 0f, .7f, .3f);
        AssertWeights(ArenaMoodRules.Weights(2f), 0f, 0f, 1f);
        // NaN, бесконечность и выход за ось — в пределы, не мусор в свет.
        AssertWeights(ArenaMoodRules.Weights(float.NaN), 1f, 0f, 0f);
        AssertWeights(ArenaMoodRules.Weights(float.PositiveInfinity), 1f, 0f, 0f);
        AssertWeights(ArenaMoodRules.Weights(-3f), 1f, 0f, 0f);
        AssertWeights(ArenaMoodRules.Weights(7f), 0f, 0f, 1f);
        for (float a = -.5f; a <= 2.5f; a += .01f)
        {
            ArenaMoodWeights w = ArenaMoodRules.Weights(a);
            Assert.That(w.Day + w.Mist + w.Dusk, Is.EqualTo(1f).Within(1e-5f), "сумма весов при оси " + a);
            Assert.That(w.Day > 0f && w.Dusk > 0f, Is.False, "день и сумерки не смешиваются напрямую: " + a);
            Assert.That(w.Day, Is.InRange(0f, 1f));
            Assert.That(w.Mist, Is.InRange(0f, 1f));
            Assert.That(w.Dusk, Is.InRange(0f, 1f));
        }
        Assert.That(ArenaMoodRules.Weights(1.4f).IsDusk, Is.False);
        Assert.That(ArenaMoodRules.Weights(1.5f).IsDusk, Is.True);
        Assert.That(ArenaMoodRules.Weights(1.3f).Blend(10f, 20f, 30f), Is.EqualTo(23f).Within(1e-4f));
    }

    [Test]
    public void DefaultTableFollowsTheActOneArc()
    {
        float[] table = ArenaMoodRules.DefaultDepthBlend;
        // Девять строк: арены 1–8 и босс (MeadowGameplay: 9 уровней, 9 — босс).
        Assert.That(table.Length, Is.EqualTo(9));
        float previous = -1f;
        for (int depth = 1; depth <= 8; depth++)
        {
            float axis = ArenaMoodRules.AxisForDepth(depth, false, table);
            Assert.That(axis, Is.GreaterThanOrEqualTo(previous), "свет не откатывается назад: арена " + depth);
            previous = axis;
            ArenaMoodWeights w = ArenaMoodRules.Weights(axis);
            if (depth <= 3) Assert.That(w.Day, Is.GreaterThan(.5f), "золотой день: арена " + depth);
            else if (depth <= 6) Assert.That(w.Mist, Is.GreaterThanOrEqualTo(.5f), "туман: арена " + depth);
            else Assert.That(w.Dusk, Is.GreaterThan(.5f), "сумерки: арена " + depth);
        }
        // Туман сгущается внутри своей трети: 4 → 5 → 6 растёт, а не стоит ступенью.
        Assert.That(ArenaMoodRules.AxisForDepth(4, false, table), Is.LessThan(ArenaMoodRules.AxisForDepth(5, false, table)));
        Assert.That(ArenaMoodRules.AxisForDepth(5, false, table), Is.LessThan(ArenaMoodRules.AxisForDepth(6, false, table)));
        // Босс — сумерки (последняя строка), с какой бы глубины его ни открыли.
        Assert.That(ArenaMoodRules.AxisForDepth(9, true, table), Is.EqualTo(ArenaMoodRules.DuskAxis));
        Assert.That(ArenaMoodRules.AxisForDepth(2, true, table), Is.EqualTo(ArenaMoodRules.DuskAxis));
        Assert.That(ArenaMoodRules.Weights(ArenaMoodRules.AxisForDepth(9, true, table)).IsDusk, Is.True);
    }

    [Test]
    public void DepthOutsideTheTableIsClamped()
    {
        float[] table = { 0f, 1f, 2f, 1.5f };
        Assert.That(ArenaMoodRules.AxisForDepth(0, false, table), Is.EqualTo(0f));
        Assert.That(ArenaMoodRules.AxisForDepth(-4, false, table), Is.EqualTo(0f));
        // Глубже таблицы — последняя НЕ-боссовая строка, а не строка босса.
        Assert.That(ArenaMoodRules.AxisForDepth(3, false, table), Is.EqualTo(2f));
        Assert.That(ArenaMoodRules.AxisForDepth(40, false, table), Is.EqualTo(2f));
        Assert.That(ArenaMoodRules.AxisForDepth(1, true, table), Is.EqualTo(1.5f));
        // Пустая таблица — умолчание; одна строка — она для всех; мусор в строке — в пределы.
        Assert.That(ArenaMoodRules.AxisForDepth(5, false, null), Is.EqualTo(ArenaMoodRules.DefaultDepthBlend[4]));
        Assert.That(ArenaMoodRules.AxisForDepth(5, false, new float[0]), Is.EqualTo(ArenaMoodRules.DefaultDepthBlend[4]));
        Assert.That(ArenaMoodRules.AxisForDepth(5, false, new[] { 1.2f }), Is.EqualTo(1.2f));
        Assert.That(ArenaMoodRules.AxisForDepth(5, true, new[] { 1.2f }), Is.EqualTo(1.2f));
        Assert.That(ArenaMoodRules.AxisForDepth(1, false, new[] { float.NaN, 9f }), Is.EqualTo(0f));
        Assert.That(ArenaMoodRules.AxisForDepth(1, true, new[] { 0f, 9f }), Is.EqualTo(2f));
    }

    [Test]
    public void ForcedModesOverrideTheTable()
    {
        float[] table = ArenaMoodRules.DefaultDepthBlend;
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Auto, 0f, 5, false, table), Is.EqualTo(1f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Day, 0f, 8, false, table), Is.EqualTo(0f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Mist, 0f, 1, false, table), Is.EqualTo(1f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Dusk, 0f, 1, false, table), Is.EqualTo(2f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Boss, 0f, 1, false, table), Is.EqualTo(2f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Axis, 1.3f, 1, false, table), Is.EqualTo(1.3f));
        Assert.That(ArenaMoodRules.AxisFor(ArenaMoodMode.Axis, 12f, 1, false, table), Is.EqualTo(2f));
        // Акцент босса: на арене босса в «авто» и всегда в режиме «босс»; принудительные сумерки — без него.
        Assert.That(ArenaMoodRules.BossAccentFor(ArenaMoodMode.Auto, true), Is.True);
        Assert.That(ArenaMoodRules.BossAccentFor(ArenaMoodMode.Auto, false), Is.False);
        Assert.That(ArenaMoodRules.BossAccentFor(ArenaMoodMode.Boss, false), Is.True);
        Assert.That(ArenaMoodRules.BossAccentFor(ArenaMoodMode.Dusk, true), Is.False);
    }

    [Test]
    public void CaptureValueParsesPresetsAndAxis()
    {
        AssertParse("off", ArenaMoodMode.Off);
        AssertParse("OFF", ArenaMoodMode.Off);
        AssertParse("auto", ArenaMoodMode.Auto);
        AssertParse("day", ArenaMoodMode.Day);
        AssertParse(" Mist ", ArenaMoodMode.Mist);
        AssertParse("dusk", ArenaMoodMode.Dusk);
        AssertParse("boss", ArenaMoodMode.Boss);
        AssertParse("туман", ArenaMoodMode.Mist);
        Assert.That(ArenaMoodRules.TryParse("1.3", out ArenaMoodMode mode, out float axis), Is.True);
        Assert.That(mode, Is.EqualTo(ArenaMoodMode.Axis));
        Assert.That(axis, Is.EqualTo(1.3f).Within(1e-5f));
        Assert.That(ArenaMoodRules.TryParse("0,7", out mode, out axis), Is.True);
        Assert.That(axis, Is.EqualTo(.7f).Within(1e-5f));
        Assert.That(ArenaMoodRules.TryParse("9", out mode, out axis), Is.True);
        Assert.That(axis, Is.EqualTo(2f), "ось зажимается в 0…2");
        Assert.That(ArenaMoodRules.TryParse(null, out mode, out _), Is.False);
        Assert.That(mode, Is.EqualTo(ArenaMoodMode.Off), "без ключа — свет как сегодня");
        Assert.That(ArenaMoodRules.TryParse("", out _, out _), Is.False);
        Assert.That(ArenaMoodRules.TryParse("nan", out _, out _), Is.False);
        Assert.That(ArenaMoodRules.TryParse("сумерек", out _, out _), Is.False);
    }

    [Test]
    public void DayLeavesComicSettingsAloneAndMistOrDuskOverride()
    {
        // День (доля 0) — рисовка ровно как в своих настройках, их умолчания не меняются.
        ArenaMoodWeights day = ArenaMoodRules.Weights(0f);
        Assert.That(ArenaMoodRules.OverlayAmount(day, 0f, 1f, 1f), Is.EqualTo(0f));
        Assert.That(ArenaMoodRules.OverlayTarget(day, 0f, 1f, 1f, 5f, 7f, 9f, -1f), Is.EqualTo(-1f));
        // Туман — целиком цель тумана.
        ArenaMoodWeights mist = ArenaMoodRules.Weights(1f);
        Assert.That(ArenaMoodRules.OverlayAmount(mist, 0f, 1f, 1f), Is.EqualTo(1f));
        Assert.That(ArenaMoodRules.OverlayTarget(mist, 0f, 1f, 1f, 5f, 7f, 9f, -1f), Is.EqualTo(7f));
        // Между днём и туманом — доля растёт с туманом, а цель остаётся туманной (день цели не задаёт).
        ArenaMoodWeights dayMist = ArenaMoodRules.Weights(.7f);
        Assert.That(ArenaMoodRules.OverlayAmount(dayMist, 0f, 1f, 1f), Is.EqualTo(.7f).Within(1e-5f));
        Assert.That(ArenaMoodRules.OverlayTarget(dayMist, 0f, 1f, 1f, 5f, 7f, 9f, -1f), Is.EqualTo(7f).Within(1e-5f));
        // Между туманом и сумерками — смесь их целей.
        ArenaMoodWeights mistDusk = ArenaMoodRules.Weights(1.25f);
        Assert.That(ArenaMoodRules.OverlayAmount(mistDusk, 0f, 1f, 1f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(ArenaMoodRules.OverlayTarget(mistDusk, 0f, 1f, 1f, 5f, 7f, 9f, -1f), Is.EqualTo(7.5f).Within(1e-4f));
        // Целые числа (фонари, светлячки) — округлённая смесь.
        Assert.That(ArenaMoodRules.BlendCount(ArenaMoodRules.Weights(1.85f), 0, 0, 24), Is.EqualTo(20));
        Assert.That(ArenaMoodRules.BlendCount(ArenaMoodRules.Weights(1.5f), 0, 0, 5), Is.EqualTo(3));
        Assert.That(ArenaMoodRules.BlendCount(ArenaMoodRules.Weights(0f), 40, 30, 0), Is.EqualTo(40));
    }

    [Test]
    public void GladeFieldMatchesTheSimulationInsideAndStaysSmoothOutside()
    {
        foreach (GladeShape shape in Enum.GetValues(typeof(GladeShape)))
        for (int turn = 0; turn < 4; turn++)
        {
            GladeRegion glade = Glade(20, 30, 12, 10, shape, turn);
            // Внутри рамки радиусов — то же поле, что у симуляции и покраски земли.
            for (double x = 8.5; x <= 31.5; x += 1.7)
            for (double z = 20.5; z <= 39.5; z += 1.3)
            {
                float sim = glade.Field(new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z))).ToFloat();
                Assert.That(ArenaMoodRules.GladeField(glade, x, z), Is.EqualTo(sim).Within(1e-4f), $"{shape}/{turn} ({x}, {z})");
            }
            // На рамке поле не прыгает к 2, а за ней плавно растёт.
            foreach (var (dx, dz) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
            {
                double ex = 20 + dx * 12, ez = 30 + dz * 10;
                float inner = ArenaMoodRules.GladeField(glade, ex - dx * .01, ez - dz * .01);
                float outer = ArenaMoodRules.GladeField(glade, ex + dx * .01, ez + dz * .01);
                Assert.That(Math.Abs(outer - inner), Is.LessThan(.05f), $"{shape}/{turn}: разрыв на рамке");
                float far = ArenaMoodRules.GladeField(glade, ex + dx * 6, ez + dz * 6);
                Assert.That(far, Is.GreaterThan(outer + 1f), $"{shape}/{turn}: за рамкой поле растёт");
            }
        }
    }

    [Test]
    public void SpotLightsTheGladeAndShadesTheForest()
    {
        var p = Params(spot: .75f, dapple: .2f, inside: 0f);
        // Середина поляны — полное солнце при любом кружеве (внутри доля кружева 0).
        Assert.That(ArenaMoodRules.GroundLight(0f, 0f, p), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(ArenaMoodRules.GroundLight(.5f, 1f, p), Is.EqualTo(1f).Within(1e-5f));
        // Далеко за краем — тень края × кружево: в тени кроны темнее, в луче — светлее.
        Assert.That(ArenaMoodRules.GroundLight(5f, 1f, p), Is.EqualTo(.25f).Within(1e-5f));
        Assert.That(ArenaMoodRules.GroundLight(5f, 0f, p), Is.EqualTo(.25f * .8f).Within(1e-5f));
        Assert.That(ArenaMoodRules.OutsideLight(p), Is.EqualTo(.25f * .9f).Within(1e-5f));
        // От середины к лесу свет только гаснет.
        float previous = 2f;
        for (float field = 0f; field <= 3f; field += .05f)
        {
            float light = ArenaMoodRules.GroundLight(field, 1f, p);
            Assert.That(light, Is.LessThanOrEqualTo(previous + 1e-6f), "поле " + field);
            previous = light;
        }
        // Без пятна и кружева — ровный свет, как сегодня.
        var none = Params(spot: 0f, dapple: 0f, inside: 0f);
        Assert.That(ArenaMoodRules.GroundLight(3f, 0f, none), Is.EqualTo(1f));
        Assert.That(ArenaMoodRules.OutsideLight(none), Is.EqualTo(1f));
    }

    [Test]
    public void DappleIsSoftPatchesTiedToTheGround()
    {
        int lit = 0, total = 0;
        double sum = 0;
        for (double x = -40; x < 40; x += .37)
        for (double z = -40; z < 40; z += .41)
        {
            float d = ArenaMoodRules.Dapple(x, z, 1234u, 3.2f);
            Assert.That(d, Is.InRange(0f, 1f));
            Assert.That(ArenaMoodRules.Dapple(x, z, 1234u, 3.2f), Is.EqualTo(d), "рисунок не дрожит");
            sum += d;
            total++;
            if (d > .5f) lit++;
        }
        // Пятна, а не сплошной свет или сплошная тень.
        Assert.That(sum / total, Is.InRange(.25, .75));
        Assert.That((double)lit / total, Is.InRange(.2, .8));
        // У другой арены — другой рисунок.
        int differs = 0;
        for (int i = 0; i < 50; i++)
            if (Math.Abs(ArenaMoodRules.Dapple(i * 1.3, i * .7, 1u, 3.2f) - ArenaMoodRules.Dapple(i * 1.3, i * .7, 2u, 3.2f)) > .05f) differs++;
        Assert.That(differs, Is.GreaterThan(10));
    }

    [Test]
    public void CookieInTheSunPlaneLandsOnTheGladeOnTheGround()
    {
        GladeRegion glade = Glade(20, 30, 12, 10, GladeShape.Oval, 0);
        var p = Params(spot: .75f, dapple: .2f, inside: 0f);
        const double half = 12 + 30, step = .5;
        int n = (int)Math.Ceiling(2 * half / step) + 1;
        var ground = new float[n * n];
        double minX = 20 - half, minZ = 30 - half;
        ArenaMoodRules.BakeGround(ground, n, minX, minZ, step, glade, p);
        float outside = ArenaMoodRules.OutsideLight(p);

        // Солнце арены: Euler (42.6, −4.15, 0), как Key Light сцены; позиция не в нуле — проверка переноса.
        double[,] r = Rotation(42.6, -4.15);
        double[] position = { 3, 40, -7 };
        var lightToWorld = new float[12];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++) lightToWorld[row * 4 + column] = (float)r[row, column];
            lightToWorld[row * 4 + 3] = (float)position[row];
        }
        double[] centre = ToLight(r, position, 20, 0, 30);
        const int resolution = 256;
        double size = 2.4 * half;
        var cookie = new byte[resolution * resolution];
        ArenaMoodRules.BakeCookie(cookie, resolution, lightToWorld, centre[0], centre[1], size, ground, n, minX, minZ, step, outside);

        // Рамка — ровный свет «далеко за поляной»: его тянет дальше Clamp текстуры.
        byte rim = (byte)(outside * 255f + .5f);
        Assert.That(cookie[0], Is.EqualTo(rim));
        Assert.That(cookie[resolution * resolution - 1], Is.EqualTo(rim));
        Assert.That(cookie[resolution * 100 + 1], Is.EqualTo(rim));
        // Середина текстуры — центр поляны: полное солнце.
        Assert.That(cookie[resolution / 2 * resolution + resolution / 2], Is.GreaterThan(245));

        // Любая точка земли: URP берёт uv = (точка в плоскости света − offset) / size + ½ — там свет сетки.
        foreach (var (x, z) in new[] { (20.0, 30.0), (26.0, 33.0), (35.0, 30.0), (20.0, 47.0), (5.0, 20.0), (40.0, 50.0) })
        {
            double[] local = ToLight(r, position, x, 0, z);
            double u = (local[0] - centre[0]) / size + .5, v = (local[1] - centre[1]) / size + .5;
            int i = (int)Math.Floor(u * resolution), j = (int)Math.Floor(v * resolution);
            Assert.That(i, Is.InRange(2, resolution - 3), $"({x}, {z}) в текстуре");
            Assert.That(j, Is.InRange(2, resolution - 3), $"({x}, {z}) в текстуре");
            float expected = ArenaMoodRules.SampleGround(ground, n, minX, minZ, step, x, z, outside);
            Assert.That(cookie[j * resolution + i] / 255f, Is.EqualTo(expected).Within(.08f), $"({x}, {z})");
        }
        // Пятно: внутри поляны светло, далеко в лесу — тень края.
        Assert.That(Sample(cookie, resolution, r, position, centre, size, 20, 30), Is.GreaterThan(.95f));
        Assert.That(Sample(cookie, resolution, r, position, centre, size, 20 + 30, 30), Is.LessThan(.3f));
    }

    [Test]
    public void ContourFollowsTheGladeShape()
    {
        // Овал Oval: 0,76 радиуса по x и 0,96 по z от рамки (GladeRegion.Field).
        GladeRegion glade = Glade(0, 0, 10, 8, GladeShape.Oval, 0);
        var radii = new float[ArenaMoodRules.ContourSamples];
        ArenaMoodRules.Contour(radii, glade);
        Assert.That(radii[0], Is.EqualTo(7.6f).Within(.06f));
        Assert.That(radii[ArenaMoodRules.ContourSamples / 4], Is.EqualTo(7.68f).Within(.06f));
        Assert.That(radii[ArenaMoodRules.ContourSamples / 2], Is.EqualTo(7.6f).Within(.06f));
        Assert.That(ArenaMoodRules.ContourRadius(radii, 0f), Is.EqualTo(radii[0]).Within(1e-5f));
        Assert.That(ArenaMoodRules.ContourRadius(radii, (float)(2 * Math.PI)), Is.EqualTo(radii[0]).Within(1e-3f));
        Assert.That(ArenaMoodRules.ContourRadius(radii, (float)(-Math.PI / 2)),
            Is.EqualTo(radii[3 * ArenaMoodRules.ContourSamples / 4]).Within(1e-3f));
        // Каждая точка контура — на краю поляны у любой формы.
        foreach (GladeShape shape in Enum.GetValues(typeof(GladeShape)))
        {
            GladeRegion g = Glade(5, -3, 14, 11, shape, 1);
            ArenaMoodRules.Contour(radii, g);
            for (int a = 0; a < ArenaMoodRules.ContourSamples; a += 5)
            {
                double angle = a * 2 * Math.PI / ArenaMoodRules.ContourSamples;
                float field = ArenaMoodRules.GladeField(g, 5 + Math.Cos(angle) * radii[a], -3 + Math.Sin(angle) * radii[a]);
                Assert.That(field, Is.EqualTo(1f).Within(.05f), $"{shape}: угол {a * 5}°");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------

    static void AssertWeights(ArenaMoodWeights w, float day, float mist, float dusk)
    {
        Assert.That(w.Day, Is.EqualTo(day).Within(1e-5f), "день");
        Assert.That(w.Mist, Is.EqualTo(mist).Within(1e-5f), "туман");
        Assert.That(w.Dusk, Is.EqualTo(dusk).Within(1e-5f), "сумерки");
    }

    static void AssertParse(string value, ArenaMoodMode expected)
    {
        Assert.That(ArenaMoodRules.TryParse(value, out ArenaMoodMode mode, out _), Is.True, value);
        Assert.That(mode, Is.EqualTo(expected), value);
    }

    static ArenaGladeLightParams Params(float spot, float dapple, float inside) => new ArenaGladeLightParams
    {
        SpotStrength = spot,
        DappleStrength = dapple,
        DappleInside = inside,
        DappleScale = 3.2f,
        SpotInnerField = .7f,
        SpotOuterField = 1.45f,
        Seed = 77u,
    };

    static GladeRegion Glade(double x, double z, double rx, double rz, GladeShape shape, int turn) =>
        new GladeRegion(new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z)),
            new FixVec2(Fix64.FromDouble(rx), Fix64.FromDouble(rz)), shape, turn);

    /// <summary>Quaternion.Euler(pitch, yaw, 0) Unity как матрица: Ry · Rx.</summary>
    static double[,] Rotation(double pitchDegrees, double yawDegrees)
    {
        double a = pitchDegrees * Math.PI / 180, b = yawDegrees * Math.PI / 180;
        double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
        double[,] rx = { { 1, 0, 0 }, { 0, ca, -sa }, { 0, sa, ca } };
        double[,] ry = { { cb, 0, sb }, { 0, 1, 0 }, { -sb, 0, cb } };
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++) m[i, j] += ry[i, k] * rx[k, j];
        return m;
    }

    /// <summary>Точка мира в пространстве света: Rᵀ (p − позиция).</summary>
    static double[] ToLight(double[,] r, double[] position, double x, double y, double z)
    {
        double px = x - position[0], py = y - position[1], pz = z - position[2];
        return new[]
        {
            r[0, 0] * px + r[1, 0] * py + r[2, 0] * pz,
            r[0, 1] * px + r[1, 1] * py + r[2, 1] * pz,
            r[0, 2] * px + r[1, 2] * py + r[2, 2] * pz,
        };
    }

    static float Sample(byte[] cookie, int resolution, double[,] r, double[] position, double[] centre, double size, double x, double z)
    {
        double[] local = ToLight(r, position, x, 0, z);
        int i = (int)Math.Floor(((local[0] - centre[0]) / size + .5) * resolution);
        int j = (int)Math.Floor(((local[1] - centre[1]) / size + .5) * resolution);
        i = Math.Max(0, Math.Min(resolution - 1, i));
        j = Math.Max(0, Math.Min(resolution - 1, j));
        return cookie[j * resolution + i] / 255f;
    }
}
