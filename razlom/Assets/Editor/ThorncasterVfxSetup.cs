using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX ШИПОМЁТА, ревизия V1 (план мобов леса, 26.09). Целевые кадры — выбор
/// владельца (ART/…/5.forest-elite-shipomet/production/vfx_target_frames_2026-09-26):
///
/// • Линия (1-line-spikes): по сегменту линии (1,75 × 1,4 м) из треснувшей
///   земли встаёт один крупный деревянный шип с красно-оранжевыми кончиками —
///   куст из главного шипа, трёх боковых и двух побегов; под ним полоса
///   трещин вдоль линии, тёмное пятно земли, комья, щепки, пара листьев и
///   немного тёплой пыли. Шипы встают по одному — в тик своего удара (27/33/
///   39/45 от начала линии), стоят, пока руки Шипомёта в земле, и уходят вниз.
/// • Всплеск (3-burst): вокруг Шипомёта звездой — длинные шипы низко над
///   землёй на весь круг 2,4 м, короткие между ними, кольцо пыли, щепки,
///   комья, листья, пятно и звезда трещин.
/// • Выстрел: щепки и дымок у кончика руки на выпуске. Сам шип, его след и
///   щепки там, где он встал, рисует ForestThornShotView. Полосы на земле у
///   выстрела нет (владелец, 27.09: только снаряд, от которого можно увернуться).
///
/// Паки: CFXR (debris unlit 3x3, debris wood unlit 3x3, лист leave a + cfxr
/// mesh leave — без освещения, smoke cloud x4 blurred и мягкое cloud blur,
/// плёнка sword trail plain как маска трещин), Hovl (Crack4, Crater19,
/// Crater2, Crater40). Кора шипов — bark01_bottom пака Fantasy Forest,
/// переведённая в Textures/ThornWood.png: низ — мшистая кора, верхняя
/// четверть — красно-оранжевый кончик, как шипы на теле Шипомёта; альфа —
/// гладкость (кончик блестит, кора матовая). Меши шипов — свои трубы из колец,
/// как корни воя Вендиго V12 (не плоские полосы).
///
/// Шипы — MeshRenderer'ы в узле «Thorns» (порядок детей — роли: 0 — главный),
/// ось шипа +Y, основание в нуле, полный размер — масштаб в префабе.
/// ThorncasterCombatView растит и опускает их по тикам Sim, частицы ведёт по
/// возрасту через Simulate. Корень префаба лежит на земле: +Z — направление
/// линии (или взгляд Шипомёта), +Y — вверх.
/// </summary>
public static class ThorncasterVfxSetup
{
    // V2 — шип выстрела ThornDart (закрытое веретено вместо трубы с раструбом).
    private const string Revision = "ThorncasterVfxV3";
    private const string Root = "Assets/Resources/VFX/Thorncaster";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string TextureFolder = Root + "/Textures";
    private const string ThornTexture = TextureFolder + "/ThornWood.png";

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

