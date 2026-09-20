using System;
using System.IO;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CampLookAuthoring
{
    const string Folder = "Assets/Resources/Environment/Camp/LookVariants";
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Request => Path.Combine(Repo, "artifacts/request-camp-look");

    [InitializeOnLoadMethod] static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (Application.isBatchMode || Application.isPlaying || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(Request)) return;
        string action = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (action == "preview-check") CheckPreview(); else Install(); }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(Repo, "artifacts/camp-look-error.txt"), e.ToString());
        }
    }

    [MenuItem("Разлом/Лагерь/Подготовить варианты постобработки")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Остановите Play Mode перед настройкой.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Нужна основная сцена.");
        var world = UnityEngine.Object.FindAnyObjectByType<SceneWorldView>();
        var volume = world.CampRoot.GetComponentsInChildren<Volume>(true)
            .Single(v => v.sharedProfile != null && v.sharedProfile.name == "CampLook");
        var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
        var sun = lights.Single(l => l.name == "Key Light");
        var fill = lights.Single(l => l.name == "Fill Light");
        string backup = Path.Combine(Repo, "artifacts/camp-look-original");
        Directory.CreateDirectory(backup);
        if (!File.Exists(Path.Combine(backup, "SampleScene.unity")))
        {
            File.Copy(scene.path, Path.Combine(backup, "SampleScene.unity"));
            File.Copy(AssetDatabase.GetAssetPath(volume.sharedProfile), Path.Combine(backup, "CampLook.asset"));
            File.WriteAllText(Path.Combine(backup, "lighting.json"), JsonUtility.ToJson(new LightingSnapshot
            { sunColor = sun.color, shadowStrength = sun.shadowStrength, fillIntensity = fill.intensity, sunRotation = sun.transform.rotation }, true));
        }
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var controller = volume.GetComponent<CampLookController>() ?? Undo.AddComponent<CampLookController>(volume.gameObject);
        Undo.RecordObject(controller, "Варианты обработки лагеря");
        controller.Volume = volume; controller.Sun = sun; controller.Fill = fill;
        controller.Original = volume.sharedProfile;
        controller.Clean = Create(volume.sharedProfile, "CampClean", CampLookStyle.Clean);
        controller.Painterly = Create(volume.sharedProfile, "CampPainterly", CampLookStyle.Painterly);
        controller.Film = Create(volume.sharedProfile, "CampFilm", CampLookStyle.Film);
        controller.Aces = Create(volume.sharedProfile, "CampAcesComparison", CampLookStyle.Aces);
        controller.GoldenEvening = Create(volume.sharedProfile, "CampGoldenEvening", CampLookStyle.GoldenEvening);
        // Вечер поднимает яркость костра и фонарей через компонент ветра и огня.
        controller.Ambience = UnityEngine.Object.FindAnyObjectByType<CampAmbience>();
        controller.Style = CampLookStyle.Clean;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = controller.gameObject;
        File.WriteAllText(Path.Combine(Repo, "artifacts/camp-look-installed.txt"), DateTime.Now.ToString("O") + "\n" + JsonUtility.ToJson(controller, true));
        foreach (SceneView view in SceneView.sceneViews)
        { view.sceneViewState.showImageEffects = true; view.sceneLighting = true; }
        SceneView.RepaintAll();
        Debug.Log("[camp-look] Варианты установлены. Style работает в Scene и Play Mode.");
    }

    static VolumeProfile Create(VolumeProfile source, string name, CampLookStyle style)
    {
        string path = Folder + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) == null)
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new IOException(path);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        // Янтарный закат (выбор владельца 16 сентября): тёплые света, холодные тени,
        // мягкое свечение огней. Сам свет и туман ставит CampLookController.
        bool evening = style == CampLookStyle.GoldenEvening;
        var color = Get<ColorAdjustments>(profile);
        color.contrast.Override(evening ? 10 : style == CampLookStyle.Painterly ? 5 : style == CampLookStyle.Film ? 22 : 10);
        color.saturation.Override(evening ? 8 : style == CampLookStyle.Painterly ? 9 : style == CampLookStyle.Film ? -10 : 6);
        // Косое солнце само по себе съедает яркость земли — закату экспозиция нужна выше остальных стилей.
        color.postExposure.Override(evening ? .25f : style == CampLookStyle.Painterly ? .22f : style == CampLookStyle.Film ? .15f : .1f);
        var balance = Get<WhiteBalance>(profile);
        balance.temperature.Override(evening ? 20 : style == CampLookStyle.Painterly ? 6 : style == CampLookStyle.Film ? -9 : 0);
        balance.tint.Override(evening ? 3 : style == CampLookStyle.Film ? 3 : 0);
        var tones = Get<ShadowsMidtonesHighlights>(profile);
        tones.shadows.Override(evening ? new Vector4(.70f, .82f, 1, -.03f) :
            style == CampLookStyle.Painterly ? new Vector4(.76f, .94f, 1, .025f) :
            style == CampLookStyle.Film ? new Vector4(.74f, .9f, 1, .035f) : new Vector4(.83177567f, .9439252f, 1, -.025f));
        tones.highlights.Override(evening ? new Vector4(1, .84f, .62f, .04f) :
            style == CampLookStyle.Painterly ? new Vector4(1, .94f, .82f, .015f) :
            style == CampLookStyle.Film ? new Vector4(1, .97f, .9f, 0) : new Vector4(1, .9710145f, .92753625f, 0));
        var bloom = Get<Bloom>(profile);
        bloom.intensity.Override(evening ? .3f : style == CampLookStyle.Painterly ? .32f : .18f);
        bloom.threshold.Override(evening ? 1.02f : style == CampLookStyle.Painterly ? 1.1f : 1.25f);
        bloom.scatter.Override(evening ? .62f : style == CampLookStyle.Painterly ? .7f : .55f);
        var vignette = Get<Vignette>(profile);
        vignette.intensity.Override(evening ? .12f : style == CampLookStyle.Painterly ? .13f : style == CampLookStyle.Film ? .2f : 0);
        vignette.smoothness.Override(.8f);
        var grain = Get<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(style == CampLookStyle.Film ? .1f : 0);
        Get<Tonemapping>(profile).mode.Override(style == CampLookStyle.Aces ? TonemappingMode.ACES : TonemappingMode.Neutral);
        // Нулевые overrides исключают наследование этих эффектов от общего Volume.
        Get<MotionBlur>(profile).intensity.Override(0);
        Get<DepthOfField>(profile).mode.Override(DepthOfFieldMode.Off);
        Get<ChromaticAberration>(profile).intensity.Override(0);
        foreach (var component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static void CheckPreview()
    {
        var look = UnityEngine.Object.FindAnyObjectByType<CampLookController>();
        var world = UnityEngine.Object.FindAnyObjectByType<SceneWorldView>();
        var oldStyle = look.Style;
        var oldColor = look.Sun.color;
        float oldShadow = look.Sun.shadowStrength, oldFill = look.Fill.intensity;
        var oldProfile = look.Volume.sharedProfile;
        string folder = Path.Combine(Repo, "artifacts/camp-look-scene-preview");
        Directory.CreateDirectory(folder);
        var go = new GameObject("Проверка предпросмотра") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main);
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 6;
        camera.transform.rotation = Quaternion.Euler(48, 35, 0);
        camera.transform.position = world.CampRoot.transform.Find("Campfire").position + Vector3.up * .6f - camera.transform.forward * 80;
        var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
        var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            foreach (CampLookStyle style in Enum.GetValues(typeof(CampLookStyle)))
            {
                look.Style = style;
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, style + ".png"), pixels.EncodeToPNG());
                if (look.Sun.color != oldColor || look.Sun.shadowStrength != oldShadow || look.Fill.intensity != oldFill || look.Volume.sharedProfile != oldProfile)
                    throw new InvalidOperationException("Предпросмотр оставил временный свет после кадра.");
            }
            File.WriteAllText(Path.Combine(folder, "check.txt"), "PASS: every camera render restored original lights and profile.\n");
        }
        finally
        {
            look.Style = oldStyle; look.EndEditorPreview(); camera.targetTexture = null;
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(go);
            SceneView.RepaintAll();
        }
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var component))
        {
            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        component.active = true;
        return component;
    }

    [Serializable] class LightingSnapshot
    {
        public Color sunColor;
        public float shadowStrength, fillIntensity;
        public Quaternion sunRotation;
    }
}
