using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    [DefaultExecutionOrder(650)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ForestBudCombatView : MonoBehaviour
    {
        private sealed class FruitView
        {
            public Transform Root, Fruit, Mark, Burst;
            public MeshRenderer Trail;
            public Renderer[] FruitRenderers;
            public MaterialPropertyBlock FruitProperties;
            public Vector3 FruitScale;
            public Mesh TrailMesh;
            public Vector3[] TrailVertices, TrailNormals;
            public Color[] TrailColors;
            public MeshRenderer MarkRenderer;
            public MaterialPropertyBlock Properties;
            public int Serial = -1, Slot = -1;
            public bool Active;
            public Vector3 Origin, Target, Fan;
            public float ArcHeight;
            public float Started, Ended;
            public float Radius;
            public readonly Vector4[] NearbyDisks = new Vector4[8];
        }

        private TickDriver _driver;
        private ArenaView _arena;
        private LayoutView _layout;
        private Simulation _shownSim;
        private int _generation = -1, _depth = -1;
        private FruitView[] _items;
        private int[] _slotViews;
        private bool _overflowReported;
        private float _clock;
        private Material _markMaterial, _trailMaterial, _burstMaterial;
        private static readonly int Progress = Shader.PropertyToID("_Progress");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int NeighborCount = Shader.PropertyToID("_NeighborCount");
        private static readonly int NearbyDisks = Shader.PropertyToID("_NearbyDisks");
        private static readonly int BurstCharge = Shader.PropertyToID("_BurstCharge");

        private void Awake()
        {
            _driver = GetComponent<TickDriver>(); _arena = GetComponent<ArenaView>();
            _layout = GetComponent<LayoutView>();
            var prefab = Resources.Load<GameObject>("Characters/Forest_Bud/Forest_Bud_Projectile");
            _markMaterial = new Material(Resources.Load<Material>("Characters/Forest_Bud/VFX/ForestBud_Landing"));
            _trailMaterial = new Material(Shader.Find("Razlom/Forest Sap"));
            _burstMaterial = new Material(Shader.Find("Sprites/Default"));
            _burstMaterial.color = new Color(1f, .53f, .16f, .85f);
            _slotViews = new int[TickDriver.MaxSimCapacity * 10];
            System.Array.Fill(_slotViews, -1);
            _items = new FruitView[480];
            for (int i = 0; i < _items.Length; i++) _items[i] = Create(prefab, i);
        }

        private FruitView Create(GameObject prefab, int index)
        {
            var item = new FruitView();
            item.Root = new GameObject("Forest fruit / " + index).transform;
            item.Root.SetParent(transform, false);
            // Всё создаётся при прогреве; после начала залпа переиспользуется слот симуляции.
            GameObject fruit = Instantiate(prefab, item.Root);
            item.Fruit = fruit.transform;
            item.Fruit.localScale *= 1.85f;
            item.FruitScale = item.Fruit.localScale;
            item.FruitRenderers = fruit.GetComponentsInChildren<Renderer>();
            item.FruitProperties = new MaterialPropertyBlock();
            foreach (var collider in fruit.GetComponentsInChildren<Collider>()) Destroy(collider);
            foreach (var rigidbody in fruit.GetComponentsInChildren<Rigidbody>()) Destroy(rigidbody);
            item.Mark = new GameObject("Зафиксированное место падения").transform;
            item.Mark.SetParent(item.Root, false);
            item.Mark.gameObject.AddComponent<MeshFilter>().sharedMesh = Quad();
            item.MarkRenderer = item.Mark.gameObject.AddComponent<MeshRenderer>();
            item.MarkRenderer.sharedMaterial = _markMaterial;
            item.MarkRenderer.shadowCastingMode = ShadowCastingMode.Off;
            item.MarkRenderer.receiveShadows = false;
            item.Properties = new MaterialPropertyBlock();
            item.Trail = new GameObject("Капли сока").AddComponent<MeshRenderer>();
            item.Trail.transform.SetParent(item.Root, false);
            item.Trail.sharedMaterial = _trailMaterial;
            item.Trail.shadowCastingMode = ShadowCastingMode.Off;
            item.Trail.receiveShadows = false;
            item.TrailMesh = new Mesh { name = "ForestSapDrops" };
            item.TrailMesh.MarkDynamic();
            item.TrailVertices = new Vector3[6 * 42];
            item.TrailNormals = new Vector3[item.TrailVertices.Length];
            item.TrailColors = new Color[item.TrailVertices.Length];
            var indices = new int[6 * 6 * 6 * 6];
            int at = 0;
            for (int drop = 0; drop < 6; drop++)
                for (int y = 0; y < 6; y++)
                    for (int x = 0; x < 6; x++)
                    {
                        int a = drop*42 + y*6+x, b = drop*42+y*6+(x+1)%6;
                        indices[at++]=a; indices[at++]=b; indices[at++]=b+6;
                        indices[at++]=a; indices[at++]=b+6; indices[at++]=a+6;
                    }
            item.TrailMesh.vertices = item.TrailVertices;
            item.TrailMesh.triangles = indices;
            item.Trail.gameObject.AddComponent<MeshFilter>().sharedMesh = item.TrailMesh;
            item.Burst = new GameObject("Выброс из чаши").transform;
            item.Burst.SetParent(item.Root, false);
            item.Burst.gameObject.AddComponent<MeshFilter>().sharedMesh = BurstMesh();
            var burstRenderer = item.Burst.gameObject.AddComponent<MeshRenderer>();
            burstRenderer.sharedMaterial = _burstMaterial;
            burstRenderer.shadowCastingMode = ShadowCastingMode.Off;
            item.Root.gameObject.SetActive(false);
            return item;
        }

        private static Mesh _burstMesh;
        private static Mesh BurstMesh()
        {
            if (_burstMesh != null) return _burstMesh;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphere = primitive.GetComponent<MeshFilter>().sharedMesh;
            var combine = new CombineInstance[5];
            for (int i = 0; i < 5; i++)
            {
                float angle = i * Mathf.PI * .4f;
                combine[i] = new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(
                    new Vector3(Mathf.Cos(angle)*.19f, .18f+(i%2)*.07f, Mathf.Sin(angle)*.19f),
                    Quaternion.identity, new Vector3(.075f,.14f,.075f)) };
            }
            _burstMesh = new Mesh { name = "ForestBudFiveSapFlecks" }; _burstMesh.CombineMeshes(combine);
            Destroy(primitive); return _burstMesh;
        }

        private static Mesh _quad;
        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "ForestBudLandingQuad" };
            const int sides = 80;
            var vertices = new Vector3[4+(sides+1)*2];
            var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length];
            vertices[0]=new Vector3(-1,0,-1); vertices[1]=new Vector3(-1,0,1);
            vertices[2]=new Vector3(1,0,1); vertices[3]=new Vector3(1,0,-1);
            uv[0]=Vector2.zero; uv[1]=Vector2.up; uv[2]=Vector2.one; uv[3]=Vector2.right;
            var triangles = new int[6+sides*6];
            triangles[0]=0; triangles[1]=1; triangles[2]=2; triangles[3]=0; triangles[4]=2; triangles[5]=3;
            for (int i=0;i<=sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;
                var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.955f;
                int k=4+i*2;
                vertices[k]=radial; vertices[k+1]=radial+Vector3.up*(.07f+.24f*Mathf.Pow(.5f+.5f*Mathf.Cos(angle*5),8));
                uv[k]=uv[k+1]=new Vector2(radial.x*.5f+.5f,radial.z*.5f+.5f);
                colors[k]=new Color(1,0,0,1); colors[k+1]=new Color(1,1,0,1);
                if (i==sides) continue;
                int t=6+i*6;
                triangles[t]=k; triangles[t+1]=k+1; triangles[t+2]=k+3;
                triangles[t+3]=k; triangles[t+4]=k+3; triangles[t+5]=k+2;
            }
            _quad.vertices=vertices; _quad.uv=uv; _quad.colors=colors;
            _quad.triangles=triangles; _quad.RecalculateBounds();
            return _quad;
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;
            if (!ReferenceEquals(sim, _shownSim) || _generation != _driver.Generation || depth != _depth)
            {
                Clear(); _shownSim = sim; _generation = _driver.Generation; _depth = depth;
            }
            if (sim == null || _driver.GameplayPaused) return;
            _clock += Time.deltaTime;
            float renderTick = sim.Tick - 1 + _driver.Alpha;
            for (int slot = 0; slot < sim.ForestFruitCapacity; slot++)
            {
                if (_slotViews[slot] >= 0 || !sim.TryGetForestFruit(slot, out var added)) continue;
                int free = -1;
                for (int j = 0; j < _items.Length; j++)
                    if (_items[j].Slot < 0) { free = j; break; }
                if (free < 0)
                {
                    if (!_overflowReported) { Debug.LogError("ForestBud: исчерпан прогретый пул плодов."); _overflowReported = true; }
                    continue;
                }
                _slotViews[slot] = free; _items[free].Slot = slot;
            }
            for (int i = 0; i < _items.Length; i++)
            {
                var item = _items[i];
                if (item.Slot < 0) continue;
                if (!sim.TryGetForestFruit(item.Slot, out var fruit))
                {
                    if (item.Active) { item.Active = false; item.Ended = _clock; item.Fruit.gameObject.SetActive(false); item.Trail.enabled = false; }
                    if (item.Root.gameObject.activeSelf)
                    {
                        float fade = Mathf.Clamp01((_clock - item.Ended) / .16f);
                        item.Properties.SetFloat(Opacity, 1f - fade);
                        item.Properties.SetFloat(Progress, 1f);
                        item.MarkRenderer.SetPropertyBlock(item.Properties);
                        item.Burst.gameObject.SetActive(false);
                        if (fade >= 1f)
                        { item.Root.gameObject.SetActive(false); _slotViews[item.Slot] = -1; item.Slot = -1; }
                    }
                    continue;
                }
                if (!item.Active || item.Serial != fruit.Serial) Launch(item, fruit);
                float t = Mathf.Clamp01((renderTick - fruit.LaunchTick) / Mathf.Max(1, fruit.ImpactTick - fruit.LaunchTick));
                Vector3 position = Arc(item, t);
                item.Fruit.position = position;
                Vector3 direction = Arc(item, Mathf.Min(1f, t + .005f)) - Arc(item, Mathf.Max(0f, t - .005f));
                item.Fruit.rotation = Quaternion.FromToRotation(Vector3.up, direction.sqrMagnitude > .00001f ? -direction.normalized : Vector3.up)
                    * Quaternion.AngleAxis(t * 400f + fruit.ShotIndex * 72f, Vector3.up);
                float pressure = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.55f,1f,t));
                item.Fruit.localScale = item.FruitScale * (1f + pressure * (.09f + .015f * Mathf.Sin(t*90f)));
                item.FruitProperties.SetFloat(BurstCharge, .15f + pressure * .85f);
                foreach (var renderer in item.FruitRenderers) renderer.SetPropertyBlock(item.FruitProperties);
                UpdateDrops(item, t, fruit.ShotIndex);
                item.Properties.SetFloat(Progress, t); item.Properties.SetFloat(Opacity, 1f);
                bool markVisible = true;
                int neighbors = 0;
                // Совпавшие при неподвижном герое диски показывают ближайшее падение,
                // а не пять непрозрачных слоёв и пять спорящих таймеров.
                for (int other = 0; other < _items.Length; other++)
                {
                    var earlier = _items[other];
                    if (!earlier.Active || earlier == item) continue;
                    float distance = (earlier.Target - item.Target).sqrMagnitude;
                    if (distance < .01f)
                    { if (earlier.Serial < item.Serial) markVisible = false; continue; }
                    float reach = item.Radius + earlier.Radius;
                    if (distance < reach * reach && neighbors < item.NearbyDisks.Length)
                        item.NearbyDisks[neighbors++] = new Vector4(earlier.Target.x, earlier.Target.z, earlier.Radius, 0);
                }
                item.Properties.SetFloat(NeighborCount, neighbors);
                item.Properties.SetVectorArray(NearbyDisks, item.NearbyDisks);
                item.MarkRenderer.SetPropertyBlock(item.Properties);
                item.MarkRenderer.enabled = markVisible;
                float puff = (_clock - item.Started) / .18f;
                item.Burst.gameObject.SetActive(puff < 1f);
                item.Burst.position = item.Origin + Vector3.up * (.2f * puff);
                item.Burst.localScale = Vector3.one * Mathf.Sin(Mathf.PI * Mathf.Clamp01(puff));
            }
        }

        private void Launch(FruitView item, ForestFruitState fruit)
        {
            item.Active = true; item.Serial = fruit.Serial; item.Started = _clock;
            item.Origin = new Vector3(fruit.Origin.X.ToFloat(), 1.15f, fruit.Origin.Y.ToFloat());
            if (_arena.TryGetEntityView(fruit.Source, out var owner))
            {
                var bud = owner.GetComponent<ForestBudAnimatorView>();
                if (bud != null) item.Origin = bud.LaunchPosition(fruit.ShotIndex);
            }
            float ground = _layout != null ? _layout.WeaponGroundHeight(fruit.Target.X.ToFloat(), fruit.Target.Y.ToFloat()) : 0f;
            item.Target = new Vector3(fruit.Target.X.ToFloat(), ground + .23f, fruit.Target.Y.ToFloat());
            var direction = item.Target - item.Origin; direction.y = 0f; direction.Normalize();
            // Вариация не трогает Sim RNG и обнуляется у обоих концов дуги.
            uint variation = unchecked((uint)fruit.Serial * 747796405u + 2891336453u);
            variation = (variation ^ (variation >> 16)) * 2246822519u;
            float lateral = ((variation & 65535u) / 65535f * 2f - 1f) * .2f;
            float vertical = ((variation >> 16) / 65535f * 2f - 1f) * .3f;
            item.Fan = Vector3.Cross(Vector3.up, direction) * ((fruit.ShotIndex - 2) * .65f + lateral);
            item.ArcHeight = 3.5f + vertical;
            item.Mark.position = new Vector3(item.Target.x, ground + .055f, item.Target.z);
            item.Radius = fruit.Radius.ToFloat();
            item.Mark.localScale = Vector3.one * (item.Radius / .98f);
            item.Fruit.gameObject.SetActive(true); item.Trail.enabled = true;
            item.MarkRenderer.enabled = true;
            item.Root.gameObject.SetActive(true);
            if (CaptureRig.ForestBudShowcase)
                Debug.Log($"[forest-fruit] serial={fruit.Serial} source={fruit.Source} shot={fruit.ShotIndex} launch={fruit.LaunchTick} impact={fruit.ImpactTick} origin={item.Origin} target={item.Target}");
        }

        private static void UpdateDrops(FruitView item, float t, int shot)
        {
            // Короткий шлейф состоит из объёмных капель: нет повёрнутой к камере ленты.
            for (int drop=0;drop<6;drop++)
            {
                float delay=.016f+drop*.011f;
                float age=Mathf.Clamp01(t/delay);
                float sample=Mathf.Max(0,t-delay);
                var center=Arc(item,sample);
                float spiral=t*19+drop*2.4f+shot;
                center+=new Vector3(Mathf.Cos(spiral),Mathf.Sin(spiral),Mathf.Sin(spiral*.8f))*(.025f+drop*.012f);
                var tangent=Arc(item,Mathf.Min(1,sample+.004f))-Arc(item,Mathf.Max(0,sample-.004f));
                var rotation=Quaternion.FromToRotation(Vector3.up,tangent.normalized);
                float size=Mathf.Lerp(.085f,.018f,drop/5f)*age;
                Color color=Color.Lerp(new Color(1,.69f,.19f,.88f),new Color(1,.22f,.045f,0),drop/6f);
                for(int y=0;y<=6;y++)
                    for(int x=0;x<6;x++)
                    {
                        float latitude=y*Mathf.PI/6, longitude=x*Mathf.PI/3;
                        var normal=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                        int k=drop*42+y*6+x;
                        item.TrailVertices[k]=item.Root.InverseTransformPoint(center+rotation*Vector3.Scale(normal,new Vector3(size,size*1.45f,size)));
                        item.TrailNormals[k]=item.Root.InverseTransformDirection(rotation*normal);
                        item.TrailColors[k]=color;
                    }
            }
            item.TrailMesh.vertices=item.TrailVertices;
            item.TrailMesh.normals=item.TrailNormals;
            item.TrailMesh.colors=item.TrailColors;
            item.TrailMesh.RecalculateBounds();
        }

        private static Vector3 Arc(FruitView item, float t)
            => Vector3.Lerp(item.Origin, item.Target, t) + (Vector3.up * item.ArcHeight + item.Fan) * (4f * t * (1f - t));

        private void Clear()
        {
            if (_items == null) return;
            foreach (var item in _items) { item.Active = false; item.Serial = -1; item.Slot = -1; item.Root.gameObject.SetActive(false); }
            System.Array.Fill(_slotViews, -1);
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (_items != null) foreach (var item in _items) Destroy(item.TrailMesh);
            Destroy(_markMaterial); Destroy(_trailMaterial); Destroy(_burstMaterial);
        }
    }
}
