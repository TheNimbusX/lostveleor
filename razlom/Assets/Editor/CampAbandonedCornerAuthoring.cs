using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Game.View;
using Object = UnityEngine.Object;

// Локальный проход у старой палатки: редактирует открытую сцену, сохраняя ручную расстановку.
public static class CampAbandonedCornerAuthoring
{
    const string Main = "Assets/Scenes/SampleScene.unity";
    const string Models = "Assets/Resources/Environment/Camp/";
    const string Folder = Models + "AbandonedCorner";
    const string RootName = "Заросший уголок у старой палатки";
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Output => Path.Combine(Repo, "ART/CAMP/abandoned-corner-2026-09-13");
    static string Request => Path.Combine(Repo, "artifacts/request-camp-abandoned-corner");

    [InitializeOnLoadMethod] static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (Application.isBatchMode || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string action = File.ReadAllText(Request).Trim(); File.Delete(Request); Directory.CreateDirectory(Output);
        try
        {
            if (EditorSceneManager.GetActiveScene().path != Main) throw new InvalidOperationException("Откройте основную сцену лагеря.");
            if (action == "inspect") Inspect();
            else if (action == "apply") Apply();
            else if (action == "refine") Refine();
            else if (action == "preview") Render(new Vector3(-13.9f, .3f, 4.8f), Quaternion.Euler(48, 35, 0), 8.3f, "corner-preview.png");
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); }
    }

    static Vector3[] _groundVertices;
    static int[] _groundTriangles;
    static Vector3 _origin;
    static readonly Vector3 Across = Quaternion.Euler(0, 35, 0) * Vector3.right;
    static readonly Vector3 Back = Quaternion.Euler(0, 35, 0) * Vector3.forward;
    static Vector3 Point(float across, float forward) => _origin + Across * across - Back * forward;
    static float Ground(Vector3 p)
    {
        for (int i = 0; i < _groundTriangles.Length; i += 3)
        {
            Vector3 a = _groundVertices[_groundTriangles[i]], b = _groundVertices[_groundTriangles[i + 1]], c = _groundVertices[_groundTriangles[i + 2]];
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < .000001f) continue;
            float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / denominator;
            if (u < -.0001f || u > 1.0001f) continue;
            float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / denominator;
            if (v >= -.0001f && u + v <= 1.0001f) return a.y * u + b.y * v + c.y * (1 - u - v);
        }
        throw new InvalidOperationException("Декор выходит за существующую поверхность: " + p);
    }
    static MeshFilter Source(Transform camp, string prefix) => camp.GetComponentsInChildren<MeshFilter>(true).First(f => f.name.StartsWith(prefix) && f.sharedMesh != null);
    static readonly Dictionary<Material, Material> AgedMaterials = new Dictionary<Material, Material>();
    static Material Aged(Material source)
    {
        if (AgedMaterials.TryGetValue(source, out var result)) return result;
        result = new Material(source) { name = "Old " + source.name, enableInstancing = true };
        if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", result.GetColor("_BaseColor") * new Color(.83f, .86f, .78f, 1));
        if (result.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", .08f);
        AssetDatabase.CreateAsset(result, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + result.name + ".mat"));
        AgedMaterials.Add(source, result); return result;
    }
    static GameObject Place(Transform parent, MeshFilter source, string name, float x, float y, float longest, Vector3 angles, bool aged, float bury = .025f, float heightScale = 1)
    {
        if (source.sharedMesh == null) throw new InvalidOperationException("Пустая модель: " + name);
        long triangles = Enumerable.Range(0, source.sharedMesh.subMeshCount).Sum(s => (long)source.sharedMesh.GetIndexCount(s) / 3);
        if (triangles > 12000) throw new InvalidOperationException("Превышен бюджет модели: " + name + " " + triangles);
        var go = new GameObject(name) { layer = 2 };
        go.transform.SetParent(parent, false);
        var mesh = go.AddComponent<MeshFilter>(); mesh.sharedMesh = source.sharedMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials.Select(m => aged ? Aged(m) : m).ToArray();
        var size = source.sharedMesh.bounds.size;
        float scale = longest / Mathf.Max(size.x, size.y, size.z);
        go.transform.localScale = new Vector3(scale, scale * heightScale, scale);
        go.transform.rotation = Quaternion.Euler(angles);
        Vector3 p = Point(x, y); go.transform.position = p;
        var bounds = renderer.bounds;
        go.transform.position += new Vector3(p.x - bounds.center.x, Ground(p) - bounds.min.y - bury, p.z - bounds.center.z);
        return go;
    }
    static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    [MenuItem("Разлом/Лагерь/Добавить заросший уголок у старой палатки")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != Main || Application.isPlaying) throw new InvalidOperationException("Нужна основная сцена вне Play.");
        var camp = Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
        if (camp.Find(RootName) != null) throw new InvalidOperationException("Уголок уже сохранён: правьте отдельные объекты.");
        CampSceneAmbiencePreview.Stop();
        EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Output);
        var backup = Path.Combine(Repo, "artifacts/camp-abandoned-corner"); Directory.CreateDirectory(backup);
        EditorSceneManager.SaveScene(scene, Path.Combine(backup, "before.unity"), true);
        var previous = camp.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localToWorldMatrix);
        var oldTent = Source(camp, "tattered+camp+tent"); _origin = oldTent.transform.position; _origin.y = 0;
        var ground = camp.GetComponentInChildren<CampGroundStudy>().transform.Find("Continuous ground").GetComponent<MeshFilter>();
        _groundVertices = ground.sharedMesh.vertices.Select(ground.transform.TransformPoint).ToArray(); _groundTriangles = ground.sharedMesh.triangles;
        var cart = Source(camp, "wooden+cart"); var barrel = Source(camp, "wooden+barrel");
        var stump = Source(camp, "tree+stump"); var logs = Source(camp, "wood+log+pile");
        var bush = Source(camp, "UNS_Bush_LOD0");
        var grass = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab").GetComponentInChildren<MeshFilter>();
        var stone = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Cobble02.prefab").GetComponentInChildren<MeshFilter>();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Models.TrimEnd('/'), "AbandonedCorner");
        AgedMaterials.Clear();
        Render(new Vector3(-13.9f, .3f, 4.8f), Quaternion.Euler(48, 35, 0), 8.3f, "before-corner.png");
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Заросший уголок у старой палатки");
        var root = new GameObject(RootName); root.transform.SetParent(camp, true); root.AddComponent<CampSceneryDecoration>();
        Place(root.transform, cart, "Брошенная телега", 4.3f, 4.1f, 2.25f, new Vector3(5, 142, 11), true, .09f);
        Place(root.transform, barrel, "Бочка на боку", .35f, 5.2f, 1.05f, new Vector3(4, 17, 87), true, .07f);
        Place(root.transform, stump, "Старый пень", 5.0f, 8.15f, 1.65f, new Vector3(0, 105, 0), true, .04f, 1.15f);
        Place(root.transform, logs, "Забытые брёвна", 1.75f, 8.35f, 2.0f, new Vector3(2, 95, -6), true, .09f, .70f);

        // Растительность собирается вокруг брошенных вещей; середина тропки остаётся читаемой.
        var clusters = new[] { new Vector2(.2f, 4.6f), new Vector2(.3f, 6.15f), new Vector2(1.25f, 6.8f), new Vector2(1.2f, 8.9f),
            new Vector2(2.55f, 8.65f), new Vector2(4.2f, 8.25f), new Vector2(5.35f, 7.35f), new Vector2(5.2f, 5.45f),
            new Vector2(5.2f, 3.7f), new Vector2(3.8f, 3.55f), new Vector2(3.3f, 4.55f), new Vector2(4.45f, 6.3f) };
        var random = new System.Random(91326);
        for (int i = 0; i < clusters.Length; i++)
        {
            var c = clusters[i];
            Place(root.transform, bush, "Низкая поросль " + (i + 1), c.x, c.y, Range(random, .8f, 1.25f), new Vector3(0, Range(random, 0, 360), 0), false, .07f, .48f);
            for (int j = 0; j < 5; j++)
            {
                float a = Range(random, 0, Mathf.PI * 2), r = Range(random, .3f, .9f);
                Place(root.transform, grass, "Трава " + (i * 5 + j + 1), c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r,
                    Range(random, .35f, .66f), new Vector3(0, Range(random, 0, 360), 0), false, .035f, Range(random, .7f, 1.15f));
            }
        }
        for (int i = 0; i < 12; i++)
        {
            float d = Range(random, 3.3f, 8.3f), x = 2.15f + .10f * d + Range(random, -.65f, .65f);
            Place(root.transform, stone, "Камень в старой тропе " + (i + 1), x, d, Range(random, .20f, .44f), new Vector3(0, Range(random, 0, 360), 0), false, .015f, .28f);
        }
        PaintTrail(ground.GetComponent<Renderer>().sharedMaterial, backup);
        foreach (var pair in previous)
            if (pair.Key == null || pair.Key.localToWorldMatrix != pair.Value) throw new InvalidOperationException("Изменён исходный объект: " + pair.Key);
        Undo.RegisterCreatedObjectUndo(root, "Заросший уголок у старой палатки"); Undo.CollapseUndoOperations(undo);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Output, "installation.txt"), "objects=" + root.GetComponentsInChildren<MeshFilter>().Length + " triangles=" + Triangles(root) + " colliders=" + root.GetComponentsInChildren<Collider>().Length + " originalTransformsPreserved=" + previous.Count + "\n" +
            string.Join("\n", root.GetComponentsInChildren<MeshRenderer>().Select(r => r.name + " " + r.bounds)));
        Render(new Vector3(-13.9f, .3f, 4.8f), Quaternion.Euler(48, 35, 0), 8.3f, "corner-preview.png");
        SceneView.RepaintAll();
    }

    static void Refine()
    {
        var camp = Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
        var root = camp.Find(RootName);
        if (root == null) throw new InvalidOperationException("Уголок не установлен.");
        var ground = camp.GetComponentInChildren<CampGroundStudy>().transform.Find("Continuous ground").GetComponent<MeshFilter>();
        _groundVertices = ground.sharedMesh.vertices.Select(ground.transform.TransformPoint).ToArray(); _groundTriangles = ground.sharedMesh.triangles;
        _origin = Source(camp, "tattered+camp+tent").transform.position; _origin.y = 0;
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        var centres = renderers.ToDictionary(r => r, r => r.bounds.center);
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Масштаб и зарастание старого уголка");
        Vector3 parentScale = camp.lossyScale;
        root.localScale = new Vector3(1 / parentScale.x, 1 / parentScale.y, 1 / parentScale.z);
        foreach (var r in renderers)
        {
            var p = centres[r]; float bury = .025f;
            if (r.name == "Брошенная телега")
            {
                // Перевёрнутая набок телега прячет груз и выглядит оставленной, а не приготовленной к поездке.
                r.transform.rotation = Quaternion.Euler(7, 118, 108); r.transform.localScale *= 1.08f; bury = .17f;
            }
            if (r.name == "Забытые брёвна") bury = .1f;
            if (r.name == "Бочка на боку") bury = .07f;
            if (r.name.StartsWith("Трава") || r.name.StartsWith("Низкая поросль"))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = r.bounds;
            r.transform.position += new Vector3(p.x - b.center.x, Ground(p) - b.min.y - bury, p.z - b.center.z);
        }
        // Низкие листья зарастают в борта и колесо, но не скрывают силуэт старой телеги.
        var foliage = Source(camp, "bush+with+flowers");
        var random = new System.Random(91329);
        foreach (var c in new[] { new Vector2(3.6f, 4.4f), new Vector2(4.3f, 4.3f), new Vector2(4.8f, 3.8f), new Vector2(2.0f, 8.1f), new Vector2(5.15f, 8.4f) })
            Place(root, foliage, "Листья у старого дерева " + root.childCount, c.x, c.y, Range(random, .85f, 1.1f), new Vector3(0, Range(random, 0, 360), 0), false, .015f, .56f);
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Output, "refinement.txt"), "worldScale=" + root.lossyScale + " triangles=" + Triangles(root.gameObject) + " colliders=" + root.GetComponentsInChildren<Collider>().Length + "\n" + string.Join("\n", renderers.Take(4).Select(r => r.name + " " + r.bounds)));
        Render(new Vector3(-13.9f, .3f, 4.8f), Quaternion.Euler(48, 35, 0), 8.3f, "corner-preview.png");
        SceneView.RepaintAll();
    }

    static void PaintTrail(Material surface, string backup)
    {
        var map = (Texture2D)surface.GetTexture("_SurfaceMap"); var bounds = surface.GetVector("_SurfaceBounds");
        var data = CampPathPainter.EnsureData(map);
        File.WriteAllBytes(Path.Combine(backup, "path-mask-before.bin"), data.Path);
        Undo.RecordObject(data, "Заросшая тропка у палатки");
        Vector3[] points = { Point(1.95f, 2.55f), Point(2.25f, 3.4f), Point(2.4f, 4.8f), Point(2.8f, 6.45f), Point(3.15f, 8.15f) };
        int touched = 0;
        for (int z = 0; z < data.Height; z++) for (int x = 0; x < data.Width; x++)
        {
            Vector3 p = new Vector3(bounds.x + x * bounds.z / (data.Width - 1), 0, bounds.y + z * bounds.w / (data.Height - 1));
            float distance = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var ab = points[i + 1] - points[i]; float t = Mathf.Clamp01(Vector3.Dot(p - points[i], ab) / ab.sqrMagnitude);
                distance = Mathf.Min(distance, Vector3.Distance(p, points[i] + ab * t));
            }
            if (distance > .85f) continue;
            float rough = Mathf.PerlinNoise(p.x * 3.1f + 8, p.z * 3.1f + 2);
            float radius = .43f + .23f * rough;
            float weight = 1 - Mathf.SmoothStep(radius * .45f, radius, distance);
            float worn = Mathf.Lerp(.53f, .95f, Mathf.PerlinNoise(p.x * 1.65f + 4, p.z * 1.65f + 9));
            int at = z * data.Width + x; byte value = (byte)Mathf.RoundToInt(weight * worn * 255);
            if (value > data.Path[at]) { data.Path[at] = value; touched++; }
        }
        EditorUtility.SetDirty(data); CampPathPainter.Apply(data); CampPathPainter.Save(data);
        File.WriteAllText(Path.Combine(Output, "path-paint.txt"), "changedPixels=" + touched + "\nThe existing path mask is preserved outside the narrow new trail.");
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
    static long Triangles(GameObject go) => go.GetComponentsInChildren<MeshFilter>(true)
        .Where(f => f.sharedMesh != null).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (long)f.sharedMesh.GetIndexCount(s) / 3));
    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }
    static void Inspect()
    {
        var camp = Object.FindAnyObjectByType<SceneWorldView>().CampRoot;
        var report = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        report.AppendLine("SCENE " + scene.path + " dirty=" + scene.isDirty + " play=" + Application.isPlaying);
        report.AppendLine("SELECTION " + (Selection.activeTransform != null ? PathOf(Selection.activeTransform) : "none"));
        foreach (Transform t in camp.transform) report.AppendLine("ROOT " + t.name + " pos=" + t.position.ToString("F3") + " rotation=" + t.eulerAngles + " bounds=" + BoundsOf(t.gameObject));
        foreach (var r in camp.GetComponentsInChildren<MeshRenderer>(true))
        {
            string path = PathOf(r.transform);
            if (path.Contains("tattered") || path.Contains("wooden+cart") || path.Contains("wooden+barrel") || path.Contains("wooden+log") ||
                path.Contains("tree+stump") || path.Contains("wood+log+pile") || path.Contains("Training") || path.Contains("Tent"))
                report.AppendLine("OBJECT " + path + " pos=" + r.transform.position.ToString("F3") + " rotation=" + r.transform.eulerAngles + " scale=" + r.transform.lossyScale + " bounds=" + r.bounds + " tris=" + Triangles(r.gameObject) + " mats=" + string.Join(";", r.sharedMaterials.Select(AssetDatabase.GetAssetPath)));
        }
        foreach (string filename in new[] { "tattered+camp+tent+3d+model.fbx", "wooden+cart+3d+model.fbx", "wooden+barrel+3d+model.fbx", "wood+log+pile+3d+model.fbx", "wooden+log+3d+model.fbx", "ground/tree+stump+3d+model.fbx" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Models + filename);
            if (asset == null) { report.AppendLine("MISSING " + filename); continue; }
            report.AppendLine("ASSET " + filename + " bounds=" + BoundsOf(asset) + " tris=" + Triangles(asset));
            foreach (var f in asset.GetComponentsInChildren<MeshFilter>(true)) report.AppendLine("  MESH " + f.name + " bounds=" + f.sharedMesh.bounds + " transform=" + f.transform.localToWorldMatrix + " materials=" + string.Join(";", f.GetComponent<Renderer>().sharedMaterials.Select(AssetDatabase.GetAssetPath)));
        }
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            report.AppendLine("SCENEVIEW pivot=" + view.pivot + " rotation=" + view.rotation.eulerAngles + " size=" + view.size + " camera=" + view.camera.transform.position + " ortho=" + view.camera.orthographicSize);
            Render(view.pivot, view.rotation, view.camera.orthographicSize, "before-scene.png");
        }
        File.WriteAllText(Path.Combine(Output, "inspection.txt"), report.ToString());
    }

    static void Render(Vector3 centre, Quaternion rotation, float size, string filename)
    {
        var go = new GameObject("Corner review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        if (Camera.main != null) camera.CopyFrom(Camera.main);
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = size; camera.aspect = 16f / 9;
        camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * 65, rotation);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var rt = RenderTexture.GetTemporary(1920, 1080, 24); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, filename), texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(go); }
    }
}
