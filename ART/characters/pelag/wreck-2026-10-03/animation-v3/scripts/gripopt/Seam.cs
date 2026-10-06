using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using AnchorBake;
using Game.View;

namespace GripOpt
{
    /// <summary>
    /// What the game rig shows on the first press (head on the back): Teleport(OnBack) at the back mount, then
    /// Drive(Baked) every 60 fps frame toward the bake with the rig's own AnchorBlend (accel limit 400 m/s², deadline contact − 1).
    /// Same calls as PelagAnchorRig.Apply → AnchorRigCore.Drive; no Unity.
    /// </summary>
    public static class Seam
    {
        public static int Run(string bakePath, GripTrack body, HeadModel head, bool deadline, string outPath = null)
        {
            var shown = new System.Text.Json.Nodes.JsonArray();
            using var doc = JsonDocument.Parse(File.ReadAllBytes(bakePath));
            var root = doc.RootElement;
            var s = root.GetProperty("samples");
            int n = s.GetArrayLength(); var t = new float[n]; var P = new Vector3[n]; var V = new Vector3[n]; var Q = new Quaternion[n];
            int i = 0;
            foreach (var x in s.EnumerateArray())
            {
                t[i] = x.GetProperty("t").GetSingle(); P[i] = V3(x.GetProperty("p")); V[i] = V3(x.GetProperty("v"));
                var q = x.GetProperty("q"); Q[i] = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle());
                i++;
            }
            float contact = root.GetProperty("contacts")[0].GetProperty("frame").GetSingle();
            var sk = new Body(body);
            Quaternion spine = body.Spine2Q[0];
            Vector3 centre = sk.Spine2At(0) + Vector3.Transform(new Vector3(.008f, -.031f, -.075f) * (float)body.BoneUnitMetres, spine);
            var core = new AnchorRigCore(new AnchorRigSettings());
            core.Body.Hull = head.HullLocal; core.Body.EyeLocal = head.EyeLocal; core.Body.Mass = 12f;
            core.Teleport(AnchorRigMode.OnBack, AnchorPose.At(centre, spine * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -25f * (float)Math.PI / 180f)));
            Console.WriteLine($"back mount centre ({centre.X:F2} {centre.Y:F2} {centre.Z:F2}), bake frame 0 ({P[0].X:F2} {P[0].Y:F2} {P[0].Z:F2}), seam {(P[0] - centre).Length():F2} m");
            float maxAcc = 0, last = t[n - 1];
            for (int k = 0; k <= (int)(last * 2); k++)
            {
                float f = k / 2f;
                var target = Sample(t, P, Q, V, f);
                float dl = deadline && f < contact - 1 ? (contact - 1 - f) / 30f : -1f;
                core.Drive(AnchorRigMode.Baked, 1, target, 1f / 60f, dl);
                maxAcc = Math.Max(maxAcc, core.CorrectionAccel);
                var o = core.Output;
                shown.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["t"] = f, ["p"] = new System.Text.Json.Nodes.JsonArray(o.Position.X, o.Position.Y, o.Position.Z),
                    ["q"] = new System.Text.Json.Nodes.JsonArray(o.Rotation.X, o.Rotation.Y, o.Rotation.Z, o.Rotation.W),
                    ["offset"] = core.BlendError, ["accel"] = core.CorrectionAccel,
                });
                if (k % 2 == 0 || Math.Abs(f - contact) < .01f)
                    Console.WriteLine($"  f{f,5:0.0} shown ({o.Position.X:F2} {o.Position.Y:F2} {o.Position.Z:F2}) |v| {o.Velocity.Length():F1} offset {core.BlendError:F3} accel {core.CorrectionAccel:F0} residual {core.DeadlineResidual:F3}");
            }
            Console.WriteLine($"SEAM max correction accel {maxAcc:F0} m/s²");
            if (outPath != null)
                File.WriteAllText(outPath, new System.Text.Json.Nodes.JsonObject
                {
                    ["note"] = "game rig first press: OnBack at the back mount -> Drive(Baked) per 60 fps frame (AnchorBlend, 400 m/s2, deadline contact-1); Unity root axes",
                    ["samples"] = shown,
                }.ToJsonString());
            return 0;
        }

        static AnchorPose Sample(float[] t, Vector3[] P, Quaternion[] Q, Vector3[] V, float f)
        {
            int i = 1; while (i < t.Length - 1 && t[i] < f) i++;
            float u = Math.Clamp((f - t[i - 1]) / Math.Max(1e-6f, t[i] - t[i - 1]), 0, 1);
            return new AnchorPose(Vector3.Lerp(P[i - 1], P[i], u), Quaternion.Slerp(Q[i - 1], Q[i], u), Vector3.Lerp(V[i - 1], V[i], u), Vector3.Zero);
        }

        static Vector3 V3(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
    }
}
