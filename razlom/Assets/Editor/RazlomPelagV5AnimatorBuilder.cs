using System;
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
        AnimationClip idle = Load("Pelag_MX_Idle.fbx", "Pelag_MX_Idle");
        AnimationClip run = Load("Pelag_MX_Run.fbx", "Pelag_MX_Run");
        AnimationClip attackA = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackA");
        AnimationClip attackB = Load("Pelag_MX_SaberCombo.fbx", "Pelag_MX_SaberAttackB");
        AnimationClip whirlwind = Load("Pelag_MX_Whirlwind.fbx", "Pelag_MX_Whirlwind");
        AnimationClip anchor = Load("Pelag_MX_AnchorAttack.fbx", "Pelag_MX_AnchorAttack");
        AnimationClip hit = Load("Pelag_MX_Hit.fbx", "Pelag_MX_Hit");
        AnimationClip death = Load("Pelag_MX_Death.fbx", "Pelag_MX_Death");

        AnimationClip[] required = { idle, run, attackA, attackB, whirlwind, anchor, hit, death };
        if (required.Any(clip => clip == null))
        {
            Debug.LogError("[Разлом] Pelag v5 controller не собран: не все Mixamo-клипы импортированы.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Output) != null)
            AssetDatabase.DeleteAsset(Output);

        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(Output);
        AddParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Relaxed", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "Stunned", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "AttackA", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "AttackB", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HeavyAttack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Hook", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "HitFront", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Death", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = State(machine, "Idle_v5", idle, 1f);
        // 3.25x technically matched translation, but produced six tiny
        // footfalls per second. 1.55x gives a readable run cadence (about 2.8
        // footfalls/s); deterministic movement remains owned by simulation.
        AnimatorState runState = State(machine, "Run_v5", run, 1.55f);
        machine.defaultState = idleState;

        Transition(idleState, runState, "MoveSpeed", AnimatorConditionMode.Greater, 0.1f, 0.10f);
        Transition(runState, idleState, "MoveSpeed", AnimatorConditionMode.Less, 0.1f, 0.12f);

        Combat(machine, idleState, "Saber_A_v5", attackA, "AttackA", 2.15f, 0.04f, 0.90f);
        Combat(machine, idleState, "Saber_B_v5", attackB, "AttackB", 2.15f, 0.04f, 0.90f);
        Combat(machine, idleState, "Whirlwind_v5", whirlwind, "HeavyAttack", 2.20f, 0.03f, 0.94f);
        Combat(machine, idleState, "Anchor_v5", anchor, "Hook", 2.65f, 0.04f, 0.88f);
        Combat(machine, idleState, "Hit_v5", hit, "HitFront", 2.45f, 0.02f, 0.90f);

        AnimatorState deathState = State(machine, "Death_v5", death, 2.45f);
        AnimatorStateTransition deathEnter = machine.AddAnyStateTransition(deathState);
        deathEnter.AddCondition(AnimatorConditionMode.If, 0f, "Death");
        deathEnter.hasExitTime = false;
        deathEnter.duration = 0.03f;
        deathEnter.canTransitionToSelf = false;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[Разлом] Pelag v5 controller собран только из новых Mixamo-клипов: " + Output);
    }

    private static void AddParameter(AnimatorController controller, string name,
        AnimatorControllerParameterType type) => controller.AddParameter(name, type);

    private static AnimatorState State(AnimatorStateMachine machine, string name,
        AnimationClip clip, float speed)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        state.speed = speed;
        return state;
    }

    private static void Transition(AnimatorState from, AnimatorState to, string parameter,
        AnimatorConditionMode mode, float threshold, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.AddCondition(mode, threshold, parameter);
        transition.hasExitTime = false;
        transition.duration = duration;
    }

    private static void Combat(AnimatorStateMachine machine, AnimatorState back, string stateName,
        AnimationClip clip, string trigger, float speed, float blend, float exitTime)
    {
        AnimatorState state = State(machine, stateName, clip, speed);
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;

        AnimatorStateTransition exit = state.AddTransition(back);
        exit.hasExitTime = true;
        exit.exitTime = exitTime;
        exit.duration = 0.07f;
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
