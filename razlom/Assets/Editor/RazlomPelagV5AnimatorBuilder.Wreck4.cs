using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Крушение v4 (06.10, принято владельцем по превью) в контроллере Пелага: клипы Pelag_AN_Wreck4_{Draw, Swing1, Swing2,
/// Lunge, Stow} из ART/characters/pelag/wreck-2026-10-03/animation-v4/clips (timing.json: кадр = тик), по состоянию на
/// клип на базовом слое, у каждого своё время (Wreck4Phase*), переходов в графе нет — вход, стыки и выход задаёт
/// CharacterAnimatorView.Wreck2. Имена — PelagWreckClipRules («Wreck4_Swing1» и т. д.; риг сверяет слой 0 по ним —
/// AnchorRigWreckPlan.StateName). Предел таза 0,5, привязка Pelag_AN_Wreck4Bind (= Wreck2Bind, покой v6 стоя).
/// Нет хоть одного FBX или клип не собрался — состояния не добавляются, остаётся прежний путь WreckA/B/Finish_v5.
/// Сохранение — только своё (SaveBuiltAssets, память razlom-animator-builder-saveassets). Заменил Wreck2 (v3).
/// </summary>
public static partial class RazlomPelagV5AnimatorBuilder
{
    private static bool Wreck4OnDisk(string name)
    {
        string path = Folder + "/" + name + ".fbx";
        return File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>FBX Крушения v4, что лежат в Folder (клипы и привязка) — для принудительного импорта.</summary>
    private static string[] Wreck4Files()
    {
        var files = new List<string>();
        foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
            if (Wreck4OnDisk(PelagWreckClipRules.ClipName(clip))) files.Add(PelagWreckClipRules.ClipName(clip) + ".fbx");
        if (Wreck4OnDisk(PelagWreckClipRules.BindReference)) files.Add(PelagWreckClipRules.BindReference + ".fbx");
        return files.ToArray();
    }

    private static void AddWreck4Parameters(AnimatorController controller)
    {
        foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
            AddParameter(controller, PelagWreckClipRules.PhaseParameter(clip), AnimatorControllerParameterType.Float);
    }

    /// <summary>Базовый слой: пять клипов Крушения v4.</summary>
    private static void AddWreck4States(AnimatorStateMachine machine)
    {
        if (!Wreck4OnDisk(PelagWreckClipRules.BindReference)
            || PelagWreckClipRules.Required.Any(clip => !Wreck4OnDisk(PelagWreckClipRules.ClipName(clip))))
        {
            Debug.LogWarning("[Разлом] Крушение v4: нет FBX в " + Folder + " — состояния Wreck4 не собраны, остаётся прежний WreckA/B/Finish_v5.");
            return;
        }
        var clips = new Dictionary<PelagWreckClip, AnimationClip>();
        try
        {
            foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
                clips[clip] = RazlomPelagAuthoredClips.Build(PelagWreckClipRules.ClipName(clip), PelagWreckClipRules.Loops(clip),
                    PelagWreckClipRules.BindReference, PelagWreckClipRules.HipLimit);
        }
        catch (System.Exception error)
        {
            // Один упавший клип не должен ронять весь контроллер: без Wreck4 рантайм играет прежнее Крушение.
            Debug.LogError("[Разлом] Крушение v4 не собрано: " + error.Message);
            return;
        }
        GroundWreck4Clips(clips);
        foreach (var pair in clips)
        {
            AnimatorState state = State(machine, PelagWreckClipRules.StateName(pair.Key), pair.Value, 1f);
            state.writeDefaultValues = false;
            state.timeParameterActive = true;
            state.timeParameter = PelagWreckClipRules.PhaseParameter(pair.Key);
        }
        Debug.Log("[Разлом] Крушение v4: " + clips.Count + " состояний Wreck4_* на базовом слое, предел таза " + PelagWreckClipRules.HipLimit);
    }

    /// <summary>
    /// Опоры Крушения — на землю, как у рывка, Шквала и Абордажа: перенос с рига Blender на Pelag_v6 поднимает стопы.
    /// Сдвиг ЗАМЕРЯЕТСЯ по кадрам опор (rows.json клипов v4, PelagWreckClipRules.PlantedFrames) и один на все клипы:
    /// разный сдвиг порвал бы стыки (0° по author.log). Меньше 5 мм рига — не трогаем.
    /// </summary>
    private static void GroundWreck4Clips(Dictionary<PelagWreckClip, AnimationClip> clips)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        AnimationClip idle = Load("Pelag_MX_Idle.fbx", "Pelag_MX_Idle");
        if (model == null || idle == null) return;
        var body = Object.Instantiate(model);
        body.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in body.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var bones = body.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
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
            foreach (var pair in clips)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int side = 0; side < 2; side++)
                    foreach (int frame in PelagWreckClipRules.PlantedFrames(pair.Key, side))
                    {
                        pair.Value.SampleAnimation(body, frame / 30f);
                        float height = Low(side) - ground;
                        sum += height;
                        count++;
                        min = Mathf.Min(min, height);
                        max = Mathf.Max(max, height);
                    }
                Debug.Log($"[Pelag wreck4] {pair.Key}: опоры над землёй {min:F4}…{max:F4} ед. рига");
            }
            float lift = sum / Mathf.Max(1, count);
            if (Mathf.Abs(lift) < .005f) { Debug.Log($"[Pelag wreck4] опоры на земле ({lift:F4} ед. рига), таз не сдвигается"); return; }
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
            Debug.Log($"[Pelag wreck4] опоры на землю: таз ниже на {lift:F4} ед. рига ({lift * 1.82f:F3} м в игре)");
        }
        finally { Object.DestroyImmediate(body); }
    }
}
