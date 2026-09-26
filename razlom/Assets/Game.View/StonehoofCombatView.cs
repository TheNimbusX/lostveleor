using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(650)]
    public sealed class StonehoofCombatView : MonoBehaviour
    {
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
        }
        private TickDriver _driver;
        private LayoutView _layout;
        private ArenaView _arena;
        private Simulation _shown;
        private readonly Lane[] _lanes = new Lane[4];
        private Burst[] _hooves, _impacts;
        private int _hoofCursor, _impactCursor;
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
        {
            var burst = pool[cursor++ % pool.Length]; if (burst == null) return;
            burst.Tick = tick; burst.Root.transform.position = Ground(point) + Vector3.up * .025f;
            burst.Root.transform.localScale = Vector3.one * scale; burst.Root.SetActive(true);
        }
        private void Hoof(Lane lane, int hoof, float tick, float scale)
        {
            if (lane.Body == null || lane.Body.Hooves[hoof] == null) return;
            Emit(_hooves, ref _hoofCursor, lane.Body.Hooves[hoof].position, tick, scale);
        }
        private void LateUpdate()
        {
            var sim = _driver.Sim; if (sim == null) return;
            if (_shown != sim)
            {
                _shown = sim;
                foreach (var lane in _lanes) { lane.Entity = -1; lane.Root.SetActive(false); }
                ResetPool(_hooves); ResetPool(_impacts);
            }
            float tick = sim.Tick - 1 + _driver.Alpha;
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
            Advance(_hooves, tick); Advance(_impacts, tick);
        }
        private static void ResetPool(Burst[] pool)
        { foreach (var b in pool) if (b != null) { b.Tick = -1000; b.Root.SetActive(false); } }
        private static void Advance(Burst[] pool, float tick)
        {
            foreach (var b in pool)
            {
                if (b == null || !b.Root.activeSelf) continue;
                float age = Mathf.Max(0, tick - b.Tick) / Simulation.TicksPerSecond;
                if (age > 1.4f) { b.Root.SetActive(false); continue; }
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
