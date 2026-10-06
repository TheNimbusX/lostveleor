using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — удар оземь и фронт по полосе (спека 5; кадры A, B-seagreen, C):
    ///  • УДАР ОЗЕМЬ (WreckSlam): кольцо воды раскрывается до круга Sim (ImpactRadius) за полтора
    ///    тика и рвётся, комья земли падают внутри круга, корона брызг стоячими каплями, короткая
    ///    тёмная трещина по полосе (.WreckSweep), толчок камеры 0,3; цвет — форма удара (Волнорез —
    ///    зелень, Девятый вал — индиго);
    ///  • ВАЛ (база, Панцирь, Девятый вал): стоячий гребень поперёк полосы бежит шагами фронта
    ///    Sim (губа = край урона в тик шага), белая пена на гребне, мокрый след за ним ~0,4 с, на
    ///    преграде — разбивается брызгами; высота и ширина горба Девятого вала — по доле урона;
    ///  • СТЕНА ВОЛНОРЕЗА: тот же гребень 1,1 м с загибом, ширина 2 м, несомые приподняты на груди
    ///    (.WreckCues), обрушение — всплеск 2 м и пена, о преграду — выше и шире;
    ///  • ГОРБ ДЕВЯТОГО ВАЛА (заряд): гребень стоит в точке удара, растёт с зарядом, ширина —
    ///    будущая полоса Sim; в удар его сменяет бегущий вал.
    /// Видимый край = край урона Sim (PelagWreckVfxRules.WaveFront, CraterCrest).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckWaveRun
        {
            public bool Active, Hump, EndBurst, Released;
            public int Fx = -1, Serial, ChargeStart = -1, ReleaseTick = -1, ReleasedCharge;
            public GameObject Object;
            public MeshFilter Crest, Trail;
            public ParticleSystem Spray, Foam;
            public readonly PelagWreckCrestWater CrestWater = new PelagWreckCrestWater();
            public readonly PelagWreckTrailWater TrailWater = new PelagWreckTrailWater();
            public readonly FormGroundGrid Ground = new FormGroundGrid();
            public WkWave Wave;
            public float Flow, SprayCarry, FoamCarry, BaseHalf, Shown;
            public PelagForm Form;
            public System.Func<float, float> AgeAt;
        }

        private sealed class WreckCraterRun
        {
            public bool Active;
            public int Fx = -1, Tick;
            public GameObject Object;
            public MeshFilter Ring;
            public readonly PelagAbordageRingWater Water = new PelagAbordageRingWater();
            public readonly FormGroundGrid Ground = new FormGroundGrid();
            public Vector3 Centre;
            public float Radius;
        }

        private readonly WreckWaveRun[] _wkWaves = { new WreckWaveRun(), new WreckWaveRun(), new WreckWaveRun() };
        private readonly WreckCraterRun[] _wkCraters = { new WreckCraterRun(), new WreckCraterRun(), new WreckCraterRun() };

        private WreckWaveRun TakeWreckWave()
        {
            WreckWaveRun pick = null;
            foreach (WreckWaveRun run in _wkWaves)
                if (!run.Active) { pick = run; break; }
                else if (pick == null || run.Wave.Tick < pick.Wave.Tick) pick = run;
            ReleaseWreckWave(pick);
            return pick;
        }

        private void ReleaseWreckWave(WreckWaveRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetWreckWaves()
        {
            foreach (WreckWaveRun run in _wkWaves) ReleaseWreckWave(run);
            foreach (WreckCraterRun run in _wkCraters)
            {
                if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
                run.Active = false;
                run.Object = null;
            }
        }

        private bool BindWreckWave(WreckWaveRun run, Vector3 root, float life)
        {
            int fx = WkSpawn(PelagVfxId.WreckCrest, root, Quaternion.identity, 0f, life, run.Form, out GameObject go);
            if (fx < 0) return false;
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            run.Crest = go.transform.Find("Crest")?.GetComponent<MeshFilter>();
            run.Trail = go.transform.Find("Trail")?.GetComponent<MeshFilter>();
            run.Spray = go.transform.Find("Spray")?.GetComponent<ParticleSystem>();
            run.Foam = go.transform.Find("Foam")?.GetComponent<ParticleSystem>();
            // Объект пула мог прийти из горба Девятого вала (след выключен) — у вала след снова виден.
            MeshRenderer trail = run.Trail != null ? run.Trail.GetComponent<MeshRenderer>() : null;
            if (trail != null) trail.enabled = true;
            run.CrestWater.Begin();
            run.TrailWater.Begin();
            return true;
        }

        /// <summary>Кольцо воды на земле до радиуса Sim (удар оземь, обрушение стены) с комьями и короной брызг.</summary>
        private void BeginWreckCrater(Vector3 centre, float radius, int tick, PelagForm form, bool clods, float splash)
        {
            WreckCraterRun run = null;
            foreach (WreckCraterRun c in _wkCraters)
                if (!c.Active) { run = c; break; }
                else if (run == null || c.Tick < run.Tick) run = c;
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            _abGroundBase = centre.y;
            if (_abGround == null) _abGround = AbordageGroundAt;
            run.Ground.Sample(centre, radius + 1f, _abGround);
            run.Centre = new Vector3(centre.x, run.Ground.Centre, centre.z);
            run.Radius = radius;
            run.Tick = tick;
            int fx = WkSpawn(PelagVfxId.WreckSlam, run.Centre, Quaternion.identity, 0f, PelagWreckVfxRules.CraterLifeSeconds, form, out GameObject go);
            if (fx < 0) return;
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            run.Ring = go.transform.Find("Ring")?.GetComponent<MeshFilter>();
            run.Water.Begin();
            run.Active = true;
            // Корона брызг стоячими каплями: слой «Splash» префаба — разовый, масштаб по кругу Sim.
            Transform crown = go.transform.Find("Splash");
            if (crown != null) crown.localScale = Vector3.one * Mathf.Max(.5f, radius / 1.2f) * splash;
            ParticleSystem lumps = clods ? go.transform.Find("Clods")?.GetComponent<ParticleSystem>() : null;
            if (lumps == null || CaptureRig.NoVfx) return;
            float g = 9.81f * lumps.main.gravityModifierMultiplier;
            for (int i = 0; i < 14; i++)
            {
                float angle = (i + Random.Range(-.35f, .35f)) * (Mathf.PI * 2f / 14);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float reach = radius * Random.Range(PelagWreckVfxRules.ClodReachMin, PelagWreckVfxRules.ClodReachMax);
                float flight = Random.Range(.30f, .45f);
                PelagWreckVfxRules.Launch(reach - .2f, flight, .1f, g, out float horizontal, out float vertical);
                lumps.Emit(new ParticleSystem.EmitParams
                {
                    position = run.Centre + radial * .2f + Vector3.up * .1f, velocity = radial * horizontal + Vector3.up * vertical,
                    startLifetime = flight, applyShapeToPosition = false
                }, 1);
            }
        }

        private void UpdateWreckCraters(float shown)
        {
            foreach (WreckCraterRun run in _wkCraters)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                float crest = PelagWreckVfxRules.CraterCrest(shown, run.Tick, run.Radius);
                float age = PelagWreckVfxRules.CraterBreakAge(shown, run.Tick);
                if (run.Ring != null)
                    run.Water.Build(FormWaterMesh.MeshFor(run.Ring, PelagAbordageRingWater.MeshName), run.Centre,
                        crest * .45f, crest, age, .25f, .3f * PelagWreckVfxRules.Seconds(shown - run.Tick), shown / Simulation.TicksPerSecond, 1f, run.Ground);
            }
        }

        /// <summary>Удар оземь (тик показа): круг, комья, корона, свет и толчок; горб Девятого вала сменяет бегущий вал.</summary>
        private void PlayWreckSlam(in WkPending p)
        {
            WkWave wave = p.Wave;
            // 06.10 поздно: выпад всех форм — стек серии сабли (.WreckComboLunge: стоячий всплеск, стоячий гребень по полосе,
            // камни, искры, след); формы — другой краской. Ловля, обрушение стены и заряд Девятого вала — свои, как были.
            if (PlayWreckComboLunge(p))
            {
                if (!CaptureRig.NoVfx) _juice?.PunchCamera(.30f, .06f);
                foreach (WreckWaveRun charged in _wkWaves)
                    if (charged.Active && charged.Hump && charged.Serial == p.Serial) ReleaseWreckWave(charged);
                return;
            }
            // 06.10 вечер: формы пока рисуются теми же рисованными кусками в цвете формы (свои эффекты — позже); стена
            // Волнореза и горб Девятого вала — их ловля, обрушение и заряд остаются своими (Catch/Crash/Charge).
            if (wave.Form != PelagForm.None && WreckPaintedLungeReady)
            {
                PlayWreckPaintedLunge(p);
                if (!CaptureRig.NoVfx) _juice?.PunchCamera(.30f, .06f);
                // Горб заряда Девятого вала уходит в удар, как и на пенном пути.
                foreach (WreckWaveRun charged in _wkWaves)
                    if (charged.Active && charged.Hump && charged.Serial == p.Serial) ReleaseWreckWave(charged);
                return;
            }
            if (WreckIronReady && wave.Form == PelagForm.None)
            {
                // База (и Панцирь): «холодное железо» вместо пены (.WreckIron).
                PlayWreckIronSlam(p);
                return;
            }
            BeginWreckCrater(wave.Impact, Mathf.Max(.3f, wave.ImpactRadius), p.Tick, wave.Form, true, 1f);
            if (!CaptureRig.NoVfx)
            {
                Vector3 foot = new Vector3(wave.Impact.x, WreckGroundAt(wave.Impact) + .02f, wave.Impact.z);
                WreckCrown(foot, wave.Dir, 1.4f * Mathf.Max(.8f, wave.ImpactRadius / 1.2f), wave.Form);
                PlayWreckCrack(wave.Impact, wave.Dir, wave.ImpactRadius);
                _juice?.PunchCamera(.30f, .06f);
                PulseCombatLight(p.Flag ? .7f : .55f);
            }
            foreach (WreckWaveRun hump in _wkWaves)
                if (hump.Active && hump.Hump && hump.Serial == p.Serial) ReleaseWreckWave(hump);
            if (wave.Travel <= 0 && !wave.Stopped) return;
            WreckWaveRun run = TakeWreckWave();
            run.Wave = wave;
            run.Hump = false;
            run.Serial = p.Serial;
            run.Form = wave.Form;
            run.Flow = run.SprayCarry = run.FoamCarry = 0f;
            run.EndBurst = false;
            float mid = .5f * (wave.Start + Mathf.Max(wave.Start, wave.End));
            _abGroundBase = wave.Origin.y;
            if (_abGround == null) _abGround = AbordageGroundAt;
            run.Ground.Sample(wave.Origin + wave.Dir * mid, .5f * Mathf.Max(1f, wave.End - wave.Start) + wave.HalfWidth + 1.5f, _abGround);
            run.AgeAt = run.AgeAt ?? (s => PelagWreckVfxRules.TrailAge(run.Shown, run.Wave.Tick, run.Wave.Start, run.Wave.Step, s));
            run.Active = BindWreckWave(run, wave.Origin, PelagWreckVfxRules.WaveLifeSeconds(wave.Travel));
        }
    }
}
