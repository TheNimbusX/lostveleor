using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Абордаж v2 (02.10) и v3 (03.10) в контроллере Пелага: клипы Pelag_AN_Abordage2_* из
/// ART/characters/pelag/abordage-2026-10-02/animation (timing.json: кадр = тик при W = 3, A = 6,
/// P = 12), по состоянию на клип, у каждого своё время (Abordage2Phase*), переходов в графе
/// нет — вход, стыки и выход задаёт CharacterAnimatorView.Abordage. Имена — PelagAbordageClipRules.
///
/// Обязательные — Throw (v3: замах через плечо, 10 кадров), Pull, Punch, Recover; без любого из
/// них (нет FBX или клип не собрался) состояния Abordage2 не добавляются, остаётся прежний
/// AnchorLeap_v5. Клипы v3 PullShort, Uppercut, Slam — по наличию: не собрался один — вид
/// показывает на его месте Pull или Punch (CharacterAnimatorView.Abordage).
///
/// Предел таза 0,5 (клипы дают до 0,433 длины корпуса): по умолчанию 0,4 сборка упала бы
/// с «hip offset exceeds authored crouch envelope». Привязка — Pelag_AN_Abordage2Bind
/// (покой v6 стоя, объект +90° по X, как у рывка и Шквала).
/// </summary>
public static partial class RazlomPelagV5AnimatorBuilder
{
    /// <summary>
    /// Кадры, где стопа стоит (timing.json planted_frames_v3 / rows: носок ≤ 1,2 см): [0] — левая,
    /// [1] — правая. По ним считается, на сколько перенос с рига Blender поднял стопы.
    /// </summary>
    private static readonly Dictionary<PelagAbordageClip, int[][]> Abordage2Contacts = new Dictionary<PelagAbordageClip, int[][]>
    {
        [PelagAbordageClip.Throw] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[] { 0, 3, 4, 5, 6, 7, 8, 9 } },
        [PelagAbordageClip.Pull] = new[] { new[] { 0, 12 }, new[] { 0, 12 } },
        [PelagAbordageClip.Punch] = new[] { new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 } },
        [PelagAbordageClip.Recover] = new[] { new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 0, 3, 4, 5, 6 } },
        [PelagAbordageClip.PullShort] = new[] { new[] { 0, 5 }, new[] { 0, 5 } },
        [PelagAbordageClip.Uppercut] = new[] { new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 } },
        [PelagAbordageClip.Slam] = new[] { new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 } },
    };

    /// <summary>FBX Абордажа и привязка — в Folder, рядом с остальными клипами.</summary>
    private static string[] Abordage2Files()
    {
        var files = PelagAbordageClipRules.Clips.Select(clip => PelagAbordageClipRules.ClipName(clip) + ".fbx").ToList();
        files.Add(PelagAbordageClipRules.BindReference + ".fbx");
        return files.ToArray();
    }

    private static bool Abordage2FbxExists(string file) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + file) != null;

    private static void AddAbordage2Parameters(AnimatorController controller)
    {
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
            AddParameter(controller, PelagAbordageClipRules.PhaseParameter(clip), AnimatorControllerParameterType.Float);
    }

    private static AnimationClip BuildAbordage2Clip(PelagAbordageClip clip)
        => RazlomPelagAuthoredClips.Build(PelagAbordageClipRules.ClipName(clip), false,
            PelagAbordageClipRules.BindReference, PelagAbordageClipRules.HipLimit);

    private static void AddAbordage2States(AnimatorStateMachine machine)
    {
        var required = PelagAbordageClipRules.RequiredClips.Select(clip => PelagAbordageClipRules.ClipName(clip) + ".fbx").ToList();
        required.Add(PelagAbordageClipRules.BindReference + ".fbx");
        foreach (string file in required)
            if (!Abordage2FbxExists(file))
            {
                Debug.LogWarning("[Разлом] Абордаж v2: нет " + Folder + "/" + file + " — состояния Abordage2 не собраны, остаётся прежний AnchorLeap_v5.");
                return;
            }

        var clips = new Dictionary<PelagAbordageClip, AnimationClip>();
        try
        {
            foreach (PelagAbordageClip clip in PelagAbordageClipRules.RequiredClips)
                clips[clip] = BuildAbordage2Clip(clip);
        }
        catch (System.Exception error)
        {
            // Один упавший клип не должен ронять весь контроллер: без Abordage2 рантайм играет прежний Абордаж.
            Debug.LogError("[Разлом] Абордаж v2 не собран: " + error.Message);
            return;
        }
        // v3 (03.10): короткая тяга и клипы форм — каждый сам по себе; нет — вид берёт Pull/Punch.
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
        {
            if (PelagAbordageClipRules.IsRequired(clip)) continue;
            string file = PelagAbordageClipRules.ClipName(clip) + ".fbx";
            if (!Abordage2FbxExists(file))
            {
                Debug.LogWarning("[Разлом] Абордаж v3: нет " + Folder + "/" + file + " — без состояния " + PelagAbordageClipRules.StateName(clip));
                continue;
            }
            try { clips[clip] = BuildAbordage2Clip(clip); }
            catch (System.Exception error)
            {
                Debug.LogError("[Разлом] Абордаж v3: " + PelagAbordageClipRules.ClipName(clip) + " не собран: " + error.Message);
            }
        }
        GroundAbordage2Clips(clips);
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
        {
            if (!clips.TryGetValue(clip, out AnimationClip built)) continue;
            AnimatorState state = State(machine, PelagAbordageClipRules.StateName(clip), built, 1f);
            state.writeDefaultValues = false;
            state.timeParameterActive = true;
            state.timeParameter = PelagAbordageClipRules.PhaseParameter(clip);
        }
        Debug.Log("[Разлом] Абордаж v2/v3: " + clips.Count + " состояний Abordage2_* из " + PelagAbordageClipRules.Clips.Length
                  + ", предел таза " + PelagAbordageClipRules.HipLimit);
    }

    /// <summary>
    /// Опоры Абордажа — на землю, как у рывка и Шквала (их сдвиги не трогаем): перенос с рига
    /// Blender на Pelag_v6 поднимал стопы рывка на 9–10 см. Сдвиг ЗАМЕРЯЕТСЯ по кадрам опор
    /// обязательных клипов (Throw, Pull, Punch, Recover — тот же набор, что в v2) и один на все,
    /// включая PullShort/Uppercut/Slam: разный сдвиг клипов порвал бы стыки (0° по timing.json).
    /// Опоры клипов v3 только пишутся в журнал. Меньше 5 мм рига — не трогаем.
    /// </summary>
    private static void GroundAbordage2Clips(Dictionary<PelagAbordageClip, AnimationClip> clips)
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
            foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
            {
                if (!clips.ContainsKey(clip)) continue;
                bool measured = PelagAbordageClipRules.IsRequired(clip);
                float min = float.MaxValue, max = float.MinValue;
                for (int side = 0; side < 2; side++)
                    foreach (int frame in Abordage2Contacts[clip][side])
                    {
                        clips[clip].SampleAnimation(body, frame / 30f);
                        float height = Low(side) - ground;
                        if (measured)
                        {
                            sum += height;
                            count++;
                        }
                        min = Mathf.Min(min, height);
                        max = Mathf.Max(max, height);
                    }
                Debug.Log($"[Pelag abordage2] {clip}: опоры над землёй {min:F4}…{max:F4} ед. рига" + (measured ? "" : " (в сдвиг не входит)"));
            }
            float lift = sum / Mathf.Max(1, count);
            if (Mathf.Abs(lift) < .005f)
            {
                Debug.Log($"[Pelag abordage2] опоры на земле ({lift:F4} ед. рига), таз не сдвигается");
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
            Debug.Log($"[Pelag abordage2] опоры на землю: таз ниже на {lift:F4} ед. рига ({lift * 1.82f:F3} м в игре)");
        }
        finally { Object.DestroyImmediate(body); }
    }
}
