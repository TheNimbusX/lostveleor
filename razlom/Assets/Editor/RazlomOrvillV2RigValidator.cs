using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RazlomOrvillV2RigValidator
{
    private const string ModelPath =
        "Assets/Resources/Characters/Orvill_v2/Orvill_v2_CombatRig.fbx";

    private static readonly string[] RequiredBones =
    {
        "Hips", "UpperChest", "LeftHand", "RightHand", "Shield_L", "Weapon_R"
    };

    private static readonly string[] ClipPaths =
    {
        "Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations/Orvill_GuardIdle.fbx",
        "Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations/Orvill_GuardWalk.fbx",
        "Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations/Orvill_SwordAttack.fbx",
        "Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations/Orvill_HitLeft.fbx",
        "Assets/Resources/Characters/Orvill_ShieldInfantry_01/Animations/Orvill_DeathBack.fbx"
    };

    [Serializable]
    private sealed class Report
    {
        public string unityVersion;
        public string modelPath;
        public string animationType;
        public string avatarSetup;
        public bool avatarExists;
        public bool avatarIsValid;
        public bool avatarIsHuman;
        public int meshRenderers;
        public int vertices;
        public int triangles;
        public int rendererBones;
        public Vector3 meshBoundsSize;
        public Vector3 meshBoundsCenter;
        public string hipsMapping;
        public string upperChestMapping;
        public string leftHandMapping;
        public string rightHandMapping;
        public string[] requiredBonesFound;
        public string[] humanoidClips;
        public string[] errors;
        public bool passed;
    }

    public static void Run()
    {
        var errors = new List<string>();
        var report = new Report
        {
            unityVersion = Application.unityVersion,
            modelPath = ModelPath
        };

        try
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                errors.Add("ModelImporter missing");
            }
            else
            {
                report.animationType = importer.animationType.ToString();
                report.avatarSetup = importer.avatarSetup.ToString();
                if (importer.animationType != ModelImporterAnimationType.Human)
                    errors.Add("Model is not imported as Humanoid");

                Dictionary<string, string> mapping = importer.humanDescription.human
                    .ToDictionary(item => item.humanName, item => item.boneName);
                report.hipsMapping = GetMapping(mapping, "Hips");
                report.upperChestMapping = GetMapping(mapping, "UpperChest");
                report.leftHandMapping = GetMapping(mapping, "LeftHand");
                report.rightHandMapping = GetMapping(mapping, "RightHand");
                RequireMapping(mapping, "Hips", "Hips", errors);
                RequireMapping(mapping, "UpperChest", "UpperChest", errors);
                RequireMapping(mapping, "LeftHand", "LeftHand", errors);
                RequireMapping(mapping, "RightHand", "RightHand", errors);
            }

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
            report.avatarExists = avatar != null;
            report.avatarIsValid = avatar != null && avatar.isValid;
            report.avatarIsHuman = avatar != null && avatar.isHuman;
            if (!report.avatarExists) errors.Add("Imported Avatar missing");
            else
            {
                if (!report.avatarIsValid) errors.Add("Imported Avatar is invalid");
                if (!report.avatarIsHuman) errors.Add("Imported Avatar is not Humanoid");
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                errors.Add("Imported model prefab missing");
            }
            else
            {
                var transforms = model.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(item => item.name, item => item);
                report.requiredBonesFound = RequiredBones
                    .Where(transforms.ContainsKey)
                    .ToArray();
                foreach (string bone in RequiredBones)
                    if (!transforms.ContainsKey(bone)) errors.Add("Required bone missing: " + bone);

                SkinnedMeshRenderer[] renderers =
                    model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.meshRenderers = renderers.Length;
                report.vertices = renderers.Sum(item =>
                    item.sharedMesh != null ? item.sharedMesh.vertexCount : 0);
                report.triangles = renderers.Sum(item =>
                    item.sharedMesh != null ? item.sharedMesh.triangles.Length / 3 : 0);
                report.rendererBones = renderers.Sum(item => item.bones.Length);
                if (renderers.Length > 0 && renderers[0].sharedMesh != null)
                {
                    report.meshBoundsSize = renderers[0].sharedMesh.bounds.size;
                    report.meshBoundsCenter = renderers[0].sharedMesh.bounds.center;
                }
                if (renderers.Length != 1) errors.Add("Expected one SkinnedMeshRenderer");
                // Unity splits source vertices at hard-normal and UV seams.
                if (report.vertices != 14501)
                    errors.Add("Unexpected vertex count: " + report.vertices);
                if (report.triangles != 21511)
                    errors.Add("Unexpected triangle count: " + report.triangles);
                if (report.rendererBones != 45)
                    errors.Add("Unexpected renderer bone count: " + report.rendererBones);
                if (Mathf.Abs(report.meshBoundsSize.x - 1.26294f) > 0.01f ||
                    Mathf.Abs(report.meshBoundsSize.y - 0.6668f) > 0.01f ||
                    Mathf.Abs(report.meshBoundsSize.z - 1.88f) > 0.01f)
                    errors.Add("Unexpected imported mesh bounds: " + report.meshBoundsSize);
                if (renderers.Any(item => item.sharedMesh == null || item.bones.Any(bone => bone == null)))
                    errors.Add("Skinned mesh has missing mesh or bone bindings");
            }

            var humanoidClips = new List<string>();
            foreach (string path in ClipPaths)
            {
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(item => !item.name.StartsWith("__", StringComparison.Ordinal));
                if (clip == null)
                    errors.Add("Animation clip missing: " + path);
                else if (!clip.isHumanMotion)
                    errors.Add("Animation clip is not Humanoid: " + path);
                else
                    humanoidClips.Add(clip.name);
            }
            report.humanoidClips = humanoidClips.ToArray();
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
        }

        report.errors = errors.ToArray();
        report.passed = errors.Count == 0;
        string output = GetArgument("-validationOutput");
        if (string.IsNullOrEmpty(output))
            output = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "orvill_v2_rig_validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("RAZLOM_ORVILL_V2_RIG_PASSED=" + report.passed);
        if (!report.passed) throw new Exception(string.Join(" | ", errors));
    }

    private static string GetMapping(IReadOnlyDictionary<string, string> mapping, string humanName)
    {
        return mapping.TryGetValue(humanName, out string boneName) ? boneName : string.Empty;
    }

    private static void RequireMapping(
        IReadOnlyDictionary<string, string> mapping,
        string humanName,
        string expectedBone,
        ICollection<string> errors)
    {
        if (!mapping.TryGetValue(humanName, out string actualBone) || actualBone != expectedBone)
            errors.Add($"{humanName} maps to '{actualBone ?? "<missing>"}', expected '{expectedBone}'");
    }

    private static string GetArgument(string key)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length - 1; index++)
            if (arguments[index] == key) return arguments[index + 1];
        return null;
    }
}