    // Тона — как у Вендиго V12: земля и пыль луга тёплые, без чёрного; листья оливковые.
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f);
    private static readonly Color SoilDark = new Color(.30f, .20f, .12f);
    private static readonly Color DustLight = new Color(.76f, .58f, .39f);
    private static readonly Color DustDark = new Color(.58f, .43f, .28f);
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f);
    private static readonly Color LeafDark = new Color(.42f, .52f, .20f);
    // Щепки — кора шипов: оливково-бурые, часть с красным кончиком.
    private static readonly Color SplinterLight = new Color(.62f, .52f, .30f);
    private static readonly Color SplinterDark = new Color(.42f, .30f, .18f);
    private static readonly Color SplinterRed = new Color(.78f, .30f, .14f);

    private sealed class Kit
    {
        public Material Wood, Clod, Splinter, Leaf, Dust, Haze, Soil, Crater, Crack, Fissure;
        public Mesh LeafMesh, Quad;
        /// <summary>Главный шип, боковые, короткие побеги: ось +Y, длина 1, основание в нуле.</summary>
        public Mesh Spire;
        public Mesh[] Thorns, Shoots;
        /// <summary>
        /// Шип выстрела (ForestThornShotView): без раструба и с закрытым основанием —
        /// в полёте основание смотрит в камеру, и открытая труба читалась воронкой.
        /// </summary>
        public Mesh Dart;
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += () => Install(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => Install(false);
        };
    }

    [MenuItem("Разлом/Шипомёт/VFX: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Шипомёт/VFX: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisUnlit) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(HovlTextures + "Crack4.png") == null || !System.IO.File.Exists(ThornTexture))
        {
            Debug.LogWarning("[thorncaster-vfx] Нет паков CFXR/Hovl или Textures/ThornWood.png — VFX Шипомёта не собран.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + ThorncasterCombatView.BurstPrefab + ".prefab");
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Thorn_ShotRibbon.mat") != null
            && AssetDatabase.LoadAssetAtPath<Mesh>(GeometryFolder + "/ThornDart.asset") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + ThorncasterCombatView.LineSpikePrefab + ".prefab") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + ThorncasterCombatView.ShotReleasePrefab + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "Thorncaster");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Textures");
        var kit = Materials();
        SaveLineSpike(kit);
        SaveBurst(kit);
        SaveShotRelease(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + ThorncasterCombatView.BurstPrefab + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[thorncaster-vfx] Шип линии, всплеск и выстрел собраны из паков, ревизия " + Revision + ".");
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        // Освещённые материалы CFXR в URP бледнеют, дым Hovl без depth-текстуры
        // невидим: только unlit CFXR и его же размытые облака.
        kit.Clod = PackCopy("M_Thorn_Clod", CfxrDebrisUnlit);
        kit.Splinter = PackCopy("M_Thorn_Splinter", CfxrDebrisWood);
        kit.Leaf = Unlit(PackCopy("M_Thorn_Leaf", CfxrLeafMaterial));
        kit.Dust = Plain(PackCopy("M_Thorn_Dust", CfxrSmokeBlurred));
        kit.Haze = Textured(Plain(PackCopy("M_Thorn_DustHaze", CfxrSmokeBlurred)), CfxrCloudBlur, true);
        kit.Soil = Textured(Plain(PackCopy("M_Thorn_Soil", CfxrSmokeBlurred)), HovlTextures + "Crater2.png", false);
        kit.Crater = Textured(Plain(PackCopy("M_Thorn_Crater", CfxrSmokeBlurred)), HovlTextures + "Crater40.png", false);
        kit.Crack = Textured(NoDissolve(PackCopy("M_Thorn_Crack", CfxrTrailMaterial)), HovlTextures + "Crack4.png", true);
        kit.Fissure = Textured(NoDissolve(PackCopy("M_Thorn_Fissure", CfxrTrailMaterial)), HovlTextures + "Crater19.png", true);
        ShotRibbon();
        // Декали рисуются до пыли и комьев.
        foreach (var decal in new[] { kit.Soil, kit.Crater, kit.Crack, kit.Fissure }) { decal.renderQueue = 2990; EditorUtility.SetDirty(decal); }
        kit.Wood = Wood();
        kit.LeafMesh = LoadMesh(CfxrLeafMesh);
        kit.Quad = FlatQuad("ThornFlatQuad");
        // (радиус основания, изгиб, скрутка, узлы, красные отростки у кончика)
        kit.Spire = ThornMesh("ThornSpire", 41, .15f, .05f, .9f, .07f, 3);
        // Шип выстрела: почти прямой, основание сужено (веретено) и закрыто.
        kit.Dart = ThornMesh("ThornDart", 45, .15f, .02f, .9f, .05f, 3, -.45f, true);
        kit.Thorns = new[]
        {
            ThornMesh("ThornA", 42, .13f, .10f, 1.3f, .09f, 1),
            ThornMesh("ThornB", 43, .12f, .16f, 1.8f, .10f, 2),
            ThornMesh("ThornC", 44, .14f, .07f, 1.0f, .08f, 0)
        };
        kit.Shoots = new[]
        {
            ThornMesh("ThornShootA", 51, .16f, .20f, 1.6f, .12f, 1),
            ThornMesh("ThornShootB", 52, .17f, .14f, 2.2f, .14f, 0)
        };
        return kit;
    }

    private static void ShotRibbon()
    {
        // Preserve the accepted Wendigo/CFXR ribbon shader and mask family;
        // tint only this derivative to the Thorncaster bark and red tip.
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VFX/Wendigo/Materials/M_Wendigo_ClawRibbon.mat");
        if (source == null) return;
        var material = PackCopy("M_Thorn_ShotRibbon", AssetDatabase.GetAssetPath(source));
        material.SetColor("_Core", new Color(.88f, .47f, .21f));
        material.SetColor("_Mid", new Color(.68f, .32f, .14f));
        material.SetColor("_Edge", new Color(.42f, .38f, .15f));
        material.SetColor("_Rim", new Color(.23f, .20f, .09f));
        material.SetFloat("_Timed", 0f);
        material.SetFloat("_Glow", .04f);
        material.SetFloat("_TaperSkew", 1.9f);
        material.SetFloat("_TaperPower", .7f);
        EditorUtility.SetDirty(material);
    }

    /// <summary>
    /// Кора шипа: URP Lit с тенью и светом, как пропсы леса и корни Вендиго.
    /// Текстура ThornWood: v = 0 у земли (мох), v = 1 — красный кончик; гладкость
    /// из альфы (кора 0,14, кончик 0,52). Цвет белый — без подъёма яркости.
    /// </summary>
    private static Material Wood()
    {
        var importer = AssetImporter.GetAtPath(ThornTexture) as TextureImporter;
        if (importer != null)
        {
            bool changed = false;
            // С .meta из одного guid карта приходила кубмапой — шипы рисовались белыми.
            if (importer.textureShape != TextureImporterShape.Texture2D) { importer.textureShape = TextureImporterShape.Texture2D; changed = true; }
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            if (!importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }
            if (importer.alphaSource != TextureImporterAlphaSource.FromInput) { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (importer.alphaIsTransparency) { importer.alphaIsTransparency = false; changed = true; }
            if (importer.wrapModeU != TextureWrapMode.Repeat) { importer.wrapModeU = TextureWrapMode.Repeat; changed = true; }
            if (importer.wrapModeV != TextureWrapMode.Clamp) { importer.wrapModeV = TextureWrapMode.Clamp; changed = true; }
            if (importer.maxTextureSize != 1024) { importer.maxTextureSize = 1024; changed = true; }
            if (changed) importer.SaveAndReimport();
        }
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Thorn_Wood.mat", Shader.Find("Universal Render Pipeline/Lit"));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ThornTexture));
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 1f);
        material.SetFloat("_SmoothnessTextureChannel", 1f);
        material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        if (source == null) throw new System.InvalidOperationException("Нет материала пака: " + sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
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
    /// Шип: труба из колец вдоль почти прямого хребта (ось +Y, длина 1,
    /// основание в нуле), сечение с долями и скруткой по высоте, раструб у
    /// земли, узлы коры, острый кончик. prongs красных отростков у кончика —
    /// куст острий, как у шипов цели. Развёртка под ThornWood: v — вдоль
    /// (0 у земли), u — вокруг; отростки целиком в красной части (v ≥ 0,8).
    /// flare — раструб у основания (0,45 — шип из земли; меньше нуля — основание
    /// сужено веретеном), cap — основание закрыто веером (шип в полёте).
    /// </summary>
    private static Mesh ThornMesh(string name, int salt, float radius, float bend, float twist, float knots, int prongs,
        float flare = .45f, bool cap = false)
    {
        const int rings = 14, sides = 10;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        System.Func<float, Vector3> spine = t => new Vector3(bend * t * t + .03f * Mathf.Sin(t * 5f + phase), t,
            bend * .35f * Mathf.Sin(t * 2.7f + phase));
        System.Func<float, float> thickness = t => radius * Mathf.Pow(1f - t, .9f)
            * (1f + flare * Mathf.Pow(Mathf.Max(0f, 1f - t / .18f), 2f))
            * (1f + knots * (Mathf.Sin(t * 13f + phase) + .5f * Mathf.Sin(t * 29f + 2f * phase)) * (1f - t));
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            RingFrame(spine, t, out Vector3 center, out _, out Vector3 side, out Vector3 other);
            float rad = thickness(t);
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .22f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(center + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * rad * lobe);
                uv.Add(new Vector2(s / (float)sides, t));
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                // Обход по часовой, если смотреть снаружи: нормали наружу.
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        if (cap)
        {
            // Веер основания: центр чуть выпукло назад по оси, обход (центр, s, s + 1)
            // даёт нормаль наружу — против оси шипа.
            RingFrame(spine, 0f, out Vector3 bottom, out Vector3 axis0, out _, out _);
            int hub = vertices.Count;
            vertices.Add(bottom - axis0 * (thickness(0f) * .35f));
            uv.Add(new Vector2(.5f, 0f));
            for (int s = 0; s < sides; s++) triangles.AddRange(new[] { hub, s, s + 1 });
        }
        // Красные отростки: конусы из стенки у кончика наружу и вверх.
        for (int k = 0; k < prongs; k++)
        {
            float t = Mathf.Lerp(.56f, .78f, (k + Hash01(salt, 20 + k)) / Mathf.Max(1, prongs));
            RingFrame(spine, t, out Vector3 center, out Vector3 tangent, out Vector3 side, out Vector3 other);
            float a = (k / (float)Mathf.Max(1, prongs) + Hash01(salt, 30 + k) * .3f) * Mathf.PI * 2f;
            Vector3 outward = side * Mathf.Cos(a) + other * Mathf.Sin(a);
            Vector3 axis = (outward * .55f + tangent * .85f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangent).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = center + outward * thickness(t) * .6f;
            float baseRadius = Mathf.Max(.012f, thickness(t) * .55f), length = .13f + .09f * Hash01(salt, 40 + k);
            int start = vertices.Count;
            for (int s = 0; s <= 6; s++)
            {
                float b = s / 6f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s / 6f, .82f));
                vertices.Add(root + axis * length); uv.Add(new Vector2(s / 6f, 1f));
                if (s < 6) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // Шов развёртки: у пары вершин одного места — общая нормаль.
        var normals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
        }
        mesh.normals = normals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void RingFrame(System.Func<float, Vector3> spine, float t, out Vector3 center, out Vector3 tangent,
        out Vector3 side, out Vector3 other)
    {
        center = spine(t);
        tangent = (spine(Mathf.Min(1f, t + .01f)) - spine(Mathf.Max(0f, t - .01f))).normalized;
        side = Vector3.Cross(tangent, Vector3.forward).normalized;
        other = Vector3.Cross(side, tangent);
    }

    /// <summary>
    /// Плоский квадрат на земле с центром в нуле: X — ширина, Z — длина, нормаль
    /// вверх, v — вдоль Z. Цвет вершин 8-битный: Float32-цвет мешевая частица
    /// читает мусором.
    /// </summary>
    private static Mesh FlatQuad(string name)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(new List<Vector3> { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
        mesh.SetColors(new List<Color32> { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
        mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Точки эмиссии: вершины — места, нормали — направления. Треугольники
    /// вырожденные: форма Mesh в режиме Vertex не принимает топологию Points.
    /// </summary>
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

    /// <summary>
    /// Кольцо: count мест по окружности со сдвигом и разбросом радиуса в
    /// [inner, outer], направление — вверх с наклоном наружу tiltMin…tiltMax (от вертикали).
    /// </summary>
    private static Mesh RingPoints(string name, int count, float inner, float outer, float tiltMin, float tiltMax, float lift, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, Hash01(i, salt + 1));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius + Vector3.up * lift);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Детерминированный разброс: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>
    /// Шип-MeshRenderer в узле «Thorns»: основание в at, ось — direction, длина
    /// length (м), толщина thick (множитель к радиусу меша), поворот вокруг оси roll.
    /// </summary>
    private static void Thorn(Transform thorns, string name, Mesh mesh, Material material, Vector3 at, Vector3 direction,
        float length, float thick, float roll)
    {
        var go = new GameObject(name);
        go.transform.SetParent(thorns, false);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized) * Quaternion.AngleAxis(roll, Vector3.up);
        go.transform.localScale = new Vector3(thick, length, thick);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>Направление: вверх с наклоном tilt градусов к азимуту yaw (0 — +Z, 90 — +X).</summary>
    private static Vector3 Tilted(float tilt, float yaw)
    {
        float t = tilt * Mathf.Deg2Rad, y = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(y) * Mathf.Sin(t), Mathf.Cos(t), Mathf.Cos(y) * Mathf.Sin(t));
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

    /// <summary>
    /// Система с одним залпом, местное пространство, постоянное зерно: вид
    /// переигрывает её через Simulate по возрасту, и кадр паузы не дрожит.
    /// </summary>
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

    /// <summary>
    /// Комья, щепки: спрайты CFXR 3×3 (случайный кадр), баллистика, отскок
    /// от земли корня, лежат и тают в конце жизни.
    /// </summary>
    private static ParticleSystem Debris(GameObject root, string name, Material material, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float delay, Color light, Color dark)
    {
        var particles = Particles(root, name, count, 1.5f, 2.1f, speedMin, speedMax, sizeMin, sizeMax, delay);
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
        Collide(particles, root.transform, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR, кувыркаются, планируют и ложатся на землю.</summary>
    private static ParticleSystem Leaves(GameObject root, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(root, name, count, 1.6f, 2.2f, speedMin, speedMax, .15f, .24f, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .2f;
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.6f);
        drag.dampen = .12f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        Collide(particles, root.transform, .05f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>
    /// Пыль: мягкое облако CFXR (haze) или размытые клубы 2×2, тёплая охра с
    /// малой альфой, тормозит, растёт и тает. flat — облако лежит на земле.
    /// </summary>
    private static ParticleSystem Dust(GameObject root, Kit kit, string name, int count, Vector3 at, Vector3 direction, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay, bool flat, bool haze = true)
    {
        var particles = Particles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        // Стоячее облако без мягких частиц земля срезала бы прямой линией — поднимаем.
        particles.transform.localPosition = flat ? at : at + Vector3.up * (.45f * sizeMax);
        particles.transform.localRotation = Aim(direction);
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
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .25f, .95f, 1f, 1.4f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.08f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (haze) renderer.flip = new Vector3(.5f, .5f, 0f);
        else Sheet(particles, 2, 3.99f);
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = haze ? kit.Haze : kit.Dust;
        renderer.sortingFudge = flat ? 1f : -1f;
        return particles;
    }

    /// <summary>
    /// Круглое пятно на земле: «горизонтальный билборд» на грунте корня,
    /// быстро раскрывается и тает с fadeOut (доля жизни).
    /// </summary>
    private static ParticleSystem Decal(GameObject root, string name, Material material, Vector3 at, float size,
        float life, float fadeOut, Color color, float lift)
    {
        var particles = Particles(root, name, 1, life, life, 0f, 0f, size, size, 0f);
        particles.transform.localPosition = at + Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var grow = particles.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .04f, 1.04f, .08f, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeOut);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 4f;
        return particles;
    }

    /// <summary>
    /// Вытянутое пятно по оси корня (+Z): плоский квадрат пака с местной
    /// ориентацией — полоса трещин лежит вдоль линии, а не как попало.
    /// Раскрывается вдоль за open долю жизни.
    /// </summary>
    private static ParticleSystem Strip(GameObject root, Kit kit, string name, Material material, Vector3 at, float width, float length,
        float life, float open, float fadeOut, Color color, float lift)
    {
        var particles = Particles(root, name, 1, life, life, 0f, 0f, 1f, 1f, 0f);
        particles.transform.localPosition = at + Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        main.startRotation = 0f;
        main.startSize3D = true;
        main.startSizeX = width;
        main.startSizeY = 1f;
        main.startSizeZ = length;
        var shape = particles.shape; shape.enabled = false;
        // Оси — в одном режиме (кривая): иначе Unity ругается «curves must all be in the same mode».
        var grow = particles.sizeOverLifetime; grow.enabled = true;
        grow.separateAxes = true;
        grow.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, open, 1f, 1f, 1f));
        grow.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, 1f));
        grow.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .35f, open, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeOut);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = kit.Quad;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 4f;
        return particles;
    }

    /// <summary>Эмиссия из вершин меша по порядку (Loop): каждое место — ровно одна частица.</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, float randomDirection)
    {
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = points;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.alignToDirection = false;
        shape.randomDirectionAmount = randomDirection;
        shape.normalOffset = 0f;
        particles.transform.localRotation = Quaternion.identity;
    }

    // ---------------------------------------------------------------- prefabs

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    private static Transform ThornNode(GameObject root)
    {
        var thorns = new GameObject(ThorncasterCombatView.ThornNodeName).transform;
        thorns.SetParent(root.transform, false);
        return thorns;
    }

    /// <summary>
    /// Шип линии (1-line-spikes). Корень — центр сегмента на земле, +Z — вдоль
    /// линии (сегмент 1,75 × 1,4 м). Куст: главный шип ~1,5 м над землёй с
    /// тремя красными отростками у кончика, три боковых шипа и два побега у
    /// основания; полоса трещин вдоль сегмента (с запасом — соседние
    /// сливаются в одну треснувшую полосу), тёмное пятно и воронка у основания,
    /// комья, щепки, пара листьев, немного тёплой пыли.
    /// </summary>
    private static void SaveLineSpike(Kit kit)
    {
        var root = new GameObject(ThorncasterCombatView.LineSpikePrefab);
        var thorns = ThornNode(root);
        // 0 — главный: крупный, чуть вперёд по линии. Основания — под землёй: шип растёт из неё.
        Thorn(thorns, "Thorn0", kit.Spire, kit.Wood, new Vector3(0f, -.12f, .05f), Tilted(9f, 15f), 1.6f, 1.9f, 20f);
        Thorn(thorns, "Thorn1", kit.Thorns[0], kit.Wood, new Vector3(-.24f, -.1f, -.16f), Tilted(24f, -130f), 1.0f, 1.25f, 70f);
        Thorn(thorns, "Thorn2", kit.Thorns[1], kit.Wood, new Vector3(.26f, -.1f, 0f), Tilted(22f, 80f), .9f, 1.15f, 150f);
        Thorn(thorns, "Thorn3", kit.Thorns[2], kit.Wood, new Vector3(.04f, -.1f, .34f), Tilted(30f, 10f), .72f, 1.05f, 240f);
        Thorn(thorns, "Shoot4", kit.Shoots[0], kit.Wood, new Vector3(-.2f, -.06f, .3f), Tilted(38f, -45f), .42f, .9f, 300f);
        Thorn(thorns, "Shoot5", kit.Shoots[1], kit.Wood, new Vector3(.2f, -.06f, -.34f), Tilted(42f, 150f), .36f, .85f, 30f);

        Strip(root, kit, "Cracks", kit.Crack, Vector3.zero, 1.25f, 1.95f, 2.4f, .06f, .72f,
            new Color(SoilDark.r, SoilDark.g, SoilDark.b, .9f), .03f);
        Decal(root, "Soil", kit.Soil, Vector3.zero, 1.5f, 2.4f, .7f, new Color(.46f, .34f, .22f, .8f), .025f);
        Decal(root, "Crater", kit.Crater, Vector3.zero, .95f, 2.4f, .7f, new Color(1f, .95f, .88f, .7f), .035f);
        Debris(root, "Clods", kit.Clod, 14, Vector3.up * .1f, Vector3.up, 38f, .35f, 2.6f, 4.6f, .10f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        Debris(root, "Splinters", kit.Splinter, 7, Vector3.up * .25f, Vector3.up, 30f, .2f, 2.4f, 4.2f, .10f, .19f, 1.3f, .01f, SplinterLight, SplinterDark);
        Leaves(root, kit, "Leaves", 3, Vector3.up * .2f, Vector3.up, 45f, 1.3f, 2.3f, .02f);
        Dust(root, kit, "Dust", 4, Vector3.zero, Vector3.up, 55f, .35f, .5f, 1.1f, .55f, .9f, .8f, 1.2f, .26f, 0f, false);
        Dust(root, kit, "DustLow", 7, Vector3.up * .04f, Vector3.up, 82f, .45f, .7f, 1.4f, .9f, 1.4f, 1.0f, 1.5f, .26f, .01f, true);
        Save(root);
    }

    /// <summary>
    /// Всплеск (3-burst). Корень — центр круга (Шипомёт), +Z — его взгляд.
    /// Звезда: 22 длинных шипа от ног наружу низко над землёй (наклон 64–76°
    /// от вертикали, острия на ~2,1–2,35 м — внутри круга Sim 2,4 м), 12
    /// коротких между ними; тёмное пятно, воронка и звезда трещин, кольцо
    /// пыли по краю круга, щепки с красным, комья, листья.
    /// </summary>
    private static void SaveBurst(Kit kit)
    {
        var root = new GameObject(ThorncasterCombatView.BurstPrefab);
        var thorns = ThornNode(root);
        const int longCount = 22, shortCount = 12;
        for (int i = 0; i < longCount; i++)
        {
            float yaw = (i + Hash01(i, 501) * .7f) / longCount * 360f;
            float r0 = Mathf.Lerp(.3f, .55f, Hash01(i, 502));
            float tilt = Mathf.Lerp(64f, 76f, Hash01(i, 503));
            float length = Mathf.Lerp(1.3f, 1.85f, Hash01(i, 504));
            float thick = Mathf.Lerp(.95f, 1.25f, Hash01(i, 505));
            float lean = yaw + (Hash01(i, 506) - .5f) * 12f;
            var at = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * r0, -.12f, Mathf.Cos(yaw * Mathf.Deg2Rad) * r0);
            var mesh = i % 4 == 0 ? kit.Spire : kit.Thorns[i % 3];
            Thorn(thorns, "Long" + i.ToString("00"), mesh, kit.Wood, at, Tilted(tilt, lean), length, thick, Hash01(i, 507) * 360f);
        }
        for (int j = 0; j < shortCount; j++)
        {
            float yaw = (j + .5f + (Hash01(j, 511) - .5f) * .6f) / shortCount * 360f;
            float r0 = Mathf.Lerp(.7f, 1.4f, Hash01(j, 512));
            float tilt = Mathf.Lerp(40f, 58f, Hash01(j, 513));
            float length = Mathf.Lerp(.5f, .85f, Hash01(j, 514));
            float thick = Mathf.Lerp(.85f, 1.05f, Hash01(j, 515));
            var at = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad) * r0, -.08f, Mathf.Cos(yaw * Mathf.Deg2Rad) * r0);
            var mesh = j % 3 == 0 ? kit.Thorns[j % 3] : kit.Shoots[j % 2];
            Thorn(thorns, "Short" + j.ToString("00"), mesh, kit.Wood, at, Tilted(tilt, yaw), length, thick, Hash01(j, 516) * 360f);
        }

        Decal(root, "Soil", kit.Soil, Vector3.zero, 3.4f, 2.6f, .7f, new Color(.46f, .34f, .22f, .85f), .025f);
        Decal(root, "Crater", kit.Crater, Vector3.zero, 1.8f, 2.6f, .7f, new Color(1f, .95f, .88f, .75f), .032f);
        Decal(root, "Fissures", kit.Fissure, Vector3.zero, 4.6f, 2.6f, .65f,
            new Color(SoilDark.r * .75f, SoilDark.g * .75f, SoilDark.b * .75f, 1f), .04f);
        var rim = RingPoints("ThornBurstDustPoints", 18, 1.3f, 2.3f, 78f, 88f, .05f, 601);
        var spray = RingPoints("ThornBurstSprayPoints", 20, .4f, 1.6f, 50f, 72f, .15f, 651);
        var dustLow = Dust(root, kit, "DustLow", 18, Vector3.zero, Vector3.up, 0f, .1f, .6f, 1.4f, 1.0f, 1.6f, 1.1f, 1.6f, .26f, .02f, true);
        FromPoints(dustLow, rim, .25f);
        var dust = Dust(root, kit, "Dust", 8, Vector3.zero, Vector3.up, 0f, .1f, .3f, .7f, .6f, 1.0f, .9f, 1.3f, .22f, .02f, false);
        FromPoints(dust, rim, .5f);
        dust.transform.localPosition = Vector3.up * .4f;
        var splinters = Debris(root, "Splinters", kit.Splinter, 18, Vector3.zero, Vector3.up, 0f, .1f, 3.0f, 5.5f, .10f, .2f, 1.3f, 0f, SplinterDark, SplinterRed);
        FromPoints(splinters, spray, .3f);
        var clods = Debris(root, "Clods", kit.Clod, 16, Vector3.zero, Vector3.up, 0f, .1f, 2.8f, 5.0f, .11f, .24f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, spray, .35f);
        var leaves = Leaves(root, kit, "Leaves", 8, Vector3.up * .1f, Vector3.up, 0f, 1.8f, 3.2f, .02f);
        FromPoints(leaves, spray, .5f);
        Save(root);
    }

    /// <summary>
    /// Выпуск шипа: у кончика правой руки (+Z — полёт) щепки и дымок вперёд.
    /// Высоко над землёй: без столкновений, короткая жизнь.
    /// </summary>
    private static void SaveShotRelease(Kit kit)
    {
        var root = new GameObject(ThorncasterCombatView.ShotReleasePrefab);
        var splinters = Debris(root, "Splinters", kit.Splinter, 6, Vector3.zero, new Vector3(0f, .2f, 1f), 22f, .04f, 2.5f, 4.5f, .06f, .12f, 1.2f, 0f,
            SplinterLight, SplinterRed);
        Airborne(splinters, .35f, .55f);
        Dust(root, kit, "Puff", 3, Vector3.zero, Vector3.forward, 25f, .05f, .6f, 1.2f, .22f, .38f, .35f, .55f, .22f, 0f, false);
        Save(root);
    }

    /// <summary>Щепки в воздухе: земли под корнем нет — без столкновений, живут коротко.</summary>
    private static void Airborne(ParticleSystem particles, float lifeMin, float lifeMax)
    {
        var collision = particles.collision; collision.enabled = false;
        var main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.duration = Mathf.Max(.2f, lifeMax);
    }
}
