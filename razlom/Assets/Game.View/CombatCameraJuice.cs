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

        // Микростоп (HeroHitFeedback, этап 4): до этого момента толчок не гаснет и не дрожит —
        // камера стоит в том сдвиге, куда её бросил удар. Часы — неигровые, как у затухания толчка.
        private float _holdUntil = -1f;
        private bool _holdCaptured;
        private Vector2 _heldNoise;
        private float _heldRoll;

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

        /// <summary>
        /// Микростоп камеры на <paramref name="seconds"/>: толчок этого кадра замирает на месте (не гаснет
        /// и не дрожит), потом идёт дальше как обычно. Только картинка: Sim, ввод и время не трогаются.
        /// При тряске 0 % в настройках камера стоит и так — держать нечего.
        /// </summary>
        public void Hold(float seconds)
        {
            if (seconds <= 0f) return;
            float until = Time.unscaledTime + seconds;
            if (until <= _holdUntil) return;
            // Новый стоп поверх идущего продолжает держать тот же сдвиг, а не прыгает в новый.
            if (Time.unscaledTime >= _holdUntil) _holdCaptured = false;
            _holdUntil = until;
        }

        /// <summary>Capture-only framing hook; normal gameplay never calls it.</summary>
        public void SetBaseOrthographicSize(float size)
        {
            if (size <= 0f) return;
            _restSize = size;
            _restRotation = transform.rotation;
            if (_camera != null) _camera.orthographicSize = size;
        }

        /// <summary>
        /// Базовый размер без запоминания поворота: кат-сцена (CameraFollow.SetCinematic) меняет его
        /// каждый кадр, и посреди тряски SetBaseOrthographicSize вшил бы крен в покой камеры.
        /// </summary>
        public float BaseOrthographicSize
        {
            get => _restSize;
            set { if (value > 0f) _restSize = value; }
        }

        private void LateUpdate()
        {
            transform.position -= _appliedOffset;
            _appliedOffset = Vector3.zero;

            float dt = Time.unscaledDeltaTime;
            bool holding = Time.unscaledTime < _holdUntil;
            if (!holding)
            {
                _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 3.8f);
                _zoomPunch = Mathf.MoveTowards(_zoomPunch, 0f, dt * 5.5f);
            }

            // Сила тряски из настроек (0–100%): 0 — камера стоит, толчок зумом тоже гаснет.
            float shake = GameUserSettings.ScreenShake;
            float strength = _trauma * _trauma * shake;
            if (strength > 0.0001f)
            {
                Vector2 n;
                float roll;
                if (holding && _holdCaptured)
                {
                    n = _heldNoise;
                    roll = _heldRoll;
                }
                else
                {
                    n = new Vector2(SignedNoise(), SignedNoise());
                    roll = SignedNoise();
                    if (holding)
                    {
                        _heldNoise = n;
                        _heldRoll = roll;
                        _holdCaptured = true;
                    }
                }
                _appliedOffset = (transform.right * n.x + transform.up * n.y) * (0.24f * strength);
                transform.position += _appliedOffset;
                transform.rotation = _restRotation * Quaternion.Euler(0f, 0f, roll * strength * 0.65f);
            }
            else
            {
                transform.rotation = _restRotation;
            }

            _camera.orthographicSize = _restSize * (1f - _zoomPunch * 0.055f * shake);
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
