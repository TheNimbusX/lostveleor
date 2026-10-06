using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Выпад Крушения v4 «просто» (06.10; ассеты — Editor/PelagWreckLungeVfxSetup; целевой кадр
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png). От события WreckSlam (очередь до
    /// тика показа): ОДНА круглая вспышка в точке удара (звёзды префаба, холодный свет) и пара комьев земли из воронки;
    /// ОДНА прямая линия по полосе Sim — лента по земле от точки удара до конца вала, голова идёт с фронтом Sim
    /// (PelagWreckVfxRules.WaveFront), на шагах фронта вверх выбивает крупные комья (всего ~5). Больше ничего: корона
    /// плит, оттиски звеньев, рваная земля и мелкий мусор «железа» v7 (.WreckIron) базовому выпаду не рисуются.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckLungeRun
        {
            public bool Active, EndBurst;
            public int Fx = -1, Tick, Travel, StepsDone;
            public GameObject Object;
            public Vector3 Root, Origin, Dir;
            public float Start, End, Step, Seed;
            public MeshFilter Line;
            public Renderer LineRenderer;
            public ParticleSystem Chunks;
            public Light Light;
        }

        private readonly WreckLungeRun[] _wlRuns = { new WreckLungeRun(), new WreckLungeRun() };
        private MaterialPropertyBlock _wlBlock;
        private Vector3[] _wlVertices;
        private Vector2[] _wlUv;
        private int[] _wlTriangles;
        private static readonly int WlFrontId = Shader.PropertyToID("_Front"), WlAgeId = Shader.PropertyToID("_Age");
        private static readonly int WlLengthId = Shader.PropertyToID("_Length"), WlTravelId = Shader.PropertyToID("_Travel");
        private static readonly int WlSeedId = Shader.PropertyToID("_Seed");
        private const string WlMeshName = "WreckLungeLine";
        /// <summary>
        /// Полуширина ленты, м (V2: волна у удара шире, шипы и гуляющая ось — внутри ленты), подъём над землёй, шаг.
        /// </summary>
        private const float WlHalfWidth = 1.15f, WlLift = .07f, WlSegment = .12f;
        private static readonly Color[] WlEarth =
        {
            new Color(.52f, .47f, .44f), new Color(.44f, .40f, .38f), new Color(.46f, .34f, .25f), new Color(.38f, .29f, .22f)
        };

        private bool WreckLungeReady => _pools != null && (int)PelagVfxId.WreckLunge < _pools.Length && _pools[(int)PelagVfxId.WreckLunge] != null;

        /// <summary>Удар выпада (тик показа): вспышка, свет, комья из воронки, линия по полосе. False — префаба нет.</summary>
        private bool PlayWreckLunge(in WkPending p)
        {
            if (!WreckLungeReady) return false;
            if (CaptureRig.NoVfx) return true;
            WkWave wave = p.Wave;
            WreckLungeRun run = _wlRuns[0].Active && (!_wlRuns[1].Active || _wlRuns[1].Tick < _wlRuns[0].Tick) ? _wlRuns[1] : _wlRuns[0];
            ReleaseWreckLunge(run);
            System.Func<float, float, float> ground = WreckIronGroundAt(wave.Impact.y);
            var root = new Vector3(wave.Impact.x, ground(wave.Impact.x, wave.Impact.z), wave.Impact.z);
            bool lane = wave.Travel > 0 || wave.Stopped;
            int travel = lane ? Mathf.Max(1, wave.Travel) : 0;
            float life = PelagWreckVfxRules.Seconds(Mathf.Max(1, travel)) + 1.0f;
            int fx = WiSpawn(PelagVfxId.WreckLunge, root, Quaternion.identity, 0f, life, out GameObject go);
            if (fx < 0) return true;
            go.transform.localScale = Vector3.one;
            run.Active = true;
            run.Fx = fx;
            run.Object = go;
            run.Tick = p.Tick;
            run.Root = root;
            run.Origin = wave.Origin;
            run.Dir = wave.Dir.sqrMagnitude > .01f ? new Vector3(wave.Dir.x, 0f, wave.Dir.z).normalized : PlayerFacing();
            run.Start = wave.Start;
            run.End = Mathf.Max(wave.Start + .1f, wave.End);
            run.Step = Mathf.Max(.05f, wave.Step);
            run.Travel = travel;
            run.StepsDone = 0;
            run.EndBurst = !lane;
            // Свой рисунок волны (вздутия, ось, шипы) на каждый выпад.
            run.Seed = Random.Range(0f, 17f);
            run.Line = go.transform.Find("Line")?.GetComponent<MeshFilter>();
            run.LineRenderer = run.Line != null ? run.Line.GetComponent<Renderer>() : null;
            run.Chunks = go.transform.Find("Chunks")?.GetComponent<ParticleSystem>();
            run.Light = go.GetComponentInChildren<Light>(true);
            if (run.LineRenderer != null) run.LineRenderer.enabled = lane;
            if (lane && run.Line != null) BuildWreckLungeLine(run, ground);
            BurstWreckLungeCrater(run, Mathf.Max(.3f, wave.ImpactRadius), ground);
            UpdateWreckLungeRun(run, p.Tick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-lunge] tick={p.Tick} impact={root.ToString("F2")} start={run.Start:F2} end={run.End:F2} travel={travel} lane={lane}");
            return true;
        }

        /// <summary>Лента по земле от точки удара до конца вала: шаг WlSegment, высота — земля под каждой точкой.</summary>
        private void BuildWreckLungeLine(WreckLungeRun run, System.Func<float, float, float> ground)
        {
            float length = run.End - run.Start;
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / WlSegment), 2, 80);
            int count = (segments + 1) * 2;
            if (_wlVertices == null || _wlVertices.Length != count)
            {
                _wlVertices = new Vector3[count];
                _wlUv = new Vector2[count];
                _wlTriangles = new int[segments * 6];
                for (int i = 0; i < segments; i++)
                {
                    int v = i * 2, at = i * 6;
                    _wlTriangles[at] = v; _wlTriangles[at + 1] = v + 2; _wlTriangles[at + 2] = v + 1;
                    _wlTriangles[at + 3] = v + 1; _wlTriangles[at + 4] = v + 2; _wlTriangles[at + 5] = v + 3;
                }
            }
            var perp = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            for (int i = 0; i <= segments; i++)
            {
                float k = i / (float)segments;
                Vector3 axis = run.Origin + run.Dir * (run.Start + length * k);
                for (int s = 0; s < 2; s++)
                {
                    Vector3 p = axis + perp * (s == 0 ? -WlHalfWidth : WlHalfWidth);
                    p.y = ground(p.x, p.z) + WlLift;
                    _wlVertices[i * 2 + s] = p - run.Root;
                    _wlUv[i * 2 + s] = new Vector2(k, s);
                }
            }
            run.Line.transform.SetPositionAndRotation(run.Root, Quaternion.identity);
            Mesh mesh = FormWaterMesh.MeshFor(run.Line, WlMeshName);
            mesh.Clear();
            mesh.vertices = _wlVertices;
            mesh.uv = _wlUv;
            mesh.triangles = _wlTriangles;
            mesh.RecalculateBounds();
        }

        /// <summary>Вспышка: несколько комьев камня и земли из воронки (падают внутри круга удара).</summary>
        private void BurstWreckLungeCrater(WreckLungeRun run, float radius, System.Func<float, float, float> ground)
        {
            int n = 6;
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-.3f, .3f)) * Mathf.PI * 2f / n;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 from = run.Root + radial * radius * Random.Range(.1f, .3f) + Vector3.up * .15f;
                WiChunk(run.Chunks, from, run.Root + radial * radius * Random.Range(.6f, 1.05f), Random.Range(.40f, .55f),
                    WlEarth[i % WlEarth.Length], i % 2 == 0 ? Random.Range(.22f, .32f) : Random.Range(.13f, .19f), ground);
            }
        }

        /// <summary>Кадр выпада (из UpdateWreckIron): фронт и возраст в ленту, свет за фронтом, комья на шагах фронта.</summary>
        private void UpdateWreckLunge(float shown)
        {
            foreach (WreckLungeRun run in _wlRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                UpdateWreckLungeRun(run, shown);
            }
        }

        private void UpdateWreckLungeRun(WreckLungeRun run, float shown)
        {
            float age = PelagWreckVfxRules.Seconds(shown - run.Tick);
            float length = run.End - run.Start;
            float front = run.Travel > 0 ? PelagWreckVfxRules.WaveFront(shown, run.Tick, run.Start, run.Step, run.Travel, run.End) : run.Start;
            if (run.LineRenderer != null && run.LineRenderer.enabled)
            {
                if (_wlBlock == null) _wlBlock = new MaterialPropertyBlock();
                _wlBlock.Clear();
                _wlBlock.SetFloat(WlFrontId, Mathf.Clamp01((front - run.Start) / Mathf.Max(.1f, length)));
                _wlBlock.SetFloat(WlAgeId, Mathf.Max(0f, age));
                _wlBlock.SetFloat(WlLengthId, length);
                _wlBlock.SetFloat(WlTravelId, PelagWreckVfxRules.Seconds(Mathf.Max(1, run.Travel)));
                _wlBlock.SetFloat(WlSeedId, run.Seed);
                run.LineRenderer.SetPropertyBlock(_wlBlock);
            }
            if (run.Light != null)
            {
                Vector3 at = run.Travel > 0 && age > .03f ? run.Origin + run.Dir * front : run.Root;
                run.Light.transform.position = new Vector3(at.x, run.Root.y + .8f, at.z);
                run.Light.intensity = CaptureRig.NoVfx ? 0f : PelagWreckIronRules.LightAt(age, Mathf.Max(1, run.Travel));
            }
            EmitWreckLungeFront(run, shown);
            if (age > PelagWreckVfxRules.Seconds(Mathf.Max(1, run.Travel)) + .95f) ReleaseWreckLunge(run);
        }

        /// <summary>
        /// Фронт дошёл до нового шага Sim: крупные комья вверх с линии в НЕРОВНЫХ местах (V2: не через шаг — по жребию,
        /// иногда два рядом, иногда пропуск; кадр: ~5 крупных камней вдоль линии), разного размера, со сдвигом в сторону.
        /// </summary>
        private void EmitWreckLungeFront(WreckLungeRun run, float shown)
        {
            if (CaptureRig.NoVfx || run.Travel <= 0) return;
            int steps = Mathf.Clamp(Mathf.FloorToInt(shown - run.Tick + 1f + 1e-3f), 0, run.Travel);
            System.Func<float, float, float> ground = WreckIronGroundAt(run.Root.y);
            var perp = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            while (run.StepsDone < steps)
            {
                run.StepsDone++;
                float roll = Random.value;
                bool last = run.StepsDone == run.Travel;
                if (roll > .55f && !last) continue;
                int count = roll < .14f ? 2 : 1;
                float at = Mathf.Min(run.End, run.Start + run.Step * run.StepsDone);
                for (int c = 0; c < count; c++)
                {
                    Vector3 lip = run.Origin + run.Dir * (at - run.Step * Random.Range(.1f, .9f)) + perp * Random.Range(-.3f, .3f);
                    lip.y = ground(lip.x, lip.z) + .15f;
                    Vector3 land = run.Origin + run.Dir * Mathf.Min(run.End + .4f, at + Random.Range(.2f, 1.1f)) + perp * Random.Range(-.7f, .7f);
                    bool big = c == 0 && (roll < .35f || last);
                    WiChunk(run.Chunks, lip, land, Random.Range(.5f, .8f), WlEarth[(run.StepsDone + c) % WlEarth.Length],
                        big ? Random.Range(.48f, .66f) : Random.Range(.28f, .42f), ground);
                }
            }
        }

        private void ReleaseWreckLunge(WreckLungeRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object))
            {
                if (run.Light != null) run.Light.intensity = 0f;
                Release(run.Fx);
            }
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetWreckLunge()
        {
            foreach (WreckLungeRun run in _wlRuns) ReleaseWreckLunge(run);
        }
    }
}
