using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace AnchorBake
{
    /// <summary>Step A output (anchor_grip_export.py): grip + body in Unity root space, `sub` samples per clip frame,
    /// sampled the way Unity plays the built clip. Between samples: linear (240 Hz is below 1 mm of chord error).</summary>
    public sealed class GripTrack
    {
        public string Clip, Path, Sha256, SourceFbx, SourceSha, Bind, Hand;
        public int Fps = 30, Sub, Frames;
        public double BoneUnitMetres, RestHeight;
        public string[] BoneNames;
        public Vector3[] Grip, Support;
        public Quaternion[] GripQ, Spine2Q;
        public Vector3[][] Bones;   // [sample][bone]
        public float Duration => Frames / (float)Fps;

        public static GripTrack Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var doc = JsonDocument.Parse(bytes);
            var r = doc.RootElement;
            var t = new GripTrack { Path = path, Sha256 = Hash.Sha256(bytes) };
            t.Clip = r.GetProperty("clip").GetString();
            t.Hand = r.GetProperty("hand").GetString();
            t.Sub = r.GetProperty("sub").GetInt32();
            t.Frames = r.GetProperty("frames").GetInt32();
            var src = r.GetProperty("source");
            t.SourceFbx = src.GetProperty("fbx").GetString();
            t.SourceSha = src.GetProperty("sha256").GetString();
            t.Bind = src.GetProperty("bind").GetString();
            var units = r.GetProperty("units");
            t.BoneUnitMetres = units.GetProperty("boneUnitMetres").GetDouble();
            t.RestHeight = units.GetProperty("restHeight").GetDouble();
            var names = new List<string>();
            foreach (var n in r.GetProperty("boneNames").EnumerateArray()) names.Add(n.GetString());
            t.BoneNames = names.ToArray();
            var samples = r.GetProperty("samples");
            int count = samples.GetArrayLength();
            t.Grip = new Vector3[count]; t.Support = new Vector3[count];
            t.GripQ = new Quaternion[count]; t.Spine2Q = new Quaternion[count];
            t.Bones = new Vector3[count][];
            int i = 0;
            foreach (var s in samples.EnumerateArray())
            {
                t.Grip[i] = V(s.GetProperty("grip"));
                t.Support[i] = V(s.GetProperty("support"));
                t.GripQ[i] = Q(s.GetProperty("gripQ"));
                t.Spine2Q[i] = Q(s.GetProperty("spine2Q"));
                var b = s.GetProperty("bones");
                t.Bones[i] = new Vector3[t.BoneNames.Length];
                int j = 0;
                foreach (var p in b.EnumerateArray()) t.Bones[i][j++] = V(p);
                i++;
            }
            if (count != t.Frames * t.Sub + 1) throw new InvalidDataException("sample count does not match frames*sub+1");
            return t;
        }

        static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        static Quaternion Q(JsonElement e) => new Quaternion(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

        public int Bone(string name)
        {
            int i = Array.IndexOf(BoneNames, name);
            if (i < 0) throw new KeyNotFoundException(name);
            return i;
        }

        void Locate(float frame, out int i, out float u)
        {
            float x = Math.Clamp(frame, 0, Frames) * Sub;
            i = Math.Min((int)Math.Floor(x), Grip.Length - 2);
            u = x - i;
        }

        public Vector3 GripAt(float frame) { Locate(frame, out int i, out float u); return Vector3.Lerp(Grip[i], Grip[i + 1], u); }
        public Vector3 SupportAt(float frame) { Locate(frame, out int i, out float u); return Vector3.Lerp(Support[i], Support[i + 1], u); }
        public Quaternion GripQAt(float frame) { Locate(frame, out int i, out float u); return Quaternion.Slerp(GripQ[i], GripQ[i + 1], u); }
        public Vector3 BoneAt(int bone, float frame)
        {
            Locate(frame, out int i, out float u);
            return Vector3.Lerp(Bones[i][bone], Bones[i + 1][bone], u);
        }

        /// <summary>Grip velocity in m/s (central difference over one sample of the clip clock), times the clip rate.</summary>
        public Vector3 GripVelocity(float frame, float clipRate)
        {
            float h = 1f / Sub;
            float a = Math.Max(0, frame - h), b = Math.Min(Frames, frame + h);
            if (b <= a) return Vector3.Zero;
            return (GripAt(b) - GripAt(a)) / ((b - a) / Fps) * clipRate;
        }
    }

    /// <summary>Body capsules from bone positions (Unity root space). Radii: DESIGN 1.5 for the physics set,
    /// anatomical set for validation (what the eye reads as "through the body").</summary>
    public readonly struct Capsule
    {
        public readonly Vector3 A, B; public readonly float R; public readonly string Name;
        public Capsule(string name, Vector3 a, Vector3 b, float r) { Name = name; A = a; B = b; R = r; }
        public float Depth(Vector3 p, out Vector3 normal)
        {
            Vector3 ab = B - A;
            float u = Math.Clamp(Vector3.Dot(p - A, ab) / Math.Max(1e-9f, ab.LengthSquared()), 0, 1);
            Vector3 d = p - (A + ab * u);
            float len = d.Length();
            normal = len > 1e-6f ? d / len : Vector3.UnitX;
            return R - len;
        }
        /// <summary>Closest distance between this capsule's axis and segment pq (minus radius = clearance).</summary>
        public float SegmentClearance(Vector3 p, Vector3 q)
        {
            float best = float.MaxValue;
            for (int k = 0; k <= 24; k++)
            {
                Vector3 x = Vector3.Lerp(p, q, k / 24f);
                best = Math.Min(best, -Depth(x, out _));
            }
            return best;
        }
    }

    /// <summary>Bones of the grip track in Unity root space. Physics capsules come from <see cref="Game.View.AnchorRigBody"/>
    /// (the same builder the rig uses in the game); <see cref="Anatomy"/> is the stricter set for validation only.</summary>
    public sealed class Body
    {
        readonly GripTrack _t;
        readonly int hips = -1, spine2 = -1, neck = -1, head = -1, top = -1, lArm = -1, lFore = -1, lHand = -1, rArm = -1, rFore = -1, rHand = -1,
            lUp = -1, lLeg = -1, lFoot = -1, rUp = -1, rLeg = -1, rFoot = -1, lToe = -1, rToe = -1;
        public bool HasBones { get; }
        public Body(GripTrack t)
        {
            _t = t;
            HasBones = t.BoneNames != null && t.BoneNames.Length > 0 && Array.IndexOf(t.BoneNames, "Hips") >= 0;
            if (!HasBones) return;
            hips = t.Bone("Hips"); spine2 = t.Bone("Spine2"); neck = t.Bone("Neck"); head = t.Bone("Head"); top = t.Bone("HeadTop_End");
            lArm = t.Bone("LeftArm"); lFore = t.Bone("LeftForeArm"); lHand = t.Bone("LeftHand");
            rArm = t.Bone("RightArm"); rFore = t.Bone("RightForeArm"); rHand = t.Bone("RightHand");
            lUp = t.Bone("LeftUpLeg"); lLeg = t.Bone("LeftLeg"); lFoot = t.Bone("LeftFoot"); lToe = t.Bone("LeftToeBase");
            rUp = t.Bone("RightUpLeg"); rLeg = t.Bone("RightLeg"); rFoot = t.Bone("RightFoot"); rToe = t.Bone("RightToeBase");
        }
        Vector3 P(int b, float f) => b < 0 ? Game.View.AnchorSkeleton.Missing : _t.BoneAt(b, f);
        public Vector3 HipsAt(float f) => HasBones ? P(hips, f) : new Vector3(0, .91f, 0);
        public Vector3 Spine2At(float f) => HasBones ? P(spine2, f) : new Vector3(0, 1.28f, 0);
        public Vector3 NeckAt(float f) => HasBones ? P(neck, f) : new Vector3(0, 1.5f, 0);
        public Vector3 HeadTopAt(float f) => HasBones ? P(top, f) : new Vector3(0, 1.82f, 0);

        /// <summary>Skeleton for <see cref="Game.View.AnchorRigBody.Build"/>: root at the origin of the clip (no root motion).</summary>
        public Game.View.AnchorSkeleton Skeleton(float f)
        {
            if (!HasBones) return Game.View.AnchorSkeleton.Empty(Vector3.Zero);
            return new Game.View.AnchorSkeleton
            {
                Root = Vector3.Zero, Hips = P(hips, f), Spine2 = P(spine2, f), Head = P(head, f),
                LUpLeg = P(lUp, f), LLeg = P(lLeg, f), LFoot = P(lFoot, f), RUpLeg = P(rUp, f), RLeg = P(rLeg, f), RFoot = P(rFoot, f),
                LArm = P(lArm, f), LForeArm = P(lFore, f), LHand = P(lHand, f), RArm = P(rArm, f), RForeArm = P(rFore, f), RHand = P(rHand, f),
            };
        }

        /// <summary>Anatomical body for validation: penetration of the hull / taut chain into these.</summary>
        public Capsule[] Anatomy(float f) => !HasBones ? new[] { new Capsule("torso", new Vector3(0, .91f, 0), new Vector3(0, 1.5f, 0), .18f) } : new[]
        {
            new Capsule("torso", P(hips, f), P(neck, f), .18f), new Capsule("head", P(head, f), P(top, f), .11f),
            new Capsule("L thigh", P(lUp, f), P(lLeg, f), .09f), new Capsule("L shin", P(lLeg, f), P(lFoot, f), .065f),
            new Capsule("R thigh", P(rUp, f), P(rLeg, f), .09f), new Capsule("R shin", P(rLeg, f), P(rFoot, f), .065f),
            new Capsule("L foot", P(lFoot, f), P(lToe, f), .05f), new Capsule("R foot", P(rFoot, f), P(rToe, f), .05f),
            new Capsule("L upper arm", P(lArm, f), P(lFore, f), .06f), new Capsule("L forearm", P(lFore, f), P(lHand, f), .05f),
            new Capsule("R upper arm", P(rArm, f), P(rFore, f), .06f), new Capsule("R forearm", P(rFore, f), P(rHand, f), .05f),
        };
    }

    static class Hash
    {
        public static string Sha256(byte[] bytes)
        {
            using var h = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(h.ComputeHash(bytes)).ToLowerInvariant();
        }
    }
}
