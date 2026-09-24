using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Косые лучи закатного солнца над лагерем: несколько объёмных полос, наклонённых
    /// по солнцу. Объём считает шейдер «Game/Camp Rift Glow» — тот же, что у лучей арки:
    /// он интегрирует свет внутри меша и обрезает его глубиной сцены, поэтому луч
    /// прячется за палатками и деревьями, а не лежит поверх картинки.
    ///
    /// Объекты создаются в игре под этим компонентом: авторская сцена не меняется.
    /// Числа правятся в инспекторе, положение — коробкой <see cref="AreaSize"/>
    /// относительно самого компонента.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Лучи солнца")]
    public sealed class CampSunbeams : MonoBehaviour
    {
        [Tooltip("Солнце: по нему считается наклон лучей. Пусто — самый яркий направленный свет сцены")]
        public Light Sun;
        [Range(0, 24)] public int Count = 9;
        [Tooltip("Коробка, внутри которой стоят лучи, в метрах")]
        public Vector3 AreaSize = new Vector3(14f, 0f, 14f);
        [Tooltip("Высота верхней точки луча над компонентом. Высоко поднятый луч не перекрывается " +
                 "постройками и лежит поверх картинки жёлтой полосой")]
        public float Height = 4.5f;
        [Tooltip("Длина и толщина полосы, метры. Длинный луч при взгляде сверху тянется через весь лагерь")]
        public float Length = 6f;
        public Vector2 Width = new Vector2(.7f, 1.5f);
        [Tooltip("Ближе к белому: насыщенный жёлтый читается как краска, а не как свет")]
        public Color Colour = new Color(1f, .87f, .72f, 1f);
        // Аудит 23 сентября: полосы шли через весь кадр и лезли на героя — вдвое слабее и шире (мягче край).
        [Range(0f, 3f)] public float Intensity = .1f;
        [Tooltip("Скорость мерцания внутри луча")]
        [Range(0f, 2f)] public float FlowSpeed = .35f;
        [Tooltip("Зерно расстановки: одно и то же зерно даёт одну и ту же картину")]
        public int Seed = 20260916;

        [Header("Пылинки в воздухе")]
        public bool Motes = true;
        [Range(0, 300)] public int MoteLimit = 140;
        [Tooltip("Сколько пылинок рождается в секунду")]
        [Range(0f, 60f)] public float MoteRate = 26f;
        [Tooltip("Камера лагеря стоит далеко: пылинки мельче 8 см на общем плане меньше пикселя и не видны")]
        public Vector2 MoteSize = new Vector2(.09f, .2f);
        public Color MoteColour = new Color(1f, .92f, .74f, 1f);

        Material _material;
        Mesh _mesh;
        Transform[] _beams;
        MeshRenderer[] _renderers;
        MaterialPropertyBlock[] _blocks;
        float _started;

        GameObject _motes;

        void OnEnable()
        {
            _started = Time.unscaledTime;
            if (_beams != null)
            {
                foreach (Transform beam in _beams) if (beam != null) beam.gameObject.SetActive(true);
                if (_motes != null) _motes.SetActive(true);
                return;
            }
            Build();
        }

        void OnDisable()
        {
            if (_motes != null) _motes.SetActive(false);
            if (_beams == null) return;
            foreach (Transform beam in _beams) if (beam != null) beam.gameObject.SetActive(false);
        }

        /// <summary>
        /// Пылинки в воздухе: тот же аддитивный шейдер, что у светлячков арки.
        /// Медленно оседают и сносятся ветром — в лучах их видно, вне лучей почти нет.
        /// </summary>
        void BuildMotes()
        {
            Shader shader = Resources.Load<Shader>("Shaders/CampRiftMotes");
            if (shader == null) return;
            _motes = new GameObject("Пылинки в воздухе");
            _motes.transform.SetParent(transform, false);
            _motes.transform.localPosition = new Vector3(0f, Height * .5f, 0f);
            var particles = _motes.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.useUnscaledTime = true;
            main.maxParticles = MoteLimit;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(MoteSize.x, MoteSize.y);
            main.startColor = MoteColour;
            var emission = particles.emission;
            emission.rateOverTime = MoteRate;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(AreaSize.x, Height, AreaSize.z);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(.05f, .16f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.09f, -.02f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.06f, .06f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .05f;
            noise.frequency = .6f;
            noise.scrollSpeed = .2f;
            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(MoteColour, 0f), new GradientColorKey(MoteColour, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.9f, .25f), new GradientAlphaKey(.7f, .7f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var renderer = _motes.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
        }

        void Build()
        {
            Shader shader = Shader.Find("Game/Camp Rift Glow");
            if (shader == null) return;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetColor("_Color", Colour);
            _mesh = BeamMesh();

            var random = new System.Random(Seed);
            float Range(float from, float to) => from + (float)random.NextDouble() * (to - from);

            _beams = new Transform[Count];
            _renderers = new MeshRenderer[Count];
            _blocks = new MaterialPropertyBlock[Count];
            Vector3 direction = SunDirection();
            for (int i = 0; i < Count; i++)
            {
                var beam = new GameObject("Луч солнца " + (i + 1));
                beam.transform.SetParent(transform, false);
                beam.transform.localPosition = new Vector3(
                    Range(-AreaSize.x, AreaSize.x) * .5f,
                    Height + Range(-AreaSize.y, AreaSize.y) * .5f,
                    Range(-AreaSize.z, AreaSize.z) * .5f);
                // Меш вытянут по своей оси Y, поэтому «вверх» луча смотрит вдоль света.
                beam.transform.rotation = Quaternion.FromToRotation(Vector3.up, -direction);
                float width = Range(Width.x, Width.y);
                beam.transform.localScale = new Vector3(width, Length * Range(.8f, 1.2f), width);
                beam.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = beam.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _beams[i] = beam.transform;
                _renderers[i] = renderer;
                _blocks[i] = new MaterialPropertyBlock();
            }
            if (Motes) BuildMotes();
        }

        Vector3 SunDirection()
        {
            Light sun = Sun;
            if (sun == null)
                foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.isActiveAndEnabled && light.type == LightType.Directional && (sun == null || light.intensity > sun.intensity)) sun = light;
            return sun != null ? sun.transform.forward : Quaternion.Euler(25f, 35f, 0f) * Vector3.forward;
        }

        /// <summary>Куб единичного размера: шейдер интегрирует свет внутри него, сама поверхность не рисуется.</summary>
        static Mesh BeamMesh()
        {
            var mesh = new Mesh { name = "Объём луча солнца" };
            mesh.vertices = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            mesh.triangles = new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        void LateUpdate()
        {
            if (_material == null || _beams == null) return;
            // Перекомпиляция в Play сохраняет массив лучей, но не блоки свойств: восстановить.
            if (_blocks == null || _blocks.Length != _beams.Length || _renderers == null || _renderers.Length != _beams.Length)
            {
                _blocks = new MaterialPropertyBlock[_beams.Length];
                _renderers = new MeshRenderer[_beams.Length];
                for (int i = 0; i < _beams.Length; i++)
                {
                    _blocks[i] = new MaterialPropertyBlock();
                    if (_beams[i] != null) _renderers[i] = _beams[i].GetComponent<MeshRenderer>();
                }
            }
            float time = (Time.unscaledTime - _started) * FlowSpeed;
            _material.SetColor("_Color", Colour);
            _material.SetFloat("_FlowTime", time);
            Vector3 direction = SunDirection();
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null || _renderers[i] == null) continue;
                _beams[i].rotation = Quaternion.FromToRotation(Vector3.up, -direction);
                float phase = i * 2.399963f;
                _blocks[i].SetFloat("_Phase", phase);
                // Лучи дышат вразнобой: одинаковая яркость читается как декорация, а не как свет.
                _blocks[i].SetFloat("_Intensity", Intensity * (.75f + .25f * Mathf.Sin(time * 1.3f + phase)));
                _renderers[i].SetPropertyBlock(_blocks[i]);
            }
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
