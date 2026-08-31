using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Пол Разлома: по плоской плите на каждый модуль.
    ///
    /// Отладочная отрисовка, а не оформление. Её задача — показать, что карта
    /// собралась и связна; настоящие стены и пол придут с художником.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class LayoutView : MonoBehaviour
    {
        // Тёплый камень, а не тёмно-синяя плита. Пол занимает бо́льшую часть
        // кадра, поэтому именно он, а не персонажи, решает, выглядит ли сцена
        // яркой локацией первого акта или подземельем.
        public Color RoomColor = new Color(0.72f, 0.66f, 0.53f);
        public Color EntranceColor = new Color(0.66f, 0.68f, 0.55f);
        public float Thickness = 0.2f;

        [Header("Граница уровня")]
        public Color WallColor = new Color(0.34f, 0.40f, 0.39f, 1f);
        public float WallHeight = 0.62f;
        public float WallThickness = 0.24f;

        // Зазор был отладочным: он показывал, что карта собралась из модулей.
        // На картинке это читалось как сетка на полу — прямой запрет из брифа.
        // Плиты теперь стыкуются, а границы комнат показывает сам пол.
        [Tooltip("Зазор между плитами. 0 — плиты стыкуются без шва.")]
        public float Gap = 0f;

        private TickDriver _driver;
        private ViewPool _pool;
        private ViewPool _wallPool;
        private Transform[] _tiles;
        private Transform[] _walls;
        private int _tileCount;
        private int _wallCount;

        private Material _roomMaterial;
        private Material _entranceMaterial;
        private Material _wallMaterial;
        private readonly HashSet<long> _occupiedCells = new HashSet<long>();

        private int _generation = -1;
        private int _depthShown = -1;
        private bool _initialized;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        /// <summary>
        /// Плиты собираются лениво: игра начинается в лагере, где карты нет.
        /// </summary>
        private void Initialize()
        {
            _initialized = true;

            Transform root = new GameObject("Пул: плиты").transform;
            root.SetParent(transform, false);

            _roomMaterial = ViewMaterials.CreateArenaFloor(RoomColor,
                new Color(0.44f, 0.58f, 0.34f, 1f));
            _entranceMaterial = ViewMaterials.CreateArenaFloor(EntranceColor,
                new Color(0.38f, 0.62f, 0.40f, 1f));

            _pool = new ViewPool(root, () => CreateTile(_roomMaterial), 72);
            _tiles = new Transform[64];

            Transform wallRoot = new GameObject("Пул: стены").transform;
            wallRoot.SetParent(transform, false);
            _wallMaterial = ViewMaterials.CreateLit(WallColor);
            _wallPool = new ViewPool(wallRoot, () => CreateWall(_wallMaterial), 192);
            _walls = new Transform[256];
        }

        private void LateUpdate()
        {
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

            Rebuild(_driver.Run.Map);
            _generation = _driver.Generation;
            _depthShown = _driver.Run.Depth;
        }

        /// <summary>
        /// Перекладывает пол под карту. null означает «убрать пол совсем» —
        /// так лагерь остаётся пустым, а не с плитами прошлого Разлома.
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

            for (int i = 0; _walls != null && i < _wallCount; i++)
            {
                if (_walls[i] == null) continue;
                if (_wallPool != null) _wallPool.Release(_walls[i].gameObject);
                else _walls[i].gameObject.SetActive(false);
                _walls[i] = null;
            }
            _wallCount = 0;
            _occupiedCells.Clear();

            if (map == null)
            {
                // Следующий Разлом обязан перестроить пол с нуля, даже если
                // придёт с той же глубиной и тем же поколением.
                _generation = -1;
                _depthShown = -1;
                return;
            }

            if (_pool == null || _tiles == null)
            {
                _initialized = false;
                Initialize();
            }

            if (_tiles.Length < map.PlacedCount) _tiles = new Transform[map.PlacedCount * 2];

            float cell = LayoutMap.CellSize.ToFloat();

            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule placed = map.GetPlaced(i);

                Transform tile = _pool.Acquire().transform;
                tile.GetComponent<MeshRenderer>().sharedMaterial =
                    i == 0 ? _entranceMaterial : _roomMaterial;

                float width = placed.Width * cell - Gap;
                float height = placed.Height * cell - Gap;

                tile.localScale = new Vector3(width, Thickness, height);

                FixVec2 center = map.CenterOf(i);
                tile.position = new Vector3(center.X.ToFloat(), -Thickness * 0.5f, center.Y.ToFloat());
                tile.rotation = Quaternion.identity;

                _tiles[_tileCount++] = tile;

                for (int x = placed.OriginX; x < placed.OriginX + placed.Width; x++)
                    for (int y = placed.OriginY; y < placed.OriginY + placed.Height; y++)
                        _occupiedCells.Add(CellKey(x, y));
            }

            BuildBoundaryWalls(cell);
        }

        private void BuildBoundaryWalls(float cell)
        {
            foreach (long key in _occupiedCells)
            {
                int x = (int)(key >> 32);
                int y = (int)key;
                float centerX = (x + 0.5f) * cell;
                float centerZ = (y + 0.5f) * cell;
                float centerY = WallHeight * 0.5f;

                if (!_occupiedCells.Contains(CellKey(x, y + 1)))
                    AddWall(new Vector3(centerX, centerY, (y + 1) * cell),
                        new Vector3(cell + WallThickness, WallHeight, WallThickness));
                if (!_occupiedCells.Contains(CellKey(x, y - 1)))
                    AddWall(new Vector3(centerX, centerY, y * cell),
                        new Vector3(cell + WallThickness, WallHeight, WallThickness));
                if (!_occupiedCells.Contains(CellKey(x + 1, y)))
                    AddWall(new Vector3((x + 1) * cell, centerY, centerZ),
                        new Vector3(WallThickness, WallHeight, cell + WallThickness));
                if (!_occupiedCells.Contains(CellKey(x - 1, y)))
                    AddWall(new Vector3(x * cell, centerY, centerZ),
                        new Vector3(WallThickness, WallHeight, cell + WallThickness));
            }
        }

        private void AddWall(Vector3 position, Vector3 scale)
        {
            if (_walls == null) _walls = new Transform[256];
            if (_wallCount >= _walls.Length)
                System.Array.Resize(ref _walls, _walls.Length * 2);

            Transform wall = _wallPool.Acquire().transform;
            wall.position = position;
            wall.rotation = Quaternion.identity;
            wall.localScale = scale;
            wall.GetComponent<MeshRenderer>().sharedMaterial = _wallMaterial;
            _walls[_wallCount++] = wall;
        }

        private static long CellKey(int x, int y)
            => ((long)x << 32) ^ (uint)y;

        private static GameObject CreateTile(Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Плита";

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject CreateWall(Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Граница";
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go;
        }
    }
}
