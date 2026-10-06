using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Слои форм Броска якоря (спека §5.3; видимый край = край урона, вода — на настоящей земле):
    ///  • ВЕЕР (кадр C-fan, индиго-фиолет): два водяных призрака якоря летят по полосам ±30° Sim
    ///    (своя дальность у каждой), цепи — ленты воды от руки; в ловлю рассыпаются брызгами у руки.
    ///    Призрак — меш нашей головы на шейдере пенного двойника (Razlom/Squall Foam Ghost), металл один;
    ///  • НЕВОД (кадр B-net, кобальт): лист воды сети 3 м с белыми нитями и узлами раскрывается за тик до
    ///    натяга (Sim ловит сетью в T), в возврате стягивается за головой к герою валиком пены; пойманным —
    ///    комья пены у ног (узлы). Гарпун — в .AnchorThrowCues/.AnchorThrowFlight (укус, голова в теле).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AnchorThrowGhost
        {
            public bool Active, Dropped;
            public int Lane, Serial = -1, Fx = -1, ChainFx = -1;
            public float DropFrom = -1f, Born, Flow, DropCarry, ReleaseHeight, ReleaseShown;
            public AnchorThrowState Snap;
            public GameObject Object, ChainObject;
            public MeshFilter ChainFilter;
            public ParticleSystem ChainDrops, Burst;
            public Renderer[] Renderers;
            public Transform Ring;
            public Vector3 ReleaseError;
            public readonly PelagAbordageRibbon Chain = new PelagAbordageRibbon();
        }

        private sealed class AnchorThrowNetRun
        {
            public bool Active;
            public int Serial = -1, Fx = -1;
            public float FullLength, Flow, BreakFrom = -1f, DropCarry;
            public GameObject Object;
            public MeshFilter Water, Strands;
            public ParticleSystem Foam, Drops;
            public Vector3 Root, Dir;
            public AnchorThrowState Snap;
            public readonly PelagAnchorThrowNetWater Sheet = new PelagAnchorThrowNetWater();
            public readonly PelagAnchorThrowNetStrands Lattice = new PelagAnchorThrowNetStrands();
            public readonly AnchorThrowLaneGround Ground = new AnchorThrowLaneGround();
        }

        private readonly AnchorThrowGhost[] _atGhosts = { new AnchorThrowGhost(), new AnchorThrowGhost() };
        private readonly AnchorThrowNetRun _atNet = new AnchorThrowNetRun();
        private MaterialPropertyBlock _atGhostBlock;
        private static readonly int AtOpacityId = Shader.PropertyToID("_Opacity"), AtDissolveId = Shader.PropertyToID("_Dissolve"),
            AtClockId = Shader.PropertyToID("_Clock"), AtBaseId = Shader.PropertyToID("_Base");

        // ---------------------------------------------------------------- Веер

        private void BeginAnchorThrowGhosts(AnchorThrowRun run, Vector3 fist, float shown)
        {
            if (run.Snap.Lanes != 3 || CaptureRig.NoVfx) return;
            for (int g = 0; g < _atGhosts.Length; g++)
            {
                AnchorThrowGhost ghost = _atGhosts[g];
                EndAnchorThrowGhost(ghost);
                ghost.Lane = g + 1;
                ghost.Fx = AtSpawn(PelagVfxId.AnchorThrowGhost, fist, Quaternion.LookRotation(AtDir(run.Snap, ghost.Lane), Vector3.up),
                    0f, 6f, PelagForm.AnchorThrowFan, false, out ghost.Object);
                if (ghost.Fx < 0) continue;
                ghost.Object.transform.localScale = Vector3.one;
                Transform t = ghost.Object.transform;
                Transform model = t.Find("Head");
                ghost.Renderers = (model != null ? model : t).GetComponentsInChildren<MeshRenderer>(true);
                ghost.Ring = t.Find("Head/Ring") ?? t.Find("Ring");
                ghost.Burst = t.Find("Burst")?.GetComponent<ParticleSystem>();
                ghost.ChainFx = AtSpawnRibbon(PelagForm.AnchorThrowFan, fist, out ghost.ChainObject, out ghost.ChainFilter, out ghost.ChainDrops);
                ghost.Chain.Begin();
                ghost.Serial = run.Serial;
                ghost.Snap = run.Snap;
                ghost.ReleaseHeight = run.ReleaseHeight;
                AtHeadOnLine(ghost.ReleaseHeight, run.Snap, ghost.Lane, shown, out Vector3 line);
                ghost.ReleaseError = fist - line;
                ghost.ReleaseShown = shown;
                ghost.Born = Time.time;
                ghost.Flow = ghost.DropCarry = 0f;
                ghost.DropFrom = -1f;
                ghost.Dropped = false;
                ghost.Active = true;
            }
        }

        private void UpdateAnchorThrowGhosts(float shown, float dt)
        {
            AnchorThrowRun run = _atRun;
            foreach (AnchorThrowGhost ghost in _atGhosts)
            {
                if (!ghost.Active) continue;
                if (!AtStill(ghost.Fx, ghost.Object)) { EndAnchorThrowGhost(ghost); continue; }
                bool live = run.Active && run.Serial == ghost.Serial;
                if (live) ghost.Snap = run.Snap;
                AnchorThrowState s = ghost.Snap;
                Vector3 dir = AtDir(s, ghost.Lane);
                AtHeadOnLine(ghost.ReleaseHeight, s, ghost.Lane, shown, out Vector3 head);
                head += ghost.ReleaseError * PelagAnchorThrowVfxRules.ReleaseSeam(shown, ghost.ReleaseShown);
                head -= dir * PelagAnchorThrowVfxRules.Jerk(shown, s.TautTick);
                Vector3 grip = live ? run.Grip : ChainHandPosition();
                ghost.Object.transform.SetPositionAndRotation(head, Quaternion.LookRotation(FlatDirection(grip, head), Vector3.up));

                float opacity = PelagAnchorThrowVfxRules.GhostOpacity(s, shown);
                float dissolve = PelagAnchorThrowVfxRules.GhostDissolve(s, shown);
                if (ghost.DropFrom >= 0f)
                {
                    float gone = PelagAnchorThrowVfxRules.Clamp01((shown - ghost.DropFrom) / 4f);
                    dissolve = Mathf.Max(dissolve, gone);
                    opacity *= 1f - gone;
                }
                TintAnchorThrowGhost(ghost, opacity, dissolve, AtGround(head));

                Vector3 ring = ghost.Ring != null ? ghost.Ring.position : head - dir * .35f;
                float from = ghost.DropFrom >= 0f ? ghost.DropFrom : s.CatchTick;
                float age = PelagAnchorThrowVfxRules.BreakAge(shown, from);
                float tension = PelagAnchorThrowVfxRules.Tension(s, shown);
                ghost.Flow += dt * (shown < s.TautTick ? AtSleeveFlowFlight : AtSleeveFlowTaut);
                if (AtStill(ghost.ChainFx, ghost.ChainObject))
                {
                    Camera camera = Camera.main;
                    Vector3 eye = camera != null ? camera.transform.position : grip + Vector3.up * 10f;
                    Vector3 down = camera != null ? -camera.transform.up : Vector3.down;
                    float half = PelagAnchorThrowVfxRules.SleeveHalfWidth(tension, PelagForm.AnchorThrowFan) * 1.3f;
                    ghost.ChainObject.transform.position = grip;
                    if (ghost.ChainFilter != null)
                        ghost.Chain.Build(FormWaterMesh.MeshFor(ghost.ChainFilter, PelagAbordageRibbon.MeshName), grip, grip, ring, eye, down, 0f,
                            half * .7f, half, age, age, ghost.Flow, .1f + .3f * tension, opacity, Time.time);
                    if (age <= 0f && tension > .9f && dt > 0f && !CaptureRig.NoVfx)
                    {
                        ghost.DropCarry += dt * PelagAnchorThrowVfxRules.SleeveDropRate(tension) * Vector3.Distance(grip, ring);
                        int count = (int)ghost.DropCarry;
                        ghost.DropCarry -= count;
                        if (count > 0) EmitAnchorThrowDrops(ghost.ChainDrops, PelagForm.AnchorThrowFan, grip, ring, -(ring - grip).normalized, count, 1f);
                    }
                    if (age > .55f) { Release(ghost.ChainFx); ghost.ChainFx = -1; ghost.ChainObject = null; }
                }
                // Брызги «рассыпался у руки» — частицы этого объекта: держать его, пока они не долетят (~0,45 с).
                if (opacity <= .01f && dissolve >= .99f && ghost.ChainFx < 0 && shown >= from + 14f) EndAnchorThrowGhost(ghost);
            }
        }

        /// <summary>Призрак: цвета Веера, проявление и растворение пеной — свой блок (шейдер двойника Шквала).</summary>
        private void TintAnchorThrowGhost(AnchorThrowGhost ghost, float opacity, float dissolve, float ground)
        {
            if (ghost.Renderers == null) return;
            if (_atGhostBlock == null) _atGhostBlock = new MaterialPropertyBlock();
            PelagAnchorThrowFormLook.Palette palette = PelagAnchorThrowFormLook.For(PelagForm.AnchorThrowFan);
            foreach (Renderer r in ghost.Renderers)
            {
                if (r == null) continue;
                _atGhostBlock.Clear();
                PelagAnchorThrowFormLook.Write(_atGhostBlock, r.sharedMaterial, palette);
                _atGhostBlock.SetFloat(AtOpacityId, opacity);
                _atGhostBlock.SetFloat(AtDissolveId, dissolve);
                _atGhostBlock.SetFloat(AtClockId, Time.time - ghost.Born);
                _atGhostBlock.SetFloat(AtBaseId, ground);
                r.SetPropertyBlock(_atGhostBlock);
                r.enabled = opacity > .005f;
            }
        }

        private void EndAnchorThrowGhost(AnchorThrowGhost ghost)
        {
            if (ghost.Active && AtStill(ghost.Fx, ghost.Object)) Release(ghost.Fx);
            if (AtStill(ghost.ChainFx, ghost.ChainObject)) Release(ghost.ChainFx);
            ghost.Active = false;
            ghost.Fx = ghost.ChainFx = -1;
            ghost.Object = ghost.ChainObject = null;
            ghost.Renderers = null;
        }

        // ---------------------------------------------------------------- Невод

        private void BeginAnchorThrowNet(AnchorThrowRun run)
        {
            AnchorThrowNetRun net = _atNet;
            AnchorThrowState s = run.Snap;
            net.Serial = run.Serial;
            if (CaptureRig.NoVfx) return;
            net.Dir = AtDir(s, 0);
            var origin = new Vector3(s.Origin.X.ToFloat(), PlayerPosition().y, s.Origin.Y.ToFloat());
            origin.y = AtGround(origin);
            net.Root = origin;
            net.FullLength = Mathf.Max(.5f, s.Reach0.ToFloat() - PelagAnchorThrowVfxRules.HandReach);
            net.Ground.Sample(origin, net.Dir, net.FullLength + .4f, PelagAnchorThrowVfxRules.NetWaterHalfWidth + .3f, AnchorThrowGround());
            net.Fx = AtSpawn(PelagVfxId.AnchorThrowNet, origin, Quaternion.identity, 0f, 6f, PelagForm.AnchorThrowNet, false, out net.Object);
            if (net.Fx < 0) return;
            net.Object.transform.localScale = Vector3.one;
            Transform t = net.Object.transform;
            net.Water = t.Find("Water")?.GetComponent<MeshFilter>();
            net.Strands = t.Find("Strands")?.GetComponent<MeshFilter>();
            // Нити и узлы — свои белые цвета материала, не вода формы.
            t.Find("Strands")?.GetComponent<MeshRenderer>()?.SetPropertyBlock(null);
            net.Foam = t.Find("Foam")?.GetComponent<ParticleSystem>();
            net.Drops = t.Find("Drops")?.GetComponent<ParticleSystem>();
            net.Sheet.Begin();
            net.Lattice.Begin();
            net.Snap = s;
            net.Flow = net.DropCarry = 0f;
            net.BreakFrom = -1f;
            net.Active = true;
            // Сеть разворачивается: брызги вдоль обоих краёв полосы.
            Vector3 right = new Vector3(net.Dir.z, 0f, -net.Dir.x) * PelagAnchorThrowVfxRules.NetWaterHalfWidth;
            Vector3 far = origin + net.Dir * net.FullLength;
            EmitAnchorThrowDrops(net.Drops, PelagForm.AnchorThrowNet, origin + right, far + right, right.normalized + Vector3.up, 14, 1.2f);
            EmitAnchorThrowDrops(net.Drops, PelagForm.AnchorThrowNet, origin - right, far - right, -right.normalized + Vector3.up, 14, 1.2f);
        }

        private void UpdateAnchorThrowNet(float shown, float dt)
        {
            AnchorThrowRun run = _atRun;
            AnchorThrowNetRun net = _atNet;
            if (!net.Active)
            {
                AnchorThrowState plan = run.Snap;
                if (run.Active && run.Released && run.Form == PelagForm.AnchorThrowNet && net.Serial != run.Serial && run.RetractStart < 0f
                    && plan.TautTick > 0 && plan.Reach0.ToFloat() >= 1f
                    && shown >= plan.TautTick + PelagAnchorThrowVfxRules.NetOpenFrom)
                    BeginAnchorThrowNet(run);
                if (!net.Active) return;
            }
            if (!AtStill(net.Fx, net.Object)) { net.Active = false; return; }
            if (run.Active && run.Serial == net.Serial) net.Snap = run.Snap;
            AnchorThrowState s = net.Snap;
            float open = PelagAnchorThrowVfxRules.NetOpen(shown, s.TautTick);
            float far = PelagAnchorThrowVfxRules.NetFar(s, shown) - PelagAnchorThrowVfxRules.HandReach;
            float gathered = PelagAnchorThrowVfxRules.Clamp01(far / net.FullLength);
            float half = PelagAnchorThrowVfxRules.NetWaterHalfWidth * open * Mathf.Lerp(.45f, 1f, Mathf.Sqrt(gathered));
            float age = net.BreakFrom >= 0f ? PelagAnchorThrowVfxRules.BreakAge(shown, net.BreakFrom) : 0f;
            net.Flow += dt * (shown > s.TautTick ? 2.4f : .6f);
            if (net.Water != null)
                net.Sheet.Build(FormWaterMesh.MeshFor(net.Water, PelagAnchorThrowNetWater.MeshName), net.Root, net.Dir, 0f, far, half,
                    1f - gathered, age, net.Flow, Time.time, 1f, net.Ground, 0f);
            if (net.Strands != null)
                net.Lattice.Build(FormWaterMesh.MeshFor(net.Strands, PelagAnchorThrowNetStrands.MeshName), net.Root, net.Dir, 0f, far,
                    net.FullLength, half * .92f, age, open, net.Ground, 0f);
            // Валик у дальнего края в возврате сыплет каплями.
            if (shown > s.TautTick && age <= 0f && dt > 0f && !CaptureRig.NoVfx)
            {
                net.DropCarry += dt * 30f;
                int count = (int)net.DropCarry;
                net.DropCarry -= count;
                Vector3 edge = net.Root + net.Dir * far;
                Vector3 right = new Vector3(net.Dir.z, 0f, -net.Dir.x) * half;
                if (count > 0) EmitAnchorThrowDrops(net.Drops, PelagForm.AnchorThrowNet, edge - right, edge + right, -net.Dir + Vector3.up, count, 1f);
            }
            if (age > .6f) EndAnchorThrowNet();
        }

        /// <summary>Сеть поймала тело (Hit полоса 3): комья пены у ног — узел сети.</summary>
        private void KnotAnchorThrowNet(Vector3 body)
        {
            AnchorThrowNetRun net = _atNet;
            if (!net.Active || net.Foam == null) return;
            Vector3 feet = AtFeet(body) + Vector3.up * .25f;
            EmitAnchorThrowDrops(net.Foam, PelagForm.AnchorThrowNet, feet - Vector3.right * .25f, feet + Vector3.right * .25f, Vector3.up * .5f, 10, .8f);
        }

        private void EndAnchorThrowNet()
        {
            AnchorThrowNetRun net = _atNet;
            if (AtStill(net.Fx, net.Object)) Release(net.Fx);
            net.Active = false;
            net.Fx = -1;
            net.Object = null;
        }

        // ---------------------------------------------------------------- общий ход форм

        /// <summary>Ловля: призраки рассыпаются брызгами у руки, сеть рвётся на капли.</summary>
        private void CatchAnchorThrowForms()
        {
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : 0f;
            foreach (AnchorThrowGhost ghost in _atGhosts)
                if (ghost.Active && AtStill(ghost.Fx, ghost.Object) && !CaptureRig.NoVfx)
                {
                    Vector3 at = ghost.Object.transform.position;
                    EmitAnchorThrowDrops(ghost.Burst != null ? ghost.Burst : ghost.ChainDrops, PelagForm.AnchorThrowFan, at - Vector3.up * .2f,
                        at + Vector3.up * .2f, AtDir(_atRun.Snap, ghost.Lane) + Vector3.up * .3f, 24, 1.5f);
                }
            if (_atNet.Active && _atNet.BreakFrom < 0f) _atNet.BreakFrom = shown;
        }

        /// <summary>Срыв до ловли: призраки тают на месте, сеть рвётся сразу.</summary>
        private void DropAnchorThrowForms()
        {
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : 0f;
            foreach (AnchorThrowGhost ghost in _atGhosts)
                if (ghost.Active && ghost.DropFrom < 0f) ghost.DropFrom = shown;
            if (_atNet.Active && _atNet.BreakFrom < 0f) _atNet.BreakFrom = shown;
        }

        /// <summary>Показ каста закончен: не пойманные призраки тают на месте, сеть рвётся — доживают сами.</summary>
        private void EndAnchorThrowForms()
        {
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : 0f;
            foreach (AnchorThrowGhost ghost in _atGhosts)
                if (ghost.Active && ghost.DropFrom < 0f && shown < ghost.Snap.CatchTick) ghost.DropFrom = shown;
            if (_atNet.Active && _atNet.BreakFrom < 0f) _atNet.BreakFrom = shown;
        }

        private void ForgetAnchorThrowForms()
        {
            foreach (AnchorThrowGhost ghost in _atGhosts) EndAnchorThrowGhost(ghost);
            EndAnchorThrowNet();
            _atNet.Serial = -1;
        }
    }
}
