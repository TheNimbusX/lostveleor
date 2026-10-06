using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Крушение v2 — кадр фронта по полосе (вал, стена, горб), подхват и обрушение стены Волнореза.</summary>
    public sealed partial class PelagVfxController
    {
        private void UpdateWreckWaves(Simulation sim, float shown, float dt)
        {
            foreach (WreckWaveRun run in _wkWaves)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Fx = -1; run.Object = null; continue; }
                run.Shown = shown;
                if (run.Hump) { UpdateWreckHump(run, sim, shown, dt); continue; }
                WkWave w = run.Wave;
                float time = shown / Simulation.TicksPerSecond;
                float full = PelagWreckVfxRules.CrestHeight(w.Form, w.DamagePercent);
                float front = PelagWreckVfxRules.WaveFront(shown, w.Tick, w.Start, w.Step, w.Travel, w.End);
                float height = full * PelagWreckVfxRules.CrestRise(shown, w.Tick) * PelagWreckVfxRules.CrestCollapse(shown, w.Tick, w.Travel);
                float back = PelagWreckVfxRules.CrestBack(full);
                float age = PelagWreckVfxRules.CrestBreakAge(shown, w.Tick, w.Travel);
                float x = (shown - w.Tick + 1f) / Mathf.Max(1, w.Travel);
                float churn = .15f + .35f * PelagWreckVfxRules.Smooth01(x);
                run.Flow += dt * 3f;
                Vector3 root = run.Object.transform.position;
                if (run.Crest != null)
                    run.CrestWater.Build(FormWaterMesh.MeshFor(run.Crest, PelagWreckCrestWater.MeshName), root, w.Origin, w.Dir, front,
                        w.HalfWidth, Mathf.Max(.02f, height), back, PelagWreckVfxRules.CrestCurl(w.Form), age, churn, run.Flow, time, 1f, run.Ground);
                if (run.Trail != null)
                    run.TrailWater.Build(FormWaterMesh.MeshFor(run.Trail, PelagWreckTrailWater.MeshName), root, w.Origin, w.Dir, w.Start,
                        Mathf.Max(w.Start, front - back * .6f), w.HalfWidth, run.AgeAt, churn, run.Flow, time, 1f, run.Ground);
                if (!CaptureRig.NoVfx && dt > 0f)
                {
                    var perp = new Vector3(-w.Dir.z, 0f, w.Dir.x);
                    float speed = w.Step * Simulation.TicksPerSecond;
                    if (x < 1.05f && height > .1f)
                    {
                        // Брызги и клочья с бегущей губы по всей ширине полосы (кадр A — капли выше пояса).
                        float strength = Mathf.Clamp01(1.15f - x * .5f) * (1f + w.HalfWidth);
                        run.SprayCarry += dt * 45f * strength;
                        run.FoamCarry += dt * 18f * strength;
                        EmitWreckLip(run, run.Spray, ref run.SprayCarry, front, height, perp, speed, true);
                        EmitWreckLip(run, run.Foam, ref run.FoamCarry, front, height, perp, speed, false);
                    }
                    if (!run.EndBurst && shown >= PelagWreckVfxRules.WaveEndTick(w.Tick, w.Travel))
                    {
                        run.EndBurst = true;
                        // На преграде вал разбивается брызгами (стену Волнореза рушит своё событие).
                        if (w.Stopped && w.Form != PelagForm.WreckBreakwater)
                        {
                            float burst = 34f;
                            EmitWreckLip(run, run.Spray, ref burst, front, full, perp, speed * 1.6f, true);
                            Vector3 wall = w.Origin + w.Dir * front;
                            wall.y = WreckGroundAt(wall) + .02f;
                            WkSpawn(PelagVfxId.WreckBurst, wall, Quaternion.LookRotation(-w.Dir, Vector3.up), .9f, 0f, w.Form, out _);
                        }
                    }
                }
                if (shown > PelagWreckVfxRules.WaveEndTick(w.Tick, w.Travel) + PelagWreckVfxRules.CrestCollapseTicks + 20f) ReleaseWreckWave(run);
            }
        }

        /// <summary>Капли (spray) или клочья пены с губы гребня: по всей ширине, вперёд и вверх.</summary>
        private void EmitWreckLip(WreckWaveRun run, ParticleSystem system, ref float carry, float front, float height, Vector3 perp, float speed, bool spray)
        {
            int count = (int)carry;
            carry -= count;
            if (system == null || count <= 0) return;
            WkWave w = run.Wave;
            for (int i = 0; i < count; i++)
            {
                float across = Random.Range(-1f, 1f) * w.HalfWidth;
                Vector3 at = w.Origin + w.Dir * (front - Random.Range(0f, .25f)) + perp * across;
                at.y = run.Ground.At(at.x, at.z) + height * Random.Range(.6f, .95f);
                Vector3 velocity = spray
                    ? w.Dir * (speed * .22f + Random.Range(.5f, 2f)) + perp * Random.Range(-.8f, .8f) + Vector3.up * Random.Range(1.6f, 3.4f)
                    : w.Dir * (speed * .12f + Random.Range(.2f, .8f)) + Vector3.up * Random.Range(.3f, .9f);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = velocity, applyShapeToPosition = false,
                    startColor = PelagWreckFormLook.DropColor(run.Form, spray ? Random.Range(.35f, 1f) : Random.Range(0f, .3f))
                }, 1);
            }
        }

        // ---------------------------------------------------------------- Девятый вал: горб в точке удара

        private void BeginWreckHump(in WkPending p)
        {
            WreckWaveRun run = TakeWreckWave();
            Vector3 hero = PlayerPosition();
            run.Hump = true;
            run.Serial = p.Serial;
            run.Form = PelagForm.WreckNinthWave;
            run.ChargeStart = p.Tick;
            run.ReleaseTick = -1;
            run.BaseHalf = p.BaseHalf;
            run.Flow = run.SprayCarry = run.FoamCarry = 0f;
            run.Wave = new WkWave { Tick = p.Tick, Origin = hero, Dir = PlayerFacing(), Impact = p.At, Form = PelagForm.WreckNinthWave };
            _abGroundBase = hero.y;
            if (_abGround == null) _abGround = AbordageGroundAt;
            run.Ground.Sample(p.At, 3.5f, _abGround);
            run.Active = BindWreckWave(run, hero, 3f);
            if (run.Active && run.Trail != null) run.Trail.GetComponent<MeshRenderer>().enabled = false;
        }

        private void ReleaseWreckHump(in WkPending p)
        {
            foreach (WreckWaveRun run in _wkWaves)
            {
                if (!run.Active || !run.Hump || run.Serial != p.Serial) continue;
                run.ReleaseTick = p.Tick;
                run.ReleasedCharge = p.Amount;
                if (!p.Flag || CaptureRig.NoVfx) continue;
                // Полный заряд: горб вскипает брызгами.
                float burst = 30f;
                var perp = new Vector3(-run.Wave.Dir.z, 0f, run.Wave.Dir.x);
                float front = Vector3.Distance(new Vector3(run.Wave.Origin.x, 0f, run.Wave.Origin.z), new Vector3(run.Wave.Impact.x, 0f, run.Wave.Impact.z));
                EmitWreckLip(run, run.Spray, ref burst, front, PelagWreckVfxRules.HumpHeight(1f), perp, 3f, true);
            }
        }

        /// <summary>Горб растёт с зарядом (видимый рост = заряд), ширина — будущая полоса Sim, поворачивается за курсором по снимку.</summary>
        private void UpdateWreckHump(WreckWaveRun run, Simulation sim, float shown, float dt)
        {
            WreckState s = sim.Wreck;
            if (s.Serial == run.Serial && s.Stage == 2 && s.Phase != WreckPhase.None)
            {
                run.Wave.Origin = PlayerPosition();
                var dir = new Vector3(s.Direction.X.ToFloat(), 0f, s.Direction.Y.ToFloat());
                if (dir.sqrMagnitude > .25f) run.Wave.Dir = dir.normalized;
                run.Wave.Impact = WreckWorld(s.ImpactPoint, run.Wave.Origin.y);
            }
            float charge = PelagWreckVfxRules.ChargeShown(shown, run.ChargeStart, run.ReleaseTick, run.ReleasedCharge);
            float k = PelagWreckVfxRules.Charge01(charge);
            float rise = PelagWreckVfxRules.Smooth01((shown - run.ChargeStart) / 3f);
            float height = PelagWreckVfxRules.HumpHeight(k) * rise;
            float half = PelagWreckVfxRules.HumpHalfWidth(run.BaseHalf, charge);
            float reach = Vector3.Distance(new Vector3(run.Wave.Origin.x, 0f, run.Wave.Origin.z), new Vector3(run.Wave.Impact.x, 0f, run.Wave.Impact.z));
            run.Flow += dt * (2f + 3f * k);
            if (run.Crest != null)
                run.CrestWater.Build(FormWaterMesh.MeshFor(run.Crest, PelagWreckCrestWater.MeshName), run.Object.transform.position,
                    run.Wave.Origin, run.Wave.Dir, reach + .3f, half, Mathf.Max(.03f, height), PelagWreckVfxRules.CrestBack(height) * .8f,
                    PelagWreckVfxRules.CrestCurl(PelagForm.WreckNinthWave), 0f, .3f + .3f * k, run.Flow, shown / Simulation.TicksPerSecond, 1f, run.Ground);
            if (CaptureRig.NoVfx || dt <= 0f) return;
            run.SprayCarry += dt * (6f + 26f * k);
            var perp = new Vector3(-run.Wave.Dir.z, 0f, run.Wave.Dir.x);
            run.Wave.HalfWidth = half;
            EmitWreckLip(run, run.Spray, ref run.SprayCarry, reach + .3f, height, perp, 0f, true);
        }

        /// <summary>Серия кончилась без удара оземь (рывок, оглушение): горб опадает сразу.</summary>
        private void DropWreckHumps(int serial)
        {
            foreach (WreckWaveRun run in _wkWaves)
                if (run.Active && run.Hump && run.Serial == serial) ReleaseWreckWave(run);
        }

        // ---------------------------------------------------------------- Волнорез

        /// <summary>Стена подхватила: несомого вид приподнимает и откидывает на груди стены до обрушения; у ног — корона пены.</summary>
        private void PlayWreckCatch(in WkPending p)
        {
            Vector3 body = EntityPosition(p.Target, p.At);
            Vector3 dir = _wkHasWave && _wkWave.Dir.sqrMagnitude > .01f ? _wkWave.Dir : FlatDirection(PlayerPosition(), body);
            if (p.Flag) WreckCarry(p.Target, p.Tick, p.Tick + Mathf.Max(1, p.Amount), dir);
            if (CaptureRig.NoVfx) return;
            Vector3 foot = new Vector3(body.x, WreckGroundAt(body) + .02f, body.z);
            WreckCrown(foot, dir, p.Flag ? .9f : .7f, PelagForm.WreckBreakwater);
        }

        /// <summary>Обрушение стены: всплеск радиуса Sim (2 м) и белая пена, о преграду — выше и шире.</summary>
        private void PlayWreckCrash(in WkPending p)
        {
            float radius = p.Extra > 0 ? p.Extra / 100f : Simulation.WreckBreakwaterCrashRadius.ToFloat();
            float scale = PelagWreckVfxRules.CrashSplashScale(p.Flag);
            BeginWreckCrater(p.At, radius, p.Tick, PelagForm.WreckBreakwater, false, scale);
            if (CaptureRig.NoVfx) return;
            Vector3 dir = p.Wave.Dir.sqrMagnitude > .01f ? p.Wave.Dir : PlayerFacing();
            Vector3 foot = new Vector3(p.At.x, WreckGroundAt(p.At) + .02f, p.At.z);
            WkSpawn(PelagVfxId.WreckBurst, foot, Quaternion.LookRotation(p.Flag ? -dir : dir, Vector3.up), scale, 0f, PelagForm.WreckBreakwater, out _);
            WreckCrown(foot, dir, 1.2f * scale, PelagForm.WreckBreakwater);
            _juice?.PunchCamera(p.Flag ? .26f : .18f, .05f);
        }
    }
}
