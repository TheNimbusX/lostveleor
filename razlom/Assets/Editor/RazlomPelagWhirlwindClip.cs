using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>One complete pivot, followed by a short, continuous recovery.</summary>
public static class RazlomPelagWhirlwindClip
{
    public static AnimationClip Build(AnimationClip source)
    {
        if (source == null) return null;
        float contact = Game.View.CharacterAnimatorView.WhirlwindContactTime;
        float duration = Game.View.CharacterAnimatorView.WhirlwindClipDuration;
        var timing = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.12f, 0.15f),
            new Keyframe(contact, 0.52f), new Keyframe(0.48f, 0.72f),
            new Keyframe(0.62f, 1.05f), new Keyframe(duration, source.length));
        return BuildTimed(source, "Pelag_Whirlwind_Timed", duration, timing);
    }

    public static AnimationClip BuildTimed(AnimationClip source, string name, float duration, AnimationCurve timing,
        float heightScale = 1f)
    {
        if (source == null) return null;
        string path = "Assets/Resources/Characters/Pelag_v5/" + name + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.name = name;
        clip.frameRate = 60f;
        for (int i = 0; i < timing.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(timing, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(timing, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve input = AnimationUtility.GetEditorCurve(source, binding);
            var output = new AnimationCurve();
            int frames = Mathf.CeilToInt(duration * 120f);
            for (int frame = 0; frame <= frames; frame++)
            {
                float t = duration * frame / frames;
                float value = input.Evaluate(timing.Evaluate(t));
                if (binding.path.EndsWith("mixamorig:Hips") && binding.propertyName == "m_LocalPosition.y")
                    value = input.Evaluate(0f) + (value - input.Evaluate(0f)) * heightScale;
                output.AddKey(t, value);
            }
            Keyframe[] sampled = output.keys;
            var keep = new SortedSet<int> { 0, sampled.Length - 1 };
            Reduce(sampled, 0, sampled.Length - 1, keep);
            output = new AnimationCurve();
            foreach (int index in keep) output.AddKey(sampled[index]);
            for (int key = 0; key < output.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(output, key, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(output, key, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, binding, output);
        }
        AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void Reduce(Keyframe[] keys, int first, int last, SortedSet<int> keep)
    {
        float error = 0.00004f;
        int split = -1;
        for (int i = first + 1; i < last; i++)
        {
            float value = Mathf.Lerp(keys[first].value, keys[last].value,
                Mathf.InverseLerp(keys[first].time, keys[last].time, keys[i].time));
            float deviation = Mathf.Abs(value - keys[i].value);
            if (deviation > error) { error = deviation; split = i; }
        }
        if (split < 0) return;
        keep.Add(split);
        Reduce(keys, first, split, keep);
        Reduce(keys, split, last, keep);
    }
}
