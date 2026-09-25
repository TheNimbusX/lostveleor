using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ForestWendigoTestWindow : EditorWindow
{
    private const string Pending="WendigoTest.Pending", Previous="WendigoTest.PreviousScene", Pack="WendigoTest.Pack";
    private bool _withPack;
    [MenuItem("Разлом/Лесной вендиго/Тестовый бой",priority=10)]
    public static void Open()=>GetWindow<ForestWendigoTestWindow>("Лесной вендиго");
    [InitializeOnLoadMethod]
    private static void Register(){EditorApplication.update-=StartWhenReady;EditorApplication.update+=StartWhenReady;}
    private void OnGUI()
    {
        minSize=new Vector2(360,225);EditorGUILayout.Space(12);
        GUILayout.Label("Лесной вендиго · тестовый бой",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Когти: 45 урона · между атаками 1,5 с.\nПрыжок: интервал 6,5 с, цель отмечается заранее.\nХодьба 3 м/с · 420 здоровья · без второй фазы",MessageType.None);
        _withPack=EditorGUILayout.Toggle("Добавить двух корнеползов",_withPack);
        using(new EditorGUI.DisabledScope(EditorApplication.isCompiling||EditorApplication.isUpdating))
            if(GUILayout.Button(EditorApplication.isPlaying?"Повторить бой":"Начать тестовый бой",GUILayout.Height(34)))Launch(_withPack);
        if(EditorApplication.isPlaying&&GUILayout.Button("Завершить тест"))EditorApplication.isPlaying=false;
        GUILayout.Label("Обычное управление. F8 — бессмертие и настройки героя.\nПрогресс и запасы зелий не сохраняются из этого боя.",EditorStyles.wordWrappedMiniLabel);
    }
    public static void Launch(bool withPack=false)
    {
        if(!EditorApplication.isPlaying)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var previous=EditorSceneManager.playModeStartScene;
            SessionState.SetString(Previous,previous!=null?AssetDatabase.GetAssetPath(previous):"");
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        }
        SessionState.SetBool(Pack,withPack);SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;
    }
    private static void StartWhenReady()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        var driver=Object.FindAnyObjectByType<TickDriver>();if(driver?.Session==null)return;
        var layout=driver.GetComponent<LayoutView>();if(layout?.Profile==null)return;
        SessionState.SetBool(Pending,false);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Previous,""));
        Time.timeScale=1;var menu=driver.GetComponent<MainMenuView>();if(menu!=null&&menu.enabled)menu.StartGame();
        driver.SetGameplayPaused(false);driver.StartWendigoTest(layout.Profile,20260925UL,SessionState.GetBool(Pack,false));
        string record = SessionState.GetString("WendigoTest.Record", "");
        if (!string.IsNullOrEmpty(record))
        {
            SessionState.SetString("WendigoTest.Record", "");
            WendigoEditorRecorder.Begin(record, SessionState.GetInt("WendigoTest.RecordFps", 60));
        }
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
}
