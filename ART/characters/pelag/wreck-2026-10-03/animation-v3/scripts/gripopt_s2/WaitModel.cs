using System;
using System.Collections.Generic;
using System.Numerics;
using AnchorBake;

namespace GripOpt2
{
    /// <summary>Wait1/Wait2: upper-body loop of L frames (grip G[0] = G[L] = the seam grip of the previous clip), played
    /// Loops times from the previous bake's head state (live pendulum, not baked). Scored only for what the live rig must
    /// not do: taut chain through the body, head in the body, hands far from the hip, jerky hands, a frozen-looking loop.</summary>
    public sealed class WaitModel
    {
        public readonly GripTrack Body;               // stand-in body: L frames (one loop), sub samples; arms follow the grip
        public readonly HeadModel Head;
        public readonly Rec Start;
        public readonly int L, Loops;
        public Vector3 SeamVel;
        public float VMax = 6f, AMax = 120f, Radius = .28f, Clear = .03f, SlowWeight = .02f, SlackWeight = 1f, ReachL = .50f, ReachR = .54f, HandleW = .55f, ClearWeight = 1f, YMin = .62f, YMax = 1.35f, NearTaut = .10f, ThighClear = .33f, TorsoClear = .34f;
        public float[] Robust = new float[0];
        readonly int[] _arm, _fore, _hand;

        public WaitModel(GripTrack body, HeadModel head, Rec start, int loops)
        {
            Body = body; Head = head; Start = start; L = body.Frames; Loops = loops;
            int B(string n) => Array.IndexOf(body.BoneNames, n);
            _arm = new[] { B("LeftArm"), B("RightArm") };
            _fore = new[] { B("LeftForeArm"), B("RightForeArm") };
            _hand = new[] { B("LeftHand"), B("LeftHandMiddle1"), B("LeftHandIndex1"), B("RightHand"), B("RightHandMiddle1"), B("RightHandIndex1") };
        }

        /// <summary>Loops × the loop, the grip replaced; hands and forearms of the stand-in follow the grip (capsules).</summary>
        public GripTrack TrackOf(Path2 p, bool moveArms = true)
        {
            int sub = Body.Sub, n = L * Loops * sub + 1;
            var t = new GripTrack
            {
                Clip = Body.Clip, Path = Body.Path, Sha256 = "", SourceFbx = Body.SourceFbx, SourceSha = "", Bind = Body.Bind, Hand = "Left",
                Fps = 30, Sub = sub, Frames = L * Loops, BoneUnitMetres = Body.BoneUnitMetres, RestHeight = Body.RestHeight, BoneNames = Body.BoneNames,
                Grip = new Vector3[n], Support = new Vector3[n], GripQ = new Quaternion[n], Spine2Q = new Quaternion[n], Bones = new Vector3[n][],
            };
            for (int i = 0; i < n; i++)
            {
                int j = i % (L * sub);
                if (i > 0 && j == 0) j = L * sub;             // last sample of each loop = frame L of the loop
                float x = j / (float)sub;
                int f = Math.Min((int)Math.Floor(x), L - 1);
                // exact: the body file is the exported clip (anchor_grip_export) - its own grip samples, as Unity plays them
                Vector3 g = moveArms ? Vector3.Lerp(p.G[f], p.G[f + 1], x - f) : Body.Grip[j], d = g - Body.Grip[j];
                t.Grip[i] = g; t.Support[i] = Body.Support[j] + d; t.GripQ[i] = Body.GripQ[j]; t.Spine2Q[i] = Body.Spine2Q[j];
                var b = (Vector3[])Body.Bones[j].Clone();
                if (moveArms)
                {
                    foreach (int k in _hand) b[k] += d;
                    foreach (int k in _fore) b[k] += d * .55f;
                }
                t.Bones[i] = b;
            }
            return t;
        }

        public BakeConfig Config() => new BakeConfig
        {
            Kind = BakeKind.Swing, ChainLength = 1.6f, ContactFrame = 1f, Mass = 12f, Gravity = 9.81f, BodyLimbs = true,
            Start = "prev@seam", StartState = Start,
        };

        static double H(double x) => x > 0 ? x : 0;
        static double SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; float t = Math.Clamp(Vector3.Dot(p - a, d) / Math.Max(1e-6f, d.LengthSquared()), 0, 1);
            return (p - (a + d * t)).Length();
        }

