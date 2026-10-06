using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — кого тянет и куда (спека §5.2): от пояса каждого лёгкого задетого к цепи —
    /// тонкая струйка воды (видно, кого потянет, держится до ловли); с натяга под каждым тянутым
    /// телом — пенная борозда по его настоящему пути к месту на полукольце (кодом следа рывка
    /// PelagDashWake, префаб следа Водоворота под своим id; у Гарпуна — маджентовая), за головой
    /// на возврате — вспаханная пена (префаб следа рывка, уже). Борозда — только если тело правда
    /// поехало: тяжёлые и элита стоят — следа нет.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private const int AtTowSlots = 8;
        /// <summary>Корень борозды позади тела, м: голова следа (HeadLead) встаёт у ног, а не впереди тела.</summary>
        private const float AtDragBack = PelagDashWake.HeadLead - .20f;
        /// <summary>Вспаханная пена за головой — уже следа рывка (голова, а не тело героя).</summary>
        private const float AtHeadWakeWidth = .55f;

        private sealed class AtWake
        {
            public readonly PelagDashWake Wake = new PelagDashWake();
            public bool Active, Started;
            public int Fx = -1, Target = -1, StillFrames;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem SkidFoam, SkidDrops, TailDrops;
            public Vector3 From, Direction, Start;
            public float Ground, SkidAlong, FoamCarry, DropCarry, LastShift, Born, Cap;
            public PelagForm Form;
        }

        private sealed class AnchorThrowTow
        {
            public bool Active;
            public int Target = -1, Serial = -1, Fx = -1, Lane;
            public float Flow, BreakFrom = -1f;
            public GameObject Object;
            public MeshFilter Filter;
            public readonly PelagAbordageRibbon Ribbon = new PelagAbordageRibbon();
            public readonly AtWake Drag = new AtWake();
        }

        private AnchorThrowTow[] _atTows;
        private readonly AtWake _atHeadWake = new AtWake();
        private int _atHeadWakeSerial = -1;

        private AnchorThrowTow[] AtTows
        {
            get
            {
                if (_atTows != null) return _atTows;
                _atTows = new AnchorThrowTow[AtTowSlots];
                for (int i = 0; i < _atTows.Length; i++) _atTows[i] = new AnchorThrowTow();
                return _atTows;
            }
        }

        /// <summary>Hit с флагом «на тягу»: тело запомнено для борозды; <paramref name="tether"/> — струйка к цепи.</summary>
        private AnchorThrowTow BeginAnchorThrowTow(int target, bool tether, int lane)
        {
            AnchorThrowTow free = null;
            foreach (AnchorThrowTow t in AtTows)
            {
                if (t.Active && t.Target == target && t.Serial == _atRun.Serial) return t;
                if (free == null && !t.Active) free = t;
            }
            if (free == null) return null;
            free.Active = true;
            free.Target = target;
            free.Serial = _atRun.Serial;
            free.Lane = lane;
            free.BreakFrom = -1f;
            free.Flow = 0f;
            free.Fx = -1;
            free.Object = null;
            ReleaseAtWake(free.Drag);
            if (!tether || CaptureRig.NoVfx) return free;
            free.Fx = AtSpawnRibbon(_atRun.Form, EntityPosition(target, PlayerPosition()), out free.Object, out free.Filter, out _);
            free.Ribbon.Begin();
            return free;
        }

        private void UpdateAnchorThrowTows(Simulation sim, float shown, float dt)
        {
            if (_atTows == null) return;
            AnchorThrowRun run = _atRun;
            Camera camera = Camera.main;
            foreach (AnchorThrowTow t in _atTows)
            {
                if (!t.Active) continue;
                bool alive = (uint)t.Target < (uint)sim.Entities.Count && sim.Entities.Alive[t.Target];
                if (!alive && t.BreakFrom < 0f) t.BreakFrom = shown;
                if (AtStill(t.Fx, t.Object))
                {
                    Vector3 body = EntityPosition(t.Target, t.Object.transform.position);
                    Vector3 waist = body + Vector3.up * (PelagAbordageVfxRules.BiteHeight(AtBodyRadius(t.Target)) * .8f);
                    bool live = run.Active && run.Serial == t.Serial;
                    Vector3 a = live ? run.Grip : waist, b = live ? run.Ring : waist;
                    // Задетый призраком Веера — к водяной цепи своего призрака.
                    if (live && (t.Lane == 1 || t.Lane == 2))
                    {
                        AnchorThrowGhost ghost = _atGhosts[t.Lane - 1];
                        if (ghost.Active && AtStill(ghost.Fx, ghost.Object))
                            b = ghost.Ring != null ? ghost.Ring.position : ghost.Object.transform.position;
                    }
                    float k = PelagAnchorThrowVfxRules.ClosestOnSegment(a.x, a.z, b.x, b.z, waist.x, waist.z);
                    Vector3 onChain = Vector3.Lerp(a, b, k);
                    float age = PelagAnchorThrowVfxRules.BreakAge(shown, t.BreakFrom < 0f ? float.MaxValue : t.BreakFrom);
                    t.Flow += dt * 2.5f;
                    Vector3 eye = camera != null ? camera.transform.position : waist + Vector3.up * 10f;
                    t.Object.transform.position = waist;
                    if (t.Filter != null)
                        t.Ribbon.Build(FormWaterMesh.MeshFor(t.Filter, PelagAbordageRibbon.MeshName), waist, waist, onChain, eye, Vector3.down, 0f,
                            .016f, .012f, age, age, t.Flow, .15f, 1f, Time.time);
                    if (age > .55f) { Release(t.Fx); t.Fx = -1; t.Object = null; }
                }
                if (t.Fx < 0 && !t.Drag.Active) t.Active = false;
            }
        }

        /// <summary>Натяг: под каждым тянутым телом — борозда к его месту на полукольце (Sim AnchorThrowLandingSpot).</summary>
        private void StartAnchorThrowDrags()
        {
            if (_atTows == null) return;
            foreach (AnchorThrowTow t in _atTows) StartAtDrag(t);
        }

        private void StartAtDrag(AnchorThrowTow t)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || CaptureRig.NoVfx) return;
            if (!t.Active || t.Serial != _atRun.Serial || t.Drag.Active || !sim.AnchorThrowReeled(t.Target)) return;
            Vector3 body = EntityPosition(t.Target, PlayerPosition());
            FixVec2 spot = sim.AnchorThrowLandingSpot(t.Target);
            Vector3 to = new Vector3(spot.X.ToFloat(), body.y, spot.Y.ToFloat());
            Vector3 path = to - body;
            path.y = 0f;
            if (path.magnitude < .3f) return;
            AtWake d = t.Drag;
            d.Active = true;
            d.Started = false;
            d.Target = t.Target;
            d.Start = body;
            d.Direction = path.normalized;
            d.Cap = path.magnitude + .3f;
            d.StillFrames = 0;
            d.LastShift = 0f;
            d.Born = Time.time;
            d.Ground = AtGround(body);
            d.Form = _atRun.Form;
        }

        /// <summary>Натяг: за головой на возврате — вспаханная пена от конца полосы к руке (у Невода её прячет сеть).</summary>
        private void StartAnchorThrowHeadWake()
        {
            AnchorThrowRun run = _atRun;
            AnchorThrowState s = run.Snap;
            if (CaptureRig.NoVfx || run.Form == PelagForm.AnchorThrowNet || (s.Stuck && run.HarpoonPulled) || _atHeadWakeSerial == run.Serial) return;
            float reach = s.Reach0.ToFloat();
            if (reach < 1.5f) return;
            _atHeadWakeSerial = run.Serial;
            AtWake w = _atHeadWake;
            ReleaseAtWake(w);
            Vector3 dir = AtDir(s, 0);
            var end = new Vector3(s.Center.X.ToFloat(), 0f, s.Center.Y.ToFloat()) + dir * reach;
            end.y = AtGround(end);
            Vector3 root = end + dir * AtDragBack;
            root.y = end.y;
            if (!BindAtWake(w, PelagVfxId.AnchorThrowWake, root, -dir, run.Form)) return;
            w.Object.transform.localScale = new Vector3(AtHeadWakeWidth, 1f, 1f);
            w.From = end;
            w.Direction = -dir;
            w.Ground = end.y;
            w.Started = true;
            w.Target = -1;
            w.Cap = reach - PelagAnchorThrowVfxRules.HandReach;
            w.Wake.Begin(w.Cap + .3f, Random.Range(0f, 8f), GameUserSettings.FlashScale);
            if (w.Filter != null) w.Wake.Build(PelagDashWake.MeshFor(w.Filter));
        }

        private bool BindAtWake(AtWake w, PelagVfxId id, Vector3 root, Vector3 dir, PelagForm form)
        {
            int fx = AtSpawn(id, root, Quaternion.LookRotation(dir, Vector3.up), 0f, PelagDashWake.MaxLife + .2f, form, true, out GameObject go);
            if (fx < 0) return false;
            go.transform.localScale = Vector3.one;
            Transform t = go.transform;
            w.Active = true;
            w.Fx = fx;
            w.Object = go;
            w.Form = form;
            w.Filter = t.Find("Wake")?.GetComponent<MeshFilter>();
            w.SkidFoam = t.Find("SkidFoam")?.GetComponent<ParticleSystem>();
            w.SkidDrops = t.Find("SkidDrops")?.GetComponent<ParticleSystem>();
            w.TailDrops = t.Find("TailDrops")?.GetComponent<ParticleSystem>();
            w.SkidAlong = 0f;
            w.FoamCarry = w.DropCarry = .6f;
            return true;
        }

        private void ReleaseAtWake(AtWake w)
        {
            if (w.Active && AtStill(w.Fx, w.Object)) Release(w.Fx);
            w.Active = w.Started = false;
            w.Fx = -1;
            w.Object = null;
        }

        /// <summary>Кадр борозд: рост за телом (за головой), занос у ног, распад, возврат в пул.</summary>
        private void UpdateAnchorThrowWakes(float dt)
        {
            if (_atHeadWake.Active)
            {
                AtWake w = _atHeadWake;
                Simulation sim = _driver.Sim;
                float along = 0f;
                if (sim != null && _atRun.Active && _atRun.Serial == _atHeadWakeSerial)
                {
                    float shown = PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha);
                    along = Mathf.Max(0f, _atRun.Snap.Reach0.ToFloat() - PelagAnchorThrowVfxRules.Along(_atRun.Snap, 0, shown));
                }
                if (!AtStill(w.Fx, w.Object)) w.Active = false;
                else AdvanceAtWake(w, dt, Mathf.Max(w.Wake.Length, along));
            }
            if (_atTows == null) return;
            foreach (AnchorThrowTow t in _atTows)
            {
                AtWake d = t.Drag;
                if (!d.Active) continue;
                Vector3 body = EntityPosition(d.Target, d.Start);
                Vector3 moved = body - d.Start;
                moved.y = 0f;
                float shift = Mathf.Max(0f, Vector3.Dot(moved, d.Direction));
                if (!d.Started)
                {
                    // След — только если тело правда поехало (упёрлось в дерево — ждёт, не телепортируется).
                    if (shift >= .06f)
                    {
                        Vector3 root = d.Start - d.Direction * AtDragBack;
                        root.y = d.Ground;
                        PelagForm form = d.Form;
                        if (!BindAtWake(d, PelagVfxId.AnchorThrowDrag, root, d.Direction, form)) { d.Active = false; continue; }
                        d.From = new Vector3(d.Start.x, d.Ground, d.Start.z);
                        d.Started = true;
                        d.Wake.Begin(d.Cap, Random.Range(0f, 8f), GameUserSettings.FlashScale);
                        if (d.TailDrops != null) PelagDashWake.EmitTailDrops(d.TailDrops, root, d.Direction, d.Ground);
                    }
                    else if (Time.time - d.Born > .5f) { d.Active = false; continue; }
                    if (!d.Started) continue;
                }
                if (!AtStill(d.Fx, d.Object)) { d.Active = false; continue; }
                if (!d.Wake.Ended)
                {
                    d.StillFrames = shift - d.LastShift < .003f ? d.StillFrames + 1 : 0;
                    if (d.StillFrames >= 4 || Time.time - d.Born > .6f) d.Wake.End(Mathf.Max(shift, d.Wake.Length));
                }
                d.LastShift = shift;
                AdvanceAtWake(d, dt, shift);
            }
        }

        private void AdvanceAtWake(AtWake w, float dt, float along)
        {
            w.Wake.Advance(dt, along);
            if (w.SkidFoam != null && w.SkidDrops != null && w.Wake.Length > w.SkidAlong)
            {
                PelagDashWake.EmitSkid(w.SkidFoam, w.SkidDrops, w.From, w.Direction, w.Ground,
                    w.SkidAlong, w.Wake.Length, ref w.FoamCarry, ref w.DropCarry);
                w.SkidAlong = w.Wake.Length;
            }
            if (w.Filter != null) w.Wake.Build(PelagDashWake.MeshFor(w.Filter));
            if (w.Wake.Done) ReleaseAtWake(w);
        }

        /// <summary>Ловля или срыв: струйки рвутся на капли, борозды и пена за головой дальше не растут.</summary>
        private void CatchAnchorThrowTows()
        {
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : 0f;
            if (_atTows != null)
                foreach (AnchorThrowTow t in _atTows)
                {
                    if (!t.Active) continue;
                    if (t.BreakFrom < 0f) t.BreakFrom = shown;
                    if (t.Drag.Active && t.Drag.Started && !t.Drag.Wake.Ended) t.Drag.Wake.End(t.Drag.Wake.Length);
                }
            if (_atHeadWake.Active && !_atHeadWake.Wake.Ended) _atHeadWake.Wake.End(_atHeadWake.Wake.Length);
        }

        private void DropAnchorThrowTows() => CatchAnchorThrowTows();

        private void EndAnchorThrowTows() => CatchAnchorThrowTows();

        private void ForgetAnchorThrowTows()
        {
            if (_atTows != null)
                foreach (AnchorThrowTow t in _atTows)
                {
                    if (AtStill(t.Fx, t.Object)) Release(t.Fx);
                    ReleaseAtWake(t.Drag);
                    t.Active = false;
                    t.Fx = -1;
                    t.Object = null;
                }
            ReleaseAtWake(_atHeadWake);
            _atHeadWakeSerial = -1;
        }
    }
}
