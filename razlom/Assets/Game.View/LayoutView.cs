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
        public Color RoomColor = new Color(0.10f, 0.11f, 0.16f);
        public Color EntranceColor = new Color(0.12f, 0.19f, 0.20f);
        public float Thickness = 0.2f;

        [Tooltip("Зазор между плитами, чтобы стыки было видно.")]
        public float Gap = 0.15f;

        private TickDriver _driver;
        private ViewPool _pool;
        private Transform[] _tiles;
        private int _tileCount;

        private Material _roomMaterial;
        private Material _entranceMaterial;

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
                new Color(0.12f, 0.48f, 0.56f, 1f));
            _entranceMaterial = ViewMaterials.CreateArenaFloor(EntranceColor,
                new Color(1.00f, 0.34f, 0.23f, 1f));

            _pool = new ViewPool(root, () => CreateTile(_roomMaterial), 72);
            _tiles = new Transform[64];
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
            for (int i = 0; i < _tileCount; i++)
            {
                if (_tiles[i] == null) continue;
                _pool.Release(_tiles[i].gameObject);
                _tiles[i] = null;
            }
            _tileCount = 0;

            if (map == null)
            {
                // Следующий Разлом обязан перестроить пол с нуля, даже если
                // придёт с той же глубиной и тем же поколением.
                _generation = -1;
                _depthShown = -1;
                return;
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
            }
        }

        private static GameObject CreateTile(Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Плита";

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
