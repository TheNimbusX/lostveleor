using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.View
{
    /// <summary>
    /// Мелкая жизнь лагеря: бабочки над травой, листья, слетающие с крон, и
    /// светлячки у воды. Всё — нативные частицы, созданные в игре под этим
    /// компонентом: авторская сцена не меняется, коллизий нет.
    ///
    /// Привязки берутся из самой сцены, а не из имён объектов: река — по контуру
    /// <see cref="CampRiver"/>, кроны — по высоким мешам вокруг лагеря
    /// (круг школ с микробиомами владелец убрал, старые якоря не годятся).
    /// Ветер двигает листья и бабочек через <see cref="CampAmbience"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Живность")]
    public sealed class CampWildlife : MonoBehaviour
    {
        [Tooltip("Ветер: направление и сила сноса листьев и бабочек")]
        public CampAmbience Ambience;
        [Tooltip("Река: вдоль неё светлячки. Пусто — ищется в сцене")]
        public CampRiver River;
        [Tooltip("Размер площадки лагеря, метры")]
        public Vector2 Area = new Vector2(16f, 16f);

        // 16 сентября: первая проба дала сорок одинаковых жёлтых точек — конфетти.
        // Теперь каждой живности свой цвет, высота и редкость, чтобы читалось раздельно.
        // Владелец 16 сентября: «убери листву и бабочек, выглядит плохо». Системы оставлены
        // выключенными — включаются флажком в инспекторе, если решим вернуться.
        [Header("Бабочки — выключены по решению владельца")]
        public bool Butterflies;
        [Range(0, 60)] public int ButterflyLimit = 8;
        [Range(0f, 10f)] public float ButterflyRate = 1.1f;
        [Tooltip("Камера лагеря стоит далеко: мелкие частицы занимают меньше пикселя и не видны")]
        public Vector2 ButterflySize = new Vector2(.28f, .42f);
        public Vector2 ButterflyHeight = new Vector2(.3f, .9f);
        public Color ButterflyColour = new Color(1f, .97f, .88f, 1f);

        [Header("Падающие листья — выключены по решению владельца")]
        public bool Leaves;
        [Range(0, 120)] public int LeafLimit = 18;
        [Range(0f, 20f)] public float LeafRate = 2.2f;
        public Vector2 LeafSize = new Vector2(.3f, .5f);
        public Color LeafColour = new Color(.86f, .48f, .18f, 1f);
        [Tooltip("С какой высоты слетают листья, если кроны не нашлись")]
        public float LeafHeight = 5.5f;

        [Header("Светлячки — только у воды")]
        public bool Fireflies = true;
        [Tooltip("Сколько точек вдоль реки занять")]
        [Range(0, 8)] public int FireflySpots = 3;
        [Range(0, 60)] public int FireflyLimit = 14;
        public Vector2 FireflySize = new Vector2(.12f, .2f);
        public Color FireflyColour = new Color(.92f, 1f, .5f, 1f);

        readonly List<ParticleSystem> _systems = new List<ParticleSystem>();
        ParticleSystem _leaves, _butterflies;
        Material _butterflyMaterial, _leafMaterial, _glow;
        bool _built;

        void OnEnable()
        {
            if (!_built) { Build(); _built = true; }
            foreach (ParticleSystem system in _systems) if (system != null) system.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            foreach (ParticleSystem system in _systems) if (system != null) system.gameObject.SetActive(false);
        }

        void Build()
        {
            Shader soft = Resources.Load<Shader>("Shaders/CampBiomeParticles");
            Shader glow = Resources.Load<Shader>("Shaders/CampRiftMotes");
            if (glow != null) _glow = new Material(glow) { hideFlags = HideFlags.HideAndDontSave };
            // Без текстуры шейдер рисует мягкий круг: 16 сентября бабочки и листья
            // вышли белыми и оранжевыми шарами. Силуэты лежат рядом в Resources.
            _butterflyMaterial = Silhouette(soft, "butterfly");
            _leafMaterial = Silhouette(soft, "leaf");

            if (Butterflies && _butterflyMaterial != null) _butterflies = BuildButterflies();
            if (Leaves && _leafMaterial != null) _leaves = BuildLeaves();
            if (Fireflies && _glow != null) BuildFireflies();
        }

        /// <summary>Материал частицы с маской-силуэтом: шейдер берёт форму из красного канала.</summary>
        static Material Silhouette(Shader shader, string name)
        {
            if (shader == null) return null;
            var texture = Resources.Load<Texture2D>("Environment/Camp/Wildlife/" + name);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetFloat("_Textured", 1f);
            }
            return material;
        }

        ParticleSystem BuildButterflies()
        {
            ParticleSystem particles = Emitter("Бабочки", new Vector3(0f, Mathf.Lerp(ButterflyHeight.x, ButterflyHeight.y, .5f), 0f), _butterflyMaterial);
            var main = particles.main;
            main.maxParticles = ButterflyLimit;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(ButterflySize.x, ButterflySize.y);
            main.startColor = ButterflyColour;
            var emission = particles.emission;
            emission.rateOverTime = ButterflyRate;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Area.x, Mathf.Max(.1f, ButterflyHeight.y - ButterflyHeight.x), Area.y);
            // Порхание: сильный высокочастотный шум и почти нулевая собственная скорость.
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .55f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = .9f;
            noise.damping = false;
            Fade(particles, ButterflyColour, .18f);
            particles.Play();
            return particles;
        }

        ParticleSystem BuildLeaves()
        {
            float height = CanopyHeight();
            ParticleSystem particles = Emitter("Падающие листья", new Vector3(0f, height, 0f), _leafMaterial);
            var main = particles.main;
            main.maxParticles = LeafLimit;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(LeafSize.x, LeafSize.y);
            main.startColor = LeafColour;
            main.gravityModifier = .035f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = particles.emission;
            emission.rateOverTime = LeafRate;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Area.x * .9f, .6f, Area.y * .9f);
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .18f;
            noise.frequency = .5f;
            noise.scrollSpeed = .3f;
            Fade(particles, LeafColour, .12f);
            particles.Play();
            return particles;
        }

        void BuildFireflies()
        {
            Vector3[] spots = RiverSpots();
            for (int i = 0; i < spots.Length; i++)
            {
                ParticleSystem particles = Emitter("Светлячки " + (i + 1), transform.InverseTransformPoint(spots[i]) + Vector3.up * .35f, _glow);
                var main = particles.main;
                main.maxParticles = FireflyLimit;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(FireflySize.x, FireflySize.y);
                main.startColor = FireflyColour;
                var emission = particles.emission;
                emission.rateOverTime = 3f;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(3.4f, .7f, 3.4f);
                var noise = particles.noise;
                noise.enabled = true;
                noise.strength = .22f;
                noise.frequency = .8f;
                noise.scrollSpeed = .4f;
                // Мигание: прозрачность то гаснет, то вспыхивает за время жизни.
                var colour = particles.colorOverLifetime;
                colour.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(FireflyColour, 0f), new GradientColorKey(FireflyColour, 1f) },
                    new[]
                    {
                        new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.95f, .18f), new GradientAlphaKey(.15f, .38f),
                        new GradientAlphaKey(.9f, .6f), new GradientAlphaKey(.2f, .78f), new GradientAlphaKey(0f, 1f)
                    });
                colour.color = gradient;
                particles.Play();
            }
        }

        /// <summary>Высота крон: берём верх самых высоких мешей вокруг лагеря, иначе запасное число.</summary>
        float CanopyHeight()
        {
            float best = 0f;
            foreach (MeshRenderer renderer in FindTreeRenderers())
            {
                float top = renderer.bounds.max.y - transform.position.y;
                if (top > best && top < 24f) best = top;
            }
            return best > 2.5f ? best * .85f : LeafHeight;
        }

        List<MeshRenderer> FindTreeRenderers()
        {
            var found = new List<MeshRenderer>();
            Transform root = transform.parent != null ? transform.parent : transform;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                Bounds bounds = renderer.bounds;
                if (bounds.size.y < 3f) continue;
                Vector3 delta = bounds.center - transform.position;
                delta.y = 0f;
                if (delta.magnitude > Mathf.Max(Area.x, Area.y)) continue;
                found.Add(renderer);
            }
            return found;
        }

        Vector3[] RiverSpots()
        {
            CampRiver river = River != null ? River : FindAnyObjectByType<CampRiver>();
            if (river == null || FireflySpots <= 0) return System.Array.Empty<Vector3>();
            Vector2[] points = river.HasBakedShape ? river.BakedCentres : river.Contour;
            if (points == null || points.Length == 0) return System.Array.Empty<Vector3>();
            var spots = new List<Vector3>();
            int step = Mathf.Max(1, points.Length / FireflySpots);
            for (int i = 0; i < points.Length && spots.Count < FireflySpots; i += step)
            {
                Vector3 at = river.transform.TransformPoint(new Vector3(points[i].x, 0f, points[i].y));
                Vector3 delta = at - transform.position;
                delta.y = 0f;
                // Берём только те точки реки, что рядом с лагерем: дальний берег в кадр не попадает.
                if (delta.magnitude < Mathf.Max(Area.x, Area.y) * 1.3f) spots.Add(at);
            }
            return spots.ToArray();
        }

        ParticleSystem Emitter(string name, Vector3 localPosition, Material material)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = localPosition;
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.useUnscaledTime = true;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _systems.Add(particles);
            return particles;
        }

        static void Fade(ParticleSystem particles, Color colour, float edge)
        {
            var over = particles.colorOverLifetime;
            over.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, edge), new GradientAlphaKey(1f, 1f - edge), new GradientAlphaKey(0f, 1f) });
            over.color = gradient;
        }

        void LateUpdate()
        {
            if (Ambience == null) return;
            // Листья и бабочки сносит тем же ветром, что качает листву: порыв виден и в них.
            float angle = Ambience.BreezeDirection * Mathf.Deg2Rad;
            float strength = Ambience.BreezeStrength;
            Vector3 wind = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * strength;
            Drift(_leaves, wind * .45f, -.32f);
            Drift(_butterflies, wind * .12f, 0f);
        }

        static void Drift(ParticleSystem particles, Vector3 wind, float fall)
        {
            if (particles == null) return;
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x * .6f, wind.x * 1.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(fall * 1.3f, fall * .7f);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z * .6f, wind.z * 1.4f);
        }

        void OnDestroy()
        {
            if (_butterflyMaterial != null) Destroy(_butterflyMaterial);
            if (_leafMaterial != null) Destroy(_leafMaterial);
            if (_glow != null) Destroy(_glow);
        }
    }
}
