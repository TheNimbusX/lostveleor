using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>Retimes the full source strokes without slowing the contact sweep.</summary>
public static class RazlomPelagSaberClips
{
    public static AnimationClip Build(AnimationClip source, string name, float contactSeconds)
    {
        const float duration = Game.View.CharacterAnimatorView.BasicAttackClipDuration;
        float contact = Game.Sim.Simulation.AttackWindupTicks / (float)Game.Sim.Simulation.TicksPerSecond;
        string path = "Assets/Resources/Characters/Pelag_v5/" + name + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.name = name;
        clip.frameRate = 60f;
        // Preserve anticipation, accelerate the last portion of the cut, then
        // carry the complete source recovery into the next 20-tick stroke.
        float[] times = { 0f, 0.08f, 0.20f, contact, 0.40f, 0.53f, duration };
        float tail = source.length - contactSeconds;
        float[] sourceTimes = { 0f, contactSeconds * 0.30f, contactSeconds * 0.55f,
            contactSeconds, contactSeconds + tail * 0.38f,
            contactSeconds + tail * 0.82f, source.length };
        // Монотонная кривая времени сохраняет контакт, но убирает скачки
        // скорости на границах замаха, маха и завершения.
        var timing = new AnimationCurve();
        for (int i = 0; i < times.Length; i++) timing.AddKey(times[i], sourceTimes[i]);
        for (int i = 0; i < timing.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(timing, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(timing, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve input = AnimationUtility.GetEditorCurve(source, binding);
            var output = new AnimationCurve();
            // 100 Hz includes every timing knot, including exact contact.
            for (int frame = 0; frame <= Mathf.RoundToInt(duration * 100f); frame++)
            {
                float t = frame / 100f;
                float at = timing.Evaluate(t);
                output.AddKey(t, input.Evaluate(at));
            }
            output = Reduce(output);
            for (int key = 0; key < output.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(output, key, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(output, key, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, binding, output);
        }
        // Source markers refer to the old time domain. Gameplay contact is a
        // Sim event; keep only a presentation cue at its new authored position.
        AnimationUtility.SetAnimationEvents(clip, new[] {
            new AnimationEvent { time = contact, functionName = "AttackContactCue" } });
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimationCurve Reduce(AnimationCurve curve)
    {
        Keyframe[] keys = curve.keys;
        var keep = new SortedSet<int> { 0, 8, 20, 30, 40, 53, 64 };
        int[] knots = { 0, 8, 20, 30, 40, 53, 64 };
        for (int i = 1; i < knots.Length; i++) ReduceSegment(keys, knots[i - 1], knots[i], keep);
        var reduced = new AnimationCurve();
        foreach (int index in keep) reduced.AddKey(keys[index]);
        return reduced;
    }

    private static void ReduceSegment(Keyframe[] keys, int first, int last, SortedSet<int> keep)
    {
        float error = 0.00002f;
        int split = -1;
        for (int i = first + 1; i < last; i++)
        {
            float linear = Mathf.Lerp(keys[first].value, keys[last].value,
                Mathf.InverseLerp(keys[first].time, keys[last].time, keys[i].time));
            float deviation = Mathf.Abs(keys[i].value - linear);
            if (deviation > error) { error = deviation; split = i; }
        }
        if (split < 0) return;
        keep.Add(split);
        ReduceSegment(keys, first, split, keep);
        ReduceSegment(keys, split, last, keep);
    }
}
