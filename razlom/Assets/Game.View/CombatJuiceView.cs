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
        // Одно убийство стоит 37 слотов: 22 осколка, 14 пылинок и послесвечение.
        // Вихрь кладёт пятерых разом — это 185, и при старом пуле в 160 часть
        // эффектов молча не появлялась бы ровно в тот момент, ради которого всё
        // и делалось. Слоты выделяются один раз при старте и живут весь забег.
        private const int PoolSize = 320;
        private const int TrailSamples = 48;
        private const int TrailVerticesPerSample = 3;
        private static readonly float AttackContactTime =
            Simulation.AttackWindupTicks / (float)Simulation.TicksPerSecond;

        private enum FxKind : byte { Spark, Slash, Contact, DeathShard, Dust, Afterglow }

        private struct FxSlot
        {
            public Transform Transform;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public Vector3 Velocity;
            public Color Color;
            public Vector2 SpriteSize;
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

        private Sprite _sparkSprite;
        private Sprite _contactSprite;
        private Sprite _slashSprite;
        private Sprite _dustSprite;
        private Sprite _afterglowSprite;
        private Material _combatFxMaterial;
        private Mesh _fxQuadMesh;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RadialMaskId = Shader.PropertyToID("_RadialMask");

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
        private readonly Vector2[] _trailUvs = new Vector2[TrailSamples * TrailVerticesPerSample];
        private readonly int[] _trailTriangles = new int[(TrailSamples - 1) * 12];
        private int _trailCount;
        private float _trailDelay;
        private float _trailActive;
        private float _trailFade;
        private bool _trailWhirlwind;

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

            // These pooled sprites are the compact contact layer: a brief
            // contact flash and a capped burst for the first contacts in an
            // AoE frame. They are intentionally built once at startup.
            _sparkSprite = MakeSparkSprite();
            _contactSprite = MakeContactSprite();
            _slashSprite = MakeSlashSprite();
            _dustSprite = MakeDustSprite();
            _afterglowSprite = MakeAfterglowSprite();
            Shader combatFxShader = Shader.Find("Razlom/CombatFx");
            if (combatFxShader != null)
            {
                _combatFxMaterial = new Material(combatFxShader)
                {
                    name = "Runtime Combat FX Material",
                    renderQueue = 4000
                };
            }
            BuildFxMeshes();
            BuildPool();
            BuildSwordTrail();
        }

        private void LateUpdate()
        {
            UpdateHitStop();
            ConsumeEvents();
            AnimateFx();
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
            _pendingBasicHeavy = e.Amount == 1;
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

            bool basicContact = fromPlayer
                                && e.DamageOrigin == DamageOrigin.BasicAttack
                                && e.Target == _pendingBasicTarget;
            bool heavyBasicContact = basicContact && _pendingBasicHeavy;
            if (basicContact) _pendingBasicTarget = -1;

            // SpriteRenderer depth is evaluated against the 3D character
            // meshes. Nudge the contact toward the camera so the authored
            // flash sits on the hit silhouette instead of disappearing inside
            // the shield when the target is viewed at an angle.
            Vector3 at = ContactAt(e.Position, fromPlayer ? 0.78f : 0.86f);
            Color core = e.Flag
                ? new Color(1f, 0.92f, 0.40f, 1f)
                : fromPlayer
                    ? new Color(1f, 0.36f, 0.08f, 1f)
                    : new Color(1f, 0.20f, 0.24f, 0.86f);

            // A very short white-hot core is the readable "contact frame"; it
            // is emitted only from confirmed player Damage, never from the
            // attack windup.
            if (fromPlayer && _contactSprite != null)
                SpawnFx(FxKind.Contact, _contactSprite, at, Vector3.zero,
                    e.Flag ? new Color(1f, 0.98f, 0.72f, 1f)
                           : new Color(1f, 0.92f, 0.72f, 0.98f),
                    e.Flag ? 0.16f : 0.13f, e.Flag ? 0.78f : 0.60f,
                    e.Flag ? 0.070f : 0.060f, 0f, 0f, 0f);

            if (_burstBudget > 0 && _sparkSprite != null)
            {
                _burstBudget--;
                SpawnBurst(at, core, e.Flag ? 11 : 7, e.Flag ? 5.2f : 4.0f, false);
            }

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

            Vector3 deathDirection = Vector3.zero;
            Simulation sim = _driver.Sim;
            if (sim != null
                && (uint)source < (uint)sim.Entities.Count
                && (uint)e.Target < (uint)sim.Entities.Count)
            {
                FixVec2 from = sim.Entities.Position[source];
                FixVec2 to = sim.Entities.Position[e.Target];
                deathDirection = new Vector3(to.X.ToFloat() - from.X.ToFloat(), 0f,
                    to.Y.ToFloat() - from.Y.ToFloat());
            }

            // The hit already supplied the contact burst. Death adds only a
            // shard burst, so the kill reads as a payoff rather than a second
            // copy of the impact.
            if (playerKill && _sparkSprite != null)
                SpawnBurst(At(e.Position, 0.76f), new Color(1f, 0.38f, 0.16f, 1f),
                    22, 8.4f, true);

            if (playerKill)
            {
                _arena?.ReactToDeath(e.Target, deathDirection, 1f);
                SpawnKillAftermath(e.Position);

                // УБИЙСТВО ОБЯЗАНО ЗАМЕТНО ОТЛИЧАТЬСЯ ОТ КРИТА, ИНАЧЕ ОНО
                // ТЕРЯЕТСЯ В ПОТОКЕ ПОПАДАНИЙ.
                //
                // Раньше пауза убийства была 0.095 с против 0.070 у крита —
                // разница в треть, которую глаз не отделяет. За сессию игрок
                // убивает десятки тысяч мобов, и если смерть звучит как
                // очередной удар, то главное событие боя не награждается ничем.
                //
                // 0.15 с — это вдвое дольше крита, и это уже читается как
                // «оно кончилось». Дальше растить нельзя: пауза на каждом
                // убийстве превращает зачистку толпы в череду заиканий.
                Accumulate(
                    trauma: 0.85f,
                    zoom: 1.25f,
                    stopDuration: 0.15f,
                    stopScale: 0.02f);
            }
        }

        private void SpawnKillAftermath(FixVec2 position)
        {
            if (_afterglowSprite != null)
                SpawnFx(FxKind.Afterglow, _afterglowSprite, ContactAt(position, 0.58f),
                    Vector3.up * 0.08f, new Color(1.55f, 0.42f, 0.075f, 0.62f),
                    0.58f, 0.46f, 1.58f, 0f, 0f, 0f);

            if (_dustSprite == null) return;

            // ПЫЛЬ — ЭТО СЛЕД СОБЫТИЯ, А НЕ УКРАШЕНИЕ.
            //
            // Шесть облачков, расходящихся на треть метра, гасли раньше, чем
            // глаз успевал их прочитать: смерть заканчивалась в тот же кадр,
            // в котором началась. Четырнадцать, вдвое быстрее и вдвое дальше,
            // держат место убийства заметным ещё почти секунду — это и есть
            // то послевкусие, которого не хватало.
            Vector3 center = At(position, 0.18f);
            for (int i = 0; i < 14; i++)
            {
                float angle = Random01() * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 velocity = direction * Mathf.Lerp(0.65f, 1.55f, Random01())
                    + Vector3.up * Mathf.Lerp(0.18f, 0.52f, Random01());
                SpawnFx(FxKind.Dust, _dustSprite,
                    center + direction * Mathf.Lerp(0.05f, 0.34f, Random01()), velocity,
                    new Color(0.92f, 0.66f, 0.38f, Mathf.Lerp(0.34f, 0.52f, Random01())),
                    Mathf.Lerp(0.52f, 0.86f, Random01()),
                    Mathf.Lerp(0.34f, 0.62f, Random01()),
                    Mathf.Lerp(0.86f, 1.35f, Random01()),
                    Mathf.Lerp(-55f, 55f, Random01()), Random01() * 180f, 0.34f);
            }
        }

        private void SpawnAbility(in SimEvent e)
        {
            _pendingBasicTarget = -1;
            if (e.Source != Simulation.PlayerId) return;

            Simulation sim = _driver.Sim;
            if (sim == null || (uint)e.Amount >= Simulation.AbilitySlots) return;
            AbilityBuild build = sim.GetAbility(e.Amount);
            if (build != null && build.DefinitionId == AbilityDefinition.WhirlwindId)
                StartWhirlwindTrail();
        }

        /// <summary>Запускает клинковую ленту в изолированной VFX-витрине.</summary>
        public void PlayWhirlwindTrail()
        {
            StartWhirlwindTrail();
        }

        /// <summary>Запускает обычную ленту сабли в изолированной VFX-витрине.</summary>
        public void PlayBasicAttackTrail()
        {
            StartBasicAttackTrail();
        }

        private void StartWhirlwindTrail()
        {
            if (_arena == null || !_arena.TryGetPlayerBlade(out _bladeRoot, out _bladeTip))
            {
                return;
            }
            _trailCount = 0;
            _trailDelay = 0.05f;
            _trailActive = 0.46f;
            _trailFade = 0.16f;
            _trailWhirlwind = true;
            if (_trailRenderer != null) _trailRenderer.enabled = false;
        }

        private void StartBasicAttackTrail()
        {
            if (_arena == null || !_arena.TryGetPlayerBlade(out _bladeRoot, out _bladeTip))
                return;

            // Start the ribbon in the acceleration phase and carry it across
            // the shared contact tick into follow-through.
            _trailCount = 0;
            _trailDelay = Mathf.Max(0f, AttackContactTime - 0.16f);
            _trailActive = 0.19f;
            _trailFade = 0.09f;
            _trailWhirlwind = false;
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
            if (_trailRenderer.sharedMaterial.HasProperty("_Glow"))
                _trailRenderer.sharedMaterial.SetFloat("_Glow", 1.55f);
            _trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trailRenderer.receiveShadows = false;
            _trailRenderer.sortingOrder = 100;
            _trailRenderer.sharedMaterial.renderQueue = 3100;
            _trailRenderer.enabled = false;
        }

        private void AnimateSwordTrail()
        {
            float dt = Time.deltaTime;
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
            Vector3 blade = tip - root;
            if (_trailWhirlwind)
            {
                // The authored Stone Slash owns the large contact silhouette.
                // This mesh is only a soft connector from the real sabre, so it
                // stays close to the blade and cannot read as an angular fan.
                tip = root + blade * 1.34f;
                root += blade * 0.34f;
            }
            else
            {
                // Basic attacks remain compact and leave the target readable.
                root += blade * 0.48f;
            }

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

            float fadeDuration = _trailWhirlwind ? 0.16f : 0.09f;
            float fade = _trailActive > 0f ? 1f : Mathf.Clamp01(_trailFade / fadeDuration);
            for (int i = 0; i < _trailCount; i++)
            {
                float along = _trailCount > 1 ? i / (float)(_trailCount - 1) : 1f;
                float width = _trailWhirlwind
                    ? Mathf.Lerp(0.06f, 0.30f, Mathf.Sqrt(along))
                    : Mathf.Lerp(0.08f, 0.58f, Mathf.Sqrt(along));
                Vector3 mid = (_trailRoots[i] + _trailTips[i]) * 0.5f;
                Vector3 half = (_trailTips[i] - _trailRoots[i]) * (0.5f * width);
                int vertex = i * TrailVerticesPerSample;
                _trailVertices[vertex] = mid - half;
                _trailVertices[vertex + 1] = mid;
                _trailVertices[vertex + 2] = mid + half;

                float alpha = Mathf.SmoothStep(0f, 1f, along) * fade;
                if (_trailWhirlwind)
                {
                    _trailColors[vertex] = new Color(1.65f, 0.06f, 0.32f, alpha * 0.035f);
                    _trailColors[vertex + 1] = new Color(0.18f, 1.45f, 1.65f, alpha * 0.14f);
                    _trailColors[vertex + 2] = new Color(2.8f, 0.72f, 0.18f, alpha * 0.26f);
                }
                else
                {
                    _trailColors[vertex] = new Color(1.35f, 0.18f, 0.02f, alpha * 0.06f);
                    _trailColors[vertex + 1] = new Color(2.1f, 0.48f, 0.05f, alpha * 0.30f);
                    _trailColors[vertex + 2] = new Color(3.0f, 1.05f, 0.16f, alpha * 0.48f);
                }
                _trailUvs[vertex] = new Vector2(along, 0f);
                _trailUvs[vertex + 1] = new Vector2(along, 0.5f);
                _trailUvs[vertex + 2] = new Vector2(along, 1f);
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
            _trailMesh.SetUVs(0, _trailUvs, 0, _trailCount * TrailVerticesPerSample);
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
                    death ? Mathf.Lerp(0.20f, 0.34f, Random01())
                          : Mathf.Lerp(0.16f, 0.27f, Random01()),
                    death ? 0.015f : 0.025f,
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
            slot.Filter.sharedMesh = _fxQuadMesh;
            slot.SpriteSize = sprite != null
                ? new Vector2(sprite.bounds.size.x, sprite.bounds.size.y)
                : Vector2.one;
            slot.Properties.SetTexture(MainTexId, sprite == null ? Texture2D.whiteTexture : sprite.texture);
            slot.Properties.SetColor(ColorId, color);
            slot.Properties.SetFloat(RadialMaskId, kind == FxKind.Afterglow ? 1f : 0f);
            slot.Renderer.SetPropertyBlock(slot.Properties);
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
            slot.Transform.localScale = new Vector3(
                startScale * slot.SpriteSize.x,
                startScale * slot.SpriteSize.y,
                1f);
        }

        private void AnimateFx()
        {
            if (_pool == null) return;
            float unscaledDt = Time.unscaledDeltaTime;
            float scaledDt = Time.deltaTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                ref FxSlot slot = ref _pool[i];
                if (slot.Remaining <= 0f) continue;
                if (slot.Transform == null || slot.Renderer == null)
                {
                    slot.Remaining = 0f;
                    continue;
                }

                // Contact cores and slash sparks are allowed to bloom during
                // hit-stop. The kill aftermath is different: if it also uses
                // unscaled time, almost its whole life is spent while the
                // world is frozen and the player never sees a residual trail
                // after the body starts moving. Tie the death plume, shards
                // and afterglow to combat time so they survive the impact hold
                // and travel with the death animation.
                float dt = slot.Kind == FxKind.Afterglow
                           || slot.Kind == FxKind.Dust
                           || slot.Kind == FxKind.DeathShard
                    ? scaledDt
                    : unscaledDt;
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
                slot.Transform.localScale = new Vector3(
                    scale * slot.SpriteSize.x,
                    scale * slot.SpriteSize.y,
                    1f);

                if (_camera != null)
                    slot.Transform.rotation = _camera.transform.rotation
                        * Quaternion.Euler(0f, 0f, slot.Angle + slot.Spin * t);

                Color c = slot.Color;
                float fadeStart = slot.Kind == FxKind.Contact ? 0.16f
                    : slot.Kind == FxKind.Slash ? 0.25f
                    : slot.Kind == FxKind.Dust ? 0.08f
                    : slot.Kind == FxKind.Afterglow ? 0.12f
                    : 0.48f;
                c.a *= 1f - Mathf.SmoothStep(fadeStart, 1f, t);
                slot.Properties.SetColor(ColorId, c);
                slot.Renderer.SetPropertyBlock(slot.Properties);
            }
        }

        private void StartHitStop(float duration, float scale)
        {
            if (_driver != null && _driver.GameplayPaused) return;
            if (_hitStopRemaining <= 0f) _timeScaleBeforeStop = Time.timeScale;
            _hitStopRemaining = Mathf.Max(_hitStopRemaining, duration);
            Time.timeScale = Mathf.Min(Time.timeScale, scale);
        }

        /// <summary>
        /// Завершает presentation-only hit-stop перед системной паузой и
        /// возвращает нормальный масштаб времени, который она должна сохранить.
        /// </summary>
        public float CancelHitStopForPause()
        {
            float gameplayTimeScale = _hitStopRemaining > 0f
                ? _timeScaleBeforeStop
                : Time.timeScale;

            _hitStopRemaining = 0f;
            _timeScaleBeforeStop = gameplayTimeScale;
            Time.timeScale = gameplayTimeScale;
            return gameplayTimeScale;
        }

        private void UpdateHitStop()
        {
            if (_driver != null && _driver.GameplayPaused) return;
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
                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _fxQuadMesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                if (_combatFxMaterial != null) renderer.sharedMaterial = _combatFxMaterial;
                renderer.sortingOrder = 5000 + i % 4;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
                go.SetActive(false);
                _pool[i] = new FxSlot
                {
                    Transform = go.transform,
                    Filter = filter,
                    Renderer = renderer,
                    Properties = new MaterialPropertyBlock()
                };
            }
        }

        private void BuildFxMeshes()
        {
            _fxQuadMesh = new Mesh { name = "Runtime Combat FX Quad" };
            _fxQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };
            _fxQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            _fxQuadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _fxQuadMesh.RecalculateBounds();

        }

        private static Vector3 At(FixVec2 position, float height)
            => new Vector3(position.X.ToFloat(), height, position.Y.ToFloat());

        private Vector3 ContactAt(FixVec2 position, float height)
        {
            Vector3 at = At(position, height);
            // The character mesh writes depth while the FX is transparent.
            // A view-space offset of roughly half a world unit keeps the
            // contact in front of armour at every camera angle without
            // changing its screen position in the orthographic view.
            return _camera != null ? at - _camera.transform.forward * 0.45f : at;
        }

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

        private static Sprite MakeContactSprite()
        {
            Texture2D texture = NewTexture("Runtime Contact Flash", 128, 128);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / 64f - 1f;
                float ny = (y + 0.5f) / 64f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float diamond = 1f - Mathf.SmoothStep(0.05f, 0.48f,
                    Mathf.Abs(nx) + Mathf.Abs(ny));
                float horizontal = (1f - Mathf.SmoothStep(0.035f, 0.13f, Mathf.Abs(ny)))
                    * (1f - Mathf.SmoothStep(0.30f, 0.92f, Mathf.Abs(nx)));
                float vertical = (1f - Mathf.SmoothStep(0.035f, 0.13f, Mathf.Abs(nx)))
                    * (1f - Mathf.SmoothStep(0.30f, 0.92f, Mathf.Abs(ny)));
                float edgeFade = 1f - Mathf.SmoothStep(0.72f, 1f, radius);
                float alpha = Mathf.Clamp01(Mathf.Max(diamond, Mathf.Max(horizontal, vertical) * 0.78f))
                    * edgeFade;
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

        private static Sprite MakeDustSprite()
        {
            Texture2D texture = NewTexture("Runtime Soft Kill Dust", 64, 64);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / 32f - 1f;
                float ny = (y + 0.5f) / 32f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float wobble = Mathf.Sin(nx * 9.1f + ny * 5.7f) * 0.045f
                    + Mathf.Sin(nx * 4.3f - ny * 8.9f) * 0.035f;
                float alpha = 1f - Mathf.SmoothStep(0.20f, 0.94f, radius + wobble);
                alpha *= 1f - Mathf.SmoothStep(0.72f, 1f, Mathf.Abs(ny));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * 0.72f));
            }
            return FinishSprite(texture, 64f);
        }

        private static Sprite MakeAfterglowSprite()
        {
            Texture2D texture = NewTexture("Runtime Kill Afterglow", 96, 96);
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x + 0.5f) / 48f - 1f;
                float ny = (y + 0.5f) / 48f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float core = 1f - Mathf.SmoothStep(0.02f, 0.72f, radius);
                float halo = 1f - Mathf.SmoothStep(0.22f, 0.82f, radius);
                float alpha = Mathf.Clamp01(core * 0.72f + halo * 0.38f);
                // CombatFx uses additive SrcAlpha blending. Premultiplying the
                // radial falloff into RGB as well removes the faint square
                // footprint of the billboard without weakening its hot core.
                texture.SetPixel(x, y, new Color(alpha, alpha, alpha, alpha));
            }
            return FinishSprite(texture, 72f);
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
