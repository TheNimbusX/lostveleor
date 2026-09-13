using UnityEngine;

namespace Game.View
{
    /// <summary>Огонь Arcadia на клинке и свободные частицы, сохраняющие инерцию взмаха.</summary>
    public sealed class PelagBladeFireView : MonoBehaviour
    {
        private const int Segments = 12;
        private const string Folder = "VFX/Pelag/BlazeFire/";
        private readonly Vector3[] _vertices = new Vector3[(Segments + 1) * 2];
        private readonly Vector2[] _uv = new Vector2[(Segments + 1) * 2];
        private readonly int[] _triangles = new int[Segments * 6];
        private Mesh _mesh;
        private MeshRenderer _ribbon;
        private ParticleSystem _body, _core, _embers;
        private Transform _base, _tip;
        private Camera _camera;
        private MaterialPropertyBlock _block;
        private Vector3 _previousBase, _previousTip;
        private float _bodyBudget, _coreBudget, _emberBudget, _heat;
        private uint _random = 751;
        private bool _wasActive;
        private static readonly int HeatId = Shader.PropertyToID("_Heat");

        public int ParticleCount => (_body != null ? _body.particleCount : 0)
            + (_core != null ? _core.particleCount : 0) + (_embers != null ? _embers.particleCount : 0);

        public void Initialize(Transform bladeBase, Transform bladeTip)
        {
            _base = bladeBase;
            _tip = bladeTip;
            _camera = Camera.main;
            _block = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "Arcadia blade flame ribbon" };
            _mesh.MarkDynamic();
            for (int i = 0; i <= Segments; i++)
            {
                _uv[i * 2] = new Vector2(0, i / (float)Segments);
                _uv[i * 2 + 1] = new Vector2(1, i / (float)Segments);
                if (i == Segments) continue;
                int v = i * 2, t = i * 6;
                _triangles[t] = v; _triangles[t + 1] = v + 2; _triangles[t + 2] = v + 1;
                _triangles[t + 3] = v + 1; _triangles[t + 4] = v + 2; _triangles[t + 5] = v + 3;
            }
            _mesh.vertices = _vertices;
            _mesh.uv = _uv;
            _mesh.triangles = _triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _ribbon = gameObject.AddComponent<MeshRenderer>();
            _ribbon.sharedMaterial = Resources.Load<Material>(Folder + "M_ArcadiaBladeFire");
            _ribbon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ribbon.receiveShadows = false;
            _ribbon.enabled = false;
            _body = CreateParticles("Detached flame", "M_ArcadiaFlameParticle", 180, .3f, .13f, .8f);
            _core = CreateParticles("Hot core", "M_ArcadiaFlameParticle", 80, .16f, .075f, 1.1f);
            _embers = CreateParticles("Free embers", "M_ArcadiaEmber", 48, .45f, .012f, 1.4f);
            ConfigureColor(_body, new Color(1, .8f, .55f), new Color(1, .45f, .12f), .65f);
            ConfigureColor(_core, new Color(1.8f, 1.4f, .9f), new Color(1.4f, .8f, .22f), .4f);
            ConfigureColor(_embers, new Color(2.4f, 1.15f, .16f), new Color(1, .08f, .005f), 1);
            var noise = _body.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = .12f; noise.strengthY = .07f; noise.strengthZ = .12f;
            noise.frequency = 2.2f; noise.scrollSpeed = .6f; noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.damping = true;
        }

