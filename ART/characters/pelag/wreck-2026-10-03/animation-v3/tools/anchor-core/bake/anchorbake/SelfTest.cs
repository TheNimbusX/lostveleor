using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Game.View;

namespace AnchorBake
{
    /// <summary>anchorbake --selftest: the tool checks its own physics before anyone trusts a bake (DESIGN 3.5).
    /// The bake steps Game.View.AnchorRigCore (the game's own code); checks 5-9 cover what the rig adds on top of the head:
    /// hull vs limb, bounce instead of a freeze, loop closure, the grip_socket.json and bake-file contracts the game reads.
    /// 1) determinism: same input twice -> bit-identical path; 2) energy of a free pendulum on the taut chain
    /// (fixed grip, no air, no ground) never gains energy (> +1 %) and loses &lt; 10 % in 5 s (the cable projection is inelastic); 3) conical pendulum (grip on a 0.3 m circle, 1.5 Hz):
    /// chain stretch &lt; 1 mm and the head stays taut; 4) small-swing period against the rigid double pendulum
    /// eigen-period (chain as a massless rod at the ring) within 3 %.</summary>
    public static class SelfTest
    {
        public static int Run(HeadModel head, GripTrack track, string socketPath)
        {
            int fails = 0;
            void Check(string name, bool ok, string detail) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name}: {detail}"); if (!ok) fails++; }
            Console.WriteLine("== selftest");
            const float L = 1.6f, dt = 1f / 240f, g = 9.81f;

            // 1. determinism
            var sim = new BakeSim(track, head);
            var c = new BakeConfig { Start = "taut:-120" };
            var r1 = sim.Run(c); var r2 = sim.Run(new BakeConfig { Start = "taut:-120" });
            bool same = r1.Records.Count == r2.Records.Count;
            for (int i = 0; same && i < r1.Records.Count; i++)
                same = r1.Records[i].P == r2.Records[i].P && r1.Records[i].Q == r2.Records[i].Q;
            Check("детерминизм", same, r1.Records.Count + " сэмплов, побитово " + (same ? "равны" : "РАЗНЫЕ"));

            // 2. energy of a free swing (head hanging straight below the ring, 25 deg off vertical)
            var h = head.NewBody(12f, new Vector3(.75f, .55f, .75f));
            Vector3 grip = new Vector3(0, 5, 0);
            float a0 = 25f * (float)Math.PI / 180f;
            Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, a0);
            Vector3 down = Vector3.Transform(-Vector3.UnitY, q);
            Vector3 ring = grip + down * L;
            h.Reset(ring - Vector3.Transform(head.EyeLocal, q), q);
            double E(AnchorHeadDynamics b)
            {
                var w = Vector3.Transform(b.AngularVelocity, Quaternion.Conjugate(b.Rotation));
                return .5 * b.Mass * b.Velocity.LengthSquared() + .5 * Vector3.Dot(w * b.Inertia, w) + b.Mass * g * b.Position.Y;
            }
            double e0 = E(h), swing = e0 - 12 * g * (grip.Y - L - head.EyeLocal.Length()), gain = 0, loss = 0;
            float prevX = h.Position.X; int crossings = 0; float firstCross = -1, lastCross = -1;
            for (int i = 1; i <= (int)(5f / dt); i++)
            {
                h.Advance(dt, new Vector3(0, -g, 0));
                h.ConstrainCable(grip, Vector3.Zero, L, 0, dt);
                gain = Math.Max(gain, (E(h) - e0) / swing); loss = Math.Max(loss, (e0 - E(h)) / swing);
                if (prevX > 0 && h.Position.X <= 0 || prevX < 0 && h.Position.X >= 0)
                {
                    float t = i * dt; crossings++;
                    if (firstCross < 0) firstCross = t; lastCross = t;
                }
                prevX = h.Position.X;
            }
            Check("энергия свободного маятника", gain < .01 && loss < .10, "рост " + (gain * 100).ToString("0.00") + " %, потеря " + (loss * 100).ToString("0.0") + " % за 5 с (числовое гашение ConstrainCable)");

