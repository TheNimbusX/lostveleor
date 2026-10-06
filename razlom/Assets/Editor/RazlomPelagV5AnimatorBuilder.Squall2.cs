using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Шквал v2 (02.10) в контроллере Пелага: семь клипов Pelag_AN_Squall2_* из
/// ART/characters/pelag/squall-forms-2026-10-02/animation (timing.json: кадр = тик),
/// по состоянию на клип, у каждого своё время (Squall2Phase*), переходов в графе нет —
/// вход, стыки и выход задаёт CharacterAnimatorView.Squall. Имена — PelagSquallClipRules.
///
/// Предел таза 0,5 (клипы дают 0,48 длины корпуса): по умолчанию 0,4 сборка упала бы
/// с «hip offset exceeds authored crouch envelope». Привязка — Pelag_AN_Squall2Bind
/// (покой v6 стоя, объект +90° по X, как у рывка).
///
/// Прежние ChainStep_* остаются, пока новый показ не принят: рантайм в них не входит,
/// если контроллер собран с Squall2_* (CharacterAnimatorView.SupportsSquall2).
/// Нет FBX или клип не собрался — состояния Squall2 не добавляются, остальной
/// контроллер собирается как прежде.
/// </summary>
public static partial class RazlomPelagV5AnimatorBuilder
{
    /// <summary>
    /// Кадры, где стопа стоит (timing.json rows: носок ≤ 1 см): [0] — левая, [1] — правая.
    /// По ним считается, на сколько перенос с рига Blender поднял стопы.
    /// </summary>
    private static readonly Dictionary<PelagSquallClip, int[][]> Squall2Contacts = new Dictionary<PelagSquallClip, int[][]>
    {
        [PelagSquallClip.Load] = new[] { new[] { 0, 1, 2 }, new[] { 0, 2 } },
        [PelagSquallClip.Forehand] = new[] { new[] { 0, 6, 7, 8 }, new[] { 0, 6, 8 } },
        [PelagSquallClip.Backhand] = new[] { new[] { 0, 6, 7, 8 }, new[] { 0, 6, 8 } },
        [PelagSquallClip.FinishFore] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[] { 0, 1, 4, 5, 6, 7, 8, 9 } },
        [PelagSquallClip.FinishBack] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[] { 0, 1, 5, 6, 7, 8, 9 } },
        [PelagSquallClip.ReturnFore] = new[] { new[] { 0, 6, 7, 8, 9, 10 }, new[] { 0, 6, 7, 8, 9, 10 } },
        [PelagSquallClip.ReturnBack] = new[] { new[] { 0, 6, 7, 8, 9, 10 }, new[] { 0, 6, 7, 8, 9, 10 } },
    };

    /// <summary>FBX Шквала v2 и привязка — в Folder, рядом с остальными клипами.</summary>
    private static string[] Squall2Files()
    {
        var files = PelagSquallClipRules.Clips.Select(clip => PelagSquallClipRules.ClipName(clip) + ".fbx").ToList();
        files.Add(PelagSquallClipRules.BindReference + ".fbx");
        return files.ToArray();
    }

    private static void AddSquall2Parameters(AnimatorController controller)
    {
        foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
            AddParameter(controller, PelagSquallClipRules.PhaseParameter(clip), AnimatorControllerParameterType.Float);
    }

    private static void AddSquall2States(AnimatorStateMachine machine)
    {
        foreach (string file in Squall2Files())
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + file) == null)
            {
                Debug.LogWarning("[Разлом] Шквал v2: нет " + Folder + "/" + file + " — состояния Squall2 не собраны, остаётся прежний Шквал.");
                return;
            }

        var clips = new Dictionary<PelagSquallClip, AnimationClip>();
        try
        {
            foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
                clips[clip] = RazlomPelagAuthoredClips.Build(PelagSquallClipRules.ClipName(clip), false,
                    PelagSquallClipRules.BindReference, PelagSquallClipRules.HipLimit);
        }
        catch (System.Exception error)
        {
            // Один упавший клип не должен ронять весь контроллер: без Squall2 рантайм играет прежний Шквал.
            Debug.LogError("[Разлом] Шквал v2 не собран: " + error.Message);
            return;
        }
        GroundSquall2Clips(clips);
        foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
        {
            AnimatorState state = State(machine, PelagSquallClipRules.StateName(clip), clips[clip], 1f);
            state.writeDefaultValues = false;
            state.timeParameterActive = true;
            state.timeParameter = PelagSquallClipRules.PhaseParameter(clip);
        }
        Debug.Log("[Разлом] Шквал v2: " + PelagSquallClipRules.Clips.Length + " состояний Squall2_*, предел таза "
                  + PelagSquallClipRules.HipLimit);
    }

    /// <summary>
    /// Опоры Шквала — на землю, как у рывка (GroundDashClip, его не трогаем): перенос
    /// с рига Blender на Pelag_v6 поднимал стопы рывка на 9–10 см, а стойка Шквала — та же,
    /// в которой кончается рывок. Сдвиг ЗАМЕРЯЕТСЯ по кадрам опор всех семи клипов и
    /// один на все: разный сдвиг клипов порвал бы стыки (0° по timing.json). Меньше 5 мм
    /// рига — не трогаем.
    /// </summary>
    private static void GroundSquall2Clips(Dictionary<PelagSquallClip, AnimationClip> clips)
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
            foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int side = 0; side < 2; side++)
                    foreach (int frame in Squall2Contacts[clip][side])
                    {
                        clips[clip].SampleAnimation(body, frame / 30f);
                        float height = Low(side) - ground;
                        sum += height;
                        count++;
                        min = Mathf.Min(min, height);
                        max = Mathf.Max(max, height);
                    }
                Debug.Log($"[Pelag squall2] {clip}: опоры над землёй {min:F4}…{max:F4} ед. рига");
            }
            float lift = sum / Mathf.Max(1, count);
            if (Mathf.Abs(lift) < .005f)
            {
                Debug.Log($"[Pelag squall2] опоры на земле ({lift:F4} ед. рига), таз не сдвигается");
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
            Debug.Log($"[Pelag squall2] опоры на землю: таз ниже на {lift:F4} ед. рига ({lift * 1.82f:F3} м в игре)");
        }
        finally { Object.DestroyImmediate(body); }
    }
}
