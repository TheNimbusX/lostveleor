using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Бросок якоря (03.10) в контроллере Пелага: пять клипов Pelag_AN_AnchorThrow_* из
/// ART/characters/pelag/anchor-throw-2026-10-03/animation (timing.json: кадр = тик при W = 2,
/// F = 10, R = 8), по состоянию на клип, у каждого своё время (AnchorThrowPhase*), переходов в
/// графе нет — вход, стыки и выход задаёт CharacterAnimatorView.AnchorThrow. Имена — PelagAnchorThrowClipRules.
///
/// Привязка — Pelag_AN_AnchorThrowBind (покой v6 стоя, объект +90° по X, как у Абордажа v2); предел
/// таза 0,45 (замер Blender 0,387). Нет FBX или клип не собрался — состояния не добавляются, остальной
/// контроллер собирается как прежде.
///
/// ДВА ВХОДА. Полная сборка Build() зовёт AddAnchorThrowParameters/AddAnchorThrowStates (правка
/// RazlomPelagV5AnimatorBuilder.cs). Чтобы не пересобирать весь контроллер ради пяти состояний, свой ключ
/// сессии (AnchorThrowSessionKey) ОДИН раз за сессию редактора дописывает их в ГОТОВЫЙ контроллер, если их
/// там нет, и сохраняет ТОЛЬКО контроллер и свои пять .anim (память razlom-animator-builder-saveassets:
/// общий SaveAssets затирал чужие ассеты общего редактора). То же — пункт меню.
/// </summary>
public static partial class RazlomPelagV5AnimatorBuilder
{
    private const string AnchorThrowSessionKey = "Razlom.PelagV5Animator.AnchorThrow.v1";

