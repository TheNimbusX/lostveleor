using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(700)]
    public sealed class EnemyContactPose : MonoBehaviour
    {
        private Transform _chest;
        private Quaternion _authored;
        private bool _applied;
        private float _started = -10f, _strength;
        private Vector3 _axis;
        private EnemyKind _kind;
        private bool _heavy;

        public void Initialize()
        {
            // Только при создании тела: поиск костей не попадает в боевой кадр.
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                string key = bone.name.ToLowerInvariant();
                if (key.EndsWith("spine2") || key.EndsWith("chest")) { _chest = bone; break; }
                if (_chest == null && key.Contains("spine")) _chest = bone;
            }
        }

        public void Hit(Vector3 direction, float strength, EnemyKind kind, bool heavy = false)
        {
            if (_chest == null) return;
            if (_heavy && !heavy && Time.time - _started < .16f) return;
            _kind = kind;
            _heavy = heavy;
            // Новое попадание заменяет импульс; частые удары не складывают наклон.
            _strength = Mathf.Clamp01(strength);
            _started = Time.time;
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            _axis = axis.sqrMagnitude > 0.001f ? axis.normalized : transform.right;
        }

        private void Update() => RestorePose();

        private void LateUpdate()
        {
            if (_chest == null) return;
            float duration = _heavy ? .24f : _kind == EnemyKind.ForestRootSwarm ? 0.18f : 0.14f;
            float t = (Time.time - _started) / duration;
            if (t < 0f || t >= 1f) return;
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Sqrt(t)) * (1f - t);
            float angle = (_heavy ? 18f : _kind == EnemyKind.ForestRootSwarm ? 18f : 7f) * _strength * envelope;
            _authored = _chest.localRotation;
            Vector3 localAxis = _chest.parent != null
                ? _chest.parent.InverseTransformDirection(_axis) : _axis;
            _chest.localRotation = Quaternion.AngleAxis(angle, localAxis) * _authored;
            _applied = true;
        }

        public void Clear()
        {
            RestorePose();
            _started = -10f;
        }

        private void RestorePose()
        {
            if (_applied && _chest != null) _chest.localRotation = _authored;
            _applied = false;
        }

        private void OnDisable() => Clear();
    }
}
