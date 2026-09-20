using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public sealed partial class PelagVfxController
    {
        private TrailRenderer _skewerWake;
        private Material _skewerWakeMaterial;
        private bool _skewerWakeActive;

        private void PrepareSkewerWake()
        {
            var source = Resources.Load<Material>("VFX/Pelag/Materials/M_LeapStroke");
            if (source == null) return;
            _skewerWakeMaterial = new Material(source) { name = "Skewer steel wake" };
            _skewerWakeMaterial.SetColor("_BaseColor", new Color(.23f,.38f,.43f,1));
            _skewerWakeMaterial.SetColor("_CoreColor", new Color(1,.95f,.77f,1));
            _skewerWakeMaterial.SetFloat("_Emission", 1.6f);
            _skewerWakeMaterial.SetFloat("_Opacity", .8f);
            var root = new GameObject("Skewer wake"); root.transform.SetParent(transform, false);
            _skewerWake = root.AddComponent<TrailRenderer>();
            _skewerWake.sharedMaterial = _skewerWakeMaterial;
            _skewerWake.time = .12f; _skewerWake.minVertexDistance = .025f;
            _skewerWake.widthMultiplier = .48f;
            _skewerWake.widthCurve = new AnimationCurve(new Keyframe(0,.3f), new Keyframe(.25f,1), new Keyframe(1,0));
            _skewerWake.startColor = Color.white; _skewerWake.endColor = new Color(1,1,1,0);
            _skewerWake.numCornerVertices = 4;
            _skewerWake.shadowCastingMode = ShadowCastingMode.Off;
            _skewerWake.receiveShadows = false; _skewerWake.emitting = false;
        }
        private void BeginSkewerWake()
        {
            if (_skewerWake == null || CaptureRig.NoVfx) return;
            _skewerWake.transform.position = PlayerPosition() + Vector3.up*.72f;
            _skewerWake.Clear(); _skewerWakeActive = true;
        }
        private void UpdateSkewerWake()
        {
            if (_skewerWake == null) return;
            var sim = _driver.Sim;
            bool moving = _skewerWakeActive && sim != null &&
                sim.Entities.ForcedKind[Simulation.PlayerId] == (byte)ForcedMotionKind.Skewer;
            _skewerWake.transform.position = PlayerPosition() + Vector3.up*.72f;
            _skewerWake.emitting = moving && !CaptureRig.NoVfx;
            if (!moving) _skewerWakeActive = false;
        }
        private void StopSkewerWake()
        {
            _skewerWakeActive = false;
            if (_skewerWake != null) { _skewerWake.emitting = false; _skewerWake.Clear(); }
        }
        private void OnDestroy()
        {
            if (_skewerWakeMaterial != null) Destroy(_skewerWakeMaterial);
        }
    }
}
