using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AnchorBake;
using Game.View;

namespace Wreck4Sim
{
    /// <summary>
    /// wreck4sim &lt;plan.json&gt; &lt;out.json&gt; — серия клипов Wreck4 подряд (оси Unity: x вправо, y вверх, z вперёд, м).
    /// Махи: живая физика игры (AnchorRigCore.StepLive) на короткой цепи от точки выхода цепи из правого кулака.
    /// Выпад: до выпуска — та же физика; выпуск → ведомый бросок (Drive Thrown: кривая Эрмита от положения и скорости
    /// головы к точке удара в 2,2 м, приход в тик контакта), цепь выдаётся натянутой; удержание в воронке; рывок —
    /// снова живая физика (EnterLive Caught: цепь выбирается рукой до короткой). Поворот корня между ударами — плавный.
    /// </summary>
    public static class Program
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        sealed class Seg { public GripTrack T; public Body B; public float[] Fw; public Vector3[] Ax; public string Clip; public float From, To, Yaw, Turn, Start; public Vector3 Base; public float BaseYaw; }

        public static int Main(string[] args)
        {
            var plan = JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
            string outPath = args[1];
            string repo = plan.GetProperty("repo").GetString();
            var head = HeadModel.Load(Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"), .94f, new Vector3(0, .405f, .01f));
            float Ls = F(plan, "chainSwing", .55f), reelSpeed = F(plan, "reelSpeed", 9f);
            var segs = new List<Seg>();
            var cache = new Dictionary<string, GripTrack>(); var fwCache = new Dictionary<string, float[]>(); var axCache = new Dictionary<string, Vector3[]>();
            float t0 = 0; Vector3 basePos = Vector3.Zero; float prevYaw = 0;
            foreach (var s in plan.GetProperty("segments").EnumerateArray())
            {
                string path = s.GetProperty("track").GetString();
                if (!cache.TryGetValue(path, out var tr)) { cache[path] = tr = GripTrack.Load(path); fwCache[path] = LoadFwd(path); axCache[path] = LoadAxis(path); }
                var seg = new Seg { T = tr, B = new Body(tr), Fw = fwCache[path], Ax = axCache[path], Clip = tr.Clip, From = F(s, "from", 0), To = F(s, "to", tr.Frames),
                                    Yaw = F(s, "yaw", prevYaw), Turn = F(s, "turn", 0), Start = t0, BaseYaw = prevYaw };
                segs.Add(seg); t0 += (seg.To - seg.From) / 30f; prevYaw = seg.Yaw;
            }
            // Основание корня каждого отрезка: конец прошлого (ход выпада вперёд по взгляду).
            for (int i = 0; i < segs.Count; i++)
            {
                segs[i].Base = basePos;
                basePos = RootPos(segs[i], segs[i].To);
            }
            float total = t0 + F(plan, "tail", 0f);          // хвост: последний кадр стоит (голова доходит до крепления)
            var caps = new AnchorCapsule[AnchorRigBody.Capacity]; var chainCaps = new AnchorCapsule[AnchorRigBody.Capacity];
            Func<Vector3, float> ground = _ => 0f;
            JsonElement Opt(string name) => plan.TryGetProperty(name, out var e) ? e : default;
            float At(JsonElement e, string name, float def)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return def;
                var sg = segs[e.GetProperty("seg").GetInt32()];
                return sg.Start + (v.GetSingle() - sg.From) / 30f;
            }
            JsonElement lunge = Opt("lunge"), draw = Opt("draw"), stow = Opt("stow");
            float tRel = At(lunge, "release", -1), tCon = At(lunge, "contact", -1), tHold = At(lunge, "hold", -1), reach = F(lunge, "reach", 2.2f);
            float tGrab = At(draw, "grab", -1), tDrawEnd = At(draw, "end", -1), tReel = At(stow, "reel", -1), tMount = At(stow, "mount", -1);
            // Контакты махов: голова (центр) в точке перед героем (вперёд, вбок, высота — оси героя в тик контакта).
            var hits = new List<(float T, Vector3 Target)>(); var zones = new List<(Vector3 Root, float Yaw, float[] Z)>();
            if (Opt("contacts").ValueKind == JsonValueKind.Array)
            foreach (var c in Opt("contacts").EnumerateArray())
            {
                float T = At(c, "frame", 0);
                Sample(segs, T, out _, out var r, out float y, out _);
                hits.Add((T, r + Vector3.Transform(new Vector3(F(c, "side", 0), F(c, "h", .85f), F(c, "fwd", 1.3f)), Quaternion.CreateFromAxisAngle(Vector3.UnitY, y))));
                // зона контакта (как NearestValid запечки): угол от взгляда ±deg, радиус от корня, высота центра головы
                zones.Add((r, y, new[] { F(c, "deg", 0), F(c, "rMin", 0), F(c, "rMax", 0), F(c, "hMin", 0), F(c, "hMax", 0) }));
            }
            float G = F(plan, "guideTicks", 3) / 30f;
            var follows = new List<(float A, float B, float Om)>();
            if (Opt("follow").ValueKind == JsonValueKind.Array)
                foreach (var w in Opt("follow").EnumerateArray()) follows.Add((At(w, "from", 0), At(w, "to", 0), F(w, "omega", 18)));
            float Follow(float T, out float om)
            {
                om = 0; float best = 0;
                foreach (var (a, b, o) in follows)
                {
                    float r = 1f / 30f, w = Math.Clamp((T - a) / r, 0, 1) * Math.Clamp((b - T) / r, 0, 1);
                    if (w > best) { best = w; om = o; }
                }
                return best;
            }
            int fps = (int)F(plan, "fps", 60), stepsPerOut = 120 / fps;

            string Run(Vector3[] A, Vector3[] got, out string ev, out float maxPush)
            {
                var core = new AnchorRigCore(new AnchorRigSettings { ChainLength = Ls, MinReelSpeed = reelSpeed });
                core.Body.Hull = head.HullLocal; core.Body.EyeLocal = head.EyeLocal; core.Body.Mass = F(plan, "mass", 12f);
                core.Body.Inertia = new Vector3(.75f, .55f, .75f);
                Sample(segs, 0, out var g0, out _, out _, out _);
                if (tGrab >= 0) core.Teleport(AnchorRigMode.OnBack, Mount(segs, 0));
                else
                {   // голова лежит под неподвижным хватом кадра 0 (как «rest» запечки)
                    core.Teleport(AnchorRigMode.InHandLive, AnchorPose.At(g0 + new Vector3(.25f, -.5f, .35f), Quaternion.CreateFromYawPitchRoll(.4f, .9f, .2f)));
                    for (float s = 0; s < F(plan, "preroll", 1.5f); s += 1f / 120f) core.StepLive(1f / 120f, g0, g0, ground, caps, Capsules(segs, 0, caps, chainCaps));
                    core.Body.Velocity = core.Body.AngularVelocity = Vector3.Zero;
                }
                AnchorPose rel = default, land = default, drawFrom = default; bool drawing = false, thrown = false, caught = false, grabbed = tGrab < 0, reeling = false, mounted = false;
                var outFrames = new StringBuilder("["); var events = new List<string>();
                Vector3 prevGrip = g0; int k = 0; maxPush = 0;
                for (int step = 0; step * (1f / 120f) <= total + 1e-5f; step++)
                {
                    float T = step / 120f;
                    Sample(segs, T, out var grip, out var root, out float yaw, out var sp);
                    int n = Capsules(segs, T, caps, chainCaps);
                    float wf = Follow(T, out float om);
                    if (core.Live && wf > 0)
                    {   // мах «как булава»: голова тянется к продолжению оси рукояти на длину цепи (крит. демпфирование ω), цепь и тело — физика
                        // цель чуть дальше длины цепи (followReach): тяга всегда наружу — цепь на скорости не провисает
                        float reachL = Ls + F(plan, "followReach", .12f);
                        Vector3 tp = MaceTarget(segs, T, reachL, head.EyeLocal.Y), tv = (MaceTarget(segs, T + 1f / 240f, reachL, head.EyeLocal.Y) - MaceTarget(segs, T - 1f / 240f, reachL, head.EyeLocal.Y)) * 120f;
                        Vector3 acc = wf * (om * om * (tp - core.Body.Position) + 2 * om * (tv - core.Body.Velocity));
                        float aMax = F(plan, "followMaxAccel", 300f);
                        if (acc.Length() > aMax) acc *= aMax / acc.Length();
                        core.Body.Velocity += acc / 120f;
                    }
                    for (int i = 0; i < hits.Count; i++)      // наведение (как Guide запечки): колокол 30u²(1−u)² за G до контакта
                        if (core.Live && T > hits[i].T - G && T <= hits[i].T + 1e-6f)
                        {
                            float u = (T - 1f / 240f - (hits[i].T - G)) / G;
                            core.Body.Velocity += A[i] * (30 * u * u * (1 - u) * (1 - u) / 120f);
                        }
                    if (!grabbed && T >= tGrab)
                    {
                        grabbed = true; events.Add($"\"grab\": {N(T)}");
                        if (tDrawEnd > tGrab) { drawFrom = core.Output; drawing = true; }   // снятие — ведомая дуга через правое плечо
                        else core.EnterLive(AnchorRigMode.Draw, grip);
                    }
                    if (drawing && T >= tDrawEnd) { drawing = false; core.EnterLive(AnchorRigMode.InHandLive, grip); events.Add($"\"drawn\": {N(T)}"); }
                    if (lSegOK(tRel) && !thrown && T >= tRel)
                    {
                        thrown = true; rel = core.Output;
                        Sample(segs, tCon, out _, out var rootC, out float yawC, out _);
                        land = Land(rootC, yawC, reach, F(lunge, "landPitch", 50), F(lunge, "bury", .03f), F(lunge, "landSpeed", 14f));
                        events.Add($"\"release\": {{\"t\": {N(T)}, \"p\": {V(rel.Position)}, \"v\": {V(rel.Velocity)}, \"speed\": {N(rel.Velocity.Length())}}}");
                    }
                    if (tReel >= 0 && !reeling && T >= tReel && core.Live)
                    {
                        reeling = true; core.Relabel(AnchorRigMode.Stow); core.HoldIgnoreTorso = true;
                        core.ReelTo(F(stow, "length", .3f), F(stow, "speed", 4f)); events.Add($"\"stow\": {N(T)}");
                    }
                    if (!grabbed) core.Drive(AnchorRigMode.OnBack, 0, Mount(segs, T), 1f / 120f);
                    else if (drawing)
                    {   // со спины вверх через правое плечо к продолжению рукояти в конце снятия (кубика Безье, скорость конца — для маха)
                        float D = tDrawEnd - tGrab, u = (T - tGrab) / D, a = 1 - u;
                        Sample(segs, tGrab, out _, out var rG, out float yG, out _);
                        var ch = Chest(segs, tGrab);
                        Vector3 p3 = MaceTarget(segs, tDrawEnd, Ls, head.EyeLocal.Y), v3 = (MaceTarget(segs, tDrawEnd, Ls, head.EyeLocal.Y) - MaceTarget(segs, tDrawEnd - 1f / 60f, Ls, head.EyeLocal.Y)) * 60f;
                        Vector3 c1 = drawFrom.Position + ch.Up * F(draw, "lift", .9f) + ch.Right * .15f, c2 = p3 - v3 * (D / 3f);
                        Vector3 p = a * a * a * drawFrom.Position + 3 * a * a * u * c1 + 3 * a * u * u * c2 + u * u * u * p3;
                        Vector3 v = (3 * a * a * (c1 - drawFrom.Position) + 6 * a * u * (c2 - c1) + 3 * u * u * (p3 - c2)) / D;
                        Vector3 yAx = Vector3.Normalize(MaceTarget(segs, T, 0, 0) - p);     // кольцо к руке
                        Vector3 zAx = Vector3.Normalize(Vector3.Cross(ch.Right, yAx)), xAx = Vector3.Cross(yAx, zAx);
                        var qt = Quaternion.CreateFromRotationMatrix(new Matrix4x4(xAx.X, xAx.Y, xAx.Z, 0, yAx.X, yAx.Y, yAx.Z, 0, zAx.X, zAx.Y, zAx.Z, 0, 0, 0, 0, 1));
                        float w = u * u * (3 - 2 * u);
                        core.Drive(AnchorRigMode.Thrown, 3, new AnchorPose(p, Quaternion.Normalize(Quaternion.Slerp(drawFrom.Rotation, qt, w)), v, Vector3.Zero), 1f / 120f);
                    }
                    else if (tMount >= 0 && T >= tMount) { if (!mounted) { mounted = true; events.Add($"\"mount\": {N(T)}"); } core.Drive(AnchorRigMode.OnBack, 2, Mount(segs, T), 1f / 120f, Math.Max(.01f, tMount + F(stow, "settle", .15f) - T), F(stow, "omega", 30f)); }
                    else if (thrown && !caught && T < tHold)
                    {
                        AnchorPose target = new AnchorPose(land.Position, land.Rotation, Vector3.Zero, Vector3.Zero);
                        if (T < tCon)
                        {
                            float D = tCon - tRel, u = (T - tRel) / D, w = u * u * (3 - 2 * u);
                            Vector3 p, v;
                            if (F(lunge, "arcUp", 0) > 0)
                            {   // бросок дугой над головой героя: вверх от точки выпуска, сверху-сзади в воронку (кубика Безье)
                                Vector3 fw = Vector3.Normalize(new Vector3(land.Position.X - rel.Position.X, 0, land.Position.Z - rel.Position.Z));
                                Vector3 c1 = rel.Position + Vector3.UnitY * F(lunge, "arcUp", .8f) + fw * F(lunge, "arcFwd", .2f);
                                Vector3 c2 = land.Position - fw * F(lunge, "arcBack", .9f) + Vector3.UnitY * F(lunge, "arcHigh", 1.4f);
                                float a = 1 - u;
                                p = a * a * a * rel.Position + 3 * a * a * u * c1 + 3 * a * u * u * c2 + u * u * u * land.Position;
                                v = 3 * a * a * (c1 - rel.Position) + 6 * a * u * (c2 - c1) + 3 * u * u * (land.Position - c2);
                            }
                            else Hermite(rel.Position, rel.Velocity * D, land.Position, land.Velocity * D, u, out p, out v);
                            target = new AnchorPose(p, Quaternion.Normalize(Quaternion.Slerp(rel.Rotation, land.Rotation, w)), v / D, Vector3.Zero);
                        }
                        core.Drive(AnchorRigMode.Thrown, 1, target, 1f / 120f);
                    }
                    else if (thrown && !caught) { caught = true; core.EnterLive(AnchorRigMode.Caught, grip); events.Add($"\"reel\": {{\"t\": {N(T)}, \"cable\": {N(core.CableLength)}}}"); }
                    else core.StepLive(1f / 120f, prevGrip, grip, ground, caps, n);
                    prevGrip = grip; maxPush = Math.Max(maxPush, core.LastBodyPush);
                    var h = core.Body;
                    var o = core.Live ? new AnchorPose(h.Position, h.Rotation, h.Velocity, h.AngularVelocity) : core.Output;
                    for (int i = 0; i < hits.Count; i++) if (Math.Abs(T - hits[i].T) < 1f / 240f) { got[2 * i] = o.Position; got[2 * i + 1] = o.Velocity; }
                    if (step % stepsPerOut != 0) continue;
                    Vector3 ring = o.Position + Vector3.Transform(head.EyeLocal, o.Rotation);
                    float span = Vector3.Distance(ring, grip), cable = core.Live ? core.CableLength : span;
                    if (k++ > 0) outFrames.Append(",\n");
                    outFrames.Append($"{{\"t\": {N(T)}, \"seg\": {sp.Seg}, \"clip\": \"{sp.Clip}\", \"frame\": {N(sp.Frame)}, \"root\": {V(root)}, \"yaw\": {N(yaw)}, " +
                                     $"\"p\": {V(o.Position)}, \"q\": [{N(o.Rotation.X)}, {N(o.Rotation.Y)}, {N(o.Rotation.Z)}, {N(o.Rotation.W)}], \"v\": {N(o.Velocity.Length())}, " +
                                     $"\"ring\": {V(ring)}, \"grip\": {V(grip)}, \"cable\": {N(cable)}, \"span\": {N(span)}, \"mode\": \"{core.Mode}\", " +
                                     $"\"push\": {N(core.LastBodyPush)}, \"grounded\": {(h.Grounded ? "true" : "false")}}}");
                }
                ev = string.Join(", ", events);
                return outFrames.Append("]").ToString();
            }
            bool lSegOK(float t) => t >= 0;

            var Acc = new Vector3[hits.Count]; var hit = new Vector3[2 * hits.Count];
            string frames = null, evs = null; float push = 0; var miss0 = new float[hits.Count];
            for (int pass = 0; pass <= (int)F(plan, "guidePasses", 6); pass++)
            {
                frames = Run(Acc, hit, out evs, out push);
                bool done = true;
                for (int i = 0; i < hits.Count; i++)
                {
                    Vector3 miss = Nearest(zones[i].Root, zones[i].Yaw, zones[i].Z, hit[2 * i], hits[i].Target) - hit[2 * i];
                    if (pass == 0) miss0[i] = miss.Length();
                    if (miss.Length() > .02f) done = false;
                    Acc[i] += 2 * miss / (G * G) * .9f;
                }
                if (done || hits.Count == 0) break;
            }
            var rep = new StringBuilder("[");
            for (int i = 0; i < hits.Count; i++)
                rep.Append((i > 0 ? ", " : "") + $"{{\"t\": {N(hits[i].T)}, \"target\": {V(hits[i].Target)}, \"got\": {V(hit[2 * i])}, \"speed\": {N(hit[2 * i + 1].Length())}, " +
                           $"\"missFree\": {N(miss0[i])}, \"miss\": {N((Nearest(zones[i].Root, zones[i].Yaw, zones[i].Z, hit[2 * i], hits[i].Target) - hit[2 * i]).Length())}, \"guideAccel\": {N(Acc[i].Length())}}}");
            File.WriteAllText(outPath, "{\"chainSwing\": " + N(Ls) + ", \"fps\": " + fps + ", \"duration\": " + N(total) + ", \"maxBodyPush\": " + N(push) +
                ", \"contacts\": " + rep + "], \"events\": {" + evs + "}, \"frames\": " + frames + "}");
            Console.WriteLine($"W4SIM {outPath} L {Ls} duration {total:0.000}s maxPush {push:0.000} contacts {rep}]");
            return 0;
        }

