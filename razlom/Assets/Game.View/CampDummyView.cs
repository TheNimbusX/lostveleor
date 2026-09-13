using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DisallowMultipleComponent]
    public sealed class CampDummyView : MonoBehaviour
    {
        [Min(1)] public int Health = 2000;
        [Min(0)] public int Armor;
        [Range(0, 100)] public int FireResistance;
        public string Label = "Манекен";
        public Vector3 TargetOffset;
        [Min(0)] public float BarOffset = .25f;
        public int EntityId { get; internal set; } = -1;
        Renderer[] _renderers;
        Quaternion _restRotation;
        float _hitAt = -100;
        float _hitStrength;
        void Awake() { _restRotation = transform.localRotation; }
        public void ShowHit(bool periodic)
        {
            _hitAt = Time.unscaledTime;
            _hitStrength = periodic ? .25f : 1f;
        }
        void LateUpdate()
        {
            float elapsed = Time.unscaledTime - _hitAt;
            float recoil = elapsed < .5f ? Mathf.Sin(elapsed * 24) * Mathf.Exp(-elapsed * 9) * 7f * _hitStrength : 0;
            transform.localRotation = _restRotation * Quaternion.Euler(recoil,0,recoil * .45f);
        }
        void OnDisable() { transform.localRotation = _restRotation; }
        public Vector3 TargetPosition => transform.TransformPoint(TargetOffset);
        public Bounds VisualBounds
        {
            get
            {
                if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(TargetPosition, Vector3.zero);
                foreach (var r in _renderers) if (r != null) bounds.Encapsulate(r.bounds);
                return bounds;
            }
        }
        public Vector3 BarPosition { get { var b = VisualBounds; return new Vector3(b.center.x, b.max.y + BarOffset, b.center.z); } }
        internal CampDummyDefinition Definition => new CampDummyDefinition(
            CampTrainingView.Flat(TargetPosition), Mathf.Max(1, Health), Fix64.FromInt(Armor), Fix64.FromInt(FireResistance));
    }
}
