using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnchorBake;
using GripOpt;

namespace GripOpt2
{
    /// <summary>
    /// gripopt_s2 --body &lt;stand-in.grip.json&gt; --start &lt;prev.anchorbake.json&gt;@&lt;t&gt; --out &lt;dir&gt; [--init path.json] [--gens 300] [--restarts 3]
    /// gripopt_s2 ... --eval path.json [--name X]   (table + X.grip.json (body + this grip) + X.plan.json + X.path.json)
    /// Options: --contact 6 --frames 11 --vmax 8 --dirw 40 --follow 1 --seamw 30 --nodir (no backhand direction term)
    /// </summary>
    public static class Program2
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        static string Repo(string start)
        {
            var d = new DirectoryInfo(start);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "razlom"))) d = d.Parent;
            return d.FullName;
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var a = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("--")) a[args[i].Substring(2)] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "1";
            string repo = Repo(Directory.GetCurrentDirectory());
            var head = HeadModel.Load(Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"), .94f, new Vector3(0, .405f, .01f));
            var body = GripTrack.Load(a["body"]);
            var start = LoadState(a["start"], out Vector3 g0, out Vector3 gv);
            var m = new Model2(body, head, start) { SeamVel = gv };
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            m.Contact = F("contact", 6); m.VMax = F("vmax", 8); m.DirWeight = F("dirw", 40); m.FollowWeight = F("follow", 1);
            m.SeamWeight = F("seamw", 30); m.AMax = F("amax", 150); m.LiftWeight = F("liftw", 0); m.LiftFrame = F("liftf", 2.5f);
            m.ContactY = F("cy", .72f); m.HandTop = F("handtop", 1.75f);
            if (a.ContainsKey("nodir")) m.Reverse = false;
            int n = body.Frames;
            string outDir = a.TryGetValue("out", out var o) ? o : ".";
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"seam grip ({g0.X:F3} {g0.Y:F3} {g0.Z:F3}) step ({gv.X:F3} {gv.Y:F3} {gv.Z:F3}) head ({start.P.X:F2} {start.P.Y:F2} {start.P.Z:F2}) v ({start.V.X:F1} {start.V.Y:F1} {start.V.Z:F1})");
            if (a.ContainsKey("wait")) return WaitProgram.Run(a, head, body, start, g0, gv, outDir);
            if (a.ContainsKey("eval"))
            {
                var p = Load(a["eval"], g0, n);
                var e = m.Evaluate(p);
                Print(e);
                string name = a.TryGetValue("name", out var nm) ? nm : "plan";
                WriteGrip(a["body"], Path.Combine(outDir, name + ".grip.json"), m.TrackOf(p), name);
                Save(Path.Combine(outDir, name + ".path.json"), p, e);
                WritePlan(Path.Combine(outDir, name + ".plan.json"), e, n);
                return 0;
            }
            Path2 best = a.TryGetValue("init", out var ip) ? Load(ip, g0, n) : Guess(g0, gv, n);
            var bestE = m.Evaluate(best);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            int gens = a.TryGetValue("gens", out var g) ? int.Parse(g) : 300;
            int restarts = a.TryGetValue("restarts", out var rs) ? int.Parse(rs) : 3;
            int seed = a.TryGetValue("seed", out var sd) ? int.Parse(sd) : 1;
            double sigma = F("sigma", .06f);
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(best.ToVector(), sigma * (r == 0 ? 1 : .6), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask();
                    var cost = new double[pop.Length]; var evals = new Eval2[pop.Length];
                    Parallel.For(0, pop.Length, k => { evals[k] = m.Evaluate(Path2.FromVector(pop[k], g0, n)); cost[k] = evals[k].Cost; });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = evals[kb]; best = Path2.FromVector(pop[kb], g0, n); }
                    if (gen % 25 == 0) Console.WriteLine($"r{r} g{gen} best {bestE.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestE));
                    if (bestE.Cost > last - 1e-4) stall++; else stall = 0;
                    last = Math.Min(last, bestE.Cost);
                    if (stall > 80 || cma.Sigma < 2e-4) break;
                }
                Save(Path.Combine(outDir, "best.path.json"), best, bestE);
                Console.WriteLine($"restart {r} done: {bestE.Cost:F3}");
            }
            Print(bestE);
            return 0;
        }

        /// <summary>Head state of a previous bake at frame t (+ the grip there and its step per frame for continuity).</summary>
        static Rec LoadState(string spec, out Vector3 grip, out Vector3 step)
        {
            int at = spec.LastIndexOf('@');
            string file = spec.Substring(0, at); float t = float.Parse(spec.Substring(at + 1), I);
            using var d = JsonDocument.Parse(File.ReadAllBytes(file));
            var s = d.RootElement.GetProperty("samples").EnumerateArray().ToList();
            int k = Enumerable.Range(0, s.Count).OrderBy(i => Math.Abs(s[i].GetProperty("t").GetSingle() - t)).First();
            var x = s[k];
            grip = V(x.GetProperty("grip"));
            int k1 = Enumerable.Range(0, s.Count).OrderBy(i => Math.Abs(s[i].GetProperty("t").GetSingle() - (t - 1))).First();
            step = grip - V(s[k1].GetProperty("grip"));
            var q = x.GetProperty("q");
            return new Rec { P = V(x.GetProperty("p")), V = V(x.GetProperty("v")), W = V(x.GetProperty("w")),
                Q = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()) };
        }

        static Path2 Guess(Vector3 g0, Vector3 gv, int n)
        {
            // over the left shoulder: fists rise to the left shoulder (frames 1-3), then down-right across the body (3-6), on to the right hip
            var p = new Path2(n); p.G[0] = g0;
            Vector3[] k = { g0, g0 + gv, new Vector3(-.40f, 1.30f, .30f), new Vector3(-.35f, 1.45f, .25f), new Vector3(-.20f, 1.35f, .40f),
                new Vector3(.00f, 1.15f, .50f), new Vector3(.15f, 1.00f, .50f), new Vector3(.30f, .95f, .40f), new Vector3(.40f, .98f, .25f),
                new Vector3(.45f, 1.05f, .15f), new Vector3(.45f, 1.12f, .08f), new Vector3(.42f, 1.18f, .04f) };
            for (int f = 1; f <= n; f++) p.G[f] = k[Math.Min(f, k.Length - 1)];
            return p;
        }

        public static Path2 Load(string path, Vector3 g0, int n)
        {
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            var p = new Path2(n); int f = 0;
            foreach (var v in d.RootElement.GetProperty("frames").EnumerateArray()) { if (f > n) break; p.G[f++] = V(v); }
            for (; f <= n; f++) p.G[f] = p.G[f - 1];
            p.G[0] = g0;
            return p;
        }

        static string Terms(Eval2 e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | c {e.V.ContactError:F2} r {e.V.ContactRadius:F2} h {e.V.ContactHeight:F2} ang {e.V.ContactAngle:F0} v {e.V.ContactSpeed:F1} slack {e.V.SlackFastFrames} jump {e.V.MaxJump:F3} pen {e.V.Penetration * 100:F1} clr {(e.V.ChainClearance == double.MaxValue ? 9 : e.V.ChainClearance) * 100:F1}";

        public static void Print(Eval2 e)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows)
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            int last = (int)Math.Round(e.Run.Records[^1].Tick * 4);
            for (int k = 0; k <= last; k += 2)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                Console.WriteLine($"  f{k / 4f,5:0.00} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) v ({x.V.X:F1} {x.V.Y:F1} {x.V.Z:F1}) |v| {x.V.Length():F1} ang {Math.Atan2(x.P.X, x.P.Z) * 180 / Math.PI:F0} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {x.Span:F2} T {x.Tension:F0}");
            }
        }

        static void Save(string path, Path2 p, Eval2 e)
        {
            var o = new JsonObject
            {
                ["frames"] = new JsonArray(p.G.Select(v => (JsonNode)V3(v)).ToArray()), ["cost"] = R(e.Cost),
                ["terms"] = new JsonObject(e.Terms.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, R(kv.Value)))),
                ["contact"] = new JsonObject { ["error"] = R(e.V.ContactError), ["radius"] = R(e.V.ContactRadius), ["height"] = R(e.V.ContactHeight),
                    ["angle"] = R(e.V.ContactAngle), ["speed"] = R(e.V.ContactSpeed) },
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));
        static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        static void WriteGrip(string standIn, string path, GripTrack t, string clip)
        {
            var root = JsonNode.Parse(File.ReadAllText(standIn));
            root["clip"] = clip;
            var s = root["samples"].AsArray();
            for (int i = 0; i < s.Count; i++) s[i]["grip"] = V3(t.Grip[i]);
            File.WriteAllText(path, root.ToJsonString());
        }

        static void WritePlan(string path, Eval2 e, int n)
        {
            var arr = new JsonArray();
            for (int k = 0; k <= 4 * n; k++)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                arr.Add(new JsonObject
                {
                    ["frame"] = R(k / 4.0), ["grip"] = V3(x.Grip), ["ring"] = V3(x.Ring), ["p"] = V3(x.P),
                    ["q"] = new JsonArray(R(x.Q.X), R(x.Q.Y), R(x.Q.Z), R(x.Q.W)), ["v"] = V3(x.V), ["span"] = R(x.Span), ["taut"] = x.Taut,
                });
            }
            File.WriteAllText(path, new JsonObject { ["axes"] = "Unity root: x right, y up, z forward, metres", ["chain"] = 1.6, ["samples"] = arr }.ToJsonString());
        }
    }
}
