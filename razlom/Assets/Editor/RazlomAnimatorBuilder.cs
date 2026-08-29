using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Собирает AnimatorController персонажа из импортированных клипов.
///
/// ЗАЧЕМ КОДОМ, А НЕ РУКАМИ. Контроллер — это ассет, который легко собрать
/// мышкой и невозможно потом воспроизвести: он не читается в диффе, ломается
/// при переносе и не переживает переимпорт модели. Собранный скриптом
/// восстанавливается одной командой в любой момент.
///
/// Имена параметров заданы не здесь, а в CharacterAnimatorView — он их дёргает
/// в бою. Разъедутся имена, и анимация молча перестанет реагировать, поэтому
/// они вынесены в константы рядом.
/// </summary>
public static class RazlomAnimatorBuilder
{
    private const string ClipsFolder = "Assets/Art/Characters/Pelag_Rodin01/Animations";
    private const string OutputPath = "Assets/Resources/Characters/Pelag_Rodin01/Pelag_Rodin01.controller";

    // Те же имена, что читает CharacterAnimatorView.
    private const string MoveSpeed = "MoveSpeed";
    private const string Relaxed = "Relaxed";
    private const string Stunned = "Stunned";
    private const string AttackA = "AttackA";
    private const string AttackB = "AttackB";

    // Эти триггеры боевой код дёргает всегда, даже если клипа под них ещё нет.
    // Параметр, которого нет в контроллере, — это предупреждение Unity на
    // КАЖДЫЙ удар: консоль забивается, и в ней тонет всё остальное.
    private const string HeavyAttack = "HeavyAttack";
    private const string Hook = "Hook";
    private const string HitFront = "HitFront";
    private const string Death = "Death";

    private const float MoveThreshold = 0.1f;
    private const string BuildVersionKey = "Razlom.PelagAnimator.GameplayV2";

    [InitializeOnLoadMethod]
    private static void AutoBuildGameplayVersion()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(BuildVersionKey, false)) return;
            SessionState.SetBool(BuildVersionKey, true);
            Build();
        };
    }

    [MenuItem("Разлом/Собрать контроллер пелага")]
    public static void Build()
    {
        AnimationClip idle = FindClip("Pelag_Idle");
        AnimationClip run = FindClip("Pelag_Run");
        AnimationClip attackA = FindClip("Pelag_AttackA");
        AnimationClip attackB = FindClip("Pelag_AttackB");

        if (idle == null || run == null)
        {
            Debug.LogError("[Разлом] Не найдены клипы Idle и Run — контроллер не собран. " +
                           "Проверь " + ClipsFolder);
            return;
        }

        string folder = Path.GetDirectoryName(OutputPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("[Разлом] Нет папки " + folder);
            return;
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);
        controller.AddParameter(MoveSpeed, AnimatorControllerParameterType.Float);
        controller.AddParameter(Relaxed, AnimatorControllerParameterType.Bool);
        controller.AddParameter(Stunned, AnimatorControllerParameterType.Bool);
        controller.AddParameter(AttackA, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(AttackB, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(HeavyAttack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(Hook, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(HitFront, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(Death, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState idleState = root.AddState("Idle");
        idleState.motion = idle;
        Loop(idle);

        AnimatorState runState = root.AddState("Run");
        runState.motion = run;
        Loop(run);

        root.defaultState = idleState;

        // Ходьба и стойка — по скорости. Порог с запасом в обе стороны,
        // иначе на границе состояние дребезжит.
        AnimatorStateTransition toRun = idleState.AddTransition(runState);
        toRun.AddCondition(AnimatorConditionMode.Greater, MoveThreshold, MoveSpeed);
        toRun.hasExitTime = false;
        toRun.duration = 0.12f;

        AnimatorStateTransition toIdle = runState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.Less, MoveThreshold, MoveSpeed);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;

        AddAttack(controller, root, idleState, attackA, "AttackA", AttackA);
        AddAttack(controller, root, idleState, attackB, "AttackB", AttackB);

        // Круговой удар идёт на способность: под неё клип есть, и он заметно
        // отличается от обычных комбо — а способность и должна отличаться.
        AddAttack(controller, root, idleState, FindClip("Pelag_AttackSpin"), "HeavyAttack", HeavyAttack);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Разлом] Контроллер собран: " + OutputPath);
    }

    /// <summary>
    /// Удар вешается на Any State: бить можно из любого состояния, и ждать,
    /// пока доиграет шаг, персонаж не должен — это читалось бы как задержка
    /// ввода, а ввод здесь и так на удержании.
    /// </summary>
    private static void AddAttack(AnimatorController controller, AnimatorStateMachine root,
        AnimatorState back, AnimationClip clip, string stateName, string trigger)
    {
        if (clip == null)
        {
            Debug.LogWarning("[Разлом] Нет клипа для " + stateName + ", удар останется без анимации.");
            return;
        }

        AnimatorState state = root.AddState(stateName);
        state.motion = clip;
        state.speed = stateName == "AttackA" ? 10f
                    : stateName == "AttackB" ? 5f
                    : 4.5f;
        Once(clip);

        AnimatorStateTransition enter = root.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = 0.05f;
        enter.canTransitionToSelf = false;

        AnimatorStateTransition exit = state.AddTransition(back);
        exit.hasExitTime = true;
        exit.exitTime = 0.92f;
        exit.duration = 0.08f;
    }

    private static void Loop(AnimationClip clip) => SetLoop(clip, true);
    private static void Once(AnimationClip clip) => SetLoop(clip, false);

    private static void SetLoop(AnimationClip clip, bool loop)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].loopTime != loop)
            {
                clips[i].loopTime = loop;
                changed = true;
            }

            if (ConfigureCombatWindow(path, clips[i])) changed = true;
        }
        if (!changed) return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static bool ConfigureCombatWindow(string path, ModelImporterClipAnimation clip)
    {
        float first;
        float last;
        if (path.EndsWith("Pelag_AttackA.fbx"))
        {
            first = 31f;
            last = 111f;
        }
        else if (path.EndsWith("Pelag_AttackB.fbx"))
        {
            first = 21f;
            last = 61f;
        }
        else
        {
            return false;
        }

        bool changed = clip.firstFrame != first || clip.lastFrame != last
                       || !clip.lockRootPositionXZ || !clip.lockRootHeightY;
        clip.firstFrame = first;
        clip.lastFrame = last;
        clip.lockRootPositionXZ = true;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionXZ = true;
        clip.keepOriginalPositionY = true;
        return changed;
    }

    private static AnimationClip FindClip(string fileName)
    {
        string path = ClipsFolder + "/" + fileName + ".fbx";
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
        if (all == null) return null;

        foreach (Object o in all)
        {
            var clip = o as AnimationClip;

            // У модели рядом с настоящим клипом лежит служебный __preview__.
            if (clip != null && !clip.name.StartsWith("__")) return clip;
        }
        return null;
    }
}
