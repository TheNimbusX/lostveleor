using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Бросок якоря «морская пена» (03.10; формы — Невод, Веер, Гарпун). Целевые кадры —
/// ART/characters/pelag/anchor-throw-2026-10-03/chatgpt-results: A-base, B-net, C-fan, D-harpoon.
/// Семья та же, что серия сабли, рывок, пенный Вихрь, Шквал v2 и Абордаж v2: плоская вода, белые
/// комья пены по гребням, тонкий тёмный обвод, вырезанные капли; без свечения и дыма. Вид —
/// Game.View/PelagVfxController.AnchorThrow*.cs, цвета форм — PelagAnchorThrowFormLook.
///
/// СОБРАНО НА ПРИНЯТОМ (только чтение, своих правок в чужих ассетах нет):
///  • под своими id (свои пулы): лента воды на цепи, росчерк, тень-линия и струйки к цепи —
///    VFX_Pelag_Abordage_Ribbon; всплеск на теле — VFX_Pelag_Sabre_Splash; корона у ноги —
///    VFX_Pelag_Dash_Splash; корона «петли», узла и приземления — VFX_Pelag_Whirlwind_CrownSplash;
///    веер укуса Гарпуна — VFX_Pelag_Whirlwind_WaveSplash; вспаханная пена за головой —
///    VFX_Pelag_Dash_Wake; борозда волока — VFX_Pelag_Whirlwind_MaelstromDrag; голова якоря и цепь
///    временного пути — VFX_AnchorLeap_Throw / VFX_AnchorLeap_Chain (модель якоря подставляет пул по id
///    AnchorThrowAnchor — правка CreatePooledEffect);
///  • свои материалы на принятых шейдерах: сеть Невода — Razlom/Whirlwind Form Water (M_AnchorThrow_Net —
///    кобальтовый лист, M_AnchorThrow_NetStrand — белые нити и узлы), призрак Веера — Razlom/Squall Foam
///    Ghost (M_AnchorThrow_Ghost, индиго); текстуры CFXR «cfxr sword trail noise bubbles», «cfxr perlin mid»;
///    капли и комья — M_Sabre_Drop, M_Dash_Foam (только чтение).
/// Свои префабы: VFX_Pelag_AnchorThrow_Net (лист «Water», нити «Strands», узлы «Foam», капли «Drops»),
/// VFX_Pelag_AnchorThrow_Ghost (меш нашей головы «Head» + кольцо «Head/Ring», капли «Drops», брызги «Burst»).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или поднята <see cref="Version"/>
/// (userData .meta префаба сети). Сохраняются только свои ассеты (SaveAssetIfDirty), общего SaveAssets нет
/// (razlom-animator-builder-saveassets); библиотека пишется, только если наши записи поменялись. Чужие
/// префабы и материалы не трогаются. Любая правка сборки — поднять Version.
/// </summary>
public static partial class PelagAnchorThrowFoamVfxSetup
{
    /// <summary>Версия сборки ассетов Броска якоря. 1 (03.10): первая сборка по кадрам A–D.</summary>
    private const int Version = 1;
    private static readonly string Revision = "PelagAnchorThrowFoamV" + Version;

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
    private const string SabreDropMaterial = MaterialFolder + "/M_Sabre_Drop.mat";
    private const string RibbonPrefab = PrefabFolder + "/VFX_Pelag_Abordage_Ribbon.prefab";
    private const string SabreSplashPrefab = PrefabFolder + "/VFX_Pelag_Sabre_Splash.prefab";
    private const string DashSplashPrefab = PrefabFolder + "/VFX_Pelag_Dash_Splash.prefab";
    private const string DashWakePrefab = PrefabFolder + "/VFX_Pelag_Dash_Wake.prefab";
    private const string CrownSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_CrownSplash.prefab";
    private const string WaveSplashPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_WaveSplash.prefab";
    private const string MaelstromDragPrefab = PrefabFolder + "/VFX_Pelag_Whirlwind_MaelstromDrag.prefab";
    private const string AnchorHeadPrefab = PrefabFolder + "/VFX_AnchorLeap_Throw.prefab";
    private const string AnchorChainPrefab = PrefabFolder + "/VFX_AnchorLeap_Chain.prefab";

