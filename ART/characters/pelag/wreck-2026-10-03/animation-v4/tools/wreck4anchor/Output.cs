using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using AnchorBake;
using Game.View;

namespace Wreck4Anchor
{
    public sealed partial class Sim
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;
        public float BiteTop = -1f, BiteFirst = -1f; public Quaternion ImpactRot = Quaternion.Identity;
        AnchorHeadDynamics _probe;

        public static string N_(float v) => v.ToString("0.#####", I);
        public static string V_(Vector3 v) => $"[{N_(v.X)}, {N_(v.Y)}, {N_(v.Z)}]";
        static float Ang(Vector3 a, Vector3 b) => a.Length() < 1e-4f || b.Length() < 1e-4f ? 0
            : MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1, 1)) * 57.2958f;

        /// <summary>ФИЗИКА: ядро игры на полёт и землю — та же голова (оболочка, кольцо, масса 12 кг), цепь длиной chain.</summary>
        AnchorRigCore NewCore(float chain, float bounce = .18f)
        {
            var c = new AnchorRigCore(new AnchorRigSettings { ChainLength = chain, MinReelSpeed = 9f, BounceRestitution = bounce });
            c.Body.Hull = Head.HullLocal; c.Body.EyeLocal = Head.EyeLocal; c.Body.Mass = 12f; c.Body.Inertia = new Vector3(.75f, .55f, .75f);
            return c;
        }

        void Record(float T, string mode, Vector3 p, Vector3 v, Quaternion rot, Vector3 E, float cable, AnchorRigCore core)
        {
            Quaternion qc = rot;
            if (WhipOn) { rot = _qd; cable = _L; }                   // показ: вторичный поворот, длина цепи-хлыста
            Vector3 ring = p + Vector3.Transform(Head.EyeLocal, rot);
            Handle(T, out var h0, out var h1);
            float pen = 0; string where = "";
            if (mode != "OnBack" && mode != "Settle")
            {
                _probe ??= Head.NewBody(12f, new Vector3(.75f, .55f, .75f));
                pen = HeadContact.Penetration(_probe, p, rot, TL.Anatomy(T), out where);
            }
            float span = Vector3.Distance(ring, E);
            Frames.Add(new Frame { T = T, Mode = mode, P = p, V = v, Rot = rot, Ring = ring, Grip = E, Cable = Math.Max(cable, span), Span = span,
                                   Speed = v.Length(), H0 = h0, H1 = h1, Pen = pen, PenWhere = where,
                                   Grounded = core != null && (mode == "Flight" || mode == "Bite") && core.Body.Grounded,
                                   Qc = qc, Chain = WhipOn ? ChainSnapshot() : null, Bow = WhipOn ? _chain.Bow() : 0,
                                   Strain = WhipOn ? _chain.MaxStrain : 0, CPen = WhipOn ? _chain.MaxPen : 0, Dev = WhipOn ? _sec.LastDev * 57.2958f : 0 });
        }

        public string Json(string contactsJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"whip\": " + (WhipOn ? "true" : "false") + ", \"chainSwing\": " + N_(L) + ", \"fps\": 60, \"duration\": " + N_(TEnd) + ", \"contacts\": " + contactsJson + ", \"events\": {");
            int k = 0;
            foreach (var e in Events) sb.Append((k++ > 0 ? ", " : "") + "\"" + e.Key + "\": " + e.Value);
            sb.Append("}, \"frames\": [");
            for (int i = 0; i < Frames.Count; i++)
            {
                var f = Frames[i];
                var sp = TL.Find(f.T, out float fr);
                TL.Root(f.T, out var root, out float yaw, out _);
                if (i > 0) sb.Append(",\n");
                sb.Append($"{{\"t\": {N_(f.T)}, \"seg\": {TL.IndexOf(sp)}, \"clip\": \"{sp.Clip}\", \"frame\": {N_(fr)}, \"root\": {V_(root)}, \"yaw\": {N_(yaw)}, " +
                          $"\"p\": {V_(f.P)}, \"q\": [{N_(f.Rot.X)}, {N_(f.Rot.Y)}, {N_(f.Rot.Z)}, {N_(f.Rot.W)}], \"v\": {N_(f.Speed)}, " +
                          $"\"ring\": {V_(f.Ring)}, \"grip\": {V_(f.Grip)}, \"cable\": {N_(f.Cable)}, \"span\": {N_(f.Span)}, \"mode\": \"{f.Mode}\", " +
                          $"\"h0\": {V_(f.H0)}, \"h1\": {V_(f.H1)}, \"pen\": {N_(f.Pen)}, \"grounded\": {(f.Grounded ? "true" : "false")}");
                if (f.Chain != null)
                {
                    sb.Append($", \"qc\": [{N_(f.Qc.X)}, {N_(f.Qc.Y)}, {N_(f.Qc.Z)}, {N_(f.Qc.W)}], \"bow\": {N_(f.Bow)}, \"strain\": {N_(f.Strain)}, \"cpen\": {N_(f.CPen)}, \"dev\": {N_(f.Dev)}, \"chain\": [");
                    for (int c = 0; c < f.Chain.Length; c++) sb.Append((c > 0 ? ", " : "") + V_(f.Chain[c]));
                    sb.Append("]");
                }
                sb.Append("}");
            }
            return sb.Append("]}").ToString();
        }
    }
}
