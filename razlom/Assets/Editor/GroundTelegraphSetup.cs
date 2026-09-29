using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Материалы общих меток и стенд для их проверки без Play.
///
/// Материалы лежат в Resources не ради удобства: шейдер, на который не
/// ссылается ни один материал сборки, в плеер не попадает, и Shader.Find там
/// вернёт null — метки молча пропали бы из съёмки capture.ps1. Создаются один
/// раз, если их нет; руками не правятся. Вид целиком в шейдерах
/// (GroundTelegraphStyle.hlsl), процедурный, без текстур, поэтому материалы
/// при смене стиля не меняются — меняется Revision, и Ensure переимпортирует
/// шейдеры меток. 29.09 владелец отверг «трещины со светом изнутри» и выбрал
/// «B — пунктир и шевроны»: светящийся пунктир по тонкой тёмной обводке,
/// мягкая заливка, шевроны по ходу удара и засечки круга, загорающиеся по
/// мере заливки.
///
/// Стенд: образцы всех фигур (полосы, сектор, круг, кольцо, коготь и круг
/// когтей Вендиго) кладутся на землю открытой сцены объектами
/// HideAndDontSave — в сцену не сохраняются и убираются перед входом в Play
/// (DontSave-объекты иначе переживают выход из Play). RenderReview снимает их
/// ортокамерой игры (размер 6,2, наклон 48°) на четырёх долях заливки и меряет
/// контраст кромки с землёй прямо по пикселям кадра. Кромка пунктирная, и
/// проба — лучший из пяти разрезов на одном шаге пунктира вдоль ребра: в
/// просвете между штрихами видна земля, край держит соседний штрих. Строгий
/// замер одним разрезом пишется рядом. Сплошной кромке окно не мешает, так
/// что стенду стиль по-прежнему не важен: им же сравнивать варианты.
/// </summary>
public static class GroundTelegraphSetup
{
    private const string Folder = "Assets/Resources/VFX/Telegraphs";
    private const string SectorPath = Folder + "/EnemySector.mat";
    private const string LanePath = Folder + "/EnemyLane.mat";
    private const string SectorShader = "Razlom/Ground Telegraph Sector";
    private const string LaneShader = "Razlom/Ground Telegraph Lane";
    /// <summary>Материал Вендиго создаёт ForestWendigoBuilder; здесь он только читается.</summary>
    private const string WendigoPath = "Assets/Resources/Characters/Forest_Wendigo/WendigoWarning.mat";
    private const string PreviewName = "Образцы меток (не сохраняются)";

    /// <summary>Подъём над землёй — как у GroundTelegraphView.</summary>
    private const float GroundLift = .055f;

    /// <summary>Порог замера: контраст кромки с землёй (цель ревью 29.09).</summary>
    private const float ContrastGoal = 3f;

    /// <summary>Шаг пунктира кромки, м — GTDashPeriod в GroundTelegraphStyle.hlsl.</summary>
    private const float DashPeriod = .36f;

    /// <summary>Разрезов на пробу вдоль ребра в пределах шага пунктира; средний — в самой точке.</summary>
    private const int DashCuts = 5;

    /// <summary>
    /// Ревизия вида меток: меняется при каждой смене стиля в
    /// GroundTelegraphStyle.hlsl. Ensure сверяет её с userData импортёра
    /// EnemySector.mat и при расхождении переимпортирует шейдеры меток
    /// (StyleShaders) и заново назначает материалам их шейдеры. Материалы не
    /// пересоздаются — GUID и ссылки на них целы. 29.09: трещины отвергнуты,
    /// выбран «B — пунктир и шевроны» (у сектора и полосы новое свойство
    /// _Flash — вспышка контакта, по умолчанию 0).
    /// </summary>
    private const string Revision = "GroundTelegraphStyleB0929";

