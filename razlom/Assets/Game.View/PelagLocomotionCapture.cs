using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Foot trajectories from the actual player. Installed only by locomotion capture.</summary>
    public sealed class PelagLocomotionCapture : MonoBehaviour
    {
        private StreamWriter _writer;
        private string _directory;
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public void Initialize(string directory)
        {
            _directory = directory;
            StartCoroutine(Record());
        }

        private IEnumerator Record()
        {
            ArenaView arena = null;
            TickDriver driver = null;
            Transform body = null;
            while (body == null)
            {
                yield return null;
                arena = FindAnyObjectByType<ArenaView>();
                driver = FindAnyObjectByType<TickDriver>();
                if (arena != null && driver != null && driver.Sim != null)
                    arena.TryGetEntityView(Simulation.PlayerId, out body);
            }

            Directory.CreateDirectory(_directory);
            SampleClips(body.localScale);
            Animator animator = body.GetComponentInChildren<Animator>();
            Transform left = FindBone(body, "mixamorig:LeftToeBase");
            Transform right = FindBone(body, "mixamorig:RightToeBase");
            Transform hips = FindBone(body, "mixamorig:Hips");
            Transform leftKnee = FindBone(body, "mixamorig:LeftLeg");
            Transform rightKnee = FindBone(body, "mixamorig:RightLeg");
            if (animator == null || left == null || right == null)
                throw new InvalidOperationException("Pelag locomotion capture requires Animator and toe bones.");

            _writer = new StreamWriter(Path.Combine(_directory, "locomotion.csv")) { AutoFlush = true };
            _writer.WriteLine("time,tick,state,phase,transition,playback,moveX,moveY,rootX,rootY,rootZ,yaw,leftX,leftY,leftZ,rightX,rightY,rightZ,hipsYaw,turnPhase,leftKneeX,leftKneeY,leftKneeZ,rightKneeX,rightKneeY,rightKneeZ");
            var endOfFrame = new WaitForEndOfFrame();
            while (body != null && driver.Sim != null)
            {
                yield return endOfFrame;
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                string name = StateName(state);
                _writer.WriteLine(string.Join(",", new[] {
                    F(Time.time), driver.Sim.Tick.ToString(Culture), name, F(state.normalizedTime),
                    animator.IsInTransition(0) ? "1" : "0", F(animator.GetFloat("LocomotionPlaybackSpeed")),
                    F(animator.GetFloat("MoveX")), F(animator.GetFloat("MoveY")),
                    V(body.position), F(body.eulerAngles.y), V(left.position), V(right.position),
                    F(hips.eulerAngles.y), F(animator.GetFloat("TurnPhase")), V(leftKnee.position), V(rightKnee.position) }));
            }
        }

        private void SampleClips(Vector3 scale)
        {
            GameObject prefab = Resources.Load<GameObject>("Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig");
            GameObject sample = Instantiate(prefab);
            sample.name = "Pelag locomotion sample (capture only)";
            sample.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            sample.transform.localScale = scale;
            foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (Animator animator in sample.GetComponentsInChildren<Animator>()) animator.enabled = false;
            Transform left = FindBone(sample.transform, "mixamorig:LeftToeBase");
            Transform right = FindBone(sample.transform, "mixamorig:RightToeBase");
            try
            {
                using (var writer = new StreamWriter(Path.Combine(_directory, "clip-feet.csv")))
                {
                    writer.WriteLine("clip,time,length,leftX,leftY,leftZ,rightX,rightY,rightZ,hipsYaw,rootYaw");
                    Transform hips = FindBone(sample.transform, "mixamorig:Hips");
                    foreach (string name in new[] { "Run", "RunStart", "RunStop", "StrafeLeft", "StrafeRight", "StrafeBack", "TurnLeft", "TurnRight", "TurnLeft_InPlace", "TurnRight_InPlace" })
                    {
                        AnimationClip clip = Resources.LoadAll<AnimationClip>(
                            "Characters/Pelag_v5/Mixamo/Pelag_MX_" + name)
                            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                        if (clip == null && name.EndsWith("_InPlace", StringComparison.Ordinal))
                            clip = Resources.Load<AnimationClip>("Characters/Pelag_v5/Pelag_" + name);
                        if (clip == null) throw new InvalidOperationException("Missing locomotion clip: " + name);
                        int steps = Mathf.CeilToInt(clip.length * 240f);
                        for (int i = 0; i <= steps; i++)
                        {
                            float time = clip.length * i / steps;
                            clip.SampleAnimation(sample, time);
                            writer.WriteLine(name + "," + F(time) + "," + F(clip.length) + "," + V(left.position) + "," + V(right.position)
                                + "," + F(hips.eulerAngles.y) + "," + F(sample.transform.eulerAngles.y));
                        }
                    }
                }
            }
            finally { Destroy(sample); }
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>())
                if (bone.name == name) return bone;
            return null;
        }

        private static string StateName(AnimatorStateInfo state)
        {
            foreach (string name in new[] { "Run_v5", "RunStart_v5", "RunStop_v5", "RelaxedIdle_v5", "CombatIdle_v5", "TurnLeft_v5", "TurnRight_v5" })
                if (state.IsName("Base Layer." + name)) return name;
            return state.shortNameHash.ToString(Culture);
        }

        private static string F(float value) => value.ToString("F6", Culture);
        private static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private void OnDestroy() { _writer?.Dispose(); }
        private void OnApplicationQuit() { _writer?.Dispose(); }
    }
}
