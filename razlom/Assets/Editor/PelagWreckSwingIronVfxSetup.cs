using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Махи Крушения v4 «холодное железо» на РИСОВАННОЙ технике принятой серии сабли (06.10, сборка V1). Ленту за головой
/// якоря (PelagWreckSwingVfxSetup, V2) владелец отверг: «VFX первых двух ударов в целом говно… у всех предыдущих скилов
/// ты делал круто, рисовано так». Здесь — то же, что у PelagSabreComboVfxSetup: полумесяц пака CFXR
/// «cfxr mesh sword_trail 180 thick», пересчитанный под мах, на наших шейдерах «Razlom/Sabre Foam Wave» (полосы, порог
/// с тёмным обводом, раскадровка по возрасту частицы) и «Razlom/Sabre Foam Blob» (рисованные частицы с обводом), рождение —
/// от событий Sim (контакт маха, Damage). Целевые кадры — ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/
/// swing1.png и swing2.png: полоса тёмного железа с ярким холодным голубым ядром (#4FA8FF → #9CD8FF), белая наружная
/// кромка, толстый тёмный обвод, звенья-призраки цепи вдоль полосы (своя маска звеньев на Blob), искры с головы.
/// Размер — досягаемость короткой цепи: радиус 1,2–1,5 м на высоте пояса (ставит контроллер). Мах 2 — зеркало (корень
/// повёрнут вокруг оси удара, как удар 2 сабли) и тяжелее: шире размах, толще полоса, звеньев больше, живёт дольше.
/// На задетом — сколы железа (лист обломков пака), голубые искры, звезда удара и клуб пыли (дым пака) на Blob вместо
/// клякс пены. Цвет формы ведёт ядро (Water/Shallow через MaterialPropertyBlock): база #4FA8FF, Волнорез #1FB37E,
/// Девятый вал #4B3FD0, Якорная броня #E4EEF6. Без пены, красного, оранжевого и золота.
///
/// Ассеты свои и версионные (Revision в userData .meta лёгкого префаба); сохраняются только свои (SaveAssetIfDirty,
/// SaveAsPrefabAsset) — общего AssetDatabase.SaveAssets нет (razlom-animator-builder-saveassets). Пак и прежние ассеты
/// махов не трогаются и не удаляются. Геометрия и текстуры — .Meshes.cs, префабы — .Prefabs.cs.
/// </summary>
public static partial class PelagWreckSwingIronVfxSetup
{
    /// <summary>
    /// Версия сборки. Любая правка ассетов — поднять. V3 — по целевым кадрам v4-swing-left/right.png (06.10).
    /// V4 (06.10 вечер) — владелец: «мыло… и линейно»: чёткая форма как волна сабли (плотное тело, жёсткие полосы, обвод),
    /// своя сетка с сужением и утолщениями, внутренний край — штрихи кисти (своя маска мазков), звенья с обводом.
    /// </summary>
    private const int Version = 4;
    private static readonly string Revision = "PelagWreckSwingIronV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string TextureFolder = Root + "/Textures";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    // Чужое — только чтение.
    private const string DropTexture = CfxrGraphics + "cfxr water drop blur anim.png";
    private const string SpikesTexture = CfxrGraphics + "cfxr spikes impact dissolve.png";
    private const string SmokeTexture = CfxrGraphics + "cfxr smoke cloud x4 dissolve.png";
    private const string DebrisTexture = CfxrGraphics + "cfxr debris unlit 3x3.png";
    // V4: полоса — «Razlom/Wreck Iron Wave» (рисованная техника волны сабли + мазки кисти), частицы — Blob сабли.
    private const string WaveShaderName = "Razlom/Wreck Iron Wave";
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";

    public const string LightName = "VFX_Pelag_WreckIronSwing";
    public const string HeavyName = "VFX_Pelag_WreckIronSwingHeavy";
    public const string HitName = "VFX_Pelag_WreckIronSwingHit";

    // Дуга «C» по кадрам: хвост (откуда шёл якорь) кончается чуть за боком, голова закручивается за спину героя.
    // Мах 1 (v4-swing-left): хвост 100°, голова 150°; мах 2 (v4-swing-right): 95° и 160°.
    internal const float LightTail = 100f, LightHead = 150f, HeavyTail = 95f, HeavyHead = 160f;
    /// <summary>Толщина полосы в самом широком месте, доля радиуса (сплошная часть ~0,36 R, штрихи кисти — до 0,62 R).</summary>
    private const float LightCompress = .62f, HeavyCompress = .70f;
    /// <summary>Жизнь полумесяца, с: мах 2 начинается через 0,2 с — мах 1 к нему почти рассыпался.</summary>
    private const float LightLife = .26f, HeavyLife = .34f;
    /// <summary>Звенья-призраки (кадр: 3 крупных; мах 1 — по обе стороны якоря, мах 2 — по стороне головы), отступ, град.</summary>
    private static readonly float[] LightLinkAt = { -45f, 40f, 82f };
    private static readonly float[] HeavyLinkAt = { 18f, 55f, 92f };
    /// <summary>Звено: длина и ширина, доли радиуса (≈0,8 × 0,43 м при R 2,2–2,4; кадр — ~90 × 45 px).</summary>
    private const float LinkLength = .36f, LinkWidth = .19f;

