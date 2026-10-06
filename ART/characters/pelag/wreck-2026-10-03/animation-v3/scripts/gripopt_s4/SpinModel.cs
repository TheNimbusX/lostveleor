using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnchorBake;

namespace GripOpt4
{
    /// <summary>Swing2 = continuation of the spin: grip path G[0..N] (Unity root: x right, y up, z forward, m) and pelvis yaw Psi[0..N]
    /// (deg, + to the left, unwrapped). Frame 0 = Swing1 frame 8 (seam: grip and head fixed). Frames ≥ CopyFrom are Swing1 frames
    /// (f − Contact + 7) turned by 360° (= the same pose): the second hit lands in the Swing1 contact pose.</summary>
    public sealed class Path4
    {
        public Vector3[] G; public float[] Psi;
        public Path4(int n) { G = new Vector3[n + 1]; Psi = new float[n + 1]; }
        public int N => G.Length - 1;
        public static int FreeYaw;                    // yaw is free for frames 1..FreeYaw (0 = yaw fixed)

        public double[] ToVector()
        {
            var v = new List<double>();
            for (int f = 1; f <= N; f++) { v.Add(G[f].X); v.Add(G[f].Y); v.Add(G[f].Z); }
            for (int f = 1; f <= FreeYaw; f++) v.Add(Psi[f] / 100.0);
            return v.ToArray();
        }

        public static Path4 FromVector(double[] v, Path4 template)
        {
            int n = template.N; var p = new Path4(n); p.G[0] = template.G[0];
            Array.Copy(template.Psi, p.Psi, n + 1);
            for (int f = 1; f <= n; f++) p.G[f] = new Vector3((float)v[3 * f - 3], (float)v[3 * f - 2], (float)v[3 * f - 1]);
            for (int f = 1; f <= FreeYaw; f++) p.Psi[f] = (float)(v[3 * n + f - 1] * 100.0);
            return p;
        }
    }

    public sealed class Eval4
    {
        public double Cost; public BakeRun Run; public Validation V; public GripTrack Track;
        public Dictionary<string, double> Terms = new Dictionary<string, double>();
    }

    public sealed class SpinModel
    {
        public readonly GripTrack Base;          // Swing1 grip track (bones of the accepted clip)
        public readonly GripTrack Real;          // authored Swing2 body (bones), or null → proxy body from Base and Psi
        public readonly HeadModel Head; public readonly Rec Start;
        public Vector3 SeamVel;
        public int Contact = 12, N = 17, CopyFrom = 11;
        public float Chain = 1.6f, VRel = 8f, AMax = 150f, ReachL = .56f, ReachR = .55f, ContactY = .72f, HandTop = 1.75f;
        public float SpeedMin = 21f, SpeedMax = 29f, SpeedGoal = 25f, PeakMax = 31.5f, YawRate = 33f, SeamWeight = 30f, JerkW = 20f, FollowW = 1f, JumpW = 300f, JumpAt = .125f, ClrMin = .004f, TorsoClear = .25f, ThighClear = .14f, HandOut = 0f, ArmMin = 0f, GripMin = 0f, FrontMax = 80f;
        public float[] S1Psi; public float[] YawCap; public float[] Twist;   // chest − pelvis per frame (proxy: upper body turned by Twist − 18 more)   // per transition f→f+1 (planted foot: lower; pirouette: up to 34)                    // Swing1 pelvis yaw per frame (measured from Base bones)
        readonly int iLLeg, iRLeg, iHips, iLUp, iRUp, iLArm, iRArm, iLFore, iRFore, iLHand, iRHand, iSp2, iHead, iTop;
        readonly float l1L, l2L, l1R, l2R;

        public SpinModel(GripTrack baseTrack, GripTrack real, HeadModel head, Rec start)
        {
            Base = baseTrack; Real = real; Head = head; Start = start;
            var t = baseTrack;
            iHips = t.Bone("Hips"); iLLeg = t.Bone("LeftLeg"); iRLeg = t.Bone("RightLeg"); iLUp = t.Bone("LeftUpLeg"); iRUp = t.Bone("RightUpLeg"); iLArm = t.Bone("LeftArm"); iRArm = t.Bone("RightArm");
            iLFore = t.Bone("LeftForeArm"); iRFore = t.Bone("RightForeArm"); iLHand = t.Bone("LeftHand"); iRHand = t.Bone("RightHand");
            iSp2 = t.Bone("Spine2"); iHead = t.Bone("Head"); iTop = t.Bone("HeadTop_End");
            var b = t.Bones[8 * t.Sub];
            l1L = (b[iLFore] - b[iLArm]).Length(); l2L = (b[iLHand] - b[iLFore]).Length() + .06f;
            l1R = (b[iRFore] - b[iRArm]).Length(); l2R = (b[iRHand] - b[iRFore]).Length() + .06f;
            S1Psi = new float[t.Frames + 1];
            for (int f = 0; f <= t.Frames; f++) S1Psi[f] = PelvisYaw(t.Bones[f * t.Sub]);
        }

