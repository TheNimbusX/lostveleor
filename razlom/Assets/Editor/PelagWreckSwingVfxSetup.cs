using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение v4 «холодное железо» — МАХИ, сборка V2 (06.10). Целевые кадры владельца:
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/swing1.png и swing2.png — тяжёлый сплошной серп холодного
/// голубого с тёмным контуром и призрачными звеньями, сколы железа, короткий голубой росчерк, пыль, отброс. Вид —
/// Game.View/PelagVfxController.WreckSwing*.cs, числа — PelagWreckSwingRules / PelagWreckSwingImpact, цвета — одна таблица
/// PelagWreckSwingLook. V1 (тонкая белёсая лента штрихами и круглый голубой шар на задетом) владелец отверг.
///
/// СОБРАНО НА ПАКАХ (razlom-vfx-build-on-packs, razlom-urp-pack-materials — всё unlit):
///   серп — свой живой меш по пути головы на шейдере Razlom/Wreck Swing по рецепту CFXR «sword trail slash»: маска
///     «cfxr sword trail mask lines» (тело сплошное, хвост и края рвутся штрихами) + распад «cfxr perlin mid»;
///   звенья-призраки — меш звена «железа» базы WreckIronLink (только чтение) на том же шейдере (режим «звено»);
///   росчерк задетого — лист CFXR «cfxr stretch trait» растянутыми частицами (тёмный контур, тело, сердцевина);
///   сколы — лист CFXR «cfxr debris unlit 3x3» (тело — тёмное железо, кромка пака — цвет формы);
///   искры с головы и пыль — материалы «холодного железа» базы M_WreckIron_Spark / M_WreckIron_Dust (только чтение).
/// Без пены, молний, красного, оранжевого и золота.
///
/// Префабы: VFX_Pelag_WreckSwing_Arc («Crescent» — меш пишет вид, «Link0…2» — звенья, ставит вид, «Glints» — искры,
/// выбрасывает вид), VFX_Pelag_WreckSwing_Hit («Ink», «Streak», «Core», «Chips», «Dust» — выбрасывает вид).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка — только если своих ассетов нет или поднята <see cref="Version"/> (userData .meta префаба
/// дуги). Сохраняются только свои ассеты (SaveAssetIfDirty, префабы — SaveAsPrefabAsset), общего SaveAssets нет
/// (razlom-animator-builder-saveassets); библиотека пишется, только если наши записи поменялись. Ассеты пака и
/// «железа» базы не трогаются. Материал вмятины V1 (M_WreckSwing_Dent) больше ни на что не ссылается — не удаляется
/// (файлы не трогаем, их можно убрать руками). Любая правка сборки — поднять Version.
/// </summary>
public static partial class PelagWreckSwingVfxSetup
{
    /// <summary>Версия сборки ассетов махов. V2 — серп по пути рига, звенья, росчерк, сколы пака. Поднимать при любой правке.</summary>
    private const int Version = 2;
    private static readonly string Revision = "PelagWreckSwingV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string ShaderName = "Razlom/Wreck Swing";

    // Чужое — только чтение: пак и «железо» базы.
    private const string SlashMask = CfxrGraphics + "cfxr sword trail mask lines.png";
    private const string NoiseTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string StreakTexture = CfxrGraphics + "cfxr stretch trait.png";
    private const string DebrisTexture = CfxrGraphics + "cfxr debris unlit 3x3.png";
    private const string IronSpark = MaterialFolder + "/M_WreckIron_Spark.mat";
    private const string IronDust = MaterialFolder + "/M_WreckIron_Dust.mat";
    private const string IronLinkMesh = GeometryFolder + "/WreckIronLink.asset";

    public const string ArcName = "VFX_Pelag_WreckSwing_Arc";
    public const string HitName = "VFX_Pelag_WreckSwing_Hit";
    private const string CrescentMaterialName = "M_WreckSwing_Ribbon";
    private const string LinkMaterialName = "M_WreckSwing_Link";
    private const string StreakMaterialName = "M_WreckSwing_Streak";
    private const string ChipMaterialName = "M_WreckSwing_Chip";

    private static readonly string[] OwnPrefabs = { ArcName, HitName };
    private static readonly string[] OwnMaterials = { CrescentMaterialName, LinkMaterialName, StreakMaterialName, ChipMaterialName };

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    private static Color Hex(int hex, float a = 1f)
    {
        PelagWreckSwingLook.Rgb(hex, out float r, out float g, out float b);
        return new Color(r, g, b, a);
    }

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: подключить махи «холодного железа»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: пересобрать махи «холодного железа»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(ShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SlashMask) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(StreakTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DebrisTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(IronSpark) == null
            || AssetDatabase.LoadAssetAtPath<Material>(IronDust) == null
            || AssetDatabase.LoadAssetAtPath<Mesh>(IronLinkMesh) == null)
        {
            Debug.LogWarning("[wreck-swing-setup] Нет шейдера Razlom/Wreck Swing, листов CFXR или ассетов «холодного железа» базы — махи не собраны (сначала PelagWreckIronVfxSetup).");
            return;
        }
        if (force || !UpToDate()) Build();

