using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    // Взаимодействия сцены и карта ходьбы лагеря; Пелагом управляет обычная боевая симуляция.
    [DefaultExecutionOrder(-20)]
    public sealed class CampPlayerView : MonoBehaviour
    {
        public static CampPlayerView Instance { get; private set; }
        public Vector3 Position => Body != null ? Body.position : _start;
        internal Vector3 InteractionPosition => Active ? World(_driver.Session.CampSim.Entities.Position[0]) : Position;
        internal Transform Body => _arena != null && _arena.TryGetEntityView(Simulation.PlayerId, out var body) ? body : null;
        public bool Active => _driver != null && _driver.Session != null && _driver.Session.Mode == GameMode.Camp && !_driver.Session.OnProvingGround;
        public bool InventoryOpen => _inventory != null && _inventory.IsOpen;
        public bool EntranceOpen => _entrance != null && _entrance.IsOpen;
        public bool InputBlocked => _walkMap == null || InventoryOpen || EntranceOpen || CampTransition.Busy || CampServicesView.Instance?.IsOpen == true || CampPreparationView.Instance?.IsOpen == true || CampForgeView.Instance?.IsOpen == true || CampServicesView.ConsumedFrame == Time.frameCount || CampRiftEntrance.ClosedFrame == Time.frameCount || CampPreparationView.ClosedFrame == Time.frameCount || CampForgeView.ClosedFrame == Time.frameCount;
        internal CampWalkMap WalkMap => _walkMap;
        internal IReadOnlyList<CampNavigationGeometry.Footprint> NavigationFootprints => _navigationFootprints;
        List<CampNavigationGeometry.Footprint> _navigationFootprints;
        internal Bounds MapBounds { get; private set; }
        public Transform Tent { get; private set; }
        TickDriver _driver; ArenaView _arena;
        float _height;
        CampRiverPassage _riverPassage;
        public float SurfaceHeight(float x,float z)
        {
            if(_riverPassage==null)_riverPassage=FindAnyObjectByType<CampRiverPassage>();
            return _riverPassage!=null?_riverPassage.SurfaceHeight(x,z,_height):_height;
        }
        CampWalkMap _walkMap;
        public float GroundHeight => _height;
        CampRoute _routing;
        CampInventoryView _inventory;
        NavMeshDataInstance _navigation; bool _approach;
        Bounds _tentBounds; Vector3 _start;
        /// <summary>Вход в палатку: объект «Вход в палатку» в сцене под Tent - Player, иначе середина передней стороны.</summary>
        Transform _tentEntrance;
        /// <summary>Сколько до входа, чтобы палатка открылась (владелец 23 сентября: «прям подойти надо»).</summary>
        const float TentReach = 1.3f;
        CampRiftEntrance _entrance;
        Vector2 _pressPointer;
        GameObject _scenePelagPreview; bool _scenePelagPreviewWasActive;
        GameObject _navigationGround;

        void Start()
        {
            Instance = this; _driver = GetComponent<TickDriver>();
            var world = FindAnyObjectByType<SceneWorldView>();
            if (world == null || world.CampRoot == null) { enabled = false; return; }
            Transform root = world.CampRoot.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.Replace(" ", "").ToLowerInvariant();
                if (n == "tent-player") Tent = t;
                if (t.name == "Anchor - Player") _start = t.position;

            }
            // Старый контейнер палатки сохраняет имя Tent - Player; импорт лежит под ним.
            if (Tent != null)
            {
                foreach (Transform child in Tent.GetComponentsInChildren<Transform>())
                    if (child.name.StartsWith("camping+tent")) { Tent = child; break; }
                bool first = true;
                foreach (Renderer r in Tent.GetComponentsInChildren<Renderer>())
                { if (first) { _tentBounds = r.bounds; first = false; } else _tentBounds.Encapsulate(r.bounds); }
                if (first) _tentBounds = new Bounds(Tent.position, Vector3.one * 2);
                _tentEntrance = FindTentEntrance(root);
            }
            _entrance = root.GetComponentInChildren<CampRiftEntrance>();
            BuildNavigation(root);
            _arena = FindAnyObjectByType<ArenaView>();
            if (NavMesh.SamplePosition(_start, out var spawn, 10f, NavMesh.AllAreas)) _start = spawn.position;
            _height = _start.y;
            _riverPassage=FindAnyObjectByType<CampRiverPassage>();
            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices.Length == 0) { Debug.LogError("[camp] No walkable surface."); enabled = false; return; }
            var bounds = new Bounds(_start, Vector3.zero);
            foreach (var vertex in triangulation.vertices) bounds.Encapsulate(vertex);
            MapBounds = bounds;
            const float cell = .125f;
            Vector3 origin = new Vector3(Mathf.Floor(bounds.min.x), _height, Mathf.Floor(bounds.min.z));
            int width = Mathf.CeilToInt((bounds.max.x-origin.x)/cell)+1;
            int height = Mathf.CeilToInt((bounds.max.z-origin.z)/cell)+1;
            var cells = new bool[width*height];
            for (int z=0;z<height;z++) for (int x=0;x<width;x++)
            {
                Vector3 point = origin + new Vector3((x+.5f)*cell,0,(z+.5f)*cell);
                cells[z*width+x] = NavMesh.SamplePosition(point,out var floor,.35f,NavMesh.AllAreas)
                    && Mathf.Abs(floor.position.y-_height)<.3f
                    // Построенный пол на 5 см ниже точки появления. Горизонтальное
                    // смещение проверяется отдельно: разница высоты не является стеной.
                    && (new Vector2(floor.position.x-point.x,floor.position.z-point.z)).sqrMagnitude < .0025f;
            }
            _riverPassage?.StraightenWalkCells(cells, origin, cell, width, height);
            var map = new CampWalkMap(Flat(origin), Fix64.Ratio(1,8), width,height,cells);
            _walkMap = map;
            _routing = new CampRoute(map);
            // Выбираем проходимую клетку, чтобы округление у края не заперло точку появления.
            if (!map.Contains(Flat(_start)) && map.TryNearestReachable(Flat(_start), Flat(_start), out var nearestSpawn))
                _start = new Vector3(nearestSpawn.X.ToFloat(), _height, nearestSpawn.Y.ToFloat());
            _driver.Session.ConfigureCampWorld(Flat(_start),map);
            root.GetComponent<CampTrainingView>()?.Initialize(_driver);
            _scenePelagPreview = GameObject.Find("Pelag_MX_Idle");
            if(_scenePelagPreview != null) { _scenePelagPreviewWasActive=_scenePelagPreview.activeSelf; _scenePelagPreview.SetActive(false); }
            _inventory = gameObject.AddComponent<CampInventoryView>(); _inventory.Initialize(_driver);
            if (GetComponent<CampFootsteps>() == null) gameObject.AddComponent<CampFootsteps>();
            if(Tent!=null)
            {
                var interaction=Tent.GetComponent<CampServiceNpc>()??Tent.gameObject.AddComponent<CampServiceNpc>();
                interaction.Kind=CampServiceKind.Tent;interaction.Reach=TentReach;interaction.Entrance=_tentEntrance;
            }
            InstallOathBoard(root);
            gameObject.AddComponent<CampServicesView>().Initialize(this,_driver);
            CampNpcLife.Install(root);
            if (GetComponent<CampGuideView>() == null) gameObject.AddComponent<CampGuideView>();
            if (GetComponent<CampCharacterShadows>() == null) gameObject.AddComponent<CampCharacterShadows>();
            // Дымная завеса перехода создаётся и прогревается сейчас, а не в миг входа в арку.
            CampTransition.Prewarm();
            Debug.Log($"[camp] spawn={_start} sharedCombat=True tent={Tent} cells={width*height}");
        }

        // Проверка импорта в редакторе использует тот же каталог оснований, что игра.
        public static bool UsedByNavigation(MeshFilter mesh)
        {
            if (mesh == null) return false;
            var world = FindAnyObjectByType<SceneWorldView>();
            Transform root = world != null && world.CampRoot != null ? world.CampRoot.transform : mesh.transform.root;
            return CampNavigationGeometry.TryDescribe(mesh, root, 0, out _);
        }

        void BuildNavigation(Transform root)
        {
            if (_entrance != null) _entrance.BuildNavigationBarrier();
            var passage = FindAnyObjectByType<CampRiverPassage>();
            Transform bridge = passage != null ? passage.Bridge : null;
            // Видимый меш не становится коллайдером. Старым и новым моделям задаётся
            // простое основание; листва и ткань не могут молча превратиться в стены.
            var footprints = CampNavigationGeometry.Collect(root, bridge, _start.y);
            _navigationFootprints = footprints;

            // В текущем blockout якорь Пелага лежит чуть за краем единственного
            // декоративного Floor. Без этой площадки SamplePosition выбирает
            // ближайший треугольник прямо у костра, и огонь закрывает героя
            // в первом кадре. Площадка живёт только до построения NavMesh:
            // она не видна, не записывается в сцену и не меняет authored layout.
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f, groundY = _start.y;
            bool foundBounds = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<CampFlameProView>() != null) continue;
                if (renderer.GetComponentInParent<CampMagicDecoration>() != null) continue;
                if (renderer.GetComponentInParent<CampRiver>() != null || renderer.GetComponentInParent<CampSceneryDecoration>() != null) continue;
                // Unity возвращает editor-only объект «пустого» компонента: ?. его не отсекает.
                var mesh = renderer.GetComponent<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.name == "Объём луча арки") continue;
                Bounds bounds = renderer.bounds;
                if (!foundBounds)
                {
                    minX = bounds.min.x; maxX = bounds.max.x;
                    minZ = bounds.min.z; maxZ = bounds.max.z;
                    foundBounds = true;
                }
                else
                {
                    minX = Mathf.Min(minX, bounds.min.x); maxX = Mathf.Max(maxX, bounds.max.x);
                    minZ = Mathf.Min(minZ, bounds.min.z); maxZ = Mathf.Max(maxZ, bounds.max.z);
                }
                if (renderer.name.Equals("Floor", System.StringComparison.OrdinalIgnoreCase))
                {
                    groundY = bounds.max.y;
                }
            }
            if (!foundBounds)
            {
                minX = _start.x - 6f; maxX = _start.x + 6f;
                minZ = _start.z - 6f; maxZ = _start.z + 6f;
            }
            const float padding = 2f;
            minX -= padding; maxX += padding;
            minZ -= padding; maxZ += padding;
            _navigationGround = new GameObject("CampRuntimeWalkable");
            _navigationGround.transform.SetParent(root, true);
            _navigationGround.transform.position = new Vector3((minX + maxX) * .5f,
                groundY - .05f, (minZ + maxZ) * .5f);
            BoxCollider ground = _navigationGround.AddComponent<BoxCollider>();
            ground.size = new Vector3(Mathf.Max(maxX - minX, 4f), .1f,
                Mathf.Max(maxZ - minZ, 4f));
            Physics.SyncTransforms();
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            // Из физических коллайдеров берём только временный плоский пол и явную
            // границу за аркой. Коллайдеры декора не возвращаются в карту незаметно.
            NavMeshBuilder.CollectSources(_navigationGround.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            if (_entrance != null)
            {
                var gateSources = new List<NavMeshBuildSource>();
                var barrier = _entrance.transform.Find("Граница лагеря за аркой");
                if (barrier != null)
                {
                    NavMeshBuilder.CollectSources(barrier, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, gateSources);
                    sources.AddRange(gateSources);
                }
            }
            CampNavigationGeometry.AddSources(sources, footprints, groundY);
            // Новые NPC стоят в корне сцены: их маленькие опорные области тоже участвуют в обходе.
            foreach(var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Exclude))
            {
                // ServicesView обновляет присутствие после построения. Закрытый житель
                // не должен оставлять невидимое тело в неизменяемой карте.
                var camp = _driver.Session.Camp;
                if (npc.Kind == CampServiceKind.Trader && !camp.HasResident(CampResident.Trader)
                    || npc.Kind == CampServiceKind.Alchemist && !camp.HasResident(CampResident.Alchemist)
                    || npc.Kind == CampServiceKind.Tent || npc.Kind == CampServiceKind.TravelTable) continue;
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,
                    transform=Matrix4x4.TRS(npc.transform.position+Vector3.up,Quaternion.identity,Vector3.one),size=new Vector3(.65f,3,.65f)});
            }
            foreach(var river in root.GetComponentsInChildren<CampRiver>())river.AddNavigationSources(sources);
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .3f; settings.agentHeight = 1.7f; settings.agentClimb = .25f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(root.position, Vector3.one * 250), Vector3.zero, Quaternion.identity);
            if (data != null) _navigation = NavMesh.AddNavMeshData(data);
            Destroy(_navigationGround);
            _navigationGround = null;
            Debug.Log($"[camp-navigation] simpleFootprints={footprints.Count} sources={sources.Count} visualMeshCollidersAdded=0");
        }

        bool _wasActive, _sawRift;

        void Update()
        {
            // Вернулись из забега мимо дымной завесы (пути разработчика) — лагерь проявляется из
            // тёплой пелены. Смена под завесой рассеивается сама: ReturnToCamp тогда ничего не делает.
            bool active = Active;
            if (!active && _driver?.Session != null && _driver.Session.Mode == GameMode.Rift) _sawRift = true;
            if (active && !_wasActive && _sawRift) { _sawRift = false; if (!CampIntegrationCapture.IsRunning) CampTransition.ReturnToCamp(); }
            _wasActive = active;
            if (!Active) { _inventory?.Close(); return; }
            if(CampServicesView.ConsumedFrame==Time.frameCount)return;
            if (!_driver.GameplayPaused && !InventoryOpen) _entrance?.Check(_driver, World(_driver.Session.CampSim.Entities.Position[0]));
            if (_driver.GameplayPaused || InputBlocked) { Stop(); return; }
            if (CampIntegrationCapture.IsRunning) return;
            bool interact; bool click; bool held; Vector2 pointer;
#if ENABLE_INPUT_SYSTEM
            interact = GameKeyBindings.Pressed(GameAction.Interact);
            // Здесь выбирается только интерактивный объект. Приказ движения
            // поступает из TickDriver вместе с удержанием и короткими тапами.
            click = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            held = Mouse.current != null && Mouse.current.rightButton.isPressed;
            pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            interact = GameKeyBindings.Pressed(GameAction.Interact);
            click = Input.GetMouseButtonDown(1); held = Input.GetMouseButton(1);
            pointer = Input.mousePosition;
#endif
            // Клик по сервису уже запускает маршрут в CampServicesView;
            // повторно превращать его в клик по земле нельзя.
            if (CampServicesView.PointerGesture) { click = false; held = false; }
            if (GameUserSettings.WasdMovement) { click = held = false; if(CampServicesView.Instance?.Pending==null)CancelRoute(); }

            if (_driver.PointerOverHud(pointer)) { click = false; held = false; }

            if (click && Camera.main != null && !CampInventoryView.PointerOverUI() && !CampTrainingView.PointerOverPanel(pointer))
                HandleWorldPress(pointer);

            // РУЛЕНИЕ УДЕРЖАНИЕМ ОТМЕНЯЕТ МАРШРУТ — но признаком удержания
            // служит СДВИГ КУРСОРА, а не время.
            //
            // Раньше маршрут снимался, если кнопку держали дольше 0.2 с. Это
            // ошибка: обычный клик легко держится дольше, и маршрут умирал сразу
            // после построения — герой шёл напрямую и упирался в препятствие.
            // Отсюда же и «не всегда»: успеет игрок отпустить кнопку или нет,
            // от препятствия не зависит.
            //
            // Тащат мышь — значит правда рулят; кликнули и держат, не двигая, —
            // это всё ещё клик.
            const float dragPixels = 40f;
            if (held && !click && (pointer - _pressPointer).sqrMagnitude > dragPixels * dragPixels)
                CancelRoute();
            if (_approach && NearTent()) { _approach = false; Stop(); _inventory.Open(); }

        }

        internal void HandleWorldPress(Vector2 pointer)
            {
                if (_driver.PointerOverPlayer(pointer))
                {
                    CancelRoute();
                    return;
                }
                Ray ray = Camera.main.ScreenPointToRay(pointer);
                bool hitWorld = Physics.Raycast(ray, out var hit, 300f);
                var floor = new Plane(Vector3.up, new Vector3(0, _height, 0));
                if (!hitWorld && !floor.Raycast(ray, out _)) { CancelRoute(); return; }

                _approach = hitWorld && Tent != null && hit.transform.IsChildOf(Tent);

                _pressPointer = pointer;

                // В палатку идём к её краю, в остальных случаях — ровно туда,
                // куда ткнули. Непроходимую точку разберёт сам поиск: он
                // приводит цель к ближайшей достижимой клетке.
                Vector3 target;
                if (hitWorld) target = hit.point;
                else { floor.Raycast(ray, out float distance); target = ray.GetPoint(distance); }
                RouteTo(_approach ? TentDoor : target);
            }

        /// <summary>
        /// Прокладывает маршрут по карте проходимости.
        ///
        /// СУЩЕСТВУЕТ, ЧТОБЫ ГЕРОЙ ОБХОДИЛ, А НЕ УПИРАЛСЯ. Приказ движения сам
        /// по себе задаёт лишь НАПРАВЛЕНИЕ: боевой мотор везёт тело по прямой,
        /// и первый же камень между героем и точкой клика останавливает его
        /// намертво. Поиск пути тут был, но им пользовался только подход к
        /// палатке — обычный ПКМ шёл мимо него.
        ///
        /// Само следование живёт в <see cref="CampRoute"/>, в симуляции: там
        /// его можно прогнать тестом вокруг настоящей стены, а здесь — только
        /// запустить игру и посмотреть глазами.
        /// </summary>
        internal bool RouteTo(Vector3 target)
        {
            if (_routing == null || _walkMap == null)
            {
                Debug.LogWarning("[camp-route] карты проходимости нет — иду напрямую.");
                return false;
            }

            FixVec2 from = Flat(InteractionPosition), to = Flat(target);
            bool routed = _routing.To(from, to);

            // ЗАМЕР, А НЕ ОТЛАДОЧНЫЙ МУСОР. Маршрут строится на карте, которую
            // видно только в игре, и «не обходит» может значить четыре разные
            // вещи: клик не дошёл, герой вне карты, цель вне карты, поиск не
            // связал их. Одна строка разделяет все четыре.
            Debug.Log($"[camp-route] откуда {Position.x:0.00},{Position.z:0.00}"
                + $" → {target.x:0.00},{target.z:0.00}"
                + $" | герой в карте={_walkMap.Contains(from)}"
                + $" цель в карте={_walkMap.Contains(to)}"
                + $" прямая={_walkMap.CanTravel(from, to)}"
                + $" углов={_routing.CornerCount} маршрут={routed}");

            return routed;
        }

        /// <summary>Достижимый подход, с запасом для остановки маршрута внутри радиуса разговора.</summary>
        public bool TryServiceApproach(CampServiceNpc npc, Vector3 from, out Vector3 reachable)
        {
            reachable = from;
            if (npc == null || _walkMap == null || !_walkMap.Contains(Flat(from))) return false;
            Vector3 approach = npc.Kind == CampServiceKind.Tent ? npc.Target(from) : npc.Approach;
            float reach = Mathf.Max(.05f, npc.Reach - .4f);
            for (float radius = 0; radius <= 1.5f; radius += .25f)
            for (int i = 0; i < (radius == 0 ? 1 : 16); i++)
            {
                Vector3 point = approach + new Vector3(Mathf.Cos(i * Mathf.PI / 8), 0, Mathf.Sin(i * Mathf.PI / 8)) * radius;
                if (npc.Distance(point) > reach || !_walkMap.TryNearestReachable(Flat(from), Flat(point), out var at)) continue;
                Vector3 resolved = World(at);
                if (npc.Distance(resolved) > reach) continue;
                reachable = resolved; return true;
            }
            return false;
        }

        void CancelRoute()
        {
            _routing?.Cancel();
            _approach = false;
            CampServicesView.Instance?.CancelPending();
        }
        static FixVec2 Flat(Vector3 p) => new FixVec2(Fix64.FromRaw((long)(p.x * Fix64.One.Raw)), Fix64.FromRaw((long)(p.z * Fix64.One.Raw)));
        Vector3 World(FixVec2 p) => new Vector3(p.X.ToFloat(), _height, p.Y.ToFloat());

        public void PrepareInput(ref InputFrame input)
        {
            if (!Active) return;
            if (input.Has(InputFlags.DirectMovement) && (input.MoveDirection.LengthSq>Fix64.Zero || CampServicesView.Instance?.Pending==null)) { CancelRoute(); return; }
            if (input.AbilityMask != 0 || input.Has(InputFlags.Attack)) CancelRoute();

            // Маршрут снимает перетаскивание мыши, и решается это в Update,
            // где виден курсор.
            if (_routing == null) return;
            if (!_routing.Advance(Flat(InteractionPosition), out FixVec2 aim, out bool final)) return;

            input.Aim = aim;
            input.Flags = CampRoute.FlagsFor(final);
            input.AttackTarget = -1;
        }
        void Stop()
        {
            CancelRoute();
            if (Active) _driver.Session.CampSim.StopPlayerMovement();
        }
        internal void StopForService()=>Stop();
        internal void OpenTent(){Stop();_inventory.Open();}

        /// <summary>
        /// Доска клятв у палатки (06.10): стойка «Доска клятв …» (или узел с «Картой владельца») получает своё взаимодействие —
        /// открывает палатку на «Клятвах». Сцена и префаб стойки не правятся. Подходить — к лицу карты: у стандартного
        /// квадрата лицо смотрит в −forward; свой «Подход к доске» под стойкой важнее.
        /// </summary>
        static void InstallOathBoard(Transform root)
        {
            Transform board = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                bool named = t.name.StartsWith("Доска клятв", System.StringComparison.Ordinal);
                if (named || (board == null && t.Find("Карта владельца") != null)) { board = t; if (named) break; }
            }
            if (board == null) return;
            if (!board.TryGetComponent(out CampServiceNpc stand)) stand = board.gameObject.AddComponent<CampServiceNpc>();
            stand.Kind = CampServiceKind.OathBoard; stand.Reach = 1.8f;
            Transform approach = board.Find("Подход к доске"), map = board.Find("Карта владельца");
            Vector3 face = map != null ? -map.forward : board.right; face.y = 0f;
            stand.ApproachOffset = approach != null ? approach.position - board.position : (face.sqrMagnitude > .0001f ? face.normalized : Vector3.back) * 1.1f;
        }
        public bool ApproachTent()
        {
            if (Tent == null) return false;
            _approach = true;
            Vector3 target = TentDoor;
            bool routed = RouteTo(target);
            Debug.Log($"[camp-path] corners={_routing?.CornerCount} from={Position} target={target}");
            return routed;
        }
        Vector3 TentDoor => _tentEntrance != null ? _tentEntrance.position : _tentBounds.ClosestPoint(Position);
        bool NearTent() { Vector3 d = TentDoor - Position; d.y = 0; return Tent != null && d.sqrMagnitude < TentReach * TentReach; }

        /// <summary>
        /// Точка входа в палатку. Владелец двигает её в сцене (объект «Вход в палатку»);
        /// без него — середина передней стороны модели, чуть внутри края.
        /// </summary>
        Transform FindTentEntrance(Transform campRoot)
        {
            foreach (Transform t in campRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "Вход в палатку") return t;
            var marker = new GameObject("Вход в палатку (по модели)").transform;
            marker.SetParent(Tent, true);
            var mesh = Tent.GetComponent<MeshFilter>();
            float depth = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds.extents.z * Tent.lossyScale.z : 1.5f;
            Vector3 centre = _tentBounds.center; centre.y = Tent.position.y;
            marker.position = centre + Tent.forward * depth * .92f;
            return marker;
        }

        void OnDestroy()
        {
            if (_navigation.valid) _navigation.Remove();
            if (_navigationGround != null) Destroy(_navigationGround);
            if (_scenePelagPreview != null) _scenePelagPreview.SetActive(_scenePelagPreviewWasActive);
            if (Instance == this) Instance = null;
        }
    }
}