        public float PelvisYaw(Vector3[] b)
        {
            Vector3 a = b[iLUp] - b[iRUp]; Vector3 fwd = Vector3.Cross(Vector3.UnitY, a);
            return (float)(Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI);
        }
        public float ChestYaw(Vector3[] b)
        {
            Vector3 a = b[iLArm] - b[iRArm]; Vector3 fwd = Vector3.Cross(Vector3.UnitY, a);
            return (float)(Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI);
        }

        static Vector3 RotY(Vector3 p, Vector3 c, float deg)
        {
            double a = deg * Math.PI / 180; float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
            Vector3 d = p - c;
            return c + new Vector3(d.X * cs - d.Z * sn, d.Y, d.X * sn + d.Z * cs);
        }

        /// <summary>Swing1 source frame for Swing2 frame x and the extra yaw turning it.</summary>
        public float SrcFrame(float x) => x >= CopyFrom ? Math.Min(Base.Frames, x - Contact + 7) : 8f;

        int[] _upper;
        int[] Upper => _upper ??= Base.BoneNames.Select((n, i) => (n, i)).Where(t => !t.n.Contains("Leg") && !t.n.Contains("Foot") && !t.n.Contains("Toe") && t.n != "Hips" && t.n != "Spine").Select(t => t.i).ToArray();
        public float TwAt(float x)
        {
            if (Twist == null) return 18f;
            int f = Math.Min((int)Math.Floor(x), Twist.Length - 2); float u = x - f;
            return Twist[f] + (Twist[f + 1] - Twist[f]) * u;
        }

        public bool ArcInterp = true, RealElbows = true; public float SpeedHardW = 0f;
        float YawAt(Path4 p, int f)
        {
            if (Real != null) return Unwrap(f, p);
            return p.Psi[f] + (f < CopyFrom ? TwAt(f) : 18f);
        }
        float[] _unw;
        float Unwrap(int f, Path4 p)
        {
            if (_unw == null)
            {
                var u = new float[Real.Frames + 1]; float prev = 0;
                for (int k = 0; k <= Real.Frames; k++)
                {
                    float y = ChestYaw(Real.Bones[k * Real.Sub]);
                    if (k > 0) { while (y - prev > 180) y -= 360; while (y - prev < -180) y += 360; }
                    u[k] = y; prev = y;
                }
                _unw = u;
            }
            return _unw[Math.Min(f, _unw.Length - 1)];
        }
        Vector3 HipsAt(int f) => (Real ?? Base).Bones[Math.Min(f * (Real ?? Base).Sub, (Real ?? Base).Bones.Length - 1)][iHips] * new Vector3(1, 0, 1);

        float PsiAt(Path4 p, float x)
        {
            int f = Math.Min((int)Math.Floor(x), p.N - 1); float u = x - f;
            return p.Psi[f] + (p.Psi[f + 1] - p.Psi[f]) * u;
        }

        /// <summary>Two-bone arm (elbow down-out): shoulder S → hand H, upper/lower lengths, side ±1 (left −x).</summary>
        static Vector3 Elbow(Vector3 S, Vector3 H, float l1, float l2, Vector3 outward)
        {
            Vector3 d = H - S; float L = Math.Max(1e-4f, d.Length()); Vector3 u = d / L;
            L = Math.Min(L, (l1 + l2) * .999f);
            float a = (l1 * l1 - l2 * l2 + L * L) / (2 * L), h = (float)Math.Sqrt(Math.Max(0, l1 * l1 - a * a));
            Vector3 pole = outward - u * Vector3.Dot(outward, u);
            pole = pole.LengthSquared() > 1e-8f ? Vector3.Normalize(pole) : Vector3.UnitY * -1;
            return S + u * a + pole * h;
        }

