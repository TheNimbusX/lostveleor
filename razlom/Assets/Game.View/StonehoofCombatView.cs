using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(650)]
    public sealed class StonehoofCombatView : MonoBehaviour
    {
        /// <summary>
        /// Сколько живёт залп клыков VFX_Tusk, с. Самая долгая частица префаба —
        /// 1,8 с (травинки); StonehoofBuilder не соберёт префаб с частицей дольше.
        /// </summary>
        public const float TuskBurstSeconds = 1.9f;

        private sealed class Lane
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public readonly Vector3[] Vertices = new Vector3[129 * 5];
            public readonly Vector2[] Uvs = new Vector2[129 * 5];
            public int Entity = -1, Serial;
            public float LastTick;
            public StonehoofAnimatorView Body;
        }
        private sealed class Burst
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
            public float Tick = -1000;
            /// <summary>До какого возраста системы уже досчитаны (догоняющий залп клыков); −1 — с нуля.</summary>
            public float Simulated = -1f;
        }

        /// <summary>
        /// Шаг догоняющей симуляции залпа клыков: комья летят по баллистике и
        /// отскакивают от плоскости земли. Один Simulate на весь возраст — это
        /// один шаг Эйлера (двойная гравитация) и один отрезок до земли: к
        /// середине жизни комья съезжали бы по отрезку обратно к морде.
        /// </summary>
        private const float SimulateStep = 1f / 30f;
        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private readonly Lane[] _lanes = new Lane[4];
        private Burst[] _hooves, _impacts, _tusks;
        private int _hoofCursor, _impactCursor, _tuskCursor;
        private Material _laneMaterial;
        private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
            Length = Shader.PropertyToID("_Length"), Width = Shader.PropertyToID("_Width"), Consumed = Shader.PropertyToID("_Consumed");
        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _layout = GetComponent<LayoutView>(); _arena = GetComponent<ArenaView>();
            _laneMaterial = Resources.Load<Material>("VFX/Telegraphs/EnemyLane");
            var indices = new int[128 * 4 * 6]; int at = 0;
            for (int y = 0; y < 128; y++) for (int x = 0; x < 4; x++)
            {
                int a = y * 5 + x, b = a + 1, c = a + 5, d = c + 1;
                indices[at++] = a; indices[at++] = c; indices[at++] = b;
                indices[at++] = b; indices[at++] = c; indices[at++] = d;
            }
            for (int i = 0; i < _lanes.Length; i++)
            {
                var lane = new Lane { Root = new GameObject("Камнекопыт: полоса " + i), Mesh = new Mesh { name = "Stonehoof grounded lane" } };
                lane.Root.transform.SetParent(transform, false); lane.Mesh.MarkDynamic(); lane.Mesh.vertices = lane.Vertices; lane.Mesh.triangles = indices;
                lane.Root.AddComponent<MeshFilter>().sharedMesh = lane.Mesh; lane.Renderer = lane.Root.AddComponent<MeshRenderer>();
                lane.Renderer.sharedMaterial = _laneMaterial; lane.Renderer.shadowCastingMode = ShadowCastingMode.Off; lane.Renderer.receiveShadows = false;
                lane.Root.SetActive(false); _lanes[i] = lane;
            }
            _hooves = Pool("Characters/Forest_Stonehoof/VFX_Hoof", 20);
            _impacts = Pool("Characters/Forest_Stonehoof/VFX_Wall", 4);
            // Взмах клыками: комья земли, камешки, травинки и пыль (StonehoofBuilder.BuildTuskBurst).
            _tusks = Pool("Characters/Forest_Stonehoof/VFX_Tusk", 4);
        }
        private Burst[] Pool(string resource, int count)
        {
            var prefab = Resources.Load<GameObject>(resource); var pool = new Burst[count];
            if (prefab == null) return pool;
            for (int i = 0; i < count; i++)
            {
                var root = Instantiate(prefab, transform); root.SetActive(false);
                pool[i] = new Burst { Root = root, Particles = root.GetComponentsInChildren<ParticleSystem>(true) };
            }
            return pool;
        }
        private Vector3 Ground(Vector3 p) { p.y = _layout != null ? _layout.WeaponGroundHeight(p.x, p.z) : 0; return p; }
        private void Emit(Burst[] pool, ref int cursor, Vector3 point, float tick, float scale)
            => Emit(pool, ref cursor, point, Quaternion.identity, tick, scale);
        private void Emit(Burst[] pool, ref int cursor, Vector3 point, Quaternion rotation, float tick, float scale)
        {
            var burst = pool[cursor++ % pool.Length]; if (burst == null) return;
            burst.Tick = tick; burst.Simulated = -1f;
            burst.Root.transform.SetPositionAndRotation(Ground(point) + Vector3.up * .025f, rotation);
            burst.Root.transform.localScale = Vector3.one * scale; burst.Root.SetActive(true);
        }
        private void Hoof(Lane lane, int hoof, float tick, float scale) => Hoof(lane.Body, hoof, tick, scale);
        private void Hoof(StonehoofAnimatorView body, int hoof, float tick, float scale)
        {
            if (body == null || body.Hooves[hoof] == null) return;
            Emit(_hooves, ref _hoofCursor, body.Hooves[hoof].position, tick, scale);
        }

        /// <summary>
        /// Взмах клыками: залп земли и пыли VFX_Tusk в точке удара. Спавн — из
        /// события EnemyActionImpact (StonehoofTusk), не опросом состояния;
        /// возраст — от тика удара (SimulationTick − 1), поэтому пауза, стоп-кадр
        /// и съёмка переигрывают залп кадр в кадр. Точка — из события (перед
        /// мордой на половине радиуса удара), направление — зафиксированное в
        /// начале замаха; клип Stonehoof_Tusk в этот тик как раз проносит клыки
        /// (контакт на 14-м кадре). Задел героя — залп полный, мимо — поменьше.
        /// Выпад корпуса на контакте упирает передние копыта — у них своя пыль.
        /// </summary>
        private void TuskImpacts(Simulation sim)
        {
            var contexts = _driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.Type != SimEventType.EnemyActionImpact || e.ActionVariant != (int)EnemyActionKind.StonehoofTusk) continue;
                int id = e.Source;
                if ((uint)id >= (uint)sim.Entities.Count) continue;
                float at = contexts[i].SimulationTick - 1;
                Vector3 forward = sim.TryGetStonehoofTusk(id, out var tusk)
                    ? new Vector3(tusk.Direction.X.ToFloat(), 0, tusk.Direction.Y.ToFloat())
                    : _driver.GetRenderFacing(id);
                forward.y = 0;
                if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
                var point = new Vector3(e.Position.X.ToFloat(), 0, e.Position.Y.ToFloat());
                Emit(_tusks, ref _tuskCursor, point, Quaternion.LookRotation(forward.normalized, Vector3.up), at, e.Flag ? 1f : .8f);
                var body = _arena != null && _arena.TryGetEntityView(id, out var view) ? view.GetComponent<StonehoofAnimatorView>() : null;
                Hoof(body, 0, at, .7f); Hoof(body, 1, at, .7f);
            }
        }
        private void LateUpdate()
        {
            var sim = _driver.Sim; if (sim == null) return;
            if (_shown != sim)
            {
                _shown = sim;
                foreach (var lane in _lanes) { lane.Entity = -1; lane.Root.SetActive(false); }
                ResetPool(_hooves); ResetPool(_impacts); ResetPool(_tusks);
            }
            float tick = sim.Tick - 1 + _driver.Alpha;
            TuskImpacts(sim);
            foreach (var lane in _lanes)
            {
                if (lane.Entity < 0) continue;
                if (!sim.Entities.Alive[lane.Entity] || !sim.TryGetStonehoofAction(lane.Entity, out var a) || a.Serial != lane.Serial)
                { lane.Root.SetActive(false); lane.Entity = -1; continue; }
                float distance = a.Distance.ToFloat(), radius = Simulation.StonehoofRadius.ToFloat();
                float traveled = Vector3.Dot(_driver.GetRenderPosition(lane.Entity) - new Vector3(a.Origin.X.ToFloat(), 0, a.Origin.Y.ToFloat()),
                    new Vector3(a.Direction.X.ToFloat(), 0, a.Direction.Y.ToFloat()));
                lane.Block.SetFloat(Progress, Mathf.Clamp01((tick - a.StartTick) / 30));
                lane.Block.SetFloat(Consumed, tick >= a.LaunchTick ? Mathf.Clamp01(traveled / (distance + radius * 2)) : 0);
                lane.Block.SetFloat(Opacity, tick >= a.StopTick ? 0 : 1);
                lane.Renderer.SetPropertyBlock(lane.Block);
                // Hoof contacts from the accepted clips, sampled on the simulation clock.
                for (int n = 0; n < 2; n++)
                {
                    float scrape = a.StartTick + (n == 0 ? 9 : 18);
                    if (lane.LastTick < scrape && tick >= scrape) Hoof(lane, 0, scrape, .6f);
                }
                if (lane.LastTick < a.LaunchTick && tick >= a.LaunchTick)
                { Hoof(lane, 2, a.LaunchTick, 1); Hoof(lane, 3, a.LaunchTick, 1); }
                int first = Mathf.Max(0, Mathf.FloorToInt((lane.LastTick - a.LaunchTick - 6) / 12));
                int last = Mathf.Max(0, Mathf.FloorToInt((tick - a.LaunchTick - 6) / 12));
                for (int cycle = first; cycle <= last; cycle++) for (int foot = 0; foot < 4; foot++)
                {
                    float contact = a.LaunchTick + 6 + cycle * 12 + (foot == 0 ? 3 : foot == 1 ? 3.7f : foot == 2 ? 6.1f : 6.8f);
                    if (contact < a.BrakeTick && lane.LastTick < contact && tick >= contact) Hoof(lane, foot, contact, .65f);
                }
                if (a.StopReason == StonehoofStop.ArenaEdge)
                    for (int n = 0; n < 3; n++)
                    {
                        float contact = a.BrakeTick + n * 5;
                        if (lane.LastTick < contact && tick >= contact) { Hoof(lane, 0, contact, .9f); Hoof(lane, 1, contact, .9f); }
                    }
                if (a.StopReason == StonehoofStop.Obstacle && lane.LastTick < a.StopTick && tick >= a.StopTick)
                    Emit(_impacts, ref _impactCursor, lane.Body != null && lane.Body.Head != null ? lane.Body.Head.position : _driver.GetRenderPosition(lane.Entity), a.StopTick, 1);
                lane.LastTick = tick;
            }
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.TryGetStonehoofAction(id, out var action)) continue;
                Lane free = null; bool found = false;
                foreach (var lane in _lanes) { if (lane.Entity == id && lane.Serial == action.Serial) found = true; if (lane.Entity < 0) free = lane; }
                if (!found && free != null) Build(free, id, action, tick);
            }
            Advance(_hooves, tick, 1.4f); Advance(_impacts, tick, 1.4f); AdvanceStepped(_tusks, tick, TuskBurstSeconds);
        }
        private static void ResetPool(Burst[] pool)
        { foreach (var b in pool) if (b != null) { b.Tick = -1000; b.Simulated = -1f; b.Root.SetActive(false); } }
        /// <summary>
        /// Залп с баллистикой и отскоком (клыки): возраст — от тика Sim, вперёд
        /// системы догоняются шагами по SimulateStep, назад (перемотка, съёмка) —
        /// перепрогоном с нуля; на паузе возраст стоит, и частицы стоят.
        /// </summary>
        private static void AdvanceStepped(Burst[] pool, float tick, float life)
        {
            foreach (var b in pool)
            {
                if (b == null || !b.Root.activeSelf) continue;
                float age = Mathf.Max(0, tick - b.Tick) / Simulation.TicksPerSecond;
                if (age > life) { b.Simulated = -1f; b.Root.SetActive(false); continue; }
                if (b.Simulated >= 0f && Mathf.Abs(age - b.Simulated) < 1e-5f) continue;
                bool restart = b.Simulated < 0f || age < b.Simulated;
                float from = restart ? 0f : b.Simulated;
                foreach (var ps in b.Particles)
                {
                    float done = from;
                    bool first = restart;
                    do
                    {
                        float step = Mathf.Min(SimulateStep, age - done);
                        ps.Simulate(step, false, first, false);
                        first = false;
                        done += step;
                    } while (done < age - 1e-5f);
                    ps.Pause(false);
                }
                b.Simulated = age;
            }
        }
        private static void Advance(Burst[] pool, float tick, float life)
        {
            foreach (var b in pool)
            {
                if (b == null || !b.Root.activeSelf) continue;
                float age = Mathf.Max(0, tick - b.Tick) / Simulation.TicksPerSecond;
                if (age > life) { b.Root.SetActive(false); continue; }
                foreach (var ps in b.Particles) { ps.Simulate(age, false, true, false); ps.Pause(false); }
            }
        }
        private void Build(Lane lane, int entity, StonehoofActionState a, float tick)
        {
            lane.Entity = entity; lane.Serial = a.Serial; lane.LastTick = tick - .01f;
            lane.Body = _arena.TryGetEntityView(entity, out var body) ? body.GetComponent<StonehoofAnimatorView>() : null;
            var origin = new Vector3(a.Origin.X.ToFloat(), 0, a.Origin.Y.ToFloat());
            var forward = new Vector3(a.Direction.X.ToFloat(), 0, a.Direction.Y.ToFloat());
            var right = Vector3.Cross(Vector3.up, forward); float radius = Simulation.StonehoofRadius.ToFloat();
            float length = a.Distance.ToFloat() + radius * 2;
            for (int y = 0; y <= 128; y++) for (int x = 0; x <= 4; x++)
            {
                float u = x / 4f, v = y / 128f;
                lane.Vertices[y * 5 + x] = Ground(origin + forward * (v * length - radius) + right * ((u - .5f) * radius * 2)) + Vector3.up * .065f;
                lane.Uvs[y * 5 + x] = new Vector2(u, v);
            }
            lane.Mesh.vertices = lane.Vertices; lane.Mesh.uv = lane.Uvs; lane.Mesh.RecalculateBounds();
            lane.Block.SetFloat(Length, length); lane.Block.SetFloat(Width, radius * 2);
            lane.Block.SetFloat(Progress, 0); lane.Block.SetFloat(Consumed, 0); lane.Block.SetFloat(Opacity, 1);
            lane.Renderer.SetPropertyBlock(lane.Block); lane.Root.SetActive(true);
        }
        private void OnDestroy()
        { foreach (var lane in _lanes) if (lane != null) { Destroy(lane.Mesh); Destroy(lane.Root); } }
    }
}
