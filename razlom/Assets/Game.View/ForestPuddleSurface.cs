using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Ground-conforming acid, glossy raised bubbles and the actual fruit rind.
    /// Warmed with the four puddle instances. Placement samples terrain once;
    /// all subsequent motion is an analytic function of the Sim age and serial.
    /// </summary>
    public sealed class ForestPuddleSurface : MonoBehaviour
    {
        public const string ShaderName = "Razlom/Forest Acid Surface";
        public const string DropShaderName = "Razlom/Forest Acid Drop";
        public const string FruitShaderName = "Razlom/Forest Acid Fruit";
        private const int GridSteps = 32, BubbleCount = 10, AccentBubbleCount = 3, DropCount = 6, RingCount = 4;
        private const float SurfaceLift = .045f;
        [SerializeField] private Material surfaceMaterial, dropMaterial;
        [SerializeField] private GameObject fruitPrefab;

        private Mesh _groundMesh, _dropMesh, _bubbleMesh, _ringMesh;
        private MeshRenderer _ground;
        private Transform _fruit;
        private readonly List<Material> _fruitMaterials = new List<Material>();
        private Transform[] _drops;
        private Renderer[] _dropRenderers;
        private Transform[] _rings;
        private Renderer[] _ringRenderers;
        private Vector3[] _anchors, _vertices;
        private Quaternion[] _anchorRotations;
        private float[] _bubbleSizes, _bubbleOnsets, _bubblePeriods, _popAges;
        private MaterialPropertyBlock _surfaceProperties;
        private MaterialPropertyBlock _dropProperties;
        private float _seed, _extent;
        private Vector3 _fruitRest;
        private Quaternion _fruitRotation;
        private static readonly int Age = Shader.PropertyToID("_AgeSeconds"), Opacity = Shader.PropertyToID("_Opacity"),
            Spread = Shader.PropertyToID("_Spread"), Seed = Shader.PropertyToID("_Seed");

        public void ConfigureAssets(Material surface, Material drops, GameObject fruit)
        { surfaceMaterial = surface; dropMaterial = drops; fruitPrefab = fruit; }

        private void Awake() => Prepare();

        private void Prepare()
        {
            if (_groundMesh != null || surfaceMaterial == null) return;
            // Unity native objects cannot be constructed on the prefab import thread.
            _surfaceProperties = new MaterialPropertyBlock();
            _dropProperties = new MaterialPropertyBlock();
            var host = new GameObject("Viscous acid surface");
            host.transform.SetParent(transform, false);
            _groundMesh = new Mesh { name = "Acid terrain grid 32x32" };
            _vertices = new Vector3[(GridSteps + 1) * (GridSteps + 1)];
            var uv = new Vector2[_vertices.Length];
            var triangles = new int[GridSteps * GridSteps * 6];
            for (int z = 0; z <= GridSteps; z++)
                for (int x = 0; x <= GridSteps; x++)
                    uv[z * (GridSteps + 1) + x] = new Vector2(x / (float)GridSteps, z / (float)GridSteps);
            int at = 0;
            for (int z = 0; z < GridSteps; z++)
                for (int x = 0; x < GridSteps; x++)
                {
                    int a = z * (GridSteps + 1) + x, b = a + GridSteps + 1;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 1;
                    triangles[at++] = a + 1; triangles[at++] = b; triangles[at++] = b + 1;
                }
            _groundMesh.vertices = _vertices; _groundMesh.uv = uv; _groundMesh.triangles = triangles;
            host.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
            _ground = host.AddComponent<MeshRenderer>(); ConfigureRenderer(_ground, surfaceMaterial);

            _dropMesh = MakeDropMesh();
            _bubbleMesh = MakeBubbleMesh();
            _ringMesh = MakeRingMesh();
            _drops = new Transform[BubbleCount + DropCount];
            _dropRenderers = new Renderer[_drops.Length];
            _anchors = new Vector3[BubbleCount];
            _anchorRotations = new Quaternion[BubbleCount];
            _bubbleSizes = new float[BubbleCount]; _bubbleOnsets = new float[BubbleCount];
            _bubblePeriods = new float[BubbleCount]; _popAges = new float[BubbleCount];
            for (int i = 0; i < _drops.Length; i++)
            {
                var drop = new GameObject(i < BubbleCount ? "Acid bubble" : "Acid droplet");
                drop.transform.SetParent(transform, false);
                drop.AddComponent<MeshFilter>().sharedMesh = i < BubbleCount ? _bubbleMesh : _dropMesh;
                var renderer = drop.AddComponent<MeshRenderer>(); ConfigureRenderer(renderer, dropMaterial);
                _drops[i] = drop.transform; _dropRenderers[i] = renderer;
            }
            _rings = new Transform[RingCount]; _ringRenderers = new Renderer[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                var ring = new GameObject("Bubble pop liquid lip");
                ring.transform.SetParent(transform, false);
                ring.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
                var renderer = ring.AddComponent<MeshRenderer>(); ConfigureRenderer(renderer, dropMaterial);
                _rings[i] = ring.transform; _ringRenderers[i] = renderer;
            }
            PrepareFruit();
        }

        private static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void PrepareFruit()
        {
            if (fruitPrefab == null) return;
            _fruit = new GameObject("Retained rotten fruit rind").transform;
            _fruit.SetParent(transform, false);
            var fruit = Instantiate(fruitPrefab, _fruit);
            var renderers = fruit.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = .52f / Mathf.Max(.001f, longest);
            var center = _fruit.InverseTransformPoint(bounds.center);
            fruit.transform.localScale *= scale;
            fruit.transform.localPosition -= center * scale;
            var fragmentShader = Shader.Find(FruitShaderName);
            foreach (var renderer in renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                // The imported fruit is deliberately not CPU-readable. Clip a
                // ragged missing quarter in a derivative of its own materials,
                // preserving the existing mesh, UVs and base-color textures.
                if (fragmentShader == null) continue;
                var localBounds = renderer.localBounds;
                float size = Mathf.Max(localBounds.size.x, Mathf.Max(localBounds.size.y, localBounds.size.z));
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    var material = new Material(materials[i]) { shader = fragmentShader, name = "Retained fruit: " + materials[i].name };
                    material.SetVector("_CutCenter", localBounds.center);
                    material.SetFloat("_CutScale", Mathf.Max(.001f, size));
                    material.SetColor("_BaseColor", new Color(.72f, .78f, .42f, 1f));
                    materials[i] = material; _fruitMaterials.Add(material);
                }
                renderer.sharedMaterials = materials;
            }
            var pulp = new GameObject("Exposed dark pulp");
            pulp.transform.SetParent(_fruit, false);
            pulp.transform.localPosition = new Vector3(.06f, -.02f, 0f);
            pulp.transform.localScale = new Vector3(.17f, .125f, .17f);
            pulp.AddComponent<MeshFilter>().sharedMesh = _dropMesh;
            var pulpRenderer = pulp.AddComponent<MeshRenderer>(); ConfigureRenderer(pulpRenderer, dropMaterial);
            var pulpProperties = new MaterialPropertyBlock();
            pulpProperties.SetColor("_Deep", new Color(.12f, .075f, .018f));
            pulpProperties.SetColor("_Bright", new Color(.39f, .24f, .035f));
            pulpProperties.SetColor("_Specular", new Color(.65f, .43f, .12f));
            pulpRenderer.SetPropertyBlock(pulpProperties);
        }

        public void Place(LayoutView layout, int serial, float radius)
        {
            Prepare();
            if (_groundMesh == null) return;
            _seed = (uint)serial % 997u;
            _extent = radius / .82f;
            var origin = transform.position;
            for (int z = 0; z <= GridSteps; z++)
                for (int x = 0; x <= GridSteps; x++)
                {
                    float px = (x / (float)GridSteps * 2f - 1f) * _extent;
                    float pz = (z / (float)GridSteps * 2f - 1f) * _extent;
                    float height = layout != null ? layout.WeaponGroundHeight(origin.x + px, origin.z + pz) : origin.y;
                    _vertices[z * (GridSteps + 1) + x] = new Vector3(px, height - origin.y + SurfaceLift, pz);
                }
            _groundMesh.vertices = _vertices; _groundMesh.RecalculateNormals(); _groundMesh.RecalculateBounds();
            float arm = Simulation.PuddleArmTicks / (float)Simulation.TicksPerSecond;
            for (int i = 0; i < _anchors.Length; i++)
            {
                bool accent = i < AccentBubbleCount;
                // Three spaced accents stay readable at the game camera. Their
                // anchors never slide outward when the surface front is revealed.
                float angle = (accent ? i / 3f + Rand(0, 1) : i * .6180339f + Rand(i, 1)) * Mathf.PI * 2f;
                float reach = radius * Mathf.Lerp(accent ? .32f : .22f, accent ? .58f : .73f, Rand(i, 2));
                _anchors[i] = GroundPoint(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach);
                _anchorRotations[i] = Quaternion.FromToRotation(Vector3.up, GroundNormal(_anchors[i]))
                    * Quaternion.Euler(0f, Rand(i, 7) * 360f, 0f);
                _bubbleSizes[i] = Mathf.Lerp(accent ? .12f : .045f, accent ? .18f : .09f, Rand(i, 5));
                float frontArrival = arm * Mathf.Clamp01((reach / radius - .24f) / .76f);
                _bubbleOnsets[i] = frontArrival + .025f + i * .035f + Rand(i, 4) * .1f;
                _bubblePeriods[i] = Mathf.Lerp(accent ? 1.1f : .8f, accent ? 1.55f : 1.25f, Rand(i, 3));
            }
            if (_fruit != null)
            {
                float angle = (Rand(0, 1) + .5f) * Mathf.PI * 2f;
                _fruitRest = GroundPoint(Mathf.Cos(angle) * radius * .34f, Mathf.Sin(angle) * radius * .34f)
                    + Vector3.up * .11f;
                _fruitRotation = Quaternion.Euler(12f, 35f + _seed * 47f, -16f);
                _fruit.SetLocalPositionAndRotation(_fruitRest, _fruitRotation);
            }
            Sample(0f, 1f);
        }

        public void Sample(float age, float visible)
        {
            if (_ground == null) return;
            float arm = Simulation.PuddleArmTicks / (float)Simulation.TicksPerSecond;
            float fadeStart = (Simulation.PuddleArmTicks + Simulation.PuddleLifeTicks) / (float)Simulation.TicksPerSecond;
            float fadeDuration = Simulation.PuddleFadeTicks / (float)Simulation.TicksPerSecond;
            float fade = Mathf.Clamp01(visible) * Mathf.Clamp01(1f - (age - fadeStart) / fadeDuration);
            float spread = Mathf.Lerp(.24f, 1f, Mathf.SmoothStep(0f, 1f, age / arm));
            _surfaceProperties.SetFloat(Age, age); _surfaceProperties.SetFloat(Opacity, fade);
            _surfaceProperties.SetFloat(Spread, spread); _surfaceProperties.SetFloat(Seed, _seed);
            _ground.SetPropertyBlock(_surfaceProperties);
            for (int i = 0; i < BubbleCount; i++)
            {
                float elapsed = age - _bubbleOnsets[i];
                float cycle = Mathf.Repeat(Mathf.Max(0f, elapsed), _bubblePeriods[i]);
                float inflateDuration = _bubblePeriods[i] * .66f;
                _popAges[i] = elapsed < 0f ? -1f : cycle - inflateDuration;
                float growth = Mathf.Clamp01(cycle / inflateDuration);
                float swell = Mathf.SmoothStep(0f, 1f, growth);
                float collapse = 1f - Mathf.Clamp01(_popAges[i] / .055f);
                bool active = elapsed >= 0f && collapse > 0f && fade > .001f;
                _dropRenderers[i].enabled = active;
                if (!active) continue;
                // A shallow viscous blister fills its footprint, then raises a
                // lopsided dome. It snaps down in two Sim ticks instead of gently
                // reversing a sine wave. All cycles can be sampled out of order.
                float radius = _bubbleSizes[i] * Mathf.Lerp(.08f, 1f, Mathf.Sqrt(swell));
                float wobble = Mathf.Sin(growth * 13f + i * 1.7f) * .07f * swell;
                float height = _bubbleSizes[i] * Mathf.Lerp(.03f, 1.05f + wobble, swell * swell) * collapse;
                _drops[i].localPosition = _anchors[i] + Vector3.up * .003f;
                _drops[i].localRotation = _anchorRotations[i];
                _drops[i].localScale = new Vector3(radius * (1f + wobble), Mathf.Max(.001f, height), radius * (1f - wobble));
                _dropProperties.SetFloat(Opacity, fade);
                _dropRenderers[i].SetPropertyBlock(_dropProperties);
            }
            SamplePopDrops(fade);
            SamplePopRings(fade);
            if (_fruit != null)
            {
                float land = Mathf.Clamp01(age / .2f);
                _fruit.localPosition = _fruitRest + Vector3.up * ((1f - land) * .22f - (1f - fade) * .36f);
                _fruit.localRotation = _fruitRotation * Quaternion.Euler(0f, 0f, (1f - land) * 18f);
                _fruit.localScale = Vector3.one * Mathf.Lerp(.72f, 1f, land) * Mathf.Sqrt(fade);
            }
        }

        private void SamplePopDrops(float fade)
        {
            for (int i = 0; i < DropCount; i++)
            {
                int bubble = i / 2, index = BubbleCount + i;
                float popAge = _popAges[bubble] - (i % 2) * .018f;
                float duration = Mathf.Min(Mathf.Lerp(.30f, .43f, Rand(i, 11)), _bubblePeriods[bubble] * .34f - .035f);
                bool active = popAge >= 0f && popAge < duration && fade > .001f;
                _dropRenderers[index].enabled = active;
                if (!active) continue;
                float phase = popAge / duration;
                float angle = (Rand(i, 12) + i * .5f) * Mathf.PI * 2f;
                float travel = Mathf.Lerp(.16f, .34f, Rand(i, 13)) * phase;
                var anchor = _anchors[bubble];
                // Current XZ is sampled from the already conformed grid; using
                // only the launch height would send drops through a rising slope.
                var point = GroundPoint(anchor.x + Mathf.Cos(angle) * travel, anchor.z + Mathf.Sin(angle) * travel);
                float height = _bubbleSizes[bubble] * (1f - phase)
                    + 4f * phase * (1f - phase) * Mathf.Lerp(.20f, .34f, Rand(i, 14));
                float radius = Mathf.Lerp(.028f, .044f, Rand(i, 15));
                float landing = 1f - Mathf.SmoothStep(.78f, 1f, phase);
                _drops[index].localPosition = point + Vector3.up * (height + radius * .5f);
                _drops[index].localScale = new Vector3(radius, radius * Mathf.Lerp(1.8f, .5f, phase), radius);
                _dropProperties.SetFloat(Opacity, fade * landing);
                _dropRenderers[index].SetPropertyBlock(_dropProperties);
            }
        }

        private void SamplePopRings(float fade)
        {
            for (int i = 0; i < RingCount; i++)
            {
                float phase = _popAges[i] / .24f;
                bool active = phase >= 0f && phase < 1f && fade > .001f;
                _ringRenderers[i].enabled = active;
                if (!active) continue;
                float radius = _bubbleSizes[i] * Mathf.Lerp(.65f, 1.22f, phase);
                _rings[i].localPosition = _anchors[i] + Vector3.up * .009f;
                _rings[i].localRotation = _anchorRotations[i];
                _rings[i].localScale = new Vector3(radius, radius * .5f * (1f - phase), radius);
                _dropProperties.SetFloat(Opacity, fade * (1f - phase));
                _ringRenderers[i].SetPropertyBlock(_dropProperties);
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
            if (_dropMesh != null) Destroy(_dropMesh);
            if (_bubbleMesh != null) Destroy(_bubbleMesh);
            if (_ringMesh != null) Destroy(_ringMesh);
            foreach (var material in _fruitMaterials) if (material != null) Destroy(material);
        }
    }
}
