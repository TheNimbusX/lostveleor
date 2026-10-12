using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение · ПРИЗРАЧНЫЙ ЯКОРЬ (форма 13; 07.10; ассеты — Editor/PelagWreckComboVfxSetup.Ghost, числа —
    /// PelagWreckGhostLook; целевой кадр v4-form-ghost-anchor.png). Удар выпада этой формы — только всплеск стека выпада в
    /// краске формы (полосы нет: Sim шлёт WreckSlam с Amount 0). В тот же тик показа высоко над точкой выпада проявляется
    /// 3D якорь Пелага и падает с разгоном; в тик события WreckGhostAnchor (77) — ровно через WreckGhostDelayTicks — входит
    /// в землю (сплющивание, вспышка), в этот кадр рождается удар (.WreckGhostLand: кольцо до круга Sim, всплеск, трещины,
    /// камни, свет); якорь стоит ~0,45 с и рассыпается вверх осколками. Задетые событием — знак выпада в фиолетовом (круг).
    /// Серию сорвали после удара — якорь всё равно падает (как в Sim); событие не пришло к тику падения — тает в воздухе.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckGhostRun
        {
            public bool Active, Confirmed, Landed, Fizzled;
            public int Fx = -1, Serial, SlamTick, LandTick, Amount;
            public GameObject Object;
            public Vector3 Point, Dir;
            public Transform Tilt, BodyT;
            public Renderer Body, GlowIn, GlowOut;
            public readonly Renderer[] Parts = new Renderer[3];
            public ParticleSystem Streaks, Shards, Motes;
            public int LandFx = -1;
            public GameObject LandObject;
            public Light Light;
            public readonly List<int> Targets = new List<int>(8);
        }

        private readonly WreckGhostRun[] _wgRuns = { new WreckGhostRun(), new WreckGhostRun() };
        private MaterialPropertyBlock _wgBlock;
        private int _wgHitTick = -1, _wgHitAmount;
        private Vector3 _wgHitPoint;
        private static readonly int WgAgeId = Shader.PropertyToID("_Age"), WgAppearId = Shader.PropertyToID("_Appear"),
            WgDissolveId = Shader.PropertyToID("_Dissolve"), WgFlashId = Shader.PropertyToID("_Flash");

        /// <summary>Свет удара в пике, у.е. Light (точечный, 7,5 м): камням, мобам; пол арены светит кольцо (маска, канал b).</summary>
        private const float WgLightPeak = 3.5f;

        private bool WreckGhostReady => _pools != null && (int)PelagVfxId.WreckGhostLand < _pools.Length
            && _pools[(int)PelagVfxId.WreckGhostAnchor] != null && _pools[(int)PelagVfxId.WreckGhostLand] != null;

        /// <summary>Удар выпада (тик показа, из PlayWreckSlam): у Призрачного якоря — якорь проявляется над точкой и падает.</summary>
        private void BeginWreckGhost(in WkPending p)
        {
            if (p.Wave.Look != PelagForm.WreckGhostAnchor || !WreckGhostReady || CaptureRig.NoVfx) return;
            StartWreckGhost(p.Wave.Impact, p.Wave.Dir, p.Tick, p.Serial);
        }

        private WreckGhostRun StartWreckGhost(Vector3 impact, Vector3 dir, int slamTick, int serial)
        {
            WreckGhostRun run = !_wgRuns[0].Active ? _wgRuns[0] : !_wgRuns[1].Active ? _wgRuns[1]
                : _wgRuns[0].SlamTick <= _wgRuns[1].SlamTick ? _wgRuns[0] : _wgRuns[1];
            ReleaseWreckGhost(run);
            if (!TryAcquire(PelagVfxId.WreckGhostAnchor, out GameObject go, out PelagVfxElement element)) return null;
            System.Func<float, float, float> ground = WreckIronGroundAt(impact.y);
            var point = new Vector3(impact.x, ground(impact.x, impact.z), impact.z);
            int index = ReserveActive();
            element.Begin(point, Quaternion.identity);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckGhostAnchor, Object = go, Element = element,
                Duration = PelagWreckGhostLook.FallSeconds + PelagWreckGhostLook.AfterLandSeconds + .8f,
                Start = point, End = point, Motion = Motion.Static, FollowIndex = -1
            };
            Transform t = go.transform;
            run.Active = true;
            run.Confirmed = run.Landed = run.Fizzled = false;
            run.Fx = index;
            run.Object = go;
            run.Point = point;
            dir.y = 0f;
            run.Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : PlayerFacing();
            run.SlamTick = slamTick;
            run.LandTick = slamTick + PelagWreckGhostLook.FallTicks;
            run.Serial = serial;
            run.Amount = 0;
            run.Targets.Clear();
            run.Tilt = t.Find("Tilt");
            run.BodyT = run.Tilt != null ? run.Tilt.Find("Body") : null;
            run.Body = run.BodyT != null ? run.BodyT.GetComponent<Renderer>() : null;
            run.GlowIn = run.Tilt != null ? run.Tilt.Find("GlowIn")?.GetComponent<Renderer>() : null;
            run.GlowOut = run.Tilt != null ? run.Tilt.Find("GlowOut")?.GetComponent<Renderer>() : null;
            run.Streaks = t.Find("Streaks")?.GetComponent<ParticleSystem>();
            run.Shards = t.Find("Shards")?.GetComponent<ParticleSystem>();
            run.Motes = t.Find("Motes")?.GetComponent<ParticleSystem>();
            run.Parts[0] = run.Body; run.Parts[1] = run.GlowIn; run.Parts[2] = run.GlowOut;
            foreach (Renderer r in run.Parts) if (r != null) r.enabled = true;
            // Событие 77 уже пришло (вид отстаёт от Sim на 2 тика — так бывает, если удар показан поздно).
            if (_wgHitTick == run.LandTick) Confirm(run);
            UpdateWreckGhostRun(run, slamTick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-ghost] fall slam={slamTick} land={run.LandTick} point={point.ToString("F2")}");
            return run;
        }

        /// <summary>Событие WreckGhostAnchor (время Sim): точка и радиус падения, задетые — следом в этом же тике.</summary>
        private void WreckGhostLanded(in SimEvent e, int tick)
        {
            _wgHitTick = tick;
            _wgHitAmount = e.Amount;
            _wgHitPoint = WreckWorld(e.Position, PlayerPosition().y);
            if (!WreckGhostReady || CaptureRig.NoVfx) return;
            WreckGhostRun run = null;
            foreach (WreckGhostRun r in _wgRuns)
                if (r.Active && !r.Landed && r.LandTick == tick) run = r;
            // Удар не показывали (очередь сброшена): якорь появляется на последних тиках падения.
            if (run == null) run = StartWreckGhost(_wgHitPoint, PlayerFacing(), tick - PelagWreckGhostLook.FallTicks, e.ActionVariant);
            if (run != null) Confirm(run);
        }

        private void Confirm(WreckGhostRun run)
        {
            run.Confirmed = true;
            run.Amount = _wgHitAmount;
            System.Func<float, float, float> ground = WreckIronGroundAt(_wgHitPoint.y);
            run.Point = new Vector3(_wgHitPoint.x, ground(_wgHitPoint.x, _wgHitPoint.z), _wgHitPoint.z);
        }

        /// <summary>
        /// Damage слота Крушения в тик падения якоря: знак выпада (круг) в фиолетовом от точки падения, отдача тела наружу
        /// в кадр удара. False — не удар якоря.
        /// </summary>
        private bool TakeWreckGhostDamage(in SimEvent e, int tick)
        {
            if (tick != _wgHitTick || _wgHitTick < 0) return false;
            Vector3 at = WreckWorld(e.Position, PlayerPosition().y);
            float radius = PelagWreckGhostLook.RingRadius(_wgHitAmount);
            WreckEnqueue(new WkPending
            {
                Kind = WkKind.Hit, Hit = WreckVfxHit.Circle, Tick = tick, Target = e.Target, At = EntityPosition(e.Target, at),
                Form = PelagForm.WreckGhostAnchor,
                Wave = new WkWave { Impact = _wgHitPoint, Dir = PlayerFacing(), ImpactRadius = radius, Tick = tick, Look = PelagForm.WreckGhostAnchor }
            });
            foreach (WreckGhostRun r in _wgRuns)
                if (r.Active && r.LandTick == tick && !r.Targets.Contains(e.Target)) r.Targets.Add(e.Target);
            return true;
        }

        /// <summary>Кадр (из UpdateWreckVfx, тик показа).</summary>
        private void UpdateWreckGhost(float shown)
        {
            foreach (WreckGhostRun run in _wgRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                UpdateWreckGhostRun(run, shown);
            }
        }

        private void ReleaseWreckGhost(WreckGhostRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            if (run.LandObject != null && WkStill(run.LandFx, run.LandObject))
            {
                if (run.Light != null) run.Light.intensity = 0f;
                Release(run.LandFx);
            }
            run.Active = false;
            run.Fx = run.LandFx = -1;
            run.Object = run.LandObject = null;
            run.Light = null;
        }

        private void ForgetWreckGhost()
        {
            foreach (WreckGhostRun run in _wgRuns) ReleaseWreckGhost(run);
            _wgHitTick = -1;
        }

        // ---- краска призрака на кусках общего стека (удар выпада, знак задетого)

        private static readonly int WgShadeId = Shader.PropertyToID("_Shade"), WgCoreId = Shader.PropertyToID("_Core"),
            WgOutlineId = Shader.PropertyToID("_Outline");
        private static readonly string[] WgLungeParts = { "Half", "Star", "StarEcho", "GroundStar", "Glow", "Sparks" };
        private static readonly string[] WgMarkParts = { "Burst" };
        private static readonly Color WgShade = new Color(.82f, .78f, 1f, 1f), WgCore = new Color(1.0f, 1.0f, 1.45f, 1f),
            WgContour = new Color(.023f, .014f, .147f, .9f);
        private MaterialPropertyBlock _wgTintBlock;

        /// <summary>
        /// Призрачный якорь (07.10): у кусков общего стека (всплеск удара выпада, звезда знака задетого) тень у края —
        /// бледно-фиолетовая, ядро — белое, контур — тёмно-фиолетовый (у базы — синяя тень и чернила: призрак выходил
        /// кислотно-синим). Не призрак — значения материала обратно (объекты пула общие), база рисуется как прежде.
        /// </summary>
        private void TintWreckGhostBits(Transform root, bool ghost, bool mark)
        {
            if (root == null) return;
            if (_wgTintBlock == null) _wgTintBlock = new MaterialPropertyBlock();
            foreach (string name in mark ? WgMarkParts : WgLungeParts)
            {
                Renderer r = root.Find(name)?.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || !m.HasProperty(WgShadeId) || !m.HasProperty(WgCoreId) || !m.HasProperty(WgOutlineId)) continue;
                r.GetPropertyBlock(_wgTintBlock);
                if (!ghost && _wgTintBlock.isEmpty) continue;
                _wgTintBlock.SetColor(WgShadeId, ghost ? WgShade : m.GetColor(WgShadeId));
                _wgTintBlock.SetColor(WgCoreId, ghost ? WgCore : m.GetColor(WgCoreId));
                _wgTintBlock.SetColor(WgOutlineId, ghost ? WgContour : m.GetColor(WgOutlineId));
                r.SetPropertyBlock(_wgTintBlock);
            }
        }
    }
}
