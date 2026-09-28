using System.Collections.Generic;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX КОРНЕХВАТА по целевому кадру 1-roots-snare (выбор владельца 26.09,
/// ART/…/7.forest-root-snarer/production/vfx_target_frames_2026-09-26).
///
/// На кадре: под героем треснувший круг, из него рвутся толстые узловатые
/// тёмные корни и оплетают ноги героя; от вбитых в землю плит к кругу бегут
/// тёмные трещины с корешками; вокруг — комья, камни, немного тёплой пыли.
///
/// • VFX_RootSnarer_SlamCracks — удар плитами (тик 15, круг встал): у каждой
///   плиты тёмное пятно земли, звезда трещин (Hovl Crater19), пыль и комья;
///   от плит к кругу — сегменты трещины «Seg L/R i» (вид расставляет их по
///   длине и задерживает по бегу трещины).
/// • VFX_RootSnarer_RootsErupt — корни (тик 36): кольцо толстых загнутых внутрь
///   корней в коре и короткие колючие побеги по кромке, треснувшая земля круга
///   (Hovl Crack4, Crater19, Crater2), кольцо мягкой пыли, комья, камни, щепки,
///   листья. Корни держатся, пока моб прижат, и уходят в землю (вид).
/// • VFX_RootSnarer_SnareOnHero — путы: три кольца корней вокруг ног и два
///   побега у стоп, трещинки под ногами, пыль и комья на старте.
///
/// Паки: CFXR (debris unlit 3x3, debris wood unlit 3x3, лист leave a + cfxr
/// mesh leave — без освещения, smoke cloud x4 blurred с cloud blur, плёнка sword
/// trail plain одним каналом под маски Hovl), Hovl (Crack4, Crater19, Crater2),
/// кора Fantasy Forest (bark01_bottom). Наш слой — меши корней (трубы из колец
/// с узлами и колючками, как корни воя Вендиго), раскладка и тайминг.
///
/// Растущие корни — не частицы, а MeshRenderer-дети корня префаба с именем
/// «Grow|задержка мс|скрутка °|подпись»: RootSnarerCombatView выдвигает их
/// из земли вдоль оси +Y ребёнка и уводит обратно по возрасту от тика Sim.
/// Корни префабов лежат на земле: +Z — от моба к кругу, +Y — вверх.
/// </summary>
public static class RootSnarerVfxSetup
{
    private const string Revision = "RootSnarerVfxV3";
    private const string Root = "Assets/Resources/VFX/RootSnarer";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string CfxrCloudBlur = CfxrGraphics + "cfxr cloud blur.png";
    private const string HovlTextures = "Assets/Hovl Studio/HSFiles/Textures/";
    private const string BarkTexture = "Assets/Fantasy Forest Environment Free Sample/Textures/bark01_bottom.tga";

    // Тона: земля и пыль луга (как у Вендиго), корни — тёмная тёплая кора.
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f);
    private static readonly Color SoilDark = new Color(.30f, .20f, .12f);
    private static readonly Color DustLight = new Color(.76f, .58f, .39f);
    private static readonly Color DustDark = new Color(.58f, .43f, .28f);
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f);
    private static readonly Color LeafDark = new Color(.42f, .52f, .20f);
    private static readonly Color BarkLight = new Color(.62f, .48f, .33f), BarkDark = new Color(.40f, .29f, .19f);
    private static readonly Color RockLight = new Color(.50f, .38f, .27f), RockDark = new Color(.34f, .25f, .17f);
    private static readonly Color CrackTone = new Color(SoilDark.r * .7f, SoilDark.g * .7f, SoilDark.b * .7f, .95f);

    // «Волна из корней» (кадр 1-mend-ring): золотисто-зелёный свет, не неон — тёплая
    // трава на закате. Альфа держит яркость: материалы без подъёма цвета.
    private static readonly Color MendGold = new Color(.93f, .82f, .40f), MendGreen = new Color(.56f, .78f, .30f);
    private const string CfxrGlowSoft = "cfxr proc glow soft ab.mat", CfxrRing = "cfxr proc ring ab.mat",
        CfxrStar = "cfxr magic star hdr ab.mat";

    private sealed class Kit
    {
        public Material Clod, Splinter, Leaf, Haze, Splat, Crack, Vein, Wood, WoodDark, Glow, Ring, Spark;
        public Mesh LeafMesh, Quad;
        public Mesh[] Curls, Stubs, Coils;
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Корнехват/VFX: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Корнехват/VFX: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisUnlit) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrTrailMaterial) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(HovlTextures + "Crater19.png") == null)
        {
            Debug.LogWarning("[rootsnarer-vfx] Нет паков Hovl/CFXR, VFX Корнехвата не собран.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + RootSnarerCombatView.RootsEruptName + ".prefab");
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + RootSnarerCombatView.SlamCracksName + ".prefab") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + RootSnarerCombatView.SnareName + ".prefab") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + RootSnarerCombatView.MendRingName + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "RootSnarer");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var kit = Materials();
        SaveSlamCracks(kit);
        SaveRootsErupt(kit);
        SaveSnare(kit);
        SaveMendChannel(kit);
        SaveMendRing(kit);
        SaveMendLeaves(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + RootSnarerCombatView.RootsEruptName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[rootsnarer-vfx] Удар плитами, корни, путы и волна лечения собраны из паков, ревизия " + Revision + ".");
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        // Освещённые материалы CFXR в URP бледнеют, дым Hovl без depth-текстуры
        // невидим: только unlit CFXR и его размытые облака.
        kit.Clod = PackCopy("M_RootSnarer_Clod", CfxrDebrisUnlit);
        kit.Splinter = PackCopy("M_RootSnarer_Splinter", CfxrDebrisWood);
        kit.Leaf = Unlit(PackCopy("M_RootSnarer_Leaf", CfxrLeafMaterial));
        kit.Haze = Textured(Plain(PackCopy("M_RootSnarer_DustHaze", CfxrSmokeBlurred)), CfxrCloudBlur, true);
        // Земля: своя окраска маски Hovl через материал облаков CFXR.
        kit.Splat = Textured(Plain(PackCopy("M_RootSnarer_SoilSplat", CfxrSmokeBlurred)), HovlTextures + "Crater2.png", false);
        // Трещины — одноканальная плёнка CFXR с маской Hovl, тёмный цвет из частиц.
        kit.Crack = Textured(NoDissolve(PackCopy("M_RootSnarer_Crack", CfxrTrailMaterial)), HovlTextures + "Crack4.png", true);
        kit.Vein = Textured(NoDissolve(PackCopy("M_RootSnarer_Vein", CfxrTrailMaterial)), HovlTextures + "Crater19.png", true);
        // Волна лечения: мягкое свечение, кольцо и искры CFXR — альфа-смешение, без аддитива.
        kit.Glow = Plain(PackCopy("M_RootSnarer_MendGlow", CfxrGraphics + CfxrGlowSoft));
        // Кольцо волны — диск Hovl с яркой кромкой (Circle17) одним каналом: процедурное
        // кольцо CFXR без своих данных частиц невидимо (съёмка 28.09).
        kit.Ring = Textured(NoDissolve(PackCopy("M_RootSnarer_MendRing", CfxrTrailMaterial)), HovlTextures + "Circle17.png", true);
        kit.Spark = Plain(PackCopy("M_RootSnarer_MendSpark", CfxrGraphics + CfxrStar));
        // Декали рисуются до пыли и комьев.
        foreach (var decal in new[] { kit.Splat, kit.Crack, kit.Vein }) { decal.renderQueue = 2990; EditorUtility.SetDirty(decal); }
        // Корни — URP Lit с корой: свет, тень и объём, как у корней воя. Тёмная тёплая кора.
        kit.Wood = Wood("M_RootSnarer_RootWood", new Color(.62f, .50f, .43f));
        kit.WoodDark = Wood("M_RootSnarer_RootDark", new Color(.48f, .38f, .32f));
        kit.LeafMesh = LoadMesh(CfxrLeafMesh);
        kit.Quad = Quad("RootSnarerCrackQuad");
        // (длина, радиус основания, загиб внутрь, узлы, колючки, скрутка)
        kit.Curls = new[]
        {
            CurlRoot("RootSnarerCurlA", 11, 1.35f, .15f, 2.3f, .12f, 4, 2.0f),
            CurlRoot("RootSnarerCurlB", 12, 1.20f, .14f, 2.6f, .14f, 3, 2.6f),
            CurlRoot("RootSnarerCurlC", 13, 1.50f, .17f, 2.0f, .10f, 5, 1.6f),
            CurlRoot("RootSnarerCurlD", 14, 1.10f, .13f, 2.8f, .15f, 3, 3.0f),
        };
        kit.Stubs = new[]
        {
            CurlRoot("RootSnarerStubA", 21, .62f, .10f, .9f, .16f, 3, 2.2f),
            CurlRoot("RootSnarerStubB", 22, .48f, .09f, 1.3f, .18f, 2, 2.8f),
        };
        // (радиус кольца, витки, низ, верх, толщина, по часовой)
        kit.Coils = new[]
        {
            Coil("RootSnarerCoilA", 31, .30f, 1.25f, -.10f, .55f, .065f, true),
            Coil("RootSnarerCoilB", 32, .34f, 1.00f, -.10f, .38f, .075f, false),
            Coil("RootSnarerCoilC", 33, .27f, 1.45f, -.10f, .70f, .055f, true),
        };
        return kit;
    }

    private static Material Wood(string name, Color tint)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Lit"));
        var bark = AssetDatabase.LoadAssetAtPath<Texture2D>(BarkTexture);
        if (bark != null) material.SetTexture("_BaseMap", bark);
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", .15f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят спрайт у земли.</summary>
    private static Material Plain(Material material)
    {
        NoDissolve(material);
        material.DisableKeyword("_FADING_ON");
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material NoDissolve(Material material)
    {
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_FADING_ON", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        if (material.HasProperty("_UseLighting")) material.SetFloat("_UseLighting", 0f);
        if (material.HasProperty("_UseNormalMap")) material.SetFloat("_UseNormalMap", 0f);
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        if (material.HasProperty("_CFXR_DITHERED_SHADOWS")) material.SetFloat("_CFXR_DITHERED_SHADOWS", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Textured(Material material, string texturePath, bool singleChannel)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", singleChannel ? 1f : 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    // -------------------------------------------------------------- geometry

    /// <summary>
    /// Плоский квадрат 1×1 на земле с центром в нуле, нормаль вверх — декаль
    /// сегмента трещины (мешевая частица, оси системы). Цвет вершин 8-битный:
    /// с Float32-цветом мешевая частица читает мусор.
    /// </summary>
    private static Mesh Quad(string name)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(new List<Vector3> { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
        var white = new Color32(255, 255, 255, 255);
        mesh.SetColors(new List<Color32> { white, white, white, white });
        mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Корень, загнутый внутрь: основание под землёй (−0,14 м), растёт по +Y и
    /// загибается к +Z — угол от вертикали curl·s^1,7 (к концу крючок смотрит
    /// вниз, в круг). Сечение с долями и скруткой, пята, узлы, колючки.
    /// </summary>
    private static Mesh CurlRoot(string name, int salt, float length, float radius, float curl, float knots, int thorns, float twist)
    {
        const int rings = 18;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        var centers = new Vector3[rings + 1];
        var radii = new float[rings + 1];
        var p = new Vector3(0f, -.14f, 0f);
        centers[0] = p;
        for (int r = 1; r <= rings; r++)
        {
            float s = (r - .5f) / rings;
            float phi = curl * Mathf.Pow(s, 1.7f);
            float wobble = .18f * Mathf.Sin(s * 5f + phase);
            p += new Vector3(wobble, Mathf.Cos(phi), Mathf.Sin(phi)).normalized * (length / rings);
            centers[r] = p;
        }
        for (int r = 0; r <= rings; r++)
        {
            float s = r / (float)rings;
            radii[r] = radius * Mathf.Pow(1f - s, .75f)
                * (1f + .5f * Mathf.Pow(Mathf.Max(0f, 1f - s / .16f), 2f))
                * (1f + knots * (Mathf.Sin(s * 13f + phase) + .5f * Mathf.Sin(s * 29f + 2f * phase)) * (1f - s)) + .004f;
        }
        return Tube(name, salt, centers, radii, 9, twist, thorns, length * .9f);
    }

    /// <summary>
    /// Кольцо пут: корень выходит из земли чуть снаружи ног и обвивает их по
    /// спирали вокруг вертикали (turns витков) до высоты top, к концу тоньше.
    /// </summary>
    private static Mesh Coil(string name, int salt, float ringRadius, float turns, float bottom, float top, float radius, bool clockwise)
    {
        const int rings = 44;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f, sign = clockwise ? -1f : 1f;
        var centers = new Vector3[rings + 1];
        var radii = new float[rings + 1];
        for (int r = 0; r <= rings; r++)
        {
            float s = r / (float)rings;
            float angle = phase + sign * turns * Mathf.PI * 2f * s;
            float ring = ringRadius * (1f + .6f * Mathf.Max(0f, 1f - s / .12f));
            float y = Mathf.Lerp(bottom, top, Mathf.Pow(s, .85f)) + .02f * Mathf.Sin(s * 17f + phase);
            centers[r] = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
            radii[r] = radius * (1f - .55f * s) * (1f + .5f * Mathf.Pow(Mathf.Max(0f, 1f - s / .1f), 2f))
                * Mathf.Pow(Mathf.Clamp01((1f - s) / .1f), .6f) + .003f;
        }
        return Tube(name, salt, centers, radii, 8, 3f, 3, turns * ringRadius * 5f);
    }

    /// <summary>
    /// Труба вдоль хребта centers: рамки переносом (без переворота на крутом
    /// загибе), сечение с долями и скруткой twist по длине, боковые колючки.
    /// Кора: u — вокруг сечения, v — вдоль (0 у земли, мшистый низ текстуры).
    /// </summary>
    private static Mesh Tube(string name, int salt, Vector3[] centers, float[] radii, int sides, float twist, int thorns, float vLength)
    {
        int rings = centers.Length - 1;
        float phase = Hash01(salt, 11) * Mathf.PI * 2f;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        var tangents = new Vector3[rings + 1];
        var normals = new Vector3[rings + 1];
        var binormals = new Vector3[rings + 1];
        Vector3 carried = Vector3.zero;
        for (int r = 0; r <= rings; r++)
        {
            Vector3 tangent = (centers[Mathf.Min(rings, r + 1)] - centers[Mathf.Max(0, r - 1)]).normalized;
            Vector3 normal = r == 0 ? Vector3.Cross(tangent, Vector3.forward) : carried - tangent * Vector3.Dot(carried, tangent);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right);
            normal.Normalize();
            carried = normal;
            tangents[r] = tangent; normals[r] = normal; binormals[r] = Vector3.Cross(normal, tangent);
        }
        var white = new Color32(255, 255, 255, 255);
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .24f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(centers[r] + (normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a)) * radii[r] * lobe);
                uv.Add(new Vector2(s / (float)sides, t * vLength));
                colors.Add(white);
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        // Боковые колючки: конус из стенки наружу и вперёд по росту.
        for (int k = 0; k < thorns; k++)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(.22f, .7f, (k + Hash01(salt, 20 + k)) / thorns) * rings), 1, rings - 1);
            float a = Hash01(salt, 30 + k) * Mathf.PI * 2f;
            Vector3 outward = normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a);
            Vector3 axis = (outward * .8f + tangents[r] * .6f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangents[r]).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = centers[r] + outward * radii[r] * .7f;
            float baseRadius = radii[r] * .4f, length = Mathf.Max(.06f, radii[r] * (1.1f + .6f * Hash01(salt, 40 + k)));
            int start = vertices.Count;
            float v0 = r / (float)rings * vLength;
            for (int s = 0; s <= 5; s++)
            {
                float b = s / 5f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s * .06f, v0)); colors.Add(white);
                vertices.Add(root + axis * length); uv.Add(new Vector2(s * .06f, v0 + .1f)); colors.Add(white);
                if (s < 5) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // Шов развёртки: у пары вершин одного места — общая нормаль.
        var meshNormals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            meshNormals[a] = meshNormals[b] = (meshNormals[a] + meshNormals[b]).normalized;
        }
        mesh.normals = meshNormals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Детерминированный разброс: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Точки эмиссии: вершины — места, нормали — направления. Треугольники вырожденные.</summary>
    private static Mesh Points(string name, List<Vector3> positions, List<Vector3> directions)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(positions);
        mesh.SetNormals(directions);
        var triangles = new int[((positions.Count + 2) / 3) * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = Mathf.Min(i, positions.Count - 1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>count мест по кольцу [inner, outer], направление — вверх с наклоном наружу tiltMin…tiltMax.</summary>
    private static Mesh RingPoints(string name, int count, float inner, float outer, float tiltMin, float tiltMax, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, Hash01(i, salt + 1));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    // ------------------------------------------------------------- particles

    /// <summary>Кривая по парам (время, значение), отрезки линейные.</summary>
    private static AnimationCurve Curve(params float[] pairs)
    {
        var keys = new Keyframe[pairs.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    /// <summary>Прозрачность по жизни: появление к fadeIn, уход с fadeOut (доли жизни).</summary>
    private static Gradient Alpha(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(.001f, fadeIn)),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    /// <summary>Система с одним залпом, местное пространство, постоянное зерно: вид переигрывает её по возрасту.</summary>
    private static ParticleSystem Particles(GameObject parent, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(parent, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(parent.name + "/" + name);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        return particles;
    }

    private static Quaternion Aim(Vector3 direction) => Quaternion.FromToRotation(Vector3.forward, direction.normalized);

    private static void Collide(ParticleSystem particles, Transform ground, float bounce)
    {
        var collision = particles.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground);
        collision.bounce = bounce;
        collision.dampen = .65f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .45f;
        collision.minKillSpeed = 0f;
    }

    /// <summary>Атлас n×n: кадр наугад на частицу, без анимации.</summary>
    private static void Sheet(ParticleSystem particles, int tiles, float lastFrame)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = tiles; sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
        sheet.cycleCount = 1;
    }

    /// <summary>Комья, камни, щепки: спрайты CFXR 3×3, баллистика, отскок от земли host, тают в конце.</summary>
    private static ParticleSystem Debris(GameObject host, string name, Material material, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float delay, Color light, Color dark)
    {
        var particles = Particles(host, name, count, 1.5f, 2.1f, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Collide(particles, host.transform, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR без освещения, кувыркаются, планируют и ложатся.</summary>
    private static ParticleSystem Leaves(GameObject host, Kit kit, string name, int count, Vector3 at, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, 1.8f, 2.4f, speedMin, speedMax, .16f, .26f, delay);
        particles.transform.localPosition = at;
        var main = particles.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.6f);
        drag.dampen = .12f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        Collide(particles, host.transform, .05f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>Тёплая пыль: мягкое облако CFXR, малая альфа, тормозит, растёт и тает. flat — лежит на земле.</summary>
    private static ParticleSystem Dust(GameObject host, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay, bool flat)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        // Без мягких частиц земля срезала бы стоячее облако прямой линией — поднимаем.
        particles.transform.localPosition = flat ? at : at + Vector3.up * (.45f * sizeMax);
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(DustLight.r, DustLight.g, DustLight.b, alpha),
            new Color(DustDark.r, DustDark.g, DustDark.b, alpha));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.25f);
        drag.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .25f, .95f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.08f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.flip = new Vector3(.5f, .5f, 0f);
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Haze;
        renderer.sortingFudge = flat ? 1f : -1f;
        return particles;
    }

    /// <summary>Пятно на земле: горизонтальный билборд на грунте host, быстро раскрывается, тает с fadeOut.</summary>
    private static ParticleSystem Decal(GameObject host, string name, Material material, float sizeMin, float sizeMax,
        float life, float fadeOut, Color color, float delay, float lift)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, sizeMin, sizeMax, delay);
        particles.transform.localPosition = Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .04f, 1.04f, .08f, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeOut);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 4f;
        return particles;
    }

    /// <summary>Эмиссия из вершин меша по порядку (Loop): каждое место — ровно одна частица.</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, float randomDirection)
    {
        particles.transform.localRotation = Quaternion.identity;
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = points;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.alignToDirection = false;
        shape.randomDirectionAmount = randomDirection;
        shape.normalOffset = 0f;
    }

    /// <summary>
    /// Растущий корень: MeshRenderer-ребёнок корня префаба, имя несёт задержку
    /// выхода (мс) и довинчивание (°) для RootSnarerCombatView. Кора с тенью.
    /// </summary>
    private static void GrowChild(GameObject root, string label, Mesh mesh, Material material, Vector3 position, Quaternion rotation,
        float scale, int delayMs, int twistDegrees)
    {
        var go = new GameObject(RootSnarerCombatView.GrowPrefix + delayMs + RootSnarerCombatView.GrowSeparator + twistDegrees
            + RootSnarerCombatView.GrowSeparator + label);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>Кольцо волны: горизонтальный билборд кольца CFXR, растёт из центра до радиуса волны.</summary>
    private static ParticleSystem Wave(GameObject host, string name, Material material, float diameter, float life,
        float from, Color color, float delay)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, diameter, diameter, delay);
        particles.transform.localPosition = Vector3.up * .05f;
        var main = particles.main;
        main.startColor = color;
        main.startRotation = 0f;
        var shape = particles.shape; shape.enabled = false;
        var size = particles.sizeOverLifetime; size.enabled = true;
        // Быстро разбегается и тормозит к кромке: 0,8 м → радиус волны.
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, from, .35f, .86f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, .45f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 3f;
        return particles;
    }

    /// <summary>Искры и пылинки света: звёздочки CFXR, медленно всплывают и гаснут.</summary>
    private static ParticleSystem Motes(GameObject host, string name, Material material, int count, Vector3 at, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color color, float delay, bool ring)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = -.06f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, .85f),
            new Color(color.r * .8f, color.g * .95f, color.b * .7f, .7f));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ring ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
        shape.angle = ring ? 0f : 20f;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.8f);
        drag.dampen = .15f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .4f, .2f, 1f, 1f, .2f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.1f, .55f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Удар плитами. Корень — в центре моба, +Z — на круг. У каждой плиты
    /// (export.json, контакт кадра 15) — тёмное пятно, звезда трещин, пыль, комья
    /// и камни; сегменты «Seg L/R i» — трещина к кругу: декаль Crater19 на мешевой
    /// частице (оси сегмента, длину и ширину даёт масштаб ребёнка «Crack») и
    /// горсть комьев. Вид ставит сегменты по длине до кромки круга.
    /// </summary>
    /// <summary>
    /// Сбор волны (кадры 8–30 клипа Mend): у каждой плиты в земле — мягкое
    /// золотисто-зелёное свечение и пылинки света, которые всплывают порциями
    /// всю секунду сбора; у ног шевелится пыль и пара листьев. Корень — в
    /// центре моба, +Z — взгляд.
    /// </summary>
    private static void SaveMendChannel(Kit kit)
    {
        var root = new GameObject(RootSnarerCombatView.MendChannelName);
        const float planted = 8f / 30f;
        for (int side = 0; side < 2; side++)
        {
            var slab = new GameObject(side == 0 ? "Slab L" : "Slab R");
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = side == 0 ? RootSnarerCombatView.SlabLeft : RootSnarerCombatView.SlabRight;
            if (kit.Glow != null)
                Decal(slab, "Glow", kit.Glow, 1.1f, 1.4f, 1.1f, .7f, new Color(MendGreen.r, MendGreen.g, MendGreen.b, .5f), planted, .04f);
            if (kit.Spark != null)
            {
                var motes = Motes(slab, "Motes", kit.Spark, 12, Vector3.up * .1f, .3f, .3f, .7f, .07f, .12f, .8f, 1.2f, MendGold, planted, false);
                var emission = motes.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3), new ParticleSystem.Burst(.22f, 3),
                    new ParticleSystem.Burst(.44f, 3), new ParticleSystem.Burst(.62f, 3) });
            }
        }
        Dust(root, kit, "Stir", 4, Vector3.up * .03f, 88f, .6f, .2f, .5f, .6f, .9f, .8f, 1.1f, .18f, planted, true);
        Leaves(root, kit, "Stir Leaves", 4, Vector3.up * .15f, .5f, 1.1f, planted + .1f);
        Save(root);
    }

    /// <summary>
    /// Волна (кадр 30 клипа Mend, 1-mend-ring): золотисто-зелёное кольцо
    /// разбегается от моба до 5 м за полсекунды, за ним второе потоньше;
    /// по земле расползаются светлые корешки-прожилки (маска Hovl Crater19);
    /// листья и искры летят низко наружу, пыль — юбкой. Корень — центр волны.
    /// </summary>
    private static void SaveMendRing(Kit kit)
    {
        var root = new GameObject(RootSnarerCombatView.MendRingName);
        float diameter = Simulation.RootSnarerMendRadius.ToFloat() * 2f;
        if (kit.Ring != null)
        {
            Wave(root, "Ring", kit.Ring, diameter, .7f, .16f, new Color(MendGold.r, MendGold.g, MendGold.b, .75f), 0f);
            Wave(root, "Ring Inner", kit.Ring, diameter * .9f, .6f, .12f, new Color(MendGreen.r, MendGreen.g, MendGreen.b, .5f), .08f);
        }
        // Корешки — тёплое золото с зеленью, не лайм: V2 читалась молниями.
        Decal(root, "Veins", kit.Vein, diameter * .8f, diameter * .88f, 1.3f, .5f,
            new Color((MendGold.r + MendGreen.r) * .45f, (MendGold.g + MendGreen.g) * .45f, (MendGold.b + MendGreen.b) * .4f, .6f), 0f, .03f);
        if (kit.Glow != null)
            Decal(root, "Glow", kit.Glow, 3.2f, 3.8f, .9f, .35f, new Color(MendGold.r, MendGold.g, MendGold.b, .35f), 0f, .04f);
        var leaves = Leaves(root, kit, "Leaves", 14, Vector3.up * .15f, 3.0f, 5.5f, .02f);
        var leafShape = leaves.shape; leafShape.enabled = true;
        leafShape.shapeType = ParticleSystemShapeType.Circle; leafShape.radius = .8f;
        leaves.transform.localRotation = Aim(Vector3.up);
        if (kit.Spark != null)
            Motes(root, "Sparks", kit.Spark, 24, Vector3.up * .2f, 1.0f, 1.8f, 3.6f, .08f, .15f, .7f, 1.1f, MendGold, 0f, true);
        Dust(root, kit, "Skirt", 10, Vector3.up * .03f, 88f, .8f, 2.4f, 4.0f, .8f, 1.3f, .7f, 1.0f, .2f, 0f, true);
        Save(root);
    }

    /// <summary>
    /// Лечение на союзнике: мягкое свечение у ног, листья раскрываются вокруг
    /// тела и всплывают искры. Корень — у ног союзника.
    /// </summary>
    private static void SaveMendLeaves(Kit kit)
    {
        var root = new GameObject(RootSnarerCombatView.MendLeavesName);
        if (kit.Glow != null)
            Decal(root, "Glow", kit.Glow, 1.3f, 1.6f, 1.1f, .5f, new Color(MendGreen.r, MendGreen.g, MendGreen.b, .45f), 0f, .04f);
        var leaves = Leaves(root, kit, "Leaves", 8, Vector3.up * .35f, .8f, 1.6f, 0f);
        var main = leaves.main; main.gravityModifier = .12f;
        var shape = leaves.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = .35f;
        leaves.transform.localRotation = Aim(Vector3.up);
        if (kit.Spark != null)
            Motes(root, "Motes", kit.Spark, 12, Vector3.up * .2f, .45f, .8f, 1.6f, .07f, .13f, .8f, 1.2f, MendGold, 0f, false);
        Save(root);
    }

    private static void SaveSlamCracks(Kit kit)
    {
        var root = new GameObject(RootSnarerCombatView.SlamCracksName);
        for (int side = 0; side < 2; side++)
        {
            var slab = new GameObject(side == 0 ? "Slab L" : "Slab R");
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = side == 0 ? RootSnarerCombatView.SlabLeft : RootSnarerCombatView.SlabRight;
            Decal(slab, "Soil", kit.Splat, .95f, 1.15f, 2.3f, .7f, new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .8f), 0f, .03f);
            Decal(slab, "Cracks", kit.Vein, 1.5f, 1.8f, 2.3f, .7f, CrackTone, 0f, .04f);
            Dust(slab, kit, "Dust", 4, Vector3.zero, 55f, .25f, .6f, 1.3f, .45f, .7f, .7f, 1.0f, .28f, 0f, false);
            Dust(slab, kit, "DustLow", 6, Vector3.up * .03f, 85f, .3f, .9f, 1.8f, .8f, 1.2f, .9f, 1.3f, .28f, .01f, true);
            Debris(slab, "Clods", kit.Clod, 12, Vector3.up * .08f, new Vector3(0f, 1f, .35f), 40f, .25f, 2.6f, 4.6f, .09f, .2f, 1.6f, 0f, SoilLight, SoilDark);
            Debris(slab, "Rocks", kit.Clod, 3, Vector3.up * .08f, new Vector3(0f, 1f, .2f), 35f, .2f, 2.2f, 3.4f, .22f, .34f, 1.9f, 0f, RockLight, RockDark);
        }
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < RootSnarerCombatView.RunSegmentsPerSide; i++)
            {
                var seg = new GameObject(RootSnarerCombatView.SegmentName(side, i));
                seg.transform.SetParent(root.transform, false);
                var crack = Particles(seg, "Crack", 1, 2.1f, 2.1f, 0f, 0f, 1f, 1f, 0f);
                crack.transform.localPosition = Vector3.up * .035f;
                var main = crack.main;
                main.startRotation = 0f;
                main.startColor = CrackTone;
                var shape = crack.shape; shape.enabled = false;
                // Трещина прорезается за первые ~0,12 с: растёт по длине сегмента.
                var size = crack.sizeOverLifetime; size.enabled = true;
                size.separateAxes = true;
                size.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .06f, 1f, 1f, 1f));
                size.y = new ParticleSystem.MinMaxCurve(1f);
                size.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .06f, 1f, 1f, 1f));
                var fade = crack.colorOverLifetime; fade.enabled = true;
                fade.color = Alpha(0f, .72f);
                var renderer = crack.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = kit.Quad;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                renderer.sharedMaterial = kit.Vein;
                renderer.sortingFudge = 4f;
                Debris(seg, "Clods", kit.Clod, 3, Vector3.up * .06f, new Vector3(0f, 1f, .2f), 35f, .2f, 1.4f, 2.8f, .06f, .13f, 1.4f, 0f, SoilLight, SoilDark);
            }
        Save(root);
    }

    /// <summary>
    /// Корни (тик 36). Корень — в центре круга, +Z — от моба. Восемь толстых
    /// корней на кольце 0,95–1,35 м, основанием чуть наружу, крючком внутрь,
    /// шесть колючих побегов по кромке наружу; треснувшая тёмная земля круга,
    /// кольцо мягкой пыли, комья, камни, щепки, листья.
    /// </summary>
    private static void SaveRootsErupt(Kit kit)
    {
        float radius = Simulation.RootSnarerCircleRadius.ToFloat();
        var root = new GameObject(RootSnarerCombatView.RootsEruptName);
        Decal(root, "Soil", kit.Splat, radius * 2.3f, radius * 2.5f, RootSnarerCombatView.RootsEruptLife, .72f,
            new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .75f), 0f, .03f);
        Decal(root, "Cracked", kit.Crack, radius * 2.2f, radius * 2.3f, RootSnarerCombatView.RootsEruptLife, .72f, CrackTone, 0f, .036f);
        Decal(root, "Veins", kit.Vein, radius * 2.9f, radius * 3.1f, RootSnarerCombatView.RootsEruptLife, .72f,
            new Color(CrackTone.r, CrackTone.g, CrackTone.b, .85f), 0f, .042f);
        for (int i = 0; i < 8; i++)
        {
            float angle = (i + Hash01(i, 501) * .6f) / 8f * Mathf.PI * 2f;
            float ring = Mathf.Lerp(.95f, 1.35f, Hash01(i, 502)) * radius / 1.5f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            var inward = Quaternion.AngleAxis((Hash01(i, 503) - .5f) * 24f, Vector3.up) * -outward;
            // Основание наклонено наружу, загиб крючка приводит кончик в круг.
            var rotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(-Mathf.Lerp(8f, 20f, Hash01(i, 504)), 0f, 0f);
            GrowChild(root, "Корень " + i, kit.Curls[i % kit.Curls.Length], i % 3 == 1 ? kit.WoodDark : kit.Wood,
                outward * ring, rotation, Mathf.Lerp(.9f, 1.15f, Hash01(i, 505)), Mathf.RoundToInt(Hash01(i, 506) * 55f), 0);
        }
        for (int i = 0; i < 6; i++)
        {
            float angle = (i + .5f + Hash01(i, 511) * .5f) / 6f * Mathf.PI * 2f;
            float ring = Mathf.Lerp(1.3f, 1.6f, Hash01(i, 512)) * radius / 1.5f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            var rotation = Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(-Mathf.Lerp(5f, 15f, Hash01(i, 513)), 0f, 0f);
            GrowChild(root, "Побег " + i, kit.Stubs[i % kit.Stubs.Length], kit.WoodDark, outward * ring, rotation,
                Mathf.Lerp(.85f, 1.2f, Hash01(i, 514)), 15 + Mathf.RoundToInt(Hash01(i, 515) * 65f), 0);
        }
        var rim = RingPoints("RootSnarerEruptRimPoints", 18, radius * .75f, radius * 1.1f, 55f, 80f, 601);
        var burst = RingPoints("RootSnarerEruptBurstPoints", 16, radius * .6f, radius, 15f, 40f, 611);
        var dustLow = Dust(root, kit, "DustLow", 18, Vector3.up * .03f, 0f, .1f, .4f, 1.0f, 1.2f, 1.9f, 1.1f, 1.6f, .24f, .02f, true);
        FromPoints(dustLow, rim, .3f);
        var dust = Dust(root, kit, "Dust", 8, Vector3.up * .02f, 0f, .1f, .3f, .8f, .7f, 1.1f, .9f, 1.3f, .2f, .03f, false);
        FromPoints(dust, rim, .6f);
        var clods = Debris(root, "Clods", kit.Clod, 24, Vector3.up * .1f, Vector3.up, 0f, .1f, 3.0f, 5.2f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, burst, .35f);
        var rocks = Debris(root, "Rocks", kit.Clod, 7, Vector3.up * .1f, Vector3.up, 0f, .1f, 2.4f, 4.0f, .24f, .38f, 1.9f, 0f, RockLight, RockDark);
        FromPoints(rocks, burst, .3f);
        var splinters = Debris(root, "Splinters", kit.Splinter, 10, Vector3.up * .1f, Vector3.up, 0f, .1f, 2.6f, 4.4f, .11f, .2f, 1.3f, .02f, BarkLight, BarkDark);
        FromPoints(splinters, burst, .5f);
        var leaves = Leaves(root, kit, "Leaves", 6, Vector3.up * .15f, 1.8f, 3.2f, .03f);
        FromPoints(leaves, burst, .6f);
        Save(root);
    }

    /// <summary>
    /// Путы на герое. Корень — у стоп героя (вид держит его на герое), +Y — вверх.
    /// Три кольца вокруг ног (довинчиваются при выходе), два побега у стоп,
    /// трещинки и тёмная земля под ногами, пыль и комья на старте.
    /// </summary>
    private static void SaveSnare(Kit kit)
    {
        var root = new GameObject(RootSnarerCombatView.SnareName);
        Decal(root, "Soil", kit.Splat, .8f, .95f, 1.4f, .5f, new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .7f), 0f, .03f);
        Decal(root, "Cracks", kit.Crack, 1.0f, 1.2f, 1.4f, .5f, CrackTone, 0f, .036f);
        for (int i = 0; i < kit.Coils.Length; i++)
            GrowChild(root, "Кольцо " + i, kit.Coils[i], i == 1 ? kit.WoodDark : kit.Wood, Vector3.zero,
                Quaternion.Euler(0f, i * 120f + (Hash01(i, 701) - .5f) * 40f, 0f), 1f, i * 30, i % 2 == 0 ? 110 : -110);
        for (int i = 0; i < 2; i++)
        {
            var at = new Vector3(i == 0 ? -.32f : .3f, 0f, i == 0 ? .12f : -.14f);
            var rotation = Quaternion.LookRotation(-at.normalized, Vector3.up) * Quaternion.Euler(-10f, 0f, 0f);
            GrowChild(root, "Побег " + i, kit.Stubs[i % kit.Stubs.Length], kit.WoodDark, at, rotation, .7f, 40 + i * 25, 0);
        }
        Dust(root, kit, "DustLow", 6, Vector3.up * .03f, 85f, .3f, .6f, 1.2f, .5f, .8f, .7f, 1.0f, .24f, 0f, true);
        Debris(root, "Clods", kit.Clod, 6, Vector3.up * .06f, Vector3.up, 55f, .3f, 1.6f, 2.8f, .06f, .12f, 1.5f, 0f, SoilLight, SoilDark);
        Save(root);
    }
}
