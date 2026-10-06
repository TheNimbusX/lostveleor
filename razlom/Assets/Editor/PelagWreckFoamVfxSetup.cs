using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение v2 «морская пена» (03.10; спека artifacts/wreck/plan/SPEC.md 5). Целевые кадры —
/// ART/characters/pelag/wreck-2026-10-03/chatgpt-results: A-base, B-breakwater-seagreen,
/// C-ninth-wave, D-shell («как ульты» — в игре размер по числам Sim). Семья та же, что серия
/// сабли, рывок, пенный Вихрь, Шквал v2 и Абордаж v2: вода, белые комья пены по гребням,
/// тонкий тёмный обвод, вырезанные капли; без свечения и дыма; без красного, оранжевого,
/// золота. Вид — Game.View/PelagVfxController.Wreck*.cs, цвета форм — PelagWreckFormLook
/// (база — бирюза #2EC4C9: свои материалы собраны в базе этой таблицы).
///
/// СОБРАНО НА ПРИНЯТОМ:
///  • вода (дуга за головой якоря и вихрь Девятого вала, стоячий гребень вала/стены/горба,
///    мокрый след, кольцо круга удара и обрушения, ленты Панциря) — шейдер воды форм Вихря
///    Razlom/Whirlwind Form Water (не меняется), свои материалы M_Wreck_*, текстуры CFXR
///    «cfxr sword trail noise bubbles» и «cfxr perlin mid»; меш пишет вид;
///  • капли, клочья пены, комья земли — материалы рывка, серии сабли и Обвала (только чтение):
///    M_Dash_Foam, M_Dash_Drop, M_Sabre_Drop, M_Abordage_Clod;
///  • под своими id (свои пулы), без правки самих префабов: серп маха — VFX_Pelag_Sabre_Wave
///    (полумесяц пака CFXR «sword_trail 180 thick», спека 5 «Мах 1–2»), всплеск на теле —
///    VFX_Pelag_Sabre_Splash, корона у точки удара и подхваченных — VFX_Pelag_Dash_Splash,
///    корона сбитого — VFX_Pelag_Whirlwind_CrownSplash, веер обрушения и четвёртого удара —
///    VFX_Pelag_Whirlwind_WaveSplash.
///
/// Свои префабы: VFX_Pelag_Wreck_Arc (тонкая лента следа + капли и клочья), _Crest (гребень +
/// след + брызги губы), _Slam (кольцо круга, комья, разовая корона брызг «Splash»), _Shell
/// (ленты Панциря, капли, пласты, мокрые пятна), _Crack (короткая тёмная трещина удара оземь —
/// копия линии раскола Рассекающего VFX_Pelag_Cleave_Crack со своим тёмным материалом
/// M_Wreck_Crack, без кольца и белых осколков; кадр A).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или поднята <see cref="Version"/>
/// (userData .meta префаба дуги). Сохраняются только свои ассеты (SaveAssetIfDirty), общего
/// SaveAssets нет (razlom-animator-builder-saveassets); библиотека пишется, только если наши
/// записи поменялись. Прежние ассеты (VFX_AnchorLeap_Land, VFX_AnchorSlam_Contact) не трогаются:
/// на них держатся запасной путь вида и старый Удар якорем. Любая правка сборки — поднять Version.
/// </summary>
public static partial class PelagWreckFoamVfxSetup
{
    /// <summary>Версия сборки ассетов Крушения v2. Поднимать при любой правке сборки.</summary>
    private const int Version = 1;
    private static readonly string Revision = "PelagWreckFoamV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string PerlinTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string WaterShaderName = "Razlom/Whirlwind Form Water";

    // Чужое принятое — только чтение.
    private const string DashFoamMaterial = MaterialFolder + "/M_Dash_Foam.mat";
    private const string DashDropMaterial = MaterialFolder + "/M_Dash_Drop.mat";
    private const string SabreDropMaterial = MaterialFolder + "/M_Sabre_Drop.mat";
    private const string ClodMaterial = MaterialFolder + "/M_Abordage_Clod.mat";
    private const string SabreSplashPrefab = PrefabFolder + "/VFX_Pelag_Sabre_Splash.prefab";
    private const string DashSplashPrefab = PrefabFolder + "/VFX_Pelag_Dash_Splash.prefab";
    private const string CrownSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_CrownSplash.prefab";
    private const string WaveSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_WaveSplash.prefab";
    private const string SabreWavePrefab = PrefabFolder + "/VFX_Pelag_Sabre_Wave.prefab";
    private const string CleaveCrackPrefab = PrefabFolder + "/VFX_Pelag_Cleave_Crack.prefab";
    private const string CleaveCrackMaterial = MaterialFolder + "/M_Cleave_Crack.mat";

