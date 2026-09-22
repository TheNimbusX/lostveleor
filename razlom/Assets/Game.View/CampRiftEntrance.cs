using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    [DisallowMultipleComponent]
    public sealed class CampRiftEntrance : MonoBehaviour
    {
        [Header("Зона входа — относительно арки")]
        public Vector3 TriggerCenter = new Vector3(0, .2f, 0);
        public Vector3 TriggerSize = new Vector3(.38f, .6f, .3f);
        public Material GlowMaterial;
        public Color GlowColor = new Color(1f, .46f, .075f, 1f);
        public Vector3 GlowOffset = new Vector3(0, 0, .10f);
        [Header("Объёмные лучи")]
        [Range(.2f, 3f)] public float GlowIntensity = 2.2f;
        public bool IsOpen { get; private set; }
        public static int ClosedFrame { get; private set; } = -1;
        bool _armed = true;
        TickDriver _driver;
        Material _material;
        Mesh _mesh;
        const int BeamCount = 18;
        readonly Transform[] _beams = new Transform[BeamCount];
        readonly Vector3[] _beamPositions = new Vector3[BeamCount];
        readonly Vector3[] _beamScales = new Vector3[BeamCount];
        readonly MaterialPropertyBlock[] _beamBlocks = new MaterialPropertyBlock[BeamCount];
        readonly MeshRenderer[] _beamRenderers = new MeshRenderer[BeamCount];
        Material _moteMaterial;
        Material _outlineMaterial;
        readonly System.Collections.Generic.List<MeshRenderer> _outlineRenderers = new System.Collections.Generic.List<MeshRenderer>();
        BoxCollider _barrier;
        float _glowStarted;
        public bool Contains(Vector3 position) => new Bounds(TriggerCenter, TriggerSize).Contains(transform.InverseTransformPoint(position));
        internal void BuildNavigationBarrier()
        {
            if (_barrier != null) return;
            // Вопрос возникает перед преградой. Продолжение пути возможно только через смену режима.
            var barrier = new GameObject("Граница лагеря за аркой");
            barrier.transform.SetParent(transform, false);
            _barrier = barrier.AddComponent<BoxCollider>();
            _barrier.center = TriggerCenter + new Vector3(0,0,TriggerSize.z * .5f + .045f);
            _barrier.size = new Vector3(TriggerSize.x + .08f, TriggerSize.y, .05f);
        }
        public void Check(TickDriver driver, Vector3 position)
        {
            _driver = driver;
            bool inside = Contains(position);
            if (!inside) _armed = true;
            if (inside && _armed && !IsOpen && !driver.GameplayPaused)
            {
                _armed = false; IsOpen = true;
                // Арка просыпается, пока игрок решает, входить ли.
                GameSound.Play("rift_awaken", .5f, .02f, 2f);
                driver.ClearCapturedInput();
            }
            if (!IsOpen) return;
#if ENABLE_INPUT_SYSTEM
            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            bool cancel = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (cancel) Respond(false);
        }
        public void Respond(bool enter)
        {
            if (!IsOpen) return;
            IsOpen = false; ClosedFrame = Time.frameCount;
            _driver.ClearCapturedInput();
            if (enter) { GameSound.Sequence(("rift_whoosh", 0f, .75f), ("rift_portal", .08f, .9f)); _driver.Session.EnterRift(); }
        }
        void Start()
        {
            BuildOutline();
            if (GlowMaterial == null) return;
            _glowStarted = Time.unscaledTime;
            _material = new Material(GlowMaterial); _material.SetColor("_Color", GlowColor);
            _material.SetFloat("_Intensity", GlowIntensity);
            // Меш ограничивает область интегрирования света, а не рисует поверхность.
            // Потоки выходят из глубины арки к герою, оставаясь объёмными с любого ракурса.
            _mesh = new Mesh { name = "Объём луча арки" };
            _mesh.vertices = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            _mesh.triangles = new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5 };
            _mesh.RecalculateBounds();
            for (int i = 0; i < BeamCount; i++)
            {
                float length = .23f + .055f * Mathf.Sin(i * 2.39f + 1);
                float width = .047f + .012f * Mathf.Sin(i * 3.1f);
                var beam = new GameObject("Мягкий луч арки " + (i + 1));
                beam.transform.SetParent(transform, false);
                beam.transform.localPosition = GlowOffset + new Vector3(TriggerCenter.x + Mathf.Sin(i * 2.7f) * .14f,
                    .055f + (i % 6) * .066f, TriggerCenter.z + Mathf.Sin(i * 1.9f) * .02f);
                beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.back);
                beam.transform.localScale = new Vector3(width, length, width);
                beam.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = beam.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _beams[i] = beam.transform;
                _beamPositions[i] = beam.transform.localPosition;
                _beamScales[i] = beam.transform.localScale;
                _beamRenderers[i] = renderer;
                _beamBlocks[i] = new MaterialPropertyBlock();
                _beamBlocks[i].SetFloat("_Phase", i * 2.399963f);
            }
            BuildMotes();
        }
        void LateUpdate()
        {
            Vector2 pointer;
#if ENABLE_INPUT_SYSTEM
            pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-10,-10);
