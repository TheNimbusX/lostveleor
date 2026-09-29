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
                    if (BoundaryBlocksClearance(point, radius) || NearLandmark(point.x, point.y, radius * .6f)) continue;
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
                                && !NearRiver(outer.x, outer.y, _decorRadii[companion] * companionScale)
                                && !NearLandmark(outer.x, outer.y, _decorRadii[companion] * companionScale * .6f))
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
                            if (BoundaryBlocksClearance(outer, canopyRadius) || NearPond(outer.x, outer.y, canopyRadius)
                                || NearLandmark(outer.x, outer.y, canopyRadius * .35f) || ShadesLandmark(outer.x, outer.y, canopyRadius * .85f)) continue;
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
            CloseOutlineGaps(bushes, rocks);
        }

        // Сухие отрезки проходимого контура с внешней нормалью. Берег воды виден сам по себе.
        private List<(Vector2 Point, Vector2 Normal)> OutlineEdges()
        {
            var edges = new List<(Vector2, Vector2)>();
            var occupied = new List<long>(_occupiedCells); occupied.Sort();
            foreach (long key in occupied)
            {
                int x = (int)(key >> 32), z = (int)key;
                for (int d = 0; d < 4; d++)
                {
                    Directions.Step((Direction)d, out int dx, out int dz);
                    if (_occupiedCells.Contains(CellKey(x + dx, z + dz))) continue;
                    var edge = new Vector2((x + .5f + dx * .5f) * .5f, (z + .5f + dz * .5f) * .5f);
                    bool water = NearRiver(edge.x, edge.y, 1);
                    for (int w = 0; w < _shownMap.WaterCount && !water; w++)
                    {
                        var pond = _shownMap.GetWater(w);
                        water = Vector2.Distance(edge, TrailPoint(pond.Center)) < pond.Radius.ToFloat() + .8f;
                    }
                    if (!water) edges.Add((edge, new Vector2(dx, dz)));
                }
            }
            return edges;
        }

        // Проходимый край должен читаться везде. Заросли выше оставляют просветы, и игрок упирался
        // в невидимую стену посреди травы: замер 27 сентября — 83% края без единого куста рядом,
        // открытые участки до 50 м. Каждый просвет закрывает куст или камень вплотную к краю, но не
        // на полу. Колоски сюда не годятся: 15 тысяч треугольников на пучок. У точек появления врагов
        // крупное не встаёт — там край отмечает высокая трава кромки (LayoutView.Grass).
        private void CloseOutlineGaps(List<int> bushes, List<int> rocks)
        {
            var cover = new List<Vector3>();
            for (int i = 0; i < _decorCount; i++)
                if (_style.DecorVariants[_decorVariant[i]].Kind != DecorKind.GrassTuft)
                    cover.Add(new Vector3(_decor[i].position.x, _decor[i].position.z, VisibleRadius(i)));
            var character = _shownMap.GladeCount == 1 ? CharacterOf(_shownMap, 0) : GladeCharacter.Rocky;
            float rockShare = rocks.Count == 0 ? 0 : bushes.Count == 0 ? 1 : character == GladeCharacter.Rocky ? .3f : .12f;
            foreach (var (edge, normal) in OutlineEdges())
            {
                bool covered = false;
                foreach (var c in cover)
                    if ((edge - new Vector2(c.x, c.y)).sqrMagnitude < (c.z + .85f) * (c.z + .85f)) { covered = true; break; }
                if (covered) continue;
                var rng = DecorRandom(unchecked(Mathf.RoundToInt(edge.x * 4) * 486187739 + Mathf.RoundToInt(edge.y * 4) * 290797), 641);
                bool rock = rng.NextDouble() < rockShare;
                int variant = PickDetail(rock ? rocks : bushes, rng);
                if (variant < 0) continue;
                float scale = rock ? .85f + (float)rng.NextDouble() * .4f : 1f + (float)rng.NextDouble();
                // Ставим по видимому радиусу, а на пол не пускает точный габарит (PlaceOffFloor):
                // по габаритному кругу пула куст отъезжал так далеко, что не закрывал даже свою точку.
                float visible = _decorRadii[variant] * scale / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y) * .7f;
                var tangent = new Vector2(normal.y, -normal.x);
                // Разная глубина от края: иначе заросли выстраивались шеренгой вдоль контура.
                var point = edge + normal * (visible + .1f + (float)rng.NextDouble() * .45f)
                    + tangent * ((float)rng.NextDouble() - .5f) * .5f;
                if (BoundaryBlocksClearance(point, visible * .5f) || NearPond(point.x, point.y, visible)
                    || NearLandmark(point.x, point.y, visible)) continue;
                if (!PlaceOffFloor(variant, point, normal, scale, !rock, rng)) continue;
                var placed = _decor[_decorCount - 1];
                cover.Add(new Vector3(placed.position.x, placed.position.z, VisibleRadius(_decorCount - 1)));
                // Спутники делают из метки куртину: ниже и глубже основного куста, по обе стороны.
                for (int follower = rng.NextDouble() < .15 ? 2 : rng.NextDouble() < .55 ? 1 : 0; follower > 0; follower--)
                {
                    int companion = PickDetail(bushes, rng);
                    if (companion < 0) break;
                    var outer = new Vector2(placed.position.x, placed.position.z) + normal * (visible * (.4f + (float)rng.NextDouble() * .8f))
                        + tangent * visible * (follower == 1 ? 1 : -1) * (.7f + (float)rng.NextDouble() * .5f);
                    if (NearPond(outer.x, outer.y, visible * .6f) || NearLandmark(outer.x, outer.y, visible * .6f)) continue;
                    if (PlaceOffFloor(companion, outer, normal, scale * (.5f + (float)rng.NextDouble() * .4f), true, rng))
                        cover.Add(new Vector3(_decor[_decorCount - 1].position.x, _decor[_decorCount - 1].position.z, VisibleRadius(_decorCount - 1)));
                }
            }
        }

        // Объект встаёт в точку и отодвигается наружу, пока его настоящий габарит касается пола:
        // граница не заходит на игровое поле ни одним углом повёрнутой модели.
        private bool PlaceOffFloor(int variant, Vector2 point, Vector2 normal, float scale, bool squash, System.Random rng)
        {
            SpawnDecor(variant, point.x, point.y, rng);
            var placed = _decor[_decorCount - 1];
            placed.localScale *= scale;
            if (squash)
            {
                var size = placed.localScale; size.y *= .6f + (float)rng.NextDouble() * .28f;
                placed.localScale = size;
            }
            var renderers = placed.GetComponentsInChildren<Renderer>();
            for (int step = 0; step <= 16; step++)
            {
                placed.position = new Vector3(point.x, BackgroundHeight(_shownMap, point.x, point.y) - .035f, point.y);
                bool touches = false, crowds = false;
                foreach (var renderer in renderers)
                {
                    var bounds = renderer.bounds;
                    for (int z = Mathf.FloorToInt(bounds.min.z * 2); z <= Mathf.FloorToInt(bounds.max.z * 2) && !touches; z++)
                        for (int x = Mathf.FloorToInt(bounds.min.x * 2); x <= Mathf.FloorToInt(bounds.max.x * 2) && !touches; x++)
                            touches = _shownMap.Outline.ContainsCell(x, z);
                    // Строй появления врагов виден целиком: габарит не заходит в его круг.
                    crowds |= NearEncounter(new Vector2(bounds.center.x, bounds.center.z), Mathf.Max(bounds.extents.x, bounds.extents.z));
                    if (touches) break;
                }
                if (!touches && !crowds) return true;
                if (crowds) break;
                point += normal * .1f;
            }
            // Места нет: объект возвращается в пул, в зарослях его заменит трава кромки.
            _decorCount--;
            _decorPools[variant].Release(placed.gameObject);
            return false;
        }

        // Видимый радиус у земли: габаритный круг пула описывает ещё и углы повёрнутой модели.
        private float VisibleRadius(int index)
        {
            int variant = _decorVariant[index];
            return _decorRadii[variant] * _decor[index].localScale.x / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y) * .75f;
        }

        private bool NearEncounter(Vector2 point, float radius)
        {
            if (_shownEncounters == null) return false;
            float clearance = _shownEncounters.FormationRadius.ToFloat() + radius + 1;
            for (int e = 0; e < _shownEncounters.Count; e++)
                if ((point - TrailPoint(_shownEncounters.Get(e).Center)).sqrMagnitude < clearance * clearance) return true;
            return false;
        }

        private bool BoundaryBlocksClearance(Vector2 point, float radius)
        {
            if (NearEncounter(point, radius)) return true;
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
