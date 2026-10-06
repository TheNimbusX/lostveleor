using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Выпад Крушения на СТЕКЕ СЕРИИ САБЛИ (06.10 поздно; ассеты — Editor/PelagWreckComboVfxSetup.Lunge, числа —
    /// PelagWreckComboLungeLook; целевой кадр ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png).
    /// Как добивающий сабли (стоячая волна к камере): от события WreckSlam (очередь до тика показа) в точке удара встаёт
    /// всплеск префаба (полузвезда от земли, звезда, эхо, кольцо-звезда и трещины по земле, камни, искры, пыль); по полосе
    /// Sim бежит СТОЯЧИЙ гребень — лента вдоль оси, поднятая к экрану (нормаль к взгляду и полосе), голова — на фронте
    /// Sim (PelagWreckVfxRules.WaveFront — то же, что TryGetWreckWave, на тик показа); точка ленты стареет с тех пор, как
    /// через неё прошёл фронт: встаёт с перелётом, оседает, шейдер рвёт её гранями. Эхо гребня — тиком позже, выше,
    /// дальше от камеры. На шагах фронта — искры, сколы, комья, камни, пыль, след трещин (.WreckComboLungeBits).
    /// Формы — та же сборка другой краской; у Призрачного якоря полосы нет (только удар).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckComboLungeRun
        {
            public bool Active, Lane, Ended;
            public int Fx = -1, Tick, Travel, StepsDone, Frame;
            public GameObject Object;
            public Vector3 Root, Origin, Dir;
            public float Start, End, Step, Half, Height;
            public PelagForm Form;
            public Color Cobalt, Deep, Hot;
            public MeshFilter Crest, Echo;
            public ParticleSystem Sparks, Chips, Clods, Rocks, Dust, Scorch, ScorchGlow, EndHalf;
        }

        private readonly WreckComboLungeRun[] _wclRuns = { new WreckComboLungeRun(), new WreckComboLungeRun() };
        private readonly List<Vector3> _wclVertices = new List<Vector3>(256);
        private readonly List<Vector4> _wclUv = new List<Vector4>(256);
        private readonly List<int> _wclTriangles = new List<int>(768);
        private MaterialPropertyBlock _wclBlock;
        private const string WclCrestMesh = "WreckComboCrest", WclEchoMesh = "WreckComboCrestEcho";

        private bool WreckComboLungeReady => _pools != null && (int)PelagVfxId.WreckComboLunge < _pools.Length
            && _pools[(int)PelagVfxId.WreckComboLunge] != null;

        /// <summary>Удар выпада (тик показа): всплеск префаба, гребень по полосе Sim. False — префаба нет (прежний путь).</summary>
        private bool PlayWreckComboLunge(in WkPending p)
        {
            if (!WreckComboLungeReady) return false;
            if (CaptureRig.NoVfx) return true;
            WkWave wave = p.Wave;
            WreckComboLungeRun run = _wclRuns[0].Active && (!_wclRuns[1].Active || _wclRuns[1].Tick < _wclRuns[0].Tick) ? _wclRuns[1] : _wclRuns[0];
            ReleaseWreckComboLunge(run);
            System.Func<float, float, float> ground = WreckIronGroundAt(wave.Impact.y);
            var root = new Vector3(wave.Impact.x, ground(wave.Impact.x, wave.Impact.z), wave.Impact.z);
            bool lane = wave.Travel > 0 || wave.Stopped;
            int travel = lane ? Mathf.Max(1, wave.Travel) : 0;
            Vector3 dir = wave.Dir.sqrMagnitude > .01f ? new Vector3(wave.Dir.x, 0f, wave.Dir.z).normalized : PlayerFacing();
            if (!TryAcquire(PelagVfxId.WreckComboLunge, out GameObject go, out PelagVfxElement element)) return true;
            PelagForm form = wave.Look != PelagForm.None ? wave.Look : wave.Form;
            run.Form = form;
            WreckComboLungeColors(run, form);
            Transform t = go.transform;
            run.Crest = t.Find("Crest")?.GetComponent<MeshFilter>();
            run.Echo = t.Find("CrestEcho")?.GetComponent<MeshFilter>();
            run.Sparks = t.Find("LaneSparks")?.GetComponent<ParticleSystem>();
            run.Chips = t.Find("LaneChips")?.GetComponent<ParticleSystem>();
            run.Clods = t.Find("LaneClods")?.GetComponent<ParticleSystem>();
            run.Rocks = t.Find("LaneRocks")?.GetComponent<ParticleSystem>();
            run.Dust = t.Find("LaneDust")?.GetComponent<ParticleSystem>();
            run.Scorch = t.Find("Scorch")?.GetComponent<ParticleSystem>();
            run.ScorchGlow = t.Find("ScorchGlow")?.GetComponent<ParticleSystem>();
            run.EndHalf = t.Find("EndHalf")?.GetComponent<ParticleSystem>();
            // Краска формы — до старта систем: залп удара рождается уже в цвете формы.
            TintWreckComboLunge(t, run);
            float life = Mathf.Max(PelagWreckComboLungeLook.AfterSeconds + 1.3f, PelagWreckVfxRules.Seconds(travel) + 1.5f);
            int index = ReserveActive();
            element.Begin(root, Quaternion.LookRotation(dir, Vector3.up));
            t.localScale = Vector3.one;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckComboLunge, Object = go, Element = element,
                Duration = life, Start = root, End = root, Motion = Motion.Static, FollowIndex = -1
            };
            run.Active = true;
            run.Fx = index;
            run.Object = go;
            run.Tick = p.Tick;
            run.Root = root;
            run.Origin = wave.Origin;
            run.Dir = dir;
            run.Start = wave.Start;
            run.End = Mathf.Max(wave.Start + .1f, wave.End);
            run.Step = Mathf.Max(.05f, wave.Step);
            run.Half = Mathf.Max(.3f, wave.HalfWidth);
            run.Travel = travel;
            run.StepsDone = 0;
            run.Lane = lane;
            run.Ended = !lane;
            run.Height = PelagWreckComboLungeLook.CrestHeight(wave.Form, wave.DamagePercent);
            // Кадр рождения: залп систем удара выходит только на следующем обновлении частиц — лента ждёт его, чтобы гребень
            // не появлялся раньше всплеска (L2: одинокий клин на кадр раньше звезды); меш из пула прошлого выпада — пустой.
            run.Frame = Time.frameCount;
            foreach (MeshFilter filter in new[] { run.Crest, run.Echo })
            {
                if (filter == null) continue;
                FormWaterMesh.MeshFor(filter, filter == run.Crest ? WclCrestMesh : WclEchoMesh).Clear();
                filter.transform.SetPositionAndRotation(root, Quaternion.identity);
                filter.transform.localScale = Vector3.one;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = lane;
            }
            UpdateWreckComboLungeRun(run, p.Tick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-combo-lunge] tick={p.Tick} impact={root.ToString("F2")} start={run.Start:F2} end={run.End:F2} half={run.Half:F2} travel={travel} lane={lane} height={run.Height:F2} form={form}");
            return true;
        }

        /// <summary>Кадр выпада (из UpdateWreckIron): лента гребня и эхо по фронту, куски на шагах фронта, конец полосы.</summary>
        private void UpdateWreckComboLunge(float shown)
        {
            foreach (WreckComboLungeRun run in _wclRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                UpdateWreckComboLungeRun(run, shown);
            }
        }

        private void UpdateWreckComboLungeRun(WreckComboLungeRun run, float shown)
        {
            if (!run.Lane || Time.frameCount == run.Frame) return;
            Camera camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.8f, .6f);
            // Лента — к экрану: нормаль к взгляду и полосе (как стоячая волна сабли, только вдоль полосы); вверх.
            Vector3 lift = Vector3.Cross(view, run.Dir);
            if (lift.sqrMagnitude < 1e-4f) lift = Vector3.up;
            lift.Normalize();
            if (lift.y < 0f) lift = -lift;
            Vector3 away = Vector3.ProjectOnPlane(view, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            System.Func<float, float, float> ground = WreckIronGroundAt(run.Root.y);
            BuildWreckCrest(run, run.Crest, WclCrestMesh, shown, 1f, Vector3.zero, lift, ground);
            BuildWreckCrest(run, run.Echo, WclEchoMesh, shown - PelagWreckComboLungeLook.EchoDelaySeconds * Simulation.TicksPerSecond,
                PelagWreckComboLungeLook.EchoHeight, away * PelagWreckComboLungeLook.EchoBack, lift, ground);
            EmitWreckComboLungeFront(run, shown, lift, ground);
            float last = PelagWreckVfxRules.WaveEndTick(run.Tick, run.Travel);
            if (PelagWreckVfxRules.Seconds(shown - last) > PelagWreckComboLungeLook.AfterSeconds) HideWreckComboCrest(run);
        }

        /// <summary>
        /// Лента гребня: от <see cref="PelagWreckComboLungeLook.TailLead"/> до точки удара и до фронта Sim на тик показа
        /// <paramref name="shown"/>; высота точки — профиль по её возрасту; верх наклонён вперёд; низ — под землёй.
        /// UV: u — метры / 4, v — низ/верх, z — возраст точки, w — возраст удара (с).
        /// </summary>
        private void BuildWreckCrest(WreckComboLungeRun run, MeshFilter filter, string meshName, float shown,
            float heightScale, Vector3 offset, Vector3 lift, System.Func<float, float, float> ground)
        {
            if (filter == null) return;
            Mesh mesh = FormWaterMesh.MeshFor(filter, meshName);
            mesh.Clear();
            float front = PelagWreckVfxRules.WaveFront(shown, run.Tick, run.Start, run.Step, run.Travel, run.End);
            float tail = run.Start - PelagWreckComboLungeLook.TailLead;
            if (front <= tail + .05f || shown < run.Tick - 1f) return;
            int segments = Mathf.Clamp(Mathf.CeilToInt((front - tail) / PelagWreckComboLungeLook.Segment), 2, 120);
            float global = Mathf.Max(0f, PelagWreckVfxRules.Seconds(shown - run.Tick + 1f));
            _wclVertices.Clear();
            _wclUv.Clear();
            _wclTriangles.Clear();
            for (int i = 0; i <= segments; i++)
            {
                float s = Mathf.Lerp(tail, front, i / (float)segments);
                float pass = run.Tick - 1f + Mathf.Max(0f, s - run.Start) / run.Step;
                float age = Mathf.Max(0f, PelagWreckVfxRules.Seconds(shown - pass));
                float h = run.Height * heightScale * PelagWreckComboLungeLook.Profile(age);
                if (s < run.Start) h *= PelagWreckComboLungeLook.Smooth01((s - tail) / PelagWreckComboLungeLook.TailLead);
                Vector3 axis = run.Origin + run.Dir * s + offset;
                axis.y = ground(axis.x, axis.z) - PelagWreckComboLungeLook.Sink;
                Vector3 top = axis + lift * (h + PelagWreckComboLungeLook.Sink) + run.Dir * (PelagWreckComboLungeLook.Lean * h);
                float u = s / PelagWreckComboLungeLook.MetersPerU;
                _wclVertices.Add(axis - run.Root);
                _wclVertices.Add(top - run.Root);
                _wclUv.Add(new Vector4(u, 0f, age, global));
                _wclUv.Add(new Vector4(u, 1f, age, global));
                if (i == segments) break;
                int v = i * 2;
                _wclTriangles.Add(v); _wclTriangles.Add(v + 1); _wclTriangles.Add(v + 2);
                _wclTriangles.Add(v + 1); _wclTriangles.Add(v + 3); _wclTriangles.Add(v + 2);
            }
            mesh.SetVertices(_wclVertices);
            mesh.SetUVs(0, _wclUv);
            mesh.SetTriangles(_wclTriangles, 0);
            mesh.RecalculateBounds();
        }

        private static void HideWreckComboCrest(WreckComboLungeRun run)
        {
            foreach (MeshFilter filter in new[] { run.Crest, run.Echo })
            {
                Renderer renderer = filter != null ? filter.GetComponent<Renderer>() : null;
                if (renderer != null && renderer.enabled) renderer.enabled = false;
            }
        }

        private void ReleaseWreckComboLunge(WreckComboLungeRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object))
            {
                HideWreckComboCrest(run);
                Release(run.Fx);
            }
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetWreckComboLunge()
        {
            foreach (WreckComboLungeRun run in _wclRuns) ReleaseWreckComboLunge(run);
        }
    }
}
