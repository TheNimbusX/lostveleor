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
                // Берег — та же земля, что у берегов прудов, а не плоская бурая заливка.
                var bank = CreateLocationGround(new Color(.52f, .49f, .37f), _style.RoomFloorTexture,
                    _style.PathFloorTexture, _style.FloorTextureTiling, 1);
                if (bank.HasProperty("_IsRiverBank")) bank.SetFloat("_IsRiverBank", 1);
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
                // Русло лежит на террасе своего сегмента (у арены — за последним уступом).
                var level = Vector3.up * FloorLevel(river.Center.X.ToFloat(), river.Center.Y.ToFloat());
                float length = river.HalfLength.ToFloat(), width = river.HalfWidth.ToFloat();
                for (int segment = 0; segment < 168; segment++)
                {
                    float t0 = -length + segment * length / 84, t1 = -length + (segment + 1) * length / 84;
                    var a = RiverPoint(river, t0) + level; var b = RiverPoint(river, t1) + level;
                    Quad(water, waterIndices, a - along * width, b - along * width, b + along * width, a + along * width);
                    for (int side = -1; side <= 1 && Mathf.Abs((t0 + t1) * .5f) < length - 6; side += 2)
                    {
                        var innerA = a + along * (width * side); var innerB = b + along * (width * side);
                        var outerA = a + along * ((width + .8f) * side); outerA.y = level.y - .025f;
                        var outerB = b + along * ((width + .8f) * side); outerB.y = level.y - .025f;
                        Quad(banks, bankIndices, innerA, innerB, outerB, outerA);
                    }
                }
                // У арены вместо дощатого моста — импровизированный брод из камней.
                if (map.IsArena) { PlaceStoneFord(river, r); continue; }
                var bridge = _bridgePool.Acquire();
                bridge.transform.position = new Vector3(river.Center.X.ToFloat(), FloorLevel(river.Center.X.ToFloat(), river.Center.Y.ToFloat()), river.Center.Y.ToFloat());
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
                DressWaterline(map, river, r);
            }
        }

        // Кромка воды (владелец, 5 октября: «улучшить качество и логику объектов»): у самого берега —
        // папоротники, мелкие камни и пучки травы куртинами с просветами; ряд выше (ScatterRiverBanks)
        // стоял в полутора метрах от воды, и русло читалось голой полосой. У брода — свободно: там тропа.
        private void DressWaterline(LayoutMap map, LayoutRiver river, int index)
        {
            int fern = VariantNamed(FernPrefab);
            var stones = new List<int>(); var tufts = new List<int>();
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var variant = _style.DecorVariants[i];
                if (variant.Prefab == null) continue;
                if (variant.Prefab.name.StartsWith("ArenaCreatingRock")) stones.Add(i);
                else if (variant.Kind == DecorKind.GrassTuft && variant.Weight > 0) tufts.Add(i);
            }
            var rng = DecorRandom(index, 877);
            float length = river.HalfLength.ToFloat() - 4, width = river.HalfWidth.ToFloat();
            for (float t = -length; t <= length; t += .7f + (float)rng.NextDouble() * .6f)
                for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 2; row++)
                {
                    // Брод и тропа к нему — без растений.
                    if (Mathf.Abs(t) < river.BridgeHalfWidth.ToFloat() + 3) continue;
                    var at = TrailPoint(river.Point(Fix64.FromDouble(t)));
                    float clump = Mathf.PerlinNoise(at.x * .23f + side * 17, at.y * .23f + 41);
                    // Куртины: в густых местах оба ряда, в просветах — редкие кусты; второй ряд — только в куртинах.
                    float dense = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .65f, clump));
                    if (rng.NextDouble() > (row == 0 ? Mathf.Lerp(.35f, 1f, dense) : dense * .8f)) continue;
                    double roll = rng.NextDouble();
                    int variant = row == 0 && roll < .18 && stones.Count > 0 ? stones[rng.Next(stones.Count)] : roll < .78 && fern >= 0 ? fern
                        : tufts.Count > 0 ? tufts[rng.Next(tufts.Count)] : -1;
                    if (variant < 0) continue;
                    bool stone = stones.Contains(variant);
                    float size = stone ? .7f + (float)rng.NextDouble() * .6f : variant == fern ? .95f + (float)rng.NextDouble() * .5f
                        : 1.2f + (float)rng.NextDouble() * .6f;
                    float reach = _decorRadii[variant] / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y) * size * .5f;
                    var normal = TrailPoint(river.Along);
                    // Камни — у самой воды и чуть в ней, растения — на сухом краю берега.
                    var point = at + normal * side * (width + (stone ? -.1f : .2f) + reach + row * 1.4f + (float)rng.NextDouble() * .6f)
                        + new Vector2(normal.y, -normal.x) * (((float)rng.NextDouble() - .5f) * .6f);
                    if (TouchesOutlinedFloor(point.x, point.y, reach + .2f) || NearLandmark(point.x, point.y, reach)) continue;
                    SpawnDecor(variant, point.x, point.y, rng);
                    var placed = _decor[_decorCount - 1];
                    placed.localScale *= size;
                    // Уровень береговой полосы, а не впадины русла: под ней растения пропадали.
                    placed.position = new Vector3(point.x, Mathf.Max(BackgroundHeight(map, point.x, point.y), FloorLevel(point.x, point.y)) - (stone ? .12f : .03f), point.y);
                }
        }

        // Два неровных ряда плоских камней поперёк русла по ширине переправы и валуны у берегов.
        // Пол под переправой вырезан (LayoutView.Outline), поэтому между камнями видна вода.
        private void PlaceStoneFord(LayoutRiver river, int index)
        {
            var steps = new List<int>(); var boulders = new List<int>();
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var prefab = _style.DecorVariants[i].Prefab;
                if (prefab == null || _style.DecorVariants[i].Kind != DecorKind.Rock) continue;
                if (prefab.name.StartsWith("ArenaCreatingRock")) steps.Add(i);
                else if (prefab.name == "CreatingRock" || prefab.name.StartsWith("Rock")) boulders.Add(i);
            }
            if (steps.Count == 0) return;
            var rng = DecorRandom(index, 863);
            var center = new Vector3(river.Center.X.ToFloat(), FloorLevel(river.Center.X.ToFloat(), river.Center.Y.ToFloat()), river.Center.Y.ToFloat());
            var along = new Vector3(river.Along.X.ToFloat(), 0, river.Along.Y.ToFloat());
            var across = new Vector3(river.Across.X.ToFloat(), 0, river.Across.Y.ToFloat());
            float reach = river.HalfWidth.ToFloat() + .3f;
            int rows = 5;
            for (int row = 0; row < rows; row++)
                for (int column = -1; column <= 1; column += 2)
                {
                    float s = -reach + row * 2 * reach / (rows - 1) + ((float)rng.NextDouble() - .5f) * .3f;
                    float t = column * .8f + (row % 2 == 0 ? .3f : -.3f) + ((float)rng.NextDouble() - .5f) * .5f;
                    var point = center + along * s + across * t;
                    SpawnDecor(steps[rng.Next(steps.Count)], point.x, point.z, rng);
                    var stone = _decor[_decorCount - 1];
                    float spread = 1.25f + (float)rng.NextDouble() * .35f;
                    stone.localScale = Vector3.Scale(stone.localScale, new Vector3(spread, .45f, spread));
                    SetTop(stone, .05f);
                }
            if (boulders.Count == 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                // Валуны у концов брода стоят за краем переправы и не мешают пройти.
                float t = side * (river.BridgeHalfWidth.ToFloat() + .6f + (float)rng.NextDouble() * .5f);
                float s = (rng.NextDouble() < .5 ? -1 : 1) * (reach - .2f);
                var point = center + along * s + across * t;
                SpawnDecor(boulders[rng.Next(boulders.Count)], point.x, point.z, rng);
                var boulder = _decor[_decorCount - 1];
                boulder.localScale *= .75f;
                SetTop(boulder, .45f + (float)rng.NextDouble() * .25f);
            }
        }

        private static void SetTop(Transform item, float top)
        {
            var renderer = item.GetComponentInChildren<Renderer>();
            if (renderer != null) item.position += Vector3.up * (top - renderer.bounds.max.y);
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
