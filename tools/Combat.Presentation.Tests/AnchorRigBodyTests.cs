using System;
using System.Numerics;
using System.Text.Json;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Риг якоря, правки критика 03.10: одно тело для игры и запечки (AnchorRigBody), выход головы со спины и уборка
/// натяжением цепи (не пружиной), контракт grip_socket.json v2, Бросок якоря по фазам спеки Броска.
/// </summary>
public sealed class AnchorRigBodyTests
{
    private static AnchorSkeleton Standing()
    {
        var s = AnchorSkeleton.Empty(Vector3.Zero);
        s.Hips = new Vector3(0, .95f, 0); s.Spine2 = new Vector3(0, 1.3f, 0); s.Head = new Vector3(0, 1.55f, 0);
        s.LUpLeg = new Vector3(-.1f, .9f, 0); s.LLeg = new Vector3(-.1f, .5f, 0); s.LFoot = new Vector3(-.1f, .08f, 0);
        s.RUpLeg = new Vector3(.1f, .9f, 0); s.RLeg = new Vector3(.1f, .5f, 0); s.RFoot = new Vector3(.1f, .08f, 0);
        s.LArm = new Vector3(-.2f, 1.4f, 0); s.LForeArm = new Vector3(-.3f, 1.15f, 0); s.LHand = new Vector3(-.3f, .9f, .1f);
        s.RArm = new Vector3(.2f, 1.4f, 0); s.RForeArm = new Vector3(.3f, 1.15f, 0); s.RHand = new Vector3(.3f, .9f, .1f);
        return s;
    }

    [Test]
    public void OneBodyForTheGameAndTheBake()
    {
        var head = new AnchorCapsule[AnchorRigBody.Capacity];
        var chain = new AnchorCapsule[AnchorRigBody.Capacity];
        AnchorRigBody.Build(Standing(), 1f, head, out int heads, chain, out int chains);
        Assert.That(heads, Is.EqualTo(12));
        Assert.That(chains, Is.EqualTo(8), "кисти и предплечья держат цепь — цепь их не обходит");
        Assert.That(head[0].Kind, Is.EqualTo(AnchorCapsuleKind.Center));
        Assert.That(head[0].Radius, Is.EqualTo(.38f).Within(1e-6f));
        Assert.That(head[1].Kind, Is.EqualTo(AnchorCapsuleKind.Ring));
        int hull = 0, trunk = 0;
        for (int i = 0; i < heads; i++) { if (head[i].Kind == AnchorCapsuleKind.Hull) hull++; if (head[i].Torso) trunk++; }
        Assert.That(hull, Is.EqualTo(10));
        Assert.That(trunk, Is.EqualTo(3), "центр, кольцо и оболочка торса — корпус");
        // Масштаб тела: радиусы растут вместе с ростом.
        AnchorRigBody.Build(Standing(), 1.5f, head, out _, chain, out _);
        Assert.That(head[0].Radius, Is.EqualTo(.57f).Within(1e-5f));
    }

    [Test]
    public void HeadLeavesTheBackMountWithoutBeingShotOutOfTheTorso()
    {
        // Голова на креплении спины внутри корпуса: при снятии корпус её не выстреливает (IgnoreTorso), пока она не вышла.
        var head = new AnchorCapsule[AnchorRigBody.Capacity];
        AnchorRigBody.Build(Standing(), 1f, head, out int n, null, out _);
        var core = new AnchorRigCore();
        core.Body.EyeLocal = new Vector3(0, .38f, 0);
        core.Body.Hull = AnchorRigCoreTests.BoxHull(.3f, .2f, .1f);
        core.Teleport(AnchorRigMode.OnBack, AnchorPose.At(new Vector3(0, 1.1f, -.15f), Quaternion.Identity));
        var grip = new Vector3(-.3f, .95f, .25f);
        core.EnterLive(AnchorRigMode.Draw, grip);
        Assert.That(core.IgnoreTorso, Is.True);
        core.StepLive(1f / 60, grip, grip, p => 0f, head, n);
        Assert.That(core.Body.Velocity.Length(), Is.LessThan(1f), "не выстрел из корпуса");
        for (int f = 0; f < 90 && core.IgnoreTorso; f++) core.StepLive(1f / 60, grip, grip, p => 0f, head, n);
        Assert.That(core.IgnoreTorso, Is.False, "вышла из корпуса — корпус снова твёрдый");
    }

