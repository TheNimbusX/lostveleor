using Game.Sim;
using Game.View;
using NUnit.Framework;

// Махи Крушения на стеке серии сабли (06.10 поздно, PelagWreckComboLook): база — кобальт и сталь заданными цветами,
// формы — та же сборка другой краской без красного, оранжевого и золота, полосы темнеют к герою, мах 2 крупнее маха 1
// и оба не выходят за сектор Sim. NUnit без Is.AnyOf / Assert.Multiple (Unity).
public sealed class WreckComboLookTests
{
    private static readonly PelagForm[] Forms =
        { PelagForm.None, PelagForm.WreckBreakwater, PelagForm.WreckNinthWave, PelagForm.WreckGhostAnchor };

    private static float Luma(float r, float g, float b) => .2126f * r + .7152f * g + .0722f * b;

    [Test]
    public void BaseIsCobaltAndSteel()
    {
        PelagWreckComboLook.Palette p = PelagWreckComboLook.For(PelagForm.None);
        PelagWreckSwingLook.Rgb(0x0A1F4A, out float dr, out float dg, out float db);
        PelagWreckSwingLook.Rgb(0x2E6FE0, out float mr, out float mg, out float mb);
        PelagWreckSwingLook.Rgb(0x8FC4FF, out float lr, out float lg, out float lb);
        Assert.AreEqual(dr, p.DeepR, 1e-5f); Assert.AreEqual(dg, p.DeepG, 1e-5f); Assert.AreEqual(db, p.DeepB, 1e-5f);
        Assert.AreEqual(mr, p.MidR, 1e-5f); Assert.AreEqual(mg, p.MidG, 1e-5f); Assert.AreEqual(mb, p.MidB, 1e-5f);
        Assert.AreEqual(lr, p.LightR, 1e-5f); Assert.AreEqual(lg, p.LightG, 1e-5f); Assert.AreEqual(lb, p.LightB, 1e-5f);
        // Горячая кромка — почти белая (ярче 1) и холодная: синий канал сильнее красного (тёплый грейд игры желтит белое).
        Assert.Greater(Luma(p.HotR, p.HotG, p.HotB), 1f);
        Assert.Greater(p.HotB, p.HotR);
    }

    [Test]
    public void FormsAreRepaintsWithoutWarmHues()
    {
        foreach (PelagForm form in Forms)
        {
            PelagWreckSwingLook.HueSat(PelagWreckComboLook.MidHex(form), out float hue, out float sat);
            Assert.IsTrue(hue > 130f && hue < 290f, form + " hue " + hue);
            PelagWreckComboLook.Palette p = PelagWreckComboLook.For(form);
            float deep = Luma(p.DeepR, p.DeepG, p.DeepB), mid = Luma(p.MidR, p.MidG, p.MidB);
            float light = Luma(p.LightR, p.LightG, p.LightB), hot = Luma(p.HotR, p.HotG, p.HotB);
            Assert.Less(deep, mid, form.ToString());
            Assert.Less(mid, light, form.ToString());
            Assert.Less(light, hot, form.ToString());
        }
        PelagWreckSwingLook.Rgb(PelagWreckComboLook.MidHex(PelagForm.WreckBreakwater), out float br, out float bg, out float bb);
        PelagWreckSwingLook.Rgb(0x1FB37E, out float er, out float eg, out float eb);
        Assert.AreEqual(er, br, 1e-5f); Assert.AreEqual(eg, bg, 1e-5f); Assert.AreEqual(eb, bb, 1e-5f);
        Assert.AreEqual(0x4B3FD0, PelagWreckComboLook.MidHex(PelagForm.WreckNinthWave));
    }

