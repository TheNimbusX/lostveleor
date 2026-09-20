using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(660)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ForestBudImpactView : MonoBehaviour
    {
        private const int PoolSize = 192;
        private const float Lifetime = .72f;
        private const int Rinds = 6, Drops = 12, Seeds = 5, Jets = 7;
        private const int PieceCount = Rinds + Drops + Seeds + Jets;
        private sealed class Shape
        {
            public Vector3[] Vertices, Normals;
            public Color[] Colors;
            public int[] Indices;
        }
        private struct Piece
        {
            public Vector3 Velocity, Axis;
            public Quaternion Rotation;
            public float Spin, Size, Ground;
        }
        private sealed class Impact
        {
            public Transform Root;
            public Mesh Mesh;
            public MeshRenderer Ground;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public readonly Piece[] Pieces = new Piece[PieceCount];
            public Vector3[] Vertices, Normals;
            public Color[] Colors;
            public int Tick;
            public bool Active;
        }

        private TickDriver _driver;
        private LayoutView _layout;
        private Simulation _shownSim;
        private int _generation = -1, _depth = -1;
        private Impact[] _pool;
        private Shape[] _shapes;
        private Material _debrisMaterial, _groundMaterial;
        private Mesh _groundMesh;
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        public int ActiveCount { get; private set; }
        public int EmittedCount { get; private set; }
        public float OldestAge { get; private set; }

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _layout = GetComponent<LayoutView>();
            _debrisMaterial = new Material(Shader.Find("Razlom/Forest Fruit Debris"));
            _groundMaterial = new Material(Shader.Find("Razlom/Forest Fruit Impact"));
            _groundMesh = new Mesh { name = "Forest fruit sap splash" };
            _groundMesh.vertices = new[] { new Vector3(-1,0,-1), new Vector3(-1,0,1), new Vector3(1,0,1), new Vector3(1,0,-1) };
            _groundMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _groundMesh.triangles = new[] { 0,1,2,0,2,3 };
            var rind = BuildRind();
            var drop = BuildDrop(new Color(.94f,.13f,.008f), new Color(1f,.54f,.055f));
            var seed = BuildDrop(new Color(.18f,.045f,.008f), new Color(.55f,.21f,.025f));
            var jet = BuildDrop(new Color(1f,.20f,.008f), new Color(1.2f,.69f,.13f));
            _shapes = new Shape[PieceCount];
            var indices = new List<int>();
            int vertices = 0;
            for (int i = 0; i < PieceCount; i++)
            {
                var shape = i < Rinds ? rind : i < Rinds + Drops ? drop : i < Rinds + Drops + Seeds ? seed : jet;
                _shapes[i] = shape;
                foreach (int index in shape.Indices) indices.Add(vertices + index);
                vertices += shape.Vertices.Length;
            }
            int[] triangles = indices.ToArray();
            _pool = new Impact[PoolSize];
            for (int i = 0; i < _pool.Length; i++)
            {
                var effect = new Impact();
                effect.Root = new GameObject("Разрыв лесного плода / " + i).transform;
                effect.Root.SetParent(transform, false);
                effect.Vertices = new Vector3[vertices]; effect.Normals = new Vector3[vertices]; effect.Colors = new Color[vertices];
                effect.Mesh = new Mesh { name = "Fruit rind, pulp and seeds" };
                effect.Mesh.MarkDynamic(); effect.Mesh.vertices = effect.Vertices; effect.Mesh.triangles = triangles;
                effect.Mesh.bounds = new Bounds(Vector3.up * .5f, new Vector3(4,3,4));
                effect.Root.gameObject.AddComponent<MeshFilter>().sharedMesh = effect.Mesh;
                var renderer = effect.Root.gameObject.AddComponent<MeshRenderer>();
                Configure(renderer, _debrisMaterial);
                var ground = new GameObject("Всплеск сока на земле");
                ground.transform.SetParent(effect.Root, false);
                ground.transform.localPosition = Vector3.up * .045f;
                ground.gameObject.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
                effect.Ground = ground.AddComponent<MeshRenderer>(); Configure(effect.Ground, _groundMaterial);
                effect.Root.gameObject.SetActive(false);
                _pool[i] = effect;
            }
        }

        private static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            if (!ReferenceEquals(sim, _shownSim) || _generation != _driver.Generation || depth != _depth)
            { Clear(); _shownSim = sim; _generation = _driver.Generation; _depth = depth; }
            if (sim == null || _driver.GameplayPaused) return;
            // Событие содержит тик после Step. Интерполяция плода заканчивается на тик раньше.
            // Возраст от времени симуляции не уплывает при паузе и нескольких шагах за кадр.
            var events = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Event.Type == SimEventType.ForestFruitImpact)
                    Emit(events[i].Event, events[i].SimulationTick - 1);
            float tick = sim.Tick - 1 + _driver.Alpha;
            ActiveCount = 0; OldestAge = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                var effect = _pool[i];
                if (!effect.Active) continue;
                float age = Mathf.Max(0, (tick - effect.Tick) / Simulation.TicksPerSecond);
                if (age >= Lifetime) { effect.Active = false; effect.Root.gameObject.SetActive(false); continue; }
                ActiveCount++; OldestAge = Mathf.Max(OldestAge, age);
                Draw(effect, age);
            }
        }

        private void Emit(SimEvent e, int tick)
        {
            int free = -1, oldest = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Active) { free = i; break; }
                if (_pool[i].Tick < _pool[oldest].Tick) oldest = i;
            }
            // На перегрузе заменяется самый старый декоративный хвост, без выделения памяти.
            var effect = _pool[free >= 0 ? free : oldest];
            float x = e.Position.X.ToFloat(), z = e.Position.Y.ToFloat();
            float ground = _layout != null ? _layout.WeaponGroundHeight(x,z) : 0;
            effect.Root.position = new Vector3(x,ground,z);
            effect.Tick = tick; effect.Active = true;
            effect.Root.gameObject.SetActive(true);
            float seed = e.Source * 2.39996f + e.ActionVariant * 1.73f + tick * .013f;
            effect.Properties.SetFloat(SeedId, seed);
            for (int i = 0; i < PieceCount; i++)
            {
                bool rind = i < Rinds, jet = i >= Rinds + Drops + Seeds;
                float angle = seed + i * (rind ? Mathf.PI * 2 / Rinds : 2.39996f);
                float variation = .5f + .5f * Mathf.Sin(i * 7.13f + seed);
                var radial = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                float speed = rind ? 3.5f + variation * 1.2f : 3f + variation * 2f;
                var piece = new Piece
                {
                    Velocity = radial * speed + Vector3.up * (rind ? 2.5f + variation * 1.1f : 1.8f + variation * 2f),
                    Axis = new Vector3(Mathf.Sin(angle), .4f, Mathf.Cos(angle)).normalized,
                    Rotation = Quaternion.Euler(25 + variation * 80, angle * Mathf.Rad2Deg, variation * 160),
                    Spin = 300 + variation * 480,
                    Size = rind ? .8f + variation * .4f : i < Rinds + Drops ? .055f + variation * .055f : .038f + variation * .018f
                };
                if (rind) piece.Rotation = Quaternion.LookRotation(radial) * Quaternion.Euler(-12 + variation * 24,0,0);
                if (jet) { piece.Velocity = radial; piece.Size = .8f + variation * .4f; }
                Vector3 end = effect.Root.position + radial * speed * .22f;
                piece.Ground = _layout != null ? _layout.WeaponGroundHeight(end.x,end.z) - ground : 0;
                effect.Pieces[i] = piece;
            }
            EmittedCount++;
            if (CaptureRig.ForestBudShowcase)
                Debug.Log($"[forest-impact-vfx] tick={tick} shot={e.ActionVariant} emitted={EmittedCount} position=({x:F2},{z:F2})");
        }

        private void Draw(Impact effect, float age)
        {
            int offset = 0;
            for (int i = 0; i < PieceCount; i++)
            {
                var shape = _shapes[i]; var piece = effect.Pieces[i];
                bool rind = i < Rinds, jet = i >= Rinds + Drops + Seeds;
                Vector3 position, scale;
                Quaternion rotation;
                float alpha;
                if (jet)
                {
                    float t = Mathf.Clamp01(age / .22f);
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    var direction = (piece.Velocity + Vector3.up * .85f).normalized;
                    position = piece.Velocity * (.08f + t * .36f) + Vector3.up * (.11f + t * .11f);
                    scale = new Vector3(.10f, .43f, .10f) * (envelope * piece.Size);
                    rotation = Quaternion.FromToRotation(Vector3.up,direction);
                    alpha = 1 - t * t;
                }
                else
                {
                    const float gravity = 12f;
                    float floor = piece.Ground + (rind ? .055f : .025f);
                    float start = .16f;
                    float hit = (piece.Velocity.y + Mathf.Sqrt(piece.Velocity.y * piece.Velocity.y + 2 * gravity * Mathf.Max(0,start-floor))) / gravity;
                    float after = Mathf.Max(0,age-hit);
                    float travel = .20f * (1 - Mathf.Exp(-Mathf.Min(age,hit)/.20f)) + after * .035f;
                    position = new Vector3(piece.Velocity.x * travel, start + piece.Velocity.y * age - .5f * gravity * age * age, piece.Velocity.z * travel);
                    if (rind) position += new Vector3(piece.Velocity.x,0,piece.Velocity.z).normalized * .13f;
                    if (after > 0) position.y = floor + Mathf.Max(0, piece.Velocity.y * .2f * after - .5f * gravity * after * after);
                    rotation = piece.Rotation * Quaternion.AngleAxis(piece.Spin * Mathf.Min(age,hit+.1f),piece.Axis);
                    alpha = 1 - Mathf.SmoothStep(0,1,Mathf.InverseLerp(.36f,Lifetime,age));
                    float shrink = Mathf.Lerp(.3f,1,alpha);
                    scale = rind ? Vector3.one * (piece.Size * shrink) : new Vector3(1,1.45f,1) * (piece.Size * shrink);
                }
                var matrix = Matrix4x4.TRS(position,rotation,scale);
                for (int v = 0; v < shape.Vertices.Length; v++)
                {
                    effect.Vertices[offset+v] = matrix.MultiplyPoint3x4(shape.Vertices[v]);
                    effect.Normals[offset+v] = rotation * shape.Normals[v];
                    var color = shape.Colors[v]; color.a *= alpha;
                    effect.Colors[offset+v] = color;
                }
                offset += shape.Vertices.Length;
            }
            effect.Mesh.vertices = effect.Vertices; effect.Mesh.normals = effect.Normals; effect.Mesh.colors = effect.Colors;
            effect.Properties.SetFloat(AgeId,age);
            effect.Ground.SetPropertyBlock(effect.Properties);
        }

        private static Shape BuildDrop(Color bottom, Color top)
        {
            const int rings = 6, sides = 7;
            var shape = new Shape { Vertices = new Vector3[(rings+1)*sides], Normals = new Vector3[(rings+1)*sides], Colors = new Color[(rings+1)*sides], Indices = new int[rings*sides*6] };
            int index = 0;
            for (int y = 0; y <= rings; y++)
            {
                float t = (float)y/rings, theta = t*Mathf.PI;
                for (int x = 0; x < sides; x++)
                {
                    float phi = x*Mathf.PI*2/sides;
                    var n = new Vector3(Mathf.Sin(theta)*Mathf.Cos(phi), -Mathf.Cos(theta), Mathf.Sin(theta)*Mathf.Sin(phi));
                    int v = y*sides+x; shape.Vertices[v] = new Vector3(n.x*(1-.38f*t),n.y,n.z*(1-.38f*t));
                    shape.Normals[v] = n; shape.Colors[v] = Color.Lerp(bottom,top,t);
                    if (y == rings) continue;
                    int b = y*sides+(x+1)%sides;
                    shape.Indices[index++]=v; shape.Indices[index++]=v+sides; shape.Indices[index++]=b+sides;
                    shape.Indices[index++]=v; shape.Indices[index++]=b+sides; shape.Indices[index++]=b;
                }
            }
            return shape;
        }

        private static Shape BuildRind()
        {
            const int rows = 4, columns = 4, sideVertices = (rows+1)*(columns+1);
            var vertices = new Vector3[sideVertices*2]; var colors = new Color[vertices.Length];
            var indices = new List<int>();
            for (int side = 0; side < 2; side++)
                for (int y = 0; y <= rows; y++)
                    for (int x = 0; x <= columns; x++)
                    {
                        float v = (float)y/rows, u = (float)x/columns;
                        float theta = Mathf.Lerp(.35f,2.65f,v), phi = Mathf.Lerp(-.46f,.46f,u);
                        float radius = side == 0 ? .235f : .192f;
                        int k = side*sideVertices+y*(columns+1)+x;
                        vertices[k] = new Vector3(Mathf.Sin(phi)*Mathf.Sin(theta),Mathf.Cos(theta),Mathf.Cos(phi)*Mathf.Sin(theta)-.55f)*radius;
                        colors[k] = side == 0 ? Color.Lerp(new Color(.42f,.10f,.008f),new Color(1,.43f,.025f),.3f+.7f*v) : Color.Lerp(new Color(1,.32f,.025f),new Color(1,.65f,.20f),u);
                        if (y == rows || x == columns) continue;
                        int b=k+1,c=k+columns+2,d=k+columns+1;
                        if (side == 0) { indices.AddRange(new[] { k,d,c,k,c,b }); }
                        else { indices.AddRange(new[] { k,b,c,k,c,d }); }
                    }
            // Замкнутая толщина показывает светлую мякоть на изломе с любой стороны.
            for (int y=0;y<rows;y++)
                for (int edge=0;edge<2;edge++)
                { int a=y*(columns+1)+edge*columns,b=a+columns+1; AddEdge(indices,a,b,sideVertices,edge==0); }
            for (int x=0;x<columns;x++)
                for (int edge=0;edge<2;edge++)
                { int a=edge*rows*(columns+1)+x; AddEdge(indices,a,a+1,sideVertices,edge!=0); }
            var mesh = new Mesh(); mesh.vertices=vertices; mesh.triangles=indices.ToArray(); mesh.RecalculateNormals();
            var result = new Shape { Vertices=vertices,Normals=mesh.normals,Colors=colors,Indices=mesh.triangles };
            Destroy(mesh); return result;
        }

        private static void AddEdge(List<int> indices, int a, int b, int offset, bool reverse)
        {
            if (reverse) indices.AddRange(new[] { a,b,b+offset,a,b+offset,a+offset });
            else indices.AddRange(new[] { a,b+offset,b,a,a+offset,b+offset });
        }

        private void Clear()
        {
            if (_pool != null) foreach (var effect in _pool) { effect.Active=false; effect.Root.gameObject.SetActive(false); }
            ActiveCount=0; EmittedCount=0; OldestAge=0;
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (_pool != null) foreach (var effect in _pool) { Destroy(effect.Mesh); if (effect.Root != null) Destroy(effect.Root.gameObject); }
            Destroy(_groundMesh); Destroy(_debrisMaterial); Destroy(_groundMaterial);
        }
    }
}
