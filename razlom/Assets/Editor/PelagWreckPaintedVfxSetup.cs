using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение v4 на РИСОВАННЫХ ТЕКСТУРАХ (06.10, сборка V1). Процедурные попытки владелец отверг: выпад «кривой, косой,
/// простой, как будто из примитивов собран», махи «плоско, звенья цепи внутри нечитаемые, некрасивые и мыльные». Волны
/// сабли приняты, потому что это рисунок пака на простом меше с нашим порогом и обводом, — здесь то же для своего
/// рисунка: лист ART/characters/pelag/wreck-look-2026-10-06/vfx-textures/sheet-base-v1.png, нарезанный скриптом
/// artifacts/wreck/v4/vfx-painted/tools/cut_painted.py на 5 текстур (Textures/WreckPainted: полумесяц со звеньями,
/// выпрямленный вдоль дуги; звено; звезда удара; полоса выпада; 6 сколов и комьев сеткой 4×2). Целевые кадры —
/// chatgpt-results/v4-swing-left.png, v4-swing-right.png, v4-lunge-simple.png.
///
/// Шейдер один — «Razlom/Wreck Painted» (цвет рисунка как есть, силуэт порогом по альфе с тонким тёмным обводом,
/// раскрытие вдоль u, распад порогом без прозрачности, цвет формы — только синим местам). Префабы:
///  • VFX_Pelag_WreckPaintedSwing / …SwingHeavy — полумесяц пака CFXR «sword_trail 180 thick» (u вдоль дуги),
///    пересчитанный под размах маха, с рисунком полумесяца; мах 2 — зеркало (поворот корня), ярче и дольше;
///  • VFX_Pelag_WreckPaintedHit — звезда удара (спрайт) и сколы-спрайты с вращением и тяжестью;
///  • VFX_Pelag_WreckPaintedLunge — рисованная полоса на земле (меш пишет вид по полосе Sim, голова — фронт Sim),
///    звезда удара на земле и комья-спрайты.
///
/// Ассеты свои и версионные (Revision в userData .meta префаба маха); сохраняются только свои (SaveAssetIfDirty,
/// SaveAsPrefabAsset) — общего AssetDatabase.SaveAssets нет (razlom-animator-builder-saveassets). Прежние махи и выпад
/// (PelagWreckSwingIronVfxSetup, PelagWreckLungeVfxSetup, шейдеры «Wreck Iron Wave/Line») не удаляются, но их записи
/// библиотеки (WreckIronSwing*, WreckLunge) снимаются: вид рисует только этот путь. Префабы — .Prefabs.cs.
/// </summary>
public static partial class PelagWreckPaintedVfxSetup
{
    /// <summary>
    /// Версия сборки. Любая правка ассетов — поднять. V2 (съёмка 1): хвост рисунка тонул под порогом альфы (полумесяц
    /// читался «)»), полоса тонкая и темнее кадра — порог ниже, полоса толще, рисунок ярче; звезда и комья крупнее.
    /// </summary>
    private const int Version = 2;
    private static readonly string Revision = "PelagWreckPaintedV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string TextureFolder = Root + "/Textures/WreckPainted";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string ShaderName = "Razlom/Wreck Painted";

    public const string SwingName = "VFX_Pelag_WreckPaintedSwing";
    public const string HeavyName = "VFX_Pelag_WreckPaintedSwingHeavy";
    public const string HitName = "VFX_Pelag_WreckPaintedHit";
    public const string LungeName = "VFX_Pelag_WreckPaintedLunge";

    private static readonly string[] TextureNames =
        { "WreckPainted_Swing", "WreckPainted_Link", "WreckPainted_Star", "WreckPainted_Lunge", "WreckPainted_Chips" };

    /// <summary>Жизнь полумесяца, с: мах 2 начинается через ~0,2 с — мах 1 к нему почти рассыпался.</summary>
    internal const float SwingLife = .30f, HeavyLife = .38f;

    /// <summary>Тёмный обвод — чернила (как у волны сабли и врагов), не чистый чёрный.</summary>
    private static readonly Color Ink = new Color(.03f, .05f, .16f, 1f);

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";
    private static string TexturePath(string name) => TextureFolder + "/" + name + ".png";

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Install(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: рисованные текстуры (махи и выпад) — подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: рисованные текстуры (махи и выпад) — пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(ShaderName) == null || ThickMesh() == null || !TexturesPresent())
        {
            Debug.LogWarning("[wreck-painted] Нет шейдера «Wreck Painted», меша пака или рисованных текстур — не собрано.");
            return;
        }
        if (force || !UpToDate()) Build();
        // 06.10 поздно: рисованные махи и знак отвергнуты («плоско, наклейка») — махи 1–2 и знак рисует стек серии сабли
        // (PelagWreckComboVfxSetup); здесь в библиотеке остаётся только рисованный выпад (префабы махов на месте).
        // 06.10 ещё позже: рисованный выпад тоже отвергнут — выпад рисует стек серии сабли (PelagWreckComboVfxSetup.Lunge);
        // в библиотеке отсюда не остаётся ничего (префабы и код на месте).
        Unbind(library, PelagVfxId.WreckIronSwing, PelagVfxId.WreckIronSwingHeavy, PelagVfxId.WreckIronSwingHit, PelagVfxId.WreckLunge,
            PelagVfxId.WreckPaintedSwing, PelagVfxId.WreckPaintedSwingHeavy, PelagVfxId.WreckPaintedHit, PelagVfxId.WreckPaintedLunge);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Снять записи прежних процедурных махов и выпада (их префабы и код остаются на месте).</summary>
    private static void Unbind(AbilityVfxLibrary library, params PelagVfxId[] ids)
    {
        int before = library.Entries.Length;
        library.Entries = System.Array.FindAll(library.Entries, e => System.Array.IndexOf(ids, e.Id) < 0);
        if (library.Entries.Length != before) EditorUtility.SetDirty(library);
    }