        static Vector3 Nearest(Vector3 root, float yaw, float[] z, Vector3 p, Vector3 point)
        {
            if (z[0] <= 0) return point;
            Vector3 d = p - root; float fwd = d.X * MathF.Sin(yaw) + d.Z * MathF.Cos(yaw), side = d.X * MathF.Cos(yaw) - d.Z * MathF.Sin(yaw);
            float ang = Math.Clamp(MathF.Atan2(side, fwd), -z[0] * MathF.PI / 180f, z[0] * MathF.PI / 180f), r = Math.Clamp(MathF.Sqrt(fwd * fwd + side * side), z[1], z[2]);
            float f2 = r * MathF.Cos(ang), s2 = r * MathF.Sin(ang);
            return new Vector3(root.X + f2 * MathF.Sin(yaw) + s2 * MathF.Cos(yaw), Math.Clamp(p.Y, z[3], z[4]), root.Z + f2 * MathF.Cos(yaw) - s2 * MathF.Sin(yaw));
        }

        static AnchorPose Land(Vector3 rootC, float yawC, float reach, float pitchDeg, float bury, float speed)
        {
            // Голова лежит венцом в воронке в reach м перед героем, кольцо — к герою, лапы поперёк линии.
            Vector3 fwd = new Vector3(MathF.Sin(yawC), 0, MathF.Cos(yawC)), right = new Vector3(fwd.Z, 0, -fwd.X);
            float pitch = pitchDeg * MathF.PI / 180f;
            Vector3 y = Vector3.Normalize(-fwd * MathF.Cos(pitch) + Vector3.UnitY * MathF.Sin(pitch)), x = right, z = Vector3.Cross(x, y);
            var q = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
            Vector3 centre = rootC + fwd * reach; centre.Y = .47f * MathF.Sin(pitch) - bury;
            Vector3 dir = Vector3.Normalize(fwd * MathF.Cos(.7f) - Vector3.UnitY * MathF.Sin(.7f));
            return new AnchorPose(centre, q, dir * speed, Vector3.Zero);
        }

