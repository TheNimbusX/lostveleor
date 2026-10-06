using System;
using System.Collections.Generic;
using System.Numerics;
using AnchorBake;
using Game.View;

namespace GripOpt
{
    /// <summary>
    /// Swing1 v3 grip path (left-hand socket = chain start, Unity root space: x right, y up, z forward, metres).
    /// One point per clip frame 0..12 (frame = Sim tick); between frames linear, as Unity plays the per-frame keys.
    /// Start of the head (frame 0) = the draw: the head leaves the back mount (position = mount + D, rotation = mount)
    /// already flung over the right shoulder with velocity V0 - a 12 kg head on a 1.6 m chain cannot be yanked off the
    /// back by hands that stay within 1 m of it, so the flip is the start state; from frame 0 everything is StepLive.
    /// The game shows the first press as OnBack -> Baked through the rig's seam (AnchorBlend, 400 m/s²): scored too.
    /// </summary>
    public sealed class PathParams
    {
        public const int Frames = 12;
        public Vector3[] G = new Vector3[Frames + 1];
        public Vector3 D = new Vector3(.2f, 0, -.15f), V0 = new Vector3(6, -4, -2);
        public static float DS = 1f;
        public static bool Free0;          // кадр 0 — снятие (кисти у правого плеча), а не стойка: точка хвата кадра 0 тоже ищется       // vector scale of D.X, D.Y (whirl: degrees -> 0.01)

        public double[] ToVector()
        {
            var v = new List<double>();
            for (int f = Free0 ? 0 : 1; f <= Frames; f++) { v.Add(G[f].X); v.Add(G[f].Y); v.Add(G[f].Z); }
            v.Add(D.X * DS); v.Add(D.Y * DS); v.Add(D.Z); v.Add(V0.X / 20); v.Add(V0.Y / 20); v.Add(V0.Z / 20);
            return v.ToArray();
        }

        public static PathParams FromVector(double[] v, Vector3 frame0)
        {
            var p = new PathParams();
            p.G[0] = frame0;
            int o = Free0 ? 3 : 0;
            for (int f = Free0 ? 0 : 1; f <= Frames; f++) p.G[f] = new Vector3((float)v[3 * f - 3 + o], (float)v[3 * f - 2 + o], (float)v[3 * f - 1 + o]);
            int k = 3 * Frames + o;
            p.D = new Vector3((float)(v[k] / DS), (float)(v[k + 1] / DS), (float)v[k + 2]);
            p.V0 = new Vector3((float)v[k + 3] * 20, (float)v[k + 4] * 20, (float)v[k + 5] * 20);
            return p;
        }
    }

    public sealed class Eval
    {
        public double Cost;
        public BakeRun Run;
        public Validation V;
        public Dictionary<string, double> Terms = new Dictionary<string, double>();
        public Rec Start;
        public float SeamAtContact, SeamAt6, SeamMaxAccel, SeamPen, SeamClear, SeamFront;
    }

    public sealed class Model
    {
        public readonly GripTrack Body;      // body bones (sub samples); grip replaced per evaluation
        public readonly HeadModel Head;
        public float VMax = 8f, AMax = 150f, Reach = .74f, DrawVMax = 13f, ReachL = .56f, ReachR = .55f;
        public float Chain = 1.6f, Contact = 7f, StartSpeedWeight = .5f, SeamWeight = 300f, DMax = .25f;
        public bool Whirl;
        public float V0Cap = 99f, DegMin = 70f, Pose1Weight = 0f;
        public Vector3 LeftHip = new Vector3(-.50f, .92f, .26f), RightHip = new Vector3(.24f, .96f, .22f);
        public float HandHigh = 0f, HandTop = 1.05f, ContactY = .74f;     // контакт: высота таза в выпаде (0,72) ± 0,15
        public float SpeedMin = 22.5f, Draw0Weight = 0f, RadiusRate = .07f, SpeedPull = .05f;
        public Vector3 Draw0 = new Vector3(.30f, 1.24f, .10f);   // кадр 0: кисти у правого плеча берут рукоять со спины
        public int SeamFromHalfFrames = 4;   // проверка показа со спины — с кадра 2 (первые 2 тика голова выходит из крепления)
        public readonly Vector3 BackCentre; public readonly Quaternion BackRotation;

