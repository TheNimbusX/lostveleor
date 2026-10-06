using System;
using System.Collections.Generic;
using System.Numerics;
using Game.View;

namespace AnchorBake
{
    /// <summary>Что запекается и чем проверяется (DESIGN §2.1, §5): мах, удар оземь, цикл, выход из цикла, замах броска.</summary>
    public static class BakeKind
    {
        public const string Swing = "swing", Slam = "slam", Loop = "loop", Release = "release", Windup = "windup";
        public static bool Ground(string k) => k == Slam || k == Release;
        public static bool Valid(string k) => k == Swing || k == Slam || k == Loop || k == Release || k == Windup;
    }

    public sealed class BakeConfig
    {
        public float ChainLength = 1.6f, Mass = 12f, Gravity = 9.81f, Dt = 1f / 240f;
        public Vector3 Inertia = new Vector3(.75f, .55f, .75f);
        public float AirLinear = .6f, AirAngular = 1.5f;
        public string Kind = BakeKind.Swing;
        public float ContactFrame = 6f;        // clip frame = Sim tick of contact from StageStartTick
        public float OverheadFrame, ReleaseFrame, SheathFrame;
        public Vector3 Impact = new Vector3(0, 0, 2.2f);   // slam / release: Sim point in root space (y ignored)
        public float LoopFrom, LoopTo;         // loop: clip frames of one cycle
        public int Cycles = 30;                // loop: cycles simulated (air drag 0.6/s needs ~10 s of run-in), the last one is recorded
        public float Ground;                   // WeaponGroundHeight - root.y (flat arena floor: 0)
        public float ClipRate = 1f;            // AbilityExecutionTicks variation (0.8x / 1.25x)
        public float RetimeShift;              // frames, monotonic warp of [0..contact], ends pinned (DESIGN 3.3 p.1)
        public string Start = "rest";
        public float PreRoll = 2f;
        public Vector3 GuideAccel;             // shooting result: A of a = A * 30u^2(1-u)^2 over the last GuideTicks
        public float GuideTicks = 3f;
        public bool BodyLimbs = true;
        public int RecordPerTick = 4;
        public Rec? StartState;                // seam from a previous bake
        public bool Loop => Kind == BakeKind.Loop && LoopTo > LoopFrom;
    }

    public struct Rec
    {
        public float T, Tick, Frame;           // seconds from stage start; bake frame (= Sim tick, file "t"); source clip frame
        public Vector3 P, V, W, Ring, RingV, Grip, GripV, Support;
        public Quaternion Q, GripQ;
        public float Tension, Span, BodyPush;
        public bool Taut, Grounded, Guided, Settling;
    }

    public sealed class BakeRun
    {
        public BakeConfig Cfg;
        public List<Rec> Records = new List<Rec>();
        public float ContactTime;              // real seconds from stage start
        public int BodyContacts, Bounces;
        public float MaxBodyPush;
        public double MillisecondsPerFrame60;  // cost of AnchorRigCore.StepLive per 60 fps frame (4 substeps)
    }

    /// <summary>
    /// Запечка = тот же <see cref="AnchorRigCore.StepLive"/>, что в игре (DESIGN §3.2): Advance → ConstrainCable → земля
    /// с отскоком → тело <see cref="AnchorRigBody"/> (корпус, кольцо, оболочка против ног, рук, головы) → воздух.
    /// Хват подаётся кадрами 120 Гц (2 подшага 1/240 с на кадр, как игра подаёт кадрами рендера). Своей физики нет.
    /// </summary>
    public sealed class BakeSim
    {
        readonly GripTrack _t; readonly Body _body; readonly HeadModel _head;
        readonly AnchorCapsule[] _caps = new AnchorCapsule[AnchorRigBody.Capacity], _chainCaps = new AnchorCapsule[AnchorRigBody.Capacity];
        public BakeSim(GripTrack t, HeadModel head) { _t = t; _head = head; _body = new Body(t); }
        public GripTrack Track => _t;
        public Body Skeleton => _body;

