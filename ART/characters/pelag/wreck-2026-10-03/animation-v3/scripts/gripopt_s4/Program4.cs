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

namespace GripOpt4
{
    /// <summary>
    /// gripopt_s4 --base Swing1.grip.json --start Swing1_w7.anchorbake.json@8 --contact 12 [--frames 17] [--copy 1] [--real Swing2_body.grip.json]
    ///            --out dir [--init path.json] [--gens 300] [--restarts 3] [--sigma .05] [--seed 1] [--fixyaw]
    /// gripopt_s4 ... --eval path.json --name X → table, X.path.json (grip + yaw), X.plan.json (quarter frames: grip, ring, head), X.grip.json (body + grip)
    /// gripopt_s4 --stow ... → StowProgram4 (live stow with a configurable back mount).   gripopt_s4 --slamprobe → SlamProbe.
    /// </summary>
    public static class Program4
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static string Repo(string start)
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
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            string outDir = a.TryGetValue("out", out var o) ? o : ".";
            Directory.CreateDirectory(outDir);
            if (a.ContainsKey("stow")) return StowProgram4.Run(a, head, outDir);
            if (a.ContainsKey("slam")) return SlamProgram4.Run(a);
            var baseT = GripTrack.Load(a["base"]);
            var real = a.TryGetValue("real", out var rp) ? GripTrack.Load(rp) : null;
            var start = LoadState(a["start"], out Vector3 g0, out Vector3 gv);
            int C = (int)F("contact", 12), n = (int)F("frames", C + 5), copy = (int)F("copy", 1);
            var m = new SpinModel(baseT, real, head, start)
            {
                SeamVel = gv, Contact = C, N = n, CopyFrom = C - copy, VRel = F("vrel", 8), AMax = F("amax", 150), PeakMax = F("peak", 31.5f),
                YawRate = F("yawrate", 33), SpeedGoal = F("vgoal", 25), JerkW = F("jerkw", 20), ContactY = F("cy", .72f), HandTop = F("handtop", 1.75f), JumpW = F("jumpw", 300), JumpAt = F("jumpat", .125f), ClrMin = F("clrmin", .004f), TorsoClear = F("torsoclr", .25f), ThighClear = F("thighclr", .14f), HandOut = F("handout", 0f), ArmMin = F("armmin", 0f), GripMin = F("gripmin", 0f), FrontMax = F("frontmax", 80f), ArcInterp = !a.ContainsKey("chord"), RealElbows = !a.ContainsKey("proxyelbows"), SpeedHardW = F("vhard", 0f),
            };
            if (a.TryGetValue("yawcap", out var yc))
            {   // "0-2:30,2-9:34,9-12:28" → cap per transition f→f+1
                m.YawCap = Enumerable.Repeat(m.YawRate, n).ToArray();
                foreach (var part in yc.Split(','))
                {
                    var kv = part.Split(':'); var rg = kv[0].Split('-');
                    for (int f = int.Parse(rg[0]); f < Math.Min(n, int.Parse(rg[1])); f++) m.YawCap[f] = float.Parse(kv[1], I);
                }
            }
            if (a.TryGetValue("twist", out var tws))
            {   // "0:18,3:42,6:30,10:20" → twist per frame (pchip-free: linear between knots)
                var kn = tws.Split(',').Select(t => t.Split(':')).Select(t => (f: float.Parse(t[0], I), v: float.Parse(t[1], I))).OrderBy(t => t.f).ToList();
                m.Twist = new float[n + 1];
                for (int f = 0; f <= n; f++)
                {
                    int j = kn.FindLastIndex(t => t.f <= f); if (j < 0) j = 0;
                    var k0 = kn[j]; var k1 = kn[Math.Min(j + 1, kn.Count - 1)];
                    m.Twist[f] = k1.f > k0.f ? k0.v + (k1.v - k0.v) * Math.Clamp((f - k0.f) / (k1.f - k0.f), 0, 1) : k0.v;
                }
            }
            Path4.FreeYaw = real != null || a.ContainsKey("fixyaw") ? 0 : m.CopyFrom - 1;
            Console.WriteLine($"seam grip ({g0.X:F3} {g0.Y:F3} {g0.Z:F3}) step ({gv.X:F3} {gv.Y:F3} {gv.Z:F3}) head ({start.P.X:F2} {start.P.Y:F2} {start.P.Z:F2}) v ({start.V.X:F1} {start.V.Y:F1} {start.V.Z:F1}) | C {C} N {n} copyFrom {m.CopyFrom} S1 psi8 {m.S1Psi[8]:F1} psi6 {m.S1Psi[6]:F1}");
            Path4 init = a.TryGetValue("init", out var ip) ? Load(ip, g0, n, m) : a.TryGetValue("eval", out var ev0) ? Load(ev0, g0, n, m) : Guess(m, g0, baseT);
            if (a.ContainsKey("eval"))
            {
                var e = m.Evaluate(init);
                Print(e, m, init);
                string name = a.TryGetValue("name", out var nm) ? nm : "swing2";
                WriteTrack(Path.Combine(outDir, name + ".grip.json"), e.Track, baseT, name);
                Save(Path.Combine(outDir, name + ".path.json"), init, e, m);
                WritePlan(Path.Combine(outDir, name + ".plan.json"), e, n);
                return 0;
            }
            var best = init; var bestE = m.Evaluate(best);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            int gens = (int)F("gens", 300), restarts = (int)F("restarts", 3), seed = (int)F("seed", 1); double sigma = F("sigma", .05f);
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(best.ToVector(), sigma * (r == 0 ? 1 : .6), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask(); var cost = new double[pop.Length]; var evs = new Eval4[pop.Length]; var tpl = best;
                    Parallel.For(0, pop.Length, k => { evs[k] = m.Evaluate(Path4.FromVector(pop[k], tpl)); cost[k] = evs[k].Cost; });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = evs[kb]; best = Path4.FromVector(pop[kb], tpl); }
                    if (gen % 25 == 0) Console.WriteLine($"r{r} g{gen} best {bestE.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestE));
                    if (bestE.Cost > last - 1e-4) stall++; else stall = 0;
                    last = Math.Min(last, bestE.Cost);
                    if (stall > 80 || cma.Sigma < 2e-4) break;
                }
                Save(Path.Combine(outDir, "best.path.json"), best, bestE, m);
                Console.WriteLine($"restart {r} done: {bestE.Cost:F3}");
            }
            Print(bestE, m, best);
            return 0;
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

        static float Smooth(float u) => u * u * (3 - 2 * u);

        static Path4 Guess(SpinModel m, Vector3 g0, GripTrack s1)
        {
            int n = m.N; var p = new Path4(n); p.G[0] = g0;
            float psi0 = m.S1Psi[8], psiC = m.S1Psi[Math.Max(0, m.CopyFrom - m.Contact + 7)] + 360f;
            for (int f = 0; f <= n; f++)
            {
                if (f >= m.CopyFrom) { int sf = Math.Min(s1.Frames, f - m.Contact + 7); p.Psi[f] = m.S1Psi[sf] + 360f; p.G[f] = s1.GripAt(sf); continue; }
                float u = f / (float)m.CopyFrom; p.Psi[f] = psi0 + (psiC - psi0) * (.35f * u + .65f * Smooth(u));
                double a = (p.Psi[f] - psi0) * Math.PI / 180; Vector3 c = s1.BoneAt(s1.Bone("Hips"), 8), d = s1.GripAt(8) - c;
                p.G[f] = c + new Vector3((float)(d.X * Math.Cos(a) - d.Z * Math.Sin(a)), d.Y + .1f * (float)Math.Sin(Math.PI * u), (float)(d.X * Math.Sin(a) + d.Z * Math.Cos(a)));
            }
            p.G[0] = g0;
            return p;
        }

        public static Path4 Load(string path, Vector3 g0, int n, SpinModel m)
        {
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            var p = new Path4(n); int f = 0;
            foreach (var v in d.RootElement.GetProperty("frames").EnumerateArray()) { if (f > n) break; p.G[f++] = V(v); }
            for (; f <= n; f++) p.G[f] = p.G[f - 1];
            p.G[0] = g0;
            var ps = d.RootElement.TryGetProperty("psi", out var pe) ? pe.EnumerateArray().Select(x => x.GetSingle()).ToList() : new List<float>();
            for (f = 0; f <= n; f++) p.Psi[f] = f < ps.Count ? ps[f] : (ps.Count > 0 ? ps[^1] : m.S1Psi[8]);
            for (f = m.CopyFrom; f <= n; f++) p.Psi[f] = m.S1Psi[Math.Min(m.Base.Frames, Math.Max(0, f - m.Contact + 7))] + 360f;
            return p;
        }

        static string Terms(Eval4 e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | c {e.V.ContactError:F2} r {e.V.ContactRadius:F2} h {e.V.ContactHeight:F2} ang {e.V.ContactAngle:F0} v {e.V.ContactSpeed:F1} peak {e.V.MaxSpeed:F1} slack {e.V.SlackFastFrames}/{e.V.MaxSlackFast * 100:F0} jump {e.V.MaxJump:F3} pen {e.V.Penetration * 100:F1} clr {(e.V.ChainClearance == double.MaxValue ? 9 : e.V.ChainClearance) * 100:F1}";

        public static void Print(Eval4 e, SpinModel m, Path4 p)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows)
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            Console.WriteLine("  psi " + string.Join(" ", p.Psi.Select(x => x.ToString("0", I))));
            int last = (int)Math.Round(e.Run.Records[^1].Tick * 4);
            for (int k = 0; k <= last; k += 2)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                Console.WriteLine($"  f{k / 4f,5:0.00} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) |v| {x.V.Length():F1} ang {Math.Atan2(x.P.X, x.P.Z) * 180 / Math.PI:F0} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {x.Span:F2} T {x.Tension:F0}");
            }
        }

        public static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        public static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));
        public static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        static void Save(string path, Path4 p, Eval4 e, SpinModel m)
        {
            var o = new JsonObject
            {
                ["frames"] = new JsonArray(p.G.Select(v => (JsonNode)V3(v)).ToArray()), ["psi"] = new JsonArray(p.Psi.Select(x => (JsonNode)R(x)).ToArray()),
                ["contact_frame"] = m.Contact, ["copy_from"] = m.CopyFrom, ["cost"] = R(e.Cost),
                ["twist"] = m.Twist == null ? null : new JsonArray(m.Twist.Select(x => (JsonNode)R(x)).ToArray()),
                ["terms"] = new JsonObject(e.Terms.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, R(kv.Value)))),
                ["contact"] = new JsonObject { ["error"] = R(e.V.ContactError), ["radius"] = R(e.V.ContactRadius), ["height"] = R(e.V.ContactHeight),
                    ["angle"] = R(e.V.ContactAngle), ["speed"] = R(e.V.ContactSpeed), ["peak"] = R(e.V.MaxSpeed) },
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void WritePlan(string path, Eval4 e, int n)
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

        /// <summary>A grip track file (anchor_grip_export format) of a proxy / authored body + this grip path.</summary>
        public static void WriteTrack(string path, GripTrack t, GripTrack src, string clip)
        {
            var s = new JsonArray();
            for (int i = 0; i < t.Grip.Length; i++)
                s.Add(new JsonObject
                {
                    ["t"] = R(i / (double)t.Sub / 30), ["frame"] = R(i / (double)t.Sub), ["grip"] = V3(t.Grip[i]), ["gripQ"] = new JsonArray(0, 0, 0, 1),
                    ["support"] = V3(t.Support[i]), ["supportQ"] = new JsonArray(0, 0, 0, 1), ["spine2Q"] = new JsonArray(0, 0, 0, 1),
                    ["bones"] = new JsonArray(t.Bones[i].Select(b => (JsonNode)V3(b)).ToArray()),
                });
            var o = new JsonObject
            {
                ["version"] = 1, ["clip"] = clip, ["hand"] = "Left", ["fps"] = 30, ["sub"] = t.Sub, ["frames"] = t.Frames,
                ["source"] = new JsonObject { ["fbx"] = "(proxy body: gripopt_s4)", ["sha256"] = "", ["bind"] = src.Bind ?? "", ["bindSha256"] = "" },
                ["units"] = new JsonObject { ["boneUnitMetres"] = src.BoneUnitMetres, ["restHeight"] = src.RestHeight },
                ["boneNames"] = new JsonArray(t.BoneNames.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()), ["samples"] = s,
            };
            File.WriteAllText(path, o.ToJsonString());
        }
    }
}
