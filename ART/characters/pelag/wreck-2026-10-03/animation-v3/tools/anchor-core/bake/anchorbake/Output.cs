using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Game.View;

namespace AnchorBake
{
    /// <summary>Writes the bake (DESIGN 2.1 format, read by Game.View.AnchorBake), the retime proposal and the sheet data.</summary>
    public static class Output
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;
        static double R(double v, int d = 5) => Math.Round(v, d);
        static void V3(Utf8JsonWriter w, string name, Vector3 v, int d = 5)
        { w.WriteStartArray(name); w.WriteNumberValue(R(v.X, d)); w.WriteNumberValue(R(v.Y, d)); w.WriteNumberValue(R(v.Z, d)); w.WriteEndArray(); }
        static void Q4(Utf8JsonWriter w, string name, Quaternion q)
        { w.WriteStartArray(name); w.WriteNumberValue(R(q.X, 6)); w.WriteNumberValue(R(q.Y, 6)); w.WriteNumberValue(R(q.Z, 6)); w.WriteNumberValue(R(q.W, 6)); w.WriteEndArray(); }

        public static void WriteBake(string path, string name, BakeRun run, GripTrack t, HeadModel head, Validation v,
            float retimeShift, string startFrom, string endsInto)
        {
            var c = run.Cfg;
            using var fs = File.Create(path);
            using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false });
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("clip", t.Clip);
            w.WriteString("bake", name);
            w.WriteString("kind", c.Kind);
            w.WriteNumber("windupTicks", c.Kind == BakeKind.Loop ? 0 : (int)Math.Round(c.ContactFrame));
            w.WriteNumber("clipRate", c.ClipRate);
            w.WriteStartObject("source");
            w.WriteString("fbx", t.SourceFbx); w.WriteString("sha256", t.SourceSha); w.WriteString("bind", t.Bind);
            w.WriteString("gripTrack", Path.GetFileName(t.Path)); w.WriteString("gripSha256", t.Sha256);
            w.WriteString("hand", t.Hand);
            w.WriteString("physics", "Game.View.AnchorRigCore.StepLive + AnchorRigBody (same source files as the game)");
            w.WriteEndObject();
            w.WriteStartObject("rig");
            w.WriteNumber("chainLength", c.ChainLength); w.WriteNumber("mass", c.Mass);
            V3(w, "inertia", c.Inertia); V3(w, "eyeLocal", head.EyeLocal);
            w.WriteString("hull", Path.GetFileName(head.HullSource) + "#" + head.HullLocal.Length + ":" + head.HullSha.Substring(0, 12));
            w.WriteNumber("headSize", head.Scale); w.WriteNumber("gravity", c.Gravity);
            w.WriteStartArray("airDrag"); w.WriteNumberValue(c.AirLinear); w.WriteNumberValue(c.AirAngular); w.WriteEndArray();
            w.WriteNumber("step", R(c.Dt, 7)); w.WriteNumber("ground", c.Ground);
            w.WriteEndObject();
            float span = c.Loop ? c.LoopTo - c.LoopFrom : t.Frames;
            w.WriteNumber("fps", t.Fps); w.WriteNumber("sub", c.RecordPerTick); w.WriteNumber("frames", span);
            w.WriteStartArray("samples");
            foreach (var r in run.Records)
            {
                w.WriteStartObject();
                // "t" = bake frame (Sim tick), exactly i/sub; "sec" and "src" (source clip frame before retime) are for the report.
                w.WriteNumber("t", R(r.Tick, 4)); w.WriteNumber("sec", R(r.T, 5)); w.WriteNumber("src", R(r.Frame, 4));
                V3(w, "p", r.P); Q4(w, "q", r.Q); V3(w, "v", r.V, 4); V3(w, "w", r.W, 4);
                V3(w, "grip", r.Grip); Q4(w, "gripQ", r.GripQ);
                w.WriteBoolean("taut", r.Taut); w.WriteNumber("tension", R(r.Tension, 1));
                if (r.Guided) w.WriteBoolean("guided", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("contacts");
            if (c.Kind == BakeKind.Swing || BakeKind.Ground(c.Kind))
            {
                var at = BakeSim.At(run, run.ContactTime);
                w.WriteStartObject(); w.WriteString("kind", c.Kind == BakeKind.Swing ? "swing" : "ground"); w.WriteNumber("frame", c.ContactFrame);
                V3(w, "point", at.P); w.WriteNumber("radius", R(v.ContactRadius, 3)); w.WriteNumber("speed", R(v.ContactSpeed, 2));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("ground");
            float? from = null;
            foreach (var r in run.Records)
            {
                if (r.Grounded && from == null) from = r.Tick;
                if (!r.Grounded && from != null) { w.WriteStartObject(); w.WriteNumber("from", R(from.Value, 3)); w.WriteNumber("to", R(r.Tick, 3)); w.WriteEndObject(); from = null; }
            }
            if (from != null) { w.WriteStartObject(); w.WriteNumber("from", R(from.Value, 3)); w.WriteNumber("to", R(run.Records[^1].Tick, 3)); w.WriteEndObject(); }
            w.WriteEndArray();
            // JsonUtility cannot read null into a class field: "no loop" is {0, 0}.
            w.WriteStartObject("loop"); w.WriteNumber("from", 0); w.WriteNumber("to", c.Loop ? span : 0); w.WriteEndObject();
            float liveFrom = BakeKind.Ground(c.Kind) ? (float)(v.FirstGroundTick >= 0 ? v.FirstGroundTick : c.ContactFrame)
                : c.Kind == BakeKind.Windup && c.ReleaseFrame > 0 ? c.ReleaseFrame : 0f;
            w.WriteNumber("liveFrom", R(liveFrom, 3));
            w.WriteNumber("overheadFrame", c.OverheadFrame);
            w.WriteNumber("sheathFrame", c.SheathFrame);
            w.WriteStartObject("seams"); w.WriteString("startFrom", startFrom); w.WriteString("endsInto", endsInto); w.WriteEndObject();
            w.WriteStartObject("retime"); w.WriteNumber("shiftFrames", retimeShift); w.WriteString("warp", "x + s*sin(pi*x/contact), x<contact"); w.WriteEndObject();
            w.WriteStartObject("guidance");
            w.WriteBoolean("applied", c.GuideAccel != Vector3.Zero);
            w.WriteStartArray("frames"); w.WriteNumberValue(c.ContactFrame - c.GuideTicks); w.WriteNumberValue(c.ContactFrame); w.WriteEndArray();
            w.WriteNumber("peakAccel", R(v.GuidePeak, 2)); w.WriteNumber("peakDeltaV", R(v.GuideDeltaV, 3));
            w.WriteEndObject();
            w.WriteStartObject("validation");
            w.WriteNumber("gripError", 0); w.WriteNumber("maxStretch", R(v.MaxStretch, 5)); w.WriteNumber("contactError", R(v.ContactError, 4));
            w.WriteNumber("maxJump", R(v.MaxJump, 4)); w.WriteNumber("bodyPenetration", R(v.Penetration, 4));
            w.WriteNumber("slackWhileFast", v.SlackFastFrames); w.WriteBoolean("pass", v.AllPass);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        /// <summary>
        /// Ретайм руки (DESIGN §3.3 п. 1) — предложение для timing.json клипа в формате throw_retime Абордажа: для каждого тика
        /// 0…frames — кадр исходного клипа. razlom/ и ART/ сам не правит: файл кладёт автор клипа (или --timing-out).
        /// </summary>
        public static void WriteRetime(string path, string clip, string name, float shift, float contact, int frames, double errorBefore, double errorAfter)
        {
            var map = new List<double>();
            for (int k = 0; k <= frames; k++)
                map.Add(Math.Round(k < contact && k > 0 ? k + shift * Math.Sin(Math.PI * k / contact) : k, 4));
            Dictionary<string, object> root = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) : new Dictionary<string, object>();
            root["anchor_retime_" + name] = new Dictionary<string, object>
            {
                ["clip"] = clip, ["shiftFrames"] = shift, ["contactFrame"] = contact, ["ticksToSourceFrames"] = map,
                ["contactErrorBefore"] = Math.Round(errorBefore, 3), ["contactErrorAfter"] = Math.Round(errorAfter, 3),
                ["note"] = "tick k plays source frame map[k]; rebuild the clip with it, then re-export the grip and re-bake (DESIGN 3.3)",
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (File.Exists(path)) { using var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite); f.SetLength(0); f.Write(bytes); }
            else File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// 60 fps frames for sheet.py: head pose, ring, grip, support hand, chain points, body bones. The chain is drawn the
        /// way the rig draws it (AnchorChainDrawState: taut → AnchorChainLine, slack → AnchorSlackChain on the rig's capsules).
        /// </summary>
        public static void WriteSheet(string path, string title, BakeRun run, BakeSim sim, HeadModel head, Validation v)
        {
            var c = run.Cfg; var t = sim.Track;
            var slack = new AnchorSlackChain(); var draw = new AnchorChainDrawState();
            var nodes = new Vector3[66];
            int frames = (int)Math.Floor(run.Records[^1].T * 60 + 1e-4);
            Func<Vector3, float> ground = _ => c.Ground;
            using var fs = File.Create(path);
            using var w = new Utf8JsonWriter(fs);
            w.WriteStartObject();
            w.WriteString("title", title);
            w.WriteNumber("chainLength", c.ChainLength);
            w.WriteNumber("contactTime", run.ContactTime);
            w.WriteNumber("contactFrame60", Math.Round(run.ContactTime * 60, 3));
            w.WriteString("kind", c.Kind);
            w.WriteStartArray("boneNames"); foreach (var n in t.BoneNames) w.WriteStringValue(n); w.WriteEndArray();
            w.WriteStartArray("hull"); foreach (var p in head.HullLocal) { w.WriteStartArray(); w.WriteNumberValue(R(p.X, 4)); w.WriteNumberValue(R(p.Y, 4)); w.WriteNumberValue(R(p.Z, 4)); w.WriteEndArray(); } w.WriteEndArray();
            w.WriteStartArray("rows");
            foreach (var row in v.Rows) { w.WriteStartObject(); w.WriteString("id", row.Id); w.WriteString("name", row.Name); w.WriteString("value", row.Value); w.WriteString("target", row.Target); w.WriteString("pass", row.Pass == null ? "info" : row.Pass.Value ? "ok" : "FAIL"); w.WriteEndObject(); }
            w.WriteEndArray();
            double solverMs = 0; int solverFrames = 0;
            w.WriteStartArray("frames");
            Rec prev = BakeSim.At(run, 0);
            for (int k = 0; k <= frames; k++)
            {
                float time = k / 60f;
                var r = BakeSim.At(run, time);
                float span = Vector3.Distance(r.Ring, r.Grip);
                draw.Update(r.Taut, span, c.ChainLength, draw.UseLine ? 0f : slack.Deviation());
                float sag = AnchorChainLine.Sag(span, r.Tension, c.Gravity);
                int count;
                if (draw.UseLine) count = AnchorChainLine.Layout(r.Grip, r.Ring, sag, nodes, out _);
                else
                {
                    int n = sim.ChainCapsules(r.Frame, out var caps);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    if (draw.SeedSolver)
                    {
                        slack.Configure(c.ChainLength);
                        slack.Seed(r.Ring, r.Grip, (r.Ring - prev.Ring) * 60f, (r.Grip - prev.Grip) * 60f, sag);
                    }
                    slack.Advance(1f / 60f, prev.Ring, r.Ring, prev.Grip, r.Grip, ground, caps, n);
                    sw.Stop(); solverMs += sw.Elapsed.TotalMilliseconds; solverFrames++;
                    count = slack.Count;
                    for (int i = 0; i < count; i++) nodes[i] = slack[i];
                }
                prev = r;
                w.WriteStartObject();
                w.WriteNumber("f", k); w.WriteNumber("t", R(time, 4)); w.WriteNumber("clip", R(r.Frame, 3));
                V3(w, "p", r.P, 4); Q4(w, "q", r.Q); V3(w, "ring", r.Ring, 4); V3(w, "grip", r.Grip, 4); V3(w, "support", r.Support, 4);
                w.WriteNumber("speed", R(r.V.Length(), 2)); w.WriteNumber("span", R(r.Span, 3)); w.WriteBoolean("taut", r.Taut);
                w.WriteBoolean("grounded", r.Grounded);
                w.WriteStartArray("chain");
                for (int i = 0; i < count; i++) V3Arr(w, nodes[i]);
                w.WriteEndArray();
                w.WriteStartArray("bones");
                for (int b = 0; b < t.BoneNames.Length; b++) V3Arr(w, t.BoneAt(b, r.Frame));
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("solverMsPerFrame", Math.Round(solverMs / Math.Max(1, solverFrames), 4));
            w.WriteNumber("solverFrames", solverFrames);
            w.WriteEndObject();
        }

        /// <summary>
        /// Цена провисшей цепи (DESIGN §5 #16): решатель рига (1) на ВСЕХ кадрах пути запечки, натяг не спасает, и (2) в
        /// «маятнике окна» — кольцо ходит кругом 1 Гц в 1,2 м от неподвижного хвата у бедра (цепь всё время провисла
        /// на 0,4 м), капсулы тела кадра 0. 60 к/с; возвращает худшее среднее и пик мс/кадр (.NET; под Mono ждать ×2–3).
        /// </summary>
        public static (double mean, double peak) BenchSlack(BakeRun run, BakeSim sim, int repeats = 5)
        {
            var c = run.Cfg;
            int frames = (int)Math.Floor(run.Records[^1].T * 60 + 1e-4);
            var path = Bench(c, sim, frames, k => { var r = BakeSim.At(run, k / 60f); return (r.Ring, r.Grip, r.Frame); }, repeats);
            Vector3 hip = sim.Track.GripAt(0);
            var window = Bench(c, sim, 120, k =>
            {
                float a = 2f * (float)Math.PI * k / 60f;
                Vector3 ring = hip + new Vector3(.9f * (float)Math.Cos(a), -.75f, .9f * (float)Math.Sin(a));
                ring.Y = Math.Max(ring.Y, c.Ground + .15f);
                return (ring, hip, 0f);
            }, repeats);
            return path.mean >= window.mean ? path : window;
        }

        static (double mean, double peak) Bench(BakeConfig c, BakeSim sim, int frames, Func<int, (Vector3 ring, Vector3 grip, float frame)> at, int repeats)
        {
            Func<Vector3, float> ground = _ => c.Ground;
            double best = double.MaxValue, peak = 0;
            for (int rep = 0; rep < repeats; rep++)
            {
                var slack = new AnchorSlackChain();
                var prev = at(0);
                slack.Configure(c.ChainLength);
                slack.Seed(prev.ring, prev.grip, Vector3.Zero, Vector3.Zero, .02f);
                double total = 0, worst = 0;
                for (int k = 1; k <= frames; k++)
                {
                    var now = at(k);
                    int n = sim.ChainCapsules(now.frame, out var caps);
                    long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    slack.Advance(1f / 60f, prev.ring, now.ring, prev.grip, now.grip, ground, caps, n);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    total += ms; worst = Math.Max(worst, ms);
                    prev = now;
                }
                double mean = total / Math.Max(1, frames);
                if (mean < best) { best = mean; peak = worst; }
            }
            return (best, peak);
        }

        static void V3Arr(Utf8JsonWriter w, Vector3 v) { w.WriteStartArray(); w.WriteNumberValue(R(v.X, 4)); w.WriteNumberValue(R(v.Y, 4)); w.WriteNumberValue(R(v.Z, 4)); w.WriteEndArray(); }
    }
}
