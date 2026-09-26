using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class StonehoofTestWindow : EditorWindow
{
    private const string Pending = "StonehoofTest.Pending", Previous = "StonehoofTest.PreviousScene";
    private bool _obstacle = true, _pack;
    [MenuItem("Разлом/Камнекопыт/Тестовый бой", priority = 11)]
    public static void Open() => GetWindow<StonehoofTestWindow>("Камнекопыт");
    [InitializeOnLoadMethod]
    private static void Register() { EditorApplication.update -= StartWhenReady; EditorApplication.update += StartWhenReady; }
    private void OnGUI()
    {
        minSize = new Vector2(360, 255); EditorGUILayout.Space(12);
        GUILayout.Label("Камнекопыт · тестовый бой", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("180 здоровья · 30 физического урона\nПодготовка 1 с · разбег 12 м/с\nСтена: оглушение 1,2 с · открытый край: торможение\nНовый замах через 4 с после остановки", MessageType.None);
        _pack = EditorGUILayout.Toggle("Три Камнекопыта", _pack);
        _obstacle = EditorGUILayout.Toggle("Камень для столкновения", _obstacle);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
            if (GUILayout.Button(EditorApplication.isPlaying ? "Повторить бой" : "Начать тестовый бой", GUILayout.Height(34))) Launch(_pack ? 3 : 1, _obstacle);
        if (EditorApplication.isPlaying && GUILayout.Button("Завершить тест")) EditorApplication.isPlaying = false;
        GUILayout.Label("Обычное управление. F8 — бессмертие и способности.\nТест не меняет прогресс, экипировку или запасы зелий.", EditorStyles.wordWrappedMiniLabel);
    }
    public static void Launch(int count = 1, bool obstacle = true)
    {
        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.playModeStartScene;
            SessionState.SetString(Previous, previous != null ? AssetDatabase.GetAssetPath(previous) : "");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        }
        SessionState.SetInt("StonehoofTest.Count", count); SessionState.SetBool("StonehoofTest.Obstacle", obstacle);
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StartWhenReady()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        var driver = Object.FindAnyObjectByType<TickDriver>(); if (driver?.Session == null) return;
        var layout = driver.GetComponent<LayoutView>(); if (layout?.Profile == null) return;
        SessionState.SetBool(Pending, false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Previous, ""));
        Time.timeScale = 1; var menu = driver.GetComponent<MainMenuView>(); if (menu != null && menu.enabled) menu.StartGame();
        driver.SetGameplayPaused(false);
        driver.StartStonehoofTest(layout.Profile, 20260925UL, SessionState.GetInt("StonehoofTest.Count", 1), SessionState.GetBool("StonehoofTest.Obstacle", true));
        string record = SessionState.GetString("StonehoofTest.Record", "");
        if (!string.IsNullOrEmpty(record))
        { SessionState.SetString("StonehoofTest.Record", ""); StonehoofEditorRecorder.Begin(record, SessionState.GetInt("StonehoofTest.RecordFps", 60)); }
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
}
