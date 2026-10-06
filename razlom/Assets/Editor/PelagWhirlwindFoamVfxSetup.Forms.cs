using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ФОРМЫ ВИХРЯ (владелец 02.10) поверх принятой «морской пены». Целевые кадры —
/// выбор владельца, ART/characters/pelag/whirlwind-forms-2026-10-02/chatgpt-results:
///  • Буря — storm-2: водяной столб из витков-полос, ломающийся гребень сверху,
///    брызги с кромки; у земли витки прозрачнее, чтобы враги внутри читались;
///  • Водоворот — vortex-1: шесть рукавов-спиралей на земле, пена по выпуклой
///    кромке, струи к центру. v3 (02.10, вечер): рукава — живая вода на
///    радиусе удара (меш пишет вид, PelagMaelstromWater), тяга 4 м — только
///    струи; за притянутыми — пенный след (копия следа рывка, свой материал);
///  • Пенные волны — waves-2: кольцо воды с пенными гребнями, тонкий тёмный
///    обвод; на ударе — корона брызг. v3: кольцо — живая вода (PelagFoamRingWater):
///    волнистый гребень, выход с замедлением, без остановки, распад на капли.
/// v4 (проверка по выбранным кадрам, 02.10): волны — полоса около метра с градиентом
///    глубины и крупными комьями пены по обоим краям, на ударе — большой веер брызг
///    (VFX_Pelag_Whirlwind_WaveSplash); рукава — фиолетовая вода (градиент, струи,
///    рваный гребень, капли с концов «TipDrops»), струи тяги — изогнутые струи воды
///    («Strands», материал M_Whirlwind_MaelstromStrand), след тяги — узкая пенная
///    борозда. Вода ложится поверх низких препятствий, но не поверх тел (_Over шейдера).
/// Язык тот же: плоская бирюзовая вода, белые комья пены, тонкий тёмный обвод,
/// вырезанные капли, без свечения и дыма. Шейдеры — серии сабли (не меняются)
/// и свой Razlom/Whirlwind Form Water (вода v3 в языке следа рывка),
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
    // v6 (02.10): вторая проба v4 — редактор успел собрать v5 до того, как его заняли; съёмка и
    // следующий запуск редактора пересобирают формы по этой версии.
    private const int FormsVersion = 6;
    private static readonly string FormsRevision = "PelagWhirlwindFormsV" + FormsVersion;
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";

    public const string StormColumnName = "VFX_Pelag_Whirlwind_Storm_Column";
    public const string StormSplashName = "VFX_Pelag_Whirlwind_Storm_Splash";
    public const string MaelstromName = "VFX_Pelag_Whirlwind_Maelstrom";
    public const string FoamWaveName = "VFX_Pelag_Whirlwind_FoamWave";
    public const string CrownSplashName = "VFX_Pelag_Whirlwind_CrownSplash";
    public const string MaelstromDragName = "VFX_Pelag_Whirlwind_MaelstromDrag";
    public const string WaveSplashName = "VFX_Pelag_Whirlwind_WaveSplash";
    /// <summary>Вода v3: кольца волн и рукава Водоворота (меш пишет вид каждый кадр).</summary>
    private const string FormWaterShaderName = "Razlom/Whirlwind Form Water";
    private const string FormsPerlinTexture = CfxrGraphics + "cfxr perlin mid.png";
    /// <summary>След тяги — копия следа рывка (его префаб и материал только читаются).</summary>
    private const string DashWakePrefab = PrefabFolder + "/VFX_Pelag_Dash_Wake.prefab";
    private const string DashWakeMaterial = MaterialFolder + "/M_Dash_Wake.mat";
    private static string FormPrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static readonly string[] FormPrefabNames =
        { StormColumnName, StormSplashName, MaelstromName, FoamWaveName, CrownSplashName, MaelstromDragName, WaveSplashName };
    private static readonly string[] FormMaterialNames =
        { "M_Whirlwind_StormBand", "M_Whirlwind_StormCrest", "M_Whirlwind_StormRing", "M_Whirlwind_MaelstromArm", "M_Whirlwind_FoamWaveRing",
          "M_Whirlwind_MaelstromDrag", "M_Whirlwind_MaelstromStrand" };

    // Жизни витков, с: виток рождается, обегает героя и рассыпается — столб из
    // накладывающихся витков держится, пока их рождает удержание.
    private const float StormBandLife = .46f, StormCrestLife = .50f, StormRingLife = .42f;
    /// <summary>Объект Водоворота живёт, пока рукава не рассыпались (их время ведёт вид), с.</summary>
    private const float MaelstromLife = .95f;
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
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindMaelstromDrag, FormPrefabPath(MaelstromDragName), 6);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WhirlwindWaveSplash, FormPrefabPath(WaveSplashName), 10);
        // Пишется, только если записи действительно поменялись (Bind ставит dirty лишь при смене).
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Свои ассеты форм на месте и собраны этой версией; иначе собрать. false — собрать нечем.</summary>
    public static bool EnsureForms(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        if (Shader.Find(WaveShaderName) == null || Shader.Find(FormWaterShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(FormsPerlinTexture) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(DashWakePrefab) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashWakeMaterial) == null) return false;
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
    /// <summary>Пена Водоворота (v4): светлая сиренево-кремовая, а не бирюзово-белая пена базы.</summary>
    private static readonly Color MaelstromFoam = new Color(1.12f, 1.06f, 1.14f), MaelstromFoamShade = new Color(.78f, .70f, .96f);

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
        // Кольцо всплеска Бури (кольцо Пенных волн v3 — живой меш вида, ассета нет).
        Mesh ring = MirroredRingMesh("WhirlwindFormFoamRing", .17f, .40f);
        // Рукава Водоворота v2 были мешем-ассетом; v3 пишет вид — старый ассет больше никому не нужен.
        AssetDatabase.DeleteAsset(GeometryFolder + "/WhirlwindFormMaelstromArms.asset");

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
        // Вода v3 (02.10, вечер): рукава Водоворота и кольцо Пенных волн — живой меш вида на
        // шейдере Razlom/Whirlwind Form Water (язык следа рывка: валики пены по краям, распад
        // на капли без тёмного кружева). Время — секунды вида; когда рвётся вода — _Break.x.
        var perlin = AssetDatabase.LoadAssetAtPath<Texture2D>(FormsPerlinTexture);
        Shader formWater = Shader.Find(FormWaterShaderName);
        // Рукав: контакт через 0,33 с после тяги; хвост старше головы на 0,10 с (вид), значит хвост
        // белеет пеной с ~0,38 с и рвётся к ~0,52 с, голова — к ~0,62 с; последние капли — к 0,82 с.
        // v4 (vortex-1, «плоская фиолетовая краска с белой каймой»): фиолетовая ВОДА — от глубокой у
        // вогнутой стороны (там рукав прозрачнее и без туши, уходит в воронку) к светлой у выпуклой,
        // светлые струи внутри, по выпуклой кромке — рваный гребень крупных комьев сиреневой пены.
        Material armMat = FormWaterMaterial(formWater, "M_Whirlwind_MaelstromArm", bubbles, perlin,
            new Vector4(.62f, .06f, 0f, .14f), .74f, .82f);
        // Проба v4a: гребень 0,14 на ячейках 0,55 читался тонкой кремовой каймой — комья крупнее и толще.
        // Внутренняя сторона прозрачна слегка (0,8): полупрозрачный фиолет на траве — та же серая муть.
        armMat.SetVector("_Crest", new Vector4(.22f, .16f, .035f, .12f));
        armMat.SetVector("_Bands", new Vector4(.15f, .75f, .55f, .40f));
        armMat.SetVector("_Across", new Vector4(.95f, .80f, .25f, .75f));
        armMat.SetFloat("_ClumpScale", .40f);
        armMat.SetFloat("_EndRag", .30f);
        armMat.SetVector("_Lines", new Vector4(4f, 1.5f, .45f, .95f));
        armMat.SetVector("_DarkLines", new Vector4(3f, 2.2f, .35f, .45f));
        armMat.SetColor("_Foam", MaelstromFoam);
        armMat.SetColor("_FoamShade", MaelstromFoamShade);
        // Кольцо: вид ведёт возраст так, что вода рвётся (0,30) вскоре после хода кольца и белеет
        // пеной только перед самым разрывом. v4 (waves-2): полоса около метра — градиент от глубокой
        // воды у внутреннего края к светлой у внешнего, по ОБОИМ краям крупные круглые комья пены
        // (ячейки крупнее вдвое), рвётся разом по кругу (_Break.y меньше) на крупные капли.
        Material waveMat = FormWaterMaterial(formWater, "M_Whirlwind_FoamWaveRing", bubbles, perlin,
            new Vector4(PelagFoamRingWater.ShaderBreakAge, .04f, 0f, .06f), .40f, .46f);
        waveMat.SetVector("_Crest", new Vector4(.17f, .13f, .035f, .70f));
        waveMat.SetVector("_Bands", new Vector4(.15f, .75f, .30f, .30f));
        waveMat.SetVector("_Across", new Vector4(.85f, 1f, 1f, 0f));
        waveMat.SetFloat("_ClumpScale", .48f);
        waveMat.SetFloat("_CoarseScale", .45f);
        waveMat.SetFloat("_BreakScale", 1.3f);
        waveMat.SetFloat("_DropLife", .08f);
        waveMat.SetVector("_Lines", new Vector4(5f, 1.6f, .32f, .85f));
        waveMat.SetVector("_DarkLines", new Vector4(3f, 2.2f, .30f, .30f));
        // Струи тяги (v4): узкие изогнутые струи фиолетовой воды, голова в пене, не рвутся — стягиваются.
        Material strandMat = FormWaterMaterial(formWater, "M_Whirlwind_MaelstromStrand", bubbles, perlin,
            new Vector4(10f, 0f, 0f, .10f), 9f, 10f);
        // Проба v4a: тёмно-фиолетовые струи читались тонкими «лезвиями» — светлая сиреневая вода, пенная голова.
        // Проба v4b: тонкая струя с тушью по обоим краям читалась тёмной линией — обвод фиолетовый и тоньше, пены больше.
        strandMat.SetVector("_Crest", new Vector4(.042f, .02f, .01f, 1f));
        strandMat.SetColor("_Outline", new Color(.22f, .10f, .42f, .85f));
        strandMat.SetVector("_Bands", new Vector4(0f, .35f, 0f, 0f));
        strandMat.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        strandMat.SetFloat("_ClumpScale", 1.2f);
        strandMat.SetFloat("_EndRag", .04f);
        strandMat.SetFloat("_OutlinePx", 1.0f);
        strandMat.SetFloat("_Glow", .25f);
        strandMat.SetVector("_Lines", new Vector4(2f, 1.2f, .5f, 0f));
        strandMat.SetVector("_DarkLines", new Vector4(2f, 1.2f, .5f, 0f));
        strandMat.SetColor("_Foam", MaelstromFoam);
        strandMat.SetColor("_FoamShade", MaelstromFoamShade);
        // След тяги: копия материала следа рывка, цвета Водоворота. v4 («фиолетовые плиты»): пенная
        // борозда — толстые комковатые валики пены по обоим краям, светлая вода между ними, у ног каша
        // пены; саму полосу префаб сужает до 0,55 (SaveMaelstromDrag), код следа рывка не меняется.
        Material dragMat = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath("M_Whirlwind_MaelstromDrag"),
            Shader.Find("Razlom/Dash Foam Wake"));
        EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<Material>(DashWakeMaterial), dragMat);
        dragMat.name = "M_Whirlwind_MaelstromDrag";
        dragMat.SetVector("_Crest", new Vector4(.11f, .22f, .11f, .03f));
        dragMat.SetFloat("_HeadFoam", .95f);
        dragMat.SetVector("_Bands", new Vector4(.05f, .55f, 0f, 0f));
        dragMat.SetVector("_Lines", new Vector4(4f, 1.6f, .40f, .95f));
        dragMat.SetColor("_Foam", MaelstromFoam);
        dragMat.SetColor("_FoamShade", MaelstromFoamShade);

        // Цвета форм (владелец 02.10: «бурю более стальную, водоворот более фиолет» — как иконки форм).
        // Пена и обвод общие (у Водоворота v4 пена сиренево-кремовая); база Вихря остаётся бирюзовой.
        foreach (Material m in new[] { bandMat, crestMat, ringMat }) TintForm(m, StormDeep, StormWater, StormShallow);
        TintForm(armMat, MaelstromDeep, MaelstromWater, MaelstromShallow);
        TintForm(strandMat, MaelstromDeep, MaelstromWater, MaelstromShallow);
        TintForm(dragMat, MaelstromDeep, MaelstromWater, MaelstromShallow);
        TintForm(waveMat, WavesDeep, WavesWater, WavesShallow);
        var materials = new[] { bandMat, crestMat, ringMat, armMat, waveMat, dragMat, strandMat };
        foreach (Material material in materials) { EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); }
        foreach (Mesh mesh in new[] { band, crest, ring }) AssetDatabase.SaveAssetIfDirty(mesh);

        SaveStormColumn(band, crest, bandMat, crestMat, dropMat, bitMat);
        SaveStormSplash(ring, ringMat, dropMat, bitMat);
        SaveMaelstrom(armMat, strandMat, dropMat, bitMat);
        SaveFoamWave(waveMat, dropMat, bitMat);
        SaveCrownSplash(splatMat, dropMat, bitMat);
        SaveMaelstromDrag(dragMat);
        SaveWaveSplash(splatMat, dropMat, bitMat);

        foreach (Material material in materials)
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(FormPrefabPath(StormColumnName));
        if (importer != null && importer.userData != FormsRevision)
        {
            importer.userData = FormsRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[whirlwind-forms] Формы Вихря собраны: столб и всплеск Бури, рукава и следы тяги Водоворота, кольцо Пенных волн, корона брызг, ревизия " + FormsRevision + ".");
        return true;
    }
    /// <summary>Материал воды v3: язык следа рывка (те же пена, обвод, капли); цвета базы тонирует форма.</summary>
    private static Material FormWaterMaterial(Shader shader, string name, Texture2D bubbles, Texture2D perlin,
        Vector4 breakUp, float fadeFrom, float fadeTo)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_FoamTex", bubbles);
        material.SetTexture("_ErodeTex", perlin);
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetFloat("_CoarseScale", .5f);
        material.SetFloat("_ClumpScale", .85f);
        material.SetFloat("_EndRag", .28f);
        material.SetFloat("_OutlinePx", 2f);
        material.SetFloat("_Glow", .2f);
        material.SetVector("_Break", breakUp);
        material.SetFloat("_BreakScale", 2.2f);
        material.SetFloat("_DropLife", .07f);
        material.SetFloat("_EdgeEarly", .03f);
        material.SetFloat("_BreakRimPx", 1.5f);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeTo);
        // v4: вода ложится поверх всего, что не выше метра над ней (колодец, корни, кочки, трава),
        // поверх тел — никогда (трафарет Razlom/Texture Toon); у ног героя — прежний допуск 0,3 м.
        material.SetVector("_Over", new Vector4(1f, .25f, .45f, .30f));
        material.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        return material;
    }
}
