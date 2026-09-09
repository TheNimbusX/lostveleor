using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Grounded source idle and a reversible pair of planted-foot steps.</summary>
public static class RazlomPelagStanceClips
{
    private const string Folder = "Assets/Resources/Characters/Pelag_v5/";
    private static readonly string[] Properties = {
        "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
        "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };

    private struct Pose
    {
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public Pose(Transform t) { Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale; }
        public void Apply(Transform t) { t.localPosition = Position; t.localRotation = Rotation; t.localScale = Scale; }
    }

    public static AnimationClip Build(AnimationClip source, AnimationClip relaxed, out AnimationClip steps)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder.Replace("Pelag_v5/", "Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"));
        var rig = UnityEngine.Object.Instantiate(prefab);
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in rig.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var all = rig.GetComponentsInChildren<Transform>();
            Transform Bone(string name) => all.First(t => t.name == "mixamorig:" + name);
            var hips = Bone("Hips");
            var tracks = all.Where(t => t != rig.transform && (t.name.StartsWith("mixamorig:") || hips.IsChildOf(t))).ToArray();
            var containers = tracks.Where(t => !t.name.StartsWith("mixamorig:")).ToArray();
            var left = Bone("LeftFoot"); var right = Bone("RightFoot");
            var leftToe = Bone("LeftToeBase"); var rightToe = Bone("RightToeBase");
            relaxed.SampleAnimation(rig, 0f);
            var containerRest = containers.Select(t => new Pose(t)).ToArray();
            var rootRest = new Pose(rig.transform);
            Vector3 relaxedCentre = (left.position + right.position) * .5f;
            float floor = Mathf.Min(leftToe.position.y, rightToe.position.y);
            var start = tracks.Select(t => new Pose(t)).ToArray();
            Vector3 leftStart = left.position, rightStart = right.position;
            Quaternion leftStartRotation = left.rotation, rightStartRotation = right.rotation;

