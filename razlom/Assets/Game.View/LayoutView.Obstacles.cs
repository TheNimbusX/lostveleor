using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private ViewPool[] _solidPools;
        private readonly List<GameObject> _solids = new List<GameObject>();
        private readonly List<int> _solidKinds = new List<int>();
        public int SolidCount => _solids.Count;

        private void ClearSolids()
        {
            for (int i = 0; i < _solids.Count; i++) _solidPools[_solidKinds[i]].Release(_solids[i]);
            _solids.Clear(); _solidKinds.Clear();
        }

        private void BuildSolids(LayoutMap map)
        {
            if (map.ObstacleCount == 0) return;
            int rocks = _style.ObstacleRocks.Length;
            if (_solidPools == null)
            {
                _solidPools = new ViewPool[rocks + 1];
                var root = CreateRoot("Пул: препятствия");
                for (int k = 0; k < _solidPools.Length; k++)
                {
                    int kind = k;
                    _solidPools[k] = new ViewPool(root, () => CreateSolid(kind == rocks
                        ? _style.ObstacleTree : _style.ObstacleRocks[kind], kind == rocks), 16, false);
                }
            }
            for (int i = 0; i < map.ObstacleCount; i++)
            {
                var obstacle = map.GetObstacle(i);
                int kind = obstacle.VisualKind == 1 || rocks == 0 ? rocks : i % rocks;
                var go = _solidPools[kind].Acquire();
                float radius = obstacle.Radius.ToFloat();
                go.transform.position = new Vector3(obstacle.Center.X.ToFloat(), 0, obstacle.Center.Y.ToFloat());
                go.transform.rotation = Quaternion.Euler(0, (float)DecorRandom(i, 71).NextDouble() * 360, 0);
                go.transform.GetChild(0).localScale = Vector3.one * (kind == rocks ? 1 : radius);
                // Bury only the visual rock base; the simulation footprint and collider stay unchanged.
                go.transform.GetChild(0).localPosition = kind == rocks ? Vector3.zero
                    : Vector3.down * radius * Mathf.Lerp(.15f, .3f, (float)DecorRandom(i, 73).NextDouble());
                var collider = go.GetComponent<CapsuleCollider>();
                collider.radius = radius;
                collider.height = kind == rocks ? 3 : radius * 2;
                collider.center = Vector3.up * collider.height * .5f;
                _solids.Add(go); _solidKinds.Add(kind);
            }
        }

        private GameObject CreateSolid(GameObject prefab, bool tree)
        {
            var go = new GameObject("Препятствие: " + (tree ? "ствол" : "камень"));
            var model = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            RemoveColliders(model);
            model.transform.SetParent(go.transform, false);
            go.AddComponent<CapsuleCollider>();
            return go;
        }
    }
}
