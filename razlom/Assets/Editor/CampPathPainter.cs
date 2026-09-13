using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>Рисует по принятой поверхности, не пересоздавая объекты лагеря.</summary>
public sealed class CampPathPainter : EditorWindow
{
    internal const string DataPath = "Assets/Editor/CampPaths/CampPathPaintData.asset";
    const string FoliagePath = "Assets/Resources/Environment/Camp/ground/Study/CampPaintedPaths.asset";
    [SerializeField] float _diameter = 1.5f;
    [SerializeField] float _softness = .25f;
    [SerializeField] bool _erase;
    bool _painting;
    bool _stroke;
    int _undoGroup;
    int _control;
    Vector3 _previous;
    CampGroundStudy _study;
    CampPathPaintData _data;

    [MenuItem("Разлом/Лагерь/Рисовать дорожки")]
    public static void Open()
    {
        var window = GetWindow<CampPathPainter>("Дорожки лагеря");
        window.minSize = new Vector2(310, 300);
        window.Show();
    }

    [InitializeOnLoadMethod]
    static void InitializeUndo()
    {
        Undo.undoRedoPerformed -= RestoreAfterUndo;
        Undo.undoRedoPerformed += RestoreAfterUndo;
    }

    static void RestoreAfterUndo()
    {
        var data = AssetDatabase.LoadAssetAtPath<CampPathPaintData>(DataPath);
        if (data == null) return;
        Apply(data);
        Save(data);
    }

