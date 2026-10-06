using System;
using System.Collections.Generic;
using System.Numerics;
using AnchorBake;

namespace GripOpt2
{
    /// <summary>Swing2 grip path: left-hand socket per clip frame 0..N (Unity root space: x right, y up, z forward, m).
    /// Frame 0 is the seam (= Swing1 frame 8): grip and the head state are fixed (the head starts from the Swing1 bake).</summary>
    public sealed class Path2
    {
        public Vector3[] G;
        public Path2(int n) { G = new Vector3[n + 1]; }
        public int N => G.Length - 1;

        public double[] ToVector()
        {
            var v = new List<double>();
            for (int f = 1; f <= N; f++) { v.Add(G[f].X); v.Add(G[f].Y); v.Add(G[f].Z); }
            return v.ToArray();
        }

        public static Path2 FromVector(double[] v, Vector3 g0, int n)
        {
            var p = new Path2(n); p.G[0] = g0;
            for (int f = 1; f <= n; f++) p.G[f] = new Vector3((float)v[3 * f - 3], (float)v[3 * f - 2], (float)v[3 * f - 1]);
            return p;
        }
    }

    public sealed class Eval2
    {
        public double Cost;
        public BakeRun Run;
        public Validation V;
        public Dictionary<string, double> Terms = new Dictionary<string, double>();
        public double NearTautClear = 9; public string NearTautWhere = "";   // Wait: straight-drawn chain (span ≥ L − 0,10) vs body
    }

    public sealed class Model2
    {
        public readonly GripTrack Body;
        public readonly HeadModel Head;
        public readonly Rec Start;
        public Vector3 SeamVel;                       // grip step per frame of the previous clip at the seam (continuity)
        public float Chain = 1.6f, Contact = 6f, VMax = 8f, AMax = 150f, ReachL = .56f, ReachR = .55f, ContactY = .72f;
        public float SpeedMin = 21f, SpeedMax = 29f, SpeedGoal = 24f, DirWeight = 40f, DownMin = .15f, DownMax = .65f;
        public float FollowWeight = 1f, SeamWeight = 30f, HandTop = 1.75f, ShoulderUp = .30f, LiftFrame = 2.5f, LiftWeight = 0f;
        public bool Reverse = true;                   // backhand: head crosses the front moving right (+x)

        public Model2(GripTrack body, HeadModel head, Rec start) { Body = body; Head = head; Start = start; }