        public float FrameAt(BakeConfig c, float time)
        {
            float x = time * _t.Fps * c.ClipRate;
            if (c.Loop)
            {
                float span = c.LoopTo - c.LoopFrom;
                return c.LoopFrom + (x % span + span) % span;
            }
            if (c.RetimeShift != 0 && x > 0 && x < c.ContactFrame)
                x += c.RetimeShift * (float)Math.Sin(Math.PI * x / c.ContactFrame);
            return Math.Clamp(x, 0, _t.Frames);
        }

        public float Period(BakeConfig c) => (c.LoopTo - c.LoopFrom) / (_t.Fps * c.ClipRate);
        public float Duration(BakeConfig c) => c.Loop ? Period(c) * c.Cycles : _t.Frames / (_t.Fps * c.ClipRate);

        public AnchorRigCore NewCore(BakeConfig c)
        {
            var settings = new AnchorRigSettings
            {
                ChainLength = c.ChainLength, Gravity = c.Gravity, Step = c.Dt, AirDrag = c.AirLinear, SpinDrag = c.AirAngular,
            };
            var core = new AnchorRigCore(settings);
            core.Body.Hull = _head.HullLocal; core.Body.EyeLocal = _head.EyeLocal; core.Body.Mass = c.Mass; core.Body.Inertia = c.Inertia;
            return core;
        }

        /// <summary>Капсулы тела в кадре клипа — тот же <see cref="AnchorRigBody.Build"/>, что у рига в игре.</summary>
        public int Capsules(BakeConfig c, float frame, out AnchorCapsule[] caps)
        {
            var s = _body.Skeleton(frame);
            if (!c.BodyLimbs) s = AnchorSkeleton.Empty(s.Root);
            AnchorRigBody.Build(s, 1f, _caps, out int n, _chainCaps, out _);
            caps = _caps;
            return n;
        }

        public int ChainCapsules(float frame, out AnchorCapsule[] caps)
        {
            AnchorRigBody.Build(_body.Skeleton(frame), 1f, _caps, out _, _chainCaps, out int n);
            caps = _chainCaps;
            return n;
        }

        Func<Vector3, float> GroundOf(BakeConfig c) { float g = c.Ground; return _ => g; }

        void Settle(BakeConfig c, AnchorRigCore core, float frame, float seconds)
        {
            Vector3 grip = _t.GripAt(frame);
            int n = Capsules(c, frame, out var caps);
            for (float s = 0; s < seconds; s += 1f / 120f) core.StepLive(1f / 120f, grip, grip, GroundOf(c), caps, n);
            core.Body.Velocity = core.Body.AngularVelocity = Vector3.Zero;
        }

