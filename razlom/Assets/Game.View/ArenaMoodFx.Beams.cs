using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Лучи в тумане: два-три узких луча сверху-справа, как на рефе «туман сгущается», с медленными искрами.
    // Объём считает шейдер лучей лагеря «Game/Camp Rift Glow» (обрезается глубиной сцены: кроны его режут).
    //
    // Луч держится за камерой, а не за землёй: поляна шире кадра (камера видит ~17 м в глубину, поляна —
    // 20–35 м), луч на дальней кромке почти всё время был бы за кадром. Солнце при этом не поворачивается
    // (решение владельца не принято): настоящее солнце светит почти вдоль взгляда камеры, и луч по нему вышел
    // бы короткой горизонтальной кляксой — поэтому направление лучей задано по кадру, сверху-справа.
    // Низ луча гаснет выше середины кадра: по аудиту 23.09 лучи не ложатся на героя и бой.
    public sealed partial class ArenaMoodFx
    {
        private const int MaxBeams = 4;
        private const float BeamTopHeight = 10f, BeamBottomHeight = .5f, BeamFlowSpeed = .35f;

        [Header("Лучи в тумане")]
        [Tooltip("Ближе к белому: насыщенный жёлтый читается как краска, а не как свет.")]
        public Color BeamColour = new Color(1f, .93f, .78f, 1f);
        [Tooltip("Ширина лучей — множитель к 1–1,8 м раскладки. На рефе тумана лучи — широкие мягкие столбы; " +
                 "при ширине 1 они читались тонкими штрихами (съёмка 01.10).")]
        [Range(.5f, 4f)] public float BeamWidthScale = 2.2f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int FlowTimeId = Shader.PropertyToID("_FlowTime");

        private Transform _beamRig;
        private readonly Transform[] _beams = new Transform[MaxBeams];
        private readonly MeshRenderer[] _beamRenderers = new MeshRenderer[MaxBeams];
        private readonly ParticleSystem[] _sparks = new ParticleSystem[MaxBeams];
        private MaterialPropertyBlock _beamBlock;
        private Material _beamMaterial, _sparkMaterial;
        private Mesh _beamMesh;
        private int _beamCount, _sparkCount, _beamScreenW, _beamScreenH;
        private float _beamIntensity;
        private Vector3 _beamCameraBase;
        private bool _beamFollowing;

        private int ShowBeams()
        {
            ArenaMoodState s = _state;
            _beamCount = Mathf.Clamp(s.BeamCount, 0, MaxBeams);
            _beamIntensity = Mathf.Max(0f, s.BeamIntensity);
            if (_beamCount == 0 || _beamIntensity <= .001f || _camera == null || !EnsureBeams())
            {
                _beamCount = 0;
                return 0;
            }
            _sparkCount = ArenaMoodFxRules.SparkCount(s.Weights, s.PollenCount, _beamCount);
            _beamMaterial.SetColor(ColourId, BeamColour);
            LayoutBeams();
            _beamRig.gameObject.SetActive(true);
            FollowCamera(true);
            for (int i = 0; i < MaxBeams; i++)
            {
                bool on = i < _beamCount;
                _beams[i].gameObject.SetActive(on);
                ParticleSystem sparks = _sparks[i];
                if (sparks == null) continue;
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                int share = on ? SparkShare(i) : 0;
                sparks.gameObject.SetActive(share > 0);
                if (share <= 0) continue;
                ConfigureSparks(sparks, i, share);
                sparks.Play();
            }
            return _beamCount;
        }

        private int SparkShare(int index)
        {
            if (_beamCount <= 0 || _sparkCount <= 0) return 0;
            int share = _sparkCount / _beamCount;
            return share + (index < _sparkCount % _beamCount ? 1 : 0);
        }

        /// <summary>Лучи по местам в кадре: верх выше края кадра на высоте крон, низ гаснет выше середины.</summary>
        private void LayoutBeams()
        {
            _beamRig.position = Vector3.zero;
            _beamCameraBase = _camera.transform.position;
            _beamScreenW = Screen.width;
            _beamScreenH = Screen.height;
            for (int i = 0; i < _beamCount; i++)
            {
                ArenaMoodBeamSlot slot = ArenaMoodFxRules.BeamLayout(i, _beamCount, _seed);
                Vector3 top = ViewportOnHeight(slot.TopX, slot.TopY, BeamTopHeight);
                Vector3 bottom = ViewportOnHeight(slot.BottomX, slot.BottomY, BeamBottomHeight);
                Vector3 axis = bottom - top;
                float length = axis.magnitude;
                if (length < .5f) axis = Vector3.down * (length = .5f);
                Transform beam = _beams[i];
                // Ось Y меша — вдоль света сверху вниз: шейдер ярче у верха и гаснет к низу, луч шире книзу.
                beam.SetPositionAndRotation((top + bottom) * .5f, Quaternion.FromToRotation(Vector3.up, axis / length));
                float width = slot.Width * Mathf.Max(.1f, BeamWidthScale);
                beam.localScale = new Vector3(width, length, width);
                ParticleSystem sparks = _sparks[i];
                if (sparks != null)
                {
                    // Искры — в верхней, яркой половине луча; система без масштаба луча.
                    sparks.transform.SetPositionAndRotation(beam.position - beam.up * (length * .12f), beam.rotation);
                    sparks.transform.localScale = Vector3.one;
                    var shape = sparks.shape;
                    shape.scale = new Vector3(width * .45f, length * .4f, width * .45f);
                }
            }
        }

        /// <summary>Точка мира на высоте <paramref name="height"/>, видимая в доле кадра (x, y).</summary>
        private Vector3 ViewportOnHeight(float x, float y, float height)
        {
            Ray ray = _camera.ViewportPointToRay(new Vector3(x, y, 0f));
            float dy = ray.direction.y;
            if (Mathf.Abs(dy) < 1e-4f) return ray.origin;
            return ray.origin + ray.direction * ((height - ray.origin.y) / dy);
        }

        private void TickBeams(float time)
        {
            if (_beamCount == 0 || _beamRig == null || _camera == null) return;
            // Окно сменило размер — места лучей в кадре пересчитать.
            if (Screen.width != _beamScreenW || Screen.height != _beamScreenH) LayoutBeams();
            float flow = time * BeamFlowSpeed;
            _beamMaterial.SetFloat(FlowTimeId, flow);
            for (int i = 0; i < _beamCount; i++)
            {
                MeshRenderer renderer = _beamRenderers[i];
                if (renderer == null) continue;
                float phase = i * 2.399963f + (_seed & 0xFF) * .01f;
                _beamBlock.SetFloat(PhaseId, phase);
                // Лучи дышат вразнобой: одинаковая яркость читается как декорация, а не как свет.
                _beamBlock.SetFloat(IntensityId, _beamIntensity * ArenaMoodFxRules.BeamBreath(flow, phase));
                renderer.SetPropertyBlock(_beamBlock);
            }
        }

        /// <summary>
        /// Лучи идут за камерой: в кадре стоят на месте (ортокамера), кроны под ними проезжают. Сдвиг — прямо
        /// перед отрисовкой кадра камерой: камера едет за героем позже этого компонента (CameraFollow, порядок
        /// 1000) и трясётся от ударов, а луч, отстающий на кадр, дрожал бы в кадре.
        /// </summary>
        private void FollowCamera(bool on)
        {
            if (on == _beamFollowing) return;
            _beamFollowing = on;
            if (on) RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            else RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_beamCount == 0 || _beamRig == null || camera != _camera) return;
            _beamRig.position = camera.transform.position - _beamCameraBase;
        }

        private bool EnsureBeams()
        {
            if (_beamRig != null && _beamMaterial != null) return true;
            if (_beamMaterial == null)
            {
                // Шейдер лучей попадает в сборку через материал лагеря в Resources.
                var source = Resources.Load<Material>("Environment/Camp/M_CampRiftGlow");
                Shader shader = source != null ? source.shader : Shader.Find("Game/Camp Rift Glow");
                if (shader == null) return false;
                _beamMaterial = new Material(shader) { name = "Свет арены: лучи", hideFlags = HideFlags.DontSave };
            }
            if (_sparkMaterial == null)
            {
                _sparkMaterial = LoadShaderMaterial("Shaders/CampMagicMotes", "Свет арены: искры в лучах");
                if (_sparkMaterial != null) _sparkMaterial.SetFloat(StyleId, 1f);
            }
            _beamBlock = new MaterialPropertyBlock();
            _beamMesh = BeamMesh();
            var rig = new GameObject("Лучи в тумане");
            rig.transform.SetParent(_root, false);
            _beamRig = rig.transform;
            for (int i = 0; i < MaxBeams; i++)
            {
                var beam = new GameObject("Луч " + (i + 1));
                beam.transform.SetParent(_beamRig, false);
                beam.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
                var renderer = beam.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _beamMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                beam.SetActive(false);
                _beams[i] = beam.transform;
                _beamRenderers[i] = renderer;
                if (_sparkMaterial != null) _sparks[i] = CreateSparks(i);
            }
            rig.SetActive(false);
            return true;
        }

        private ParticleSystem CreateSparks(int index)
        {
            var host = new GameObject("Искры в луче " + (index + 1));
            host.transform.SetParent(_beamRig, false);
            host.SetActive(false);
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 6f;
            main.prewarm = true;
            main.useUnscaledTime = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.05f, .09f);
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Медленно плывут вниз вдоль луча и чуть вбок.
            velocity.x = new ParticleSystem.MinMaxCurve(-.04f, .04f);
            velocity.y = new ParticleSystem.MinMaxCurve(.03f, .1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.04f, .04f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .06f;
            noise.frequency = .6f;
            noise.scrollSpeed = .2f;
            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = FadeGradient(new[] { 0f, .25f, .7f, 1f }, new[] { 0f, .85f, .6f, 0f });
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return particles;
        }

        private void ConfigureSparks(ParticleSystem sparks, int index, int count)
        {
            var main = sparks.main;
            main.maxParticles = count + 4;
            // Бледное золото пыльцы настроения, не оранжевое; меньше 1 — без блума.
            Color colour = _state.PollenColor;
            colour.a = 1f;
            main.startColor = colour;
            var emission = sparks.emission;
            emission.enabled = true;
            emission.rateOverTime = ArenaMoodFxRules.SteadyRate(count, 5.5f);
            sparks.useAutoRandomSeed = false;
            sparks.randomSeed = ArenaMoodRandom.Mix(_seed, index, 0x737061);
        }

        private void HideBeams()
        {
            FollowCamera(false);
            if (_beamRig == null) return;
            for (int i = 0; i < MaxBeams; i++)
                if (_sparks[i] != null) _sparks[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _beamRig.gameObject.SetActive(false);
            _beamCount = 0;
        }

        private void DestroyBeams()
        {
            FollowCamera(false);
            DestroyOwned(_beamMaterial);
            DestroyOwned(_sparkMaterial);
            DestroyOwned(_beamMesh);
            _beamMaterial = _sparkMaterial = null;
            _beamMesh = null;
            _beamRig = null;
        }

        /// <summary>Куб единичного размера: шейдер интегрирует свет внутри него, сама поверхность не рисуется.</summary>
        private static Mesh BeamMesh()
        {
            var mesh = new Mesh { name = "Свет арены: объём луча", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f),
                new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Белый градиент с заданной прозрачностью по времени жизни (мигание, появление, угасание).</summary>
        private static Gradient FadeGradient(float[] times, float[] alphas)
        {
            var keys = new GradientAlphaKey[times.Length];
            for (int i = 0; i < times.Length; i++) keys[i] = new GradientAlphaKey(alphas[i], times[i]);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, keys);
            return gradient;
        }
    }
}
