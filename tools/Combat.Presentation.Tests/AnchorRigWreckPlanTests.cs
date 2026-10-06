using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Game.View;
using NUnit.Framework;

// Крушение v4 на риге: правила (AnchorRigWreckPlan), цепь-хлыст (AnchorWhipChain), вторичный поворот (AnchorSecondary)
// и настоящие запечки Resources/Weapons/Pelag/AnchorBakes/Pelag_AN_Wreck4_* (artifacts/wreck/v4/integrate/bake).
public sealed class AnchorRigWreckPlanTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static string BakeDir
    {
        get
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                string p = Path.Combine(dir, "razlom", "Assets", "Resources", "Weapons", "Pelag", "AnchorBakes");
                if (Directory.Exists(p)) return p;
            }
            return Path.GetFullPath(Path.Combine("..", "..", "razlom", "Assets", "Resources", "Weapons", "Pelag", "AnchorBakes"));
        }
    }

    private static AnchorBake Load(string clip)
    {
        string path = Path.Combine(BakeDir, clip + ".anchorbake.json");
        Assert.That(File.Exists(path), Is.True, path);
        var bake = new AnchorBake(JsonSerializer.Deserialize<AnchorBakeData>(File.ReadAllText(path), Json));
        Assert.That(bake.Valid, Is.True, clip + ": " + bake.Error);
        return bake;
    }

    [Test]
    public void StateAndBakeNames_AreTheClipsWithoutPrefix()
    {
        Assert.That(AnchorRigWreckPlan.StateName(AnchorRigWreckPlan.Swing1), Is.EqualTo("Base Layer.Wreck4_Swing1"));
        Assert.That(AnchorRigWreckPlan.BakeName(AnchorRigWreckPlan.Lunge, 0), Is.EqualTo("Pelag_AN_Wreck4_Lunge"));
        Assert.That(AnchorRigWreckPlan.RequiredClips, Has.Length.EqualTo(5));
    }

    [Test]
    public void RealBakes_FiveClips_ContactsOnTheClipFrames_SeamsMeet()
    {
        string[] clips = AnchorRigWreckPlan.RequiredClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnchorBake bake = Load(clips[i]);
            Assert.That(bake.Sub, Is.EqualTo(4), "запись 120 Гц — 4 сэмпла на тик");
            Assert.That(bake.HasCable && bake.HasHandle, Is.True, "v3: длина цепи и рукоять в сэмплах");
            Assert.That(bake.CableAt(0f), Is.GreaterThan(.4f));
            if (i == 0) continue;
            // Стык: кадр 0 этого клипа = конец прошлого (превью шло серией подряд).
            AnchorBake prev = Load(clips[i - 1]);
            Vector3 a = prev.Sample(prev.EndFrame, out _, out _).Position, b = bake.Sample(0f, out _, out _).Position;
            Assert.That(Vector3.Distance(a, b), Is.LessThan(.002f), clips[i - 1] + " → " + clips[i]);
        }
        foreach (string swing in new[] { AnchorRigWreckPlan.Swing1, AnchorRigWreckPlan.Swing2 })
        {
            AnchorBake bake = Load(swing);
            Assert.That(bake.ContactFrame, Is.EqualTo(5f));
            Vector3 hit = bake.Sample(5f, out _, out _).Position;
            Assert.That(hit.Z, Is.EqualTo(1.25f).Within(.08f), swing + ": голова в контакте перед героем (цель превью 1,25 м)");
            Assert.That(hit.Y, Is.EqualTo(.85f).Within(.12f), swing + ": высота контакта (сэмпл 120 Гц после шага удара)");
            Assert.That(bake.Sample(5f, out _, out _).Velocity.Length(), Is.GreaterThan(12f), "хлёст в контакте");
        }
        AnchorBake lunge = Load(AnchorRigWreckPlan.Lunge);
        Assert.That(lunge.GroundContact, Is.True);
        Assert.That(lunge.ContactPoint.Z, Is.EqualTo(2.2f).Within(.05f), "удар в 2,2 м перед корнем после шага");
        Assert.That(lunge.Phases.release, Is.EqualTo(4.5f));
        Assert.That(lunge.CableAt(10f), Is.GreaterThan(1.3f), "на выпаде цепь выдана");
        Assert.That(Load(AnchorRigWreckPlan.Stow).Phases.land, Is.EqualTo(5.5f));
    }

    [Test]
    public void GoesLive_SwingWithoutNextPressFromFrame7_EndOfBake()
    {
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Swing1, 6.9f, 9f, false), Is.False);
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Swing1, 7f, 9f, false), Is.True, "мах без нажатия — маятник");
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Swing2, 8.5f, 9f, true), Is.False, "нажатие было — запечка до стыка");
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Lunge, 15f, 16f, false), Is.False);
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Lunge, 16f, 16f, false), Is.True);
        Assert.That(AnchorRigWreckPlan.GoesLive(AnchorRigWreckPlan.Stow, 8f, 8f, false), Is.False, "уборка — до посадки на спину");
    }

    [Test]
    public void Weights_DrawRampsSecondaryAndHandle_StowLaysThemDown_ImpactFadesInTheReel()
    {
        var draw = new AnchorBakePhases { grab = 3.25f };
        Assert.That(AnchorRigWreckPlan.HandleWeight(AnchorRigWreckPlan.Draw, 3f, draw), Is.EqualTo(0f));
        Assert.That(AnchorRigWreckPlan.HandleWeight(AnchorRigWreckPlan.Draw, 4.25f, draw), Is.EqualTo(1f), "рукоять в руках за кадр после хвата");
        Assert.That(AnchorRigWreckPlan.SecondaryWeight(AnchorRigWreckPlan.Draw, 3.25f, draw), Is.EqualTo(0f));
        Assert.That(AnchorRigWreckPlan.SecondaryWeight(AnchorRigWreckPlan.Draw, 8f, draw), Is.EqualTo(1f));
        var stow = new AnchorBakePhases { lay0 = 3f, lay1 = 4f, land = 5.5f };
        Assert.That(AnchorRigWreckPlan.HandleWeight(AnchorRigWreckPlan.Stow, 4f, stow), Is.EqualTo(0f), "рукоять на спине к lay1");
        Assert.That(AnchorRigWreckPlan.SecondaryWeight(AnchorRigWreckPlan.Stow, 4f, stow), Is.EqualTo(0f), "на посадке — ровно управляемый поворот");
        var lunge = new AnchorBakePhases { release = 4.5f, hold = 10.5f, @short = 14.5f };
        Assert.That(AnchorRigWreckPlan.ImpactWeight(4.5f, 8f, lunge), Is.EqualTo(0f));
        Assert.That(AnchorRigWreckPlan.ImpactWeight(8f, 8f, lunge), Is.EqualTo(1f), "к удару подвод целиком");
        Assert.That(AnchorRigWreckPlan.ImpactWeight(10f, 8f, lunge), Is.EqualTo(1f), "в воронке держится");
        Assert.That(AnchorRigWreckPlan.ImpactWeight(14.5f, 8f, lunge), Is.EqualTo(0f), "к натягу цепи — без сдвига");
        Assert.That(AnchorRigWreckPlan.SecondaryTune(AnchorRigWreckPlan.Lunge, 9f, 8f, lunge).W, Is.EqualTo(AnchorSecondary.Bite.W));
        Assert.That(AnchorRigWreckPlan.HangDrag(0f), Is.EqualTo(7.8f).Within(1e-4f));
        Assert.That(AnchorRigWreckPlan.HangDrag(2f), Is.EqualTo(.8f).Within(.01f));
        Assert.That(AnchorRigWreckPlan.WhipLength(.5f, 1.5f, .4f, 1f / 60f), Is.EqualTo(.5f + 40f / 60f).Within(1e-4f), "выдача не быстрее 40 м/с");
        Assert.That(AnchorRigWreckPlan.WhipLength(.5f, .3f, .45f, 1f / 60f), Is.EqualTo(.452f).Within(1e-4f), "не короче хорды");
    }

    [Test]
    public void WhipChain_HangsWithSag_KeepsLength_StaysOutOfCapsules()
    {
        var chain = new AnchorWhipChain(16);
        Vector3 a = new Vector3(0, 1.4f, 0), b = new Vector3(.4f, 1.4f, 0);
        var caps = new[] { new AnchorCapsule(new Vector3(.2f, 1.0f, 0), new Vector3(.2f, 1.3f, 0), .05f) };
        chain.Seed(a, b, .53f);
        for (int i = 0; i < 480; i++) chain.Step(1f / 480f, a, b, Vector3.Zero, caps, 1, 0, 0, p => 0f);
        float length = 0f;
        for (int i = 0; i < chain.N; i++) length += Vector3.Distance(chain.X[i], chain.X[i + 1]);
        Assert.That(length, Is.EqualTo(.53f).Within(.53f * .03f), "длина держится (≤ 3 %)");
        Assert.That(chain.Bow(), Is.GreaterThan(.05f), "цепь провисает, а не палка");
        Assert.That(chain.X[0], Is.EqualTo(a));
        Assert.That(chain.X[chain.N], Is.EqualTo(b));
        for (int i = 1; i < chain.N; i++)
            Assert.That(chain.Depth(caps[0], chain.X[i], out _), Is.LessThan(.01f), "звено " + i + " не в капсуле");
        var nodes = new Vector3[16];
        int count = chain.Resample(AnchorChainLine.Pitch, nodes);
        Assert.That(nodes[0], Is.EqualTo(b), "звенья от кольца");
        Assert.That(nodes[count - 1], Is.EqualTo(a), "последнее — у рукояти");
        for (int i = 0; i < count - 2; i++) Assert.That(Vector3.Distance(nodes[i], nodes[i + 1]), Is.LessThanOrEqualTo(AnchorChainLine.Pitch + 1e-4f));
    }

    [Test]
    public void WhipChain_FollowsAFastHeadWithLag_NotAStraightStick()
    {
        var chain = new AnchorWhipChain(16);
        Vector3 a = new Vector3(0, 1.2f, 0);
        chain.Seed(a, a + new Vector3(0, 0, .45f), .53f);
        float maxBow = 0f;
        for (int i = 0; i < 120; i++)
        {
            float t = i / 240f, angle = t * 20f;   // голова идёт по дуге 20 рад/с (хлёст маха)
            Vector3 b = a + new Vector3((float)Math.Sin(angle), 0, (float)Math.Cos(angle)) * .48f;
            for (int s = 0; s < 2; s++) chain.Step(1f / 480f, a, b, Vector3.Zero, null, 0, 0, 0, null);
            maxBow = Math.Max(maxBow, chain.Bow());
        }
        Assert.That(maxBow, Is.GreaterThan(.03f), "на маху цепь выгибается");
        Assert.That(chain.MaxStrain, Is.LessThan(.2f));
    }

    [Test]
    public void Secondary_LagsAndSettles_NeverSpins()
    {
        var sec = new AnchorSecondary();
        Quaternion q = Quaternion.Identity;
        sec.Reset(q);
        float maxDev = 0f;
        for (int i = 0; i < 120; i++)
        {
            q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Math.Min(i, 30) * .05f));   // разгон 1,5 рад за 0,5 с
            sec.Step(q, 1f / 60f, AnchorSecondary.Mace);
            maxDev = Math.Max(maxDev, sec.LastDev);
            Assert.That(sec.Wd.Length(), Is.LessThanOrEqualTo(AnchorSecondary.Mace.MaxW + 1e-3f), "не раскручивается");
        }
        Assert.That(maxDev, Is.GreaterThan(.02f), "на разгоне голова отстаёт");
        Assert.That(maxDev, Is.LessThanOrEqualTo(AnchorSecondary.Mace.MaxDev + 1e-4f), "отставание в клине");
        Assert.That(AnchorQuat.Angle(sec.Show(q, 1f), q), Is.LessThan(.01f), "после остановки сошлась с управляемым");
        Assert.That(AnchorQuat.Angle(sec.Show(q, 0f), q), Is.LessThan(1e-4f));
    }
}
