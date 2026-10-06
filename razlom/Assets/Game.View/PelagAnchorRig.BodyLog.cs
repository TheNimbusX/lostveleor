using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Журнал съёмки Крушения v4 (только -capture-live-skill): скрутка корпуса за кадр — рысканье таза (LeftUpLeg →
    /// RightUpLeg) и груди (LeftShoulder → RightShoulder) от взгляда корня и их разница, клип и кадр ленты, слои
    /// аниматора с весом. Ту же меру даёт проба клипа в редакторе (SampleAnimation) — сверка «игра = клип».
    /// </summary>
    public sealed partial class PelagAnchorRig
    {
        private Transform _logLShoulder, _logRShoulder;
        private bool _logBonesSearched;

        private string BodyLogLine()
        {
            if (!_logBonesSearched)
            {
                _logBonesSearched = true;
                foreach (var bone in GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "mixamorig:LeftShoulder") _logLShoulder = bone;
                    else if (bone.name == "mixamorig:RightShoulder") _logRShoulder = bone;
                }
            }
            if (_lUpLeg == null || _rUpLeg == null || _logLShoulder == null || _logRShoulder == null) return "";
            Vector3 forward = transform.forward; forward.y = 0f;
            float root = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float hips = Lateral(_lUpLeg.position, _rUpLeg.position) - 90f;
            float chest = Lateral(_logLShoulder.position, _logRShoulder.position) - 90f;
            var text = new System.Text.StringBuilder();
            text.Append($" body root=({transform.position.x:F2},{transform.position.z:F2}) yaw={root:F0} hips={Mathf.DeltaAngle(root, hips):F0} chest={Mathf.DeltaAngle(root, chest):F0} twist={Mathf.DeltaAngle(hips, chest):F0} clip={_wreckClip} f={_wreckFrame:F2}");
            if (_hips != null)
            {
                // Ось таза по самой кости (местная +X в мировых) и места бёдер в осях таза — сверка с клипом в редакторе.
                Vector3 axis = _hips.rotation * Vector3.right; axis.y = 0f;
                text.Append($" hipsAxis={Mathf.DeltaAngle(root, Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg - 90f):F0} hipsQ={_hips.localRotation.ToString("F3")} lUp={_lUpLeg.localPosition.ToString("F4")} rUp={_rUpLeg.localPosition.ToString("F4")} lUpS={_lUpLeg.lossyScale.ToString("F3")}");
            }
            if (_probes != null)
            {
                text.Append(" probe");
                foreach (var probe in _probes) text.Append($" {probe.Tag}={(probe.Frame == Time.frameCount ? Mathf.DeltaAngle(root, probe.Hips).ToString("F0") : "-")}");
            }
            if (_animator != null)
                for (int i = 0; i < _animator.layerCount; i++)
                {
                    float w = i == 0 ? 1f : _animator.GetLayerWeight(i);
                    if (w < .01f) continue;
                    AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(i);
                    text.Append($" L{i}={w:F2}:{info.fullPathHash}{(_animator.IsInTransition(i) ? ">" + _animator.GetNextAnimatorStateInfo(i).fullPathHash : "")}");
                }
            return text.ToString();
        }

        private int _releasedFrame = -1000;
        private PelagPoseProbe[] _probes;

        /// <summary>Съёмка: где между аниматором и ригом таз получает поворот (пробы до и после видов 1000/1005/1010/1020).</summary>
        private void InstallPoseProbes()
        {
            if (!CaptureRig.LiveSkill || _probes != null) return;
            _probes = new PelagPoseProbe[]
            {
                gameObject.AddComponent<PelagPoseProbeEarly>(), gameObject.AddComponent<PelagPoseProbe999>(),
                gameObject.AddComponent<PelagPoseProbe1001>(), gameObject.AddComponent<PelagPoseProbe1019>(),
            };
            foreach (var probe in _probes) probe.Bind(_lUpLeg, _rUpLeg);
        }

        /// <summary>Полторы секунды после отдачи: где голова (поза спины эквипа), чтобы видеть конец уборки.</summary>
        private void LogAfterRelease(Game.Sim.Simulation sim)
        {
            if (_equipment == null || _equipment.SlamHead == null) return;
            Debug.Log($"{Log} after-release tick={(sim != null ? sim.Tick : -1)} mode={_core.Mode} slamHead={_equipment.SlamHead.position.ToString("F3")} visible={_equipment.AnchorHeadVisible}{BodyLogLine()}");
        }

        private static float Lateral(Vector3 left, Vector3 right)
        {
            Vector3 d = right - left; d.y = 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>Проба съёмки: рысканье таза (LeftUpLeg → RightUpLeg) в своём месте порядка LateUpdate.</summary>
    public abstract class PelagPoseProbe : MonoBehaviour
    {
        private Transform _l, _r;
        public int Frame { get; private set; } = -1;
        public float Hips { get; private set; }
        public abstract string Tag { get; }
        public void Bind(Transform left, Transform right) { _l = left; _r = right; }
        private void LateUpdate()
        {
            if (_l == null || _r == null) return;
            Vector3 d = _r.position - _l.position; d.y = 0f;
            Hips = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 90f;
            Frame = Time.frameCount;
        }
    }

    [DefaultExecutionOrder(-900)] public sealed class PelagPoseProbeEarly : PelagPoseProbe { public override string Tag => "early"; }
    [DefaultExecutionOrder(999)] public sealed class PelagPoseProbe999 : PelagPoseProbe { public override string Tag => "p999"; }
    [DefaultExecutionOrder(1001)] public sealed class PelagPoseProbe1001 : PelagPoseProbe { public override string Tag => "p1001"; }
    [DefaultExecutionOrder(1019)] public sealed class PelagPoseProbe1019 : PelagPoseProbe { public override string Tag => "p1019"; }
}
