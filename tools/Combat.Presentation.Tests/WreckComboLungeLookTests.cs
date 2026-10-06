using Game.Sim;
using Game.View;
using NUnit.Framework;

// Выпад Крушения на стеке серии сабли (06.10 поздно, PelagWreckComboLungeLook): гребень по полосе стоит плавником —
// ноль на самом фронте, быстрый подъём с перелётом, ровный спад назад к острому хвосту; Волнорез выше базы, Девятый
// вал растёт с долей урона; всплеск удара — по кругу Sim. NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckComboLungeLookTests
{
    [Test]
    public void CrestIsAFinTallAtTheFrontSharpAtTheTail()
    {
        Assert.AreEqual(0f, PelagWreckComboLungeLook.Profile(0f), 1e-6f, "на самом фронте высоты нет");
        float peak = 0f, peakAge = 0f;
        for (float a = .002f; a < .5f; a += .002f)
        {
            float h = PelagWreckComboLungeLook.Profile(a);
            if (h > peak) { peak = h; peakAge = a; }
        }
        Assert.Greater(peak, 1f, "перелёт хлопка");
        Assert.Less(peak, 1.2f);
        Assert.Less(peakAge, .05f, "пик сразу за фронтом (меньше 0,75 м при 15 м/с)");
        // За пиком — только спад до пола: хвост острый, без полки во всю полосу.
        float last = peak;
        for (float a = peakAge; a < .5f; a += .01f)
        {
            float h = PelagWreckComboLungeLook.Profile(a);
            Assert.LessOrEqual(h, last + 1e-5f, "спад на " + a);
            last = h;
        }
        Assert.AreEqual(PelagWreckComboLungeLook.CollapseFloor, PelagWreckComboLungeLook.Profile(.45f), 1e-5f);
        // Выпуклая спина: на 3 м позади фронта гребень ещё высокий, к 5 м — низкий.
        Assert.Greater(PelagWreckComboLungeLook.Profile(.2f), .6f, "спина выпуклая");
        Assert.Less(PelagWreckComboLungeLook.Profile(.36f), .45f, "к 5 м позади фронта гребень уже низкий");
    }

    [Test]
    public void FormsChangeHeightBaseStaysInSpec()
    {
        float baseHeight = PelagWreckComboLungeLook.CrestHeight(PelagForm.None, 100);
        Assert.IsTrue(baseHeight >= .6f && baseHeight <= 1f, "база " + baseHeight);
        Assert.Greater(PelagWreckComboLungeLook.CrestHeight(PelagForm.WreckBreakwater, 100), baseHeight);
        float ninth1 = PelagWreckComboLungeLook.CrestHeight(PelagForm.WreckNinthWave, 100);
        float ninth2 = PelagWreckComboLungeLook.CrestHeight(PelagForm.WreckNinthWave, 200);
        Assert.Greater(ninth2, ninth1, "Девятый вал растёт с зарядом");
        Assert.LessOrEqual(ninth2, 1.6f);
        Assert.Greater(PelagWreckComboLungeLook.EchoHeight, 1f, "эхо выше основного");
    }

    [Test]
    public void ImpactFitsTheSimCircle()
    {
        const float radius = 1.2f;   // Simulation.WreckSlamRadius
        Assert.AreEqual(radius, PelagWreckComboLungeLook.RingRadius(radius), 1e-5f);
        float star = PelagWreckComboLungeLook.StarDiameter(radius);
        Assert.IsTrue(star > radius && star < 2f * radius + .01f, "звезда " + star);
        Assert.Greater(PelagWreckComboLungeLook.CircleHitScale, PelagWreckComboLungeLook.WaveHitScale);
        Assert.IsTrue(PelagWreckComboLungeLook.BigRockAt(8, 8, .99f), "на последнем шаге — всегда крупный камень");
    }
}
