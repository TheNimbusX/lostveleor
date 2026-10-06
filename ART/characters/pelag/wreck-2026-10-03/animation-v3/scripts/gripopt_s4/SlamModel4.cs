using System;
using System.Collections.Generic;
using System.Numerics;
using AnchorBake;
using Game.View;

namespace GripOpt4
{
    /// <summary>Slam grip path: left-hand socket per clip frame 0..N (Unity root: x right, y up, z forward, m); frame 0 = the seam grip.
    /// Start of the head: the previous stage's state (honest) or that state + (Dp, Dv) (designed: the game's AnchorBlend
    /// carries the shown head from the previous stage onto this bake before contact − 1, ≤ 400 m/s², like the Swing1 draw).</summary>
    public sealed class Path3s
    {
        public Vector3[] G; public Vector3 Dp, Dv;
        public Path3s(int n) { G = new Vector3[n + 1]; }
        public int N => G.Length - 1;
        public static bool Designed;

        public double[] ToVector()
        {
            var v = new List<double>();
            for (int f = 1; f <= N; f++) { v.Add(G[f].X); v.Add(G[f].Y); v.Add(G[f].Z); }
            if (Designed) { v.Add(Dp.X / 5); v.Add(Dp.Y / 5); v.Add(Dp.Z / 5); v.Add(Dv.X / 50); v.Add(Dv.Y / 50); v.Add(Dv.Z / 50); }
            return v.ToArray();
        }

        public static Path3s FromVector(double[] v, Vector3 g0, int n)
        {
            var p = new Path3s(n); p.G[0] = g0;
            for (int f = 1; f <= n; f++) p.G[f] = new Vector3((float)v[3 * f - 3], (float)v[3 * f - 2], (float)v[3 * f - 1]);
            if (Designed)
            {
                int k = 3 * n;
                p.Dp = new Vector3((float)v[k] * 5, (float)v[k + 1] * 5, (float)v[k + 2] * 5);
                p.Dv = new Vector3((float)v[k + 3] * 50, (float)v[k + 4] * 50, (float)v[k + 5] * 50);
            }
            return p;
        }
    }

    public sealed class Eval3s
    {
        public double Cost; public BakeRun Run; public Validation V; public Rec Start;
        public Dictionary<string, double> Terms = new Dictionary<string, double>();
        public float SeamResidual, SeamAccel, SeamMean, SeamAt0; public int EarlyGround; public float Above, Down, Side;
    }

    public sealed class SlamModel4
    {
        public readonly GripTrack Body; public readonly HeadModel Head; public readonly Rec Prev;
        public Vector3 SeamVel;
        public float ReachW = 2000f; public Vector3[] SubOff;   // сдвиг хвата между кадрами: выгрузка (дуга поворотов) минус линейная интерполяция
        public float Chain = 1.6f, Contact = 9f, Overhead = 5f, VMax = 8f, AMax = 150f, ReachL = .56f, ReachR = .55f, HandTop = 2.05f;
        public Vector3 Impact = new Vector3(0, 0, 2.2f);
        public Dictionary<int, Vector3> HandKeys; public float ClrMin = -.004f, ContactW = 400f, SmoothW = 0f, LandY = .27f; public float KeyW = 200f, ThighClear = .15f, JumpW = 3000f, OverUp = .12f, ForwardZ = .50f, PoseW = 1f, JerkW = 20f, HeadClear = .22f, ExitVMax = 4f, SpeedGoal = 22f, EndRadius = 1.3f, SeamWeight = 30f, AboveMin = .30f, Vert = 0f, VertW = 60f;

        public SlamModel4(GripTrack body, HeadModel head, Rec prev) { Body = body; Head = head; Prev = prev; }

