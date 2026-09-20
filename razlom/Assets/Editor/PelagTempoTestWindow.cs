using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PelagTempoTestWindow : EditorWindow
{
    const string Pending = "PelagTempo.Pending", Previous = "PelagTempo.Scene";
    static readonly string[] Names = { "Вихрь", "Рассекающий удар", "Ладно смазал", "Шквал",
        "Удар якорем", "Крушение", "Абордаж", "Взрывная смесь", "На вылет", "Отбой" };
    [SerializeField] int[] _skills = { 0, 1, 8, 9 };
    [SerializeField] int _preset;

    [MenuItem("Разлом/Пелаг/Темп боя", priority = 11)]
    public static void Open() => GetWindow<PelagTempoTestWindow>("Темп боя");

    [MenuItem("Разлом/Пелаг/Удар якорем — проверка", priority = 12)]
    public static void OpenAnchorSlam()
    {
        var window=GetWindow<PelagTempoTestWindow>("Удар якорем");
        window._skills=new[]{4,1,8,9};window._preset=0;
    }

    [InitializeOnLoadMethod]
    static void Register()
    {
        EditorApplication.update -= StartWhenReady;
        EditorApplication.update += StartWhenReady;
    }
    void OnGUI()
    {
        minSize = new Vector2(380, 295);
        EditorGUILayout.Space(10);
        GUILayout.Label("Пелаг · темп боя", EditorStyles.boldLabel);
        for (int i = 0; i < 4; i++) _skills[i] = EditorGUILayout.Popup("Навык " + (i + 1), _skills[i], Names);
        _preset = EditorGUILayout.Popup("Сборка", _preset, new[] { "Базовая", "Средняя", "Быстрая" });
        EditorGUILayout.HelpBox(_preset == 0 ? "Начальные статы, настоящий расход лавидия и перезарядки."
            : _preset == 1 ? "+40% исполнение · +50% восстановление · +3 лавидия/с · +35% атака · +10% ходьба"
            : "+100% исполнение и восстановление · +6 лавидия/с · +80% атака · +20% ходьба", MessageType.None);
        bool distinct = true;
        for (int i = 0; i < 4; i++) for (int j = 0; j < i; j++) if (_skills[i] == _skills[j]) distinct = false;
        if (!distinct) EditorGUILayout.HelpBox("Выберите четыре разных навыка.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!distinct || EditorApplication.isCompiling || EditorApplication.isUpdating))
            if (GUILayout.Button(EditorApplication.isPlaying ? "Повторить бой с выбранной сборкой" : "Начать бой", GUILayout.Height(35))) Launch();
        if (EditorApplication.isPlaying && GUILayout.Button("Завершить тест")) EditorApplication.isPlaying = false;
        EditorGUILayout.LabelField("Лесной бутон и четыре ближника. ESC — управление мышью/WASD. Настройки стенда не сохраняются в прогресс.", EditorStyles.wordWrappedMiniLabel);
    }
    void Launch()
    {
        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.playModeStartScene;
            SessionState.SetString(Previous, scene != null ? AssetDatabase.GetAssetPath(scene) : "");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        }
        for (int i = 0; i < 4; i++) SessionState.SetInt("PelagTempo.Skill" + i, _skills[i]);
        SessionState.SetInt("PelagTempo.Preset", _preset);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }
    static void StartWhenReady()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        var driver = Object.FindAnyObjectByType<TickDriver>();
        if (driver?.Session == null || driver.GetComponent<LayoutView>()?.Profile == null) return;
        SessionState.SetBool(Pending, false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Previous, ""));
        driver.GetComponent<MainMenuView>()?.StartGame();
        driver.SetGameplayPaused(false);
        Time.timeScale = 1f;
        var skills = new int[4];
        for (int i = 0; i < 4; i++) skills[i] = SessionState.GetInt("PelagTempo.Skill" + i, i);
        driver.StartTempoTest(skills, SessionState.GetInt("PelagTempo.Preset", 0));
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
}
