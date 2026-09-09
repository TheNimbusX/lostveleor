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
        private ViewPool _portalPool, _cachePool;
        private Mesh _bankMesh, _ringMesh;
        private GameObject _banks;
        private Material _glowMaterial;
        private MaterialPropertyBlock _landmarkBlock;

        private void OnDisable() => _meadowLighting.Restore();

        private void ClearMeadow()
        {
            foreach (var portal in _portals) _portalPool?.Release(portal.gameObject);
            foreach (var cache in _caches) _cachePool?.Release(cache.gameObject);
            _portals.Clear(); _caches.Clear();
            if (_banks != null) _banks.SetActive(false);
            _meadowLighting.Restore();
        }
        private void DisposeMeadow()
        {
            ClearMeadow();
            foreach (var mesh in _meadowMeshes) DestroyOwned(mesh);
            _meadowMeshes.Clear();
            _bankMesh = _ringMesh = null; _banks = null;
            _portalPool = _cachePool = null;
        }

        private void InitializeMeadow()
        {
            _landmarkBlock = new MaterialPropertyBlock();
            var root = CreateRoot("Пул: ориентиры");
            _glowMaterial = ViewMaterials.CreateLit(Color.white);
            _glowMaterial.EnableKeyword("_EMISSION");
            _glowMaterial.SetFloat("_Cull", 0);
            _ownedMaterials.Add(_glowMaterial);
            _ringMesh = MakeRing(); _meadowMeshes.Add(_ringMesh);
            _portalPool = new ViewPool(root, () => MakeLandmark(true), 9, false);
            _cachePool = new ViewPool(root, () => MakeLandmark(false), 8, false);
            _banks = new GameObject("Земляной край");
            _banks.transform.SetParent(CreateRoot("Граница лугов"), false);
            _bankMesh = new Mesh { name = "Контур занятого пола" };
            _meadowMeshes.Add(_bankMesh);
            _banks.AddComponent<MeshFilter>().sharedMesh = _bankMesh;
            var material = ViewMaterials.CreateMeadowGround(new Color(.72f, .8f, .67f),
                _style.RoomFloorTexture, _style.PathFloorTexture, _style.FloorTextureTiling, .6f);
            _ownedMaterials.Add(material);
            _banks.AddComponent<MeshRenderer>().sharedMaterial = material;
            var background = ViewMaterials.CreateMeadowGround(new Color(.65f, .76f, .64f),
                _style.RoomFloorTexture, _style.PathFloorTexture, _style.FloorTextureTiling, 0);
            _ownedMaterials.Add(background);
            _groundFill.GetComponent<MeshRenderer>().sharedMaterial = background;
        }

        private void BuildMeadow(LayoutMap map, float cell)
        {
            if (map.PlacedCount == 0) return;
            if (_portalPool == null) InitializeMeadow();
            BuildBanks(cell);
            ScatterForest(map, cell);
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
            if (_shownMap != null && _portalPool != null && Application.isPlaying)
                _meadowLighting.Apply(_style);
            for (int i = 1; i < _portals.Count; i++)
                SetGlow(_portals[i], run.Phase == RunPhase.SeekingExit ? new Color(.22f, .95f, .65f) : new Color(.8f, .42f, .1f));
            for (int b = 0; b < _caches.Count; b++) _caches[b].gameObject.SetActive(!run.IsBranchClaimed(b));
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
                    int start = vertices.Count;
                    for (int s = 0; s < 5; s++)
                    {
                        float outward = s * .4f + .02f;
                        float height = Mathf.Sin(s / 4f * Mathf.PI) * .28f - .035f;
                        vertices.Add(center - along * cell * .5f + normal * outward + Vector3.up * height);
                        vertices.Add(center + along * cell * .5f + normal * outward + Vector3.up * height);
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
            _groundFill.transform.position = new Vector3((minX+maxX)*.5f, -_style.Thickness*.5f-_style.GroundFillDepthOffset, (minZ+maxZ)*.5f);
            _groundFill.transform.localScale = new Vector3(Mathf.Max(_style.GroundFillSize,maxX-minX+80),_style.Thickness,Mathf.Max(_style.GroundFillSize,maxZ-minZ+80));
            if (trees.Count==0 || _style.ForestBandWidth<=0) return;
            int created=0;
            for (float x=minX-_style.ForestBandWidth;x<maxX+_style.ForestBandWidth;x+=_style.ForestSpacing)
                for (float z=minZ-_style.ForestBandWidth;z<maxZ+_style.ForestBandWidth;z+=_style.ForestSpacing)
                {
                    var rng=DecorRandom(unchecked((int)(x*73)+(int)(z*997)),91);
                    float px=x+(float)(rng.NextDouble()-.5)*3, pz=z+(float)(rng.NextDouble()-.5)*3;
                    int variant=trees[rng.Next(trees.Count)];
                    float nearest=float.MaxValue;
                    for (int m=0;m<map.PlacedCount;m++)
                    {
                        var p=map.GetPlaced(m);
                        float dx=Mathf.Max(0,Mathf.Max(p.OriginX*cell-px,px-(p.OriginX+p.Width)*cell));
                        float dz=Mathf.Max(0,Mathf.Max(p.OriginY*cell-pz,pz-(p.OriginY+p.Height)*cell));
                        nearest=Mathf.Min(nearest,Mathf.Sqrt(dx*dx+dz*dz));
                    }
                    if (nearest<_decorRadii[variant]*1.45f+2 || nearest>_style.ForestBandWidth) continue;
                    SpawnDecor(variant,px,pz,rng);
                    _decor[_decorCount-1].localScale*=1.45f;
                    if (++created>=240) return;
                }
        }
    }
}
