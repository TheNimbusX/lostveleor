using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Сказочная поляна по референсу владельца (29 сентября): середина арены — тёплая утоптанная
    // земля с каймой травы у края, в центре — рунный круг вровень с землёй, по кромке — красные
    // грибы, лиловые цветы и светящиеся кристаллы, у порталов — фонари, на земле — опавшие листья.
    // Только представление: Sim, проходимость и потоки случайности симуляции не меняются.
    public sealed partial class LayoutView
    {
        private const string LeavesPath = "Environment/Camp/GroundDetails/";
        private const string AltarPrefab = "CreatingAltar", PebblesPrefab = "MeadowPebbles";
        private float[] _clearingDistance;
        private readonly List<Matrix4x4>[] _pebbleField = { new List<Matrix4x4>(), new List<Matrix4x4>() };
        private Mesh[] _pebbleMeshes;
        private Material[] _pebbleMaterials;
        private Matrix4x4[] _pebblePivots;
        private Bounds _pebbleBounds;
        private bool _pebblesLookedUp;
        private readonly List<Matrix4x4>[] _leafField = new List<Matrix4x4>[6];
        private Material[] _leafMaterials;
        private Mesh _leafMesh;
        private Bounds _leafBounds;
        private bool _leavesLookedUp;
        private readonly List<(MeshRenderer Renderer, Color Glow)> _runes = new List<(MeshRenderer, Color)>();

        private int VariantNamed(string name)
        {
            for (int i = 0; i < _style.DecorVariants.Length; i++)
                if (_style.DecorVariants[i].Prefab != null && _style.DecorVariants[i].Prefab.name == name) return i;
            return -1;
        }

        // Земля вместо газона: внутри поляны дальше одного-трёх метров от края — вытоптанный грунт,
        // у края — кайма травы, внутри — редкие травяные островки. В грунте — вросшие камни:
        // редкие по всей земле и россыпями, гуще у каменистой арены (владелец, 29 сентября).
        private void PaintEarthClearing()
        {
            if (_shownMap.Outline == null || _shownMap.GladeCount != 1) return;
            const int n = TrailResolution;
            if (_clearingDistance == null) _clearingDistance = new float[n * n];
            var dist = _clearingDistance;
            FloorDistance(dist, true);
            float stones = CharacterOf(_shownMap, 0) == GladeCharacter.Rocky ? .7f : .45f;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (dist[i] <= 0) continue;
                    float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                    float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                    float rim = .7f + 1.3f * Mathf.PerlinNoise(px * .19f + 5, pz * .19f + 71);
                    float earth = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rim, rim + 1.3f, dist[i]));
                    earth *= 1 - .8f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.64f, .8f, Mathf.PerlinNoise(px * .11f + 33, pz * .11f + 9)));
                    if (earth <= .01f) continue;
                    var pixel = _campSurfacePixels[i];
                    pixel.r = (byte)Mathf.Max(pixel.r, earth * 215);
                    float bed = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.42f, .72f, Mathf.PerlinNoise(px * .21f + 17, pz * .21f + 41)));
                    pixel.g = (byte)Mathf.Lerp(pixel.g, Mathf.Lerp(.1f, stones, bed) * 255, earth);
                    _campSurfacePixels[i] = pixel;
                }
        }

        // Доля грунта в маске земли под точкой: 0 — трава, 1 — тропа или земля поляны.
        private float SurfaceEarth(float x, float z)
        {
            if (_campSurfacePixels == null) return 0;
            int px = Mathf.FloorToInt((x - _trailBounds.x) / _trailBounds.z * TrailResolution);
            int pz = Mathf.FloorToInt((z - _trailBounds.y) / _trailBounds.w * TrailResolution);
            if (px < 0 || pz < 0 || px >= TrailResolution || pz >= TrailResolution) return 0;
            return _campSurfacePixels[pz * TrailResolution + px].r / 255f;
        }

        // Рунный круг в центре поляны: кольцо-руина сплюснуто по высоте и лежит вровень с землёй,
        // по нему ходят. Растения пола из середины убираются, трава и листья его обходят.
        private void PlaceCenterCircle(LayoutMap map, int ring)
        {
            if (ring < 0 || map.GladeCount != 1 || (_shownEncounters != null && _shownEncounters.BossId >= 0)) return;
            var center = TrailPoint(map.GetGlade(0).Center);
            const float scale = 1.05f;
            float radius = _decorRadii[ring] / Mathf.Max(.01f, _style.DecorVariants[ring].ScaleRange.y) * .72f * scale;
            for (int a = 0; a < 16; a++)
            {
                float angle = a * Mathf.PI / 8;
                var edge = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + .6f);
                if (!map.Outline.Contains(new FixVec2(Fix64.FromDouble(edge.x), Fix64.FromDouble(edge.y)))) return;
            }
            for (int o = 0; o < map.ObstacleCount; o++)
            {
                var obstacle = map.GetObstacle(o);
                if (Vector2.Distance(center, TrailPoint(obstacle.Center)) < radius + obstacle.Radius.ToFloat() + .5f) return;
            }
            for (int i = _decorCount - 1; i >= 0; i--)
            {
                var p = _decor[i].position;
                if (Vector2.Distance(center, new Vector2(p.x, p.z)) > radius + .3f) continue;
                // Растения пола под кругом уходят в пул; порядок остального декора не важен.
                _decorPools[_decorVariant[i]].Release(_decor[i].gameObject);
                _decorCount--;
                _decor[i] = _decor[_decorCount]; _decorVariant[i] = _decorVariant[_decorCount];
            }
            SpawnDecor(ring, center.x, center.y, DecorRandom(0, 1021));
            var placed = _decor[_decorCount - 1];
            placed.localScale = new Vector3(scale, scale * .35f, scale);
            placed.position = new Vector3(center.x, 0, center.y);
            AddLandmark(.72f, false);
            var renderer = placed.GetComponentInChildren<MeshRenderer>();
            if (renderer != null) _runes.Add((renderer, renderer.sharedMaterial.GetColor("_EmissionColor")));
        }

        // Каменный алтарь из Creating (владелец, 29 сентября) — разовый ориентир на плече поляны,
        // на северной, дальней от камеры дуге: оттуда он виден целиком, кроны его не закрывают.
        // Стоит лицом к середине поляны и на боевой пол не заходит.
        private void PlaceAltar(LayoutMap map)
        {
            int altar = VariantNamed(AltarPrefab);
            if (altar < 0 || map.GladeCount == 0) return;
            var rng = DecorRandom(0, 1063);
            var glade = map.GetGlade(map.GladeCount == 1 ? 0 : rng.Next(map.GladeCount));
            var center = TrailPoint(glade.Center);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                // Сначала узкая дуга напротив камеры, потом — вся верхняя половина края.
                float spread = attempt < 24 ? .55f : 1.3f;
                float angle = Mathf.PI * (.5f + ((float)rng.NextDouble() - .5f) * spread);
                float shoulder = _decorRadii[altar] * .6f + .4f + (float)rng.NextDouble() * 1.8f;
                var point = center + new Vector2(Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                    Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                if (!TryForestDetail(map, altar, point, rng)) continue;
                var toward = center - point;
                _decor[_decorCount - 1].rotation = Quaternion.LookRotation(new Vector3(toward.x, 0, toward.y));
                AddLandmark(.75f, true);
                return;
            }
        }

        private bool FindPebbles()
        {
            if (_pebblesLookedUp) return _pebbleMeshes != null;
            _pebblesLookedUp = true;
            int variant = VariantNamed(PebblesPrefab);
            if (variant < 0) return false;
            var prefab = _style.DecorVariants[variant].Prefab;
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            int count = Mathf.Min(filters.Length, _pebbleField.Length);
            if (count == 0) return false;
            var meshes = new Mesh[count]; var materials = new Material[count]; var pivots = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                var renderer = filters[i].GetComponent<MeshRenderer>();
                if (filters[i].sharedMesh == null || renderer == null || renderer.sharedMaterial == null) return false;
                meshes[i] = filters[i].sharedMesh;
                // Своя копия с инстансингом: материал камней декора не меняется.
                materials[i] = new Material(renderer.sharedMaterial) { name = renderer.sharedMaterial.name + " — галька", enableInstancing = true };
                _ownedMaterials.Add(materials[i]);
                pivots[i] = prefab.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
            }
            _pebbleMeshes = meshes; _pebbleMaterials = materials; _pebblePivots = pivots;
            return true;
        }

        // Галька на утоптанной земле: мелкие камни кучками по одному-четыре, у каменистой арены гуще.
        // Рисуется инстансингом, как трава и листья: не декор и не препятствие, бой не закрывает.
        private void ScatterPebbles(LayoutMap map)
        {
            foreach (var list in _pebbleField) list.Clear();
            if (map.Outline == null || map.GladeCount != 1 || !FindPebbles()) return;
            var glade = map.GetGlade(0);
            var rng = DecorRandom(0, 1061);
            float rx = glade.Radii.X.ToFloat() + 2, rz = glade.Radii.Y.ToFloat() + 2;
            var center = TrailPoint(glade.Center);
            _pebbleBounds = new Bounds(new Vector3(center.x, 0, center.y), new Vector3(rx * 2 + 2, 2, rz * 2 + 2));
            int clusters = CharacterOf(map, 0) == GladeCharacter.Rocky ? 70 : 45;
            for (int attempt = 0; attempt < 600 && clusters > 0; attempt++)
            {
                var p = center + new Vector2(((float)rng.NextDouble() * 2 - 1) * rx, ((float)rng.NextDouble() * 2 - 1) * rz);
                int count = rng.Next(1, 5);
                double roll = rng.NextDouble();
                float earth = SurfaceEarth(p.x, p.y);
                if (earth < .45f || roll > .3f + earth * .4f) continue;
                if (NearPond(p.x, p.y, .5f) || NearLandmark(p.x, p.y, .3f) || InsideSolidDecor(p.x, p.y)) continue;
                clusters--;
                for (int k = 0; k < count; k++)
                {
                    var q = p + new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * .9f;
                    float size = k == 0 ? .2f + (float)rng.NextDouble() * .18f : .09f + (float)rng.NextDouble() * .12f;
                    int mesh = rng.Next(_pebbleMeshes.Length);
                    var rotation = Quaternion.Euler(((float)rng.NextDouble() - .5f) * 30, (float)rng.NextDouble() * 360,
                        ((float)rng.NextDouble() - .5f) * 30);
                    var scale = new Vector3(size, size * (.55f + (float)rng.NextDouble() * .35f), size);
                    if (SurfaceEarth(q.x, q.y) < .3f) continue;
                    // Камень наполовину в земле: торчит только верх, как у вросшей гальки.
                    _pebbleField[mesh].Add(Matrix4x4.TRS(new Vector3(q.x, -scale.y * .3f, q.y), rotation, scale) * _pebblePivots[mesh]);
                }
            }
        }

        private void DrawPebbles()
        {
            if (_pebbleMeshes == null) return;
            for (int m = 0; m < _pebbleMeshes.Length; m++)
            {
                var list = _pebbleField[m];
                if (list.Count == 0) continue;
                var parameters = new RenderParams(_pebbleMaterials[m])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = _pebbleBounds,
                };
                for (int start = 0; start < list.Count; start += 1023)
                    Graphics.RenderMeshInstanced(parameters, _pebbleMeshes[m], 0, list, Mathf.Min(1023, list.Count - start), start);
            }
        }

        // Акценты кромки: куртины грибов и лиловых цветов в травяной кайме у самого края пола —
        // перед зарослями, где их видно с камеры, — и редкие друзы кристаллов, светящиеся из зарослей.
        // Грибы и цветы — мелкая трава, как пучки на полу: сквозь них проходят, бой они не закрывают.
        private void ScatterEdgeAccents(LayoutMap map)
        {
            if (map.Outline == null || map.GladeCount != 1) return;
            int mushrooms = VariantNamed("MeadowMushrooms"), flowers = VariantNamed("MeadowFlower"), crystals = VariantNamed("MeadowCrystals");
            if (mushrooms < 0 && flowers < 0 && crystals < 0) return;
            var character = CharacterOf(map, 0);
            float crystalShare = character == GladeCharacter.Rocky ? .3f : .18f;
            var portals = new List<Vector2> { TrailPoint(map.EntryPoint) };
            for (int e = 0; e < map.ExitCount; e++) portals.Add(TrailPoint(map.ExitPoint(e)));
            var taken = new List<Vector2>();
            int crystalCount = 0;
            foreach (var (edge, normal) in OutlineEdges())
            {
                bool near = false;
                foreach (var spot in taken)
                    if ((spot - edge).sqrMagnitude < 36) { near = true; break; }
                foreach (var portal in portals)
                    if ((portal - edge).sqrMagnitude < 12) { near = true; break; }
                if (near) continue;
                var rng = DecorRandom(unchecked(Mathf.RoundToInt(edge.x * 4) * 486187739 + Mathf.RoundToInt(edge.y * 4) * 290797), 1031);
                taken.Add(edge);
                double roll = rng.NextDouble();
                if (roll < .15) continue;
                var tangent = new Vector2(normal.y, -normal.x);
                if (crystals >= 0 && crystalCount < 3 && roll < .15 + crystalShare)
                {
                    float scale = .9f + (float)rng.NextDouble() * .4f;
                    float reach = _decorRadii[crystals] / Mathf.Max(.01f, _style.DecorVariants[crystals].ScaleRange.y) * scale * .6f;
                    var point = edge + normal * (reach + .1f) + tangent * ((float)rng.NextDouble() - .5f);
                    if (!NearPond(point.x, point.y, reach) && !NearLandmark(point.x, point.y, reach)
                        && PlaceOffFloor(crystals, point, normal, scale, false, rng)) crystalCount++;
                    continue;
                }
                int kind = mushrooms >= 0 && (flowers < 0 || rng.NextDouble() < (character == GladeCharacter.Sunny ? .4 : .6)) ? mushrooms : flowers;
                if (kind < 0) continue;
                int count = kind == mushrooms ? rng.Next(3, 6) : rng.Next(5, 10);
                for (int item = 0; item < count; item++)
                {
                    var point = edge - normal * (.15f + (float)rng.NextDouble() * .85f) + tangent * ((float)rng.NextDouble() - .5f) * 2.8f;
                    float size = kind == mushrooms ? 1.1f + (float)rng.NextDouble() * .7f : 1.2f + (float)rng.NextDouble() * .45f;
                    if (!map.Outline.ContainsCell(Mathf.FloorToInt(point.x * 2), Mathf.FloorToInt(point.y * 2))
                        || NearPond(point.x, point.y, .3f) || NearLandmark(point.x, point.y, .2f) || InsideSolidDecor(point.x, point.y)
                        || NearNaturalTrail(point.x, point.y, .6f)) continue;
                    bool onObstacle = false;
                    for (int o = 0; o < map.ObstacleCount && !onObstacle; o++)
                        onObstacle = Vector2.Distance(point, TrailPoint(map.GetObstacle(o).Center)) < map.GetObstacle(o).Radius.ToFloat() + .3f;
                    if (onObstacle) continue;
                    SpawnDecor(kind, point.x, point.y, rng);
                    _decor[_decorCount - 1].localScale *= size;
                }
            }
        }

        // По фонарю с каждой стороны портала: столб за краем прохода, огонь развёрнут к тропе.
        private void PlacePortalLanterns(LayoutMap map)
        {
            int lantern = VariantNamed("MeadowLanternPost");
            if (lantern < 0 || map.Outline == null) return;
            var rng = DecorRandom(0, 1039);
            foreach (var portal in _portals)
            {
                var renderers = portal.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float half = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var forward = new Vector2(portal.forward.x, portal.forward.z).normalized;
                var side = new Vector2(forward.y, -forward.x);
                var center = new Vector2(portal.position.x, portal.position.z);
                for (int s = -1; s <= 1; s += 2)
                {
                    var point = center + side * s * (half * .75f + .5f) + forward * .4f;
                    float radius = _decorRadii[lantern] * .5f;
                    for (int push = 0; push < 12 && TouchesOutlinedFloor(point.x, point.y, radius); push++) point += side * s * .2f;
                    if (TouchesOutlinedFloor(point.x, point.y, radius) || NearPond(point.x, point.y, radius)) continue;
                    SpawnDecor(lantern, point.x, point.y, rng);
                    var placed = _decor[_decorCount - 1];
                    placed.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .03f, point.y);
                    // Кронштейн модели смотрит вдоль её оси X: фонарь висит над проходом.
                    var toward = -side * s;
                    placed.rotation = Quaternion.Euler(0, Mathf.Atan2(-toward.y, toward.x) * Mathf.Rad2Deg, 0);
                }
            }
        }

        private bool FindLeaves()
        {
            if (_leavesLookedUp) return _leafMesh != null;
            _leavesLookedUp = true;
            _leafMesh = Resources.Load<Mesh>(LeavesPath + "Ground detail quad");
            if (_leafMesh == null) return false;
            _leafMaterials = new Material[_leafField.Length];
            for (int i = 0; i < _leafMaterials.Length; i++)
            {
                var source = Resources.Load<Material>(LeavesPath + "Leaves " + i);
                if (source == null) { _leafMesh = null; return false; }
                // Своя копия с инстансингом: материал лагеря не меняется.
                _leafMaterials[i] = new Material(source) { name = source.name + " — разлом", enableInstancing = true };
                _ownedMaterials.Add(_leafMaterials[i]);
                _leafField[i] = new List<Matrix4x4>();
            }
            return true;
        }

        // Опавшие листья кучками у кромки и редкими пятнами по земле поляны.
        private void ScatterFallenLeaves(LayoutMap map)
        {
            foreach (var list in _leafField) list?.Clear();
            if (map.Outline == null || map.GladeCount != 1 || !FindLeaves()) return;
            var glade = map.GetGlade(0);
            var rng = DecorRandom(0, 1049);
            float rx = glade.Radii.X.ToFloat() + 3, rz = glade.Radii.Y.ToFloat() + 3;
            var center = TrailPoint(glade.Center);
            _leafBounds = new Bounds(new Vector3(center.x, 0, center.y), new Vector3(rx * 2 + 2, 2, rz * 2 + 2));
            for (int attempt = 0; attempt < 1600; attempt++)
            {
                var p = center + new Vector2(((float)rng.NextDouble() * 2 - 1) * rx, ((float)rng.NextDouble() * 2 - 1) * rz);
                int material = rng.Next(_leafField.Length);
                float size = .5f + (float)rng.NextDouble() * .6f, yaw = (float)rng.NextDouble() * 360;
                double roll = rng.NextDouble();
                if (!map.Outline.ContainsCell(Mathf.FloorToInt(p.x * 2), Mathf.FloorToInt(p.y * 2))) continue;
                float edge = EdgeDistance(p);
                float chance = edge < 3 ? .5f
                    : .45f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .8f, Mathf.PerlinNoise(p.x * .2f + 57, p.y * .2f + 3)));
                if (roll > chance || NearPond(p.x, p.y, .4f) || NearLandmark(p.x, p.y, .2f) || TrailWear(p.x, p.y) > 140) continue;
                _leafField[material].Add(Matrix4x4.TRS(new Vector3(p.x, .012f, p.y), Quaternion.Euler(0, yaw, 0), new Vector3(size, 1, size)));
            }
        }

        // Расстояние от точки пола до его края по полю, посчитанному для земли поляны.
        private float EdgeDistance(Vector2 p)
        {
            if (_clearingDistance == null) return 0;
            int px = Mathf.FloorToInt((p.x - _trailBounds.x) / _trailBounds.z * TrailResolution);
            int pz = Mathf.FloorToInt((p.y - _trailBounds.y) / _trailBounds.w * TrailResolution);
            if (px < 0 || pz < 0 || px >= TrailResolution || pz >= TrailResolution) return 0;
            return _clearingDistance[pz * TrailResolution + px];
        }

        private void DrawFallenLeaves()
        {
            if (_leafMesh == null || _leafMaterials == null) return;
            for (int m = 0; m < _leafField.Length; m++)
            {
                var list = _leafField[m];
                if (list == null || list.Count == 0) continue;
                var parameters = new RenderParams(_leafMaterials[m])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = _leafBounds,
                };
                for (int start = 0; start < list.Count; start += 1023)
                    Graphics.RenderMeshInstanced(parameters, _leafMesh, 0, list, Mathf.Min(1023, list.Count - start), start);
            }
        }

        private ParticleSystem _wisps;

        // Голубые огоньки над кромкой поляны, как в референсе: нативные частицы с мягким свечением
        // шейдера светлячков лагеря. Рождаются в полосе у края, над серединой боя их почти нет.
        private void BuildWisps(LayoutMap map)
        {
            if (map.Outline == null || map.GladeCount != 1) { if (_wisps != null) _wisps.gameObject.SetActive(false); return; }
            if (_wisps == null)
            {
                var shader = Resources.Load<Shader>("Shaders/CampRiftMotes");
                if (shader == null) return;
                var material = new Material(shader) { name = "Огоньки поляны" };
                _ownedMaterials.Add(material);
                var host = new GameObject("Огоньки поляны");
                host.transform.SetParent(CreateRoot("Огоньки поляны"), false);
                _wisps = host.AddComponent<ParticleSystem>();
                _wisps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var renderer = host.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var main = _wisps.main;
                main.loop = true; main.startSpeed = 0; main.maxParticles = 48;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6, 11);
                main.startSize = new ParticleSystem.MinMaxCurve(.14f, .26f);
                main.startColor = new Color(.5f, .9f, 1f, 1f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = _wisps.emission; emission.rateOverTime = 4.5f;
                var shape = _wisps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.rotation = new Vector3(90, 0, 0);
                shape.radiusThickness = .22f;
                var noise = _wisps.noise;
                noise.enabled = true; noise.strength = .35f; noise.frequency = .45f; noise.scrollSpeed = .25f;
                var velocity = _wisps.velocityOverLifetime;
                velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(0, 0);
                velocity.y = new ParticleSystem.MinMaxCurve(.03f, .12f);
                velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
                // Мерцание: огонёк то разгорается, то гаснет за время жизни.
                var colour = _wisps.colorOverLifetime;
                colour.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.95f, .2f), new GradientAlphaKey(.35f, .45f),
                        new GradientAlphaKey(.9f, .7f), new GradientAlphaKey(0, 1) });
                colour.color = gradient;
            }
            var glade = map.GetGlade(0);
            float rx = glade.Radii.X.ToFloat(), rz = glade.Radii.Y.ToFloat(), radius = Mathf.Max(rx, rz);
            var wispShape = _wisps.shape;
            wispShape.radius = radius + 1.5f;
            wispShape.scale = new Vector3(rx / radius, rz / radius, 1);
            _wisps.transform.position = new Vector3(glade.Center.X.ToFloat(), .7f, glade.Center.Y.ToFloat());
            _wisps.gameObject.SetActive(true);
            _wisps.Clear(); _wisps.Play();
        }

        // У половины стоячих камней — друза кристаллов у подножия, в пределах отпечатка камня.
        private void AddMenhirCrystals(LayoutObstacle obstacle, int index)
        {
            int crystals = VariantNamed("MeadowCrystals");
            var rng = DecorRandom(index, 1051);
            if (crystals < 0 || rng.NextDouble() < .5) return;
            float angle = (float)rng.NextDouble() * Mathf.PI * 2;
            var point = TrailPoint(obstacle.Center) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * obstacle.Radius.ToFloat() * .7f;
            SpawnDecor(crystals, point.x, point.y, rng);
            _decor[_decorCount - 1].localScale *= .55f;
        }

        private void UpdateRunes()
        {
            // Руны медленно дышат, а не мигают: это ориентир, а не сигнал опасности.
            float breath = .72f + .28f * Mathf.Sin(Time.time * .8f);
            foreach (var (renderer, glow) in _runes)
            {
                if (renderer == null) continue;
                _runeBlock.SetColor("_EmissionColor", glow * breath);
                renderer.SetPropertyBlock(_runeBlock);
            }
        }
    }
}
