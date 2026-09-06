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
        "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx";
    private const string MixamoFolder =
        "Assets/Resources/Characters/Pelag_v5/Mixamo";
    private const string ControllerPath =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller";
    private const string BaseLayerName = "Base Layer";
    private const string LegacyIdleStateName = "Idle_v5";
    private const string RelaxedIdleStateName = "RelaxedIdle_v5";
    private const string CombatIdleStateName = "CombatIdle_v5";
    private const string RelaxedParameterName = "Relaxed";
    private const float RelaxedIdleStateSpeed = 0.88f;
    private const float CombatIdleStateSpeed = 1.00f;

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
        // Граница 25 общая с клипом A и держит стык связки — двигать нельзя,
        // история попытки в RazlomCharacterImport.
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
        // Bones carrying vertex weights. Informational: Mixamo leaves the leaf
        // bones unweighted, so this sits below the rig count and says nothing
        // about whether clips will play.
        public int boneCount;
        // Transforms named mixamorig:*. THIS is what Generic clips bind to.
        public int rigBoneCount;
        public int triangleCount;
        // Height of the bind pose in the model's own units. WoleScale is
        // derived from it (1.78 / meshHeight), so a body that arrives at a
        // different scale is a number to read here, not a guess.
        public float meshHeight;
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
        // Насколько клинок расходится с тиком урона у каждой атаки. Не ошибка:
        // с нынешней нарезкой свести их можно только замедлением, а оно уже
        // пробовалось и откатывалось. Число печатается, чтобы промах был
        // виден и не забывался до перерезки клипов.
        public string[] contactSyncNotes;
        public int baseColorWidth;
        public int baseColorHeight;
        public bool toonShaderFound;
        public bool weaponLoaded;
        public float weaponLengthMeters;
        public string[] errors;
        public bool passed;
    }

    [MenuItem("Разлом/Проверить Pelag v6 runtime")]
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

        // The whole report goes to the console, not just the flag. The flag
        // was enough for CI; a person at the screen needs the numbers.
        Debug.Log("[Razlom] " + RuntimePath + "\n" + JsonUtility.ToJson(report, true));
        Debug.Log("RAZLOM_PELAG_V5_RUNTIME_PASSED=" + report.passed);
        if (!report.passed) throw new Exception(string.Join(" | ", errors));
    }

    private static void ValidateRuntimeModel(Report report, List<string> errors)
    {
        GameObject prefab = Resources.Load<GameObject>(
            "Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig");
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
        // WHAT MATTERS IS THE HIERARCHY, NOT THE SKIN.
        //
        // Generic clips bind by transform path, so a clip plays as long as the
        // 65 mixamorig transforms exist. renderer.bones only lists the ones
        // carrying vertex weights, and Mixamo leaves the 13 leaf bones
        // unweighted - ten finger tips plus HeadTop_End and the two Toe_End.
        // The v6 body reports 52 there and animates perfectly; the old
        // equality on that number called a healthy rig broken.
        report.rigBoneCount = prefab.GetComponentsInChildren<Transform>(true)
            .Count(bone => bone.name.StartsWith("mixamorig:"));
        if (report.rigBoneCount != 65)
            errors.Add("Expected 65 mixamorig transforms, got " + report.rigBoneCount + ".");
        if (report.triangleCount < 5000 || report.triangleCount > 80000)
            errors.Add("Runtime triangles out of range: " + report.triangleCount + ".");

        report.meshHeight = renderers.Length == 0 ? 0f : renderers.Max(
            renderer => renderer.sharedMesh != null ? renderer.sharedMesh.bounds.size.y : 0f);

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
        RequireParameter(controller, RelaxedParameterName, AnimatorControllerParameterType.Bool, errors);
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
        RequireState(states, BaseLayerName, LegacyIdleStateName, true, errors);
        RequireState(states, BaseLayerName, RelaxedIdleStateName, true, errors);
        RequireState(states, BaseLayerName, CombatIdleStateName, true, errors);
        RequireState(states, BaseLayerName, "Run_v5", true, errors);
        RequireState(states, BaseLayerName, "RunStart_v5", true, errors);
        RequireState(states, BaseLayerName, "RunStop_v5", true, errors);
        RequireState(states, BaseLayerName, "TurnLeft_v5", true, errors);
        RequireState(states, BaseLayerName, "TurnRight_v5", true, errors);
        RequireState(states, BaseLayerName, "Anchor_v5", true, errors);
        RequireState(states, BaseLayerName, "AnchorLeap_v5", true, errors);
        RequireState(states, BaseLayerName, "AnchorSweep_v5", true, errors);
        RequireState(states, BaseLayerName, "ChainStep_v5", true, errors);
        RequireState(states, BaseLayerName, "Hit_v5", true, errors);
        RequireState(states, BaseLayerName, "Death_v5", true, errors);
        RequireState(states, "UpperBody Combat", "UpperBody_Empty", false, errors);
        RequireState(states, "UpperBody Combat", "Saber_A_v5", true, errors);
        RequireState(states, "UpperBody Combat", "Saber_B_v5", true, errors);
        RequireState(states, "UpperBody Combat", "Whirlwind_v5", true, errors);
        RequireState(states, "LowerBody Combat", "LowerBody_Empty", false, errors);
        RequireState(states, "LowerBody Combat", "Lower_Saber_A_v5", true, errors);
        RequireState(states, "LowerBody Combat", "Lower_Saber_B_v5", true, errors);
        RequireState(states, "LowerBody Combat", "Lower_Whirlwind_v5", true, errors);

        ValidateIdleContract(controller, states, errors);

        AnimatorState run = FindState(states, "Base Layer", "Run_v5");
        if (run != null)
        {
            if (Mathf.Abs(run.speed - 0.96f) > 0.001f)
                errors.Add("Run_v5 speed must be 0.96, got " + run.speed + ".");
            if (!run.speedParameterActive || run.speedParameter != "LocomotionPlaybackSpeed")
                errors.Add("Run_v5 must use LocomotionPlaybackSpeed.");
            if (!(run.motion is BlendTree))
                errors.Add("Run_v5 must use the directional locomotion blend tree.");
        }

        // Перекрывающий слой с маской и включённым Write Defaults пишет позу
        // покоя рига поверх всего, что попало в маску. У «UpperBody Combat»
        // вес 1 и рантайм его не опускает — с Write Defaults это означало
        // замершие руки в беге и прыжок из позы покоя в середину удара.
        RequireWriteDefaultsOff(states, "UpperBody Combat", errors);
        RequireWriteDefaultsOff(states, "LowerBody Combat", errors);
        RequireDrivenLayerWeight(controller, "UpperBody Combat", errors);
        RequireDrivenLayerWeight(controller, "LowerBody Combat", errors);

        // Derived strokes share a 0.3 s contact and a complete 0.64 s recovery.
        var syncNotes = new List<string>();
        AnimatorState saberA = FindState(states, "UpperBody Combat", "Saber_A_v5");
        AnimatorState saberB = FindState(states, "UpperBody Combat", "Saber_B_v5");
        CheckAttackState(saberA, "Saber_A_v5", 1f, 18f, errors, syncNotes);
        CheckAttackState(saberB, "Saber_B_v5", 1f, 18f, errors, syncNotes);
        report.contactSyncNotes = syncNotes.ToArray();
        if (saberB != null && saberB.motion is AnimationClip saberBClip)
        {
            if (Mathf.Abs(saberBClip.length - Game.View.CharacterAnimatorView.BasicAttackClipDuration) > 0.002f)
                errors.Add("Saber_B_v5 must contain the complete retimed 0.64-second stroke.");
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

    // Validate the shared contact of the derived clips at their normal speed.
    private static void CheckAttackState(AnimatorState state, string stateName,
        float expectedSpeed, float contactFrame, List<string> errors, List<string> notes)
    {
        if (state == null) return;

        if (Mathf.Abs(state.speed - expectedSpeed) > 0.005f)
            errors.Add(stateName + " speed must be " + expectedSpeed.ToString("F2")
                       + ", got " + state.speed + ".");

        if (!(state.motion is AnimationClip clip)) return;
        int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
        if (contactFrame >= frames)
        {
            errors.Add(stateName + ": контакт назначен на кадр " + contactFrame
                       + ", а в клипе " + frames + " кадров — клип перерезали, "
                       + "а число контакта не пересчитали.");
            return;
        }

        float frameAtDamage = (Game.Sim.Simulation.AttackWindupTicks / (float)Game.Sim.Simulation.TicksPerSecond) * clip.frameRate * state.speed;
        float missFrames = frameAtDamage - contactFrame;
        if (Mathf.Abs(missFrames) > 0.01f)
            errors.Add(stateName + ": derived contact is not synchronized with simulation damage.");
        if (Mathf.Abs(clip.length - Game.View.CharacterAnimatorView.BasicAttackClipDuration) > 0.002f)
            errors.Add(stateName + ": expected complete 0.64 s stroke.");
        notes.Add(stateName + ": contact at " + (contactFrame / clip.frameRate).ToString("F3")
            + " s; timing error " + (missFrames / clip.frameRate).ToString("F4") + " s.");
    }

    /// <summary>
    /// Перекрывающий слой с маской обязан стартовать с нулевым весом: им
    /// управляет CharacterAnimatorView.
    ///
    /// При весе 1 пустое состояние такого слоя не отдаёт кости базовому слою.
    /// Замер на живом контроллере: правая кисть в idle (0.179, 0.467, 0.036),
    /// во время удара (-0.210, 0.580, 0.140), в пустом состоянии при весе 1 —
    /// (-0.208, 0.579, 0.139), то есть поза удара, а при весе 0 — (0.177,
    /// 0.458, 0.040), то есть idle. Отпускает только вес. Ровно так Пелаг
    /// замирал с вытянутой саблей после последнего удара.
    /// </summary>
    private static void RequireDrivenLayerWeight(AnimatorController controller,
        string layerName, List<string> errors)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (!string.Equals(layers[i].name, layerName, StringComparison.Ordinal)) continue;
            if (layers[i].defaultWeight > 0.001f)
                errors.Add("Слой «" + layerName + "» обязан стартовать с весом 0: вес "
                           + "ведёт CharacterAnimatorView, а вес 1 замораживает кости "
                           + "маски в последней записанной позе. Сейчас "
                           + layers[i].defaultWeight.ToString("F2") + ".");
            return;
        }
    }

    /// <summary>
    /// Пустое состояние перекрывающего слоя с Write Defaults пишет позу покоя
    /// рига на все кости маски. Режимы внутри слоя смешивать тоже нельзя,
    /// поэтому проверяется весь слой целиком.
    /// </summary>
    private static void RequireWriteDefaultsOff(Dictionary<string, AnimatorState> states,
        string layerName, List<string> errors)
    {
        string prefix = layerName + ".";
        foreach (KeyValuePair<string, AnimatorState> pair in states)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!pair.Value.writeDefaultValues) continue;
            errors.Add("Write Defaults обязан быть выключен на " + pair.Key
                       + ": слой перекрывающий и с маской.");
        }
    }

    private static void ValidateIdleContract(AnimatorController controller,
        Dictionary<string, AnimatorState> states, List<string> errors)
    {
        AnimatorState legacy = FindState(states, BaseLayerName, LegacyIdleStateName);
        AnimatorState relaxed = FindState(states, BaseLayerName, RelaxedIdleStateName);
        AnimatorState combat = FindState(states, BaseLayerName, CombatIdleStateName);

        RequireCompatibleIdleMotion(legacy, LegacyIdleStateName, errors);
        RequireCompatibleIdleMotion(relaxed, RelaxedIdleStateName, errors);
        RequireCompatibleIdleMotion(combat, CombatIdleStateName, errors);

        if (legacy != null && relaxed != null && legacy.motion != relaxed.motion)
            errors.Add(LegacyIdleStateName + " and " + RelaxedIdleStateName
                       + " must use the same Pelag_MX_Idle clip.");
        if (legacy != null && combat != null && legacy.motion != combat.motion)
            errors.Add(LegacyIdleStateName + " and " + CombatIdleStateName
                       + " must use the same Pelag_MX_Idle clip.");

        if (relaxed != null && Mathf.Abs(relaxed.speed - RelaxedIdleStateSpeed) > 0.005f)
            errors.Add(RelaxedIdleStateName + " speed must be "
                       + RelaxedIdleStateSpeed.ToString("F2") + ", got " + relaxed.speed + ".");
        if (combat != null && Mathf.Abs(combat.speed - CombatIdleStateSpeed) > 0.005f)
            errors.Add(CombatIdleStateName + " speed must be "
                       + CombatIdleStateSpeed.ToString("F2") + ", got " + combat.speed + ".");

        AnimatorControllerParameter relaxedParameter = controller.parameters
            .FirstOrDefault(parameter => string.Equals(parameter.name,
                RelaxedParameterName, StringComparison.Ordinal));
        if (relaxedParameter != null && !relaxedParameter.defaultBool)
            errors.Add("Controller parameter " + RelaxedParameterName
                       + " must default to true for the relaxed spawn posture.");

        AnimatorStateMachine baseMachine = null;
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (!string.Equals(layers[i].name, BaseLayerName, StringComparison.Ordinal)) continue;
            baseMachine = layers[i].stateMachine;
            break;
        }
        if (baseMachine == null || baseMachine.defaultState == null
            || !string.Equals(baseMachine.defaultState.name, RelaxedIdleStateName,
                StringComparison.Ordinal))
        {
            errors.Add("Base Layer default state must be " + RelaxedIdleStateName + ".");
        }

        RequireTransition(states, BaseLayerName, RelaxedIdleStateName,
            CombatIdleStateName, RelaxedParameterName, AnimatorConditionMode.IfNot,
            requireNoExitTime: true, errors: errors);
        RequireTransition(states, BaseLayerName, CombatIdleStateName,
            RelaxedIdleStateName, RelaxedParameterName, AnimatorConditionMode.If,
            requireNoExitTime: true, errors: errors);
        // The legacy state can still be entered by older callers. It must hand
        // control to the same mode-specific states instead of trapping the
        // character in a third idle variant.
        RequireTransition(states, BaseLayerName, LegacyIdleStateName,
            RelaxedIdleStateName, RelaxedParameterName, AnimatorConditionMode.If,
            requireNoExitTime: true, errors: errors);
        RequireTransition(states, BaseLayerName, LegacyIdleStateName,
            CombatIdleStateName, RelaxedParameterName, AnimatorConditionMode.IfNot,
            requireNoExitTime: true, errors: errors);

        // Locomotion and full-body actions must never fall back to the legacy
        // idle or choose a mode at random. Their exits carry the same Relaxed
        // branch as the idle switch, so a presentation-only combat toggle is
        // honoured even when it changes during a stop/recovery.
        RequireModeExit(states, "RunStop_v5", RelaxedIdleStateName, true,
            requireMoveLess: false, requireExitTime: true, errors: errors);
        RequireModeExit(states, "RunStop_v5", CombatIdleStateName, false,
            requireMoveLess: false, requireExitTime: true, errors: errors);
        RequireModeExit(states, "TurnLeft_v5", RelaxedIdleStateName, true,
            requireMoveLess: true, requireExitTime: false, errors: errors);
        RequireModeExit(states, "TurnLeft_v5", CombatIdleStateName, false,
            requireMoveLess: true, requireExitTime: false, errors: errors);
        RequireModeExit(states, "TurnRight_v5", RelaxedIdleStateName, true,
            requireMoveLess: true, requireExitTime: false, errors: errors);
        RequireModeExit(states, "TurnRight_v5", CombatIdleStateName, false,
            requireMoveLess: true, requireExitTime: false, errors: errors);

        string[] fullBodyActions = { "Anchor_v5", "AnchorLeap_v5", "AnchorSweep_v5",
            "ChainStep_v5", "Hit_v5" };
        for (int i = 0; i < fullBodyActions.Length; i++)
        {
            RequireModeExit(states, fullBodyActions[i], RelaxedIdleStateName, true,
                requireMoveLess: true, requireExitTime: true, errors: errors);
            RequireModeExit(states, fullBodyActions[i], CombatIdleStateName, false,
                requireMoveLess: true, requireExitTime: true, errors: errors);
        }
    }

    private static void RequireCompatibleIdleMotion(AnimatorState state, string stateName,
        List<string> errors)
    {
        if (state == null || !(state.motion is AnimationClip clip)) return;

        string path = AssetDatabase.GetAssetPath(clip);
        if (!string.Equals(clip.name, "Pelag_MX_Idle", StringComparison.Ordinal)
            || !string.Equals(path, MixamoFolder + "/Pelag_MX_Idle.fbx",
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(stateName + " must use the compatible Generic clip "
                       + MixamoFolder + "/Pelag_MX_Idle.fbx/Pelag_MX_Idle.");
        }
    }

    private static void RequireTransition(Dictionary<string, AnimatorState> states,
        string layerName, string fromName, string toName, string parameter,
        AnimatorConditionMode mode, bool requireNoExitTime, List<string> errors)
    {
        AnimatorState source = FindState(states, layerName, fromName);
        if (source == null) return;

        AnimatorStateTransition[] transitions = source.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition == null || transition.destinationState == null
                || !string.Equals(transition.destinationState.name, toName,
                    StringComparison.Ordinal))
                continue;
            if (requireNoExitTime && transition.hasExitTime) continue;

            AnimatorCondition[] conditions = transition.conditions;
            for (int c = 0; c < conditions.Length; c++)
            {
                AnimatorCondition condition = conditions[c];
                if (string.Equals(condition.parameter, parameter, StringComparison.Ordinal)
                    && condition.mode == mode)
                    return;
            }
        }

        string modeText = mode == AnimatorConditionMode.IfNot ? "false" : "true";
        errors.Add("Missing mode transition: " + layerName + "." + fromName + " -> "
                   + toName + " when " + parameter + " is " + modeText + ".");
    }

    private static void RequireModeExit(Dictionary<string, AnimatorState> states,
        string fromName, string toName, bool relaxed, bool requireMoveLess,
        bool requireExitTime, List<string> errors)
    {
        AnimatorState source = FindState(states, BaseLayerName, fromName);
        if (source == null) return;

        AnimatorStateTransition[] transitions = source.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition == null || transition.destinationState == null
                || !string.Equals(transition.destinationState.name, toName,
                    StringComparison.Ordinal)
                || transition.hasExitTime != requireExitTime)
                continue;

            bool modeFound = false;
            bool moveFound = !requireMoveLess;
            AnimatorCondition[] conditions = transition.conditions;
            for (int c = 0; c < conditions.Length; c++)
            {
                AnimatorCondition condition = conditions[c];
                if (string.Equals(condition.parameter, RelaxedParameterName,
                        StringComparison.Ordinal)
                    && condition.mode == (relaxed ? AnimatorConditionMode.If
                        : AnimatorConditionMode.IfNot))
                    modeFound = true;
                if (requireMoveLess
                    && string.Equals(condition.parameter, "MoveSpeed",
                        StringComparison.Ordinal)
                    && condition.mode == AnimatorConditionMode.Less
                    && condition.threshold < 0.1001f)
                    moveFound = true;
            }

            if (modeFound && moveFound) return;
        }

        string modeText = relaxed ? "true" : "false";
        string moveText = requireMoveLess ? " and MoveSpeed < 0.1" : string.Empty;
        string timingText = requireExitTime ? " with exit time" : " without exit time";
        errors.Add("Missing mode-aware exit: " + BaseLayerName + "." + fromName + " -> "
                   + toName + " when " + RelaxedParameterName + " is " + modeText
                   + moveText + timingText + ".");
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