        /// <summary>Proxy body track: Swing1 bones turned by (Psi − Swing1 yaw) about the hips; arms reach the grip path.</summary>
        public GripTrack TrackOf(Path4 p)
        {
            var src = Real ?? Base; int sub = src.Sub;
            var t = new GripTrack
            {
                Clip = "Pelag_AN_Wreck2_Swing2", Path = src.Path, Sha256 = "", SourceFbx = src.SourceFbx, SourceSha = "", Bind = src.Bind, Hand = "Left",
                Fps = 30, Sub = sub, Frames = N, BoneUnitMetres = src.BoneUnitMetres, RestHeight = src.RestHeight, BoneNames = src.BoneNames,
            };
            int n = N * sub + 1;
            t.Grip = new Vector3[n]; t.Support = new Vector3[n]; t.GripQ = new Quaternion[n]; t.Spine2Q = new Quaternion[n]; t.Bones = new Vector3[n][];
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)sub; int f = Math.Min((int)Math.Floor(x), p.N - 1);
                if (ArcInterp)
                {   // между ключами кисть идёт по дуге вместе с телом (FK Build: локальные повороты линейно), а не по хорде
                    float y0 = YawAt(p, f), y1 = YawAt(p, f + 1), u = x - f; Vector3 c0 = HipsAt(f), c1 = HipsAt(f + 1);
                    Vector3 a0 = RotY(p.G[f] - c0, Vector3.Zero, -y0), a1 = RotY(p.G[f + 1] - c1, Vector3.Zero, -y1);
                    t.Grip[i] = Vector3.Lerp(c0, c1, u) + RotY(Vector3.Lerp(a0, a1, u), Vector3.Zero, y0 + (y1 - y0) * u);
                }
                else t.Grip[i] = Vector3.Lerp(p.G[f], p.G[f + 1], x - f);
                t.GripQ[i] = Quaternion.Identity; t.Spine2Q[i] = Quaternion.Identity;
                if (Real != null) { t.Bones[i] = Real.Bones[Math.Min(i, Real.Bones.Length - 1)]; t.Support[i] = Real.Support[Math.Min(i, Real.Bones.Length - 1)]; continue; }
                float sf = SrcFrame(x); var b0 = new Vector3[src.BoneNames.Length];
                for (int k = 0; k < b0.Length; k++) b0[k] = Base.BoneAt(k, sf);
                float rot = PsiAt(p, x) - (S1Psi[(int)Math.Floor(sf)] + (S1Psi[Math.Min(Base.Frames, (int)Math.Floor(sf) + 1)] - S1Psi[(int)Math.Floor(sf)]) * (sf - (float)Math.Floor(sf))) - (x >= CopyFrom ? 360f : 0f);
                Vector3 c = b0[iHips];
                for (int k = 0; k < b0.Length; k++) b0[k] = RotY(b0[k], c, rot);
                if (Twist != null && x < CopyFrom)
                {
                    float tw = TwAt(x) - 18f;
                    foreach (int k in Upper) b0[k] = RotY(b0[k], c, tw);
                }
                // arms on the grip path: left hand at the socket point, right on the chain 0,15 m out (direction ≈ away from the chest)
                Vector3 g = t.Grip[i], ch = b0[iSp2]; Vector3 out_ = g - new Vector3(ch.X, g.Y, ch.Z);
                out_ = out_.LengthSquared() > 1e-6f ? Vector3.Normalize(out_) : Vector3.UnitZ;
                Vector3 sup = g + out_ * .15f;
                Vector3 side = Vector3.Normalize(b0[iLArm] - b0[iRArm]);
                b0[iLHand] = g; b0[iLFore] = Elbow(b0[iLArm], g, l1L, l2L, side - Vector3.UnitY * .8f);
                b0[iRHand] = sup; b0[iRFore] = Elbow(b0[iRArm], sup, l1R, l2R, -side - Vector3.UnitY * .8f);
                t.Bones[i] = b0; t.Support[i] = sup;
            }
            return t;
        }

        public BakeConfig Config() => new BakeConfig
        {
            Kind = BakeKind.Swing, ChainLength = Chain, ContactFrame = Contact, Mass = 12f, Gravity = 9.81f, BodyLimbs = true,
            Start = "Swing1@8", StartState = Start,
        };

        public Eval4 Evaluate(Path4 p)
        {
            var e = new Eval4();
            e.Track = TrackOf(p);
            e.Run = new BakeSim(e.Track, Head).Run(Config());
            e.V = Validation.Run(e.Run, e.Track, Head, new ExternalChecks());
            Score(e, p);
            return e;
        }

        static double H(double x) => x > 0 ? x : 0;
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-6f, ab.LengthSquared()), 0, 1);
            return Vector3.Distance(p, a + ab * t);
        }
        float YawOf(GripTrack t, int f) => Real != null ? ChestYaw(t.Bones[f * t.Sub]) : 0f;

        void Score(Eval4 e, Path4 p)
        {
            var v = e.V; var T = e.Terms; var tr = e.Track;
            var at = BakeSim.At(e.Run, Contact / 30f);
            double r = Math.Sqrt(at.P.X * at.P.X + at.P.Z * at.P.Z), ang = Math.Atan2(at.P.X, at.P.Z) * 180 / Math.PI;
            T["contact"] = 400 * v.ContactError + 20 * Math.Pow(r - 2.40, 2) + 150 * Math.Pow(at.P.Y - ContactY, 2) + .02 * ang * ang;
            double s = v.ContactSpeed;
            T["speed"] = 8 * H(SpeedMin - s) + 8 * H(s - SpeedMax) + .05 * (s - SpeedGoal) * (s - SpeedGoal) + 20 * H(v.MaxSpeed - PeakMax) + SpeedHardW * Math.Pow(H(s - 29.4), 2);
            double soft = 0;
            foreach (var rr in e.Run.Records)
            {
                double rel = (rr.RingV - rr.GripV).Length(), sl = Chain - rr.Span;
                soft += H(rel - 7) * H(sl - .012);
            }
            T["slack"] = 4 * v.SlackFastFrames + 60 * v.MaxSlackFast + 3 * soft;
            T["pen"] = 1500 * H(v.Penetration - .004) + 1500 * H(ClrMin - (v.ChainClearance == double.MaxValue ? 1 : v.ChainClearance));
            T["jump"] = JumpW * H(v.MaxJump - JumpAt) + 2000 * H(v.MaxStretch - .004);
            // hands: speed and acceleration relative to the turning chest (the body carries them round)
            double kin = 0, reach = 0, jerk = 0, yaw = 0;
            Vector3 Rel(int f) { float y = Real != null ? YawOf(tr, f) : p.Psi[f]; return RotY(p.G[f], Vector3.Zero, -y); }
            for (int f = 0; f < p.N; f++)
            {
                double vf = (Rel(f + 1) - Rel(f)).Length() * 30;
                kin += 2 * Math.Pow(H(vf - VRel), 2);
                Vector3 prev = f == 0 ? p.G[0] - SeamVel : p.G[f - 1];
                double af = (p.G[f + 1] - 2 * p.G[f] + prev).Length() * 900;
                kin += .002 * Math.Pow(H(af - AMax), 2);
                Vector3 prev2 = f <= 1 ? (f == 0 ? p.G[0] - 2 * SeamVel : p.G[0] - SeamVel) : p.G[f - 2];
                if (f >= 1) jerk += (p.G[f + 1] - 3 * p.G[f] + 3 * prev - prev2).LengthSquared();
            }
            T["seam"] = SeamWeight * (p.G[1] - p.G[0] - SeamVel).LengthSquared() * 100;
            for (int f = 1; f <= p.N; f++)
            {
                var b = tr.Bones[f * tr.Sub]; var rec = BakeSim.At(e.Run, f / 30f);
                Vector3 g = p.G[f], dir = Vector3.Normalize(rec.Ring - rec.Grip);
                reach += 2000 * Math.Pow(H((g - b[iLArm]).Length() - ReachL), 2);
                reach += 2000 * Math.Pow(H((g + dir * .15f - b[iRArm]).Length() - ReachR), 2);
                Vector3 c = b[iSp2];
                reach += 400 * Math.Pow(H(.24 - new Vector2(g.X - c.X, g.Z - c.Z).Length()), 2);
                reach += 400 * Math.Pow(H(.62 - g.Y), 2) + 400 * Math.Pow(H(g.Y - HandTop), 2);
                reach += 800 * Math.Pow(H(.22 - (g - b[iHead]).Length()), 2);
                // кулаки и предплечья (40 % к плечу) — не в корпус, бёдра и голову (сетка рук ≈ 6 см, корпус ≈ 18 см)
                Vector3 fr = g + dir * .15f;
                // локти — как ставит их автор (полюс: левый назад-наружу-вниз, правый вправо-вниз от груди), точки вдоль предплечий
                float cyw = ChestYaw(b); double ca = cyw * Math.PI / 180;
                Vector3 fwC = new Vector3((float)-Math.Sin(ca), 0, (float)Math.Cos(ca)), lfC = new Vector3((float)-Math.Cos(ca), 0, (float)-Math.Sin(ca));
                Vector3 poleL = -.05f * fwC + .85f * lfC - .35f * Vector3.UnitY, poleR = -.05f * fwC - .85f * lfC - .35f * Vector3.UnitY;
                if (f <= Contact) reach += 3000 * Math.Pow(H(GripMin - g.Y), 2);
                Vector2 hx = new Vector2(g.X - b[iHips].X, g.Z - b[iHips].Z), sx = new Vector2(fr.X - b[iHips].X, fr.Z - b[iHips].Z);
                reach += 3000 * Math.Pow(H(HandOut - hx.Length()), 2) + 3000 * Math.Pow(H(HandOut + .04 - sx.Length()), 2);
                reach += 3000 * Math.Pow(H(ArmMin - (g - b[iLArm]).Length()), 2) + 3000 * Math.Pow(H(ArmMin - (fr - b[iRArm]).Length()), 2);   // рука не сложена: локоть не уходит в корпус
                Vector3 eL = Elbow(b[iLArm], g, l1L, l2L, poleL), eR = Elbow(b[iRArm], fr, l1R, l2R, poleR);
                if (Real != null && RealElbows)
                {   // локти авторского тела (прошлый круг) + половина сдвига кисти: модель ближе к настоящему IK, чем «полюс»
                    int si = Math.Min(f * Real.Sub, Real.Grip.Length - 1);
                    eL = b[iLFore] + (g - Real.Grip[si]) * .5f; eR = b[iRFore] + (fr - Real.Support[si]) * .5f;
                }
                var pts = new List<Vector3> { g, fr };
                for (int k = 1; k <= 4; k++) { pts.Add(Vector3.Lerp(g, eL, k / 4f)); pts.Add(Vector3.Lerp(fr, eR, k / 4f)); }
                pts.Add(Vector3.Lerp(eL, b[iLArm], .5f)); pts.Add(Vector3.Lerp(eR, b[iRArm], .5f));
                foreach (var q in pts)
                {
                    reach += 3000 * Math.Pow(H(TorsoClear - SegDist(q, b[iHips], b[iSp2])), 2);
                    reach += 3000 * Math.Pow(H(ThighClear - SegDist(q, b[iLUp], b[iLLeg])), 2) + 3000 * Math.Pow(H(ThighClear - SegDist(q, b[iRUp], b[iRLeg])), 2);
                    reach += 3000 * Math.Pow(H(.16 - SegDist(q, b[iHead], b[iTop])), 2);
                }
                // fists in front of the chest (the body turns with the head; the chain leaves forward, not round the back)
                float cy = Real != null ? ChestYaw(b) : p.Psi[f] + (f < CopyFrom ? TwAt(f) : 18f); double a = cy * Math.PI / 180;
                Vector2 fw = new Vector2((float)-Math.Sin(a), (float)Math.Cos(a)), hv = new Vector2(g.X - c.X, g.Z - c.Z);
                double cosA = hv.Length() > 1e-4f ? Vector2.Dot(fw, hv) / hv.Length() : 1;
                reach += 300 * Math.Pow(H(Math.Acos(Math.Clamp(cosA, -1, 1)) * 180 / Math.PI - (f <= Contact ? FrontMax : 80)) / 10, 2);
            }
            for (int f = 0; f < p.N; f++)
            {
                double d = p.Psi[f + 1] - p.Psi[f], cap = YawCap != null && f < YawCap.Length ? YawCap[f] : YawRate;
                yaw += 50 * Math.Pow(H(Math.Abs(d) - cap), 2) + 30 * Math.Pow(H(-d), 2);
                if (f >= 1) yaw += .02 * Math.Pow(p.Psi[f + 1] - 2 * p.Psi[f] + p.Psi[f - 1], 2);
            }
            T["kin"] = kin; T["reach"] = reach; T["jerk"] = JerkW * jerk; T["yaw"] = yaw;
            // after contact: the head goes on to the left and keeps flying (Slam / Wait2 start from it)
            var end = BakeSim.At(e.Run, Math.Min(p.N, Contact + 1) / 30f);
            T["follow"] = FollowW * (2 * H(end.P.X - .2) + .5 * H(18 - end.V.Length()));
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
        }
    }
}
