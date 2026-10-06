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

namespace GripOpt5
{
    /// <summary>
    /// gripopt_s5nw --body &lt;stand-in.grip.json&gt; [--suboff &lt;exported.grip.json&gt;] --entry &lt;Slam_w13.anchorbake.json&gt;@7 --init &lt;path.json&gt; --out dir
    ///              [--gens 200] [--restarts 2] [--sigma .03] [--seed 1] [--axis x,y,z] [--vlo 24] [--vhi 27.5] [--reachl .585] [--reachr .6]
    ///              [--headclr .17] [--entryw 20] [--cycles 30] [--rate 1]
    /// gripopt_s5nw ... --eval path.json --name X [--exact] → table, X.path.json, X.plan.json (quarter frames: grip, ring, p, q, v), X.grip.json, X.seam.txt
    /// --exact: the body track's own grip (exported FBX) instead of the path.  --rates 1,1.1,1.2,1.33: closure / speed / checks per view rate.
    /// </summary>
    public static class LoopProgram
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var a = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("--")) a[args[i].Substring(2)] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "1";
            if (a.ContainsKey("rel")) return RelProgram.Run(a);    // ChargeRelease: RelModel (SlamModel4 + кулаки вперёд на спуске, веса ключей)
            if (a.ContainsKey("slam")) return GripOpt4.SlamProgram4.Run(a);   // ChargeRelease: путь хвата удара оземь (gripopt_s4 --slam) от состояния петли
            string repo = Repo(Directory.GetCurrentDirectory());
            var head = HeadModel.Load(Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"), .94f, new Vector3(0, .405f, .01f));
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            string outDir = a.TryGetValue("out", out var o) ? o : ".";
            Directory.CreateDirectory(outDir);
            var body = GripTrack.Load(a["body"]);
            var entry = GripOpt4.SlamProgram4.LoadState(a["entry"], out _, out _);
            var m = new LoopModel(body, head, entry)
            {
                Gap = F("gap", .15f), ReachL = F("reachl", .585f), ReachR = F("reachr", .60f), HeadClear = F("headclr", .17f), VMax = F("vmax", 5f),
                JerkW = F("jerkw", 40f), VLo = F("vlo", 24f), VHi = F("vhi", 27.5f), ClrMin = F("clrmin", .01f), EntryW = F("entryw", 20f),
                SeamW = F("seamw", 1f), Cycles = (int)F("cycles", 30), Rate = F("rate", 1f), StartOrbit = !a.ContainsKey("fromentry"), StartAz = F("startaz", 165f), AboveMin = F("above", .10f),
            };
            if (a.TryGetValue("axis", out var ax)) { var q = ax.Split(','); m.Axis = Vector3.Normalize(new Vector3(float.Parse(q[0], I), float.Parse(q[1], I), float.Parse(q[2], I))); }
            if (a.TryGetValue("suboff", out var so))
            {
                var r = GripTrack.Load(so); int sb = r.Sub; m.SubOff = new Vector3[r.Grip.Length];
                for (int i = 0; i < r.Grip.Length; i++)
                {
                    int f0 = Math.Min(i / sb, r.Frames - 1); float u = (i - f0 * sb) / (float)sb;
                    m.SubOff[i] = r.Grip[i] - Vector3.Lerp(r.Grip[f0 * sb], r.Grip[Math.Min((f0 + 1) * sb, r.Grip.Length - 1)], u);
                }
            }
            if (a.ContainsKey("eval"))
            {
                var p = Load(a["eval"]);
                bool exact = a.ContainsKey("exact");
                string name = a.TryGetValue("name", out var nm) ? nm : "loop";
                if (a.TryGetValue("rates", out var rs))
                {
                    foreach (var rate in rs.Split(',').Select(x => float.Parse(x, I)))
                    {
                        m.Rate = rate; var er = m.Evaluate(p, exact ? body : null);
                        Console.WriteLine($"RATE {rate:0.00} period {0.4f / rate:0.000} s: " + Terms(er) + " | fails " + string.Join(",", er.V.Rows.Where(x => x.Pass == false).Select(x => x.Id)));
                    }
                    m.Rate = F("rate", 1f);
                }
                var e = m.Evaluate(p, exact ? body : null);
                Print(e);
                var log = new List<string>(); m.InGameSeam(e, log);
                if (!exact) GripOpt4.SlamProgram4.WriteGrip(a["body"], Path.Combine(outDir, name + ".grip.json"), m.TrackOf(p), name);
                Save(Path.Combine(outDir, name + ".path.json"), p, e);
                WritePlan(Path.Combine(outDir, name + ".plan.json"), e);
                var st = m.OrbitStart(p);    // старт прогона на круге — для anchorbake --kind loop --start <это>@0 (из покоя малый круг кулаков голову не раскрутит)
                File.WriteAllText(Path.Combine(outDir, name + ".start.anchorbake.json"), new JsonObject
                {
                    ["version"] = 1, ["clip"] = name + "_start", ["note"] = "orbit start: head 1.98 m from grip@0 at azimuth " + m.StartAz.ToString("0", I) + "°, tangential speed omega*R",
                    ["samples"] = new JsonArray(new JsonObject { ["t"] = 0, ["p"] = V3(st.P), ["q"] = new JsonArray(R(st.Q.X), R(st.Q.Y), R(st.Q.Z), R(st.Q.W)), ["v"] = V3(st.V), ["w"] = V3(st.W) }),
                }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllLines(Path.Combine(outDir, name + ".seam.txt"), new[] { $"in-game seam Slam_w13@7 → loop@0 (AnchorBlend, no deadline): at0 {e.SeamAt0:F3} m, max accel {e.SeamAccel:F0} m/s², offset < 2 cm from frame {e.SeamSettle:F1}; honest entry state diff dp {e.EntryDp:F3} m dv {e.EntryDv:F2} m/s" }.Concat(log));
                return 0;
            }
            LoopPath best = Load(a["init"]);
            var bestE = m.Evaluate(best);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            int gens = (int)F("gens", 200), restarts = (int)F("restarts", 2), seed = (int)F("seed", 1);
            double sigma = F("sigma", .03f);
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(best.ToVector(), sigma * (r == 0 ? 1 : .5), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask(); var cost = new double[pop.Length]; var ev = new LoopEval[pop.Length];
                    Parallel.For(0, pop.Length, k => { ev[k] = m.Evaluate(LoopPath.FromVector(pop[k])); cost[k] = ev[k].Cost; });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = ev[kb]; best = LoopPath.FromVector(pop[kb]); }
                    if (gen % 20 == 0) Console.WriteLine($"r{r} g{gen} best {bestE.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestE));
                    if (bestE.Cost > last - 1e-4) stall++; else stall = 0;
                    last = Math.Min(last, bestE.Cost);
                    if (stall > 60 || cma.Sigma < 2e-4) break;
                }
                Save(Path.Combine(outDir, "best.path.json"), best, bestE);
                Console.WriteLine($"restart {r} done: {bestE.Cost:F3}");
            }
            Print(bestE);
            return 0;
        }

        static string Repo(string start)
        {
            var d = new DirectoryInfo(start);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "razlom"))) d = d.Parent;
            return d.FullName;
        }

        static string Terms(LoopEval e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | v {e.MeanSpeed:F1} [{e.MinSpeed:F1}..{e.MaxSpeed:F1}] close {e.V.LoopDp * 1000:F1}mm/{e.V.LoopDv:F2} entry {e.EntryDp:F2}/{e.EntryDv:F1} seam {e.SeamAt0:F2}/{e.SeamAccel:F0}/{e.SeamSettle:F1}"
            + $" jump {e.V.MaxJump:F3} slack {e.V.SlackFastFrames} pen {e.V.Penetration * 100:F1} clr {(e.V.ChainClearance == double.MaxValue ? 9 : e.V.ChainClearance) * 100:F1} aboveCrown {e.MinHeadAboveCrown:F2}";

        public static void Print(LoopEval e)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows)
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            for (int k = 0; k <= 48; k += 2)
            {
                var x = BakeSim.At(e.Run, k / 120f / e.Run.Cfg.ClipRate);
                Console.WriteLine($"  f{k / 4f,5:0.00} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) |v| {x.V.Length():F1} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {x.Span:F2} T {x.Tension:F0}");
            }
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));
        static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        public static LoopPath Load(string path)
        {
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            var p = new LoopPath(); int f = 0;
            foreach (var v in d.RootElement.GetProperty("frames").EnumerateArray()) { if (f >= LoopPath.N) break; p.G[f++] = V(v); }
            return p;
        }

        static void Save(string path, LoopPath p, LoopEval e)
        {
            var o = new JsonObject
            {
                ["frames"] = new JsonArray(Enumerable.Range(0, LoopPath.N + 1).Select(f => (JsonNode)V3(p.At(f))).ToArray()), ["cost"] = R(e.Cost),
                ["terms"] = new JsonObject(e.Terms.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, R(kv.Value)))),
                ["speed"] = new JsonObject { ["mean"] = R(e.MeanSpeed), ["min"] = R(e.MinSpeed), ["max"] = R(e.MaxSpeed) },
                ["close"] = new JsonObject { ["dp"] = R(e.V.LoopDp), ["dv"] = R(e.V.LoopDv) },
                ["entry"] = new JsonObject { ["dp"] = R(e.EntryDp), ["dv"] = R(e.EntryDv), ["seamAt0"] = R(e.SeamAt0), ["seamAccel"] = R(e.SeamAccel), ["seamSettle"] = R(e.SeamSettle) },
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        static void WritePlan(string path, LoopEval e)
        {
            var arr = new JsonArray();
            for (int k = 0; k <= 4 * LoopPath.N; k++)
            {
                var x = BakeSim.At(e.Run, k / 120f / e.Run.Cfg.ClipRate);
                arr.Add(new JsonObject
                {
                    ["frame"] = R(k / 4.0), ["grip"] = V3(x.Grip), ["ring"] = V3(x.Ring), ["p"] = V3(x.P),
                    ["q"] = new JsonArray(R(x.Q.X), R(x.Q.Y), R(x.Q.Z), R(x.Q.W)), ["v"] = V3(x.V), ["span"] = R(x.Span), ["taut"] = x.Taut,
                });
            }
            File.WriteAllText(path, new JsonObject { ["axes"] = "Unity root: x right, y up, z forward, metres", ["chain"] = 1.6, ["loop"] = true, ["samples"] = arr }.ToJsonString());
        }
    }
}