        private ParticleSystem CreateParticles(string name, string material, int capacity, float life, float size, float rise)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var particles = obj.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false; main.playOnAwake = false;
            main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .7f, life * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .65f, size * 1.25f);
            main.startSpeed = 0;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            if (material == "M_ArcadiaFlameParticle")
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(size * .7f, size);
                main.startSizeY = new ParticleSystem.MinMaxCurve(size * 1.7f, size * 2.2f);
                main.startSizeZ = size;
                main.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
            }
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var force = particles.forceOverLifetime;
            force.enabled = true; force.space = ParticleSystemSimulationSpace.World; force.y = rise;
            var sizeLife = particles.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, .25f), new Keyframe(.2f, 1), new Keyframe(.6f, .8f), new Keyframe(1, 0)));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>(Folder + material);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private static void ConfigureColor(ParticleSystem particles, Color start, Color end, float alpha)
        {
            var colors = particles.colorOverLifetime;
            colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(alpha, .12f),
                    new GradientAlphaKey(alpha * .7f, .5f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
        }

        public void RenderFire(bool active, bool alive)
        {
            if (_base == null || _tip == null || _ribbon == null) return;
            if (!alive) { Clear(); return; }
            float dt = Time.deltaTime;
            Vector3 from = _base.position, to = _tip.position;
            if (!_wasActive && active)
            {
                _previousBase = from; _previousTip = to;
                _bodyBudget = _coreBudget = _emberBudget = 0;
            }
            _heat = Mathf.MoveTowards(_heat, active ? 1 : 0, dt * 8);
            _ribbon.enabled = _heat > 0;
            if (_heat > 0)
            {
                Vector3 axis = (to - from).normalized;
                Vector3 across = Vector3.Cross(axis, _camera != null ? _camera.transform.forward : Vector3.forward).normalized;
                if (across.sqrMagnitude < .1f) across = _base.right;
                float length = Vector3.Distance(from, to);
                float width = length * .36f;
                // Плотное основание следует за металлом; внешние частицы живут в мире.
                for (int i = 0; i <= Segments; i++)
                {
                    float u = i / (float)Segments;
                    Vector3 center = Vector3.Lerp(from, to, Mathf.Lerp(.12f, 1.06f, u)) + Vector3.up * .065f;
                    if (_camera != null) center -= _camera.transform.forward * .025f;
                    _vertices[i * 2] = transform.InverseTransformPoint(center - across * width * .5f);
                    _vertices[i * 2 + 1] = transform.InverseTransformPoint(center + across * width * .5f);
                }
                _mesh.vertices = _vertices;
                _mesh.RecalculateBounds();
                _block.SetFloat(HeatId, _heat);
                _ribbon.SetPropertyBlock(_block);
            }
            if (active && dt > 0)
            {
                Emit(_body, ref _bodyBudget, 290, from, to, dt, .24f);
                Emit(_core, ref _coreBudget, 140, from, to, dt, .13f);
                Emit(_embers, ref _emberBudget, 32, from, to, dt, .65f);
            }
            _previousBase = from; _previousTip = to;
            _wasActive = active;
        }

        private void Emit(ParticleSystem particles, ref float budget, float rate, Vector3 from, Vector3 to, float dt, float spread)
        {
            budget += dt * rate;
            int count = Mathf.Min(32, (int)budget);
            budget -= count;
            for (int i = 0; i < count; i++)
            {
                float along = Mathf.Lerp(.18f, .98f, Random01());
                Vector3 previous = Vector3.Lerp(_previousBase, _previousTip, along);
                Vector3 current = Vector3.Lerp(from, to, along);
                Vector3 inherited = Vector3.ClampMagnitude((current - previous) / dt, 12) * .12f;
                Vector3 drift = new Vector3(Random01() - .5f, .25f + Random01() * .4f, Random01() - .5f) * spread;
                var emit = new ParticleSystem.EmitParams
                {
                    position = Vector3.Lerp(previous, current, (i + .5f) / count),
                    velocity = Vector3.up * .35f + inherited + drift,
                    randomSeed = _random
                };
                particles.Emit(emit, 1);
            }
        }

        private float Random01()
        {
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            return (_random & 0xffffff) / 16777216f;
        }

        public void Clear()
        {
            if (_ribbon != null) _ribbon.enabled = false;
            if (_body != null) _body.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_core != null) _core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_embers != null) _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _heat = 0; _wasActive = false;
        }

        private void OnDisable() => Clear();
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
