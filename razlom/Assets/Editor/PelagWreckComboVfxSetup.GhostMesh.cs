using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Меши Призрачного якоря (см. PelagWreckComboVfxSetup.Ghost): якорь Пелага с призрачной цепью витком вокруг ствола (как
/// на целевом кадре) и диск кольца удара. Свои ассеты в Geometry; меши паков не трогаются.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    // Цепь витком вокруг ствола якоря в долях высоты (ствол у меша Tripo: x ±.046, z от −.10 до .01 на y .55….95).
    private const int GhostChainLinks = 11;
    private const float GhostChainTop = .86f, GhostChainBottom = .47f, GhostChainTurns = 1.3f, GhostChainRadius = .088f;
    private const float GhostChainLinkLength = .11f, GhostChainAxisZ = -.045f;

    private static Mesh GhostSourceMesh()
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(GhostModel))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    /// <summary>
    /// Якорь Пелага копией в свой меш: низ — в 0, высота — 1 м, по центру XZ (размер ставит префаб) + звенья цепи по
    /// спирали вокруг ствола (через одно повёрнуты на 90°). TEXCOORD1 — сглаженные нормали (среднее по совпадающим точкам:
    /// у Tripo нормали рвутся по швам UV, оболочка ореола дала бы щели). Цвета вершин 8-битные белые.
    /// </summary>
    private static Mesh GhostAnchorMesh()
    {
        Mesh source = GhostSourceMesh();
        Mesh link = AssetDatabase.LoadAssetAtPath<Mesh>(GhostLinkMesh);
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/WreckGhostAnchor.asset", "WreckGhostAnchor");
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        Vector3[] sv = source.vertices;
        Vector3[] sn = source.normals;
        Bounds b = source.bounds;
        float k = 1f / Mathf.Max(.01f, b.size.y);
        var shift = new Vector3(b.center.x, b.min.y, b.center.z);
        for (int i = 0; i < sv.Length; i++)
        {
            vertices.Add((sv[i] - shift) * k);
            normals.Add(sn != null && sn.Length == sv.Length ? sn[i] : Vector3.up);
        }
        triangles.AddRange(source.triangles);

        // Цепь: звенья вдоль спирали сверху вниз; длинная ось звена (Z меша звена) — по касательной.
        Vector3[] lv = link.vertices;
        Vector3[] ln = link.normals;
        int[] lt = link.triangles;
        Bounds lb = link.bounds;
        float lk = GhostChainLinkLength / Mathf.Max(.001f, lb.size.z);
        for (int n = 0; n < GhostChainLinks; n++)
        {
            float t = (n + .5f) / GhostChainLinks;
            Vector3 p = GhostChainPoint(t);
            Vector3 tangent = (GhostChainPoint(t + .01f) - GhostChainPoint(t - .01f)).normalized;
            Vector3 radial = new Vector3(p.x, 0f, p.z - GhostChainAxisZ).normalized;
            Vector3 up = (n & 1) == 0 ? radial : Vector3.Cross(tangent, radial).normalized;
            Quaternion rotation = Quaternion.LookRotation(tangent, up);
            int baseIndex = vertices.Count;
            for (int i = 0; i < lv.Length; i++)
            {
                vertices.Add(p + rotation * ((lv[i] - lb.center) * lk));
                normals.Add(rotation * (ln != null && ln.Length == lv.Length ? ln[i] : Vector3.up));
            }
            for (int i = 0; i < lt.Length; i++) triangles.Add(baseIndex + lt[i]);
        }

        var sums = new Dictionary<Vector3Int, Vector3>();
        var keys = new Vector3Int[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 q = vertices[i] * 2000f;
            keys[i] = new Vector3Int(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), Mathf.RoundToInt(q.z));
            sums.TryGetValue(keys[i], out Vector3 sum);
            sums[keys[i]] = sum + normals[i];
        }
        var smooth = new List<Vector3>(vertices.Count);
        var colors = new Color32[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            smooth.Add(sums[keys[i]].normalized);
            colors[i] = new Color32(255, 255, 255, 255);
        }
        mesh.Clear();
        mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(1, smooth);
        mesh.colors32 = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    /// <summary>Точка спирали цепи, <paramref name="t"/> 0 — верх (у кольца якоря), 1 — низ (над тройником лап).</summary>
    private static Vector3 GhostChainPoint(float t)
    {
        float y = Mathf.Lerp(GhostChainTop, GhostChainBottom, t);
        float a = t * GhostChainTurns * Mathf.PI * 2f + .6f;
        // Виток чуть шире у середины — цепь висит свободно, а не облегает.
        float r = GhostChainRadius * (1f + .18f * Mathf.Sin(t * Mathf.PI));
        return new Vector3(Mathf.Cos(a) * r, y, GhostChainAxisZ + Mathf.Sin(a) * r);
    }

    /// <summary>
    /// Диск кольца в плоскости XZ (y = 0): u — угол целиком (0…1), v = r / DiscOuter, линия кольца на r = 1 (масштаб
    /// частицы = радиус Sim). Кольца вершин гуще у линии. Цвета вершин 8-битные белые (Float32-цвет ломает частицы-меши).
    /// </summary>
    private static Mesh GhostDiscMesh()
    {
        const int segments = 180;
        float[] rows = { 0f, .12f, .25f, .38f, .50f, .58f, .64f, .67f, .69f, .71f, .74f, .78f, .84f, .92f, 1f };
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/WreckGhostDisc.asset", "WreckGhostDisc");
        int cols = segments + 1;
        var vertices = new Vector3[cols * rows.Length];
        var uv = new Vector2[vertices.Length];
        var colors = new Color32[vertices.Length];
        var triangles = new List<int>(segments * (rows.Length - 1) * 6);
        for (int r = 0; r < rows.Length; r++)
            for (int i = 0; i < cols; i++)
            {
                float u = i / (float)segments;
                float a = u * Mathf.PI * 2f;
                int at = r * cols + i;
                vertices[at] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rows[r] * PelagWreckGhostLook.DiscOuter);
                uv[at] = new Vector2(u, rows[r]);
                colors[at] = new Color32(255, 255, 255, 255);
            }
        for (int r = 0; r < rows.Length - 1; r++)
            for (int i = 0; i < segments; i++)
            {
                int v = r * cols + i;
                triangles.Add(v); triangles.Add(v + cols); triangles.Add(v + 1);
                triangles.Add(v + 1); triangles.Add(v + cols); triangles.Add(v + cols + 1);
            }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.colors32 = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    /// <summary>Камни выпада (копии «Tiny Rock» в одну клетку палитры) — те же ассеты, не пересобираются.</summary>
    private static Mesh[] GhostRocks()
    {
        var list = new List<Mesh>();
        for (int i = 1; i <= 5; i++)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GeometryFolder + "/WreckLungeRock" + i + ".asset");
            if (mesh != null) list.Add(mesh);
        }
        return list.ToArray();
    }
}
