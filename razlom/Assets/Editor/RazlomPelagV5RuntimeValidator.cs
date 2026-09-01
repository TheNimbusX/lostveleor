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
    private const int ExpectedMixamoImporterCount = 19;
    private const string RuntimePath =
        "Assets/Resources/Characters/Pelag_v5/Runtime/Pelag_v5_MixamoRig.fbx";
    private const string MixamoFolder =
        "Assets/Resources/Characters/Pelag_v5/Mixamo";
    private const string ControllerPath =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller";

    // Keep this list explicit. A loose folder count can pass while a required
    // delivery is missing and an unrelated FBX happens to take its place.
    private static readonly string[] RequiredMixamoFiles =
    {
        "Pelag_MX_AnchorAttack.fbx",
        "Pelag_MX_AnchorLeap.fbx",
        "Pelag_MX_AnchorSweep.fbx",
        "Pelag_MX_ChainStep.fbx",
        "Pelag_MX_Death.fbx",
        "Pelag_MX_DualCombo.fbx",
        "Pelag_MX_Hit.fbx",
        "Pelag_MX_Idle.fbx",
        "Pelag_MX_InjuredRun.fbx",
        "Pelag_MX_Run.fbx",
        "Pelag_MX_RunStart.fbx",
        "Pelag_MX_RunStop.fbx",
        "Pelag_MX_SaberCombo.fbx",
        "Pelag_MX_StrafeBack.fbx",
        "Pelag_MX_StrafeLeft.fbx",
        "Pelag_MX_StrafeRight.fbx",
        "Pelag_MX_TurnLeft.fbx",
        "Pelag_MX_TurnRight.fbx",
        "Pelag_MX_Whirlwind.fbx"
    };

    private sealed class ClipContract
    {
        public readonly string file;
        public readonly string clip;
        public readonly float firstFrame;
        public readonly float lastFrame;
        public readonly bool loop;
        public readonly float minLength;
        public readonly float maxLength;

        public ClipContract(string file, string clip, float firstFrame, float lastFrame,
            bool loop, float minLength, float maxLength)
        {
            this.file = file;
            this.clip = clip;
            this.firstFrame = firstFrame;
            this.lastFrame = lastFrame;
            this.loop = loop;
            this.minLength = minLength;
            this.maxLength = maxLength;
        }
    }

    // These are the authored 30 Hz deliveries. The broad length tolerances
    // account for Unity's FBX endpoint sampling while still catching a stale
    // 60 Hz cut or an accidentally unbounded take.
    private static readonly ClipContract[] AuthoredClipContracts =
    {
        new ClipContract("Pelag_MX_AnchorLeap.fbx", "Pelag_MX_AnchorLeap",
            1f, 16f, false, 0.46f, 0.54f),
        new ClipContract("Pelag_MX_AnchorSweep.fbx", "Pelag_MX_AnchorSweep",
            1f, 16f, false, 0.46f, 0.54f),
        new ClipContract("Pelag_MX_ChainStep.fbx", "Pelag_MX_ChainStep",
            1f, 6f, true, 0.14f, 0.20f),
        new ClipContract("Pelag_MX_RunStart.fbx", "Pelag_MX_RunStart",
            1f, 6f, false, 0.14f, 0.20f),
        new ClipContract("Pelag_MX_RunStop.fbx", "Pelag_MX_RunStop",
            1f, 6f, false, 0.14f, 0.20f),
        new ClipContract("Pelag_MX_StrafeLeft.fbx", "Pelag_MX_StrafeLeft",
            1f, 17f, true, 0.49f, 0.58f),
        new ClipContract("Pelag_MX_StrafeRight.fbx", "Pelag_MX_StrafeRight",
            1f, 17f, true, 0.49f, 0.58f),
        new ClipContract("Pelag_MX_StrafeBack.fbx", "Pelag_MX_StrafeBack",
            1f, 17f, true, 0.49f, 0.58f),
        new ClipContract("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackA",
            1f, 25f, false, 0.74f, 0.86f),
        new ClipContract("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackB",
            25f, 74f, false, 1.55f, 1.70f),
        new ClipContract("Pelag_MX_DualCombo.fbx", "Pelag_MX_DualCombo",
            1f, 76f, false, 2.42f, 2.58f)
    };

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
        public string[] controllerLayers;
        public int genericMixamoImporters;
        public int mixamoImporterCount;
        public string[] requiredMixamoFiles;
        public string[] missingMixamoFiles;
        public string[] authoredClipChecks;
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

        report.controllerParameters = controller.parameters
            .Select(parameter => parameter.name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        RequireParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "MoveX", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "MoveY", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "TurnDirection", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "Relaxed", AnimatorControllerParameterType.Bool, errors);
        RequireParameter(controller, "Stunned", AnimatorControllerParameterType.Bool, errors);
        RequireParameter(controller, "LocomotionPlaybackSpeed", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "AttackPlaybackSpeed", AnimatorControllerParameterType.Float, errors);
        RequireParameter(controller, "AttackA", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "AttackB", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "LowerAttackA", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "LowerAttackB", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "LowerHeavyAttack", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "HeavyAttack", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "Hook", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "AnchorLeap", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "AnchorSweep", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "ChainStep", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "HitFront", AnimatorControllerParameterType.Trigger, errors);
        RequireParameter(controller, "Death", AnimatorControllerParameterType.Trigger, errors);

        if (controller.layers == null || controller.layers.Length == 0)
        {
            report.controllerLayers = Array.Empty<string>();
            report.controllerStates = Array.Empty<string>();
            report.animationClips = Array.Empty<string>();
            errors.Add("Pelag v5 controller has no animator layers.");
            return;
        }

        report.controllerLayers = controller.layers
            .Select(layer => layer.name)
            .ToArray();

        var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
        for (int i = 0; i < controller.layers.Length; i++)
        {
            AnimatorControllerLayer layer = controller.layers[i];
            if (layer.stateMachine == null) continue;
            CollectStates(layer.stateMachine, layer.name, string.Empty, states);
        }

        report.controllerStates = states.Values
            .Select(state => state.name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Base-layer ability states drive the full-body authored clips. Basic
        // saber/whirlwind states intentionally live on masked layers, so they
        // must be checked by their layer-qualified path rather than assumed to
        // be children of Base Layer.
        RequireState(states, "Base Layer", "Idle_v5", true, errors);
        RequireState(states, "Base Layer", "Run_v5", true, errors);
        RequireState(states, "Base Layer", "RunStart_v5", true, errors);
        RequireState(states, "Base Layer", "RunStop_v5", true, errors);
        RequireState(states, "Base Layer", "TurnLeft_v5", true, errors);
        RequireState(states, "Base Layer", "TurnRight_v5", true, errors);
        RequireState(states, "Base Layer", "Anchor_v5", true, errors);
        RequireState(states, "Base Layer", "AnchorLeap_v5", true, errors);
        RequireState(states, "Base Layer", "AnchorSweep_v5", true, errors);
        RequireState(states, "Base Layer", "ChainStep_v5", true, errors);
        RequireState(states, "Base Layer", "Hit_v5", true, errors);
        RequireState(states, "Base Layer", "Death_v5", true, errors);
        RequireState(states, "UpperBody Combat", "UpperBody_Empty", false, errors);
        RequireState(states, "UpperBody Combat", "Saber_A_v5", true, errors);
        RequireState(states, "UpperBody Combat", "Saber_B_v5", true, errors);
        RequireState(states, "UpperBody Combat", "Whirlwind_v5", true, errors);
        RequireState(states, "LowerBody Combat", "LowerBody_Empty", false, errors);
        RequireState(states, "LowerBody Combat", "Lower_Saber_A_v5", true, errors);
        RequireState(states, "LowerBody Combat", "Lower_Saber_B_v5", true, errors);
        RequireState(states, "LowerBody Combat", "Lower_Whirlwind_v5", true, errors);

        AnimatorState run = FindState(states, "Base Layer", "Run_v5");
        if (run != null)
        {
            if (Mathf.Abs(run.speed - 0.68f) > 0.001f)
                errors.Add("Run_v5 speed must be 0.68, got " + run.speed + ".");
            if (!run.speedParameterActive || run.speedParameter != "LocomotionPlaybackSpeed")
                errors.Add("Run_v5 must use LocomotionPlaybackSpeed.");
            if (!(run.motion is BlendTree))
                errors.Add("Run_v5 must use the directional locomotion blend tree.");
        }

        AnimatorState saberA = FindState(states, "UpperBody Combat", "Saber_A_v5");
        AnimatorState saberB = FindState(states, "UpperBody Combat", "Saber_B_v5");
        if (saberA != null && Mathf.Abs(saberA.speed - 0.92f) > 0.001f)
            errors.Add("Saber_A_v5 speed must be 0.92, got " + saberA.speed + ".");
        if (saberB != null && Mathf.Abs(saberB.speed - 1.20f) > 0.001f)
            errors.Add("Saber_B_v5 speed must be 1.20, got " + saberB.speed + ".");
        if (saberB != null && saberB.motion is AnimationClip saberBClip)
        {
            if (saberBClip.length < 1.55f)
                errors.Add("Saber_B_v5 must include authored recovery through frame 74.");
        }

        AnimatorState whirlwind = FindState(states, "UpperBody Combat", "Whirlwind_v5");
        if (whirlwind != null && Mathf.Abs(whirlwind.speed - 1.55f) > 0.001f)
            errors.Add("Whirlwind_v5 speed must be 1.55, got " + whirlwind.speed + ".");

        report.animationClips = states.Values
            .Select(state => state.motion)
            .OfType<AnimationClip>()
            .Select(clip => clip.name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
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

        report.requiredMixamoFiles = RequiredMixamoFiles.ToArray();
        var missing = new List<string>();
        var authoredChecks = new List<string>();

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absoluteFolder = Path.Combine(
            projectRoot, MixamoFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(absoluteFolder))
        {
            report.mixamoImporterCount = 0;
            report.missingMixamoFiles = RequiredMixamoFiles.ToArray();
            errors.Add("Pelag Mixamo folder is missing: " + MixamoFolder + ".");
            return;
        }

        string[] files = Directory.GetFiles(absoluteFolder, "*.fbx", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        report.mixamoImporterCount = files.Length;
        if (report.mixamoImporterCount != ExpectedMixamoImporterCount)
            errors.Add("Expected " + ExpectedMixamoImporterCount +
                       " Mixamo FBX deliveries, got " + report.mixamoImporterCount + ".");

        var expected = new HashSet<string>(RequiredMixamoFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string file in RequiredMixamoFiles)
        {
            string path = MixamoFolder + "/" + file;
            string absolutePath = Path.Combine(absoluteFolder, file);
            if (!File.Exists(absolutePath))
            {
                missing.Add(file);
                errors.Add("Required Pelag Mixamo clip is missing: " + path);
                continue;
            }

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                errors.Add("Mixamo clip importer is missing: " + path);
                continue;
            }

            if (importer.animationType != ModelImporterAnimationType.Generic)
                errors.Add("Mixamo clip is not a Generic animation importer: " + path);
            if (!importer.importAnimation)
                errors.Add("Mixamo clip has animation import disabled: " + path);
            else
                report.genericMixamoImporters++;

            ValidateImporterClips(file, importer, authoredChecks, errors);
        }

        foreach (string file in files)
            if (!expected.Contains(file))
                errors.Add("Unexpected FBX in Pelag Mixamo folder: " + file);

        report.missingMixamoFiles = missing.ToArray();
        report.authoredClipChecks = authoredChecks.ToArray();
    }

    private static void ValidateImporterClips(string file, ModelImporter importer,
        List<string> checks, List<string> errors)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            errors.Add("Mixamo importer has no configured clip: " + file);
            return;
        }

        // Every take, including the legacy 11 clips, must keep entity position
        // under Simulation. The importer exposes these as the two Bake Into Pose
        // flags plus their Based Upon companions.
        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            if (!clip.lockRootPositionXZ || !clip.keepOriginalPositionXZ
                || !clip.lockRootHeightY || !clip.keepOriginalPositionY)
            {
                errors.Add("Mixamo clip does not bake root position into pose: " +
                           file + "/" + clip.name + ".");
            }
        }

        for (int i = 0; i < AuthoredClipContracts.Length; i++)
        {
            ClipContract contract = AuthoredClipContracts[i];
            if (!string.Equals(contract.file, file, StringComparison.OrdinalIgnoreCase)) continue;

            ModelImporterClipAnimation configured = clips.FirstOrDefault(clip =>
                string.Equals(clip.name, contract.clip, StringComparison.Ordinal));
            if (configured == null)
            {
                errors.Add("Configured clip is missing: " + file + "/" + contract.clip + ".");
                continue;
            }

            if (Mathf.Abs(configured.firstFrame - contract.firstFrame) > 0.01f
                || Mathf.Abs(configured.lastFrame - contract.lastFrame) > 0.01f)
            {
                errors.Add("Unexpected frame range for " + file + "/" + contract.clip +
                           ": got " + configured.firstFrame.ToString("0.##") + "-" +
                           configured.lastFrame.ToString("0.##") + ", expected " +
                           contract.firstFrame.ToString("0.##") + "-" +
                           contract.lastFrame.ToString("0.##") + ".");
            }
            if (configured.loopTime != contract.loop)
                errors.Add("Unexpected loop flag for " + file + "/" + contract.clip + ".");

            AnimationClip imported = AssetDatabase.LoadAllAssetsAtPath(MixamoFolder + "/" + file)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => string.Equals(clip.name, contract.clip,
                    StringComparison.Ordinal));
            if (imported == null)
            {
                errors.Add("Imported AnimationClip subasset is missing: " +
                           file + "/" + contract.clip + ".");
            }
            else
            {
                if (imported.length < contract.minLength || imported.length > contract.maxLength)
                    errors.Add("Unexpected duration for " + file + "/" + contract.clip +
                               ": got " + imported.length.ToString("0.###") + " s.");
                if (contract.loop != imported.isLooping)
                    errors.Add("Imported loop state disagrees for " + file + "/" + contract.clip + ".");
                checks.Add(file + "/" + contract.clip + ": " +
                           imported.length.ToString("0.###") + " s @ " +
                           imported.frameRate.ToString("0.##") + " fps");
            }
        }
    }

    private static void RequireParameter(AnimatorController controller, string name,
        AnimatorControllerParameterType expectedType, List<string> errors)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!string.Equals(parameters[i].name, name, StringComparison.Ordinal)) continue;
            if (parameters[i].type != expectedType)
                errors.Add("Controller parameter has wrong type: " + name +
                           " (expected " + expectedType + ").");
            return;
        }
        errors.Add("Controller parameter is missing: " + name);
    }

    private static void CollectStates(AnimatorStateMachine machine, string layerName,
        string parentPath, Dictionary<string, AnimatorState> states)
    {
        ChildAnimatorState[] children = machine.states;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].state == null) continue;
            string statePath = string.IsNullOrEmpty(parentPath)
                ? layerName + "." + children[i].state.name
                : layerName + "." + parentPath + "." + children[i].state.name;
            states[statePath] = children[i].state;
        }

        ChildAnimatorStateMachine[] childMachines = machine.stateMachines;
        for (int i = 0; i < childMachines.Length; i++)
        {
            if (childMachines[i].stateMachine == null) continue;
            string childPath = string.IsNullOrEmpty(parentPath)
                ? childMachines[i].stateMachine.name
                : parentPath + "." + childMachines[i].stateMachine.name;
            CollectStates(childMachines[i].stateMachine, layerName, childPath, states);
        }
    }

    private static AnimatorState FindState(Dictionary<string, AnimatorState> states,
        string layerName, string stateName)
    {
        string prefix = layerName + ".";
        foreach (KeyValuePair<string, AnimatorState> pair in states)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            int lastDot = pair.Key.LastIndexOf('.');
            if (lastDot >= 0 && string.Equals(pair.Key.Substring(lastDot + 1), stateName,
                StringComparison.Ordinal))
                return pair.Value;
        }
        return null;
    }

    private static void RequireState(Dictionary<string, AnimatorState> states,
        string layerName, string stateName, bool requireMotion, List<string> errors)
    {
        AnimatorState state = FindState(states, layerName, stateName);
        if (state == null)
        {
            errors.Add("Controller state is missing: " + layerName + "." + stateName);
            return;
        }
        if (requireMotion && state.motion == null)
            errors.Add("Controller state has no motion: " + layerName + "." + stateName);
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