        struct ChestAxes { public Vector3 O, Right, Up, Back; }

        static ChestAxes Chest(List<Seg> segs, float T)
        {
            var s = Find(segs, T, out float f);
            float yaw = YawAt(s, f); Vector3 root = RootPos(s, f);
            var t = s.T;
            Vector3 P(string b) => ToWorld(t.BoneAt(t.Bone(b), f), root, yaw);
            Vector3 o = P("Spine2"), right = Vector3.Normalize(P("RightArm") - P("LeftArm"));
            Vector3 up = P("Neck") - o; up = Vector3.Normalize(up - Vector3.Dot(up, right) * right);
            return new ChestAxes { O = o, Right = right, Up = up, Back = -Vector3.Cross(right, up) };
        }

        /// <summary>Крепление на спине (черновик v4): голова поперёк лопаток, кольцо у правого плеча, плашмя к спине.</summary>
        static AnchorPose Mount(List<Seg> segs, float T)
        {
            var s = Find(segs, T, out float f);
            float yaw = YawAt(s, f); Vector3 root = RootPos(s, f);
            var t = s.T;
            Vector3 P(string b) => ToWorld(t.BoneAt(t.Bone(b), f), root, yaw);
            Vector3 o = P("Spine2"), right = Vector3.Normalize(P("RightArm") - P("LeftArm"));
            Vector3 up = P("Neck") - o; up = Vector3.Normalize(up - Vector3.Dot(up, right) * right);
            Vector3 back = -Vector3.Cross(right, up);
            float a = 25f * MathF.PI / 180f;
            Vector3 y = Vector3.Normalize(up * MathF.Cos(a) + right * MathF.Sin(a)), z = back, x = Vector3.Normalize(Vector3.Cross(y, z));
            z = Vector3.Cross(x, y);
            var q = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
            return AnchorPose.At(o - up * .12f + back * .24f - right * .02f, q);
        }

