using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RazlomGameplayAnimationValidator
{
    [Serializable] private sealed class Report
    {
        public string unityVersion;
        public bool pelagPrefabLoaded;
        public bool orvillPrefabLoaded;
        public int pelagClipCount;
        public int orvillClipCount;
        public string[] pelagParameters;
        public string[] orvillParameters;
        public bool sampleSceneHasBootstrap;
        public int equipmentSwitcherScripts;
        public string[] errors;
        public bool passed;
    }

    public static void Run()
    {
        var errors = new List<string>();
        var report = new Report { unityVersion = Application.unityVersion };
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameObject pelag = Resources.Load<GameObject>(
                "Characters/Pelag_Boarder_01/Pelag_Boarder_01_Validated");
            GameObject orvill = Resources.Load<GameObject>(
                "Characters/Orvill_ShieldInfantry_01/Orvill_ShieldInfantry_01_Validated");
            report.pelagPrefabLoaded = ValidatePrefab(pelag, "Pelag", errors, out report.pelagParameters);
            report.orvillPrefabLoaded = ValidatePrefab(orvill, "Orvill", errors, out report.orvillParameters);

            report.pelagClipCount = CountClips("Assets/Resources/Characters/Pelag_Boarder_01/Animations");
            report.orvillClipCount = CountClips("Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations");
            if (report.pelagClipCount != 13) errors.Add("Expected 13 Pelag clips, got " + report.pelagClipCount);
            if (report.orvillClipCount != 11) errors.Add("Expected 11 Orvill clips, got " + report.orvillClipCount);

            report.equipmentSwitcherScripts = AssetDatabase.FindAssets("RazlomEquipmentSwitcher t:MonoScript").Length;
            if (report.equipmentSwitcherScripts != 1)
                errors.Add("Expected exactly one RazlomEquipmentSwitcher script, got " + report.equipmentSwitcherScripts);

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            report.sampleSceneHasBootstrap = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true))
                .Any(component => component != null && component.GetType().FullName == "Game.View.Bootstrap");
            if (!report.sampleSceneHasBootstrap) errors.Add("SampleScene has no Game.View.Bootstrap");
        }
        catch (Exception ex)
        {
            errors.Add(ex.ToString());
        }

        report.errors = errors.ToArray();
        report.passed = errors.Count == 0;
        string output = GetArgument("-validationOutput");
        if (string.IsNullOrEmpty(output))
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "gameplay_animation_validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("RAZLOM_GAMEPLAY_ANIMATION_PASSED=" + report.passed);
        if (!report.passed) throw new Exception(string.Join(" | ", errors));
    }

    private static bool ValidatePrefab(GameObject prefab, string label, List<string> errors,
        out string[] parameters)
    {
        parameters = Array.Empty<string>();
        if (prefab == null) { errors.Add(label + " Resources prefab missing"); return false; }
        var animator = prefab.GetComponent<Animator>();
        if (animator == null) { errors.Add(label + " Animator missing"); return false; }
        if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            errors.Add(label + " Humanoid Avatar invalid");
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) { errors.Add(label + " AnimatorController missing"); return false; }
        parameters = controller.parameters.Select(p => p.name).ToArray();
        foreach (string required in label == "Pelag"
            ? new[] { "MoveSpeed", "AttackA", "AttackB", "HeavyAttack", "HitFront", "Death" }
            : new[] { "MoveSpeed", "SwordAttack", "ShieldBash", "HitLeft", "HitRight", "Death" })
            if (!parameters.Contains(required)) errors.Add(label + " parameter missing: " + required);
        return true;
    }

    private static int CountClips(string folder)
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Any(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)))
                count++;
        }
        return count;
    }

    private static string GetArgument(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
        return null;
    }
}
