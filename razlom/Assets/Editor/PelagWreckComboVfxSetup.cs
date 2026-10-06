using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Махи 1–2 Крушения и знак на задетом на СТЕКЕ СЕРИИ САБЛИ (06.10 вечер). Три прошлых захода (процедурная полоса на
/// земле, рисованная картинка на дуге) были одним плоским слоем — «наклейка», владелец сравнил с обычной атакой саблей.
/// Здесь слой в слой как PelagSabreComboVfxSetup (SaveWavePrefab + SaveSplashPrefab), а якорная семья отличается
/// МАТЕРИАЛОМ:
///  • волна — полумесяц пака CFXR «sword_trail 180 thick», согнутый под мах якоря (размах 150°, тело толще — сжатие
///    0,58, голова тупая и тяжёлая), на высоте головы якоря вокруг Пелага, радиус — по сектору махов Sim; основная
///    волна + эхо (сдвиг, задержка, рвётся и гаснет первым), хлопок масштаба, доворот по ходу маха; шейдер
///    «Razlom/Wreck Combo Wave» — полосы кобальт/сталь, горячая холодная кромка, грани «noise ice» вместо пузырьков,
///    жёсткие штрихи и бегущий блик, порог с чернильным обводом, распад вдоль u;
///  • с наружного края — искры (вытянуты по скорости, короткие, горячие бело-голубые) и сколы железа с тяжестью;
///  • знак на теле — вспышка-звезда «cfxr spikes impact dissolve» (кобальт, белое ядро, обвод, хлопок), конус искр
///    по ходу маха, сколы и комья земли с тяжестью; у маха 2 крупнее.
/// Мах 2 — зеркало маха 1 (контроллер поворачивает корень), крупнее и ярче. Формы — та же сборка другой краской
/// (контроллер: блок свойств и цвет частиц, PelagWreckComboLook). Звеньев цепи в волне нет.
///
/// Свои версионные ассеты (Revision в userData .meta префаба маха), сохраняются только они (SaveAssetIfDirty,
/// SaveAsPrefabAsset) — общего AssetDatabase.SaveAssets нет. Ассеты, материалы и шейдеры сабли не трогаются.
/// Рисованные махи и знак (WreckPaintedSwing*, WreckPaintedHit) и махи «железа» (WreckIronSwing*) от библиотеки
/// отвязываются (файлы на месте); выпад (WreckPaintedLunge) остаётся как был. Префабы — .Prefabs.cs.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    /// <summary>Версия сборки: любая правка ассетов — поднять.</summary>
    private const int Version = 8;
    private static readonly string Revision = "PelagWreckComboV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string FacetTexture = CfxrGraphics + "cfxr sword trail noise ice.png";
    private const string SparkTexture = CfxrGraphics + "cfxr stretch spike fade.png";
    private const string BurstTexture = CfxrGraphics + "cfxr spikes impact dissolve.png";
    private const string DebrisTexture = CfxrGraphics + "cfxr debris unlit 3x3.png";
    private const string WaveShaderName = "Razlom/Wreck Combo Wave";
    private const string BitShaderName = "Razlom/Wreck Combo Bit";

    public const string SwingName = "VFX_Pelag_WreckComboSwing";
    public const string HeavyName = "VFX_Pelag_WreckComboSwingHeavy";
    public const string HitName = "VFX_Pelag_WreckComboHit";

    /// <summary>Размах полумесяца, град: сектор Sim ±72°, тупые концы чуть выходят за него.</summary>
    internal const float SpanDegrees = 150f;

    /// <summary>Жизнь волны, с: мах 2 бьёт через ~0,2 с — мах 1 к нему уже рассыпается; мах 2 держится дольше.</summary>
    internal const float SwingLife = .32f, HeavyLife = .38f;

    // Палитра базы (кобальт и сталь) — цвета материалов; формы перекрашивает контроллер.
    private static readonly Color Deep = Hex(0x0A1F4A);
    private static readonly Color Mid = Hex(0x2E6FE0);
    private static readonly Color Light = Hex(0x8FC4FF);
    private static readonly Color Hot = new Color(.92f, 1.06f, 1.40f, 1f);
    private static readonly Color HotShade = new Color(.50f, .72f, 1.05f, 1f);
    private static readonly Color Ink = new Color(.024f, .043f, .133f, .94f);
    internal static readonly Color SparkHot = new Color(1.12f, 1.2f, 1.32f, 1f);
    internal static readonly Color SparkBlue = new Color(.56f, .80f, 1.10f, 1f);
    // Цвета частиц — вершинные, шейдер их не переводит из sRGB: задаются сразу в линейном пространстве (V1 с
    // sRGB-числами вышел светлым: сколы — голубая галька, вспышка — бледная).
    internal static readonly Color BurstCobalt = new Color(.20f, .45f, 1.0f, 1f);
    internal static readonly Color IronFace = new Color(.06f, .075f, .11f, 1f);
    internal static readonly Color IronSteel = new Color(.10f, .13f, .20f, 1f);
    internal static readonly Color Clod = new Color(.12f, .07f, .035f, 1f);
    internal static readonly Color ClodLight = new Color(.17f, .10f, .05f, 1f);

    private static Color Hex(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);
    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Install(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: махи на стеке сабли — подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: махи на стеке сабли — пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(WaveShaderName) == null || Shader.Find(BitShaderName) == null || ThickMesh() == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture) == null)
        {
            Debug.LogWarning("[wreck-combo] Нет шейдеров «Wreck Combo», меша или текстур CFXR — махи не собраны.");
            return;
        }
        if (force || !UpToDate()) Build();
        bool ok = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckComboSwing, PrefabPath(SwingName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckComboSwingHeavy, PrefabPath(HeavyName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckComboHit, PrefabPath(HitName), 10);
        if (!ok) Debug.LogWarning("[wreck-combo] Не все префабы нашлись — записи библиотеки не созданы.");
        // Махи и знак рисует только этот стек; выпад (WreckPaintedLunge) не трогается.
        Unbind(library, PelagVfxId.WreckPaintedSwing, PelagVfxId.WreckPaintedSwingHeavy, PelagVfxId.WreckPaintedHit,
            PelagVfxId.WreckIronSwing, PelagVfxId.WreckIronSwingHeavy, PelagVfxId.WreckIronSwingHit);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Снять записи прежних махов (их префабы и код остаются на месте).</summary>
    internal static void Unbind(AbilityVfxLibrary library, params PelagVfxId[] ids)
    {
        int before = library.Entries.Length;
        library.Entries = System.Array.FindAll(library.Entries, e => System.Array.IndexOf(ids, e.Id) < 0);
        if (library.Entries.Length != before) EditorUtility.SetDirty(library);
    }

    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(SwingName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in new[] { HeavyName, HitName })
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
        Shader waveShader = Shader.Find(WaveShaderName);
        Shader bitShader = Shader.Find(BitShaderName);
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);

        Mesh wave = WaveMesh("WreckComboWave", ThickMesh(), SpanDegrees, .48f);

        Material main = WaveMaterial(waveShader, "M_WreckCombo_Wave", facets, SwingLife, .40f, .10f, .09f, .27f, .21f, .31f);
        Material echo = WaveMaterial(waveShader, "M_WreckCombo_WaveEcho", facets, SwingLife, .36f, .11f, .06f, .22f, .17f, .27f);
        Material heavy = WaveMaterial(waveShader, "M_WreckCombo_Heavy", facets, HeavyLife, .40f, .11f, .10f, .29f, .24f, .35f);
        Material heavyEcho = WaveMaterial(waveShader, "M_WreckCombo_HeavyEcho", facets, HeavyLife, .36f, .12f, .07f, .25f, .20f, .31f);
        foreach (var e in new[] { echo, heavyEcho })
        {
            // Эхо — глубже и темнее, рвётся сильнее, без блика: второй, тяжёлый «слой металла» под основной волной.
            e.SetFloat("_EdgeRag", .24f);
            e.SetVector("_Bands", new Vector4(.32f, .70f, .88f, .14f));
            e.SetFloat("_Sheen", 0f);
            e.SetFloat("_Streaks", .45f);
            e.SetFloat("_Glow", .12f);
        }
        // Мах 2 — ярче: шире горячая кромка, сильнее свечение и блик.
        heavy.SetVector("_Bands", new Vector4(.22f, .52f, .70f, .12f));
        heavy.SetFloat("_HeadHot", .20f);
        heavy.SetFloat("_Glow", .42f);
        heavy.SetFloat("_Sheen", .9f);
        // Сдвиг к камере, м: волна на высоте груди идёт сквозь тела задетых (как у сабли).
        main.SetFloat("_CameraPush", 1.0f);
        heavy.SetFloat("_CameraPush", 1.0f);
        echo.SetFloat("_CameraPush", .9f);
        heavyEcho.SetFloat("_CameraPush", .9f);

        Material spark = BitMaterial(bitShader, "M_WreckCombo_Spark", SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .34f);
        Material burst = BitMaterial(bitShader, "M_WreckCombo_Burst", BurstTexture, 1.5f, .06f, .92f, 1.3f, .06f, .16f, .30f);
        Material chip = BitMaterial(bitShader, "M_WreckCombo_Chip", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        chip.SetFloat("_ShapeAlpha", 1f);
        // Комья земли — тот же лист, скос бурый, а не стальной.
        Material clod = BitMaterial(bitShader, "M_WreckCombo_Clod", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        clod.SetFloat("_ShapeAlpha", 1f);
        clod.SetColor("_BevelAdd", new Color(.46f, .34f, .22f, 1f));
        foreach (var m in new[] { spark, burst, chip, clod }) m.SetFloat("_CameraPush", .6f);

        foreach (var m in new[] { main, echo, heavy, heavyEcho, spark, burst, chip, clod })
        {
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }
        AssetDatabase.SaveAssetIfDirty(wave);

        SaveSwingPrefab(SwingName, PelagVfxId.WreckComboSwing, wave, main, echo, spark, chip, SwingLife, false);
        SaveSwingPrefab(HeavyName, PelagVfxId.WreckComboSwingHeavy, wave, heavy, heavyEcho, spark, chip, HeavyLife, true);
        SaveHitPrefab(burst, spark, chip, clod);

        var importer = AssetImporter.GetAtPath(PrefabPath(SwingName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-combo] Махи Крушения на стеке сабли собраны: " + SwingName + ", " + HeavyName + ", " + HitName
            + " (" + Revision + ").");
    }
}
