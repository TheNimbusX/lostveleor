using System;
using System.Collections.Generic;
using System.Numerics;
using AnchorBake;
using Game.View;

namespace GripOpt5
{
    /// <summary>ChargeLoop grip path: left-hand socket at clip frames 0..11 (frame 12 = frame 0), Unity root axes (x right, y up, z fwd, m).</summary>
    public sealed class LoopPath
    {
        public const int N = 12;
        public Vector3[] G = new Vector3[N];
        public Vector3 At(int f) => G[((f % N) + N) % N];
        public double[] ToVector() { var v = new double[3 * N]; for (int f = 0; f < N; f++) { v[3 * f] = G[f].X; v[3 * f + 1] = G[f].Y; v[3 * f + 2] = G[f].Z; } return v; }
        public static LoopPath FromVector(double[] v)
        { var p = new LoopPath(); for (int f = 0; f < N; f++) p.G[f] = new Vector3((float)v[3 * f], (float)v[3 * f + 1], (float)v[3 * f + 2]); return p; }
    }

    public sealed class LoopEval
    {
        public double Cost; public BakeRun Run; public Validation V; public Dictionary<string, double> Terms = new Dictionary<string, double>();
        public float MeanSpeed, MinSpeed, MaxSpeed, EntryDp, EntryDv, SeamAt0, SeamAccel, SeamSettle, MinHeadAboveCrown;
    }

    /// <summary>
    /// Steady «helicopter» orbit from a periodic grip path: BakeSim (kind loop, the game's AnchorRigCore.StepLive) runs Cycles turns from the
    /// real entry state (Slam_w13@7 — the tick the Sim starts the charge), the last turn is the loop bake. Costs: loop closure, head speed band,
    /// chain/body checks of the bake tool, reach of both fists from the shoulders of the stand-in body, fists clear of the hero's head,
    /// smooth hands, and the in-game seam Slam@7 → loop@0 (AnchorBlend, ≤ 400 m/s²).
    /// </summary>
    public sealed class LoopModel
    {
        public readonly GripTrack Body; public readonly HeadModel Head; public readonly Rec Entry;
        public Vector3[] SubOff;
        public Vector3 Axis = Vector3.Normalize(new Vector3(.6f, 0f, -.8f));  // handle: left fist → right fist (fixed yaw: hands translate, chain swivels)
        public float Gap = .15f, ReachL = .585f, ReachR = .60f, ReachW = 20000f, HeadClear = .17f, VMax = 5f, JerkW = 40f;
        public float VLo = 24f, VHi = 27.5f, ClrMin = .01f, EntryW = 20f, SeamW = 1f, Rate = 1f;
        public int Cycles = 30; public float AboveMin = .10f;
        /// <summary>Старт прогона на круге (голова в 1,98 м от хвата кадра 0 по азимуту StartAz, касательная скорость ω·R), а не из Slam@7:
        /// малый круг кулаков не ловит голову из чужого состояния, а в игре вход и так — смешивание рига (InGameSeam).</summary>
        public bool StartOrbit = true; public float StartAz = 165f, StartR = 1.98f;

