using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds Pelag's gameplay controller exclusively from the approved v5 clips.
/// The generated controller is disposable; the FBXs and this recipe are truth.
/// </summary>
public static class RazlomPelagV5AnimatorBuilder
{
    private const string Folder = "Assets/Resources/Characters/Pelag_v5/Mixamo";
    private const string Output =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller";
    private const string UpperBodyMaskOutput =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_UpperBody.mask";
    private const string LowerBodyMaskOutput =
        "Assets/Resources/Characters/Pelag_v5/Pelag_v5_LowerBodyCombat.mask";
    private const string UpperBodyLayerName = "UpperBody Combat";
    private const string LowerBodyLayerName = "LowerBody Combat";
    private const string AttackPlaybackSpeed = "AttackPlaybackSpeed";
    private const string LocomotionPlaybackSpeed = "LocomotionPlaybackSpeed";
    private const string AbilityPlaybackSpeed = "AbilityPlaybackSpeed";
    private const string MoveX = "MoveX";
    private const string MoveY = "MoveY";
    private const string AutoBuildSessionKey = "Razlom.PelagV5Animator.AutoBuild.v15.AnchorClips";
    private const float RelaxedIdleStateSpeed = 0.92f;
    private const float CombatIdleStateSpeed = 1.08f;
    private const float IdleTransitionDuration = 0.15f;

    // Derived clips contain their own timing: contact at 0.3 s, full recovery
    // by 0.64 s. Both masked layers play that shared timeline at speed 1.
    private const float SaberAStateSpeed = 1f;
    private const float SaberBStateSpeed = 1f;