    // Палитра по кадру (снято пипеткой): внутреннее железо — плотный насыщенный синий (V4: без полупрозрачности), ядро —
    // яркий холодный голубой, светлое ядро — голубой лёд, белая кромка (чуть за порогом блума), тонкий тёмный обвод.
    private static readonly Color Iron = new Color(.16f, .33f, .74f, 1f);
    private static readonly Color Core = new Color(.25f, .55f, .96f);
    private static readonly Color CoreLight = new Color(.58f, .87f, 1.00f);
    private static readonly Color Rim = new Color(1.16f, 1.20f, 1.24f);
    private static readonly Color RimShade = new Color(.86f, .95f, 1.04f);
    private static readonly Color Ink = new Color(.03f, .05f, .16f, 1f);
    /// <summary>Край полосы в два тона (кадр, пипетка: #224174 снаружи, #2960B8 под ним).</summary>
    private static readonly Color EdgeNavy = new Color(.05f, .10f, .30f, 1f);
    private static readonly Color EdgeBlue = new Color(.16f, .38f, .80f, 1f);

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // 06.10 вечер: отвязано — махи и выпад рисует PelagWreckPaintedVfxSetup (рисованные текстуры; владелец отверг
    // процедурные V3/V4). Автоподключения нет, файлы и меню остаются; записи библиотеки снимает новая сборка.

