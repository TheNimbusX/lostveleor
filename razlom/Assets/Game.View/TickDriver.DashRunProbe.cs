using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ТОЛЬКО СЪЁМКА: проба стыка рывок → бег (TickDriver.DashRunCapture.cs). Каждый кадр
    /// после отрисовки — строка [dash-run-probe]: кадр видео, тик, случай и тик от нажатия,
    /// скорость и взгляд Sim, корень и показанный взгляд, состояния и веса слоёв, параметры
    /// бега, стопы и носки, таз в системе корня, наибольший скачок поворота кости за кадр
    /// (в системе корня — поворот всего тела сюда не входит).
    /// </summary>
    public sealed partial class TickDriver
    {
        private static readonly string[] DashRunPopBones =
        {
            "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2", "mixamorig:Neck", "mixamorig:Head",
            "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot", "mixamorig:RightUpLeg", "mixamorig:RightLeg",
            "mixamorig:RightFoot", "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:RightArm", "mixamorig:RightForeArm",
        };

        private static readonly (int Hash, string Name)[] DashRunStates =
        {
            (Animator.StringToHash("Base Layer.Dash_v5"), "Dash"), (Animator.StringToHash("Base Layer.Run_v5"), "Run"),
            (Animator.StringToHash("Base Layer.CombatIdle_v5"), "CIdle"), (Animator.StringToHash("Base Layer.RelaxedIdle_v5"), "RIdle"),
            (Animator.StringToHash("Base Layer.RunStart_v5"), "RunStart"), (Animator.StringToHash("Base Layer.RunStop_v5"), "RunStop"),
            (Animator.StringToHash("Base Layer.TurnLeft_v5"), "TurnL"), (Animator.StringToHash("Base Layer.TurnRight_v5"), "TurnR"),
            (Animator.StringToHash("Base Layer.Roll_v5"), "Roll"),
        };

        private static string DashRunStateName(int hash)
        {
            foreach (var state in DashRunStates) if (state.Hash == hash) return state.Name;
            return hash.ToString("x8");
        }

        private static string F(float value, string format = "F3") => value.ToString(format, CultureInfo.InvariantCulture);

        private IEnumerator DashRunProbe(int generation)
        {
            var wait = new WaitForEndOfFrame();
            string output = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-out") output = args[i + 1];
            string frames = output != null ? Path.Combine(output, "video_frames") : null;
            float shotWidth = 1920f, shotHeight = 1080f;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-capture-width") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotWidth);
                if (args[i] == "-capture-height") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotHeight);
            }
            int videoFrame = 0;
            var arena = GetComponent<ArenaView>();
            Transform body = null, hips = null, leftFoot = null, rightFoot = null, leftToe = null, rightToe = null;
            Transform[] popBones = new Transform[DashRunPopBones.Length];
            Quaternion[] popLast = new Quaternion[DashRunPopBones.Length];
            bool popValid = false;
            Animator animator = null; int recovery = -1;
            var line = new StringBuilder(768);
            while (Generation == generation && _dashRunCase < DashRunCases.Length)
            {
                yield return wait;
                if (frames != null)
                    while (File.Exists(Path.Combine(frames, "frame_" + videoFrame.ToString("0000", CultureInfo.InvariantCulture) + ".jpg"))) videoFrame++;
                var sim = Sim;
                if (sim == null || arena == null || !arena.TryGetEntityView(Simulation.PlayerId, out Transform view)) continue;
                if (view != body)
                {
                    body = view; popValid = false;
                    hips = leftFoot = rightFoot = leftToe = rightToe = null;
                    foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    {
                        switch (bone.name)
                        {
                            case "mixamorig:Hips": hips = bone; break;
                            case "mixamorig:LeftFoot": leftFoot = bone; break;
                            case "mixamorig:RightFoot": rightFoot = bone; break;
                            case "mixamorig:LeftToeBase": leftToe = bone; break;
                            case "mixamorig:RightToeBase": rightToe = bone; break;
                        }
                        int index = System.Array.IndexOf(DashRunPopBones, bone.name);
                        if (index >= 0) popBones[index] = bone;
                    }
                    animator = body.GetComponentInChildren<Animator>();
                    recovery = animator != null ? animator.GetLayerIndex("Recovery Footwork") : -1;
                    Debug.Log($"[dash-run-probe] bones hips={hips != null} feet={leftFoot != null}/{rightFoot != null}"
                        + $" toes={leftToe != null}/{rightToe != null} animator={animator != null} recovery={recovery}");
                }
                PelagDashState dash = sim.PelagDash;
                var entities = sim.Entities;
                Vector3 root = body.position;
                Quaternion rootRotation = body.rotation;
                Vector3 shown = Vector3.ProjectOnPlane(rootRotation * Quaternion.Euler(0f, -arena.ModelYaw, 0f) * Vector3.forward, Vector3.up);
                FixVec2 velocity = entities.Velocity[Simulation.PlayerId], facing = entities.Facing[Simulation.PlayerId];
                line.Clear();
                line.Append("[dash-run-probe] vf=").Append(videoFrame).Append(" t=").Append(F(Time.time))
                    .Append(" dt=").Append(F(Time.deltaTime, "F4")).Append(" tick=").Append(sim.Tick).Append(" a=").Append(F(Alpha, "F2"))
                    .Append(" case=").Append(DashRunLabel).Append(" stage=").Append(_dashRunStage)
                    .Append(" rel=").Append(_dashRunPressTick >= 0 ? sim.Tick - _dashRunPressTick : -999)
                    .Append(" serial=").Append(dash.Serial).Append(" dmov=").Append(dash.Moving ? 1 : 0).Append(" stop=").Append(dash.StopTick)
                    .Append(" forced=").Append(entities.ForcedTicksLeft[Simulation.PlayerId])
                    .Append(" sv=(").Append(F(velocity.X.ToFloat() * Simulation.TicksPerSecond)).Append(',')
                    .Append(F(velocity.Y.ToFloat() * Simulation.TicksPerSecond)).Append(')')
                    .Append(" syaw=").Append(F(Mathf.Atan2(facing.X.ToFloat(), facing.Y.ToFloat()) * Mathf.Rad2Deg, "F1"))
                    .Append(" vyaw=").Append(F(Mathf.Atan2(shown.x, shown.z) * Mathf.Rad2Deg, "F1"));
                AppendProbe(line, "root", root);
                Camera shot = Camera.main;
                if (shot != null)
                {
                    // Точка кадра (пиксели снимка, y сверху) на высоте таза — центр выреза листов.
                    Vector3 viewport = shot.WorldToViewportPoint(root + Vector3.up * .8f);
                    line.Append(" scr=(").Append(F(viewport.x * shotWidth, "F0")).Append(',').Append(F((1f - viewport.y) * shotHeight, "F0")).Append(')');
                }
                if (animator != null)
                {
                    var current = animator.GetCurrentAnimatorStateInfo(0);
                    line.Append(" st=").Append(DashRunStateName(current.fullPathHash)).Append('@').Append(F(current.normalizedTime, "F2"));
                    if (animator.IsInTransition(0))
                    {
                        var next = animator.GetNextAnimatorStateInfo(0);
                        line.Append("->").Append(DashRunStateName(next.fullPathHash)).Append('@').Append(F(next.normalizedTime, "F2"))
                            .Append('~').Append(F(animator.GetAnimatorTransitionInfo(0).normalizedTime, "F2"));
                    }
                    for (int layer = 1; layer < animator.layerCount; layer++)
                    {
                        float weight = animator.GetLayerWeight(layer);
                        if (weight < .02f) continue;
                        var clips = animator.GetCurrentAnimatorClipInfo(layer);
                        var state = animator.GetCurrentAnimatorStateInfo(layer);
                        line.Append(" L").Append(layer).Append('=').Append(clips.Length > 0 ? clips[0].clip.name.Replace("Pelag_", "") : "-")
                            .Append('@').Append(F(state.normalizedTime, "F2")).Append('w').Append(F(weight, "F2"));
                    }
                    if (recovery >= 0) line.Append(" rw=").Append(F(animator.GetLayerWeight(recovery), "F2"));
                    line.Append(" dp=").Append(F(animator.GetFloat("DashPhase")))
                        .Append(" mx=").Append(F(animator.GetFloat("MoveX"), "F2")).Append(" my=").Append(F(animator.GetFloat("MoveY"), "F2"))
                        .Append(" ms=").Append(F(animator.GetFloat("MoveSpeed"), "F2"))
                        .Append(" lps=").Append(F(animator.GetFloat("LocomotionPlaybackSpeed"), "F2"));
                }
                if (leftToe != null) AppendProbe(line, "lt", leftToe.position);
                if (rightToe != null) AppendProbe(line, "rt", rightToe.position);
                if (leftFoot != null) AppendProbe(line, "lf", leftFoot.position);
                if (rightFoot != null) AppendProbe(line, "rf", rightFoot.position);
                if (hips != null) AppendProbe(line, "hipsL", Quaternion.Inverse(rootRotation) * (hips.position - root));
                // Скачок позы: наибольший поворот кости за кадр в системе корня.
                float pop = 0f; string popName = "-";
                for (int i = 0; i < popBones.Length; i++)
                {
                    if (popBones[i] == null) continue;
                    Quaternion local = Quaternion.Inverse(rootRotation) * popBones[i].rotation;
                    if (popValid)
                    {
                        float angle = Quaternion.Angle(popLast[i], local);
                        if (angle > pop) { pop = angle; popName = DashRunPopBones[i].Replace("mixamorig:", ""); }
                    }
                    popLast[i] = local;
                }
                popValid = true;
                line.Append(" pop=").Append(F(pop, "F1")).Append(' ').Append(popName);
                Debug.Log(line.ToString());
            }
        }
    }
}
