using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Махи «железо» на технике сабли — геометрия и маски (V3 по целевым кадрам v4-swing-left/right.png 06.10):
/// полумесяц-«C» из полосы пака (как PelagSabreComboVfxSetup.WaveMesh, но несимметричный: хвост — до TailDeg от
/// направления удара, голова — до HeadDeg, закручивается за спину героя), отдельные звенья-призраки по дуге (каждое —
/// свой изогнутый четырёхугольник, плитка маски — одно звено с обрывками боковых), маска звена и маска сколов.
/// </summary>
public static partial class PelagWreckSwingIronVfxSetup
{
    /// <summary>Отступ точки дуги от направления удара, град: − — сторона хвоста, + — сторона головы.</summary>
    private static float OffsetAt(float u, float tail, float head) => Mathf.Lerp(-tail, head, Mathf.Clamp01(u));

    /// <summary>Угол в осях корня, рад: направление удара — 180° (−X), голова — к +Y.</summary>
    private static float RootAngle(float offset) => (180f - offset) * Mathf.Deg2Rad;

    /// <summary>
    /// Толщина полосы (доля наибольшей) по отступу — V4 по кадрам, НЕ ровная полоса: самое толстое место — у фронта, где
    /// якорь бьёт (плато −15…+12°); короткая сторона держит толщину и к концу «распушается» штрихами (шейдер);
    /// длинная сторона долго сужается в тонкое острое жало (степень .7: на +60° ≈ .75, +100° ≈ .5, +130° ≈ .27);
    /// поверх — два неровных утолщения (рисованный мазок, а не циркуль).
    /// </summary>
    private static float Thickness(float offset, float tail, float head)
    {
        float shortSide = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-tail, -tail * .15f, offset)), .6f);
        float longSide = Mathf.Pow(1f - Mathf.Clamp01(Mathf.InverseLerp(head * .08f, head, offset)), .7f);
        float u = Mathf.InverseLerp(-tail, head, offset);
        float wobble = 1f + .09f * Mathf.Sin(2f * Mathf.PI * (u * 2.2f + .15f)) + .05f * Mathf.Sin(2f * Mathf.PI * (u * 4.7f + .55f));
        return shortSide * longSide * wobble;
    }

    /// <summary>
    /// Полумесяц V4 — своя сетка (120 × 6) вместо полосы пака 25 × 3: на дуге 250° у пака грань в 10° ломала обвод и
    /// не держала сужение. u вдоль маха (0 — короткая сторона), v поперёк (0 — внутренний край, 1 — наружная дуга);
    /// радиус точки — 1 − compress · Thickness · (1 − v), наружный край остаётся окружностью.
    /// </summary>
    private static Mesh WaveMesh(string name, float tail, float head, float compress)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int columns = 120, rows = 6;
        var vertices = new Vector3[(columns + 1) * (rows + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[columns * rows * 6];
        for (int c = 0; c <= columns; c++)
        {
            float u = c / (float)columns;
            float offset = OffsetAt(u, tail, head);
            float thick = compress * Thickness(offset, tail, head);
            float angle = RootAngle(offset);
            for (int r = 0; r <= rows; r++)
            {
                float v = r / (float)rows;
                float radius = 1f - thick * (1f - v);
                int at = c * (rows + 1) + r;
                vertices[at] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                uv[at] = new Vector2(u, v);
            }
        }
        int t = 0;
        for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
            {
                int a = c * (rows + 1) + r, b = a + rows + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    /// <summary>
    /// Звенья: по одному изогнутому четырёхугольнику на звено (центр — отступ из <paramref name="at"/>, поперёк — на
    /// v .62 полосы, под белой кромкой), длина <paramref name="length"/> и ширина <paramref name="width"/> — доли радиуса.
    /// Меш 0 — звенья стороны хвоста и фронта (отступ ≤ 10°), меш 1 — стороны головы (раскрываются позже).
    /// </summary>
    private static Mesh[] LinkStrips(string name, float tail, float head, float compress, float[] at, float length, float width)
    {
        var meshes = new Mesh[2];
        const int segments = 6;
        for (int s = 0; s < 2; s++)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (float centre in at)
            {
                if ((centre <= 10f) != (s == 0)) continue;
                float u = Mathf.InverseLerp(-tail, head, centre);
                float thick = compress * Thickness(centre, tail, head);
                float r = 1f - thick * (1f - .62f);
                float half = length * .5f / r * Mathf.Rad2Deg;
                int first = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float k = i / (float)segments;
                    float angle = RootAngle(centre + Mathf.Lerp(-half, half, k));
                    var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    vertices.Add(dir * (r - width * .5f));
                    vertices.Add(dir * (r + width * .5f));
                    uv.Add(new Vector2(k, 0f));
                    uv.Add(new Vector2(k, 1f));
                    if (i == segments) continue;
                    int v = first + i * 2;
                    triangles.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                }
            }
            string suffix = s == 0 ? "Tail" : "Head";
            Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + suffix + ".asset", name + suffix);
            PelagWhirlwindVfxSetup.Fill(mesh, vertices.ToArray(), uv.ToArray(), triangles.ToArray());
            meshes[s] = mesh;
        }
        return meshes;
    }

    // ------------------------------------------------------------------ маски

    /// <summary>
    /// Маска звена (r), плитка 384×256 — ОДНО звено: овал анфас по центру и обрывки боковых звеньев к краям плитки
    /// (гаснут к краю). Поле расстояния до средней линии прута (1 на линии → 0 на 0,14 высоты): порог Blob даёт белую
    /// сердцевину, голубое свечение и кромку цветом обвода материала — светящийся «призрак» звена, внутри — полоса.
    /// </summary>
    private static Texture2D LinkTexture()
    {
        const int w = 384, h = 256;
        var pixels = new Color32[w * h];
        const float a = .62f, b = .36f, half = .21f;                 // овал: полуоси в долях высоты (V4 — прут толще, читается рисунком)
        const float aspect = w / (float)h, cx = aspect * .5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = (x + .5f) / h, py = (y + .5f) / h - .5f;
                float dx = px - cx, k = Mathf.Sqrt(dx * dx / (a * a) + py * py / (b * b));
                float ring = k > 1e-4f ? Mathf.Abs(k - 1f) * Mathf.Sqrt(dx * dx + py * py) / k : b;
                // Обрывки боковых звеньев: прут по оси от овала к краю плитки, гаснет к краю.
                float reach = Mathf.Abs(dx) - a;
                float bar = reach > 0f ? Mathf.Abs(py) + Mathf.Max(0f, reach - .08f) * 2.5f : 1f;
                float edgeFade = Mathf.Clamp01(Mathf.Min(px, aspect - px) / .06f);
                float m = Mathf.Clamp01(1f - ring / half);
                float mb = Mathf.Clamp01(1f - bar / (half * .8f)) * edgeFade;
                byte v = (byte)Mathf.RoundToInt(255f * Mathf.Max(m, mb));
                pixels[y * w + x] = new Color32(v, v, v, 255);
            }
        return SaveMask("WreckIronSwingLinkV4", w, h, pixels, TextureWrapMode.Clamp);
    }

    /// <summary>
    /// Маска мазков кисти V4 (1024 × 256, повтор по u, зажим по v), своя, в полном разрешении:
    /// r — мазки вдоль маха: гладкий шум, вытянутый по u (порог шейдера режет длинные заострённые штрихи);
    /// g — крупный гладкий периодический шум (эрозия, лёгкая неровность наружной дуги);
    /// b — заострённые мазки-блики: иглы разной длины (60–420 px) и толщины, к концам сходят на нет.
    /// </summary>
    private static Texture2D BrushTexture()
    {
        const int w = 1024, h = 256;
        var rng = new System.Random(20261006);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        // r — мазки: гладкий шум, вытянутый вдоль маха (3 бугра по u на плитку, ~9 поперёк): порог шейдера режет
        // из него длинные штрихи шириной ~1/10 полосы с заострёнными концами — без волосков тоньше обвода (V4a: узкие
        // дорожки 5–26 px давали «волосы» из одного обвода).
        var lanes = new float[h * w];
        float[] l1 = Grid(rng, 3, 10), l2 = Grid(rng, 8, 17);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                lanes[y * w + x] = Mathf.Clamp01(.5f + 1.6f * (.72f * Smooth(l1, 3, 10, x / (float)w, y / (float)(h - 1))
                    + .28f * Smooth(l2, 8, 17, x / (float)w, y / (float)(h - 1)) - .5f));
        var coarse = new float[h * w];
        float[] g1 = Grid(rng, 8, 4), g2 = Grid(rng, 16, 7);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                coarse[y * w + x] = Mathf.Clamp01(.68f * Smooth(g1, 8, 4, x / (float)w, y / (float)(h - 1)) + .32f * Smooth(g2, 16, 7, x / (float)w, y / (float)(h - 1)));
        var glints = new float[h * w];
        for (int s = 0; s < 30; s++)
        {
            float yc = Rand(6f, h - 6f), half = Rand(1.4f, 3.4f), drift = Rand(-3f, 3f);
            int length = rng.Next(60, 421), x0 = rng.Next(0, w);
            for (int i = 0; i <= length; i++)
            {
                float t = i / (float)length;
                float hw = half * Mathf.Pow(Mathf.Sin(Mathf.PI * t), .6f);
                float cy = yc + drift * t;
                int x = (x0 + i) % w;
                for (int y = Mathf.Max(0, (int)(cy - 6f)); y <= Mathf.Min(h - 1, (int)(cy + 6f)); y++)
                {
                    float m = hw > .05f ? Mathf.Clamp01(1f - Mathf.Abs(y - cy) / (2f * hw)) : 0f;
                    if (m > glints[y * w + x]) glints[y * w + x] = m;
                }
            }
        }
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = lanes[y * w + x];
                pixels[y * w + x] = new Color32((byte)Mathf.RoundToInt(255f * r), (byte)Mathf.RoundToInt(255f * coarse[y * w + x]),
                    (byte)Mathf.RoundToInt(255f * glints[y * w + x]), 255);
            }
        Texture2D texture = SaveMask("WreckIronSwingBrushV4", w, h, pixels, TextureWrapMode.Repeat);
        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer && importer.wrapModeV != TextureWrapMode.Clamp)
        {
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(texture));
        }
        return texture;
    }

    private static float[] Grid(System.Random rng, int nx, int ny)
    {
        var grid = new float[nx * ny];
        for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
        return grid;
    }

    /// <summary>Гладкий шум по сетке: повтор по x (плитка), зажим по y.</summary>
    private static float Smooth(float[] grid, int nx, int ny, float x, float y)
    {
        float gx = x * nx, gy = y * (ny - 1);
        int x0 = Mathf.FloorToInt(gx), y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, ny - 2);
        float fx = Mathf.SmoothStep(0f, 1f, gx - x0), fy = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gy - y0));
        int xa = ((x0 % nx) + nx) % nx, xb = (xa + 1) % nx;
        float a = Mathf.Lerp(grid[y0 * nx + xa], grid[y0 * nx + xb], fx);
        float b = Mathf.Lerp(grid[(y0 + 1) * nx + xa], grid[(y0 + 1) * nx + xb], fx);
        return Mathf.Lerp(a, b, fy);
    }

    /// <summary>Маска сколов (r): лист пака «cfxr debris unlit 3x3» — r × альфа (прозрачное — ноль) и мягкий край для обвода.</summary>
    private static Texture2D ChipTexture()
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, DebrisTexture)));
        const int size = 512;
        int sw = source.width, sh = source.height;
        var src = source.GetPixels32();
        var mask = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color32 c = src[(y * sh / size) * sw + x * sw / size];
                mask[y * size + x] = c.r / 255f * (c.a / 255f);
            }
        Object.DestroyImmediate(source);
        // Мягкий край: два прохода коробочного размытия радиусом 3 px, затем сердцевина назад к 1.
        for (int pass = 0; pass < 2; pass++) mask = Blur(mask, size, 3);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte m = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(mask[i] * 1.35f));
            pixels[i] = new Color32(m, m, m, 255);
        }
        return SaveMask("WreckIronSwingChips3x3", size, size, pixels, TextureWrapMode.Clamp);
    }

    private static float[] Blur(float[] src, int size, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f; int n = 0;
                for (int k = -r; k <= r; k++) { int xx = x + k; if (xx < 0 || xx >= size) continue; sum += src[y * size + xx]; n++; }
                tmp[y * size + x] = sum / n;
            }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f; int n = 0;
                for (int k = -r; k <= r; k++) { int yy = y + k; if (yy < 0 || yy >= size) continue; sum += tmp[yy * size + x]; n++; }
                dst[y * size + x] = sum / n;
            }
        return dst;
    }

    /// <summary>Маска в PNG своей папки: линейная, без «альфа — прозрачность», с мипами; повтор или зажим.</summary>
    private static Texture2D SaveMask(string name, int w, int h, Color32[] pixels, TextureWrapMode wrap)
    {
        string path = TextureFolder + "/" + name + ".png";
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