        bool ok = true;
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckSwingArc, PrefabPath(ArcName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckSwingHit, PrefabPath(HitName), 8);
        if (!ok) Debug.LogWarning("[wreck-swing-setup] Не все префабы махов нашлись — недостающие записи библиотеки не созданы.");
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
        var kit = new Kit
        {
            Crescent = CrescentMaterial(), Link = LinkMaterial(), Streak = StreakMaterial(), Chip = ChipMaterial(),
            Spark = AssetDatabase.LoadAssetAtPath<Material>(IronSpark), Dust = AssetDatabase.LoadAssetAtPath<Material>(IronDust),
            LinkMesh = AssetDatabase.LoadAssetAtPath<Mesh>(IronLinkMesh)
        };
        // Только свои материалы — на диск; префабы SaveAsPrefabAsset пишет сам; ассеты пака и «железа» — не трогаются.
        foreach (Material m in new[] { kit.Crescent, kit.Link, kit.Streak, kit.Chip }) AssetDatabase.SaveAssetIfDirty(m);
        SaveArcPrefab(kit);
        SaveHitPrefab(kit);
        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем (razlom-unity-particle-gotchas).
        foreach (string name in OwnMaterials)
            AssetDatabase.ImportAsset(MaterialPath(name), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(ArcName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-swing-setup] Махи Крушения V2 собраны: серп по пути рига (маска CFXR sword trail, контур, звенья-призраки), росчерк, сколы, пыль; ревизия " + Revision + ".");
    }

    private static Material OwnMaterial(string name, float mode, int queue)
    {
        string path = MaterialPath(name);
        Shader shader = Shader.Find(ShaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        material.SetFloat("_Mode", mode);
        material.renderQueue = queue;
        material.enableInstancing = true;
        material.SetColor("_Tint", Hex(PelagWreckSwingLook.Tint(Game.Sim.PelagForm.None)));
        material.SetColor("_TintLight", Hex(PelagWreckSwingLook.Light(Game.Sim.PelagForm.None)));
        material.SetColor("_OutlineColor", Hex(PelagWreckSwingLook.OutlineHex));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Серп: маска CFXR «sword trail mask lines», распад «perlin mid», белая кромка, толстый контур.</summary>
    private static Material CrescentMaterial()
    {
        Material m = OwnMaterial(CrescentMaterialName, 0f, 3000);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SlashMask));
        m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture));
        m.SetFloat("_Glow", 1.5f);
        m.SetFloat("_EdgeFrom", .8f);
        m.SetFloat("_Opacity", .95f);
        m.SetFloat("_OutlineMeters", .055f);
        m.SetFloat("_Ink", .16f);
        m.SetFloat("_Cut", .1f);
        m.SetFloat("_MaskFrom", .42f);
        m.SetFloat("_MaskTo", .99f);
        m.SetFloat("_NoiseMeters", 1.4f);
        return m;
    }

    /// <summary>Звено-призрак: поверх серпа (очередь +1), светлое ядро к белому, чернила у силуэта прута.</summary>
    private static Material LinkMaterial()
    {
        Material m = OwnMaterial(LinkMaterialName, 1f, 3001);
        m.SetFloat("_Glow", 1.35f);
        m.SetFloat("_Opacity", .8f);
        m.SetFloat("_RimFrom", .55f);
        return m;
    }

    /// <summary>Росчерк задетого: одноканальный лист CFXR «stretch trait», цвет и альфа — частицы.</summary>
    private static Material StreakMaterial()
    {
        Material m = OwnMaterial(StreakMaterialName, 2f, 3000);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(StreakTexture));
        m.SetFloat("_Glow", 1.5f);
        return m;
    }

    /// <summary>Скол: лист обломков CFXR 3×3, тело — тёмное железо, кромка — цвет частицы.</summary>
    private static Material ChipMaterial()
    {
        Material m = OwnMaterial(ChipMaterialName, 3f, 3000);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DebrisTexture));
        m.SetColor("_IronColor", Hex(PelagWreckSwingLook.IronHex));
        m.SetFloat("_Glow", 1.25f);
        return m;
    }

    /// <summary>Что нужно префабам: свои материалы, ассеты «железа» базы и меш звена (только чтение).</summary>
    private sealed class Kit
    {
        public Material Crescent, Link, Streak, Chip, Spark, Dust;
        public Mesh LinkMesh;
    }
}
