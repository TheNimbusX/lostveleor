using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Бросок якоря · БАЗА «холодное железо» (07–08.10; целевые кадры ART/characters/pelag/anchor-throw-2026-10-07/chatgpt-results/
/// base-hit-v1.png и base-yank-v1.png). Партиал сетапа якорной семьи — его хелперы частиц (Sparks, Chips, Cone, Lane, Rocks, Dust,
/// BitMaterial…), палитра Крушения на стеке сабли (глубина #0A1F4A, кобальт #2E6FE0, сталь #8FC4FF, почти белая сердцевина).
/// Свои ассеты M_ThrowIron_*, VFX_Pelag_ThrowIron*; чужие (камни и пыль выпада Крушения) — только чтение:
///  • VFX_Pelag_ThrowIronChain — струна вдоль цепи («Razlom/Anchor Throw Streak»: основная + ореол шире), искры с головы,
///    шевроны-искры рывка к руке, вспышки головы (колючая звезда кобальта с белым ядром) — частицы без залпа, их выпускает вид;
///  • VFX_Pelag_ThrowIronHit — знак на каждом задетом: стек знака Крушения (белое ядро, кобальтовые лучи втягиваются, эхо),
///    искры по ходу броска, сколы железа, комья, НАСТОЯЩИЕ освещённые камни (инстансинг выключен), пыль;
///  • VFX_Pelag_ThrowIronFurrow — борозда волока («Razlom/Anchor Throw Furrow», единственный плоский слой рывка) и у её
///    переднего края комья, камни и пыль (вид выпускает на шагах тела);
///  • VFX_Pelag_ThrowIronCatch — маленькая вспышка ловли у кулака; VFX_Pelag_ThrowIronStun — звёзды оглушения тяжёлого.
/// Край светящихся вещей — тёмный насыщенный кобальт, НЕ чёрные чернила (урок Призрачного якоря 07.10). Шейдеры, материалы и
/// префабы Крушения не меняются. Сохраняются только свои ассеты и библиотека (SaveAssetIfDirty); общего SaveAssets нет.
/// Пенный Бросок 03.10 (PelagAnchorThrowFoamVfxSetup) от библиотеки отвязан там же, файлы на месте.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int ThrowIronVersion = 6;
    private static readonly string ThrowIronRevision = "PelagThrowIronV" + ThrowIronVersion;
    private const string ThrowStreakShaderName = "Razlom/Anchor Throw Streak";
    private const string ThrowIronFurrowShaderName = "Razlom/Anchor Throw Furrow";
    private const string ThrowIronStarTexture = CfxrGraphics + "cfxr star.png";
    private const string WreckRockMaterial = MaterialFolder + "/M_WreckLunge_Rock.mat";
    private const string WreckDustMaterial = MaterialFolder + "/M_WreckLunge_Dust.mat";
    public const string IronChainName = "VFX_Pelag_ThrowIronChain", IronHitName = "VFX_Pelag_ThrowIronHit",
        IronFurrowName = "VFX_Pelag_ThrowIronFurrow", IronCatchName = "VFX_Pelag_ThrowIronCatch", IronStunName = "VFX_Pelag_ThrowIronStun";

    [InitializeOnLoadMethod]
    private static void ScheduleThrowIron()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallThrowIron(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: база «холодное железо» — подключить")]
    public static void InstallThrowIron() => InstallThrowIron(false);

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: база «холодное железо» — пересобрать")]
    public static void RebuildThrowIron() => InstallThrowIron(true);

    public static void InstallThrowIron(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string shader in new[] { ThrowStreakShaderName, ThrowIronFurrowShaderName, BitShaderName })
            if (Shader.Find(shader) == null)
            {
                Debug.LogWarning("[throw-iron] Нет шейдера «" + shader + "» — база Броска не собрана.");
                return;
            }
        foreach (string path in new[] { FacetTexture, SparkTexture, BurstTexture, DebrisTexture, ThrowIronStarTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[throw-iron] Нет текстуры " + path + " — база Броска не собрана.");
                return;
            }
        if (AssetDatabase.LoadAssetAtPath<Material>(WreckRockMaterial) == null || AssetDatabase.LoadAssetAtPath<Material>(WreckDustMaterial) == null
            || IronRockMeshes().Length == 0)
        {
            Debug.LogWarning("[throw-iron] Нет камней/пыли выпада Крушения (M_WreckLunge_Rock, M_WreckLunge_Dust, WreckLungeRock*) — база Броска не собрана.");
            return;
        }
        if (force || !ThrowIronUpToDate()) BuildThrowIron();
        bool ok = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowIronChain, PrefabPath(IronChainName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowIronHit, PrefabPath(IronHitName), 8);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowIronFurrow, PrefabPath(IronFurrowName), 6);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowIronCatch, PrefabPath(IronCatchName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowIronStun, PrefabPath(IronStunName), 4);
        if (!ok) Debug.LogWarning("[throw-iron] Не все префабы базы Броска нашлись — записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool ThrowIronUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(IronChainName));
        if (importer == null || importer.userData != ThrowIronRevision) return false;
        foreach (string name in new[] { IronHitName, IronFurrowName, IronCatchName, IronStunName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    /// <summary>Цвет из sRGB-числа палитры (материал переводит сам) с множителем HDR.</summary>
    private static Color IronHex(int hex, float gain = 1f, float a = 1f)
    {
        Color c = Hex(hex);
        return new Color(c.r * gain, c.g * gain, c.b * gain, a);
    }

    /// <summary>Вершинный цвет частицы (шейдер не переводит) из sRGB-числа: сразу в линейном.</summary>
    internal static Color IronLinear(int hex, float gain = 1f)
    {
        Color c = Hex(hex).linear;
        return new Color(c.r * gain, c.g * gain, c.b * gain, 1f);
    }

    private static Material IronStreakMaterial(string name, Texture2D facets, bool halo, in IronPaint paint)
    {
        Material m = Fresh(name, Shader.Find(ThrowStreakShaderName));
        m.SetTexture("_FacetTex", facets);
        // V2: край ленты — насыщенный цвет краски, не тёмно-синий (на рыжей земле тёмный край читался обводкой).
        m.SetColor("_Deep", WithAlpha(paint.Edge, 1f));
        m.SetColor("_Mid", paint.Mid);
        m.SetColor("_Light", paint.Light);
        m.SetColor("_Hot", paint.Hot);
        m.SetColor("_HotShade", paint.HotShade);
        m.SetColor("_Edge", WithAlpha(paint.Edge, .92f));
        if (halo)
        {
            // Ореол: свет вокруг цепи — плотный насыщенный кобальт с рваным колючим краем, без сердцевины, за основной лентой.
            // V2: полупрозрачный тёмный кобальт на рыжей земле давал фиолетовую муть — плотнее и светлее.
            m.SetFloat("_HaloMode", 1f);
            m.SetColor("_Deep", paint.HaloDeep);
            m.SetColor("_Light", paint.HaloLight);
            m.SetVector("_Bands", new Vector4(0f, .22f, .66f, 0f));
            m.SetFloat("_EdgePx", 0f);
            m.SetFloat("_EdgeRag", .22f);
            m.SetFloat("_EdgeTeeth", .30f);
            m.SetFloat("_TeethCount", 9f);
            m.SetFloat("_Opacity", PelagAnchorThrowIronLook.HaloOpacity);
            m.SetFloat("_Glow", .18f);
            m.SetFloat("_Sheen", 0f);
            m.SetFloat("_Streaks", 0f);
            m.SetFloat("_CameraPush", -.16f);
        }
        else
        {
            // V4: шевроны в шейдере — редкие и тонкие внутри светлой полосы (белые «стрелки» по всей цепи были шумом);
            // рывок к руке читают искры-шевроны у рук.
            m.SetFloat("_Chevrons", 3.5f);
            m.SetFloat("_ChevronWidth", .06f);
            m.SetFloat("_EdgeRag", .12f);
            m.SetFloat("_EdgeTeeth", .15f);
            m.SetFloat("_CameraPush", -.10f);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Материал стека (Bit) базы Броска в краске: край — тёмный насыщенный цвет краски, не чернила.</summary>
    private static Material IronBit(Shader shader, string name, string texture, float gain, float cutFrom, float cutTo, float cutPower,
        float outline, float shade, float core, in IronPaint paint)
    {
        Material m = BitMaterial(shader, name, texture, gain, cutFrom, cutTo, cutPower, outline, shade, core);
        m.SetColor("_Outline", WithAlpha(paint.Edge, .90f));
        m.SetColor("_Shade", paint.BitShade);
        m.SetColor("_Core", paint.BitCore);
        m.SetFloat("_CameraPush", .6f);
        return m;
    }

    private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    private static void BuildThrowIron()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);
        Shader bit = Shader.Find(BitShaderName);

        Material furrow = Fresh("M_ThrowIron_Furrow", Shader.Find(ThrowIronFurrowShaderName));
        furrow.SetTexture("_FacetTex", facets);
        // V5: в игре канава читалась чёрной дырой — земля светлее (бурая, как на кадре рывка), край мягче, распад раньше.
        furrow.SetColor("_Trench", new Color(.25f, .15f, .08f, 1f));
        furrow.SetColor("_Wall", new Color(.38f, .24f, .12f, 1f));
        furrow.SetColor("_WallShade", new Color(.29f, .18f, .09f, 1f));
        furrow.SetColor("_Lip", new Color(.70f, .51f, .32f, 1f));
        furrow.SetColor("_LipShade", new Color(.48f, .33f, .18f, 1f));
        furrow.SetColor("_Rim", new Color(.18f, .10f, .05f, .45f));
        furrow.SetFloat("_ErodeFrom", .7f);
        furrow.SetFloat("_ErodeTo", 1.3f);
        furrow.SetFloat("_FadeFrom", 1.1f);
        furrow.SetFloat("_FadeTo", PelagAnchorThrowIronLook.FurrowAfterSeconds - .1f);
        EditorUtility.SetDirty(furrow);
        // Комья земли: скос бурый, край тёмно-бурый (общие для базы и форм — земля не перекрашивается).
        Material clod = BitMaterial(bit, "M_ThrowIron_Clod", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        clod.SetFloat("_ShapeAlpha", 1f);
        clod.SetColor("_BevelAdd", new Color(.46f, .34f, .22f, 1f));
        clod.SetColor("_Outline", new Color(.07f, .04f, .02f, .9f));
        clod.SetFloat("_CameraPush", .3f);
        foreach (var m in new[] { furrow, clod }) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }

        ThrowIronMaterials t = BuildIronPaint(BasePaint, facets, bit, furrow, clod);
        SaveIronFurrowPrefab(t);

        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(furrow), ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(clod), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(IronChainName));
        if (importer != null && importer.userData != ThrowIronRevision)
        {
            importer.userData = ThrowIronRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[throw-iron] База Броска якоря «холодное железо» собрана (" + ThrowIronRevision + ").");
    }

    /// <summary>
    /// Материалы и префабы стека в краске <paramref name="paint"/>: струна, ореол, знак, вспышка, искры, звезда оглушения,
    /// сколы → префабы струны, знака, ловли, оглушения. Борозда, комья, камни и пыль — общие (земля одна).
    /// </summary>
    private static ThrowIronMaterials BuildIronPaint(in IronPaint paint, Texture2D facets, Shader bit, Material furrow, Material clod)
    {
        string tag = paint.Tag ?? "";
        Material streak = IronStreakMaterial("M_ThrowIron_Streak" + tag, facets, false, paint);
        Material halo = IronStreakMaterial("M_ThrowIron_Halo" + tag, facets, true, paint);
        // Знак: белое ядро, лучи краски втягиваются (как знак Крушения), край краской.
        // V5: белое — только в сердцевине и холодное (V2: широкое ядро под тёплым грейдом арены читалось кремовым пятном).
        Material burst = IronBit(bit, "M_ThrowIron_Burst" + tag, BurstTexture, 1.5f, .06f, .92f, 1.3f, .06f, .16f, .50f * paint.CoreFrom, paint);
        burst.SetColor("_Core", paint.BurstCore);
        // Вспышка головы: колючая звезда, ядро шире (на кадре голова в белом свете с лучами краски).
        Material flare = IronBit(bit, "M_ThrowIron_Flare" + tag, BurstTexture, 1.4f, .10f, .95f, 1.1f, .05f, .14f, .42f * paint.CoreFrom, paint);
        // Вспышки и знак — к камере на 1,6 м: на камере 48° верх высокого тела иначе закрывает звезду у груди (проба V1).
        flare.SetFloat("_CameraPush", .8f);
        burst.SetFloat("_CameraPush", 1.6f);
        Material spark = IronBit(bit, "M_ThrowIron_Spark" + tag, SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .34f, paint);
        spark.SetFloat("_CameraPush", 1.0f);
        // V6: звёзды оглушения — краска без белого ядра (V5 под грейдом читались белыми).
        Material star = IronBit(bit, "M_ThrowIron_StunStar" + tag, ThrowIronStarTexture, 1f, 0f, .20f, 1f, .07f, .26f, 1.5f, paint);
        star.SetFloat("_CameraPush", .4f);
        // Сколы железа: грань — средняя сталь, скос светлый (не «чёрное конфетти»), край — глубина краски.
        Material chip = BitMaterial(bit, "M_ThrowIron_Chip" + tag, DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        chip.SetFloat("_ShapeAlpha", 1f);
        float[] bevel = PelagWreckComboLook.ChipBevelAdd;
        chip.SetColor("_BevelAdd", new Color(bevel[0], bevel[1], bevel[2], 1f));
        chip.SetColor("_Outline", WithAlpha(paint.ChipEdge, .92f));
        chip.SetFloat("_CameraPush", .6f);

        var all = new[] { streak, halo, burst, flare, spark, star, chip };
        foreach (var m in all) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
        var t = new ThrowIronMaterials
        {
            Streak = streak, Halo = halo, Furrow = furrow, Burst = burst, Flare = flare, Spark = spark, Star = star, Chip = chip, Clod = clod,
            Dust = AssetDatabase.LoadAssetAtPath<Material>(WreckDustMaterial),
            Rocks = new LungeMaterials { Rock = AssetDatabase.LoadAssetAtPath<Material>(WreckRockMaterial), Rocks = IronRockMeshes() }
        };
        SaveIronChainPrefab(t, paint);
        SaveIronHitPrefab(t, paint);
        SaveIronCatchPrefab(t, paint);
        SaveIronStunPrefab(t, paint);
        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем свои.
        foreach (var m in all) AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(m), ImportAssetOptions.ForceUpdate);
        return t;
    }

    /// <summary>
    /// Краска стека: суффикс материалов, имена и id префабов, цвета материалов (sRGB, как Hex — материал переводит сам; HDR
    /// допускается), цвета частиц (линейные: шейдер Bit их не переводит), звенья призрачной цепи (только Веер).
    /// </summary>
    private struct IronPaint
    {
        public string Tag, ChainName, HitName, CatchName, StunName;
        public PelagVfxId ChainId, HitId, CatchId, StunId;
        public Color Edge, Mid, Light, Hot, HotShade, HaloDeep, HaloLight, BurstCore, ChipEdge, BitShade, BitCore;
        public Color Star, Echo, StunStar;
        public Material Links;
        /// <summary>Пунктир орбиты за звёздами оглушения (Гарпун).</summary>
        public bool StunTrail;
        /// <summary>Белое ядро знака и вспышки начинается дальше от центра (×; база 1): у форм звезда — цвет формы, не белое пятно.</summary>
        public float CoreFrom;
    }

    /// <summary>База «холодное железо» — ровно числа V6 (кобальт и сталь Крушения).</summary>
    private static IronPaint BasePaint => new IronPaint
    {
        Tag = "", ChainName = IronChainName, HitName = IronHitName, CatchName = IronCatchName, StunName = IronStunName,
        ChainId = PelagVfxId.AnchorThrowIronChain, HitId = PelagVfxId.AnchorThrowIronHit, CatchId = PelagVfxId.AnchorThrowIronCatch,
        StunId = PelagVfxId.AnchorThrowIronStun,
        Edge = Hex(PelagAnchorThrowIronLook.EdgeHex), Mid = Mid, Light = Light, Hot = Hot, HotShade = new Color(.62f, .82f, 1.15f, 1f),
        HaloDeep = Hex(0x2050D0), HaloLight = Hex(0x6FA8FF), BurstCore = new Color(1.0f, 1.12f, 1.5f, 1f),
        ChipEdge = Hex(PelagAnchorThrowIronLook.ChipEdgeHex), BitShade = new Color(.42f, .58f, .92f, 1f), BitCore = new Color(1.15f, 1.22f, 1.34f, 1f),
        Star = BurstCobalt, Echo = new Color(.42f, .70f, 1.15f, 1f), StunStar = new Color(.20f, .46f, .98f, 1f), CoreFrom = 1f
    };

    private struct ThrowIronMaterials
    {
        public Material Streak, Halo, Furrow, Burst, Flare, Spark, Star, Chip, Clod, Dust;
        public LungeMaterials Rocks;
    }

    /// <summary>Камни выпада Крушения (свои копии мешей «Tiny Rock» в Geometry) — только чтение.</summary>
    private static Mesh[] IronRockMeshes()
    {
        var list = new System.Collections.Generic.List<Mesh>();
        for (int i = 1; i <= 5; i++)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GeometryFolder + "/WreckLungeRock" + i + ".asset");
            if (mesh != null) list.Add(mesh);
        }
        return list.ToArray();
    }
}
