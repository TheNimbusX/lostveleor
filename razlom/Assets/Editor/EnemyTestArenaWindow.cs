using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class EnemyTestArenaWindow : EditorWindow
{
    public const string ScenePath = "Assets/Scenes/EnemyTestArena.unity";
    private EnemyTestArena _arena;
    private EnemyTestSpawnPoint _point;
    private EnemyKind _kind = EnemyKind.ForestGuardian;
    private int _count = 1, _health = 100, _selectedEntity = -1;
    private Vector3 _position = new Vector3(6, 0, 0);
    private Vector2 _scroll;
    internal static readonly EnemyKind[] Kinds = BuildKinds();
    internal static readonly string[] Names = Array.ConvertAll(Kinds, EnemyTexts.Name);

    [MenuItem("Разлом/Мобы/Создать / открыть тестовый стенд", priority = 10)]
    public static void Open()
    {
        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateSceneIfMissing();
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        }
        GetWindow<EnemyTestArenaWindow>("Стенд мобов").Show();
    }

    // Safe to call from automation: save only a new additive scene, preserve the user's
    // active scene, and never overwrite authored spawn points on repeated invocation.
    public static void CreateSceneIfMissing()
    {
        if (System.IO.File.Exists(ScenePath)) return;
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Останови Play перед созданием сцены.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject("Стенд мобов");
            var arena = root.AddComponent<EnemyTestArena>();
            var bootstrap = root.AddComponent<Bootstrap>();
            arena.Bootstrap = bootstrap; bootstrap.EnemySandbox = arena;
            bootstrap.RunSeed = 42;
            bootstrap.Location = Resources.Load<LocationTheme>("Locations/Meadow");
            var cameraObject = new GameObject("Gameplay Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(-24, 30, -24), Quaternion.Euler(38, 45, 0));
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 8;
            camera.nearClipPlane = .1f; camera.farClipPlane = 180;
            camera.backgroundColor = new Color(.11f, .15f, .18f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<CombatCameraJuice>();
            cameraObject.AddComponent<CameraFollow>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_gameplayCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var lightObject = new GameObject("Свет");
            lightObject.transform.rotation = Quaternion.Euler(48, -35, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.48f, .52f, .57f);
            RenderSettings.sun = light;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Пол 40 × 40 м";
            floor.transform.localScale = Vector3.one * (Simulation.EnemySandboxSize / 10f);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/Environment/Camp/ground/M_Ground_Base_New.mat");
            arena.PlayerSpawn = Child(root.transform, "Старт героя", Vector3.zero);
            arena.SpawnRoot = Child(root.transform, "Точки появления", Vector3.zero);
            arena.ObstacleRoot = Child(root.transform, "Препятствия", Vector3.zero);
            for (int i = 0; i < 2; i++)
            {
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                obstacle.name = "Препятствие " + (i + 1);
                obstacle.transform.SetParent(arena.ObstacleRoot, false);
                obstacle.transform.position = new Vector3(i == 0 ? -4 : 4, 1, i == 0 ? -3 : 3);
                obstacle.transform.localScale = new Vector3(2, 1, 2);
                obstacle.GetComponent<Renderer>().sharedMaterial = floor.GetComponent<Renderer>().sharedMaterial;
                obstacle.AddComponent<EnemyTestObstacle>();
            }
            for (int i = 0; i < Kinds.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / Kinds.Length;
                var point = Child(arena.SpawnRoot, EnemyTexts.Name(Kinds[i]),
                    new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 10f).gameObject.AddComponent<EnemyTestSpawnPoint>();
                point.Kind = Kinds[i];
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Transform Child(Transform parent, string name, Vector3 at)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = at;
        return go.transform;
    }

    private static EnemyKind[] BuildKinds()
    {
        var list = new List<EnemyKind>();
        foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            if (EnemyArchetypes.IsDefined(kind)) list.Add(kind);
        // Хозяин Чащи без вида не показывается: включится вместе с переключателем босса.
        if (!Simulation.UseThicketMasterBoss) list.Remove(EnemyKind.ForestThicketMaster);
        return list.ToArray();
    }

    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        if (_arena == null) _arena = Object.FindAnyObjectByType<EnemyTestArena>();
        if (_arena == null)
        {
            EditorGUILayout.HelpBox("Открой отдельную сцену стенда из меню Разлом → Мобы.", MessageType.Info);
            if (!EditorApplication.isPlaying && GUILayout.Button("Создать / открыть сцену")) Open();
            return;
        }
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField("Все мобы · отдельная сцена", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("До Play меняй точки и препятствия в Hierarchy и Scene. Ctrl+D — копия точки. Сохрани сцену. В Play команды выполняются на ближайшем тике, изменения препятствий — при сбросе. F8: «Пелаг» — способности, «Бой» — бессмертие.", MessageType.None);
        if (!EditorApplication.isPlaying)
        {
            var arenaObject = new SerializedObject(_arena);
            arenaObject.Update(); EditorGUILayout.PropertyField(arenaObject.FindProperty("Seed"));
            arenaObject.ApplyModifiedProperties();
            if (GUILayout.Button("Выбрать настройки сцены")) Selection.activeObject = _arena;
        }
        _point = (EnemyTestSpawnPoint)EditorGUILayout.ObjectField("Точка", _point, typeof(EnemyTestSpawnPoint), true);
        if (Selection.activeGameObject != null)
        {
            var selectedPoint = Selection.activeGameObject.GetComponent<EnemyTestSpawnPoint>();
            if (selectedPoint != null && GUILayout.Button("Использовать выбранную точку")) _point = selectedPoint;
        }
        int kindIndex = Mathf.Max(0, Array.IndexOf(Kinds, _kind));
        _kind = Kinds[EditorGUILayout.Popup("Вид", kindIndex, Names)];
        _count = EditorGUILayout.IntSlider("Количество", _count, 1, 40);
        _health = EditorGUILayout.IntSlider("Здоровье, %", _health, 1, 100);
        if (_point == null) _position = EditorGUILayout.Vector3Field("Позиция", _position);
        else EditorGUILayout.LabelField("Позиция", _point.transform.position.ToString("F1"));
        if (!EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Добавить точку в сцену"))
            {
                var go = new GameObject(EnemyTexts.Name(_kind));
                Undo.RegisterCreatedObjectUndo(go, "Добавить точку моба");
                go.transform.SetParent(_arena.SpawnRoot, false);
                go.transform.position = _point != null ? _point.transform.position + Vector3.right * 2 : _position;
                var point = go.AddComponent<EnemyTestSpawnPoint>();
                point.Kind = _kind; point.Count = _count; point.HealthPercent = _health;
                _point = point; Selection.activeGameObject = go;
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            EditorGUILayout.HelpBox("Play запускается обычной кнопкой Unity. Изменения в Play временные.", MessageType.None);
        }
        else DrawRuntime();
        EditorGUILayout.EndScrollView();
    }

    private void DrawRuntime()
    {
        var driver = _arena.Driver;
        if (driver == null || driver.Sim == null) return;
        var sim = driver.Sim;
        if (GUILayout.Button("Добавить мобов"))
            driver.QueueEnemySandboxSpawn(_kind, _point != null ? _point.transform.position : _position, _count, _health);
        if (_point != null && GUILayout.Button("Добавить по настройкам точки"))
            driver.QueueEnemySandboxSpawn(_point.Kind, _point.transform.position, _point.Count, _point.HealthPercent);
        var ids = new List<int>(); var labels = new List<string>();
        for (int i = 1; i < sim.Entities.Count; i++)
            if (sim.Entities.Alive[i]) { ids.Add(i); labels.Add(i + ": " + EnemyTexts.Name(sim.Entities.Kind[i]) + " · " + sim.Entities.Health[i] + " HP"); }
        if (ids.Count > 0)
        {
            int selected = Mathf.Max(0, ids.IndexOf(_selectedEntity));
            _selectedEntity = ids[EditorGUILayout.Popup("Живой моб", selected, labels.ToArray())];
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Убрать")) driver.QueueEnemySandboxAction(EnemySandboxAction.Remove, _selectedEntity);
            if (GUILayout.Button("Убить")) driver.QueueEnemySandboxAction(EnemySandboxAction.Kill, _selectedEntity);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox("Убрать — без смерти и распада. Убить — настоящая смерть, у Расщепеня появятся дети.", MessageType.None);
        if (GUILayout.Button("Убрать всех мобов")) driver.QueueEnemySandboxAction(EnemySandboxAction.Clear);
        if (GUILayout.Button("Сбросить бой · тот же сид")) driver.QueueEnemySandboxReset();
        if (GUILayout.Button(driver.GameplayPaused ? "Продолжить" : "Пауза"))
        {
            if (!driver.GameplayPaused || Time.timeScale > 0) driver.SetGameplayPaused(!driver.GameplayPaused);
        }
        EditorGUILayout.LabelField("Живых: " + sim.CountAliveEnemies() + " · занято слотов: " + sim.Entities.Count + "/" + sim.Entities.Capacity);
        if (driver.GameplayPaused || EditorApplication.isPaused) EditorGUILayout.HelpBox("Пауза: команды ждут продолжения. Если открыт F8 — закрой его в Game.", MessageType.Info);
        if (!string.IsNullOrEmpty(sim.EnemySandboxError)) EditorGUILayout.HelpBox(sim.EnemySandboxError, MessageType.Warning);
        if (!sim.Entities.Alive[Simulation.PlayerId]) EditorGUILayout.HelpBox("Герой погиб. Сбрось бой для повторной попытки.", MessageType.Info);
    }
}

[CustomEditor(typeof(EnemyTestSpawnPoint))]
public sealed class EnemyTestSpawnPointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var kind = serializedObject.FindProperty("Kind");
        int index = Mathf.Max(0, Array.IndexOf(EnemyTestArenaWindow.Kinds, (EnemyKind)kind.intValue));
        kind.intValue = (int)EnemyTestArenaWindow.Kinds[EditorGUILayout.Popup("Вид моба", index, EnemyTestArenaWindow.Names)];
        EditorGUILayout.PropertyField(serializedObject.FindProperty("SpawnOnStart"), new GUIContent("Появляется при запуске"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Count"), new GUIContent("Количество"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("HealthPercent"), new GUIContent("Здоровье, %"));
        serializedObject.ApplyModifiedProperties();
        var point = (EnemyTestSpawnPoint)target;
        var arena = point.GetComponentInParent<EnemyTestArena>();
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || arena?.Driver == null))
            if (GUILayout.Button("Добавить в текущий бой"))
                arena.Driver.QueueEnemySandboxSpawn(point.Kind, point.transform.position, point.Count, point.HealthPercent);
    }
}
