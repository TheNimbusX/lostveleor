using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using AnchorBake;

namespace Wreck4Anchor
{
    /// <summary>Тело серии (клипы Wreck4 подряд, ход и поворот корня) — только чтение: хват, ось рукояти, кулаки, грудь, спина.
    /// Оси Unity: x вправо, y вверх, z вперёд, метры. Те же правила корня, что у wreck4sim (root_fwd трека, yaw по плану).</summary>
    public sealed class Timeline
    {
        public sealed class Seg
        {
            public GripTrack T; public Body B; public float[] Fw; public Vector3[] RF, LF;
            public string Clip; public float From, To, Yaw, Turn, Start, BaseYaw; public Vector3 Base;
        }

        public readonly List<Seg> Segs = new List<Seg>();
        public float Duration;
        int _spine2 = -1;

        public Timeline(JsonElement plan)
        {
            float t0 = 0, prevYaw = 0;
            var cache = new Dictionary<string, Seg>();
            foreach (var s in plan.GetProperty("segments").EnumerateArray())
            {
                string path = s.GetProperty("track").GetString();
                if (!cache.TryGetValue(path, out var proto))
                {
                    var tr = GripTrack.Load(path);
                    proto = new Seg { T = tr, B = new Body(tr) };
                    Load(path, proto);
                    cache[path] = proto;
                }
                var seg = new Seg { T = proto.T, B = proto.B, Fw = proto.Fw, RF = proto.RF, LF = proto.LF, Clip = proto.T.Clip,
                                    From = F(s, "from", 0), To = F(s, "to", proto.T.Frames), Yaw = F(s, "yaw", prevYaw), Turn = F(s, "turn", 0),
                                    Start = t0, BaseYaw = prevYaw };
                Segs.Add(seg); t0 += (seg.To - seg.From) / 30f; prevYaw = seg.Yaw;
            }
            Vector3 basePos = Vector3.Zero;
            foreach (var s in Segs) { s.Base = basePos; basePos = RootPos(s, s.To); }
            Duration = t0;
            _spine2 = Segs[0].T.Bone("Spine2");
        }

        static void Load(string path, Seg s)
        {
            var fw = new List<float>(); var rf = new List<Vector3>(); var lf = new List<Vector3>();
            foreach (var e in JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("samples").EnumerateArray())
            {
                Vector3 V3(string n) { var a = e.GetProperty(n); return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()); }
                fw.Add(e.TryGetProperty("root_fwd", out var v) ? v.GetSingle() : 0f);
                rf.Add(V3("rfist")); lf.Add(V3("lfist"));
            }
            s.Fw = fw.ToArray(); s.RF = rf.ToArray(); s.LF = lf.ToArray();
        }

        public static float F(JsonElement e, string name, float def) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetSingle() : def;

        /// <summary>Время T (с) по номеру отрезка и кадру клипа.</summary>
        public float TimeOf(int seg, float frame) => Segs[seg].Start + (frame - Segs[seg].From) / 30f;

        public Seg Find(float T, out float frame)
        {
            for (int i = 0; i < Segs.Count; i++)
            {
                var s = Segs[i]; float len = (s.To - s.From) / 30f;
                if (T <= s.Start + len + 1e-6f || i == Segs.Count - 1)
                {
                    frame = Math.Clamp(s.From + (T - s.Start) * 30f, s.From, s.To);
                    return s;
                }
            }
            throw new InvalidOperationException();
        }

        public int IndexOf(Seg s) => Segs.IndexOf(s);

        static float YawAt(Seg s, float frame)
        {
            if (s.Turn <= 0) return s.Yaw;
            float u = Math.Clamp((frame - s.From) / s.Turn, 0, 1); u = u * u * (3 - 2 * u);
            return s.BaseYaw + (s.Yaw - s.BaseYaw) * u;
        }

        static float Arr(float[] a, Seg s, float frame)
        {
            float x = Math.Clamp(frame * s.T.Sub, 0, a.Length - 1); int i = Math.Min((int)x, a.Length - 2);
            return a[i] + (a[i + 1] - a[i]) * (x - i);
        }

        static Vector3 Arr(Vector3[] a, Seg s, float frame)
        {
            float x = Math.Clamp(frame * s.T.Sub, 0, a.Length - 1); int i = Math.Min((int)x, a.Length - 2);
            return Vector3.Lerp(a[i], a[i + 1], x - i);
        }

        static Vector3 RootPos(Seg s, float frame)
            => s.Base + new Vector3(MathF.Sin(s.Yaw), 0, MathF.Cos(s.Yaw)) * (Arr(s.Fw, s, frame) - Arr(s.Fw, s, s.From));

        /// <summary>Корень, рысканье и поворот корня в момент T.</summary>
        public void Root(float T, out Vector3 root, out float yaw, out Quaternion qy)
        {
            var s = Find(T, out float f);
            yaw = YawAt(s, f); root = RootPos(s, f); qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        }

        Vector3 W(float T, Func<Seg, float, Vector3> local)
        {
            var s = Find(T, out float f);
            float yaw = YawAt(s, f);
            return RootPos(s, f) + Vector3.Transform(local(s, f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        }

        /// <summary>Точка выхода цепи из правого кулака (хват клипа).</summary>
        public Vector3 Grip(float T) => W(T, (s, f) => s.T.GripAt(f));
        public Vector3 RFist(float T) => W(T, (s, f) => Arr(s.RF, s, f));
        public Vector3 LFist(float T) => W(T, (s, f) => Arr(s.LF, s, f));
        public Vector3 Bone(float T, string name) => W(T, (s, f) => s.T.BoneAt(s.T.Bone(name), f));
        public Vector3 GripVelocity(float T) => (Grip(T + 1f / 240f) - Grip(T - 1f / 240f)) * 120f;

        /// <summary>Ось рукояти: от левого кулака (кисточка) к правому (выход цепи).</summary>
        public Vector3 Axis(float T) => Vector3.Normalize(RFist(T) - LFist(T));
        public float HandGap(float T) => Vector3.Distance(RFist(T), LFist(T));

        public Quaternion GripRot(float T)
        {
            var s = Find(T, out float f);
            return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, YawAt(s, f)) * s.T.GripQAt(f));
        }

        /// <summary>Поворот Spine2 в мире (оси Unity) — для крепления на спине.</summary>
        public Quaternion Spine2Rot(float T)
        {
            var s = Find(T, out float f);
            float x = Math.Clamp(f, 0, s.T.Frames) * s.T.Sub; int i = Math.Min((int)x, s.T.Spine2Q.Length - 2);
            var q = Quaternion.Slerp(s.T.Spine2Q[i], s.T.Spine2Q[i + 1], x - i);
            return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, YawAt(s, f)) * q);
        }

        public Vector3 Spine2(float T) => W(T, (s, f) => s.T.BoneAt(_spine2, f));

        /// <summary>Анатомические капсулы тела (проверка проникновения головы), в мире.</summary>
        public Capsule[] Anatomy(float T)
        {
            var s = Find(T, out float f);
            float yaw = YawAt(s, f); Vector3 root = RootPos(s, f); var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            var src = s.B.Anatomy(f); var dst = new Capsule[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = new Capsule(src[i].Name, root + Vector3.Transform(src[i].A, q), root + Vector3.Transform(src[i].B, q), src[i].R);
            return dst;
        }
    }
}