    private static bool TexturesPresent()
    {
        foreach (string name in TextureNames)
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(name)) == null) return false;
        return true;
    }

    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(SwingName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in new[] { HeavyName, HitName, LungeName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static Mesh ThickMesh()
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh && mesh.name == "cfxr mesh sword_trail 180 thick") return mesh;
        return null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        foreach (string name in TextureNames) EnsureImport(TexturePath(name));
        Shader shader = Shader.Find(ShaderName);
        Texture2D swing = Tex("WreckPainted_Swing"), star = Tex("WreckPainted_Star");
        Texture2D lunge = Tex("WreckPainted_Lunge"), chips = Tex("WreckPainted_Chips");

        float span = PelagWreckPaintedLook.SwingTailBack + PelagWreckPaintedLook.SwingHeadPast;
        Mesh arc = ArcMesh("WreckPaintedSwingArc", ThickMesh(), PelagWreckPaintedLook.SwingTailBack, PelagWreckPaintedLook.SwingHeadPast,
            PelagWreckPaintedLook.SwingInner(span));
        Mesh quad = GroundQuad("WreckPaintedGroundQuad");

        var m = new Materials
        {
            // Мах: голова пробегает дугу от хвоста за ~4 кадра, держится, рассыпается от хвоста порогом по мазкам.
            Swing = Painted(shader, "M_WreckPainted_Swing", swing, .30f, .30f, .08f, 0f, SwingLife, .20f, .075f, .60f, .35f, .11f, .29f, 1.0f),
            Heavy = Painted(shader, "M_WreckPainted_SwingHeavy", swing, .28f, .40f, .12f, 0f, HeavyLife, .18f, .085f, .60f, .35f, .14f, .37f, 1.0f),
            // Звезда на задетом: целиком с рождения, рассыпается лучами (сначала тёмное).
            HitStar = Painted(shader, "M_WreckPainted_HitStar", star, .45f, .28f, .10f, 0f, .26f, 1.02f, 0f, .70f, 0f, .09f, .26f, .7f),
            // Сколы: возраст частицы нормирован (жизнь 1), рассыпаются в конце полёта.
            Chips = Painted(shader, "M_WreckPainted_Chips", chips, .5f, .12f, .0f, 0f, 1f, 1.02f, 0f, .50f, 0f, .62f, 1.0f, .5f),
            // Выпад: возраст и фронт — блоком свойств (меш вида на земле).
            Lunge = Painted(shader, "M_WreckPainted_Lunge", lunge, .45f, .28f, .10f, 1f, 1f, 0f, 0f, .60f, .20f, .22f, .52f, .05f),
            LungeStar = Painted(shader, "M_WreckPainted_LungeStar", star, .45f, .30f, .12f, 1f, 1f, 1.02f, 0f, .70f, 0f, .26f, .52f, .06f),
            Chunks = Painted(shader, "M_WreckPainted_Chunks", chips, .5f, .12f, .0f, 0f, 1f, 1.02f, 0f, .50f, 0f, .70f, 1.0f, .35f),
        };
        m.Lunge.SetFloat("_UseFront", 1f);
        // Полумесяц: тонкий хвост рисунка — слабое свечение; обвод с порога ниже, рисунок ярче (кадры светлее).
        foreach (var material in new[] { m.Swing, m.Heavy }) material.SetFloat("_OutlineCut", .10f);
        m.Swing.SetFloat("_Gain", 1.12f);
        m.Heavy.SetFloat("_Gain", 1.18f);
        m.Lunge.SetFloat("_Gain", 1.08f);
        foreach (var material in new[] { m.Swing, m.Heavy, m.HitStar, m.Chips, m.Lunge, m.LungeStar, m.Chunks })
        {
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        AssetDatabase.SaveAssetIfDirty(arc);
        AssetDatabase.SaveAssetIfDirty(quad);

        SaveSwingPrefab(SwingName, PelagVfxId.WreckPaintedSwing, arc, m.Swing, SwingLife, -12f);
        SaveSwingPrefab(HeavyName, PelagVfxId.WreckPaintedSwingHeavy, arc, m.Heavy, HeavyLife, -14f);
        SaveHitPrefab(m);
        SaveLungePrefab(m, quad);

        var importer = AssetImporter.GetAtPath(PrefabPath(SwingName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-painted] Крушение на рисованных текстурах собрано: " + SwingName + ", " + HeavyName + ", " + HitName
            + ", " + LungeName + " (" + Revision + ").");
    }

    private struct Materials
    {
        public Material Swing, Heavy, HitStar, Chips, Lunge, LungeStar, Chunks;
    }

    private static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(name));

    /// <summary>
    /// Импорт рисунка: цвет (sRGB), альфа — прозрачность, мипы с резким фильтром Кайзера, clamp, без сжатия (RGBA32),
    /// без уменьшения. Правится и переимпортируется, только если что-то не так (.meta пишет нарезка).
    /// </summary>
    private static void EnsureImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool dirty = importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || !importer.alphaIsTransparency
            || !importer.mipmapEnabled || importer.mipmapFilter != TextureImporterMipFilter.KaiserFilter
            || importer.wrapMode != TextureWrapMode.Clamp || importer.textureCompression != TextureImporterCompression.Uncompressed
            || importer.maxTextureSize < 4096 || importer.npotScale != TextureImporterNPOTScale.None;
        if (!dirty) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 4;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }
}
