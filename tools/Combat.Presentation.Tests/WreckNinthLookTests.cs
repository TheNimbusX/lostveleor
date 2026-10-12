using Game.Sim;
using Game.View;
using NUnit.Framework;

// Крушение · Девятый вал (07.10, PelagWreckNinthLook): заряды по доле урона Sim, заряд — один за мах 1 или 2, вал шире у
// точки удара ровно на полуширину Sim и сужается вперёд, гребень выше по зарядам, камни крупнее. NUnit без Is.AnyOf.
public sealed class WreckNinthLookTests
{
    [Test]
    public void ChargesFollowSimDamagePercent()
    {
        Assert.AreEqual(0, PelagWreckNinthLook.ChargesFromPercent(Simulation.WreckNinthPercent(0)));
        Assert.AreEqual(1, PelagWreckNinthLook.ChargesFromPercent(Simulation.WreckNinthPercent(1)));
        Assert.AreEqual(2, PelagWreckNinthLook.ChargesFromPercent(Simulation.WreckNinthPercent(2)));
        Assert.AreEqual(2, PelagWreckNinthLook.ChargesFromPercent(Simulation.WreckNinthPercent(5)), "больше двух не бывает");
    }

    [Test]
    public void ChargeOncePerSwingOnlyForSwingsOneAndTwo()
    {
        Assert.IsTrue(PelagWreckNinthLook.GainsCharge(0, 0));
        Assert.IsTrue(PelagWreckNinthLook.GainsCharge(1, 0));
        Assert.IsTrue(PelagWreckNinthLook.GainsCharge(1, 1), "мах 2 после заряда маха 1");
        Assert.IsFalse(PelagWreckNinthLook.GainsCharge(0, 1), "второй задетый тем же махом — не заряд");
        Assert.IsFalse(PelagWreckNinthLook.GainsCharge(1, 3));
        Assert.IsFalse(PelagWreckNinthLook.GainsCharge(2, 0), "выпад зарядов не даёт");
        Assert.IsFalse(PelagWreckNinthLook.GainsCharge(-1, 0));
    }

    [Test]
    public void WaveIsLaneWideAtImpactAndTapersForward()
    {
        Assert.AreEqual(1f, PelagWreckNinthLook.LateralTaper(0f), 1e-6f, "у точки удара — ровно край полосы Sim");
        float last = 1f;
        for (int i = 1; i <= 10; i++)
        {
            float k = PelagWreckNinthLook.LateralTaper(i / 10f);
            Assert.Less(k, last, "сужается вперёд, шаг " + i);
            last = k;
        }
        Assert.Greater(last, .3f, "к концу не в точку — полоса Sim там ещё бьёт");
        Assert.AreEqual(1f, PelagWreckNinthLook.HeightTaper(0f), 1e-6f);
        Assert.Less(PelagWreckNinthLook.HeightTaper(1f), 1f);
        Assert.AreEqual(0f, PelagWreckNinthLook.PathFraction(2.2f, 2.2f, 8f), 1e-6f);
        Assert.AreEqual(1f, PelagWreckNinthLook.PathFraction(8f, 2.2f, 8f), 1e-6f);
    }

    [Test]
    public void MoreChargesTallerCrestBiggerRocksStrongerGlow()
    {
        Assert.AreEqual(.95f, PelagWreckNinthLook.CenterHeight(0), 1e-5f, "без зарядов — высота гребня базы");
        Assert.Greater(PelagWreckNinthLook.CenterHeight(1), PelagWreckNinthLook.CenterHeight(0));
        Assert.Greater(PelagWreckNinthLook.CenterHeight(2), PelagWreckNinthLook.CenterHeight(1));
        Assert.Greater(PelagWreckNinthLook.RockMax(2), PelagWreckNinthLook.RockMax(1));
        Assert.Greater(PelagWreckNinthLook.BigRocksAt(2, 1), 0);
        Assert.AreEqual(0, PelagWreckNinthLook.BigRocksAt(0, 1));
        Assert.Greater(PelagWreckNinthLook.AuraScale(2), PelagWreckNinthLook.AuraScale(1));
        Assert.AreEqual(0f, PelagWreckNinthLook.AuraScale(0), 1e-6f);
        Assert.Greater(PelagWreckNinthLook.MotesPerSecond(2), PelagWreckNinthLook.MotesPerSecond(1));
        Assert.Greater(PelagWreckNinthLook.FlareSize(2), PelagWreckNinthLook.FlareSize(1));
    }

    [Test]
    public void FrontIsAWedgeAndTheEndIsPointed()
    {
        Assert.AreEqual(0f, PelagWreckNinthLook.Profile(0f), 1e-6f, "на самом фронте высоты нет");
        Assert.Less(PelagWreckNinthLook.Profile(PelagWreckNinthLook.RiseSeconds * .25f), .5f, "фронт — клин, не отвесная стенка");
        Assert.Greater(PelagWreckNinthLook.Profile(PelagWreckNinthLook.RiseSeconds), 1f, "перелёт хлопка");
        Assert.AreEqual(1f, PelagWreckNinthLook.Profile(PelagWreckNinthLook.SettleSeconds), 1e-4f);
        Assert.AreEqual(PelagWreckNinthLook.CollapseFloor, PelagWreckNinthLook.Profile(1f), 1e-6f, "позади — осел");
        Assert.AreEqual(1f, PelagWreckNinthLook.EndTaper(4f, 8f), 1e-6f, "до последних метров — полная высота");
        Assert.AreEqual(PelagWreckNinthLook.EndTaperFloor, PelagWreckNinthLook.EndTaper(8f, 8f), 1e-6f, "конец полосы — острый");
        Assert.Less(PelagWreckNinthLook.EndTaper(7.5f, 8f), PelagWreckNinthLook.EndTaper(7f, 8f));
    }

    [Test]
    public void FanSpansSimLaneWidth()
    {
        // Полуширина Sim: Width 1,5 × доля / 200 — 1 заряд 1,125 м, 2 заряда 1,5 м.
        Assert.Greater(PelagWreckNinthLook.FanWidth(1.5f), 3f);
        Assert.Greater(PelagWreckNinthLook.FanWidth(1.5f), PelagWreckNinthLook.FanWidth(1.125f));
    }

    [Test]
    public void PaletteIsIndigoWithoutWarmHues()
    {
        Assert.AreEqual(PelagWreckComboLook.MidHex(PelagForm.WreckNinthWave), PelagWreckNinthLook.IndigoHex);
        foreach (int hex in new[] { PelagWreckNinthLook.IndigoHex, PelagWreckNinthLook.DeepHex, PelagWreckNinthLook.LightHex,
                     PelagWreckNinthLook.PaleHex, PelagWreckNinthLook.ContourHex })
        {
            int r = (hex >> 16) & 255, g = (hex >> 8) & 255, b = hex & 255;
            Assert.Greater(b, r, hex.ToString("X6") + ": синее красного");
            Assert.GreaterOrEqual(r, g - 2, hex.ToString("X6") + ": фиолетовый, не голубой");
        }
    }
}