#else
            pointer = Input.mousePosition;
#endif
            if (CampIntegrationCapture.IsRunning) pointer = CampIntegrationCapture.HoverPointer;
            UpdateOutlineHover(pointer);
            if (_material == null) return;
            float time = Time.unscaledTime - _glowStarted;
            _material.SetFloat("_FlowTime", time);
            _material.SetColor("_Color", GlowColor);
            var camp = CampPlayerView.Instance;
            Vector3 hero = camp != null && camp.Active ? transform.InverseTransformPoint(camp.Position + Vector3.up)
                : new Vector3(0,.16f,-.5f);
            hero.x = Mathf.Clamp(hero.x,-.28f,.28f);
            hero.z = Mathf.Min(hero.z, -.35f);
            if (_outlineMaterial != null) _outlineMaterial.SetColor("_Color", new Color(1,.72f,.25f,.42f + .08f * Mathf.Sin(time * 1.5f)));
            // Нити рождаются у арки и явно перемещаются наружу. Плавный вход и выход убирают скачок цикла.
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null) continue;
                float phase = i * 2.399963f;
                float progress = Mathf.Repeat(time * (.36f + (i % 3) * .035f) + phase / (Mathf.PI * 2), 1);
                float breath = .85f + .15f * Mathf.Sin(time * 1.5f + phase);
                Vector3 scale = _beamScales[i];
                scale.y *= breath;
                scale.x *= 1f + .2f * Mathf.Sin(time * 1.3f + phase);
                var origin = _beamPositions[i];
                var direction = hero - origin;
                direction.y *= .25f;
                direction.Normalize();
                var at = origin + direction * (.015f + progress * .30f);
                at.x += Mathf.Sin(time * 1.6f + phase) * .012f;
                _beams[i].localPosition = at;
                _beams[i].localScale = scale;
                _beams[i].localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                _beamBlocks[i].SetFloat("_Intensity", GlowIntensity * (.75f + .25f * breath) * Mathf.Sin(progress * Mathf.PI));
                _beamRenderers[i].SetPropertyBlock(_beamBlocks[i]);
            }
        }
        void BuildMotes()
        {
            var shader = Resources.Load<Shader>("Shaders/CampRiftMotes");
            if (shader == null) return;
            _moteMaterial = new Material(shader);
            var go = new GameObject("Светлячки разлома");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = GlowOffset + new Vector3(TriggerCenter.x, .20f, TriggerCenter.z);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true; main.useUnscaledTime = true; main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.3f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(.008f,.017f);
            main.startColor = new Color(2f,1f,.2f,1);
            var emission = particles.emission; emission.rateOverTime = 8;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.26f,.36f,.07f);
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-.02f,.02f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.008f,.008f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.22f,-.14f);
            var noise = particles.noise; noise.enabled = true; noise.strength = .025f; noise.frequency = 2; noise.scrollSpeed = .35f;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1,.85f,.4f),0), new GradientColorKey(new Color(1,.4f,.04f),1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(.85f,.18f), new GradientAlphaKey(.65f,.65f), new GradientAlphaKey(0,1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _moteMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particles.Play();
        }
        void BuildOutline()
        {
            var shader = Resources.Load<Shader>("Shaders/CampArchOutline");
            if (shader == null) return;
            _outlineMaterial = new Material(shader);
            foreach (var source in GetComponentsInChildren<MeshFilter>())
            {
                if (source.sharedMesh == null) continue;
                var outline = new GameObject("Подсветка арки");
                outline.transform.SetParent(source.transform, false);
                outline.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = outline.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _outlineMaterial;
                renderer.enabled = false;
                _outlineRenderers.Add(renderer);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
        Vector2 _hoverPointer = new Vector2(-1e6f, -1e6f);
        int _nextHoverRaycastFrame;
        bool _hoverHit, _outlineShown;

        /// <summary>
        /// Луч на 300 м против всех коллайдеров лагеря — не каждый кадр, а при
        /// движении курсора и раз в шесть кадров (камера идёт за героем, и мир под
        /// неподвижным курсором всё равно меняется). Контур переключается только
        /// при смене состояния.
        /// </summary>
        internal bool UpdateOutlineHover(Vector2 pointer)
        {
            bool eligible = !IsOpen && Camera.main != null && CampPlayerView.Instance != null
                && CampPlayerView.Instance.Active && !CampPlayerView.Instance.InputBlocked;
            if (eligible && ((pointer - _hoverPointer).sqrMagnitude > .25f || Time.frameCount >= _nextHoverRaycastFrame))
            {
                _hoverHit = Physics.Raycast(Camera.main.ScreenPointToRay(pointer), out var hit, 300f)
                    && hit.collider != _barrier && hit.transform.IsChildOf(transform);
                _hoverPointer = pointer;
                _nextHoverRaycastFrame = Time.frameCount + 6;
            }
            bool hover = eligible && _hoverHit;
            if (hover != _outlineShown)
            {
                foreach (var renderer in _outlineRenderers) if (renderer != null) renderer.enabled = hover;
                _outlineShown = hover;
            }
            return hover;
        }
        void OnGUI()
        {
            if (!IsOpen) return;
            int depth = GUI.depth; GUI.depth = -100;
            Color before = GUI.color; GUI.color = new Color(0,0,0,.65f);
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), Texture2D.whiteTexture); GUI.color = before;
            var rect = new Rect((Screen.width - 480) / 2f, (Screen.height - 200) / 2f, 480, 200);
            GUI.Box(rect, "");
            GUI.Label(new Rect(rect.x + 24, rect.y + 30, 432, 64), "Вы хотите отправиться в забег?",
                new GUIStyle(GameTypography.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, wordWrap = true });
            if (GUI.Button(new Rect(rect.x + 28, rect.y + 121, 202, 44), "Остаться в лагере")) Respond(false);
            if (GUI.Button(new Rect(rect.x + 250, rect.y + 121, 202, 44), "Отправиться")) Respond(true);
            GUI.depth = depth;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix; Gizmos.color = new Color(.15f,.8f,1f,.15f);
            Gizmos.DrawCube(TriggerCenter, TriggerSize); Gizmos.color = Color.cyan; Gizmos.DrawWireCube(TriggerCenter, TriggerSize);
            Gizmos.matrix = Matrix4x4.identity;
        }
        void OnDisable() { IsOpen = false; }
        void OnDestroy() { if (_material != null) Destroy(_material); if (_mesh != null) Destroy(_mesh); if (_moteMaterial != null) Destroy(_moteMaterial); if (_outlineMaterial != null) Destroy(_outlineMaterial); }
    }
}