    void OnEnable() => SceneView.duringSceneGui += DuringSceneGui;
    void OnDisable()
    {
        FinishStroke();
        SceneView.duringSceneGui -= DuringSceneGui;
        SceneView.RepaintAll();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Дорожки прямо в сцене", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("ЛКМ + движение мыши — рисовать. Shift — временно стирать.\nCtrl+Z — отмена, Ctrl+Y — повтор. Alt и ПКМ — обычная навигация сцены.", MessageType.Info);
        _study = FindStudy();
        bool available = !EditorApplication.isPlayingOrWillChangePlaymode && _study != null && _study.isActiveAndEnabled;
        if (!available)
        {
            _painting = false;
            FinishStroke();
            EditorGUILayout.HelpBox("Открой SampleScene вне Play. Объект Ground Study - Campfire и его компонент Camp Ground Study должны быть включены.", MessageType.Warning);
            if (_study != null && !EditorApplication.isPlayingOrWillChangePlaymode && !_study.enabled && GUILayout.Button("Включить общую землю"))
            {
                Undo.RecordObject(_study, "Включить землю лагеря");
                _study.enabled = true;
            }
        }
        using (new EditorGUI.DisabledScope(!available))
        {
            _diameter = EditorGUILayout.Slider("Ширина, м", _diameter, .25f, 5f);
            _softness = EditorGUILayout.Slider("Мягкость края", _softness, .05f, .8f);
            _erase = GUILayout.Toolbar(_erase ? 1 : 0, new[] { "Дорожка", "Стереть" }) == 1;
            EditorGUILayout.Space();
            bool painting = GUILayout.Toggle(_painting, _painting ? "Кисть включена — нажми, чтобы выключить" : "Включить кисть", "Button", GUILayout.Height(36));
            if (painting != _painting)
            {
                FinishStroke();
                _painting = painting;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Показать землю в Scene"))
            {
                SceneView view = SceneView.lastActiveSceneView;
                var ground = SurfaceRenderer(_study);
                if (view != null && ground != null) view.Frame(ground.bounds, false);
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Изменения видны сразу и сохраняются после каждого мазка. Пересобирать участок земли не нужно. Escape выключает кисть.", MessageType.None);
    }

    void DuringSceneGui(SceneView view)
    {
        if (!_painting || EditorApplication.isPlayingOrWillChangePlaymode) return;
        _study = _study != null ? _study : FindStudy();
        if (_study == null || !_study.isActiveAndEnabled) return;
        Event ev = Event.current;
        _control = GUIUtility.GetControlID("CampPathBrush".GetHashCode(), FocusType.Passive);
        if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
        {
            FinishStroke(); _painting = false; ev.Use(); Repaint(); view.Repaint(); return;
        }
        if (ev.type == EventType.MouseUp && ev.button == 0 && _stroke)
        {
            FinishStroke(); ev.Use(); return;
        }
        if (ev.alt || ev.button == 1 || ev.button == 2) return;
        var renderer = SurfaceRenderer(_study);
        if (renderer == null) return;
        Material material = renderer.sharedMaterial;
        Vector4 bounds = material.GetVector("_SurfaceBounds");
        if (bounds.z <= 0 || bounds.w <= 0) return;
        var plane = new Plane(Vector3.up, new Vector3(0, renderer.bounds.min.y, 0));
        Ray ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
        if (!plane.Raycast(ray, out float distance)) return;
        Vector3 point = ray.GetPoint(distance);
        if (ev.type == EventType.Layout) HandleUtility.AddDefaultControl(_control);
        bool erase = _erase || ev.shift;
        if (ev.type == EventType.Repaint)
        {
            Handles.color = erase ? new Color(1, .35f, .25f, 1) : new Color(1, .8f, .25f, 1);
            var oldZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.DrawWireDisc(point + Vector3.up * .03f, Vector3.up, _diameter * .5f);
            Handles.DrawWireDisc(point + Vector3.up * .03f, Vector3.up, _diameter * .5f * (1 - _softness));
            Handles.zTest = oldZ;
        }
        if (ev.type == EventType.MouseMove) view.Repaint();
        bool starts = ev.type == EventType.MouseDown && ev.button == 0;
        bool drags = ev.type == EventType.MouseDrag && ev.button == 0 && _stroke && GUIUtility.hotControl == _control;
        if (!starts && !drags) return;
        if (starts)
        {
            // Клик вне земли не создаёт ни assets, ни пустой шаг Undo.
            if (point.x < bounds.x || point.z < bounds.y || point.x > bounds.x + bounds.z || point.z > bounds.y + bounds.w) return;
            _data = EnsureData(material.GetTexture("_SurfaceMap") as Texture2D);
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erase ? "Стереть дорожку лагеря" : "Нарисовать дорожку лагеря");
            Undo.RegisterCompleteObjectUndo(_data, erase ? "Стереть дорожку лагеря" : "Нарисовать дорожку лагеря");
            _stroke = true; _previous = point; GUIUtility.hotControl = _control;
        }
        PaintSegment(_data, bounds, _previous, point, _diameter, _softness, erase);
        _previous = point;
        Apply(_data);
        ev.Use(); view.Repaint();
    }

    void FinishStroke()
    {
        if (!_stroke) return;
        _stroke = false;
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        Undo.CollapseUndoOperations(_undoGroup);
        Save(_data);
    }

    internal static CampGroundStudy FindStudy() => UnityEngine.Object.FindAnyObjectByType<CampGroundStudy>();

    internal static MeshRenderer SurfaceRenderer(CampGroundStudy study)
    {
        if (study == null) return null;
        foreach (var renderer in study.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_IsSurface") && renderer.sharedMaterial.GetFloat("_IsSurface") > .5f)
                return renderer;
        return null;
    }

    internal static CampPathPaintData EnsureData(Texture2D surface)
    {
        if (surface == null || !surface.isReadable) throw new InvalidOperationException("Карта дорожек отсутствует или недоступна для чтения.");
        var data = AssetDatabase.LoadAssetAtPath<CampPathPaintData>(DataPath);
        if (data != null)
        {
            if (data.Surface != surface || data.Width != surface.width || data.Height != surface.height)
                throw new InvalidOperationException("Земля заменена после начала рисования. Открой исходную сцену лагеря.");
            return data;
        }
        // Исходная разметка сохраняется один раз и не меняется при последующих мазках.
        Directory.CreateDirectory("Assets/Editor/CampPaths");
        AssetDatabase.Refresh();
        data = CreateInstance<CampPathPaintData>();
        data.Surface = surface; data.Width = surface.width; data.Height = surface.height;
        Color32[] pixels = surface.GetPixels32();
        data.Path = new byte[pixels.Length]; data.OriginalPath = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) data.Path[i] = data.OriginalPath[i] = pixels[i].r;
        data.ClearedFoliage = new Texture2D(surface.width, surface.height, TextureFormat.RGBA32, false, true)
        { name = "Camp painted paths", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        AssetDatabase.CreateAsset(data.ClearedFoliage, FoliagePath);
        AssetDatabase.CreateAsset(data, DataPath);
        Apply(data); Save(data);
        return data;
    }

    internal static void PaintSegment(CampPathPaintData data, Vector4 bounds, Vector3 from, Vector3 to, float diameter, float softness, bool erase)
    {
        float radius = diameter * .5f;
        int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.x, to.x) - radius - bounds.x) / bounds.z * (data.Width - 1)), 0, data.Width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.x, to.x) + radius - bounds.x) / bounds.z * (data.Width - 1)), 0, data.Width - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.z, to.z) - radius - bounds.y) / bounds.w * (data.Height - 1)), 0, data.Height - 1);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.z, to.z) + radius - bounds.y) / bounds.w * (data.Height - 1)), 0, data.Height - 1);
        Vector2 a = new Vector2(from.x, from.z), ab = new Vector2(to.x - from.x, to.z - from.z);
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(bounds.x + x * bounds.z / (data.Width - 1), bounds.y + z * bounds.w / (data.Height - 1));
                float t = ab.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, a + ab * t);
                if (d >= radius) continue;
                // Мазок — непрерывная капсула между событиями мыши, поэтому быстрый жест не оставляет дыр.
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius * (1 - softness), radius, d));
                int i = z * data.Width + x;
                byte value = (byte)Mathf.RoundToInt((erase ? 1 - weight : weight) * 255);
                data.Path[i] = erase ? (byte)Math.Min(data.Path[i], value) : (byte)Math.Max(data.Path[i], value);
            }
        EditorUtility.SetDirty(data);
    }

    internal static void Apply(CampPathPaintData data)
    {
        if (data == null || data.Surface == null || data.ClearedFoliage == null) return;
        var pixels = data.Surface.GetPixels32();
        var clear = new Color32[pixels.Length];
        if (data.Path == null || data.Path.Length != pixels.Length) return;
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i].r = data.Path[i];
            // Не трогаем вручную поставленную траву на старых дорогах: скрываем только вновь нарисованное.
            byte added = data.Path[i] > data.OriginalPath[i] + 5 ? data.Path[i] : (byte)0;
            clear[i] = new Color32(added, 0, 0, 255);
        }
        data.Surface.SetPixels32(pixels); data.Surface.Apply(false, false);
        data.ClearedFoliage.SetPixels32(clear); data.ClearedFoliage.Apply(false, false);
        EditorUtility.SetDirty(data.Surface); EditorUtility.SetDirty(data.ClearedFoliage);
        var study = FindStudy();
        if (study != null) study.RefreshPathSurface();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    internal static void Save(CampPathPaintData data)
    {
        if (data == null) return;
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.SaveAssetIfDirty(data.Surface);
        AssetDatabase.SaveAssetIfDirty(data.ClearedFoliage);
    }
}

[CustomEditor(typeof(CampGroundStudy))]
public sealed class CampGroundStudyInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Рисовать дорожки в Scene", GUILayout.Height(30))) CampPathPainter.Open();
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
