using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnchorBake;
using Game.View;

namespace GripOpt3
{
    /// <summary>
    /// Stow = the game's own Stow (PelagAnchorRig.Modes.StowDirective), offline: live physics (AnchorRigCore.StepLive) from the
    /// window's last state; Hand — the clip's left socket ≤ 4 cm from the back handle mount (or 0,35 s → slide 0,22 s);
    /// Reel — handle on the back (slide 1 frame), torso ignored (HoldIgnoreTorso), chain reeled to |back grip − back ring| at
    /// 5 m/s, the head pulled by tension only; caught: ring ≤ 10 cm from the back ring and ≤ 2,5 m/s → OnBack blend (≤ 0,9 s).
    /// Back mount (PelagAppearance): handle point = Spine2 ⊗ TRS((.103,.144,−.105), E(−90,0,90), .55) · grip exit
    /// (= Spine2-local (0.231, 0.280, −0.169) m), head centre = Spine2-local (.008,−.031,−.075)·1.82, rotation Spine2·Rz(−25°).
    /// </summary>
    public sealed class StowModel
    {
        public readonly GripTrack Body; public readonly HeadModel Head; public readonly Rec Start;
        public Vector3 SeamVel;
        public int HandFrame = 3; public float VMax = 9f, AMax = 200f, ReachL = .56f;
        static readonly Vector3 GripLocal = new Vector3(.231f, .280f, -.169f);
        readonly Vector3 _centreLocal;
        public StowModel(GripTrack body, HeadModel head, Rec start)
        {
            Body = body; Head = head; Start = start;
            _centreLocal = new Vector3(.008f, -.031f, -.075f) * (float)body.BoneUnitMetres;
        }

        Quaternion Q2(float f)
        {
            float x = Math.Clamp(f, 0, Body.Frames) * Body.Sub; int i = Math.Min((int)x, Body.Spine2Q.Length - 2);
            return Quaternion.Slerp(Body.Spine2Q[i], Body.Spine2Q[i + 1], x - i);
        }
        Vector3 Sp2(float f) => Body.BoneAt(Array.IndexOf(Body.BoneNames, "Spine2"), Math.Clamp(f, 0, Body.Frames));
        public Vector3 BackGrip(float f) => Sp2(f) + Vector3.Transform(GripLocal, Q2(f));
        public Quaternion BackRot(float f) => Q2(f) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -25f * (float)Math.PI / 180f);
        public Vector3 BackCentre(float f) => Sp2(f) + Vector3.Transform(_centreLocal, Q2(f));
        public Vector3 BackRing(float f) => BackCentre(f) + Vector3.Transform(Head.EyeLocal, BackRot(f));

        public sealed class Run
        {
            public List<(float t, Vector3 p, Quaternion q, Vector3 v, Vector3 ring, Vector3 grip, float span, bool ground, int step)> S = new();
            public float HandT = -1, CatchT = -1, HandGap, CatchGap, CatchSpeed, Pen, Clear = 9, Jump; public bool Timeout, Caught;
            public string PenWhere = "", ClearWhere = ""; public double Cost; public Dictionary<string, double> Terms = new();
        }

