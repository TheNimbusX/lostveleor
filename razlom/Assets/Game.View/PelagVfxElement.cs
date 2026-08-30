using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Кэш компонентов одного pooled-prefab. В бою здесь нет GetComponents,
    /// Instantiate или создания материалов: всё находится один раз на прогреве.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PelagVfxElement : MonoBehaviour
    {
        public PelagVfxId Id;
        [Min(0.05f)] public float DefaultLifetime = 0.35f;
        public bool DynamicLine;

        private LineRenderer[] _lines;
        private TrailRenderer[] _trails;
        private ParticleSystem[] _particles;
        private PelagChainLinkStrip _chainLinks;
        private Vector3 _initialScale;

        public LineRenderer PrimaryLine => _lines != null && _lines.Length > 0 ? _lines[0] : null;

        private void Awake()
        {
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _trails = GetComponentsInChildren<TrailRenderer>(true);
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _chainLinks = GetComponentInChildren<PelagChainLinkStrip>(true);
            _initialScale = transform.localScale;
        }

        public void Begin(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _initialScale;

            for (int i = 0; i < _lines.Length; i++)
            {
                if (DynamicLine) _lines[i].positionCount = 0;
                _lines[i].enabled = true;
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                _trails[i].Clear();
                _trails[i].emitting = true;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Clear(true);
                _particles[i].Play(true);
            }
            _chainLinks?.SetVisible(DynamicLine);
        }

        public void SetLine(Vector3 a, Vector3 b)
        {
            LineRenderer line = PrimaryLine;
            if (line == null) return;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        public void SetLine(Vector3 a, Vector3 bend, Vector3 b)
        {
            LineRenderer line = PrimaryLine;
            if (line == null) return;
            line.positionCount = 3;
            line.SetPosition(0, a);
            line.SetPosition(1, bend);
            line.SetPosition(2, b);
            _chainLinks?.SetChain(a, bend, b);
        }

        public void End()
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                if (DynamicLine) _lines[i].positionCount = 0;
                _lines[i].enabled = false;
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                _trails[i].emitting = false;
                _trails[i].Clear();
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _chainLinks?.SetVisible(false);
        }
    }
}
