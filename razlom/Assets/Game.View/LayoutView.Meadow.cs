using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private readonly MeadowLighting _meadowLighting = new MeadowLighting();
        private readonly List<Mesh> _meadowMeshes = new List<Mesh>();
        private readonly List<Transform> _portals = new List<Transform>();
        private readonly List<Transform> _caches = new List<Transform>();
        private readonly List<Transform> _dropMarks = new List<Transform>();
        private ViewPool _portalPool, _cachePool, _dropPool;
        private Mesh _bankMesh, _ringMesh, _backgroundMesh;
        private Vector2 _reliefOffset;
        private readonly List<Vector4> _ponds = new List<Vector4>();
        private int _arenaCharacter = -1;
        private MeshRenderer _runeRenderer;
        private Color _runeGlow;
        private MaterialPropertyBlock _runeBlock;
        // x, y — центр ориентира на земле (мировые x и z), z — радиус, который он реально занимает,
        // w = 1 — ориентир виден только под открытым небом (круг рун, домик), кроны над ним не нависают.
        private readonly List<Vector4> _landmarkSpots = new List<Vector4>();
        private bool[] _landmarkVariants;
        private Mesh _waterMesh;
        private GameObject _water;
        private GameObject _banks;
        private Material _glowMaterial;
        private Material _portalSurfaceMaterial;
        private MaterialPropertyBlock _landmarkBlock;
        private bool _forestBreezeActive;
        private Vector4 _savedBreeze;
        private float _savedBreezeTime, _previousBreezeTime;

        private void OnDisable() { _meadowLighting.Restore(); RestoreForestBreeze(); }

        private void ClearMeadow()
        {
            RestoreForestBreeze();
            ClearRivers();
            foreach (var portal in _portals) _portalPool?.Release(portal.gameObject);
            foreach (var cache in _caches) _cachePool?.Release(cache.gameObject);
            foreach (var mark in _dropMarks) _dropPool?.Release(mark.gameObject);
            _portals.Clear(); _caches.Clear(); _dropMarks.Clear();
            _runeRenderer = null;
            _landmarkSpots.Clear();
            if (_banks != null) _banks.SetActive(false);
            if (_water != null) _water.SetActive(false);
            if (_shore != null) _shore.SetActive(false);
            _meadowLighting.Restore();
        }
        private void DisposeMeadow()
        {
            ClearMeadow();
            _meadowLighting.Dispose();
            foreach (var mesh in _meadowMeshes) DestroyOwned(mesh);
            _meadowMeshes.Clear();
            DestroyOwned(_campSurfaceMap); _campSurfaceMap = null; _campSurfacePixels = null;
            _bankMesh = _ringMesh = _backgroundMesh = null; _banks = null;
            _waterMesh = null; _water = null; _ponds.Clear();
            _shoreMesh = null; _shore = null;
            _riverMesh = _riverBankMesh = _bridgeMesh = null;
            _riverObject = _riverBanks = null; _bridgePool = null;
            _portalPool = _cachePool = _dropPool = null;
        }

        private void InitializeMeadow()
        {
            _landmarkBlock = new MaterialPropertyBlock();
            _runeBlock = new MaterialPropertyBlock();
            var root = CreateRoot("Пул: ориентиры");
            _glowMaterial = ViewMaterials.CreateLit(Color.white);
            _glowMaterial.EnableKeyword("_EMISSION");
            _glowMaterial.SetFloat("_Cull", 0);
            _ownedMaterials.Add(_glowMaterial);
            _portalSurfaceMaterial = new Material(Shader.Find("Game/Forest Portal"));
            _ownedMaterials.Add(_portalSurfaceMaterial);
            _ringMesh = MakeRing(); _meadowMeshes.Add(_ringMesh);
            _portalPool = new ViewPool(root, () => MakeLandmark(true), 9, false);
            _cachePool = new ViewPool(root, () => MakeLandmark(false), 8, false);
            _dropPool = new ViewPool(root, () => MakeLandmark(false), 4, false);
            _banks = new GameObject("Земляной край");
            _banks.transform.SetParent(CreateRoot("Граница лугов"), false);
            _banks.AddComponent<MeshFilter>();
            var material = CreateLocationGround(new Color(.65f, .76f, .64f),
                _style.RoomFloorTexture, _style.PathFloorTexture, _style.FloorTextureTiling, .035f);
            _ownedMaterials.Add(material);
            _banks.AddComponent<MeshRenderer>().sharedMaterial = material;
            var background = CreateLocationGround(new Color(.65f, .76f, .64f),
                _style.RoomFloorTexture, _style.PathFloorTexture, _style.FloorTextureTiling, 0);
            _ownedMaterials.Add(background);
            _groundFill.GetComponent<MeshRenderer>().sharedMaterial = background;
            _backgroundMesh = new Mesh { name = "Мягкий рельеф фона" };
            _meadowMeshes.Add(_backgroundMesh);
            _groundFill.GetComponent<MeshFilter>().sharedMesh = _backgroundMesh;
            _water = new GameObject("Лесные пруды");
            _water.transform.SetParent(_banks.transform.parent, false);
            _waterMesh = new Mesh { name = "Вода прудов" }; _meadowMeshes.Add(_waterMesh);
            _water.AddComponent<MeshFilter>().sharedMesh = _waterMesh;
            var waterMaterial = new Material(Shader.Find("Razlom/Forest Water"));
            _ownedMaterials.Add(waterMaterial);
            _water.AddComponent<MeshRenderer>().sharedMaterial = waterMaterial;
        }

        private void BuildMeadow(LayoutMap map, float cell)
        {
            if (map.PlacedCount == 0) return;
            if (_portalPool == null) InitializeMeadow();
            ChoosePonds(map);
            BuildBanks(map.Outline != null ? .5f : cell);
            ScatterForest(map, cell);
            BuildPondWater();
            BuildRivers(map);
            BuildReadableShores();
            ScatterForestDetails(map);
            if (map.Routes != null)
            {
                AddPortal(map.EntryPoint, map.Routes.EntryFacing, false);
                for (int e = 0; e < map.ExitCount; e++)
                {
                    var point = map.ExitPoint(e);
                    int index = map.Routes.CellAt(point), parent = index >= 0 ? map.Routes.ParentCell(index) : -1;
                    var direction = parent >= 0 ? point - map.Routes.GetCell(parent).Center : map.Routes.EntryFacing;
                    AddPortal(point, direction, true);
                }
                for (int b = 0; b < map.RewardBranchCount; b++)
                {
                    var cache = _cachePool.Acquire().transform;
                    var point = map.CenterOf(map.GetRewardBranch(b));
                    cache.position = new Vector3(point.X.ToFloat(), .03f, point.Y.ToFloat());
                    cache.rotation = Quaternion.Euler(0, 25, 0); _caches.Add(cache);
                }
            }
            if (Application.isPlaying && _driver != null) _meadowLighting.Apply(_style);
            ApplyCampSurface();
            UpdateMeadow();
        }

        private void AddPortal(FixVec2 point, FixVec2 direction, bool exit)
        {
            var portal = _portalPool.Acquire().transform;
            portal.name = exit ? "Проход дальше" : "Вход в луга";
            portal.position = new Vector3(point.X.ToFloat(), 0, point.Y.ToFloat());
            portal.rotation = Quaternion.LookRotation(new Vector3(direction.X.ToFloat(), 0, direction.Y.ToFloat()));
            SetGlow(portal, exit ? new Color(.8f, .42f, .1f) : new Color(.18f, .65f, .52f));
            _portals.Add(portal);
        }
        private void UpdateMeadow()
        {
            var run = _driver?.Run;
            if (run == null || run.Map != _shownMap) return;
            if (Application.isPlaying && _style.UseCampLighting)
            {
                if (!_forestBreezeActive)
                {
                    _savedBreeze = Shader.GetGlobalVector("_CampBreeze");
                    _savedBreezeTime = Shader.GetGlobalFloat("_CampBreezePreviousTime");
                    _previousBreezeTime = Time.time;
                    _forestBreezeActive = true;
                }
                // Те же лагерные материалы получают тихие порывы только на время разлома.
                Shader.SetGlobalVector("_CampBreeze", new Vector4(.788f, .616f,
                    .48f + .1f * Mathf.Sin(Time.time * .37f), Time.time));
                Shader.SetGlobalFloat("_CampBreezePreviousTime", _previousBreezeTime);
                _previousBreezeTime = Time.time;
            }
            if (_shownMap != null && _portalPool != null && Application.isPlaying)
                _meadowLighting.Apply(_style);
            for (int i = 1; i < _portals.Count; i++)
                SetGlow(_portals[i], run.Phase == RunPhase.SeekingExit ? new Color(.22f, .95f, .65f) : new Color(.8f, .42f, .1f));
            for (int b = 0; b < _caches.Count; b++) _caches[b].gameObject.SetActive(!run.IsBranchClaimed(b));
            UpdateDropMarks(run);
            if (_runeRenderer != null)
            {
                // Руны медленно дышат, а не мигают: это ориентир, а не сигнал опасности.
                _runeBlock.SetColor("_EmissionColor", _runeGlow * (.72f + .28f * Mathf.Sin(Time.time * .8f)));
                _runeRenderer.SetPropertyBlock(_runeBlock);
            }
        }

        private void RestoreForestBreeze()
        {
            if (!_forestBreezeActive) return;
            Shader.SetGlobalVector("_CampBreeze", _savedBreeze);
            Shader.SetGlobalFloat("_CampBreezePreviousTime", _savedBreezeTime);
            _forestBreezeActive = false;
        }

        /// <summary>
        /// Добыча с элит: уменьшенный схрон с кольцом на месте смерти, пока предмет
        /// не подобран. Способность подсвечена красным, вещь — золотым. Это
        /// временная метка до арта владельца.
        /// </summary>
        private void UpdateDropMarks(RiftRun run)
        {
            while (_dropPool != null && _dropMarks.Count < run.DropCount)
            {
                var mark = _dropPool.Acquire().transform;
                mark.name = "Добыча с элиты";
                mark.localScale = Vector3.one * .6f;
                _dropMarks.Add(mark);
            }
            for (int d = 0; d < _dropMarks.Count; d++)
            {
                bool visible = d < run.DropCount && !run.GetDrop(d).Claimed;
                _dropMarks[d].gameObject.SetActive(visible);
                if (!visible) continue;
                RunDrop drop = run.GetDrop(d);
                _dropMarks[d].position = new Vector3(drop.Position.X.ToFloat(), .03f, drop.Position.Y.ToFloat());
                SetGlow(_dropMarks[d], drop.Offer.Kind == RewardKind.Ability
                    ? new Color(.95f, .35f, .2f) : new Color(1f, .78f, .3f));
            }
        }
        private void SetGlow(Transform root, Color color)
        {
            var renderer = root.Find("Сияние").GetComponent<MeshRenderer>();
            _landmarkBlock.SetColor("_BaseColor", color);
            _landmarkBlock.SetColor("_EmissionColor", color * 1.7f);
            renderer.SetPropertyBlock(_landmarkBlock);
        }

        private GameObject MakeLandmark(bool portal)
        {
            var root = new GameObject(portal ? "Портал" : "Схрон");
            var prefab = portal ? _style.PortalPrefab : _style.CachePrefab;
            if (prefab != null)
            {
                var model = Instantiate(prefab, root.transform);
                model.name = "Модель"; RemoveColliders(model);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = new Bounds(); bool first = true;
                foreach (var renderer in renderers)
                {
                    if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
                    var materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        var source = materials[m];
                        // Отдельная поверхность портала из пака; каменная рама сохраняет материал.
                        if (portal && source != null && source.name == "Portal01")
                        { materials[m] = _portalSurfaceMaterial; continue; }
                        if (source == null || source.shader.name.StartsWith("Universal Render Pipeline")) continue;
                        var replacement = ViewMaterials.CreateLit(source.HasProperty("_Color") ? source.color : Color.white);
                        if (source.HasProperty("_MainTex")) replacement.SetTexture("_BaseMap", source.mainTexture);
                        _ownedMaterials.Add(replacement); materials[m] = replacement;
                    }
                    renderer.sharedMaterials = materials;
                }
                float scale = (portal ? 3.1f : 1.1f) / Mathf.Max(.01f, portal ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z));
                model.transform.localScale *= scale;
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            }
            else if (!portal)
            {
                var box = CreateTile(_entranceMaterial); box.transform.SetParent(root.transform, false);
                box.transform.localScale = new Vector3(1, .65f, .7f); box.transform.localPosition = Vector3.up * .325f;
            }
            var glow = new GameObject("Сияние"); glow.transform.SetParent(root.transform, false);
            glow.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            glow.AddComponent<MeshRenderer>().sharedMaterial = _glowMaterial;
            glow.transform.localPosition = portal ? Vector3.up * 1.4f : Vector3.up * .04f;
            glow.transform.localRotation = portal ? Quaternion.identity : Quaternion.Euler(90, 0, 0);
            glow.transform.localScale = portal ? new Vector3(1, 1.25f, 1) : Vector3.one * .7f;
            SetGlow(root.transform, new Color(.9f, .65f, .18f));
            return root;
        }

        private static Mesh MakeRing()
        {
            var vertices = new Vector3[66]; var triangles = new int[32 * 6];
            for (int i = 0; i <= 32; i++)
            {
                float angle = i * Mathf.PI / 16;
                var point = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                vertices[i * 2] = point * .88f; vertices[i * 2 + 1] = point;
                if (i == 32) continue;
                int t = i * 6, v = i * 2;
                triangles[t] = v; triangles[t+1] = v+2; triangles[t+2] = v+1;
                triangles[t+3] = v+1; triangles[t+4] = v+2; triangles[t+5] = v+3;
            }
            var mesh = new Mesh { name = "Свечение ориентира", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private void BuildBanks(float cell)
        {
            // Естественный контур уже окружён непрерывным фоновым рельефом.
            // Полосы по каждой клетке накладывались друг на друга на поворотах
            // и выдавали сетку резкими треугольными гранями освещения.
            if (_shownMap.Outline != null) { _banks.SetActive(false); return; }
            if (_bankMesh == null)
            {
                _bankMesh = new Mesh { name = "Контур занятого пола" };
                _meadowMeshes.Add(_bankMesh);
                _banks.GetComponent<MeshFilter>().sharedMesh = _bankMesh;
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var ordered = new List<long>(_occupiedCells); ordered.Sort();
            foreach (long key in ordered)
            {
                int x = (int)(key >> 32), y = (int)key;
                for (int d = 0; d < 4; d++)
                {
                    Directions.Step((Direction)d, out int dx, out int dy);
                    if (_occupiedCells.Contains(CellKey(x + dx, y + dy))) continue;
                    Vector3 normal = new Vector3(dx, 0, dy), along = new Vector3(dy, 0, -dx);
                    var center = new Vector3((x + .5f) * cell, 0, (y + .5f) * cell) + normal * cell * .5f;
                    if (NearPond(center.x, center.z, .4f)) continue;
                    int start = vertices.Count;
                    for (int s = 0; s < 5; s++)
                    {
                        float outward = s * .3f + .02f;
                        var a = center - along * cell * .5f + normal * outward;
                        var b = center + along * cell * .5f + normal * outward;
                        // Пятна плавно уходят под фон, поэтому край не образует сплошную ограду.
                        a.y = BankHeight(a.x, a.z, s / 4f);
                        b.y = BankHeight(b.x, b.z, s / 4f);
                        vertices.Add(a); vertices.Add(b);
                        if (s == 4) continue;
                        int v = start + s * 2;
                        triangles.Add(v); triangles.Add(v+2); triangles.Add(v+1);
                        triangles.Add(v+1); triangles.Add(v+2); triangles.Add(v+3);
                    }
                }
            }
            _bankMesh.Clear(); _bankMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _bankMesh.SetVertices(vertices); _bankMesh.SetTriangles(triangles, 0); _bankMesh.RecalculateNormals(); _bankMesh.RecalculateBounds();
            _banks.SetActive(true);
        }

        private static float BankHeight(float x, float z, float across)
        {
            float patch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.47f, .66f,
                Mathf.PerlinNoise(x * .095f + 137, z * .095f + 281)));
            float height = Mathf.Lerp(.09f, .22f, Mathf.PerlinNoise(x * .23f, z * .23f));
            return Mathf.Sin(across * Mathf.PI) * height * patch - .045f;
        }

        private enum GladeCharacter { Sunny, Rocky, Waterside }

        private static int WatersideGlade(LayoutMap map)
        {
            int chosen = Mathf.Max(0, map.GladeCount - 1);
            float nearest = float.MaxValue;
            for (int g = 0; g < map.GladeCount; g++)
                for (int w = 0; w < map.WaterCount; w++)
                {
                    var delta = map.GetGlade(g).Center - map.GetWater(w).Center;
                    float dx = delta.X.ToFloat(), dz = delta.Y.ToFloat();
                    float distance = dx * dx + dz * dz;
                    if (distance < nearest) { nearest = distance; chosen = g; }
                }
            return chosen;
        }

        private GladeCharacter CharacterOf(LayoutMap map, int index)
        {
            if (map.GladeCount == 1) return ArenaCharacter(map);
            if (map.GladeCount < 3) return GladeCharacter.Rocky;
            int water = WatersideGlade(map);
            if (index == water) return GladeCharacter.Waterside;
            // После исключения берега чередуем открытые и каменистые поляны.
            return (index < water ? index : index - 1) % 2 == 0 ? GladeCharacter.Sunny : GladeCharacter.Rocky;
        }

        // Одна арена на уровень: без выбора по сиду каждая арена была бы каменистой.
        // Береговая — только если в арене есть вода. Поток визуальный, Simulation.Rng не трогается.
        private GladeCharacter ArenaCharacter(LayoutMap map)
        {
            if (_arenaCharacter < 0)
                _arenaCharacter = DecorRandom(0, 877).Next(map.WaterCount > 0 ? 3 : 2);
            return _arenaCharacter == 0 ? GladeCharacter.Sunny
                : _arenaCharacter == 1 ? GladeCharacter.Rocky : GladeCharacter.Waterside;
        }

        private static int NearestGlade(LayoutMap map, float x, float z)
        {
            int nearest = 0; float distance = float.MaxValue;
            for (int g = 0; g < map.GladeCount; g++)
            {
                var center = map.GetGlade(g).Center;
                float dx = x - center.X.ToFloat(), dz = z - center.Y.ToFloat();
                float candidate = dx * dx + dz * dz;
                if (candidate < distance) { nearest = g; distance = candidate; }
            }
            return nearest;
        }

        private void ScatterForest(LayoutMap map, float cell)
        {
            var trees = new List<int>();
            for (int i = 0; i < _style.DecorVariants.Length; i++)
                if (_style.DecorVariants[i].Kind == DecorKind.Tree && _style.DecorVariants[i].Weight > 0) trees.Add(i);
            var first = map.GetPlaced(0);
            float minX=first.OriginX*cell, maxX=(first.OriginX+first.Width)*cell;
            float minZ=first.OriginY*cell, maxZ=(first.OriginY+first.Height)*cell;
            for (int m=1;m<map.PlacedCount;m++)
            {
                var p=map.GetPlaced(m); minX=Mathf.Min(minX,p.OriginX*cell); maxX=Mathf.Max(maxX,(p.OriginX+p.Width)*cell);
                minZ=Mathf.Min(minZ,p.OriginY*cell); maxZ=Mathf.Max(maxZ,(p.OriginY+p.Height)*cell);
            }
            BuildBackgroundRelief(map, minX, maxX, minZ, maxZ);
            PlaceLandmarks(map);
            // Вода и рельеф уже выбраны: опушка учитывает берег и высоту земли этой карты.
            ScatterBoundaryDecor(map.Outline != null ? .5f : cell);
            if (trees.Count==0 || _style.ForestBandWidth<=0) return;
            // Ближние группы занимают часть общего бюджета, не увеличивая лимит деревьев.
            int created = map.Outline != null ? _edgeTreeCount : 0;
            var groveRng = DecorRandom(0, 193);
            float groveX = (float)groveRng.NextDouble() * 1000, groveZ = (float)groveRng.NextDouble() * 1000;
            float treeWeight = 0;
            foreach (int tree in trees) treeWeight += _style.DecorVariants[tree].Weight;
            for (float x=minX-_style.ForestBandWidth;x<maxX+_style.ForestBandWidth;x+=_style.ForestSpacing)
                for (float z=minZ-_style.ForestBandWidth;z<maxZ+_style.ForestBandWidth;z+=_style.ForestSpacing)
                {
                    var rng=DecorRandom(unchecked((int)(x*73)+(int)(z*997)),91);
                    // Two spatial scales produce small copses, larger groves and persistent open gaps.
                    float grove = Mathf.PerlinNoise(x * .055f + groveX, z * .055f + groveZ) * .7f
                        + Mathf.PerlinNoise(x * .12f + groveZ, z * .12f + groveX) * .3f;
                    if (grove < .32f || rng.NextDouble() > Mathf.Lerp(.5f, .98f, Mathf.InverseLerp(.32f, .65f, grove))) continue;
                    float px=x+(float)(rng.NextDouble()-.5)*_style.ForestSpacing*.85f;
                    float pz=z+(float)(rng.NextDouble()-.5)*_style.ForestSpacing*.85f;
                    if (NearPond(px, pz, 3)) continue;
                    if (map.GladeCount > 0)
                    {
                        var character = CharacterOf(map, NearestGlade(map, px, pz));
                        // Светлая опушка получает просветы в кронах, без дополнительных источников света.
                        float density = character == GladeCharacter.Sunny ? .68f : character == GladeCharacter.Rocky ? .85f : .98f;
                        if (rng.NextDouble() > density) continue;
                    }
                    float pick = (float)rng.NextDouble() * treeWeight;
                    int variant = trees[trees.Count - 1];
                    foreach (int tree in trees) { pick -= _style.DecorVariants[tree].Weight; if (pick <= 0) { variant = tree; break; } }
                    // Крона может нависать над ориентиром, ствол — нет.
                    if (NearLandmark(px, pz, _decorRadii[variant] * .35f) || ShadesLandmark(px, pz, _decorRadii[variant] * 1.1f)) continue;
                    float nearest=float.MaxValue;
                    for (int m=0;m<map.PlacedCount;m++)
                    {
                        var p=map.GetPlaced(m);
                        float dx=Mathf.Max(0,Mathf.Max(p.OriginX*cell-px,px-(p.OriginX+p.Width)*cell));
                        float dz=Mathf.Max(0,Mathf.Max(p.OriginY*cell-pz,pz-(p.OriginY+p.Height)*cell));
                        nearest=Mathf.Min(nearest,Mathf.Sqrt(dx*dx+dz*dz));
                    }
                    if (map.Outline != null)
                    {
                        nearest = float.MaxValue;
                        for (int c = 0; c < map.Routes.CellCount; c++)
                        {
                            var point = map.Routes.GetCell(c).Center;
                            nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(px, pz), new Vector2(point.X.ToFloat(), point.Y.ToFloat())) - 1.5f);
                        }
                        if (TouchesOutlinedFloor(px, pz, _decorRadii[variant] * 1.45f)) continue;
                    }
                    if (nearest<_decorRadii[variant]*1.45f+.5f || nearest>_style.ForestBandWidth) continue;
                    SpawnDecor(variant,px,pz,rng);
                    _decor[_decorCount-1].localScale*=1.45f;
                    var treePosition = _decor[_decorCount-1].position;
                    treePosition.y = BackgroundHeight(map, px, pz) - .08f;
                    _decor[_decorCount-1].position = treePosition;
                    if (++created>=420) return;
                }
        }

        public float WeaponGroundHeight(float x, float z)
        {
            return _shownMap == null || !_style.NaturalGround ? 0f
                : Mathf.Max(0f, BackgroundHeight(_shownMap, x, z));
        }

        private float BackgroundHeight(LayoutMap map, float x, float z)
        {
            float distance = float.MaxValue;
            // Keep the entire module footprint and a shoulder around it flat, including all paths.
            for (int m = 0; m < map.PlacedCount; m++)
            {
                var room = map.GetPlaced(m);
                float cell = LayoutMap.CellSize.ToFloat();
                float dx = Mathf.Max(0, Mathf.Max(room.OriginX * cell - x, x - (room.OriginX + room.Width) * cell));
                float dz = Mathf.Max(0, Mathf.Max(room.OriginY * cell - z, z - (room.OriginY + room.Height) * cell));
                distance = Mathf.Min(distance, Mathf.Sqrt(dx * dx + dz * dz));
            }
            float fade = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3, 13, distance));
            float broad = Mathf.PerlinNoise(x * .035f + _reliefOffset.x, z * .035f + _reliefOffset.y);
            float detail = Mathf.PerlinNoise(x * .09f + _reliefOffset.y, z * .09f + _reliefOffset.x);
            float height = -_style.GroundFillDepthOffset + fade * (broad * 1.1f + detail * .25f);
            for (int r = 0; r < map.RiverCount; r++)
            {
                var river = map.GetRiver(r);
                var point = new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z));
                if (river.ContainsWater(point, Fix64.One)) height = Mathf.Min(height, -.55f);
            }
            foreach (var pond in _ponds)
            {
                float r = PondRadius(pond, x, z);
                if (r < 1.3f) height = Mathf.Min(height, Mathf.Lerp(-.65f, height, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, 1.3f, r))));
            }
            return height;
        }

        private static float PondRadius(Vector4 pond, float x, float z)
        {
            float dx = (x - pond.x) / pond.z, dz = (z - pond.y) / pond.w;
            float angle = Mathf.Atan2(dz, dx);
            return Mathf.Sqrt(dx * dx + dz * dz) / (1 + .07f * Mathf.Sin(angle * 3 + pond.x));
        }

        private bool NearPond(float x, float z, float margin)
        {
            if (NearRiver(x, z, margin)) return true;
            foreach (var pond in _ponds)
                if (PondRadius(pond, x, z) < 1.3f + margin / Mathf.Min(pond.z, pond.w)) return true;
            return false;
        }

        private void ChoosePonds(LayoutMap map)
        {
            _ponds.Clear();
            for (int i = 0; i < map.WaterCount; i++)
            {
                var water = map.GetWater(i);
                _ponds.Add(new Vector4(water.Center.X.ToFloat(), water.Center.Y.ToFloat(),
                    water.Radius.ToFloat(), water.Radius.ToFloat()));
            }
            for (int r = 0; r < map.RiverCount; r++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var river = map.GetRiver(r);
                    var point = river.Point((river.HalfLength - Fix64.FromInt(2)) * side);
                    _ponds.Add(new Vector4(point.X.ToFloat(), point.Y.ToFloat(), 4.6f, 4.6f));
                }
            if (map.Outline == null || map.GladeCount == 0) return;
            int existingPonds = _ponds.Count;
            var rng = DecorRandom(0, 397);
            for (int attempt = 0; attempt < 96 && _ponds.Count < existingPonds + Mathf.Clamp(_style.PondCount, 0, 6); attempt++)
            {
                var glade = map.GetGlade(map.GladeCount >= 3 ? WatersideGlade(map) : attempt % map.GladeCount);
                float rx = 5.5f + (float)rng.NextDouble() * 3, rz = 4.8f + (float)rng.NextDouble() * 3;
                float radius = Mathf.Max(rx, rz) * 1.4f + 3;
                float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                float x = glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + radius + 2);
                float z = glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + radius + 2);
                if (NearPond(x, z, radius)) continue;
                bool clear = true;
                for (int m = 0; m < map.PlacedCount; m++)
                {
                    var room = map.GetPlaced(m); float cell = LayoutMap.CellSize.ToFloat();
                    float dx = Mathf.Max(0, Mathf.Max(room.OriginX * cell - x, x - (room.OriginX + room.Width) * cell));
                    float dz = Mathf.Max(0, Mathf.Max(room.OriginY * cell - z, z - (room.OriginY + room.Height) * cell));
                    if (dx * dx + dz * dz < radius * radius) { clear = false; break; }
                }
                if (clear) _ponds.Add(new Vector4(x, z, rx, rz));
            }
        }

        private void BuildPondWater()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
            foreach (var pond in _ponds)
            {
                int start = vertices.Count;
                vertices.Add(new Vector3(pond.x, -.12f, pond.y)); uv.Add(Vector2.zero);
                for (int i = 0; i <= 64; i++)
                {
                    float angle = i * Mathf.PI / 32;
                    float radius = .85f * (1 + .07f * Mathf.Sin(angle * 3 + pond.x));
                    vertices.Add(new Vector3(pond.x + Mathf.Cos(angle) * pond.z * radius, -.12f,
                        pond.y + Mathf.Sin(angle) * pond.w * radius));
                    uv.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
                    if (i == 64) continue;
                    indices.Add(start); indices.Add(start + i + 2); indices.Add(start + i + 1);
                }
            }
            _waterMesh.Clear(); _waterMesh.SetVertices(vertices); _waterMesh.SetUVs(0, uv);
            _waterMesh.SetTriangles(indices, 0); _waterMesh.RecalculateNormals(); _waterMesh.RecalculateBounds();
            _water.SetActive(_ponds.Count > 0);
        }

        private void ScatterForestDetails(LayoutMap map)
        {
            if (map.Outline == null || _style.ForestBandWidth <= 0
                || (_style.DecorPerCell <= 0 && _style.BoundaryDecorChance <= 0)) return;
            var rocks = new List<int>(); var bushes = new List<int>(); var grass = new List<int>();
            int log = -1, stump = -1, bridge = -1, seeds = -1;
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var variant = _style.DecorVariants[i];
                if (variant.Prefab == null) continue;
                // Мост, колоски и ориентиры (PlaceLandmarks) расставляются явно, а не через взвешенный пул.
                if (variant.Prefab.name == "CreatingBridge") { bridge = i; continue; }
                if (variant.Prefab.name == "CreatingSeedHeads") { seeds = i; continue; }
                if (_landmarkVariants[i] || variant.Prefab.name == "CreatingFence") continue;
                if (variant.Weight <= 0) continue;
                if (variant.Prefab.name == "MeadowFallenLog") log = i;
                else if (variant.Prefab.name == "CreatingStump") stump = i;
                else if (variant.Kind == DecorKind.Rock) rocks.Add(i);
                else if (variant.Kind == DecorKind.Bush) bushes.Add(i);
                else if (variant.Kind == DecorKind.GrassTuft) grass.Add(i);
            }
            // Поваленные стволы лежат в лесу у любой арены, а не только у береговой композиции.
            // Ставятся до подлеска: иначе длинному стволу в густой полосе не находилось места.
            if (log >= 0)
                for (int g = 0; g < map.GladeCount; g++)
                {
                    var glade = map.GetGlade(g); var rng = DecorRandom(g, 971);
                    for (int trunk = rng.Next(1, 3), attempt = 0; trunk > 0 && attempt < 24; attempt++)
                    {
                        float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                        float shoulder = _decorRadii[log] + 1.5f + (float)rng.NextDouble() * 4;
                        var point = new Vector2(glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                            glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                        if (!TryForestDetail(map, log, point, rng)) continue;
                        DressDetail(map, point, _decorRadii[log] * .6f, bushes, grass, rng);
                        trunk--;
                    }
                }
            // Подлесок привязан к уже существующим кронам, а не к ещё одной сетке.
            // Ограниченный бюджет не увеличивает число объектов с площадью фонового леса.
            int canopyCount = _decorCount, dressed = 0;
            for (int i = 0; i < canopyCount && dressed < 48; i++)
            {
                int variant = _decorVariant[i];
                if (_style.DecorVariants[variant].Kind != DecorKind.Tree) continue;
                var tree = _decor[i];
                var rng = DecorRandom(i, 947);
                if (rng.NextDouble() < .35) continue;
                DressDetail(map, new Vector2(tree.position.x, tree.position.z),
                    _decorRadii[variant] * .65f, bushes, grass, rng);
                dressed++;
            }
            // У каждой композиции есть опорный объект; мелкие детали растут у его основания.
            for (int group = 0; group < map.GladeCount * 5; group++)
            {
                var glade = map.GetGlade(group / 5); var rng = DecorRandom(group, 449);
                var character = CharacterOf(map, group / 5);
                int anchor = character == GladeCharacter.Sunny ? PickDetail(group % 3 == 0 ? bushes : grass, rng)
                    : character == GladeCharacter.Rocky ? PickDetail(rocks, rng)
                    : group % 3 == 0 && log >= 0 ? log
                    : group % 3 == 1 && stump >= 0 ? stump : PickDetail(bushes, rng);
                if (anchor < 0) continue;
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                    float shoulder = _decorRadii[anchor] + 1 + (float)rng.NextDouble() * 3;
                    var center = new Vector2(glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                        glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                    if (!TryForestDetail(map, anchor, center, rng)) continue;
                    DressDetail(map, center, _decorRadii[anchor], bushes, grass, rng);
                    if (character == GladeCharacter.Rocky)
                    {
                        for (int rock = 0; rock < 2; rock++)
                        {
                            int follower = PickDetail(rocks, rng);
                            if (follower < 0) break;
                            var point = center + DetailOffset(rng, _decorRadii[anchor] + _decorRadii[follower] + .3f);
                            if (TryForestDetail(map, follower, point, rng))
                                DressDetail(map, point, _decorRadii[follower], bushes, grass, rng);
                        }
                    }
                    break;
                }
            }
            if (seeds >= 0) PlaceSeedDrifts(map, seeds);
            // Короткие заросшие участки берега чередуются с открытой водой.
            for (int pondIndex = 0; pondIndex < _ponds.Count; pondIndex++)
            {
                var pond = _ponds[pondIndex]; var rng = DecorRandom(pondIndex, 457);
                float start = (float)rng.NextDouble() * Mathf.PI * 2;
                for (int item = 0; item < 9; item++)
                {
                    int variant = PickDetail(item % 3 == 0 ? bushes : grass, rng);
                    if (variant < 0) continue;
                    float angle = start + item * .16f;
                    float margin = _decorRadii[variant] + .35f;
                    var point = new Vector2(pond.x + Mathf.Cos(angle) * (pond.z * 1.4f + margin),
                        pond.y + Mathf.Sin(angle) * (pond.w * 1.4f + margin));
                    TryForestDetail(map, variant, point, rng);
                }
                // Колоски у воды читаются как тростник: одна куртина на пруд, в стороне от мостика.
                // Свой поток случайности: мостики и заросли берега остаются на прежних местах.
                if (seeds >= 0)
                {
                    var reeds = DecorRandom(pondIndex, 461);
                    float angle = start + 1.4f + (float)reeds.NextDouble() * .8f;
                    float margin = _decorRadii[seeds] + .2f;
                    PlaceSeedDrift(map, seeds, new Vector2(pond.x + Mathf.Cos(angle) * (pond.z * 1.4f + margin),
                        pond.y + Mathf.Sin(angle) * (pond.w * 1.4f + margin)), reeds.Next(3, 6), reeds);
                }
                // Небольшой мостик-настил у берега части прудов, не пересекающий воду.
                if (bridge >= 0 && rng.NextDouble() < .45)
                {
                    float angle = start + 3.3f;
                    float margin = _decorRadii[bridge] + .3f;
                    var point = new Vector2(pond.x + Mathf.Cos(angle) * (pond.z * 1.4f + margin),
                        pond.y + Mathf.Sin(angle) * (pond.w * 1.4f + margin));
                    if (TryForestDetail(map, bridge, point, rng))
                        _decor[_decorCount - 1].rotation = Quaternion.LookRotation(
                            new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), Vector3.up);
                }
            }
        }

        // Ориентиры встают сразу после рельефа, до опушки и леса: иначе заросли занимали
        // плечо поляны, и на аренах домик, круг рун и изгородь почти не находили места.
        private void PlaceLandmarks(LayoutMap map)
        {
            _landmarkSpots.Clear();
            if (_landmarkVariants == null || _landmarkVariants.Length != _style.DecorVariants.Length)
                _landmarkVariants = new bool[_style.DecorVariants.Length];
            int treehouse = -1, runes = -1, fence = -1;
            for (int i = 0; i < _style.DecorVariants.Length; i++)
            {
                var variant = _style.DecorVariants[i];
                string name = variant.Prefab != null ? variant.Prefab.name : "";
                if (name == "MeadowTreehouse") treehouse = i;
                else if (name == "CreatingStoneRuin") runes = i;
                else if (name == "CreatingFence" && variant.Weight > 0) fence = i;
                _landmarkVariants[i] = i == treehouse || i == runes || i == fence;
            }
            if (map.Outline == null || _style.ForestBandWidth <= 0 || map.GladeCount == 0) return;
            if (treehouse >= 0) PlaceTreehouseLandmark(map, treehouse);
            if (runes >= 0) PlaceRuneCircle(map, runes);
            if (fence >= 0)
                for (int g = 0; g < map.GladeCount; g++)
                {
                    var rng = DecorRandom(g, 953);
                    if (rng.NextDouble() < .45) PlaceFenceRun(map, fence, g, rng);
                }
        }

        // footprint — доля габаритного круга, которую ориентир занимает у земли: крона домика
        // и углы квадрата вокруг круга камней не мешают траве и кустам подходить вплотную.
        private void AddLandmark(float footprint, bool openSky)
        {
            var placed = _decor[_decorCount - 1]; int variant = _decorVariant[_decorCount - 1];
            float radius = _decorRadii[variant] * placed.localScale.x / Mathf.Max(.01f, _style.DecorVariants[variant].ScaleRange.y);
            _landmarkSpots.Add(new Vector4(placed.position.x, placed.position.z, radius * footprint, openSky ? 1 : 0));
        }

        private bool NearLandmark(float x, float z, float radius) => TouchesLandmark(x, z, radius, false);

        // Камера смотрит сверху: плоский круг рун и крышу домика кроны прячут целиком,
        // поэтому деревья держат над ними открытое небо, а не только место для ствола.
        private bool ShadesLandmark(float x, float z, float canopy) => TouchesLandmark(x, z, canopy, true);

        private bool TouchesLandmark(float x, float z, float radius, bool openSkyOnly)
        {
            foreach (var spot in _landmarkSpots)
            {
                if (openSkyOnly && spot.w < .5f) continue;
                float gap = spot.z + radius, dx = x - spot.x, dz = z - spot.y;
                // Камера смотрит с юга под наклоном: высокая ель южнее ориентира закрывает его
                // верхушкой даже в 7–8 м, поэтому с этой стороны деревья держатся вдвое дальше.
                if (openSkyOnly && dz < 0) dz *= .5f;
                if (dx * dx + dz * dz < gap * gap) return true;
            }
            return false;
        }

        // Старая изгородь — короткий прерывистый ряд вдоль опушки, повёрнутый по касательной
        // к краю поляны, а не одиночная секция, случайно стоящая посреди поля.
        private void PlaceFenceRun(LayoutMap map, int fence, int glade, System.Random rng)
        {
            var region = map.GetGlade(glade);
            float rx = region.Radii.X.ToFloat(), rz = region.Radii.Y.ToFloat();
            // Шаг по габаритному кругу секции: соседи не отбраковывают друг друга, между ними остаётся щель.
            float span = _decorRadii[fence] * 2 + .25f;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                var tangent = new Vector2(-rx * Mathf.Sin(angle), rz * Mathf.Cos(angle)).normalized;
                float shoulder = _decorRadii[fence] + 1.2f + (float)rng.NextDouble() * 1.5f;
                var start = new Vector2(region.Center.X.ToFloat() + Mathf.Cos(angle) * (rx + shoulder),
                    region.Center.Y.ToFloat() + Mathf.Sin(angle) * (rz + shoulder));
                // Локальная ось X модели — длина секции.
                float yaw = Mathf.Atan2(-tangent.y, tangent.x) * Mathf.Rad2Deg;
                int segments = rng.Next(2, 4), placed = 0;
                for (int s = 0; s < segments; s++)
                {
                    if (!TryForestDetail(map, fence, start + tangent * (s * span), rng)) break;
                    float lean = s == segments - 1 && rng.NextDouble() < .4 ? 7 + (float)rng.NextDouble() * 5 : 0;
                    _decor[_decorCount - 1].rotation = Quaternion.Euler(lean, yaw + ((float)rng.NextDouble() - .5f) * 14, 0);
                    AddLandmark(.6f, false);
                    placed++;
                }
                if (placed > 0) return;
            }
        }

        // Разовый ориентир: не более одного домика на карту, только на светлой поляне,
        // подальше от воды и маршрутов. DecorRandom — визуальный поток, RNG симуляции не трогает.
        private void PlaceTreehouseLandmark(LayoutMap map, int treehouse)
        {
            if (map.GladeCount == 0) return;
            var rng = DecorRandom(0, 733);
            // Арена — отдельный уровень: домик в каждом из них перестал бы быть ориентиром.
            if (map.GladeCount == 1 && (CharacterOf(map, 0) != GladeCharacter.Sunny || rng.NextDouble() > .5)) return;
            int start = rng.Next(map.GladeCount);
            for (int offset = 0; offset < map.GladeCount; offset++)
            {
                int index = (start + offset) % map.GladeCount;
                if (map.GladeCount >= 3 && CharacterOf(map, index) != GladeCharacter.Sunny) continue;
                var glade = map.GetGlade(index);
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                    // Домик крупнее боевой площадки: выбираем плечо поляны, не её свободный центр.
                    float shoulder = _decorRadii[treehouse] + .6f + (float)rng.NextDouble() * 2;
                    var point = new Vector2(
                        glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                        glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                    if (TryForestDetail(map, treehouse, point, rng)) { AddLandmark(.5f, true); return; }
                }
            }
        }

        // Круг рунных камней — разовый ориентир: всегда у арены босса, у каменистой арены —
        // в половине уровней. Стоит на плече поляны, как домик: камни не заходят на боевой пол.
        private void PlaceRuneCircle(LayoutMap map, int runes)
        {
            if (map.GladeCount == 0) return;
            var rng = DecorRandom(0, 983);
            bool boss = _shownEncounters != null && _shownEncounters.BossId >= 0;
            if (map.GladeCount == 1 && !boss && (CharacterOf(map, 0) != GladeCharacter.Rocky || rng.NextDouble() > .5)) return;
            int start = rng.Next(map.GladeCount);
            for (int offset = 0; offset < map.GladeCount; offset++)
            {
                int index = (start + offset) % map.GladeCount;
                if (map.GladeCount >= 3 && CharacterOf(map, index) != GladeCharacter.Rocky) continue;
                var glade = map.GetGlade(index);
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                    float shoulder = _decorRadii[runes] + .4f + (float)rng.NextDouble() * 2.5f;
                    var point = new Vector2(
                        glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                        glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                    if (!TryForestDetail(map, runes, point, rng)) continue;
                    // Круг вписан в габаритный квадрат: у земли он занимает около 0,7 его радиуса.
                    AddLandmark(.7f, true);
                    _runeRenderer = _decor[_decorCount - 1].GetComponentInChildren<MeshRenderer>();
                    _runeGlow = _runeRenderer != null ? _runeRenderer.sharedMaterial.GetColor("_EmissionColor") : Color.black;
                    return;
                }
            }
        }

        // Высокие колоски растут куртинами у светлой опушки, а не поштучно по всему лесу.
        private void PlaceSeedDrifts(LayoutMap map, int seeds)
        {
            for (int g = 0; g < map.GladeCount; g++)
            {
                var character = CharacterOf(map, g);
                var glade = map.GetGlade(g); var rng = DecorRandom(g, 967);
                int drifts = character == GladeCharacter.Sunny ? 3 : 2;
                for (int drift = 0; drift < drifts; drift++)
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float angle = (float)rng.NextDouble() * Mathf.PI * 2;
                        float shoulder = .8f + (float)rng.NextDouble() * 1.6f;
                        var center = new Vector2(glade.Center.X.ToFloat() + Mathf.Cos(angle) * (glade.Radii.X.ToFloat() + shoulder),
                            glade.Center.Y.ToFloat() + Mathf.Sin(angle) * (glade.Radii.Y.ToFloat() + shoulder));
                        if (PlaceSeedDrift(map, seeds, center, rng.Next(4, 8), rng) > 0) break;
                    }
            }
        }

        // Куртина — несколько пучков: самый высокий в середине, по краям ниже и реже.
        // Сверху тонкие стебли читаются слабо, поэтому пучки крупнее трав подлеска.
        private int PlaceSeedDrift(LayoutMap map, int seeds, Vector2 center, int count, System.Random rng)
        {
            int placed = 0;
            for (int item = 0; item < count; item++)
            {
                float spread = item == 0 ? 0 : .35f + (float)rng.NextDouble() * .85f;
                float scale = item == 0 ? 1.2f + (float)rng.NextDouble() * .25f : .85f + (float)rng.NextDouble() * .35f;
                if (TryForestDetail(map, seeds, center + DetailOffset(rng, spread), rng, scale, .45f)) placed++;
            }
            return placed;
        }

        private int PickDetail(List<int> variants, System.Random rng)
        {
            if (variants.Count == 0) return -1;
            float weight = 0;
            foreach (int index in variants) weight += _style.DecorVariants[index].Weight;
            float pick = (float)rng.NextDouble() * weight;
            foreach (int index in variants)
            {
                pick -= _style.DecorVariants[index].Weight;
                if (pick <= 0) return index;
            }
            return variants[variants.Count - 1];
        }

        private static Vector2 DetailOffset(System.Random rng, float distance)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        private void DressDetail(LayoutMap map, Vector2 center, float radius, List<int> bushes,
            List<int> grass, System.Random rng)
        {
            // Куртина с одной стороны опоры, а не бусы по окружности: кусты разного
            // размера перекрываются, трава расходится от них неровной каймой.
            float lean = (float)rng.NextDouble() * Mathf.PI * 2;
            var heart = center + new Vector2(Mathf.Cos(lean), Mathf.Sin(lean)) * (radius + .45f);
            int shrubs = rng.Next(2, 5);
            for (int item = 0; item < shrubs; item++)
            {
                int variant = PickDetail(bushes, rng);
                if (variant < 0) break;
                float scale = item == 0 ? 1.2f + (float)rng.NextDouble() * .35f : .65f + (float)rng.NextDouble() * .45f;
                var point = heart + DetailOffset(rng, .25f + item * .45f + (float)rng.NextDouble() * .35f);
                TryForestDetail(map, variant, point, rng, scale, .55f);
            }
            int tufts = rng.Next(4, 9);
            for (int item = 0; item < tufts; item++)
            {
                int variant = PickDetail(grass, rng);
                if (variant < 0) break;
                // Часть травы уходит к опоре, часть — в сторону поляны от куртины.
                var origin = item % 3 == 0 ? center : heart;
                var point = origin + DetailOffset(rng, radius * .4f + .6f + (float)rng.NextDouble() * 1.6f);
                TryForestDetail(map, variant, point, rng, .8f + (float)rng.NextDouble() * .5f, .6f);
            }
        }

        // scale уменьшает или увеличивает экземпляр и учитывается во всех проверках габарита;
        // spacing < 1 позволяет подлеску (кусты и трава) частично перекрываться в куртине.
        private bool TryForestDetail(LayoutMap map, int variant, Vector2 point, System.Random rng,
            float scale = 1f, float spacing = 1f)
        {
            float radius = _decorRadii[variant] * scale;
            var kind = _style.DecorVariants[variant].Kind;
            bool understory = kind == DecorKind.Bush || kind == DecorKind.GrassTuft;
            if (TouchesOutlinedFloor(point.x, point.y, radius + .2f)
                || NearPond(point.x, point.y, radius) || BlocksRoute(variant, point.x, point.y)
                || NearLandmark(point.x, point.y, understory ? radius * .7f : radius)) return false;
            // Учитываем уже расставленный лес и соседние группы, а не только текущую композицию.
            for (int i = 0; i < _decorCount; i++)
            {
                var other = _decor[i];
                int otherVariant = _decorVariant[i];
                // Ориентиры уже проверены по занятому ими месту, а не по габаритному кругу.
                if (_landmarkVariants != null && _landmarkVariants[otherVariant]) continue;
                float maxScale = Mathf.Max(.01f, _style.DecorVariants[otherVariant].ScaleRange.y);
                float otherRadius = _decorRadii[otherVariant] * other.localScale.x / maxScale;
                var otherKind = _style.DecorVariants[otherVariant].Kind;
                // Низкий подлесок может заходить под крону, но не в ствол.
                if (otherKind == DecorKind.Tree && understory) otherRadius *= .3f;
                float gap = radius + otherRadius;
                if (understory && (otherKind == DecorKind.Bush || otherKind == DecorKind.GrassTuft)) gap *= spacing;
                var delta = point - new Vector2(other.position.x, other.position.z);
                if (delta.sqrMagnitude < gap * gap) return false;
            }
            SpawnDecor(variant, point.x, point.y, rng);
            var placed = _decor[_decorCount - 1];
            if (scale != 1f) placed.localScale *= scale;
            placed.position = new Vector3(point.x, BackgroundHeight(map, point.x, point.y) - .035f, point.y);
            return true;
        }

        private void BuildBackgroundRelief(LayoutMap map, float minX, float maxX, float minZ, float maxZ)
        {
            var rng = DecorRandom(0, 271);
            _reliefOffset = new Vector2((float)rng.NextDouble() * 1000, (float)rng.NextDouble() * 1000);
            float width = Mathf.Max(_style.GroundFillSize, maxX - minX + 80);
            float depth = Mathf.Max(_style.GroundFillSize, maxZ - minZ + 80);
            float originX = (minX + maxX - width) * .5f, originZ = (minZ + maxZ - depth) * .5f;
            int columns = Mathf.CeilToInt(width / 2), rows = Mathf.CeilToInt(depth / 2);
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var triangles = new int[columns * rows * 6];
            for (int z = 0; z <= rows; z++)
                for (int x = 0; x <= columns; x++)
                {
                    float px = originX + x * width / columns, pz = originZ + z * depth / rows;
                    int v = z * (columns + 1) + x;
                    vertices[v] = new Vector3(px, BackgroundHeight(map, px, pz), pz);
                    if (x == columns || z == rows) continue;
                    int t = (z * columns + x) * 6;
                    triangles[t] = v; triangles[t + 1] = v + columns + 1; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + columns + 1; triangles[t + 5] = v + columns + 2;
                }
            _backgroundMesh.Clear(); _backgroundMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _backgroundMesh.vertices = vertices; _backgroundMesh.triangles = triangles;
            _backgroundMesh.RecalculateNormals(); _backgroundMesh.RecalculateBounds();
            _groundFill.transform.position = Vector3.zero; _groundFill.transform.localScale = Vector3.one;
        }
    }
}
