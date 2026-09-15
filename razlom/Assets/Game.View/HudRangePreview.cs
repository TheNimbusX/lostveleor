using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Плоская лента в мире: глубина сцены закрывает её под камнями и декорациями.
    // Один переиспользуемый меш, мягкая кромка вместо ступенек экранных GUI-линий.
    internal sealed class HudRangePreview
    {
        readonly GameObject _root;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MeshRenderer _renderer;
        readonly List<Vector3> _vertices = new List<Vector3>(2048);
        readonly List<Vector2> _uv = new List<Vector2>(2048);
        readonly List<int> _indices = new List<int>(3072);
        float _halfWidth;
        public HudRangePreview(Transform owner)
        {
            _root = new GameObject("HUD ability reach") { layer = 5, hideFlags = HideFlags.HideAndDontSave };
            _root.transform.SetParent(owner,false);
            _mesh = new Mesh { name="HUD reach ribbons" }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            var shader=Resources.Load<Shader>("UI/HUD/AbilityReach");
            if(shader!=null) { _material=new Material(shader); _renderer.sharedMaterial=_material; }
            _renderer.enabled=false;
        }
        public void Begin(Camera camera, bool available)
        {
            _vertices.Clear(); _uv.Clear(); _indices.Clear();
            _halfWidth = camera != null && camera.orthographic ? camera.orthographicSize/Screen.height*3.5f : .025f;
            if(_material!=null) _material.SetColor("_Tint", available ? new Color(1f,.91f,.67f,.78f) : new Color(.8f,.74f,.60f,.4f));
        }
        public void Line(Vector3 from, Vector3 to)
        {
            Vector3 direction=to-from;
            if(direction.sqrMagnitude<.000001f)return;
            Vector3 side=Vector3.Cross(Vector3.up,direction.normalized)*_halfWidth;
            int start=_vertices.Count;
            _vertices.Add(from-side); _vertices.Add(from+side); _vertices.Add(to+side); _vertices.Add(to-side);
            _uv.Add(new Vector2(0,0)); _uv.Add(new Vector2(0,1)); _uv.Add(new Vector2(1,1)); _uv.Add(new Vector2(1,0));
            _indices.Add(start); _indices.Add(start+1); _indices.Add(start+2);
            _indices.Add(start); _indices.Add(start+2); _indices.Add(start+3);
        }
        public void End()
        {
            _mesh.Clear();
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0,_uv); _mesh.SetTriangles(_indices,0);
            _renderer.enabled=_material!=null && _vertices.Count>0;
        }
        public void Hide() { if(_renderer!=null)_renderer.enabled=false; }
        public void Dispose()
        {
            if(_root!=null)Object.Destroy(_root);
            if(_mesh!=null)Object.Destroy(_mesh);
            if(_material!=null)Object.Destroy(_material);
        }
    }
}