    [Test]
    public void SwingTwoIsBiggerAndBothStayInsideTheSector()
    {
        const float sector = 2.8f;
        float one = PelagWreckComboLook.Radius(0, sector), two = PelagWreckComboLook.Radius(1, sector);
        Assert.Greater(two, one);
        Assert.LessOrEqual(two - PelagWreckComboLook.Back, sector);
        Assert.Greater(one, .75f * sector);
        Assert.Greater(PelagWreckComboLook.HitScale[1], PelagWreckComboLook.HitScale[0]);
    }

    // 07.10: махи Призрачного якоря — палитра его якоря (тень, полосы, тело), обвод не чёрный; принятый выпад формы
    // (For) — прежний бледный, у остальных форм махи и выпад в одной палитре.
    [Test]
    public void GhostSwingsWearTheAnchorPalette()
    {
        PelagWreckComboLook.Palette p = PelagWreckComboLook.ForSwing(PelagForm.WreckGhostAnchor);
        PelagWreckComboLook.Palette lunge = PelagWreckComboLook.For(PelagForm.WreckGhostAnchor);
        AssertHex(0xA99CF0, lunge.MidR, lunge.MidG, lunge.MidB);
        foreach (PelagForm form in Forms)
        {
            if (form == PelagForm.WreckGhostAnchor) continue;
            PelagWreckComboLook.Palette a = PelagWreckComboLook.For(form), s = PelagWreckComboLook.ForSwing(form);
            Assert.AreEqual(a.MidR, s.MidR, 1e-6f, form.ToString()); Assert.AreEqual(a.HotB, s.HotB, 1e-6f, form.ToString());
        }
        float sd = Luma(p.DeepR, p.DeepG, p.DeepB), sm = Luma(p.MidR, p.MidG, p.MidB), sl = Luma(p.LightR, p.LightG, p.LightB);
        Assert.Less(sd, sm); Assert.Less(sm, sl); Assert.Less(sl, Luma(p.HotR, p.HotG, p.HotB));
        AssertHex(PelagWreckGhostLook.ShadowHex, p.DeepR, p.DeepG, p.DeepB);
        AssertHex(PelagWreckGhostLook.BandHex, p.MidR, p.MidG, p.MidB);
        AssertHex(PelagWreckGhostLook.CoreHex, p.LightR, p.LightG, p.LightB);
        Assert.AreEqual(0xD9CCFF, PelagWreckGhostLook.CoreHex);
        Assert.AreEqual(0x8E7BFF, PelagWreckComboLook.MidHex(PelagForm.WreckGhostAnchor));
        PelagWreckSwingLook.Rgb(PelagWreckComboLook.GhostOutlineHex, out float r, out float g, out float b);
        Assert.Greater(Luma(r, g, b), .3f);
        PelagWreckSwingLook.HueSat(PelagWreckComboLook.GhostOutlineHex, out float hue, out _);
        Assert.IsTrue(hue > 230f && hue < 290f, "outline hue " + hue);
    }

    private static void AssertHex(int hex, float r, float g, float b)
    {
        PelagWreckSwingLook.Rgb(hex, out float er, out float eg, out float eb);
        Assert.AreEqual(er, r, 1e-5f); Assert.AreEqual(eg, g, 1e-5f); Assert.AreEqual(eb, b, 1e-5f);
    }

    // 07.10: сколы с края — реже, крупнее и светлее прежних (5/6 штук .17–.28 м, грань почти чёрная).
    [Test]
    public void EdgeChipsAreFewerLargerAndLighter()
    {
        Assert.Less(PelagWreckComboLook.ChipCount[0], 5);
        Assert.Less(PelagWreckComboLook.ChipCount[1], 6);
        Assert.Greater(PelagWreckComboLook.ChipSizeMin, .17f);
        Assert.Greater(PelagWreckComboLook.ChipSizeMax, .28f);
        float[] d = PelagWreckComboLook.ChipFaceDark;
        Assert.Greater(Luma(d[0], d[1], d[2]), Luma(.10f, .13f, .20f));
        float[] bevel = PelagWreckComboLook.ChipBevelAdd;
        Assert.Greater(Luma(bevel[0], bevel[1], bevel[2]), Luma(.38f, .45f, .58f));
        // Знак задетого махом — те же светлые сколы реже; знак задетого выпадом — принятый (3 по .15–.24 м, тёмные).
        Assert.Less(PelagWreckComboLook.HitChipCount, PelagWreckComboLook.MarkChipCount);
        Assert.Greater(PelagWreckComboLook.HitChipSizeMax, PelagWreckComboLook.MarkChipSizeMax);
        Assert.AreEqual(3, PelagWreckComboLook.MarkChipCount);
        Assert.AreEqual(.15f, PelagWreckComboLook.MarkChipSizeMin, 1e-6f);
        Assert.AreEqual(.24f, PelagWreckComboLook.MarkChipSizeMax, 1e-6f);
    }