    /// <summary>
    /// Кадры, где стопа стоит плашмя (timing.json planted_ankles, contact = flat): [0] — левая, [1] — правая.
    /// По ним считается, на сколько перенос с рига Blender поднял стопы.
    /// </summary>
    private static readonly Dictionary<PelagAnchorThrowClip, int[][]> AnchorThrowContacts = new Dictionary<PelagAnchorThrowClip, int[][]>
    {
        [PelagAnchorThrowClip.Throw] = new[] { new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 2 } },
        [PelagAnchorThrowClip.Fly] = new[] { new[] { 0, 1, 2, 3 }, new[] { 1, 2, 3 } },
        [PelagAnchorThrowClip.Yank] = new[] { new[] { 0, 1, 2 }, new[] { 0, 2 } },
        [PelagAnchorThrowClip.Haul] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 0, 1, 2, 3, 4, 5, 6 } },
        [PelagAnchorThrowClip.Catch] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[] { 0, 1, 2, 3, 6, 7, 8, 9 } },
    };

    /// <summary>FBX Броска и привязка — в Folder, рядом с остальными клипами.</summary>
    private static string[] AnchorThrowFiles()
    {
        var files = PelagAnchorThrowClipRules.Clips.Select(clip => PelagAnchorThrowClipRules.ClipName(clip) + ".fbx").ToList();
        files.Add(PelagAnchorThrowClipRules.BindReference + ".fbx");
        return files.ToArray();
    }

    private static void AddAnchorThrowParameters(AnimatorController controller)
    {
        foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
        {
            string name = PelagAnchorThrowClipRules.PhaseParameter(clip);
            if (controller.parameters.Any(p => p.name == name)) continue;
            AddParameter(controller, name, AnimatorControllerParameterType.Float);
        }
    }

    /// <summary>Пять состояний AnchorThrow_*_v5 со временем AnchorThrowPhase*. false — FBX нет или клип не собрался.</summary>
    private static bool AddAnchorThrowStates(AnimatorStateMachine machine)
    {
        foreach (string file in AnchorThrowFiles())
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + file) == null)
            {
                Debug.LogWarning("[Разлом] Бросок якоря: нет " + Folder + "/" + file + " — состояния AnchorThrow не собраны.");
                return false;
            }

        var clips = new Dictionary<PelagAnchorThrowClip, AnimationClip>();
        try
        {
            foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
                clips[clip] = RazlomPelagAuthoredClips.Build(PelagAnchorThrowClipRules.ClipName(clip), false,
                    PelagAnchorThrowClipRules.BindReference, PelagAnchorThrowClipRules.HipLimit);
        }
        catch (System.Exception error)
        {
            // Один упавший клип не должен ронять весь контроллер: без AnchorThrow тело в броске просто стоит.
            Debug.LogError("[Разлом] Бросок якоря не собран: " + error.Message);
            return false;
        }
        GroundAnchorThrowClips(clips);
        foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
        {
            EditorUtility.SetDirty(clips[clip]);
            string name = PelagAnchorThrowClipRules.StateName(clip);
            AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                                  ?? State(machine, name, clips[clip], 1f);
            state.motion = clips[clip];
            state.speed = 1f;
            state.writeDefaultValues = false;
            state.timeParameterActive = true;
            state.timeParameter = PelagAnchorThrowClipRules.PhaseParameter(clip);
        }
        Debug.Log("[Разлом] Бросок якоря: " + PelagAnchorThrowClipRules.Clips.Length + " состояний AnchorThrow_*, предел таза "
                  + PelagAnchorThrowClipRules.HipLimit);
        return true;
    }

    private static bool HasAnchorThrowStates(AnimatorController controller)
    {
        if (controller == null || controller.layers.Length == 0) return false;
        var names = new HashSet<string>(controller.layers[0].stateMachine.states.Select(s => s.state.name));
        return PelagAnchorThrowClipRules.Clips.All(clip => names.Contains(PelagAnchorThrowClipRules.StateName(clip))
                                                          && controller.parameters.Any(p => p.name == PelagAnchorThrowClipRules.PhaseParameter(clip)));
    }

    [InitializeOnLoadMethod]
    private static void AutoAddAnchorThrow()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            // Импорт FBX внутри может перезагрузить домен — без ключа сессии сборка шла бы по кругу (как AutoBuild).
            // Интеграция 03.10: полная сборка по ключу v38.AnchorSkills уже кладёт состояния Броска якоря —
            // дописывать только после неё (если её ключ в этой сессии уже стоит), чтобы контроллер собирался один раз.
            if (!SessionState.GetBool(AutoBuildSessionKey, false)) return;
            if (SessionState.GetBool(AnchorThrowSessionKey, false)) return;
            SessionState.SetBool(AnchorThrowSessionKey, true);
            AddAnchorThrowToController();
        };
    }

    /// <summary>
    /// Дописать Бросок якоря в готовый контроллер (без пересборки остального): параметры и пять состояний,
    /// если их нет. Сохраняет только контроллер и свои .anim — SaveAssetIfDirty, без общего SaveAssets.
    /// </summary>
    [MenuItem("Разлом/Pelag v5 — дописать Бросок якоря в контроллер")]
    public static void AddAnchorThrowToController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Output);
        if (controller == null)
        {
            Debug.LogWarning("[Разлом] Бросок якоря: нет контроллера " + Output + " — соберётся полной сборкой Build().");
            return;
        }
        if (HasAnchorThrowStates(controller))
        {
            Debug.Log("[Разлом] Бросок якоря: состояния AnchorThrow_* уже в контроллере — ничего не пишем.");
            return;
        }
        ForceImport(AnchorThrowFiles());
        AddAnchorThrowParameters(controller);
        if (!AddAnchorThrowStates(controller.layers[0].stateMachine)) return;
        EditorUtility.SetDirty(controller);
        SaveAnchorThrowAssets(controller);
    }

    /// <summary>Только своё: контроллер и пять производных .anim Броска (Assets/Resources/Characters/Pelag_v5/*.anim).</summary>
    private static void SaveAnchorThrowAssets(AnimatorController controller)
    {
        int saved = 0;
        foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
        {
            var anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputFolder + "/" + PelagAnchorThrowClipRules.ClipName(clip) + ".anim");
            if (anim == null || !EditorUtility.IsDirty(anim)) continue;
            AssetDatabase.SaveAssetIfDirty(anim);
            saved++;
        }
        if (EditorUtility.IsDirty(controller))
        {
            AssetDatabase.SaveAssetIfDirty(controller);
            saved++;
        }
        Debug.Log("[Разлом] Бросок якоря: сохранено своих ассетов " + saved + " (контроллер и клипы, без общего SaveAssets).");
    }

    /// <summary>
    /// Опоры Броска — на землю, как у рывка, Шквала и Абордажа (их сдвиги не трогаем): перенос с рига Blender на
    /// Pelag_v6 поднимал стопы рывка на 9–10 см. Сдвиг ЗАМЕРЯЕТСЯ по кадрам опор всех пяти клипов и один на все:
    /// разный сдвиг клипов порвал бы стыки (0° по timing.json). Меньше 5 мм рига — не трогаем.
    /// </summary>
    private static void GroundAnchorThrowClips(Dictionary<PelagAnchorThrowClip, AnimationClip> clips)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        AnimationClip idle = Load("Pelag_MX_Idle.fbx", "Pelag_MX_Idle");
        if (model == null || idle == null) return;
        var body = Object.Instantiate(model);
        body.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in body.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var bones = body.GetComponentsInChildren<Transform>().GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());
            Transform hips = bones["mixamorig:Hips"];
            string[][] feet =
            {
                new[] { "mixamorig:LeftFoot", "mixamorig:LeftToeBase", "mixamorig:LeftToe_End" },
                new[] { "mixamorig:RightFoot", "mixamorig:RightToeBase", "mixamorig:RightToe_End" },
            };
            float Low(int side)
            {
                float low = float.MaxValue;
                foreach (string bone in feet[side])
                    if (bones.TryGetValue(bone, out var t)) low = Mathf.Min(low, t.position.y - body.transform.position.y);
                return low;
            }
            idle.SampleAnimation(body, 0f);
            float ground = Mathf.Min(Low(0), Low(1));
            float sum = 0f;
            int count = 0;
            foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int side = 0; side < 2; side++)
                    foreach (int frame in AnchorThrowContacts[clip][side])
                    {
                        clips[clip].SampleAnimation(body, frame / 30f);
                        float height = Low(side) - ground;
                        sum += height;
                        count++;
                        min = Mathf.Min(min, height);
                        max = Mathf.Max(max, height);
                    }
                Debug.Log($"[Pelag anchor-throw] {clip}: опоры над землёй {min:F4}…{max:F4} ед. рига");
            }
            float lift = sum / Mathf.Max(1, count);
            if (Mathf.Abs(lift) < .005f)
            {
                Debug.Log($"[Pelag anchor-throw] опоры на земле ({lift:F4} ед. рига), таз не сдвигается");
                return;
            }
            Vector3 shift = hips.parent.InverseTransformVector(Vector3.down * lift);
            string path = AnimationUtility.CalculateTransformPath(hips, body.transform);
            foreach (AnimationClip clip in clips.Values)
            {
                for (int c = 0; c < 3; c++)
                {
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[c]);
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) continue;
                    Keyframe[] keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++) keys[k].value += shift[c];
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                EditorUtility.SetDirty(clip);
            }
            Debug.Log($"[Pelag anchor-throw] опоры на землю: таз ниже на {lift:F4} ед. рига ({lift * 1.82f:F3} м в игре)");
        }
        finally { Object.DestroyImmediate(body); }
    }
}
