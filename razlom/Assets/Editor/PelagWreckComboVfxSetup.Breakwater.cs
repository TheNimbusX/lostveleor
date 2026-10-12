using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение · ВОЛНОРЕЗ (форма 11; 07.10; целевой кадр
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-breakwater.png). Стек слоёв, а не один плоский слой
/// (уроки 06.10): главное — СТЕНА НАСТОЯЩИХ 3D-ПЛИТ (меши пака RPG Tiny Fantasy Forest «Rock04/Rock06_1/Rock03_1» копиями
/// в единичный ящик), освещённых солнцем арены и зелёным светом фронта, наклонённых вперёд, как вал; свет морской зелени
/// — из трещин плит (шейдер «Razlom/Wreck Breakwater Slab») и стоячими полосами света в щелях между плитами; по земле —
/// второй слой: светящиеся и тёмные трещины под стеной. У подошвы каждой плиты — пыль и комья; обломки — 3D-камни
/// (освещённые, без GPU-инстансинга). Несомые — всплеск гребня и крошка из-под тела; обрушение — плиты рушатся в
/// обломки, пыль, зелёные искры; о преграду — стоячий каменный взрыв и вспышка зелёных трещин.
/// Пены, воды, молний, красного/оранжевого/золотого нет; чёрной обводки нет (контур — тёмная морская зелень).
/// Сроки и размеры — Sim (PelagWreckBreakwaterLook). Свои ассеты (ревизия в userData префаба), сохраняются только они;
/// выпад базы, махи и сабля не трогаются. Префаб — .BreakwaterPrefab.cs.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int BreakVersion = 9;
    private static readonly string BreakRevision = "PelagWreckBreakwaterV" + BreakVersion;
    public const string BreakWallName = "VFX_Pelag_WreckBreakWall";
    private const string BreakSlabShaderName = "Razlom/Wreck Breakwater Slab";
    private const string BreakRockModels = "Assets/RPG Tiny Fantasy Forest PBR/Mesh/Rock/";
    private const string BreakCellTexture = "Assets/Hovl Studio/HSFiles/Textures/Noise29.png";
    private const string BreakNetTexture = "Assets/Hovl Studio/HSFiles/Textures/Crack4.png";
    private const string BreakSeamTexture = "Assets/Hovl Studio/HSFiles/Textures/Crack5.png";

    [InitializeOnLoadMethod]
    private static void ScheduleBreakwater()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallBreakwater(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: Волнорез — подключить")]
    public static void InstallBreakwater() => InstallBreakwater(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: Волнорез — пересобрать")]
    public static void RebuildBreakwater() => InstallBreakwater(true);

    public static void InstallBreakwater(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string shader in new[] { BreakSlabShaderName, BitShaderName })
            if (Shader.Find(shader) == null)
            {
                Debug.LogWarning("[wreck-breakwater] Нет шейдера «" + shader + "» — Волнорез не собран.");
                return;
            }
        foreach (string path in new[] { BreakCellTexture, BreakNetTexture, BreakSeamTexture, GlowCrackTexture, SparkTexture, BurstTexture,
                     HalfTexture, SmokeTexture, DebrisTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[wreck-breakwater] Нет текстуры " + path + " — Волнорез не собран.");
                return;
            }
        if (force || !BreakUpToDate()) BuildBreakwater();
        if (!PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckBreakWall, PrefabPath(BreakWallName), 2))
            Debug.LogWarning("[wreck-breakwater] Префаб не нашёлся — запись библиотеки не создана.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool BreakUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(BreakWallName));
        return importer != null && importer.userData == BreakRevision;
    }

    private struct BreakMaterials
    {
        public Material Slab, Chunk, Seam, GroundGlow, GroundCrack, Spark, Half, Star, Dust, Clod;
        public Mesh[] Slabs, Chunks;
    }

    // Цвета частиц — вершинные (шейдер Bit их не переводит): сразу в линейном пространстве.
    internal static readonly Color BreakGreen = new Color(.014f, .45f, .21f, 1f);
    internal static readonly Color BreakMint = new Color(.30f, 1.05f, .70f, 1f);
    private static readonly Color BreakContour = new Color(.012f, .10f, .065f, .9f);

    private static void BuildBreakwater()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        Shader bit = Shader.Find(BitShaderName);
        var m = new BreakMaterials();

        // ---- камень плит и обломков: освещённый, грани, свет трещин морской зелени (у обломков — только цвет частицы).
        m.Slab = BreakStone("M_WreckBreak_Slab", false);
        m.Chunk = BreakStone("M_WreckBreak_Chunk", true);

        // ---- свет в щелях между плитами: стоячий веер-полузвезда (лучи вверх) за каждой плитой — его закрывают соседние
        //      плиты, виден он только в щели. Раунд 3: жилы Crack5 после порога были почти не видны — сплошной веер пака.
        // Раунд 4: свет в щелях слабее целевого кадра — веер держится полным дольше (порог растёт медленнее).
        m.Seam = BreakBit(bit, "M_WreckBreak_Seam", HalfTexture, 1.25f, .05f, .42f, 1.8f, .04f, .12f, .55f);
        // ---- земля под стеной: светящаяся сетка трещин (Crack4) и тёмные трещины, что остаются после стены.
        m.GroundGlow = BreakBit(bit, "M_WreckBreak_GroundGlow", BreakNetTexture, 1.7f, .30f, .92f, 1.3f, .04f, .08f, .36f);
        m.GroundCrack = BitMaterial(bit, "M_WreckBreak_GroundCrack", BreakNetTexture, 1.6f, .12f, .62f, 1.5f, .07f, .22f, 2f);
        m.GroundCrack.SetColor("_Shade", new Color(.72f, .72f, .74f, 1f));
        m.GroundCrack.SetColor("_Outline", new Color(.035f, .025f, .02f, .85f));
        // ---- искры зелёные, всплеск гребня (полузвезда от земли), звезда удара о преграду, свет трещин у конца стены.
        m.Spark = BreakBit(bit, "M_WreckBreak_Spark", SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .36f);
        // Раунд 4: всплеск гребня под несомыми копился у фронта бело-голубым пятном — мята только в самой сердцевине.
        m.Half = BreakBit(bit, "M_WreckBreak_Half", HalfTexture, 1f, .08f, .97f, 1.5f, .045f, .12f, .74f);
        m.Star = BreakBit(bit, "M_WreckBreak_Star", BurstTexture, 1.15f, .06f, .92f, 1.3f, .055f, .15f, .52f);
        // ---- пыль: мультяшные клубы пака, два тона; комья: лист пака, скос бурый.
        m.Dust = BitMaterial(bit, "M_WreckBreak_Dust", SmokeTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        m.Dust.SetFloat("_ShapeAlpha", 1f);
        m.Dust.SetFloat("_BevelMul", .74f);
        m.Dust.SetColor("_BevelAdd", Color.black);
        m.Dust.SetColor("_Outline", new Color(.16f, .12f, .09f, .6f));
        m.Clod = BitMaterial(bit, "M_WreckBreak_Clod", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        m.Clod.SetFloat("_ShapeAlpha", 1f);
        m.Clod.SetColor("_BevelAdd", new Color(.46f, .34f, .22f, 1f));
        m.Clod.SetColor("_Outline", new Color(.06f, .04f, .03f, .94f));
        m.Seam.SetFloat("_CameraPush", -.05f);
        m.GroundGlow.SetFloat("_CameraPush", .02f);
        m.GroundCrack.SetFloat("_CameraPush", 0f);
        m.Spark.SetFloat("_CameraPush", .35f);
        m.Half.SetFloat("_CameraPush", .25f);
        m.Star.SetFloat("_CameraPush", .45f);
        m.Dust.SetFloat("_CameraPush", .25f);
        m.Clod.SetFloat("_CameraPush", .3f);

        foreach (var mat in new[] { m.Slab, m.Chunk, m.Seam, m.GroundGlow, m.GroundCrack, m.Spark, m.Half, m.Star, m.Dust, m.Clod })
        {
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }
        m.Slabs = BreakSlabMeshes();
        m.Chunks = BreakChunkMeshes();
        if (m.Slabs.Length == 0 || m.Chunks.Length == 0) { Debug.LogWarning("[wreck-breakwater] Нет камней пака RPG Tiny Fantasy Forest — Волнорез не собран."); return; }

        SaveBreakWallPrefab(m);
        var importer = AssetImporter.GetAtPath(PrefabPath(BreakWallName));
        if (importer != null && importer.userData != BreakRevision)
        {
            importer.userData = BreakRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-breakwater] Волнорез собран: " + BreakWallName + " (" + BreakRevision + ").");
    }

    /// <summary>Камень плиты: светлый тёплый серый, тень холоднее, верх светлее, подошва тёмная; свет трещин — морская зелень.</summary>
    private static Material BreakStone(string name, bool particle)
    {
        Material m = Fresh(name, Shader.Find(BreakSlabShaderName));
        m.SetTexture("_CrackTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BreakCellTexture));
        m.SetFloat("_CrackScale", 1.1f);
        // Проба 1: светлый тёплый камень под тёплым светом выходил персиковым — серый, как плиты целевого кадра.
        // Раунд 1 в игре: свет арены (×~3 с грейдом) делал серый #84878A кремовыми «зубами» — камень темнее и серее,
        // в кадре выходит серый камень целевого кадра.
        // Раунд 2: серый под тёплым грейдом и зелёным светом фронта уходил в хаки — камень холоднее, верх светлее (свет
        // солнца на верхних гранях, как на целевом кадре).
        // Раунд 4: под тёплым грейдом камень всё ещё жёлто-бежевый, у целевого кадра — серо-бурый: ещё холоднее и темнее.
        m.SetColor("_Stone", Hex(0x575D68));
        // Раунд 3 рядом с целевым кадром: камень ровно-светлый, без контраста — тень темнее и шире, верх светлее.
        m.SetColor("_StoneShade", Hex(0x22252D));
        m.SetColor("_StoneTop", Hex(0x8A9298));
        m.SetColor("_FootColor", Hex(0x3A332E));
        m.SetFloat("_FootHeight", .30f);
        m.SetFloat("_LightThreshold", .58f);
        m.SetFloat("_LightFeather", .045f);
        m.SetFloat("_AmbientWeight", .22f);
        m.SetFloat("_EdgeDark", .34f);
        m.SetFloat("_Facet", .85f);
        // Проба 1: HDR выше 1 в пробе (без тонмаппинга) резался в бело-голубое — свет насыщенный, ядро — мята, не белое.
        // Проба 2: свет выходил бирюзой (это цвет сабли) — зеленее, к #1FB37E; ядро — мята.
        m.SetColor("_GlowColor", new Color(.015f, .62f, .22f, 1f));
        m.SetColor("_GlowCore", new Color(.20f, .95f, .50f, 1f));
        // Раунд 2: свет фронта красил всю ближнюю плиту в зелёное стекло — на камне вдвое слабее (мобам — полный).
        // Раунд 4: свет фронта вблизи давал на ближней плите бело-голубое пятно («мыло») — на камне почти без него.
        m.SetFloat("_PointLightGain", .35f);
        m.SetFloat("_Glow", 1f);
        m.SetFloat("_GlowRise", .32f);
        m.SetFloat("_CrackWidth", .88f);
        m.SetFloat("_FootSeam", .06f);
        // Проба 2: свет на половине плиты — «облиты краской»; свет в щели — нижняя треть, бока — у земли.
        m.SetFloat("_BackRise", .32f);
        m.SetFloat("_SideRise", .12f);
        m.SetFloat("_BackFacing", .50f);
        m.SetFloat("_Rag", .12f);
        m.SetFloat("_BreakScale", .55f);
        // Раунд 1: свет только у подошвы читался зелёными «носками» — свет и по кромкам граней в щелях, почти до верха.
        m.SetFloat("_RimFrom", .60f);
        m.SetFloat("_RimRise", .90f);
        m.SetFloat("_Crumble", 0f);
        m.SetFloat("_VertexColor", particle ? 1f : 0f);
        m.enableInstancing = false;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Материал стека (Bit) в морской зелени: контур — тёмная зелень (не чёрный), тень — зелёная, ядро — бледная мята.</summary>
    private static Material BreakBit(Shader shader, string name, string texture, float gain,
        float cutFrom, float cutTo, float cutPower, float outline, float shade, float core)
    {
        Material m = BitMaterial(shader, name, texture, gain, cutFrom, cutTo, cutPower, outline, shade, core);
        m.SetColor("_Outline", BreakContour);
        m.SetColor("_Shade", new Color(.55f, .80f, .70f, 1f));
        m.SetColor("_Core", new Color(.78f, 1.30f, 1.06f, 1f));
        return m;
    }

    /// <summary>
    /// Плиты: меши пака копиями в единичный ящик (x, z −.5….5 по центру, y 0…1 — низ на земле); пропорции плиты даёт
    /// масштаб объекта (контроллер). Нормали пересчитаны под растяжку ящика; цвет вершин не пишется.
    /// </summary>
    private static Mesh[] BreakSlabMeshes()
    {
        var list = new List<Mesh>();
        // Проба 2: одни «Rock04/06» в растяжке читались скруглёнными капсулами — гранёные Rock01/03_1 разнообразят верх.
        // Раунд 1: «Rock04/06_1» в растяжке — одинаковые скруглённые «зубы»; больше гранёных Rock01/03_1 (рваный верх).
        string[] sources = { "Rock01", "Rock03_1", "Rock04", "Rock01", "Rock03_1", "Rock06_1" };
        for (int i = 0; i < sources.Length; i++)
        {
            Mesh source = BreakSource(sources[i]);
            if (source == null) continue;
            // Повторы — повёрнутые на 90° вокруг вертикали: другая грань к камере.
            Quaternion turn = Quaternion.Euler(0f, i >= 3 ? 90f : 0f, 0f);
            Vector3[] v = source.vertices;
            Vector3[] n = source.normals;
            for (int k = 0; k < v.Length; k++) { v[k] = turn * v[k]; n[k] = turn * n[k]; }
            var b = new Bounds(v[0], Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(p);
            Vector3 size = new Vector3(Mathf.Max(.01f, b.size.x), Mathf.Max(.01f, b.size.y), Mathf.Max(.01f, b.size.z));
            for (int k = 0; k < v.Length; k++)
            {
                v[k] = new Vector3((v[k].x - b.center.x) / size.x, (v[k].y - b.min.y) / size.y, (v[k].z - b.center.z) / size.z);
                n[k] = Vector3.Scale(n[k], size).normalized;
            }
            list.Add(BreakSave("WreckBreakSlab" + (i + 1), v, n, source));
        }
        return list.ToArray();
    }

    /// <summary>Обломки: те же камни пака, центр в начале координат, наибольшая ось — 1 м (размер частицы = метры); не больше 4.</summary>
    private static Mesh[] BreakChunkMeshes()
    {
        var list = new List<Mesh>();
        foreach (string name in new[] { "Rock01", "Rock03_1", "Rock04", "Rock06_1" })
        {
            Mesh source = BreakSource(name);
            if (source == null) continue;
            Vector3[] v = source.vertices;
            Bounds b = source.bounds;
            float k = 1f / Mathf.Max(.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) * k;
            list.Add(BreakSave("WreckBreakChunk" + (list.Count + 1), v, source.normals, source));
        }
        return list.ToArray();
    }

    private static Mesh BreakSource(string name)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(BreakRockModels + name + ".fbx"))
            if (asset is Mesh mesh && mesh.name == "LOD0") return mesh;
        return null;
    }

    private static Mesh BreakSave(string name, Vector3[] vertices, Vector3[] normals, Mesh source)
    {
        Mesh copy = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        copy.vertices = vertices;
        copy.normals = normals;
        copy.uv = source.uv;
        copy.triangles = source.triangles;
        copy.RecalculateBounds();
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        return copy;
    }
}
