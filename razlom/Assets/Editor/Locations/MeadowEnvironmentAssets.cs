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
            variants.RemoveAll(v => v.Kind == DecorKind.Bush || v.Kind == DecorKind.GrassTuft);
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
