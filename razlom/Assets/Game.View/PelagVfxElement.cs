using UnityEngine.VFX;
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
        private const string ChainGlintName = "Chain Glint Flipbook";

        public PelagVfxId Id;
        [Min(0.05f)] public float DefaultLifetime = 0.35f;
        public bool DynamicLine;

        private LineRenderer[] _lines;
        private Gradient[] _lineGradients;
        private GradientColorKey[][] _lineColors;
        private GradientAlphaKey[][] _lineAlphas;
        private float[][] _lineBaseAlphas;
        private Vector3[][] _brushPoints;
        private TrailRenderer[] _trails;
        private ParticleSystem[] _particles;
        private VisualEffect[] _graphs;
        private PelagChainLinkStrip _chainLinks;
        private Transform _chainGlint;
        private Vector3 _initialScale;
        private readonly Vector3[] _chainPoints = new Vector3[33];
        private Vector3 _bendOffset, _bendVelocity;
        private readonly Vector3[] _chainVelocity = new Vector3[33];
        private bool _chainInitialized;

        public LineRenderer PrimaryLine => _lines != null && _lines.Length > 0 ? _lines[0] : null;

        private void Awake()
        {
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _lineGradients = new Gradient[_lines.Length];
            _lineColors = new GradientColorKey[_lines.Length][];
            _lineAlphas = new GradientAlphaKey[_lines.Length][];
            _lineBaseAlphas = new float[_lines.Length][];
            if (Id == PelagVfxId.WhirlwindRing || Id == PelagVfxId.ChainStepHit)
                _brushPoints = new Vector3[_lines.Length][];
            for (int i = 0; i < _lines.Length; i++)
            {
                _lineGradients[i] = _lines[i].colorGradient;
                _lineColors[i] = _lineGradients[i].colorKeys;
                _lineAlphas[i] = _lineGradients[i].alphaKeys;
                _lineBaseAlphas[i] = new float[_lineAlphas[i].Length];
                if (_brushPoints != null)
                {
                    _brushPoints[i] = new Vector3[_lines[i].positionCount];
                    _lines[i].GetPositions(_brushPoints[i]);
                }
                for (int k = 0; k < _lineAlphas[i].Length; k++)
                    _lineBaseAlphas[i][k] = _lineAlphas[i][k].alpha;
            }
            _trails = GetComponentsInChildren<TrailRenderer>(true);
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _graphs = GetComponentsInChildren<VisualEffect>(true);
            _chainLinks = GetComponentInChildren<PelagChainLinkStrip>(true);
            _chainGlint = transform.Find(ChainGlintName);
            _initialScale = transform.localScale;
        }

        public void Begin(Vector3 position, Quaternion rotation)
        {
            _chainInitialized = false;
            _bendVelocity = Vector3.zero;
            SetOpacity(1f);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _initialScale;
            foreach (var graph in _graphs) { graph.Reinit(); graph.Play(); }

            for (int i = 0; i < _lines.Length; i++)
            {
                if (DynamicLine) _lines[i].positionCount = 0;
                _lines[i].enabled = !DynamicLine || _chainLinks == null;
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                _trails[i].Clear();
                _trails[i].emitting = true;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Clear(false);
                _particles[i].Play(false);
            }
            _chainLinks?.SetVisible(DynamicLine);
        }

        public void SetOpacity(float opacity)
        {
            if (_lines == null) return;
            for (int i = 0; i < _lines.Length; i++)
            {
                for (int k = 0; k < _lineAlphas[i].Length; k++)
                    _lineAlphas[i][k].alpha = _lineBaseAlphas[i][k] * Mathf.Clamp01(opacity);
                _lineGradients[i].SetKeys(_lineColors[i], _lineAlphas[i]);
                _lines[i].colorGradient = _lineGradients[i];
            }
        }

        public void AnimateBrush(float age)
        {
            if (_brushPoints == null) return;
            for (int i = 0; i < _lines.Length; i++)
            {
                Vector3[] points = _brushPoints[i];
                if (points.Length < 2) continue;
                // Мазки расходятся с небольшой задержкой, оставляя просветы вокруг героя.
                float delay = (i % 3) * .022f;
                float reveal = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - delay) / .085f));
                float erase = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - delay - .10f) / .22f));
                for (int p = 0; p < points.Length; p++)
                {
                    float u = Mathf.Lerp(erase, Mathf.Max(erase,reveal), p / (float)(points.Length-1)) * (points.Length-1);
                    int index = Mathf.Min(points.Length-2,Mathf.FloorToInt(u));
                    _lines[i].SetPosition(p,Vector3.Lerp(points[index],points[index+1],u-index));
                }
            }
        }

        public void SetLine(Vector3 a, Vector3 b)
        {
            LineRenderer line = PrimaryLine;
            if (line == null) return;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            PlaceChainGlint(Vector3.Lerp(a, b, 0.5f), a, b);
        }

        public void SetLine(Vector3 a, Vector3 bend, Vector3 b)
        {
            SetLineProgress(a, bend, b, 1f);
        }

        public void SetLineProgress(Vector3 a, Vector3 bend, Vector3 b, float progress)
        {
            LineRenderer line = PrimaryLine;
            if (line == null) return;
            progress = Mathf.Clamp01(progress);
            // Узлы запаздывают отдельно, а не как один жёсткий изогнутый прут.
            // Ограничение отклонения сохраняет совпадение с игровой траекторией.
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float step = dt / steps;
            for (int i = 0; i < _chainPoints.Length; i++)
            {
                float t = progress * i / (_chainPoints.Length - 1);
                Vector3 wanted = Quadratic(a, bend, b, t);
                bool endpoint = i == 0 || i == _chainPoints.Length - 1;
                if (!_chainInitialized || endpoint)
                { _chainPoints[i] = wanted; _chainVelocity[i] = Vector3.zero; continue; }
                float belly = Mathf.Sin(t * Mathf.PI);
                for (int s = 0; s < steps; s++)
                {
                    _chainVelocity[i] += ((wanted - _chainPoints[i]) * 1300f
                        - _chainVelocity[i] * 48f + Vector3.down * (2f * belly)) * step;
                    _chainPoints[i] += _chainVelocity[i] * step;
                }
                _chainPoints[i] = wanted + Vector3.ClampMagnitude(_chainPoints[i] - wanted, .15f * belly);
            }
            _chainInitialized = true;
            line.positionCount = _chainPoints.Length;
            line.SetPositions(_chainPoints);
            _chainLinks?.SetPoints(_chainPoints);
            PlaceChainGlint(_chainPoints[_chainPoints.Length / 2], a, _chainPoints[_chainPoints.Length - 1]);
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 bend, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * bend + t * t * b;
        }

        public void SetCurvePoints(Vector3[] points)
        {
            if (PrimaryLine == null) return;
            PrimaryLine.positionCount = points.Length;
            PrimaryLine.SetPositions(points);
            _chainLinks?.SetPoints(points);
            if (_chainGlint != null) _chainGlint.gameObject.SetActive(false);
        }

        private void PlaceChainGlint(Vector3 position, Vector3 a, Vector3 b)
        {
            if (_chainGlint == null) return;
            _chainGlint.position = position;
            float scale = Mathf.Clamp(Vector3.Distance(a, b) / 2.5f, 0.65f, 1.25f);
            _chainGlint.localScale = Vector3.one * scale;
        }

        public void End()
        {
            foreach (var graph in _graphs) { graph.Stop(); graph.Reinit(); graph.Stop(); }
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
                _particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _chainLinks?.SetVisible(false);
        }
    }
}
