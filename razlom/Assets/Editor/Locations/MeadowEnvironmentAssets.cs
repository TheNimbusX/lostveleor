using System;
using System.IO;
using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.LocationEditor
{
    [InitializeOnLoad]
    public static class MeadowEnvironmentAssets
    {
        private const string Folder = "Assets/Resources/Environment/Meadow";
        static MeadowEnvironmentAssets() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            const string request = "Library/MeadowEnvironment.request";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            File.Delete(request);
            try { if (action == "trees") ConfigureTrees(); else if (action == "camp") UseCampAppearance(); else Configure(); File.WriteAllText(LocationTestRunner.RequestPath, "refresh"); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [MenuItem("Разлом/Локации/Собрать окружение лугов из новых ассетов", priority = 23)]
        public static void Configure()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var theme = MeadowLocationAssets.EnsureCreated();
            var style = theme.Style;
            style.ForestClearings = true;
            string ground = "Assets/Resources/Environment/Camp/ground/";
            style.RoomFloorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ground + "CampTurf_v3.png");
            style.PathFloorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ground + "CampStonyEarth_v1.png");
            if (style.RoomFloorTexture == null || style.PathFloorTexture == null) throw new InvalidOperationException("Missing meadow textures");
            style.RoomColor = new Color(.63f,.68f,.58f);
            style.EntranceColor = style.ExitColor = new Color(.62f,.59f,.51f);
            style.FloorTextureTiling = .22f;
            style.DecorPerCell = .45f;
            style.RouteWidth = 2;
            style.BoundaryDecorChance = .36f;
            style.BoundaryDecorJitter = .6f;
            style.ObstacleTree = PrepareMeadowTree();
            style.ObstacleRocks = new GameObject[5];
            var variants = new List<DecorVariant>();
            for (int i = 0; i < 5; i++)
            {
                style.ObstacleRocks[i] = Prepare("Rock" + i, "Assets/Resources/Decor/UNS_Standard_Rock_0" + (i + 1) + ".prefab", 1, true);
                variants.Add(Variant(style.ObstacleRocks[i], DecorKind.Rock, .5f, true, .9f, 1.5f));
            }
            variants.Add(Variant(style.ObstacleTree, DecorKind.Tree, 2, true, .85f, 1.2f));
            variants.Add(Variant(Prepare("FlowerBush", "Assets/Resources/Environment/Camp/bush+with+flowers+3d+model.fbx", 1.1f, false), DecorKind.Bush, 2, true, .8f, 1.3f));
            // The imported grass meshes exceed a million vertices per tuft.
            // Keep mass dressing light; use the compact flowering bush for accents.
            variants.Add(Variant(Prepare("Grass", "Assets/Resources/Decor/UNS_Grass.prefab", .35f, false), DecorKind.GrassTuft, 12, false, .7f, 1.4f));
            variants.Add(Variant(Prepare("Flowers", "Assets/Resources/Environment/Camp/bush+with+flowers+3d+model.fbx", .4f, false), DecorKind.GrassTuft, 4, false, .8f, 1.2f));
            style.DecorVariants = variants.ToArray();
            style.Validate(); theme.Gameplay.SolidEnvironment = true;
            EditorUtility.SetDirty(theme); EditorUtility.SetDirty(theme.Gameplay);
            AssetDatabase.SaveAssets();
            Debug.Log("[Луга] Новое окружение подключено.");
        }

        [MenuItem("Разлом/Локации/Использовать землю и растительность лагеря", priority = 26)]
        public static void UseCampAppearance()
        {
            var theme = MeadowLocationAssets.EnsureCreated();
            const string camp = "Assets/Resources/Environment/Camp/";
            var surface = AssetDatabase.LoadAssetAtPath<Material>(camp + "Unified/Camp Study CampSurface.mat");
            if (surface == null) throw new InvalidOperationException("Не найден материал земли лагеря");
            theme.Style.CampSurfaceMaterial = surface;
            theme.Style.RoomFloorTexture = (Texture2D)surface.GetTexture("_TurfTex");
            theme.Style.PathFloorTexture = (Texture2D)surface.GetTexture("_StonyTex");
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            // Кусты и трава из Creating подключаются своими шагами и здесь остаются:
            // раньше этот шаг попутно выбрасывал из пулов падуб.
            variants.RemoveAll(v => (v.Kind == DecorKind.Bush || v.Kind == DecorKind.GrassTuft)
                && (v.Prefab == null || !v.Prefab.name.Contains("Creating")));
            var bush = Prepare("CampBush", "Assets/Resources/Decor/UNS_Bush.prefab", 1.1f, false);
            var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(bush));
            try
            {
                var leaves = AssetDatabase.LoadAssetAtPath<Material>(camp + "Unified/Camp UNS_Bush_Leaves - Camp breeze.mat");
                if (leaves == null) throw new InvalidOperationException("Не найден материал кустов лагеря");
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = leaves;
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(bush));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            variants.Add(Variant(bush, DecorKind.Bush, 2, true, .7f, 1.15f));
            for (int i = 0; i < 3; i++) variants.Add(Variant(PrepareCampGrass(i), DecorKind.GrassTuft, 4, false, .85f, 1.3f));
            theme.Style.DecorVariants = variants.ToArray(); theme.Style.Validate();
            EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
            Debug.Log("[Луга] Подключены земля, кусты и три пучка травы лагеря.");
        }

        private static GameObject PrepareCampGrass(int variant)
        {
            const string sourceFolder = "Assets/Resources/Environment/Camp/ground/Study/";
            var first = AssetDatabase.LoadAssetAtPath<Mesh>(sourceFolder + "Grass 0.asset");
            var roots = new List<Vector4>(); first.GetUVs(3, roots);
            var unique = new List<Vector3>(); var seen = new HashSet<Vector3>();
            foreach (int index in first.triangles)
            {
                var p = roots[index]; var at = new Vector3(p.x, p.y, p.z);
                if (seen.Add(at)) unique.Add(at);
            }
            if (unique.Count < 9) throw new InvalidOperationException("Не найдены пучки лагерной травы");
            var root = new GameObject("CampGrass" + variant);
            try
            {
                int total = 0;
                for (int tone = 0; tone < 3; tone++)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Mesh>(sourceFolder + "Grass " + tone + ".asset");
                    var vertices = source.vertices; var normals = source.normals;
                    var bends = new List<Vector4>(); source.GetUVs(3, bends);
                    var positions = new List<Vector3>(); var outputNormals = new List<Vector3>(); var outputBends = new List<Vector4>();
                    // Берём целые пучки по точке укоренения, не режем лезвия границей квадрата.
                    for (int tuft = 0; tuft < 3; tuft++)
                    {
                        var origin = unique[(variant * 137 + tuft * 47) % unique.Count];
                        float angle = tuft * Mathf.PI * 2 / 3;
                        var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .22f;
                        foreach (int index in source.triangles)
                        {
                            var bend = bends[index]; var at = new Vector3(bend.x, bend.y, bend.z);
                            if ((at - origin).sqrMagnitude > .000001f) continue;
                            positions.Add(vertices[index] - origin + offset); outputNormals.Add(normals[index]);
                            outputBends.Add(new Vector4(offset.x, 0, offset.z, bend.w));
                        }
                    }
                    if (positions.Count == 0) continue;
                    total += positions.Count;
                    string path = Folder + "/CampGrass" + variant + "Tone" + tone + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); } else mesh.Clear();
                    var indices = new int[positions.Count]; for (int i = 0; i < indices.Length; i++) indices[i] = i;
                    mesh.SetVertices(positions); mesh.SetNormals(outputNormals); mesh.SetUVs(3, outputBends);
                    mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                    var child = new GameObject("Grass " + tone); child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/Resources/Environment/Camp/Unified/Camp Study Grass " + tone + ".mat");
                }
                if (total == 0 || total > 12000) throw new InvalidOperationException("Неожиданный размер пучка травы: " + total);
                Debug.Log("[Луга] " + root.name + ": " + total + " вершин");
                return PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + root.name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static DecorVariant Variant(GameObject prefab, DecorKind kind, float weight, bool boundary, float min, float max)
            => new DecorVariant { Prefab = prefab, Kind = kind, Weight = weight, UseAsBoundary = boundary, ScaleRange = new Vector2(min,max) };

        [MenuItem("Разлом/Локации/Восстановить лес с новыми ассетами", priority = 25)]
        public static void RestoreMeadow()
        {
            Configure(); ConfigureTrees();
            var theme = MeadowLocationAssets.EnsureCreated();
            theme.Gameplay.NaturalGlade = true; theme.Gameplay.SolidEnvironment = true;
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            variants.RemoveAll(v => v.Prefab != null && (v.Prefab.name == "MeadowFallenLog" || v.Prefab.name.StartsWith("Creating")));
            var rock = PrepareImported("CreatingRock", "rock", 1.2f, true);
            var stump = PrepareImported("CreatingStump", "tree_stump", 1.1f, false);
            var trunk = PrepareImported("MeadowFallenLog", "tree_trunk", 2.1f, true);
            var bush = PrepareImported("CreatingBush", "bush", .85f, false);
            variants.Add(Variant(rock, DecorKind.Rock, .7f, true, .8f, 1.25f));
            variants.Add(Variant(stump, DecorKind.Rock, .3f, true, .8f, 1.2f));
            variants.Add(Variant(trunk, DecorKind.Rock, .3f, true, .85f, 1.15f));
            variants.Add(Variant(bush, DecorKind.Bush, .8f, true, .8f, 1.2f));
            theme.Style.DecorVariants = variants.ToArray();
            var rocks = new List<GameObject>(theme.Style.ObstacleRocks); rocks.Insert(0, rock);
            theme.Style.ObstacleRocks = rocks.ToArray();
            EditorUtility.SetDirty(theme); EditorUtility.SetDirty(theme.Gameplay); AssetDatabase.SaveAssets();
        }

        [MenuItem("Разлом/Локации/Добавить мост, забор, траву и домик из Creating", priority = 26)]
        public static void AddCreatingExtras()
        {
            var theme = MeadowLocationAssets.EnsureCreated();
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            variants.RemoveAll(v => v.Prefab != null && (v.Prefab.name == "CreatingFence"
                || v.Prefab.name == "CreatingBridge" || v.Prefab.name == "CreatingGrass"
                || v.Prefab.name == "MeadowTreehouse"));
            var fence = PrepareImported("CreatingFence", "wooden_fence", 1f, false);
            var bridge = PrepareImported("CreatingBridge", "wooden bridge", 2.2f, true);
            var grass = Prepare("CreatingGrass", "Assets/Art/Meadow/Creating/grass/grass.glb", .3f, false);
            var treehouse = Prepare("MeadowTreehouse",
                "Assets/Art/Meadow/Creating/treehouse/storybook+treehouse+3d+model.glb", 6.5f, false);
            variants.Add(Variant(fence, DecorKind.Rock, .5f, true, .85f, 1.15f));
            // Мост и домик расставляются явно в LayoutView.Meadow (пруды / одна светлая поляна);
            // нулевой вес не даёт им попасть во взвешенные общие и граничные пулы.
            variants.Add(Variant(bridge, DecorKind.Rock, 0f, false, .9f, 1.1f));
            variants.Add(Variant(grass, DecorKind.GrassTuft, 3f, false, .8f, 1.3f));
            variants.Add(Variant(treehouse, DecorKind.Rock, 0f, false, .95f, 1.05f));
            theme.Style.DecorVariants = variants.ToArray();
            theme.Style.Validate();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log("[Луга] Добавлены забор, мост, трава и домик на дереве из Creating.");
        }

        [MenuItem("Разлом/Локации/Добавить окружение арен из Creating", priority = 27)]
        public static void AddArenaDressing()
        {
            AssetDatabase.Refresh();
            var theme = MeadowLocationAssets.EnsureCreated();
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            variants.RemoveAll(v => v.Prefab != null && v.Prefab.name.StartsWith("ArenaCreating"));
            variants.Add(Variant(PrepareImported("ArenaCreatingRockA", "arena_smallRock_v1", .75f, true),
                DecorKind.Rock, .4f, true, .8f, 1.3f));
            variants.Add(Variant(PrepareImported("ArenaCreatingRockB", "arena_smallRock_v2", .9f, true),
                DecorKind.Rock, .4f, true, .8f, 1.3f));
            // Небольшой вес: новые пучки служат акцентами, основная трава остаётся лёгкой лагерной.
            variants.Add(Variant(PrepareImported("ArenaCreatingGrass", "arena_grass", .38f, false),
                DecorKind.GrassTuft, .7f, false, .8f, 1.2f));
            theme.Style.DecorVariants = variants.ToArray();
            theme.Style.Validate(); EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
        }

        [MenuItem("Разлом/Локации/Добавить оставшиеся модели из Creating", priority = 28)]
        public static void AddRemainingCreating()
        {
            AssetDatabase.Refresh();
            var theme = MeadowLocationAssets.EnsureCreated();
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            variants.RemoveAll(v => v.Prefab != null && (v.Prefab.name == "CreatingBush" || v.Prefab.name == "ArenaCreatingGrassB"
                || v.Prefab.name == "CreatingSeedHeads" || v.Prefab.name == "CreatingStoneRuin"));
            variants.Add(Variant(AddBreeze(PrepareImported("CreatingBush", "bush", .85f, false), .05f),
                DecorKind.Bush, .4f, true, .85f, 1.2f));
            variants.Add(Variant(AddBreeze(PrepareImported("ArenaCreatingGrassB", "arena_grass_v2", .36f, false), .12f),
                DecorKind.GrassTuft, .6f, false, .8f, 1.2f));
            // Колоски и круг рун расставляются явно в LayoutView.Meadow: нулевой вес не пускает их
            // ни в общие пулы, ни на боевой пол (PlaceModuleDecor выбирает декор по весу).
            variants.Add(Variant(PrepareSeedHeads(), DecorKind.GrassTuft, 0f, false, .85f, 1.2f));
            variants.Add(Variant(PrepareStoneRuin(), DecorKind.Rock, 0f, false, .95f, 1.05f));
            // Остальные пучки из Creating качаются тем же ветром, что и трава лагеря.
            foreach (var variant in variants)
                if (variant.Prefab != null && (variant.Prefab.name == "CreatingGrass" || variant.Prefab.name == "ArenaCreatingGrass"))
                    AddBreeze(variant.Prefab, .12f);
            SinkTreehouseBase();
            // Сказочный домик и оранжевый слом ствола кричали на фоне приглушённого вечернего леса.
            TintPrefab("MeadowTreehouse", new Color(.7f, .74f, .64f));
            TintPrefab("MeadowFallenLog", new Color(.74f, .71f, .66f));
            theme.Style.DecorVariants = variants.ToArray();
            theme.Style.Validate(); EditorUtility.SetDirty(theme); AssetDatabase.SaveAssets();
            Debug.Log("[Луга] Подключены падуб, вторая широколистная трава, колоски и круг рун из Creating.");
        }

        // Домик из Creating — диорама на квадратной плите с земляным срезом; сверху плита
        // читалась парящей игровой клеткой. Модель опускается так, что плита уходит под землю,
        // а дом и корни растут прямо из грунта. Повторный запуск не опускает её второй раз.
        private static void SinkTreehouseBase()
        {
            string path = Folder + "/MeadowTreehouse.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Renderer slab = null; float flattest = 0;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var size = renderer.bounds.size;
                    float flat = Mathf.Min(size.x, size.z) / Mathf.Max(.01f, size.y);
                    if (flat > flattest) { flattest = flat; slab = renderer; }
                }
                float top = slab != null ? slab.bounds.max.y : 0;
                if (top <= .02f) return;
                root.transform.GetChild(0).localPosition += Vector3.down * (top + .03f);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Луга] MeadowTreehouse: плита {slab.name} опущена на {top + .03f:0.00} м");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void TintPrefab(string name, Color tint)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_BaseColor")
                        || !AssetDatabase.GetAssetPath(material).StartsWith(Folder + "/")) continue;
                    material.SetColor("_BaseColor", tint);
                    EditorUtility.SetDirty(material);
                }
        }

        // Лагерный ветер (Game/Camp Breeze Lit) поворачивает вершины вокруг корня растения:
        // TEXCOORD3.xyz — корень в пространстве меша, w — угол изгиба, растущий к макушке.
        private static GameObject AddBreeze(GameObject prefab, float amplitude)
        {
            var shader = Shader.Find("Game/Camp Breeze Lit");
            if (shader == null) throw new InvalidOperationException("Не найден шейдер лагерного ветра");
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int index = 0;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var mesh = OwnedMesh(filter.sharedMesh, Folder + "/" + prefab.name + "_Breeze" + index++ + ".asset");
                    BakeBreeze(mesh, filter.transform, root.transform, amplitude);
                    filter.sharedMesh = mesh;
                    foreach (var material in filter.GetComponent<MeshRenderer>().sharedMaterials)
                    {
                        if (material == null || material.shader == shader) continue;
                        if (!AssetDatabase.GetAssetPath(material).StartsWith(Folder + "/"))
                            throw new InvalidOperationException("Чужой материал у " + prefab.name + ": " + material.name);
                        material.shader = shader;
                        material.SetFloat("_Cull", 0);
                        material.enableInstancing = true;
                        EditorUtility.SetDirty(material);
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Меш из FBX/GLB только читается: ветер запекается в собственную копию рядом с префабом.
        private static Mesh OwnedMesh(Mesh source, string path)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (sourcePath.StartsWith(Folder + "/") && sourcePath.EndsWith(".asset")) return source;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(mesh, path); }
            else
            {
                mesh.Clear();
                mesh.indexFormat = source.indexFormat;
                mesh.vertices = source.vertices; mesh.normals = source.normals; mesh.tangents = source.tangents;
                mesh.uv = source.uv; mesh.colors32 = source.colors32;
                mesh.subMeshCount = source.subMeshCount;
                for (int s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(source.GetTriangles(s), s);
                mesh.RecalculateBounds();
            }
            mesh.name = Path.GetFileNameWithoutExtension(path);
            return mesh;
        }

        // У каждой связной части (стебля, листа) свой корень — низ её габарита: основание
        // неподвижно, макушка гнётся сильнее, а стебли одного пучка качаются не в такт.
        private static void BakeBreeze(Mesh mesh, Transform filter, Transform root, float amplitude)
        {
            var vertices = mesh.vertices;
            var toRoot = root.worldToLocalMatrix * filter.localToWorldMatrix;
            var toMesh = toRoot.inverse;
            var points = new Vector3[vertices.Length];
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                points[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                // Швы развёртки дублируют вершины; по позиции стебель остаётся одной частью.
                var key = Vector3Int.RoundToInt(vertices[i] * 10000);
                if (!weld.TryGetValue(key, out int welded)) { welded = weld.Count; weld[key] = welded; }
                id[i] = welded;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int first = Find(id[triangles[t]]);
                parent[Find(id[triangles[t + 1]])] = first;
                parent[Find(id[triangles[t + 2]])] = first;
            }
            float bottom = float.MaxValue, top = float.MinValue;
            var low = new Dictionary<int, Vector3>(); var high = new Dictionary<int, Vector3>();
            for (int i = 0; i < points.Length; i++)
            {
                int part = Find(id[i]);
                bottom = Mathf.Min(bottom, points[i].y); top = Mathf.Max(top, points[i].y);
                low[part] = low.TryGetValue(part, out var min) ? Vector3.Min(min, points[i]) : points[i];
                high[part] = high.TryGetValue(part, out var max) ? Vector3.Max(max, points[i]) : points[i];
            }
            float height = Mathf.Max(.01f, top - bottom);
            var bends = new List<Vector4>(vertices.Length);
            for (int i = 0; i < points.Length; i++)
            {
                int part = Find(id[i]);
                var anchor = new Vector3((low[part].x + high[part].x) * .5f, low[part].y, (low[part].z + high[part].z) * .5f);
                float rise = Mathf.Clamp01((points[i].y - anchor.y) / height);
                var local = toMesh.MultiplyPoint3x4(anchor);
                bends.Add(new Vector4(local.x, local.y, local.z, amplitude * Mathf.Pow(rise, 1.5f)));
            }
            mesh.SetUVs(3, bends);
            EditorUtility.SetDirty(mesh);
        }

        // tall_seed_heads.glb — 1 000 180 треугольников на пучок. Штатный генератор Mesh LOD Unity
        // упрощает меш, в префаб уходит первый уровень не больше 16К треугольников.
        // Колоски — акценты у опушки и берега, не массовая трава.
        private static GameObject PrepareSeedHeads()
        {
            const string source = "Assets/Art/Meadow/Creating/tall_seed_heads/tall_seed_heads.glb";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (asset == null) throw new InvalidOperationException("Missing environment asset: " + source);
            var work = UnityEngine.Object.Instantiate(asset.GetComponentInChildren<MeshFilter>(true).sharedMesh);
            string meshPath = Folder + "/CreatingSeedHeads_Mesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            try
            {
                MeshLodUtility.GenerateMeshLods(work, (MeshLodUtility.LodGenerationFlags)0, -1);
                int level = 0;
                while (level + 1 < work.lodCount && LodTriangles(work, level) > 16000) level++;
                ExtractLod(work, level, mesh);
                mesh.name = "CreatingSeedHeads_Mesh";
                Debug.Log($"[Луга] CreatingSeedHeads: LOD {level}, {LodTriangles(work, level)} из {LodTriangles(work, 0)} треугольников");
            }
            finally { UnityEngine.Object.DestroyImmediate(work); }
            var original = asset.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            string materialPath = Folder + "/CreatingSeedHeads_Surface.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", original.HasProperty("baseColorTexture")
                ? original.GetTexture("baseColorTexture") : original.mainTexture);
            material.SetColor("_BaseColor", new Color(.9f, .88f, .82f));
            material.SetFloat("_Smoothness", .1f); material.SetFloat("_Cull", 0);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            var root = new GameObject("CreatingSeedHeads");
            try
            {
                var model = new GameObject("Колоски"); model.transform.SetParent(root.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.AddComponent<MeshRenderer>().sharedMaterial = material;
                var bounds = mesh.bounds; float factor = 1.1f / Mathf.Max(.01f, bounds.size.y);
                model.transform.localScale = Vector3.one * factor;
                model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * factor;
                return AddBreeze(PrefabUtility.SaveAsPrefabAsset(root, Folder + "/CreatingSeedHeads.prefab"), .17f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static int LodTriangles(Mesh mesh, int level)
        {
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) count += (int)mesh.GetLod(s, level).indexCount / 3;
            return count;
        }

        // Уровень Mesh LOD — диапазон индексов внутри подмеша; в отдельный меш уходят только его вершины.
        private static void ExtractLod(Mesh mesh, int level, Mesh target)
        {
            var positions = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv;
            var remap = new Dictionary<int, int>();
            var outPositions = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUv = new List<Vector2>();
            var submeshes = new List<int[]>();
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var raw = new List<int>();
                if (data[0].indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                    foreach (var index in data[0].GetIndexData<ushort>()) raw.Add(index);
                else foreach (var index in data[0].GetIndexData<int>()) raw.Add(index);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var descriptor = mesh.GetSubMesh(s); var range = mesh.GetLod(s, level);
                    var indices = new int[range.indexCount];
                    for (int k = 0; k < indices.Length; k++)
                    {
                        int vertex = raw[descriptor.indexStart + (int)range.indexStart + k] + descriptor.baseVertex;
                        if (!remap.TryGetValue(vertex, out int mapped))
                        {
                            mapped = outPositions.Count; remap[vertex] = mapped;
                            outPositions.Add(positions[vertex]);
                            if (normals.Length > 0) outNormals.Add(normals[vertex]);
                            if (uv.Length > 0) outUv.Add(uv[vertex]);
                        }
                        indices[k] = mapped;
                    }
                    submeshes.Add(indices);
                }
            }
            target.Clear();
            target.indexFormat = outPositions.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            target.SetVertices(outPositions);
            if (outNormals.Count > 0) target.SetNormals(outNormals);
            if (outUv.Count > 0) target.SetUVs(0, outUv);
            target.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) target.SetTriangles(submeshes[s], s);
            target.RecalculateBounds();
            EditorUtility.SetDirty(target);
        }

        // Круг рунных камней из Creating плоский (высота около 0,18 диаметра): ставится одним
        // ориентиром у края арены, салатовые руны текстуры получают собственное свечение.
        private static GameObject PrepareStoneRuin()
        {
            var prefab = PrepareImported("CreatingStoneRuin", "arena_stone_ruin", 3.7f, true);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/CreatingStoneRuin_Surface.mat");
            material.SetTexture("_EmissionMap", BuildRuneEmission("arena_stone_ruin", Folder + "/CreatingStoneRuin_Runes.png"));
            material.SetColor("_EmissionColor", new Color(.75f, 1f, .45f) * 1.5f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return prefab;
        }

        private static Texture2D BuildRuneEmission(string category, string output)
        {
            var files = Directory.GetFiles("Assets/Art/Meadow/Creating/" + category, "*.jpg", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            var texture = new Texture2D(2, 2);
            try
            {
                texture.LoadImage(File.ReadAllBytes(files[0]));
                var pixels = texture.GetPixels32();
                int lit = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    // Руны — яркий салатовый (около 211/227/102); мох и камень темнее и не такие зелёные.
                    float rune = p.r >= p.g ? 0 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(195, 225, p.g))
                        * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(75, 110, p.g - p.b));
                    if (rune > .05f) lit++;
                    pixels[i] = new Color32((byte)(p.r * rune), (byte)(p.g * rune), (byte)(p.b * rune), 255);
                }
                texture.SetPixels32(pixels);
                File.WriteAllBytes(output, texture.EncodeToPNG());
                Debug.Log($"[Луга] Руны: светится {lit * 100f / pixels.Length:0.00}% текстуры");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(output);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        private static GameObject PrepareImported(string name, string category, float size, bool footprint)
        {
            string folder = "Assets/Art/Meadow/Creating/" + category;
            var sources = Directory.GetFiles(folder, "*.fbx", SearchOption.AllDirectories);
            Array.Sort(sources, StringComparer.Ordinal);
            var prefab = Prepare(name, sources[0].Replace('\\', '/'), size, footprint);
            var textures = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories);
            Array.Sort(textures, StringComparer.Ordinal);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textures[0].Replace('\\', '/'));
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string materialPath = Folder + "/" + name + "_Surface.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", new Color(.85f, .86f, .8f));
                material.SetFloat("_Smoothness", .12f); material.SetFloat("_Cull", 0);
                material.enableInstancing = true;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
                EditorUtility.SetDirty(material);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject PrepareMeadowTree() => PrepareNaturalTree("MeadowBroadleaf",
            "Assets/Fantasy Forest Environment Free Sample/Meshes/Source/tree_1.fbx", 7);

        [MenuItem("Разлом/Локации/Подготовить смешанный лес лугов", priority = 24)]
        public static void ConfigureTrees()
        {
            var theme = MeadowLocationAssets.EnsureCreated();
            Undo.RecordObject(theme, "Заменить деревья лугов");
            var tree = PrepareMeadowTree();
            theme.Style.ObstacleTree = tree;
            var variants = new List<DecorVariant>(theme.Style.DecorVariants);
            variants.RemoveAll(v => v.Kind == DecorKind.Tree);
            variants.RemoveAll(v => v.Prefab != null && v.Prefab.name == "MeadowFallenLog");
            variants.Add(Variant(Prepare("MeadowFallenLog",
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Props/Logs/Prefabs/UNS_Log.prefab", 2.2f, true),
                DecorKind.Rock, .35f, false, .85f, 1.15f));
            variants.Add(Variant(tree, DecorKind.Tree, 3, true, .8f, 1.15f));
            variants.Add(Variant(PrepareNaturalTree("MeadowSpruceA", "Assets/Resources/Decor/UNS_Spruce_01.prefab", 7.5f),
                DecorKind.Tree, 1, true, .8f, 1.15f));
            variants.Add(Variant(PrepareNaturalTree("MeadowSpruceB", "Assets/Resources/Decor/UNS_Spruce_02.prefab", 6.5f),
                DecorKind.Tree, 1, true, .85f, 1.2f));
            theme.Style.DecorVariants = variants.ToArray();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log("[Луга] Подключены лиственное дерево и две ели с отдельными приглушёнными материалами.");
        }

        private static GameObject PrepareNaturalTree(string name, string source, float height)
        {
            var prefab = Prepare(name, source, height, false);
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var original = materials[i];
                        if (original == null) continue;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long id);
                        string materialPath = Folder + "/" + name + "_Muted_" + guid + "_" + id + ".mat";
                        var copy = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (copy == null) { copy = new Material(original); AssetDatabase.CreateAsset(copy, materialPath); }
                        else copy.CopyPropertiesFromMaterial(original);
                        bool leaf = original.name.ToLowerInvariant().Contains("branch")
                            || original.name.ToLowerInvariant().Contains("leaf")
                            || original.name.ToLowerInvariant().Contains("needle");
                        var tint = copy.HasProperty("_BaseColor") ? copy.GetColor("_BaseColor") : Color.white;
                        // Reduce green dominance while retaining bark and textured needles.
                        float grey = tint.grayscale;
                        tint = Color.Lerp(tint, new Color(grey, grey, grey, 1), .45f);
                        copy.SetColor("_BaseColor", tint * new Color(.88f, .79f, .82f, 1));
                        if (name == "MeadowBroadleaf")
                        {
                            // The imported materials were previously reduced to flat colours.
                            string texture = leaf ? "tree_branches.png" : "bark01_bottom.tga";
                            copy.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                                "Assets/Fantasy Forest Environment Free Sample/Textures/" + texture));
                            copy.SetColor("_BaseColor", leaf ? new Color(.72f, .76f, .66f) : new Color(.78f, .75f, .7f));
                        }
                        copy.SetFloat("_Smoothness", .12f);
                        if (leaf)
                        {
                            copy.SetColor("_BaseColor", name == "MeadowBroadleaf"
                                ? new Color(.9f, .94f, .82f) : new Color(.3f, .42f, .24f));
                            // A small textured fill keeps the backs of leaf cards from reading as black.
                            copy.SetTexture("_EmissionMap", copy.GetTexture("_BaseMap"));
                            copy.SetColor("_EmissionColor", name == "MeadowBroadleaf"
                                ? new Color(.075f, .085f, .06f) : new Color(.015f, .025f, .01f));
                            copy.EnableKeyword("_EMISSION");
                            copy.SetFloat("_AlphaClip", 1); copy.SetFloat("_Cutoff", .4f);
                            copy.SetFloat("_Cull", 0); copy.EnableKeyword("_ALPHATEST_ON");
                            copy.renderQueue = 2450;
                        }
                        EditorUtility.SetDirty(copy); materials[i] = copy;
                    }
                    renderer.sharedMaterials = materials;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject Prepare(string name, string source, float size, bool footprint)
        {
            string path = Folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (asset == null) throw new InvalidOperationException("Missing environment asset: " + source);
            var root = new GameObject(name);
            try
            {
                var model = UnityEngine.Object.Instantiate(asset, root.transform);
                model.transform.localPosition = Vector3.zero;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                var renderers = model.GetComponentsInChildren<Renderer>();
                int vertices = 0;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>()) vertices += filter.sharedMesh.vertexCount;
                Debug.Log($"[Луга] {name}: {vertices} вершин");
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float factor = size / Mathf.Max(.001f, footprint ? new Vector2(bounds.extents.x,bounds.extents.z).magnitude : bounds.size.y);
                model.transform.localScale *= factor;
                model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * factor;
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var original = materials[i];
                        if (original == null || original.shader.name.StartsWith("Universal Render Pipeline/")) continue;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long localId);
                        string matPath = Folder + "/" + name + "_" + guid + "_" + localId + ".mat";
                        var copy = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (copy == null)
                        {
                            copy = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            copy.SetTexture("_BaseMap", original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture);
                            copy.SetColor("_BaseColor", original.HasProperty("_Color") ? original.color : Color.white);
                            copy.SetFloat("_Smoothness", .15f);
                            copy.SetFloat("_Cull", 0);
                            AssetDatabase.CreateAsset(copy, matPath);
                        }
                        Texture texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
                        if (texture == null && Array.IndexOf(original.GetTexturePropertyNames(), "_BaseColor") >= 0)
                            texture = original.GetTexture("_BaseColor");
                        if (texture == null)
                        {
                            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(source)))
                                if (Path.GetFileName(file).ToLowerInvariant().Contains("basecolor") && !file.EndsWith(".meta"))
                                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
                        }
                        copy.SetTexture("_BaseMap", texture);
                        EditorUtility.SetDirty(copy);
                        materials[i] = copy;
                    }
                    renderer.sharedMaterials = materials;
                }
                Debug.Log($"[Луга] {name}: исходные габариты {bounds.size}, масштаб {factor}");
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
