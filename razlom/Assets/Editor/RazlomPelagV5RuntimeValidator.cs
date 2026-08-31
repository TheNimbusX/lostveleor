using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Verifies the exact Pelag v5 assets used by ArenaView. The older gameplay
/// validator targets the retired Humanoid prefab and cannot prove the current
/// Generic Mixamo runtime contract.
/// </summary>
public static class RazlomPelagV5RuntimeValidator
{
    private const string RuntimePath =
        "Assets/Resources/Characters/Pelag_v5/Runtime/Pelag_v5_MixamoRig.fbx";
    private const string MixamoFolder =
        "Assets/Resources/Characters/Pelag_v5/Mixamo";
    private const string ControllerPath =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller";

    [Serializable]
    private sealed class Report
    {
        public string unityVersion;
        public bool runtimeModelLoaded;
        public bool runtimeAvatarValid;
        public bool runtimeAvatarIsGeneric;
        public int skinnedMeshCount;
        public int boneCount;
        public int triangleCount;
        public bool rightHandSocketFound;
        public bool controllerLoaded;
        public string[] controllerParameters;
        public string[] controllerStates;
        public string[] animationClips;
        public int genericMixamoImporters;
        public int mixamoImporterCount;
        public int baseColorWidth;
        public int baseColorHeight;
        public bool toonShaderFound;
        public bool weaponLoaded;
        public float weaponLengthMeters;
        public string[] errors;
        public bool passed;
    }

    [MenuItem("Разлом/Проверить Pelag v5 runtime")]
    public static void Run()
    {
        var errors = new List<string>();
        var report = new Report { unityVersion = Application.unityVersion };

        ValidateRuntimeModel(report, errors);
        ValidateController(report, errors);
        ValidateImporters(report, errors);
        ValidateLookdevAndWeapon(report, errors);

        report.errors = errors.ToArray();
        report.passed = errors.Count == 0;

        string output = Argument("-pelagV5ValidationOutput");
        if (string.IsNullOrEmpty(output))
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "pelag_v5_validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));

        Debug.Log("RAZLOM_PELAG_V5_RUNTIME_PASSED=" + report.passed);
        if (!report.passed) throw new Exception(string.Join(" | ", errors));
    }

    private static void ValidateRuntimeModel(Report report, List<string> errors)
    {
        GameObject prefab = Resources.Load<GameObject>(
            "Characters/Pelag_v5/Runtime/Pelag_v5_MixamoRig");
        report.runtimeModelLoaded = prefab != null;
        if (prefab == null)
        {
            errors.Add("Runtime Pelag v5 model is missing from Resources.");
            return;
        }

        Animator animator = prefab.GetComponent<Animator>();
        report.runtimeAvatarValid = animator != null && animator.avatar != null && animator.avatar.isValid;
        report.runtimeAvatarIsGeneric = report.runtimeAvatarValid && !animator.avatar.isHuman;
        if (!report.runtimeAvatarValid) errors.Add("Runtime Generic Avatar is invalid.");
        else if (!report.runtimeAvatarIsGeneric) errors.Add("Runtime Avatar unexpectedly became Humanoid.");

        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        report.skinnedMeshCount = renderers.Length;
        report.boneCount = renderers.Length == 0 ? 0 : renderers.Max(renderer => renderer.bones.Length);
        report.triangleCount = renderers.Sum(renderer => TriangleCount(renderer.sharedMesh));
        if (report.skinnedMeshCount != 1)
            errors.Add("Expected one skinned mesh, got " + report.skinnedMeshCount + ".");
        if (report.boneCount != 65)
            errors.Add("Expected the canonical 65-bone bind, got " + report.boneCount + ".");
        if (report.triangleCount != 19636)
            errors.Add("Expected 19636 runtime triangles, got " + report.triangleCount + ".");

        report.rightHandSocketFound = Find(prefab.transform, "mixamorig:RightHand") != null;
        if (!report.rightHandSocketFound) errors.Add("mixamorig:RightHand socket is missing.");
    }

    private static void ValidateController(Report report, List<string> errors)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        report.controllerLoaded = controller != null;
        if (controller == null)
        {
            errors.Add("Pelag v5 FullCombat controller is missing.");
            return;
        }

