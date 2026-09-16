using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Mesh _riverMesh, _riverBankMesh, _bridgeMesh;
        private GameObject _riverObject, _riverBanks;
        private ViewPool _bridgePool;
        private readonly List<GameObject> _bridges = new List<GameObject>();

        private bool NearRiver(float x, float z, float margin)
        {
            if (_shownMap == null) return false;
            var point = new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z));
            for (int r = 0; r < _shownMap.RiverCount; r++)
                if (_shownMap.GetRiver(r).ContainsWater(point, Fix64.FromDouble(margin))) return true;
            return false;
        }

        private void ClearRivers()
        {
            foreach (var bridge in _bridges) _bridgePool.Release(bridge);
            _bridges.Clear();
            if (_riverObject != null) _riverObject.SetActive(false);
            if (_riverBanks != null) _riverBanks.SetActive(false);
        }

        private GameObject RiverObject(string title, Material material, out Mesh mesh)
        {
            var go = new GameObject(title);
            go.transform.SetParent(_banks.transform.parent, false);
            mesh = new Mesh { name = title }; _meadowMeshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private void BuildRivers(LayoutMap map)
        {
            if (map.RiverCount == 0) return;
            if (_riverObject == null)
            {
                _riverObject = RiverObject("Лесные реки", _water.GetComponent<Renderer>().sharedMaterial, out _riverMesh);
                var bank = ViewMaterials.CreateLit(new Color(.36f, .33f, .24f));
                _ownedMaterials.Add(bank);
                _riverBanks = RiverObject("Берега рек", bank, out _riverBankMesh);
                _bridgeMesh = MakeBridgeMesh(); _meadowMeshes.Add(_bridgeMesh);
                var wood = ViewMaterials.CreateLit(new Color(.39f, .25f, .13f));
                _ownedMaterials.Add(wood);
                _bridgePool = new ViewPool(CreateRoot("Пул: мосты"), () =>
                {
                    var go = new GameObject("Деревянный мост");
                    go.AddComponent<MeshFilter>().sharedMesh = _bridgeMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = wood;
                    return go;
                }, 2, false);
            }
            var water = new List<Vector3>(); var banks = new List<Vector3>();
            var waterIndices = new List<int>(); var bankIndices = new List<int>();
            for (int r = 0; r < map.RiverCount; r++)
            {
                var river = map.GetRiver(r);
                var along = new Vector3(river.Along.X.ToFloat(), 0, river.Along.Y.ToFloat());
                float length = river.HalfLength.ToFloat(), width = river.HalfWidth.ToFloat();
                for (int segment = 0; segment < 168; segment++)
                {
                    float t0 = -length + segment * length / 84, t1 = -length + (segment + 1) * length / 84;
                    var a = RiverPoint(river, t0); var b = RiverPoint(river, t1);
                    Quad(water, waterIndices, a - along * width, b - along * width, b + along * width, a + along * width);
                    for (int side = -1; side <= 1 && Mathf.Abs((t0 + t1) * .5f) < length - 6; side += 2)
                    {
                        var innerA = a + along * (width * side); var innerB = b + along * (width * side);
                        var outerA = a + along * ((width + .8f) * side); outerA.y = -.025f;
                        var outerB = b + along * ((width + .8f) * side); outerB.y = -.025f;
                        Quad(banks, bankIndices, innerA, innerB, outerB, outerA);
                    }
                }
                var bridge = _bridgePool.Acquire();
                bridge.transform.position = new Vector3(river.Center.X.ToFloat(), 0, river.Center.Y.ToFloat());
                bridge.transform.rotation = Quaternion.LookRotation(along);
                _bridges.Add(bridge);
            }
            FillRiverMesh(_riverMesh, water, waterIndices);
            var riverUv = new List<Vector2>(water.Count);
            for (int i = 0; i < water.Count; i++) riverUv.Add(new Vector2(i % 4 < 2 ? -1 : 1, 0));
            _riverMesh.SetUVs(0, riverUv);
            FillRiverMesh(_riverBankMesh, banks, bankIndices);
            _riverObject.SetActive(true); _riverBanks.SetActive(true);
            ScatterRiverBanks(map);
        }

        private void ScatterRiverBanks(LayoutMap map)
        {
            if (_style.BoundaryDecorChance <= 0 || _style.ForestBandWidth <= 0) return;
            var rocks = new List<int>(); var bushes = new List<int>(); var grass = new List<int>();
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var variant = _style.DecorVariants[i];
                if (variant.Weight <= 0) continue;
                if (variant.Kind == DecorKind.Rock) rocks.Add(i);
                if (variant.Kind == DecorKind.Bush) bushes.Add(i);
                if (variant.Kind == DecorKind.GrassTuft) grass.Add(i);
            }
            for (int r = 0; r < map.RiverCount; r++)
            {
                var river = map.GetRiver(r); var rng = DecorRandom(r, 853);
                for (int t = -32; t <= 32; t += 6)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (rng.NextDouble() < .25) continue;
                        int variant = PickDetail(rng.Next(3) == 0 ? rocks : bushes, rng);
                        if (variant < 0) continue;
                        var point = river.Point(Fix64.FromInt(t)) + river.Along * Fix64.FromDouble(
                            side * (river.HalfWidth.ToFloat() + _decorRadii[variant] + 1.3f));
                        var center = TrailPoint(point);
                        if (TryForestDetail(map, variant, center, rng))
                            DressDetail(map, center, _decorRadii[variant], bushes, grass, rng);
                    }
            }
        }

        private static Vector3 RiverPoint(LayoutRiver river, float t)
        {
            var p = river.Point(Fix64.FromDouble(t));
            return new Vector3(p.X.ToFloat(), -.18f, p.Y.ToFloat());
        }

        private static void Quad(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            // Оба берега должны смотреть наружу независимо от поворота карты.
            bool reverse = Vector3.Cross(b - a, c - a).y < 0;
            indices.Add(i); indices.Add(i + (reverse ? 2 : 1)); indices.Add(i + (reverse ? 1 : 2));
            indices.Add(i); indices.Add(i + (reverse ? 3 : 2)); indices.Add(i + (reverse ? 2 : 3));
        }

        private static void FillRiverMesh(Mesh mesh, List<Vector3> vertices, List<int> indices)
        {
            var uv = new List<Vector2>(vertices.Count);
            foreach (var v in vertices) uv.Add(new Vector2(v.x * .12f, v.z * .12f));
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        private static Mesh MakeBridgeMesh()
        {
            // Плоский настил сохраняет высоту ног в двумерной симуляции.
            var parts = new List<CombineInstance>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = cube.GetComponent<MeshFilter>().sharedMesh;
            void Beam(Vector3 center, Vector3 size) => parts.Add(new CombineInstance
                { mesh = source, transform = Matrix4x4.TRS(center, Quaternion.identity, size) });
            for (int i = 0; i < 19; i++)
                Beam(new Vector3(0, -.04f, (i - 9) * .45f), new Vector3(4.4f + .08f * Mathf.Sin(i * 2), .12f, .435f));
            for (int side = -1; side <= 1; side += 2)
            {
                Beam(new Vector3(side * 2.2f, .64f, 0), new Vector3(.14f, .16f, 8.5f));
                Beam(new Vector3(side * 1.7f, -.28f, 0), new Vector3(.25f, .35f, 8.8f));
                for (int post = -1; post <= 1; post++)
                    Beam(new Vector3(side * 2.2f, .25f, post * 3.9f), new Vector3(.24f, 1.15f, .24f));
            }
            cube.SetActive(false); DestroyOwned(cube);
            var mesh = new Mesh { name = "Доски и перила лесного моста" };
            mesh.CombineMeshes(parts.ToArray()); mesh.RecalculateBounds(); return mesh;
        }
    }
}
