using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение «холодное железо» — база (06.10, целевой кадр
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/base-v1.webp; числа —
    /// PelagWreckIronRules; ассеты — Editor/PelagWreckIronVfxSetup). Удар оземь базы (и Панциря —
    /// его вал тоже базовый): разбитая земля круга ImpactRadius с трещинами, светящимися холодным
    /// голубым, кольцо крупных камней по кромке, обломки и пыль; по полосе до WallEnd фронт Sim
    /// шагами открывает трещины, выбивает камни и железные осколки по краям и впечатывает 3–4
    /// крупных звена, которые светятся изнутри; каждый кусок встаёт, когда до него дошёл фронт,
    /// лежит секунду и оседает в землю. Холодный точечный свет идёт за фронтом. Всё рождается
    /// от события WreckSlam (очередь до тика показа), не опросом. Волнорез и Девятый вал — пока
    /// прежняя «пена» (их переделка — после приёмки базы).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Префабы «холодного железа» собраны: удар оземь базы, махи и знаки — новым путём.</summary>
        private bool WreckIronReady => _pools != null && (int)PelagVfxId.WreckIronKnock < _pools.Length
            && _pools[(int)PelagVfxId.WreckIronSlam] != null && _pools[(int)PelagVfxId.WreckIronStreak] != null
            && _pools[(int)PelagVfxId.WreckIronHit] != null && _pools[(int)PelagVfxId.WreckIronKnock] != null;

        /// <summary>Серия в «железе»: база и Панцирь (Волнорез и Девятый вал — прежняя пена до своей переделки).</summary>
        private static bool WreckIronForm(PelagForm form) => form != PelagForm.WreckBreakwater && form != PelagForm.WreckNinthWave;

        private enum WiKind : byte { Stone, Iron, Link }

        private struct WiPiece
        {
            public Transform Pivot;
            public Renderer Renderer;
            public Vector3 Rest;
            public Quaternion Rotation;
            public Vector3 Scale;
            public float Arrival, Depth, GroundY;
            public WiKind Kind;
            /// <summary>Оттенок куска (глыба, плита) и «ком земли» (0…1) — V5.</summary>
            public Color Tint;
            public float Earth;
        }

        private sealed class WreckIronRun
        {
            public bool Active, Lane;
            public int Fx = -1, Tick, Serial, Travel, StepsDone, LinkSparks;
            public GameObject Object;
            public Vector3 Root, Impact, Origin, Dir;
            public float Radius, Start, End, HalfWidth, Step;
            public bool Stopped, EndBurst;
            public MeshFilter Ground;
            public Renderer GroundRenderer;
            public Light Light;
            public ParticleSystem Chunks, Shards, Spray, Dust, Sparks;
            public readonly WiPiece[] Pieces = new WiPiece[PelagWreckIronRules.MaxStones + PelagWreckIronRules.MaxLinks];
            public int Count;
        }

        private readonly WreckIronRun[] _wiRuns = { new WreckIronRun(), new WreckIronRun(), new WreckIronRun() };
        private readonly PelagWreckIronGround _wiGround = new PelagWreckIronGround();
        private readonly PelagWreckIronRules.Stone[] _wiStones = new PelagWreckIronRules.Stone[PelagWreckIronRules.MaxStones];
        private MaterialPropertyBlock _wiBlock;
        private static readonly int WiAgeId = Shader.PropertyToID("_Age");
        private static readonly int WiGlowId = Shader.PropertyToID("_Glow");
        private static readonly int WiGroundId = Shader.PropertyToID("_GroundY");
        private static readonly int WiTintId = Shader.PropertyToID("_Tint");
        private static readonly int WiEarthId = Shader.PropertyToID("_Earth");

        private System.Func<float, float, float> _wiGroundAt;

        /// <summary>Земля в точке (уступы арены; лагерь — навигация), база — высота у точки удара.</summary>
        private System.Func<float, float, float> WreckIronGroundAt(float baseY)
        {
            _abGroundBase = baseY;
            return _wiGroundAt ?? (_wiGroundAt = AbordageGroundAt);
        }

        private WreckIronRun TakeWreckIron()
        {
            WreckIronRun pick = null;
            foreach (WreckIronRun run in _wiRuns)
                if (!run.Active) { pick = run; break; }
                else if (pick == null || run.Tick < pick.Tick) pick = run;
            ReleaseWreckIron(pick);
            return pick;
        }

        private void ReleaseWreckIron(WreckIronRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object))
            {
                HideWreckIronPieces(run);
                Release(run.Fx);
            }
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
            run.Count = 0;
        }

        private void ForgetWreckIron()
        {
            foreach (WreckIronRun run in _wiRuns) ReleaseWreckIron(run);
            ForgetWreckLunge();
            ForgetWreckPaintedLunge();
            ForgetWreckComboLunge();
        }

        private static void HideWreckIronPieces(WreckIronRun run)
        {
            for (int i = 0; i < run.Count; i++)
                if (run.Pieces[i].Renderer != null) run.Pieces[i].Renderer.enabled = false;
            if (run.Light != null) run.Light.intensity = 0f;
        }

        /// <summary>Объект пула «железа» в точке без цвета форм; −1 — эффектов нет (NoVfx) или пула нет.</summary>
        private int WiSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        /// <summary>Удар оземь базы (тик показа): земля, камни, звенья по снимку Sim; толчок камеры.</summary>
        private void PlayWreckIronSlam(in WkPending p)
        {
            WkWave wave = p.Wave;
            // 06.10: выпад базы — «просто» по целевому кадру v4-lunge-simple.png (.WreckLunge: вспышка и линия); круг,
            // корона, звенья и мусор «железа» v7 — только без его префаба.
            // 06.10 вечер: выпад на рисованных текстурах (.WreckPaintedLunge); процедурный «просто» — только без его сборки.
            if (!PlayWreckPaintedLunge(p) && !PlayWreckLunge(p))
                BeginWreckIron(p.Tick, p.Serial, wave.Impact, Mathf.Max(.3f, wave.ImpactRadius), wave, wave.Travel > 0 || wave.Stopped);
            if (!CaptureRig.NoVfx) _juice?.PunchCamera(.30f, .06f);
        }

        /// <summary>Круг (и полоса, если <paramref name="lane"/>) «железа»: объект пула, меш земли, куски по правилам.</summary>
        private void BeginWreckIron(int tick, int serial, Vector3 impact, float radius, in WkWave wave, bool lane)
        {
            if (CaptureRig.NoVfx) return;
            WreckIronRun run = TakeWreckIron();
            System.Func<float, float, float> ground = WreckIronGroundAt(impact.y);
            var root = new Vector3(impact.x, ground(impact.x, impact.z), impact.z);
            float life = PelagWreckIronRules.LifeSeconds(lane ? wave.Travel : 1) + .3f;
            int fx = WiSpawn(PelagVfxId.WreckIronSlam, root, Quaternion.identity, 0f, life, out GameObject go);
            if (fx < 0) return;
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            run.Tick = tick;
            run.Serial = serial;
            run.Root = root;
            run.Impact = impact;
            run.Radius = radius;
            run.Lane = lane;
            run.Origin = wave.Origin;
            run.Dir = wave.Dir.sqrMagnitude > .01f ? new Vector3(wave.Dir.x, 0f, wave.Dir.z).normalized : PlayerFacing();
            run.Start = wave.Start;
            run.End = Mathf.Max(wave.Start, wave.End);
            run.HalfWidth = Mathf.Max(.2f, wave.HalfWidth);
            run.Step = Mathf.Max(.05f, wave.Step);
            run.Travel = lane ? Mathf.Max(1, wave.Travel) : 0;
            run.Stopped = wave.Stopped;
            run.StepsDone = 0;
            run.LinkSparks = 0;
            run.EndBurst = !lane;
            run.Ground = go.transform.Find("Ground")?.GetComponent<MeshFilter>();
            run.GroundRenderer = run.Ground != null ? run.Ground.GetComponent<Renderer>() : null;
            run.Light = go.GetComponentInChildren<Light>(true);
            run.Chunks = go.transform.Find("Chunks")?.GetComponent<ParticleSystem>();
            run.Shards = go.transform.Find("Shards")?.GetComponent<ParticleSystem>();
            run.Spray = go.transform.Find("Spray")?.GetComponent<ParticleSystem>();
            run.Dust = go.transform.Find("Dust")?.GetComponent<ParticleSystem>();
            run.Sparks = go.transform.Find("Sparks")?.GetComponent<ParticleSystem>();
            run.Active = true;
            PlaceWreckIronPieces(run, ground);
            int links = lane ? PelagWreckIronRules.LinkCount(run.Start, run.End) : 0;
            if (run.Ground != null)
                _wiGround.Build(FormWaterMesh.MeshFor(run.Ground, PelagWreckIronGround.MeshName), new PelagWreckIronGround.Slam
                {
                    Root = root, Impact = impact, Origin = run.Origin, Dir = run.Dir, Radius = radius, Start = run.Start, End = run.End,
                    HalfWidth = run.HalfWidth, Step = run.Step, SlamTick = tick, Serial = serial, Links = links, Lane = lane
                }, ground);
            BurstWreckIronCrater(run, ground);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-vfx] iron slam tick={tick} radius={radius:F2} lane={lane} start={run.Start:F2} end={run.End:F2} half={run.HalfWidth:F2} links={links} pieces={run.Count}");
        }

        /// <summary>Кадр «железа»: куски встают по фронту, светятся и оседают; земля стареет; свет идёт за фронтом.</summary>
        private void UpdateWreckIron(float shown)
        {
            if (_wiBlock == null) _wiBlock = new MaterialPropertyBlock();
            UpdateWreckLunge(shown);
            UpdateWreckPaintedLunge(shown);
            UpdateWreckComboLunge(shown);
            foreach (WreckIronRun run in _wiRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { HideWreckIronPieces(run); run.Active = false; run.Object = null; run.Count = 0; continue; }
                float age = PelagWreckIronRules.Seconds(shown - run.Tick);
                for (int i = 0; i < run.Count; i++) PoseWreckIronPiece(ref run.Pieces[i], shown);
                if (run.GroundRenderer != null)
                {
                    _wiBlock.Clear();
                    _wiBlock.SetFloat(WiAgeId, Mathf.Max(0f, age));
                    run.GroundRenderer.SetPropertyBlock(_wiBlock);
                }
                float front = run.Lane
                    ? PelagWreckVfxRules.WaveFront(shown, run.Tick, run.Start, run.Step, run.Travel, run.End) : 0f;
                if (run.Light != null)
                {
                    Vector3 at = run.Lane && age > .03f ? run.Origin + run.Dir * front : run.Impact;
                    run.Light.transform.position = new Vector3(at.x, run.Root.y + .8f, at.z);
                    run.Light.intensity = CaptureRig.NoVfx ? 0f : PelagWreckIronRules.LightAt(age, run.Lane ? run.Travel : 1);
                }
                if (run.Lane) EmitWreckIronFront(run, shown, front);
                if (age > PelagWreckIronRules.LifeSeconds(run.Lane ? run.Travel : 1)) ReleaseWreckIron(run);
            }
        }

        private void PoseWreckIronPiece(ref WiPiece piece, float shown)
        {
            if (piece.Pivot == null || piece.Renderer == null) return;
            float local = PelagWreckIronRules.Seconds(shown - piece.Arrival);
            float sink = PelagWreckIronRules.Sink(local);
            bool visible = local > 0f && sink < 1f;
            if (piece.Renderer.enabled != visible) piece.Renderer.enabled = visible;
            if (!visible) return;
            float rise = PelagWreckIronRules.Rise(local);
            float lift = rise <= 1f ? -piece.Depth * (1f - rise) : piece.Depth * .35f * (rise - 1f);
            Vector3 at = piece.Rest + Vector3.up * (lift - piece.Depth * 1.05f * sink);
            float grow = (rise < 1f ? .55f + .45f * rise : 1f) * (1f - .35f * sink);
            piece.Pivot.SetPositionAndRotation(at, piece.Rotation);
            piece.Pivot.localScale = piece.Scale * grow;
            float glow = piece.Kind == WiKind.Link ? PelagWreckIronRules.LinkGlow(local)
                : PelagWreckIronRules.StoneGlow(local) * (piece.Kind == WiKind.Iron ? 1.3f : 1f);
            // Вспышка впечатывания звена — мягко сжатая: свыше 1 растёт втрое медленнее (иначе на съёмке 06.10
            // новое звено вспыхивало белым пятном, а не голубым железом).
            if (piece.Kind == WiKind.Link && glow > 1f) glow = 1f + (glow - 1f) * .35f;
            _wiBlock.Clear();
            _wiBlock.SetFloat(WiGlowId, glow);
            _wiBlock.SetFloat(WiGroundId, piece.GroundY);
            if (piece.Kind != WiKind.Link)
            {
                _wiBlock.SetColor(WiTintId, piece.Tint);
                _wiBlock.SetFloat(WiEarthId, piece.Earth);
            }
            piece.Renderer.SetPropertyBlock(_wiBlock);
        }
    }
}
