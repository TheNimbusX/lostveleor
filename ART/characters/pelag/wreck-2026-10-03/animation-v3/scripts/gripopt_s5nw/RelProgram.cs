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
using GripOpt4;

namespace GripOpt5
{
    /// <summary>
    /// gripopt_s3 --body &lt;stand-in.grip.json&gt; --prev &lt;state.json&gt;@&lt;t&gt; --out &lt;dir&gt; [--designed] [--init path.json] [--gens 300] [--restarts 3]
    /// gripopt_s3 ... --eval path.json [--exact] [--name X]  → table, X.path.json, X.plan.json, X.grip.json, X.start.anchorbake.json, X.seam.txt
    /// gripopt_s3 --stow ... → StowProgram (live stow: hand at the back mount, rig reel, catch).
    /// Options: --contact 9 --overhead 5 --impact 0,2.2 --vmax 8 --amax 150 --sigma .05 --seed 1 --seamw 30
    /// </summary>
    /// <summary>v3s5nw: копия SlamProgram4 с RelModel (ChargeRelease); ключи кистей «кадр: [x, y, z(, вес)]», --fwdw/--fwdmin/--fwdfrom/--fwdto.</summary>
    public static class RelProgram
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static int Run(Dictionary<string, string> a)
        {
            string repo = Repo(Directory.GetCurrentDirectory());
            var head = HeadModel.Load(Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"), .94f, new Vector3(0, .405f, .01f));
            var body = GripTrack.Load(a["body"]);
            var prev = LoadState(a["prev"], out Vector3 g0, out Vector3 gv);
            string outDir = a.TryGetValue("out", out var o) ? o : ".";
            Directory.CreateDirectory(outDir);
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            Console.WriteLine($"seam grip ({g0.X:F3} {g0.Y:F3} {g0.Z:F3}) step ({gv.X:F3} {gv.Y:F3} {gv.Z:F3}) head ({prev.P.X:F2} {prev.P.Y:F2} {prev.P.Z:F2}) v ({prev.V.X:F1} {prev.V.Y:F1} {prev.V.Z:F1})");
                        Path3s.Designed = a.ContainsKey("designed");
            var m = new RelModel(body, head, prev) { SeamVel = gv };
            m.Contact = F("contact", 9); m.Overhead = F("overhead", 5); m.VMax = F("vmax", 8); m.AMax = F("amax", 150); m.SeamWeight = F("seamw", 30);
            m.HandTop = F("handtop", 2.05f); m.AboveMin = F("above", .30f); m.Vert = F("vert", 0f); m.VertW = F("vertw", 60f); m.EndRadius = F("endr", 1.3f); m.HeadClear = F("headclr", .22f); m.ExitVMax = F("exitv", 4f); m.JumpW = F("jumpw", 3000f); m.ThighClear = F("thighclr", .15f); m.KeyW = F("keyw", 200f); m.ClrMin = F("clrmin", -.004f); m.ContactW = F("contactw", 400f); m.SmoothW = F("smoothw", 0f); m.LandY = F("landy", .27f); m.ReachL = F("reachl", .56f); m.ReachR = F("reachr", .55f); m.ReachW = F("reachw", 2000f);
            if (a.TryGetValue("suboff", out var so))
            {
                var r = GripTrack.Load(so); int sb = r.Sub; m.SubOff = new Vector3[r.Grip.Length];
                for (int i = 0; i < r.Grip.Length; i++)
                {
                    int f0 = Math.Min(i / sb, r.Frames - 1); float u = (i - f0 * sb) / (float)sb;
                    m.SubOff[i] = r.Grip[i] - Vector3.Lerp(r.Grip[f0 * sb], r.Grip[Math.Min((f0 + 1) * sb, r.Grip.Length - 1)], u);
                }
            }
            if (a.TryGetValue("handkeys", out var hk))
            {
                using var hd = JsonDocument.Parse(File.ReadAllBytes(hk));
                m.HandKeys = new Dictionary<int, Vector3>();
                foreach (var kv in hd.RootElement.EnumerateObject()) { m.HandKeys[int.Parse(kv.Name)] = V(kv.Value); if (kv.Value.GetArrayLength() > 3) m.HandKeyW[int.Parse(kv.Name)] = kv.Value[3].GetSingle(); }
            } m.FwdW = F("fwdw", 0f); m.FwdMin = F("fwdmin", .38f); m.FwdFrom = (int)F("fwdfrom", 2); m.FwdTo = (int)F("fwdto", 5); m.OverUp = F("overup", .12f); m.ForwardZ = F("fwdz", .5f); m.PoseW = F("posew", 1f); m.JerkW = F("jerkw", 20f);
            if (a.TryGetValue("impact", out var im)) { var q = im.Split(','); m.Impact = new Vector3(float.Parse(q[0], I), 0, float.Parse(q[1], I)); }
            int n = body.Frames;
            if (a.ContainsKey("eval"))
            {
                var p = Load(a["eval"], g0, n);
                var e = m.Evaluate(p, a.ContainsKey("exact"));
                var log = new List<string>(); m.InGameSeam(e, log);
                Print(e, m);
                string name = a.TryGetValue("name", out var nm) ? nm : "slam";
                if (!a.ContainsKey("exact")) WriteGrip(a["body"], Path.Combine(outDir, name + ".grip.json"), m.TrackOf(p), name);
                Save(Path.Combine(outDir, name + ".path.json"), p, e);
                WritePlan(Path.Combine(outDir, name + ".plan.json"), e, n);
                WriteStart(Path.Combine(outDir, name + ".start.anchorbake.json"), e.Start, name);
                File.WriteAllLines(Path.Combine(outDir, name + ".seam.txt"), new[] { $"in-game seam from the previous stage (AnchorBlend, deadline frame {m.Contact - 1}): at0 {e.SeamAt0:F3} m, residual {e.SeamResidual:F3} m, max accel {e.SeamAccel:F0} m/s², mean offset {e.SeamMean:F3} m" }.Concat(log));
                return 0;
            }
            Path3s best = a.TryGetValue("init", out var ip) ? Load(ip, g0, n) : Load(a["guess"], g0, n);
            var bestE = m.Evaluate(best);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            int gens = a.TryGetValue("gens", out var g) ? int.Parse(g) : 300, restarts = a.TryGetValue("restarts", out var rs) ? int.Parse(rs) : 3;
            int seed = a.TryGetValue("seed", out var sd) ? int.Parse(sd) : 1;
            double sigma = F("sigma", .05f);
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(best.ToVector(), sigma * (r == 0 ? 1 : .6), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask(); var cost = new double[pop.Length]; var ev = new Eval3s[pop.Length];
                    Parallel.For(0, pop.Length, k => { ev[k] = m.Evaluate(Path3s.FromVector(pop[k], g0, n)); cost[k] = ev[k].Cost; });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = ev[kb]; best = Path3s.FromVector(pop[kb], g0, n); }
                    if (gen % 25 == 0) Console.WriteLine($"r{r} g{gen} best {bestE.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestE));
                    if (bestE.Cost > last - 1e-4) stall++; else stall = 0;
                    last = Math.Min(last, bestE.Cost);
                    if (stall > 80 || cma.Sigma < 2e-4) break;
                }
                Save(Path.Combine(outDir, "best.path.json"), best, bestE);
                Console.WriteLine($"restart {r} done: {bestE.Cost:F3}");
            }
            Print(bestE, m);
            return 0;
        }

