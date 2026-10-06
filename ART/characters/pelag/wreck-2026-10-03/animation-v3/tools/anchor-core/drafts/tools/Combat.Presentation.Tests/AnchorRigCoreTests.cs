using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Game.View;
using NUnit.Framework;

/// <summary>Риг якоря (anchor-core DESIGN §1, §5 #4, #9, #17): живая физика честная, стыки режимов без скачков.</summary>
public sealed class AnchorRigCoreTests
{
    internal static Vector3[] BoxHull(float x, float y, float z)
    {
        var hull = new Vector3[8];
        for (int c = 0; c < 8; c++)
            hull[c] = new Vector3((c & 1) == 0 ? -x : x, (c & 2) == 0 ? -y : y, (c & 4) == 0 ? -z : z);
        return hull;
    }

    internal static AnchorRigCore Hanging(Vector3 grip, Vector3 eyeLocal, float offsetX = 0)
    {
        var core = new AnchorRigCore();
        core.Body.EyeLocal = eyeLocal;
        float l = core.Settings.ChainLength;
        // Кольцо ровно на L от хвата (цепь натянута), кольцо над центром (поворот единичный).
        Vector3 eye = grip + new Vector3(offsetX, -(float)Math.Sqrt(l * l - offsetX * offsetX), 0);
        var pose = AnchorPose.At(eye - eyeLocal, Quaternion.Identity);
        core.Teleport(AnchorRigMode.OnBack, pose);
        core.EnterLive(AnchorRigMode.InHandLive, grip);
        return core;
    }

