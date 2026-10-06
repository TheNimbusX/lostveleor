using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AnchorBake;

namespace Wreck4Anchor
{
    /// <summary>
    /// wreck4anchor &lt;plan.json&gt; &lt;out.json&gt; — голова якоря и цепь по серии Wreck4 (тело клипов не меняется).
    /// Стрельба: поправки наведения махов (поворот направления покоя булавы в колоколе вокруг контакта) и скорость выпуска
    /// броска (полёт — ядро игры) подбираются, пока промах ≤ 1,5 см. Выход — формат wreck4sim (+ h0/h1 рукояти, pen).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var plan = JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
            string repo = plan.GetProperty("repo").GetString();
            var head = HeadModel.Load(Path.Combine(repo, "razlom/Assets/Resources/Weapons/Pelag/AnchorDemo/AnchorHeadShape.asset"), .94f, new Vector3(0, .405f, .01f));
            var tl = new Timeline(plan);
            var sim = new Sim(plan, tl, head);
            Vector3 towardHand = Vector3.Normalize(tl.Grip(sim.TCon) - sim.ImpactPoint);
            sim.ImpactCentre = sim.ImpactPoint + Vector3.UnitY * (sim.Ground(sim.ImpactPoint) + .012f - .01f + sim.CrownDepth(Sim.ChainFrame(towardHand, Vector3.UnitY)));
            int passes = (int)Timeline.F(plan, "passes", 12);
            float[] miss = new float[sim.Hits.Count]; float impactMiss = 0;
            int passesRun = 0;
            for (int pass = 0; pass < passes; pass++)
            {
                sim.Run(); passesRun++;
                bool done = true;
                for (int i = 0; i < sim.Hits.Count; i++)
                {
                    Vector3 m = sim.Hits[i].Target - sim.HitGot[i]; miss[i] = m.Length();
                    if (miss[i] > .015f) done = false;
                    Vector3 E = tl.Grip(sim.Hits[i].T), d = Vector3.Normalize(sim.HitGot[i] - E);
                    var a = sim.Mace.Aims[i];
                    sim.Mace.Aims[i] = (a.Tc, a.A, a.B, a.Delta + Vector3.Cross(d, m) / sim.R * 1.2f);
                }
                if (sim.TRel >= 0)
                {
                    sim.ImpactCentre.Y = sim.ImpactPoint.Y + sim.Ground(sim.ImpactPoint) + .012f - .01f + sim.CrownDepth(sim.ImpactRot);
                    Vector3 m = sim.ImpactCentre - sim.ImpactGot; impactMiss = m.Length();
                    if (impactMiss > .015f) done = false;
                    if (!sim.HasV0) { sim.V0 = Release(sim); sim.HasV0 = true; }
                    sim.V0 += m / (sim.TCon - sim.TRel) * .95f;
                }
                Console.WriteLine($"pass {pass}: contacts {string.Join(", ", Array.ConvertAll(miss, x => x.ToString("0.000")))}  impact {impactMiss:0.000}");
                if (done && pass > 0) break;
            }
            var rep = new StringBuilder("[");
            for (int i = 0; i < sim.Hits.Count; i++)
                rep.Append((i > 0 ? ", " : "") + $"{{\"t\": {Sim.N_(sim.Hits[i].T)}, \"target\": {Sim.V_(sim.Hits[i].Target)}, \"got\": {Sim.V_(sim.HitGot[i])}, " +
                           $"\"speed\": {Sim.N_(sim.HitVel[i].Length())}, \"miss\": {Sim.N_(miss[i])}, \"aimDeg\": {Sim.N_(sim.Mace.Aims[i].Delta.Length() * 57.2958f)}}}");
            rep.Append("]");
            File.WriteAllText(args[1], sim.Json(rep.ToString()));
            float pen = 0; string penAt = "";
            foreach (var f in sim.Frames) if (f.Pen > pen) { pen = f.Pen; penAt = $"{f.Mode} t={f.T:0.000} {f.PenWhere}"; }
            Console.WriteLine($"W4A {args[1]} R {sim.R:0.000} contacts {rep}");
            foreach (var e in sim.Events) Console.WriteLine($"  {e.Key}: {e.Value}");
            Console.WriteLine($"  mountFlip {sim.MountFlip}, drawSpanMax {sim.DrawSpan:0.000} m, exitOffsetAtGrab {sim.ExitOffsetAtGrab:0.000} m, stowChainExcess {sim.StowExcess:0.000} m, hull-in-body max {pen:0.000} m ({penAt})");
            if (sim.WhipOn)
            {
                float frames60 = sim.TEnd * 60f;
                Console.WriteLine($"  whip: links {sim.WhipLinks}, substeps {sim._chainSubsteps()}, capsule tests {sim._chainTests()}, " +
                                  $"cost layer {sim.WhipClock.Elapsed.TotalMilliseconds / frames60 * 1000:0.0} us / solver {sim.SolverClock.Elapsed.TotalMilliseconds / frames60 * 1000:0.0} us per 60 Hz frame (last pass of {passesRun}), " +
                                  $"max strain {sim.WhipMaxStrain:0.0000} ({sim.WhipStrainAt}), max link-in-capsule {sim.WhipMaxPen:0.0000} m ({sim.WhipPenAt}), max head lag {sim.WhipMaxDev * 57.2958f:0.0} deg");
            }
            return 0;
        }

        static Frame FrameAt(Sim sim, float T)
        {
            Frame best = sim.Frames[0];
            foreach (var f in sim.Frames) if (Math.Abs(f.T - T) < Math.Abs(best.T - T)) best = f;
            return best;
        }

        static Vector3 Release(Sim sim)
        {
            var f = FrameAt(sim, sim.TRel); float Tf = sim.TCon - sim.TRel;
            return (sim.ImpactCentre - f.P) / Tf + new Vector3(0, 4.905f * Tf, 0);
        }
    }
}
