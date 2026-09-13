using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Доставание левой рукой и зеркальный фрагмент поливания; ноги остаются в стойке.</summary>
public static class RazlomPelagBlazeClip
{
    private const string Folder = "Assets/Resources/Characters/Pelag_v5/";
    public static AnimationClip Build(AnimationClip idle, AnimationClip draw, AnimationClip pour)
    {
        if (draw == null || pour == null || draw.length < 1f || pour.length < 1f)
            throw new InvalidOperationException("Blaze: нужны полные тейки Drawing Gun и Bartending");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        var rig = UnityEngine.Object.Instantiate(prefab);
        var donor = UnityEngine.Object.Instantiate(prefab);
        try
        {
            foreach (var a in rig.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var a in donor.GetComponentsInChildren<Animator>()) a.enabled = false;
            var bones = rig.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("mixamorig:")).ToArray();
            var other = donor.GetComponentsInChildren<Transform>();
            Transform Find(string n) => other.First(t => t.name == n);
            idle.SampleAnimation(rig, 0);
            var positions = bones.Select(t => t.localPosition).ToArray();
            var rotations = bones.Select(t => t.localRotation).ToArray();
            var curves = new AnimationCurve[bones.Length, 7];
            for (int i=0;i<bones.Length;i++) for(int j=0;j<7;j++) curves[i,j]=new AnimationCurve();
            const int frames = 120;
            for (int frame=0;frame<=frames;frame++)
            {
                float time=frame/60f;
                for(int i=0;i<bones.Length;i++) { bones[i].localPosition=positions[i]; bones[i].localRotation=rotations[i]; }
                float drawTime = time < .65f ? Mathf.Lerp(0, 39f/30f, Mathf.Clamp01(time/.65f))
                    : Mathf.Lerp(39f/30f, 12f/30f, Mathf.InverseLerp(1.3f,1.82f,time));
                draw.SampleAnimation(donor, drawTime);
                float weight = Smooth(time/.14f) * (1-Smooth((time-1.82f)/.18f));
                for(int i=0;i<bones.Length;i++)
                    if(bones[i].name.Contains(":Left" ) && (bones[i].name.Contains("Arm") || bones[i].name.Contains("Hand") || bones[i].name.Contains("Shoulder")))
                        bones[i].localRotation=Quaternion.Slerp(rotations[i],Find(bones[i].name).localRotation,weight);
                // Меняется только рука: исходный бармен второй рукой держит стакан.
                float pouring = Smooth((time-.55f)/.2f) * (1-Smooth((time-1.3f)/.18f));
                pour.SampleAnimation(donor, Mathf.Lerp(9f/30f,29f/30f,Mathf.InverseLerp(.75f,1.3f,time)));
                foreach(var bone in bones)
                    if(bone.name.Contains(":Left") && (bone.name.Contains("Arm") || bone.name.Contains("Hand") || bone.name.Contains("Shoulder")))
                    {
                        Quaternion q=Find(bone.name.Replace(":Left",":Right")).localRotation;
                        bone.localRotation=Quaternion.Slerp(bone.localRotation,new Quaternion(q.x,-q.y,-q.z,q.w),pouring);
                    }
                for(int i=0;i<bones.Length;i++)
                {
                    for(int j=0;j<3;j++)curves[i,j].AddKey(time,bones[i].localPosition[j]);
                    for(int j=0;j<4;j++)curves[i,j+3].AddKey(time,bones[i].localRotation[j]);
                }
            }
            string path=Folder+"Pelag_Blaze_Pour.anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
            clip.ClearCurves();clip.name="Pelag_Blaze_Pour";clip.frameRate=60;
            string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
            for(int i=0;i<bones.Length;i++)for(int j=0;j<7;j++)
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i],rig.transform),typeof(Transform),props[j]),curves[i,j]);
            clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);
            Debug.Log($"[blaze-clips] draw={draw.length:F3}s pour={pour.length:F3}s result={clip.length:F3}s");
            return clip;
        }
        finally {UnityEngine.Object.DestroyImmediate(rig);UnityEngine.Object.DestroyImmediate(donor);}
    }
    private static float Smooth(float x) => Mathf.SmoothStep(0,1,Mathf.Clamp01(x));
}