    [Test]
    public void PointMassPendulumHasFreePeriodWithoutSprings()
    {
        // Кольцо в центре массы: голова — точечный маятник на L = 1,6. Пружин к позе нет — период 2π√(L/g).
        var grip = new Vector3(0, 3, 0);
        var core = Hanging(grip, Vector3.Zero);
        core.Settings.AirDrag = 0; core.Settings.SpinDrag = 0;
        core.Body.Velocity = new Vector3(.35f, 0, 0);
        float time = 0, previousX = 0, firstCross = -1, lastCross = -1;
        int crossings = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            core.StepLive(1f / 60, grip, grip, null, Array.Empty<AnchorCapsule>(), 0);
            time += 1f / 60;
            float x = core.Body.Position.X;
            if (frame > 0 && previousX > 0 && x <= 0)
            {
                float t = time - 1f / 60 * (x / (x - previousX));
                if (firstCross < 0) firstCross = t; else { lastCross = t; crossings++; }
            }
            previousX = x;
        }
        float period = (lastCross - firstCross) / crossings;
        float expected = 2 * (float)Math.PI * (float)Math.Sqrt(core.Settings.ChainLength / core.Settings.Gravity);
        Assert.That(period, Is.EqualTo(expected).Within(expected * .02f));
    }

    [Test]
    public void SpinningOverheadKeepsChainLengthAndStaysTaut()
    {
        // «Вертолёт» (§5 #4, #10): хват по кругу r 0,35 м, 2 оборота/с, голова уже раскручена в фазе с рукой.
        // Цепь не тянется больше 5 мм; на скорости голова относительно хвата > 8 м/с — цепь натянута.
        const float a = .35f, omega = 4 * (float)Math.PI, height = 2.1f;
        var core = new AnchorRigCore();
        core.Body.EyeLocal = new Vector3(0, .38f, 0);
        core.Body.Hull = BoxHull(.3f, .2f, .1f);
        float l = core.Settings.ChainLength, reach = a + l + .38f;
        // Ось кольца (+Y головы) смотрит на хват (−X): поворот на 90° вокруг Z.
        var spin = new AnchorPose(new Vector3(reach, height - .06f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI / 2),
            new Vector3(0, 0, omega * reach), new Vector3(0, omega, 0));
        core.Teleport(AnchorRigMode.Baked, spin);
        Vector3 previous = new Vector3(a, height, 0);
        core.EnterLive(AnchorRigMode.InHandLive, previous);
        float worstStretch = 0; int slackFast = 0, fastFrames = 0;
        for (int frame = 1; frame <= 120; frame++)
        {
            float angle = frame / 60f * omega;
            Vector3 grip = new Vector3(a * (float)Math.Cos(angle), height, a * (float)Math.Sin(angle));
            core.StepLive(1f / 60, previous, grip, p => -10f, Array.Empty<AnchorCapsule>(), 0);
            float span = Vector3.Distance(core.Body.Eye, grip);
            worstStretch = Math.Max(worstStretch, span - core.CableLength);
            if ((core.Body.Velocity - (grip - previous) * 60).Length() > 8)
            {
                fastFrames++;
                if (span < core.CableLength - .02f) slackFast++;
            }
            previous = grip;
        }
        Assert.That(worstStretch, Is.LessThan(.005f));
        Assert.That(fastFrames, Is.GreaterThan(60));
        Assert.That(slackFast, Is.Zero);
    }

    [Test]
    public void ModeSeamsKeepPositionVelocityAndMeetContact()
    {
        var grip = new Vector3(0, 1.2f, 0);
        var core = Hanging(grip, new Vector3(0, .38f, 0), .4f);
        for (int i = 0; i < 20; i++) core.StepLive(1f / 60, grip, grip, null, Array.Empty<AnchorCapsule>(), 0);
        AnchorPose live = core.Output;
        // Запечка стартует в 5 см и с другой скоростью: показанное не прыгает, смещение гаснет к контакту − 1 (3 тика)
        // с ускорением поправки ≤ 400 м/с² (§5 #8).
        var target = new AnchorPose(live.Position + new Vector3(.05f, .02f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .6f),
            live.Velocity + new Vector3(-.6f, 0, .2f), new Vector3(0, 0, 3));
        float deadline = 3f / 30;
        core.Drive(AnchorRigMode.Baked, 7, target, 1f / 60, deadline);
        Assert.That(core.SeamJump, Is.LessThan(1e-5f));
        Assert.That(core.SeamVelocityJump, Is.LessThan(1e-4f));
        Assert.That(Vector3.Distance(core.Output.Position, live.Position), Is.LessThan(1e-5f));
        float t = 0;
        while (t < deadline - 1e-4f)
        {
            target.Position += target.Velocity / 60; t += 1f / 60;
            core.Drive(AnchorRigMode.Baked, 7, target, 1f / 60, deadline - t);
            Assert.That(core.CorrectionAccel, Is.LessThanOrEqualTo(AnchorBlend.MaxAccel + 1f));
        }
        Assert.That(core.BlendError, Is.LessThan(.02f));
        Assert.That(core.DeadlineResidual, Is.Zero);
        AnchorPose before = core.Output;
        core.EnterLive(AnchorRigMode.InHandLive, grip);
        Assert.That(core.Body.Position, Is.EqualTo(before.Position));
        Assert.That(core.Body.Velocity, Is.EqualTo(before.Velocity));
        Assert.That(core.Body.AngularVelocity, Is.EqualTo(before.AngularVelocity));
    }

    [TestCase(.05f, 1)] [TestCase(.1f, 2)] [TestCase(.2f, 3)]
    public void DeadlineRaisesStiffnessSoErrorIsGoneBeforeContact(float error, int ticks)
    {
        var blend = new AnchorBlend();
        var target = AnchorPose.At(Vector3.Zero, Quaternion.Identity);
        blend.Start(AnchorPose.At(new Vector3(error, 0, 0), Quaternion.Identity), target);
        float seconds = ticks / 30f;
        blend.MeetDeadline(seconds);
        float peak = blend.Acceleration;
        for (int i = 0; i < ticks * 4; i++) { blend.Advance(seconds / (ticks * 4)); peak = Math.Max(peak, blend.Acceleration); }
        Assert.That(blend.Magnitude, Is.LessThan(.02f));
        Assert.That(blend.Omega, Is.LessThanOrEqualTo(AnchorBlend.MaxOmega));
        Assert.That(peak, Is.LessThanOrEqualTo(AnchorBlend.MaxAccel + 1f));
    }

    [Test]
    public void BigSeamIsAccelerationLimitedAndTheMissIsReported()
    {
        // Разворот > 60° / быстрое нажатие (критик): метр ошибки и 8 м/с за 3 тика — прежде ω до 260 и ≥ 20 м/с добавки
        // за 2–3 тика («защёлкивание»). Теперь поправка ≤ 400 м/с² в каждом кадре, а недолёт к контакту виден в DeadlineResidual.
        var blend = new AnchorBlend();
        blend.Start(new AnchorPose(new Vector3(.8f, .3f, 0), Quaternion.Identity, new Vector3(0, 0, -8), Vector3.Zero), AnchorPose.At(Vector3.Zero, Quaternion.Identity));
        float seconds = 3f / 30, peak = blend.Acceleration;
        for (int i = 0; i < 6; i++)
        {
            blend.MeetDeadline(seconds - i / 60f);
            blend.Advance(1f / 60);
            peak = Math.Max(peak, blend.Acceleration);
        }
        Assert.That(peak, Is.LessThanOrEqualTo(AnchorBlend.MaxAccel + 1f));
        Assert.That(blend.Magnitude, Is.GreaterThan(.05f), "честный недолёт, а не рывок");
        Assert.That(AnchorBlend.PeakAccel(new Vector3(.8f, .3f, 0), new Vector3(0, 0, -8), AnchorBlend.DefaultOmega), Is.GreaterThan(AnchorBlend.MaxAccel));
    }

    [Test]
    public void DeadlineMissIsReportedWhenTheLimitForbidsIt()
    {
        var blend = new AnchorBlend();
        blend.Start(AnchorPose.At(new Vector3(.6f, 0, 0), Quaternion.Identity), AnchorPose.At(Vector3.Zero, Quaternion.Identity));
        blend.MeetDeadline(2f / 30);
        Assert.That(blend.DeadlineResidual, Is.GreaterThan(.02f));
    }

    [Test]
    public void ThrowReleaseSeamIsTheArmsBlowAndNotLimited()
    {
        // §5 #8 исключает выпуск и рывок броска: голова уходит с линией Sim, а не отстаёт на 0,3 с.
        var core = new AnchorRigCore();
        core.Teleport(AnchorRigMode.Baked, AnchorPose.At(Vector3.Zero, Quaternion.Identity));
        core.Drive(AnchorRigMode.Thrown, -1002, new AnchorPose(new Vector3(0, 0, .5f), Quaternion.Identity, new Vector3(0, 0, 30), Vector3.Zero), 1f / 60);
        Assert.That(core.BlendOmega, Is.EqualTo(AnchorBlend.DefaultOmega).Within(1e-3f));
        core.Drive(AnchorRigMode.Baked, 1, new AnchorPose(new Vector3(0, 0, 1.5f), Quaternion.Identity, new Vector3(0, 0, -10), Vector3.Zero), 1f / 60);
        Assert.That(core.BlendOmega, Is.LessThan(AnchorBlend.DefaultOmega));
    }

    [Test]
    public void ThrowJerkBouncesBackThenReelsIntoPendulum()
    {
        var core = new AnchorRigCore();
        core.Body.EyeLocal = new Vector3(0, .38f, 0);
        var grip = new Vector3(0, 1.1f, 0);
        Vector3 dir = Vector3.UnitZ;
        core.Teleport(AnchorRigMode.Thrown, AnchorPose.At(grip + dir * .5f, Quaternion.Identity));
        float along = .5f, peakBack = 0;
        for (int f = 0; f < 20; f++)
        {
            along = Math.Min(7f, along + 20f / 60);
            var target = new AnchorPose(grip + dir * along, Quaternion.Identity, dir * (along < 7 ? 20 : 0), Vector3.Zero);
            if (f == 18) core.Kick(-dir * core.KickSpeedFor(.15f));
            core.Drive(AnchorRigMode.Thrown, -1002, target, 1f / 60);
            if (f >= 18) peakBack = Math.Max(peakBack, Vector3.Dot(target.Position - core.Output.Position, dir));
        }
        for (int f = 0; f < 20; f++) core.Drive(AnchorRigMode.Thrown, -1002, AnchorPose.At(grip + dir * 7, Quaternion.Identity), 1f / 60);
        Assert.That(peakBack, Is.InRange(.10f, .20f));
        Assert.That(core.BlendError, Is.LessThan(.01f));
        // Рывок: голова по линии к хвату 15 м/с; на 2,5 м — поймал, цепь выбирается рукой до L без скачка.
        float s = 7;
        while (s > 2.5f)
        {
            s -= 15f / 60;
            core.Drive(AnchorRigMode.Yank, -1004, new AnchorPose(grip + dir * s, Quaternion.Identity, -dir * 15, Vector3.Zero), 1f / 60);
        }
        AnchorPose released = core.Output;
        core.EnterLive(AnchorRigMode.Caught, grip);
        Assert.That(core.CableLength, Is.GreaterThan(core.Settings.ChainLength));
        Vector3 last = released.Position;
        float previousLength = core.CableLength;
        for (int f = 0; f < 60; f++)
        {
            core.StepLive(1f / 60, grip, grip, null, Array.Empty<AnchorCapsule>(), 0);
            Assert.That(core.CableLength, Is.LessThanOrEqualTo(previousLength + 1e-6f));
            Assert.That(Vector3.Distance(core.Output.Position, last), Is.LessThan(20f / 60 + .02f));
            previousLength = core.CableLength; last = core.Output.Position;
        }
        Assert.That(core.CableLength, Is.EqualTo(core.Settings.ChainLength).Within(1e-4f));
    }

    [Test]
    public void HardLandingBouncesAndSettlesInsteadOfFreezing()
    {
        var core = new AnchorRigCore();
        core.Body.Hull = BoxHull(.35f, .15f, .12f);
        var grip = new Vector3(0, 1.5f, 0);
        // Цепь в провисе на всём падении (хват в 1,5 м сверху): удар о землю — без участия троса.
        core.Teleport(AnchorRigMode.Baked, new AnchorPose(new Vector3(.3f, .8f, .6f), Quaternion.Identity, new Vector3(0, -18, 2), Vector3.Zero));
        core.EnterLive(AnchorRigMode.InHandLive, grip);
        float maxUp = 0, time = 0, settledAt = -1;
        for (int f = 0; f < 150; f++)
        {
            core.StepLive(1f / 60, grip, grip, p => 0f, Array.Empty<AnchorCapsule>(), 0);
            time += 1f / 60;
            maxUp = Math.Max(maxUp, core.Body.Velocity.Y);
            if (settledAt < 0 && f > 10 && core.Body.Velocity.Length() < .1f) settledAt = time;
        }
        Assert.That(core.Bounces, Is.EqualTo(1));
        Assert.That(maxUp, Is.GreaterThan(1f));
        Assert.That(settledAt, Is.InRange(0f, 2f));
    }

    [Test]
    public void SameInputGivesBitwiseSameHead()
    {
        AnchorPose Run()
        {
            var core = Hanging(new Vector3(0, 1.4f, 0), new Vector3(0, .38f, 0), .5f);
            core.Body.Hull = BoxHull(.3f, .2f, .1f);
            for (int f = 0; f < 90; f++)
                core.StepLive(1f / 60, new Vector3(0, 1.4f, f * .01f), new Vector3(0, 1.4f, (f + 1) * .01f), p => 0f, Array.Empty<AnchorCapsule>(), 0);
            return core.Output;
        }
        AnchorPose a = Run(), b = Run();
        Assert.That(a.Position, Is.EqualTo(b.Position));
        Assert.That(a.Rotation, Is.EqualTo(b.Rotation));
    }

    [Test]
    public void LiveCoreNeverCallsServoForces()
    {
        // §5 #17: в ядре рига нет ни одной пружины к позе (PD прежнего Удара якорем остаётся только там).
        // Тело героя (AnchorRigBody) — тоже живая физика: только выталкивание, без пружин.
        foreach (string file in new[] { "AnchorRigCore.cs", "AnchorRigBody.cs" })
        {
            string source = File.ReadAllText(Path.Combine(SourceDir(), "..", "..", "razlom", "Assets", "Game.View", file));
            foreach (string servo in new[] { "PullEye(", "TurnTowards(", ".Catch(", "LaunchHead(" })
                Assert.That(source, Does.Not.Contain(servo), file + ": " + servo);
        }
    }

    private static string SourceDir([CallerFilePath] string file = "") => Path.GetDirectoryName(file);
}