        public Eval2 Evaluate(Path2 p, bool moveArms = true)
        {
            var e = new Eval2();
            var track = TrackOf(p, moveArms);
            var sim = new BakeSim(track, Head);
            e.Run = sim.Run(Config());
            e.V = Validation.Run(e.Run, track, Head, new ExternalChecks());
            var T = e.Terms;
            var body = new AnchorBake.Body(track);
            double clr = 0, slow = 0, slack = 0;
            foreach (var r in e.Run.Records)
            {
                double rel = (r.RingV - r.GripV).Length(), sl = 1.6 - r.Span;
                slack += H(rel - 7) * H(sl - .015);           // a slack chain while the head is still fast reads as a glitch
                if (r.Span >= 1.6f - NearTaut)     // a chain 1 cm short of taut is drawn just as straight: score it too
                {
                    Vector3 dir = Vector3.Normalize(r.Ring - r.Grip), from = r.Grip + dir * .15f;
                    foreach (var c in body.Anatomy(r.Frame))
                    {
                        double cl = c.SegmentClearance(from, r.Ring);
                        clr += Math.Pow(H(Clear - cl), 2);
                        if (cl < e.NearTautClear) { e.NearTautClear = cl; e.NearTautWhere = c.Name + " @t" + r.Tick.ToString("0.00"); }
                    }
                }
                slow += r.V.LengthSquared() * r.T;            // the head should calm down over the window
            }
            T["clear"] = ClearWeight * 4000 * clr / Math.Max(1, e.Run.Records.Count) * 10;
            // robustness: the live state at the seam differs a little in the game (frame timing, walking) - the same
            // loop must keep the chain off the body for a head a bit slower / faster than the nominal one
            foreach (float k in Robust)
            {
                var c = Config(); var st = Start; st.V *= k; c.StartState = st;
                var run = sim.Run(c);
                double cl2 = 0;
                foreach (var r in run.Records)
                    if (r.Span >= 1.6f - NearTaut)
                    {
                        Vector3 dir = Vector3.Normalize(r.Ring - r.Grip), from = r.Grip + dir * .15f;
                        foreach (var cp in body.Anatomy(r.Frame)) cl2 += Math.Pow(H(Clear - cp.SegmentClearance(from, r.Ring)), 2);
                    }
                T["clear"] += ClearWeight * 4000 * cl2 / Math.Max(1, run.Records.Count) * 10;
            }
            T["pen"] = 1500 * H(e.V.Penetration - .004);
            T["jump"] = 300 * H(e.V.MaxJump - .12) + 2000 * H(e.V.MaxStretch - .004);
            T["slow"] = SlowWeight * slow / Math.Max(1, e.Run.Records.Count);
            T["slack"] = SlackWeight * (4 * e.V.SlackFastFrames + 60 * e.V.MaxSlackFast + 3 * slack);
            double kin = 0, reach = 0, jerk = 0;
            for (int f = 0; f < L; f++)
            {
                Vector3 a = p.G[f], b = p.G[f + 1], prev = f == 0 ? p.G[L - 1] : p.G[f - 1];   // loop: frame L = frame 0
                kin += 2 * Math.Pow(H((b - a).Length() * 30 - VMax), 2);
                kin += .002 * Math.Pow(H((b - 2 * a + prev).Length() * 900 - AMax), 2);
                Vector3 prev2 = f <= 1 ? p.G[L - 2 + f] : p.G[f - 2];
                jerk += (b - 3 * a + 3 * prev - prev2).LengthSquared();
                reach += 300 * Math.Pow(H((a - p.G[0]).Length() - Radius), 2) + 400 * Math.Pow(H(YMin - a.Y), 2) + 400 * Math.Pow(H(a.Y - YMax), 2);
            }
            // both fists within reach of the real shoulders (left socket; right fist on the handle GAP further on)
            int la = Array.IndexOf(Body.BoneNames, "LeftArm"), ra = Array.IndexOf(Body.BoneNames, "RightArm");
            for (int f = 1; f < L; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                // handle axis as the author places it (v3w1_keys.handle_dir): seam axis turned toward the right shoulder
                Vector3 ax0 = Vector3.Normalize(Body.Support[0] - Body.Grip[0]), toR = Vector3.Normalize(b[ra] - p.G[f]);
                Vector3 ax = Vector3.Normalize(Vector3.Lerp(ax0, toR, HandleW * (float)Math.Sin(Math.PI * f / L)));
                reach += 2000 * Math.Pow(H((p.G[f] - b[la]).Length() - ReachL), 2);
                reach += 2000 * Math.Pow(H((p.G[f] + ax * .15f - b[ra]).Length() - ReachR), 2);
            }
            // fists off the left thigh and the hips (the mesh check of the clip: hand/forearm not in thigh or torso)
            int lu = Array.IndexOf(Body.BoneNames, "LeftUpLeg"), ll = Array.IndexOf(Body.BoneNames, "LeftLeg"),
                hp = Array.IndexOf(Body.BoneNames, "Hips"), nk = Array.IndexOf(Body.BoneNames, "Neck");
            for (int f = 1; f < L; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                reach += 2000 * Math.Pow(H(ThighClear - SegDist(p.G[f], b[lu], b[ll])), 2) + 2000 * Math.Pow(H(TorsoClear - SegDist(p.G[f], b[hp], b[nk])), 2);
            }
            // first loop leaves the seam with the previous clip's grip velocity (no kink at Swing1 frame 12)
            T["seam"] = 300 * (p.G[1] - p.G[0] - SeamVel).LengthSquared();
            T["kin"] = kin; T["reach"] = reach; T["jerk"] = 20 * jerk;
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
            return e;
        }
    }
}