            // 4. period vs linearised double pendulum: rod L (massless) to the ring, rigid head (m, I_z) hanging on it
            double m = 12, d = head.EyeLocal.Length(), Iz = .75;   // inertia about the swing axis (local z here)
            double M11 = m * L * L, M12 = m * L * d, M22 = Iz + m * d * d, K1 = m * g * L, K2 = m * g * d;
            // det(K - w^2 M) = 0
            double A = M11 * M22 - M12 * M12, B = -(K1 * M22 + K2 * M11), C = K1 * K2;
            double w2 = (-B - Math.Sqrt(B * B - 4 * A * C)) / (2 * A);
            double expected = 2 * Math.PI / Math.Sqrt(w2), measured = crossings > 2 ? 2 * (lastCross - firstCross) / (crossings - 1) : 0;
            Check("период качания (двойной маятник)", Math.Abs(measured / expected - 1) < .03,
                $"{measured:0.000} с против {expected:0.000} с (25°, мода 1)");

            // 3. conical pendulum: grip on a circle r 0.3 m at 1.5 Hz, 4 s
            h = head.NewBody(12f, new Vector3(.75f, .55f, .75f));
            Vector3 centre = new Vector3(0, 6, 0);
            h.Reset(centre + new Vector3(.3f, -L - .38f, 0), Quaternion.Identity);
            float stretch = 0, slackLate = 0; Vector3 prev = centre + new Vector3(.3f, 0, 0);
            for (int i = 1; i <= (int)(4f / dt); i++)
            {
                float t = i * dt, ph = 2 * (float)Math.PI * 1.5f * t;
                Vector3 gp = centre + new Vector3(.3f * (float)Math.Cos(ph), 0, .3f * (float)Math.Sin(ph));
                h.Advance(dt, new Vector3(0, -g, 0));
                h.ConstrainCable(gp, (gp - prev) / dt, L, 0, dt);
                prev = gp;
                float span = Vector3.Distance(h.Eye, gp);
                stretch = Math.Max(stretch, span - L);
                if (t > 2f) slackLate = Math.Max(slackLate, L - span);
            }
            Check("конический маятник: растяжение", stretch < .001f, (stretch * 1000).ToString("0.00") + " мм");
            Check("конический маятник: натяг после разгона", slackLate < .02f, "провис " + (slackLate * 100).ToString("0.0") + " см");
            fails += RigChecks(head, socketPath);
            Console.WriteLine(fails == 0 ? "SELFTEST PASS" : "SELFTEST FAIL " + fails);
            return fails;
        }

        static int RigChecks(HeadModel head, string socketPath)
        {
            int fails = 0;
            void C(string n, bool ok, string d) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {n}: {d}"); if (!ok) fails++; }
            // 5. hull vs a limb capsule (thigh r 0.10 across the fall line): the head comes to rest on it, not through it
            var core = Core(head);
            core.Teleport(AnchorRigMode.InHandLive, AnchorPose.At(new Vector3(0, 1.4f, 0), Quaternion.CreateFromYawPitchRoll(.3f, .2f, .1f)));
            var thigh = new[] { new AnchorCapsule(new Vector3(-.6f, .6f, 0), new Vector3(.6f, .6f, 0), .10f, AnchorCapsuleKind.Hull) };
            for (int i = 0; i < 120; i++) core.StepLive(1f / 120f, new Vector3(0, 3, 0), new Vector3(0, 3, 0), _ => -5f, thigh, 1);
            float depth = 0;
            foreach (var local in core.Body.Hull)
            {
                Vector3 p = core.Body.Position + Vector3.Transform(local, core.Body.Rotation);
                depth = Math.Max(depth, thigh[0].Radius - thigh[0].AxisDistance(p, out _));
            }
            C("оболочка против бедра (AnchorRigBody.PushHull)", depth <= .01f, "проникновение " + (depth * 100).ToString("0.0") + " см после 1 с");
            // 6. bounce: a fast drop onto soft ground kicks the head up, no 3-frame freeze
            core = Core(head);
            core.Teleport(AnchorRigMode.InHandLive, new AnchorPose(new Vector3(0, .9f, 0), Quaternion.Identity, new Vector3(0, -12f, 1f), Vector3.Zero));
            float up = 0; int touch = -1;
            for (int i = 0; i < 60; i++)
            {
                core.StepLive(1f / 120f, new Vector3(0, 2.2f, 0), new Vector3(0, 2.2f, 0), _ => 0f, Array.Empty<AnchorCapsule>(), 0);
                if ((core.Body.Grounded || core.Bounces > 0) && touch < 0) touch = i;
                if (touch >= 0 && i <= touch + 6) up = Math.Max(up, core.Body.Velocity.Y);
            }
            C("отскок вместо замирания", core.Bounces >= 1 && up > .5f, "отскоков " + core.Bounces + ", подскок " + up.ToString("0.00") + " м/с за 3 кадра");
            // 7. loop: a synthetic "helicopter" (grip circle r 0.2 m at 2.1 m, 0.40 s) closes on itself after run-in
            var loopTrack = Synthetic(12, f => new Vector3(.2f * (float)Math.Cos(2 * Math.PI * f / 12), 2.1f, .2f * (float)Math.Sin(2 * Math.PI * f / 12)));
            var sim = new BakeSim(loopTrack, head);
            var cfg = new BakeConfig { Kind = BakeKind.Loop, LoopFrom = 0, LoopTo = 12, Cycles = 30, ContactFrame = 0, BodyLimbs = false, Ground = -5f };
            var run = sim.Run(cfg);
            var v = Validation.Run(run, loopTrack, head, new ExternalChecks());
            C("цикл замыкается", v.LoopDp <= .005 && v.LoopDv <= .3, "Δp " + (v.LoopDp * 1000).ToString("0.0") + " мм, Δv " + v.LoopDv.ToString("0.00") + " м/с, 30 оборотов");
            // 8. grip_socket.json is the file the rig reads (metres, version 2)
            bool socketOk = false; string socketInfo = "нет файла " + socketPath;
            if (File.Exists(socketPath))
            {
                var data = JsonSerializer.Deserialize<AnchorGripSocketData>(File.ReadAllText(socketPath), new JsonSerializerOptions { IncludeFields = true });
                socketOk = data != null && data.Valid && data.bone == "mixamorig:LeftHand";
                socketInfo = socketOk ? "v" + data.version + ", " + data.bone + " (" + string.Join("; ", data.position) + ") м" : "не читается как AnchorGripSocketData";
            }
            C("grip_socket.json = контракт рига", socketOk, socketInfo);
            // 9. the bake file round-trips through the game's reader (t = frame, loop {from,to})
            string tmp = Path.Combine(Path.GetTempPath(), "anchorbake-selftest.anchorbake.json");
            Output.WriteBake(tmp, "selftest_loop", run, loopTrack, head, v, 0, "rest", "live");
            var parsed = JsonSerializer.Deserialize<AnchorBakeData>(File.ReadAllText(tmp), new JsonSerializerOptions { IncludeFields = true });
            var bake = new Game.View.AnchorBake(parsed);
            File.Delete(tmp);
            var s0 = bake.Valid ? bake.Sample(0, out _, out _) : default;
            C("файл запечки читает AnchorBake игры", bake.Valid && bake.Looped && (s0.Position - run.Records[0].P).Length() < 1e-4f,
                bake.Valid ? "цикл " + bake.LoopFrom + "…" + bake.LoopTo + ", кадр 0 совпал" : "отказ: " + bake.Error);
            return fails;
        }

        static AnchorRigCore Core(HeadModel head)
        {
            var core = new AnchorRigCore();
            core.Body.Hull = head.HullLocal; core.Body.EyeLocal = head.EyeLocal;
            return core;
        }

        static GripTrack Synthetic(int frames, Func<float, Vector3> grip)
        {
            const int sub = 8;
            int n = frames * sub + 1;
            var t = new GripTrack
            {
                Clip = "selftest_loop", Path = "selftest", Sha256 = "", SourceFbx = "", SourceSha = "", Bind = "", Hand = "Left",
                Fps = 30, Sub = sub, Frames = frames, BoneUnitMetres = 1.82, BoneNames = Array.Empty<string>(),
                Grip = new Vector3[n], Support = new Vector3[n], GripQ = new Quaternion[n], Spine2Q = new Quaternion[n], Bones = new Vector3[n][],
            };
            for (int i = 0; i < n; i++)
            {
                t.Grip[i] = grip(i / (float)sub); t.Support[i] = t.Grip[i]; t.GripQ[i] = t.Spine2Q[i] = Quaternion.Identity;
                t.Bones[i] = Array.Empty<Vector3>();
            }
            return t;
        }
    }
}
