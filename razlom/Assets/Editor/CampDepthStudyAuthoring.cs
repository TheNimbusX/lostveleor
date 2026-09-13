using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Game.View;
using Object = UnityEngine.Object;

// Художественный проход сохраняется в отдельном этюде, материалы основной сцены не редактируются.
public static class CampDepthStudyAuthoring
{
    const string Study = "Assets/Scenes/Studies/CampAtmospheric.unity";
    const string Folder = "Assets/Scenes/Studies/SmithDepth";
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Output => Path.Combine(Repo, "ART/CAMP/visual-direction-2026-09-13");
    static Vector3 _origin, _across, _back;
    static readonly Dictionary<Material, Material> PrivateMaterials = new Dictionary<Material, Material>();

    [MenuItem("Разлом/Лагерь/Визуал — объём и древняя кладка кузницы")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Нужна сохранённая сцена в Edit Mode.");
        string previous = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(Study);
        try
        {
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            if (world.CampRoot.transform.Find("Кузница — древнее основание") != null)
                throw new InvalidOperationException("Участок уже построен; ручные правки не перезаписываются.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            CampSceneAmbiencePreview.Stop();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Path.Combine(Output, "before-depth.unity"), true);
            var smith = world.CampRoot.transform.Find("Smith Shelter");
            var forge = smith.Find("medieval+blacksmith+forge+3d+model");
            var anchor = world.CampRoot.transform.Find("Anchor - Smith");
            _origin = forge.position; _back = (_origin - anchor.position).normalized; _back.y = 0; _back.Normalize();
            _across = Vector3.Cross(Vector3.up, _back);
            var decoration = new GameObject("Кузница — древнее основание");
            decoration.transform.SetParent(world.CampRoot.transform, true);
            decoration.AddComponent<CampSceneryDecoration>();
            PrivateMaterials.Clear();

            var stone = Solid("Ancient slate", new Color(.37f, .405f, .39f), .12f);
            var paleStone = Solid("Worn stone edges", new Color(.46f, .485f, .43f), .1f);
            var darkStone = Solid("Buried stone", new Color(.28f, .32f, .30f), .08f);
            var rootMaterial = Solid("Old roots", new Color(.23f, .16f, .105f), .08f);
            var inlay = Solid("Weathered copper", new Color(.18f, .35f, .30f), .22f);
            inlay.SetFloat("_Metallic", .28f); EditorUtility.SetDirty(inlay);

            // Узкий цоколь находится под существующей постройкой, вне дорожки и точки взаимодействия.
            var pedestal = NewMesh("Основание кузницы", decoration.transform,
                BeveledBlock(new Vector3(6.55f, .30f, 4.12f), .12f), stone);
            Place(pedestal, 0, .065f, .15f);
            forge.position += Vector3.up * .24f;
            int index = 0;
            for (int i = 0; i < 6; i++)
            {
                var block = NewMesh("Передняя кладка " + i, decoration.transform,
                    BeveledBlock(new Vector3(1.06f, .36f + (i % 2) * .025f, .52f), .07f), i % 3 == 0 ? paleStone : stone);
                Place(block, -2.7f + i * 1.08f, .115f, -1.77f);
                block.transform.Rotate(0, i % 2 == 0 ? 1.3f : -1.2f, 0);
            }
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 4; i++)
                {
                    var block = NewMesh("Боковая кладка " + index++, decoration.transform,
                        BeveledBlock(new Vector3(.48f, .40f, .94f), .065f), i % 2 == 0 ? stone : darkStone);
                    Place(block, side * 3.08f, .12f, -1.3f + i * .94f);
                }

            // Старая кладка продолжается отдельными обломками, не превращаясь в ровную декоративную ограду.
            int ruin = 0;
            foreach (var item in new[]
            {
                new Vector4(3.7f, 1.6f, 1.2f, .58f), new Vector4(3.9f, 2.7f, 1.05f, .45f),
                new Vector4(-3.65f, 1.5f, 1.45f, .65f), new Vector4(-4.4f, 2.2f, .9f, .40f),
                new Vector4(4.35f, 4f, 1.1f, .45f), new Vector4(-4.7f, 4.2f, 1.5f, .60f)
            })
            {
                var go = NewMesh("Обломок древней кладки " + ruin, decoration.transform,
                    BeveledBlock(new Vector3(item.z, item.w, item.z * .68f), .10f), ruin % 2 == 0 ? stone : paleStone);
                Place(go, item.x, GroundRise(Point(item.x, 0, item.y)) + item.w * .24f, item.y);
                go.transform.Rotate(ruin % 2 == 0 ? 6 : -4, ruin * 37 + 13, 4);
                ruin++;
            }

            var pillar = NewMesh("Расколотый межевой камень", decoration.transform,
                BeveledBlock(new Vector3(.78f, 1.48f, .64f), .095f), stone);
            Place(pillar, 3.75f, GroundRise(Point(3.75f, 0, 1.7f)) + .59f, 1.7f);
            pillar.transform.Rotate(0, -8f, -5f);
            Rune(pillar.transform, inlay);

            // Корни лежат у существующих деревьев позади кузницы; середина тропы остаётся открытой.
            Root(decoration.transform, rootMaterial, "Корень лесной кромки", new[]
            {
                Point(-1.5f, 0, 6.9f), Point(-1.1f, 0, 5.7f), Point(-.2f, 0, 4.8f), Point(.2f, 0, 3.8f), Point(1.05f, 0, 3.0f)
            }, .24f);
            Root(decoration.transform, rootMaterial, "Корень у обломков", new[]
            {
                Point(3.8f, 0, 6.5f), Point(3.3f, 0, 5.4f), Point(3.7f, 0, 4.2f), Point(3.4f, 0, 3.3f), Point(4.35f, 0, 2.6f)
            }, .20f);
            Root(decoration.transform, rootMaterial, "Тонкий корень", new[]
            {
                Point(-.2f, 0, 4.8f), Point(-1.1f, 0, 4.3f), Point(-1.8f, 0, 3.7f), Point(-2.2f, 0, 2.6f)
            }, .115f);

            SculptGround(world.CampRoot);
            GroupPlants(world.CampRoot);
            TuneMaterials(world.CampRoot);
            AddPlants(decoration.transform, stone);
            AssetDatabase.SaveAssets(); CampSceneAmbiencePreview.Stop();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            ReportGeometry(decoration);
        }
        finally { EditorSceneManager.OpenScene(previous); }
    }