        public GripTrack TrackOf(Path3s p)
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
                float x = i / (float)Body.Sub; int f = Math.Min((int)Math.Floor(x), p.N - 1);
                t.Grip[i] = Vector3.Lerp(p.G[f], p.G[f + 1], x - f) + (SubOff != null && i < SubOff.Length ? SubOff[i] : Vector3.Zero);
            }
            return t;
        }

        public Rec StartOf(Path3s p) => new Rec { P = Prev.P + p.Dp, Q = Prev.Q, V = Prev.V + p.Dv, W = Prev.W };

        public BakeConfig Config(Rec s) => new BakeConfig
        {
            Kind = BakeKind.Slam, ChainLength = Chain, ContactFrame = Contact, OverheadFrame = Overhead, Impact = Impact, Mass = 12f,
            Gravity = 9.81f, BodyLimbs = true, Start = Path3s.Designed ? "designed" : "Swing2@7", StartState = s,
        };

        public Eval3s Evaluate(Path3s p, bool exact = false)
        {
            var e = new Eval3s { Start = StartOf(p) };
            var track = exact ? Body : TrackOf(p);
            e.Run = new BakeSim(track, Head).Run(Config(e.Start));
            e.V = Validation.Run(e.Run, track, Head, new ExternalChecks());
            InGameSeam(e);
            Score(e, p, track);
            return e;
        }

        /// <summary>The game at the stage start: shown head = previous stage's state (Baked), new segment → AnchorBlend onto this
        /// bake, deadline contact − 1 (PelagAnchorRig.Wreck: Drive(Baked, Segment, target, dt, deadline)), 60 fps.</summary>
        public void InGameSeam(Eval3s e, List<string> log = null)
        {
            var core = new AnchorRigCore(new AnchorRigSettings());
            core.Body.Hull = Head.HullLocal; core.Body.EyeLocal = Head.EyeLocal; core.Body.Mass = 12f;
            core.Teleport(AnchorRigMode.Baked, new AnchorPose(Prev.P, Prev.Q, Prev.V, Prev.W));
            float acc = 0, sum = 0; int n = 0;
            for (int k = 0; k <= 2 * (int)Contact; k++)
            {
                float f = k / 2f; var r = BakeSim.At(e.Run, f / 30f);
                float dl = f < Contact - 1 ? (Contact - 1 - f) / 30f : -1f;
                core.Drive(AnchorRigMode.Baked, 2, new AnchorPose(r.P, r.Q, r.V, r.W), 1f / 60f, dl);
                if (k == 0) e.SeamAt0 = core.BlendError;
                acc = Math.Max(acc, core.CorrectionAccel); sum += core.BlendError; n++;
                if (Math.Abs(f - (Contact - 1)) < 1e-3f) e.SeamResidual = core.BlendError;
                if (log != null) { var o = core.Output; log.Add($"  seam f{f,4:0.0} bake ({r.P.X:F2} {r.P.Y:F2} {r.P.Z:F2}) shown ({o.Position.X:F2} {o.Position.Y:F2} {o.Position.Z:F2}) |v| {o.Velocity.Length():F1} offset {core.BlendError:F3} accel {core.CorrectionAccel:F0}"); }
            }
            e.SeamAccel = acc; e.SeamMean = sum / Math.Max(1, n);
        }

        static double H(double x) => x > 0 ? x : 0;
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-6f, ab.LengthSquared()), 0, 1);
            return Vector3.Distance(p, a + ab * t);
        }
        int B(string n) => Array.IndexOf(Body.BoneNames, n);

        void Score(Eval3s e, Path3s p, GripTrack track)
        {
            var v = e.V; var T = e.Terms;
            T["contact"] = ContactW * v.ContactError;
            {   // гладкие слагаемые (ступенька тика посадки в ContactError — CMA на ней стоит): высота головы и XZ в тик контакта,
                // голова ещё в воздухе за полтика до него, скорость в касании ≤ 27,5
                var c0 = BakeSim.At(e.Run, Contact / 30f); var cm = BakeSim.At(e.Run, Math.Max(0, Contact - .5f) / 30f);
                double miss = new Vector2(c0.P.X - Impact.X, c0.P.Z - Impact.Z).Length();
                T["land"] = SmoothW * (Math.Pow(c0.P.Y - LandY, 2) * 40 + miss * miss * 20 + Math.Pow(H(LandY + .10 - cm.P.Y), 2) * 40)
                          + 100 * Math.Pow(H(v.ContactSpeed - 27.4), 2);
            }
            double s = v.ContactSpeed;
            T["speed"] = 8 * H(17 - s) + 8 * H(s - 27) + .05 * (s - SpeedGoal) * (s - SpeedGoal);
            var skel = new AnchorBake.Body(track);
            var o = BakeSim.At(e.Run, Overhead / 30f);
            e.Above = o.P.Y - skel.HeadTopAt(Overhead).Y;
            T["overhead"] = 300 * Math.Pow(H(AboveMin - e.Above), 2);
            float lt = (float)(e.V.FirstGroundTick < 0 ? Contact : e.V.FirstGroundTick);
            var vin = BakeSim.At(e.Run, Math.Max(0, lt / 30f - 1f / 120f)).V;
            double down = vin.Length() > 1e-3f ? -vin.Y / vin.Length() : 0, side = vin.Length() > 1e-3f ? Math.Abs(vin.X) / vin.Length() : 0;
            e.Down = (float)down; e.Side = (float)side;
            T["vert"] = Vert > 0 ? VertW * (Math.Pow(H(Vert - down), 2) + Math.Pow(H(side - .55), 2)) : 0;
            double dt60 = 0, early = 0, after = 0;
            foreach (var r in e.Run.Records)
            {
                if (r.Grounded && r.Tick < Contact - .6f) early += 1;
                if (r.Tick > Contact + 1 && r.Tick < p.N) after += H(r.P.Y - .75);
                double rel = (r.RingV - r.GripV).Length(), sl = Chain - r.Span;
                dt60 += H(rel - 7) * H(sl - .015);
            }
            e.EarlyGround = (int)early;
            T["ground"] = 3 * early + 30 * after / 4;
            T["slack"] = 4 * v.SlackFastFrames + 60 * v.MaxSlackFast + 3 * dt60;
            T["pen"] = 1500 * H(v.Penetration - .004) + 1500 * H(ClrMin - (v.ChainClearance == double.MaxValue ? 1 : v.ChainClearance));
            T["jump"] = JumpW * H(v.MaxJump - .13) + 2000 * H(v.MaxStretch - .004) + 20 * H(v.MaxSpeed - 28.5);
            double kin = 0, reach = 0, jerk = 0;
            for (int f = 0; f < p.N; f++)
            {
                double vf = (p.G[f + 1] - p.G[f]).Length() * 30;
                kin += 2 * Math.Pow(H(vf - VMax), 2);
                Vector3 prev = f == 0 ? p.G[0] - SeamVel : p.G[f - 1];
                kin += .002 * Math.Pow(H((p.G[f + 1] - 2 * p.G[f] + prev).Length() * 900 - AMax), 2);
                Vector3 prev2 = f <= 1 ? (f == 0 ? p.G[0] - 2 * SeamVel : p.G[0] - SeamVel) : p.G[f - 2];
                if (f >= 1) jerk += (p.G[f + 1] - 3 * p.G[f] + 3 * prev - prev2).LengthSquared();
            }
            T["seamv"] = SeamWeight * (p.G[1] - p.G[0] - SeamVel).LengthSquared() * 100;
            int la = B("LeftArm"), ra = B("RightArm"), sp2 = B("Spine2"), hd = B("Head"), ht = B("HeadTop_End"), hp = B("Hips"), lu = B("LeftUpLeg"), ll = B("LeftLeg"), ru = B("RightUpLeg"), rl = B("RightLeg");
            double pose = 0;
            for (int f = 1; f <= p.N; f++)
            {
                var b = Body.Bones[f * Body.Sub]; var rec = BakeSim.At(e.Run, f / 30f);
                Vector3 g = p.G[f], dir = Vector3.Normalize(rec.Ring - rec.Grip);
                reach += ReachW * Math.Pow(H((g - b[la]).Length() - ReachL), 2);
                reach += ReachW * Math.Pow(H((g + dir * .15f - b[ra]).Length() - ReachR), 2);
                if (g.Y < b[hd].Y - .05f) reach += 400 * Math.Pow(H(.24 - new Vector2(g.X - b[sp2].X, g.Z - b[sp2].Z).Length()), 2);
                reach += 400 * Math.Pow(H(.62 - g.Y), 2) + 400 * Math.Pow(H(g.Y - HandTop), 2);
                reach += 800 * Math.Pow(H(.22 - (g - b[hd]).Length()), 2);
                // кулаки и предплечья (точка на 40 % к плечу) — мимо головы: капсула Head–HeadTop, зазор HeadClear
                Vector3 h0 = b[hd], h1 = b[ht], fr = g + dir * .15f;
                foreach (var q in new[] { g, fr, Vector3.Lerp(g, b[la], .4f), Vector3.Lerp(fr, b[ra], .4f) })
                    reach += 3000 * Math.Pow(H(HeadClear - SegDist(q, h0, h1)), 2);
                if (f >= Contact + 2) kin += 4 * Math.Pow(H((p.G[f] - p.G[f - 1]).Length() * 30 - ExitVMax), 2);
                // бёдра и корпус: кулаки и предплечья не входят (выпад, рывок вниз)
                foreach (var q in new[] { g, fr, Vector3.Lerp(g, b[la], .4f), Vector3.Lerp(fr, b[ra], .4f) })
                {
                    reach += 3000 * Math.Pow(H(ThighClear - SegDist(q, b[lu], b[ll])), 2) + 3000 * Math.Pow(H(ThighClear - SegDist(q, b[ru], b[rl])), 2);
                    reach += 3000 * Math.Pow(H(.21 - SegDist(q, b[hp], b[sp2])), 2);
                }
                // поза 5: кулаки над головой в кадре «над головой» (±1 — вполсилы); поза 6: кулаки вперёд-вниз в ударе
                float wo = Math.Abs(f - Overhead) < .5f ? 1f : Math.Abs(f - Overhead) < 1.5f ? .5f : 0f;
                if (wo > 0)
                {
                    Vector3 top = b[ht];
                    pose += wo * (800 * Math.Pow(H(top.Y + OverUp - g.Y), 2) + 400 * Math.Pow(H(new Vector2(g.X - top.X, g.Z - top.Z).Length() - .30), 2));
                }
                if (f >= Contact - 1 && f <= Contact + 3) pose += 400 * Math.Pow(H(ForwardZ - (g.Z - b[hp].Z)), 2);
            }
            if (HandKeys != null)
                foreach (var kv in HandKeys) if (kv.Key <= p.N) pose += KeyW * (p.G[kv.Key] - kv.Value).LengthSquared();
            T["kin"] = kin; T["reach"] = reach; T["pose"] = PoseW * pose; T["jerk"] = JerkW * jerk;
            var end = BakeSim.At(e.Run, p.N / 30f);
            double rEnd = Math.Sqrt(end.P.X * end.P.X + end.P.Z * end.P.Z);
            T["end"] = 50 * Math.Pow(H(rEnd - EndRadius), 2) + 2 * Math.Pow(H(end.V.Length() - 1.5), 2);
            T["seam"] = Path3s.Designed ? 300 * e.SeamResidual + .3 * H(e.SeamAccel - 400) + 2 * e.SeamMean
                                         + 5 * Math.Pow(H(p.Dv.Length() - 30), 2) : 0;
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
        }
    }
}
