using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools
{
    /// <summary>Creates visual river candidates without changing source meshes, contours or navigation.</summary>
    public static class CampBridgePolishGeometry
    {
        public const float InnerRadius = 6f;
        public const float OuterRadius = 8f;
        const int BedColumns = 13;

        public static float SampleInfluence(Vector3 world, Vector3 centre)
        {
            float distance = new Vector2(world.x - centre.x, world.z - centre.z).magnitude;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InnerRadius, OuterRadius, distance));
        }

        /// <param name="kind">0: near bank; 1: bed; 2: far bank, using the existing Ribbon topology.</param>
        public static Mesh Clone(Mesh original, Transform meshTransform, Vector3 bridgeCentre, float waterWorldY, int kind)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (meshTransform == null) throw new ArgumentNullException(nameof(meshTransform));
            if (kind < 0 || kind > 2) throw new ArgumentOutOfRangeException(nameof(kind));
            int columns = kind == 0 ? 6 : kind == 1 ? 2 : 7;
            if (original.vertexCount < columns * 2 || original.vertexCount % columns != 0 || original.subMeshCount != 1)
                throw new ArgumentException("Expected the existing single-submesh river Ribbon grid.", nameof(original));

            var vertices = new List<Vector3>(original.vertices);
            var normals = new List<Vector3>(original.normals);
            var tangents = new List<Vector4>(original.tangents);
            var colors = new List<Color>(original.colors);
            var uv = new List<Vector4>[8];
            for (int channel = 0; channel < uv.Length; channel++)
            {
                uv[channel] = new List<Vector4>();
                original.GetUVs(channel, uv[channel]);
            }
            if (uv[0].Count != original.vertexCount || uv[3].Count != original.vertexCount)
                throw new ArgumentException("River candidates need the original UV0 and UV3 channels.", nameof(original));

            int[] triangles = original.triangles;
            if (kind == 1) triangles = SubdivideBed(vertices, normals, tangents, colors, uv);
            var changed = new bool[vertices.Count];
            int rows = original.vertexCount / columns;
            if (kind == 1)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 world = meshTransform.TransformPoint(vertices[i]);
                    float influence = SampleInfluence(world, bridgeCentre);
                    if (influence <= 0f) continue;
                    float across = Mathf.Clamp01(uv[3][i].y);
                    float fromEdge = Mathf.Min(across, 1f - across);
                    float channel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.13f, .5f, fromEdge));
                    float irregularity = (Mathf.PerlinNoise(world.x * .35f + 43f, world.z * .35f + 17f) - .5f) * .04f;
                    float depth = Mathf.Lerp(.08f, .55f + irregularity, channel);
                    world.y = Mathf.Lerp(world.y, waterWorldY - depth, influence);
                    vertices[i] = meshTransform.InverseTransformPoint(world);
                    changed[i] = true;
                }
            }
            else
            {
                for (int row = 0; row < rows; row++)
                {
                    int start = row * columns;
                    int upper = start + (kind == 0 ? 1 : 4);
                    int lower = start + (kind == 0 ? 5 : 0);
                    Vector3 direction = meshTransform.TransformPoint(vertices[lower]) - meshTransform.TransformPoint(vertices[upper]);
                    direction.y = 0f;
                    direction.Normalize();
                    for (int column = 0; column < columns; column++)
                    {
                        // The near bank has two upper seams; the far bank's final three columns are dry ground.
                        if (kind == 0 ? column <= 1 : column >= 4) continue;
                        int i = start + column;
                        Vector3 world = meshTransform.TransformPoint(vertices[i]);
                        if (world.y >= waterWorldY + .12f) continue;
                        float influence = SampleInfluence(world, bridgeCentre);
                        if (influence <= 0f) continue;
                        float wet = Mathf.Clamp01(uv[0][i].y);
                        float submerged = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(waterWorldY + .12f, waterWorldY - .06f, world.y));
                        float broad = Mathf.PerlinNoise(world.x * .22f + 19f, world.z * .22f + 61f);
                        float fine = Mathf.PerlinNoise(world.x * .83f + 47f, world.z * .83f + 13f);
                        float noise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, .7f, broad * .8f + fine * .2f));
                        float shape = influence * submerged * wet;
                        world += direction * (Mathf.Lerp(.18f, .65f, noise) * shape);
                        world.y -= (.015f + .035f * noise) * shape;
                        vertices[i] = meshTransform.InverseTransformPoint(world);
                        changed[i] = true;
                    }
                }
            }

            Mesh result = UnityEngine.Object.Instantiate(original);
            result.name = original.name + " — bridge polish";
            if (vertices.Count > ushort.MaxValue) result.indexFormat = IndexFormat.UInt32;
            result.SetVertices(vertices);
            for (int channel = 0; channel < uv.Length; channel++)
                if (uv[channel].Count == vertices.Count) result.SetUVs(channel, uv[channel]);
            if (colors.Count == vertices.Count) result.SetColors(colors);
            result.SetTriangles(triangles, 0);
            RestoreUnchangedLighting(result, normals, tangents, changed);
            result.RecalculateBounds();
            return result;
        }

        static int[] SubdivideBed(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
            List<Color> colors, List<Vector4>[] uv)
        {
            int sourceCount = vertices.Count, rows = sourceCount / 2;
            var grid = new int[rows, BedColumns];
            for (int row = 0; row < rows; row++)
            {
                int left = row * 2, right = left + 1;
                grid[row, 0] = left;
                grid[row, BedColumns - 1] = right;
                for (int column = 1; column < BedColumns - 1; column++)
                {
                    float t = column / (float)(BedColumns - 1);
                    grid[row, column] = vertices.Count;
                    vertices.Add(Vector3.Lerp(vertices[left], vertices[right], t));
                    if (normals.Count >= sourceCount) normals.Add(Vector3.Lerp(normals[left], normals[right], t).normalized);
                    if (tangents.Count >= sourceCount) tangents.Add(Vector4.Lerp(tangents[left], tangents[right], t));
                    if (colors.Count >= sourceCount) colors.Add(Color.Lerp(colors[left], colors[right], t));
                    for (int channel = 0; channel < uv.Length; channel++)
                        if (uv[channel].Count >= sourceCount) uv[channel].Add(Vector4.Lerp(uv[channel][left], uv[channel][right], t));
                }
            }
            var triangles = new int[(rows - 1) * (BedColumns - 1) * 6];
            int index = 0;
            for (int row = 1; row < rows; row++)
            for (int column = 1; column < BedColumns; column++)
            {
                int a = grid[row, column], b = grid[row - 1, column - 1];
                int c = grid[row, column - 1], d = grid[row - 1, column];
                triangles[index++] = b; triangles[index++] = c; triangles[index++] = d;
                triangles[index++] = d; triangles[index++] = c; triangles[index++] = a;
            }
            return triangles;
        }

        static void RestoreUnchangedLighting(Mesh mesh, List<Vector3> originalNormals, List<Vector4> originalTangents, bool[] changed)
        {
            mesh.RecalculateNormals();
            Vector3[] normals = mesh.normals;
            if (originalNormals.Count == normals.Length)
                for (int i = 0; i < normals.Length; i++) if (!changed[i]) normals[i] = originalNormals[i];
            mesh.normals = normals;
            mesh.RecalculateTangents();
            Vector4[] tangents = mesh.tangents;
            if (originalTangents.Count == tangents.Length)
                for (int i = 0; i < tangents.Length; i++) if (!changed[i]) tangents[i] = originalTangents[i];
            mesh.tangents = tangents;
        }
    }
}
