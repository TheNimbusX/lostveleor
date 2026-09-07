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
        readonly Vector3[] _corners = new Vector3[128];
        int _cornerCount, _corner;
        CampInventoryView _inventory;
        NavMeshDataInstance _navigation; bool _approach;
        Bounds _tentBounds; Vector3 _start;
        Transform _exit; bool _approachExit;
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

        void BuildNavigation(Transform root)
        {
            // Добавляем недостающие коллизии только runtime: сохранённые трансформы не затрагиваются.
            foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null || mesh.GetComponent<Collider>() != null) continue;
                var collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh;
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
            bool interact; bool click; Vector2 pointer;
#if ENABLE_INPUT_SYSTEM
            interact = Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame;
            // Здесь выбирается только интерактивный объект. Приказ движения
            // поступает из TickDriver вместе с удержанием и короткими тапами.
            click = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            interact = Input.GetKeyDown(KeyCode.I);
            click = Input.GetMouseButtonDown(1); pointer = Input.mousePosition;
#endif
            if (interact && NearTent()) { Stop(); _inventory.Open(); return; }
            if (interact && NearExit()) { Stop(); _driver.Session.EnterRift(); return; }
            if (click && Camera.main != null && !CampInventoryView.PointerOverUI())
                HandleWorldPress(pointer);
            if (_approach && NearTent()) { _approach = false; Stop(); _inventory.Open(); }
            if (_approachExit && NearExit()) { _approachExit = false; Stop(); _driver.Session.EnterRift(); }
        }

        internal void HandleWorldPress(Vector2 pointer)
            {
                if (_driver.PointerOverPlayer(pointer))
                {
                    _approach = _approachExit = false;
                    return;
                }
                Ray ray = Camera.main.ScreenPointToRay(pointer);
                if (Physics.Raycast(ray, out var hit, 300f))
                {
                    _approach = Tent != null && hit.transform.IsChildOf(Tent);
                    _approachExit = _exit != null && Vector3.Distance(hit.point, _exit.position) < 1.5f;
                    if (_approach) ApproachTent();
                }
                else { _approach = false; _approachExit = false; }
            }
        static FixVec2 Flat(Vector3 p) => new FixVec2(Fix64.FromRaw((long)(p.x * Fix64.One.Raw)), Fix64.FromRaw((long)(p.z * Fix64.One.Raw)));
        Vector3 World(FixVec2 p) => new Vector3(p.X.ToFloat(), _height, p.Y.ToFloat());

        public void PrepareInput(ref InputFrame input)
        {
            if (!Active) return;
            if (input.AbilityMask != 0 || input.Has(InputFlags.Attack)) _approach = _approachExit = false;
            if (!_approach || _corner >= _cornerCount) return;
            while (_corner < _cornerCount - 1 && Vector3.Distance(Position,_corners[_corner]) < .045f) _corner++;
            input.Aim = Flat(_corners[_corner]);
            input.Flags = (byte)(InputFlags.MoveOrder | InputFlags.NavigationWaypoint);
            input.AttackTarget = -1;
        }
        void Stop()
        {
            _approach = _approachExit = false;
            if (Active) _driver.Session.CampSim.StopPlayerMovement();
        }
        public bool ApproachTent()
        {
            if (Tent == null) return false;
            Vector3 target = _tentBounds.ClosestPoint(Position);
            if (_walkMap == null) return false;
            var route = _walkMap.FindPath(Flat(Position), Flat(target));
            _cornerCount = Mathf.Min(route.Length,_corners.Length);
            for(int i=0;i<_cornerCount;i++)_corners[i]=World(route[i]);
            _corner = 1;
            _approach = _cornerCount > 1;
            Debug.Log($"[camp-path] corners={_cornerCount} from={Position} target={target} end={(_cornerCount>0?_corners[_cornerCount-1]:Position)}");
            return _approach;
        }
        bool NearTent() { Vector3 d = _tentBounds.ClosestPoint(Position) - Position; d.y = 0; return Tent != null && d.sqrMagnitude < 2.25f; }
        bool NearExit() => _exit != null && Vector3.Distance(Position,_exit.position) < 1.7f;
        void OnGUI()
        {
            if (!Active || InventoryOpen || _driver.GameplayPaused) return;
            GUI.Box(new Rect(Screen.width / 2 - 260, Screen.height - 150, 520, 34), NearTent() ? "Палатка · I — снаряжение" : NearExit() ? "I — отправиться в забег" : "ПКМ — идти · T — Полигон");
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
