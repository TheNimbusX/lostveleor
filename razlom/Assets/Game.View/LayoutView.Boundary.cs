using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Mesh _shoreMesh;
        private GameObject _shore;
        private int _edgeTreeCount;

        private void ScatterOutlinedBoundary(float cell)
        {
            _edgeTreeCount = 0;
            if (_style.BoundaryDecorChance <= 0) return;
            var bushes = new List<int>(); var rocks = new List<int>(); var trees = new List<int>();
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var variant = _style.DecorVariants[i];
                if (!variant.UseAsBoundary || variant.Weight <= 0) continue;
                // Изгородь ставится рядами в ScatterForestDetails, одиночная секция на контуре читается как мусор.
                if (variant.Prefab != null && variant.Prefab.name == "CreatingFence") continue;
                if (variant.Kind == DecorKind.Bush) bushes.Add(i);
                else if (variant.Kind == DecorKind.Rock) rocks.Add(i);
                else if (variant.Kind == DecorKind.Tree) trees.Add(i);
            }
            if (bushes.Count == 0 && rocks.Count == 0) return;
            var occupied = new List<long>(_occupiedCells); occupied.Sort();
            var placed = new List<Vector3>();
            var copses = new List<Vector2>();
            float spacing = Mathf.Clamp(_style.BoundarySpacing, 1.2f, 3)
                * Mathf.Clamp(Mathf.Sqrt(.36f / _style.BoundaryDecorChance), .85f, 2);
            foreach (long key in occupied)
            {
                int x = (int)(key >> 32), z = (int)key;
                for (int d = 0; d < 4; d++)
                {
                    Directions.Step((Direction)d, out int dx, out int dz);
                    if (_occupiedCells.Contains(CellKey(x + dx, z + dz))) continue;
                    var edge = new Vector2((x + .5f + dx * .5f) * cell, (z + .5f + dz * .5f) * cell);
                    bool water = false;
                    for (int w = 0; w < _shownMap.WaterCount; w++)
                    {
                        var pond = _shownMap.GetWater(w);
                        if (Vector2.Distance(edge, TrailPoint(pond.Center)) < pond.Radius.ToFloat() + .8f) { water = true; break; }
                    }
                    if (water || NearRiver(edge.x, edge.y, 1)) continue;
                    var rng = DecorRandom(unchecked(x * 486187739 + z * 290797 + d * 65497), 619);
                    float patch = Mathf.PerlinNoise(edge.x * .13f + 91, edge.y * .13f + 37);
                    // Крупный шум вдоль опушки чередует густые заросли и просветы; ровный шаг
                    // по всему контуру давал пунктирную цепочку одинаковых кустов.
                    float thicket = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .72f,
                        Mathf.PerlinNoise(edge.x * .07f + 213, edge.y * .07f + 57)));
                    if (thicket < .12f && rng.NextDouble() < .8) continue;
                    var choices = bushes.Count == 0 || rocks.Count > 0 && patch > .62f ? rocks : bushes;
                    int variant = PickDetail(choices, rng);
                    if (variant < 0) continue;
                    bool bush = _style.DecorVariants[variant].Kind == DecorKind.Bush;
                    float scale = bush ? Mathf.Lerp(1.1f, 2.2f, Mathf.Pow((float)rng.NextDouble(), .8f) * (.55f + thicket * .45f)) : 1;
                    float radius = _decorRadii[variant] * scale;
                    var normal = new Vector2(dx, dz); var tangent = new Vector2(dz, -dx);
                    // Кусты уходят вглубь на разную глубину — край не повторяет контур пола.
                    var point = edge + normal * (radius + .15f + (float)rng.NextDouble() * Mathf.Lerp(.25f, 1.4f, thicket))
                        + tangent * ((float)rng.NextDouble() - .5f) * .6f;
                    // Квадрат максимальных габаритов учитывает поворот модели и вогнутые участки контура.
                    int push = 0;
                    while (BoundaryBlocksClearance(point, radius) && push++ < 8) point += normal * .15f;
                    if (BoundaryBlocksClearance(point, radius)) continue;
                    bool overlap = false;
                    // В зарослях кусты смыкаются, в просветах стоят редко.
                    float stride = spacing * Mathf.Lerp(1.6f, .6f, thicket);
                    foreach (var other in placed)
                    {
                        float gap = Mathf.Max(stride * Mathf.Lerp(.85f, 1.15f, patch), (radius + other.z) * Mathf.Lerp(.8f, .55f, thicket));
                        if ((point - new Vector2(other.x, other.y)).sqrMagnitude < gap * gap) { overlap = true; break; }
                    }
                    if (overlap) continue;
                    // Положение вне пола сохраняет все внутренние проходы и боевые площадки.
                    SpawnDecor(variant, point.x, point.y, rng);
                    _decor[_decorCount - 1].localScale *= scale;
                    if (bush)
                    {
                        var size = _decor[_decorCount - 1].localScale;
                        size.y *= .6f + (float)rng.NextDouble() * .28f;
                        _decor[_decorCount - 1].localScale = size;
                    }
                    placed.Add(new Vector3(point.x, point.y, radius));
                    // Второй нерегулярный слой превращает цепочку меток в край леса.
                    int followers = thicket < .35f ? rng.Next(0, 2) : rng.Next(1, 5);
                    for (int follower = 0; follower < followers; follower++)
                    {
                        int companion = PickDetail(bushes, rng);
                        if (companion >= 0)
                        {
                            float companionScale = scale * (.45f + (float)rng.NextDouble() * .4f);
                            // Спутники перекрывают куст вдоль опушки и уходят глубже, образуя массу, а не метки.
                            float side = (follower % 2 == 0 ? 1 : -1) * (.5f + (float)rng.NextDouble() * .7f);
                            var outer = point + normal * (radius * (.2f + (float)rng.NextDouble() * .8f))
                                + tangent * radius * side;
                            if (!BoundaryBlocksClearance(outer, _decorRadii[companion] * companionScale)
                                && !NearRiver(outer.x, outer.y, _decorRadii[companion] * companionScale))
                            {
                                SpawnDecor(companion, outer.x, outer.y, rng);
                                _decor[_decorCount - 1].localScale *= companionScale;
                            }
                        }
                    }
                    // Отдельный поток не меняет кусты при настройке верхнего яруса леса.
                    var canopyRng = DecorRandom(unchecked(x * 486187739 + z * 290797 + d * 65497), 631);
                    if (_style.ForestBandWidth > 0 && patch > .38f && _edgeTreeCount < 96
                        && canopyRng.NextDouble() < _style.EdgeCanopyDensity)
                    {
                        bool nearby = false;
                        foreach (var copse in copses)
                            if ((copse - point).sqrMagnitude < 81) { nearby = true; break; }
                        if (nearby) continue;
                        bool planted = false;
                        int count = canopyRng.Next(2, 5);
                        for (int member = 0; member < count && _edgeTreeCount < 96; member++)
                        {
                            int tree = PickDetail(trees, canopyRng);
                            if (tree < 0) break;
                            float treeScale = member == 0 ? 1.3f : .85f + (float)canopyRng.NextDouble() * .3f;
                            float canopyRadius = _decorRadii[tree] * treeScale;
                            // Куст растёт под кроной: второй отступ на его радиус создавал пустую полосу.
                            var outer = point + normal * (canopyRadius - radius + .35f + member * .65f)
                                + tangent * ((member - (count - 1) * .5f) * canopyRadius * .85f);
                            // Проверяем всю крону, а не только ствол: она не закрывает боевой центр.
                            if (BoundaryBlocksClearance(outer, canopyRadius) || NearPond(outer.x, outer.y, canopyRadius)) continue;
                            SpawnDecor(tree, outer.x, outer.y, canopyRng);
                            var instance = _decor[_decorCount - 1];
                            instance.localScale *= treeScale;
                            instance.position = new Vector3(outer.x, BackgroundHeight(_shownMap, outer.x, outer.y) - .08f, outer.y);
                            _edgeTreeCount++;
                            planted = true;
                        }
                        if (planted) copses.Add(point);
                    }
                }
            }
        }

        private bool BoundaryBlocksClearance(Vector2 point, float radius)
        {
            if (_shownEncounters != null)
            {
                float clearance = _shownEncounters.FormationRadius.ToFloat() + radius + 1;
                for (int e = 0; e < _shownEncounters.Count; e++)
                    if ((point - TrailPoint(_shownEncounters.Get(e).Center)).sqrMagnitude < clearance * clearance) return true;
            }
            int minX = Mathf.FloorToInt((point.x - radius) * 2), maxX = Mathf.FloorToInt((point.x + radius) * 2);
            int minZ = Mathf.FloorToInt((point.y - radius) * 2), maxZ = Mathf.FloorToInt((point.y + radius) * 2);
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!_shownMap.Outline.ContainsCell(x, z)) continue;
                    float dx = point.x - Mathf.Clamp(point.x, x * .5f, (x + 1) * .5f);
                    float dz = point.y - Mathf.Clamp(point.y, z * .5f, (z + 1) * .5f);
                    if (Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius) return true;
                }
            return false;
        }

        private void BuildReadableShores()
        {
            if (_shore == null)
            {
                _shore = new GameObject("Берега непроходимой воды");
                _shore.transform.SetParent(_banks.transform.parent, false);
                _shoreMesh = new Mesh { name = "Береговая кромка" }; _meadowMeshes.Add(_shoreMesh);
                _shore.AddComponent<MeshFilter>().sharedMesh = _shoreMesh;
                var material = CreateLocationGround(new Color(.52f, .49f, .37f), _style.RoomFloorTexture,
                    _style.PathFloorTexture, _style.FloorTextureTiling, 1);
                if (material.HasProperty("_IsRiverBank")) material.SetFloat("_IsRiverBank", 1);
                _ownedMaterials.Add(material); _shore.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            foreach (var pond in _ponds)
            {
                int start = vertices.Count;
                for (int i = 0; i <= 64; i++)
                {
                    float angle = i * Mathf.PI / 32;
                    float irregular = 1 + .07f * Mathf.Sin(angle * 3 + pond.x);
                    var direction = new Vector2(Mathf.Cos(angle) * pond.z, Mathf.Sin(angle) * pond.w);
                    // Узкий откос отмечает место остановки, а его нижняя часть уходит под воду.
                    var outer = new Vector2(pond.x, pond.y) + direction * (1.08f + .02f * Mathf.Sin(angle * 7));
                    var inner = new Vector2(pond.x, pond.y) + direction * (.82f * irregular);
                    vertices.Add(new Vector3(outer.x, .012f, outer.y)); uv.Add(new Vector2(i / 64f, 0));
                    vertices.Add(new Vector3(inner.x, -.16f, inner.y)); uv.Add(new Vector2(i / 64f, 1));
                    if (i == 64) continue;
                    // У соединения с рекой кольцевой берег не должен перегородить воду.
                    if (NearRiver(outer.x, outer.y, .35f)) continue;
                    int v = start + i * 2;
                    triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                    triangles.Add(v + 1); triangles.Add(v + 3); triangles.Add(v + 2);
                }
            }
            _shoreMesh.Clear(); _shoreMesh.SetVertices(vertices); _shoreMesh.SetUVs(0, uv);
            _shoreMesh.SetTriangles(triangles, 0); _shoreMesh.RecalculateNormals(); _shoreMesh.RecalculateBounds();
            _shore.SetActive(_ponds.Count > 0);
        }
    }
}
