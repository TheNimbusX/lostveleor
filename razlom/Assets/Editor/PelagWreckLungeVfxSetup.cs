using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Выпад Крушения v4 «просто» (06.10, сборка V1) — строго по целевому кадру владельца
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-lunge-simple.png: ОДНА круглая яркая вспышка в точке
/// удара якоря (белое рваное ядро, голубые шипы, тёмный обвод — рисованные звёзды пака «cfxr spikes impact dissolve» на
/// шейдере Blob серии сабли) и ОДНА прямая яркая линия по полосе Sim (своя лента на «Razlom/Wreck Iron Line»: рваное
/// белое ядро, голубое свечение с шипами, тонкий тёмный обвод, острая голова на фронте вала) с несколькими крупными
/// комьями земли вдоль неё. Корона плит, оттиски звеньев, рваная земля и мелкий мусор «железа» v7 из базового выпада
/// убраны (их префаб и код на месте — формы переделаются позже).
///
/// Префаб VFX_Pelag_WreckLunge: «Line» — MeshFilter+MeshRenderer (ленту пишет вид), «BurstBlue»/«BurstWhite» —
/// звёзды-билборды (выброс при старте), «Chunks» и «Light» — копии из VFX_Pelag_WreckIron_Slam (свои ассеты: меш
/// куска и его материал, холодный точечный свет). Ассеты свои и версионные (Revision в userData .meta префаба);
/// сохраняются только свои (SaveAssetIfDirty, SaveAsPrefabAsset) — общего AssetDatabase.SaveAssets нет.
/// </summary>
public static class PelagWreckLungeVfxSetup
{
    /// <summary>
    /// Версия сборки. Любая правка ассетов — поднять. V2 (06.10 вечер) — владелец: «мыло и линейно»: линия — волна
    /// (толстая у удара, сужается вперёд, вздутия, гуляющая ось, редкие разные шипы), звёзды меньше и острее.
    /// </summary>
    private const int Version = 2;
    private static readonly string Revision = "PelagWreckLungeV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    // Чужое — только чтение.
    private const string SpikesTexture = CfxrGraphics + "cfxr spikes impact dissolve.png";
    private const string NoiseTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string LineShaderName = "Razlom/Wreck Iron Line";
    private const string BlobShaderName = "Razlom/Sabre Foam Blob";

    public const string LungeName = "VFX_Pelag_WreckLunge";
    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";

    // Палитра кадра: белое ядро чуть за порогом блума, голубой лёд, яркий холодный голубой, тень края, тёмный обвод.
    private static readonly Color Core = new Color(1.22f, 1.28f, 1.34f);
    private static readonly Color IceLight = new Color(.58f, .86f, 1.08f);
    private static readonly Color Glow = new Color(.25f, .55f, 1.00f);
    private static readonly Color Shade = new Color(.13f, .30f, .80f);
    private static readonly Color Ink = new Color(.04f, .06f, .16f, .95f);

    // 06.10 вечер: отвязано — махи и выпад рисует PelagWreckPaintedVfxSetup (рисованные текстуры; владелец отверг
    // процедурные V3/V4). Автоподключения нет, файлы и меню остаются; записи библиотеки снимает новая сборка.

