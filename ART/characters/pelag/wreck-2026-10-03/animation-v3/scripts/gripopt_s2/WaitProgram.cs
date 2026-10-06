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
    /// <summary>--wait &lt;loops&gt;: optimise / evaluate the grip loop of Wait1/Wait2 against the live pendulum.
    /// --eval path.json writes &lt;name&gt;.live.json (head, ring, grip per quarter frame over all loops: the render input).</summary>
    public static class WaitProgram
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static int Run(Dictionary<string, string> a, HeadModel head, GripTrack body, Rec start, Vector3 g0, Vector3 gv, string outDir)
        {
            int loops = int.Parse(a["wait"]);
            var m = new WaitModel(body, head, start, loops) { SeamVel = gv };
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            m.VMax = F("vmax", 6); m.AMax = F("amax", 120); m.Radius = F("radius", .28f); m.Clear = F("clear", .03f); m.SlowWeight = F("slow", .02f); m.SlackWeight = F("slackw", 1f); m.ClearWeight = F("clearw", 1f); m.YMin = F("ymin", .62f); m.YMax = F("ymax", 1.35f); m.NearTaut = F("neartaut", .10f); m.ReachL = F("reachl", .50f); m.ReachR = F("reachr", .54f);
            if (a.TryGetValue("robust", out var rb)) m.Robust = rb.Split(',').Select(x => float.Parse(x, I)).ToArray();
            bool arms = !a.ContainsKey("exact");     // exact: the body file already has the authored arms (exported clip)
            int L = body.Frames;
            if (a.ContainsKey("eval"))
            {
                var p = Program2.Load(a["eval"], g0, L);
                var e = m.Evaluate(p, arms);
                Print(e, m);
                string name = a.TryGetValue("name", out var nm) ? nm : "wait";
                WriteLive(Path.Combine(outDir, name + ".live.json"), e, L * loops);
                return 0;
            }
            var best = a.TryGetValue("init", out var ip) ? Program2.Load(ip, g0, L) : Hold(g0, L);
            var bestE = m.Evaluate(best, arms);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            int gens = a.TryGetValue("gens", out var g) ? int.Parse(g) : 300, restarts = a.TryGetValue("restarts", out var rs) ? int.Parse(rs) : 3;
            int seed = a.TryGetValue("seed", out var sd) ? int.Parse(sd) : 1;
            double sigma = F("sigma", .05f);
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(Vec(best), sigma * (r == 0 ? 1 : .6), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask();
                    var cost = new double[pop.Length]; var ev = new Eval2[pop.Length];
                    Parallel.For(0, pop.Length, k => { ev[k] = m.Evaluate(FromVec(pop[k], g0, L), arms); cost[k] = ev[k].Cost; });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = ev[kb]; best = FromVec(pop[kb], g0, L); }
                    if (gen % 25 == 0) Console.WriteLine($"r{r} g{gen} best {bestE.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestE));
                    if (bestE.Cost > last - 1e-4) stall++; else stall = 0;
                    last = Math.Min(last, bestE.Cost);
                    if (stall > 80 || cma.Sigma < 2e-4) break;
                }
                File.WriteAllText(Path.Combine(outDir, "best.path.json"), new JsonObject
                {
                    ["frames"] = new JsonArray(best.G.Select(v => (JsonNode)new JsonArray(R(v.X), R(v.Y), R(v.Z))).ToArray()), ["cost"] = R(bestE.Cost),
                }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"restart {r} done: {bestE.Cost:F3}");
            }
            Print(bestE, m);
            return 0;
        }

        static Path2 Hold(Vector3 g0, int L) { var p = new Path2(L); for (int f = 0; f <= L; f++) p.G[f] = g0; return p; }

        // loop: G[0] = G[L] = seam grip, free G[1..L-1]
        static double[] Vec(Path2 p) { var v = new List<double>(); for (int f = 1; f < p.N; f++) { v.Add(p.G[f].X); v.Add(p.G[f].Y); v.Add(p.G[f].Z); } return v.ToArray(); }
        static Path2 FromVec(double[] v, Vector3 g0, int L)
        {
            var p = new Path2(L); p.G[0] = p.G[L] = g0;
            for (int f = 1; f < L; f++) p.G[f] = new Vector3((float)v[3 * f - 3], (float)v[3 * f - 2], (float)v[3 * f - 1]);
            return p;
        }

        static string Terms(Eval2 e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | pen {e.V.Penetration * 100:F1} clr {(e.V.ChainClearance == double.MaxValue ? 9 : e.V.ChainClearance) * 100:F1} {e.V.ClearanceWhere} jump {e.V.MaxJump:F3} ground {e.V.GroundTime:F2}s | nearTaut clr {e.NearTautClear * 100:F1} {e.NearTautWhere}";

        static void Print(Eval2 e, WaitModel m)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows.Where(r => r.Id is "2" or "3" or "4" or "8" or "10" or "i1" or "i2" or "i3" or "16"))
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            int last = (int)Math.Round(e.Run.Records[^1].Tick * 4);
            for (int k = 0; k <= last; k += 4)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                Console.WriteLine($"  f{k / 4f,5:0.0} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) |v| {x.V.Length():F1} ang {Math.Atan2(x.P.X, x.P.Z) * 180 / Math.PI:F0} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {x.Span:F2} {(x.Grounded ? "ground" : "")}");
            }
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));

        static void WriteLive(string path, Eval2 e, int frames)
        {
            var arr = new JsonArray();
            for (int k = 0; k <= 4 * frames; k++)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                arr.Add(new JsonObject
                {
                    ["t"] = R(k / 4.0), ["grip"] = V3(x.Grip), ["ring"] = V3(x.Ring), ["p"] = V3(x.P),
                    ["q"] = new JsonArray(R(x.Q.X), R(x.Q.Y), R(x.Q.Z), R(x.Q.W)), ["v"] = V3(x.V), ["span"] = R(x.Span), ["taut"] = x.Taut,
                });
            }
            File.WriteAllText(path, new JsonObject { ["note"] = "live pendulum (AnchorRigCore.StepLive) under the Wait loop, not a bake", ["samples"] = arr }.ToJsonString());
        }
    }
}
