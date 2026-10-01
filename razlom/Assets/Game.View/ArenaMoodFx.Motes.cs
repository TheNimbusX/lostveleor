using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Пыльца днём и светлячки в сумерках: редко и по краю поляны (владелец отверг «конфетти» лагеря 16.09).
    // Частицы рождаются вручную в полосе у настоящего края поляны (форма из симуляции, не эллипс), поэтому
    // гуще у кромки и над лесом, а над серединой боя их почти нет. Пыльца — мягкие точки с альфой и цветом
    // меньше 1 (без блума); светлячки — аддитивные жёлто-зелёные (не оранжевые), блум даёт им ореол.
    public sealed partial class ArenaMoodFx
    {
        private const float PollenLifetime = 8f, FireflyLifetime = 7f;

        [Header("Пыльца и светлячки")]
        public Vector2 PollenSize = new Vector2(.07f, .13f);
        [Range(0f, 1f)] public float PollenAlpha = .75f;
        [Tooltip("Светлячки, метры. С ,16–,26 в широком кадре это были точки в 4–6 px — не читались (съёмка 01.10).")]
        public Vector2 FireflySize = new Vector2(.24f, .38f);

        private static readonly int StyleId = Shader.PropertyToID("_Style");

        private ParticleSystem _pollen, _fireflies;
        private Material _pollenMaterial, _fireflyMaterial;
        private ParticleSystem.Particle[] _particleBuffer;
        private int _pollenCount, _fireflyCount;
        private float _pollenRate, _fireflyRate, _pollenDebt, _fireflyDebt;
        private Color32 _pollenColour, _fireflyColour;
        private ArenaMoodRandom _moteRandom;

        private void ShowMotes(out int pollen, out int sparks, out int fireflies)
        {
            ArenaMoodState s = _state;
            sparks = _beamCount > 0 ? _sparkCount : 0;
            _pollenCount = Mathf.Max(0, s.PollenCount - sparks);
            _fireflyCount = Mathf.Max(0, s.FireflyCount);
            _moteRandom = new ArenaMoodRandom(ArenaMoodRandom.Mix(_seed, 0x6D6F74));
            EnsureMotes();
            Color pollenColour = s.PollenColor;
            pollenColour.a = PollenAlpha;
            _pollenColour = pollenColour;
            Color fireflyColour = s.FireflyColor;
            // Цвет частицы — 8 бит на канал: HDR светлячка сжимается в его оттенок, свечение даёт аддитив и блум.
            float peak = Mathf.Max(fireflyColour.r, Mathf.Max(fireflyColour.g, fireflyColour.b));
            if (peak > 1f) fireflyColour = Scaled(fireflyColour, 1f / peak);
            fireflyColour.a = 1f;
            _fireflyColour = fireflyColour;
            pollen = StartMotes(_pollen, _pollenCount, PollenLifetime, ref _pollenRate, ref _pollenDebt, false);
            fireflies = StartMotes(_fireflies, _fireflyCount, FireflyLifetime, ref _fireflyRate, ref _fireflyDebt, true);
        }

        private int StartMotes(ParticleSystem system, int count, float lifetime, ref float rate, ref float debt, bool firefly)
        {
            debt = 0f;
            rate = 0f;
            if (system == null) return 0;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (count <= 0)
            {
                system.gameObject.SetActive(false);
                return 0;
            }
            system.gameObject.SetActive(true);
            var main = system.main;
            main.maxParticles = count + 8;
            system.useAutoRandomSeed = false;
            system.randomSeed = ArenaMoodRandom.Mix(_seed, count, firefly ? 0x666C79 : 0x706F6C);
            system.Play();
            rate = ArenaMoodFxRules.SteadyRate(count, lifetime);
            // Сразу как в середине жизни арены: столько, сколько живёт в среднем, с разным остатком жизни —
            // когда дым уйдёт, кадр уже «прожит», а не наполняется на глазах.
            for (int i = 0; i < count; i++) EmitMote(system, firefly);
            if (system.particleCount > _particleBuffer.Length) return count;
            int alive = system.GetParticles(_particleBuffer);
            for (int i = 0; i < alive; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];
                particle.remainingLifetime = particle.startLifetime * _moteRandom.Range(.08f, 1f);
                _particleBuffer[i] = particle;
            }
            system.SetParticles(_particleBuffer, alive);
            return count;
        }

        private void TickMotes(float delta)
        {
            if (!(delta > 0f)) return;
            if (_pollen != null && _pollenRate > 0f) Spawn(_pollen, _pollenRate, ref _pollenDebt, false, delta);
            if (_fireflies != null && _fireflyRate > 0f) Spawn(_fireflies, _fireflyRate, ref _fireflyDebt, true, delta);
        }

        private void Spawn(ParticleSystem system, float rate, ref float debt, bool firefly, float delta)
        {
            debt += rate * delta;
            // Не больше четырёх за кадр: после долгой паузы кадра — без вспышки частиц.
            int births = Mathf.Min(4, (int)debt);
            debt -= births;
            if (debt > 4f) debt = 4f;
            for (int i = 0; i < births; i++) EmitMote(system, firefly);
        }

        private void EmitMote(ParticleSystem system, bool firefly)
        {
            ArenaMoodBand band = firefly ? ArenaMoodFxRules.FireflyBand : ArenaMoodFxRules.PollenBand;
            float angle = _moteRandom.Next01() * Mathf.PI * 2f;
            float edge = ArenaMood.ContourRadius(angle);
            float radius = ArenaMoodFxRules.BandRadius(edge, band, _moteRandom.Next01());
            float height = ArenaMoodFxRules.BandHeight(band, _moteRandom.Next01());
            Vector3 centre = ArenaMood.GladeCenter;
            var position = new Vector3(centre.x + Mathf.Cos(angle) * radius, height, centre.z + Mathf.Sin(angle) * radius);
            Vector2 size = firefly ? FireflySize : PollenSize;
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = false,
                velocity = firefly
                    ? new Vector3(_moteRandom.Range(-.06f, .06f), _moteRandom.Range(-.03f, .05f), _moteRandom.Range(-.06f, .06f))
                    : new Vector3(_moteRandom.Range(-.08f, .08f), _moteRandom.Range(-.06f, -.02f), _moteRandom.Range(-.08f, .08f)),
                startSize = _moteRandom.Range(size.x, size.y),
                startLifetime = (firefly ? FireflyLifetime : PollenLifetime) * _moteRandom.Range(.7f, 1.3f),
                startColor = firefly ? _fireflyColour : _pollenColour,
            };
            system.Emit(emit, 1);
        }

        private void EnsureMotes()
        {
            if (_particleBuffer == null) _particleBuffer = new ParticleSystem.Particle[160];
            if (_pollen == null)
            {
                // Мягкий кружок с альфой (шейдер частиц биомов лагеря): цвет меньше 1 — без блума.
                _pollenMaterial = LoadShaderMaterial("Shaders/CampBiomeParticles", "Свет арены: пыльца");
                if (_pollenMaterial != null)
                    _pollen = CreateMotes("Пыльца", _pollenMaterial, .08f, .5f,
                        FadeGradient(new[] { 0f, .2f, .8f, 1f }, new[] { 0f, 1f, 1f, 0f }));
            }
            if (_fireflies == null)
            {
                // Аддитивный мягкий огонёк (шейдер огоньков магии лагеря, форма 1 — кружок без лучей).
                _fireflyMaterial = LoadShaderMaterial("Shaders/CampMagicMotes", "Свет арены: светлячки");
                if (_fireflyMaterial != null)
                {
                    _fireflyMaterial.SetFloat(StyleId, 1f);
                    // Мигание: то гаснет, то вспыхивает за время жизни.
                    _fireflies = CreateMotes("Светлячки", _fireflyMaterial, .3f, .55f,
                        FadeGradient(new[] { 0f, .14f, .32f, .5f, .66f, .84f, 1f }, new[] { 0f, .95f, .12f, .9f, .2f, .85f, 0f }));
                }
            }
        }

        private ParticleSystem CreateMotes(string name, Material material, float noiseStrength, float noiseFrequency, Gradient fade)
        {
            var host = new GameObject(name);
            host.transform.SetParent(_root, false);
            host.SetActive(false);
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.useUnscaledTime = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = noiseStrength;
            noise.frequency = noiseFrequency;
            noise.scrollSpeed = .25f;
            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return particles;
        }

        private void HideMotes()
        {
            _pollenRate = _fireflyRate = 0f;
            if (_pollen != null)
            {
                _pollen.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _pollen.gameObject.SetActive(false);
            }
            if (_fireflies != null)
            {
                _fireflies.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _fireflies.gameObject.SetActive(false);
            }
        }

        private void DestroyMotes()
        {
            DestroyOwned(_pollenMaterial);
            DestroyOwned(_fireflyMaterial);
            _pollenMaterial = _fireflyMaterial = null;
            _pollen = _fireflies = null;
        }
    }
}
