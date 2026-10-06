using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Свои меши камня Крушения «холодное железо» V6 (см. PelagWreckIronVfxSetup.cs): угловатые плиты и
/// осколки, которые встают торчком из земли (V5 — круглые камни арены читались «бежевой картошкой»).
/// Гранёные (плоская нормаль на грань), 1 м по наибольшей оси вокруг центра, ось Y — «вверх» плиты,
/// широкая грань — к ±Z (вид ставит её наружу), uv — проекция на грань (фактура камня арены).
/// Цвета вершин — Color32 (razlom-unity-particle-gotchas). Любая правка — поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    /// <summary>Плита (широкая, верх сколот наискось), осколок (высокий, к острию), клин (коренастый).</summary>
    private static readonly string[] SlabMeshNames = { "WreckIronSlab", "WreckIronSpike", "WreckIronWedge" };

    private static Mesh[] SlabMeshes()
    {
        return new[]
        {
            Prism(OwnMesh(SlabMeshNames[0]),
                new[] { V(-.5f, -.17f), V(-.12f, -.23f), V(.4f, -.16f), V(.5f, .06f), V(.2f, .2f), V(-.34f, .18f) },
                .84f, V(.03f, .02f), new[] { .66f, .8f, 1f, .9f, .74f, .58f }, .86f),
            Prism(OwnMesh(SlabMeshNames[1]),
                new[] { V(-.24f, -.12f), V(.06f, -.16f), V(.26f, -.04f), V(.16f, .14f), V(-.18f, .12f) },
                .22f, V(.07f, .01f), new[] { .9f, 1f, .97f, .9f, .86f }, 1.06f),
            Prism(OwnMesh(SlabMeshNames[2]),
                new[] { V(-.38f, -.26f), V(.1f, -.3f), V(.4f, -.08f), V(.3f, .26f), V(-.2f, .3f), V(-.42f, .04f) },
                .7f, V(.04f, -.03f), new[] { .5f, .56f, .78f, .84f, .66f, .52f }, .74f)
        };
    }

    private static Vector2 V(float x, float z) => new Vector2(x, z);

    /// <summary>
    /// Призма по многоугольнику основания (x — ширина, z — толщина) высотой до <paramref name="heights"/>
    /// по вершинам верха; верх сжат в <paramref name="topScale"/> и сдвинут — сколотая вершина плиты.
    /// </summary>
    private static Mesh Prism(Mesh mesh, Vector2[] poly, float topScale, Vector2 topShift, float[] heights, float apex)
    {
        int n = poly.Length;
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 p in poly) centroid += p / n;
        var bottom = new Vector3[n];
        var top = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            bottom[i] = new Vector3(poly[i].x, 0f, poly[i].y);
            Vector2 t = centroid + (poly[i] - centroid) * topScale + topShift;
            top[i] = new Vector3(t.x, heights[i], t.y);
        }
        Vector2 tc = centroid + topShift;
        var apexPoint = new Vector3(tc.x, apex, tc.y);
        var basePoint = new Vector3(centroid.x, 0f, centroid.y);
        var middle = new Vector3(centroid.x, apex * .5f, centroid.y);

        var tris = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, (a + b + c) / 3f - middle) < 0f) (b, c) = (c, b);
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Tri(bottom[i], bottom[j], top[j]);
            Tri(bottom[i], top[j], top[i]);
            Tri(apexPoint, top[i], top[j]);
            Tri(basePoint, bottom[j], bottom[i]);
        }

        // К 1 м по наибольшей оси вокруг центра рамки.
        var box = new Bounds(tris[0], Vector3.zero);
        foreach (Vector3 p in tris) box.Encapsulate(p);
        float k = 1f / Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
        var vertices = new List<Vector3>(tris.Count);
        var normals = new List<Vector3>(tris.Count);
        var uv = new List<Vector2>(tris.Count);
        var triangles = new List<int>(tris.Count);
        for (int f = 0; f < tris.Count; f += 3)
        {
            Vector3 a = (tris[f] - box.center) * k, b = (tris[f + 1] - box.center) * k, c = (tris[f + 2] - box.center) * k;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            // Проекция фактуры на грань по главной оси нормали; сдвиг на грань — пятна не повторяются.
            var shift = new Vector2(.37f * (f / 3 % 5), .23f * (f / 3 % 7));
            foreach (Vector3 p in new[] { a, b, c })
            {
                triangles.Add(vertices.Count);
                vertices.Add(p);
                normals.Add(normal);
                Vector2 plane = Mathf.Abs(normal.y) >= Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.z)) ? new Vector2(p.x, p.z)
                    : Mathf.Abs(normal.x) >= Mathf.Abs(normal.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                uv.Add(plane * .9f + shift);
            }
        }
        Fill(mesh, vertices, normals, uv, triangles);
        return mesh;
    }
}
