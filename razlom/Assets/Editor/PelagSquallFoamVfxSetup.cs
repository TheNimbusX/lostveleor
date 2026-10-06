using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Шквал v2 «морская пена» (02.10, владелец: «резкое быстрое… очень органично»;
/// формы как аспекты Hades). Целевые кадры — выбор владельца,
/// ART/characters/pelag/squall-forms-2026-10-02/chatgpt-results: trail-wavy-1 и
/// trail-zigzag-2 (Пенный след и база — пенная струя на каждый прыжок),
/// elusive-return-3 (Неуловимый: двойник и дуга возврата); Охота — на тех же кадрах.
/// Семья та же, что серия сабли, рывок и пенный Вихрь: плоская бирюзовая вода,
/// белые комья пены по гребням, тонкий тёмный обвод, вырезанные капли; без
/// свечения и дыма. Вид — Game.View/PelagVfxController.Squall*.cs.
///
/// СОБРАНО НА ПРИНЯТОМ:
///  • вода (струя, полоса, дуга) — шейдер воды форм Вихря Razlom/Whirlwind Form Water
///    (язык следа рывка; не меняется), свой материал M_Squall_Water, текстуры CFXR
///    «cfxr sword trail noise bubbles» и «cfxr perlin mid»; меш пишет вид;
///  • комья, лужицы, капли — материалы рывка и серии сабли (только чтение):
///    M_Dash_Foam (клякса CFXR), M_Dash_Drop (белые вырезанные капли короны),
///    M_Sabre_Drop (капли заноса);
///  • всплеск на теле — префаб всплеска серии сабли VFX_Pelag_Sabre_Splash, корона у
///    ноги — VFX_Pelag_Dash_Splash, волна-толчок Охоты — VFX_Pelag_Sabre_Crash: в
///    библиотеку Пелага под своими id (свои пулы), сами префабы не меняются;
///  • свой шейдер только у двойника Неуловимого: Razlom/Squall Foam Ghost
///    (вода и пена на копии позы героя; самого героя ничто не красит). Нет шейдера —
///    собирается всё, кроме двойника.
///
/// Свои префабы: VFX_Pelag_Squall_Water (вода + выбрасываемые частицы), _Strike и
/// _Finish (кольцо пены у ног цели, последний удар — крупнее, с короной), _Mark
/// (метка добычи Охоты), _Cue (пена у ног замедленных, вуаль неуязвимости), _Ghost
/// (двойник: 12 слотов позы, капли, лужица, всплеск).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или поднята
/// <see cref="Version"/> (пишется в userData .meta префаба воды). Сохраняются только
/// свои ассеты (SaveAssetIfDirty), общего SaveAssets нет; библиотека пишется, только
/// если наши записи поменялись. Прежний PelagSquallVfxSetup (CFXR-росчерк и удары
/// ChainStep*) не трогается: его префабы нужны Skewer и Wreck и запасному пути вида.
/// Любая правка сборки ниже — поднять Version.
/// </summary>
public static partial class PelagSquallFoamVfxSetup
{
    /// <summary>Версия сборки ассетов Шквала v2. Поднимать при любой правке сборки.</summary>
    private const int Version = 1;
    private static readonly string Revision = "PelagSquallFoamV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string BubblesTexture = CfxrGraphics + "cfxr sword trail noise bubbles.png";
    private const string PerlinTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string WaterShaderName = "Razlom/Whirlwind Form Water";
    private const string GhostShaderName = "Razlom/Squall Foam Ghost";

    // Чужое принятое — только чтение.
    private const string DashFoamMaterial = MaterialFolder + "/M_Dash_Foam.mat";
    private const string DashDropMaterial = MaterialFolder + "/M_Dash_Drop.mat";
    private const string SabreDropMaterial = MaterialFolder + "/M_Sabre_Drop.mat";
    private const string SabreSplashPrefab = PrefabFolder + "/VFX_Pelag_Sabre_Splash.prefab";
    private const string DashSplashPrefab = PrefabFolder + "/VFX_Pelag_Dash_Splash.prefab";
    private const string SabreCrashPrefab = PrefabFolder + "/VFX_Pelag_Sabre_Crash.prefab";

    public const string WaterName = "VFX_Pelag_Squall_Water";
    public const string StrikeName = "VFX_Pelag_Squall_Strike";
    public const string FinishName = "VFX_Pelag_Squall_Finish";
    public const string MarkName = "VFX_Pelag_Squall_Mark";
    public const string CueName = "VFX_Pelag_Squall_Cue";
    public const string GhostName = "VFX_Pelag_Squall_Ghost";
    private const string WaterMaterialName = "M_Squall_Water";
    private const string GhostMaterialName = "M_Squall_Ghost";

    private static readonly string[] OwnPrefabs = { WaterName, StrikeName, FinishName, MarkName, CueName };

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // База Шквала = бирюза базового Вихря (палитра серии сабли, вариант Б). Формы — PelagSquallFormLook.
    private static readonly Color Deep = new Color(.03f, .24f, .32f);
    private static readonly Color Water = new Color(.06f, .60f, .68f);
    private static readonly Color Shallow = new Color(.42f, .90f, .92f);
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .86f, .91f);
    private static readonly Color Outline = new Color(.02f, .08f, .11f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color FoamWhite = new Color(.96f, 1.08f, 1.10f, 1f);
    private static readonly Color DropAqua = new Color(.50f, .92f, .95f, 1f);

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Шквал v2: подключить «морскую пену»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Шквал v2: пересобрать «морскую пену»")]
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
            || AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial) == null)
        {
            Debug.LogWarning("[squall-setup] Нет шейдера воды форм, текстур CFXR или материалов рывка/сабли — Шквал v2 не собран.");
            return;
        }
        bool ghost = Shader.Find(GhostShaderName) != null;
        if (!ghost) Debug.LogWarning("[squall-setup] Нет шейдера Razlom/Squall Foam Ghost — двойник Неуловимого не собран.");
        if (force || !UpToDate(ghost)) Build(ghost);

        // Bind ставит dirty, только если запись поменялась; библиотека пишется только тогда.
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallWater, PrefabPath(WaterName), 8);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallStrike, PrefabPath(StrikeName), 6);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallFinish, PrefabPath(FinishName), 2);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallMark, PrefabPath(MarkName), 2);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallCue, PrefabPath(CueName), 1);
        // Прогрев = слотов двойников в виде (PelagVfxController.SquallCues: _sqGhosts, 4): двойник точки каста
        // и до трёх коротких на отрывах живут разом; при 3 съёмка Неуловимого писала «Пул исчерпан».
        if (ghost) PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallGhost, PrefabPath(GhostName), 4);
        // Принятые префабы семьи — под своими id (свои пулы), без правки самих префабов.
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallSplash, SabreSplashPrefab, 6);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallCrown, DashSplashPrefab, 2);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.SquallSurge, SabreCrashPrefab, 1);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate(bool ghost)
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(WaterName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in OwnPrefabs)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(WaterMaterialName)) == null) return false;
        if (ghost && (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(GhostName)) == null
                      || AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(GhostMaterialName)) == null)) return false;
        return true;
    }

    private static void Build(bool ghost)
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        Material water = WaterMaterial();
        Material ghostMat = ghost ? GhostMaterial() : null;
        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам.
        AssetDatabase.SaveAssetIfDirty(water);
        if (ghostMat != null) AssetDatabase.SaveAssetIfDirty(ghostMat);

        Material foam = AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial);
        Material crownDrop = AssetDatabase.LoadAssetAtPath<Material>(DashDropMaterial);
        Material drop = AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial);
        SaveWaterPrefab(water, foam, drop);
        SaveRingPrefab(StrikeName, PelagVfxId.SquallStrike, false, foam, crownDrop);
        SaveRingPrefab(FinishName, PelagVfxId.SquallFinish, true, foam, crownDrop);
        SaveMarkPrefab(foam, crownDrop);
        SaveCuePrefab(foam, drop, crownDrop);
        if (ghostMat != null) SaveGhostPrefab(ghostMat, foam, drop);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем.
        AssetDatabase.ImportAsset(MaterialPath(WaterMaterialName), ImportAssetOptions.ForceUpdate);
        if (ghostMat != null) AssetDatabase.ImportAsset(MaterialPath(GhostMaterialName), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(WaterName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[squall-setup] Шквал v2 «морская пена» собран: вода прыжков, кольца ударов, метка Охоты, знаки у ног"
                  + (ghost ? ", двойник Неуловимого" : "") + ", ревизия " + Revision + ".");
    }

    // -------------------------------------------------------------- materials

    /// <summary>Материал с нуля при сохранении ассета и GUID (как у серии сабли и рывка).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    /// <summary>
    /// Вода Шквала: язык следа рывка на шейдере воды форм. Гребни по ОБОИМ краям
    /// одинаковые (_Crest.w = 1), глубины у внутренней кромки нет (_Bands.w = 0) —
    /// у прямой струи «внутреннего» края нет. Возраст распада ведёт вид
    /// (PelagSquallWater.ShaderBreakAge = _Break.x): вода белеет пеной за 0,12
    /// до трещин, капли живут ещё 0,07.
    /// </summary>
    private static Material WaterMaterial()
    {
        Material material = Fresh(WaterMaterialName, Shader.Find(WaterShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetFloat("_CoarseScale", .5f);
        material.SetFloat("_ClumpScale", .85f);
        material.SetVector("_Bands", new Vector4(.15f, .75f, 0f, 0f));
        material.SetVector("_Crest", new Vector4(.11f, .09f, .03f, 1f));
        material.SetFloat("_EndRag", .28f);
        material.SetVector("_Lines", new Vector4(4.5f, 1.6f, .40f, .85f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.6f, .32f, .45f));
        material.SetFloat("_OutlinePx", 2f);
        material.SetFloat("_Glow", .2f);
        material.SetVector("_Break", new Vector4(PelagSquallFoamRules.ShaderBreakAge, PelagSquallFoamRules.ShaderBreakJitter * 2f, 0f,
            PelagSquallFoamRules.ShaderFoamUp));
        material.SetFloat("_BreakScale", 2.2f);
        material.SetFloat("_DropLife", PelagSquallFoamRules.ShaderDropLife);
        material.SetFloat("_EdgeEarly", .03f);
        material.SetFloat("_BreakRimPx", 1.5f);
        material.SetFloat("_FadeFrom", .40f);
        material.SetFloat("_FadeTo", .46f);
        // Шейдер форм v4 вместо _CameraPush: свой тест глубины, поверх тел не рисует (трафарет).
        // Меш Шквала пишет COLOR.r = 1 по всей длине, и у ног героя (там сабля), поэтому
        // допуск везде прежний 0,3 м (_Over.x = _Over.w), без пены поверх препятствий (_Over.z = 0).
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        material.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Двойник Неуловимого: полупрозрачная бирюза, пена по кромке силуэта, стекает вниз.</summary>
    private static Material GhostMaterial()
    {
        Material material = Fresh(GhostMaterialName, Shader.Find(GhostShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetFloat("_BodyAlpha", .42f);
        material.SetFloat("_RimAlpha", .95f);
        material.SetVector("_Rim", new Vector4(.45f, .80f, 0f, 0f));
        material.SetFloat("_FoamScale", 2.4f);
        material.SetFloat("_FlowSpeed", .35f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