    [MenuItem("Разлом/Pelag VFX/Крушение: махи «железо» на технике сабли — подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: махи «железо» на технике сабли — пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaveShaderName) == null || Shader.Find(BlobShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SpikesTexture) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(DebrisTexture) == null)
        {
            Debug.LogWarning("[wreck-swing-iron] Нет шейдеров серии сабли или листов CFXR — махи не собраны.");
            return;
        }
        if (force || !UpToDate()) Build();
        bool ok = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronSwing, PrefabPath(LightName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronSwingHeavy, PrefabPath(HeavyName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronSwingHit, PrefabPath(HitName), 10);
        if (!ok) Debug.LogWarning("[wreck-swing-iron] Не все префабы нашлись — записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(LightName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in new[] { HeavyName, HitName })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Textures");
        Texture2D links = LinkTexture();
        Texture2D chips = ChipTexture();
        Texture2D brush = BrushTexture();
        Shader wave = Shader.Find(WaveShaderName), blob = Shader.Find(BlobShaderName);

        Mesh lightWave = WaveMesh("WreckIronSwingWaveV4", LightTail, LightHead, LightCompress);
        Mesh heavyWave = WaveMesh("WreckIronSwingWaveHeavyV4", HeavyTail, HeavyHead, HeavyCompress);
        Mesh[] lightLinks = LinkStrips("WreckIronSwingLinksV4", LightTail, LightHead, LightCompress, LightLinkAt, LinkLength, LinkWidth);
        Mesh[] heavyLinks = LinkStrips("WreckIronSwingLinksHeavyV4", HeavyTail, HeavyHead, HeavyCompress, HeavyLinkAt, LinkLength, LinkWidth);

        var m = new Materials
        {
            Wave = WaveMaterial(wave, "M_WreckIronSwing_Wave", brush, LightLife, .55f, .06f, .10f, .23f, .22f, .26f, 12f),
            HeavyWave = WaveMaterial(wave, "M_WreckIronSwing_WaveHeavy", brush, HeavyLife, .55f, .07f, .14f, .30f, .30f, .34f, 11f),
            // Звено V4 — рисунок, как всё остальное: белая сердцевина прута, голубой лёд к краю, тонкий тёмный обвод.
            Links = BlobMaterial(blob, "M_WreckIronSwing_Links", links, Ink, new Color(.45f, .74f, 1f), .30f, .86f, 1.8f, .09f, .30f, 1.0f),
            Spark = BlobMaterial(blob, "M_WreckIronSwing_Spark", AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture), Ink,
                new Color(.45f, .75f, 1f), .26f, .80f, 2f, .09f, .22f, .7f),
            Burst = BlobMaterial(blob, "M_WreckIronSwing_Burst", AssetDatabase.LoadAssetAtPath<Texture2D>(SpikesTexture), Ink,
                new Color(.42f, .70f, 1f), .22f, .90f, 1.3f, .08f, .26f, .7f),
            // Сколы — тёмное железо и камень с серой гранью и тёмным обводом (кадр: почти чёрные куски).
            Chip = BlobMaterial(blob, "M_WreckIronSwing_Chip", chips, Ink, new Color(.62f, .62f, .68f), .30f, .85f, 2.5f, .10f, .30f, .55f),
            Dust = BlobMaterial(blob, "M_WreckIronSwing_Dust", AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexture),
                new Color(.30f, .22f, .15f, .35f), new Color(.86f, .80f, .72f), .18f, .92f, 1.1f, .05f, .26f, .3f),
        };
        foreach (var material in new[] { m.Wave, m.HeavyWave, m.Links, m.Spark, m.Burst, m.Chip, m.Dust })
            AssetDatabase.SaveAssetIfDirty(material);
        foreach (var mesh in new[] { lightWave, heavyWave, lightLinks[0], lightLinks[1], heavyLinks[0], heavyLinks[1] })
            AssetDatabase.SaveAssetIfDirty(mesh);

        SaveSwingPrefab(LightName, PelagVfxId.WreckIronSwing, lightWave, lightLinks, m, false);
        SaveSwingPrefab(HeavyName, PelagVfxId.WreckIronSwingHeavy, heavyWave, heavyLinks, m, true);
        SaveHitPrefab(m);

        var importer = AssetImporter.GetAtPath(PrefabPath(LightName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-swing-iron] Махи «железо» собраны на технике серии сабли: " + LightName + ", " + HeavyName + ", " + HitName + " (" + Revision + ").");
    }

    private struct Materials
    {
        public Material Wave, HeavyWave, Links, Spark, Burst, Chip, Dust;
    }

    /// <summary>Материал с нуля при сохранении ассета и GUID (как PelagSabreComboVfxSetup.Fresh).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static Material WaveMaterial(Shader shader, string name, Texture2D noise, float life,
        float headFrom, float headSeconds, float erodeFrom, float erodeTo, float fadeFrom, float fadeTo, float aspect)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_BrushTex", noise);
        material.SetColor("_Deep", Iron);
        material.SetColor("_Water", Core);
        material.SetColor("_Shallow", CoreLight);
        material.SetColor("_Foam", Rim);
        material.SetColor("_FoamShade", RimShade);
        material.SetColor("_Outline", EdgeNavy);
        material.SetColor("_EdgeBlue", EdgeBlue);
        material.SetFloat("_EdgeBluePx", 2.2f);
        // Мазки кисти (своя маска): дорожки вдоль маха ~1,3 плитки на дугу, крупный шум — для эрозии.
        material.SetVector("_BrushScale", new Vector4(1.3f, 1f, 1.6f, .6f));
        // Поперёк (кадр, снаружи внутрь): белая кромка ~12 %, голубой лёд ~18 %, яркое ядро ~28 %, внутри — плотное
        // железо, внутренний край — штрихи кисти с обводом; границы полос рвутся по тем же мазкам.
        material.SetVector("_Bands", new Vector4(.34f, .66f, .88f, .16f));
        material.SetVector("_Inner", new Vector4(.24f, .30f, .40f, .22f));
        material.SetFloat("_EdgeRag", .03f);
        material.SetFloat("_HeadFoam", .06f);
        material.SetFloat("_OutlinePx", 1.6f);
        material.SetFloat("_Aspect", aspect);
        material.SetFloat("_ErodeGain", 1.7f);
        material.SetFloat("_Streaks", .85f);
        material.SetFloat("_Glow", .12f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_HeadFrom", headFrom);
        material.SetFloat("_HeadSeconds", headSeconds);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeTo);
        material.SetFloat("_ErodeAlong", .75f);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeTo);
        material.SetFloat("_FlowSpeed", .6f);
        // Полоса идёт на высоте пояса сквозь тела задетых — к камере, как волна сабли.
        material.SetFloat("_CameraPush", 1.0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material BlobMaterial(Shader shader, string name, Texture2D mask, Color outline, Color shade,
        float cutFrom, float cutTo, float cutPower, float outlineWidth, float shadeWidth, float push)
    {
        Material material = Fresh(name, shader);
        material.SetTexture("_MainTex", mask);
        material.SetColor("_Outline", outline);
        material.SetColor("_Shade", shade);
        material.SetFloat("_CutFrom", cutFrom);
        material.SetFloat("_CutTo", cutTo);
        material.SetFloat("_CutPower", cutPower);
        material.SetFloat("_OutlineWidth", outlineWidth);
        material.SetFloat("_ShadeWidth", shadeWidth);
        material.SetFloat("_CameraPush", push);
        EditorUtility.SetDirty(material);
        return material;
    }
}
