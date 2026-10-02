using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ФОРМЫ ВИХРЯ (владелец 02.10) поверх принятой «морской пены». Целевые кадры —
/// выбор владельца, ART/characters/pelag/whirlwind-forms-2026-10-02/chatgpt-results:
///  • Буря — storm-2: водяной столб из витков-полос, ломающийся гребень сверху,
///    брызги с кромки; у земли витки прозрачнее, чтобы враги внутри читались;
///  • Водоворот — vortex-1: шесть плоских рукавов-спиралей на земле, пена по
///    выпуклой кромке, струи пены к центру;
///  • Пенные волны — waves-2: чистое кольцо воды с белыми струями и пенным
///    гребнем на бегущей кромке, тонкий тёмный обвод; на ударе — корона брызг.
/// Язык тот же: плоская бирюзовая вода, белые комья пены, тонкий тёмный обвод,
/// вырезанные капли, без свечения и дыма. Шейдеры — серии сабли (не меняются),
/// капли, комья и клякса — материалы пенного Вихря (только чтение); свои —
/// материалы M_Whirlwind_Storm*/Maelstrom*/FoamWave* и меши Geometry/Whirlwind*Form*.
///
/// МИГРАЦИЯ ПО ВЕРСИИ: своя <see cref="FormsVersion"/> (userData .meta префаба
/// столба Бури) — пересборка форм не трогает принятый пенный Вихрь
/// (<see cref="Version"/>). Сохраняются только свои ассеты, общего SaveAssets нет.
/// Любая правка ниже — поднять FormsVersion.
/// </summary>
public static partial class PelagWhirlwindFoamVfxSetup
{
    /// <summary>Версия сборки форм. Поднимать при любой правке сборки форм.</summary>
    private const int FormsVersion = 2;
    private static readonly string FormsRevision = "PelagWhirlwindFormsV" + FormsVersion;
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";

    public const string StormColumnName = "VFX_Pelag_Whirlwind_Storm_Column";
    public const string StormSplashName = "VFX_Pelag_Whirlwind_Storm_Splash";
    public const string MaelstromName = "VFX_Pelag_Whirlwind_Maelstrom";
    public const string FoamWaveName = "VFX_Pelag_Whirlwind_FoamWave";
    public const string CrownSplashName = "VFX_Pelag_Whirlwind_CrownSplash";
    private static string FormPrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static readonly string[] FormPrefabNames = { StormColumnName, StormSplashName, MaelstromName, FoamWaveName, CrownSplashName };
    private static readonly string[] FormMaterialNames =
        { "M_Whirlwind_StormBand", "M_Whirlwind_StormCrest", "M_Whirlwind_StormRing", "M_Whirlwind_MaelstromArm", "M_Whirlwind_FoamWaveRing" };

    // Жизни витков, с: виток рождается, обегает героя и рассыпается — столб из
    // накладывающихся витков держится, пока их рождает удержание.
    private const float StormBandLife = .46f, StormCrestLife = .50f, StormRingLife = .42f, MaelstromLife = .80f;
    /// <summary>Петля эмиттеров столба, с: длиннее удержания (3 с), чтобы стартовая вспышка не повторялась.</summary>
    private const float StormLoopSeconds = 4f;

    [InitializeOnLoadMethod]
    private static void ScheduleForms() => EditorApplication.delayCall += InstallFormsOnLoad;

