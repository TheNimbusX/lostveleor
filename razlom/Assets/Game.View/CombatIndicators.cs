using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Опознаватели на полу: кто свой, кто чужой, куда я иду и кого сейчас бью.
    ///
    /// ЗАЧЕМ. В кадре до сорока тел, и разобрать в этой каше собственного
    /// персонажа, зону своего удара и текущую цель по одним только силуэтам
    /// нельзя — особенно пока арт прототипный. Кольцо под ногами читается
    /// мгновенно и не занимает центр экрана, а значит не спорит с правилом
    /// «середина экрана свободна всегда».
    ///
    /// Кольцо и сектор рисуются под телами. Зелёный отклик ПКМ повторяет
    /// поверхность пола, включая мост, и остаётся частью игрового мира.
    ///
    /// Представление только читает симуляцию и ничего в ней не трогает.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(900)]
    public sealed class CombatIndicators : MonoBehaviour
    {
        [Header("Что показывать")]
        public bool ShowFootRings = false;
        public bool ShowAttackArc = true;
        public bool ShowMoveOrder = true;

        [Tooltip("Менять курсор на боевой, когда он над врагом.")]
        public bool ShowAttackCursor = true;

        [Header("Цвета")]
        [Tooltip("Свой. Бирюза народов Вола — тот же язык, что и в палитрах.")]
        public Color AllyColor = new Color(0.15f, 0.90f, 0.95f, 1.00f);

        public Color EnemyColor = new Color(0.95f, 0.25f, 0.22f, 0.52f);

        [Tooltip("Цель, которую персонаж бьёт прямо сейчас.")]
        public Color TargetColor = new Color(1.00f, 0.12f, 0.08f, 0.95f);

        [Tooltip("Тело под курсором. Наведение обязано быть видно ДО удара.")]
        public Color HoverColor = new Color(1.00f, 0.16f, 0.11f, 0.95f);

        /// <summary>Цвет кольца приземления: тёплый, как сама цепь.</summary>
        public Color LandingColor = new Color(1.00f, 0.62f, 0.22f, 0.85f);

        [Tooltip("Сектор автоатаки. Виден, пока зажата кнопка удара.")]
        public Color ArcColor = new Color(1.00f, 0.72f, 0.30f, 0.16f);

        [Header("Размеры")]
        [Tooltip("Кольцо рисуется по НАСТОЯЩЕМУ радиусу тела из симуляции. " +
                 "Множитель только добавляет каёмку, чтобы обод не резался телом.")]
        public float RingScale = 1.12f;

        [Tooltip("Сколько секунд цель считается подсвеченной после удара по ней.")]
        public float TargetHighlight = 0.35f;

        [Tooltip("Потолок колец. Больше в кадре и не нужно: дальние всё равно не читаются.")]
        public int MaxRings = 96;

        [Tooltip("Длительность короткого импульса ПКМ. Диапазон удерживает метку быстрой, как в MOBA.")]
        [Range(0.45f, 0.65f)]
        public float MovePingDuration = 0.56f;

        [Tooltip("Размер наземной метки приказа относительно стандартного.")]
        [Range(0.65f, 1.35f)]
        public float MovePingDiameter = 0.98f;

        [Tooltip("Интервал повторного пинга, пока ПКМ удерживается над землёй.")]
        [Range(0.30f, 0.65f)]
        public float MovePingRepeatInterval = 0.42f;

        private TickDriver _driver;
        private ArenaView _arena;

        private Transform _ringRoot;
        private SpriteRenderer[] _rings;
        private SpriteRenderer _arc;
        private Mesh[] _moveMarkerMeshes;
        private MeshRenderer[] _moveMarkerRenderers;
        private Vector3[][] _moveMarkerVertices;
        private Material _moveMarkerMaterial;
        private bool _moveMarkerTextureReady;
        private LayoutView _layout;
        private LineRenderer _landingRing;

        private Sprite _ringSprite;
        private Sprite _arcSprite;
        private Material _orderLineMaterial;
        private static readonly Vector2[] MoveDirections =
            { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
        private static readonly float[] MoveRotations = { 0f, -90f, 180f, 90f };
        private const int MoveMeshResolution = 6;
        private const float MoveSurfaceClearance = 0.12f;

        // Метка приказа — короткая реакция на сам клик, а не отображение
        // долгоживущего приказа из симуляции. Поэтому здесь хранится только
        // зафиксированная в момент нажатия точка и время визуального импульса.
        private Vector3 _movePingPosition;
        private float _movePingStartedAt = -99f;
        private float _nextMovePingAt = float.PositiveInfinity;
        private bool _movePingHeldLastFrame;

        // Кого игрок ударил последним и когда. Подсветка живёт доли секунды:
        // постоянная метка цели превратилась бы в прицел, а прицела в этой
        // игре нет — цель выбирает симуляция.
        private int _lastTarget = -1;
        private float _lastTargetAt = -99f;

        private Texture2D _attackCursor;
        private bool _cursorIsAttack;

        private bool _ready;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _arena = GetComponent<ArenaView>();
        }

        private void Build()
        {
            _ready = true;

            _ringSprite = MakeRingSprite(128, 0.66f);
            _arcSprite = MakeArcSprite(160, Simulation.AutoAttackArcCos.ToFloat());
            _ringRoot = new GameObject("Пул: опознаватели").transform;
            _ringRoot.SetParent(transform, false);

            _rings = new SpriteRenderer[Mathf.Max(1, MaxRings)];
            for (int i = 0; i < _rings.Length; i++)
                _rings[i] = MakeFlatSprite(_ringRoot, _ringSprite, "Кольцо " + i, -120);

            _arc = MakeFlatSprite(_ringRoot, _arcSprite, "Сектор удара", -140);
            BuildMoveOrderMarker();

            Debug.Log($"[Разлом] Опознаватели собраны: колец {_rings.Length}, " +
                      $"сектор {(_arcSprite != null ? "есть" : "НЕТ")}, курсор готов.");
        }

        private void LateUpdate()
        {
            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                _arena?.SetHoveredEntity(-1);
                if (_ready) HideAll();
                return;
            }

            if (!_ready) Build();

            TrackAttacks();
            _arena?.SetHoveredEntity(_driver.HoveredEntity);
            DrawFootRings(sim);
            DrawAttackArc(sim);
            DrawMoveOrder();
            DrawLandingRing(sim);
        }

        /// <summary>Запоминает, кого игрок ударил в этом кадре.</summary>
        private void TrackAttacks()
        {
            IReadOnlyList<SimEvent> events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Attack) continue;
                if (e.Source != Simulation.PlayerId) continue;

                _lastTarget = e.Target;
                _lastTargetAt = Time.unscaledTime;
            }
        }

        private void DrawFootRings(Simulation sim)
        {
            int used = 0;
            if (ShowFootRings)
            {
                bool highlightAlive = Time.unscaledTime - _lastTargetAt < TargetHighlight;
                EntityStore entities = sim.Entities;
                int hovered = _driver.HoveredEntity;

                // Назначенная цель подсвечена ПОСТОЯННО, пока приказ жив:
                // игрок должен видеть, кого персонаж добивает, а не гадать
                // по вспышкам последнего удара.
                int ordered = sim.AttackTarget;

                for (int i = 0; i < entities.Count && used < _rings.Length; i++)
                {
                    if (!entities.Alive[i]) continue;

                    bool isPlayer = i == Simulation.PlayerId;
                    bool isTarget = i == ordered || (highlightAlive && i == _lastTarget);

                    bool isHovered = i == hovered;

                    SpriteRenderer ring = _rings[used++];
                    ring.enabled = true;

                    // Порядок важностей: кого бью > на кого навёл > кто это вообще.
                    ring.color = isTarget ? TargetColor
                        : isHovered ? HoverColor
                        : isPlayer ? AllyColor
                        : EnemyColor;

                    // Диаметр — из симуляции. Кольцо и есть тот самый «хитбокс»:
                    // тела расталкиваются ровно по этому радиусу, и нарисованное
                    // не может разъехаться с настоящим.
                    float diameter = entities.BodyRadius[i].ToFloat() * 2f * RingScale;

                    // Цель и наведённый крупнее: цвет читается хуже размера,
                    // когда на экране двадцать красных колец.
                    if (isTarget) diameter *= 1.10f;
                    if (isHovered)
                        diameter *= 1.15f + 0.035f * Mathf.Sin(Time.unscaledTime * 12f);

                    Place(ring.transform, _driver.GetRenderPosition(i), diameter);
                }
            }

            for (int i = used; i < _rings.Length; i++)
                if (_rings[i].enabled) _rings[i].enabled = false;
        }

        /// <summary>
        /// Сектор автоатаки под игроком. Именно СЕКТОР, а не круг: бить можно
        /// только вперёд, и круг обманывал бы — игрок решил бы, что достаёт
        /// и за спину.
        /// </summary>
        private void DrawAttackArc(Simulation sim)
        {
            bool show = ShowAttackArc && sim.Entities.Alive[Simulation.PlayerId] && _driver.AttackHeld;
            _arc.enabled = show;
            if (!show) return;

            _arc.color = ArcColor;

            Vector3 at = _driver.GetRenderPosition(Simulation.PlayerId);
            float diameter = Simulation.AutoAttackRange.ToFloat() * 2f;

            FixVec2 facing = sim.Entities.Facing[Simulation.PlayerId];
            Vector3 forward = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat());

            Transform t = _arc.transform;
            t.position = new Vector3(at.x, 0.015f, at.z);
            t.localScale = Vector3.one * diameter;

            // Спрайт нарисован сектором вокруг своего +Y. LookRotation ставит
            // +Z по направлению взгляда, поворот на 90° вокруг X кладёт спрайт
            // на землю и переводит его +Y в это направление.
            t.rotation = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// Куда игрока унесёт крюк.
        ///
        /// Бросок якоря швыряет через пол-арены, и без метки приземление
        /// читается как «меня куда-то дёрнуло». Кольцо стоит на цели весь
        /// полёт и гаснет вместе с ним — точка назначения берётся прямо из
        /// состояния тела, поэтому она не может разойтись с тем, куда тело
        /// на самом деле едет.
        /// </summary>
        private void DrawLandingRing(Simulation sim)
        {
            if (_landingRing == null) return;

            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            bool flying = e.ForcedTicksLeft[player] > 0
                          && e.ForcedKind[player] == (byte)ForcedMotionKind.Lunge;

            if (!flying)
            {
                if (_landingRing.enabled) _landingRing.enabled = false;
                return;
            }

            FixVec2 target = e.ForcedTarget[player];
            Vector3 centre = new Vector3(target.X.ToFloat(), 0f, target.Y.ToFloat());

            _landingRing.enabled = true;
            _landingRing.startColor = LandingColor;
            _landingRing.endColor = LandingColor;
            WriteCircle(_landingRing, centre, 0.035f, e.BodyRadius[player].ToFloat() * 1.45f);
        }

        private void DrawMoveOrder()
        {
            float now = Time.unscaledTime;
            float repeatInterval = Mathf.Clamp(MovePingRepeatInterval, 0.30f, 0.65f);
            bool mouseOrder = !GameUserSettings.WasdMovement && !_driver.UsingGamepad;
            bool moveHeld = mouseOrder && _driver.MoveOrderHeld;
            bool moveSeriesStarted = mouseOrder && (_driver.MoveOrderPressedThisFrame
                                     || (moveHeld && !_movePingHeldLastFrame));

            // Первый импульс появляется сразу на фронте ПКМ. При удержании
            // повторяем его редко и фиксируем уже актуальную точку курсора:
            // это читается как серия приказов, но не превращается в спам 60 раз/с.
            if (moveSeriesStarted)
            {
                RestartMovePing(now);
                _nextMovePingAt = now + repeatInterval;
            }
            else if (moveHeld && now >= _nextMovePingAt)
            {
                RestartMovePing(now);
                _nextMovePingAt = now + repeatInterval;
            }

            // Отпускание ПКМ или наведение на врага разрывает серию. Новый
            // наземный приказ снова начнётся немедленным фронтовым импульсом.
            if (!moveHeld)
                _nextMovePingAt = float.PositiveInfinity;
            _movePingHeldLastFrame = moveHeld;

            float duration = Mathf.Clamp(MovePingDuration, 0.45f, 0.65f);
            float normalizedAge = (now - _movePingStartedAt) / duration;
            bool show = ShowMoveOrder && _moveMarkerTextureReady
                && normalizedAge >= 0f && normalizedAge < 1f
                && !_driver.GameplayPaused && !MainMenuView.IsOpen
                && CampServicesView.Instance?.IsOpen != true
                && CampPlayerView.Instance?.InventoryOpen != true;
            SetMoveOrderVisible(show);
            if (!show) return;
            AnimateMoveMarker(normalizedAge);
        }

        private void RestartMovePing(float now)
        {
            FixVec2 clicked = _driver.CursorWorld;
            _movePingPosition = new Vector3(clicked.X.ToFloat(), 0f, clicked.Y.ToFloat());
            _movePingStartedAt = now;
        }

        private void BuildMoveOrderMarker()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            _orderLineMaterial = new Material(shader)
            {
                name = "Кольцо приземления: материал"
            };
            BuildMoveMarkerWorld();

            // Кольцо приземления. Отдельное от метки приказа намеренно: та
            // живёт своей серией импульсов с повторами и затуханием, и
            // вплетать в неё чужое состояние значило бы получить два хозяина
            // у одного объекта.
            _landingRing = MakeOrderLine("Кольцо приземления", true, 40, 0.045f);

            SetMoveOrderVisible(false);
        }

        private void BuildMoveMarkerWorld()
        {
            Texture2D texture = Resources.Load<Texture2D>("VFX/MoveOrderMarker");
            _moveMarkerTextureReady = texture != null;
            if (!_moveMarkerTextureReady)
            {
                Debug.LogError("[Разлом] Не найдена текстура VFX/MoveOrderMarker.");
                return;
            }
            Shader shader = Resources.Load<Shader>("Shaders/MoveOrderGround");
            if (shader == null) shader = Shader.Find("Razlom/Move Order Ground");
            if (shader == null)
            {
                Debug.LogError("[Разлом] Не найден шейдер Razlom/Move Order Ground.");
                _moveMarkerTextureReady = false;
                return;
            }
            _moveMarkerMaterial = new Material(shader) { name = "Метка приказа: зелёные штрихи" };
            _moveMarkerMaterial.SetTexture("_MainTex", texture);
            _moveMarkerMeshes = new Mesh[4];
            _moveMarkerRenderers = new MeshRenderer[4];
            _moveMarkerVertices = new Vector3[4][];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Метка приказа: штрих " + i,
                    typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(_ringRoot, false);
                var mesh = MakeMoveMarkerMesh();
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = _moveMarkerMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _moveMarkerMeshes[i] = mesh;
                _moveMarkerRenderers[i] = renderer;
                _moveMarkerVertices[i] = new Vector3[MoveMeshResolution * MoveMeshResolution];
            }
        }

        private static Mesh MakeMoveMarkerMesh()
        {
            int side = MoveMeshResolution;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float u = (float)x / (side - 1);
                float v = (float)z / (side - 1);
                uv[z * side + x] = new Vector2(0.265f + u * 0.47f, 0.265f + v * 0.47f);
            }
            int cursor = 0;
            for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = z * side + x;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;
                triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
            }
            var mesh = new Mesh { name = "Метка приказа: поверхность штриха" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            return mesh;
        }

        private void AnimateMoveMarker(float age)
        {
            float scale = Mathf.Clamp(MovePingDiameter, 0.65f, 1.35f);
            float inward = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.22f));
            float rebound = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 0.22f) / 0.20f));
            float exit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 0.63f) / 0.37f));
            float spread = (Mathf.Lerp(0.49f, 0.14f, inward) + 0.13f * rebound + 0.07f * exit) * scale;
            float diameter = 0.38f * scale;
            float alpha = 1f - Mathf.SmoothStep(0.62f, 1f, age);
            _moveMarkerMaterial.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            for (int i = 0; i < _moveMarkerMeshes.Length; i++)
            {
                _moveMarkerRenderers[i].transform.position =
                    new Vector3(_movePingPosition.x, 0f, _movePingPosition.z);
                _moveMarkerRenderers[i].transform.rotation = Quaternion.identity;
                Vector2 offset = MoveDirections[i] * spread;
                float radians = MoveRotations[i] * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
                Vector3[] vertices = _moveMarkerVertices[i];
                for (int z = 0; z < MoveMeshResolution; z++)
                for (int x = 0; x < MoveMeshResolution; x++)
                {
                    float localX = ((float)x / (MoveMeshResolution - 1) - 0.5f) * diameter;
                    float localZ = ((float)z / (MoveMeshResolution - 1) - 0.5f) * diameter;
                    float worldX = _movePingPosition.x + offset.x + localX * cosine - localZ * sine;
                    float worldZ = _movePingPosition.z + offset.y + localX * sine + localZ * cosine;
                    vertices[z * MoveMeshResolution + x] = new Vector3(worldX - _movePingPosition.x,
                        MoveSurfaceHeight(worldX, worldZ) + MoveSurfaceClearance,
                        worldZ - _movePingPosition.z);
                }
                Mesh mesh = _moveMarkerMeshes[i];
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
        }

        private float MoveSurfaceHeight(float x, float z)
        {
            var camp = CampPlayerView.Instance;
            if (camp != null && camp.Active) return camp.SurfaceHeight(x, z);
            if (_layout == null) _layout = FindAnyObjectByType<LayoutView>();
            return _layout != null ? _layout.WeaponGroundHeight(x, z) : 0f;
        }

        private LineRenderer MakeOrderLine(string objectName, bool loop, int points, float width)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(_ringRoot, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _orderLineMaterial;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sortingOrder = 220;
            line.enabled = false;
            return line;
        }

        private static void WriteCircle(LineRenderer line, Vector3 center, float y, float radius)
        {
            int count = line.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                line.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    y,
                    center.z + Mathf.Sin(angle) * radius));
            }
        }

        private void SetMoveOrderVisible(bool visible)
        {
            if (_moveMarkerRenderers == null) return;
            for (int i = 0; i < _moveMarkerRenderers.Length; i++)
                _moveMarkerRenderers[i].enabled = visible && _moveMarkerTextureReady;
        }

        private void HideAll()
        {
            for (int i = 0; i < _rings.Length; i++) _rings[i].enabled = false;
            _arc.enabled = false;
            SetMoveOrderVisible(false);
            _nextMovePingAt = float.PositiveInfinity;
            _movePingHeldLastFrame = false;
            _arena?.SetHoveredEntity(-1);
        }

        /// <summary>
        /// Курсор над врагом становится боевым.
        ///
        /// Это второй канал того же сообщения, что и подсветка кольцом, и он
        /// нужен именно как второй: кольцо под ногами живёт на полу и теряется
        /// в толпе, а курсор всегда там, куда игрок и смотрит.
        /// </summary>
        private void OnDisable()
        {
            _arena?.SetHoveredEntity(-1);
            SetMoveOrderVisible(false);
        }

        private void OnDestroy()
        {
            if (_orderLineMaterial != null) Destroy(_orderLineMaterial);
            if (_moveMarkerMaterial != null) Destroy(_moveMarkerMaterial);
            if (_moveMarkerMeshes != null)
                foreach (Mesh mesh in _moveMarkerMeshes)
                    if (mesh != null) Destroy(mesh);
        }

        /// <summary>Кладёт вспомогательный спрайт ровно над плоским полом.</summary>
        private static void Place(Transform t, Vector3 at, float diameter)
        {
            // Чуть выше нуля: ровно на полу спрайт дерётся с плитой за глубину
            // и мерцает.
            t.position = new Vector3(at.x, 0.02f, at.z);
            t.rotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = Vector3.one * diameter;
        }

        private static SpriteRenderer MakeFlatSprite(Transform root, Sprite sprite, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>Кольцо. thickness — доля радиуса, занятая ободком.</summary>
        private static Sprite MakeRingSprite(int size, float innerRatio)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Мягкие края с обеих сторон ободка: жёсткий край на полу
                    // выглядит как ступенька, особенно на наклонной камере.
                    float outer = 1f - Mathf.SmoothStep(0.92f, 1.0f, r);
                    float inner = Mathf.SmoothStep(innerRatio - 0.08f, innerRatio, r);
                    byte a = (byte)(Mathf.Clamp01(outer * inner) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>
        /// Сектор вокруг оси +Y текстуры. arcCos — косинус ПОЛОВИНЫ угла,
        /// ровно та же величина, по которой симуляция выбирает цель.
        /// </summary>
        private static Sprite MakeArcSprite(int size, float arcCos)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    byte a = 0;
                    if (r <= 1f && r > 0.0001f)
                    {
                        float cos = dy / r;                       // ось сектора — это +Y
                        if (cos >= arcCos)
                        {
                            // Гасим к внешнему краю и у боковых границ: заливка
                            // с резкой каймой читается как объект, а не как зона.
                            float edge = 1f - Mathf.SmoothStep(0.80f, 1.0f, r);
                            float side = Mathf.SmoothStep(arcCos, Mathf.Min(1f, arcCos + 0.18f), cos);
                            a = (byte)(Mathf.Clamp01(edge * side) * 255f);
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
