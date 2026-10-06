using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace AnchorBake
{
    /// <summary>
    /// anchorbake --grip &lt;clip.grip.json&gt; [--kind swing|slam|loop|release|windup] [--contact 6] [--chain 1.6] [--name …]
    ///   swing:   контакт на дуге 2,2–2,7 м (по умолчанию)
    ///   slam:    --impact x,z (точка Sim в осях корня, 0,2.2) [--overhead 5]; касание земли в тик контакта
    ///   loop:    --loop from,to [--cycles 30]; записывается последний оборот, проверка замыкания
    ///   release: --start &lt;loop.anchorbake.json&gt; --variants 8 → _p0…_p7 (старт с фазы цикла через 45°), проверки slam
    ///   windup:  --release 2; скорость головы вдоль Direction в тик выпуска
    ///   общее:   [--start rest|back|taut:&lt;deg&gt;|&lt;prev.anchorbake.json&gt;@&lt;t&gt;] [--ground 0] [--retime-search] [--guide] [--tempo]
    ///            [--timing &lt;timing.json&gt; (sheathFrame)] [--timing-out &lt;file&gt;] [--anim-check …] [--capture-check …] [--out &lt;dir&gt;]
    /// Физика — Game.View.AnchorRigCore.StepLive (те же файлы, что в игре). Код выхода 0 — честная запечка проходит всё, 2 — нет.
    /// </summary>
    public static class Program
    {
        static readonly CultureInfo I = CultureInfo.InvariantCulture;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var a = Args.Parse(args);
            string repo = a.Get("repo", FindRepo());
            string hull = a.Get("hull", Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"));
            var head = HeadModel.Load(hull, a.F("head-size", .94f), a.V("eye", new Vector3(0, .405f, .01f)));
            var track = GripTrack.Load(a.Get("grip", null) ?? throw new ArgumentException("--grip is required"));
            if (a.Has("selftest")) return SelfTest.Run(head, track, a.Get("socket", Path.Combine(AppContext.BaseDirectory, "grip_socket.json"))) == 0 ? 0 : 3;
            var cfg = new BakeConfig
            {
                Kind = a.Get("kind", BakeKind.Swing), ChainLength = a.F("chain", 1.6f), ContactFrame = a.F("contact", 6f), Mass = a.F("mass", 12f),
                Gravity = a.F("gravity", 9.81f), PreRoll = a.F("preroll", 2f), BodyLimbs = !a.Has("no-limbs"), Ground = a.F("ground", 0f),
                OverheadFrame = a.F("overhead", 0f), ReleaseFrame = a.F("release", 0f), Cycles = (int)a.F("cycles", 30f),
            };
            if (!BakeKind.Valid(cfg.Kind)) throw new ArgumentException("--kind: " + cfg.Kind);
            Vector2 impact = a.V2("impact", new Vector2(0, 2.2f));
            cfg.Impact = new Vector3(impact.X, 0, impact.Y);
            if (cfg.Kind == BakeKind.Loop)
            {
                Vector2 loop = a.V2("loop", new Vector2(0, track.Frames));
                cfg.LoopFrom = loop.X; cfg.LoopTo = loop.Y; cfg.ContactFrame = 0;
            }
            cfg.SheathFrame = ReadSheathFrame(a.Get("timing", null), track.Clip);
            string outDir = a.Get("out", Path.GetDirectoryName(Path.GetFullPath(track.Path)));
            Directory.CreateDirectory(outDir);
            var ext = LoadExternal(a.Get("anim-check", null), a.Get("capture-check", null));
            var sim = new BakeSim(track, head);
            string[] starts = a.Get("start", "rest").Split(',');
            int variants = (int)a.F("variants", 0f);
            bool allPass = true;
            if (cfg.Kind == BakeKind.Release && variants > 0)
            {
                string loopFile = starts[0];
                float span = LoopSpan(loopFile);
                for (int k = 0; k < variants; k++)
                {
                    var c = Copy(cfg);
                    c.StartState = LoadSeam(loopFile + "@" + (span * k / variants).ToString("0.###", I), out string label);
                    c.Start = label;
                    allPass &= BakeOne(a, sim, c, track.Clip + "_p" + k, new[] { label }, head, track, ext, outDir);
                }
            }
            else
            {
                string name = a.Get("name", track.Clip + (cfg.Kind == BakeKind.Loop ? "" : "_w" + cfg.ContactFrame.ToString("0", I)));
                if (starts[0].Contains("@")) { cfg.StartState = LoadSeam(starts[0], out string label); starts[0] = label; }
                cfg.Start = starts[0];
                allPass = BakeOne(a, sim, cfg, name, starts, head, track, ext, outDir);
            }
            return allPass ? 0 : 2;
        }

        static bool BakeOne(Args a, BakeSim sim, BakeConfig cfg, string name, string[] starts, HeadModel head, GripTrack track,
            ExternalChecks ext, string outDir)
        {
            Make(sim, Copy(cfg), track, head, ext, "warm-up", 0);       // JIT out of the cost number
            var baseRun = Make(sim, Copy(cfg), track, head, ext, "honest", 0);
            double cost = baseRun.Run.MillisecondsPerFrame60;
            for (int k = 0; k < 10; k++) cost = Math.Min(cost, sim.Run(Copy(cfg)).MillisecondsPerFrame60);
            baseRun.Run.MillisecondsPerFrame60 = cost;
            baseRun.V = Validation.Run(baseRun.Run, track, head, ext);
            var (slackMean, slackPeak) = Output.BenchSlack(baseRun.Run, sim);
            baseRun.V.Add("16b", "Цена провисшей цепи (AnchorSlackChain рига, все кадры в провисе)", Validation.N(slackMean, "0.000") + " мс/кадр, пик " + Validation.N(slackPeak, "0.000"),
                "≤ 0,05 мс (.NET) ⇒ ≤ 0,15 мс под Mono", slackMean <= .05);
            Report.Console(name + " (" + cfg.Kind + ", честная запечка, старт " + cfg.Start + ")", baseRun.V);
            var others = new List<Variant>();
            for (int i = 1; i < starts.Length; i++)
            {
                var c = Copy(cfg); c.StartState = null; c.Start = starts[i];
                var v = Make(sim, c, track, head, ext, "старт " + starts[i], 0);
                others.Add(v);
                Report.Console(name + " (честная запечка, старт " + starts[i] + ")", v.V);
            }
            bool aimed = cfg.Kind == BakeKind.Swing || BakeKind.Ground(cfg.Kind);
            var retime = new List<Variant>();
            Variant best = baseRun;
            if (a.Has("retime-search") && aimed)
            {
                for (float s = -1.5f; s <= 1.5001f; s += .25f)
                {
                    var c = Copy(cfg); c.RetimeShift = s;
                    var v = Make(sim, c, track, head, ext, "retime " + s.ToString("+0.00;-0.00;0", I), s);
                    retime.Add(v);
                    if (v.V.ContactError < best.V.ContactError - 1e-6) best = v;
                }
                string timingOut = a.Get("timing-out", Path.Combine(outDir, track.Clip + ".timing-retime.json"));
                if (Path.GetFullPath(timingOut).Replace('\\', '/').Contains("/razlom/"))
                    Console.WriteLine("ретайм: в razlom/ не пишу (" + timingOut + ")");
                else
                {
                    Output.WriteRetime(timingOut, track.Clip, name, best.Shift, cfg.ContactFrame, track.Frames, baseRun.V.ContactError, best.V.ContactError);
                    Console.WriteLine("ретайм " + best.Shift.ToString("+0.00;-0.00;0", I) + " → " + timingOut);
                }
            }
            Variant guided = null;
            if (a.Has("guide") && aimed)
            {
                var c = Copy(cfg); c.RetimeShift = best.Shift;
                var run = sim.Guide(c, 6, out _);
                guided = new Variant { Label = "guided", Run = run, V = Validation.Run(run, track, head, ext), Shift = best.Shift };
                Report.Console(name + " (ретайм " + best.Shift.ToString("+0.00;-0.00;0", I) + " + наведение)", guided.V);
            }
            var tempo = new List<Variant>();
            if (a.Has("tempo"))
                foreach (float rate in new[] { .8f, 1.25f })
                {
                    var c = Copy(cfg); c.ClipRate = rate;
                    tempo.Add(Make(sim, c, track, head, ext, rate.ToString("0.00", I) + "×", 0));
                }
            string endsInto = a.Get("ends-into", "live");
            string L = cfg.ChainLength.ToString("0.0#", I);
            Output.WriteBake(Path.Combine(outDir, name + ".anchorbake.json"), name, baseRun.Run, track, head, baseRun.V, 0, cfg.Start, endsInto);
            Output.WriteSheet(Path.Combine(outDir, name + ".sheet.json"), name + " — " + cfg.Kind + ", честная запечка, L " + L + " м", baseRun.Run, sim, head, baseRun.V);
            if (guided != null)
            {
                Output.WriteBake(Path.Combine(outDir, name + ".guided.anchorbake.json"), name + ".guided", guided.Run, track, head, guided.V, guided.Shift, cfg.Start, endsInto);
                Output.WriteSheet(Path.Combine(outDir, name + ".guided.sheet.json"), name + " — ретайм " + guided.Shift.ToString("+0.00;-0.00;0", I) + " + наведение", guided.Run, sim, head, guided.V);
            }
            foreach (var o in others)
            {
                string tag = o.Run.Cfg.Start.Replace(":", "").Replace("-", "m");
                Output.WriteSheet(Path.Combine(outDir, name + "." + tag + ".sheet.json"), name + " — старт " + o.Run.Cfg.Start + ", L " + L + " м", o.Run, sim, head, o.V);
            }
            Report.Write(Path.Combine(outDir, name + ".report.md"), name, track, head, baseRun, retime, best, guided, tempo, cfg.Start, ext, others);
            Console.WriteLine($"BAKE {(baseRun.V.AllPass ? "PASS" : "FAIL")} {name} -> {outDir}");
            return baseRun.V.AllPass;
        }

        static Variant Make(BakeSim sim, BakeConfig c, GripTrack t, HeadModel h, ExternalChecks ext, string label, float shift)
        {
            var run = sim.Run(c);
            return new Variant { Label = label, Run = run, V = Validation.Run(run, t, h, ext), Shift = shift };
        }

        static BakeConfig Copy(BakeConfig c) => (BakeConfig)typeof(BakeConfig).GetMethod("MemberwiseClone",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(c, null);

        /// <summary>Seam start: the state of a previous bake at bake frame t (the "t" of its samples).</summary>
        static Rec? LoadSeam(string spec, out string label)
        {
            int at = spec.LastIndexOf('@');
            string file = spec.Substring(0, at); float frame = float.Parse(spec.Substring(at + 1), I);
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            Rec? best = null; float gap = float.MaxValue;
            foreach (var s in doc.RootElement.GetProperty("samples").EnumerateArray())
            {
                float f = s.GetProperty("t").GetSingle();
                if (Math.Abs(f - frame) >= gap) continue;
                gap = Math.Abs(f - frame);
                best = new Rec { P = V(s.GetProperty("p")), Q = Q(s.GetProperty("q")), V = V(s.GetProperty("v")), W = V(s.GetProperty("w")) };
            }
            label = Path.GetFileName(file).Replace(".anchorbake.json", "") + "@" + frame.ToString("0.##", I);
            return best;
        }

        static float LoopSpan(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var loop = doc.RootElement.GetProperty("loop");
            float span = loop.GetProperty("to").GetSingle() - loop.GetProperty("from").GetSingle();
            if (span <= 0) throw new InvalidDataException(file + ": не цикл (loop.to <= loop.from)");
            return span;
        }

        static float ReadSheathFrame(string timing, string clip)
        {
            if (timing == null || !File.Exists(timing)) return 0;
            using var d = JsonDocument.Parse(File.ReadAllBytes(timing));
            if (d.RootElement.TryGetProperty("clips", out var clips) && clips.TryGetProperty(clip, out var c)
                && c.TryGetProperty("sheathFrame", out var s) && s.ValueKind == JsonValueKind.Number) return s.GetSingle();
            return 0;
        }

        static ExternalChecks LoadExternal(string anim, string capture)
        {
            var e = new ExternalChecks();
            if (anim != null && File.Exists(anim))
            {
                using var d = JsonDocument.Parse(File.ReadAllBytes(anim));
                e.AnimSocket = d.RootElement.GetProperty("worstSocket").GetDouble();
                foreach (var f in d.RootElement.GetProperty("frames").EnumerateArray())
                    if (f.TryGetProperty("handAngleDeg", out var ang)) e.AnimAngle = Math.Max(e.AnimAngle, ang.GetDouble());
                e.AnimNote = "FK .anim; остаток — кадры, где Unity при импорте клипа сжала ключи (animationCompression 1, 0,5°)";
            }
            if (capture != null && File.Exists(capture))
            {
                using var d = JsonDocument.Parse(File.ReadAllBytes(capture));
                e.CaptureRms = d.RootElement.GetProperty("rmsAfterBlend").GetDouble();
                e.CaptureMax = d.RootElement.GetProperty("maxAfterBlend").GetDouble();
                e.CaptureNote = "колени совпадают ±3 см; кисть в игре выше клипа — позу меняют виды после аниматора (не решено; в игре риг пишет зазор в лог)";
            }
            return e;
        }

        static Vector3 V(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        static Quaternion Q(JsonElement e) => new Quaternion(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

        static string FindRepo()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "razlom"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }

    sealed class Args
    {
        readonly Dictionary<string, string> _v = new Dictionary<string, string>();
        public static Args Parse(string[] args)
        {
            var a = new Args();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string k = args[i].Substring(2);
                bool flag = i + 1 >= args.Length || args[i + 1].StartsWith("--");
                a._v[k] = flag ? "1" : args[++i];
            }
            return a;
        }
        public bool Has(string k) => _v.ContainsKey(k);
        public string Get(string k, string d) => _v.TryGetValue(k, out var v) ? v : d;
        public float F(string k, float d) => _v.TryGetValue(k, out var v) ? float.Parse(v, CultureInfo.InvariantCulture) : d;
        public Vector2 V2(string k, Vector2 d)
        {
            if (!_v.TryGetValue(k, out var v)) return d;
            var p = v.Split(',');
            return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
        }
        public Vector3 V(string k, Vector3 d)
        {
            if (!_v.TryGetValue(k, out var v)) return d;
            var p = v.Split(',');
            return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
        }
    }
}