        public Run Simulate(Vector3[] G, float tail = 1.2f)
        {
            var track = Body; var sim = new BakeSim(track, Head); var cfg = new BakeConfig { ChainLength = 1.6f, Mass = 12f };
            var core = sim.NewCore(cfg); core.Teleport(AnchorRigMode.InHandLive, new AnchorPose(Start.P, Start.Q, Start.V, Start.W));
            core.Relabel(AnchorRigMode.Stow);
            int n = G.Length - 1; float fdt = 1f / 120f; var r = new Run();
            Vector3 Hand(float f) { f = Math.Clamp(f, 0, n); int i = Math.Min((int)f, n - 1); return Vector3.Lerp(G[i], G[i + 1], f - i); }
            int step = 0; float tStep = 0, slideT = 0; Vector3 slideFrom = Vector3.Zero, prev = Hand(0);
            var probe = Head.NewBody(12f, cfg.Inertia); var skel = new AnchorBake.Body(track);
            for (int k = 0; k <= (int)((n / 30f + tail) / fdt); k++)
            {
                float t = k * fdt, f = Math.Min(t * 30, n);
                Vector3 hand = Hand(f), back = BackGrip(f), grip = hand;
                if (step == 0)
                {
                    float gap = Vector3.Distance(hand, back);
                    if (gap <= .04f || t >= .35f)
                    {
                        r.HandT = t; r.HandGap = gap; r.Timeout = gap > .04f; step = 1; tStep = t; slideFrom = hand; slideT = gap <= .04f ? 1f / 60f : .22f;
                        core.HoldIgnoreTorso = true; core.ReelTo(Vector3.Distance(back, BackRing(f)), 5f);
                    }
                }
                if (step >= 1)
                {
                    float u = Math.Clamp((t - tStep) / slideT, 0, 1);
                    grip = Vector3.Lerp(slideFrom, back, u * u * (3 - 2 * u));
                    Vector3 ring = core.Body.Eye, br = BackRing(f);
                    Vector3 tv = (BackCentre(Math.Min(f + .25f, n)) - BackCentre(f)) * 120f;
                    if (step == 1 && Vector3.Distance(ring, br) <= .10f && (core.Body.Velocity - tv).Length() <= 2.5f)
                    { step = 2; r.Caught = true; r.CatchT = t; r.CatchGap = Vector3.Distance(ring, br); r.CatchSpeed = (core.Body.Velocity - tv).Length(); }
                    if (step == 1 && t - tStep >= .9f) { step = 2; r.CatchT = t; r.CatchGap = Vector3.Distance(ring, br); }
                }
                if (k > 0)
                {
                    int nc = sim.Capsules(cfg, f, out var caps);
                    core.StepLive(fdt, prev, grip, _ => 0f, caps, nc);
                }
                prev = grip;
                var h = core.Body;
                r.S.Add((t, h.Position, h.Rotation, h.Velocity, h.Eye, grip, Vector3.Distance(h.Eye, grip), h.Grounded, step));
                // проверки: голова и натянутая цепь — в руки, голову, ноги (корпус в уборке не толкает по замыслу игры)
                var an = skel.Anatomy(f).Where(c => step == 0 || c.Name != "torso").ToArray();
                float pen = HeadContact.Penetration(probe, h.Position, h.Rotation, an, out string where);
                if (pen > r.Pen) { r.Pen = pen; r.PenWhere = where + " @t" + t.ToString("0.00"); }
                if (Vector3.Distance(h.Eye, grip) >= core.CableLength - .01f)
                {
                    Vector3 d = Vector3.Normalize(h.Eye - grip), from = grip + d * .15f;
                    foreach (var c in an.Where(c => step == 0 || !c.Name.StartsWith("R upper") && !c.Name.StartsWith("head")))
                    { float cl = c.SegmentClearance(from, h.Eye); if (cl < r.Clear) { r.Clear = cl; r.ClearWhere = c.Name + " @t" + t.ToString("0.00"); } }
                }
                if (step == 2) break;
            }
            var p60 = new List<Vector3>();
            for (int k = 0; k < r.S.Count; k += 2) p60.Add(r.S[k].p);
            for (int k = 1; k + 1 < p60.Count; k++) r.Jump = Math.Max(r.Jump, (p60[k + 1] - 2 * p60[k] + p60[k - 1]).Length());
            return r;
        }

        static double H(double x) => x > 0 ? x : 0;

        public Run Evaluate(Vector3[] G)
        {
            var r = Simulate(G); var T = r.Terms; int n = G.Length - 1;
            T["hand"] = 4000 * Math.Pow(H(Vector3.Distance(G[HandFrame], BackGrip(HandFrame)) - .025), 2) + (r.Timeout ? 50 : 0);
            T["catch"] = r.Caught ? 20 * (r.CatchT - r.HandT) : 40 + 100 * r.CatchGap;
            T["pen"] = 1500 * H(r.Pen - .005) + 1500 * H(-.01 - r.Clear);
            T["jump"] = 1000 * H(r.Jump - .13);
            double kin = 0, reach = 0, jerk = 0; int la = Array.IndexOf(Body.BoneNames, "LeftArm"), hd = Array.IndexOf(Body.BoneNames, "Head");
            for (int f = 0; f < HandFrame; f++)
            {
                kin += 2 * Math.Pow(H((G[f + 1] - G[f]).Length() * 30 - VMax), 2);
                Vector3 pv = f == 0 ? G[0] - SeamVel : G[f - 1];
                kin += .002 * Math.Pow(H((G[f + 1] - 2 * G[f] + pv).Length() * 900 - AMax), 2);
                if (f >= 1) jerk += (G[f + 1] - 2 * G[f] + pv).LengthSquared();
            }
            for (int f = 1; f <= HandFrame; f++)
            {
                var b = Body.Bones[f * Body.Sub];
                reach += 2000 * Math.Pow(H((G[f] - b[la]).Length() - ReachL), 2) + 800 * Math.Pow(H(.22 - (G[f] - b[hd]).Length()), 2);
            }
            T["kin"] = kin; T["reach"] = reach; T["jerk"] = 20 * jerk;
            r.Cost = T.Values.Sum();
            return r;
        }
    }
}
