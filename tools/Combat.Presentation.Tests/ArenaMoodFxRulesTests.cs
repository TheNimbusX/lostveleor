using System;
using Game.View;
using NUnit.Framework;

// «Жизнь в кадре» света арены по глубине (световая арка акта I, 01.10; план —
// ART/UI/concepts-2026-10-01-style-shift/light-arc-plan.md, разделы 3.5–3.6 и 5): кольца дымки чащи не
// заходят на пол боя и не закрывают тропу к порталу, лучи — только в верхней трети кадра, пыльца и
// светлячки редко и по краю, фонари — на дальней дуге за полом, мерцание в пределах.
public sealed class ArenaMoodFxRulesTests
{
    const float Pitch = 48f;
    const int Samples = 72;

    [Test]
    public void RandomIsDeterministicAndInRange()
    {
        var a = new ArenaMoodRandom(12345u);
        var b = new ArenaMoodRandom(12345u);
        var c = new ArenaMoodRandom(12346u);
        bool differs = false;
        for (int i = 0; i < 1000; i++)
        {
            float x = a.Next01();
            Assert.That(x, Is.EqualTo(b.Next01()), "одно зерно — одна картина (съёмка «было → стало»)");
            Assert.That(x, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            if (Math.Abs(x - c.Next01()) > 1e-6f) differs = true;
        }
        Assert.That(differs, Is.True, "соседние зёрна дают разную картину");
        Assert.That(new ArenaMoodRandom(0u).Next01(), Is.InRange(0f, 1f), "нулевое зерно не залипает в нуле");
        Assert.That(ArenaMoodRandom.Mix(7UL, 5), Is.EqualTo(ArenaMoodRandom.Mix(7UL, 5)));
        Assert.That(ArenaMoodRandom.Mix(7UL, 5), Is.Not.EqualTo(ArenaMoodRandom.Mix(7UL, 6)));
        Assert.That(ArenaMoodRandom.Mix(0UL, 0), Is.Not.EqualTo(0u));
    }

    [Test]
    public void NearSidePushOnlyOnTheCameraSide()
    {
        // Камера смотрит на +z: ближняя к камере сторона поляны — −z.
        float shift = 5f / (float)Math.Tan(Pitch * Math.PI / 180);
        Assert.That(ArenaMoodFxRules.NearSidePush(0f, -1f, 0f, 1f, 5f, Pitch), Is.EqualTo(shift).Within(1e-4f));
        Assert.That(ArenaMoodFxRules.NearSidePush(0f, 1f, 0f, 1f, 5f, Pitch), Is.EqualTo(0f), "дальняя сторона — без сдвига");
        Assert.That(ArenaMoodFxRules.NearSidePush(1f, 0f, 0f, 1f, 5f, Pitch), Is.EqualTo(0f).Within(1e-5f), "бок — без сдвига");
        Assert.That(ArenaMoodFxRules.NearSidePush(0f, -1f, 0f, 1f, 0f, Pitch), Is.EqualTo(0f), "у земли сдвига нет");
        Assert.That(ArenaMoodFxRules.NearSidePush(0f, -1f, 0f, 1f, 2.5f, Pitch), Is.LessThan(shift), "низкий слой сдвигается меньше");
        Assert.That(float.IsNaN(ArenaMoodFxRules.NearSidePush(0f, -1f, 0f, 1f, 5f, float.NaN)), Is.False);
    }

    [Test]
    public void HazeRingStaysOffTheFloorOnScreen()
    {
        float[] contour = Circle(10f);
        int vertices = ArenaMoodFxRules.HazeVertexCount(Samples);
        var xyz = new float[vertices * 3];
        var alpha = new float[vertices];
        // Камера смотрит на +z под наклоном 48°.
        ArenaMoodFxRules.HazeRing(contour, Samples, 3f, -2f, 1f, 18f, 0f, 1f, Pitch, null, 0, xyz, alpha);
        float tan = (float)Math.Tan(Pitch * Math.PI / 180);
        int rows = ArenaMoodFxRules.HazeRows;
        for (int v = 0; v < vertices; v++)
        {
            float x = xyz[v * 3] - 3f, y = xyz[v * 3 + 1], z = xyz[v * 3 + 2] + 2f;
            // Где точка видна на земле: на h / tg(наклона) дальше от камеры.
            float seenZ = z + y / tan;
            float seen = (float)Math.Sqrt(x * x + seenZ * seenZ);
            Assert.That(seen, Is.GreaterThanOrEqualTo(10f + 1f - 1e-3f), "дымка на экране не заходит на поляну: вершина " + v);
            Assert.That(alpha[v], Is.InRange(0f, 1f));
            if (v % rows == 0) Assert.That(alpha[v], Is.EqualTo(0f), "у поляны дымка прозрачная — без кромки");
        }
        // Три слоя снизу вверх: верхний (ближе к камере) рисуется последним.
        Assert.That(xyz[1], Is.EqualTo(ArenaMoodFxRules.HazeHeights[0]));
        Assert.That(xyz[(vertices - 1) * 3 + 1], Is.EqualTo(ArenaMoodFxRules.HazeHeights[ArenaMoodFxRules.HazeHeights.Length - 1]));
        // У леса гуще, чем у поляны.
        Assert.That(alpha[3], Is.GreaterThan(alpha[1]));
    }

    [Test]
    public void HazeClearsThePortalPath()
    {
        float[] contour = Circle(10f);
        int vertices = ArenaMoodFxRules.HazeVertexCount(Samples);
        var xyz = new float[vertices * 3];
        var alpha = new float[vertices];
        // Выход на севере: тропа от центра поляны до портала за краем.
        float[] portals = { 0f, 0f, 0f, 24f };
        ArenaMoodFxRules.HazeRing(contour, Samples, 0f, 0f, 1f, 18f, 0f, 1f, Pitch, portals, 1, xyz, alpha);
        float onPath = 0f, aside = 0f;
        for (int v = 0; v < vertices; v++)
        {
            float x = xyz[v * 3], z = xyz[v * 3 + 2];
            if (z < 11f || z > 24f) continue;
            if (Math.Abs(x) < 1f) onPath = Math.Max(onPath, alpha[v]);
            if (Math.Abs(x) > 8f) aside = Math.Max(aside, alpha[v]);
        }
        Assert.That(onPath, Is.EqualTo(0f), "тропа к порталу видна сквозь дымку");
        Assert.That(aside, Is.GreaterThan(.2f), "в стороне от тропы дымка есть");
        Assert.That(ArenaMoodFxRules.PortalClearance(0f, 32f, portals, 1), Is.EqualTo(1f), "за концом тропы — дымка как везде");
        Assert.That(ArenaMoodFxRules.PortalClearance(0f, 10f, null, 0), Is.EqualTo(1f));
        Assert.That(ArenaMoodFxRules.SegmentDistance(3f, 5f, 0f, 0f, 0f, 10f), Is.EqualTo(3f).Within(1e-5f));
        Assert.That(ArenaMoodFxRules.SegmentDistance(0f, -4f, 0f, 0f, 0f, 10f), Is.EqualTo(4f).Within(1e-5f));
    }

    [Test]
    public void HazeTrianglesStayInsideTheMesh()
    {
        var indices = new int[ArenaMoodFxRules.HazeIndexCount(Samples)];
        ArenaMoodFxRules.HazeTriangles(indices, Samples);
        int vertices = ArenaMoodFxRules.HazeVertexCount(Samples);
        foreach (int i in indices) Assert.That(i, Is.InRange(0, vertices - 1));
        Assert.That(indices.Length % 3, Is.EqualTo(0));
        Assert.Throws<ArgumentException>(() => ArenaMoodFxRules.HazeTriangles(new int[3], Samples));
    }

    [Test]
    public void PollenAndFirefliesKeepToTheEdge()
    {
        var random = new ArenaMoodRandom(99u);
        const float edge = 12f;
        int fireflyNearEdge = 0, pollenCentre = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            float r = ArenaMoodFxRules.BandRadius(edge, ArenaMoodFxRules.FireflyBand, random.Next01());
            Assert.That(r, Is.InRange(edge * .75f - 1e-3f, edge + 7f + 1e-3f));
            if (r >= edge * .85f) fireflyNearEdge++;
            float p = ArenaMoodFxRules.BandRadius(edge, ArenaMoodFxRules.PollenBand, random.Next01());
            Assert.That(p, Is.InRange(edge * .6f - 1e-3f, edge + 4f + 1e-3f), "пыльца не над серединой боя");
            if (p < edge * .7f) pollenCentre++;
            float h = ArenaMoodFxRules.BandHeight(ArenaMoodFxRules.FireflyBand, random.Next01());
            Assert.That(h, Is.InRange(ArenaMoodFxRules.FireflyBand.MinHeight, ArenaMoodFxRules.FireflyBand.MaxHeight));
        }
        Assert.That(fireflyNearEdge, Is.GreaterThan(n * 9 / 10), "светлячки в основном у кромки и за ней");
        Assert.That(pollenCentre, Is.LessThan(n / 10), "пыльцы в глубине поляны мало");
        // Треугольное распределение: края и вершина.
        Assert.That(ArenaMoodFxRules.Triangular(0f, .6f), Is.EqualTo(0f));
        Assert.That(ArenaMoodFxRules.Triangular(1f, .6f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(ArenaMoodFxRules.Triangular(.6f, .6f), Is.EqualTo(.6f).Within(1e-5f));
        Assert.That(ArenaMoodFxRules.Triangular(float.NaN, .6f), Is.EqualTo(0f));
        float previous = -1f;
        for (float u = 0f; u <= 1f; u += .01f)
        {
            float t = ArenaMoodFxRules.Triangular(u, .3f);
            Assert.That(t, Is.GreaterThanOrEqualTo(previous));
            previous = t;
        }
    }

    [Test]
    public void SparksGoToBeamsOnlyInMist()
    {
        Assert.That(ArenaMoodFxRules.SparkCount(ArenaMoodRules.Weights(0f), 40, 0), Is.EqualTo(0), "день: вся пыльца у кромки");
        Assert.That(ArenaMoodFxRules.SparkCount(ArenaMoodRules.Weights(1f), 30, 3), Is.EqualTo(30), "туман: всё — искры в лучах");
        Assert.That(ArenaMoodFxRules.SparkCount(ArenaMoodRules.Weights(1f), 30, 0), Is.EqualTo(0), "без лучей искр нет");
        int split = ArenaMoodFxRules.SparkCount(ArenaMoodRules.Weights(.7f), 33, 2);
        Assert.That(split, Is.EqualTo(23));
        Assert.That(ArenaMoodFxRules.SparkCount(ArenaMoodRules.Weights(2f), 0, 0), Is.EqualTo(0));
        Assert.That(ArenaMoodFxRules.SteadyRate(24, 6f), Is.EqualTo(4f).Within(1e-6f));
        Assert.That(ArenaMoodFxRules.SteadyRate(0, 6f), Is.EqualTo(0f));
        Assert.That(ArenaMoodFxRules.SteadyRate(10, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void BeamsStayInTheUpperThirdAndFallFromTheTopRight()
    {
        for (uint seed = 1; seed < 200; seed++)
            for (int count = 1; count <= 4; count++)
                for (int i = 0; i < count; i++)
                {
                    ArenaMoodBeamSlot slot = ArenaMoodFxRules.BeamLayout(i, count, seed);
                    Assert.That(slot.BottomY, Is.GreaterThanOrEqualTo(ArenaMoodFxRules.BeamFightLine), "луч не ложится на бой");
                    Assert.That(ArenaMoodFxRules.BeamFightLine, Is.GreaterThan(.6f), "верхняя треть с запасом над героем");
                    Assert.That(slot.TopY, Is.GreaterThan(1f), "источник луча — за верхним краем кадра");
                    Assert.That(slot.TopX, Is.GreaterThanOrEqualTo(.5f), "лучи — с правой половины, как на рефе");
                    Assert.That(slot.BottomX, Is.LessThan(slot.TopX), "луч падает сверху-справа вниз-влево");
                    Assert.That(slot.Width, Is.InRange(1f, 1.8f));
                    ArenaMoodBeamSlot again = ArenaMoodFxRules.BeamLayout(i, count, seed);
                    Assert.That(again.TopX, Is.EqualTo(slot.TopX));
                }
        // Веер: лучи одной арены не стоят друг на друге.
        ArenaMoodBeamSlot first = ArenaMoodFxRules.BeamLayout(0, 3, 7u), last = ArenaMoodFxRules.BeamLayout(2, 3, 7u);
        Assert.That(last.TopX - first.TopX, Is.GreaterThan(.2f));
        Assert.That(ArenaMoodFxRules.BeamBreath(0f, 0f), Is.InRange(.75f, 1f));
        for (float t = 0f; t < 20f; t += .37f) Assert.That(ArenaMoodFxRules.BeamBreath(t, 1.3f), Is.InRange(.75f - 1e-5f, 1f + 1e-5f));
    }

    [Test]
    public void LanternsStandOnTheFarArcAndSpreadOut()
    {
        float far = (float)(Math.PI / 2);
        for (int count = 1; count <= 8; count++)
        {
            for (int slot = 0; slot < count; slot++)
                for (int attempt = 0; attempt < ArenaMoodFxRules.LanternAttempts; attempt++)
                {
                    float angle = ArenaMoodFxRules.LanternAngle(slot, count, attempt, far, slot % 2 == 0 ? 1f : -1f);
                    Assert.That(ArenaMoodFxRules.AngleBetween(angle, far), Is.LessThanOrEqualTo(ArenaMoodFxRules.LanternArc + 1e-4f),
                        "на ближней к камере стороне фонарей нет");
                    Assert.That(angle, Is.InRange(0f, (float)(2 * Math.PI)));
                }
            // Первые места разнесены по дуге, а не кучкой.
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                {
                    float d = ArenaMoodFxRules.AngleBetween(ArenaMoodFxRules.LanternAngle(a, count, 0, far, 1f),
                        ArenaMoodFxRules.LanternAngle(b, count, 0, far, -1f));
                    Assert.That(d, Is.GreaterThan(ArenaMoodFxRules.LanternArc * 2f / count * .5f), count + " фонарей: " + a + " и " + b);
                }
        }
        Assert.That(ArenaMoodFxRules.LanternDistance(0), Is.EqualTo(ArenaMoodFxRules.LanternBeyond));
        Assert.That(ArenaMoodFxRules.LanternDistance(1), Is.GreaterThan(ArenaMoodFxRules.LanternBeyond));
        Assert.That(ArenaMoodFxRules.LanternBeyond, Is.GreaterThanOrEqualTo(1f), "фонарь за полом боя, не на нём");
        Assert.That(ArenaMoodFxRules.AngleBetween(.1f, (float)(2 * Math.PI) - .1f), Is.EqualTo(.2f).Within(1e-4f));
        Assert.That(ArenaMoodFxRules.WrapAngle(-.5f), Is.EqualTo((float)(2 * Math.PI) - .5f).Within(1e-4f));
        Assert.That(ArenaMoodFxRules.WrapAngle(float.NaN), Is.EqualTo(0f));
    }

    [Test]
    public void FlickerStaysWithinItsAmount()
    {
        for (float t = 0f; t < 30f; t += .013f)
        {
            float f = ArenaMoodFxRules.Flicker(t, 2.1f, .1f);
            Assert.That(f, Is.InRange(.9f - 1e-5f, 1.1f + 1e-5f));
        }
        Assert.That(ArenaMoodFxRules.Flicker(3f, 1f, 0f), Is.EqualTo(1f));
        Assert.That(ArenaMoodFxRules.Flicker(3f, 1f, 5f), Is.InRange(.5f - 1e-5f, 1.5f + 1e-5f), "мерцание не гасит фонарь");
    }

    static float[] Circle(float radius)
    {
        var contour = new float[Samples];
        for (int i = 0; i < Samples; i++) contour[i] = radius;
        return contour;
    }
}
