using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    // Camp scene interactions and navigation baking; the normal combat simulation owns Pelag.
    [DefaultExecutionOrder(-20)]
    public sealed class CampPlayerView : MonoBehaviour
    {
        public static CampPlayerView Instance { get; private set; }
        public Vector3 Position => Body != null ? Body.position : _start;
        internal Transform Body => _arena != null && _arena.TryGetEntityView(Simulation.PlayerId, out var body) ? body : null;
        public bool Active => _driver != null && _driver.Session != null && _driver.Session.Mode == GameMode.Camp && !_driver.Session.OnProvingGround;
        public bool InventoryOpen => _inventory != null && _inventory.IsOpen;
        public Transform Tent { get; private set; }
        TickDriver _driver; ArenaView _arena;
        float _height;
        CampWalkMap _walkMap;
        public float GroundHeight => _height;
        NavMeshPath _path;
        CampRoute _routing;
        CampInventoryView _inventory;
        NavMeshDataInstance _navigation; bool _approach;
        Bounds _tentBounds; Vector3 _start;
        Transform _exit; bool _approachExit;
        Vector2 _pressPointer;
        GameObject _scenePelagPreview; bool _scenePelagPreviewWasActive;
        GameObject _navigationGround;

        void Start()
        {
            _path = new NavMeshPath();
            Instance = this; _driver = GetComponent<TickDriver>();
            var world = FindAnyObjectByType<SceneWorldView>();
            if (world == null || world.CampRoot == null) { enabled = false; return; }
            Transform root = world.CampRoot.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.Replace(" ", "").ToLowerInvariant();
                if (n == "tent-player") Tent = t;
                if (t.name == "Anchor - Player") _start = t.position;
                if (t.name == "Anchor - Rift Portal") _exit = t;
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
            }
            BuildNavigation(root);
            _arena = FindAnyObjectByType<ArenaView>();
            if (NavMesh.SamplePosition(_start, out var spawn, 10f, NavMesh.AllAreas)) _start = spawn.position;
            _height = _start.y;
            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices.Length == 0) { Debug.LogError("[camp] No walkable surface."); enabled = false; return; }
            var bounds = new Bounds(_start, Vector3.zero);
            foreach (var vertex in triangulation.vertices) bounds.Encapsulate(vertex);
            const float cell = .125f;
            Vector3 origin = new Vector3(Mathf.Floor(bounds.min.x), _height, Mathf.Floor(bounds.min.z));
            int width = Mathf.CeilToInt((bounds.max.x-origin.x)/cell)+1;
            int height = Mathf.CeilToInt((bounds.max.z-origin.z)/cell)+1;
            var cells = new bool[width*height];
            for (int z=0;z<height;z++) for (int x=0;x<width;x++)
            {
                Vector3 point = origin + new Vector3((x+.5f)*cell,0,(z+.5f)*cell);
                cells[z*width+x] = NavMesh.SamplePosition(point,out var floor,.08f,NavMesh.AllAreas)
                    && Mathf.Abs(floor.position.y-_height)<.3f
                    && (floor.position-point).sqrMagnitude < .0025f;
            }
            var map = new CampWalkMap(Flat(origin), Fix64.Ratio(1,8), width,height,cells);
            _walkMap = map;
            _routing = new CampRoute(map);
            // Pick a valid snapshot cell, avoiding a spawn inside a rounded boundary cell.
            if (!map.Contains(Flat(_start)))
                for(int z=0;z<height;z++) for(int x=0;x<width;x++)
                { var candidate=origin+new Vector3((x+.5f)*cell,0,(z+.5f)*cell);
                  if(cells[z*width+x] && Vector3.Distance(candidate,_start)<.3f) _start=candidate; }
            _driver.Session.ConfigureCampWorld(Flat(_start),map);
            _scenePelagPreview = GameObject.Find("Pelag_MX_Idle");
            if(_scenePelagPreview != null) { _scenePelagPreviewWasActive=_scenePelagPreview.activeSelf; _scenePelagPreview.SetActive(false); }
            _inventory = gameObject.AddComponent<CampInventoryView>(); _inventory.Initialize(_driver);
            Debug.Log($"[camp] spawn={_start} sharedCombat=True tent={Tent} cells={width*height}");
        }

        /// <summary>
        /// Меш числится в LODGroup уровнем ДАЛЬШЕ нулевого.
        ///
        /// LODGroup выключает Renderer, но не сам объект, поэтому
        /// GetComponentsInChildren&lt;MeshFilter&gt; возвращает все четыре уровня ели.
        /// Раньше коллайдер вешался на каждый, и в NavMesh уходило объединение
        /// LOD0..LOD3. Дальние уровни — огрублённые силуэты, они ШИРЕ того, что
        /// игрок видит на экране, и перекрывали проходы там, где визуально
        /// пусто. Это и есть «невидимые препятствия» в лагере.
        ///
        /// Навигацию строит только LOD0: он совпадает с картинкой вблизи.
        /// </summary>
        static bool IsDistantLod(MeshFilter mesh)
        {
            LODGroup group = mesh.GetComponentInParent<LODGroup>();
            if (group == null) return false;
            LOD[] levels = group.GetLODs();
            if (levels.Length == 0) return false;

            Renderer own = mesh.GetComponent<Renderer>();
            if (own == null) return false;

            foreach (Renderer renderer in levels[0].renderers)
                if (renderer == own) return false;

            return true;
        }

        /// <summary>
        /// Пойдёт ли этот меш в навигацию лагеря.
        ///
        /// Отбор живёт в одном месте, потому что им пользуется ещё и редакторный
        /// инструмент, разрешающий чтение мешей. Разъехавшись, они дали бы
        /// худший из возможных результатов: чтение включено не тем мешам, а
        /// навигация в сборке всё равно другая.
        /// </summary>
        public static bool UsedByNavigation(MeshFilter mesh)
            => mesh.sharedMesh != null
               && mesh.GetComponent<Collider>() == null
               && mesh.GetComponentInParent<CampGroundStudy>() == null
               && !IsDistantLod(mesh);

        void BuildNavigation(Transform root)
        {
            // Добавляем недостающие коллизии только runtime: сохранённые трансформы не затрагиваются.
            var unreadable = new List<string>();
            foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                if (!UsedByNavigation(mesh)) continue;

                // Нечитаемый меш строит коллайдер в редакторе и НЕ строит в
                // плеере: данные выгружены из памяти после загрузки на карту.
                // Молча это пропустить нельзя — навигация в сборке отличалась
                // бы от того, что видно в Play mode.
                if (!mesh.sharedMesh.isReadable) unreadable.Add(mesh.sharedMesh.name);

                var collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh;
            }
            if (unreadable.Count > 0)
            {
                // Одна строка вместо два десятка одинаковых предупреждений от
                // самой Unity, и сразу с тем, что нажать.
                unreadable.Sort();
                Debug.LogWarning($"[camp] Навигация собрана из {unreadable.Count} нечитаемых мешей "
                    + "— в собранной игре их не будет. Меню «Разлом → Лагерь → "
                    + $"Разрешить чтение мешей навигации». Список: {string.Join(", ", unreadable)}");
            }

            // В текущем blockout якорь Пелага лежит чуть за краем единственного
            // декоративного Floor. Без этой площадки SamplePosition выбирает
            // ближайший треугольник прямо у костра, и огонь закрывает героя
            // в первом кадре. Площадка живёт только до построения NavMesh:
            // она не видна, не записывается в сцену и не меняет authored layout.
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f, groundY = _start.y;
            bool foundBounds = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
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
            NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .3f; settings.agentHeight = 1.7f; settings.agentClimb = .25f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(root.position, Vector3.one * 250), Vector3.zero, Quaternion.identity);
            if (data != null) _navigation = NavMesh.AddNavMeshData(data);
            Destroy(_navigationGround);
            _navigationGround = null;
        }

        void Update()
        {
            if (!Active) { _inventory?.Close(); return; }
            if (_driver.GameplayPaused || InventoryOpen) { Stop(); return; }
            bool interact; bool click; bool held; bool switchBranch; Vector2 pointer;
#if ENABLE_INPUT_SYSTEM
            interact = Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame;
            switchBranch = Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
            // Здесь выбирается только интерактивный объект. Приказ движения
            // поступает из TickDriver вместе с удержанием и короткими тапами.
            click = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            held = Mouse.current != null && Mouse.current.rightButton.isPressed;
            pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            interact = Input.GetKeyDown(KeyCode.I);
            switchBranch = Input.GetKeyDown(KeyCode.B);
            click = Input.GetMouseButtonDown(1); held = Input.GetMouseButton(1);
            pointer = Input.mousePosition;
#endif
            if (switchBranch) SwitchBranch();
            if (interact && NearTent()) { Stop(); _inventory.Open(); return; }
            if (interact && NearExit()) { Stop(); _driver.Session.EnterRift(); return; }
            if (click && Camera.main != null && !CampInventoryView.PointerOverUI())
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
            if (_approachExit && NearExit()) { _approachExit = false; Stop(); _driver.Session.EnterRift(); }
        }

        internal void HandleWorldPress(Vector2 pointer)
            {
                if (_driver.PointerOverPlayer(pointer))
                {
                    CancelRoute();
                    return;
                }
                Ray ray = Camera.main.ScreenPointToRay(pointer);
                if (!Physics.Raycast(ray, out var hit, 300f)) { CancelRoute(); return; }

                _approach = Tent != null && hit.transform.IsChildOf(Tent);
                _approachExit = _exit != null && Vector3.Distance(hit.point, _exit.position) < 1.5f;
                _pressPointer = pointer;

                // В палатку идём к её краю, в остальных случаях — ровно туда,
                // куда ткнули. Непроходимую точку разберёт сам поиск: он
                // приводит цель к ближайшей достижимой клетке.
                RouteTo(_approach ? _tentBounds.ClosestPoint(Position) : hit.point);
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
        bool RouteTo(Vector3 target)
        {
            if (_routing == null || _walkMap == null)
            {
                Debug.LogWarning("[camp-route] карты проходимости нет — иду напрямую.");
                return false;
            }

            FixVec2 from = Flat(Position), to = Flat(target);
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

        void CancelRoute()
        {
            _routing?.Cancel();
            _approach = _approachExit = false;
        }
        static FixVec2 Flat(Vector3 p) => new FixVec2(Fix64.FromRaw((long)(p.x * Fix64.One.Raw)), Fix64.FromRaw((long)(p.z * Fix64.One.Raw)));
        Vector3 World(FixVec2 p) => new Vector3(p.X.ToFloat(), _height, p.Y.ToFloat());

        public void PrepareInput(ref InputFrame input)
        {
            if (!Active) return;
            if (input.AbilityMask != 0 || input.Has(InputFlags.Attack)) CancelRoute();

            // Маршрут снимает перетаскивание мыши, и решается это в Update,
            // где виден курсор.
            if (_routing == null) return;
            if (!_routing.Advance(Flat(Position), out FixVec2 aim, out bool final)) return;

            input.Aim = aim;
            input.Flags = CampRoute.FlagsFor(final);
            input.AttackTarget = -1;
        }
        void Stop()
        {
            CancelRoute();
            if (Active) _driver.Session.CampSim.StopPlayerMovement();
        }
        public bool ApproachTent()
        {
            if (Tent == null) return false;
            _approach = true;
            Vector3 target = _tentBounds.ClosestPoint(Position);
            bool routed = RouteTo(target);
            Debug.Log($"[camp-path] corners={_routing?.CornerCount} from={Position} target={target}");
            return routed;
        }
        bool NearTent() { Vector3 d = _tentBounds.ClosestPoint(Position) - Position; d.y = 0; return Tent != null && d.sqrMagnitude < 2.25f; }
        bool NearExit() => _exit != null && Vector3.Distance(Position,_exit.position) < 1.7f;
        /// <summary>
        /// Меняет боевую ветку Пелага.
        ///
        /// ВРЕМЕННЫЙ ВХОД — КЛАВИША, А НЕ ЭКРАН. Выбор ветки принадлежит
        /// лагерю и должен когда-нибудь стоять в экране снаряжения рядом с
        /// талантами. Но экран снаряжения — собранный кодом интерфейс, и
        /// врезать в него кнопку значит трогать художественную часть, которая
        /// сейчас в работе. Клавиша даёт решение игроку сегодня и не мешает
        /// сделать нормальный экран завтра.
        /// </summary>
        void SwitchBranch()
        {
            if (_driver.Session?.Camp == null) return;

            Camp camp = _driver.Session.Camp;
            camp.SelectBranch(camp.Branch == CombatBranch.Sabre
                ? CombatBranch.Anchor
                : CombatBranch.Sabre);

            // Набор сам по себе применяется на входе в забег. Без этого игрок
            // увидел бы новые кнопки только со следующего Разлома.
            _driver.RefreshAbilityBuild();
        }

        string BranchName()
        {
            Camp camp = _driver.Session?.Camp;
            if (camp == null) return "—";
            return camp.Branch == CombatBranch.Sabre ? "сабля" : "якорь";
        }

        void OnGUI()
        {
            if (!Active || InventoryOpen || _driver.GameplayPaused) return;
            GUI.Box(new Rect(Screen.width / 2 - 260, Screen.height - 150, 520, 34),
                NearTent() ? "Палатка · I — снаряжение"
                : NearExit() ? "I — отправиться в забег"
                : $"ПКМ — идти · T — Полигон · B — ветка: {BranchName()}");
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