            void SampleSource(float time)
            {
                source.SampleAnimation(rig, time);
                rootRest.Apply(rig.transform);
                for (int i = 0; i < containers.Length; i++) containerRest[i].Apply(containers[i]);
            }
            SampleSource(0f);
            Vector3 centre = (left.position + right.position) * .5f;
            Vector3 correction = new Vector3(relaxedCentre.x - centre.x,
                floor - Mathf.Min(leftToe.position.y, rightToe.position.y), relaxedCentre.z - centre.z);
            Vector3 idleLeftPlant = left.position + correction, idleRightPlant = right.position + correction;
            Quaternion idleLeftRotation = left.rotation, idleRightRotation = right.rotation;
            var idleCurves = Curves(tracks.Length);
            int frames = Mathf.RoundToInt(source.length * 30f);
            for (int frame = 0; frame <= frames; frame++)
            {
                float time = source.length * frame / frames;
                SampleSource(time);
                hips.position += correction;
                hips.position -= Vector3.up * Mathf.Max(RequiredLowering(left, idleLeftPlant), RequiredLowering(right, idleRightPlant));
                Solve(left, idleLeftPlant); Solve(right, idleRightPlant);
                left.rotation = idleLeftRotation; right.rotation = idleRightRotation;
                Record(idleCurves, tracks, time);
            }
            var idle = Save("Pelag_KnifeIdle_Grounded", rig.transform, tracks, idleCurves, true);
            idle.SampleAnimation(rig, 0f);
            var end = tracks.Select(t => new Pose(t)).ToArray();
            Vector3 leftEnd = left.position, rightEnd = right.position;
            Quaternion leftEndRotation = left.rotation, rightEndRotation = right.rotation;
            float lift = Vector3.Distance(left.position, left.parent.position) * .18f;
            var stepCurves = Curves(tracks.Length);
            float worstPlant = 0f;
            const int stepFrames = 60;
            for (int frame = 0; frame <= stepFrames; frame++)
            {
                float p = frame / (float)stepFrames, blend = Smooth(p);
                for (int i = 0; i < tracks.Length; i++)
                {
                    tracks[i].localPosition = Vector3.Lerp(start[i].Position, end[i].Position, blend);
                    tracks[i].localRotation = Quaternion.Slerp(start[i].Rotation, end[i].Rotation, blend);
                    tracks[i].localScale = Vector3.Lerp(start[i].Scale, end[i].Scale, blend);
                }
                float lp = Mathf.Clamp01((p - .04f) / .40f);
                float rp = Mathf.Clamp01((p - .54f) / .40f);
                Vector3 lt = Vector3.Lerp(leftStart, leftEnd, Smooth(lp)) + Vector3.up * (Mathf.Sin(lp * Mathf.PI) * lift);
                Vector3 rt = Vector3.Lerp(rightStart, rightEnd, Smooth(rp)) + Vector3.up * (Mathf.Sin(rp * Mathf.PI) * lift);
                // Keep both targets reachable while the pelvis changes stance.
                float lower = Mathf.Max(RequiredLowering(left, lt), RequiredLowering(right, rt));
                hips.position -= Vector3.up * lower;
                Solve(left, lt); Solve(right, rt);
                left.rotation = Quaternion.Slerp(leftStartRotation, leftEndRotation, Smooth(lp));
                right.rotation = Quaternion.Slerp(rightStartRotation, rightEndRotation, Smooth(rp));
                if (lp == 0f || lp == 1f) worstPlant = Mathf.Max(worstPlant, Vector3.Distance(left.position, lt));
                if (rp == 0f || rp == 1f) worstPlant = Mathf.Max(worstPlant, Vector3.Distance(right.position, rt));
                Record(stepCurves, tracks, p * .675f);
            }
            steps = Save("Pelag_SaberStance_Steps", rig.transform, tracks, stepCurves, false);
            Debug.Log($"[Pelag stance] ground correction={correction:F4}, foot lift={lift:F4}, planted error={worstPlant:F6}");
            if (worstPlant > .003f) throw new InvalidOperationException("Saber stance step lost its support foot");
            return idle;
        }
        finally { UnityEngine.Object.DestroyImmediate(rig); }
    }

    private static float Smooth(float p) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p));
    private static float RequiredLowering(Transform foot, Vector3 target)
    {
        var knee = foot.parent; var thigh = knee.parent;
        float length = (Vector3.Distance(thigh.position, knee.position) + Vector3.Distance(knee.position, foot.position)) * .999f;
        Vector3 delta = thigh.position - target;
        float horizontal = delta.x * delta.x + delta.z * delta.z;
        return Mathf.Max(0f, delta.y - Mathf.Sqrt(Mathf.Max(.00001f, length * length - horizontal)));
    }
    private static void Solve(Transform foot, Vector3 target)
    {
        var knee = foot.parent; var thigh = knee.parent;
        Vector3 origin = thigh.position, elbow = knee.position;
        float a = Vector3.Distance(origin, elbow), b = Vector3.Distance(elbow, foot.position);
        Vector3 direction = (target - origin).normalized;
        float distance = Mathf.Clamp(Vector3.Distance(target, origin), Mathf.Abs(a - b) + .00001f, (a + b) * .9999f);
        Vector3 pole = Vector3.ProjectOnPlane(elbow - origin, direction).normalized;
        if (pole.sqrMagnitude < .1f) pole = Vector3.ProjectOnPlane(Vector3.forward, direction).normalized;
        float along = (a * a - b * b + distance * distance) / (2f * distance);
        Vector3 desiredKnee = origin + direction * along + pole * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        thigh.rotation = Quaternion.FromToRotation(elbow - origin, desiredKnee - origin) * thigh.rotation;
        knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, target - knee.position) * knee.rotation;
    }
    private static AnimationCurve[,] Curves(int count)
    {
        var curves = new AnimationCurve[count, 10];
        for (int i = 0; i < count; i++) for (int p = 0; p < 10; p++) curves[i, p] = new AnimationCurve();
        return curves;
    }
    private static void Record(AnimationCurve[,] curves, Transform[] tracks, float time)
    {
        for (int i = 0; i < tracks.Length; i++)
        {
            var t = tracks[i];
            for (int p = 0; p < 3; p++) curves[i, p].AddKey(time, t.localPosition[p]);
            for (int p = 0; p < 4; p++) curves[i, p + 3].AddKey(time, t.localRotation[p]);
            for (int p = 0; p < 3; p++) curves[i, p + 7].AddKey(time, t.localScale[p]);
        }
    }
    private static AnimationClip Save(string name, Transform root, Transform[] tracks, AnimationCurve[,] curves, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, Folder + name + ".anim"); }
        clip.ClearCurves(); clip.name = name; clip.frameRate = 60f;
        for (int i = 0; i < tracks.Length; i++) for (int p = 0; p < 10; p++)
        {
            var curve = curves[i, p];
            for (int k = 0; k < curve.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                AnimationUtility.CalculateTransformPath(tracks[i], root), typeof(Transform), Properties[p]), curve);
        }
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
        return clip;
    }
}
