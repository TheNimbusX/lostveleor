using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ForestBudTestWindow : EditorWindow
{
    private const string Pending = "ForestBudTest.Pending";
    private const string PreviousScene = "ForestBudTest.PreviousScene";

    [MenuItem("Разлом/Лесной бутон/Тестовый бой", priority = 10)]
    public static void Open() => GetWindow<ForestBudTestWindow>("Лесной бутон");

    [MenuItem("Разлом/Тест Forest Bud", priority = 3)]
    public static void OpenFromMainMenu() => Open();

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.update -= StartWhenReady;
        EditorApplication.update += StartWhenReady;
        // Одноразовый запрос позволяет открыть окно после импорта, не запуская бой и не меняя сцену.
        const string request = "Library/ForestBudTest.open";
        if (System.IO.File.Exists(request))
        {
            System.IO.File.Delete(request);
            EditorApplication.delayCall += Open;
        }
    }

    private void OnGUI()
    {
        minSize = new Vector2(340, 205);
        EditorGUILayout.Space(12);
        GUILayout.Label("Лесной бутон · тестовый бой", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("5 плодов · дальность 10 м\nКаждый плод выбирает цель при выстреле и падает через 1,5 секунды. Красная метка остаётся на месте.", MessageType.None);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
        {
            if (GUILayout.Button(EditorApplication.isPlaying ? "Повторить бой" : "Начать тестовый бой", GUILayout.Height(34)))
                Launch();
        }
        if (EditorApplication.isPlaying && GUILayout.Button("Завершить тест")) EditorApplication.isPlaying = false;
        EditorGUILayout.Space(8);
        GUILayout.Label("Управление обычное. F8 — настройки теста и бессмертие.", EditorStyles.wordWrappedMiniLabel);
    }

    private static void Launch()
    {
        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.playModeStartScene;
            SessionState.SetString(PreviousScene, previous != null ? AssetDatabase.GetAssetPath(previous) : "");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        }
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void StartWhenReady()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        var driver = Object.FindAnyObjectByType<TickDriver>();
        if (driver == null || driver.Session == null) return;
        var layout = driver.GetComponent<LayoutView>();
        if (layout == null || layout.Profile == null) return;
        SessionState.SetBool(Pending, false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousScene, ""));
        Time.timeScale = 1f;
        var menu = driver.GetComponent<MainMenuView>();
        if (menu != null && menu.enabled) menu.StartGame();
        driver.SetGameplayPaused(false);
        driver.StartForestBudTest(layout.Profile, 20260829UL);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
}
