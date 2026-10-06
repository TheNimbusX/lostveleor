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
    /// <summary>gripopt_s3 --stow --body &lt;Stow grip.json (exported: real Spine2)&gt; --prev &lt;state&gt;@t --init path.json [--handframe 3]
    /// [--eval path.json --name X] → X.live.json (head/ring/grip per quarter frame until the catch) + X.stow.txt.</summary>
    public static class StowProgram4
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static int Run(Dictionary<string, string> a, HeadModel head, string outDir)
        {
            var body = GripTrack.Load(a["body"]); var prev = Program4.LoadState(a["prev"], out Vector3 g0, out Vector3 gv);
            var mq = (a.TryGetValue("mount", out var ms) ? ms : ".231,.280,-.169").Split(',').Select(x => float.Parse(x, I)).ToArray();
            Vector3? cl = null;
            if (a.TryGetValue("centre", out var cs)) { var c = cs.Split(',').Select(x => float.Parse(x, I)).ToArray(); cl = new Vector3(c[0], c[1], c[2]); }
            var m = new StowModel4(body, head, prev, new Vector3(mq[0], mq[1], mq[2]), cl) { SeamVel = gv };
            float F(string k, float d) => a.TryGetValue(k, out var s) ? float.Parse(s, I) : d;
            m.HandFrame = (int)F("handframe", 3); m.TorsoW = F("torsow", 0); m.VMax = F("vmax", 9); m.AMax = F("amax", 200); m.ReachL = F("reachl", .56f);
            int n = body.Frames;
            Vector3[] Load(string p)
            {
                using var d = JsonDocument.Parse(File.ReadAllBytes(p)); var G = new Vector3[n + 1]; int f = 0;
                foreach (var v in d.RootElement.GetProperty("frames").EnumerateArray()) { if (f > n) break; G[f++] = Program4.V(v); }
                for (; f <= n; f++) G[f] = G[f - 1];
                G[0] = g0; return G;
            }
            if (a.ContainsKey("eval"))
            {
                var G = Load(a["eval"]); var r = m.Evaluate(G);
                string name = a.TryGetValue("name", out var nm) ? nm : "stow";
                var lines = Report(m, r, G); foreach (var l in lines) Console.WriteLine(l);
                File.WriteAllLines(Path.Combine(outDir, name + ".stow.txt"), lines);
                var arr = new JsonArray();
                for (int k = 0; k < r.S.Count; k += 1)
                {
                    var s = r.S[k]; if (k % 1 != 0) continue;
                    arr.Add(new JsonObject { ["t"] = R(s.t * 30), ["p"] = V3(s.p), ["q"] = new JsonArray(R(s.q.X), R(s.q.Y), R(s.q.Z), R(s.q.W)), ["v"] = V3(s.v),
                        ["ring"] = V3(s.ring), ["grip"] = V3(s.grip), ["span"] = R(s.span), ["step"] = s.step });
                }
                var back = new JsonArray();
                for (int f = 0; f <= n; f++) back.Add(new JsonObject { ["frame"] = f, ["grip"] = V3(m.BackGrip(f)), ["centre"] = V3(m.BackCentre(f)), ["ring"] = V3(m.BackRing(f)),
                    ["q"] = Q4(m.BackRot(f)) });
                File.WriteAllText(Path.Combine(outDir, name + ".live.json"), new JsonObject { ["note"] = "Stow: live physics (AnchorRigCore.StepLive) + the rig's stow (hand → reel 5 m/s → catch), t in clip frames (120 Hz samples)",
                    ["handT"] = R(r.HandT * 30), ["catchT"] = R(r.CatchT * 30), ["caught"] = r.Caught, ["samples"] = arr, ["back"] = back }.ToJsonString());
                return 0;
            }
            var best = Load(a["init"]); var bestR = m.Evaluate(best);
            Console.WriteLine($"init cost {bestR.Cost:F2} " + Terms(bestR));
            int gens = (int)F("gens", 300), restarts = (int)F("restarts", 3), seed = (int)F("seed", 1); double sigma = F("sigma", .05f);
            int h = m.HandFrame;
            double[] Vec(Vector3[] G) { var v = new List<double>(); for (int f = 1; f <= h; f++) { v.Add(G[f].X); v.Add(G[f].Y); v.Add(G[f].Z); } return v.ToArray(); }
            Vector3[] From(double[] v, Vector3[] baseG) { var G = (Vector3[])baseG.Clone(); for (int f = 1; f <= h; f++) G[f] = new Vector3((float)v[3 * f - 3], (float)v[3 * f - 2], (float)v[3 * f - 1]); return G; }
            for (int rr = 0; rr < restarts; rr++)
            {
                var cma = new Cma(Vec(best), sigma * (rr == 0 ? 1 : .6), 24, seed + 97 * rr); double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask(); var cost = new double[pop.Length]; var ev = new StowModel4.Run[pop.Length]; var bg = best;
                    Parallel.For(0, pop.Length, k => { ev[k] = m.Evaluate(From(pop[k], bg)); cost[k] = ev[k].Cost; });
                    cma.Tell(cost); int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestR.Cost) { bestR = ev[kb]; best = From(pop[kb], bg); }
                    if (gen % 25 == 0) Console.WriteLine($"r{rr} g{gen} best {bestR.Cost:F3} sigma {cma.Sigma:F4} " + Terms(bestR));
                    if (bestR.Cost > last - 1e-4) stall++; else stall = 0; last = Math.Min(last, bestR.Cost);
                    if (stall > 60 || cma.Sigma < 2e-4) break;
                }
            }
            File.WriteAllText(Path.Combine(outDir, "best.path.json"), new JsonObject { ["frames"] = new JsonArray(best.Select(v => (JsonNode)V3(v)).ToArray()), ["cost"] = R(bestR.Cost) }.ToJsonString());
            foreach (var l in Report(m, bestR, best)) Console.WriteLine(l);
            return 0;
        }

        static string Terms(StowModel4.Run r) => string.Join(" ", r.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | hand {r.HandT * 30:F2} gap {r.HandGap * 100:F1}cm catch {r.CatchT * 30:F2} caught {r.Caught} pen {r.Pen * 100:F1} {r.PenWhere} clr {r.Clear * 100:F1} {r.ClearWhere} jump {r.Jump:F3}";

        static List<string> Report(StowModel4 m, StowModel4.Run r, Vector3[] G)
        {
            var o = new List<string> { "cost " + r.Cost.ToString("F3", I) + "  " + Terms(r) };
            o.Add($"  {(r.Timeout ? "FAIL" : "ok  ")} S1 Кисть у крепления рукояти на спине: кадр {r.HandT * 30:F2}, зазор {r.HandGap * 100:F1} см (≤ 4 см; иначе риг ждёт 0,35 с и тянет рукоять 0,22 с)");
            o.Add($"  {(r.Caught ? "ok  " : "FAIL")} S2 Голова поймана креплением: кадр {r.CatchT * 30:F2} ({(r.CatchT - r.HandT):F2} с намотки; ≤ 0,9 с), зазор {r.CatchGap * 100:F1} см, скорость {r.CatchSpeed:F1} м/с (≤ 10 см, ≤ 2,5 м/с)");
            o.Add($"  {(r.Pen <= .01 ? "ok  " : "FAIL")} 2  Голова в тело (руки, голова, ноги; корпус в намотке игра не толкает): {r.Pen * 100:F1} см (≤ 1 см) {r.PenWhere}");
            o.Add($"  {(r.Clear >= -.01 ? "ok  " : "FAIL")} 3  Натянутая цепь сквозь тело: {r.Clear * 100:F1} см (≥ −1 см) {r.ClearWhere}");
            o.Add($"  {(r.Jump < .15 ? "ok  " : "FAIL")} 8  Скачок за кадр 60 к/с: {r.Jump:F3} м (< 0,15 м)");
            o.Add($"  info T  Голова сквозь корпус в намотке (игра корпус не толкает): {r.TorsoPen * 100:F1} см {r.TorsoWhere}");
            o.Add($"  info M  Крепление рукояти (Spine2, Unity, м): ({m.GripLocal.X:F3}; {m.GripLocal.Y:F3}; {m.GripLocal.Z:F3}); центр головы на спине ({m.CentreLocal.X:F3}; {m.CentreLocal.Y:F3}; {m.CentreLocal.Z:F3})");
            for (int k = 0; k < r.S.Count; k += 4)
            { var s = r.S[k]; o.Add($"  f{s.t * 30,5:0.00} step {s.step} head ({s.p.X:F2} {s.p.Y:F2} {s.p.Z:F2}) |v| {s.v.Length():F1} grip ({s.grip.X:F2} {s.grip.Y:F2} {s.grip.Z:F2}) span {s.span:F2}{(s.ground ? " ground" : "")}"); }
            return o;
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));
        static JsonArray Q4(Quaternion q) => new JsonArray(R(q.X), R(q.Y), R(q.Z), R(q.W));
    }
}