    [Test]
    public void StowReelsTheChainAndTensionPullsTheHeadInNoSpring()
    {
        var core = AnchorRigCoreTests.Hanging(new Vector3(0, 2.4f, 0), new Vector3(0, .38f, 0), .3f);
        core.Body.Hull = AnchorRigCoreTests.BoxHull(.3f, .2f, .1f);
        var back = new Vector3(.1f, 2.35f, -.15f);
        core.HoldIgnoreTorso = true;
        core.ReelTo(.3f, 5f);
        float previous = core.CableLength;
        for (int f = 0; f < 40; f++)
        {
            Vector3 grip = Vector3.Lerp(new Vector3(0, 2.4f, 0), back, Math.Min(1f, f / 13f));
            core.StepLive(1f / 60, grip, grip, p => -5f, Array.Empty<AnchorCapsule>(), 0);
            Assert.That(core.CableLength, Is.LessThanOrEqualTo(previous + 1e-6f));
            previous = core.CableLength;
        }
        Assert.That(core.CableLength, Is.EqualTo(.3f).Within(1e-4f));
        Assert.That(Vector3.Distance(core.Body.Eye, back), Is.LessThanOrEqualTo(.31f));
        core.ResetCable();
        Assert.That(core.CableLength, Is.EqualTo(core.Settings.ChainLength));
        Assert.That(core.HoldIgnoreTorso, Is.False);
    }

    [Test]
    public void GripSocketV2IsTheSameFileForRigAndExporter()
    {
        var json = new JsonSerializerOptions { IncludeFields = true };
        const string v2 = @"{ ""version"": 2, ""units"": ""metres"", ""bone"": ""mixamorig:LeftHand"", ""position"": [0.0, 0.0819, 0.02184],
            ""rotation"": [0,0,0,1], ""support"": { ""bone"": ""mixamorig:RightHand"", ""position"": [0.0, 0.0819, 0.02184] } }";
        var data = JsonSerializer.Deserialize<AnchorGripSocketData>(v2, json);
        Assert.That(data.Valid, Is.True);
        Assert.That(data.position[1], Is.EqualTo(.0819f).Within(1e-6f));
        Assert.That(data.support.bone, Is.EqualTo("mixamorig:RightHand"));
        const string v1 = @"{ ""version"": 1, ""grip"": { ""bone"": ""mixamorig:LeftHand"", ""offset"": [0.0, 0.045, 0.012] } }";
        Assert.That(JsonSerializer.Deserialize<AnchorGripSocketData>(v1, json).Valid, Is.False, "единицы кости — не метры");
    }

    private static AnchorRigThrowInput Throw(byte phase = 2) => new AnchorRigThrowInput
    {
        Serial = 4, Phase = phase, CastTick = 100, ReleaseTick = 102, TautTick = 109, CatchTick = 117, PhaseEndTick = 120,
        Origin = new Vector2(1, 1), Direction = new Vector2(0, 1), HeadNow = new Vector2(1, 4), HeadNext = new Vector2(1, 5),
    };

    [TestCase(100.5f, AnchorLinePhase.Windup)] [TestCase(103f, AnchorLinePhase.Flight)] [TestCase(109.4f, AnchorLinePhase.Taut)]
    [TestCase(112f, AnchorLinePhase.Return)] [TestCase(118f, AnchorLinePhase.Catch)] [TestCase(121f, AnchorLinePhase.Exit)]
    public void ThrowPhasesFollowTheShownTickNotTheSimPhase(float shown, AnchorLinePhase expected)
    {
        Assert.That(AnchorRigThrowPlan.PhaseAt(Throw(), shown), Is.EqualTo(expected));
        Assert.That((byte)AnchorLinePhase.Exit, Is.EqualTo(6), "числа фаз — как AnchorThrowPhase спеки Броска");
    }

    [Test]
    public void ThrowHeadComesFromTheSimLineAndAnInterruptedThrowIsCaught()
    {
        Assert.That(AnchorRigThrowPlan.Frame(Throw(), 105.25f, out var state), Is.True);
        Assert.That(state.Along, Is.EqualTo(3.25f).Within(1e-4f));
        Assert.That(state.AlongSpeed, Is.EqualTo(30f).Within(1e-3f));
        Assert.That(state.Reached, Is.False);
        Assert.That(AnchorRigThrowPlan.PhaseAt(Throw(phase: 0), 105f), Is.EqualTo(AnchorLinePhase.Catch), "срыв: голову ловит цепь");
        Assert.That(AnchorRigThrowPlan.Frame(default, 1f, out _), Is.False);
    }
}