        AnchorRigCore Initial(BakeConfig c)
        {
            var core = NewCore(c);
            float f0 = FrameAt(c, 0);
            Vector3 grip = _t.GripAt(f0);
            if (c.StartState is Rec s)
            {
                core.Teleport(AnchorRigMode.InHandLive, new AnchorPose(s.P, s.Q, s.V, s.W));
                return core;
            }
            if (c.Start == "back")
            {
                // Head on the back mount (PelagAppearance: Spine2 local (0.008; -0.031; -0.075), Euler(0, 0, -25), bone unit
                // = WoleScale), released at frame 0: the rig's OnBack -> Draw, torso ignored until the head leaves it.
                Quaternion spine = _t.Spine2Q[0];
                Vector3 centre = _body.Spine2At(0) + Vector3.Transform(new Vector3(.008f, -.031f, -.075f) * (float)_t.BoneUnitMetres, spine);
                core.Teleport(AnchorRigMode.OnBack, AnchorPose.At(centre, spine * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -25f * (float)Math.PI / 180f)));
                core.EnterLive(AnchorRigMode.Draw, grip);
                return core;
            }
            if (c.Start.StartsWith("taut:"))
            {
                // Best case for a swing: the head lies on its flat side on the ground, chain almost straight (98 % L),
                // in the horizontal direction `deg` from +z (positive toward +x) as seen from the grip.
                float deg = float.Parse(c.Start.Substring(5), System.Globalization.CultureInfo.InvariantCulture) * (float)Math.PI / 180f;
                Vector3 dir = new Vector3((float)Math.Sin(deg), 0, (float)Math.Cos(deg));
                float ringY = c.Ground + .12f, reach = .98f * c.ChainLength;
                float d = (float)Math.Sqrt(Math.Max(0, reach * reach - (grip.Y - ringY) * (grip.Y - ringY)));
                Vector3 ring = new Vector3(grip.X, ringY, grip.Z) + dir * d;
                Vector3 y = -dir, z = Vector3.UnitY, x = Vector3.Cross(y, z);
                var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
                Quaternion q = Quaternion.CreateFromRotationMatrix(basis);
                core.Teleport(AnchorRigMode.InHandLive, AnchorPose.At(ring - Vector3.Transform(_head.EyeLocal, q), q));
                Settle(c, core, f0, .5f);
                return core;
            }
            // "rest": the head settles on the chain under the still grip (a 1.6 m chain from a hand at ~0.85 m cannot
            // hang free - the head rests on the ground, the chain slack). Loops start from rest and run in.
            core.Teleport(AnchorRigMode.InHandLive, AnchorPose.At(new Vector3(grip.X - .15f, c.Ground + .35f, grip.Z - .35f),
                Quaternion.CreateFromYawPitchRoll(.4f, .3f, .2f)));
            Settle(c, core, f0, c.PreRoll);
            return core;
        }

