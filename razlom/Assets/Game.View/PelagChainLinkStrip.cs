using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Геометрические звенья между двумя точками. Это не gameplay-цепь и не
    /// физика: только pooled View-геометрия, которая делает натяжение предметным.
    /// </summary>
    public sealed class PelagChainLinkStrip : MonoBehaviour
    {
        public Transform[] Links;
        private readonly float[] _arcLengths = new float[65];

        public void SetPoints(Vector3[] points)
        {
            if (Links == null || Links.Length == 0 || points == null || points.Length < 2 || points.Length > _arcLengths.Length) return;
            _arcLengths[0] = 0f;
            for (int i = 1; i < points.Length; i++)
                _arcLengths[i] = _arcLengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            float length = _arcLengths[points.Length - 1];
            int count = Mathf.Clamp(Mathf.CeilToInt(length / 0.135f), 0, Links.Length);
            // Новое звено появляется у конца цепи, не сдвигая все предыдущие.
            float spacing = Mathf.Max(0.135f, length / Links.Length);
            int segment = 1;
            for (int i = 0; i < Links.Length; i++)
            {
                Transform link = Links[i];
                bool visible = i < count && length > 0.07f;
                if (link.gameObject.activeSelf != visible) link.gameObject.SetActive(visible);
                if (!visible) continue;
                float distance = Mathf.Min(length, spacing * (i + 0.5f));
                while (segment < points.Length - 1 && _arcLengths[segment] < distance) segment++;
                float t = Mathf.InverseLerp(_arcLengths[segment - 1], _arcLengths[segment], distance);
                Vector3 before = points[segment] - points[Mathf.Max(0, segment - 2)];
                Vector3 after = points[Mathf.Min(points.Length - 1, segment + 1)] - points[segment - 1];
                Vector3 tangent = Vector3.Lerp(before.normalized, after.normalized, t);
                link.position = Vector3.Lerp(points[segment - 1], points[segment], t);
                if (tangent.sqrMagnitude > 0.000001f)
                    link.rotation = Quaternion.LookRotation(tangent, Vector3.up)
                        * Quaternion.Euler(0f, 0f, (i & 1) == 0 ? 0f : 90f);
            }
        }

        public void SetVisible(bool visible)
        {
            if (Links == null) return;
            for (int i = 0; i < Links.Length; i++) Links[i].gameObject.SetActive(visible);
        }

        public void SetChain(Vector3 a, Vector3 bend, Vector3 b)
        {
            SetChain(a, bend, b, 1f);
        }

        /// <summary>
        /// Draws only the leading portion of the chain. Keeping the pooled
        /// links hidden until the throw reaches them makes the chain read as
        /// a thrown object with tension, instead of an instant circle.
        /// </summary>
        public void SetChain(Vector3 a, Vector3 bend, Vector3 b, float progress)
        {
            if (Links == null || Links.Length == 0) return;
            progress = Mathf.Clamp01(progress);
            float distance = Vector3.Distance(a, bend) + Vector3.Distance(bend, b);
            int count = Mathf.Clamp(Mathf.CeilToInt(distance / 0.14f), 2, Links.Length);
            for (int i = 0; i < Links.Length; i++)
            {
                float t = i / (float)(count - 1);
                if (i >= count || t > progress || distance < 0.08f)
                {
                    Links[i].gameObject.SetActive(false);
                    continue;
                }
                Vector3 p = Quadratic(a, bend, b, t);
                float nextT = Mathf.Min(1f, t + 0.02f);
                Vector3 tangent = Quadratic(a, bend, b, nextT) - p;
                if (tangent.sqrMagnitude < 0.00001f) tangent = b - a;
                if (tangent.sqrMagnitude < 0.00001f) tangent = Vector3.forward;

                Transform link = Links[i];
                link.position = p;
                link.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up)
                                * Quaternion.Euler(0f, 0f, (i & 1) == 0 ? 0f : 90f);
                link.gameObject.SetActive(true);
            }
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 bend, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * bend + t * t * b;
        }
    }
}