    private static void InstallFormsOnLoad()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        InstallForms(false);
    }

    [MenuItem("Разлом/Pelag VFX/Вихрь: пересобрать формы (Буря, Водоворот, Пенные волны)")]
    public static void RebuildFormsMenu() => InstallForms(true);

    /// <summary>Собрать формы, если их нет или поднята версия, и подключить в библиотеку Пелага.</summary>
    public static void InstallForms(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (!EnsureForms(force)) return;
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindStormColumn, FormPrefabPath(StormColumnName), 1);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindStormSplash, FormPrefabPath(StormSplashName), 1);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindMaelstrom, FormPrefabPath(MaelstromName), 1);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindFoamWave, FormPrefabPath(FoamWaveName), 3);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindCrownSplash, FormPrefabPath(CrownSplashName), 8);
        // Пишется, только если записи действительно поменялись (Bind ставит dirty лишь при смене).
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Свои ассеты форм на месте и собраны этой версией; иначе собрать. false — собрать нечем.</summary>
    public static bool EnsureForms(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        if (Shader.Find(WaveShaderName) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null) return false;
        // Капли, комья и клякса — материалы пенного Вихря: нет их — сначала он.
        if ((AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamDrop")) == null
             || AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamBit")) == null
             || AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamSplat")) == null)
            && !Ensure(false)) return false;
        if (!force && FormsUpToDate()) return true;
        return BuildForms();
    }

    private static bool FormsUpToDate()
    {
        var importer = AssetImporter.GetAtPath(FormPrefabPath(StormColumnName));
        if (importer == null || importer.userData != FormsRevision) return false;
        foreach (string name in FormPrefabNames)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FormPrefabPath(name)) == null) return false;
        foreach (string name in FormMaterialNames)
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name)) == null) return false;
        return true;
    }

    // Сталь / фиолет / морская зелень — те же цвета, что у иконок Icon_Whirlwind_Storm/_Maelstrom/_FoamWaves.
    private static readonly Color StormDeep = new Color(.16f, .19f, .23f), StormWater = new Color(.42f, .48f, .55f), StormShallow = new Color(.70f, .75f, .80f);
    private static readonly Color MaelstromDeep = new Color(.12f, .04f, .30f), MaelstromWater = new Color(.36f, .16f, .70f), MaelstromShallow = new Color(.66f, .48f, .95f);
    private static readonly Color WavesDeep = new Color(.02f, .30f, .20f), WavesWater = new Color(.10f, .70f, .48f), WavesShallow = new Color(.50f, .95f, .76f);

    private static void TintForm(Material material, Color deep, Color water, Color shallow)
    {
        material.SetColor("_Deep", deep);
        material.SetColor("_Water", water);
        material.SetColor("_Shallow", shallow);
    }

    private static bool BuildForms()
    {
        Mesh thick = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh && mesh.name == "cfxr mesh sword_trail 360 thick") thick = mesh;
        if (thick == null) { Debug.LogWarning("[whirlwind-forms] Нет меша пака «sword_trail 360 thick»."); return false; }
        Shader waveShader = Shader.Find(WaveShaderName);
        var bubbles = AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture);
        Material dropMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamDrop"));
        Material bitMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamBit"));
        Material splatMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_Whirlwind_FoamSplat"));

        // ---- меши
        Mesh band = StormBandMesh("WhirlwindFormStormBand", 280f, .12f, .30f, .05f);
        Mesh crest = BandMesh("WhirlwindFormStormCrest", thick, 300f, .30f);
        Mesh arms = MaelstromArmsMesh("WhirlwindFormMaelstromArms", 6, 115f, .16f, .22f);
        Mesh ring = MirroredRingMesh("WhirlwindFormFoamRing", .17f, .40f);

        // ---- материалы (шейдер серии сабли, свои числа)
        // Виток: голова дорисовывается за .08 с, хвост рассыпается пеной вдоль u.
        // Пена — верхняя кромка витка (v = 1 сверху): у каждого витка белый гребень, как на storm-2.
        Material bandMat = WaveMaterial(waveShader, "M_Whirlwind_StormBand", bubbles, StormBandLife,
            .35f, .08f, .22f, .44f, .36f, StormBandLife, 16f, new Vector4(9f, 1.1f, 3f, .45f));
        bandMat.SetVector("_Bands", new Vector4(.10f, .34f, .60f, .34f));
        bandMat.SetFloat("_EdgeRag", .26f);
        bandMat.SetFloat("_HeadFoam", .40f);
        bandMat.SetFloat("_ErodeAlong", .9f);
        bandMat.SetFloat("_Streaks", .9f);
        bandMat.SetFloat("_Glow", .15f);
        bandMat.SetFloat("_FlowSpeed", 1.2f);
        bandMat.SetFloat("_CameraPush", 0f);
        // Ломающийся гребень: плоское кольцо с гребнем у головы поверх столба, пены больше.
        Material crestMat = WaveMaterial(waveShader, "M_Whirlwind_StormCrest", bubbles, StormCrestLife,
            .30f, .10f, .26f, .48f, .40f, StormCrestLife, 16f, new Vector4(13f, 1.0f, 5f, .5f));
        crestMat.SetVector("_Bands", new Vector4(.12f, .30f, .46f, .40f));
        crestMat.SetFloat("_EdgeRag", .26f);
        crestMat.SetFloat("_HeadFoam", .50f);
        crestMat.SetFloat("_ErodeAlong", .9f);
        crestMat.SetFloat("_CameraPush", .3f);
        // Кольцо всплеска конца Бури: замкнутое (u зеркальный, без шва), рассыпается к .38 с.
        Material ringMat = WaveMaterial(waveShader, "M_Whirlwind_StormRing", bubbles, StormRingLife,
            1.02f, .01f, .14f, .38f, .30f, StormRingLife, 24f, new Vector4(24f, 1.2f, 8f, .5f));
        ringMat.SetVector("_Bands", new Vector4(.08f, .30f, .55f, .34f));
        ringMat.SetFloat("_HeadFoam", 0f);
        ringMat.SetFloat("_Streaks", .8f);
        ringMat.SetFloat("_ErodeAlong", .4f);
        ringMat.SetFloat("_EdgeRag", .22f);
        ringMat.SetFloat("_CameraPush", 0f);
        // Рукав Водоворота: голова (u = 1) — у центра, дорастает внутрь за .14 с; струи
        // бегут по u к центру; рассыпается от внешнего хвоста.
        Material armMat = WaveMaterial(waveShader, "M_Whirlwind_MaelstromArm", bubbles, MaelstromLife,
            .30f, .14f, .40f, .74f, .64f, MaelstromLife, 7f, new Vector4(6f, 1.1f, 2.2f, .5f));
        armMat.SetVector("_Bands", new Vector4(.14f, .38f, .62f, .32f));
        armMat.SetFloat("_EdgeRag", .24f);
        armMat.SetFloat("_HeadFoam", .30f);
        armMat.SetFloat("_ErodeAlong", .8f);
        armMat.SetFloat("_Streaks", 1f);
        armMat.SetFloat("_FlowSpeed", 1.6f);
        armMat.SetFloat("_Glow", .15f);
        armMat.SetFloat("_CameraPush", 0f);
        // Кольцо Пенных волн: время в долях жизни (жизнь ставит вид по ходу кольца из Sim):
        // ход — кольцо целое, после — рассыпается. Гребень пены — бегущая внешняя кромка.
        Material waveMat = WaveMaterial(waveShader, "M_Whirlwind_FoamWaveRing", bubbles, 1f,
            1.02f, .01f, .60f, .97f, .86f, 1f, 24f, new Vector4(24f, 1.2f, 8f, .5f));
        waveMat.SetVector("_Bands", new Vector4(.06f, .26f, .60f, .32f));
        waveMat.SetFloat("_HeadFoam", 0f);
        waveMat.SetFloat("_Streaks", 1f);
        waveMat.SetFloat("_ErodeAlong", .5f);
        waveMat.SetFloat("_EdgeRag", .22f);
        waveMat.SetFloat("_FlowSpeed", .25f);
        waveMat.SetFloat("_Glow", .2f);
        waveMat.SetFloat("_CameraPush", 0f);

        // Цвета форм (владелец 02.10: «бурю более стальную, водоворот более фиолет» — как иконки форм).
        // Пена и обвод общие; база Вихря остаётся бирюзовой.
        foreach (Material m in new[] { bandMat, crestMat, ringMat }) TintForm(m, StormDeep, StormWater, StormShallow);
        TintForm(armMat, MaelstromDeep, MaelstromWater, MaelstromShallow);
        TintForm(waveMat, WavesDeep, WavesWater, WavesShallow);
        var materials = new[] { bandMat, crestMat, ringMat, armMat, waveMat };
        foreach (Material material in materials) { EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); }
        foreach (Mesh mesh in new[] { band, crest, arms, ring }) AssetDatabase.SaveAssetIfDirty(mesh);

        SaveStormColumn(band, crest, bandMat, crestMat, dropMat, bitMat);
        SaveStormSplash(ring, ringMat, dropMat, bitMat);
        SaveMaelstrom(arms, armMat, dropMat, bitMat);
        SaveFoamWave(ring, waveMat, dropMat, bitMat);
        SaveCrownSplash(splatMat, dropMat, bitMat);

        foreach (Material material in materials)
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(FormPrefabPath(StormColumnName));
        if (importer != null && importer.userData != FormsRevision)
        {
            importer.userData = FormsRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[whirlwind-forms] Формы Вихря собраны: столб и всплеск Бури, рукава Водоворота, кольцо Пенных волн, корона брызг, ревизия " + FormsRevision + ".");
        return true;
    }
}
