using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение · ДЕВЯТЫЙ ВАЛ (форма 12; 07.10; целевой кадр
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-ninthwave.png). Стек слоёв, как у выпада базы, а не
/// один плоский слой (уроки 06.10): главное — СТОЯЧИЙ веер гребней с шипами (объём к камере), земля — вторым слоем.
///  • VFX_Pelag_WreckNinthLunge — добавка к выпаду базы при зарядах: пять лент (центр, эхо, второе эхо у двух зарядов,
///    два края полосы — наклонены наружу) на шейдере «Razlom/Wreck Ninth Crest» (шипы, свет внутри вала, тонкий
///    тёмно-индиговый контур — не чёрный); веер-полузвезда у точки удара по ширине полосы Sim; шипы-звёзды и светящиеся
///    трещины по земле (свет только в вале и трещинах); крупные освещённые камни (Ultimate Nature «Standard Rock»,
///    без GPU-инстансинга — с ним рисуются 2–3 меша из десятков); искры с краёв; короткий индиговый свет у фронта.
///  • VFX_Pelag_WreckNinthCharge — заряд на махе: вспышка-звезда и кольцо с лучами на голове якоря, искры.
///  • VFX_Pelag_WreckNinthGlow — голова якоря между зарядом и выпадом: ореол с лучами двумя ступенями (позади головы),
///    искорки за головой.
/// Сроки и размеры — Sim (PelagWreckNinthLook). Свои ассеты (ревизия в userData префаба выпада), сохраняются только
/// они; выпад базы, махи и сабля не трогаются (их материалы искр и камней берутся как есть). Префабы — .NinthPrefab.cs.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private const int NinthVersion = 9;
    private static readonly string NinthRevision = "PelagWreckNinthV" + NinthVersion;
    public const string NinthLungeName = "VFX_Pelag_WreckNinthLunge";
    public const string NinthChargeName = "VFX_Pelag_WreckNinthCharge";
    public const string NinthGlowName = "VFX_Pelag_WreckNinthGlow";
    private const string NinthCrestShaderName = "Razlom/Wreck Ninth Crest";
    private const string NinthGroundShaderName = "Razlom/Wreck Ninth Ground";
    private const string AuraTexture = CfxrGraphics + "cfxr aura rays.png";
    private const string NinthRockModels = "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/Forest/Models/UNS_Standard_Rock_0";

    [InitializeOnLoadMethod]
    private static void ScheduleNinth()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) InstallNinth(false); };
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: Девятый вал — подключить")]
    public static void InstallNinth() => InstallNinth(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: Девятый вал — пересобрать")]
    public static void RebuildNinth() => InstallNinth(true);

    public static void InstallNinth(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        foreach (string shader in new[] { NinthCrestShaderName, NinthGroundShaderName, BitShaderName })
            if (Shader.Find(shader) == null)
            {
                Debug.LogWarning("[wreck-ninth] Нет шейдера «" + shader + "» — Девятый вал не собран.");
                return;
            }
        foreach (string path in new[] { AuraTexture, FacetTexture, SparkTexture, BurstTexture, HalfTexture, GlowCrackTexture, LaneCrackTexture, PaletteTexture })
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                Debug.LogWarning("[wreck-ninth] Нет текстуры " + path + " — Девятый вал не собран.");
                return;
            }
        if (force || !NinthUpToDate()) BuildNinth();
        bool ok = PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckNinthLunge, PrefabPath(NinthLungeName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckNinthCharge, PrefabPath(NinthChargeName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckNinthGlow, PrefabPath(NinthGlowName), 1);
        if (!ok) Debug.LogWarning("[wreck-ninth] Префабы не нашлись — записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool NinthUpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(NinthLungeName));
        return importer != null && importer.userData == NinthRevision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(NinthChargeName)) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(NinthGlowName)) != null;
    }

    private struct NinthMaterials
    {
        public Material Ground, Crest, Echo, Echo2, Flank, GroundStar, Crack, Half, Spark, Flare, Ring, AuraIn, AuraOut, Rock;
        public Mesh[] Rocks;
    }

    private static void BuildNinth()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var facets = AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexture);
        Shader crestShader = Shader.Find(NinthCrestShaderName);
        Shader bitShader = Shader.Find(BitShaderName);
        var m = new NinthMaterials();

        // ---- гребни: центр, эхо (глубже, без блика, рвётся раньше), второе эхо (ещё глубже), края (ниже тело — шипы крупнее).
        m.Crest = NinthCrest(crestShader, "M_WreckNinth_Crest", facets);
        m.Echo = NinthCrest(crestShader, "M_WreckNinth_CrestEcho", facets);
        m.Echo.SetVector("_Bands", new Vector4(.34f, .74f, .90f, 0f));
        m.Echo.SetFloat("_Sheen", 0f);
        m.Echo.SetFloat("_Streaks", .35f);
        m.Echo.SetFloat("_Glow", .14f);
        m.Echo.SetFloat("_ErodeFrom", .11f);
        m.Echo.SetFloat("_ErodeTo", .32f);
        m.Echo.SetFloat("_FadeFrom", .27f);
        m.Echo.SetFloat("_FadeTo", .38f);
        m.Echo.SetFloat("_CameraPush", .08f);
        m.Echo2 = NinthCrest(crestShader, "M_WreckNinth_CrestEcho2", facets);
        m.Echo2.SetColor("_Light", Hex(PelagWreckNinthLook.IndigoHex));
        m.Echo2.SetVector("_Bands", new Vector4(.42f, .86f, .96f, 0f));
        m.Echo2.SetFloat("_Sheen", 0f);
        m.Echo2.SetFloat("_Streaks", .25f);
        m.Echo2.SetFloat("_Glow", .08f);
        m.Echo2.SetVector("_Spikes", new Vector4(7f, .12f, .44f, .25f));
        m.Echo2.SetFloat("_ErodeFrom", .09f);
        m.Echo2.SetFloat("_ErodeTo", .28f);
        m.Echo2.SetFloat("_FadeFrom", .23f);
        m.Echo2.SetFloat("_FadeTo", .34f);
        m.Echo2.SetFloat("_CameraPush", 0f);
        m.Flank = NinthCrest(crestShader, "M_WreckNinth_Flank", facets);
        m.Flank.SetVector("_Body", new Vector4(.30f, .14f, 0f, 0f));
        m.Flank.SetVector("_Spikes", new Vector4(7f, .18f, .56f, .22f));
        m.Flank.SetVector("_Bands", new Vector4(.32f, .74f, .86f, 0f));
        m.Flank.SetFloat("_SpikeLean", .09f);
        m.Flank.SetFloat("_Sheen", .25f);

        // ---- вал по земле: шипастый силуэт, середина бело-фиолетовая, тело индиго, контур тёмно-индиговый.
        m.Ground = Fresh("M_WreckNinth_Ground", Shader.Find(NinthGroundShaderName));
        m.Ground.SetTexture("_FacetTex", facets);
        m.Ground.SetColor("_Deep", Hex(0x2A1C9C));
        m.Ground.SetColor("_Mid", Hex(PelagWreckNinthLook.IndigoHex));
        m.Ground.SetColor("_Light", Hex(PelagWreckNinthLook.LightHex));
        m.Ground.SetColor("_Hot", new Color(.97f, .95f, 1.22f, 1f));
        Color groundContour = Hex(PelagWreckNinthLook.ContourHex);
        groundContour.a = .9f;
        m.Ground.SetColor("_Outline", groundContour);
        // Проба 3: тело узкое, шипы редкими иглами, светлое и белое — на всё тело. Шипы гуще и полнее (как на целевом
        // кадре), белое — тонкой серединой, светлый фиолетовый — до половины тела.
        m.Ground.SetVector("_Body", new Vector4(.42f, .10f, 0f, 0f));
        m.Ground.SetVector("_Spikes", new Vector4(12f, .24f, .62f, .26f));
        m.Ground.SetVector("_SmallSpikes", new Vector4(26f, .08f, .24f, .5f));
        m.Ground.SetFloat("_SpikeSharp", 1.3f);
        m.Ground.SetVector("_Core", new Vector4(.07f, .17f, .80f, 0f));
        EditorUtility.SetDirty(m.Ground);

        // ---- по земле и у удара: шипы-звёзды, светящиеся трещины, веер-полузвезда; искры; контур — тёмное индиго.
        // Шипы по земле живут дольше хлопка (порог растёт медленно, степень 2) — вал читается шириной полосы, а не точкой.
        m.GroundStar = NinthBit(bitShader, "M_WreckNinth_GroundStar", BurstTexture, 1.4f, .05f, .90f, 2f, .05f, .15f, .58f);
        m.Crack = NinthBit(bitShader, "M_WreckNinth_Crack", GlowCrackTexture, 1.6f, .34f, .95f, 1f, .04f, .08f, .30f);
        m.Half = NinthBit(bitShader, "M_WreckNinth_Half", HalfTexture, 1f, .08f, .97f, 1.5f, .045f, .12f, .58f);
        m.Spark = NinthBit(bitShader, "M_WreckNinth_Spark", SparkTexture, 1f, .12f, .86f, 1.2f, .05f, .10f, .34f);
        // Раунд 2: вспышка заряда — почти вся белая; белое только в сердцевине, лучи индиго.
        m.Flare = NinthBit(bitShader, "M_WreckNinth_Flare", BurstTexture, 1.5f, .06f, .92f, 1.3f, .06f, .16f, .66f);
        m.Ring = NinthBit(bitShader, "M_WreckNinth_Ring", AuraTexture, 1.25f, .30f, .95f, 1f, .05f, .12f, .55f);
        // Ореол головы — шипастая звезда позади головы (вал Девятого — шипы), две ступени вертятся навстречу; живёт секунды
        // (возраст частицы ~0) — порог постоянный, края жёсткие. Проба: кольцо «aura rays» читалось синим бубликом-значком.
        m.AuraIn = NinthBit(bitShader, "M_WreckNinth_AuraIn", BurstTexture, 1.4f, .22f, .26f, 1f, .06f, .16f, .55f);
        m.AuraOut = NinthBit(bitShader, "M_WreckNinth_AuraOut", BurstTexture, 1.3f, .14f, .18f, 1f, .07f, .22f, 2f);
        foreach (var a in new[] { m.GroundStar, m.Crack }) a.SetFloat("_CameraPush", .03f);
        m.Half.SetFloat("_CameraPush", .25f);
        m.Spark.SetFloat("_CameraPush", .4f);
        m.Flare.SetFloat("_CameraPush", .55f);
        m.Ring.SetFloat("_CameraPush", .45f);
        m.AuraIn.SetFloat("_CameraPush", -.32f);
        m.AuraOut.SetFloat("_CameraPush", -.38f);
        m.Rock = NinthRockMaterial("M_WreckNinth_Rock");

        foreach (var mat in new[] { m.Ground, m.Crest, m.Echo, m.Echo2, m.Flank, m.GroundStar, m.Crack, m.Half, m.Spark, m.Flare, m.Ring, m.AuraIn, m.AuraOut, m.Rock })
        {
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }
        m.Rocks = NinthRockMeshes();
        if (m.Rocks.Length == 0) { Debug.LogWarning("[wreck-ninth] Нет камней Ultimate Nature — Девятый вал не собран."); return; }

        SaveNinthLungePrefab(m);
        SaveNinthChargePrefab(m);
        SaveNinthGlowPrefab(m);
        var importer = AssetImporter.GetAtPath(PrefabPath(NinthLungeName));
        if (importer != null && importer.userData != NinthRevision)
        {
            importer.userData = NinthRevision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-ninth] Девятый вал собран: " + NinthLungeName + ", " + NinthChargeName + ", " + NinthGlowName + " (" + NinthRevision + ").");
    }

    /// <summary>Гребень веера: индиго, светлый фиолетовый, бело-фиолетовая середина вала и сердцевины шипов (цвета — sRGB).</summary>
    private static Material NinthCrest(Shader shader, string name, Texture2D facets)
    {
        Material m = Fresh(name, shader);
        m.SetTexture("_FacetTex", facets);
        // Проба 4: тёмная полоса низа читалась чертой поперёк вала («плита») — низ светлее и тоньше.
        m.SetColor("_Deep", Hex(0x2A1C9C));
        m.SetColor("_Mid", Hex(PelagWreckNinthLook.IndigoHex));
        m.SetColor("_Light", Hex(PelagWreckNinthLook.LightHex));
        m.SetColor("_Hot", new Color(.97f, .95f, 1.22f, 1f));
        m.SetColor("_HotShade", new Color(.76f, .71f, 1.08f, 1f));
        Color contour = Hex(PelagWreckNinthLook.ContourHex);
        contour.a = .92f;
        m.SetColor("_Outline", contour);
        m.SetVector("_FacetScale", new Vector4(1.4f, 2.2f, .9f, .8f));
        m.SetVector("_SliverScale", new Vector4(.55f, 4.5f, .80f, 1.6f));
        // Раунд 1: верх тела — ровная линия (брус); рваный край крупнее, шипы выше.
        m.SetVector("_Body", new Vector4(.46f, .18f, 0f, 0f));
        m.SetVector("_Spikes", new Vector4(7f, .16f, .50f, .22f));
        m.SetVector("_SmallSpikes", new Vector4(23f, .04f, .15f, .45f));
        m.SetFloat("_SpikeLean", .05f);
        m.SetFloat("_SpikeSharp", 1.7f);
        // Проба 1: светлое и белое занимали полвала (лаванда) — индиго главное, белое только тонкой серединой и в шипах.
        // Раунд 3: тело в стальных штрихах читалось ледяным бревном — у вала свет внутри: штрихи и блик тише, горячая
        // середина шире (как белая ось вала на целевом кадре).
        m.SetVector("_Bands", new Vector4(.14f, .62f, .84f, 0f));
        m.SetFloat("_SpikeCore", .78f);
        m.SetFloat("_FrontHot", .14f);
        m.SetFloat("_FrontHotSeconds", .08f);
        m.SetFloat("_OutlinePx", 1.5f);
        m.SetFloat("_ErodeGain", 1.6f);
        m.SetFloat("_ErodeFrom", .14f);
        m.SetFloat("_ErodeTo", .38f);
        m.SetFloat("_ErodeBias", .02f);
        m.SetFloat("_FadeFrom", .32f);
        m.SetFloat("_FadeTo", .44f);
        m.SetFloat("_Streaks", .32f);
        m.SetFloat("_Sheen", .30f);
        m.SetFloat("_SheenRepeat", 1.6f);
        m.SetFloat("_SheenRate", 4.5f);
        m.SetFloat("_SheenWidth", .045f);
        m.SetFloat("_Glow", .34f);
        m.SetFloat("_Opacity", 1f);
        m.SetFloat("_FlowSpeed", 1.4f);
        m.SetFloat("_CameraPush", .20f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Материал стека (Bit) в индиго: контур тёмно-индиговый, тень у края — фиолетовая, ядро — бело-фиолетовое.</summary>
    private static Material NinthBit(Shader shader, string name, string texture, float gain,
        float cutFrom, float cutTo, float cutPower, float outline, float shade, float core)
    {
        Material m = BitMaterial(shader, name, texture, gain, cutFrom, cutTo, cutPower, outline, shade, core);
        Color contour = Hex(PelagWreckNinthLook.ContourHex);
        contour.a = .9f;
        m.SetColor("_Outline", contour);
        m.SetColor("_Shade", new Color(.62f, .54f, 1f, 1f));
        m.SetColor("_Core", new Color(1.0f, .97f, 1.32f, 1f));
        return m;
    }

    /// <summary>Крупные камни: освещённые частицы URP (палитра пака); фиолетовым их красит свет фронта вала.</summary>
    private static Material NinthRockMaterial(string name)
    {
        // Раунд 2: подсветка эмиссией на тёмных камнях давала фиолетово-синие глыбы — без неё.
        return RockMaterial(name);
    }
}
