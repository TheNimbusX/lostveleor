using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.View;
using Object = UnityEngine.Object;

// Отдельная сцена позволяет сравнить художественный эталон с игрой, сохранив авторский лагерь.
public static class CampVisualStudy
{
    const string Study = "Assets/Scenes/Studies/CampAtmospheric.unity";
    const string Profile = "Assets/Scenes/Studies/CampAtmosphericLook.asset";
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Output => Path.Combine(Repo, "ART/CAMP/visual-direction-2026-09-13");
    static string Request => Path.Combine(Repo, "artifacts/request-camp-visual-study");

    [InitializeOnLoadMethod] static void Watch()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("CampVisualStudyRestore", false))
                EditorApplication.delayCall += Restore;
        };
    }
    static void Restore()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty) return;
        SessionState.SetBool("CampVisualStudyRestore", false);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    }
    static void Poll()
    {
        if (SessionState.GetBool("CampVisualStudyPlay", false) && EditorApplication.isPlaying && !EditorApplication.isCompiling && MainMenuView.IsOpen)
        {
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            if (menu != null) typeof(MainMenuView).GetMethod("StartGame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(menu, null);
            return;
        }
        if (SessionState.GetBool("CampVisualStudyPlay", false) && EditorApplication.isPlaying && !EditorApplication.isCompiling && Time.timeSinceLevelLoad > 5f)
        {
            SessionState.SetBool("CampVisualStudyPlay", false);
            try
            {
                bool depth = SessionState.GetBool("CampVisualStudyDepth", false);
                Render(Camera.main, depth ? "06-unity-depth-game.png" : "05-unity-atmospheric-game.png");
                if (depth) Render(Camera.main, "07-hero-depth-detail.png", 3f);
                Inspect();
            }
            finally { SessionState.SetBool("CampVisualStudyRestore", true); EditorApplication.isPlaying = false; }
        }
        if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string action = File.ReadAllText(Request).Trim();
        if (action == "create" && EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        if (action == "depth-roots") { CampDepthStudyAuthoring.RefineRoots(); return; }
        try { if (action == "create") Create(); else if (action == "play") Play(); else if (action == "play-depth") { SessionState.SetBool("CampVisualStudyDepth", true); Play(); } else if (action == "depth-build") CampDepthStudyAuthoring.Build(); else if (action == "depth-polish") CampDepthStudyAuthoring.Polish(); else if (action == "restore") Restore(); else if (action == "depth-inspect") CampDepthStudyAuthoring.Inspect(); else Inspect(); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Output, "study-error.txt"), e.ToString()); }
    }

    [MenuItem("Разлом/Лагерь/Визуал — снять атмосферный этюд в игре")]
    public static void Play()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Для проверки нужна сохранённая сцена в Edit Mode.");
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            if (EditorSceneManager.GetActiveScene().path != Study)
                throw new InvalidOperationException("Исходная сцена содержит несохранённые изменения.");
            CampSceneAmbiencePreview.Stop();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        EditorSceneManager.OpenScene(Study, OpenSceneMode.Single);
        SessionState.SetBool("CampVisualStudyPlay", true);
        EditorApplication.isPlaying = true;
    }

    static void Render(Camera source, string filename, float zoom = 1f)
    {
        if (source == null) throw new InvalidOperationException("Игровая камера не найдена.");
        var go = new GameObject("Visual study capture") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
        camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        camera.aspect = 16f / 9f;
        camera.orthographicSize /= zoom;
        var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.requiresDepthTexture = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; Texture2D pixels = null;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, filename), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            if (pixels != null) Object.DestroyImmediate(pixels); Object.DestroyImmediate(go);
        }
    }

    public static void Inspect()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        report.AppendLine($"scene={scene.path} dirty={scene.isDirty} play={Application.isPlaying}");
        report.AppendLine($"ambient={RenderSettings.ambientMode} sky={RenderSettings.ambientSkyColor} equator={RenderSettings.ambientEquatorColor} ground={RenderSettings.ambientGroundColor}");
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            report.AppendLine($"light={light.name} type={light.type} rgb={light.color} intensity={light.intensity} rotation={light.transform.eulerAngles} shadows={light.shadows} strength={light.shadowStrength}");
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            report.AppendLine($"volume={volume.name} profile={AssetDatabase.GetAssetPath(volume.sharedProfile)} weight={volume.weight}");
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            report.AppendLine($"camera={camera.name} position={camera.transform.position} rotation={camera.transform.eulerAngles} size={camera.orthographicSize}");
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world?.CampRoot != null)
            foreach (Transform t in world.CampRoot.transform)
                report.AppendLine($"camp child={t.name} position={t.position}");
        File.WriteAllText(Path.Combine(Output, "study-inspect.txt"), report.ToString());
    }

    [MenuItem("Разлом/Лагерь/Визуал — создать атмосферный этюд")]
    public static void Create()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Создание этюда запускается из исходной сцены в Edit Mode.");
        if (File.Exists(Study) || File.Exists(Profile)) throw new IOException("Этюд уже существует: перезапись вручную изменённого варианта запрещена.");
        Directory.CreateDirectory("Assets/Scenes/Studies");
        AssetDatabase.Refresh();
        CampSceneAmbiencePreview.Stop();
        if (!EditorSceneManager.SaveScene(scene, Study, true)) throw new IOException("Не удалось сохранить копию сцены.");
        // Переключение разрешено только из сохранённой сцены, чтобы не затронуть ручные правки.
        var copy = EditorSceneManager.OpenScene(Study, OpenSceneMode.Single);
        try
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var light in lights)
            {
                if (light.gameObject.scene != copy || light.type != LightType.Directional) continue;
                if (light.name == "Key Light")
                {
                    light.color = new Color(1f, .89f, .72f); light.intensity = 1.65f;
                    light.shadows = LightShadows.Soft; light.shadowStrength = .93f;
                    RenderSettings.sun = light;
                }
                else if (light.name == "Fill Light") { light.color = new Color(.48f, .69f, .78f); light.intensity = .055f; }
                else if (light.name == "Rim Light") { light.color = new Color(.53f, .72f, .88f); light.intensity = .12f; }
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.37f, .50f, .60f);
            RenderSettings.ambientEquatorColor = new Color(.24f, .35f, .39f);
            RenderSettings.ambientGroundColor = new Color(.115f, .16f, .17f);
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (volume.gameObject.scene != copy || volume.name != "Global Volume" || volume.sharedProfile == null) continue;
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile), Profile)) throw new IOException("Не удалось скопировать профиль цвета.");
                volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Profile);
                var profile = volume.sharedProfile;
                var color = Component<ColorAdjustments>(profile);
                color.postExposure.Override(.10f); color.contrast.Override(14f); color.saturation.Override(12f); color.colorFilter.Override(Color.white);
                var levels = Component<ShadowsMidtonesHighlights>(profile);
                levels.shadows.Override(new Vector4(.89f, 1.01f, 1.07f, -.025f));
                levels.midtones.Override(new Vector4(1, 1, 1, 0));
                levels.highlights.Override(new Vector4(1.035f, 1.005f, .96f, 0));
                var bloom = Component<Bloom>(profile); bloom.intensity.Override(.22f); bloom.threshold.Override(1.2f);
                Component<Vignette>(profile).intensity.Override(.1f);
                foreach (var component in profile.components) EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            var start = world.CampRoot.transform.Find("Anchor - Player");
            var smith = world.CampRoot.transform.Find("Anchor - Smith");
            // Только в этюде герой появляется у кузницы, чтобы сравнивать с выбранным кадром.
            start.position = smith.position;
            CampSceneAmbiencePreview.Stop();
            EditorSceneManager.SaveScene(copy);
            Inspect();
        }
        finally
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        }
    }
    static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component)) { component = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(component, profile); }
        return component;
    }
}
