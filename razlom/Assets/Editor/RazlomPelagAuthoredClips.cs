using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Перенос запечённых Blender-поз на точные пути и bind pose игрового Generic-рига.</summary>
public static class RazlomPelagAuthoredClips
{
    public static AnimationClip Build(string name, bool loop = false)
    {
        string sourcePath = "Assets/Resources/Characters/Pelag_v5/Mixamo/" + name + ".fbx";
        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var targetModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        if (sourceClip == null || sourceModel == null || targetModel == null)
            throw new System.InvalidOperationException("Missing authored Pelag clip: " + name);
        var source = Object.Instantiate(sourceModel);
        var target = Object.Instantiate(targetModel);
        var bindModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_Bind.fbx");
        if (bindModel == null) throw new System.InvalidOperationException("Missing Blender bind reference");
        source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in source.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var animator in target.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var sourceBones = source.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
            var targetBones = target.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToArray();
            // FBX с action сохраняет первый ключ как позу модели. Вычитать его
            // вместо bind pose означало выпрямлять локти и превращать цикл в T-позу.
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
            clip.ClearCurves(); clip.name = name; clip.frameRate = 30;
            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (var bone in targetBones)
                curves[bone.name] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            int frames = Mathf.RoundToInt(sourceClip.length * 30);
            for (int frame = 0; frame <= frames; frame++)
            {
                float time = frame / 30f;
                sourceClip.SampleAnimation(source, time);
                targetHips.position = targetHipRest + alignment * (sourceBones["mixamorig:Hips"].position - bindHips.position) * hipScale;
                // Посадка опускает таз на 16 см при длине корпуса около 53 см.
                // Запас до 40% допускает присед, но ловит ошибку единиц FBX.
                if ((targetHips.position - targetHipRest).magnitude > .4f * (bindHips.position - bindBones["mixamorig:Head"].position).magnitude * hipScale)
                    throw new System.InvalidOperationException("Pelag hip offset exceeds authored crouch envelope: " + name);
                for (int c = 0; c < 3; c++) hipPosition[c].AddKey(time, targetHips.localPosition[c]);
                foreach (var bone in targetBones)
                {
                    var sourceBone = sourceBones[bone.name];
                    // FBX может менять локальные оси костей при экспорте.
                    // Перенос дельты в пространстве тела сохраняет и позу, и roll.
                    bone.rotation = alignment * sourceBone.rotation * Quaternion.Inverse(sourceBind[bone.name])
                        * Quaternion.Inverse(alignment) * targetBind[bone.name];
                    // Направление сегмента — измеримый контракт, независимый от
                    // roll/pre-rotation FBX. На развилке таза/груди ведёт позвоночник.
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
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                    AnimationUtility.CalculateTransformPath(targetHips, target.transform), typeof(Transform),
                    "m_LocalPosition." + "xyz"[c]), hipPosition[c]);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log($"[Pelag authored] {name}: {frames + 1} frames, {targetBones.Length} mapped bones, max segment error {worstDirectionError:F3} deg at {worstBone}");
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
