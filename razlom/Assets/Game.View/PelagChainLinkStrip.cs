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

        public void SetVisible(bool visible)
        {
            if (Links == null) return;
            for (int i = 0; i < Links.Length; i++) Links[i].gameObject.SetActive(visible);
        }

        public void SetChain(Vector3 a, Vector3 bend, Vector3 b)
        {
            if (Links == null || Links.Length == 0) return;
            for (int i = 0; i < Links.Length; i++)
            {
                float t = Links.Length == 1 ? 0f : i / (float)(Links.Length - 1);
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
