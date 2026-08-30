using UnityEngine;

namespace Game.View
{
    /// <summary>Короткие camera-impulse и zoom-punch только для важных ударов.</summary>
    [DefaultExecutionOrder(2000)]
    [RequireComponent(typeof(Camera))]
    public sealed class CombatCameraJuice : MonoBehaviour
    {
        private Camera _camera;
        private Quaternion _restRotation;
        private Vector3 _appliedOffset;
        private float _restSize;
        private float _trauma;
        private float _zoomPunch;
        private uint _noise = 0xA341316Cu;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _restRotation = transform.rotation;
            _restSize = _camera.orthographicSize;
        }

        public void AddImpulse(float trauma, float zoomPunch)
        {
            _trauma = Mathf.Clamp01(Mathf.Max(_trauma, trauma));
            _zoomPunch = Mathf.Max(_zoomPunch, zoomPunch);
        }

        /// <summary>Capture-only framing hook; normal gameplay never calls it.</summary>
        public void SetBaseOrthographicSize(float size)
        {
            if (size <= 0f) return;
            _restSize = size;
            if (_camera != null) _camera.orthographicSize = size;
        }

        private void LateUpdate()
        {
            transform.position -= _appliedOffset;
            _appliedOffset = Vector3.zero;

            float dt = Time.unscaledDeltaTime;
            _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 3.8f);
            _zoomPunch = Mathf.MoveTowards(_zoomPunch, 0f, dt * 5.5f);

            float strength = _trauma * _trauma;
            if (strength > 0.0001f)
            {
                Vector2 n = new Vector2(SignedNoise(), SignedNoise());
                _appliedOffset = (transform.right * n.x + transform.up * n.y) * (0.24f * strength);
                transform.position += _appliedOffset;
                transform.rotation = _restRotation * Quaternion.Euler(0f, 0f, SignedNoise() * strength * 0.65f);
            }
            else
            {
                transform.rotation = _restRotation;
            }

            _camera.orthographicSize = _restSize * (1f - _zoomPunch * 0.055f);
        }

        private float SignedNoise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return ((_noise & 0xFFFFu) / 32767.5f) - 1f;
        }
    }
}
