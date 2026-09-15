using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private readonly List<Mesh> _outlineMeshes = new List<Mesh>();
        private Mesh _tileCubeMesh;
        private void DisposeOutline()
        {
            foreach (var mesh in _outlineMeshes) DestroyOwned(mesh);
            _outlineMeshes.Clear(); _tileCubeMesh = null;
        }

        private void ApplyOutline(LayoutMap map, int placement, Transform tile)
        {
            var filter = tile.GetComponent<MeshFilter>();
            if (_tileCubeMesh == null) _tileCubeMesh = filter.sharedMesh;
            if (map.Outline == null) { filter.sharedMesh = _tileCubeMesh; return; }
            while (_outlineMeshes.Count <= placement) _outlineMeshes.Add(new Mesh { name = "Контур поляны", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var p = map.GetPlaced(placement);
            for (int y = p.OriginY * 4; y < (p.OriginY + p.Height) * 4; y++)
                for (int x = p.OriginX * 4; x < (p.OriginX + p.Width) * 4; x++)
                {
                    if (!map.Outline.ContainsCell(x, y)) continue;
                    _occupiedCells.Add(CellKey(x, y));
                    float lx = (x - p.OriginX * 4) / (float)(p.Width * 4) - .5f;
                    float lz = (y - p.OriginY * 4) / (float)(p.Height * 4) - .5f;
                    float dx = 1f / (p.Width * 4), dz = 1f / (p.Height * 4);
                    int first = vertices.Count;
                    vertices.Add(new Vector3(lx, .5f, lz)); vertices.Add(new Vector3(lx, .5f, lz + dz));
                    vertices.Add(new Vector3(lx + dx, .5f, lz + dz)); vertices.Add(new Vector3(lx + dx, .5f, lz));
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                }
            var mesh = _outlineMeshes[placement]; mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); filter.sharedMesh = mesh;
        }

        private bool TouchesOutlinedFloor(float x, float z, float radius)
        {
            int lowX = Mathf.FloorToInt((x - radius) * 2), highX = Mathf.CeilToInt((x + radius) * 2);
            int lowZ = Mathf.FloorToInt((z - radius) * 2), highZ = Mathf.CeilToInt((z + radius) * 2);
            for (int iy = lowZ; iy <= highZ; iy++)
                for (int ix = lowX; ix <= highX; ix++)
                    if (_shownMap.Outline.ContainsCell(ix, iy)) return true;
            return false;
        }
    }
}
