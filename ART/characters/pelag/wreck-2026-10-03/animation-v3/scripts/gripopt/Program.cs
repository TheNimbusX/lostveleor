using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AnchorBake;
using Game.View;

namespace GripOpt
{
    /// <summary>
    /// gripopt --body &lt;stand-in.grip.json&gt; --out &lt;dir&gt; [--init path.json] [--gens 400] [--restarts 4] [--vmax 8] [--seed 1]
    /// gripopt --body &lt;grip.json&gt; --eval path.json --out &lt;dir&gt; [--name X]   (validation table, grip.json + start file for anchorbake)
    /// gripopt --body &lt;grip.json&gt; --seam &lt;bake.anchorbake.json&gt;              (in-game draw: OnBack -> Baked, rig blend)
    /// </summary>
    public static class Program
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
            var model = new Model(body, head);
            if (a.TryGetValue("vmax", out var vm)) model.VMax = float.Parse(vm, I);
            if (a.TryGetValue("reach", out var rc)) model.Reach = float.Parse(rc, I);
            if (a.TryGetValue("drawvmax", out var dv)) model.DrawVMax = float.Parse(dv, I);
            if (a.TryGetValue("v0w", out var vw)) model.StartSpeedWeight = float.Parse(vw, I);
            if (a.ContainsKey("free0")) PathParams.Free0 = true;
            if (a.ContainsKey("whirl")) { model.Whirl = true; PathParams.DS = .01f; }
            if (a.TryGetValue("degmin", out var dg)) model.DegMin = float.Parse(dg, I);
            if (a.TryGetValue("pose1", out var p1)) model.Pose1Weight = float.Parse(p1, I);
            if (a.TryGetValue("speedpull", out var spl)) model.SpeedPull = float.Parse(spl, I);
            if (a.TryGetValue("draw0", out var d0w)) model.Draw0Weight = float.Parse(d0w, I);
            if (a.TryGetValue("handhigh", out var hh)) model.HandHigh = float.Parse(hh, I);
            if (a.TryGetValue("handtop", out var ht)) model.HandTop = float.Parse(ht, I);
            if (a.TryGetValue("v0cap", out var vc)) model.V0Cap = float.Parse(vc, I);
            if (a.TryGetValue("dmax", out var dm)) model.DMax = float.Parse(dm, I);
            if (a.TryGetValue("seamw", out var sw)) model.SeamWeight = float.Parse(sw, I);
            string outDir = a.TryGetValue("out", out var o) ? o : ".";
            Directory.CreateDirectory(outDir);
            if (a.ContainsKey("seam")) return Seam.Run(a["seam"], body, head, a.TryGetValue("deadline", out var dl) ? dl == "1" : true,
                a.TryGetValue("seam-out", out var so) ? so : null);
            if (a.ContainsKey("eval"))
            {
                var p = Load(a["eval"], body.Grip[0]);
                var e = model.Evaluate(new BakeSim(model.TrackOf(p), head), p);
                Print(e);
                model.InGameSeam(e, true, model.TrackOf(p));
                string name = a.TryGetValue("name", out var nm) ? nm : "Pelag_AN_Wreck2_Swing1_v3plan";
                WriteGrip(a["body"], Path.Combine(outDir, name + ".grip.json"), model.TrackOf(p), name);
                WriteStart(Path.Combine(outDir, name + ".start.anchorbake.json"), e.Start);
                Save(Path.Combine(outDir, name + ".path.json"), p, e);
                WritePlan(Path.Combine(outDir, name + ".plan.json"), e);
                return 0;
            }
            PathParams init = a.TryGetValue("init", out var ip) ? Load(ip, body.Grip[0]) : Guess(body.Grip[0]);
            int gens = a.TryGetValue("gens", out var g) ? int.Parse(g) : 400;
            int restarts = a.TryGetValue("restarts", out var rs) ? int.Parse(rs) : 3;
            int seed = a.TryGetValue("seed", out var sd) ? int.Parse(sd) : 1;
            double sigma = a.TryGetValue("sigma", out var sg) ? double.Parse(sg, I) : .06;
            var sims = new ThreadLocal<BakeSim>(() => new BakeSim(body, head));
            PathParams best = init; Eval bestE = model.Evaluate(sims.Value, init);
            Console.WriteLine($"init cost {bestE.Cost:F2} " + Terms(bestE));
            for (int r = 0; r < restarts; r++)
            {
                var cma = new Cma(best.ToVector(), sigma * (r == 0 ? 1 : .6), 24, seed + 97 * r);
                double last = double.MaxValue; int stall = 0;
                for (int gen = 0; gen < gens; gen++)
                {
                    var pop = cma.Ask();
                    var cost = new double[pop.Length];
                    var evals = new Eval[pop.Length];
                    Parallel.For(0, pop.Length, k =>
                    {
                        var p = PathParams.FromVector(pop[k], body.Grip[0]);
                        evals[k] = model.Evaluate(sims.Value, p);
                        cost[k] = evals[k].Cost;
                    });
                    cma.Tell(cost);
                    int kb = Array.IndexOf(cost, cost.Min());
                    if (cost[kb] < bestE.Cost) { bestE = evals[kb]; best = PathParams.FromVector(pop[kb], body.Grip[0]); }
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

        static string Terms(Eval e) => string.Join(" ", e.Terms.Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", I)))
            + $" | seam {e.SeamAtContact:F3}/{e.SeamAt6:F3} pen {e.SeamPen:F3} clr {e.SeamClear:F2} front {e.SeamFront:F2} v0 {e.Start.V.Length():F1} | c {e.V.ContactError:F2} r {e.V.ContactRadius:F2} h {e.V.ContactHeight:F2} ang {e.V.ContactAngle:F0} v {e.V.ContactSpeed:F1} slack {e.V.SlackFastFrames} jump {e.V.MaxJump:F3} pen {e.V.Penetration * 100:F1}";

        public static void Print(Eval e)
        {
            Console.WriteLine("cost " + e.Cost.ToString("F3", I) + "  " + Terms(e));
            foreach (var r in e.V.Rows)
                Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL")} {r.Id,-3} {r.Name}: {r.Value} ({r.Target}) {r.Where}");
            for (int k = 0; k <= 48; k += 2)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                float span = (x.Ring - x.Grip).Length();
                Console.WriteLine($"  f{k / 4f,5:0.00} head ({x.P.X:F2} {x.P.Y:F2} {x.P.Z:F2}) |v| {x.V.Length():F1} grip ({x.Grip.X:F2} {x.Grip.Y:F2} {x.Grip.Z:F2}) span {span:F2} T {x.Tension:F0}");
            }
        }

        static PathParams Guess(Vector3 g0)
        {
            var p = new PathParams();
            Vector3[] g =
            {
                g0, new Vector3(.12f, 1.10f, .30f), new Vector3(.32f, .98f, .08f), new Vector3(.36f, .96f, .16f), new Vector3(.28f, .95f, .36f),
                new Vector3(.08f, .95f, .50f), new Vector3(-.16f, .95f, .52f), new Vector3(-.34f, .95f, .40f), new Vector3(-.42f, .95f, .26f),
                new Vector3(-.42f, .94f, .16f), new Vector3(-.38f, .93f, .12f), new Vector3(-.34f, .92f, .12f), new Vector3(-.30f, .92f, .12f),
            };
            Array.Copy(g, p.G, g.Length);
            p.D = new Vector3(.25f, -.05f, -.12f); p.V0 = new Vector3(7f, -3f, 3f);
            return p;
        }

        public static PathParams Load(string path, Vector3 g0)
        {
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            var r = d.RootElement; var p = new PathParams();
            int f = 0;
            foreach (var v in r.GetProperty("frames").EnumerateArray()) p.G[f++] = new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
            if (r.TryGetProperty("startDelta", out var sd)) p.D = new Vector3(sd[0].GetSingle(), sd[1].GetSingle(), sd[2].GetSingle());
            if (r.TryGetProperty("startVel", out var sv)) p.V0 = new Vector3(sv[0].GetSingle(), sv[1].GetSingle(), sv[2].GetSingle());
            if (!PathParams.Free0 && !r.TryGetProperty("keepFrame0", out _)) p.G[0] = g0;
            return p;
        }

        static void Save(string path, PathParams p, Eval e)
        {
            var o = new JsonObject
            {
                ["frames"] = new JsonArray(p.G.Select(v => (JsonNode)new JsonArray(R(v.X), R(v.Y), R(v.Z))).ToArray()),
                ["startDelta"] = V3(p.D), ["startVel"] = V3(p.V0), ["cost"] = R(e.Cost),
                ["seam"] = new JsonObject { ["atContact"] = R(e.SeamAtContact), ["atFrame6"] = R(e.SeamAt6), ["maxAccel"] = R(e.SeamMaxAccel) },
                ["terms"] = new JsonObject(e.Terms.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, R(kv.Value)))),
                ["start"] = new JsonObject { ["p"] = V3(e.Start.P), ["q"] = new JsonArray(R(e.Start.Q.X), R(e.Start.Q.Y), R(e.Start.Q.Z), R(e.Start.Q.W)) },
                ["contact"] = new JsonObject { ["error"] = R(e.V.ContactError), ["radius"] = R(e.V.ContactRadius), ["height"] = R(e.V.ContactHeight),
                    ["angle"] = R(e.V.ContactAngle), ["speed"] = R(e.V.ContactSpeed) },
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        static JsonNode R(double v) => JsonValue.Create(Math.Round(v, 5));
        static JsonArray V3(Vector3 v) => new JsonArray(R(v.X), R(v.Y), R(v.Z));

        /// <summary>Stand-in grip json with the grip replaced (anchorbake CLI input); bones stay the stand-in's.</summary>
        static void WriteGrip(string standIn, string path, GripTrack t, string clip)
        {
            var root = JsonNode.Parse(File.ReadAllText(standIn));
            root["clip"] = clip;
            var s = root["samples"].AsArray();
            for (int i = 0; i < s.Count; i++) s[i]["grip"] = V3(t.Grip[i]);
            File.WriteAllText(path, root.ToJsonString());
        }

        /// <summary>Per clip frame (and quarter frame): grip, ring, head centre/rotation of the bake - input of the Blender author.</summary>
        static void WritePlan(string path, Eval e)
        {
            var arr = new JsonArray();
            for (int k = 0; k <= 48; k++)
            {
                var x = BakeSim.At(e.Run, k / 120f);
                arr.Add(new JsonObject
                {
                    ["frame"] = R(k / 4.0), ["grip"] = V3(x.Grip), ["ring"] = V3(x.Ring), ["p"] = V3(x.P),
                    ["q"] = new JsonArray(R(x.Q.X), R(x.Q.Y), R(x.Q.Z), R(x.Q.W)), ["v"] = V3(x.V), ["span"] = R(x.Span), ["taut"] = x.Taut,
                });
            }
            var o = new JsonObject { ["axes"] = "Unity root: x right, y up, z forward, metres", ["chain"] = 1.6, ["samples"] = arr };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }

        /// <summary>Start state as a one-sample bake (anchorbake --start file@0).</summary>
        public static void WriteStart(string path, Rec s)
        {
            var o = new JsonObject
            {
                ["version"] = 1, ["clip"] = "Pelag_AN_Wreck2_Swing1_start", ["note"] = "pose 1: head at the apex of its swing behind-right, chain taut, v = 0",
                ["samples"] = new JsonArray(new JsonObject
                {
                    ["t"] = 0, ["p"] = V3(s.P), ["q"] = new JsonArray(R(s.Q.X), R(s.Q.Y), R(s.Q.Z), R(s.Q.W)), ["v"] = V3(s.V), ["w"] = V3(s.W),
                }),
            };
            File.WriteAllText(path, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
