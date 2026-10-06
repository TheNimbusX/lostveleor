using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Абордаж v2 «морская пена» (02.10, владелец: «резкое быстрое»; формы — Обвал,
/// Гейзер, Пробоина). Целевые кадры — ART/characters/pelag/abordage-2026-10-02/
/// chatgpt-results: A-base, D-quake, F-geyser-seagreen, G-breach. Семья та же, что
/// серия сабли, рывок, пенный Вихрь и Шквал v2: плоская вода, белые комья пены по
/// гребням, тонкий тёмный обвод, вырезанные капли; без свечения и дыма. Вид —
/// Game.View/PelagVfxController.Abordage*.cs, цвета форм — PelagAbordageFormLook.
///
/// СОБРАНО НА ПРИНЯТОМ:
///  • вода (лента вдоль цепи и росчерк якоря, кольцо Обвала и падения Гейзера,
///    струя Пробоины, столб Гейзера) — шейдер воды форм Вихря Razlom/Whirlwind Form
///    Water (не меняется), свои материалы M_Abordage_Ribbon/_Ground/_Column,
///    текстуры CFXR «cfxr sword trail noise bubbles» и «cfxr perlin mid»; меш пишет вид;
///  • комья, капли, комья земли — материалы рывка и серии сабли (только чтение):
///    M_Dash_Foam, M_Dash_Drop, M_Sabre_Drop;
///  • под своими id (свои пулы), без правки самих префабов: всплеск на теле —
///    VFX_Pelag_Sabre_Splash, корона у ноги — VFX_Pelag_Dash_Splash, корона сбитого —
///    VFX_Pelag_Whirlwind_CrownSplash, веер падения воды — VFX_Pelag_Whirlwind_WaveSplash,
///    след тяги — VFX_Pelag_Dash_Wake, след волока — VFX_Pelag_Whirlwind_MaelstromDrag,
///    голова якоря и цепь — VFX_AnchorLeap_Throw / VFX_AnchorLeap_Chain (те же, что у
///    прежнего броска; модель якоря подставляет пул по id AbordageAnchor).
///
/// Круг 3 (03.10): своя вода Обвала M_Abordage_Quake и тонкое кольцо падения Гейзера M_Abordage_Fall
/// (тот же шейдер воды форм), комья Обвала M_Abordage_Clod — гранёные камни CFXR «debris unlit 3x3»
/// на шейдере пены семьи Razlom/Sabre Foam Blob (сплошные, тёмный обвод), не растворяемая клякса рывка.
///
/// Круг 4 (03.10): Обвал — свой шейдер всплеска Razlom/Abordage Quake Splash (M_Abordage_Quake): лопасти
/// кобальта, непрозрачная мокрая земля у героя, распад дырами от центра (общий шейдер воды форм не тронут).
/// Пена Гейзера — слитые массы: пара материалов на своём шейдере Razlom/Abordage Foam Puff — силуэт
/// M_Abordage_FoamBack (раньше в очереди) и заливка без обвода с тенью снизу M_Abordage_FoamFill (маска капли пака
/// «cfxr water drop blur anim»); розетка —
/// свой материал M_Abordage_Rosette (сплошной диск, светлее у ствола).
///
/// Свои префабы: VFX_Pelag_Abordage_Ribbon (лента воды + выбрасываемые капли и комья),
/// _Quake (кольцо, брызги гребня, комья земли и столб брызг кулака), _Breach (струя,
/// капли-лепестки, клочья), _Geyser (столб, кольцо падения, пена шапки, брызги).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или поднята
/// <see cref="Version"/> (userData .meta префаба ленты). Сохраняются только свои
/// ассеты (SaveAssetIfDirty), общего SaveAssets нет; библиотека пишется, только если
/// наши записи поменялись. Прежние ассеты броска (RazlomAnchorLeapVfx, RazlomLeapHovlVfx)
/// не трогаются: на них держится запасной путь вида и Крушение (VFX_AnchorLeap_Land).
/// Любая правка сборки ниже — поднять Version.
/// </summary>
public static partial class PelagAbordageFoamVfxSetup
{
    /// <summary>
    /// Версия сборки ассетов Абордажа v2. Поднимать при любой правке сборки.
    /// 2 (03.10, ревью Гейзера «плоский ящик»): столб с объёмом поперёк — тёмный край к светлому.
    /// 3 (03.10, круг 2): Гейзер — юбка «BaseRing», ярче вертикальные струи столба, больше капель
    /// под струи по стволу; Обвал — выбрасываемые комья «Clods» и брызги грязи «Dirt» (кадр D).
    /// 4 (03.10, круг 3): Обвал — своя вода M_Abordage_Quake (сплошная от ядра, без обвода и пены у
    /// внутреннего края, толстый белый внешний гребень), комья и полосы грязи — сплошной камень
    /// M_Abordage_Clod (CFXR «debris unlit 3x3» на шейдере пены семьи, без растворения, тёмный обвод),
    /// «Slam»/«SlamFoam» — кобальтовые капли наружу вместо белого веера над героем. Гейзер — столб
    /// зеленее и ярче (тонкая пена краёв, сильные светлые струи), «Crown»/«BaseFoam» — круглые клубы
    /// (капли пака) вместо рваных клякс, кольцо падения — тонкое M_Abordage_Fall.
    /// 5 (03.10, круг 4, ревью в игре): Обвал — свой шейдер всплеска (M_Abordage_Quake на Razlom/Abordage Quake
    /// Splash: лопасти, мокрая земля, распад дырами), «Foam» Обвала — круглые капли пака вместо клякс рывка,
    /// «Spray» — больше капель. Гейзер — пена слитыми массами: «Crown» (заливка M_Abordage_FoamFill) + «CrownBack»
    /// (силуэт M_Abordage_FoamBack), разовый «BaseFoam» убран (клубы кромки выбрасывает вид), розетка «BaseRing» —
    /// M_Abordage_Rosette.
    /// 6 (03.10, круг 5, проверка круга 4 в игре): M_Abordage_Quake — земля мокрой грязью пятнами по месту и белыми
    /// каплями (_Mud, _MudTones; полос вдоль луча нет), кромка дыр распада — жёсткий срез (_Hole.z = QuakeHoleCut).
    /// Префабы те же (пена падения Гейзера — тот же пар «Crown»/«CrownBack», выбрасывает вид).
    /// </summary>
    private const int Version = 6;
    private static readonly string Revision = "PelagAbordageFoamV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string PerlinTexture = CfxrGraphics + "cfxr perlin mid.png";
    /// <summary>Круг 3: гранёные камни пака (канал r — грани: обод 0,5, скос 0,79, верх 1) — комья Обвала.</summary>
    private const string DebrisTexture = CfxrGraphics + "cfxr debris unlit 3x3.png";
    /// <summary>Круг 4: мягкая капля пака (маска клубов пены Гейзера — та же, что у M_Dash_Drop).</summary>
    private const string DropTexture = CfxrGraphics + "cfxr water drop blur anim.png";
    /// <summary>Круг 4: свой шейдер всплеска Обвала (лопасти, мокрая земля, распад дырами).</summary>
    private const string QuakeShaderName = "Razlom/Abordage Quake Splash";
    /// <summary>Круг 4: свой шейдер клубов пены Гейзера (силуэт / заливка с тенью снизу — пена слитыми массами).</summary>
    private const string FoamPuffShaderName = "Razlom/Abordage Foam Puff";
    private const string WaterShaderName = "Razlom/Whirlwind Form Water";
    /// <summary>Шейдер пены и капель семьи (порог маски, тень у края, тёмный обвод) — для комьев.</summary>
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";

    // Чужое принятое — только чтение.
    private const string DashFoamMaterial = MaterialFolder + "/M_Dash_Foam.mat";
    private const string DashDropMaterial = MaterialFolder + "/M_Dash_Drop.mat";
    private const string SabreDropMaterial = MaterialFolder + "/M_Sabre_Drop.mat";
    private const string SabreSplashPrefab = PrefabFolder + "/VFX_Pelag_Sabre_Splash.prefab";
    private const string DashSplashPrefab = PrefabFolder + "/VFX_Pelag_Dash_Splash.prefab";
    private const string DashWakePrefab = PrefabFolder + "/VFX_Pelag_Dash_Wake.prefab";
    private const string CrownSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_CrownSplash.prefab";
    private const string WaveSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_WaveSplash.prefab";
    private const string MaelstromDragPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_MaelstromDrag.prefab";
    private const string AnchorThrowPrefab = PrefabFolder + "/VFX_AnchorLeap_Throw.prefab";
    private const string AnchorChainPrefab = PrefabFolder + "/VFX_AnchorLeap_Chain.prefab";

    public const string RibbonName = "VFX_Pelag_Abordage_Ribbon";
    public const string QuakeName = "VFX_Pelag_Abordage_Quake";
    public const string BreachName = "VFX_Pelag_Abordage_Breach";
    public const string GeyserName = "VFX_Pelag_Abordage_Geyser";
    private const string RibbonMaterialName = "M_Abordage_Ribbon";
    private const string GroundMaterialName = "M_Abordage_Ground";
    private const string ColumnMaterialName = "M_Abordage_Column";
    private const string QuakeMaterialName = "M_Abordage_Quake";
    private const string FallMaterialName = "M_Abordage_Fall";
    private const string ClodMaterialName = "M_Abordage_Clod";
    private const string RosetteMaterialName = "M_Abordage_Rosette";
    private const string FoamFillMaterialName = "M_Abordage_FoamFill";
    private const string FoamBackMaterialName = "M_Abordage_FoamBack";

    private static readonly string[] OwnPrefabs = { RibbonName, QuakeName, BreachName, GeyserName };
    private static readonly string[] OwnMaterials =
        { RibbonMaterialName, GroundMaterialName, ColumnMaterialName, QuakeMaterialName, FallMaterialName, ClodMaterialName,
          RosetteMaterialName, FoamFillMaterialName, FoamBackMaterialName };

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // База Абордажа = бирюза базового Вихря и Шквала (палитра серии сабли, вариант Б). Формы — PelagAbordageFormLook.
    private static readonly Color Deep = new Color(.03f, .24f, .32f);
    private static readonly Color Water = new Color(.06f, .60f, .68f);
    private static readonly Color Shallow = new Color(.42f, .90f, .92f);
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .86f, .91f);
    private static readonly Color Outline = new Color(.02f, .08f, .11f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color FoamWhite = new Color(.96f, 1.08f, 1.10f, 1f);
    private static readonly Color DropAqua = new Color(.50f, .92f, .95f, 1f);
    /// <summary>
    /// Комья земли Обвала (кадр D). Круг 3: темнее — бурая глина .30/.46 сливалась с охрой пола;
    /// мокрая тёмная земля, обвод — тот же тёмный семьи.
    /// </summary>
    private static readonly Color ClodDark = new Color(.17f, .10f, .05f, 1f);
    private static readonly Color ClodLight = new Color(.30f, .19f, .10f, 1f);
    /// <summary>Капли воды Обвала в префабе (кобальт кадра D; пак-слои префаба не красятся видом).</summary>
    private static readonly Color QuakeDropLight = new Color(.82f, .90f, 1.05f, 1f);
    private static readonly Color QuakeDropBlue = new Color(.50f, .66f, 1f, 1f);

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Абордаж v2: подключить «морскую пену»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Абордаж v2: пересобрать «морскую пену»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaterShaderName) == null || Shader.Find(BlobShaderName) == null || Shader.Find(QuakeShaderName) == null
            || Shader.Find(FoamPuffShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DebrisTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashDropMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial) == null)
        {
            Debug.LogWarning("[abordage-setup] Нет шейдера воды форм, текстур CFXR или материалов рывка/сабли — Абордаж v2 не собран.");
            return;
        }
        if (force || !UpToDate()) Build();

        // Bind ставит dirty, только если запись поменялась; библиотека пишется только тогда.
        bool ok = true;
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageRibbon, PrefabPath(RibbonName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageQuake, PrefabPath(QuakeName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageBreach, PrefabPath(BreachName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageGeyser, PrefabPath(GeyserName), 3);
        // Принятые префабы семьи — под своими id (свои пулы), без правки самих префабов.
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageSplash, SabreSplashPrefab, 6);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageCrown, DashSplashPrefab, 3);
        // Круг 2: Гейзер ставит корону и у шапки, и у основания — запас пула 12.
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageKnock, CrownSplashPrefab, 12);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageBurst, WaveSplashPrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageWake, DashWakePrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageDrag, MaelstromDragPrefab, 6);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageAnchor, AnchorThrowPrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AbordageChain, AnchorChainPrefab, 2);
        if (!ok) Debug.LogWarning("[abordage-setup] Не все префабы Абордажа v2 нашлись — недостающие записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(RibbonName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in OwnPrefabs)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        foreach (string name in OwnMaterials)
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name)) == null) return false;
        return true;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        Material ribbon = RibbonMaterial();
        Material ground = GroundMaterial();
        Material column = ColumnMaterial();
        Material quake = QuakeMaterial();
        Material fall = FallMaterial();
        Material clod = ClodMaterial();
        Material rosette = RosetteMaterial();
        Material foamFill = FoamPuffMaterial(FoamFillMaterialName, false);
        Material foamBack = FoamPuffMaterial(FoamBackMaterialName, true);
        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам.
        foreach (Material m in new[] { ribbon, ground, column, quake, fall, clod, rosette, foamFill, foamBack }) AssetDatabase.SaveAssetIfDirty(m);

        Material foam = AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial);
        Material crownDrop = AssetDatabase.LoadAssetAtPath<Material>(DashDropMaterial);
        Material drop = AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial);
        SaveRibbonPrefab(ribbon, foam, drop);
        SaveQuakePrefab(quake, crownDrop, clod);
        SaveBreachPrefab(ground, foam, crownDrop);
        SaveGeyserPrefab(column, rosette, fall, crownDrop, foamFill, foamBack);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем.
        foreach (string name in OwnMaterials)
            AssetDatabase.ImportAsset(MaterialPath(name), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(RibbonName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[abordage-setup] Абордаж v2 «морская пена» собран: вода цепи и росчерк якоря, всплеск Обвала, струя Пробоины, столб Гейзера, ревизия " + Revision + ".");
    }
}
