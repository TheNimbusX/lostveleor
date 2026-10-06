using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using Game.View;

namespace AnchorBake
{
    /// <summary>The single anchor head (DESIGN 0.1): Tripo head, 68-point hull, ring 0.38 m above the centre.</summary>
    public sealed class HeadModel
    {
        public Vector3[] HullLocal;          // metres, head local (Unity), about the centre of mass
        public Vector3 EyeLocal;
        public float Scale;
        public string HullSource, HullSha;

        public static HeadModel Load(string assetPath, float scale, Vector3 eyeUnscaled)
        {
            var text = File.ReadAllText(assetPath);
            var rx = new Regex(@"\{x:\s*(-?[0-9.eE+-]+),\s*y:\s*(-?[0-9.eE+-]+),\s*z:\s*(-?[0-9.eE+-]+)\}");
            var pts = new List<Vector3>();
            foreach (Match m in rx.Matches(text))
                pts.Add(new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value)) * scale);
            if (pts.Count < 8) throw new InvalidDataException("hull asset has no points: " + assetPath);
            // Mesh pivot = centre of the bounds (head.json: bounds -0.5..0.5 on the long axis), so the centre is 0.
            return new HeadModel
            {
                HullLocal = pts.ToArray(), EyeLocal = eyeUnscaled * scale, Scale = scale, HullSource = assetPath,
                HullSha = Hash.Sha256(File.ReadAllBytes(assetPath)),
            };
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        public AnchorHeadDynamics NewBody(float mass, Vector3 inertia)
            => new AnchorHeadDynamics { Hull = HullLocal, EyeLocal = EyeLocal, Mass = mass, Inertia = inertia };
    }

    /// <summary>Validation only: how deep the hull sits in the anatomical capsules (physics contact is AnchorRigBody.PushHull).</summary>
    public static class HeadContact
    {
        public static float Penetration(AnchorHeadDynamics h, Vector3 position, Quaternion rotation, Capsule[] body, out string where)
        {
            float worst = 0; where = "";
            foreach (var local in h.Hull)
            {
                Vector3 p = position + Vector3.Transform(local, rotation);
                foreach (var c in body)
                {
                    float d = c.Depth(p, out _);
                    if (d > worst) { worst = d; where = c.Name; }
                }
            }
            return worst;
        }
    }
}
