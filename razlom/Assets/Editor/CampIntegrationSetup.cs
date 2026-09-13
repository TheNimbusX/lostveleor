using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Game.View;

// Запрос относится только к открытому лагерю; редактор и несохранённая сцена остаются у автора.
public static class CampIntegrationSetup
{
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    [InitializeOnLoadMethod]
    static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        PollPlayCheck();
        string request = Path.Combine(Repo, "artifacts/request-camp-integration");
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string action = File.ReadAllText(request).Trim();
        if ((action == "install" || action == "feedback" || action == "gold") && (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)) return;
        File.Delete(request);
        try { if (action == "install") Install(); else if (action == "feedback") TuneFeedback(); else if (action == "gold") TuneGold(); else if (action == "playcheck") BeginPlayCheck(); else if (action == "vsync") EnableGameViewVSync(); else Inspect(); }
        catch (System.Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Repo,"artifacts/camp-integration-error.txt"),e.ToString()); }
    }
    [MenuItem("Разлом/Диагностика/Включить VSync окна Game")]
    public static void EnableGameViewVSync()
    {
        // У Game View отдельная синхронизация, которую настройка сборки не включает.
        // Вызываем setter Unity: одной записи сериализованного поля недостаточно.
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
        var property = type.GetProperty("vSyncEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property == null || !property.CanWrite) throw new System.NotSupportedException("Unity не предоставляет переключатель vSyncEnabled.");
        var windows = Resources.FindObjectsOfTypeAll(type);
        if (windows.Length == 0) throw new System.InvalidOperationException("Открой вкладку Game.");
        var report = new StringBuilder();
        report.AppendLine($"time={System.DateTime.Now:O} playing={EditorApplication.isPlaying} graphics={SystemInfo.graphicsDeviceType}");
        foreach (var window in windows)
        {
            bool before = (bool)property.GetValue(window);
            property.SetValue(window, true);
            bool after = (bool)property.GetValue(window);
            ((EditorWindow)window).Repaint();
            report.AppendLine($"GameView: VSync {before} -> {after}");
            if (!after) throw new System.InvalidOperationException("Game View не включил VSync.");
        }
        File.WriteAllText(Path.Combine(Repo,"artifacts/game-view-vsync.txt"), report.ToString());
        Debug.Log(report.ToString());
    }
    static void BeginPlayCheck()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) throw new System.InvalidOperationException("Проверка запускается только из Edit Mode.");
        string output = Path.Combine(Repo,"artifacts/camp-editor-regression-"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        SessionState.SetString("CampIntegrationCheckOutput",output);
        SessionState.SetBool("CampIntegrationCheckStarted",false);
        SessionState.SetBool("CampIntegrationPlayCheck",true);
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }
    static void PollPlayCheck()
    {
        if (!SessionState.GetBool("CampIntegrationPlayCheck",false) || EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
        string output = SessionState.GetString("CampIntegrationCheckOutput","");
        if (MainMenuView.IsOpen)
        {
            // Проверка лагеря проходит ту же команду PLAY; timeScale меню равен нулю.
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            if (menu != null) typeof(MainMenuView).GetMethod("StartGame",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(menu,null);
            return;
        }
        if (!SessionState.GetBool("CampIntegrationCheckStarted",false))
        {
            if (Time.time < .7f) return;
            var driver = Object.FindAnyObjectByType<TickDriver>();
            if (driver == null || driver.Session == null) return;
            driver.gameObject.AddComponent<CampIntegrationCapture>().Initialize(output);
            if(Object.FindAnyObjectByType<CampRiver>()!=null)driver.gameObject.AddComponent<CampFinishCapture>().Initialize(output);
            SessionState.SetBool("CampIntegrationCheckStarted",true);
        }
        bool passed = File.Exists(Path.Combine(output,"result.txt"));
        if (!passed && Time.time < 40) return;
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-editor-check.txt"),(passed ? "PASS" : "FAIL: timeout")+"\n"+output);
        SessionState.SetBool("CampIntegrationPlayCheck",false);
        EditorApplication.isPlaying = false;
    }
    public static void TuneGold()
    {
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world == null || world.CampRoot == null) throw new System.InvalidOperationException("Открой лагерь.");
        var entrance = world.CampRoot.GetComponentInChildren<CampRiftEntrance>(true);
        if (entrance == null) throw new System.InvalidOperationException("Нет входа в забег.");
        var scene = world.CampRoot.scene;
        string backup = Path.Combine(Repo,"artifacts/camp-integration-live/gold-before-"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity");
        if (!EditorSceneManager.SaveScene(scene,backup,true)) throw new System.IO.IOException("Не сохранена резервная копия.");
        Undo.RecordObject(entrance,"Золотые горизонтальные лучи");
        entrance.GlowColor = new Color(1,.46f,.075f,1); entrance.GlowOffset = new Vector3(0,0,.10f);
        entrance.GlowIntensity = 2.2f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(entrance); EditorUtility.SetDirty(entrance);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-gold-installed.txt"),"18 gold beams towards the player, intensity 2.2, local z offset +0.1; "+scene.path);
    }
    [MenuItem("Разлом/Лагерь/Диагностика взаимодействий")]
    public static void Inspect()
    {
        var report = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        report.AppendLine($"scene={scene.path} dirty={scene.isDirty}");
        report.AppendLine($"playing={EditorApplication.isPlaying} paused={EditorApplication.isPaused} timeScale={Time.timeScale}");
        report.AppendLine($"time={Time.time} test={SessionState.GetBool("CampIntegrationPlayCheck",false)} started={SessionState.GetBool("CampIntegrationCheckStarted",false)} capture={CampIntegrationCapture.IsRunning}");
        var driver = Object.FindAnyObjectByType<TickDriver>();
        report.AppendLine($"driver={driver != null} enabled={driver?.enabled} session={driver?.Session != null} mode={driver?.Session?.Mode} pausedGameplay={driver?.GameplayPaused} training={driver?.Session?.Training?.Count} campInstance={CampPlayerView.Instance != null} trainingInstance={CampTrainingView.Instance != null}");
        if (driver?.Sim != null)
            for(int i=0;i<driver.Sim.Entities.Count;i++) report.AppendLine($"entity={i} pos={driver.Sim.Entities.Position[i]} hp={driver.Sim.Entities.Health[i]} alive={driver.Sim.Entities.Alive[i]}");
        foreach (var root in scene.GetRootGameObjects())
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string path = t.name; var p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            if (!path.Contains("Start The Run") && !path.Contains("Poligon") && !t.name.Contains("Anchor") && !t.name.Contains("Proving")) continue;
            report.AppendLine($"{path} position={t.position:F3} rotation={t.eulerAngles:F2} scale={t.lossyScale:F3} active={t.gameObject.activeSelf}");
            foreach (var renderer in t.GetComponents<Renderer>())
            {
                var filter = t.GetComponent<MeshFilter>();
                report.AppendLine($"  bounds={renderer.bounds} mesh={(filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "none")}");
            }
        }
        File.WriteAllText(Path.Combine(Repo, "artifacts/camp-integration-scene.txt"), report.ToString());
    }
    public static void InspectSaved()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Inspect();
    }
    public static void TuneFeedback()
    {
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world?.CampRoot == null) return;
        var scene = world.CampRoot.scene;
        string backup = Path.Combine(Repo,"artifacts/camp-integration-live"); Directory.CreateDirectory(backup);
        EditorSceneManager.SaveScene(scene,Path.Combine(backup,"feedback-before-"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity"),true);
        foreach (var dummy in world.CampRoot.GetComponentsInChildren<CampDummyView>(true))
        {
            if (dummy.Health != 100000) continue;
            Undo.RecordObject(dummy,"Читаемое здоровье манекена"); dummy.Health = 2000;
            PrefabUtility.RecordPrefabInstancePropertyModifications(dummy);
            EditorUtility.SetDirty(dummy);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-feedback-installed.txt"),"HP=2000; scene="+scene.path);
    }
    public static void BuildFeedbackCapture()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/artifacts/")) throw new System.InvalidOperationException("Изолированная проверка.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); TuneFeedback(); TuneGold();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    [MenuItem("Разлом/Лагерь/Подключить манекены и вход в забег")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world == null || world.CampRoot == null) throw new System.InvalidOperationException("Открой сцену лагеря.");
        var root = world.CampRoot.transform;
        Transform arch = null;
        var dummies = new System.Collections.Generic.List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("wooden+archway+3d+model") && t.parent.name == "Start The Run") arch = t;
            if (t.name.StartsWith("wooden+crosspost+3d+model") && t.parent.name == "Poligon") dummies.Add(t);
        }
        if (arch == null || dummies.Count != 2) throw new System.InvalidOperationException("Ожидались авторская арка Start The Run и два манекена Poligon.");
        string backup = Path.Combine(Repo,"artifacts/camp-integration-live");
        Directory.CreateDirectory(backup);
        EditorSceneManager.SaveScene(root.gameObject.scene, Path.Combine(backup,"before-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity"), true);
        int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Взаимодействия в лагере");
        if (root.GetComponent<CampTrainingView>() == null) Undo.AddComponent<CampTrainingView>(root.gameObject);
        foreach (var dummy in dummies)
            if (dummy.GetComponent<CampDummyView>() == null) Undo.AddComponent<CampDummyView>(dummy.gameObject);
        var entrance = arch.GetComponent<CampRiftEntrance>();
        if (entrance == null) entrance = Undo.AddComponent<CampRiftEntrance>(arch.gameObject);
        const string folder = "Assets/Resources/Environment/Camp";
        const string materialPath = folder + "/M_CampRiftGlow.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Game/Camp Rift Glow");
            if (shader == null) throw new System.InvalidOperationException("Не импортирован шейдер свечения арки.");
            material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath);
        }
        Undo.RecordObject(entrance,"Свечение арки"); entrance.GlowMaterial = material;
        PrefabUtility.RecordPrefabInstancePropertyModifications(entrance);
        // Удаляется только прежняя отдельная площадка; пользовательская группа Poligon остаётся.
        var serialized = new SerializedObject(world);
        var legacy = serialized.FindProperty("_provingGroundRoot");
        var oldGround = legacy.objectReferenceValue as GameObject;
        if (oldGround != null && oldGround != root.gameObject) Undo.DestroyObjectImmediate(oldGround);
        legacy.objectReferenceValue = null; serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(group);
        Inspect();
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-integration-installed.txt"),
            $"arch={arch.name}\ndummies={dummies.Count}\nscene={root.gameObject.scene.path}\nbackup={backup}\n");
    }
    public static void BuildCapture()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/artifacts/"))
            throw new System.InvalidOperationException("Проверка предназначена для отдельной копии проекта.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Install();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }
}
