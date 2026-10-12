using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение · ВОЛНОРЕЗ (07.10; ассеты — Editor/PelagWreckComboVfxSetup.Breakwater, числа — PelagWreckBreakwaterLook; целевой
    /// кадр ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-breakwater.png). Из PlayWreckSlam после стека
    /// выпада базы (он рисует удар в морской зелени): гребень и куски полосы базы этого удара снимаются, вместо них — СТЕНА
    /// освещённых 3D-плит. Каждая плита встаёт ровно в тик показа, когда фронт стены Sim (PelagWreckVfxRules.WaveFront —
    /// то же, что TryGetWreckWave) проходит её место: из-под земли с перелётом за 2–3 кадра, наклоном вперёд, пылью и
    /// комьями у подошвы, светом морской зелени из трещин и стоячей полосой света в щели за ней; за фронтом оседает.
    /// Хвост у якоря крошится раньше, остальное — в обрушение Sim (WreckBreakwaterCrash): плиты рвутся по трещинам в
    /// обломки (3D-камни), пыль, зелёные искры. Подхват, несомые и обрушение — .WreckBreakwaterBits. Тела не двигаются здесь.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private struct WbSlab
        {
            public bool Popped, Broken, Hidden;
            public float Pass, Crumble, Shade;
        }

        private sealed class WreckBreakRun
        {
            public bool Active, Crashed;
            public int Fx = -1, Tick, Travel, Frame;
            public float Start, End, Step, Half, CrashTick, CrashShown = -100f;
            public GameObject Object;
            public Vector3 Root, Origin, Dir, Side;
            public PelagWreckBreakwaterLook.Slab[] Slabs;
            public WbSlab[] States;
            public readonly MeshRenderer[] Renderers = new MeshRenderer[PelagWreckBreakwaterLook.PoolSize];
            public ParticleSystem Seams, GroundGlow, GroundCrack, Dust, Clods, Sparks, Chunks, HalfFx, Star;
            public Light Light;
            public readonly List<int> Carried = new List<int>();
            public readonly List<float> CarriedUntil = new List<float>();
            public float CarryClock;
        }

        private readonly WreckBreakRun[] _wbRuns = { new WreckBreakRun(), new WreckBreakRun() };
        private MaterialPropertyBlock _wbBlock;
        private static readonly int WbGlowId = Shader.PropertyToID("_Glow"), WbCrumbleId = Shader.PropertyToID("_Crumble"),
            WbTintId = Shader.PropertyToID("_Tint");

        private bool WreckBreakwaterReady => _pools != null && (int)PelagVfxId.WreckBreakWall < _pools.Length
            && _pools[(int)PelagVfxId.WreckBreakWall] != null;

        /// <summary>Удар выпада Волнореза (тик показа): стена плит по полосе Sim. Без полосы или префаба — ничего.</summary>
        private void PlayWreckBreakwaterWall(in WkPending p)
        {
            WkWave wave = p.Wave;
            if (wave.Look != PelagForm.WreckBreakwater || CaptureRig.NoVfx || !WreckBreakwaterReady) return;
            if (wave.Travel <= 0 && !wave.Stopped) return;
            // Гребень и куски полосы базы этого удара снимаются: их место — плиты (удар в точке — база в зелени).
            foreach (WreckComboLungeRun baseRun in _wclRuns)
                if (baseRun.Active && baseRun.Tick == p.Tick)
                {
                    baseRun.Lane = false;
                    baseRun.Ended = true;
                    HideWreckComboCrest(baseRun);
                }
            WreckBreakRun run = _wbRuns[0].Active && (!_wbRuns[1].Active || _wbRuns[1].Tick < _wbRuns[0].Tick) ? _wbRuns[1] : _wbRuns[0];
            ReleaseWreckBreakRun(run);
            System.Func<float, float, float> ground = WreckIronGroundAt(wave.Impact.y);
            var root = new Vector3(wave.Impact.x, ground(wave.Impact.x, wave.Impact.z), wave.Impact.z);
            Vector3 dir = wave.Dir.sqrMagnitude > .01f ? new Vector3(wave.Dir.x, 0f, wave.Dir.z).normalized : PlayerFacing();
            if (!TryAcquire(PelagVfxId.WreckBreakWall, out GameObject go, out PelagVfxElement element)) return;
            int travel = Mathf.Max(1, wave.Travel);
            int index = ReserveActive();
            element.Begin(root, Quaternion.LookRotation(dir, Vector3.up));
            go.transform.localScale = Vector3.one;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckBreakWall, Object = go, Element = element,
                Duration = PelagWreckVfxRules.Seconds(travel + PelagWreckBreakwaterLook.CollapseRippleTicks + 2f) + 2.1f,
                Start = root, End = root, Motion = Motion.Static, FollowIndex = -1
            };
            run.Active = true;
            run.Crashed = false;
            run.Fx = index;
            run.Object = go;
            run.Tick = p.Tick;
            run.Travel = travel;
            run.Frame = Time.frameCount;
            run.Root = root;
            run.Origin = wave.Origin;
            run.Dir = dir;
            run.Side = new Vector3(-dir.z, 0f, dir.x);
            run.Start = wave.Start;
            run.End = Mathf.Max(wave.Start + .1f, wave.End);
            run.Step = Mathf.Max(.05f, wave.Step);
            run.Half = Mathf.Max(.3f, wave.HalfWidth);
            run.CrashTick = p.Tick + travel;
            run.CrashShown = -100f;
            run.Carried.Clear();
            run.CarriedUntil.Clear();
            run.CarryClock = 0f;
            Transform t = go.transform;
            Transform slabs = t.Find("Slabs");
            for (int i = 0; i < run.Renderers.Length; i++)
            {
                Transform child = slabs != null && i < slabs.childCount ? slabs.GetChild(i) : null;
                run.Renderers[i] = child != null ? child.GetComponent<MeshRenderer>() : null;
                if (run.Renderers[i] != null) run.Renderers[i].enabled = false;
            }
            run.Seams = t.Find("Seams")?.GetComponent<ParticleSystem>();
            run.GroundGlow = t.Find("GroundGlow")?.GetComponent<ParticleSystem>();
            run.GroundCrack = t.Find("GroundCrack")?.GetComponent<ParticleSystem>();
            run.Dust = t.Find("Dust")?.GetComponent<ParticleSystem>();
            run.Clods = t.Find("Clods")?.GetComponent<ParticleSystem>();
            run.Sparks = t.Find("Sparks")?.GetComponent<ParticleSystem>();
            run.Chunks = t.Find("Chunks")?.GetComponent<ParticleSystem>();
            run.HalfFx = t.Find("Half")?.GetComponent<ParticleSystem>();
            run.Star = t.Find("Star")?.GetComponent<ParticleSystem>();
            run.Light = t.Find("Light")?.GetComponent<Light>();
            if (run.Light != null) run.Light.intensity = 0f;
            // Раскладка — от точки удара до конца стены Sim, ширина — полоса Sim; плиты встают по фронту.
            run.Slabs = PelagWreckBreakwaterLook.Layout(run.Start, run.End, run.Half, p.Tick * 7919 + p.Serial * 31 + 17);
            run.States = new WbSlab[run.Slabs.Length];
            for (int i = 0; i < run.Slabs.Length; i++)
            {
                float pass = PelagWreckBreakwaterLook.PassTick(run.Slabs[i].Along, run.Tick, run.Start, run.Step);
                run.States[i] = new WbSlab
                {
                    Pass = pass, Shade = Random.Range(.90f, 1.06f),
                    Crumble = PelagWreckBreakwaterLook.CrumbleTick(pass, run.CrashTick, run.Slabs[i].Along, run.Start, run.End)
                };
            }
            UpdateWreckBreakRun(run, p.Tick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-breakwater] wall tick={p.Tick} start={run.Start:F2} end={run.End:F2} half={run.Half:F2} step={run.Step:F2} travel={travel} slabs={run.Slabs.Length} stopped={wave.Stopped}");
        }

        /// <summary>Кадр (UpdateWreckVfx, тик показа): плиты, куски, свет фронта, крошка под несомыми.</summary>
        private void UpdateWreckBreakwater(float shown)
        {
            foreach (WreckBreakRun run in _wbRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { HideWreckBreakSlabs(run); run.Active = false; run.Object = null; continue; }
                UpdateWreckBreakRun(run, shown);
            }
        }

        private void UpdateWreckBreakRun(WreckBreakRun run, float shown)
        {
            // Кадр рождения: залп удара базы выходит на следующем обновлении частиц — плиты ждут его.
            if (Time.frameCount == run.Frame) return;
            if (_wbBlock == null) _wbBlock = new MaterialPropertyBlock();
            System.Func<float, float, float> ground = WreckIronGroundAt(run.Root.y);
            for (int i = 0; i < run.Slabs.Length; i++) PoseWreckBreakSlab(run, i, shown, ground);
            UpdateWreckBreakCarry(run, shown);
            if (run.Light != null)
            {
                float front = PelagWreckVfxRules.WaveFront(shown, run.Tick, run.Start, run.Step, run.Travel, run.End);
                Vector3 at = run.Origin + run.Dir * Mathf.Max(run.Start, front - .35f);
                at.y = ground(at.x, at.z) + 1.0f;
                run.Light.transform.position = at;
                float flash = PelagWreckVfxRules.Seconds(shown - run.CrashShown);
                float travel = shown < run.CrashTick ? 2.0f : 0f;
                run.Light.intensity = Mathf.Max(travel, flash >= 0f && flash < .4f ? 4.6f * (1f - flash / .4f) : 0f);
            }
        }

        /// <summary>Поза плиты на тик показа: выход с перелётом и наклоном, оседание, распад (наклон, в землю, рвётся).</summary>
        private void PoseWreckBreakSlab(WreckBreakRun run, int i, float shown, System.Func<float, float, float> ground)
        {
            ref WbSlab state = ref run.States[i];
            PelagWreckBreakwaterLook.Slab slab = run.Slabs[i];
            MeshRenderer renderer = run.Renderers[slab.Child];
            if (renderer == null || state.Hidden) return;
            if (shown < state.Pass) { renderer.enabled = false; return; }
            float age = PelagWreckVfxRules.Seconds(shown - state.Pass);
            float k = PelagWreckVfxRules.Seconds(shown - state.Crumble) / PelagWreckBreakwaterLook.CrumbleSeconds;
            Vector3 foot = run.Origin + run.Dir * slab.Along + run.Side * slab.Across;
            foot.y = ground(foot.x, foot.z);
            if (!state.Popped)
            {
                state.Popped = true;
                EmitWreckBreakPop(run, slab, foot, PelagWreckVfxRules.Seconds(state.Crumble - state.Pass));
            }
            if (k >= 1f) { renderer.enabled = false; state.Hidden = true; return; }
            if (k >= PelagWreckBreakwaterLook.Break && !state.Broken)
            {
                state.Broken = true;
                EmitWreckBreakCrumble(run, slab, foot);
            }
            float crumble = Mathf.Max(0f, k);
            float lean = PelagWreckBreakwaterLook.LeanAt(age, slab.Lean) + PelagWreckBreakwaterLook.CrumbleLean * PelagWreckBreakwaterLook.Smooth01(crumble);
            float stretch = PelagWreckBreakwaterLook.PopStretch(age);
            // Низ плиты (скруглённый камень) — немного под землёй: плита растёт из земли, а не стоит на ней.
            float lift = (PelagWreckBreakwaterLook.PopLift(age) - PelagWreckBreakwaterLook.CrumbleSink(crumble) - .12f) * slab.Height;
            Quaternion rotation = Quaternion.LookRotation(run.Dir, Vector3.up) * Quaternion.AngleAxis(slab.Yaw, Vector3.up)
                * Quaternion.AngleAxis(lean, Vector3.right) * Quaternion.AngleAxis(slab.Roll, Vector3.forward)
                * (slab.Flip ? Quaternion.AngleAxis(180f, Vector3.up) : Quaternion.identity);
            Transform t = renderer.transform;
            t.SetPositionAndRotation(foot + Vector3.up * lift, rotation);
            float shrink = PelagWreckBreakwaterLook.CrumbleScale(crumble);
            t.localScale = new Vector3(slab.Width / Mathf.Sqrt(stretch), slab.Height * 1.12f * stretch, slab.Thick) * shrink;
            renderer.enabled = true;
            _wbBlock.Clear();
            float flare = crumble > 0f ? 1f + 1.6f * PelagWreckBreakwaterLook.Smooth01(crumble / PelagWreckBreakwaterLook.Break) : 1f;
            _wbBlock.SetFloat(WbGlowId, PelagWreckBreakwaterLook.GlowAt(age) * flare);
            _wbBlock.SetFloat(WbCrumbleId, PelagWreckBreakwaterLook.CrumbleCut(crumble));
            _wbBlock.SetColor(WbTintId, new Color(state.Shade, state.Shade, state.Shade * .99f, 1f));
            renderer.SetPropertyBlock(_wbBlock);
        }

        private static void HideWreckBreakSlabs(WreckBreakRun run)
        {
            foreach (MeshRenderer renderer in run.Renderers)
                if (renderer != null) renderer.enabled = false;
            if (run.Light != null) run.Light.intensity = 0f;
        }

        private void ReleaseWreckBreakRun(WreckBreakRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object))
            {
                HideWreckBreakSlabs(run);
                Release(run.Fx);
            }
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
            run.Light = null;
            run.Carried.Clear();
            run.CarriedUntil.Clear();
        }

        private void ForgetWreckBreakwater()
        {
            foreach (WreckBreakRun run in _wbRuns) ReleaseWreckBreakRun(run);
        }

        /// <summary>Живой забег стены: последний по тику удара (для подхвата и обрушения).</summary>
        private WreckBreakRun LatestWreckBreakRun()
        {
            WreckBreakRun pick = null;
            foreach (WreckBreakRun run in _wbRuns)
                if (run.Active && WkStill(run.Fx, run.Object) && (pick == null || run.Tick > pick.Tick)) pick = run;
            return pick;
        }
    }
}