        public Model(GripTrack body, HeadModel head)
        {
            Body = body; Head = head;
            var sk = new Body(body);
            Quaternion spine = body.Spine2Q[0];
            BackCentre = sk.Spine2At(0) + Vector3.Transform(new Vector3(.008f, -.031f, -.075f) * (float)body.BoneUnitMetres, spine);
            BackRotation = spine * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -25f * (float)Math.PI / 180f);
        }

        public GripTrack TrackOf(PathParams p)
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
                int f = Math.Min((int)Math.Floor(x), PathParams.Frames - 1);
                t.Grip[i] = Vector3.Lerp(p.G[f], p.G[f + 1], x - f);
            }
            return t;
        }

        /// <summary>flip: centre = mount + D, velocity V0. whirl: ring on the chain from the frame-1 grip
        /// (D = deg from +z toward +x, elevation below the grip, slack·L), velocity V0.x m/s tangential in the swing
        /// sense (toward -deg), V0.y m/s up; head along the chain.</summary>
        public Rec StartState(PathParams p)
        {
            if (!Whirl) return new Rec { P = BackCentre + p.D, Q = BackRotation, V = p.V0, W = Vector3.Zero };
            double d = p.D.X * Math.PI / 180, el = p.D.Y * Math.PI / 180;
            var dir = new Vector3((float)(Math.Sin(d) * Math.Cos(el)), (float)-Math.Sin(el), (float)(Math.Cos(d) * Math.Cos(el)));
            Vector3 ring = p.G[0] + dir * (p.D.Z * Chain);
            Quaternion q = FromTo(Vector3.UnitY, -dir);
            Vector3 centre = ring - Vector3.Transform(Head.EyeLocal, q);
            var tang = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));      // toward decreasing deg (right -> front)
            return new Rec { P = centre, Q = q, V = tang * p.V0.X + Vector3.UnitY * p.V0.Y, W = Vector3.Zero };
        }

        static Quaternion FromTo(Vector3 a, Vector3 b)
        {
            a = Vector3.Normalize(a); b = Vector3.Normalize(b);
            float c = Vector3.Dot(a, b);
            if (c < -.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI);
            Vector3 ax = Vector3.Cross(a, b);
            return Quaternion.Normalize(new Quaternion(ax.X, ax.Y, ax.Z, 1 + c));
        }

        public BakeConfig Config(Rec start) => new BakeConfig
        {
            Kind = BakeKind.Swing, ChainLength = Chain, ContactFrame = Contact, Mass = 12f, Gravity = 9.81f, BodyLimbs = true,
            Start = "draw-flip", StartState = start,
        };

        public Eval Evaluate(BakeSim sim, PathParams p, bool exactTrack = false)
        {
            var e = new Eval { Start = StartState(p) };
            var track = exactTrack ? Body : TrackOf(p);
            sim = new BakeSim(track, Head);          // the sim reads the grip from ITS track
            e.Run = sim.Run(Config(e.Start));
            e.V = Validation.Run(e.Run, track, Head, new ExternalChecks());
            InGameSeam(e, false, track);
            Score(e, p);
            return e;
        }

        /// <summary>First press in the game: head on the back mount, rig Drive(Baked) at 60 fps with deadline contact − 1.</summary>
        public void InGameSeam(Eval e, bool print = false, GripTrack track = null)
        {
            var anat = new Body(track ?? Body);
            var probe = Head.NewBody(12f, new Vector3(.75f, .55f, .75f));
            int sp2 = Array.IndexOf(Body.BoneNames, "Spine2");
            e.SeamPen = 0; e.SeamClear = 9; e.SeamFront = -9;
            float pen0 = 0, r0 = 0;
            var core = new AnchorRigCore(new AnchorRigSettings());
            core.Body.Hull = Head.HullLocal; core.Body.EyeLocal = Head.EyeLocal; core.Body.Mass = 12f;
            core.Teleport(AnchorRigMode.OnBack, AnchorPose.At(BackCentre, BackRotation));
            float maxAcc = 0;
            for (int k = 0; k <= 2 * (int)Contact; k++)
            {
                float f = k / 2f;
                var r = BakeSim.At(e.Run, f / 30f);
                float dl = f < Contact - 1 ? (Contact - 1 - f) / 30f : -1f;
                core.Drive(AnchorRigMode.Baked, 1, new AnchorPose(r.P, r.Q, r.V, r.W), 1f / 60f, dl);
                maxAcc = Math.Max(maxAcc, core.CorrectionAccel);
                if (k == 2 * (int)Contact - 2) e.SeamAt6 = core.BlendError;
                if (k <= 5)
                {
                    // снятие со спины: голова выходит из тела наружу — глубина в теле (68 точек) убывает до нуля к кадру 1,5,
                    // центр удаляется от оси корпуса (а не проходит сквозь грудь/руки к другой стороне)
                    var o = core.Output; var tr = track ?? Body;
                    float pen = HeadContact.Penetration(probe, o.Position, o.Rotation, anat.Anatomy(f), out _);
                    Vector3 c = tr.BoneAt(sp2, f), d = o.Position - c; d.Y = 0;
                    if (k == 0) { pen0 = pen; r0 = d.Length(); }
                    else
                    {
                        e.SeamFront = Math.Max(e.SeamFront, pen - Math.Max(0f, pen0 * (1 - k / 3f)));
                        e.SeamFront = Math.Max(e.SeamFront, (r0 + RadiusRate * k) - d.Length());
                        // не вперёд через грудь: пока голова рядом с осью корпуса по X, она остаётся позади плоскости спины
                        if (Math.Abs(o.Position.X - c.X) < .55f) e.SeamFront = Math.Max(e.SeamFront, o.Position.Z - (c.Z - .05f));
                    }
                }
                if (k >= SeamFromHalfFrames)
                {
                    var o = core.Output;
                    e.SeamPen = Math.Max(e.SeamPen, HeadContact.Penetration(probe, o.Position, o.Rotation, anat.Anatomy(f), out _));
                    Vector3 c = (track ?? Body).BoneAt(sp2, f);
                    if (o.Position.Y > .35f && o.Position.Y < 1.8f)
                        e.SeamClear = Math.Min(e.SeamClear, new Vector2(o.Position.X - c.X, o.Position.Z - c.Z).Length());
                }
                if (print) { var o = core.Output; Console.WriteLine($"  seam f{f,4:0.0} bake ({r.P.X:F2} {r.P.Y:F2} {r.P.Z:F2}) shown ({o.Position.X:F2} {o.Position.Y:F2} {o.Position.Z:F2}) |v| {o.Velocity.Length():F1} off {core.BlendError:F3} acc {core.CorrectionAccel:F0} omega {core.BlendOmega:F1}"); }
            }
            e.SeamAtContact = core.BlendError; e.SeamMaxAccel = maxAcc;
        }

        static double Hinge(double x) => x > 0 ? x : 0;

        void Score(Eval e, PathParams p)
        {
            var v = e.V; var T = e.Terms;
            var at = BakeSim.At(e.Run, Contact / 30f);
            double r = Math.Sqrt(at.P.X * at.P.X + at.P.Z * at.P.Z), ang = Math.Atan2(at.P.X, at.P.Z) * 180 / Math.PI;
            T["contact"] = 400 * v.ContactError + 20 * Math.Pow(r - 2.45, 2) + 150 * Math.Pow(at.P.Y - ContactY, 2) + .02 * ang * ang;
            double s = v.ContactSpeed;
            T["speed"] = 8 * Hinge(SpeedMin - s) + 8 * Hinge(s - 28) + SpeedPull * (s - 24) * (s - 24);
            double soft = 0;
            foreach (var rr in e.Run.Records)
            {
                double rel = (rr.RingV - rr.GripV).Length(), sl = Chain - rr.Span;
                soft += Hinge(rel - 7) * Hinge(sl - .015);
            }
            T["slack"] = 4 * v.SlackFastFrames + 60 * v.MaxSlackFast + 3 * soft;
            T["pen"] = 1500 * Hinge(v.Penetration - .004) + 1500 * Hinge(-.004 - (v.ChainClearance == double.MaxValue ? 1 : v.ChainClearance));
            T["jump"] = 300 * Hinge(v.MaxJump - .12) + 2000 * Hinge(v.MaxStretch - .004);
            double kin = 0, reach = 0, jerk = 0;
            int sp2 = Array.IndexOf(Body.BoneNames, "Spine2");
            for (int f = 0; f < PathParams.Frames; f++)
            {
                double vf = (p.G[f + 1] - p.G[f]).Length() * 30;
                kin += 2 * Math.Pow(Hinge(vf - (f < 2 ? DrawVMax : VMax)), 2);
                if (f >= 1)
                {
                    double af = (p.G[f + 1] - 2 * p.G[f] + p.G[f - 1]).Length() * 900;
                    kin += .002 * Math.Pow(Hinge(af - (f < 3 ? 2 * AMax : AMax)), 2);
                }
                if (f >= 2) jerk += (p.G[f + 1] - 3 * p.G[f] + 3 * p.G[f - 1] - p.G[f - 2]).LengthSquared();
            }
            int la = Array.IndexOf(Body.BoneNames, "LeftArm"), ra = Array.IndexOf(Body.BoneNames, "RightArm");
            for (int f = PathParams.Free0 ? 0 : 1; f <= PathParams.Frames; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                Vector3 c = b[sp2], g = p.G[f];
                var rec = BakeSim.At(e.Run, f / 30f);
                Vector3 dir = Vector3.Normalize(rec.Ring - rec.Grip);
                // both hands on the chain: left socket from the left shoulder, right fist GAP further along the chain
                reach += 2000 * Math.Pow(Hinge((g - b[la]).Length() - ReachL), 2);
                reach += 2000 * Math.Pow(Hinge((g + dir * .15f - b[ra]).Length() - ReachR), 2);
                double h = new Vector2(g.X - c.X, g.Z - c.Z).Length();
                reach += 400 * Math.Pow(Hinge((f >= 8 ? .36 : .24) - h), 2);
                if (f >= 9)
                {   // проводка: кулак сбоку от таза, а не перед животом (предплечье не входит в корпус)
                    Vector3 hp = b[Array.IndexOf(Body.BoneNames, "Hips")];
                    reach += 800 * Math.Pow(Hinge(.42 - new Vector2(g.X - hp.X, g.Z - hp.Z).Length()), 2);
                }
                if (f >= 10) reach += 50 * Math.Pow(Hinge((p.G[f] - p.G[f - 1]).Length() * 30 - 3.5), 2);
                if (f >= 3 && f <= 9) reach += HandHigh * Math.Pow(Hinge(g.Y - HandTop), 2);   // поза 2: кисти тянут цепь у пояса, не на уровне плеч
                reach += 400 * Math.Pow(Hinge(.62 - g.Y), 2) + 400 * Math.Pow(Hinge(g.Y - 1.75), 2);
            }
            T["kin"] = kin; T["reach"] = reach; T["jerk"] = 20 * jerk;
            var end = BakeSim.At(e.Run, 12f / 30f);
            double a12 = Math.Atan2(end.P.X, end.P.Z) * 180 / Math.PI;
            T["follow"] = .002 * Math.Pow(Hinge(a12 + 100), 2) + .002 * Math.Pow(Hinge(-165 - a12), 2)
                          + 60 * Math.Pow(Hinge((p.G[12] - LeftHip).Length() - .08), 2)
                          + Pose1Weight * Math.Pow(Hinge((p.G[2] - RightHip).Length() - .20), 2)
                          + (PathParams.Free0 ? Draw0Weight * Math.Pow(Hinge((p.G[0] - Draw0).Length() - .12), 2) : 0);
            // start: out of the torso capsule (r .38 + margin about the body axis), small flip speed, near the mount
            Vector3 s0 = e.Start.P;
            T["start"] = 800 * Math.Pow(Hinge(.42 - new Vector2(s0.X, s0.Z).Length()), 2) + StartSpeedWeight * e.Start.V.Length()
                         + 50 * Math.Pow(Hinge(e.Start.V.Length() - V0Cap), 2)
                         + (Whirl ? .05 * Math.Pow(Hinge(DegMin - p.D.X), 2) + .05 * Math.Pow(Hinge(p.D.X - 175), 2) + 20000 * Math.Pow(Hinge(p.D.Z - .99), 2)
                                    + 50 * Math.Pow(Hinge(.5 - (s0.Y - .1)), 2)
                                  : 5 * Math.Pow(Hinge(p.D.Length() - DMax), 2));
            T["seam"] = SeamWeight * (e.SeamAtContact + .5 * e.SeamAt6) + 3000 * e.SeamPen + 2000 * Math.Pow(Hinge(.48 - e.SeamClear), 2)
                        + 3000 * Math.Pow(Hinge(e.SeamFront), 2);
            double sum = 0; foreach (var kv in T) sum += kv.Value;
            e.Cost = sum;
        }
    }
}