        public Rec OrbitStart(LoopPath p)
        {
            float az = StartAz * (float)Math.PI / 180f, w = 2f * (float)Math.PI / (LoopPath.N / (30f * Rate));
            var dir = new Vector3((float)Math.Sin(az), 0, (float)Math.Cos(az));
            var pos = p.At(0) + dir * StartR + new Vector3(0, -.03f, 0);
            var vel = new Vector3(-(float)Math.Cos(az), 0, (float)Math.Sin(az)) * (w * StartR);
            // локальная +Y головы (к ушку) — на хват: поворот (0,1,0) → −dir
            var to = -dir; var axis = Vector3.Cross(Vector3.UnitY, to); float ang = (float)Math.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, to), -1, 1));
            var q = axis.LengthSquared() < 1e-8f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), ang);
            return new Rec { P = pos, V = vel, Q = q, W = new Vector3(0, -w, 0) };
        }

        public LoopModel(GripTrack body, HeadModel head, Rec entry) { Body = body; Head = head; Entry = entry; }

        public GripTrack TrackOf(LoopPath p)
        {
            var t = new GripTrack
            {
                Clip = Body.Clip, Path = Body.Path, Sha256 = "", SourceFbx = Body.SourceFbx, SourceSha = "", Bind = Body.Bind, Hand = "Left",
                Fps = 30, Sub = Body.Sub, Frames = Body.Frames, BoneUnitMetres = Body.BoneUnitMetres, RestHeight = Body.RestHeight,
                BoneNames = Body.BoneNames, Support = Body.Support, GripQ = Body.GripQ, Spine2Q = Body.Spine2Q, Bones = Body.Bones,
            };
            int n = Body.Grip.Length; t.Grip = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)Body.Sub; int f = Math.Min((int)Math.Floor(x), LoopPath.N - 1);
                t.Grip[i] = Vector3.Lerp(p.At(f), p.At(f + 1), x - f) + (SubOff != null && i < SubOff.Length ? SubOff[i] : Vector3.Zero);
            }
            return t;
        }

        public BakeConfig Config(LoopPath p) => new BakeConfig
        {
            Kind = BakeKind.Loop, ChainLength = 1.6f, Mass = 12f, Gravity = 9.81f, BodyLimbs = true, LoopFrom = 0, LoopTo = LoopPath.N,
            ContactFrame = 0, Cycles = Cycles, ClipRate = Rate, Start = StartOrbit ? "orbit@" + StartAz.ToString("0") : "Slam_w13@7",
            StartState = StartOrbit ? OrbitStart(p) : Entry,
        };

        public LoopEval Evaluate(LoopPath p, GripTrack exact = null)
        {
            var e = new LoopEval();
            var track = exact ?? TrackOf(p);
            e.Run = new BakeSim(track, Head).Run(Config(p));
            e.V = Validation.Run(e.Run, track, Head, new ExternalChecks());
            Measure(e, track);
            InGameSeam(e);
            Score(e, p, track);
            return e;
        }

        void Measure(LoopEval e, GripTrack track)
        {
            double sum = 0; e.MinSpeed = 1e9f; e.MaxSpeed = 0; e.MinHeadAboveCrown = 9;
            var skel = new AnchorBake.Body(track);
            foreach (var r in e.Run.Records)
            {
                float s = r.V.Length(); sum += s; e.MinSpeed = Math.Min(e.MinSpeed, s); e.MaxSpeed = Math.Max(e.MaxSpeed, s);
                e.MinHeadAboveCrown = Math.Min(e.MinHeadAboveCrown, r.P.Y - skel.HeadTopAt(r.Frame).Y);
            }
            e.MeanSpeed = (float)(sum / e.Run.Records.Count);
            var r0 = e.Run.Records[0];
            e.EntryDp = Vector3.Distance(r0.P, Entry.P); e.EntryDv = Vector3.Distance(r0.V, Entry.V);
        }

        /// <summary>The game at ChargeStartTick: shown head = Slam bake at the overhead frame, new segment (Loop) → AnchorBlend onto loop@0…, no deadline.</summary>
        public void InGameSeam(LoopEval e, List<string> log = null)
        {
            var core = new AnchorRigCore(new AnchorRigSettings());
            core.Body.Hull = Head.HullLocal; core.Body.EyeLocal = Head.EyeLocal; core.Body.Mass = 12f;
            core.Teleport(AnchorRigMode.Baked, new AnchorPose(Entry.P, Entry.Q, Entry.V, Entry.W));
            float acc = 0; e.SeamSettle = 99;
            for (int k = 0; k <= 2 * 12; k++)
            {
                float f = k / 2f; var r = BakeSim.At(e.Run, (f % LoopPath.N) / (30f * Rate));
                core.Drive(AnchorRigMode.Baked, 9, new AnchorPose(r.P, r.Q, r.V * 1f, r.W), 1f / 60f, -1f);
                if (k == 0) e.SeamAt0 = core.BlendError;
                acc = Math.Max(acc, core.CorrectionAccel);
                if (core.BlendError < .02f && e.SeamSettle > 90) e.SeamSettle = f;
                if (log != null) { var o = core.Output; log.Add($"  seam f{f,4:0.0} bake ({r.P.X:F2} {r.P.Y:F2} {r.P.Z:F2}) shown ({o.Position.X:F2} {o.Position.Y:F2} {o.Position.Z:F2}) |v| {o.Velocity.Length():F1} offset {core.BlendError:F3} accel {core.CorrectionAccel:F0}"); }
            }
            e.SeamAccel = acc;
        }

        static double H(double x) => x > 0 ? x : 0;
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-6f, ab.LengthSquared()), 0, 1);
            return Vector3.Distance(p, a + ab * t);
        }
        int B(string n) => Array.IndexOf(Body.BoneNames, n);

        void Score(LoopEval e, LoopPath p, GripTrack track)
        {
            var v = e.V; var T = e.Terms;
            T["close"] = 3000 * H(v.LoopDp - .004) + 300 * H(v.LoopDv - .25) + 50 * v.LoopDp;
            T["speed"] = 20 * (Math.Pow(H(VLo * Rate - e.MeanSpeed), 2) + Math.Pow(H(e.MeanSpeed - VHi * Rate), 2)) + 10 * Math.Pow(H(e.MaxSpeed - 29.5 * Rate), 2);
            T["pen"] = 1500 * H(v.Penetration - .004) + 1500 * H(ClrMin - (v.ChainClearance == double.MaxValue ? 1 : v.ChainClearance));
            T["slack"] = 4 * v.SlackFastFrames + 60 * v.MaxSlackFast;
            T["jump"] = 3000 * H(v.MaxJump - .13) + 2000 * H(v.MaxStretch - .004);
            T["entry"] = EntryW * (e.EntryDp * e.EntryDp + .004 * e.EntryDv * e.EntryDv);
            T["seam"] = SeamW * (.3 * H(e.SeamAccel - 400) + 2 * e.SeamAt0);
            T["flat"] = 300 * Math.Pow(H(AboveMin - e.MinHeadAboveCrown), 2);   // «вертолёт»: голова якоря над макушкой на всём круге
            int la = B("LeftArm"), ra = B("RightArm"), hd = B("Head"), ht = B("HeadTop_End");
            double reach = 0, kin = 0;
            for (int f = 0; f < LoopPath.N; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                Vector3 g = p.At(f), rf = g + Axis * Gap;
                reach += ReachW * (Math.Pow(H((g - b[la]).Length() - ReachL), 2) + Math.Pow(H((rf - b[ra]).Length() - ReachR), 2));
                foreach (var q in new[] { g, rf })    // кулаки (предплечья идут к локтям наружу — их проверяет сборка тела)
                    reach += 3000 * Math.Pow(H(HeadClear - SegDist(q, b[hd], b[ht])), 2);
                double vf = (p.At(f + 1) - g).Length() * 30;
                kin += 2 * Math.Pow(H(vf - VMax), 2) + JerkW * (p.At(f + 1) - 2 * g + p.At(f - 1)).LengthSquared();
            }
            T["reach"] = reach; T["kin"] = kin;
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
        }
    }
}