    [InitializeOnLoadMethod]
    private static void AutoBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            // Build force-imports the source FBXs. Without a per-editor-session
            // guard that import schedules another domain reload and another
            // Build, leaving the editor in an endless Importing loop.
            if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
            SessionState.SetBool(AutoBuildSessionKey, true);
            Build();
        };
    }

    [MenuItem("Разлом/Собрать Pelag v5 — Mixamo controller")]
    public static void Build()
    {
        // Clip ranges/loop flags are authored by RazlomCharacterImport. Force
        // the generated deliveries through that recipe before reading them;
        // otherwise a stale Library can silently retain the old 60 fps cuts.
        ForceImport(
            "Pelag_MX_Idle.fbx",
            "Pelag_MX_Run.fbx",
            "Pelag_MX_TurnLeft.fbx",
            "Pelag_MX_TurnRight.fbx",
            "Pelag_MX_InjuredRun.fbx",
            "Pelag_MX_Hit.fbx",
            "Pelag_MX_Death.fbx",
            "Pelag_MX_Whirlwind.fbx",
            "Pelag_MX_AnchorAttack.fbx",
            "Pelag_MX_SaberCombo.fbx",
            "Pelag_MX_DualCombo.fbx",
            "Pelag_MX_AnchorLeap.fbx",
            "Pelag_MX_AnchorSweep.fbx",
            "Pelag_MX_ChainStep.fbx",
            "Pelag_MX_RunStart.fbx",
            "Pelag_MX_RunStop.fbx",
            "Pelag_MX_StrafeLeft.fbx",
            "Pelag_MX_StrafeRight.fbx",
            "Pelag_MX_StrafeBack.fbx");

        AnimationClip idle = Load("Pelag_MX_Idle.fbx", "Pelag_MX_Idle");
        AnimationClip run = Load("Pelag_MX_Run.fbx", "Pelag_MX_Run");
        AnimationClip runStart = Load("Pelag_MX_RunStart.fbx", "Pelag_MX_RunStart");
        AnimationClip runStop = Load("Pelag_MX_RunStop.fbx", "Pelag_MX_RunStop");
        AnimationClip strafeLeft = Load("Pelag_MX_StrafeLeft.fbx", "Pelag_MX_StrafeLeft");
        AnimationClip strafeRight = Load("Pelag_MX_StrafeRight.fbx", "Pelag_MX_StrafeRight");
        AnimationClip strafeBack = Load("Pelag_MX_StrafeBack.fbx", "Pelag_MX_StrafeBack");
        AnimationClip turnLeft = Load("Pelag_MX_TurnLeft.fbx", "Pelag_MX_TurnLeft");
        AnimationClip turnRight = Load("Pelag_MX_TurnRight.fbx", "Pelag_MX_TurnRight");
        turnLeft = RazlomPelagTurnClips.Build(turnLeft, "Pelag_TurnLeft_InPlace", -1f);
        turnRight = RazlomPelagTurnClips.Build(turnRight, "Pelag_TurnRight_InPlace", 1f);
        AnimationClip attackA = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackA");
        AnimationClip attackB = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackB");
        attackA = RazlomPelagSaberClips.Build(attackA, "Pelag_Saber_A_Timed", 9f / 30f);
        attackB = RazlomPelagSaberClips.Build(attackB, "Pelag_Saber_B_Timed", 18f / 30f);
        AnimationClip whirlwind = Load("Pelag_MX_Whirlwind.fbx", "Pelag_MX_Whirlwind");
        whirlwind = RazlomPelagWhirlwindClip.Build(whirlwind);
        AnimationClip anchor = Load("Pelag_MX_AnchorAttack.fbx", "Pelag_MX_AnchorAttack");
        AnimationClip anchorLeap = Load("Pelag_MX_AnchorLeap.fbx", "Pelag_MX_AnchorLeap");
        AnimationClip anchorSweep = Load("Pelag_MX_AnchorSweep.fbx", "Pelag_MX_AnchorSweep");
        AnimationClip chainStep = Load("Pelag_MX_ChainStep.fbx", "Pelag_MX_ChainStep");
        anchorLeap = RazlomPelagAnchorClips.Build(idle, true);
        anchorSweep = RazlomPelagAnchorClips.Build(idle, false);
        chainStep = RazlomPelagWhirlwindClip.BuildTimed(attackA, "Pelag_ChainStep_Timed",
            Game.View.PelagAbilityTiming.ChainHop + 0.07f,
            new AnimationCurve(new Keyframe(0f, 0.12f),
                new Keyframe(Game.View.PelagAbilityTiming.ChainHop, 0.30f),
                new Keyframe(Game.View.PelagAbilityTiming.ChainHop + 0.07f, attackA.length)), 0.45f);
        AnimationClip chainStepB = RazlomPelagWhirlwindClip.BuildTimed(attackB, "Pelag_ChainStep_B_Timed",
            Game.View.PelagAbilityTiming.ChainHop + 0.07f,
            new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(Game.View.PelagAbilityTiming.ChainHop, 0.30f),
                new Keyframe(Game.View.PelagAbilityTiming.ChainHop + 0.07f, attackB.length)), 0.45f);
        AnimationClip hit = Load("Pelag_MX_Hit.fbx", "Pelag_MX_Hit");
        AnimationClip death = Load("Pelag_MX_Death.fbx", "Pelag_MX_Death");

        AnimationClip[] required =
        {
            idle, run, runStart, runStop, strafeLeft, strafeRight, strafeBack,
            turnLeft, turnRight, attackA, attackB, whirlwind, anchor,
            anchorLeap, anchorSweep, chainStep, hit, death
        };
        if (required.Any(clip => clip == null))
        {
            Debug.LogError("[Разлом] Pelag v5 controller не собран: не все Mixamo-клипы импортированы.");
            return;
        }

        AvatarMask upperBodyMask = BuildUpperBodyMask(attackA, attackB, whirlwind);
        if (upperBodyMask == null)
        {
            Debug.LogError("[Разлом] Pelag v5 controller не собран: нет upper-body mask.");
            return;
        }
        AvatarMask lowerBodyMask = BuildLowerBodyMask(attackA, attackB, whirlwind);
        if (lowerBodyMask == null)
        {
            Debug.LogError("[Разлом] Pelag v5 controller не собран: нет lower-body combat mask.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Output) != null)
            AssetDatabase.DeleteAsset(Output);

        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(Output);
        AddParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, MoveX, AnimatorControllerParameterType.Float);
        AddParameter(controller, MoveY, AnimatorControllerParameterType.Float);
        AddParameter(controller, "TurnDirection", AnimatorControllerParameterType.Float);
        AddParameter(controller, "TurnPhase", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Relaxed", AnimatorControllerParameterType.Bool, defaultBool: true);
        AddParameter(controller, "Stunned", AnimatorControllerParameterType.Bool);
        AddParameter(controller, LocomotionPlaybackSpeed, AnimatorControllerParameterType.Float);
        AddParameter(controller, AttackPlaybackSpeed, AnimatorControllerParameterType.Float);
        AddParameter(controller, AbilityPlaybackSpeed, AnimatorControllerParameterType.Float);
        AddParameter(controller, "AttackA", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "AttackB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerAttackA", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerAttackB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerHeavyAttack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HeavyAttack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Hook", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "AnchorLeap", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "ChainStepB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "AnchorSweep", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "ChainStep", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HitFront", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Death", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        // Keep the legacy state for callers that still address Base Layer.Idle_v5.
        // The two mode-specific states deliberately share this compatible
        // Generic Mixamo clip; equipment is what changes between the modes.
        // Slightly different rates keep a mode switch from looking like a
        // perfectly synchronized loop restart while preserving the authored
        // motion and avoiding a second (incompatible Humanoid) idle asset.
        AnimatorState idleState = State(machine, "Idle_v5", idle, 1.35f);
        AnimatorState relaxedIdleState = State(
            machine, "RelaxedIdle_v5", idle, RelaxedIdleStateSpeed);
        AnimatorState combatIdleState = State(
            machine, "CombatIdle_v5", idle, CombatIdleStateSpeed);
        // CharacterAnimatorView sets cadence from measured clip ground speed.
        // An extra state multiplier would invalidate that calibration.
        BlendTree directionalLocomotion = BuildDirectionalLocomotion(
            controller, run, strafeLeft, strafeRight, strafeBack);
        AnimatorState runState = State(machine, "Run_v5", directionalLocomotion, 1f);
        // During a committed attack/ability Simulation deliberately caps the
        // body at 50% movement speed. Retiming only Run_v5 to the same ratio
        // keeps the planted foot attached to the ground while the upper-body
        // action retains its authored timing.
        runState.speedParameterActive = true;
        runState.speedParameter = LocomotionPlaybackSpeed;
        AnimatorState runStartState = State(machine, "RunStart_v5", runStart, 1f);
        AnimatorState runStopState = State(machine, "RunStop_v5", runStop, 1f);
        AnimatorState turnLeftState = State(machine, "TurnLeft_v5", turnLeft, 1f);
        AnimatorState turnRightState = State(machine, "TurnRight_v5", turnRight, 1f);
        turnLeftState.timeParameterActive = turnRightState.timeParameterActive = true;
        turnLeftState.timeParameter = turnRightState.timeParameter = "TurnPhase";
        // Relaxed is the spawn/camp default. CharacterAnimatorView can flip the
        // bool presentation-only when a threat or committed action appears.
        machine.defaultState = relaxedIdleState;

        // The generated start/stop clips mix unrelated gait phases. Blend from
        // the actual current pose instead of jumping to a fixed mid-stride cut.
        ConfigureIdleTransitions(idleState, runState, turnLeftState, turnRightState,
            0.08f, 0.06f);
        ConfigureIdleTransitions(relaxedIdleState, runState, turnLeftState,
            turnRightState, 0.08f, 0.06f);
        ConfigureIdleTransitions(combatIdleState, runState, turnLeftState,
            turnRightState, 0.08f, 0.06f);

        Transition(relaxedIdleState, combatIdleState, "Relaxed", AnimatorConditionMode.IfNot,
            0f, IdleTransitionDuration);
        Transition(combatIdleState, relaxedIdleState, "Relaxed", AnimatorConditionMode.If,
            0f, IdleTransitionDuration);
        // Idle_v5 is retained as a compatibility entry point for older callers;
        // it immediately resolves to the requested mode when forced directly.
        Transition(idleState, relaxedIdleState, "Relaxed", AnimatorConditionMode.If,
            0f, IdleTransitionDuration);
        Transition(idleState, combatIdleState, "Relaxed", AnimatorConditionMode.IfNot,
            0f, IdleTransitionDuration);

        TimedTransition(runStartState, runState, 1f, 0.025f);
        var stopRelaxed = runState.AddTransition(relaxedIdleState);
        stopRelaxed.hasExitTime = false;
        stopRelaxed.hasFixedDuration = true;
        stopRelaxed.duration = 0.12f;
        stopRelaxed.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        stopRelaxed.AddCondition(AnimatorConditionMode.If, 0f, "Relaxed");
        var stopCombat = runState.AddTransition(combatIdleState);
        stopCombat.hasExitTime = false;
        stopCombat.hasFixedDuration = true;
        stopCombat.duration = 0.12f;
        stopCombat.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        stopCombat.AddCondition(AnimatorConditionMode.IfNot, 0f, "Relaxed");
        TimedTransition(runStopState, relaxedIdleState, 1f, 0.035f,
            "Relaxed", AnimatorConditionMode.If);
        TimedTransition(runStopState, combatIdleState, 1f, 0.035f,
            "Relaxed", AnimatorConditionMode.IfNot);
        Transition(runStopState, runStartState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.025f);
        ConfigureTurnExit(turnLeftState, relaxedIdleState, "TurnDirection",
            AnimatorConditionMode.Greater, -0.1f, true, 0.12f);
        ConfigureTurnExit(turnLeftState, combatIdleState, "TurnDirection",
            AnimatorConditionMode.Greater, -0.1f, false, 0.12f);
        ConfigureTurnExit(turnRightState, relaxedIdleState, "TurnDirection",
            AnimatorConditionMode.Less, 0.1f, true, 0.12f);
        ConfigureTurnExit(turnRightState, combatIdleState, "TurnDirection",
            AnimatorConditionMode.Less, 0.1f, false, 0.12f);
        Transition(turnLeftState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnRightState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnLeftState, turnRightState, "TurnDirection", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnRightState, turnLeftState, "TurnDirection", AnimatorConditionMode.Less, -0.1f, 0.08f);

        // Hit/death-style reactions remain full-body. Saber attacks and the
        // moving Whirlwind live on the masked layer below so Run_v5 remains
        // authoritative for the legs instead of sliding an in-place cast.
        Combat(machine, relaxedIdleState, combatIdleState, runState, "Anchor_v5", anchor, "Hook",
            2.65f, 0.04f, 0.88f, 0.10f);
        Combat(machine, relaxedIdleState, combatIdleState, runState, "AnchorLeap_v5", anchorLeap, "AnchorLeap",
            1f, 0.06f, 0.86f, 0.10f);
        Combat(machine, relaxedIdleState, combatIdleState, runState, "AnchorSweep_v5", anchorSweep, "AnchorSweep",
            1f, 0.06f, 0.86f, 0.10f);
        Combat(machine, relaxedIdleState, combatIdleState, runState, "ChainStep_v5", chainStep, "ChainStep",
            1f, 0.025f, 0.98f, 0.07f);
        Combat(machine, relaxedIdleState, combatIdleState, runState, "ChainStep_B_v5", chainStepB, "ChainStepB",
            1f, 0.035f, 0.98f, 0.07f);
        Combat(machine, relaxedIdleState, combatIdleState, runState, "Hit_v5", hit, "HitFront",
            2.45f, 0.02f, 0.90f, 0.08f);

        AnimatorState deathState = State(machine, "Death_v5", death, 2.45f);
        AnimatorStateTransition deathEnter = machine.AddAnyStateTransition(deathState);
        deathEnter.AddCondition(AnimatorConditionMode.If, 0f, "Death");
        deathEnter.hasExitTime = false;
        deathEnter.duration = 0.03f;
        deathEnter.canTransitionToSelf = false;

        BuildUpperBodyLayer(controller, upperBodyMask, attackA, attackB, whirlwind);
        BuildLowerBodyLayer(controller, lowerBodyMask, attackA, attackB, whirlwind);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[Разлом] Pelag v5 controller: locomotion/turn + честная A/B серия: " + Output);
    }

    private static void AddParameter(AnimatorController controller, string name,
        AnimatorControllerParameterType type, bool defaultBool = false)
    {
        controller.AddParameter(name, type);
        if (type != AnimatorControllerParameterType.Bool || !defaultBool) return;

        // AnimatorController.AddParameter defaults bools to false. Explicitly
        // seed Relaxed=true so a freshly rebound Pelag starts in camp posture
        // even before CharacterAnimatorView has had its first Update.
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!string.Equals(parameters[i].name, name, StringComparison.Ordinal)) continue;
            parameters[i].defaultBool = true;
            controller.parameters = parameters;
            return;
        }
    }

    private static AnimationClip BuildAbilityClip(AnimationClip source, string name, float duration, float travel,
        float sourceContact, float heightScale = 1f)
    {
        if (source == null) return null;
        var timing = new AnimationCurve(new Keyframe(0f, 0f),
            new Keyframe(travel, source.length * sourceContact), new Keyframe(duration, source.length));
        return RazlomPelagWhirlwindClip.BuildTimed(source, name, duration, timing, heightScale);
    }

    private static void ForceImport(params string[] files)
    {
        for (int i = 0; i < files.Length; i++)
        {
            string path = Folder + "/" + files[i];
            // File.Exists resolves against the process working directory,
            // which is not guaranteed to be the Unity project root (notably
            // in batch/editor launches). AssetDatabase still needs the
            // project-relative path, so use an absolute path only for the
            // presence check.
            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                Debug.LogWarning("[Разлом] Pelag clip отсутствует, импорт пропущен: " + path);
                continue;
            }
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
    }

    private static BlendTree BuildDirectionalLocomotion(AnimatorController controller,
        AnimationClip run, AnimationClip strafeLeft, AnimationClip strafeRight,
        AnimationClip strafeBack)
    {
        var tree = new BlendTree
        {
            name = "Pelag Directional Locomotion",
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = MoveX,
            blendParameterY = MoveY,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(run, new Vector2(0f, 1f));
        tree.AddChild(strafeLeft, new Vector2(-1f, 0f));
        tree.AddChild(strafeRight, new Vector2(1f, 0f));
        tree.AddChild(strafeBack, new Vector2(0f, -1f));
        return tree;
    }

    private static AvatarMask BuildUpperBodyMask(params AnimationClip[] clips)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (int c = 0; c < clips.Length; c++)
        {
            AnimationClip clip = clips[c];
            if (clip == null) continue;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path;
                if (!string.IsNullOrEmpty(path)
                    && path.IndexOf("mixamorig:Spine", StringComparison.Ordinal) >= 0)
                    paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            Debug.LogError("[Разлом] В saber-клипах не найдены transform-curves выше Spine.");
            return null;
        }

        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskOutput);
        if (mask == null)
        {
            mask = new AvatarMask { name = "Pelag v5 Upper Body" };
            AssetDatabase.CreateAsset(mask, UpperBodyMaskOutput);
        }

        string[] ordered = paths
            .OrderBy(path => path.Count(ch => ch == '/'))
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
        mask.transformCount = ordered.Length;
        for (int i = 0; i < ordered.Length; i++)
        {
            mask.SetTransformPath(i, ordered[i]);
            mask.SetTransformActive(i, true);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static AvatarMask BuildLowerBodyMask(params AnimationClip[] clips)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (int c = 0; c < clips.Length; c++)
        {
            AnimationClip clip = clips[c];
            if (clip == null) continue;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path;
                if (!string.IsNullOrEmpty(path)
                    && path.IndexOf("mixamorig:Hips", StringComparison.Ordinal) >= 0
                    && path.IndexOf("mixamorig:Spine", StringComparison.Ordinal) < 0)
                    paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            Debug.LogError("[Разлом] В боевых клипах не найдены transform-curves таза и ног.");
            return null;
        }

        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LowerBodyMaskOutput);
        if (mask == null)
        {
            mask = new AvatarMask { name = "Pelag v5 Lower Body Combat" };
            AssetDatabase.CreateAsset(mask, LowerBodyMaskOutput);
        }

        string[] ordered = paths
            .OrderBy(path => path.Count(ch => ch == '/'))
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
        mask.transformCount = ordered.Length;
        for (int i = 0; i < ordered.Length; i++)
        {
            mask.SetTransformPath(i, ordered[i]);
            mask.SetTransformActive(i, true);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static void BuildUpperBodyLayer(AnimatorController controller, AvatarMask mask,
        AnimationClip attackA, AnimationClip attackB, AnimationClip whirlwind)
    {
        controller.AddLayer(UpperBodyLayerName);
        AnimatorControllerLayer[] layers = controller.layers;
        int layerIndex = layers.Length - 1;
        AnimatorControllerLayer layer = layers[layerIndex];
        // ВЕС 0, А НЕ 1. Слоем управляет рантайм, ровно как нижним.
        //
        // При весе 1 слой держит верх тела всегда, и его пустое состояние
        // обязано что-то с этими костями делать: с Write Defaults оно писало
        // позу покоя рига, без Write Defaults — навсегда замораживало
        // последнюю позу удара. Оба варианта неверны, и оба были в игре.
        // Правильно — молчащий слой: вес поднимает CharacterAnimatorView на
        // время удара и гасит после. Ноль по умолчанию делает молчание
        // состоянием по умолчанию, а не тем, о чём надо не забыть.
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = mask;
        layers[layerIndex] = layer;
        controller.layers = layers;

        AnimatorStateMachine upper = controller.layers[layerIndex].stateMachine;
        // WRITE DEFAULTS ОБЯЗАН БЫТЬ ВЫКЛЮЧЕН. Слой перекрывающий, вес 1 и
        // рантайм его не опускает — значит вне атаки на нём висит это пустое
        // состояние. С Write Defaults пустое состояние на generic-риге пишет
        // позу покоя рига на всё, что попадает в маску: spine, шея, голова, обе
        // руки, обе кисти с пальцами. В игре это выглядело так: в беге руки
        // висят вдоль тела и не качаются вовсе (двигался только таз и ноги), а
        // каждый удар начинался прыжком из позы покоя в середину связки за
        // 45 мс и возвращался туда же. Раскрытые пустые ладони в кадрах замаха
        // — оттуда же.
        //
        // Выключенное Write Defaults превращает пустое состояние в то, чем оно
        // и задумано: слой не пишет ничего, верх тела берётся с Base Layer, а
        // выходной переход честно смешивает конец удара с локомоцией.
        AnimatorState empty = State(upper, "UpperBody_Empty", null, 1f);
        empty.writeDefaultValues = false;
        upper.defaultState = empty;

        UpperCombat(upper, empty, "Saber_A_v5", attackA, "AttackA",
            SaberAStateSpeed, 0.11f, 0.96f, 0.18f, true, addExitTransition: false);
        UpperCombat(upper, empty, "Saber_B_v5", attackB, "AttackB",
            SaberBStateSpeed, 0.11f, 0.76f, 0.22f, true, addExitTransition: false);
        // Runtime releases both masks together after the complete pivot.
        UpperCombat(upper, empty, "Whirlwind_v5", whirlwind, "HeavyAttack",
            1f, 0.08f, 1f, 0.18f, true, addExitTransition: false);
    }

    private static void BuildLowerBodyLayer(AnimatorController controller, AvatarMask mask,
        AnimationClip attackA, AnimationClip attackB, AnimationClip whirlwind)
    {
        controller.AddLayer(LowerBodyLayerName);
        AnimatorControllerLayer[] layers = controller.layers;
        int layerIndex = layers.Length - 1;
        AnimatorControllerLayer layer = layers[layerIndex];
        // Runtime raises this layer only while Pelag is stationary. While he is
        // moving, the base Run_v5 legs remain authoritative and cannot slide;
        // while standing, the authored weight shifts and pivots are restored.
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = mask;
        layers[layerIndex] = layer;
        controller.layers = layers;

        AnimatorStateMachine lower = controller.layers[layerIndex].stateMachine;
        // То же, что и в верхнем слое. Здесь дефолтный вес 0, поэтому поза
        // покоя не пролезала постоянно, но во время подъёма/спуска веса она
        // подмешивалась к ногам и давала ватную оттяжку после удара.
        AnimatorState empty = State(lower, "LowerBody_Empty", null, 1f);
        empty.writeDefaultValues = false;
        lower.defaultState = empty;

        UpperCombat(lower, empty, "Lower_Saber_A_v5", attackA, "LowerAttackA",
            SaberAStateSpeed, 0.11f, 0.96f, 0.18f, true, addExitTransition: false);
        UpperCombat(lower, empty, "Lower_Saber_B_v5", attackB, "LowerAttackB",
            SaberBStateSpeed, 0.11f, 0.76f, 0.22f, true, addExitTransition: false);
        UpperCombat(lower, empty, "Lower_Whirlwind_v5", whirlwind, "LowerHeavyAttack",
            1f, 0.08f, 1f, 0.18f, true, addExitTransition: false);
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name,
        Motion motion, float speed)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = motion;
        state.speed = speed;
        return state;
    }

    private static void ConfigureIdleTransitions(AnimatorState source, AnimatorState runStart,
        AnimatorState turnLeft, AnimatorState turnRight, float runBlend, float turnBlend)
    {
        if (source == null) return;

        Transition(source, runStart, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, runBlend);
        Transition(source, turnLeft, "TurnDirection", AnimatorConditionMode.Less, -0.1f, turnBlend);
        Transition(source, turnRight, "TurnDirection", AnimatorConditionMode.Greater, 0.1f, turnBlend);
    }

    private static void ConfigureTurnExit(AnimatorState source, AnimatorState idle,
        string turnParameter, AnimatorConditionMode turnMode, float turnThreshold,
        bool relaxed, float duration)
    {
        AnimatorStateTransition transition = source.AddTransition(idle);
        transition.AddCondition(turnMode, turnThreshold, turnParameter);
        transition.AddCondition(relaxed ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            0f, "Relaxed");
        transition.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        transition.hasExitTime = false;
        transition.duration = duration;
    }

    private static void UpperCombat(AnimatorStateMachine machine, AnimatorState empty,
        string stateName, AnimationClip clip, string trigger, float speed, float blend,
        float exitTime, float exitBlend, bool fixedExitDuration = false,
        bool addExitTransition = true)
    {
        AnimatorState state = State(machine, stateName, clip, speed);
        // Помощник обслуживает только два перекрывающих слоя с маской, и там
        // Write Defaults выключён у пустого состояния. Смешивать режимы внутри
        // одного слоя нельзя — держим весь слой в одном режиме.
        state.writeDefaultValues = false;
        state.speedParameterActive = true;
        state.speedParameter = AttackPlaybackSpeed;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;
        enter.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        enter.orderedInterruption = false;

        // АВТОМАТИЧЕСКИЙ ВЫХОД НУЖЕН НЕ ВСЕМ. У связки A/B он ломает стык.
        //
        // Клип A доигрывает к 0.71 с, а следующий удар приходит на 0.80 с.
        // Выход на 0.96 срабатывает уже на 0.68 с и за 0.18 с уводит слой в
        // пустое состояние — к приходу B слой на две трети вернулся к бегу, и
        // B стартует не из конечной позы A, а из полу-idle. Отсюда «резкий
        // обрыв между A и B»: сама-то пара кадров в исходнике непрерывна.
        //
        // Без автоматического выхода состояние держит последний кадр, и B
        // кроссфейдится ровно из него — то есть из своего же нулевого кадра.
        // Отпускает слой вес: его гасит CharacterAnimatorView по таймеру
        // показа удара, а состояние параллельно уводится в пустое из кода.
        if (!addExitTransition) return;

        AnimatorStateTransition exit = state.AddTransition(empty);
        exit.hasExitTime = true;
        exit.exitTime = exitTime;
        exit.duration = exitBlend;
        exit.hasFixedDuration = fixedExitDuration;
        // A committed action from Sim must be able to cut through the soft
        // recovery. Without interruption, Whirlwind kept the layer busy past
        // the next basic-attack windup and the blade arrived after Damage.
        exit.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        exit.orderedInterruption = false;
    }

    private static void Transition(AnimatorState from, AnimatorState to, string parameter,
        AnimatorConditionMode mode, float threshold, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.AddCondition(mode, threshold, parameter);
        transition.hasExitTime = false;
        transition.duration = duration;
    }

    private static void TimedTransition(AnimatorState from, AnimatorState to,
        float exitTime, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
    }

    private static void TimedTransition(AnimatorState from, AnimatorState to,
        float exitTime, float duration, string parameter, AnimatorConditionMode mode)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.AddCondition(mode, 0f, parameter);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
    }

    private static void Transition(AnimatorState from, AnimatorState to, string parameterA,
        AnimatorConditionMode modeA, float thresholdA, float duration, string parameterB,
        AnimatorConditionMode modeB, float thresholdB)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.AddCondition(modeA, thresholdA, parameterA);
        transition.AddCondition(modeB, thresholdB, parameterB);
        transition.hasExitTime = false;
        transition.duration = duration;
    }

    private static void Combat(AnimatorStateMachine machine, AnimatorState relaxedIdle,
        AnimatorState combatIdle, AnimatorState run, string stateName, AnimationClip clip,
        string trigger, float speed, float blend, float exitTime, float exitBlend)
    {
        AnimatorState state = State(machine, stateName, clip, speed);
        if (trigger == "Hook" || trigger == "AnchorLeap" || trigger == "AnchorSweep"
            || trigger == "ChainStep")
        {
            // Presentation drives cast tempo per ability while Sim remains the
            // sole authority for contact and movement.
            state.speedParameterActive = true;
            state.speedParameter = AbilityPlaybackSpeed;
        }
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;

        AnimatorStateTransition exitRelaxed = state.AddTransition(relaxedIdle);
        exitRelaxed.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        exitRelaxed.AddCondition(AnimatorConditionMode.If, 0f, "Relaxed");
        exitRelaxed.hasExitTime = true;
        exitRelaxed.exitTime = exitTime;
        exitRelaxed.duration = exitBlend;

        AnimatorStateTransition exitCombat = state.AddTransition(combatIdle);
        exitCombat.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        exitCombat.AddCondition(AnimatorConditionMode.IfNot, 0f, "Relaxed");
        exitCombat.hasExitTime = true;
        exitCombat.exitTime = exitTime;
        exitCombat.duration = exitBlend;

        AnimatorStateTransition exitRun = state.AddTransition(run);
        exitRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "MoveSpeed");
        exitRun.hasExitTime = true;
        exitRun.exitTime = exitTime;
        exitRun.duration = exitBlend;
    }

    private static AnimationClip Load(string file, string clipName)
    {
        string path = Folder + "/" + file;
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => candidate.name == clipName);
        if (clip == null) Debug.LogError($"[Разлом] Нет клипа {clipName} в {path}");
        else if (clip.legacy) Debug.LogError($"[Разлом] {clipName} ошибочно импортирован как Legacy.");
        return clip;
    }
}