    /// <summary>Тик (от контакта), когда голова волны маха stage проходит азимут a (град, + — к голове).</summary>
    private static float VisualTick(int stage, float a)
    {
        float lead = PelagWreckComboLook.LeadSeconds;
        float headSeconds = stage == 0 ? .10f : .11f;
        float t = PelagWreckComboLook.ArrivalSeconds(a, lead, PelagWreckComboLook.LeadFrom, .40f, headSeconds,
            PelagWreckComboLook.SpinDegrees[stage]);
        return (t - lead) * Simulation.TicksPerSecond;
    }

    /// <summary>Сдвиг тика удара Sim от контакта для врага на азимуте a (град, + — к голове) маха stage, направление +X.</summary>
    private static int SimOffset(int stage, float a)
    {
        double rad = a * System.Math.PI / 180.0;
        // Мах 1 уходит влево (+Y Sim), мах 2 — вправо (−Y).
        double side = stage == 0 ? 1.0 : -1.0;
        var delta = new FixVec2(Fix64.FromDouble(2.5 * System.Math.Cos(rad)), Fix64.FromDouble(2.5 * side * System.Math.Sin(rad)));
        return Simulation.WreckSweepOffset(stage, new FixVec2(Fix64.One, Fix64.Zero), delta);
    }

    // 07.10: волна идёт сквозь цель в тот тик, когда Sim её бьёт: на стороне входа голова захода — с головой Sim (±0,75
    // тика), на стороне выхода — не позже удара Sim и не раньше, чем удар известен виду (тик показа − 2, у края — ½ тика).
    [Test]
    public void WaveHeadSweepsTheTargetWhenTheSimHitsIt()
    {
        for (int stage = 0; stage <= 1; stage++)
        {
            for (float a = -66f; a <= -4f; a += 2f)
            {
                int k = SimOffset(stage, a);
                Assert.LessOrEqual(System.Math.Abs(VisualTick(stage, a) - k), .75f, "stage " + stage + " entry " + a + " sim " + k);
            }
            for (float a = 4f; a <= 66f; a += 2f)
            {
                int k = SimOffset(stage, a);
                float v = VisualTick(stage, a);
                Assert.LessOrEqual(v, k + .25f, "stage " + stage + " exit " + a + " sim " + k);
                Assert.GreaterOrEqual(v, k - 2.6f, "stage " + stage + " exit " + a + " sim " + k);
            }
        }
        // Заход начинается у края входа и к контакту приходит туда же, где голова была без захода.
        Assert.AreEqual(-75f, PelagWreckComboLook.HeadAzimuth(0f, PelagWreckComboLook.LeadSeconds, PelagWreckComboLook.LeadFrom, .40f, .10f, 12f), .01f);
        Assert.AreEqual(-15f, PelagWreckComboLook.HeadAzimuth(PelagWreckComboLook.LeadSeconds, PelagWreckComboLook.LeadSeconds,
            PelagWreckComboLook.LeadFrom, .40f, .10f, 12f), .01f);
        Assert.AreEqual(3, PelagWreckComboLook.LeadTicks);
    }
}