    static Vector3 Point(float u, float y, float v) => _origin + _across * u + _back * v + Vector3.up * y;
    static Vector2 Plane(Vector3 p) { p -= _origin; return new Vector2(Vector3.Dot(p, _across), Vector3.Dot(p, _back)); }
    static void Place(GameObject go, float u, float y, float v)
    {
        go.transform.position = Point(u, y, v); go.transform.rotation = Quaternion.LookRotation(_back);
    }
    static float GroundRise(Vector3 world)
    {
        Vector2 p = Plane(world);
        float behind = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.6f, 3.6f, p.y));
        float end = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(6.8f, 9f, p.y));
        float sides = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4.5f, 7f, Mathf.Abs(p.x)));
        return behind * end * sides * (.35f + .24f * Mathf.PerlinNoise(world.x * .31f, world.z * .31f));
    }
    static Material Solid(string name, Color color, float smoothness)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
        mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", smoothness); mat.SetFloat("_Metallic", 0);
        AssetDatabase.CreateAsset(mat, Folder + "/" + name + ".mat"); return mat;
    }
    static GameObject NewMesh(string name, Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        mesh.name = name; AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".asset"));
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
    static Mesh BeveledBlock(Vector3 size, float bevel)
    {
        float x = size.x * .5f, y = size.y * .5f, z = size.z * .5f;
        bevel = Mathf.Min(bevel, Mathf.Min(x, Mathf.Min(y, z)) * .7f);
        var polygon = new[] { new Vector2(-x + bevel, -z), new Vector2(x - bevel, -z), new Vector2(x, -z + bevel), new Vector2(x, z - bevel), new Vector2(x - bevel, z), new Vector2(-x + bevel, z), new Vector2(-x, z - bevel), new Vector2(-x, -z + bevel) };
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            foreach (var p in new[] { a, b, c }) uv.Add(new Vector2(p.x, p.z));
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        }
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            Vector3 a0 = new Vector3(a.x, -y + bevel, a.y), b0 = new Vector3(b.x, -y + bevel, b.y);
            Vector3 a1 = new Vector3(a.x, y - bevel, a.y), b1 = new Vector3(b.x, y - bevel, b.y);
            Vector3 a2 = new Vector3(a.x * (1 - bevel / x), y, a.y * (1 - bevel / z));
            Vector3 b2 = new Vector3(b.x * (1 - bevel / x), y, b.y * (1 - bevel / z));
            Tri(a0, a1, b1); Tri(a0, b1, b0);
            Tri(a1, a2, b2); Tri(a1, b2, b1);
            Tri(new Vector3(0, y, 0), b2, a2);
            Tri(new Vector3(0, -y + bevel, 0), a0, b0);
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static void Rune(Transform pillar, Material material)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        void Stroke(Vector2 a, Vector2 b)
        {
            Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * .018f;
            int n = vertices.Count;
            foreach (var p in new[] { a - side, a + side, b + side, b - side }) vertices.Add(new Vector3(p.x, p.y, -.325f));
            indices.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
        }
        Stroke(new Vector2(-.04f, .43f), new Vector2(-.24f, .13f));
        Stroke(new Vector2(-.24f, .13f), new Vector2(-.04f, -.16f));
        Stroke(new Vector2(.05f, .37f), new Vector2(.23f, .10f));
        Stroke(new Vector2(.23f, .10f), new Vector2(.05f, -.20f));
        Stroke(new Vector2(-.12f, -.31f), new Vector2(.10f, -.31f));
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var rune = NewMesh("След древней печати", pillar, mesh, material); rune.transform.localPosition = Vector3.zero;
    }
    static void Root(Transform parent, Material material, string name, Vector3[] points, float radius)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        const int sides = 7;
        for (int i = 0; i < points.Length; i++)
        {
            float r = Mathf.Lerp(radius, .025f, i / (float)(points.Length - 1));
            Vector3 tangent = (points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
            Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
            Vector3 point = points[i] + Vector3.up * (GroundRise(points[i]) + r * .34f);
            for (int j = 0; j < sides; j++)
            {
                float angle = j * Mathf.PI * 2 / sides;
                vertices.Add(point + side * Mathf.Cos(angle) * r + Vector3.up * Mathf.Sin(angle) * r * .67f);
                if (i == 0) continue;
                int n = i * sides + j, next = i * sides + (j + 1) % sides;
                indices.AddRange(new[] { n - sides, next - sides, next, n - sides, next, n });
            }
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        NewMesh(name, parent, mesh, material);
    }
    static void SculptGround(GameObject camp)
    {
        var surface = camp.GetComponentInChildren<CampGroundStudy>().transform.Find("Continuous ground").GetComponent<MeshFilter>();
        var mesh = Object.Instantiate(surface.sharedMesh); var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            var world = surface.transform.TransformPoint(vertices[i]); world.y += GroundRise(world);
            vertices[i] = surface.transform.InverseTransformPoint(world);
        }
        mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.name = "Camp surface — smith rise";
        AssetDatabase.CreateAsset(mesh, Folder + "/Smith ground.asset"); surface.sharedMesh = mesh;
        var mat = Private(surface.GetComponent<Renderer>().sharedMaterial);
        mat.shader = Shader.Find("Game/Studies/Painterly Ground");
        mat.SetFloat("_TurfWeight", .27f); mat.SetFloat("_DetailSoftness", 1.25f); mat.SetFloat("_StoneRelief", .022f);
        surface.GetComponent<Renderer>().sharedMaterial = mat; EditorUtility.SetDirty(mat);
    }
    static Material Private(Material source)
    {
        if (PrivateMaterials.TryGetValue(source, out var found)) return found;
        var mat = new Material(source) { name = "Study " + source.name };
        AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + mat.name + ".mat"));
        PrivateMaterials.Add(source, mat); return mat;
    }
    static void GroupPlants(GameObject camp)
    {
        int serial = 0;
        var ground = camp.GetComponentInChildren<CampGroundStudy>();
        foreach (var filter in ground.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.name.StartsWith("Grass") && !filter.name.StartsWith("Meadow petals") && !filter.name.StartsWith("Flower centres")) continue;
            var mesh = Object.Instantiate(filter.sharedMesh); var vertices = mesh.vertices; var bends = new List<Vector4>(); mesh.GetUVs(3, bends);
            if (bends.Count != vertices.Length) { Object.DestroyImmediate(mesh); continue; }
            var keep = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 root = new Vector3(bends[i].x, bends[i].y, bends[i].z);
                Vector3 world = filter.transform.TransformPoint(root); Vector2 p = Plane(world);
                float distance = Vector2.Distance(p, new Vector2(0, .5f));
                bool inside = Mathf.Abs(p.x) < 3.3f && p.y > -1.98f && p.y < 2.3f;
                float clump = Mathf.PerlinNoise(world.x * .57f + 13, world.z * .57f + 4);
                keep[i] = !inside && (distance > 10 || clump > Mathf.Lerp(.50f, .34f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(7, 10, distance))));
                if (distance > 11) { keep[i] = true; continue; }
                Vector3 at = filter.transform.TransformPoint(vertices[i]);
                at = world + (at - world) * 1.18f; at.y += GroundRise(world);
                vertices[i] = filter.transform.InverseTransformPoint(at);
                world.y += GroundRise(world); root = filter.transform.InverseTransformPoint(world);
                bends[i] = new Vector4(root.x, root.y, root.z, bends[i].w);
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var original = mesh.GetTriangles(sub); var triangles = new List<int>();
                for (int i = 0; i < original.Length; i += 3)
                    if (keep[original[i]] && keep[original[i + 1]] && keep[original[i + 2]]) { triangles.Add(original[i]); triangles.Add(original[i + 1]); triangles.Add(original[i + 2]); }
                mesh.SetTriangles(triangles, sub);
            }
            mesh.vertices = vertices; mesh.SetUVs(3, bends); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Folder + "/Grouped foliage " + serial++ + ".asset"); filter.sharedMesh = mesh;
            var mat = Private(filter.GetComponent<Renderer>().sharedMaterial);
            if (filter.name.StartsWith("Grass"))
            {
                int tone = filter.name.StartsWith("Grass 0") ? 0 : filter.name.StartsWith("Grass 1") ? 1 : 2;
                mat.SetColor("_BaseColor", new[] { new Color(.27f, .39f, .21f), new Color(.37f, .48f, .27f), new Color(.47f, .57f, .34f) }[tone]);
            }
            filter.GetComponent<Renderer>().sharedMaterial = mat; EditorUtility.SetDirty(mat);
        }
    }
    static void TuneMaterials(GameObject camp)
    {
        foreach (var renderer in camp.GetComponentsInChildren<Renderer>())
        {
            if (renderer.GetComponentInParent<CampGroundStudy>() != null || renderer.GetComponentInParent<CampSceneryDecoration>() != null) continue;
            bool foliage = renderer.name.Contains("tree") || renderer.name.Contains("Spruce") || renderer.name.Contains("Bush") || renderer.name.Contains("bush");
            bool clothWood = renderer.name.Contains("wooden") || renderer.name.Contains("blacksmith") || renderer.name.Contains("banner") || renderer.name.Contains("log+pile");
            if (!foliage && !clothWood) continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty("_Smoothness")) continue;
                var mat = Private(materials[i]); mat.SetFloat("_Smoothness", foliage ? .07f : .17f);
                if (foliage)
                {
                    mat.SetColor("_BaseColor", materials[i].GetColor("_BaseColor") * new Color(.91f, .98f, .94f));
                    mat.SetFloat("_BumpScale", .7f);
                    mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                materials[i] = mat; EditorUtility.SetDirty(mat);
            }
            renderer.sharedMaterials = materials;
        }
    }
    static void AddPlants(Transform root, Material stone)
    {
        var grass = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        var rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock01.prefab");
        int serial = 0;
        foreach (var centre in new[] { new Vector2(4.2f, 1), new Vector2(-4.2f, 1.6f), new Vector2(3.9f, 3.8f), new Vector2(-3.8f, 4.5f), new Vector2(1.1f, 4.3f) })
        {
            Vector3 p = Point(centre.x, 0, centre.y); p.y += GroundRise(p);
            var pebble = (GameObject)PrefabUtility.InstantiatePrefab(rock, root); pebble.name = "Лесной валун " + serial;
            Fit(pebble, new Vector3(1.15f, .68f, .86f)); pebble.transform.position = p + Vector3.down * .12f;
            pebble.transform.Rotate(0, serial * 73f, 0);
            foreach (var renderer in pebble.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = stone;
            foreach (var collider in pebble.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            for (int j = 0; j < 3; j++)
            {
                var plant = (GameObject)PrefabUtility.InstantiatePrefab(grass, root); plant.name = "Крупная лесная трава " + serial++;
                Fit(plant, new Vector3(.8f + j * .13f, .55f + j * .08f, .75f));
                Vector3 at = p + _across * (.25f + j * .43f) + _back * (.22f - j * .27f); at.y = _origin.y + GroundRise(at);
                plant.transform.position = at; plant.transform.Rotate(0, serial * 137.5f, 0);
                foreach (var collider in plant.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                foreach (var renderer in plant.GetComponentsInChildren<Renderer>())
                {
                    var mat = Private(renderer.sharedMaterial);
                    int tint = mat.shader.FindPropertyIndex("_BaseColor");
                    if (tint >= 0 && mat.shader.GetPropertyType(tint) == ShaderPropertyType.Color)
                        mat.SetColor("_BaseColor", new Color(.86f, .96f, .82f));
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .05f);
                    renderer.sharedMaterial = mat; EditorUtility.SetDirty(mat);
                }
            }
        }
    }
    static void Fit(GameObject go, Vector3 size)
    {
        go.transform.localScale = Vector3.one; var renderer = go.GetComponentInChildren<Renderer>(); var bounds = renderer.bounds;
        go.transform.localScale = new Vector3(size.x / Mathf.Max(bounds.size.x, .001f), size.y / Mathf.Max(bounds.size.y, .001f), size.z / Mathf.Max(bounds.size.z, .001f));
    }
    static void ReportGeometry(GameObject root)
    {
        long triangles = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += filter.sharedMesh.GetIndexCount(i) / 3;
        File.WriteAllText(Path.Combine(Output, "depth-geometry.txt"), "Added triangles=" + triangles + "\nDecorative colliders=" + root.GetComponentsInChildren<Collider>().Length + "\nRoot=" + root.name);
    }

    public static void Polish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Нужна сохранённая сцена в Edit Mode.");
        string previous = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(Study);
        try
        {
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            var root = world.CampRoot.transform.Find("Кузница — древнее основание");
            // У палитровых моделей исходный цвет хранится в tint, а не в белой текстуре листа.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat.name.Contains("Spruce_Tree_Branch")) mat.SetColor("_BaseColor", new Color(.33f, .435f, .145f));
                if (mat.name.Contains("Bush_Leaves")) mat.SetColor("_BaseColor", new Color(.48f, .58f, .26f));
                if (mat.HasProperty("_TurfWeight")) { mat.SetFloat("_TurfWeight", .56f); mat.SetFloat("_DetailSoftness", .65f); }
                if (mat.name == "Study Grass 0") mat.SetColor("_BaseColor", new Color(.24f,.35f,.15f));
                if (mat.name == "Study Grass 1") mat.SetColor("_BaseColor", new Color(.33f,.43f,.20f));
                if (mat.name == "Study Grass 2") mat.SetColor("_BaseColor", new Color(.43f,.51f,.27f));
                EditorUtility.SetDirty(mat);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Environment/Camp/ground/CampTrailStones_v2.png");
            foreach (string name in new[] { "Ancient slate", "Worn stone edges", "Buried stone" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
                mat.SetTexture("_BaseMap", texture);
                mat.SetColor("_BaseColor", name == "Worn stone edges" ? new Color(.91f,1.02f,1.08f) : new Color(.73f,.86f,.94f));
                EditorUtility.SetDirty(mat);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!AssetDatabase.GetAssetPath(mesh).StartsWith(Folder)) continue;
                var vertices = mesh.vertices;
                if (filter.name.Contains("Корень") || filter.name.Contains("корень"))
                {
                    continue;
                }
                if (filter.name.Contains("печати")) continue;
                var uv = new Vector2[vertices.Length]; var normals = mesh.normals;
                Vector3 size=mesh.bounds.size, min=mesh.bounds.min;
                for (int i=0;i<vertices.Length;i++)
                {
                    Vector3 n=normals[i], p=vertices[i]-min;
                    Vector2 t = Mathf.Abs(n.y)>.5f ? new Vector2(p.x/size.x,p.z/size.z) : Mathf.Abs(n.x)>.5f ? new Vector2(p.z/size.z,p.y/size.y) : new Vector2(p.x/size.x,p.y/size.y);
                    // Малый блок использует участок нарисованной каменной плоскости; большая площадка — всю кладку.
                    uv[i]=filter.name=="Основание кузницы" ? new Vector2(p.x/3.4f,p.z/3.4f) : new Vector2(.45f+t.x*.13f,.76f+t.y*.11f);
                }
                mesh.uv=uv; EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets(); CampSceneAmbiencePreview.Stop(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        finally { EditorSceneManager.OpenScene(previous); }
    }

    public static void RefineRoots()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Нужна сохранённая сцена в Edit Mode.");
        string previous = EditorSceneManager.GetActiveScene().path;
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            if (previous != Study) throw new InvalidOperationException("Исходная сцена содержит несохранённые изменения.");
            CampSceneAmbiencePreview.Stop();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        EditorSceneManager.OpenScene(Study);
        try
        {
            var root = Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform.Find("Кузница — древнее основание");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Old roots.mat");
            mat.SetColor("_BaseColor", new Color(.52f,.34f,.18f)); EditorUtility.SetDirty(mat);
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.Contains("Корень") && !filter.name.Contains("корень")) continue;
                var mesh = filter.sharedMesh;
                if (mesh.name.EndsWith(" smooth")) continue;
                var old = mesh.vertices; var points = new List<Vector3>();
                for (int i=0;i<old.Length;i+=7)
                {
                    Vector3 centre=Vector3.zero; for(int j=0;j<7;j++) centre+=old[i+j]; points.Add(centre/7);
                }
                var vertices=new List<Vector3>(); var indices=new List<int>();
                float radius=filter.name=="Тонкий корень"?.115f:filter.name=="Корень у обломков"?.20f:.24f;
                int count=(points.Count-1)*8+1;
                Vector3 Sample(float t)
                {
                    int a=Mathf.Min(Mathf.FloorToInt(t),points.Count-2); float f=t-a;
                    Vector3 p0=points[Mathf.Max(0,a-1)],p1=points[a],p2=points[a+1],p3=points[Mathf.Min(points.Count-1,a+2)];
                    return .5f*((2*p1)+(-p0+p2)*f+(2*p0-5*p1+4*p2-p3)*f*f+(-p0+3*p1-3*p2+p3)*f*f*f);
                }
                for(int i=0;i<count;i++)
                {
                    float t=i/8f; Vector3 centre=Sample(t);
                    Vector3 tangent=(Sample(Mathf.Min(points.Count-1,t+.02f))-Sample(Mathf.Max(0,t-.02f))).normalized;
                    Vector3 side=Vector3.Cross(tangent,Vector3.up).normalized;
                    float r=Mathf.Lerp(radius,.018f,i/(float)(count-1));
                    for(int j=0;j<10;j++)
                    {
                        float angle=j*Mathf.PI*2/10;
                        vertices.Add(centre+side*Mathf.Cos(angle)*r+Vector3.up*Mathf.Sin(angle)*r*.67f);
                        if(i==0) continue;
                        int n=i*10+j,next=i*10+(j+1)%10;
                        indices.AddRange(new[]{n-10,next,next-10,n-10,n,next});
                    }
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.name += " smooth"; EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets(); CampSceneAmbiencePreview.Stop(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            ReportGeometry(root.gameObject);
        }
        finally { EditorSceneManager.OpenScene(previous); }
    }

    public static void Inspect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Нужна сохранённая сцена в Edit Mode.");
        string previous = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(Study);
        try
        {
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            var smith = world.CampRoot.transform.Find("Smith Shelter");
            var ground = world.CampRoot.GetComponentInChildren<CampGroundStudy>();
            var report = new StringBuilder();
            report.AppendLine($"smith pos={smith.position} rot={smith.eulerAngles} scale={smith.lossyScale}");
            var materials = new HashSet<Material>();
            foreach (var renderer in world.CampRoot.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (renderer.transform.IsChildOf(smith) || renderer.GetComponentInParent<CampGroundStudy>() != null ||
                    (Vector3.Distance(renderer.bounds.center, smith.position) < 14f && renderer.bounds.size.sqrMagnitude > 4f))
                {
                    long triangles = 0;
                    if (filter?.sharedMesh != null) for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += filter.sharedMesh.GetIndexCount(i) / 3;
                    report.AppendLine($"renderer={PathOf(renderer.transform)}\n pos={renderer.transform.position} rot={renderer.transform.eulerAngles} scale={renderer.transform.lossyScale} bounds={renderer.bounds} tris={triangles} prefab={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer.gameObject)} mesh={(filter?.sharedMesh != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "none")}");
                    foreach (var mat in renderer.sharedMaterials) if (mat != null) { materials.Add(mat); report.AppendLine(" material=" + AssetDatabase.GetAssetPath(mat)); }
                }
            }
            foreach (var mat in materials)
            {
                report.AppendLine($"MATERIAL {AssetDatabase.GetAssetPath(mat)} shader={mat.shader.name}");
                foreach (string prop in new[] { "_BaseColor", "_Color", "_Smoothness", "_Metallic", "_BumpScale", "_OutlineWidth", "_ShadowColor" })
                    if (mat.HasProperty(prop)) report.AppendLine("  " + prop + "=" + (prop.Contains("Color") ? mat.GetColor(prop).ToString() : mat.GetFloat(prop).ToString()));
            }
            File.WriteAllText(Path.Combine(Output, "depth-inspect.txt"), report.ToString());
        }
        finally { EditorSceneManager.OpenScene(previous); }
    }
    static string PathOf(Transform t) => t.parent != null ? PathOf(t.parent) + "/" + t.name : t.name;
}
