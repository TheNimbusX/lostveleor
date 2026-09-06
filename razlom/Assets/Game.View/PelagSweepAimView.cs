using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // View-only preview; the cone angle/range are shared with the authoritative hit query.
    public sealed class PelagSweepAimView : MonoBehaviour
    {
        private TickDriver _driver;
        private GameObject _root;
        private Mesh _mesh;
        private Material _material;
        private void Start()
        {
            _driver = GetComponent<TickDriver>();
            _root = new GameObject("Anchor sweep aiming cone");
            _mesh = new Mesh { name = "45 degree hook sector" };
            const int segments = 40;
            var vertices = new Vector3[segments + 2];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 3];
            colors[0] = new Color(0.2f, 1f, 0.4f, 0.12f);
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-AnchorKit.SweepAngleDegrees / 2f,
                    AnchorKit.SweepAngleDegrees / 2f, i / (float)segments) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * AnchorKit.SweepRadius.ToFloat();
                colors[i + 1] = new Color(0.2f, 1f, 0.4f, 0.22f);
                if (i < segments) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2; }
            }
            _mesh.vertices = vertices; _mesh.colors = colors; _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            _material = new Material(Shader.Find("Razlom/Aim Cone"));
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            var line = _root.AddComponent<LineRenderer>();
            line.sharedMaterial = _material; line.useWorldSpace = false; line.loop = true;
            line.widthMultiplier = 0.035f; line.positionCount = vertices.Length;
            line.SetPositions(vertices); line.startColor = line.endColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            _root.SetActive(false);
        }
        private void LateUpdate()
        {
            if (_root == null || _driver == null) return;
            bool show = _driver.AimingSweep;
            _root.SetActive(show);
            if (!show) return;
            FixVec2 from = _driver.Sim.Entities.Position[Simulation.PlayerId];
            FixVec2 delta = _driver.CursorWorld - from;
            Vector3 direction = new Vector3(delta.X.ToFloat(), 0f, delta.Y.ToFloat());
            if (direction.sqrMagnitude < 0.001f) return;
            _root.transform.SetPositionAndRotation(new Vector3(from.X.ToFloat(), 0.035f, from.Y.ToFloat()),
                Quaternion.LookRotation(direction, Vector3.up));
        }
        private void OnDestroy()
        { if (_root != null) Destroy(_root); if (_mesh != null) Destroy(_mesh); if (_material != null) Destroy(_material); }
    }
}
