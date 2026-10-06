using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Game.View;
using NUnit.Framework;

/// <summary>Запечка пути головы (anchor-core DESIGN §2): формат, выборка, оси корня, часы Sim, поправка удара.</summary>
public sealed class AnchorBakeTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static float[] A(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private static float[] A(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };

    /// <summary>Путь головы от хвата по кругу тем же ядром рига (как офлайн-инструмент), 4 сэмпла на тик + эталон 240 Гц.</summary>
    private static AnchorBakeData Bake(int ticks, out List<(float frame, Vector3 position)> reference)
    {
        var core = AnchorRigCoreTests.Hanging(new Vector3(.3f, 1.3f, 0), new Vector3(0, .38f, 0), .6f);
        core.Body.Hull = AnchorRigCoreTests.BoxHull(.3f, .2f, .1f);
        Vector3 Grip(float t) => new Vector3(.3f * (float)Math.Cos(t * 9), 1.3f, .3f * (float)Math.Sin(t * 9));
        var samples = new List<AnchorBakeSample>();
        reference = new List<(float, Vector3)>();
        float time = 0;
        for (int step = 0; step <= ticks * 8; step++)
        {
            if (step > 0)
            {
                core.StepLive(1f / 240, Grip(time), Grip(time + 1f / 240), p => -5f, Array.Empty<AnchorCapsule>(), 0);
                time += 1f / 240;
            }
            reference.Add((step / 8f, core.Output.Position));
            if (step % 2 == 0)
                samples.Add(new AnchorBakeSample
                {
                    t = step / 8f, p = A(core.Output.Position), q = A(core.Output.Rotation), v = A(core.Output.Velocity),
                    w = A(core.Output.AngularVelocity), grip = A(Grip(time)), gripQ = A(Quaternion.Identity),
                    taut = core.IsTaut(Grip(time)), tension = core.Body.LastTension,
                });
        }
        return new AnchorBakeData
        {
            version = 1, clip = "Test_Swing", windupTicks = 7, fps = 30, sub = 4, frames = ticks,
            rig = new AnchorBakeRig { chainLength = 1.6f, mass = 12, eyeLocal = new[] { 0f, .38f, 0f } },
            samples = samples.ToArray(),
            contacts = new[] { new AnchorBakeContact { kind = "swing", frame = 7, point = new[] { 0f, .7f, 2.4f }, radius = 2.45f } },
        };
    }

    [Test]
    public void JsonRoundTripAndHermiteSamplingFollowPhysicsBetweenSamples()
    {
        AnchorBakeData data = Bake(12, out var reference);
        string json = JsonSerializer.Serialize(data, Json);
        var bake = new AnchorBake(JsonSerializer.Deserialize<AnchorBakeData>(json, Json));
        Assert.That(bake.Valid, Is.True, bake.Error);
        Assert.That(bake.EndFrame, Is.EqualTo(12f));
        Assert.That(bake.ContactFrame, Is.EqualTo(7f));
        Assert.That(bake.LiveFrom, Is.EqualTo(12f), "мах уходит в живую физику в конце запечки");
        float worst = 0;
        foreach (var (frame, position) in reference)
            worst = Math.Max(worst, Vector3.Distance(bake.Sample(frame, out _, out _).Position, position));
        Assert.That(worst, Is.LessThan(.005f));
    }

    [Test]
    public void DesignFormatWithExtraKeysParses()
    {
        const string json = @"{ ""version"": 1, ""clip"": ""Pelag_AN_Wreck2_Slam"", ""windupTicks"": 9,
          ""source"": { ""fbx"": ""x.fbx"", ""sha256"": ""00"" },
          ""rig"": { ""chainLength"": 1.60, ""mass"": 12, ""inertia"": [0.75,0.55,0.75], ""eyeLocal"": [0,0.38,0], ""hull"": ""h"", ""headSize"": 0.94, ""gravity"": 9.81, ""airDrag"": [0.6,1.5] },
          ""fps"": 30, ""sub"": 4, ""frames"": 1,
          ""samples"": [
            { ""t"": 0.00, ""p"": [0,1,0], ""q"": [0,0,0,1], ""v"": [0,0,4], ""w"": [0,0,0], ""grip"": [0,1.2,0.3], ""gripQ"": [0,0,0,1], ""taut"": true, ""tension"": 812.0 },
            { ""t"": 0.25, ""p"": [0,1,0.0333], ""q"": [0,0,0,1], ""v"": [0,0,4], ""w"": [0,0,0], ""grip"": [0,1.2,0.3], ""gripQ"": [0,0,0,1], ""taut"": true, ""tension"": 800.0 } ],
          ""contacts"": [ { ""kind"": ""ground"", ""frame"": 0.25, ""point"": [0,0.2,2.2], ""radius"": 0 } ],
          ""ground"": [ { ""from"": 0.25, ""to"": 0.25 } ], ""loop"": null,
          ""seams"": { ""startFrom"": ""rest"" }, ""guidance"": { ""applied"": false }, ""validation"": { ""gripError"": 0 } }";
        var bake = new AnchorBake(JsonSerializer.Deserialize<AnchorBakeData>(json, Json));
        Assert.That(bake.Valid, Is.True, bake.Error);
        Assert.That(bake.GroundContact, Is.True);
        Assert.That(bake.LiveFrom, Is.EqualTo(.25f), "удар оземь уходит в живую физику в кадре касания");
        Assert.That(bake.Looped, Is.False);
        Assert.That(bake.Sample(.125f, out bool taut, out _).Position.Z, Is.EqualTo(.01667f).Within(.0005f));
        Assert.That(taut, Is.True);
        Assert.That(bake.TensionAt(0), Is.EqualTo(812f));
    }

    [Test]
    public void BrokenBakeIsRejectedNotGuessed()
    {
        Assert.That(new AnchorBake(new AnchorBakeData { fps = 30, sub = 4, samples = new AnchorBakeSample[1] }).Valid, Is.False);
        var data = Bake(2, out _);
        data.samples[3].t = data.samples[2].t;
        Assert.That(new AnchorBake(data).Error, Does.Contain("t"));
        // Контракт «t = кадр» (критик: инструмент писал секунды): секунды вместо кадров — отказ, а не тихий сдвиг.
        var seconds = Bake(2, out _);
        foreach (var sample in seconds.samples) sample.t /= 30f;
        Assert.That(new AnchorBake(seconds).Error, Does.Contain("не кадр"));
    }

    [Test]
    public void WorldVelocityIncludesRootMotionAndTurn()
    {
        var local = new AnchorPose(new Vector3(.2f, 1, 2), Quaternion.Identity, new Vector3(3, 0, 1), new Vector3(0, 0, 2));
        var rootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f);
        Vector3 rootPosition = new Vector3(5, 0, -2), rootVelocity = new Vector3(1, 0, 2), turn = new Vector3(0, 1.5f, 0);
        var world = AnchorBake.ToWorld(local, rootPosition, rootRotation, rootVelocity, turn, 1.1f, 1.25f);
        const float h = 1e-3f;
        var nextLocal = local; nextLocal.Position += local.Velocity * (1.25f * h);
        var next = AnchorBake.ToWorld(nextLocal, rootPosition + rootVelocity * h,
            Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.5f * h) * rootRotation), rootVelocity, turn, 1.1f, 1.25f);
        Vector3 numeric = (next.Position - world.Position) / h;
        Assert.That(Vector3.Distance(numeric, world.Velocity), Is.LessThan(.02f));
    }

    [Test]
    public void SimClockPinsStartOverheadAndContactFrames()
    {
        // Удар оземь: нажатие 100, над головой 105, удар 109; кадр над головой 5, контакта 9.
        Assert.That(AnchorBakeClock.Frame(100, 100, 109, 9, 105, 5, out _), Is.EqualTo(0f));
        Assert.That(AnchorBakeClock.Frame(105, 100, 109, 9, 105, 5, out float upRate), Is.EqualTo(5f).Within(1e-5f));
        Assert.That(upRate, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(AnchorBakeClock.Frame(109, 100, 109, 9, 105, 5, out _), Is.EqualTo(9f).Within(1e-5f));
        Assert.That(AnchorBakeClock.Frame(111.5f, 100, 109, 9, 105, 5, out float after), Is.EqualTo(11.5f).Within(1e-5f));
        Assert.That(after, Is.EqualTo(1f));
        // Ускорение каста: замах 7 → 5 тиков — тот же путь быстрее (кадров на тик 7/5), контакт в тик удара.
        Assert.That(AnchorBakeClock.Frame(204, 200, 205, 7, -1, 0, out float fast), Is.EqualTo(5.6f).Within(1e-4f));
        Assert.That(fast, Is.EqualTo(1.4f).Within(1e-5f));
        Assert.That(AnchorBakeClock.Frame(205, 200, 205, 7, -1, 0, out _), Is.EqualTo(7f).Within(1e-5f));
    }

    [Test]
    public void ChargeLoopPhaseIsSmoothAcrossFullCharge()
    {
        float previous = AnchorBakeClock.LoopFrame(0, 0, 12, .4f, .3f, 30, out float rate0);
        Assert.That(rate0, Is.EqualTo(1f).Within(1e-5f), "0,40 с на оборот из 12 кадров = кадр на тик");
        for (float t = .05f; t < 45; t += .05f)
        {
            float frame = AnchorBakeClock.LoopFrame(t, 0, 12, .4f, .3f, 30, out float rate);
            Assert.That((frame - previous) / .05f, Is.EqualTo(rate).Within(.02f), "t=" + t);
            previous = frame;
        }
        AnchorBakeClock.LoopFrame(40, 0, 12, .4f, .3f, 30, out float full);
        Assert.That(full, Is.EqualTo(12f / 9f).Within(1e-4f), "0,30 с на оборот");
    }

    [Test]
    public void ReleaseVariantPicksNearestPhase()
    {
        for (int k = 0; k < 8; k++)
        {
            float frame = 12f * k / 8 + .3f;
            Assert.That(AnchorBakeClock.ReleaseVariant(frame + 24, 12, 8, out float offset), Is.EqualTo(k));
            Assert.That(offset, Is.EqualTo(.3f).Within(1e-4f));
        }
        Assert.That(AnchorBakeClock.ReleaseVariant(11.9f, 12, 8, out float wrap), Is.EqualTo(0));
        Assert.That(wrap, Is.EqualTo(-.1f).Within(1e-4f));
    }

    [Test]
    public void ContactCorrectionEntersSmoothlyOverFourFrames()
    {
        Assert.That(AnchorBakeClock.CorrectionWeight(4.9f, 9, 4, out float s0), Is.Zero);
        Assert.That(s0, Is.Zero);
        Assert.That(AnchorBakeClock.CorrectionWeight(9, 9, 4, out float s1), Is.EqualTo(1f));
        Assert.That(s1, Is.Zero);
        float last = 0;
        for (float f = 5; f <= 9; f += .1f)
        {
            float w = AnchorBakeClock.CorrectionWeight(f, 9, 4, out _);
            Assert.That(w, Is.GreaterThanOrEqualTo(last - 1e-6f));
            last = w;
        }
        // Пик скорости поправки 0,30 м за 4 кадра: 1,875·0,30/0,133 с ≈ 4,2 м/с — на фоне 16–28 м/с удара.
        AnchorBakeClock.CorrectionWeight(7, 9, 4, out float peak);
        Assert.That(.30f * peak * 30, Is.EqualTo(4.22f).Within(.05f));
    }

    [Test]
    public void LoopedBakeWrapsInsideLoop()
    {
        var data = Bake(12, out _);
        data.loop = new AnchorBakeRange { from = 0, to = 12 };
        var bake = new AnchorBake(data);
        Assert.That(bake.Looped, Is.True);
        Assert.That(bake.Wrap(13.5f), Is.EqualTo(1.5f).Within(1e-4f));
        Assert.That(bake.Wrap(-.5f), Is.EqualTo(11.5f).Within(1e-4f));
    }
}
