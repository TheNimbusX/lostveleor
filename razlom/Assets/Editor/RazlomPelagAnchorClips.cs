using System.Linq;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>Запекает цельные позы якорных действий на игровом скелете.</summary>
public static class RazlomPelagAnchorClips
{
    private static AnimationCurve Curve(params float[] values)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < values.Length; i += 2) curve.AddKey(values[i], values[i + 1]);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        return curve;
    }

    public static AnimationClip Build(AnimationClip idle, bool leap)
    {
        if (idle == null) return null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        var sample = Object.Instantiate(prefab);
        sample.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in sample.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var bones = sample.GetComponentsInChildren<Transform>().Where(b => b.name.StartsWith("mixamorig:")).ToArray();
            Transform Bone(string name) => bones.First(b => b.name == "mixamorig:" + name);
            Transform hips = Bone("Hips"), spine = Bone("Spine"), chest = Bone("Spine2"), head = Bone("Head");
            Transform leftFoot = Bone("LeftFoot"), rightFoot = Bone("RightFoot");
            Transform leftToe = Bone("LeftToeBase"), rightToe = Bone("RightToeBase");
            idle.SampleAnimation(sample, 0f);
            Vector3 initialHips = hips.position, leftStart = leftToe.position, rightStart = rightToe.position;
            Quaternion leftRotation = leftFoot.rotation, rightRotation = rightFoot.rotation;
            // Метры позы заданы для игрового роста. FBX отображается с масштабом 1.82.
            const float unit = 1f / 1.82f;
            float contact = leap ? PelagAbilityTiming.LeapWindup : PelagAbilityTiming.SweepWindup;
            float arrival = leap ? PelagAbilityTiming.LeapArrival : contact + PelagAbilityTiming.SweepTravel;
            float duration = leap ? PelagAbilityTiming.LeapRecovery : PelagAbilityTiming.SweepRecovery;
            var height = leap
                ? Curve(0,0, .12f,-.07f, .24f,-.13f, contact,0, .48f,.20f, .64f,.16f, arrival,-.02f, arrival+.065f,-.14f, duration,0)
                : Curve(0,0, .14f,-.08f, contact,-.035f, contact+.14f,-.14f, arrival,-.065f, duration,0);
            var forward = leap
                ? Curve(0,0, .14f,-.07f, .25f,-.10f, contact,.05f, .50f,.13f, arrival,.07f, duration,0)
                : Curve(0,0, .16f,-.09f, contact,.07f, contact+.18f,-.14f, arrival,-.09f, duration,0);
            var yaw = Curve(0,0, .13f,18, .22f,24, contact,-13, arrival,-7, duration,0);
            var pitch = leap
                ? Curve(0,0, .18f,-9, contact,12, .48f,29, .67f,22, arrival,8, arrival+.07f,17, duration,0)
                : Curve(0,0, .18f,-7, contact,10, contact+.17f,-14, arrival,-8, duration,0);
            var shoulder = Curve(0,0, .13f,14, .22f,22, contact,-20, arrival,9, duration,0);
            var curves = new AnimationCurve[bones.Length, 7];
            for (int b = 0; b < bones.Length; b++)
                for (int p = 0; p < 7; p++) curves[b,p] = new AnimationCurve();
            int frames = Mathf.CeilToInt(duration * 60f);
            for (int frame = 0; frame <= frames; frame++)
            {
                float t = duration * frame / frames;
                // Один нейтральный кадр — опора ключевых поз, а не скрытое проигрывание idle.
                idle.SampleAnimation(sample, 0f);
                hips.position = initialHips + new Vector3(-.035f * Mathf.Sin(t / duration * Mathf.PI), height.Evaluate(t), forward.Evaluate(t)) * unit;
                hips.rotation = Quaternion.Euler(pitch.Evaluate(t), yaw.Evaluate(t), 0) * hips.rotation;
                spine.rotation = Quaternion.AngleAxis(shoulder.Evaluate(t) * .4f, Vector3.up) * spine.rotation;
                chest.rotation = Quaternion.AngleAxis(shoulder.Evaluate(t) * .6f, Vector3.up) * chest.rotation;
                head.rotation = Quaternion.AngleAxis(-yaw.Evaluate(t) * .65f - shoulder.Evaluate(t) * .7f, Vector3.up) * head.rotation;
                float stance = Smooth(t / .24f) * (1f - Smooth((t - arrival) / (duration - arrival)));
                Vector3 left = Vector3.Lerp(leftStart, new Vector3(-.23f,0,.17f) * unit, stance);
                Vector3 right = Vector3.Lerp(rightStart, new Vector3(.23f,0,-.17f) * unit, stance);
                if (leap)
                {
                    float flight = Mathf.Clamp01((t - contact) / PelagAbilityTiming.LeapTravel);
                    // Колени отстают от тяги, затем передняя стопа заранее ищет землю.
                    float trail = Mathf.Sin(flight * Mathf.PI);
                    left += new Vector3(0, .22f * trail, -.25f * trail) * unit;
                    right += new Vector3(0, .48f * trail, -.22f * trail) * unit;
                }
                else
                {
                    // Передняя нога переступает в замахе; задняя принимает вес на тяге.
                    left.y += .055f * Mathf.Sin(Mathf.Clamp01(t / .24f) * Mathf.PI) * unit;
                    right.y += .04f * Mathf.Sin(Mathf.Clamp01((t - arrival) / (duration - arrival)) * Mathf.PI) * unit;
                }
                Plant(Bone("LeftUpLeg"), Bone("LeftLeg"), leftFoot, leftToe, left, leftRotation, -1);
                Plant(Bone("RightUpLeg"), Bone("RightLeg"), rightFoot, rightToe, right, rightRotation, 1);
                float release = Smooth((t - .20f) / (contact - .20f));
                float haul = Smooth((t - contact) / (arrival - contact));
                float recover = Smooth((t - arrival) / (duration - arrival));
                Vector3 hand = Vector3.Lerp(new Vector3(-.32f,1.25f,-.15f), new Vector3(-.20f,1.12f,.64f), release);
                hand = Vector3.Lerp(hand, leap ? new Vector3(-.20f,1.12f,.54f) : new Vector3(-.28f,.96f,.16f), haul);
                float armWeight = Smooth(t / .20f) * (1f - recover);
                Transform leftHand = Bone("LeftHand"), rightHand = Bone("RightHand");
                Solve(Bone("LeftArm"), Bone("LeftForeArm"), leftHand,
                    Vector3.Lerp(leftHand.position, hand * unit, armWeight), new Vector3(-1,0,.15f));
                Solve(Bone("RightArm"), Bone("RightForeArm"), rightHand,
                    Vector3.Lerp(rightHand.position, (hand + new Vector3(.20f,0,.12f)) * unit,
                        Smooth((t-.24f)/.18f) * (1f-recover)), new Vector3(1,0,-.15f));
                for (int b = 0; b < bones.Length; b++)
                {
                    Vector3 position = bones[b].localPosition;
                    Quaternion rotation = bones[b].localRotation;
                    float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                    for (int p = 0; p < 7; p++) curves[b,p].AddKey(t, values[p]);
                }
            }
            string name = leap ? "Pelag_AnchorLeap_Timed" : "Pelag_AnchorSweep_Timed";
            string path = "Assets/Resources/Characters/Pelag_v5/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.name = name; clip.frameRate = 60f;
            string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int b = 0; b < bones.Length; b++)
                for (int p = 0; p < 7; p++)
                {
                    var curve = curves[b,p];
                    for (int k = 0; k < curve.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(bones[b],sample.transform),typeof(Transform),properties[p]),curve);
                }
            AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
            clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip);
            return clip;
        }
        finally { Object.DestroyImmediate(sample); }
    }

    private static float Smooth(float t) => Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
    private static void Plant(Transform hip, Transform knee, Transform ankle, Transform toe,
        Vector3 target, Quaternion rotation, float side)
    {
        ankle.rotation = rotation;
        Solve(hip,knee,ankle,ankle.position + target-toe.position,new Vector3(side*.25f,0,1));
        ankle.rotation = rotation;
    }
    private static void Solve(Transform root, Transform middle, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 origin = root.position, joint = middle.position;
        float a = Vector3.Distance(origin,joint), b = Vector3.Distance(joint,end.position);
        Vector3 delta = target-origin;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a-b)+.001f,(a+b)*.98f);
        Vector3 direction = delta.normalized;
        float along = (a*a-b*b+distance*distance)/(2*distance);
        Vector3 bend = Vector3.ProjectOnPlane(pole,direction).normalized;
        Vector3 desired = origin + direction*along + bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        root.rotation = Quaternion.FromToRotation(joint-origin,desired-origin)*root.rotation;
        middle.rotation = Quaternion.FromToRotation(end.position-middle.position,origin+direction*distance-middle.position)*middle.rotation;
    }
}
