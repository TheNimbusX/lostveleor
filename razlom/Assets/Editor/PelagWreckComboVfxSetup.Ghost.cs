using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение · ПРИЗРАЧНЫЙ ЯКОРЬ (форма 13; 07.10; целевой кадр
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-ghost-anchor.png). Стек слоёв, как у махов и выпада,
/// главное — объём и СВЕЧЕНИЕ (v4, 07.10 после отзыва владельца «обводка херовая»: чёрного обвода нет ни у якоря, ни у
/// кольца):
///  • VFX_Pelag_WreckGhostAnchor — НАСТОЯЩИЙ 3D якорь Пелага (меш Tripo «Pelag_AnchorHead_Tripo»: звенья, ствол, лапы со
///    стрелами — силуэт целевого кадра) и призрачная цепь витком вокруг ствола (звенья Pelag_ChainLink) — один свой меш
///    (низ в 0, высота 1, сглаженные нормали) на «Razlom/Wreck Ghost Anchor» (лавандовое стекло с гранями, полосы
///    фиолетового текут вниз, светлая кромка, белое HDR у края) + ореол «… Glow» двумя ступенями фиолетового светом;
///    следы падения, осколки и искорки распада вверх (куски выпускает контроллер);
///  • VFX_Pelag_WreckGhostLand — удар: кольцо «Razlom/Wreck Ghost Ring» (тонкая рваная линия, шипы разной длины и наклона,
///    маска на весь круг без повторов, свет земли внутри) и эхо, стоячая полузвезда от земли, звезда, лучи вверх, трещины
///    с фиолетовым светом, тёмный кратер, тёмные камни (десятки, освещённые — фиолетовым светом удара), комья, сколы-
///    кристаллы, искры, немного пыли, фиолетовый точечный свет. Контур искр и осколков — тёмно-фиолетовый, не чёрный.
/// Сроки и размеры — Sim (PelagWreckGhostLook). Свои ассеты (ревизия в userData префаба якоря), сохраняются только они;
/// материалы выпада и махов берутся как есть (кратер, пыль, комья), сабля не трогается. Меши — .GhostMesh.cs, слои —
/// .GhostPrefab.cs. Прежние M_WreckGhost_AnchorInk / M_WreckGhost_Ring(Echo) и шейдер «… Ink» больше не используются.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int GhostVersion = 6;
    private static readonly string GhostRevision = "PelagWreckGhostV" + GhostVersion;
    public const string GhostAnchorName = "VFX_Pelag_WreckGhostAnchor";
    public const string GhostLandName = "VFX_Pelag_WreckGhostLand";
    private const string GhostShaderName = "Razlom/Wreck Ghost Anchor";
    private const string GhostGlowShaderName = "Razlom/Wreck Ghost Anchor Glow";
    private const string GhostRingShaderName = "Razlom/Wreck Ghost Ring";
    private const string GhostModel = "Assets/Resources/Weapons/Pelag/AnchorDemo/Pelag_AnchorHead_Tripo.fbx";
    private const string GhostLinkMesh = GeometryFolder + "/Pelag_ChainLink_Centered.asset";
    private const string GhostRingTexture = Root + "/WreckGhost_RingShock.png";

    [InitializeOnLoadMethod]
    private static void ScheduleGhost()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallGhost(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: Призрачный якорь — подключить")]
    public static void InstallGhost() => InstallGhost(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: Призрачный якорь — пересобрать")]
    public static void RebuildGhost() => InstallGhost(true);

    public static void InstallGhost(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string shader in new[] { GhostShaderName, GhostGlowShaderName, GhostRingShaderName, BitShaderName })
            if (Shader.Find(shader) == null)
            {
                Debug.LogWarning("[wreck-ghost] Нет шейдера «" + shader + "» — Призрачный якорь не собран.");
                return;
            }
        foreach (string path in new[] { GhostRingTexture, FacetTexture, DebrisTexture, SparkTexture, BurstTexture, HalfTexture, GlowCrackTexture, PaletteTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[wreck-ghost] Нет текстуры " + path + " — Призрачный якорь не собран.");
                return;
            }
        if (GhostSourceMesh() == null || AssetDatabase.LoadAssetAtPath<Mesh>(GhostLinkMesh) == null)
        {
            Debug.LogWarning("[wreck-ghost] Нет меша якоря " + GhostModel + " или звена " + GhostLinkMesh + " — Призрачный якорь не собран.");
            return;
        }
        // Кратер, пыль, камни — материалы и меши выпада; комья — махов.
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckLunge_Rock")) == null) InstallLunge(false);
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckLunge_Rock")) == null) return;
        if (force || !GhostUpToDate()) BuildGhost();
        bool ok = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckGhostAnchor, PrefabPath(GhostAnchorName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckGhostLand, PrefabPath(GhostLandName), 2);
        if (!ok) Debug.LogWarning("[wreck-ghost] Префабы не нашлись — записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool GhostUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(GhostAnchorName));
        return importer != null && importer.userData == GhostRevision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(GhostLandName)) != null;
    }

    private static void BuildGhost()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        GhostMaskImport(GhostRingTexture);
        Shader bitShader = Shader.Find(BitShaderName);
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);

        // ---- тело: лавандовое стекло, грани, полосы, светлая кромка (цвета — линейные, HDR у белого).
        Material body = Fresh("M_WreckGhost_Anchor", Shader.Find(GhostShaderName));
        body.SetTexture("_FacetTex", facets);
        // Как на целевом кадре: внутри — средний фиолетовый (тень #8E7BFF, свет #B3A6FF), светлые грани почти белые,
        // полосы глубже #7462EE, кромка почти белая, у края — белое HDR.
        // Раунд 1 в игре: тёплый грейд арены уводит белое в кремово-розовое, фиолетовое — в пурпур; тело сливалось со
        // светом земли. Поэтому: тело светлее и плотнее земли, белые и фиолетовые — холоднее (меньше красного).
        body.SetColor("_Inner", Lin(0x9C96FF, 1f));
        body.SetColor("_Core", Lin(0xC8C4FF, 1f));
        body.SetColor("_Band", Lin(0x6F66F0, 1f));
        body.SetVector("_FacetTones", new Vector4(.86f, 1.42f, .64f, .28f));
        body.SetColor("_Rim", new Color(.80f, .78f, 1.25f, 1f));
        // Раунд 3: HDR-белое (>1.3) тонмаппинг и тёплый грейд арены сводят в кремовое — белое держим у 1, с синим запасом.
        body.SetColor("_Hot", new Color(1.1f, 1.1f, 1.65f, 1f));
        body.SetFloat("_RimFrom", .40f);
        body.SetFloat("_HotFrom", .58f);
        body.SetFloat("_InnerAlpha", .90f);
        body.SetFloat("_FacetLines", .85f);
        // ---- ореол: внутренняя ступень ярче и уже, внешняя шире и тусклее; порядок — внутренняя первой (трафарет).
        Material glowIn = GhostGlow("M_WreckGhost_GlowInner", facets, new Color(.44f, .42f, 1.30f, 1f), .72f, 9f, 100f, 3037);
        Material glowOut = GhostGlow("M_WreckGhost_GlowOuter", facets, new Color(.17f, .15f, .74f, 1f), .42f, 20f, 50f, 3038);

        // ---- кольцо и эхо (эхо — тоньше, без света земли, белое ядро у́же).
        Shader ringShader = Shader.Find(GhostRingShaderName);
        Material ring = Fresh("M_WreckGhost_RingShock", ringShader);
        ring.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GhostRingTexture));
        ring.SetColor("_Pale", new Color(.62f, .60f, 1.08f, 1f));
        ring.SetColor("_Violet", new Color(.24f, .19f, 1.05f, 1f));
        ring.SetColor("_White", new Color(.92f, .92f, 1.40f, 1f));
        ring.SetColor("_WashColor", new Color(.24f, .22f, .95f, 1f));
        ring.SetFloat("_CutPower", 1.3f);
        Material ringEcho = Fresh("M_WreckGhost_RingShockEcho", ringShader);
        ringEcho.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GhostRingTexture));
        ringEcho.SetColor("_Pale", Lin(PelagWreckGhostLook.BandHex, 1.15f));
        ringEcho.SetColor("_Violet", Lin(PelagWreckGhostLook.ShadowHex, 1f));
        ringEcho.SetColor("_White", Lin(PelagWreckGhostLook.CoreHex, 1.2f));
        ringEcho.SetFloat("_CutFrom", .14f);
        ringEcho.SetFloat("_CoreW", .55f);
        ringEcho.SetColor("_WashColor", Color.black);
        ringEcho.SetVector("_GlowSteps", new Vector4(.70f, .40f, .30f, .15f));

        // ---- искры, лучи, всплеск, звезда, трещины: материалы стека, контур — тёмно-фиолетовый вместо чернил.
        Material spark = GhostBit(bitShader, "M_WreckGhost_Spark", SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .34f);
        Material half = GhostBit(bitShader, "M_WreckGhost_Half", HalfTexture, 1f, .08f, .97f, 1.5f, .045f, .12f, .55f);
        Material star = GhostBit(bitShader, "M_WreckGhost_Star", BurstTexture, 1.15f, .06f, .92f, 1.3f, .055f, .15f, .45f);
        Material crack = GhostBit(bitShader, "M_WreckGhost_Crack", GlowCrackTexture, 1.6f, .30f, .95f, 1f, .035f, .08f, .26f);
        foreach (var m in new[] { half, star }) m.SetFloat("_CameraPush", .25f);
        crack.SetFloat("_CameraPush", .05f);
        // Сколы-кристаллы: силуэт из альфы, скос светлее и холоднее (стекло, не железо), обвод тёмно-фиолетовый.
        Material shard = GhostBit(bitShader, "M_WreckGhost_Shard", DebrisTexture, 1f, 0f, 0f, 1f, 0f, 0f, 2f);
        shard.SetFloat("_ShapeAlpha", 1f);
        shard.SetFloat("_BevelMul", 1.25f);
        shard.SetColor("_BevelAdd", new Color(.42f, .40f, .62f, 1f));
        shard.SetFloat("_CameraPush", .3f);
        Material rock = GhostRockMaterial("M_WreckGhost_Rock");
        foreach (var m in new[] { body, glowIn, glowOut, ring, ringEcho, spark, half, star, crack, shard, rock })
        {
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }

        Mesh anchor = GhostAnchorMesh();
        Mesh disc = GhostDiscMesh();
        Mesh[] rocks = GhostRocks();
        if (rocks.Length == 0) { Debug.LogWarning("[wreck-ghost] Нет камней выпада (WreckLungeRock*) — не собрано."); return; }

        var g = new GhostMaterials
        {
            Body = body, GlowIn = glowIn, GlowOut = glowOut, Ring = ring, RingEcho = ringEcho, Shard = shard, Spark = spark,
            Half = half, Star = star, Crack = crack, Rock = rock, AnchorMesh = anchor, Disc = disc, Rocks = rocks,
            Crater = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckLunge_Crater")),
            Dust = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckLunge_Dust")),
            Clod = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("M_WreckCombo_Clod"))
        };
        SaveGhostAnchorPrefab(g);
        SaveGhostLandPrefab(g);

        var importer = AssetImporter.GetAtPath(PrefabPath(GhostAnchorName));
        if (importer != null && importer.userData != GhostRevision)
        {
            importer.userData = GhostRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-ghost] Призрачный якорь собран: " + GhostAnchorName + ", " + GhostLandName + " (" + GhostRevision + ").");
    }

    private struct GhostMaterials
    {
        public Material Body, GlowIn, GlowOut, Ring, RingEcho, Shard, Spark, Half, Star, Crack, Rock, Crater, Dust, Clod;
        public Mesh AnchorMesh, Disc;
        public Mesh[] Rocks;
    }

    /// <summary>sRGB-цвет формы → линейный (материалы шейдеров без перевода), с множителем яркости.</summary>
    private static Color Lin(int hex, float gain)
    {
        Color c = Hex(hex).linear * gain;
        c.a = 1f;
        return c;
    }

    private static Material GhostGlow(string name, Texture2D facets, Color glow, float alpha, float widthPx, float stencilRef, int queue)
    {
        Material m = Fresh(name, Shader.Find(GhostGlowShaderName));
        m.SetTexture("_FacetTex", facets);
        m.SetColor("_Glow", glow);
        m.SetFloat("_Alpha", alpha);
        m.SetFloat("_WidthPx", widthPx);
        m.SetFloat("_StencilRef", stencilRef);
        m.renderQueue = queue;
        return m;
    }

    /// <summary>Материал стека (Bit) в краске призрака: контур тёмно-фиолетовый, тень у края — фиолетовая, ядро — белое HDR.</summary>
    private static Material GhostBit(Shader shader, string name, string texture, float gain,
        float cutFrom, float cutTo, float cutPower, float outline, float shade, float core)
    {
        Material m = BitMaterial(shader, name, texture, gain, cutFrom, cutTo, cutPower, outline, shade, core);
        Color contour = Lin(PelagWreckGhostLook.ContourHex, 1f);
        contour.a = .9f;
        m.SetColor("_Outline", contour);
        m.SetColor("_Shade", new Color(.82f, .78f, 1f, 1f));
        m.SetColor("_Core", new Color(1.0f, 1.0f, 1.45f, 1f));
        return m;
    }

    /// <summary>Тёмные камни: освещённые частицы URP (солнце арены + фиолетовый свет удара), чуть фиолетовой подсветки.</summary>
    private static Material GhostRockMaterial(string name)
    {
        Material m = RockMaterial(name);
        m.SetColor("_BaseColor", new Color(.66f, .64f, .72f, 1f));
        if (m.HasProperty("_EmissionColor"))
        {
            m.SetColor("_EmissionColor", new Color(.018f, .014f, .05f, 1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    /// <summary>Маска кольца — данные, не цвет: линейная, по кругу повтор (шов u 0/1), по радиусу без повтора, без сжатия.</summary>
    private static void GhostMaskImport(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        if (!importer.sRGBTexture && importer.wrapModeV == TextureWrapMode.Clamp && importer.wrapModeU == TextureWrapMode.Repeat
            && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.maxTextureSize >= 2048) return;
        importer.sRGBTexture = false;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }
}
