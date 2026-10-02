using System.Collections;
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
        private const float AltarMass = 2f;
        private const string AltarPrefab = "CreatingAltar", PebblesPrefab = "MeadowPebbles", RootPrefab = "CreatingRoots", FernPrefab = "CreatingFern", GiantTreePrefab = "CreatingGiantTree";
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
        // Одна строка y ∈ [1, n − 2] после лесной подстилки; поле _clearingDistance уже посчитано
        // (FloorDistance внутри пола), stones — по характеру арены (LayoutView.CampSurface).
        // lakes — озёра карты (x, z, радиус): у берега кайма травы не нужна, земля поляны доходит
        // до воды, и озеро читается краем арены (владелец, 2 октября).
        private void EarthClearingRow(int y, float stones, Vector3[] lakes)
        {
            const int n = TrailResolution;
            var dist = _clearingDistance;
            for (int x = 1; x < n - 1; x++)
            {
                int i = y * n + x;
                if (dist[i] <= 0) continue;
                float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                float rim = .7f + 1.3f * Mathf.PerlinNoise(px * .19f + 5, pz * .19f + 71);
                float shore = float.MaxValue;
                foreach (var lake in lakes) shore = Mathf.Min(shore, Mathf.Sqrt((px - lake.x) * (px - lake.x) + (pz - lake.y) * (pz - lake.y)) - lake.z);
                float atWater = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4, 1.5f, shore));
                float width = Mathf.Lerp(1.3f, .4f, atWater);
                rim = Mathf.Lerp(rim, .05f, atWater);
                float earth = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rim, rim + width, dist[i]));
                // Травяные островки — после сглаживания стыков (EarthGrassPatchRow), иначе оно их заливало.
                if (earth <= .01f) continue;
                var pixel = _campSurfacePixels[i];
                pixel.r = (byte)Mathf.Max(pixel.r, earth * 215);
                float bed = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.42f, .72f, Mathf.PerlinNoise(px * .21f + 17, pz * .21f + 41)));
                pixel.g = (byte)Mathf.Lerp(pixel.g, Mathf.Lerp(.1f, stones, bed) * 255, earth);
                _campSurfacePixels[i] = pixel;
            }
        }

        // Стыки тропы и поляны (владелец, 30 сентября): тропа входила в поляну под углом, и между
        // ними оставался острый клин травы. Размытая маска грунта выше порога заливает только
        // вогнутые углы и узкие зазоры — там грунт с трёх сторон; ровный край почти не сдвигается.
        // Тропа вливается в поляну плавным раструбом, как на референсах.
        private void FilletEarth(float meters)
        {
            const int n = TrailResolution;
            int radius = Mathf.Max(1, Mathf.RoundToInt(meters / _trailBounds.z * n));
            var source = new float[n * n];
            for (int i = 0; i < source.Length; i++) source[i] = _campSurfacePixels[i].r / 255f;
            var blurred = BoxBlur(BoxBlur(source, radius), radius);
            System.Threading.Tasks.Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float fill = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.44f, .66f, blurred[i]));
                    if (fill * 255 <= _campSurfacePixels[i].r) continue;
                    var pixel = _campSurfacePixels[i];
                    pixel.r = (byte)(fill * 255);
                    _campSurfacePixels[i] = pixel;
                }
            });
        }

        // Два прохода скользящего среднего (строки, затем столбцы).
        private static float[] BoxBlur(float[] source, int radius)
        {
            const int n = TrailResolution;
            var rows = new float[n * n]; var result = new float[n * n];
            float scale = 1f / (radius * 2 + 1);
            System.Threading.Tasks.Parallel.For(0, n, y =>
            {
                float sum = 0;
                for (int x = -radius; x <= radius; x++) sum += source[y * n + Mathf.Clamp(x, 0, n - 1)];
                for (int x = 0; x < n; x++)
                {
                    rows[y * n + x] = sum * scale;
                    sum += source[y * n + Mathf.Min(n - 1, x + radius + 1)] - source[y * n + Mathf.Max(0, x - radius)];
                }
            });
            System.Threading.Tasks.Parallel.For(0, n, x =>
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++) sum += rows[Mathf.Clamp(y, 0, n - 1) * n + x];
                for (int y = 0; y < n; y++)
                {
                    result[y * n + x] = sum * scale;
                    sum += rows[Mathf.Min(n - 1, y + radius + 1) * n + x] - rows[Mathf.Max(0, y - radius) * n + x];
                }
            });
            return result;
        }

        // Редкие мягкие пятна травы на земле поляны: трава просвечивает, а не вырезана островом.
        // Газон и смесь травы с землёй по всей поляне владелец пробовал 30 сентября и вернул землю.
        private void EarthGrassPatchRow(int y)
        {
            const int n = TrailResolution;
            for (int x = 1; x < n - 1; x++)
            {
                int i = y * n + x;
                if (_clearingDistance[i] <= 2.5f) continue;
                float px = _trailBounds.x + (x + .5f) / n * _trailBounds.z;
                float pz = _trailBounds.y + (y + .5f) / n * _trailBounds.w;
                float patch = .5f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, .84f, Mathf.PerlinNoise(px * .11f + 33, pz * .11f + 9)));
                if (patch <= 0) continue;
                var pixel = _campSurfacePixels[i];
                pixel.r = (byte)(pixel.r * (1 - patch));
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
            if (ring < 0 || !map.IsArena || (_shownEncounters != null && _shownEncounters.BossId >= 0)) return;
            // У арены из сегментов — в последнем: ориентир у сундука и выхода.
            var center = TrailPoint(map.GetGlade(map.GladeCount - 1).Center);
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
            placed.position = new Vector3(center.x, FloorLevel(center.x, center.y), center.y);
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
                // Вплотную к кромке, а не в траве поодаль (владелец, 2 октября): ищем по лучу наружу первую
                // точку, где массивное основание уже не задевает пол.
                var direction = new Vector2(Mathf.Cos(angle) * glade.Radii.X.ToFloat(), Mathf.Sin(angle) * glade.Radii.Y.ToFloat()).normalized;
                var point = center;
                float core = _decorRadii[altar] * AltarMass * .55f;
                for (float step = 0; step < 30 && TouchesOutlinedFloor(point.x, point.y, core + .25f); step += .25f) point = center + direction * step;
                if (!TryForestDetail(map, altar, point, rng, AltarMass * .55f)) continue;
                var toward = center - point;
                var placedAltar = _decor[_decorCount - 1];
                placedAltar.rotation = Quaternion.LookRotation(new Vector3(toward.x, 0, toward.y));
                // Массивнее: основание шире, чем выше, — не «табуретка» рядом с героем.
                placedAltar.localScale = Vector3.Scale(placedAltar.localScale, new Vector3(AltarMass, AltarMass * .78f, AltarMass));
                placedAltar.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .12f, point.y);
                AddLandmark(.75f, true);
                return;
            }
        }

        // Гигантские деревья (референсы 30 сентября): огромные узловатые корни у кромки — ориентир,
        // три-четыре на поляну по дальней половине и бокам, — и из них дуб в 2,3 раза крупнее леса.
        // Вокруг корней и дальше по кромке — папоротники, как подлесок референсов.
        private void PlaceRootsAndFerns(LayoutMap map)
        {
            int root = VariantNamed(RootPrefab), fern = VariantNamed(FernPrefab);
            if (map.GladeCount == 0 || (root < 0 && fern < 0)) return;
            var roots = new List<Vector2>();
            int tree = VariantNamed("MeadowBroadleaf"), ferns = 0;
            for (int g = 0; g < map.GladeCount && root >= 0; g++)
            {
                var rng = DecorRandom(g, 1087);
                var glade = map.GetGlade(g);
                var center = TrailPoint(glade.Center);
                // Гиганты по всей дальней половине кромки и по бокам (референсы 30.09): три-четыре на поляну.
                int wanted = rng.NextDouble() < .5 ? 4 : 3;
                for (int attempt = 0; attempt < 90 && wanted > 0; attempt++)
                {
                    // От −15° до 195°: вся дальняя от камеры половина и бока. Ближняя дуга свободна —
                    // крона гиганта там легла бы на бой.
                    float angle = Mathf.PI * (-.08f + (float)rng.NextDouble() * 1.16f);
                    float shoulder = _decorRadii[root] * .35f + (float)rng.NextDouble() * 2.5f;
                    var point = center + new Vector2(Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                        Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                    bool crowded = false;
                    foreach (var other in roots) crowded |= Vector2.Distance(other, point) < _decorRadii[root] * 1.6f;
                    // Плотное ядро корней — за краем пола, тонкие кончики могут подходить к самой кромке.
                    if (crowded || !TryForestDetail(map, root, point, rng, .55f)) continue;
                    var toward = center - point;
                    // Корни раскинуты от ствола за краем: «открытая» сторона модели смотрит на поляну.
                    _decor[_decorCount - 1].rotation = Quaternion.LookRotation(new Vector3(toward.x, 0, toward.y))
                        * Quaternion.Euler(0, ((float)rng.NextDouble() - .5f) * 40, 0);
                    // Корни уходят в землю: над травой — изгибы, а не ровный край меша.
                    _decor[_decorCount - 1].position += Vector3.down * .3f;
                    AddLandmark(.7f, true);
                    // Корни — основание дерева: дуб растёт прямо из них, крона нависает над кромкой.
                    if (tree >= 0)
                    {
                        // Ствол позади, со стороны леса: камера смотрит сверху, и крона над серединой
                        // корней закрыла бы их целиком. Корни выходят из-под кроны к поляне.
                        var back = point - toward.normalized * _decorRadii[root] * .38f;
                        SpawnDecor(tree, back.x, back.y, rng);
                        var trunk = _decor[_decorCount - 1];
                        // Гигантское дерево: тот же дуб леса, но в 2,3 раза крупнее соседей.
                        trunk.localScale *= 2.3f;
                        trunk.position = new Vector3(back.x, BackgroundHeight(map, back.x, back.y) - .08f, back.y);
                    }
                    roots.Add(point);
                    wanted--;
                }
            }
            if (fern < 0) return;
            for (int g = 0; g < map.GladeCount; g++)
            {
                var rng = DecorRandom(g, 1091);
                var glade = map.GetGlade(g);
                var center = TrailPoint(glade.Center);
                // Кусты папоротника у корней: 3–5 вокруг каждого.
                foreach (var spot in roots)
                    for (int n = rng.Next(3, 6), attempt = 0; n > 0 && attempt < 30; attempt++)
                    {
                        float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                        float reach = _decorRadii[root] * (.85f + (float)rng.NextDouble() * .5f);
                        if (TryForestDetail(map, fern, spot + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach, rng, .6f, .8f)) { n--; ferns++; }
                    }
                // По кромке — купами по 2–4, со всех сторон, кроме ближней к камере дуги.
                for (int clump = 0, attempt = 0; clump < 14 && attempt < 90; attempt++)
                {
                    float angle = Mathf.PI * (-.1f + (float)rng.NextDouble() * 1.2f);
                    float shoulder = .5f + (float)rng.NextDouble() * 2.5f;
                    var anchor = center + new Vector2(Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                        Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                    int placed = 0;
                    for (int n = rng.Next(3, 6), tries = 0; n > 0 && tries < 14; tries++)
                    {
                        var jitter = new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * 1.8f;
                        if (TryForestDetail(map, fern, anchor + jitter, rng, .6f, .75f)) { n--; placed++; ferns++; }
                    }
                    if (placed > 0) clump++;
                }
            }
            if (Application.isPlaying) Debug.Log($"[Луга] корни: {roots.Count}, папоротники: {ferns}");
        }

        // Гигантские деревья (владелец, 2 октября): одно-два на арену в лесу за дальним от камеры краем
        // поляны. Камера смотрит с юга сверху, высокое дерево на севере уходит вверх кадра, а не на бой;
        // крона целиком над лесом и не нависает над полом. Обычный лес не встаёт в его ствол.
        private void PlaceGiantTrees(LayoutMap map)
        {
            int giant = VariantNamed(GiantTreePrefab);
            if (giant < 0 || map.GladeCount == 0) return;
            float crown = _decorRadii[giant] / Mathf.Max(.01f, _style.DecorVariants[giant].ScaleRange.y);
            var placed = new List<Vector2>();
            for (int g = 0; g < map.GladeCount; g++)
            {
                var rng = DecorRandom(g, 1103);
                var glade = map.GetGlade(g);
                var center = TrailPoint(glade.Center);
                int wanted = rng.NextDouble() < .45 ? 2 : 1;
                for (int attempt = 0; attempt < 60 && wanted > 0; attempt++)
                {
                    // Дальняя дуга: 25–155° (север), подальше от края, чтобы крона не легла на пол.
                    float angle = attempt < 30 ? Mathf.PI * (.14f + (float)rng.NextDouble() * .72f)
                        : Mathf.PI * (-.15f + (float)rng.NextDouble() * 1.3f);
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var point = center + new Vector2(direction.x * glade.Radii.X.ToFloat(), direction.y * glade.Radii.Y.ToFloat())
                        + direction * (crown * .6f + 1 + (float)rng.NextDouble() * 7);
                    bool crowded = false;
                    foreach (var other in placed) crowded |= Vector2.Distance(other, point) < crown * 1.5f;
                    // Крона не нависает над полом (камера), ствол — не в воде и не в другом ориентире.
                    if (crowded || TouchesOutlinedFloor(point.x, point.y, crown * .55f) || NearWaterEdge(point.x, point.y, crown * .5f)
                        || NearLandmark(point.x, point.y, crown * .45f)) continue;
                    SpawnDecor(giant, point.x, point.y, rng);
                    var tree = _decor[_decorCount - 1];
                    tree.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .3f, point.y);
                    // Обычный лес не встаёт под крону гиганта — она читается отдельной массой над лесом.
                    AddLandmark(.5f, false);
                    placed.Add(point);
                    wanted--;
                }
            }
            if (Application.isPlaying) Debug.Log($"[Луга] гигантских деревьев: {placed.Count}");
        }

        // Обрыв между сегментами (владелец, 2 октября): вдоль линии уступа — гряда крупных серых камней,
        // стоящих на нижней террасе и закрывающих ступень земли. В самом проходе камней нет: там
        // спрыгивают. Камни — ориентиры: лес и кусты их не теснят.
        private void PlaceLedgeCliffs(LayoutMap map)
        {
            if (map.LedgeCount == 0) return;
            var rocks = new List<int>();
            foreach (string name in new[] { "CreatingRock", "ArenaCreatingRockA", "ArenaCreatingRockB" })
            {
                int variant = VariantNamed(name);
                if (variant >= 0) rocks.Add(variant);
            }
            if (rocks.Count == 0) return;
            for (int l = 0; l < map.LedgeCount; l++)
            {
                var ledge = map.GetLedge(l);
                var point = TrailPoint(ledge.Point);
                var down = new Vector2(ledge.Down.X.ToFloat(), ledge.Down.Y.ToFloat());
                var along = new Vector2(-down.y, down.x);
                float drop = ledge.Drop.ToFloat();
                var rng = DecorRandom(l, 1109);
                for (float t = -46; t <= 46; t += 1.6f + (float)rng.NextDouble() * 1.4f)
                {
                    int variant = rocks[rng.Next(rocks.Count)];
                    float size = 2.1f + (float)rng.NextDouble() * 1.4f;
                    float reach = _decorRadii[variant] / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y) * size * .55f;
                    // Камень вдвинут в стенку: середина чуть ниже линии обрыва.
                    var at = point + along * t + down * (reach * .35f + (float)rng.NextDouble() * .4f);
                    if (TouchesOutlinedFloor(at.x, at.y, reach + .2f) || NearWaterEdge(at.x, at.y, reach)) continue;
                    SpawnDecor(variant, at.x, at.y, rng);
                    var rock = _decor[_decorCount - 1];
                    rock.localScale *= size;
                    rock.localScale = new Vector3(rock.localScale.x, rock.localScale.y * (drop / 2f + .6f), rock.localScale.z);
                    rock.position = new Vector3(at.x, FloorLevel(at.x, at.y) - .2f, at.y);
                    AddLandmark(.6f, false);
                }
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
            if (map.Outline == null || !map.IsArena || !FindPebbles()) return;
            // Каждый сегмент арены — своя россыпь (у одной поляны — g = 0, как раньше).
            for (int g = 0; g < map.GladeCount; g++)
            {
            var glade = map.GetGlade(g);
            var rng = DecorRandom(g, 1061);
            float rx = glade.Radii.X.ToFloat() + 2, rz = glade.Radii.Y.ToFloat() + 2;
            var center = TrailPoint(glade.Center);
            var segmentBounds = new Bounds(new Vector3(center.x, FloorLevel(center.x, center.y), center.y), new Vector3(rx * 2 + 2, 2, rz * 2 + 2));
            if (g == 0) _pebbleBounds = segmentBounds; else _pebbleBounds.Encapsulate(segmentBounds);
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
                    _pebbleField[mesh].Add(Matrix4x4.TRS(new Vector3(q.x, FloorLevel(q.x, q.y) - scale.y * .3f, q.y), rotation, scale) * _pebblePivots[mesh]);
                }
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
        private IEnumerator ScatterEdgeAccentsSteps(LayoutMap map)
        {
            if (map.Outline == null || !map.IsArena) yield break;
            int mushrooms = VariantNamed("MeadowMushrooms"), flowers = VariantNamed("MeadowFlower"), crystals = VariantNamed("MeadowCrystals");
            if (mushrooms < 0 && flowers < 0 && crystals < 0) yield break;
            var character = CharacterOf(map, 0);
            float crystalShare = character == GladeCharacter.Rocky ? .3f : .18f;
            var portals = new List<Vector2> { TrailPoint(map.EntryPoint) };
            for (int e = 0; e < map.ExitCount; e++) portals.Add(TrailPoint(map.ExitPoint(e)));
            var taken = new List<Vector2>();
            int crystalCount = 0;
            foreach (var (edge, normal) in OutlineEdges())
            {
                yield return null;
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
                // Грибы растут купами у пней, корней и стволов (PlaceMushroomClusters): по кромке они
                // вставали рядами, как забор (владелец, 2 октября). Здесь — только редкие цветы.
                int kind = flowers;
                if (kind < 0 || rng.NextDouble() < .45) continue;
                int count = rng.Next(4, 9);
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
            yield return null;
            PlaceMushroomClusters(map, mushrooms);
        }

        // Грибы — редкие купы по 3–7 у оснований: пни, поваленные стволы, корни гигантов, стволы
        // деревьев у кромки. В купе один-два крупных и мелочь вокруг, купы не ближе 8 м друг к другу.
        private void PlaceMushroomClusters(LayoutMap map, int mushrooms)
        {
            if (mushrooms < 0) return;
            var anchors = new List<(Vector2 Point, float Radius, int Priority)>();
            for (int i = 0; i < _decorCount; i++)
            {
                var variant = _style.DecorVariants[_decorVariant[i]];
                string name = variant.Prefab != null ? variant.Prefab.name : "";
                int priority = name == "CreatingStump" || name == "MeadowFallenLog" || name == RootPrefab ? 0
                    : variant.Kind == DecorKind.Tree ? 1 : -1;
                if (priority < 0) continue;
                var p = new Vector2(_decor[i].position.x, _decor[i].position.z);
                // Только у кромки, где купу видно с камеры, и не на самом полу.
                if (!TouchesOutlinedFloor(p.x, p.y, priority == 0 ? 7 : 4.5f) || TouchesOutlinedFloor(p.x, p.y, .8f)) continue;
                float radius = priority == 0 ? VisibleRadius(i) * .8f : .55f * _decor[i].localScale.x;
                anchors.Add((p, radius, priority));
            }
            var rng = DecorRandom(0, 1097);
            // Пни и корни — первыми; внутри приоритета порядок перемешан детерминированно.
            for (int i = anchors.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (anchors[i], anchors[j]) = (anchors[j], anchors[i]); }
            anchors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            var clusters = new List<Vector2>();
            int wanted = rng.Next(4, 8);
            foreach (var anchor in anchors)
            {
                if (clusters.Count >= wanted) break;
                bool near = false;
                foreach (var c in clusters) near |= (c - anchor.Point).sqrMagnitude < 64;
                if (near || NearPond(anchor.Point.x, anchor.Point.y, anchor.Radius + 1)) continue;
                int count = rng.Next(3, 8), placed = 0;
                // Купа жмётся к основанию с одной стороны, а не обходит его кольцом.
                float side = (float)rng.NextDouble() * Mathf.PI * 2;
                for (int item = 0, tries = 0; item < count && tries < 24; tries++)
                {
                    float angle = side + ((float)rng.NextDouble() - .5f) * 2.2f;
                    float reach = anchor.Radius + .05f + (float)rng.NextDouble() * .7f;
                    var point = anchor.Point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                    if (NearPond(point.x, point.y, .3f) || NearLandmark(point.x, point.y, .1f)) continue;
                    SpawnDecor(mushrooms, point.x, point.y, rng);
                    // Один-два крупных, остальные мелкие.
                    float size = item < 2 ? 1.3f + (float)rng.NextDouble() * .6f : .55f + (float)rng.NextDouble() * .55f;
                    var instance = _decor[_decorCount - 1];
                    instance.localScale *= size;
                    instance.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .02f, point.y);
                    item++; placed++;
                }
                if (placed > 0) clusters.Add(anchor.Point);
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
            if (map.Outline == null || !map.IsArena || !FindLeaves()) return;
            for (int g = 0; g < map.GladeCount; g++)
            {
            var glade = map.GetGlade(g);
            var rng = DecorRandom(g, 1049);
            float rx = glade.Radii.X.ToFloat() + 3, rz = glade.Radii.Y.ToFloat() + 3;
            var center = TrailPoint(glade.Center);
            var segmentBounds = new Bounds(new Vector3(center.x, FloorLevel(center.x, center.y), center.y), new Vector3(rx * 2 + 2, 2, rz * 2 + 2));
            if (g == 0) _leafBounds = segmentBounds; else _leafBounds.Encapsulate(segmentBounds);
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
                _leafField[material].Add(Matrix4x4.TRS(new Vector3(p.x, FloorLevel(p.x, p.y) + .012f, p.y), Quaternion.Euler(0, yaw, 0), new Vector3(size, 1, size)));
            }
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
            if (map.Outline == null || !map.IsArena) { if (_wisps != null) _wisps.gameObject.SetActive(false); return; }
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
            var glade = map.GetGlade(map.GladeCount - 1);
            float rx = glade.Radii.X.ToFloat(), rz = glade.Radii.Y.ToFloat(), radius = Mathf.Max(rx, rz);
            var wispShape = _wisps.shape;
            wispShape.radius = radius + 1.5f;
            wispShape.scale = new Vector3(rx / radius, rz / radius, 1);
            _wisps.transform.position = new Vector3(glade.Center.X.ToFloat(), FloorLevel(glade.Center.X.ToFloat(), glade.Center.Y.ToFloat()) + .7f, glade.Center.Y.ToFloat());
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