        public GripTrack TrackOf(Path2 p)
        {
            var t = new GripTrack
            {
                Clip = Body.Clip, Path = Body.Path, Sha256 = "", SourceFbx = Body.SourceFbx, SourceSha = "", Bind = Body.Bind, Hand = "Left",
                Fps = 30, Sub = Body.Sub, Frames = Body.Frames, BoneUnitMetres = Body.BoneUnitMetres, RestHeight = Body.RestHeight,
                BoneNames = Body.BoneNames, Support = Body.Support, GripQ = Body.GripQ, Spine2Q = Body.Spine2Q, Bones = Body.Bones,
            };
            int n = Body.Grip.Length;
            t.Grip = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)Body.Sub;
                int f = Math.Min((int)Math.Floor(x), p.N - 1);
                t.Grip[i] = Vector3.Lerp(p.G[f], p.G[f + 1], x - f);
            }
            return t;
        }

        public BakeConfig Config() => new BakeConfig
        {
            Kind = BakeKind.Swing, ChainLength = Chain, ContactFrame = Contact, Mass = 12f, Gravity = 9.81f, BodyLimbs = true,
            Start = "Swing1@8", StartState = Start,
        };

        public Eval2 Evaluate(Path2 p, bool exact = false)
        {
            var e = new Eval2();
            var track = exact ? Body : TrackOf(p);
            var sim = new BakeSim(track, Head);
            e.Run = sim.Run(Config());
            e.V = Validation.Run(e.Run, track, Head, new ExternalChecks());
            Score(e, p);
            return e;
        }

        static double H(double x) => x > 0 ? x : 0;

        void Score(Eval2 e, Path2 p)
        {
            var v = e.V; var T = e.Terms;
            var at = BakeSim.At(e.Run, Contact / 30f);
            double r = Math.Sqrt(at.P.X * at.P.X + at.P.Z * at.P.Z), ang = Math.Atan2(at.P.X, at.P.Z) * 180 / Math.PI;
            T["contact"] = 400 * v.ContactError + 20 * Math.Pow(r - 2.45, 2) + 150 * Math.Pow(at.P.Y - ContactY, 2) + .02 * ang * ang;
            double s = v.ContactSpeed;
            T["speed"] = 8 * H(SpeedMin - s) + 8 * H(s - SpeedMax) + .05 * (s - SpeedGoal) * (s - SpeedGoal);
            // backhand: across the front to the right, coming down a little (diagonal from over the left shoulder)
            Vector3 u = at.V.Length() > 1e-3f ? Vector3.Normalize(at.V) : Vector3.Zero;
            T["dir"] = Reverse ? DirWeight * (Math.Pow(H(.80 - u.X), 2) + Math.Pow(H(DownMin + u.Y), 2) + Math.Pow(H(-u.Y - DownMax), 2)) : 0;
            double soft = 0;
            foreach (var rr in e.Run.Records)
            {
                double rel = (rr.RingV - rr.GripV).Length(), sl = Chain - rr.Span;
                soft += H(rel - 7) * H(sl - .015);
            }
            T["slack"] = 4 * v.SlackFastFrames + 60 * v.MaxSlackFast + 3 * soft;
            T["pen"] = 1500 * H(v.Penetration - .004) + 1500 * H(-.004 - (v.ChainClearance == double.MaxValue ? 1 : v.ChainClearance));
            T["jump"] = 300 * H(v.MaxJump - .12) + 2000 * H(v.MaxStretch - .004);
            double kin = 0, reach = 0, jerk = 0;
            for (int f = 0; f < p.N; f++)
            {
                double vf = (p.G[f + 1] - p.G[f]).Length() * 30;
                kin += 2 * Math.Pow(H(vf - VMax), 2);
                Vector3 prev = f == 0 ? p.G[0] - SeamVel : p.G[f - 1];
                double af = (p.G[f + 1] - 2 * p.G[f] + prev).Length() * 900;
                kin += .002 * Math.Pow(H(af - AMax), 2);
                Vector3 prev2 = f <= 1 ? (f == 0 ? p.G[0] - 2 * SeamVel : p.G[0] - SeamVel) : p.G[f - 2];
                if (f >= 1) jerk += (p.G[f + 1] - 3 * p.G[f] + 3 * prev - prev2).LengthSquared();
            }
            T["seam"] = SeamWeight * (p.G[1] - p.G[0] - SeamVel).LengthSquared() * 100;
            int la = Bone("LeftArm"), ra = Bone("RightArm"), sp2 = Bone("Spine2"), hips = Bone("Hips"), head = Bone("Head");
            for (int f = 1; f <= p.N; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                var rec = BakeSim.At(e.Run, f / 30f);
                Vector3 g = p.G[f], dir = Vector3.Normalize(rec.Ring - rec.Grip);
                reach += 2000 * Math.Pow(H((g - b[la]).Length() - ReachL), 2);
                reach += 2000 * Math.Pow(H((g + dir * .15f - b[ra]).Length() - ReachR), 2);
                Vector3 c = b[sp2];
                reach += 400 * Math.Pow(H(.24 - new Vector2(g.X - c.X, g.Z - c.Z).Length()), 2);      // fists off the chest
                reach += 400 * Math.Pow(H(.62 - g.Y), 2) + 400 * Math.Pow(H(g.Y - HandTop), 2);
                // fists never in front of the face: over the shoulder they pass beside the head, not through it
                Vector3 hd = b[head];
                reach += 800 * Math.Pow(H(.22 - (g - hd).Length()), 2);
            }
            if (LiftWeight > 0)
            {   // pose 3 -> over the left shoulder: the fists rise to the left shoulder before the backhand
                Vector3 g = p.G[(int)Math.Round(LiftFrame)], sh = Body.Bones[(int)Math.Round(LiftFrame) * Body.Sub][la];
                reach += LiftWeight * Math.Pow(H(sh.Y + ShoulderUp - .25 - g.Y), 2);
            }
            T["kin"] = kin; T["reach"] = reach; T["jerk"] = 20 * jerk;
            // after contact the head goes on to the right, back and up (start of the vertical arc of Slam)
            var end = BakeSim.At(e.Run, p.N / 30f);
            double aEnd = Math.Atan2(end.P.X, end.P.Z) * 180 / Math.PI;
            T["follow"] = FollowWeight * (.002 * Math.Pow(H(70 - aEnd), 2) + 5 * Math.Pow(H(1.1 - end.P.Y), 2));
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
        }

        int Bone(string n) => Array.IndexOf(Body.BoneNames, n);
    }
}