        static string Repo(string start)
        {
            var d = new DirectoryInfo(start);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "razlom"))) d = d.Parent;
            return d.FullName;
        }

        public static Rec LoadState(string spec, out Vector3 grip, out Vector3 step)
        {
            int at = spec.LastIndexOf('@');
            string file = spec.Substring(0, at); float t = float.Parse(spec.Substring(at + 1), I);
            using var d = JsonDocument.Parse(File.ReadAllBytes(file));
            var s = d.RootElement.GetProperty("samples").EnumerateArray().ToList();
            int k = Enumerable.Range(0, s.Count).OrderBy(i => Math.Abs(s[i].GetProperty("t").GetSingle() - t)).First();
            int k1 = Enumerable.Range(0, s.Count).OrderBy(i => Math.Abs(s[i].GetProperty("t").GetSingle() - (t - 1))).First();
            grip = V(s[k].GetProperty("grip")); step = grip - V(s[k1].GetProperty("grip"));
            var q = s[k].GetProperty("q");
            return new Rec { P = V(s[k].GetProperty("p")), V = V(s[k].GetProperty("v")), W = V(s[k].GetProperty("w")),
                Q = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()) };
        }

        public static Path3s Load(string path, Vector3 g0, int n)
        {
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            var p = new Path3s(n); int f = 0;
            foreach (var v in d.RootElement.GetProperty("frames").EnumerateArray()) { if (f > n) break; p.G[f++] = V(v); }
            for (; f <= n; f++) p.G[f] = p.G[f - 1];
            p.G[0] = g0;
            if (d.RootElement.TryGetProperty("dp", out var dp)) p.Dp = V(dp);
            if (d.RootElement.TryGetProperty("dv", out var dv)) p.Dv = V(dv);
            return p;
        }

        static string Terms(Eval3s e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | down {e.Down:F2} side {e.Side:F2} miss {e.V.ContactError:F2} land {e.V.FirstGroundTick:F2} v {e.V.ContactSpeed:F1} above {e.Above:F2} early {e.EarlyGround} slack {e.V.SlackFastFrames} jump {e.V.MaxJump:F3} pen {e.V.Penetration * 100:F1} clr {(e.V.ChainClearance == double.MaxValue ? 9 : e.V.ChainClearance) * 100:F1} seam {e.SeamAt0:F2}/{e.SeamResidual:F3}/{e.SeamAccel:F0}";

        public static void Print(Eval3s e, RelModel m)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows)
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            Console.WriteLine($"  seam: start offset {e.SeamAt0:F3} m, residual at f{m.Contact - 1} {e.SeamResidual:F3} m, accel {e.SeamAccel:F0} m/s², dp ({e.Start.P.X - m.Prev.P.X:F2} {e.Start.P.Y - m.Prev.P.Y:F2} {e.Start.P.Z - m.Prev.P.Z:F2}) dv ({e.Start.V.X - m.Prev.V.X:F1} {e.Start.V.Y - m.Prev.V.Y:F1} {e.Start.V.Z - m.Prev.V.Z:F1})");
            int last = (int)Math.Round(e.Run.Records[^1].Tick * 4);
            for (int k = 0; k <= last; k += 2)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                Console.WriteLine($"  f{k / 4f,5:0.00} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) |v| {x.V.Length():F1} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {x.Span:F2} T {x.Tension:F0}{(x.Grounded ? " ground" : "")}");
            }
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));
        static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        static void Save(string path, Path3s p, Eval3s e)
        {
            var o = new JsonObject
            {
                ["frames"] = new JsonArray(p.G.Select(v => (JsonNode)V3(v)).ToArray()), ["dp"] = V3(p.Dp), ["dv"] = V3(p.Dv), ["cost"] = R(e.Cost),
                ["terms"] = new JsonObject(e.Terms.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, R(kv.Value)))),
                ["contact"] = new JsonObject { ["miss"] = R(e.V.ContactError), ["landTick"] = R(e.V.FirstGroundTick), ["speed"] = R(e.V.ContactSpeed), ["above"] = R(e.Above) },
                ["seam"] = new JsonObject { ["at0"] = R(e.SeamAt0), ["residual"] = R(e.SeamResidual), ["accel"] = R(e.SeamAccel), ["mean"] = R(e.SeamMean) },
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void WriteGrip(string standIn, string path, GripTrack t, string clip)
        {
            var root = JsonNode.Parse(File.ReadAllText(standIn));
            root["clip"] = clip;
            var s = root["samples"].AsArray();
            for (int i = 0; i < s.Count; i++) s[i]["grip"] = V3(t.Grip[i]);
            File.WriteAllText(path, root.ToJsonString());
        }

        public static void WritePlan(string path, Eval3s e, int n)
        {
            var arr = new JsonArray();
            for (int k = 0; k <= 4 * n; k++)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                arr.Add(new JsonObject
                {
                    ["frame"] = R(k / 4.0), ["grip"] = V3(x.Grip), ["ring"] = V3(x.Ring), ["p"] = V3(x.P),
                    ["q"] = new JsonArray(R(x.Q.X), R(x.Q.Y), R(x.Q.Z), R(x.Q.W)), ["v"] = V3(x.V), ["span"] = R(x.Span), ["taut"] = x.Taut, ["ground"] = x.Grounded,
                });
            }
            File.WriteAllText(path, new JsonObject { ["axes"] = "Unity root: x right, y up, z forward, metres", ["chain"] = 1.6, ["samples"] = arr }.ToJsonString());
        }

        static void WriteStart(string path, Rec s, string name)
        {
            var o = new JsonObject
            {
                ["version"] = 1, ["clip"] = name + "_start",
                ["samples"] = new JsonArray(new JsonObject { ["t"] = 0, ["p"] = V3(s.P), ["q"] = new JsonArray(R(s.Q.X), R(s.Q.Y), R(s.Q.Z), R(s.Q.W)), ["v"] = V3(s.V), ["w"] = V3(s.W) }),
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