    public const string ArcName = "VFX_Pelag_Wreck_Arc";
    public const string CrestName = "VFX_Pelag_Wreck_Crest";
    public const string SlamName = "VFX_Pelag_Wreck_Slam";
    public const string ShellName = "VFX_Pelag_Wreck_Shell";
    public const string CrackName = "VFX_Pelag_Wreck_Crack";
    private const string ArcMaterialName = "M_Wreck_Arc";
    private const string CrestMaterialName = "M_Wreck_Crest";
    private const string TrailMaterialName = "M_Wreck_Trail";
    private const string CraterMaterialName = "M_Wreck_Crater";
    private const string ShellMaterialName = "M_Wreck_Shell";
    private const string CrackMaterialName = "M_Wreck_Crack";

    private static readonly string[] OwnPrefabs = { ArcName, CrestName, SlamName, ShellName, CrackName };
    private static readonly string[] OwnMaterials = { ArcMaterialName, CrestMaterialName, TrailMaterialName, CraterMaterialName, ShellMaterialName, CrackMaterialName };

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // Пена, обвод и капли — как у семьи (Абордаж v2, Шквал v2).
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .86f, .91f);
    private static readonly Color Outline = new Color(.02f, .08f, .11f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color FoamWhite = new Color(.96f, 1.08f, 1.10f, 1f);
    private static readonly Color DropAqua = new Color(.50f, .92f, .95f, 1f);
    private static readonly Color ClodDark = new Color(.17f, .10f, .05f, 1f);
    private static readonly Color ClodLight = new Color(.30f, .19f, .10f, 1f);

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Крушение v2: подключить «морскую пену»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение v2: пересобрать «морскую пену»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaterShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashDropMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(ClodMaterial) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(CleaveCrackPrefab) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CleaveCrackMaterial) == null)
        {
            Debug.LogWarning("[wreck-setup] Нет шейдера воды форм, текстур CFXR, материалов рывка/сабли/Обвала или линии раскола Рассекающего — Крушение v2 не собрано.");
            return;
        }
        if (force || !UpToDate()) Build();

        // Bind ставит dirty, только если запись поменялась; библиотека пишется только тогда.
        bool ok = true;
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckArc, PrefabPath(ArcName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckCrest, PrefabPath(CrestName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckSlam, PrefabPath(SlamName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckShell, PrefabPath(ShellName), 1);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckCrack, PrefabPath(CrackName), 2);
        // Принятые префабы семьи — под своими id (свои пулы), без правки самих префабов.
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckSplash, SabreSplashPrefab, 6);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckCrown, DashSplashPrefab, 4);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckKnock, CrownSplashPrefab, 8);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckBurst, WaveSplashPrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckSwing, SabreWavePrefab, 3);
        if (!ok) Debug.LogWarning("[wreck-setup] Не все префабы Крушения v2 нашлись — недостающие записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(ArcName));
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
        Material arc = ArcMaterial();
        Material crest = CrestMaterial();
        Material trail = GroundMaterial(TrailMaterialName, true);
        Material crater = GroundMaterial(CraterMaterialName, false);
        Material shell = ShellMaterial();
        Material crack = CrackMaterial(AssetDatabase.LoadAssetAtPath<Material>(CleaveCrackMaterial));
        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам.
        foreach (Material m in new[] { arc, crest, trail, crater, shell, crack }) AssetDatabase.SaveAssetIfDirty(m);

        Material foam = AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial);
        Material crownDrop = AssetDatabase.LoadAssetAtPath<Material>(DashDropMaterial);
        Material drop = AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial);
        Material clod = AssetDatabase.LoadAssetAtPath<Material>(ClodMaterial);
        SaveArcPrefab(arc, foam, drop);
        SaveCrestPrefab(crest, trail, foam, crownDrop);
        SaveSlamPrefab(crater, foam, crownDrop, clod);
        SaveShellPrefab(shell, drop, crownDrop);
        SaveCrackPrefab(crack);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем (razlom-unity-particle-gotchas).
        foreach (string name in OwnMaterials)
            AssetDatabase.ImportAsset(MaterialPath(name), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(ArcName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-setup] Крушение v2 «морская пена» собрано: дуга якоря, гребень вала/стены/горба, круг удара, трещина, Водяной панцирь, ревизия " + Revision + ".");
    }
}
