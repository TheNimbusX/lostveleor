using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public sealed partial class PelagAnchorSlamView
    {
        private TrailRenderer _weightTrail;
        private Material _weightTrailMaterial;
        private void PrepareWeightTrail()
        {
            var source = Resources.Load<Material>("VFX/Pelag/Materials/M_LeapStroke");
            if (source == null) return;
            _weightTrailMaterial = new Material(source) { name = "Anchor displaced air" };
            _weightTrailMaterial.SetColor("_BaseColor", new Color(.15f,.22f,.27f,1));
            _weightTrailMaterial.SetColor("_CoreColor", new Color(.95f,.94f,.84f,1));
            _weightTrailMaterial.SetFloat("_Emission", 1.05f);
            _weightTrailMaterial.SetFloat("_Opacity", .55f);
            var root = new GameObject("Anchor weight stroke"); root.transform.SetParent(transform, false);
            _weightTrail = root.AddComponent<TrailRenderer>();
            _weightTrail.sharedMaterial = _weightTrailMaterial;
            _weightTrail.time = .095f; _weightTrail.minVertexDistance = .025f;
            _weightTrail.widthMultiplier = .36f;
            _weightTrail.widthCurve = new AnimationCurve(new Keyframe(0, .1f), new Keyframe(.24f, 1), new Keyframe(1, 0));
            _weightTrail.startColor = new Color(1,1,1,.8f); _weightTrail.endColor = new Color(1,1,1,0);
            _weightTrail.numCornerVertices = 4;
            _weightTrail.shadowCastingMode = ShadowCastingMode.Off; _weightTrail.receiveShadows = false;
            _weightTrail.emitting = false;
        }
        private void UpdateWeightTrail()
        {
            if (_weightTrail == null) return;
            _weightTrail.transform.position = _equipment.SlamHead.position;
            bool swing = !_returning && !CaptureRig.NoVfx && ClipTime > (_wreck ? .27f : .37f) && ClipTime < (_wreck ? .59f : .515f);
            if (swing && !_weightTrail.emitting) _weightTrail.Clear();
            _weightTrail.emitting = swing;
        }
        private void StopWeightTrail() { if (_weightTrail != null) { _weightTrail.emitting = false; _weightTrail.Clear(); } }
    }
}
