using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Rebakes the delivered turns as 90-degree in-place steps, parameterized by angle.</summary>
public static class RazlomPelagTurnClips
{
    public static AnimationClip Build(AnimationClip source, string name, float sign)
    {
        const string folder = "Assets/Resources/Characters/Pelag_v5";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        var sample = Object.Instantiate(prefab);
        sample.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (Animator animator in sample.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var bones = sample.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToArray();
            Transform hips = bones.First(t => t.name == "mixamorig:Hips");
            const int probes = 240, frames = 60;
            var angle = new float[probes + 1];
            source.SampleAnimation(sample, 0f);
            float initialYaw = hips.localEulerAngles.y;
            float previousYaw = initialYaw;
            float accumulated = 0f;
            for (int i = 1; i <= probes; i++)
            {
                source.SampleAnimation(sample, source.length * i / probes);
                float yaw = hips.localEulerAngles.y;
                accumulated += Mathf.DeltaAngle(previousYaw, yaw) * sign;
                angle[i] = Mathf.Max(angle[i - 1], accumulated);
                previousYaw = yaw;
            }
            if (angle[probes] < 85f) throw new System.InvalidOperationException("Turn does not cover 90 degrees: " + source.name);
            var curves = new AnimationCurve[bones.Length, 7];
            var firstPositions = new Vector3[bones.Length];
            var firstRotations = new Quaternion[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                for (int p = 0; p < 7; p++) curves[b, p] = new AnimationCurve();
            int probe = 1;
            for (int frame = 0; frame <= frames; frame++)
            {
                float phase = frame / (float)frames;
                float wanted = phase * 90f;
                while (probe < probes && angle[probe] < wanted) probe++;
                float sourceFrame = probe - 1 + Mathf.InverseLerp(angle[probe - 1], angle[probe], wanted);
                source.SampleAnimation(sample, source.length * sourceFrame / probes);
                Quaternion undoYaw = Quaternion.AngleAxis(-Mathf.DeltaAngle(initialYaw, hips.localEulerAngles.y), Vector3.up);
                hips.localRotation = undoYaw * hips.localRotation;
                hips.localPosition = undoYaw * hips.localPosition;
                for (int b = 0; b < bones.Length; b++)
                {
                    Vector3 pos = bones[b].localPosition;
                    Quaternion rot = bones[b].localRotation;
                    if (frame == 0) { firstPositions[b] = pos; firstRotations[b] = rot; }
                    // A 180-degree reversal consists of two steps. Close the
                    // neutralized pose so their seam cannot snap the skeleton.
                    float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, phase));
                    pos = Vector3.Lerp(pos, firstPositions[b], settle);
                    rot = Quaternion.Slerp(rot, firstRotations[b], settle);
                    float t = phase * 0.3f;
                    float[] values = { pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w };
                    for (int p = 0; p < 7; p++) curves[b, p].AddKey(t, values[p]);
                }
            }
            string path = folder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.name = name;
            clip.frameRate = 60;
            string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int b = 0; b < bones.Length; b++)
                for (int p = 0; p < 7; p++)
                {
                    var curve = curves[b, p];
                    for (int k = 0; k < curve.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(bones[b], sample.transform), typeof(Transform), properties[p]), curve);
                }
            clip.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(clip);
            return clip;
        }
        finally { Object.DestroyImmediate(sample); }
    }
}
