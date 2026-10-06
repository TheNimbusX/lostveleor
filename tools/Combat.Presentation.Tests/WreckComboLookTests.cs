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
}
