using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Чем рисовать декор, если для варианта не задан свой префаб.</summary>
    public enum DecorKind
    {
        Bush,
        Rock,
        GrassTuft,
        Tree,
    }

    /// <summary>
    /// Один вариант декора с весом во взвешенном выборе.
    ///
    /// Prefab необязателен: пустое поле — это заявка на процедурную заглушку
    /// по Kind (сфера/куб/цилиндр на дефолтном материале), пока у художника
    /// не дошли руки до модели. Заменить заглушку на модель — перетащить
    /// префаб в это поле в инспекторе, код трогать не придётся.
    /// </summary>
    [System.Serializable]
    public struct DecorVariant
    {
        public DecorKind Kind;
        public GameObject Prefab;

        [Tooltip("Путь в Resources для авто-загрузки, если Prefab не назначен вручную. " +
                 "Например \"Decor/UNS_Bush\".")]
        public string ResourcePath;

        public float Weight;

        [Tooltip("Случайный множитель размера при расстановке: [min, max].")]
        public Vector2 ScaleRange;

        [Tooltip("Участвует ли вариант в разбросе по границе локации (вместо стен).")]
        public bool UseAsBoundary;
    }

    /// <summary>
    /// Пол Разлома: по плоской плите на каждый модуль, декоративный разброс
    /// (кусты/камни/трава) поверх и сгущённый декор по границе вместо стен.
    ///
    /// Отладочная отрисовка, а не оформление. Её задача — показать, что карта
    /// собралась и связна; настоящие стены и пол придут с художником.
    /// </summary>
    public sealed class LayoutView : MonoBehaviour
    {
        public LocationTheme Profile;
        private LayoutStyle _style = new LayoutStyle();
        private ulong _layoutSeed;
        private readonly List<GameObject> _ownedRoots = new List<GameObject>();
        private readonly List<Material> _ownedMaterials = new List<Material>();

        public int TileCount => _tileCount;
        public int DecorCount => _decorCount;
        public int PooledCount
        {
            get
            {
                int count = (_pool?.Created ?? 0) + (_pathTrailPool?.Created ?? 0);
                if (_decorPools != null)
                    foreach (var pool in _decorPools) count += pool.Created;
                return count;
            }
        }

        public void Configure(LocationTheme profile)
        {
            var style = profile != null ? profile.Style.Copy() : new LayoutStyle();
            style.Validate();
            DisposeVisuals();
            Profile = profile;
            _style = style;
        }

        public void Show(LayoutMap map, ulong layoutSeed)
        {
            _layoutSeed = layoutSeed;
            Rebuild(map);
        }

        private Transform CreateRoot(string label)
        {
            var root = new GameObject(label);
            root.transform.SetParent(transform, false);
            _ownedRoots.Add(root);
            return root.transform;
        }

        private void OnDestroy() => DisposeVisuals();

        private void DisposeVisuals()
        {
            foreach (var root in _ownedRoots)
                if (root != null) { root.SetActive(false); DestroyOwned(root); }
            _ownedRoots.Clear();
            foreach (var material in _ownedMaterials) DestroyOwned(material);
            _ownedMaterials.Clear();
            _pool = null;
            _pathTrailPool = null;
            _decorPools = null;
            _tiles = _decor = _pathTrail = null;
            _groundFill = null;
            _tileCount = _decorCount = _pathTrailCount = 0;
            _generation = _depthShown = -1;
            _initialized = false;
            _occupiedCells.Clear();
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        // Обе текстуры пола — из Ultimate Nature Starter (InnerverseInteractive),
        // перенесены в Resources/Terrain: LayoutView создаётся Bootstrap-ом
        // через AddComponent, а не лежит в сцене — перетащить ссылку
        // в инспектор просто некуда.
        private const string RoomFloorTextureResourcePath = "Terrain/UNS_Terrain_Grass";
        private const string PathFloorTextureResourcePath = "Terrain/UNS_Terrain_Dirt";

        // Базовые пропорции ЗАГЛУШКИ по виду декора — применяются только когда
        // у варианта нет своего Prefab. У настоящей модели свои пропорции,
        // трогать их незачем: для неё ScaleRange — это уже финальный масштаб.
        private static readonly Vector3 BushBaseScale = new Vector3(1.1f, 0.65f, 1.1f);
        private static readonly Vector3 RockBaseScale = new Vector3(0.75f, 0.5f, 0.7f);
        private static readonly Vector3 GrassTuftBaseScale = new Vector3(0.4f, 0.5f, 0.4f);
        private static readonly Vector3 TreeBaseScale = new Vector3(0.9f, 3.2f, 0.9f);

        private TickDriver _driver;
        private ViewPool _pool;
        private ViewPool _pathTrailPool;
        private ViewPool[] _decorPools;
        private GameObject _groundFill;
        private Transform[] _tiles;
        private Transform[] _pathTrail;
        private Transform[] _decor;
        private int[] _decorVariant;
        private int _tileCount;
        private int _pathTrailCount;
        private int _decorCount;

        private Material _roomMaterial;
        private Material _entranceMaterial;
        private Material _exitMaterial;
        private readonly HashSet<long> _occupiedCells = new HashSet<long>();
        private readonly List<Vector2> _connectorScratch = new List<Vector2>(8);

        private int _generation = -1;
        private int _depthShown = -1;
        private bool _initialized;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            Configure(Profile);
        }

        /// <summary>
        /// Плиты собираются лениво: игра начинается в лагере, где карты нет.
        /// </summary>
        private void Initialize()
        {
            _initialized = true;

            Transform root = CreateRoot("Пул: плиты");

            if (_style.RoomFloorTexture == null)
                _style.RoomFloorTexture = Resources.Load<Texture2D>(RoomFloorTextureResourcePath);
            if (_style.PathFloorTexture == null)
                _style.PathFloorTexture = Resources.Load<Texture2D>(PathFloorTextureResourcePath);

            // Вход и выход — вытоптанная тропа: грунт вместо травы, это и
            // читается как «сюда ходят», отдельно от цветовой маркировки.
            _roomMaterial = ViewMaterials.CreateArenaFloor(_style.RoomColor,
                new Color(0.47f, 0.60f, 0.42f, 1f), _style.RoomFloorTexture, _style.FloorTextureTiling, _style.FloorTextureStrength);
            _entranceMaterial = ViewMaterials.CreateArenaFloor(_style.EntranceColor,
                new Color(0.40f, 0.64f, 0.48f, 1f), _style.PathFloorTexture, _style.FloorTextureTiling, _style.FloorTextureStrength);
            _exitMaterial = ViewMaterials.CreateArenaFloor(_style.ExitColor,
                new Color(0.62f, 0.45f, 0.22f, 1f), _style.PathFloorTexture, _style.FloorTextureTiling, _style.FloorTextureStrength);

            _ownedMaterials.Add(_roomMaterial);
            _ownedMaterials.Add(_entranceMaterial);
            _ownedMaterials.Add(_exitMaterial);
            _pool = new ViewPool(root, () => CreateTile(_roomMaterial), 72, Application.isPlaying);
            _tiles = new Transform[64];

            // Тропа у входа/выхода — тот же грунтовый материал, что уже красит
            // сами модули входа и выхода: одна текстура, только плитки мельче
            // и ведут наружу.
            Transform pathRoot = CreateRoot("Пул: тропа");
            _pathTrailPool = new ViewPool(pathRoot, () => CreateTile(_entranceMaterial), 24, Application.isPlaying);
            _pathTrail = new Transform[32];

            // Одна большая плашка на весь Разлом, а не пул: она не появляется
            // и не исчезает по кускам, только целиком включается/выключается
            // вместе с картой.
            _groundFill = CreateTile(_roomMaterial);
            _ownedRoots.Add(_groundFill);
            _groundFill.name = "Заливка вне карты";
            _groundFill.transform.SetParent(transform, false);
            _groundFill.transform.localScale = new Vector3(_style.GroundFillSize, _style.Thickness, _style.GroundFillSize);
            _groundFill.transform.position = new Vector3(0f, -_style.Thickness * 0.5f - _style.GroundFillDepthOffset, 0f);
            _groundFill.SetActive(false);

            InitializeDecor();
        }

        /// <summary>
        /// По пулу на вариант декора — тот же принцип, что у плит: прогрев
        /// один раз, дальше только Acquire/Release. Resources.Load вызывается
        /// здесь же и ровно один раз за игровую сессию — не на каждый куст,
        /// который мог бы заметно тормознуть вход в Разлом.
        /// </summary>
        private void InitializeDecor()
        {
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                if (_style.DecorVariants[i].Prefab != null) continue;
                if (string.IsNullOrEmpty(_style.DecorVariants[i].ResourcePath)) continue;

                _style.DecorVariants[i].Prefab = Resources.Load<GameObject>(_style.DecorVariants[i].ResourcePath);
            }

            Transform decorRoot = CreateRoot("Пул: декор");

            _decorPools = new ViewPool[_style.DecorVariants.Length];
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                DecorVariant variant = _style.DecorVariants[i];
                Material placeholderMaterial = variant.Prefab == null ? PlaceholderMaterial(variant.Kind) : null;
                if (placeholderMaterial != null) _ownedMaterials.Add(placeholderMaterial);
                _decorPools[i] = new ViewPool(decorRoot, () => CreateDecorInstance(variant, placeholderMaterial), 48, Application.isPlaying);
            }

            _decor = new Transform[256];
            _decorVariant = new int[256];
        }

        private Material PlaceholderMaterial(DecorKind kind)
        {
            switch (kind)
            {
                case DecorKind.Rock: return ViewMaterials.CreateLit(_style.RockPlaceholderColor);
                case DecorKind.GrassTuft: return ViewMaterials.CreateLit(_style.GrassTuftPlaceholderColor);
                case DecorKind.Tree: return ViewMaterials.CreateLit(_style.TreePlaceholderColor);
                default: return ViewMaterials.CreateLit(_style.BushPlaceholderColor);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || _driver == null) return;
            if (_driver.Run == null || _driver.Sim == null)
            {
                // В лагере пола Разлома быть не должно.
                if (_initialized && _tileCount > 0) Rebuild(null);
                return;
            }

            if (!_initialized) Initialize();

            // Карта меняется только при входе в новый Разлом, поэтому плиты
            // перекладываются не каждый кадр.
            //
            // Признак — ГЛУБИНА, а не число комнат: два соседних Разлома могут
            // случайно собраться из одинакового числа модулей, и тогда пол
            // остался бы от предыдущего.
            if (_generation == _driver.Generation && _depthShown == _driver.Run.Depth) return;

            Show(_driver.Run.Map, _driver.Run.LayoutSeed);
            _generation = _driver.Generation;
            _depthShown = _driver.Run.Depth;
        }

        /// <summary>
        /// Перекладывает пол и декор под карту. null означает «убрать всё» —
        /// так лагерь остаётся пустым, а не с оформлением прошлого Разлома.
        /// </summary>
        private void Rebuild(LayoutMap map)
        {
            for (int i = 0; _tiles != null && i < _tileCount; i++)
            {
                if (_tiles[i] == null) continue;
                if (_pool != null) _pool.Release(_tiles[i].gameObject);
                else _tiles[i].gameObject.SetActive(false);
                _tiles[i] = null;
            }
            _tileCount = 0;
            _occupiedCells.Clear();

            for (int i = 0; _decor != null && i < _decorCount; i++)
            {
                if (_decor[i] == null) continue;
                if (_decorPools != null) _decorPools[_decorVariant[i]].Release(_decor[i].gameObject);
                else _decor[i].gameObject.SetActive(false);
                _decor[i] = null;
            }
            _decorCount = 0;

            for (int i = 0; _pathTrail != null && i < _pathTrailCount; i++)
            {
                if (_pathTrail[i] == null) continue;
                if (_pathTrailPool != null) _pathTrailPool.Release(_pathTrail[i].gameObject);
                else _pathTrail[i].gameObject.SetActive(false);
                _pathTrail[i] = null;
            }
            _pathTrailCount = 0;

            if (map == null)
            {
                // Следующий Разлом обязан перестроить пол с нуля, даже если
                // придёт с той же глубиной и тем же поколением.
                _generation = -1;
                _depthShown = -1;
                if (_groundFill != null) _groundFill.SetActive(false);
                return;
            }

            if (_pool == null || _tiles == null)
            {
                _initialized = false;
                Initialize();
            }

            if (_groundFill != null) _groundFill.SetActive(true);

            if (_tiles.Length < map.PlacedCount) _tiles = new Transform[map.PlacedCount * 2];

            float cell = LayoutMap.CellSize.ToFloat();

            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule placed = map.GetPlaced(i);

                Transform tile = _pool.Acquire().transform;
                tile.GetComponent<MeshRenderer>().sharedMaterial =
                    i == 0 ? _entranceMaterial
                    : map.IsExit(i) ? _exitMaterial
                    : _roomMaterial;

                float width = placed.Width * cell - _style.Gap;
                float height = placed.Height * cell - _style.Gap;

                tile.localScale = new Vector3(width, _style.Thickness, height);

                FixVec2 center = map.CenterOf(i);
                tile.position = new Vector3(center.X.ToFloat(), -_style.Thickness * 0.5f, center.Y.ToFloat());
                tile.rotation = Quaternion.identity;

                _tiles[_tileCount++] = tile;

                for (int x = placed.OriginX; x < placed.OriginX + placed.Width; x++)
                    for (int y = placed.OriginY; y < placed.OriginY + placed.Height; y++)
                        _occupiedCells.Add(CellKey(x, y));

                PlaceModuleDecor(map, placed, i, cell);
            }

            ScatterBoundaryDecor(cell);

            // Отдельным проходом, а не внутри цикла выше: вход — это placement
            // 0, самый первый, и на тот момент _occupiedCells ещё не знает про
            // остальную карту — «самая открытая сторона» посчиталась бы неверно.
            for (int i = 0; i < map.PlacedCount; i++)
            {
                if (i != 0 && !map.IsExit(i)) continue;
                AddPathTrail(map.GetPlaced(i), i, cell);
            }
        }

        // ---- декор внутри комнат ----

        /// <summary>
        /// Разбрасывает декор по одному размещённому модулю. Сид берётся из
        /// самой расстановки (индекс модуля, поворот, координаты origin) —
        /// у одной и той же карты Разлома декор всегда один и тот же, но
        /// поток свой, локальный для LayoutView: Simulation.Rng не тратится
        /// ни на один бросок.
        /// </summary>
        private void PlaceModuleDecor(LayoutMap map, PlacedModule placed, int placement, float cell)
        {
            if (_style.DecorPerCell <= 0 || _style.DecorVariants.Length == 0) return;

            float minX = placed.OriginX * cell + _style.DecorEdgeMargin;
            float maxX = (placed.OriginX + placed.Width) * cell - _style.DecorEdgeMargin;
            float minZ = placed.OriginY * cell + _style.DecorEdgeMargin;
            float maxZ = (placed.OriginY + placed.Height) * cell - _style.DecorEdgeMargin;
            if (minX >= maxX || minZ >= maxZ) return; // модуль тесен даже для одного куста с отступом

            float totalWeight = 0f;
            for (int i = 0; i < _style.DecorVariants.Length; i++) totalWeight += Mathf.Max(0f, _style.DecorVariants[i].Weight);
            if (totalWeight <= 0f) return;

            CollectConnectorPoints(map, placed, cell);

            System.Random rng = DecorRandom(placement);

            int cellsInModule = placed.Width * placed.Height;
            float expected = cellsInModule * _style.DecorPerCell;
            int baseCount = Mathf.RoundToInt(expected);
            int jitter = Mathf.Max(1, Mathf.RoundToInt(expected * 0.35f));
            int count = Mathf.Max(0, baseCount + rng.Next(-jitter, jitter + 1));

            // Отбраковка точки рядом с коннектором — попытка, а не гарантия:
            // на тесном модуле честнее пропустить один куст, чем закрутиться
            // в бесконечном переборе точек, которых физически может не быть.
            const int MaxAttemptsPerItem = 6;
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < MaxAttemptsPerItem; attempt++)
                {
                    float x = Mathf.Lerp(minX, maxX, (float)rng.NextDouble());
                    float z = Mathf.Lerp(minZ, maxZ, (float)rng.NextDouble());
                    if (TooCloseToConnector(x, z)) continue;

                    SpawnDecor(PickVariantIndex(rng, totalWeight), x, z, rng);
                    break;
                }
            }
        }

        // This stream belongs only to presentation. Never consumes Simulation.Rng.
        private System.Random DecorRandom(int placement, int salt = 0)
        {
            unchecked
            {
                int seed = (int)_layoutSeed ^ (int)(_layoutSeed >> 32);
                seed = seed * 486187739 + placement;
                return new System.Random(seed * 486187739 + salt);
            }
        }

        private void CollectConnectorPoints(LayoutMap map, PlacedModule placed, float cell)
        {
            _connectorScratch.Clear();

            ModuleDefinition module = map.Modules.Get(placed.ModuleIndex);
            for (int c = 0; c < module.ConnectorCount; c++)
            {
                ModuleConnector rotated = module.RotatedConnector(c, placed.Quarters);
                float wx = (placed.OriginX + rotated.X + 0.5f) * cell;
                float wz = (placed.OriginY + rotated.Y + 0.5f) * cell;
                _connectorScratch.Add(new Vector2(wx, wz));
            }
        }

        private bool TooCloseToConnector(float x, float z)
        {
            float marginSq = _style.DecorConnectorMargin * _style.DecorConnectorMargin;
            for (int i = 0; i < _connectorScratch.Count; i++)
            {
                float dx = _connectorScratch[i].x - x;
                float dz = _connectorScratch[i].y - z;
                if (dx * dx + dz * dz < marginSq) return true;
            }
            return false;
        }

        private int PickVariantIndex(System.Random rng, float totalWeight)
        {
            double roll = rng.NextDouble() * totalWeight;
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                roll -= Mathf.Max(0f, _style.DecorVariants[i].Weight);
                if (roll < 0.0) return i;
            }
            return _style.DecorVariants.Length - 1;
        }

        private void SpawnDecor(int variantIndex, float x, float z, System.Random rng)
        {
            if (_decorCount >= _decor.Length)
            {
                System.Array.Resize(ref _decor, _decor.Length * 2);
                System.Array.Resize(ref _decorVariant, _decorVariant.Length * 2);
            }

            DecorVariant variant = _style.DecorVariants[variantIndex];
            Transform instance = _decorPools[variantIndex].Acquire().transform;

            float scaleJitter = Mathf.Lerp(variant.ScaleRange.x, variant.ScaleRange.y, (float)rng.NextDouble());
            Vector3 baseScale = variant.Prefab != null ? Vector3.one : PlaceholderBaseScale(variant.Kind);
            instance.localScale = baseScale * scaleJitter;

            float yaw = (float)(rng.NextDouble() * 360.0);
            instance.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.position = new Vector3(x, 0f, z);

            _decor[_decorCount] = instance;
            _decorVariant[_decorCount] = variantIndex;
            _decorCount++;
        }

        private static Vector3 PlaceholderBaseScale(DecorKind kind)
        {
            switch (kind)
            {
                case DecorKind.Rock: return RockBaseScale;
                case DecorKind.GrassTuft: return GrassTuftBaseScale;
                case DecorKind.Tree: return TreeBaseScale;
                default: return BushBaseScale;
            }
        }

        private static GameObject CreateDecorInstance(DecorVariant variant, Material placeholderMaterial)
        {
            if (variant.Prefab != null)
            {
                // Ultimate Nature Starter собран под URP штатно — свои
                // материалы (текстуры, alpha-cutout листвы) доверяем как есть,
                // перекраска нужна только процедурным заглушкам ниже.
                GameObject prefabInstance = Instantiate(variant.Prefab);
                prefabInstance.name = "Декор: " + variant.Prefab.name;
                RemoveColliders(prefabInstance);
                return prefabInstance;
            }

            PrimitiveType shape;
            string label;
            switch (variant.Kind)
            {
                case DecorKind.Rock:
                    shape = PrimitiveType.Cube;
                    label = "Декор: камень";
                    break;
                case DecorKind.GrassTuft:
                    shape = PrimitiveType.Cylinder;
                    label = "Декор: трава";
                    break;
                case DecorKind.Tree:
                    shape = PrimitiveType.Capsule;
                    label = "Декор: дерево";
                    break;
                default:
                    shape = PrimitiveType.Sphere;
                    label = "Декор: куст";
                    break;
            }

            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = label;
            RemoveColliders(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = placeholderMaterial;
            return go;
        }

        // ---- декор по границе ----

        /// <summary>
        /// Вместо стены — сгущённый декор по контуру карты. Стен-заглушек
        /// (кубов) больше нет: IsWalkable в Game.Sim и так не пускает игрока
        /// дальше занятых клеток, рисовать это ещё и геометрией избыточно.
        ///
        /// Каждый открытый край (клетка занята, сосед — нет) с вероятностью
        /// _style.BoundaryDecorChance получает объект из вариантов с UseAsBoundary.
        /// Не 100%: сплошная шеренга кустов через клетку читалась бы забором,
        /// то есть тем же самым, от чего мы уходим, просто из другого меша.
        /// </summary>
        private void ScatterBoundaryDecor(float cell)
        {
            float totalWeight = 0f;
            for (int i = 0; i < _style.DecorVariants.Length; i++)
                if (_style.DecorVariants[i].UseAsBoundary) totalWeight += Mathf.Max(0f, _style.DecorVariants[i].Weight);
            if (totalWeight <= 0f) return;

            var orderedCells = new List<long>(_occupiedCells);
            orderedCells.Sort();
            foreach (long key in orderedCells)
            {
                int x = (int)(key >> 32);
                int y = (int)key;

                TryScatterBoundaryEdge(x, y, x, y + 1, cell, totalWeight);
                TryScatterBoundaryEdge(x, y, x, y - 1, cell, totalWeight);
                TryScatterBoundaryEdge(x, y, x + 1, y, cell, totalWeight);
                TryScatterBoundaryEdge(x, y, x - 1, y, cell, totalWeight);
            }
        }

        /// <summary>
        /// Сид — координаты самого края (клетка + сосед), а не порядок
        /// перебора HashSet: перебор по хешу не гарантирует стабильный
        /// порядок между запусками, а координаты — всегда одни и те же.
        /// </summary>
        private void TryScatterBoundaryEdge(int x, int y, int neighborX, int neighborY, float cell, float totalWeight)
        {
            if (_occupiedCells.Contains(CellKey(neighborX, neighborY))) return; // не край

            System.Random rng = DecorRandom(unchecked(
                x * 486187739 + y * 290797 + neighborX * 65497 + neighborY * 37), 1);
            if (rng.NextDouble() >= _style.BoundaryDecorChance) return;

            float dirX = neighborX - x;
            float dirZ = neighborY - y;

            float edgeX = (x + 0.5f) * cell + dirX * cell * 0.5f;
            float edgeZ = (y + 0.5f) * cell + dirZ * cell * 0.5f;

            float posX = edgeX + dirX * _style.BoundaryDecorOutset + (float)(rng.NextDouble() - 0.5) * _style.BoundaryDecorJitter;
            float posZ = edgeZ + dirZ * _style.BoundaryDecorOutset + (float)(rng.NextDouble() - 0.5) * _style.BoundaryDecorJitter;

            int variantIndex = PickBoundaryVariantIndex(rng, totalWeight);
            if (variantIndex < 0) return;

            SpawnDecor(variantIndex, posX, posZ, rng);
        }

        private int PickBoundaryVariantIndex(System.Random rng, float totalWeight)
        {
            double roll = rng.NextDouble() * totalWeight;
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                if (!_style.DecorVariants[i].UseAsBoundary) continue;
                roll -= Mathf.Max(0f, _style.DecorVariants[i].Weight);
                if (roll < 0.0) return i;
            }
            for (int i = _style.DecorVariants.Length - 1; i >= 0; i--)
                if (_style.DecorVariants[i].UseAsBoundary) return i;
            return -1;
        }

        // ---- тропа у входа и выхода ----

        /// <summary>
        /// Ведёт дорожку из мелких плит наружу от модуля — туда, где больше
        /// всего свободных соседей: не обязательно геометрически точное «туда,
        /// откуда пришли» (это потребовало бы разбирать, какой коннектор
        /// реально использован для стыковки с родителем), но всегда прочь от
        /// уже застроенной карты, а этого достаточно, чтобы читалось как
        /// тропа наружу.
        /// </summary>
        private void AddPathTrail(PlacedModule placed, int placement, float cell)
        {
            int bestDx = 0, bestDz = 0, bestCount = 0;
            CheckEdge(placed, 0, 1, ref bestDx, ref bestDz, ref bestCount);
            CheckEdge(placed, 0, -1, ref bestDx, ref bestDz, ref bestCount);
            CheckEdge(placed, 1, 0, ref bestDx, ref bestDz, ref bestCount);
            CheckEdge(placed, -1, 0, ref bestDx, ref bestDz, ref bestCount);

            // Со всех сторон плотно застроено — тропу вести некуда, и это
            // нормально: не у каждой комнаты есть свободный край.
            if (bestCount == 0) return;

            System.Random rng = DecorRandom(placement);

            float edgeX = bestDx != 0
                ? (bestDx > 0 ? placed.OriginX + placed.Width : placed.OriginX) * cell
                : (placed.OriginX + placed.Width * 0.5f) * cell;
            float edgeZ = bestDz != 0
                ? (bestDz > 0 ? placed.OriginY + placed.Height : placed.OriginY) * cell
                : (placed.OriginY + placed.Height * 0.5f) * cell;

            int steps = _style.PathTrailSteps;
            for (int step = 0; step < steps; step++)
            {
                float t = step + 0.5f;
                float px = edgeX + bestDx * cell * t + (float)(rng.NextDouble() - 0.5) * cell * _style.PathTrailJitter;
                float pz = edgeZ + bestDz * cell * t + (float)(rng.NextDouble() - 0.5) * cell * _style.PathTrailJitter;

                // Плиты мельчают к концу тропы — она тает в траве/декоре
                // границы, а не обрывается ровным краем.
                float size = Mathf.Lerp(cell * 0.85f, cell * 0.35f,
                    steps <= 1 ? 0f : step / (float)(steps - 1));

                if (_pathTrailCount >= _pathTrail.Length)
                    System.Array.Resize(ref _pathTrail, _pathTrail.Length * 2);

                Transform tile = _pathTrailPool.Acquire().transform;
                tile.localScale = new Vector3(size, _style.Thickness, size);
                tile.position = new Vector3(px, -_style.Thickness * 0.5f - 0.01f, pz);
                tile.rotation = Quaternion.identity;

                _pathTrail[_pathTrailCount++] = tile;
            }
        }

        /// <summary>Считает, сколько клеток вдоль этой грани модуля не заняты соседом.</summary>
        private void CheckEdge(PlacedModule placed, int dx, int dz, ref int bestDx, ref int bestDz, ref int bestCount)
        {
            int count = 0;
            if (dx != 0)
            {
                int x = dx > 0 ? placed.OriginX + placed.Width : placed.OriginX - 1;
                for (int y = placed.OriginY; y < placed.OriginY + placed.Height; y++)
                    if (!_occupiedCells.Contains(CellKey(x, y))) count++;
            }
            else
            {
                int y = dz > 0 ? placed.OriginY + placed.Height : placed.OriginY - 1;
                for (int x = placed.OriginX; x < placed.OriginX + placed.Width; x++)
                    if (!_occupiedCells.Contains(CellKey(x, y))) count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestDx = dx;
                bestDz = dz;
            }
        }

        private static long CellKey(int x, int y)
            => ((long)x << 32) ^ (uint)y;

        private static void RemoveColliders(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                DestroyOwned(colliders[i]);
            }
        }

        private static GameObject CreateTile(Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Плита";
            RemoveColliders(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
