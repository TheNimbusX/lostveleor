using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шквал v2 — знаки на телах и у ног:
    ///  • Пенный след: замедленный пеной враг (Simulation.SquallFoamSlowed) — пена
    ///    кипит у щиколоток (кадр trail-zigzag-2: «пена вокруг ног, вязнут»), лужица
    ///    под ним, мелкие брызги; на импульсе урона полосы (DamageOverTime) — укус
    ///    пены у ног. Вход и выход плавные (вес на враге), без вспышек.
    ///  • Неуязвимость в прыжках (Simulation.SquallShielded: Неуловимый и талант
    ///    «Неуязвимость») — пенная вуаль кружит у ног героя. Самого героя не
    ///    высветляем и не красим (владелец 24.09: «ужасно»).
    ///  • Охота: метка добычи — кипящее кольцо пены у ног следующей цели лишнего прыжка.
    ///  • Неуловимый: пенный двойник на точке каста (PelagSquallGhostParts) — капает
    ///    пеной, при посадке возврата впитывается, на каждом отрыве — короткий двойник.
    /// Объект знаков VFX_Pelag_Squall_Cue рождается от событий (полоса, старт серии)
    /// и держится, пока есть кого метить; частицы выбрасываются в него каждый кадр.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private const float SquallAnkleFoamRate = 14f, SquallAnklePoolRate = 3.2f, SquallAnkleDropRate = 5f;
        private const float SquallVeilFoamRate = 30f, SquallVeilDropRate = 8f, SquallVeilRadius = .42f;
        /// <summary>Пена у ног проявляется и сходит, с.</summary>
        private const float SquallSlowIn = .15f, SquallSlowOut = .25f;
        private const float SquallMarkMaxSeconds = 1.6f, SquallMarkLinger = .45f;
        /// <summary>Двойник Неуловимого: проявление, впитывание, страховка; короткий двойник отрыва.</summary>
        private const float SquallGhostIn = .12f, SquallGhostAbsorb = .28f, SquallGhostMaxSeconds = 5.5f, SquallGhostOpacity = .9f;
        private const float SquallTakeoffGhostSeconds = .30f, SquallTakeoffGhostOpacity = .55f;
        private const float SquallGhostDripRate = 7f, SquallGhostPoolRate = 2.5f;

        private int _sqCueFx = -1;
        private GameObject _sqCueObject;
        private ParticleSystem _sqAnkleFoam, _sqAnklePool, _sqAnkleDrops, _sqVeil, _sqVeilDrops;
        private float[] _sqSlowWeight, _sqSlowCarry;
        private float _sqVeilWeight, _sqVeilFoamCarry, _sqVeilDropCarry;
        private bool _sqAnySlow;

        private struct SquallMarkRun
        {
            public bool Active, Ending;
            public int Fx, Target;
            public GameObject Object;
            public float Age;
        }

        private readonly SquallMarkRun[] _sqMarks = new SquallMarkRun[3];
        private int _sqMarkForJump = -1;
        private readonly Dictionary<GameObject, ParticleSystem[]> _sqSystems = new Dictionary<GameObject, ParticleSystem[]>();

        private struct SquallGhostRun
        {
            public bool Active, Origin, Dissolving;
            public int Fx;
            public GameObject Object;
            public PelagSquallGhostParts Parts;
            public float Age, DissolveAge, Ground, DripCarry, PoolCarry;
        }

        private readonly SquallGhostRun[] _sqGhosts = new SquallGhostRun[4];

        // ---- объект знаков

        /// <summary>Объект знаков есть и проживёт ещё 1,5 с (частицы уже выпущенных догорают сами).</summary>
        private bool EnsureSquallCue()
        {
            if (SquallStillActive(_sqCueFx, _sqCueObject))
            {
                ref ActiveFx fx = ref _active[_sqCueFx];
                fx.Duration = fx.Age + 1.5f;
                return true;
            }
            _sqCueFx = SquallSpawn(PelagVfxId.SquallCue, PlayerPosition(), Quaternion.identity, 0f, 1.5f, out GameObject go);
            _sqCueObject = go;
            if (_sqCueFx < 0) return false;
            Transform root = go.transform;
            _sqAnkleFoam = root.Find("AnkleFoam")?.GetComponent<ParticleSystem>();
            _sqAnklePool = root.Find("AnklePool")?.GetComponent<ParticleSystem>();
            _sqAnkleDrops = root.Find("AnkleDrops")?.GetComponent<ParticleSystem>();
            _sqVeil = root.Find("Veil")?.GetComponent<ParticleSystem>();
            _sqVeilDrops = root.Find("VeilDrops")?.GetComponent<ParticleSystem>();
            return true;
        }

        private void UpdateSquallCues(Simulation sim, float dt)
        {
            EntityStore entities = sim.Entities;
            bool shielded = sim.SquallShielded;
            _sqVeilWeight = Mathf.MoveTowards(_sqVeilWeight, shielded ? 1f : 0f, dt / (shielded ? .10f : .20f));
            if (_sqAnySlow || AnySquallTrailLive())
            {
                if (_sqSlowWeight == null || _sqSlowWeight.Length < entities.Capacity)
                {
                    _sqSlowWeight = new float[entities.Capacity];
                    _sqSlowCarry = new float[entities.Capacity * 3];
                }
                _sqAnySlow = false;
                for (int i = 1; i < entities.Count; i++)
                {
                    bool slowed = entities.Alive[i] && sim.SquallFoamSlowed(i);
                    float w = Mathf.MoveTowards(_sqSlowWeight[i], slowed ? 1f : 0f, dt / (slowed ? SquallSlowIn : SquallSlowOut));
                    _sqSlowWeight[i] = w;
                    if (w > 0f) _sqAnySlow = true;
                }
            }
            UpdateSquallMarks(sim, dt);
            if ((!_sqAnySlow && _sqVeilWeight <= 0f) || CaptureRig.NoVfx || dt <= 0f || !EnsureSquallCue()) return;
            if (_sqAnySlow)
                for (int i = 1; i < entities.Count; i++)
                    if (_sqSlowWeight[i] > 0f) EmitSquallAnkle(i, entities, _sqSlowWeight[i], dt);
            if (_sqVeilWeight > 0f) EmitSquallVeil(_sqVeilWeight, dt);
        }

        /// <summary>Пена кипит у щиколоток замедленного: кружит по кругу тела, лужица под ним, брызги.</summary>
        private void EmitSquallAnkle(int id, EntityStore entities, float weight, float dt)
        {
            Vector3 feet = EntityPosition(id, entities.Position[id]);
            float radius = entities.BodyRadius[id].ToFloat();
            int k = id * 3;
            _sqSlowCarry[k] += dt * SquallAnkleFoamRate * weight;
            _sqSlowCarry[k + 1] += dt * SquallAnklePoolRate * weight;
            _sqSlowCarry[k + 2] += dt * SquallAnkleDropRate * weight;
            int foam = (int)_sqSlowCarry[k], pool = (int)_sqSlowCarry[k + 1], drops = (int)_sqSlowCarry[k + 2];
            _sqSlowCarry[k] -= foam;
            _sqSlowCarry[k + 1] -= pool;
            _sqSlowCarry[k + 2] -= drops;
            for (int i = 0; i < foam && _sqAnkleFoam != null; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var tangent = new Vector3(-radial.z, 0f, radial.x);
                Vector3 at = feet + radial * radius * Random.Range(.75f, 1.1f) + Vector3.up * Random.Range(.04f, .14f);
                _sqAnkleFoam.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = tangent * Random.Range(.4f, .9f) + radial * Random.Range(.05f, .25f) + Vector3.up * Random.Range(.05f, .2f),
                    startSize = Random.Range(.10f, .18f),
                    startLifetime = Random.Range(.35f, .50f),
                    applyShapeToPosition = false
                }, 1);
            }
            for (int i = 0; i < pool && _sqAnklePool != null; i++)
                _sqAnklePool.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + Vector3.up * .03f + SquallFlat(Random.insideUnitCircle * .05f),
                    startSize = radius * 2.3f * Random.Range(.9f, 1.1f),
                    startLifetime = Random.Range(.45f, .60f),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false
                }, 1);
            for (int i = 0; i < drops && _sqAnkleDrops != null; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _sqAnkleDrops.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + radial * radius * Random.Range(.6f, 1f) + Vector3.up * .06f,
                    velocity = Vector3.up * Random.Range(1.0f, 1.6f) + radial * Random.Range(.2f, .5f),
                    startSize = Random.Range(.04f, .07f),
                    startLifetime = Random.Range(.25f, .35f),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Неуязвим в прыжках: пенная вуаль кружит у ног героя (на теле ничего).</summary>
        private void EmitSquallVeil(float weight, float dt)
        {
            Vector3 feet = PlayerPosition();
            _sqVeilFoamCarry += dt * SquallVeilFoamRate * weight;
            _sqVeilDropCarry += dt * SquallVeilDropRate * weight;
            int foam = (int)_sqVeilFoamCarry, drops = (int)_sqVeilDropCarry;
            _sqVeilFoamCarry -= foam;
            _sqVeilDropCarry -= drops;
            for (int i = 0; i < foam + drops; i++)
            {
                bool isFoam = i < foam;
                ParticleSystem system = isFoam ? _sqVeil : _sqVeilDrops;
                if (system == null) continue;
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var tangent = new Vector3(-radial.z, 0f, radial.x);
                Vector3 at = feet + radial * SquallVeilRadius * Random.Range(.8f, 1.1f) + Vector3.up * (isFoam ? Random.Range(.05f, .25f) : .08f);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = isFoam
                        ? tangent * Random.Range(1.4f, 2.2f) + Vector3.up * Random.Range(.1f, .4f)
                        : tangent * Random.Range(.8f, 1.4f) + radial * Random.Range(.6f, 1.0f) + Vector3.up * Random.Range(1.2f, 1.8f),
                    startSize = isFoam ? Random.Range(.10f, .18f) : Random.Range(.04f, .07f),
                    startLifetime = isFoam ? Random.Range(.28f, .40f) : Random.Range(.22f, .30f),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Импульс урона полосы по замедленному: укус пены у ног.</summary>
        private void NipSquallFoam(in SimEvent e)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || CaptureRig.NoVfx || e.Target <= Simulation.PlayerId || !sim.SquallFoamSlowed(e.Target)) return;
            if (!EnsureSquallCue()) return;
            Vector3 feet = EntityPosition(e.Target, e.Position);
            float radius = sim.Entities.BodyRadius[e.Target].ToFloat();
            for (int i = 0; i < 9; i++)
            {
                bool isFoam = i < 5;
                ParticleSystem system = isFoam ? _sqAnkleFoam : _sqAnkleDrops;
                if (system == null) continue;
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + radial * radius * Random.Range(.5f, .9f) + Vector3.up * .1f,
                    velocity = radial * Random.Range(.3f, .7f) + Vector3.up * (isFoam ? Random.Range(.6f, 1.2f) : Random.Range(1.6f, 2.4f)),
                    startSize = isFoam ? Random.Range(.14f, .22f) : Random.Range(.05f, .08f),
                    startLifetime = isFoam ? Random.Range(.30f, .42f) : Random.Range(.25f, .35f),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        // ---- Охота: метка добычи

        /// <summary>Кипящее кольцо пены у ног цели лишнего прыжка; идёт за целью, гаснет на ударе по ней.</summary>
        private void MarkSquallPrey(int target)
        {
            Simulation sim = _driver.Sim;
            if (CaptureRig.NoVfx || sim == null || (uint)target >= (uint)sim.Entities.Count || !sim.Entities.Alive[target]) return;
            int slot = -1;
            for (int i = 0; i < _sqMarks.Length; i++)
            {
                ref SquallMarkRun run = ref _sqMarks[i];
                if (run.Active && !SquallStillActive(run.Fx, run.Object)) run = default;
                if (run.Active && run.Target == target && !run.Ending) { run.Age = 0f; return; }
                if (!run.Active && slot < 0) slot = i;
            }
            if (slot < 0) { slot = 0; ReleaseSquallMark(ref _sqMarks[0]); _sqMarks[0] = default; }
            float radius = sim.Entities.BodyRadius[target].ToFloat();
            Vector3 feet = SquallFeet(target, sim);
            float scale = PelagSquallFoamRules.RingScale(radius);
            int fx = SquallSpawn(PelagVfxId.SquallMark, feet, Quaternion.identity, scale, SquallMarkMaxSeconds + SquallMarkLinger, out GameObject go);
            if (fx < 0) return;
            _sqMarks[slot] = new SquallMarkRun { Active = true, Fx = fx, Target = target, Object = go };
        }

        /// <summary>Погасить метку цели (−1 — все): петли перестают рождать, остаток догорает.</summary>
        private void ReleaseSquallPrey(int target)
        {
            for (int i = 0; i < _sqMarks.Length; i++)
            {
                ref SquallMarkRun run = ref _sqMarks[i];
                if (!run.Active || run.Ending || (target >= 0 && run.Target != target)) continue;
                ReleaseSquallMark(ref run);
            }
        }

        private void ReleaseSquallMark(ref SquallMarkRun run)
        {
            if (!run.Active || !SquallStillActive(run.Fx, run.Object)) { run = default; return; }
            run.Ending = true;
            foreach (ParticleSystem system in SquallSystems(run.Object)) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            ref ActiveFx fx = ref _active[run.Fx];
            fx.Duration = fx.Age + SquallMarkLinger;
        }

        private void UpdateSquallMarks(Simulation sim, float dt)
        {
            for (int i = 0; i < _sqMarks.Length; i++)
            {
                ref SquallMarkRun run = ref _sqMarks[i];
                if (!run.Active) continue;
                if (!SquallStillActive(run.Fx, run.Object)) { run = default; continue; }
                run.Age += dt;
                bool alive = (uint)run.Target < (uint)sim.Entities.Count && sim.Entities.Alive[run.Target];
                if (alive) run.Object.transform.position = SquallFeet(run.Target, sim);
                if (!run.Ending && (!alive || run.Age > SquallMarkMaxSeconds)) ReleaseSquallMark(ref run);
            }
        }

        private Vector3 SquallFeet(int target, Simulation sim)
        {
            Vector3 body = EntityPosition(target, sim.Entities.Position[target]);
            _sqGroundBase = body.y;
            return new Vector3(body.x, SquallGroundAt(body.x, body.z) + .02f, body.z);
        }

        private static Vector3 SquallFlat(Vector2 v) => new Vector3(v.x, 0f, v.y);

        private ParticleSystem[] SquallSystems(GameObject go)
        {
            if (!_sqSystems.TryGetValue(go, out ParticleSystem[] systems))
            {
                systems = go.GetComponentsInChildren<ParticleSystem>(true);
                _sqSystems[go] = systems;
            }
            return systems;
        }

        // ---- Неуловимый: пенный двойник

        /// <summary>
        /// Двойник в позе героя: на точке каста (<paramref name="origin"/> — держится до
        /// посадки возврата, капает пеной) или короткий на отрыве. Поза — в миг события.
        /// </summary>
        private void SpawnSquallGhost(Vector3 at, bool origin)
        {
            if (!_arena.TryGetEntityView(Simulation.PlayerId, out Transform body)) return;
            if (origin) DissolveSquallGhosts(false);
            int slot = -1;
            float oldest = -1f;
            int oldestSlot = 0;
            for (int i = 0; i < _sqGhosts.Length; i++)
            {
                ref SquallGhostRun run = ref _sqGhosts[i];
                if (run.Active && !SquallStillActive(run.Fx, run.Object)) run = default;
                if (!run.Active) { if (slot < 0) slot = i; continue; }
                if (!run.Origin && run.Age > oldest) { oldest = run.Age; oldestSlot = i; }
            }
            if (slot < 0)
            {
                slot = oldestSlot;
                if (SquallStillActive(_sqGhosts[slot].Fx, _sqGhosts[slot].Object)) Release(_sqGhosts[slot].Fx);
                _sqGhosts[slot] = default;
            }
            _sqGroundBase = at.y;
            float ground = SquallGroundAt(at.x, at.z);
            int fx = SquallSpawn(PelagVfxId.SquallGhost, body.position, Quaternion.identity, 0f,
                origin ? SquallGhostMaxSeconds + 1f : SquallTakeoffGhostSeconds + .4f, out GameObject go);
            if (fx < 0) return;
            var parts = go.GetComponent<PelagSquallGhostParts>();
            if (parts == null || parts.Capture(body) == 0) { Release(fx); return; }
            parts.Look(0f, 0f, 0f, ground, PelagSquallFormLook.For(_sqForm));
            _sqGhosts[slot] = new SquallGhostRun
            {
                Active = true, Origin = origin, Fx = fx, Object = go, Parts = parts, Ground = ground, DripCarry = .5f
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] ghost origin={origin} parts={parts.Used} at={body.position.ToString("F2")}");
        }

        /// <summary>Двойник точки каста растворяется: <paramref name="absorbed"/> — герой вернулся (всплеск пены).</summary>
        private void DissolveSquallGhosts(bool absorbed)
        {
            for (int i = 0; i < _sqGhosts.Length; i++)
            {
                ref SquallGhostRun run = ref _sqGhosts[i];
                if (!run.Active || !run.Origin || run.Dissolving) continue;
                if (!SquallStillActive(run.Fx, run.Object)) { run = default; continue; }
                run.Dissolving = true;
                run.DissolveAge = run.Age;
                ParticleSystem burst = run.Parts.Burst;
                if (burst == null || CaptureRig.NoVfx) continue;
                Vector3 centre = run.Parts.Area.center;
                for (int k = 0; k < (absorbed ? 16 : 8); k++)
                {
                    Vector3 from = run.Parts.DripPoint();
                    Vector3 out_ = from - centre;
                    out_.y = 0f;
                    burst.Emit(new ParticleSystem.EmitParams
                    {
                        position = from,
                        velocity = out_.normalized * Random.Range(.6f, 1.6f) + Vector3.up * Random.Range(.4f, 1.4f),
                        startSize = Random.Range(.12f, .22f),
                        startLifetime = Random.Range(.30f, .45f),
                        applyShapeToPosition = false
                    }, 1);
                }
            }
        }

        private void UpdateSquallGhosts(float now, float dt)
        {
            PelagSquallFormLook.Palette palette = PelagSquallFormLook.For(_sqForm);
            for (int i = 0; i < _sqGhosts.Length; i++)
            {
                ref SquallGhostRun run = ref _sqGhosts[i];
                if (!run.Active) continue;
                if (!SquallStillActive(run.Fx, run.Object)) { run = default; continue; }
                run.Age += dt;
                float opacity, dissolve = 0f;
                if (run.Origin)
                {
                    if (!run.Dissolving && run.Age > SquallGhostMaxSeconds) DissolveSquallGhosts(false);
                    opacity = SquallGhostOpacity * PelagSquallWater.Smooth01(run.Age / SquallGhostIn);
                    if (run.Dissolving)
                    {
                        float d = Mathf.Clamp01((run.Age - run.DissolveAge) / SquallGhostAbsorb);
                        dissolve = d * d;
                        if (d >= 1f) { FinishSquallGhost(ref run); continue; }
                    }
                    else EmitSquallGhostDrips(ref run, dt);
                }
                else
                {
                    float k = PelagSquallWater.Smooth01(run.Age / SquallTakeoffGhostSeconds);
                    opacity = SquallTakeoffGhostOpacity * (1f - k);
                    dissolve = .6f * k;
                    if (run.Age >= SquallTakeoffGhostSeconds) { FinishSquallGhost(ref run); continue; }
                }
                run.Parts.Look(opacity, dissolve, run.Age, run.Ground, palette);
            }
        }

        /// <summary>Двойник растаял: части спрятаны, капли и всплеск догорают, объект уйдёт в пул сам.</summary>
        private void FinishSquallGhost(ref SquallGhostRun run)
        {
            run.Parts.Look(0f, 1f, run.Age, run.Ground, PelagSquallFormLook.For(_sqForm));
            ref ActiveFx fx = ref _active[run.Fx];
            fx.Duration = fx.Age + .5f;
            run = default;
        }

        /// <summary>С двойника каплет пена, под ним лужица.</summary>
        private void EmitSquallGhostDrips(ref SquallGhostRun run, float dt)
        {
            if (CaptureRig.NoVfx || dt <= 0f) return;
            run.DripCarry += dt * SquallGhostDripRate;
            run.PoolCarry += dt * SquallGhostPoolRate;
            int drips = (int)run.DripCarry, pools = (int)run.PoolCarry;
            run.DripCarry -= drips;
            run.PoolCarry -= pools;
            ParticleSystem dripSystem = run.Parts.Drips, poolSystem = run.Parts.Pool;
            for (int i = 0; i < drips && dripSystem != null; i++)
                dripSystem.Emit(new ParticleSystem.EmitParams
                {
                    position = run.Parts.DripPoint(),
                    velocity = Vector3.down * Random.Range(0f, .3f) + SquallFlat(Random.insideUnitCircle * .1f),
                    startSize = Random.Range(.05f, .08f),
                    startLifetime = Random.Range(.40f, .60f),
                    applyShapeToPosition = false
                }, 1);
            for (int i = 0; i < pools && poolSystem != null; i++)
            {
                Vector3 centre = run.Parts.Area.center;
                Vector2 jitter = Random.insideUnitCircle * .12f;
                poolSystem.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(centre.x + jitter.x, run.Ground + .03f, centre.z + jitter.y),
                    startSize = Random.Range(.55f, .75f),
                    startLifetime = Random.Range(.50f, .70f),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false
                }, 1);
            }
        }
    }
}
