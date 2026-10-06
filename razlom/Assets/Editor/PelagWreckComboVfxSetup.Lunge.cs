using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ВЫПАД Крушения на стеке серии сабли (06.10 поздно; целевой кадр
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png). Как добивающий сабли
/// (SaveCrashPrefab — стоячая волна к камере, не полоса на земле), но якорная семья — материалом:
///  • удар оземь: СТОЯЧИЙ всплеск у головы якоря — полузвезда от земли (пак «cfxr spikes half impact gradient»), звезда
///    и её эхо (знак махов «cfxr spikes impact dissolve»): хлопок за 2–3 кадра, держится, рвётся порогом с обводом;
///    под ним — короткая ударная звезда и светящиеся трещины (Hovl Crack6) по земле, тёмный кратер (Hovl Crack);
///    вверх летят настоящие камни (3D, Ultimate Nature «Tiny Rock», освещённые, с тяжестью и отскоком), комья, сколы,
///    искры и пыль;
///  • по полосе Sim — СТОЯЧИЙ гребень кобальта: лента, поднятая к экрану (шейдер «Razlom/Wreck Combo Crest» — приёмы
///    волны махов: полосы, грани, протяжка, блик, рваная горячая кромка с обводом, распад гранями), голова на фронте
///    Sim, за фронтом оседает и рвётся; эхо гребня позади, выше и темнее; с фронта — искры, сколы, камни, комья, пыль;
///    тонкий след трещин по земле за гребнем — единственное плоское по полосе.
/// Формы — та же сборка другой краской (контроллер). Звеньев цепи нет. Ассеты свои (ревизия в userData префаба),
/// сохраняются только они; сабля и махи не трогаются (материалы искр, сколов, комьев и вспышки махов берутся как есть).
/// Рисованный выпад и выпад «просто» от библиотеки отвязываются (файлы на месте). Слои — .LungePrefab.cs.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int LungeVersion = 6;
    private static readonly string LungeRevision = "PelagWreckComboLungeV" + LungeVersion;
    public const string LungeName = "VFX_Pelag_WreckComboLunge";
    private const string CrestShaderName = "Razlom/Wreck Combo Crest";
    private const string HalfTexture = CfxrGraphics + "cfxr spikes half impact gradient.png";
    private const string SmokeTexture = CfxrGraphics + "cfxr smoke cloud x4.png";
    private const string GlowCrackTexture = "Assets/Hovl Studio/HSFiles/Textures/Crack6.png";
    private const string LaneCrackTexture = "Assets/Hovl Studio/HSFiles/Textures/Crack5.png";
    private const string RockModels = "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/River/Models/UNS_Tiny_Rock_0";
    private const string PaletteTexture = "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Shared/Palettes/Textures/UNS_Color_Palette.png";

    /// <summary>Префаб живёт, с: удар, ход фронта (8 шагов ≈ 0,27 с), оседание гребня, камни на земле.</summary>
    internal const float LungeLife = 1.7f;

    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    [InitializeOnLoadMethod]
    private static void ScheduleLunge()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallLunge(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: выпад на стеке сабли — подключить")]
    public static void InstallLunge() => InstallLunge(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: выпад на стеке сабли — пересобрать")]
    public static void RebuildLunge() => InstallLunge(true);

    public static void InstallLunge(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string path in new[] { HalfTexture, SmokeTexture, GlowCrackTexture, LaneCrackTexture, PaletteTexture, BurstTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[wreck-combo-lunge] Нет текстуры пака " + path + " — выпад не собран.");
                return;
            }
        if (Shader.Find(CrestShaderName) == null || Shader.Find(BitShaderName) == null)
        {
            Debug.LogWarning("[wreck-combo-lunge] Нет шейдеров «Wreck Combo Crest/Bit» — выпад не собран.");
            return;
        }
        // Искры, сколы, комья и вспышка — материалы махов (собираются их установкой).
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Spark")) == null) Install(false);
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Spark")) == null) return;
        if (force || !LungeUpToDate()) BuildLunge();
        if (!PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckComboLunge, PrefabPath(LungeName), 2))
            Debug.LogWarning("[wreck-combo-lunge] Префаб не нашёлся — запись библиотеки не создана.");
        // Выпад рисует только этот стек: рисованный выпад и выпад «просто» отвязаны (их префабы и код на месте).
        Unbind(library, PelagVfxId.WreckPaintedLunge, PelagVfxId.WreckLunge);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool LungeUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(LungeName));
        return importer != null && importer.userData == LungeRevision;
    }

    private static void BuildLunge()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);
        Shader crestShader = Shader.Find(CrestShaderName);
        Shader bitShader = Shader.Find(BitShaderName);

        Material crest = CrestMaterial(crestShader, "M_WreckLunge_Crest", facets);
        Material echo = CrestMaterial(crestShader, "M_WreckLunge_CrestEcho", facets);
        // Эхо — глубже и темнее, без блика, рвётся и гаснет раньше: задняя стенка вала, толщина металла.
        echo.SetVector("_Bands", new Vector4(.30f, .68f, .86f, .12f));
        echo.SetFloat("_Sheen", 0f);
        echo.SetFloat("_Streaks", .45f);
        echo.SetFloat("_Glow", .12f);
        echo.SetFloat("_FrontHot", .10f);
        echo.SetFloat("_EdgeRag", .22f);
        echo.SetFloat("_ErodeFrom", .10f);
        echo.SetFloat("_ErodeTo", .32f);
        echo.SetFloat("_FadeFrom", .26f);
        echo.SetFloat("_FadeTo", .38f);
        echo.SetFloat("_CameraPush", .10f);

        // Звезда выпада — своя: белое только в сердцевине, лучи кобальтом (знак махов на весь размер белел).
        Material star = BitMaterial(bitShader, "M_WreckLunge_Star", BurstTexture, 1.15f, .06f, .92f, 1.3f, .055f, .15f, .50f);
        Material half = BitMaterial(bitShader, "M_WreckLunge_Half", HalfTexture, 1f, .08f, .97f, 1.5f, .045f, .12f, .62f);
        // Трещины — те же листы Hovl дважды: тёмные широкие (кратер, след) и под ними узкие светящиеся (холод из трещин).
        Material glow = BitMaterial(bitShader, "M_WreckLunge_Glow", GlowCrackTexture, 1.6f, .34f, .95f, 1f, .04f, .08f, .30f);
        Material laneGlow = BitMaterial(bitShader, "M_WreckLunge_LaneGlow", LaneCrackTexture, 1.5f, .34f, .95f, 1f, .04f, .08f, .30f);
        Material crater = BitMaterial(bitShader, "M_WreckLunge_Crater", GlowCrackTexture, 1.6f, .12f, .55f, 1.4f, .06f, .20f, 2f);
        Material scorch = BitMaterial(bitShader, "M_WreckLunge_Scorch", LaneCrackTexture, 1.5f, .14f, .60f, 1.4f, .06f, .20f, 2f);
        foreach (var m in new[] { crater, scorch }) m.SetColor("_Shade", new Color(.70f, .70f, .75f, 1f));
        // Пыль — мультяшные клубы пака (силуэт из альфы, два тона: тень темнее), без обвода и пузырей.
        Material dust = BitMaterial(bitShader, "M_WreckLunge_Dust", SmokeTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        dust.SetFloat("_ShapeAlpha", 1f);
        dust.SetFloat("_BevelMul", .74f);
        dust.SetColor("_BevelAdd", Color.black);
        foreach (var m in new[] { star, half, dust }) m.SetFloat("_CameraPush", .25f);
        crater.SetFloat("_CameraPush", 0f);
        scorch.SetFloat("_CameraPush", 0f);
        glow.SetFloat("_CameraPush", .05f);
        laneGlow.SetFloat("_CameraPush", .05f);
        Material rock = RockMaterial("M_WreckLunge_Rock");

        foreach (var m in new[] { crest, echo, star, half, glow, laneGlow, scorch, crater, dust, rock })
        {
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }
        Mesh[] rocks = RockMeshes();
        if (rocks.Length == 0) { Debug.LogWarning("[wreck-combo-lunge] Нет камней Ultimate Nature — выпад не собран."); return; }

        var lunge = new LungeMaterials
        {
            Crest = crest, Echo = echo, Half = half, Glow = glow, LaneGlow = laneGlow, Scorch = scorch, Crater = crater, Dust = dust,
            Rock = rock, Rocks = rocks, Burst = star,
            Spark = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Spark")),
            Chip = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Chip")),
            Clod = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Clod"))
        };
        SaveLungePrefab(lunge);

        var importer = AssetImporter.GetAtPath(PrefabPath(LungeName));
        if (importer != null && importer.userData != LungeRevision)
        {
            importer.userData = LungeRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-combo-lunge] Выпад Крушения на стеке сабли собран: " + LungeName + " (" + LungeRevision + ").");
    }

    private struct LungeMaterials
    {
        public Material Crest, Echo, Half, Glow, LaneGlow, Scorch, Crater, Dust, Rock, Burst, Spark, Chip, Clod;
        public Mesh[] Rocks;
    }

    private static Material CrestMaterial(Shader shader, string name, Texture2D facets)
    {
        Material m = Fresh(name, shader);
        m.SetTexture("_FacetTex", facets);
        m.SetColor("_Deep", Deep);
        m.SetColor("_Mid", Mid);
        m.SetColor("_Light", Light);
        m.SetColor("_Hot", Hot);
        m.SetColor("_HotShade", HotShade);
        m.SetColor("_Outline", Ink);
        m.SetVector("_FacetScale", new Vector4(1.4f, 2.2f, .9f, .8f));
        m.SetVector("_SliverScale", new Vector4(.55f, 4.5f, .80f, 1.6f));
        m.SetVector("_Bands", new Vector4(.20f, .50f, .78f, .10f));
        m.SetFloat("_EdgeRag", .18f);
        m.SetFloat("_EdgeTeeth", .16f);
        m.SetFloat("_TeethCount", 15f);
        m.SetFloat("_FrontHot", .25f);
        m.SetFloat("_FrontHotSeconds", .10f);
        m.SetFloat("_OutlinePx", 2.4f);
        m.SetFloat("_ErodeGain", 1.6f);
        m.SetFloat("_ErodeFrom", .14f);
        m.SetFloat("_ErodeTo", .38f);
        m.SetFloat("_ErodeBias", .02f);
        m.SetFloat("_FadeFrom", .32f);
        m.SetFloat("_FadeTo", .44f);
        m.SetFloat("_Streaks", .55f);
        m.SetFloat("_Sheen", .6f);
        m.SetFloat("_SheenRepeat", 1.6f);
        m.SetFloat("_SheenRate", 4.5f);
        m.SetFloat("_SheenWidth", .045f);
        m.SetFloat("_Glow", .34f);
        m.SetFloat("_Opacity", 1f);
        m.SetFloat("_FlowSpeed", 1.4f);
        m.SetFloat("_CameraPush", .20f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Камни — освещённые частицы-меши URP (объём от солнца арены) с палитрой пака Ultimate Nature.</summary>
    private static Material RockMaterial(string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        Material m = Fresh(name, shader);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteTexture));
        m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>
    /// Камни пака «Tiny Rock» (LOD2, 80–93 вершин) копиями в свои меши: частицам нужен читаемый меш, а импорт пака
    /// не трогается. Центр — в начало координат, размер — к 1 м по наибольшей оси (размер частицы = метры).
    /// </summary>
    private static Mesh[] RockMeshes()
    {
        var list = new System.Collections.Generic.List<Mesh>();
        for (int i = 1; i <= 5; i++)
        {
            Mesh source = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(RockModels + i + ".fbx"))
                if (asset is Mesh mesh && mesh.name.EndsWith("_LOD2")) source = mesh;
            if (source == null) continue;
            Mesh copy = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/WreckLungeRock" + i + ".asset", "WreckLungeRock" + i);
            Vector3[] vertices = source.vertices;
            Bounds b = source.bounds;
            float k = 1f / Mathf.Max(.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            for (int v = 0; v < vertices.Length; v++) vertices[v] = (vertices[v] - b.center) * k;
            copy.vertices = vertices;
            copy.normals = source.normals;
            // Все вершины — в одну клетку палитры пака (светлый тёплый серый камень a7a38e): у «Tiny Rock» верх мшистый и
            // жёлтый (L2: камни читались лимонами); объём даёт свет арены, оттенок земли/камня — цвет частицы.
            var uv = new Vector2[vertices.Length];
            for (int v = 0; v < uv.Length; v++) uv[v] = new Vector2(.3125f, .3955f);
            copy.uv = uv;
            copy.triangles = source.triangles;
            copy.RecalculateBounds();
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            list.Add(copy);
        }
        return list.ToArray();
    }
}