        report.controllerParameters = controller.parameters.Select(parameter => parameter.name).ToArray();
        string[] requiredParameters =
        {
            "MoveSpeed", "TurnDirection", "Relaxed", "Stunned", "AttackA", "AttackB",
            "HeavyAttack", "Hook", "HitFront", "Death"
        };
        foreach (string parameter in requiredParameters)
            if (!report.controllerParameters.Contains(parameter))
                errors.Add("Controller parameter is missing: " + parameter);

        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        report.controllerStates = states.Select(child => child.state.name).OrderBy(name => name).ToArray();
        string[] requiredStates =
        {
            "Idle_v5", "Run_v5", "TurnLeft_v5", "TurnRight_v5",
            "Saber_A_v5", "Saber_B_v5", "Whirlwind_v5", "Anchor_v5", "Hit_v5", "Death_v5"
        };
        foreach (string stateName in requiredStates)
        {
            AnimatorState state = states.Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == stateName);
            if (state == null) errors.Add("Controller state is missing: " + stateName);
            else if (state.motion == null) errors.Add("Controller state has no motion: " + stateName);
        }

        AnimatorState run = states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Run_v5");
        if (run != null && Mathf.Abs(run.speed - 0.60f) > 0.001f)
            errors.Add("Run_v5 speed must be 0.60, got " + run.speed + ".");

        AnimatorState saberA = states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Saber_A_v5");
        AnimatorState saberB = states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Saber_B_v5");
        if (saberA != null && Mathf.Abs(saberA.speed - 0.92f) > 0.001f)
            errors.Add("Saber_A_v5 speed must be 0.92, got " + saberA.speed + ".");
        if (saberB != null && Mathf.Abs(saberB.speed - 1.20f) > 0.001f)
            errors.Add("Saber_B_v5 speed must be 1.20, got " + saberB.speed + ".");
        if (saberB != null && saberB.motion is AnimationClip saberBClip && saberBClip.length < 1.5f)
            errors.Add("Saber_B_v5 must include authored recovery through frame 147.");

        AnimatorState whirlwind = states.Select(child => child.state)
            .FirstOrDefault(state => state.name == "Whirlwind_v5");
        if (whirlwind != null && Mathf.Abs(whirlwind.speed - 1.55f) > 0.001f)
            errors.Add("Whirlwind_v5 speed must be 1.55, got " + whirlwind.speed + ".");

        report.animationClips = states.Select(child => child.state.motion)
            .OfType<AnimationClip>()
            .Select(clip => clip.name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
    }

    private static void ValidateImporters(Report report, List<string> errors)
    {
        ModelImporter runtime = AssetImporter.GetAtPath(RuntimePath) as ModelImporter;
        if (runtime == null) errors.Add("Runtime model importer is missing.");
        else
        {
            if (runtime.animationType != ModelImporterAnimationType.Generic)
                errors.Add("Runtime model importer is not Generic.");
            if (runtime.importAnimation)
                errors.Add("Runtime body must not import an animation take.");
        }

        string[] files = Directory.GetFiles(MixamoFolder, "*.fbx", SearchOption.TopDirectoryOnly)
            .Select(path => path.Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();
        report.mixamoImporterCount = files.Length;
        foreach (string path in files)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && importer.animationType == ModelImporterAnimationType.Generic
                                 && importer.importAnimation)
                report.genericMixamoImporters++;
            else
                errors.Add("Mixamo clip is not a Generic animation importer: " + path);
        }
        if (report.mixamoImporterCount != 11)
            errors.Add("Expected 11 Mixamo FBX deliveries, got " + report.mixamoImporterCount + ".");
    }

    private static void ValidateLookdevAndWeapon(Report report, List<string> errors)
    {
        Texture2D baseColor = Resources.Load<Texture2D>(
            "Characters/Pelag_v4/Pelag_v4_BaseColor");
        if (baseColor == null) errors.Add("Pelag BaseColor is missing.");
        else
        {
            report.baseColorWidth = baseColor.width;
            report.baseColorHeight = baseColor.height;
            if (baseColor.width != 4096 || baseColor.height != 4096)
                errors.Add($"Expected 4096x4096 BaseColor, got {baseColor.width}x{baseColor.height}.");
        }

        report.toonShaderFound = Shader.Find("Razlom/Texture Toon") != null;
        if (!report.toonShaderFound) errors.Add("Razlom/Texture Toon shader is missing.");

        GameObject weapon = Resources.Load<GameObject>(
            "Weapons/Pelag/FantasySaber/Pelag_FantasySaber");
        report.weaponLoaded = weapon != null;
        if (weapon == null)
        {
            errors.Add("Pelag fantasy saber prefab is missing.");
            return;
        }

        MeshFilter filter = weapon.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null)
        {
            errors.Add("Pelag fantasy saber has no mesh.");
            return;
        }
        report.weaponLengthMeters = filter.sharedMesh.bounds.size.y;
        if (report.weaponLengthMeters < 1.20f || report.weaponLengthMeters > 1.45f)
            errors.Add("Normalized saber length is outside 1.20-1.45 m: " +
                       report.weaponLengthMeters + ".");
    }

    private static int TriangleCount(Mesh mesh)
    {
        if (mesh == null) return 0;
        long indices = 0;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            indices += (long)mesh.GetIndexCount(subMesh);
        return (int)(indices / 3L);
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
