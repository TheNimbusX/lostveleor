using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Выпад Крушения v4 на РИСОВАННЫХ текстурах (06.10; ассеты — Editor/PelagWreckPaintedVfxSetup; целевой кадр
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png). От события WreckSlam (очередь до
    /// тика показа): рисованная звезда удара на земле в точке удара (раскрывается за ~3 кадра, держится, рассыпается
    /// порогом) и рисованная полоса по земле вдоль полосы Sim — широкая вспышка рисунка в точке удара, тонкая волна к
    /// концу вала; голова полосы стоит на фронте Sim (PelagWreckVfxRules.WaveFront — то же, что TryGetWreckWave, на тик
    /// показа), рассыпается с тыла. На шагах фронта вверх летят комья-спрайты в неровных местах. Больше ничего: ни корон
    /// плит, ни оттисков, ни процедурных шипов. Формы пока — те же куски с цветом формы (свои эффекты — позже).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckPaintedLungeRun
        {
            public bool Active, Lane;
            public int Fx = -1, Tick, Travel, StepsDone;
            public GameObject Object;
            public Vector3 Root, Origin, Dir;
            public float Start, End, Step, From, Length, Half, StarDiameter;
            public MeshFilter Line;
            public Renderer LineRenderer, StarRenderer;
            public Transform Star;
            public ParticleSystem Chunks;
        }

        private readonly WreckPaintedLungeRun[] _wpRuns = { new WreckPaintedLungeRun(), new WreckPaintedLungeRun() };
        private Vector3[] _wpVertices;
        private Vector2[] _wpUv;
        private int[] _wpTriangles;
        private static readonly int WpAgeId = Shader.PropertyToID("_Age"), WpFrontId = Shader.PropertyToID("_Front");
        private static readonly int WpTravelId = Shader.PropertyToID("_TravelSeconds");
        private const string WpMeshName = "WreckPaintedLungeLine";
        /// <summary>Подъём над землёй, м; шаг ленты вдоль, м (лента повторяет уступы земли).</summary>
        private const float WpLift = .06f, WpSegment = .15f;

        private bool WreckPaintedLungeReady => _pools != null && (int)PelagVfxId.WreckPaintedLunge < _pools.Length
            && _pools[(int)PelagVfxId.WreckPaintedLunge] != null;

        /// <summary>Удар выпада (тик показа): звезда на земле, полоса по Sim, комья. False — префаба нет.</summary>
        private bool PlayWreckPaintedLunge(in WkPending p)
        {
            if (!WreckPaintedLungeReady) return false;
            if (CaptureRig.NoVfx) return true;
            WkWave wave = p.Wave;
            WreckPaintedLungeRun run = _wpRuns[0].Active && (!_wpRuns[1].Active || _wpRuns[1].Tick < _wpRuns[0].Tick) ? _wpRuns[1] : _wpRuns[0];
            ReleaseWreckPaintedLunge(run);
            System.Func<float, float, float> ground = WreckIronGroundAt(wave.Impact.y);
            var root = new Vector3(wave.Impact.x, ground(wave.Impact.x, wave.Impact.z), wave.Impact.z);
            bool lane = wave.Travel > 0 || wave.Stopped;
            int travel = lane ? Mathf.Max(1, wave.Travel) : 0;
            float life = PelagWreckVfxRules.Seconds(Mathf.Max(1, travel)) + 1.0f;
            int fx = WiSpawn(PelagVfxId.WreckPaintedLunge, root, Quaternion.identity, 0f, life, out GameObject go);
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
            run.Lane = lane;
            PelagWreckPaintedLook.LungeQuad(run.Start, run.End, wave.HalfWidth, out run.From, out run.Length, out run.Half);
            run.StarDiameter = PelagWreckPaintedLook.LungeStarDiameter(wave.ImpactRadius);
            Transform t = go.transform;
            run.Line = t.Find("Line")?.GetComponent<MeshFilter>();
            run.LineRenderer = run.Line != null ? run.Line.GetComponent<Renderer>() : null;
            run.Star = t.Find("Star");
            run.StarRenderer = run.Star != null ? run.Star.GetComponent<Renderer>() : null;
            run.Chunks = t.Find("Chunks")?.GetComponent<ParticleSystem>();
            if (run.LineRenderer != null) run.LineRenderer.enabled = lane;
            if (lane && run.Line != null) BuildWreckPaintedLine(run, ground);
            if (run.Star != null)
            {
                // Звезда лежит на земле в точке удара, повёрнута случайно (каждый выпад — свой рисунок лучей).
                run.Star.SetPositionAndRotation(root + Vector3.up * (WpLift + .02f), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
            TintWreckPainted(run.LineRenderer, wave.Form);
            TintWreckPainted(run.StarRenderer, wave.Form);
            TintWreckPainted(run.Chunks != null ? run.Chunks.GetComponent<Renderer>() : null, wave.Form);
            BurstWreckPaintedCrater(run, Mathf.Max(.3f, wave.ImpactRadius), ground);
            UpdateWreckPaintedLungeRun(run, p.Tick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-painted-lunge] tick={p.Tick} impact={root.ToString("F2")} start={run.Start:F2} end={run.End:F2} quad={run.From:F2}+{run.Length:F2}x{2f * run.Half:F2} travel={travel} lane={lane} form={wave.Form}");
            return true;
        }

        /// <summary>Лента вдоль оси Sim: от начала квада (вспышка рисунка — в точке удара) до конца вала, по земле.</summary>
        private void BuildWreckPaintedLine(WreckPaintedLungeRun run, System.Func<float, float, float> ground)
        {
            int segments = Mathf.Clamp(Mathf.CeilToInt(run.Length / WpSegment), 2, 96);
            int count = (segments + 1) * 2;
            if (_wpVertices == null || _wpVertices.Length != count)
            {
                _wpVertices = new Vector3[count];
                _wpUv = new Vector2[count];
                _wpTriangles = new int[segments * 6];
                for (int i = 0; i < segments; i++)
                {
                    int v = i * 2, at = i * 6;
                    _wpTriangles[at] = v; _wpTriangles[at + 1] = v + 2; _wpTriangles[at + 2] = v + 1;
                    _wpTriangles[at + 3] = v + 1; _wpTriangles[at + 4] = v + 2; _wpTriangles[at + 5] = v + 3;
                }
            }
            var perp = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            for (int i = 0; i <= segments; i++)
            {
                float k = i / (float)segments;
                Vector3 axis = run.Origin + run.Dir * (run.From + run.Length * k);
                for (int s = 0; s < 2; s++)
                {
                    // v = 1 — левый край по ходу (перпендикуляр +), как верх рисунка.
                    Vector3 q = axis + perp * (s == 0 ? -run.Half : run.Half);
                    q.y = ground(q.x, q.z) + WpLift;
                    _wpVertices[i * 2 + s] = q - run.Root;
                    _wpUv[i * 2 + s] = new Vector2(k, s);
                }
            }
            run.Line.transform.SetPositionAndRotation(run.Root, Quaternion.identity);
            Mesh mesh = FormWaterMesh.MeshFor(run.Line, WpMeshName);
            mesh.Clear();
            mesh.vertices = _wpVertices;
            mesh.uv = _wpUv;
            mesh.triangles = _wpTriangles;
            mesh.RecalculateBounds();
        }

        /// <summary>Пара комьев из точки удара: падают внутри круга удара.</summary>
        private void BurstWreckPaintedCrater(WreckPaintedLungeRun run, float radius, System.Func<float, float, float> ground)
        {
            const int n = 4;
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-.3f, .3f)) * Mathf.PI * 2f / n;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 from = run.Root + radial * radius * Random.Range(.1f, .25f) + Vector3.up * .2f;
                Vector3 land = run.Root + radial * radius * Random.Range(.7f, 1.1f);
                WpThrow(run.Chunks, from, land, Random.Range(.45f, .6f), i % 2 == 0 ? Random.Range(.44f, .58f) : Random.Range(.26f, .36f), ground);
            }
        }

        private static void WpThrow(ParticleSystem system, Vector3 from, Vector3 land, float flight, float size, System.Func<float, float, float> ground)
        {
            if (system == null) return;
            WpEmit(system, from, WiLaunch(system, from, land, flight, ground), size, flight);
        }

        /// <summary>Кадр выпада (из UpdateWreckIron): возраст и фронт в полосу, раскрытие звезды, комья на шагах фронта.</summary>
        private void UpdateWreckPaintedLunge(float shown)
        {
            foreach (WreckPaintedLungeRun run in _wpRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                UpdateWreckPaintedLungeRun(run, shown);
            }
        }

        private void UpdateWreckPaintedLungeRun(WreckPaintedLungeRun run, float shown)
        {
            float age = Mathf.Max(0f, PelagWreckVfxRules.Seconds(shown - run.Tick));
            float travelSeconds = PelagWreckVfxRules.Seconds(Mathf.Max(1, run.Travel));
            if (_wpBlock == null) _wpBlock = new MaterialPropertyBlock();
            if (run.LineRenderer != null && run.LineRenderer.enabled)
            {
                float front = run.Travel > 0 ? PelagWreckVfxRules.WaveFront(shown, run.Tick, run.Start, run.Step, run.Travel, run.End) : run.End;
                run.LineRenderer.GetPropertyBlock(_wpBlock);
                _wpBlock.SetFloat(WpAgeId, age);
                _wpBlock.SetFloat(WpFrontId, PelagWreckPaintedLook.LungeFrontU(front, run.From, run.Length));
                _wpBlock.SetFloat(WpTravelId, travelSeconds * run.Length / Mathf.Max(.1f, run.End - run.Start));
                run.LineRenderer.SetPropertyBlock(_wpBlock);
            }
            if (run.Star != null && run.StarRenderer != null)
            {
                float size = run.StarDiameter / PelagWreckPaintedLook.StarFill * PelagWreckPaintedLook.StarPop(age);
                run.Star.localScale = new Vector3(size, 1f, size);
                run.StarRenderer.GetPropertyBlock(_wpBlock);
                _wpBlock.SetFloat(WpAgeId, age);
                run.StarRenderer.SetPropertyBlock(_wpBlock);
            }
            EmitWreckPaintedFront(run, shown);
            if (age > travelSeconds + .95f) ReleaseWreckPaintedLunge(run);
        }

        /// <summary>
        /// Фронт дошёл до нового шага Sim: комья вверх с полосы в НЕРОВНЫХ местах (по жребию: иногда два рядом, иногда
        /// пропуск; кадр — ~5 крупных камней вдоль линии), разного размера, со сдвигом в сторону.
        /// </summary>
        private void EmitWreckPaintedFront(WreckPaintedLungeRun run, float shown)
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
                if (roll > .6f && !last) continue;
                int count = roll < .16f ? 2 : 1;
                float at = Mathf.Min(run.End, run.Start + run.Step * run.StepsDone);
                for (int c = 0; c < count; c++)
                {
                    Vector3 lip = run.Origin + run.Dir * (at - run.Step * Random.Range(.1f, .9f)) + perp * Random.Range(-.25f, .25f);
                    lip.y = ground(lip.x, lip.z) + .15f;
                    Vector3 land = run.Origin + run.Dir * Mathf.Min(run.End + .4f, at + Random.Range(.2f, 1.0f)) + perp * Random.Range(-.8f, .8f);
                    bool big = c == 0 && (roll < .38f || last);
                    WpThrow(run.Chunks, lip, land, Random.Range(.5f, .75f), big ? Random.Range(.58f, .78f) : Random.Range(.32f, .44f), ground);
                }
            }
        }

        private void ReleaseWreckPaintedLunge(WreckPaintedLungeRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetWreckPaintedLunge()
        {
            foreach (WreckPaintedLungeRun run in _wpRuns) ReleaseWreckPaintedLunge(run);
        }
    }
}
