using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// ЖИВАЯ КИСЛАЯ ЛУЖА V6 — всё, что лежит на земле (кадр владельца
    /// 10-bud-puddle-alive-b-vapour-crust, выбор G10 от 29.09).
    ///
    /// Одна сетка по рельефу несёт три слоя: тёмную мокрую землю и вялую траву
    /// (умножение поверх настоящей земли), сухую потрескавшуюся корку и саму
    /// мутную кислоту с рябью. Вокруг — пучки травы, которые при шлепке
    /// примяты, потом буреют и ложатся наружу. В луже — вздувающиеся и
    /// лопающиеся пузыри, капли шлепка, которые падают обратно и дают рябь
    /// ровно там и тогда, где упали, и осколки гнилой кожуры плода.
    ///
    /// Рельеф снимается один раз при постановке (Place); дальше всё — функция
    /// возраста от тика Sim, степени высыхания и угасания следа (Sample):
    /// кадр можно взять в любом порядке, перемотка даёт тот же.
    /// Префаб собирает ForestPuddleVfxSetup, ведёт ForestPuddleView.
    /// </summary>
    public sealed class ForestPuddleSurface : MonoBehaviour
    {
        public const string ShaderName = "Razlom/Forest Acid Surface";
        public const string GroundShaderName = "Razlom/Forest Acid Ground";
        public const string GrassShaderName = "Razlom/Forest Acid Grass";
        public const string DropShaderName = "Razlom/Forest Acid Drop";
        public const string FruitShaderName = "Razlom/Forest Acid Fruit";

        /// <summary>Сетка шире лужи: тёмная земля и вялая трава лежат до полутора радиусов.</summary>
        public const float GroundReach = 1.55f;
        private const int GridSteps = 40;
        private const int BubbleCount = 14, AccentBubbleCount = 4, PopDropCount = 8, RingCount = 4;
        private const int SplashDropCount = 8, ShardCount = 4, TuftCount = 24;
        /// <summary>Капли шлепка: вязкая кислота падает чуть быстрее воды.</summary>
        private const float DropGravity = 12.5f;
        /// <summary>Осколок кожуры — шапка сферы плода: радиус в осях осколка и косинус рваного края.</summary>
        private const float ShardScale = .78f, ShardRadius = .26f * ShardScale, ShardCos = .62f;
        private const float ArmSeconds = Simulation.PuddleArmTicks / (float)Simulation.TicksPerSecond;
        // Гниющая кожура: тёмно-бурая с красным, изнутри бледная мякоть.
        private static readonly Color RindTint = new Color(.55f, .40f, .85f, 1f), PulpTint = new Color(.80f, .74f, .38f, 1f);

        [SerializeField] private Material surfaceMaterial, scorchMaterial, crustMaterial, grassMaterial, dropMaterial;
        [SerializeField] private GameObject fruitPrefab;

        private Mesh _groundMesh, _tuftMesh, _dropMesh, _bubbleMesh, _ringMesh;
        private MeshRenderer _liquid, _scorch, _crust, _tufts;
        private Vector3[] _vertices, _tuftVertices;
        private Vector4[] _tuftRoots, _tuftBends;
        private Color32[] _tuftColors;
        private Transform[] _bubbles, _popDrops, _splashDrops, _rings;
        private Renderer[] _bubbleRenderers, _popDropRenderers, _splashDropRenderers, _ringRenderers;
        private Vector3[] _anchors;
        private Quaternion[] _anchorRotations;
        private float[] _bubbleSizes, _bubbleOnsets, _bubblePeriods, _popAges;
        private Vector3[] _dropLaunch, _dropVelocity;
        private float[] _dropFlight, _dropSize;
        private readonly Vector4[] _ripples = new Vector4[SplashDropCount];
        private readonly Vector4[] _popRipples = new Vector4[AccentBubbleCount];
        private Transform[] _shards;
        private Vector3[] _shardRest;
        private Quaternion[] _shardRestRotation, _shardLaunchRotation;
        private float[] _shardLand;
        private readonly List<Material> _fruitMaterials = new List<Material>();
        private MaterialPropertyBlock _liquidProperties, _groundProperties, _grassProperties, _dropProperties;
        private float _seed, _radius = 1.2f, _extent = 1.8f;

        private static readonly int Age = Shader.PropertyToID("_AgeSeconds"), Opacity = Shader.PropertyToID("_Opacity"),
            Dry = Shader.PropertyToID("_Dry"), Fade = Shader.PropertyToID("_Fade"), Seed = Shader.PropertyToID("_Seed"),
            Radius = Shader.PropertyToID("_Radius"), Ripples = Shader.PropertyToID("_Ripples"),
            PopRipples = Shader.PropertyToID("_PopRipples");

        public void ConfigureAssets(Material surface, Material scorch, Material crust, Material grass, Material drops, GameObject fruit)
        {
            surfaceMaterial = surface; scorchMaterial = scorch; crustMaterial = crust;
            grassMaterial = grass; dropMaterial = drops; fruitPrefab = fruit;
        }

        private void Awake() => Prepare();

        private void Prepare()
        {
            if (_groundMesh != null || surfaceMaterial == null) return;
            // Нативные объекты Unity нельзя создавать в потоке импорта префаба — только здесь.
            _liquidProperties = new MaterialPropertyBlock();
            _groundProperties = new MaterialPropertyBlock();
            _grassProperties = new MaterialPropertyBlock();
            _dropProperties = new MaterialPropertyBlock();
            _groundMesh = BuildGrid();
            // Порядок слоёв задают очереди материалов: земля, корка, кислота.
            _scorch = Layer("Wilted dark soil", _groundMesh, scorchMaterial);
            _crust = Layer("Dry cracked crust", _groundMesh, crustMaterial);
            _liquid = Layer("Murky acid", _groundMesh, surfaceMaterial);
            if (grassMaterial != null)
            {
                _tuftMesh = BuildTuftMesh();
                _tufts = Layer("Wilting grass tufts", _tuftMesh, grassMaterial);
            }

            _dropMesh = MakeDropMesh();
            _bubbleMesh = MakeBubbleMesh();
            _ringMesh = MakeRingMesh();
            _bubbles = Children("Acid bubble", BubbleCount, _bubbleMesh, out _bubbleRenderers);
            _popDrops = Children("Bubble pop droplet", PopDropCount, _dropMesh, out _popDropRenderers);
            _splashDrops = Children("Splat droplet", SplashDropCount, _dropMesh, out _splashDropRenderers);
            _rings = Children("Bubble pop lip", RingCount, _ringMesh, out _ringRenderers);
            _anchors = new Vector3[BubbleCount];
            _anchorRotations = new Quaternion[BubbleCount];
            _bubbleSizes = new float[BubbleCount]; _bubbleOnsets = new float[BubbleCount];
            _bubblePeriods = new float[BubbleCount]; _popAges = new float[BubbleCount];
            _dropLaunch = new Vector3[SplashDropCount]; _dropVelocity = new Vector3[SplashDropCount];
            _dropFlight = new float[SplashDropCount]; _dropSize = new float[SplashDropCount];
            PrepareShards();
        }

        private MeshRenderer Layer(string title, Mesh mesh, Material material)
        {
            if (material == null) return null;
            var host = new GameObject(title);
            host.transform.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            Configure(renderer, material);
            return renderer;
        }

        private Transform[] Children(string title, int count, Mesh mesh, out Renderer[] renderers)
        {
            var items = new Transform[count];
            renderers = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject(title);
                child.transform.SetParent(transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                Configure(renderer, dropMaterial);
                renderer.enabled = false;
                items[i] = child.transform; renderers[i] = renderer;
            }
            return items;
        }

        private static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private Mesh BuildGrid()
        {
            var mesh = new Mesh { name = "Acid puddle terrain grid" };
            _vertices = new Vector3[(GridSteps + 1) * (GridSteps + 1)];
            var triangles = new int[GridSteps * GridSteps * 6];
            int at = 0;
            for (int z = 0; z < GridSteps; z++)
                for (int x = 0; x < GridSteps; x++)
                {
                    int a = z * (GridSteps + 1) + x, b = a + GridSteps + 1;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 1;
                    triangles[at++] = a + 1; triangles[at++] = b; triangles[at++] = b + 1;
                }
            mesh.vertices = _vertices;
            mesh.triangles = triangles;
            return mesh;
        }

        /// <summary>Пучок — два скрещённых квада; положение и увядание пишет PlaceTufts.</summary>
        private Mesh BuildTuftMesh()
        {
            int vertices = TuftCount * 8;
            _tuftVertices = new Vector3[vertices];
            _tuftRoots = new Vector4[vertices];
            _tuftBends = new Vector4[vertices];
            _tuftColors = new Color32[vertices];
            var uv = new Vector2[vertices];
            var triangles = new int[TuftCount * 12];
            for (int quad = 0; quad < TuftCount * 2; quad++)
            {
                int v = quad * 4, t = quad * 6;
                uv[v] = new Vector2(0f, 0f); uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f); uv[v + 3] = new Vector2(0f, 1f);
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Wilting grass tufts" };
            mesh.vertices = _tuftVertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            return mesh;
        }

        private void PrepareShards()
        {
            if (fruitPrefab == null) return;
            var shader = Shader.Find(FruitShaderName);
            if (shader == null) return;
            var copies = new Dictionary<Material, Material>();
            _shards = new Transform[ShardCount];
            _shardRest = new Vector3[ShardCount];
            _shardRestRotation = new Quaternion[ShardCount];
            _shardLaunchRotation = new Quaternion[ShardCount];
            _shardLand = new float[ShardCount];
            for (int i = 0; i < ShardCount; i++)
            {
                var shard = new GameObject("Rotten rind shard").transform;
                shard.SetParent(transform, false);
                var fruit = Instantiate(fruitPrefab, shard);
                // Каждый осколок — другая часть плода: плод повёрнут внутри осколка.
                fruit.transform.localRotation = Quaternion.Euler(i * 97f + 20f, i * 151f, i * 43f) * fruit.transform.localRotation;
                var renderers = fruit.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) { Destroy(shard.gameObject); _shards = null; return; }
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = .52f / Mathf.Max(.001f, longest);
                var center = shard.InverseTransformPoint(bounds.center);
                fruit.transform.localScale *= scale;
                fruit.transform.localPosition -= center * scale;
                foreach (var renderer in renderers)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    // Сетка плода нечитаема процессором: шапку вырезает шейдер,
                    // сетка, развёртка и текстура плода остаются свои.
                    var materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        if (materials[m] == null) continue;
                        if (!copies.TryGetValue(materials[m], out var copy))
                        {
                            copy = new Material(materials[m]) { shader = shader, name = "Rotten rind: " + materials[m].name };
                            copy.SetColor("_BaseColor", RindTint);
                            copy.SetColor("_Pulp", PulpTint);
                            copies[materials[m]] = copy;
                            _fruitMaterials.Add(copy);
                        }
                        materials[m] = copy;
                    }
                    renderer.sharedMaterials = materials;
                    var local = renderer.localBounds;
                    // Выпуклость осколка — его ось «вверх»; в осях сетки плода.
                    var dir = renderer.transform.InverseTransformDirection(shard.up).normalized;
                    var block = new MaterialPropertyBlock();
                    block.SetVector("_CutCenter", local.center);
                    block.SetFloat("_CutScale", Mathf.Max(.001f, Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z))));
                    block.SetVector("_ShardDir", new Vector4(dir.x, dir.y, dir.z, ShardCos));
                    block.SetFloat("_ShardSeed", i * 1.37f);
                    renderer.SetPropertyBlock(block);
                }
                shard.localScale = Vector3.one * ShardScale;
                _shards[i] = shard;
            }
        }

        public void Place(LayoutView layout, int serial, float radius)
        {
            Prepare();
            if (_groundMesh == null) return;
            _seed = (uint)serial % 997u;
            _radius = Mathf.Max(.1f, radius);
            _extent = _radius * GroundReach;
            var origin = transform.position;
            for (int z = 0; z <= GridSteps; z++)
                for (int x = 0; x <= GridSteps; x++)
                {
                    float px = (x / (float)GridSteps * 2f - 1f) * _extent;
                    float pz = (z / (float)GridSteps * 2f - 1f) * _extent;
                    float height = layout != null ? layout.WeaponGroundHeight(origin.x + px, origin.z + pz) : origin.y;
                    _vertices[z * (GridSteps + 1) + x] = new Vector3(px, height - origin.y, pz);
                }
            _groundMesh.vertices = _vertices;
            _groundMesh.RecalculateNormals();
            _groundMesh.RecalculateBounds();
            var bounds = _groundMesh.bounds;
            bounds.Expand(new Vector3(0f, .3f, 0f));
            _groundMesh.bounds = bounds;
            PlaceTufts();
            PlaceBubbles();
            PlaceSplash();
            PlaceShards();
            _liquidProperties.SetVectorArray(Ripples, _ripples);
            _liquidProperties.SetVectorArray(PopRipples, _popRipples);
            Sample(0f, 0f, 1f);
        }

        private void PlaceTufts()
        {
            if (_tuftMesh == null) return;
            // Корка ложится с той же стороны, что в шейдере земли: там травы нет.
            float crustAngle = _seed * 2.39996f;
            var crust = new Vector2(Mathf.Cos(crustAngle), Mathf.Sin(crustAngle));
            int v = 0;
            for (int i = 0; i < TuftCount; i++)
            {
                float angle = (i * .6180339f + Rand(i, 21) * .35f) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float reach = Mathf.Lerp(.96f, 1.46f, Mathf.Pow(Rand(i, 22), .8f));
                if (Vector2.Dot(dir, crust) > .55f && reach < 1.36f) reach = Mathf.Lerp(1.36f, 1.48f, Rand(i, 23));
                var root = GroundPoint(dir.x * reach * _radius, dir.y * reach * _radius) - Vector3.up * .01f;
                float height = Mathf.Lerp(.20f, .34f, Rand(i, 24));
                float width = height * Mathf.Lerp(.85f, 1.1f, Rand(i, 25));
                // Ближние к кислоте вянут первыми.
                float onset = .12f + (reach - .96f) * .9f + Rand(i, 26) * .3f;
                float yaw = Rand(i, 27) * Mathf.PI;
                var tint = new Color32((byte)Mathf.Lerp(205f, 255f, Rand(i, 28)), (byte)Mathf.Lerp(195f, 245f, Rand(i, 29)),
                    (byte)Mathf.Lerp(175f, 230f, Rand(i, 30)), 255);
                for (int k = 0; k < 2; k++)
                {
                    float a = yaw + k * Mathf.PI * .5f;
                    var right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (width * .5f);
                    var up = Vector3.up * height;
                    _tuftVertices[v] = root - right; _tuftVertices[v + 1] = root + right;
                    _tuftVertices[v + 2] = root + right + up; _tuftVertices[v + 3] = root - right + up;
                    for (int c = 0; c < 4; c++)
                    {
                        _tuftRoots[v + c] = new Vector4(root.x, root.y, root.z, height);
                        _tuftBends[v + c] = new Vector4(dir.x, dir.y, onset, Rand(i, 31));
                        _tuftColors[v + c] = tint;
                    }
                    v += 4;
                }
            }
            _tuftMesh.SetVertices(_tuftVertices);
            _tuftMesh.SetUVs(1, _tuftRoots);
            _tuftMesh.SetUVs(2, _tuftBends);
            _tuftMesh.SetColors(_tuftColors);
            _tuftMesh.RecalculateBounds();
            var bounds = _tuftMesh.bounds;
            bounds.Expand(.5f);
            _tuftMesh.bounds = bounds;
        }

        private void PlaceBubbles()
        {
            for (int i = 0; i < BubbleCount; i++)
            {
                bool accent = i < AccentBubbleCount;
                // Четыре крупных купола разнесены и читаются с игровой камеры;
                // мелочь рассыпана по телу. Якоря не ползут за фронтом.
                float angle = (accent ? i / (float)AccentBubbleCount + Rand(0, 1) : i * .6180339f + Rand(i, 1)) * Mathf.PI * 2f;
                float reach = _radius * Mathf.Lerp(accent ? .28f : .18f, accent ? .6f : .74f, Rand(i, 2));
                _anchors[i] = GroundPoint(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach) + Vector3.up * .05f;
                _anchorRotations[i] = Quaternion.FromToRotation(Vector3.up, GroundNormal(_anchors[i]))
                    * Quaternion.Euler(0f, Rand(i, 7) * 360f, 0f);
                _bubbleSizes[i] = Mathf.Lerp(accent ? .11f : .035f, accent ? .17f : .075f, Rand(i, 5));
                float frontArrival = ArmSeconds * Mathf.Clamp01((reach / _radius - .24f) / .76f);
                _bubbleOnsets[i] = frontArrival + .025f + i * .03f + Rand(i, 4) * .12f;
                _bubblePeriods[i] = Mathf.Lerp(accent ? 1.1f : .7f, accent ? 1.55f : 1.2f, Rand(i, 3));
            }
            // Рябь от лопнувших крупных пузырей считает шейдер кислоты.
            for (int i = 0; i < AccentBubbleCount; i++)
                _popRipples[i] = new Vector4(_anchors[i].x, _anchors[i].z,
                    _bubbleOnsets[i] + _bubblePeriods[i] * .66f, _bubblePeriods[i]);
        }

        /// <summary>
        /// Капли шлепка: вылетают из точки падения по баллистике и падают в
        /// лужу; момент и место падения отдаются шейдеру как начало ряби.
        /// </summary>
        private void PlaceSplash()
        {
            var launch = GroundPoint(0f, 0f) + Vector3.up * .12f;
            for (int i = 0; i < SplashDropCount; i++)
            {
                float angle = (i / (float)SplashDropCount + Rand(i, 31) * .1f) * Mathf.PI * 2f;
                float reach = _radius * Mathf.Lerp(.3f, .85f, Rand(i, 32));
                var land = GroundPoint(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach) + Vector3.up * .05f;
                float apex = Mathf.Lerp(.28f, .62f, Rand(i, 33));
                float rise = Mathf.Sqrt(2f * DropGravity * apex);
                float fall = launch.y - land.y;
                float flight = (rise + Mathf.Sqrt(Mathf.Max(0f, rise * rise + 2f * DropGravity * fall))) / DropGravity;
                flight = Mathf.Max(.05f, flight);
                _dropLaunch[i] = launch;
                _dropVelocity[i] = new Vector3((land.x - launch.x) / flight, rise, (land.z - launch.z) / flight);
                _dropFlight[i] = flight;
                _dropSize[i] = Mathf.Lerp(.035f, .065f, Rand(i, 34));
                _ripples[i] = new Vector4(land.x, land.z, flight, Mathf.Lerp(.7f, 1f, Rand(i, 35)));
            }
        }

        private void PlaceShards()
        {
            if (_shards == null) return;
            for (int i = 0; i < ShardCount; i++)
            {
                float angle = (i / (float)ShardCount + Rand(i, 41) * .18f) * Mathf.PI * 2f + _seed;
                float reach = _radius * Mathf.Lerp(.18f, .62f, Rand(i, 42));
                var rest = GroundPoint(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach);
                // Половина кусков лежит кожурой вверх, остальные завалились набок — видна мякоть.
                float tilt = i % 2 == 0 ? Mathf.Lerp(8f, 25f, Rand(i, 43)) : Mathf.Lerp(55f, 80f, Rand(i, 43));
                var rotation = Quaternion.AngleAxis(Rand(i, 44) * 360f, Vector3.up) * Quaternion.AngleAxis(tilt, Vector3.right);
                // Край шапки касается земли; кусок немного утоплен в кислоту.
                _shardRest[i] = rest - rotation * Vector3.up * (ShardRadius * ShardCos) + Vector3.up * .02f;
                _shardRestRotation[i] = rotation;
                _shardLaunchRotation[i] = Quaternion.Euler(Rand(i, 45) * 360f, Rand(i, 46) * 360f, Rand(i, 47) * 360f);
                _shardLand[i] = Mathf.Lerp(.16f, .26f, Rand(i, 48));
            }
        }

        /// <param name="age">Секунды от падения плода (тик Sim с долей кадра).</param>
        /// <param name="dry">0 — кислота живая, 1 — ушла к центру и оставила корку.</param>
        /// <param name="fade">1 — след виден целиком, 0 — корка, тёмная земля и трава погасли.</param>
        public void Sample(float age, float dry, float fade)
        {
            if (_liquid == null) return;
            dry = Mathf.Clamp01(dry);
            fade = Mathf.Clamp01(fade);
            float wet = 1f - dry;

            _liquidProperties.SetFloat(Age, age); _liquidProperties.SetFloat(Opacity, 1f);
            _liquidProperties.SetFloat(Dry, dry); _liquidProperties.SetFloat(Seed, _seed);
            _liquidProperties.SetFloat(Radius, _radius);
            _liquid.SetPropertyBlock(_liquidProperties);
            _liquid.enabled = dry < 1f;

            _groundProperties.SetFloat(Age, age); _groundProperties.SetFloat(Dry, dry);
            _groundProperties.SetFloat(Fade, fade); _groundProperties.SetFloat(Seed, _seed);
            _groundProperties.SetFloat(Radius, _radius);
            if (_scorch != null) { _scorch.SetPropertyBlock(_groundProperties); _scorch.enabled = fade > .001f; }
            if (_crust != null) { _crust.SetPropertyBlock(_groundProperties); _crust.enabled = fade > .001f; }
            if (_tufts != null)
            {
                _grassProperties.SetFloat(Age, age); _grassProperties.SetFloat(Opacity, fade);
                _tufts.SetPropertyBlock(_grassProperties);
                _tufts.enabled = fade > .001f;
            }

            SampleBubbles(age, wet);
            SamplePopDrops(wet);
            SamplePopRings(wet);
            SampleSplash(age);
            SampleShards(age, fade);
        }

        private void SampleBubbles(float age, float wet)
        {
            for (int i = 0; i < BubbleCount; i++)
            {
                float elapsed = age - _bubbleOnsets[i];
                float cycle = Mathf.Repeat(Mathf.Max(0f, elapsed), _bubblePeriods[i]);
                float inflateDuration = _bubblePeriods[i] * .66f;
                _popAges[i] = elapsed < 0f || wet <= 0f ? -1f : cycle - inflateDuration;
                float growth = Mathf.Clamp01(cycle / inflateDuration);
                float swell = Mathf.SmoothStep(0f, 1f, growth);
                float collapse = 1f - Mathf.Clamp01(_popAges[i] / .055f);
                bool active = elapsed >= 0f && collapse > 0f && wet > .001f;
                _bubbleRenderers[i].enabled = active;
                if (!active) continue;
                // Плоский вязкий волдырь заполняет пятно, поднимает кривой купол и
                // хлопает за два тика, а не сдувается синусом. Высыхая, оседает.
                float radius = _bubbleSizes[i] * Mathf.Lerp(.08f, 1f, Mathf.Sqrt(swell));
                float wobble = Mathf.Sin(growth * 13f + i * 1.7f) * .07f * swell;
                float height = _bubbleSizes[i] * Mathf.Lerp(.03f, 1.05f + wobble, swell * swell) * collapse * wet;
                _bubbles[i].localPosition = _anchors[i] + Vector3.up * .003f;
                _bubbles[i].localRotation = _anchorRotations[i];
                _bubbles[i].localScale = new Vector3(radius * (1f + wobble), Mathf.Max(.001f, height), radius * (1f - wobble));
                _dropProperties.SetFloat(Opacity, wet);
                _bubbleRenderers[i].SetPropertyBlock(_dropProperties);
            }
        }

        private void SamplePopDrops(float wet)
        {
            for (int i = 0; i < PopDropCount; i++)
            {
                int bubble = i / 2;
                float popAge = _popAges[bubble] - (i % 2) * .018f;
                float duration = Mathf.Min(Mathf.Lerp(.30f, .43f, Rand(i, 11)), _bubblePeriods[bubble] * .34f - .035f);
                bool active = popAge >= 0f && popAge < duration && wet > .001f;
                _popDropRenderers[i].enabled = active;
                if (!active) continue;
                float phase = popAge / duration;
                float angle = (Rand(i, 12) + i * .5f) * Mathf.PI * 2f;
                float travel = Mathf.Lerp(.16f, .34f, Rand(i, 13)) * phase;
                var anchor = _anchors[bubble];
                // Высота — от сетки рельефа в текущей точке: иначе капля уходит в склон.
                var point = GroundPoint(anchor.x + Mathf.Cos(angle) * travel, anchor.z + Mathf.Sin(angle) * travel) + Vector3.up * .05f;
                float height = _bubbleSizes[bubble] * (1f - phase)
                    + 4f * phase * (1f - phase) * Mathf.Lerp(.20f, .34f, Rand(i, 14));
                float radius = Mathf.Lerp(.028f, .044f, Rand(i, 15));
                float landing = 1f - Mathf.SmoothStep(.78f, 1f, phase);
                _popDrops[i].localPosition = point + Vector3.up * (height + radius * .5f);
                _popDrops[i].localRotation = Quaternion.identity;
                _popDrops[i].localScale = new Vector3(radius, radius * Mathf.Lerp(1.8f, .5f, phase), radius);
                _dropProperties.SetFloat(Opacity, wet * landing);
                _popDropRenderers[i].SetPropertyBlock(_dropProperties);
            }
        }

        private void SamplePopRings(float wet)
        {
            for (int i = 0; i < RingCount; i++)
            {
                float phase = _popAges[i] / .24f;
                bool active = phase >= 0f && phase < 1f && wet > .001f;
                _ringRenderers[i].enabled = active;
                if (!active) continue;
                float radius = _bubbleSizes[i] * Mathf.Lerp(.65f, 1.22f, phase);
                _rings[i].localPosition = _anchors[i] + Vector3.up * .009f;
                _rings[i].localRotation = _anchorRotations[i];
                _rings[i].localScale = new Vector3(radius, radius * .5f * (1f - phase), radius);
                _dropProperties.SetFloat(Opacity, wet * (1f - phase));
                _ringRenderers[i].SetPropertyBlock(_dropProperties);
            }
        }

        private void SampleSplash(float age)
        {
            for (int i = 0; i < SplashDropCount; i++)
            {
                bool active = age < _dropFlight[i];
                _splashDropRenderers[i].enabled = active;
                if (!active) continue;
                var velocity = _dropVelocity[i] + Vector3.down * (DropGravity * age);
                _splashDrops[i].localPosition = _dropLaunch[i] + _dropVelocity[i] * age + Vector3.down * (.5f * DropGravity * age * age);
                _splashDrops[i].localRotation = Quaternion.FromToRotation(Vector3.up, velocity.sqrMagnitude > 1e-4f ? velocity.normalized : Vector3.up);
                float size = _dropSize[i];
                // Капля вытянута по скорости, как в кадре.
                _splashDrops[i].localScale = new Vector3(size, size * (1f + velocity.magnitude * .12f), size);
                _dropProperties.SetFloat(Opacity, 1f);
                _splashDropRenderers[i].SetPropertyBlock(_dropProperties);
            }
        }

        private void SampleShards(float age, float fade)
        {
            if (_shards == null) return;
            var start = GroundPoint(0f, 0f) + Vector3.up * .28f;
            for (int i = 0; i < ShardCount; i++)
            {
                var shard = _shards[i];
                bool visible = fade > .001f;
                if (shard.gameObject.activeSelf != visible) shard.gameObject.SetActive(visible);
                if (!visible) continue;
                float land = Mathf.Clamp01(age / _shardLand[i]);
                float ease = 1f - (1f - land) * (1f - land);
                var position = Vector3.Lerp(start, _shardRest[i], ease) + Vector3.up * (4f * land * (1f - land) * .22f);
                // Лёгший кусок оседает в кислоту, гаснущий — уходит в корку.
                position += Vector3.down * (Mathf.Min(.035f, Mathf.Max(0f, age - _shardLand[i]) * .012f) + (1f - fade) * .1f);
                shard.localPosition = position;
                shard.localRotation = Quaternion.Slerp(_shardLaunchRotation[i], _shardRestRotation[i], ease);
                shard.localScale = Vector3.one * (ShardScale * Mathf.Lerp(.6f, 1f, ease) * Mathf.Sqrt(fade));
            }
        }

        private Vector3 GroundPoint(float x, float z)
        {
            float gx = Mathf.Clamp((x / _extent + 1f) * .5f * GridSteps, 0f, GridSteps);
            float gz = Mathf.Clamp((z / _extent + 1f) * .5f * GridSteps, 0f, GridSteps);
            int ix = Mathf.Min(Mathf.FloorToInt(gx), GridSteps - 1), iz = Mathf.Min(Mathf.FloorToInt(gz), GridSteps - 1);
            int a = iz * (GridSteps + 1) + ix, b = a + GridSteps + 1;
            float low = Mathf.Lerp(_vertices[a].y, _vertices[a + 1].y, gx - ix);
            float high = Mathf.Lerp(_vertices[b].y, _vertices[b + 1].y, gx - ix);
            return new Vector3(x, Mathf.Lerp(low, high, gz - iz), z);
        }

        private Vector3 GroundNormal(Vector3 point)
        {
            const float delta = .04f;
            float dx = GroundPoint(point.x + delta, point.z).y - GroundPoint(point.x - delta, point.z).y;
            float dz = GroundPoint(point.x, point.z + delta).y - GroundPoint(point.x, point.z - delta).y;
            return new Vector3(-dx / (delta * 2f), 1f, -dz / (delta * 2f)).normalized;
        }

        private float Rand(int index, int salt)
        {
            uint value = unchecked((uint)(_seed + 1f) * 747796405u ^ (uint)(index + 1) * 2891336453u ^ (uint)salt * 277803737u);
            value ^= value >> 16; value *= 2246822519u; value ^= value >> 13;
            return (value & 0xFFFFFFu) / 16777216f;
        }

        private static Mesh MakeDropMesh()
        {
            const int rings = 6, sides = 10;
            var vertices = new Vector3[(rings + 1) * (sides + 1)];
            var normals = new Vector3[vertices.Length]; var triangles = new int[rings * sides * 6];
            int index = 0;
            for (int ring = 0; ring <= rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    float latitude = ring / (float)rings * Mathf.PI, longitude = side / (float)sides * Mathf.PI * 2f;
                    var point = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                    int vertex = ring * (sides + 1) + side; vertices[vertex] = point; normals[vertex] = point;
                    if (ring == rings || side == sides) continue;
                    int next = vertex + sides + 1;
                    triangles[index++] = vertex; triangles[index++] = vertex + 1; triangles[index++] = next;
                    triangles[index++] = vertex + 1; triangles[index++] = next + 1; triangles[index++] = next;
                }
            var mesh = new Mesh { name = "Glossy acid droplet" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles; mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh MakeBubbleMesh()
        {
            const int rings = 6, sides = 16;
            var vertices = new Vector3[(rings + 1) * (sides + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[rings * sides * 6];
            int index = 0;
            for (int ring = 0; ring <= rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    float latitude = ring / (float)rings * Mathf.PI * .5f;
                    float longitude = side / (float)sides * Mathf.PI * 2f;
                    float y = Mathf.Cos(latitude), rim = Mathf.Sin(latitude);
                    int vertex = ring * (sides + 1) + side;
                    vertices[vertex] = new Vector3(rim * Mathf.Cos(longitude) + .18f * y * y,
                        y, rim * Mathf.Sin(longitude) * (1f + .08f * y));
                    normals[vertex] = new Vector3(rim * Mathf.Cos(longitude), y, rim * Mathf.Sin(longitude)).normalized;
                    if (ring == rings || side == sides) continue;
                    int next = vertex + sides + 1;
                    triangles[index++] = vertex; triangles[index++] = vertex + 1; triangles[index++] = next;
                    triangles[index++] = vertex + 1; triangles[index++] = next + 1; triangles[index++] = next;
                }
            var mesh = new Mesh { name = "Asymmetric viscous bubble dome" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles; mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh MakeRingMesh()
        {
            const int sides = 20, tubeSides = 4;
            var vertices = new Vector3[(sides + 1) * (tubeSides + 1)];
            var normals = new Vector3[vertices.Length]; var triangles = new int[sides * tubeSides * 6];
            int index = 0;
            for (int side = 0; side <= sides; side++)
                for (int tube = 0; tube <= tubeSides; tube++)
                {
                    float angle = side / (float)sides * Mathf.PI * 2f, cross = tube / (float)tubeSides * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var normal = outward * Mathf.Cos(cross) + Vector3.up * Mathf.Sin(cross);
                    int vertex = side * (tubeSides + 1) + tube;
                    vertices[vertex] = outward * (1f + Mathf.Sin(angle * 3f) * .04f) + normal * .085f;
                    normals[vertex] = normal;
                    if (side == sides || tube == tubeSides) continue;
                    int next = vertex + tubeSides + 1;
                    triangles[index++] = vertex; triangles[index++] = vertex + 1; triangles[index++] = next;
                    triangles[index++] = vertex + 1; triangles[index++] = next + 1; triangles[index++] = next;
                }
            var mesh = new Mesh { name = "Tiny raised bubble pop lip" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles; mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (_groundMesh != null) Destroy(_groundMesh);
            if (_tuftMesh != null) Destroy(_tuftMesh);
            if (_dropMesh != null) Destroy(_dropMesh);
            if (_bubbleMesh != null) Destroy(_bubbleMesh);
            if (_ringMesh != null) Destroy(_ringMesh);
            foreach (var material in _fruitMaterials) if (material != null) Destroy(material);
        }
    }
}
