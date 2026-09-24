using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AlchemyPlaytestLauncher
{
    const string Pending = "AlchemyPlaytest.Pending";
    const string Capture = "AlchemyPlaytest.Active";
    const string PreviousScene = "AlchemyPlaytest.PreviousScene";

    [MenuItem("Разлом/Алхимик/Тестовый стенд", priority = 10)]
    public static void Launch()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString(PreviousScene, previous != null ? AssetDatabase.GetAssetPath(previous) : "");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Capture, true);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Разлом/Алхимик/Тестовый стенд", true)]
    static bool CanLaunch() => !EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

    [InitializeOnLoadMethod]
    static void Register()
    {
        EditorApplication.update -= StartWhenReady;
        EditorApplication.update += StartWhenReady;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(Pending, false))
            SessionState.SetBool(Capture, false);
    }

    static void StartWhenReady()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        var driver = Object.FindAnyObjectByType<TickDriver>();
        if (driver == null || driver.Session == null || CampPlayerView.Instance == null || !CampPlayerView.Instance.Active)
            return;
        SessionState.SetBool(Pending, false);
        RestorePreviousScene();
        Time.timeScale = 1f;
        var menu = driver.GetComponent<MainMenuView>();
        if (menu != null && menu.enabled) menu.StartGame();
        driver.SetGameplayPaused(false);
        var overlay = driver.GetComponent<AlchemyPlaytestOverlay>();
        if (overlay == null) overlay = driver.gameObject.AddComponent<AlchemyPlaytestOverlay>();
        overlay.Initialize(driver);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        SessionState.SetBool(Pending, false);
        SessionState.SetBool(Capture, false);
        RestorePreviousScene();
    }

    static void RestorePreviousScene()
    {
        string path = SessionState.GetString(PreviousScene, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
    }
}