    /// <summary>
    /// Все шейдеры, которые включают GroundTelegraphStyle.hlsl: сектор, круг и
    /// кольцо общих меток, полосы (и таран Камнекопыта), коготь и прыжок
    /// Вендиго, посадка плода.
    /// </summary>
    private static readonly string[] StyleShaders =
    {
        "Assets/Shaders/GroundTelegraphStyle.hlsl",
        "Assets/Shaders/GroundTelegraphSector.shader",
        "Assets/Shaders/GroundTelegraphLane.shader",
        "Assets/Shaders/WendigoWarning.shader",
        "Assets/Shaders/ForestBudLanding.shader",
    };

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Ensure;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) ClearPreview();
        };
    }

    [MenuItem("Разлом/Телеграфы/Материал общих меток")]
    public static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureMaterial(SectorPath, SectorShader, "EnemySector");
        EnsureMaterial(LanePath, LaneShader, "EnemyLane");
        EnsureRevision(false);
    }

    /// <summary>То же, что Ensure, но шейдеры переимпортируются и при совпавшей ревизии.</summary>
    [MenuItem("Разлом/Телеграфы/Пересобрать вид меток")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureMaterial(SectorPath, SectorShader, "EnemySector");
        EnsureMaterial(LanePath, LaneShader, "EnemyLane");
        EnsureRevision(true);
    }

    private static void EnsureRevision(bool force)
    {
        var importer = AssetImporter.GetAtPath(SectorPath);
        // Материала ещё нет (шейдер не импортирован) — повторится на следующей перезагрузке домена.
        if (importer == null) return;
        if (!force && importer.userData == Revision) return;
        foreach (string path in StyleShaders)
            if (File.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        Rebind(SectorPath, SectorShader);
        Rebind(LanePath, LaneShader);
        if (importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[Разлом] Метки на земле: шейдеры стиля переимпортированы, ревизия " + Revision + ".");
    }

    private static void Rebind(string path, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (material == null || shader == null || material.shader == shader) return;
        material.shader = shader;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
    }

    private static void EnsureMaterial(string path, string shaderName, string name)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var shader = Shader.Find(shaderName);
        // Шейдер ещё не импортирован — повторится на следующей перезагрузке домена.
        if (shader == null) { Debug.LogWarning($"[Разлом] Нет шейдера {shaderName}: материал {path} не создан."); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/VFX")) AssetDatabase.CreateFolder("Assets/Resources", "VFX");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/VFX", "Telegraphs");
        AssetDatabase.CreateAsset(new Material(shader) { name = name }, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Разлом] Материал общих меток создан: {path}");
    }

    // ------------------------------------------------------------ образцы

    private sealed class Sample
    {
        public string Name;
        public bool Lane, Wendigo;
        public Vector2 Origin, Direction;
        public float Radius, Inner, Span, Length, Width, Progress;
        public float BaseY;
        public MeshRenderer Renderer;
        public Mesh Mesh;
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    }

    private static readonly List<Sample> Samples = new List<Sample>();
    private static GameObject _root;

    private static readonly int Progress = Shader.PropertyToID("_Progress"), Opacity = Shader.PropertyToID("_Opacity"),
        Radius = Shader.PropertyToID("_Radius"), InnerRadius = Shader.PropertyToID("_InnerRadius"), Span = Shader.PropertyToID("_Span"),
        Length = Shader.PropertyToID("_Length"), Width = Shader.PropertyToID("_Width"), Consumed = Shader.PropertyToID("_Consumed"),
        IsSector = Shader.PropertyToID("_IsSector"), Ring = Shader.PropertyToID("_Ring"), Impact = Shader.PropertyToID("_Impact");

    [MenuItem("Разлом/Телеграфы/Образцы меток в сцене (вкл-выкл)")]
    private static void TogglePreview()
    {
        if (FindRoot() != null) { ClearPreview(); SceneView.RepaintAll(); return; }
        var view = SceneView.lastActiveSceneView;
        Vector3 center = view != null ? view.pivot : Vector3.zero;
        float yaw = view != null ? view.rotation.eulerAngles.y : 0f;
        SpawnPreview(center, yaw, -1f);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Кладёт образцы всех фигур вокруг center, повернув раскладку на yaw
    /// (градусы), чтобы она целиком влезала в кадр камеры с тем же поворотом.
    /// progress &lt; 0 — у каждой фигуры своя доля заливки, иначе у всех одна.
    /// </summary>
    public static void SpawnPreview(Vector3 center, float yaw, float progress)
    {
        ClearPreview();
        var sector = AssetDatabase.LoadAssetAtPath<Material>(SectorPath);
        var lane = AssetDatabase.LoadAssetAtPath<Material>(LanePath);
        var wendigo = AssetDatabase.LoadAssetAtPath<Material>(WendigoPath);
        _root = new GameObject(PreviewName) { hideFlags = HideFlags.HideAndDontSave };
        var turn = Quaternion.Euler(0f, yaw, 0f);
        Vector2 At(float x, float z) { var p = center + turn * new Vector3(x, 0f, z); return new Vector2(p.x, p.z); }
        Vector2 Dir(float x, float z) { var d = turn * new Vector3(x, 0f, z); return new Vector2(d.x, d.z).normalized; }

        // Размеры — из настоящих меток: сегмент Шипомёта 1,75 × 1,4; таран;
        // сектор Хранителя 120° на 2,4 м; круг плода 1,25; кольцо воя; коготь
        // Вендиго 140° на 2,7 м; круг когтей Вендиго 3,2 м (общий вид, как в игре).
        AddLane(lane, "полоса 4,5 × 1,4 м", At(-8f, 4.5f), Dir(1f, 0f), 4.5f, 1.4f, .6f, center.y);
        AddLane(lane, "полоса 7 × 1,5 м", At(-8f, 1.8f), Dir(1f, 0f), 7f, 1.5f, .05f, center.y);
        AddArc(sector, "сектор 120° × 2,4 м", At(-5.5f, -5f), Dir(0f, 1f), 2.4f, 0f, 2.0943951f, .45f, false, center.y);
        AddArc(sector, "круг 1,25 м", At(-.5f, -3.2f), Dir(0f, 1f), 1.25f, 0f, Mathf.PI * 2f, .85f, false, center.y);
        AddArc(sector, "кольцо 1,7–2,4 м", At(5.5f, -2.5f), Dir(0f, 1f), 2.4f, 1.7f, Mathf.PI * 2f, .3f, false, center.y);
        AddArc(wendigo, "Вендиго: коготь 140° × 2,7 м", At(2.5f, 1f), Dir(0f, 1f), 2.7f, 0f, 140f * Mathf.Deg2Rad, .7f, true, center.y);
        AddArc(sector, "Вендиго: круг когтей 3,2 м", At(7.6f, 4.6f), Dir(0f, 1f), 3.2f, 0f, Mathf.PI * 2f, .55f, false, center.y);
        if (progress >= 0f) SetPreviewProgress(progress);
    }

    /// <summary>Одна доля заливки у всех образцов; отрицательная — вернуть их собственные.</summary>
    public static void SetPreviewProgress(float progress)
    {
        foreach (var s in Samples)
        {
            if (s.Renderer == null) continue;
            s.Block.SetFloat(Progress, progress >= 0f ? Mathf.Clamp01(progress) : s.Progress);
            s.Renderer.SetPropertyBlock(s.Block);
        }
        SceneView.RepaintAll();
    }

    public static void ClearPreview()
    {
        foreach (var s in Samples) if (s.Mesh != null) Object.DestroyImmediate(s.Mesh);
        Samples.Clear();
        var root = FindRoot();
        if (root != null)
        {
            // После перезагрузки домена список образцов пуст, а меши
            // HideAndDontSave живы — снимаем их с самих объектов.
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)) Object.DestroyImmediate(filter.sharedMesh);
            Object.DestroyImmediate(root);
        }
        _root = null;
    }

    private static GameObject FindRoot()
    {
        if (_root != null) return _root;
        // После перезагрузки домена статическая ссылка теряется, а объект живёт.
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == PreviewName && !EditorUtility.IsPersistent(go)) return _root = go;
        return null;
    }

    private static void AddLane(Material material, string name, Vector2 origin, Vector2 direction, float length, float width, float progress, float baseY)
    {
        var s = NewSample(material, name, baseY);
        if (s == null) return;
        s.Lane = true; s.Origin = origin; s.Direction = direction; s.Length = length; s.Width = width; s.Progress = progress;
        // Та же раскладка UV, что у GroundTelegraphView.BuildLane: x — поперёк, y — вдоль.
        const int across = 4, along = 64, stride = across + 1;
        var right = new Vector2(direction.y, -direction.x);
        var vertices = new Vector3[stride * (along + 1)];
        var uvs = new Vector2[vertices.Length];
        for (int y = 0; y <= along; y++)
            for (int x = 0; x <= across; x++)
            {
                float u = x / (float)across, v = y / (float)along;
                Vector2 p = origin + direction * (v * length) + right * ((u - .5f) * width);
                vertices[y * stride + x] = OnGround(p, baseY);
                uvs[y * stride + x] = new Vector2(u, v);
            }
        Commit(s, vertices, uvs, across, along);
        s.Block.SetFloat(Length, length); s.Block.SetFloat(Width, width); s.Block.SetFloat(Consumed, 0f);
        s.Block.SetFloat(Progress, progress); s.Block.SetFloat(Opacity, 1f);
        s.Renderer.SetPropertyBlock(s.Block);
    }

    private static void AddArc(Material material, string name, Vector2 origin, Vector2 direction, float radius, float inner, float span,
        float progress, bool wendigo, float baseY)
    {
        var s = NewSample(material, name, baseY);
        if (s == null) return;
        s.Origin = origin; s.Direction = direction; s.Radius = radius; s.Inner = inner; s.Span = span; s.Progress = progress; s.Wendigo = wendigo;
        // Раскладка UV как у GroundTelegraphView.BuildArc и у меток Вендиго:
        // x — доля раствора, y — радиус в долях внешнего.
        const int arc = 64, radial = 8, stride = arc + 1;
        float facing = Mathf.Atan2(direction.x, direction.y);
        var vertices = new Vector3[stride * (radial + 1)];
        var uvs = new Vector2[vertices.Length];
        for (int y = 0; y <= radial; y++)
        {
            float r = Mathf.Lerp(inner, radius, y / (float)radial);
            for (int x = 0; x <= arc; x++)
            {
                float u = x / (float)arc, angle = facing + (u - .5f) * span;
                vertices[y * stride + x] = OnGround(origin + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * r, baseY);
                uvs[y * stride + x] = new Vector2(u, r / radius);
            }
        }
        Commit(s, vertices, uvs, arc, radial);
        s.Block.SetFloat(Radius, radius); s.Block.SetFloat(Progress, progress); s.Block.SetFloat(Opacity, 1f);
        if (wendigo) { s.Block.SetFloat(IsSector, 1f); s.Block.SetFloat(Ring, 1f); s.Block.SetFloat(Impact, 0f); }
        else { s.Block.SetFloat(InnerRadius, inner); s.Block.SetFloat(Span, span); }
        s.Renderer.SetPropertyBlock(s.Block);
    }

    private static Sample NewSample(Material material, string name, float baseY)
    {
        if (material == null) { Debug.LogWarning($"[Разлом] Образец «{name}»: нет материала."); return null; }
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(_root.transform, false);
        var s = new Sample { Name = name, BaseY = baseY, Mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave } };
        go.AddComponent<MeshFilter>().sharedMesh = s.Mesh;
        s.Renderer = go.AddComponent<MeshRenderer>();
        s.Renderer.sharedMaterial = material;
        s.Renderer.shadowCastingMode = ShadowCastingMode.Off; s.Renderer.receiveShadows = false;
        Samples.Add(s);
        return s;
    }

    private static void Commit(Sample s, Vector3[] vertices, Vector2[] uvs, int columns, int rows)
    {
        int stride = columns + 1, at = 0;
        var indices = new int[columns * rows * 6];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = y * stride + x, b = a + 1, c = a + stride, d = c + 1;
                indices[at++] = a; indices[at++] = c; indices[at++] = b;
                indices[at++] = b; indices[at++] = c; indices[at++] = d;
            }
        s.Mesh.vertices = vertices; s.Mesh.uv = uvs; s.Mesh.triangles = indices; s.Mesh.RecalculateBounds();
    }

    private static Vector3 OnGround(Vector2 p, float baseY) => new Vector3(p.x, GroundY(p.x, p.y, baseY) + GroundLift, p.y);

    private static float GroundY(float x, float z, float fallback)
        => Physics.Raycast(new Vector3(x, fallback + 50f, z), Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : fallback;

    // ------------------------------------------------------------ съёмка и замер

    private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

    [MenuItem("Разлом/Телеграфы/Снять образцы меток и замерить контраст")]
    private static void RenderReviewMenu() => Debug.Log(RenderReview(Path.Combine(Repo, "artifacts", "telegraph-review")));

    /// <summary>
    /// Кадры 1920×1080 образцов на долях заливки 5/35/65/95 % в outDir
    /// (marks_pNN.png) и check.txt с контрастом кромки по каждой фигуре.
    /// Кромка на разрезе — лучшее из двух: светлая кромка против земли или
    /// тёмная обводка против земли (WCAG, относительная яркость); проба —
    /// лучший из DashCuts разрезов на шаге пунктира. По фигуре — 10-й
    /// перцентиль проб, чтобы куст или камень под одной пробой не решал; рядом —
    /// тот же перцентиль одним разрезом в точке, для справки.
    /// Сцена не меняется: образцы и камера — HideAndDontSave и удаляются.
    /// </summary>
    public static string RenderReview(string outDir)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[Разлом] Съёмка меток: редактор в Play — пропуск.";
        Directory.CreateDirectory(outDir);
        var main = Camera.main;
        float yaw = main != null ? main.transform.eulerAngles.y : 0f;
        Vector3 center = ViewCenter(main);
        const int width = 1920, height = 1080;
        var go = new GameObject("Съёмка меток") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        if (main != null) camera.CopyFrom(main);
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 6.2f;
        camera.transform.rotation = Quaternion.Euler(48f, yaw, 0f);
        camera.transform.position = center - camera.transform.forward * 80f;
        camera.nearClipPlane = .3f; camera.farClipPlane = 400f;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var report = new StringBuilder();
        bool pass = true;
        var probes = new List<(Vector3 point, Vector3 inward)>();
        var values = new List<float>();
        var strict = new List<float>();
        var best = new List<float>();
        try
        {
            SpawnPreview(center, yaw, -1f);
            camera.targetTexture = target;
            foreach (float p in new[] { .05f, .35f, .65f, .95f })
            {
                SetPreviewProgress(p);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                string file = $"marks_p{Mathf.RoundToInt(p * 100f):00}.png";
                File.WriteAllBytes(Path.Combine(outDir, file), pixels.EncodeToPNG());
                var colors = pixels.GetPixels32();
                report.AppendLine($"{file} (заливка {p:0.00}):");
                foreach (var s in Samples)
                {
                    values.Clear(); strict.Clear(); best.Clear();
                    float groundSum = 0f;
                    for (int cut = 0; cut < DashCuts; cut++)
                    {
                        Probes(s, DashPeriod * (cut / (float)(DashCuts - 1) - .5f), probes);
                        while (best.Count < probes.Count) best.Add(-1f);
                        for (int k = 0; k < probes.Count; k++)
                        {
                            float c = ProbeContrast(camera, colors, width, height, probes[k].point, probes[k].inward, out float ground);
                            best[k] = Mathf.Max(best[k], c);
                            if (cut == DashCuts / 2 && c > 0f) { strict.Add(c); groundSum += ground; }
                        }
                    }
                    foreach (float c in best) if (c > 0f) values.Add(c);
                    if (values.Count == 0 || strict.Count == 0) { report.AppendLine($"  {s.Name}: вне кадра"); continue; }
                    values.Sort(); strict.Sort();
                    float p10 = values[values.Count / 10], median = values[values.Count / 2];
                    bool ok = p10 >= ContrastGoal;
                    pass &= ok;
                    report.AppendLine($"  {(ok ? "ok  " : "НИЗ ")}{s.Name}: p10 {p10:0.00}:1, медиана {median:0.00}:1, минимум {values[0]:0.00}:1; "
                        + $"одним разрезом p10 {strict[strict.Count / 10]:0.00}:1; яркость земли {groundSum / strict.Count:0.000} ({values.Count} проб)");
                }
            }
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(go);
            ClearPreview();
            SceneView.RepaintAll();
        }
        string verdict = (pass ? "PASS" : "FAIL") + $": контраст кромки с землёй ≥{ContrastGoal:0}:1 по 10-му перцентилю проб "
            + "(проба — лучший разрез на шаге пунктира)";
        File.WriteAllText(Path.Combine(outDir, "check.txt"), verdict + "\n" + report);
        return $"[Разлом] Съёмка меток → {outDir}\n{verdict}\n{report}";
    }

    /// <summary>Точка земли в центре кадра игровой камеры; без неё — центр окна сцены.</summary>
    private static Vector3 ViewCenter(Camera main)
    {
        if (main != null && Physics.Raycast(main.transform.position, main.transform.forward, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        var view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }

    /// <summary>
    /// Точки на внешней кромке фигуры и направление внутрь; along — сдвиг
    /// вдоль ребра, м (разрезы одной пробы на шаге пунктира).
    /// </summary>
    private static void Probes(Sample s, float along, List<(Vector3 point, Vector3 inward)> list)
    {
        list.Clear();
        if (s.Lane)
        {
            var right = new Vector2(s.Direction.y, -s.Direction.x);
            for (int k = 1; k <= 8; k++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 p = s.Origin + s.Direction * (k / 9f * s.Length + along) + right * (side * s.Width * .5f);
                    list.Add((OnGround(p, s.BaseY), new Vector3(-right.x * side, 0f, -right.y * side)));
                }
            return;
        }
        float facing = Mathf.Atan2(s.Direction.x, s.Direction.y);
        bool full = s.Span > 6.27f;
        const int count = 16;
        for (int k = 0; k < count; k++)
        {
            // У сектора пробы не лезут в углы: там кромка двух сторон сходится.
            float u = full ? k / (float)count : Mathf.Lerp(.12f, .88f, k / (float)(count - 1));
            float angle = facing + (u - .5f) * s.Span + along / s.Radius;
            var radial = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            list.Add((OnGround(s.Origin + radial * s.Radius, s.BaseY), new Vector3(-radial.x, 0f, -radial.y)));
        }
    }

    /// <summary>
    /// Разрез поперёк кромки: земля — медиана снаружи в 4–12 см, кромка —
    /// полоса 0–7,5 см внутрь. Возвращает max(светлое/земля, земля/тёмное);
    /// −1, если разрез уходит за кадр.
    /// </summary>
    private static float ProbeContrast(Camera camera, Color32[] colors, int width, int height, Vector3 point, Vector3 inward, out float ground)
    {
        ground = 0f;
        var outside = new List<float>(24);
        float bandMax = 0f, bandMin = 1f;
        int band = 0;
        for (int k = 0; k <= 48; k++)
        {
            float offset = Mathf.Lerp(-.12f, .12f, k / 48f);
            Vector3 screen = camera.WorldToScreenPoint(point + inward * offset);
            int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y);
            if (x < 0 || y < 0 || x >= width || y >= height) return -1f;
            float l = Luminance(colors[y * width + x]);
            if (offset <= -.04f) outside.Add(l);
            else if (offset >= 0f && offset <= .075f) { bandMax = Mathf.Max(bandMax, l); bandMin = Mathf.Min(bandMin, l); band++; }
        }
        if (outside.Count == 0 || band == 0) return -1f;
        outside.Sort();
        ground = outside[outside.Count / 2];
        return Mathf.Max((bandMax + .05f) / (ground + .05f), (ground + .05f) / (bandMin + .05f));
    }

    private static float Luminance(Color32 c) => .2126f * Linear(c.r) + .7152f * Linear(c.g) + .0722f * Linear(c.b);

    private static float Linear(byte value)
    {
        float c = value / 255f;
        return c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f);
    }
}