    public const string NetName = "VFX_Pelag_AnchorThrow_Net";
    public const string GhostName = "VFX_Pelag_AnchorThrow_Ghost";
    private const string NetMaterialName = "M_AnchorThrow_Net";
    private const string StrandMaterialName = "M_AnchorThrow_NetStrand";
    private const string GhostMaterialName = "M_AnchorThrow_Ghost";

    private static readonly string[] OwnPrefabs = { NetName, GhostName };
    private static readonly string[] OwnMaterials = { NetMaterialName, StrandMaterialName, GhostMaterialName };

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // Невод — кобальт #2D5BE3 (таблица PelagAnchorThrowFormLook; вид красит тем же блоком), Веер — индиго #4B3FD0.
    private static readonly Color NetDeep = new Color(.04f, .10f, .38f);
    private static readonly Color NetWater = new Color(.18f, .36f, .89f);
    private static readonly Color NetShallow = new Color(.58f, .74f, 1f);
    private static readonly Color FanDeep = new Color(.09f, .05f, .36f);
    private static readonly Color FanWater = new Color(.29f, .25f, .82f);
    private static readonly Color FanShallow = new Color(.66f, .62f, 1f);
    private static readonly Color Foam = new Color(1.12f, 1.22f, 1.22f);
    private static readonly Color FoamShade = new Color(.62f, .76f, .98f);
    private static readonly Color Outline = new Color(.02f, .05f, .14f, .92f);
    private static readonly Color DropWhite = new Color(1.08f, 1.18f, 1.18f, 1f);
    private static readonly Color FoamWhite = new Color(.96f, 1.08f, 1.10f, 1f);
    private static readonly Color DropBlue = new Color(.62f, .74f, 1f, 1f);
    private static readonly Color DropIndigo = new Color(.70f, .66f, 1f, 1f);

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: подключить «морскую пену»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Бросок якоря: пересобрать «морскую пену»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaterShaderName) == null || Shader.Find(GhostShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(RibbonPrefab) == null)
        {
            Debug.LogWarning("[anchor-throw-setup] Нет шейдеров воды/двойника, текстур CFXR, материалов рывка/сабли или ленты Абордажа v2 — Бросок якоря не собран.");
            return;
        }
        if (force || !UpToDate()) Build();

        // Bind ставит dirty, только если запись поменялась; библиотека пишется только тогда.
        bool ok = true;
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowNet, PrefabPath(NetName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowGhost, PrefabPath(GhostName), 3);
        // Принятые префабы семьи — под своими id (свои пулы), без правки самих префабов. Прогрев — по слотам вида:
        // лента — вода на цепи, росчерк, тень-линия, две цепи призраков и до 8 струек к цепи.
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowRibbon, RibbonPrefab, 14);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowSplash, SabreSplashPrefab, 8);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowCrown, DashSplashPrefab, 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowKnock, CrownSplashPrefab, 12);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowBurst, WaveSplashPrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowWake, DashWakePrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowDrag, MaelstromDragPrefab, 8);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowAnchor, AnchorHeadPrefab, 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.AnchorThrowChain, AnchorChainPrefab, 2);
        if (!ok) Debug.LogWarning("[anchor-throw-setup] Не все префабы Броска якоря нашлись — недостающие записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(NetName));
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
        Material net = NetMaterial();
        Material strand = StrandMaterial();
        Material ghost = GhostMaterial();
        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам.
        foreach (Material m in new[] { net, strand, ghost }) AssetDatabase.SaveAssetIfDirty(m);

        Material foam = AssetDatabase.LoadAssetAtPath<Material>(DashFoamMaterial);
        Material drop = AssetDatabase.LoadAssetAtPath<Material>(SabreDropMaterial);
        SaveNetPrefab(net, strand, foam, drop);
        SaveGhostPrefab(ghost, foam, drop);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем.
        foreach (string name in OwnMaterials)
            AssetDatabase.ImportAsset(MaterialPath(name), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(NetName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[anchor-throw-setup] Бросок якоря «морская пена» собран: сеть Невода, призрак Веера, пулы семьи, ревизия " + Revision + ".");
    }
}
