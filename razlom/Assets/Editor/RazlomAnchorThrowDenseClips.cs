using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Бросок якоря (animfix 07.10): перенос клипов Pelag_AN_AnchorThrow2_* на риг игры с ПЛОТНЫМИ ключами — та же формула, что
/// <see cref="RazlomPelagAuthoredClips.Build"/> (дельта от привязки в пространстве тела + направление сегмента), но
/// выборка не по 30 к/с, а по <c>samplesPerSecond</c> (120: FBX броска выгружены 4 ключа на тик). Прежний перенос по
/// кадрам 30 к/с с линейными касательными давал скорость кости ступенькой на каждом тике — «кривые переходы» (замер
/// artifacts/throw-0710/animfix/jumps_before.md). Общий Build не трогается: им собраны сабля, Крушение и остальные.
/// </summary>
public static class RazlomAnchorThrowDenseClips
{
    public static AnimationClip Build(string name, string bindReference, float maxHipOffsetBodyLengths, int samplesPerSecond)
    {
        string sourcePath = "Assets/Resources/Characters/Pelag_v5/Mixamo/" + name + ".fbx";
        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var targetModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        var bindModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/Pelag_v5/Mixamo/" + bindReference + ".fbx");
        if (sourceClip == null || sourceModel == null || targetModel == null)
            throw new System.InvalidOperationException("Missing authored Pelag clip: " + name);
        if (bindModel == null) throw new System.InvalidOperationException("Missing Blender bind reference");
        var source = Object.Instantiate(sourceModel);
        var target = Object.Instantiate(targetModel);
        source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in source.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var animator in target.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var sourceBones = source.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
            var targetBones = target.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToArray();
            var bindBones = bindModel.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
            var sourceBind = bindBones.ToDictionary(p => p.Key, p => p.Value.rotation);
            var targetBind = targetBones.ToDictionary(t => t.name, t => t.rotation);
            Quaternion alignment = BodyBasis(targetBones.ToDictionary(t => t.name)) * Quaternion.Inverse(BodyBasis(bindBones));
            var bindHips = bindModel.GetComponentsInChildren<Transform>().First(t => t.name == "mixamorig:Hips");
            var targetHips = targetBones.First(t => t.name == "mixamorig:Hips");
            float hipScale = (targetHips.position - targetBones.First(t => t.name == "mixamorig:Head").position).magnitude
                / Mathf.Max(.000001f, (bindHips.position - bindBones["mixamorig:Head"].position).magnitude);
            var hipPosition = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Vector3 targetHipRest = targetHips.position;
            float worstDirectionError = 0f;
            string worstBone = "";
            string path = "Assets/Resources/Characters/Pelag_v5/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.name = name; clip.frameRate = samplesPerSecond;
            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (var bone in targetBones)
                curves[bone.name] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            int samples = Mathf.RoundToInt(sourceClip.length * samplesPerSecond);
            float bodyLength = (bindHips.position - bindBones["mixamorig:Head"].position).magnitude * hipScale;
            var previous = new Dictionary<string, Quaternion>();
            for (int sample = 0; sample <= samples; sample++)
            {
                float time = sample / (float)samplesPerSecond;
                sourceClip.SampleAnimation(source, time);
                targetHips.position = targetHipRest + alignment * (sourceBones["mixamorig:Hips"].position - bindHips.position) * hipScale;
                if ((targetHips.position - targetHipRest).magnitude > maxHipOffsetBodyLengths * bodyLength)
                    throw new System.InvalidOperationException("Pelag hip offset exceeds authored crouch envelope: " + name);
                for (int c = 0; c < 3; c++) hipPosition[c].AddKey(time, targetHips.localPosition[c]);
                foreach (var bone in targetBones)
                {
                    var sourceBone = sourceBones[bone.name];
                    bone.rotation = alignment * sourceBone.rotation * Quaternion.Inverse(sourceBind[bone.name])
                        * Quaternion.Inverse(alignment) * targetBind[bone.name];
                    Transform child = null;
                    for (int c = 0; c < bone.childCount; c++)
                    {
                        var candidate = bone.GetChild(c);
                        if (!sourceBones.ContainsKey(candidate.name)) continue;
                        if (child == null) child = candidate;
                        if (candidate.name.Contains("Spine") || candidate.name.Contains("Neck")) { child = candidate; break; }
                    }
                    if (child != null)
                    {
                        Vector3 wanted = alignment * (sourceBones[child.name].position - sourceBone.position);
                        bone.rotation = Quaternion.FromToRotation(child.position - bone.position, wanted) * bone.rotation;
                    }
                    Quaternion q = bone.localRotation;
                    // Кватернионы подряд — по одну сторону (иначе интерполяция ключей идёт длинной дугой).
                    if (previous.TryGetValue(bone.name, out Quaternion last) && Quaternion.Dot(last, q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous[bone.name] = q;
                    var channels = curves[bone.name];
                    channels[0].AddKey(time, q.x); channels[1].AddKey(time, q.y);
                    channels[2].AddKey(time, q.z); channels[3].AddKey(time, q.w);
                }
                foreach (var bone in targetBones)
                {
                    if (!sourceBones.TryGetValue(bone.parent.name, out var parent)) continue;
                    Vector3 expected = alignment * (sourceBones[bone.name].position - parent.position);
                    float error = Vector3.Angle(expected, bone.position - bone.parent.position);
                    if (error > worstDirectionError) { worstDirectionError = error; worstBone = bone.name; }
                }
            }
            foreach (var bone in targetBones)
            {
                string bonePath = AnimationUtility.CalculateTransformPath(bone, target.transform);
                var channels = curves[bone.name];
                for (int c = 0; c < 4; c++)
                {
                    for (int k = 0; k < channels[c].length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(channels[c], k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(channels[c], k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform),
                        "m_LocalRotation." + "xyzw"[c]), channels[c]);
                }
            }
            clip.EnsureQuaternionContinuity();
            for (int c = 0; c < 3; c++)
            {
                for (int k = 0; k < hipPosition[c].length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(hipPosition[c], k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(hipPosition[c], k, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                    AnimationUtility.CalculateTransformPath(targetHips, target.transform), typeof(Transform),
                    "m_LocalPosition." + "xyz"[c]), hipPosition[c]);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log($"[Pelag anchor-throw dense] {name}: {samples + 1} samples at {samplesPerSecond}/s, {targetBones.Length} bones, "
                      + $"max segment error {worstDirectionError:F3} deg at {worstBone}");
            return clip;
        }
        finally { Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
    }

    private static Quaternion BodyBasis(Dictionary<string, Transform> bones)
    {
        Vector3 up = (bones["mixamorig:Head"].position - bones["mixamorig:Hips"].position).normalized;
        Vector3 right = (bones["mixamorig:LeftArm"].position - bones["mixamorig:RightArm"].position).normalized;
        return Quaternion.LookRotation(Vector3.Cross(right, up), up);
    }
}