    [MenuItem("Разлом/Pelag VFX/Крушение: выпад «просто» (вспышка и линия) — подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: выпад «просто» (вспышка и линия) — пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(LineShaderName) == null || Shader.Find(BlobShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SpikesTexture) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(PelagWreckIronVfxSetup.SlamName)) == null)
        {
            Debug.LogWarning("[wreck-lunge] Нет шейдеров, листов CFXR или префаба удара «железа» — выпад не собран.");
            return;
        }
        if (force || !UpToDate()) Build();
        if (!PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckLunge, PrefabPath(LungeName), 2))
            Debug.LogWarning("[wreck-lunge] Префаб не нашёлся — запись библиотеки не создана.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(LungeName));
        return importer != null && importer.userData == Revision;
    }

    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        var spikes = AssetDatabase.LoadAssetAtPath<Texture2D>(SpikesTexture);

        Material line = Fresh("M_WreckLunge_Line", Shader.Find(LineShaderName));
        line.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture));
        line.SetColor("_Core", Core);
        line.SetColor("_Light", IceLight);
        line.SetColor("_Glow", Glow);
        line.SetColor("_Shade", Shade);
        line.SetColor("_Outline", Ink);
        // Доли полуширины ленты (1,15 м) у точки удара: рваное ядро ~0,07 м, лёд ~0,16 м, тело ~0,35 м, шипы — ещё до ~0,37 м
        // (кадр: у вспышки линия ~0,75 м с шипами до ~1,3 м); к концу полосы ×0,38, вздутия ±30 %, ось гуляет ±0,16.
        line.SetVector("_Widths", new Vector4(.06f, .14f, .30f, .32f));
        line.SetVector("_Shape", new Vector4(1f, .38f, .30f, .16f));
        line.SetFloat("_SpikeFreq", 1.4f);
        line.SetFloat("_TipMeters", 1.1f);
        line.SetFloat("_StartMeters", .45f);
        line.SetFloat("_OutlinePx", 2.2f);
        line.SetFloat("_HoldSeconds", .20f);
        line.SetFloat("_ErodeSeconds", .35f);
        line.SetFloat("_FadeSeconds", .12f);
        line.SetFloat("_GlowAdd", .25f);
        EditorUtility.SetDirty(line);
        Material blue = Blob("M_WreckLunge_BurstBlue", spikes, Ink, new Color(.50f, .62f, .92f), .20f, .92f, 1.4f, .06f, .24f);
        Material white = Blob("M_WreckLunge_BurstWhite", spikes, new Color(.25f, .55f, 1f, 1f), new Color(.70f, .88f, 1f), .32f, .95f, 1.2f, .05f, .16f);
        foreach (var material in new[] { line, blue, white }) AssetDatabase.SaveAssetIfDirty(material);

        var slam = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(PelagWreckIronVfxSetup.SlamName));
        var root = new GameObject(LungeName);
        try
        {
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WreckLunge;
            element.DefaultLifetime = 1.4f;
            element.AuthoredRadius = 1f;

            var lineHost = new GameObject("Line");
            lineHost.transform.SetParent(root.transform, false);
            lineHost.AddComponent<MeshFilter>();
            var lineRenderer = lineHost.AddComponent<MeshRenderer>();
            lineRenderer.sharedMaterial = line;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.lightProbeUsage = LightProbeUsage.Off;
            lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Вспышка: голубая звезда шипов на круг удара (Ø ≈ 2,4 м) и белое рваное ядро поверх, обе раскрываются за
            // два кадра и втягивают лучи (порог Blob по возрасту).
            // V2: кадр — вспышка ~1,9 м по шипам, белое ядро рваное и меньше (V1 — белый блин 2,1 м).
            Star(root, "BurstBlue", blue, 2.3f, .38f, new Color(.30f, .60f, 1.05f, 1f), 0f);
            Star(root, "BurstWhite", white, 1.45f, .28f, new Color(1.20f, 1.26f, 1.32f, 1f), 1.1f);

            foreach (string child in new[] { "Chunks", "Light" })
            {
                Transform source = slam.transform.Find(child);
                if (source == null) continue;
                GameObject copy = Object.Instantiate(source.gameObject, root.transform, false);
                copy.name = child;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(LungeName));
        }
        finally { Object.DestroyImmediate(root); }

        var importer = AssetImporter.GetAtPath(PrefabPath(LungeName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-lunge] Выпад «просто» собран: " + LungeName + " (" + Revision + ").");
    }

    private static Material Blob(string name, Texture2D mask, Color outline, Color shade,
        float cutFrom, float cutTo, float cutPower, float outlineWidth, float shadeWidth)
    {
        Material material = Fresh(name, Shader.Find(BlobShaderName));
        material.SetTexture("_MainTex", mask);
        material.SetColor("_Outline", outline);
        material.SetColor("_Shade", shade);
        material.SetFloat("_CutFrom", cutFrom);
        material.SetFloat("_CutTo", cutTo);
        material.SetFloat("_CutPower", cutPower);
        material.SetFloat("_OutlineWidth", outlineWidth);
        material.SetFloat("_ShadeWidth", shadeWidth);
        material.SetFloat("_CameraPush", 1.2f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Звезда-билборд: одна частица в корне, случайный поворот, раскрытие и лёгкий откат размера.</summary>
    private static void Star(GameObject root, string name, Material material, float size, float life, Color color, float fudge)
    {
        ParticleSystem star = PelagWhirlwindVfxSetup.NewParticles(root, name, 1, life, life, 0f, 0f, size, size);
        var main = star.main;
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = star.shape; shape.enabled = false;
        var grow = star.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .45f), new Keyframe(.14f, 1.06f), new Keyframe(.4f, 1f), new Keyframe(1f, .92f)));
        var renderer = star.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -fudge;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
    }
}