        struct Where { public int Seg; public string Clip; public float Frame; }

        static Seg Find(List<Seg> segs, float T, out float frame)
        {
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i]; float len = (s.To - s.From) / 30f;
                if (T <= s.Start + len + 1e-6f || i == segs.Count - 1)
                {
                    frame = Math.Clamp(s.From + (T - s.Start) * 30f, s.From, s.To);
                    return s;
                }
            }
            throw new InvalidOperationException();
        }

        static float YawAt(Seg s, float frame)
        {
            if (s.Turn <= 0) return s.Yaw;
            float u = Math.Clamp((frame - s.From) / s.Turn, 0, 1); u = u * u * (3 - 2 * u);
            float d = s.Yaw - s.BaseYaw;
            return s.BaseYaw + d * u;
        }

        static Vector3 RootFwdAt(Seg s, float frame)
        {
            float fwd = Fwd(s, frame) - Fwd(s, s.From);
            return new Vector3(MathF.Sin(s.Yaw), 0, MathF.Cos(s.Yaw)) * fwd;
        }

        static Vector3 RootPos(Seg s, float frame) => s.Base + RootFwdAt(s, frame);

        static float Fwd(Seg s, float frame)
        {
            // root_fwd лежит в сэмплах трека (метры вперёд, как Sim везёт героя на выпаде)
            float x = Math.Clamp(frame * s.T.Sub, 0, s.Fw.Length - 1); int i = Math.Min((int)x, s.Fw.Length - 2);
            return s.Fw[i] + (s.Fw[i + 1] - s.Fw[i]) * (x - i);
        }

        static Vector3 MaceTarget(List<Seg> segs, float T, float L, float eye)
        {
            var s = Find(segs, Math.Max(0, T), out float f);
            float yaw = YawAt(s, f); Vector3 root = RootPos(s, f);
            float x = Math.Clamp(f * s.T.Sub, 0, s.Ax.Length - 1); int i = Math.Min((int)x, s.Ax.Length - 2);
            Vector3 ax = Vector3.Normalize(Vector3.Lerp(s.Ax[i], s.Ax[i + 1], x - i));
            ax = Vector3.Transform(ax, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return ToWorld(s.T.GripAt(f), root, yaw) + ax * (L + eye);
        }

        static Vector3[] LoadAxis(string path)
        {
            var list = new List<Vector3>();
            foreach (var e in JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("samples").EnumerateArray())
            {
                Vector3 V3(string n) { var a = e.GetProperty(n); return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()); }
                list.Add(e.TryGetProperty("axis", out _) ? V3("axis") : Vector3.Normalize(V3("rfist") - V3("lfist")));
            }
            return list.ToArray();
        }

        static float[] LoadFwd(string path)
        {
            var list = new List<float>();
            foreach (var e in JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("samples").EnumerateArray())
                list.Add(e.TryGetProperty("root_fwd", out var v) ? v.GetSingle() : 0f);
            return list.ToArray();
        }

        static Vector3 ToWorld(Vector3 local, Vector3 root, float yaw) => root + Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));

        static void Sample(List<Seg> segs, float T, out Vector3 grip, out Vector3 root, out float yaw, out Where w)
        {
            var s = Find(segs, T, out float f);
            yaw = YawAt(s, f); root = RootPos(s, f);
            grip = ToWorld(s.T.GripAt(f), root, yaw);
            w = new Where { Seg = segs.IndexOf(s), Clip = s.Clip, Frame = f };
        }

        static int Capsules(List<Seg> segs, float T, AnchorCapsule[] caps, AnchorCapsule[] chain)
        {
            var s = Find(segs, T, out float f);
            float yaw = YawAt(s, f); Vector3 root = RootPos(s, f);
            var k = s.B.Skeleton(f);
            Vector3 W(Vector3 v) => AnchorSkeleton.Has(v) ? ToWorld(v, root, yaw) : v;
            var sk = new AnchorSkeleton
            {
                Root = root, Hips = W(k.Hips), Spine2 = W(k.Spine2), Head = W(k.Head), LUpLeg = W(k.LUpLeg), LLeg = W(k.LLeg), LFoot = W(k.LFoot),
                RUpLeg = W(k.RUpLeg), RLeg = W(k.RLeg), RFoot = W(k.RFoot), LArm = W(k.LArm), LForeArm = W(k.LForeArm), LHand = W(k.LHand),
                RArm = W(k.RArm), RForeArm = W(k.RForeArm), RHand = W(k.RHand),
            };
            AnchorRigBody.Build(sk, 1f, caps, out int n, chain, out _);
            return n;
        }

        static void Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float u, out Vector3 p, out Vector3 dp)
        {
            float u2 = u * u, u3 = u2 * u;
            p = (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1;
            dp = (6 * u2 - 6 * u) * p0 + (3 * u2 - 4 * u + 1) * m0 + (-6 * u2 + 6 * u) * p1 + (3 * u2 - 2 * u) * m1;
        }

        static float F(JsonElement e, string name, float def) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetSingle() : def;
        static string N(float v) => v.ToString("0.#####", I);
        static string V(Vector3 v) => $"[{N(v.X)}, {N(v.Y)}, {N(v.Z)}]";
    }
}
