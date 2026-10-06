using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Свои меши Крушения «холодное железо» V5 (см. PelagWreckIronVfxSetup.cs): крупное звено цепи и
/// гранёный кусок камня для летящих обломков. Цвета вершин — Color32 (razlom-unity-particle-gotchas).
/// Любая правка — поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    private const string LinkMeshName = "WreckIronLink";
    private const string ChunkMeshName = "WreckIronChunk";

    /// <summary>Овал звена 1 м по Z (наружный), ширина PelagWreckIronRules.LinkWidthOfLength, прут 0,15 м.</summary>
    private const float LinkTube = Game.View.PelagWreckIronRules.LinkTubeOfLength;

    private static Mesh OwnMesh(string name)
    {
        string path = MeshPath(name);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
        }
        return mesh;
    }

    /// <summary>
    /// Звено цепи: трубка по «стадиону» в плоскости XZ (длина 1 м по Z, ширина 0,52 м по X), центр в нуле,
    /// гладкие нормали (вывернутая оболочка контура без разрывов). uv.x — вдоль кольца, uv.y — вокруг прута.
    /// </summary>
    private static Mesh IronLinkMesh()
    {
        Mesh mesh = OwnMesh(LinkMeshName);
        float width = Game.View.PelagWreckIronRules.LinkWidthOfLength;
        float a = width * .5f - LinkTube;          // радиус полукружий осевой линии
        float c = .5f - LinkTube - a;               // центры полукружий по Z
        const int arcSteps = 16, sideSteps = 6, ring = 14;
        var centre = new List<Vector3>();
        var outward = new List<Vector3>();
        void Arc(float zc, float from)
        {
            for (int i = 0; i < arcSteps; i++)
            {
                float t = from + Mathf.PI * i / arcSteps;
                var n = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                centre.Add(new Vector3(0f, 0f, zc) + n * a);
                outward.Add(n);
            }
        }
        void Side(float x, float z0, float z1)
        {
            for (int i = 0; i < sideSteps; i++)
            {
                float z = Mathf.Lerp(z0, z1, (float)i / sideSteps);
                centre.Add(new Vector3(x, 0f, z));
                outward.Add(new Vector3(Mathf.Sign(x), 0f, 0f));
            }
        }
        Arc(c, 0f);             // дальнее полукружие: от +X через +Z к −X
        Side(-a, c, -c);        // левый прут
        Arc(-c, Mathf.PI);      // ближнее полукружие: от −X через −Z к +X
        Side(a, -c, c);         // правый прут
        int count = centre.Count;
        var vertices = new List<Vector3>((count + 1) * (ring + 1));
        var normals = new List<Vector3>(vertices.Capacity);
        var uv = new List<Vector2>(vertices.Capacity);
        for (int i = 0; i <= count; i++)
        {
            Vector3 p = centre[i % count], o = outward[i % count];
            for (int j = 0; j <= ring; j++)
            {
                float phi = 2f * Mathf.PI * j / ring;
                Vector3 n = o * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
                vertices.Add(p + n * LinkTube);
                normals.Add(n);
                uv.Add(new Vector2((float)i / count, (float)j / ring));
            }
        }
        var triangles = new List<int>(count * ring * 6);
        for (int i = 0; i < count; i++)
            for (int j = 0; j < ring; j++)
            {
                int q = i * (ring + 1) + j, r = q + ring + 1;
                triangles.Add(q); triangles.Add(q + 1); triangles.Add(r);
                triangles.Add(r); triangles.Add(q + 1); triangles.Add(r + 1);
            }
        Fill(mesh, vertices, normals, uv, triangles);
        // Обход должен смотреть наружу: проверка по первому треугольнику и нормали.
        Vector3 v0 = vertices[triangles[0]], v1 = vertices[triangles[1]], v2 = vertices[triangles[2]];
        if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), normals[triangles[0]]) < 0f)
        {
            for (int t = 0; t < triangles.Count; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            mesh.SetTriangles(triangles, 0);
        }
        return mesh;
    }

    /// <summary>
    /// Кусок камня для летящих обломков: гранёный многогранник ~1 м (икосаэдр, неровно сжатый),
    /// плоские грани; uv — проекция сверху на фактуру камня.
    /// </summary>
    private static Mesh IronChunkMesh()
    {
        Mesh mesh = OwnMesh(ChunkMeshName);
        float g = (1f + Mathf.Sqrt(5f)) * .5f;
        var corners = new[]
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1)
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };
        // Неровный кусок: радиус по вершине 0,72–1,0, сплюснут по Y, вытянут по X (детерминированно).
        float[] bump = { .92f, .78f, 1f, .84f, .74f, .97f, .88f, .8f, 1f, .76f, .9f, .83f };
        for (int i = 0; i < corners.Length; i++)
            corners[i] = Vector3.Scale(corners[i].normalized * bump[i], new Vector3(1.12f, .74f, .94f));
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        Bounds box = new Bounds(corners[0], Vector3.zero);
        foreach (Vector3 c in corners) box.Encapsulate(c);
        float k = 1f / Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
        for (int f = 0; f < faces.Length; f += 3)
        {
            Vector3 a = (corners[faces[f]] - box.center) * k, b = (corners[faces[f + 1]] - box.center) * k, c = (corners[faces[f + 2]] - box.center) * k;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, a + b + c) < 0f) { (b, c) = (c, b); n = -n; }
            foreach (Vector3 p in new[] { a, b, c })
            {
                triangles.Add(vertices.Count);
                vertices.Add(p);
                normals.Add(n);
                uv.Add(new Vector2(p.x + .5f + p.y * .3f, p.z + .5f + p.y * .2f));
            }
        }
        Fill(mesh, vertices, normals, uv, triangles);
        return mesh;
    }

    private static void Fill(Mesh mesh, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles)
    {
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        var colors = new Color32[vertices.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        mesh.SetColors(colors);
        // V6: сглаженные нормали (среднее граней в одной точке) — в uv3 для «скорлупы» контура: у гранёных
        // плит своя нормаль на грань, и оболочка по ней рвалась бы на углах.
        var smooth = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3Int key = Key(vertices[i]);
            smooth[key] = (smooth.TryGetValue(key, out Vector3 sum) ? sum : Vector3.zero) + normals[i];
        }
        var hull = new List<Vector3>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) hull.Add(smooth[Key(vertices[i])].normalized);
        mesh.SetUVs(3, hull);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);
    }
}