        public BakeRun Run(BakeConfig c)
        {
            var run = new BakeRun { Cfg = c, ContactTime = c.ContactFrame / (_t.Fps * c.ClipRate) };
            var core = Initial(c);
            float frameDt = 1f / (_t.Fps * c.RecordPerTick);          // 120 Hz frames: exactly 2 substeps of 1/240 s
            float duration = Duration(c), recordFrom = c.Loop ? duration - Period(c) : 0f;
            int steps = (int)Math.Round(duration / frameDt);
            float guideStart = run.ContactTime - c.GuideTicks / (_t.Fps * c.ClipRate), guideSpan = run.ContactTime - guideStart;
            var ground = GroundOf(c);
            Vector3 prevGrip = _t.GripAt(FrameAt(c, 0));
            long ticks = 0;
            int bounces0 = core.Bounces;
            if (!c.Loop) Record(run, core, 0, FrameAt(c, 0), prevGrip, Vector3.Zero, false, recordFrom);
            for (int i = 1; i <= steps; i++)
            {
                float t = i * frameDt, f = FrameAt(c, t);
                Vector3 grip = _t.GripAt(f), gripV = (grip - prevGrip) / frameDt;
                bool guided = false;
                if (c.GuideAccel != Vector3.Zero && t > guideStart && t <= run.ContactTime + 1e-6f)
                {
                    float u = (t - .5f * frameDt - guideStart) / guideSpan, w = 30 * u * u * (1 - u) * (1 - u);
                    core.Body.Velocity += c.GuideAccel * (w * frameDt);   // before the step: the chain still binds it
                    guided = true;
                }
                int n = Capsules(c, f, out var caps);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                core.StepLive(frameDt, prevGrip, grip, ground, caps, n);
                ticks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                if (core.LastBodyPush > 1e-4f) { run.BodyContacts++; run.MaxBodyPush = Math.Max(run.MaxBodyPush, core.LastBodyPush); }
                prevGrip = grip;
                if (t >= recordFrom - 1e-5f) Record(run, core, t, f, grip, gripV, guided, recordFrom);
            }
            run.Bounces = core.Bounces - bounces0;
            run.MillisecondsPerFrame60 = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, steps) * 2;
            return run;
        }

        void Record(BakeRun run, AnchorRigCore core, float t, float f, Vector3 grip, Vector3 gripV, bool guided, float recordFrom)
        {
            var h = core.Body;
            Vector3 r = h.Eye - h.Position;
            float span = Vector3.Distance(h.Eye, grip), local = t - recordFrom;
            run.Records.Add(new Rec
            {
                T = local, Tick = local * _t.Fps * run.Cfg.ClipRate, Frame = f,
                P = h.Position, Q = h.Rotation, V = h.Velocity, W = h.AngularVelocity,
                Ring = h.Eye, RingV = h.Velocity + Vector3.Cross(h.AngularVelocity, r), Grip = grip, GripV = gripV,
                GripQ = _t.GripQAt(f), Support = _t.SupportAt(f), Tension = h.LastTension, Span = span, BodyPush = core.LastBodyPush,
                Taut = span >= run.Cfg.ChainLength - .01f, Grounded = h.Grounded, Guided = guided, Settling = core.IgnoreTorso,
            });
        }

        /// <summary>Contact error: distance from the head centre to the valid swing region (DESIGN 0.1 / SPEC 2.6):
        /// radius 2.2-2.7 m from the root, height 0.5-0.9 m, within +-10 deg of Direction (+z).</summary>
        public static Vector3 NearestValid(Vector3 p, float rMin = 2.2f, float rMax = 2.7f, float yMin = .5f, float yMax = .9f, float maxDeg = 10f)
        {
            float r = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
            float ang = (float)Math.Atan2(p.X, p.Z), lim = maxDeg * (float)Math.PI / 180f;
            ang = Math.Clamp(ang, -lim, lim);
            r = Math.Clamp(r, rMin, rMax);
            return new Vector3(r * (float)Math.Sin(ang), Math.Clamp(p.Y, yMin, yMax), r * (float)Math.Cos(ang));
        }

        /// <summary>Slam target: the Sim point on the ground; the head centre only has to be over it (y free).</summary>
        public static Vector3 NearestImpact(Vector3 p, Vector3 impact) => new Vector3(impact.X, p.Y, impact.Z);

        public static Rec At(BakeRun run, float time)
        {
            var list = run.Records;
            for (int i = 1; i < list.Count; i++)
                if (list[i].T >= time - 1e-5f)
                {
                    var a = list[i - 1]; var b = list[i];
                    float u = Math.Clamp((time - a.T) / Math.Max(1e-6f, b.T - a.T), 0, 1);
                    var x = u < .5f ? a : b;
                    x.T = time; x.P = Vector3.Lerp(a.P, b.P, u); x.V = Vector3.Lerp(a.V, b.V, u);
                    x.Ring = Vector3.Lerp(a.Ring, b.Ring, u); x.Grip = Vector3.Lerp(a.Grip, b.Grip, u);
                    x.Q = Quaternion.Slerp(a.Q, b.Q, u); x.Span = a.Span + (b.Span - a.Span) * u;
                    x.Frame = a.Frame + (b.Frame - a.Frame) * u; x.Tick = a.Tick + (b.Tick - a.Tick) * u;
                    return x;
                }
            return list[list.Count - 1];
        }

        /// <summary>Shooting for the guidance acceleration (DESIGN 3.3 p.2): a = A * 30u^2(1-u)^2 over the last
        /// GuideTicks, constant direction; Δp at contact = A T^2 / 2, so A += 2 Δ / T^2 each pass.</summary>
        public BakeRun Guide(BakeConfig c, int passes, out Vector3 accel)
        {
            accel = Vector3.Zero;
            float T = c.GuideTicks / (_t.Fps * c.ClipRate);
            BakeRun run = Run(c);
            for (int k = 0; k < passes; k++)
            {
                var at = At(run, run.ContactTime);
                Vector3 goal = BakeKind.Ground(c.Kind) ? NearestImpact(at.P, c.Impact) : NearestValid(at.P);
                Vector3 miss = goal - at.P;
                if (miss.Length() < .01f) break;
                accel += 2 * miss / (T * T);
                c.GuideAccel = accel;
                run = Run(c);
            }
            return run;
        }
    }
}
