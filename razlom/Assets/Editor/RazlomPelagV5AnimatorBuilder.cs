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

    [InitializeOnLoadMethod]
    private static void AutoBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            Build();
        };
    }

    [MenuItem("Разлом/Собрать Pelag v5 — Mixamo controller")]
    public static void Build()
    {
        // The combo's clip split is authored by RazlomCharacterImport. Force
        // that recipe through the importer before reading sub-clips; otherwise
        // a stale Library can silently keep the old 50–99 cut and rebuild a
        // controller whose second strike snaps before its authored recovery.
        AssetDatabase.ImportAsset(Folder + "/Pelag_MX_SaberCombo.fbx",
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        AnimationClip idle = Load("Pelag_MX_Idle.fbx", "Pelag_MX_Idle");
        AnimationClip run = Load("Pelag_MX_Run.fbx", "Pelag_MX_Run");
        AnimationClip turnLeft = Load("Pelag_MX_TurnLeft.fbx", "Pelag_MX_TurnLeft");
        AnimationClip turnRight = Load("Pelag_MX_TurnRight.fbx", "Pelag_MX_TurnRight");
        AnimationClip attackA = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackA");
        AnimationClip attackB = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackB");
        AnimationClip whirlwind = Load("Pelag_MX_Whirlwind.fbx", "Pelag_MX_Whirlwind");
        AnimationClip anchor = Load("Pelag_MX_AnchorAttack.fbx", "Pelag_MX_AnchorAttack");
        AnimationClip hit = Load("Pelag_MX_Hit.fbx", "Pelag_MX_Hit");
        AnimationClip death = Load("Pelag_MX_Death.fbx", "Pelag_MX_Death");

        AnimationClip[] required =
            { idle, run, turnLeft, turnRight, attackA, attackB, whirlwind, anchor, hit, death };
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
        AddParameter(controller, "TurnDirection", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Relaxed", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "Stunned", AnimatorControllerParameterType.Bool);
        AddParameter(controller, LocomotionPlaybackSpeed, AnimatorControllerParameterType.Float);
        AddParameter(controller, AttackPlaybackSpeed, AnimatorControllerParameterType.Float);
        AddParameter(controller, "AttackA", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "AttackB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerAttackA", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerAttackB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "LowerHeavyAttack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HeavyAttack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Hook", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HitFront", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Death", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        // Delivery idle is an 8.3-second very restrained take. At 1x its
        // breathing/weight shift disappears at gameplay scale and reads as a
        // frozen model. A moderate retime keeps the authored motion but makes
        // the combat-ready pulse visible without inventing procedural bones.
        AnimatorState idleState = State(machine, "Idle_v5", idle, 1.35f);
        // Blender audit: this 34-frame delivery is authored at 60 fps, so 1x
        // already produces 3.64 contacts/s. The previous 1.55x was still 5.64
        // contacts/s — exactly the frantic tiny-step read seen in game.
        // 0.68x — компромисс после экранного замера: оставляет тяжёлый шаг,
        // но сокращает остаточное скольжение стопы против 0.60x, не возвращая
        // прежнюю нервную дробь на 0.76x.
        AnimatorState runState = State(machine, "Run_v5", run, 0.68f);
        // During a committed attack/ability Simulation deliberately caps the
        // body at 50% movement speed. Retiming only Run_v5 to the same ratio
        // keeps the planted foot attached to the ground while the upper-body
        // action retains its authored timing.
        runState.speedParameterActive = true;
        runState.speedParameter = LocomotionPlaybackSpeed;
        AnimatorState turnLeftState = State(machine, "TurnLeft_v5", turnLeft, 1f);
        AnimatorState turnRightState = State(machine, "TurnRight_v5", turnRight, 1f);
        machine.defaultState = idleState;

        Transition(idleState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.10f);
        // MoveSpeed itself drops on the real stop, while this fixed blend keeps
        // the supporting leg settling instead of snapping into the idle pose.
        Transition(runState, idleState, "MoveSpeed", AnimatorConditionMode.Less, 0.1f, 0.14f);
        Transition(idleState, turnLeftState, "TurnDirection", AnimatorConditionMode.Less, -0.1f, 0.06f);
        Transition(idleState, turnRightState, "TurnDirection", AnimatorConditionMode.Greater, 0.1f, 0.06f);
        Transition(runState, turnLeftState, "TurnDirection", AnimatorConditionMode.Less, -0.1f, 0.08f,
            "MoveSpeed", AnimatorConditionMode.Less, 0.1f);
        Transition(runState, turnRightState, "TurnDirection", AnimatorConditionMode.Greater, 0.1f, 0.08f,
            "MoveSpeed", AnimatorConditionMode.Less, 0.1f);
        Transition(turnLeftState, idleState, "TurnDirection", AnimatorConditionMode.Greater, -0.1f, 0.12f,
            "MoveSpeed", AnimatorConditionMode.Less, 0.1f);
        Transition(turnRightState, idleState, "TurnDirection", AnimatorConditionMode.Less, 0.1f, 0.12f,
            "MoveSpeed", AnimatorConditionMode.Less, 0.1f);
        Transition(turnLeftState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnRightState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnLeftState, turnRightState, "TurnDirection", AnimatorConditionMode.Greater, 0.1f, 0.08f);
        Transition(turnRightState, turnLeftState, "TurnDirection", AnimatorConditionMode.Less, -0.1f, 0.08f);

        // Hit/death-style reactions remain full-body. Saber attacks and the
        // moving Whirlwind live on the masked layer below so Run_v5 remains
        // authoritative for the legs instead of sliding an in-place cast.
        Combat(machine, idleState, runState, "Anchor_v5", anchor, "Hook",
            2.65f, 0.04f, 0.88f, 0.10f);
        Combat(machine, idleState, runState, "Hit_v5", hit, "HitFront",
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
        AnimatorControllerParameterType type) => controller.AddParameter(name, type);

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
        layer.defaultWeight = 1f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = mask;
        layers[layerIndex] = layer;
        controller.layers = layers;

        AnimatorStateMachine upper = controller.layers[layerIndex].stateMachine;
        AnimatorState empty = State(upper, "UpperBody_Empty", null, 1f);
        upper.defaultState = empty;

        UpperCombat(upper, empty, "Saber_A_v5", attackA, "AttackA",
            0.92f, 0.11f, 0.96f, 0.18f, true);
        UpperCombat(upper, empty, "Saber_B_v5", attackB, "AttackB",
            1.20f, 0.11f, 0.76f, 0.22f, true);
        // 91 authored frames / 1.55 playback. Frame ~70 is already close to
        // locomotion, so keep the readable 0.76 exit pose and give it a real
        // six-to-eleven-frame recovery instead of snapping out in three frames.
        UpperCombat(upper, empty, "Whirlwind_v5", whirlwind, "HeavyAttack",
            1.55f, 0.06f, 0.76f, 0.18f, true);
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
        AnimatorState empty = State(lower, "LowerBody_Empty", null, 1f);
        lower.defaultState = empty;

        UpperCombat(lower, empty, "Lower_Saber_A_v5", attackA, "LowerAttackA",
            0.92f, 0.11f, 0.96f, 0.18f, true);
        UpperCombat(lower, empty, "Lower_Saber_B_v5", attackB, "LowerAttackB",
            1.20f, 0.11f, 0.76f, 0.22f, true);
        UpperCombat(lower, empty, "Lower_Whirlwind_v5", whirlwind, "LowerHeavyAttack",
            1.55f, 0.06f, 0.76f, 0.18f, true);
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name,
        AnimationClip clip, float speed)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        state.speed = speed;
        return state;
    }

    private static void UpperCombat(AnimatorStateMachine machine, AnimatorState empty,
        string stateName, AnimationClip clip, string trigger, float speed, float blend,
        float exitTime, float exitBlend, bool fixedExitDuration = false)
    {
        AnimatorState state = State(machine, stateName, clip, speed);
        state.speedParameterActive = true;
        state.speedParameter = AttackPlaybackSpeed;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;
        enter.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        enter.orderedInterruption = false;

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

    private static void Combat(AnimatorStateMachine machine, AnimatorState idle, AnimatorState run,
        string stateName, AnimationClip clip, string trigger, float speed, float blend,
        float exitTime, float exitBlend)
    {
        AnimatorState state = State(machine, stateName, clip, speed);
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;

        AnimatorStateTransition exitIdle = state.AddTransition(idle);
        exitIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");
        exitIdle.hasExitTime = true;
        exitIdle.exitTime = exitTime;
        exitIdle.duration = exitBlend;

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
