using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Presentation-only слой удовольствия от боя. Он читает подтверждённые
    /// SimEvent и добавляет slash, sparks, hit-stop и camera impulse; урон и
    /// тайминги симуляции здесь никогда не рассчитываются.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(1000)]
    public sealed class CombatJuiceView : MonoBehaviour
    {
        private const int PoolSize = 160;
        private const int TrailSamples = 18;
        private const int TrailVerticesPerSample = 3;

        private enum FxKind : byte { Spark, Ring, Slash, DeathShard }

        private struct FxSlot
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 Velocity;
            public Color Color;
            public float Remaining;
            public float Lifetime;
            public float StartScale;
            public float EndScale;
            public float Spin;
            public float Angle;
            public float Gravity;
            public FxKind Kind;
        }

        /// <summary>
        /// Сколько полных вспышек с искрами разрешено за кадр.
        ///
        /// Ограничение не косметическое: удар по площади задевает двадцать
        /// целей, по десять искр на каждую — и пул в сто шестьдесят слотов
        /// вымывается целиком, вместе с эффектами крита и добивания, ради
        /// которых он и заведён. Сверх бюджета цель получает только кольцо.
        /// </summary>
        private const int MaxBurstsPerFrame = 6;

        private TickDriver _driver;
        private ArenaView _arena;
        private Camera _camera;
        private CombatCameraJuice _cameraJuice;

        // Импульс камеры и hit-stop копятся за кадр и применяются ОДИН раз.
        // Двадцать попаданий одного кадра — это один толчок, а не двадцать
        // сложенных: сложенные дают не мощь, а тряску и тошноту.
        private int _burstBudget;
        private float _frameTrauma;
        private float _frameZoom;
        private float _frameStopDuration;
        private float _frameStopScale;
        private FxSlot[] _pool;
        private int _cursor;
        private int[] _lastDamageSource;
        private int _pendingBasicTarget = -1;
        private bool _pendingBasicHeavy;
        private bool _nextBasicHeavy;

        private Sprite _sparkSprite;
        private Sprite _ringSprite;
        private Sprite _slashSprite;

        private float _hitStopRemaining;
        private float _timeScaleBeforeStop = 1f;
        private uint _randomState = 0x9E3779B9u;

        private Transform _bladeRoot;
        private Transform _bladeTip;
        private Mesh _trailMesh;
        private MeshRenderer _trailRenderer;
        private readonly Vector3[] _trailRoots = new Vector3[TrailSamples];
        private readonly Vector3[] _trailTips = new Vector3[TrailSamples];
        private readonly Vector3[] _trailVertices = new Vector3[TrailSamples * TrailVerticesPerSample];
        private readonly Color[] _trailColors = new Color[TrailSamples * TrailVerticesPerSample];
        private readonly int[] _trailTriangles = new int[(TrailSamples - 1) * 12];
        private int _trailCount;
        private float _trailDelay;
        private float _trailActive;
        private float _trailFade;
        private float _whirlwindImpactWindow;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
        }

        private void Start()
        {
            _camera = Camera.main;
            _cameraJuice = _camera != null ? _camera.GetComponent<CombatCameraJuice>() : null;
            // Ёмкость берётся по самой большой симуляции сессии: в лагере
            // симуляции нет вовсе, а на Полигоне и в Разломе они разного размера.
            _lastDamageSource = new int[TickDriver.MaxSimCapacity];
            for (int i = 0; i < _lastDamageSource.Length; i++) _lastDamageSource[i] = -1;

            BuildSwordTrail();
        }

        private void LateUpdate()
        {
            UpdateHitStop();
            ConsumeEvents();
            AnimateSwordTrail();
        }

        private void OnDisable()
        {
            if (_hitStopRemaining > 0f) Time.timeScale = _timeScaleBeforeStop;
            _hitStopRemaining = 0f;
        }

        private void ConsumeEvents()
        {
            _burstBudget = MaxBurstsPerFrame;
            _frameTrauma = 0f;
            _frameZoom = 0f;
            _frameStopDuration = 0f;
            _frameStopScale = 1f;

            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        SpawnAttack(e);
                        break;
                    case SimEventType.Damage:
                        if ((uint)e.Target < (uint)_lastDamageSource.Length)
                            _lastDamageSource[e.Target] = e.Source;
                        SpawnHit(e);
                        break;

                    case SimEventType.DamageOverTime:
                        // Горение тикает тридцать раз в секунду на КАЖДОЙ горящей
                        // цели. Если считать это попаданиями, hit-stop продлевается
                        // каждый кадр и игра встаёт в слоу-мо, пока что-то горит.
                        //
                        // Поэтому здесь — ничего. Кто горит, видно по цифрам
                        // и по самому знаку; удар остаётся ударом.
                        if ((uint)e.Target < (uint)_lastDamageSource.Length)
                            _lastDamageSource[e.Target] = e.Source;
                        break;
                    case SimEventType.Death:
                        SpawnDeath(e);
                        break;
                    case SimEventType.AbilityCast:
                        SpawnAbility(e);
                        break;
                }
            }

            // Один стоп и один импульс на кадр, по самому сильному событию.
            if (_frameStopDuration > 0f) StartHitStop(_frameStopDuration, _frameStopScale);
            if (_frameTrauma > 0f) _cameraJuice?.AddImpulse(_frameTrauma, _frameZoom);
        }

        /// <summary>Копит самый сильный толчок кадра, а не складывает все.</summary>
        private void Accumulate(float trauma, float zoom, float stopDuration, float stopScale)
        {
            if (trauma > _frameTrauma) _frameTrauma = trauma;
            if (zoom > _frameZoom) _frameZoom = zoom;
            if (stopDuration > _frameStopDuration)
            {
                _frameStopDuration = stopDuration;
                _frameStopScale = stopScale;
            }
        }

        private void SpawnAttack(in SimEvent e)
        {
            bool playerAttack = e.Source == Simulation.PlayerId;
            if (!playerAttack) return;

            _pendingBasicTarget = e.Target;
            _pendingBasicHeavy = _nextBasicHeavy;
            _nextBasicHeavy = !_nextBasicHeavy;
            StartBasicAttackTrail();
        }

        private void SpawnHit(in SimEvent e)
        {
            bool fromPlayer = e.Source == Simulation.PlayerId;
            bool playerHit = e.Target == Simulation.PlayerId;

            // Частицы контакта рождает PelagVfxController ровно один раз на
            // подтверждённом Damage. Здесь остаются только физическая реакция,
            // камера и hit-stop — они не засоряют силуэты.
            if (!fromPlayer && !playerHit) return;

            bool basicContact = fromPlayer && e.Target == _pendingBasicTarget;
            bool heavyBasicContact = basicContact && _pendingBasicHeavy;
            if (basicContact) _pendingBasicTarget = -1;

            float push = e.Flag ? 1f
                : playerHit ? 0.8f
                : heavyBasicContact ? 0.82f
                : basicContact ? 0.70f
                : 0.62f;
            PushTarget(in e, push);

            Accumulate(
                trauma: e.Flag ? 0.52f : playerHit ? 0.33f
                    : heavyBasicContact ? 0.36f : basicContact ? 0.29f : 0.25f,
                zoom: e.Flag ? 0.75f
                    : heavyBasicContact ? 0.48f : basicContact ? 0.40f : 0.35f,
                stopDuration: e.Flag ? 0.070f
                    : heavyBasicContact ? 0.052f : basicContact ? 0.044f : 0.038f,
                stopScale: e.Flag ? 0.035f : 0.08f);
        }

        /// <summary>
        /// Толкает тело цели от бьющего: отдача и сплющивание.
        ///
        /// Само тело при этом не двигается — двигается только его картинка.
        /// Положение сущности по-прежнему решает тик, и трогать его отсюда
        /// нельзя ни при каких условиях.
        /// </summary>
        private void PushTarget(in SimEvent e, float strength)
        {
            if (_arena == null) return;

            Simulation sim = _driver.Sim;
            Vector3 direction = Vector3.zero;
            if (sim != null
                && (uint)e.Source < (uint)sim.Entities.Count
                && (uint)e.Target < (uint)sim.Entities.Count)
            {
                FixVec2 from = sim.Entities.Position[e.Source];
                FixVec2 to = sim.Entities.Position[e.Target];
                direction = new Vector3(to.X.ToFloat() - from.X.ToFloat(), 0f,
                                        to.Y.ToFloat() - from.Y.ToFloat());
            }

            _arena.ReactToHit(e.Target, direction, strength);
        }

        private void SpawnDeath(in SimEvent e)
        {
            int source = (uint)e.Target < (uint)_lastDamageSource.Length ? _lastDamageSource[e.Target] : -1;
            bool playerKill = source == Simulation.PlayerId;

            // Смерть усиливает контакт временем и камерой, но не добавляет
            // второй слой частиц поверх уже показанного hit.
            if (playerKill)
                Accumulate(
                    trauma: 0.68f,
                    zoom: 1.08f,
                    stopDuration: 0.085f,
                    stopScale: 0.025f);
        }

        private void SpawnAbility(in SimEvent e)
        {
            _pendingBasicTarget = -1;
            // Способности временно не создают декоративных VFX. Их контакт всё
            // равно подтверждается единым hit-эффектом через Damage.
        }

        private void StartWhirlwindTrail()
        {
            if (_arena == null || !_arena.TryGetPlayerBlade(out _bladeRoot, out _bladeTip))
            {
                return;
            }
            _trailCount = 0;
            _trailDelay = 0.12f;
            _trailActive = 0.38f;
            _trailFade = 0.16f;
            _whirlwindImpactWindow = 0.62f;
            if (_trailRenderer != null) _trailRenderer.enabled = false;
        }

        private void StartBasicAttackTrail()
        {
            if (_arena == null || !_arena.TryGetPlayerBlade(out _bladeRoot, out _bladeTip))
                return;

            // Contact is at 0.60 s. Sampling starts in acceleration, crosses
            // the contact frame and then fades during follow-through.
            _trailCount = 0;
            _trailDelay = 0.40f;
            _trailActive = 0.19f;
            _trailFade = 0.09f;
            if (_trailRenderer != null) _trailRenderer.enabled = false;
        }

        private void BuildSwordTrail()
        {
            GameObject go = new GameObject("Pelag Sword Trail");
            go.transform.SetParent(transform, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            _trailRenderer = go.AddComponent<MeshRenderer>();

            _trailMesh = new Mesh { name = "Runtime Pelag Whirlwind Ribbon" };
            _trailMesh.MarkDynamic();
            filter.sharedMesh = _trailMesh;

            Shader shader = Shader.Find("Razlom/SwordTrail");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _trailRenderer.sharedMaterial = new Material(shader) { name = "Runtime Pelag Whirlwind Trail" };
            _trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trailRenderer.receiveShadows = false;
            _trailRenderer.sortingOrder = 100;
            _trailRenderer.sharedMaterial.renderQueue = 3100;
            _trailRenderer.enabled = false;
        }

        private void AnimateSwordTrail()
        {
            float dt = Time.deltaTime;
            if (_whirlwindImpactWindow > 0f) _whirlwindImpactWindow -= dt;
            if (_trailDelay > 0f)
            {
                _trailDelay -= dt;
                return;
            }

            if (_trailActive > 0f && _bladeRoot != null && _bladeTip != null)
            {
                _trailActive -= dt;
                AddTrailSample(_bladeRoot.position, _bladeTip.position);
            }
            else if (_trailFade > 0f)
            {
                _trailFade -= dt;
            }
            else
            {
                if (_trailRenderer != null) _trailRenderer.enabled = false;
                return;
            }

            RebuildSwordTrail();
        }

        private void AddTrailSample(Vector3 root, Vector3 tip)
        {
            // A full guard-to-tip swept surface reads as an opaque attack cone.
            // The familiar MOBA/ARPG sword trail lives on the fast outer third
            // of the blade, leaving Pelag's silhouette and contact target clear.
            root = Vector3.Lerp(root, tip, 0.48f);

            if (_trailCount > 0)
            {
                Vector3 lastMid = (_trailRoots[_trailCount - 1] + _trailTips[_trailCount - 1]) * 0.5f;
                if (((root + tip) * 0.5f - lastMid).sqrMagnitude < 0.000025f) return;
            }

            if (_trailCount == TrailSamples)
            {
                for (int i = 1; i < TrailSamples; i++)
                {
                    _trailRoots[i - 1] = _trailRoots[i];
                    _trailTips[i - 1] = _trailTips[i];
                }
                _trailCount--;
            }

            _trailRoots[_trailCount] = root;
            _trailTips[_trailCount] = tip;
            _trailCount++;
        }

        private void RebuildSwordTrail()
        {
            if (_trailCount < 2 || _trailMesh == null) return;

            float fade = _trailActive > 0f ? 1f : Mathf.Clamp01(_trailFade / 0.18f);
            for (int i = 0; i < _trailCount; i++)
            {
                float along = _trailCount > 1 ? i / (float)(_trailCount - 1) : 1f;
                float width = Mathf.Lerp(0.10f, 0.88f, Mathf.Sqrt(along));
                Vector3 mid = (_trailRoots[i] + _trailTips[i]) * 0.5f;
                Vector3 half = (_trailTips[i] - _trailRoots[i]) * (0.5f * width);
                int vertex = i * TrailVerticesPerSample;
                _trailVertices[vertex] = mid - half;
                _trailVertices[vertex + 1] = mid;
                _trailVertices[vertex + 2] = mid + half;

                float alpha = Mathf.SmoothStep(0f, 1f, along) * fade;
                _trailColors[vertex] = new Color(1f, 0.22f, 0.02f, alpha * 0.10f);
                _trailColors[vertex + 1] = new Color(1f, 0.44f, 0.05f, alpha * 0.42f);
                _trailColors[vertex + 2] = new Color(1f, 0.72f, 0.14f, alpha * 0.66f);
            }

            int triangle = 0;
            for (int i = 0; i < _trailCount - 1; i++)
            {
                int a = i * TrailVerticesPerSample;
                int b = a + TrailVerticesPerSample;
                _trailTriangles[triangle++] = a;
                _trailTriangles[triangle++] = b;
                _trailTriangles[triangle++] = a + 1;
                _trailTriangles[triangle++] = a + 1;
                _trailTriangles[triangle++] = b;
                _trailTriangles[triangle++] = b + 1;
                _trailTriangles[triangle++] = a + 1;
                _trailTriangles[triangle++] = b + 1;
                _trailTriangles[triangle++] = a + 2;
                _trailTriangles[triangle++] = a + 2;
                _trailTriangles[triangle++] = b + 1;
                _trailTriangles[triangle++] = b + 2;
            }

            _trailMesh.Clear(false);
            _trailMesh.SetVertices(_trailVertices, 0, _trailCount * TrailVerticesPerSample);
            _trailMesh.SetColors(_trailColors, 0, _trailCount * TrailVerticesPerSample);
            _trailMesh.SetTriangles(_trailTriangles, 0, triangle, 0, true);
            _trailMesh.RecalculateBounds();
            _trailRenderer.enabled = true;
        }

        private void SpawnBurst(Vector3 at, Color color, int count, float speed, bool death)
        {
            Camera cam = _camera;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                float angle = Random01() * Mathf.PI * 2f;
                float magnitude = speed * Mathf.Lerp(0.45f, 1f, Random01());
                Vector3 velocity = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * magnitude;
                SpawnFx(death ? FxKind.DeathShard : FxKind.Spark, _sparkSprite, at, velocity,
                    color, Mathf.Lerp(0.24f, 0.46f, Random01()),
                    death ? 0.20f : 0.12f, 0.015f,
                    Mathf.Lerp(-280f, 280f, Random01()), Random01() * 180f,
                    death ? 2.5f : 0.8f);
            }
        }

        private void SpawnFx(FxKind kind, Sprite sprite, Vector3 position, Vector3 velocity,
            Color color, float lifetime, float startScale, float endScale,
            float spin, float angle, float gravity)
        {
            if (_pool == null || _pool.Length == 0) return;
            int index = _cursor++ % _pool.Length;
            ref FxSlot slot = ref _pool[index];
            if (slot.Transform == null || slot.Renderer == null) return;
            slot.Transform.gameObject.SetActive(true);
            slot.Transform.position = position;
            slot.Renderer.sprite = sprite;
            slot.Renderer.color = color;
            slot.Velocity = velocity;
            slot.Color = color;
            slot.Remaining = lifetime;
            slot.Lifetime = lifetime;
            slot.StartScale = startScale;
            slot.EndScale = endScale;
            slot.Spin = spin;
            slot.Angle = angle;
            slot.Gravity = gravity;
            slot.Kind = kind;
            slot.Transform.localScale = Vector3.one * startScale;
        }

        private void AnimateFx()
        {
            if (_pool == null) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                ref FxSlot slot = ref _pool[i];
                if (slot.Remaining <= 0f) continue;
                if (slot.Transform == null || slot.Renderer == null)
                {
                    slot.Remaining = 0f;
                    continue;
                }
                slot.Remaining -= dt;
                if (slot.Remaining <= 0f)
                {
                    slot.Transform.gameObject.SetActive(false);
                    continue;
                }

                float t = 1f - slot.Remaining / slot.Lifetime;
                slot.Velocity += Vector3.down * (slot.Gravity * dt);
                slot.Transform.position += slot.Velocity * dt;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float scale = Mathf.Lerp(slot.StartScale, slot.EndScale, eased);
                slot.Transform.localScale = Vector3.one * scale;

                if (_camera != null)
                    slot.Transform.rotation = _camera.transform.rotation
                        * Quaternion.Euler(0f, 0f, slot.Angle + slot.Spin * t);

                Color c = slot.Color;
                float fadeStart = slot.Kind == FxKind.Slash ? 0.25f : 0.48f;
                c.a *= 1f - Mathf.SmoothStep(fadeStart, 1f, t);
                slot.Renderer.color = c;
            }
        }

        private void StartHitStop(float duration, float scale)
        {
            if (_hitStopRemaining <= 0f) _timeScaleBeforeStop = Time.timeScale;
            _hitStopRemaining = Mathf.Max(_hitStopRemaining, duration);
            Time.timeScale = Mathf.Min(Time.timeScale, scale);
        }

        private void UpdateHitStop()
        {
            if (_hitStopRemaining <= 0f) return;
            _hitStopRemaining -= Time.unscaledDeltaTime;
            if (_hitStopRemaining <= 0f)
            {
                Time.timeScale = _timeScaleBeforeStop;
                _hitStopRemaining = 0f;
            }
        }

        private void BuildPool()
        {
            Transform root = new GameObject("Pool: Combat Juice").transform;
            root.SetParent(transform, false);
            _pool = new FxSlot[PoolSize];
            for (int i = 0; i < _pool.Length; i++)
            {
                GameObject go = new GameObject($"Combat FX {i}");
                go.transform.SetParent(root, false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 5000 + i % 4;
                go.SetActive(false);
                _pool[i] = new FxSlot { Transform = go.transform, Renderer = renderer };
            }
        }

        private static Vector3 At(FixVec2 position, float height)
            => new Vector3(position.X.ToFloat(), height, position.Y.ToFloat());

        private float Random01()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFFu) / 16777215f;
        }

        private static Sprite MakeSparkSprite()
        {
            Texture2D texture = NewTexture("Runtime Ink Spark", 32, 64);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = Mathf.Abs((x + 0.5f) / 16f - 1f);
                float ny = Mathf.Abs((y + 0.5f) / 32f - 1f);
                float alpha = 1f - Mathf.SmoothStep(0.72f, 1f, nx + ny * 0.42f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            return FinishSprite(texture, 64f);
        }

        private static Sprite MakeRingSprite()
        {
            Texture2D texture = NewTexture("Runtime Impact Ring", 128, 128);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / 64f - 1f;
                float ny = (y + 0.5f) / 64f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = 1f - Mathf.SmoothStep(0.055f, 0.14f, Mathf.Abs(radius - 0.67f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            return FinishSprite(texture, 96f);
        }

        private static Sprite MakeSlashSprite()
        {
            Texture2D texture = NewTexture("Runtime Comic Slash", 256, 128);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / 128f - 1f;
                float ny = (y + 0.5f) / 64f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny * 2.6f);
                float angle = Mathf.Atan2(ny * 1.5f, nx);
                float arc = 1f - Mathf.SmoothStep(0.03f, 0.12f, Mathf.Abs(radius - 0.72f));
                float taper = Mathf.SmoothStep(-1.15f, -0.65f, angle) * (1f - Mathf.SmoothStep(0.65f, 1.15f, angle));
                float alpha = arc * taper;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            return FinishSprite(texture, 96f);
        }

        private static Texture2D NewTexture(string name, int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        private static Sprite FinishSprite(Texture2D texture, float pixelsPerUnit)
        {
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }
    }
}
