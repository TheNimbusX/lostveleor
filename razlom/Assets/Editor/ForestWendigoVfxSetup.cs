using System.Collections.Generic;
using Game.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX лесного вендиго, ревизия V12 (26.09, план «Мобы леса», стадия 4.4).
///
/// V12 — доводка V11 по доскам «целевой кадр | V11»: ленты когтя идут по всему
/// маху кончика когтя широкой низкой дугой вокруг зверя, с длинными острыми
/// хвостами, борозды и веер земли — вместе с лентами; шипы воя — изогнутые
/// скрученные корни в коре (свои меши из колец: наклон, рост, толщина,
/// изгиб разные, часть — с мхом), пыль кольца — мягкое облако CFXR
/// (cloud blur), тёплое и низкое; пыль взлёта и посадки тёплая охристая,
/// у взлёта — полоса пыли по ходу прыжка.
///
/// V8–V10 — флипбуки из роликов Higgsfield на экранных плоскостях; владелец:
/// «поверх экрана, не на нашей земле», серп запаздывает. V11 собран заново из
/// паков по целевым кадрам поверх наших скриншотов
/// (ART/…/3.forest-elite-wendigo/production/vfx_target_frames_2026-09-26,
/// picks.json): всё лежит на земле или летит из неё, экранных плоскостей и
/// флипбуков нет.
///
/// • Коготь (1-claw-ivory): три коротких ленты цвета слоновой кости низко над
///   землёй по настоящему пути кончика когтя (запекается из клипа Claw), три
///   борозды в земле под нижней частью маха, веер комьев, щепок и листьев по
///   ходу удара, низкая пыль.
/// • Отталкивание (3-takeoff): из-под обеих стоп назад комья и листья, низкий
///   накат пыли, две борозды-задира (стопы — из клипа Leap).
/// • Приземление (4-landing + звезда трещин из 8): низкая юбка пыли, кратер и
///   звезда трещин на земле, комья, щепки, листья, маленький светлый акцент
///   касания (CFXR2 Ground Hit).
/// • «Вой чащи» (5-howl-roots, 7-howl-windup): на замахе по кольцу 2–5,5 м
///   прорастают кончики корней; на ударе кольцо изогнутых корней и шипов в
///   коре с пустым кругом у ног, земля под шипами, кольцо пыли, комья; бледное
///   дыхание у черепа.
///
/// Паки: CFXR (debris unlit 3x3, debris wood unlit 3x3, листья leave a +
/// cfxr mesh leave, smoke cloud x4 blurred, мягкое облако cloud blur, маска
/// плёнки меча plain, CFXR2 Ground Hit), Hovl
/// (Crater40, Crater19, Crater2, Crack4), кора из Fantasy Forest
/// (bark01_bottom, мшистый низ). Наш слой — состав, тон, путь ленты, меши
/// корней и тайминг.
/// Ленты и борозды — шейдер серпа Вихря (Razlom/Whirlwind Sweep, _Timed:
/// голова, эрозия и уход по возрасту частицы).
///
/// Корни префабов лежат на земле: +Z — направление атаки, +Y — вверх. Вид
/// (ForestWendigoCombatView) ставит корень в точку и ведёт системы по
/// возрасту от тика Sim.
/// </summary>
public static class ForestWendigoVfxSetup
{
    private const string Revision = "WendigoVfxV12";
    private const string Root = "Assets/Resources/VFX/Wendigo";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string TextureFolder = Root + "/Textures";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string CfxrTrailMaskPlain = CfxrGraphics + "cfxr sword trail mask plain.png";
    private const string CfxrCloudBlur = CfxrGraphics + "cfxr cloud blur.png";
    private const string CfxrGroundHit = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR2 Ground Hit.prefab";
    private const string HovlTextures = "Assets/Hovl Studio/HSFiles/Textures/";
    private const string BarkTexture = "Assets/Fantasy Forest Environment Free Sample/Textures/bark01_bottom.tga";
    private const string WendigoFolder = "Assets/Resources/Characters/Forest_Wendigo/";
    private const string SweepShaderName = "Razlom/Whirlwind Sweep";

    public const string Claw = "VFX_Wendigo_Claw";
    public const string Takeoff = "VFX_Wendigo_Takeoff";
    public const string Landing = "VFX_Wendigo_Landing";
    public const string HowlWindup = "VFX_Wendigo_HowlWindup";
    public const string Howl = "VFX_Wendigo_Howl";
    public const string HowlBreath = "VFX_Wendigo_HowlBreath";

    // Тона: земля и пыль луга, кость когтей, оливковые листья. Игра яркая —
    // земля тёплая, без чёрного.
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f);
    private static readonly Color SoilDark = new Color(.30f, .20f, .12f);
    // Пыль V12 — тёплая охра, как на целевых кадрах (V11 была бледно-песочной).
    private static readonly Color DustLight = new Color(.76f, .58f, .39f);
    private static readonly Color DustDark = new Color(.58f, .43f, .28f);
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f);
    private static readonly Color LeafDark = new Color(.42f, .52f, .20f);
    private static readonly Color Ivory = new Color(1.16f, 1.07f, .88f);

    private sealed class Kit
    {
        public Material Clod, Bark, Leaf, Dust, Haze, Crater, Fissure, Splat, Crack, Ribbon, Groove, Wood, Moss;
        public Mesh LeafMesh, Flat;
        // Корни воя: высокие шипы-колючки, скрученные корни, короткие побеги.
        public Mesh[] Thorns, Roots, Shoots;
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Лесной вендиго/VFX: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Лесной вендиго/VFX: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Shader.Find(SweepShaderName) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisUnlit) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null || AssetDatabase.LoadAssetAtPath<Texture2D>(HovlTextures + "Crater40.png") == null)
        {
            Debug.LogWarning("[wendigo-vfx] Нет паков Hovl/CFXR или шейдера серпа, VFX вендиго не собран.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + Howl + ".prefab");
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + Claw + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "Wendigo");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        RemoveStale();
        var kit = Materials();
        var pose = WendigoPose.Sample();
        SaveClaw(kit, pose);
        SaveTakeoff(kit, pose);
        SaveLanding(kit);
        SaveHowlWindup(kit);
        SaveHowl(kit);
        SaveHowlBreath(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + Howl + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wendigo-vfx] Коготь, взлёт, приземление и вой собраны из паков на земле, ревизия " + Revision
            + (pose.FromClip ? "" : " (путь когтя — запасной, клип не найден)") + ".");
    }

    /// <summary>
    /// Флипбуки V8–V10 и прочие старые ассеты уходят из Resources: исходники
    /// роликов остаются в ART/…/vfx_elements.
    /// </summary>
    private static void RemoveStale()
    {
        foreach (var stale in new[]
        {
            PrefabFolder + "/VFX_Wendigo_ClawFlip.prefab", PrefabFolder + "/VFX_Wendigo_LandingSkirt.prefab",
            PrefabFolder + "/VFX_Wendigo_LandingColumn.prefab", PrefabFolder + "/VFX_Wendigo_TakeoffGround.prefab",
            PrefabFolder + "/VFX_Wendigo_TakeoffColumn.prefab", PrefabFolder + "/VFX_Wendigo_ClawGround.prefab",
            PrefabFolder + "/VFX_Wendigo_ClawGlow.prefab", PrefabFolder + "/VFX_Wendigo_LandingFlip.prefab",
            PrefabFolder + "/VFX_Wendigo_TakeoffFlip.prefab", PrefabFolder + "/VFX_Wendigo_ClawSweep.prefab",
            MaterialFolder + "/M_Wendigo_Flip_ClawFlip.mat", MaterialFolder + "/M_Wendigo_Flip_LandingSkirt.mat",
            MaterialFolder + "/M_Wendigo_Flip_LandingColumn.mat", MaterialFolder + "/M_Wendigo_Flip_TakeoffGround.mat",
            MaterialFolder + "/M_Wendigo_Flip_TakeoffColumn.mat", MaterialFolder + "/M_Wendigo_Flip_LandingFlip.mat",
            MaterialFolder + "/M_Wendigo_Flip_TakeoffFlip.mat", MaterialFolder + "/M_Wendigo_IvoryGlow.mat",
            MaterialFolder + "/M_Wendigo_Ember.mat", MaterialFolder + "/M_Wendigo_Ivory.mat",
            MaterialFolder + "/M_Wendigo_GroundMark.mat", MaterialFolder + "/M_Wendigo_DustStreak.mat",
            // Кристаллы Hovl V11 читались кольями: V12 растит свои корни.
            GeometryFolder + "/WendigoRootSpike.asset", GeometryFolder + "/WendigoRootCluster.asset",
            GeometryFolder + "/WendigoHowlSpikePoints.asset", GeometryFolder + "/WendigoHowlClusterPoints.asset",
            TextureFolder
        })
            if (AssetDatabase.LoadMainAssetAtPath(stale) != null || AssetDatabase.IsValidFolder(stale)) AssetDatabase.DeleteAsset(stale);
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        // Освещённые материалы CFXR в URP бледнеют, дым Hovl без depth-текстуры
        // невидим: только unlit CFXR и его же размытые облака.
        kit.Clod = PackCopy("M_Wendigo_Clod", CfxrDebrisUnlit);
        kit.Bark = PackCopy("M_Wendigo_Bark", CfxrDebrisWood);
        kit.Leaf = Unlit(PackCopy("M_Wendigo_Leaf", CfxrLeafMaterial));
        kit.Dust = Plain(PackCopy("M_Wendigo_Dust", CfxrSmokeBlurred));
        // Мягкая пыль без «цветочных» клубов: размытое облако CFXR одним каналом,
        // цвет из частиц; им же — вытянутая полоса пыли взлёта.
        kit.Haze = Textured(Plain(PackCopy("M_Wendigo_DustHaze", CfxrSmokeBlurred)), CfxrCloudBlur, true);
        // Кратер и пятна земли: своя окраска текстуры Hovl через тот же материал облаков.
        kit.Crater = Textured(Plain(PackCopy("M_Wendigo_Crater", CfxrSmokeBlurred)), HovlTextures + "Crater40.png", false);
        kit.Splat = Textured(Plain(PackCopy("M_Wendigo_SoilSplat", CfxrSmokeBlurred)), HovlTextures + "Crater2.png", false);
        // Трещины — одноканальная плёнка CFXR с маской Hovl, цвет из частиц.
        kit.Fissure = Textured(NoDissolve(PackCopy("M_Wendigo_Fissure", CfxrTrailMaterial)), HovlTextures + "Crater19.png", true);
        kit.Crack = Textured(NoDissolve(PackCopy("M_Wendigo_Crack", CfxrTrailMaterial)), HovlTextures + "Crack4.png", true);
        // Декали рисуются до пыли и комьев.
        foreach (var decal in new[] { kit.Crater, kit.Splat, kit.Fissure, kit.Crack }) { decal.renderQueue = 2990; EditorUtility.SetDirty(decal); }
        // Лента V12: мягкая маска plain без рваных концов пака — силуэт даёт
        // сужение: длинный острый хвост, толще к голове, острая голова (коготь).
        kit.Ribbon = Sweep("M_Wendigo_ClawRibbon", Ivory, new Color(1.0f, .86f, .60f), new Color(.82f, .62f, .36f), new Color(.42f, .29f, .16f), .18f,
            CfxrTrailMaskPlain, .2f, 1.75f, .7f);
        // Ядро уже, золотистая кромка шире: у цели кость с тёплым краем, не белая полоса.
        kit.Ribbon.SetVector("_Bands", new Vector4(.12f, .34f, .56f, .80f));
        kit.Groove = Sweep("M_Wendigo_Groove", new Color(.16f, .10f, .06f), new Color(.24f, .16f, .10f), new Color(.24f, .16f, .10f), new Color(.50f, .37f, .24f), 0f,
            CfxrTrailMaskPlain, .2f, 1.0f, .55f);
        kit.Groove.renderQueue = 2995;
        EditorUtility.SetDirty(kit.Groove);
        // Дерево корней воя: URP Lit с корой — свет, тень и объём, как у пропсов
        // леса. Кора пака внизу уже мшистая: v = 0 у земли даёт мох у основания.
        // Шипы — тёмная тёплая кора, часть корней — с зелёным налётом мха.
        kit.Wood = Wood("M_Wendigo_RootWood", new Color(.92f, .76f, .72f));
        kit.Moss = Wood("M_Wendigo_RootMoss", new Color(.82f, .92f, .62f));
        kit.LeafMesh = LoadMesh(CfxrLeafMesh);
        kit.Flat = FlatQuad("WendigoDustStreakQuad");
        // (длина 1, радиус основания, изгиб, скрутка, узлы, боковые колючки)
        kit.Thorns = new[]
        {
            RootSpike("WendigoRootThornA", 11, .15f, .12f, 1.4f, .10f, 1),
            RootSpike("WendigoRootThornB", 12, .13f, .20f, 2.2f, .12f, 2),
            RootSpike("WendigoRootThornC", 13, .16f, .08f, 1.0f, .08f, 0)
        };
        kit.Roots = new[]
        {
            RootSpike("WendigoRootTwistA", 21, .12f, .20f, 3.2f, .20f, 2),
            RootSpike("WendigoRootTwistB", 22, .11f, .24f, 2.6f, .18f, 1)
        };
        kit.Shoots = new[]
        {
            RootSpike("WendigoRootShootA", 31, .14f, .24f, 1.8f, .14f, 1),
            RootSpike("WendigoRootShootB", 32, .15f, .18f, 2.4f, .16f, 0)
        };
        return kit;
    }

    private static Material Wood(string name, Color tint)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Lit"));
        var bark = AssetDatabase.LoadAssetAtPath<Texture2D>(BarkTexture);
        if (bark != null) material.SetTexture("_BaseMap", bark);
        // Развёртка корней: u — вокруг сечения, v — вдоль (0 у земли), кора целиком.
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", .18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят спрайт у земли.</summary>
    private static Material Plain(Material material)
    {
        NoDissolve(material);
        material.DisableKeyword("_FADING_ON");
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material NoDissolve(Material material)
    {
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_FADING_ON", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        if (material.HasProperty("_UseLighting")) material.SetFloat("_UseLighting", 0f);
        if (material.HasProperty("_UseNormalMap")) material.SetFloat("_UseNormalMap", 0f);
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        if (material.HasProperty("_CFXR_DITHERED_SHADOWS")) material.SetFloat("_CFXR_DITHERED_SHADOWS", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Textured(Material material, string texturePath, bool singleChannel)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", singleChannel ? 1f : 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Лента на шейдере серпа Вихря: поперёк обвод, кромка, середина, ядро;
    /// силуэт — маска плёнки меча CFXR и сужение концов, время — от возраста частицы.
    /// </summary>
    private static Material Sweep(string name, Color core, Color mid, Color edge, Color rim, float glow,
        string maskPath, float maskCut, float taperSkew, float taperPower)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find(SweepShaderName));
        material.SetColor("_Core", core);
        material.SetColor("_Mid", mid);
        material.SetColor("_Edge", edge);
        material.SetColor("_Rim", rim);
        material.SetVector("_Bands", new Vector4(.10f, .26f, .42f, .78f));
        material.SetFloat("_RimOuter", .90f);
        material.SetFloat("_ErodeAlong", .75f);
        material.SetVector("_NoiseScale", new Vector4(14f, 2.5f, 0f, 0f));
        material.SetFloat("_Periodic", 0f);
        material.SetFloat("_Streaks", glow > 0f ? .35f : 0f);
        material.SetFloat("_Glow", glow);
        material.SetFloat("_Flash", 0f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_Taper", 1f);
        material.SetFloat("_TaperPower", taperPower);
        material.SetFloat("_TaperSkew", taperSkew);
        material.SetFloat("_BandWobble", .08f);
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        material.SetTexture("_Mask", mask != null ? mask : PelagWhirlwindVfxSetup.WhiteTexture());
        material.SetFloat("_MaskCut", mask != null ? maskCut : 0f);
        material.SetFloat("_Timed", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    /// <summary>
    /// Корень-шип воя (свой меш вместо кристалла Hovl V11, читавшегося колом):
    /// труба из колец вдоль изогнутого хребта, сечение с долями и скруткой по
    /// высоте, пята у земли, узлы, 0–2 боковые колючки, острый кончик. Длина 1
    /// по −Z, основание в нуле: мешевая частица с выравниванием по направлению
    /// смотрит −Z вдоль нормали точки кольца (проба 26.09). Кора вдоль: v = 0 у
    /// земли (мшистый низ текстуры), u обходит сечение — волокна идут спиралью.
    /// </summary>
    private static Mesh RootSpike(string name, int salt, float radius, float bend, float twist, float knots, int thorns)
    {
        const int rings = 12, sides = 8;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        System.Func<float, Vector3> spine = t => new Vector3(bend * t * t + .045f * Mathf.Sin(t * 6f + phase), t,
            bend * .3f * Mathf.Sin(t * 3.1f + phase));
        System.Func<float, float> thickness = t => radius * Mathf.Pow(1f - t, .85f)
            * (1f + .35f * Mathf.Pow(Mathf.Max(0f, 1f - t / .2f), 2f))
            * (1f + knots * (Mathf.Sin(t * 11f + phase) + .5f * Mathf.Sin(t * 27f + 2f * phase)) * (1f - t));
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            RingFrame(spine, t, out Vector3 center, out Vector3 tangent, out Vector3 side, out Vector3 other);
            float rad = thickness(t);
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .24f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(center + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * rad * lobe);
                uv.Add(new Vector2(s / (float)sides, t));
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        // Боковые колючки: конус из стенки ствола наружу и вверх.
        for (int k = 0; k < thorns; k++)
        {
            float t = Mathf.Lerp(.26f, .62f, (k + Hash01(salt, 20 + k)) / thorns);
            RingFrame(spine, t, out Vector3 center, out Vector3 tangent, out Vector3 side, out Vector3 other);
            float a = Hash01(salt, 30 + k) * Mathf.PI * 2f;
            Vector3 outward = side * Mathf.Cos(a) + other * Mathf.Sin(a);
            Vector3 axis = (outward * .8f + tangent * .6f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangent).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = center + outward * thickness(t) * .7f;
            float baseRadius = thickness(t) * .45f, length = .16f + .1f * Hash01(salt, 40 + k);
            int start = vertices.Count;
            for (int s = 0; s <= 5; s++)
            {
                float b = s / 5f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s * .06f, t));
                vertices.Add(root + axis * length); uv.Add(new Vector2(s * .06f, t + .1f));
                if (s < 5) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        var upright = Quaternion.FromToRotation(Vector3.up, Vector3.back);
        for (int i = 0; i < vertices.Count; i++) vertices[i] = upright * vertices[i];
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // Шов развёртки: у пары вершин одного места — общая нормаль.
        var normals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
        }
        mesh.normals = normals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void RingFrame(System.Func<float, Vector3> spine, float t, out Vector3 center, out Vector3 tangent,
        out Vector3 side, out Vector3 other)
    {
        center = spine(t);
        tangent = (spine(Mathf.Min(1f, t + .01f)) - spine(Mathf.Max(0f, t - .01f))).normalized;
        side = Vector3.Cross(tangent, Vector3.forward).normalized;
        other = Vector3.Cross(side, tangent);
    }

    /// <summary>
    /// Плоский прямоугольник на земле для полосы пыли: ширина 1 по X, длина 1
    /// по +Z от нуля, нормаль вверх; u — вдоль (длинная ось текстуры дыма).
    /// </summary>
    private static Mesh FlatQuad(string name)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(new List<Vector3> { new Vector3(-.5f, 0f, 0f), new Vector3(.5f, 0f, 0f), new Vector3(-.5f, 0f, 1f), new Vector3(.5f, 0f, 1f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f) });
        mesh.SetColors(new List<Color32> { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
        mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ------------------------------------------------------------------ pose

    /// <summary>
    /// Точки из клипов зверя в осях корня (зверь в нуле, взгляд +Z): путь
    /// кончика когтя правой кисти за весь мах (Claw, кадры 49,5–54,5 из 96: с
    /// бока-сзади, x ≈ +1,6, через правый бок и перед к левой стороне — дуга
    /// ~140° радиусом ~2 м; вид ведёт кадр 52 на тике контакта 18) и стопы в
    /// кадре отталкивания (Leap, кадр 45 — тик 24). Запекается при сборке;
    /// если модели или клипа нет — замер 26.09.
    /// </summary>
    private sealed class WendigoPose
    {
        public bool FromClip;
        public Vector3[] ClawPath;
        public Vector3 LeftFoot = new Vector3(-.78f, 0f, -.52f), RightFoot = new Vector3(1.10f, 0f, -.98f);

        public const float ClawFrom = 49.5f, ClawTo = 54.5f, ClawStep = .25f;

        private static readonly Vector3[] Measured =
        {
            new Vector3(1.556f, 2.939f, -.921f), new Vector3(1.598f, 2.983f, -.811f), new Vector3(1.684f, 2.923f, -.684f),
            new Vector3(1.835f, 2.702f, -.570f), new Vector3(1.974f, 2.376f, -.385f), new Vector3(2.027f, 2.076f, -.118f),
            new Vector3(2.038f, 1.825f, .124f), new Vector3(2.068f, 1.603f, .214f), new Vector3(2.081f, 1.606f, .251f),
            new Vector3(2.036f, 1.781f, .568f), new Vector3(1.950f, 1.749f, 1.012f), new Vector3(1.851f, 1.425f, 1.340f),
            new Vector3(1.561f, .943f, 1.541f), new Vector3(1.064f, .567f, 1.568f), new Vector3(.597f, .414f, 1.515f),
            new Vector3(.211f, .363f, 1.496f), new Vector3(-.167f, .363f, 1.488f), new Vector3(-.499f, .414f, 1.478f),
            new Vector3(-.733f, .488f, 1.466f), new Vector3(-.844f, .561f, 1.489f), new Vector3(-.867f, .636f, 1.567f)
        };

        public static WendigoPose Sample()
        {
            var pose = new WendigoPose { ClawPath = Measured };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WendigoFolder + "ForestWendigo_Runtime.prefab");
            var claw = AssetDatabase.LoadAssetAtPath<AnimationClip>(WendigoFolder + "Claw.anim");
            var leap = AssetDatabase.LoadAssetAtPath<AnimationClip>(WendigoFolder + "Leap.anim");
            if (prefab == null || claw == null || leap == null) return pose;
            var scene = EditorSceneManager.NewPreviewScene();
            Mesh baked = new Mesh();
            try
            {
                var go = (GameObject)Object.Instantiate(prefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = go.GetComponentInChildren<Animator>();
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                Transform hand = null, leftFoot = null, rightFoot = null, leftToe = null, rightToe = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "R_hand") hand = t;
                    else if (t.name == "L_foot") leftFoot = t;
                    else if (t.name == "R_foot") rightFoot = t;
                    else if (t.name == "L_toe") leftToe = t;
                    else if (t.name == "R_toe") rightToe = t;
                }
                if (animator == null || skin == null || hand == null) return pose;
                // Кончик когтя — вершина кисти дальше всех от запястья в стойке.
                int handIndex = System.Array.IndexOf(skin.bones, hand);
                var weights = skin.sharedMesh.boneWeights;
                claw.SampleAnimation(animator.gameObject, 19f / 96f * claw.length);
                skin.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var toWorld = skin.transform.localToWorldMatrix;
                int tip = -1; float best = -1f;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i].boneIndex0 != handIndex || weights[i].weight0 < .8f) continue;
                    float distance = (toWorld.MultiplyPoint3x4(vertices[i]) - hand.position).sqrMagnitude;
                    if (distance > best) { best = distance; tip = i; }
                }
                if (tip < 0) return pose;
                var path = new List<Vector3>();
                for (float frame = ClawFrom; frame <= ClawTo + .001f; frame += ClawStep)
                {
                    claw.SampleAnimation(animator.gameObject, frame / 96f * claw.length);
                    skin.BakeMesh(baked, true);
                    path.Add(skin.transform.localToWorldMatrix.MultiplyPoint3x4(baked.vertices[tip]));
                }
                pose.ClawPath = path.ToArray();
                leap.SampleAnimation(animator.gameObject, 45f / 96f * leap.length);
                if (leftFoot != null && leftToe != null) pose.LeftFoot = Flat((leftFoot.position + leftToe.position) * .5f);
                if (rightFoot != null && rightToe != null) pose.RightFoot = Flat((rightFoot.position + rightToe.position) * .5f);
                pose.FromClip = true;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(baked);
            }
            return pose;
        }

        private static Vector3 Flat(Vector3 point) => new Vector3(point.x, 0f, point.z);
    }

    // -------------------------------------------------------------- geometry

    /// <summary>Сглаженный путь (Catmull-Rom) с равным шагом по длине, count точек.</summary>
    private static Vector3[] Resample(Vector3[] points, int count)
    {
        var dense = new List<Vector3>();
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Length - 1, i + 2)];
            for (int s = 0; s < 12; s++)
            {
                float t = s / 12f, t2 = t * t, t3 = t2 * t;
                dense.Add(.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        dense.Add(points[points.Length - 1]);
        var lengths = new float[dense.Count];
        for (int i = 1; i < dense.Count; i++) lengths[i] = lengths[i - 1] + (dense[i] - dense[i - 1]).magnitude;
        var result = new Vector3[count];
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            float want = lengths[lengths.Length - 1] * i / (count - 1);
            while (at < lengths.Length - 2 && lengths[at + 1] < want) at++;
            float k = Mathf.InverseLerp(lengths[at], lengths[at + 1], want);
            result[i] = Vector3.Lerp(dense[at], dense[at + 1], k);
        }
        return result;
    }

    /// <summary>
    /// Полосы вдоль пути: u — по ходу (0 хвост, 1 голова), v — поперёк (0 к
    /// телу, 1 наружу). Полоса лежит плашмя (нормаль вверх), каждая сдвинута
    /// наружу на offset и по высоте на lift; from/to обрезают путь.
    /// </summary>
    private static Mesh Strips(string name, Vector3[] path, (float offset, float lift, float from, float to, float width)[] strips)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 40;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        // Цвет вершин 8-битный: с Float32-цветом мешевая частица читает мусор (лента выходила синей).
        var colors = new List<Color32>();
        var triangles = new List<int>();
        Vector3 center = Vector3.zero;
        foreach (var strip in strips)
        {
            int start = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float along = Mathf.Lerp(strip.from, strip.to, u) * (path.Length - 1);
                int a = Mathf.Min(path.Length - 2, Mathf.FloorToInt(along));
                Vector3 point = Vector3.Lerp(path[a], path[a + 1], along - a);
                Vector3 tangent = path[a + 1] - path[a]; tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                Vector3 outward = point - center; outward.y = 0f;
                if (Vector3.Dot(side, outward) < 0f) side = -side;
                Vector3 mid = point + side * strip.offset + Vector3.up * strip.lift;
                vertices.Add(mid - side * strip.width * .5f); uv.Add(new Vector2(u, 0f));
                vertices.Add(mid + side * strip.width * .5f); uv.Add(new Vector2(u, 1f));
                colors.Add(new Color32(255, 255, 255, 255)); colors.Add(new Color32(255, 255, 255, 255));
                if (i == segments) continue;
                int v = start + i * 2;
                triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Точки эмиссии: вершины — места, нормали — направления (выравнивание по
    /// направлению ставит +Z частицы вдоль нормали). Треугольники вырожденные:
    /// форма Mesh в режиме Vertex не принимает топологию Points.
    /// </summary>
    private static Mesh Points(string name, List<Vector3> positions, List<Vector3> directions)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(positions);
        mesh.SetNormals(directions);
        var triangles = new int[((positions.Count + 2) / 3) * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = Mathf.Min(i, positions.Count - 1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Детерминированный разброс: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>
    /// Кольцо воя: count мест по окружности со сдвигом и разбросом радиуса в
    /// [inner, outer], направление — вверх с наклоном наружу tiltMin…tiltMax.
    /// </summary>
    private static Mesh RingPoints(string name, int count, float inner, float outer, float tiltMin, float tiltMax, float depth, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, Mathf.Lerp(Hash01(i, salt + 1), Hash01(i, salt + 2), .5f));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius + Vector3.down * depth);
            // Небольшой увод вбок, чтобы шипы не стояли строем.
            var tangent = Vector3.Cross(Vector3.up, outward) * (Hash01(i, salt + 4) - .5f) * .5f;
            directions.Add((Vector3.up * Mathf.Cos(tilt) + (outward + tangent).normalized * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>
    /// Куст у каждой опоры: per мест в пределах spread вокруг точки anchors (не
    /// ближе inner к центру — пустой круг у ног цел), наклон наружу от кольца и
    /// от опоры tiltMin…tiltMax: корни куста расходятся веером.
    /// </summary>
    private static Mesh ClusterPoints(string name, Mesh anchors, int per, float spread, float inner, float tiltMin, float tiltMax, float depth, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        var at = anchors.vertices;
        for (int i = 0; i < at.Length; i++)
            for (int k = 0; k < per; k++)
            {
                int n = i * per + k;
                float angle = Hash01(n, salt) * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var flat = new Vector3(at[i].x, 0f, at[i].z) + offset * spread * Mathf.Lerp(.45f, 1f, Hash01(n, salt + 1));
                if (flat.magnitude < inner) flat = flat.normalized * inner;
                var away = (flat.normalized + offset * .6f).normalized;
                float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(n, salt + 2)) * Mathf.Deg2Rad;
                positions.Add(flat + Vector3.down * depth);
                directions.Add((Vector3.up * Mathf.Cos(tilt) + away * Mathf.Sin(tilt)).normalized);
            }
        return Points(name, positions, directions);
    }

    // ------------------------------------------------------------- particles

    /// <summary>Кривая по парам (время, значение), отрезки линейные.</summary>
    private static AnimationCurve Curve(params float[] pairs)
    {
        var keys = new Keyframe[pairs.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    /// <summary>Прозрачность по жизни: появление к fadeIn, уход с fadeOut (доли жизни).</summary>
    private static Gradient Alpha(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(.001f, fadeIn)),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    /// <summary>
    /// Система с одним залпом, местное пространство, постоянное зерно: вид
    /// переигрывает её через Simulate по возрасту, и кадр паузы не дрожит.
    /// </summary>
    private static ParticleSystem Particles(GameObject parent, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(parent, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(parent.name + "/" + name);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        return particles;
    }

    private static Quaternion Aim(Vector3 direction) => Quaternion.FromToRotation(Vector3.forward, direction.normalized);

    private static void Collide(ParticleSystem particles, Transform ground, float bounce)
    {
        var collision = particles.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground);
        collision.bounce = bounce;
        collision.dampen = .65f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .45f;
        collision.minKillSpeed = 0f;
    }

    /// <summary>
    /// Комья, щепки, камни: спрайты CFXR 3×3 (случайный кадр), баллистика,
    /// отскок от земли корня, лежат и тают в конце жизни.
    /// </summary>
    private static ParticleSystem Debris(GameObject root, string name, Material material, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float delay, Color light, Color dark)
    {
        var particles = Particles(root, name, count, 1.7f, 2.3f, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Collide(particles, root.transform, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR, кувыркаются, планируют и ложатся на землю.</summary>
    private static ParticleSystem Leaves(GameObject root, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(root, name, count, 1.8f, 2.4f, speedMin, speedMax, .16f, .26f, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .2f;
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.6f);
        drag.dampen = .12f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        Collide(particles, root.transform, .05f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>
    /// Пыль: размытые облака CFXR (кадр 2×2 наугад) или мягкое облако haze,
    /// охра с малой альфой, тормозит, растёт и тает. flat — облако лежит на
    /// земле (накат, юбка).
    /// </summary>
    private static ParticleSystem Dust(GameObject root, Kit kit, string name, int count, Vector3 at, Vector3 direction, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay, bool flat,
        Material haze = null)
    {
        var particles = Particles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        // Стоячее облако при 48° опускает нижний край на ~0,34 размера: без
        // мягких частиц земля срезала бы его прямой линией — поднимаем.
        particles.transform.localPosition = flat ? at : at + Vector3.up * (.45f * sizeMax);
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(DustLight.r, DustLight.g, DustLight.b, alpha),
            new Color(DustDark.r, DustDark.g, DustDark.b, alpha));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.25f);
        drag.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .45f, .25f, .9f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.08f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        // Мягкое облако — одна текстура: разнообразие даёт поворот и зеркало.
        if (haze == null) Sheet(particles, 2, 3.99f);
        else renderer.flip = new Vector3(.5f, .5f, 0f);
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = haze != null ? haze : kit.Dust;
        renderer.sortingFudge = flat ? 1f : -1f;
        return particles;
    }

    /// <summary>Атлас n×n: кадр наугад на частицу, без анимации.</summary>
    private static void Sheet(ParticleSystem particles, int tiles, float lastFrame)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = tiles; sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
        sheet.cycleCount = 1;
    }

    /// <summary>
    /// Пятно на земле: частица — «горизонтальный билборд», лежит на грунте
    /// корня, быстро раскрывается и тает с fadeOut (доля жизни).
    /// </summary>
    private static ParticleSystem Decal(GameObject root, string name, Material material, int count, Vector3 at, float sizeMin, float sizeMax,
        float life, float fadeOut, Color color, float delay, float lift)
    {
        var particles = Particles(root, name, count, life, life, 0f, 0f, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at + Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .04f, 1.04f, .08f, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeOut);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 4f;
        return particles;
    }

    /// <summary>Эмиссия из вершин меша по порядку (Loop): каждое место — ровно одна частица.</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, bool align, float randomDirection)
    {
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = points;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.alignToDirection = align;
        shape.randomDirectionAmount = randomDirection;
        shape.normalOffset = 0f;
    }

    /// <summary>
    /// Слой-меш с возрастом в шейдере (лента, борозды): одна частица, местные
    /// оси корня, AgePercent в TEXCOORD0.z — как у серпа Рассекающего.
    /// </summary>
    private static ParticleSystem Strip(GameObject root, string name, Mesh mesh, Material material, float life, float delay)
    {
        var particles = Particles(root, name, 1, life, life, 0f, 0f, 1f, 1f, delay);
        var main = particles.main;
        main.startRotation = 0f;
        main.startColor = Color.white;
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
        return particles;
    }

    /// <summary>Тайминг шейдера ленты по возрасту частицы, секунды.</summary>
    private static void Timing(Material material, float life, float head, float erodeFrom, float erodeTo, float fadeFrom, float fadeTo)
    {
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_HeadFrom", 0f);
        material.SetFloat("_HeadSeconds", head);
        material.SetFloat("_FlashEnd", -1f);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeTo);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeTo);
        material.SetFloat("_FlowSpeed", 1.2f);
        EditorUtility.SetDirty(material);
    }

    /// <summary>
    /// Корни и шипы: по частице на точку кольца, ось — нормаль точки, меш —
    /// случайный из набора (длинная ось −Z, основание в нуле). Длина (Z) и
    /// толщина (X, Y) разыгрываются отдельно; поворот вокруг оси случайный —
    /// изгибы смотрят в разные стороны. Растёт только длина: корень лезет из
    /// земли и уходит обратно, а не раздувается. Момент роста у каждого свой —
    /// случайная доля между early и late. Дерево — URP Lit с корой, с тенью.
    /// </summary>
    private static ParticleSystem Spikes(GameObject root, string name, Mesh[] meshes, Mesh points, Material material, float life,
        float lengthMin, float lengthMax, float thickMin, float thickMax, AnimationCurve early, AnimationCurve late)
    {
        var particles = Particles(root, name, points.vertexCount, life, life, 0f, 0f, lengthMin, lengthMax, 0f);
        var main = particles.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(thickMin, thickMax);
        main.startSizeY = new ParticleSystem.MinMaxCurve(thickMin, thickMax);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(lengthMin, lengthMax);
        FromPoints(particles, points, true, 0f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.separateAxes = true;
        size.x = new ParticleSystem.MinMaxCurve(1f);
        size.y = new ParticleSystem.MinMaxCurve(1f);
        size.z = new ParticleSystem.MinMaxCurve(1f, early, late);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(meshes);
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return particles;
    }

    // ---------------------------------------------------------------- prefabs

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    private static readonly Color BarkLight = new Color(.66f, .52f, .36f), BarkDark = new Color(.45f, .33f, .22f);

    /// <summary>
    /// Коготь (1-claw-ivory). Корень — в центре зверя, +Z — удар. Вид
    /// запускает эффект за тик до контакта: весь мах клипа (кадры 49,5–54,5)
    /// проходит за ~0,065 с, голова лент идёт за кончиком когтя чуть впереди,
    /// без отставания. Три ленты — широкая низкая дуга вокруг зверя от его
    /// бока до левой стороны перед ним, хвосты длинные и острые. Борозды
    /// начинаются с контакта под путём и уходят за голову лент; веер земли,
    /// щепки и пыль летят из всей борозды — вместе с лентами, не после.
    /// </summary>
    private static void SaveClaw(Kit kit, WendigoPose pose)
    {
        var raw = Resample(pose.ClawPath, 64);
        var low = new Vector3[raw.Length];
        int lowFrom = -1;
        for (int i = 0; i < raw.Length; i++)
        {
            // Ленты низко над землёй: путь в плане настоящий, высота сжата
            // (хвост с верха маха лишь чуть выше головы у земли).
            low[i] = new Vector3(raw[i].x, .12f + .08f * raw[i].y, raw[i].z);
            if (lowFrom < 0 && raw[i].y < 1.05f) lowFrom = i;
        }
        if (lowFrom < 0) lowFrom = raw.Length / 2;
        // Борозды: земля под нижней частью маха и ещё ~0,7 м по ходу за концом пути.
        Vector3 end = new Vector3(raw[raw.Length - 1].x, .03f, raw[raw.Length - 1].z);
        Vector3 onward = raw[raw.Length - 1] - raw[raw.Length - 5]; onward.y = 0f; onward.Normalize();
        var furrow = new List<Vector3>();
        for (int i = lowFrom; i < raw.Length; i++) furrow.Add(new Vector3(raw[i].x, .03f, raw[i].z));
        for (int k = 1; k <= 6; k++) furrow.Add(end + onward * (.12f * k));
        var ground = furrow.ToArray();
        var ribbons = Strips("WendigoClawRibbons", low, new[]
        {
            (-.22f, .03f, .03f, .95f, .17f), (0f, 0f, 0f, 1f, .22f), (.22f, -.03f, .08f, .98f, .17f)
        });
        var grooves = Strips("WendigoClawGrooves", ground, new[]
        {
            (-.2f, 0f, .04f, .93f, .13f), (0f, .002f, 0f, 1f, .16f), (.2f, .004f, .08f, .97f, .13f)
        });
        // Голова лент: старт с задержкой 0,018 с и 0,048 с на путь — впереди
        // кончика когтя не больше чем на ~0,6 м (ease-out шейдера против
        // разгона маха); борозды — с контакта (~0,035 с) до конца маха.
        Timing(kit.Ribbon, .46f, .048f, .15f, .42f, .34f, .46f);
        Timing(kit.Groove, 2.2f, .032f, 1.45f, 2.15f, 1.75f, 2.2f);
        // Веер: точки вдоль борозды, направление — по ходу маха, наружу и вверх.
        var sprayAt = new List<Vector3>();
        var sprayDir = new List<Vector3>();
        for (int i = lowFrom; i < raw.Length; i += 3)
        {
            Vector3 along = raw[Mathf.Min(raw.Length - 1, i + 2)] - raw[Mathf.Max(0, i - 2)]; along.y = 0f; along.Normalize();
            Vector3 outward = new Vector3(raw[i].x, 0f, raw[i].z).normalized;
            sprayAt.Add(new Vector3(raw[i].x, .08f, raw[i].z));
            sprayDir.Add((along * .8f + outward * .45f + Vector3.up * .5f).normalized);
        }
        var spray = Points("WendigoClawSprayPoints", sprayAt, sprayDir);

        var root = new GameObject(Claw);
        Strip(root, "Ribbons", ribbons, kit.Ribbon, .46f, .018f);
        Strip(root, "Grooves", grooves, kit.Groove, 2.2f, .035f);
        var clods = Debris(root, "Clods", kit.Clod, 18, Vector3.zero, Vector3.up, 0f, .1f, 3.2f, 6.0f, .12f, .26f, 1.7f, .035f, SoilLight, SoilDark);
        FromPoints(clods, spray, false, .3f);
        var bark = Debris(root, "Bark", kit.Bark, 8, Vector3.zero, Vector3.up, 0f, .1f, 2.8f, 4.8f, .11f, .19f, 1.3f, .04f, BarkLight, BarkDark);
        FromPoints(bark, spray, false, .4f);
        var leaves = Leaves(root, kit, "Leaves", 5, Vector3.up * .1f, Vector3.up, 0f, 1.8f, 3.2f, .04f);
        FromPoints(leaves, spray, false, .5f);
        var dust = Dust(root, kit, "Dust", 7, Vector3.zero, Vector3.up, 0f, .1f, .9f, 1.8f, .5f, .8f, .7f, 1.0f, .38f, .03f, false, kit.Haze);
        FromPoints(dust, spray, false, .35f);
        var dustLow = Dust(root, kit, "DustLow", 7, Vector3.zero, Vector3.up, 0f, .1f, .6f, 1.4f, .8f, 1.3f, .9f, 1.3f, .38f, .035f, true, kit.Haze);
        FromPoints(dustLow, spray, false, .5f);
        foreach (var ps in new[] { clods, bark, leaves, dust, dustLow }) ps.transform.localRotation = Quaternion.identity;
        // Пыль видна уже рядом с лентами: стартует крупнее, чем общая.
        foreach (var ps in new[] { dust, dustLow })
        {
            var grow = ps.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .75f, .2f, 1f, 1f, 1.4f));
        }
        Save(root);
    }

    /// <summary>
    /// Отталкивание (3-takeoff). Корень — в центре зверя в тик взлёта, +Z —
    /// прыжок. Из-под каждой стопы назад комья и щепки, низкий накат тёплой
    /// пыли назад, две борозды-задира к пяткам; вперёд по ходу прыжка — полоса
    /// пыли на земле и несколько низких клубов, увлечённых зверем.
    /// </summary>
    private static void SaveTakeoff(Kit kit, WendigoPose pose)
    {
        var root = new GameObject(Takeoff);
        var back = new Vector3(0f, .5f, -1f);
        foreach (var (side, foot) in new[] { ("L", pose.LeftFoot), ("R", pose.RightFoot) })
        {
            Vector3 at = foot + Vector3.up * .06f;
            var furrow = Strips("WendigoTakeoffFurrow" + side,
                new[] { foot + new Vector3(0f, .03f, .15f), foot + new Vector3(0f, .03f, -.45f), foot + new Vector3(0f, .03f, -1.15f) },
                new[] { (-.09f, 0f, 0f, 1f, .14f), (.08f, .002f, .1f, .9f, .12f) });
            Strip(root, "Furrow" + side, furrow, kit.Groove, 2.2f, 0f);
            Debris(root, "Clods" + side, kit.Clod, 16, at, back, 16f, .2f, 4.0f, 7.0f, .12f, .27f, 1.3f, 0f, SoilLight, SoilDark);
            Debris(root, "Bark" + side, kit.Bark, 4, at, back + Vector3.up * .3f, 25f, .15f, 2.8f, 4.6f, .10f, .17f, 1.3f, .02f, BarkLight, BarkDark);
            Leaves(root, kit, "Leaves" + side, 3, at + Vector3.up * .1f, back + Vector3.up * .5f, 30f, 1.6f, 2.8f, .03f);
            Dust(root, kit, "Dust" + side, 2, at, new Vector3(0f, .18f, -1f), 22f, .25f, 1.6f, 2.8f, .40f, .65f, .7f, 1.0f, .32f, .01f, false, kit.Haze);
            // Накат катится дальше стоячей пыли: торможение слабее; юбка плотнее V11.
            var roll = Dust(root, kit, "DustLow" + side, 8, at, new Vector3(0f, 0f, -1f), 14f, .3f, 1.6f, 3.6f, 1.1f, 1.9f, 1.1f, 1.6f, .40f, .02f, true, kit.Haze);
            var rollDrag = roll.limitVelocityOverLifetime; rollDrag.dampen = .08f;
        }
        // Полоса пыли по ходу прыжка: вытянутое мягкое облако пака на земле от стоп
        // вперёд, проявляется за 0,1 с, вытягивается и тает к ~0,9 с.
        var streak = Particles(root, "Streak", 2, .95f, 1.1f, 0f, 0f, 1f, 1f, .02f);
        streak.transform.localPosition = new Vector3(.15f, .05f, -.9f);
        var main = streak.main;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(DustLight.r, DustLight.g, DustLight.b, .34f),
            new Color(DustDark.r, DustDark.g, DustDark.b, .30f));
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = new ParticleSystem.MinMaxCurve(-.12f, .12f);
        main.startRotationZ = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(.7f, .95f);
        main.startSizeY = 1f;
        main.startSizeZ = new ParticleSystem.MinMaxCurve(3.2f, 4.0f);
        var shape = streak.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(.8f, 0f, .4f);
        var grow = streak.sizeOverLifetime; grow.enabled = true;
        grow.separateAxes = true;
        grow.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .3f, 1f, 1f, 1.25f));
        grow.y = new ParticleSystem.MinMaxCurve(1f);
        grow.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .25f, 1f, 1f, 1.15f));
        var fade = streak.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.1f, .35f);
        var streakRenderer = streak.GetComponent<ParticleSystemRenderer>();
        streakRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        streakRenderer.mesh = kit.Flat;
        streakRenderer.alignment = ParticleSystemRenderSpace.Local;
        streakRenderer.sharedMaterial = kit.Haze;
        streakRenderer.sortingFudge = 2f;
        // Клубы, увлечённые прыжком: низко, вперёд, быстро тормозят.
        var drift = Dust(root, kit, "DustDrift", 5, new Vector3(.15f, .05f, .2f), new Vector3(0f, .12f, 1f), 12f, .5f, 2.2f, 4.0f, .45f, .75f, .8f, 1.1f, .26f, .03f, false, kit.Haze);
        var driftDrag = drift.limitVelocityOverLifetime; driftDrag.dampen = .18f;
        Save(root);
    }

    /// <summary>
    /// Приземление (4-landing + звезда трещин из 8-landing-heavy). Корень — в
    /// точке посадки, +Z — прыжок. Кратер и трещины на земле, плотная низкая
    /// юбка тёплой пыли наружу, комья и камни вверх-наружу, щепки, листья,
    /// светлый акцент касания.
    /// </summary>
    private static void SaveLanding(Kit kit)
    {
        var root = new GameObject(Landing);
        Decal(root, "Soil", kit.Splat, 1, Vector3.zero, 2.7f, 2.7f, 2.6f, .7f, new Color(.46f, .34f, .22f, .85f), 0f, .03f);
        Decal(root, "Crater", kit.Crater, 1, Vector3.zero, 1.9f, 1.9f, 2.6f, .7f, new Color(1f, .95f, .88f, .8f), 0f, .036f);
        Decal(root, "Fissures", kit.Fissure, 1, Vector3.zero, 3.6f, 3.6f, 2.6f, .65f, new Color(SoilDark.r * .75f, SoilDark.g * .75f, SoilDark.b * .75f, 1f), 0f, .042f);
        Dust(root, kit, "Skirt", 8, Vector3.zero, Vector3.up, 86f, 1.0f, 2.6f, 3.8f, .55f, .90f, .8f, 1.2f, .34f, 0f, false, kit.Haze);
        Dust(root, kit, "SkirtLow", 14, Vector3.up * .05f, Vector3.up, 88f, 1.0f, 1.8f, 3.2f, 1.2f, 1.9f, 1.1f, 1.7f, .36f, .02f, true, kit.Haze);
        Debris(root, "Clods", kit.Clod, 18, Vector3.up * .1f, Vector3.up, 55f, .6f, 3.5f, 6.0f, .13f, .28f, 1.7f, 0f, SoilLight, SoilDark);
        Debris(root, "Rocks", kit.Clod, 5, Vector3.up * .1f, Vector3.up, 45f, .5f, 3.0f, 4.5f, .30f, .45f, 1.9f, 0f, new Color(.48f, .36f, .25f), new Color(.34f, .25f, .17f));
        Debris(root, "Bark", kit.Bark, 6, Vector3.up * .1f, Vector3.up, 50f, .5f, 3.5f, 5.5f, .12f, .22f, 1.3f, .01f, BarkLight, BarkDark);
        Leaves(root, kit, "Leaves", 10, Vector3.up * .2f, Vector3.up, 70f, 2.0f, 3.8f, .02f);
        // Акцент касания — CFXR2 Ground Hit пака: кольцо и искры по земле, цвет кости, мелко.
        var hit = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrGroundHit);
        if (hit != null)
        {
            var accent = (GameObject)Object.Instantiate(hit);
            accent.name = "Accent";
            accent.transform.SetParent(root.transform, false);
            accent.transform.localPosition = new Vector3(0f, .06f, .35f);
            accent.transform.localScale = Vector3.one * .5f;
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(accent);
            foreach (var ps in accent.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.useAutoRandomSeed = false;
                ps.randomSeed = Seed("Accent/" + ps.name);
                var main = ps.main;
                main.startColor = new Color(1f, .93f, .78f, ps.main.startColor.color.a * .55f);
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }
        Save(root);
    }

    /// <summary>
    /// Замах воя (7-howl-windup): 30 тиков по кольцу 2–5,5 м из земли лезут
    /// кончики корней — каждый в свой момент, — под ними трещинки земли. На
    /// ударе (1 с) уходят, их сменяют корни удара. Метку кольца рисует общий
    /// GroundTelegraphView.
    /// </summary>
    private static void SaveHowlWindup(Kit kit)
    {
        float inner = Simulation.WendigoHowlInnerRadius.ToFloat(), outer = Simulation.WendigoHowlOuterRadius.ToFloat();
        float windup = Simulation.WendigoHowlWindupTicks / (float)Simulation.TicksPerSecond;
        const float life = 1.12f, depth = .04f;
        float impact = windup / life;
        var tips = RingPoints("WendigoHowlTipPoints", 24, inner + .15f, outer - .2f, 25f, 55f, depth, 101);
        var root = new GameObject(HowlWindup);
        Spikes(root, "Tips", kit.Shoots, tips, kit.Wood, life, .26f, .42f, .55f, .75f,
            Curve(0f, 0f, .06f, 0f, .30f, 1f, impact - .02f, 1f, impact + .06f, 0f, 1f, 0f),
            Curve(0f, 0f, .48f, 0f, .75f, 1f, impact - .02f, 1f, impact + .06f, 0f, 1f, 0f));
        var cracks = Decal(root, "Cracks", kit.Crack, tips.vertexCount, Vector3.zero, .8f, 1.2f, life, .9f,
            new Color(SoilDark.r, SoilDark.g, SoilDark.b, .9f), 0f, .03f + depth);
        FromPoints(cracks, tips, false, 0f);
        var fade = cracks.colorOverLifetime;
        fade.color = new ParticleSystem.MinMaxGradient(Alpha(.28f, impact), Alpha(.7f, impact));
        var specks = Debris(root, "Specks", kit.Clod, 12, Vector3.up * (.05f + depth), Vector3.up, 0f, .1f, .8f, 1.6f, .05f, .10f, 1.4f, 0f, SoilLight, SoilDark);
        specks.transform.localRotation = Quaternion.identity;
        FromPoints(specks, tips, false, .4f);
        var emission = specks.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(.12f, (short)4), new ParticleSystem.Burst(.4f, (short)4), new ParticleSystem.Burst(.68f, (short)4) });
        var main = specks.main;
        main.duration = life;
        main.maxParticles = 12;
        Save(root);
    }

    /// <summary>
    /// Удар воя (5-howl-roots): кольцо корней 2–5,5 м, у ног — пустой круг.
    /// Три слоя своих мешей в коре: высокие колючие шипы (над землёй ~0,85–1,4 м),
    /// скрученные корни с мхом (~0,65–1 м) и короткие побеги у подножия шипов
    /// (~0,35–0,7 м) — кольцо идёт кустами; наклон наружу 10–35°, толщина,
    /// изгиб и поворот у каждого свои. Корни лезут из земли
    /// за ~0,1 с с перелётом, стоят, пока зверь открыт (24 тика), и уходят
    /// обратно. Под ними тёмная земля и трещины, низкое тёплое кольцо мягкой
    /// пыли, комья, щепки и листья.
    /// </summary>
    private static void SaveHowl(Kit kit)
    {
        float inner = Simulation.WendigoHowlInnerRadius.ToFloat(), outer = Simulation.WendigoHowlOuterRadius.ToFloat();
        const float life = 1.5f, depth = .12f;
        var thorns = RingPoints("WendigoHowlThornPoints", 22, inner + .45f, outer - .45f, 10f, 26f, depth, 201);
        var roots = RingPoints("WendigoHowlRootPoints", 14, inner + .45f, outer - .5f, 16f, 35f, depth, 251);
        // Побеги — у подножия шипов: кольцо идёт кустами с просветами, как у цели.
        var shoots = ClusterPoints("WendigoHowlShootPoints", thorns, 2, .6f, inner + .2f, 18f, 35f, depth, 301);
        // Пыль кольца катится наружу по земле от внешнего края.
        var rim = RingPoints("WendigoHowlDustPoints", 22, outer - 1.1f, outer - .3f, 70f, 84f, depth, 401);
        var root = new GameObject(Howl);
        Spikes(root, "Thorns", kit.Thorns, thorns, kit.Wood, life, .95f, 1.5f, 1.2f, 1.6f,
            Curve(0f, 0f, .012f, 0f, .045f, 1.12f, .075f, 1f, .64f, 1f, .86f, 0f, 1f, 0f),
            Curve(0f, 0f, .035f, 0f, .075f, 1.12f, .105f, 1f, .68f, 1f, .92f, 0f, 1f, 0f));
        Spikes(root, "Roots", kit.Roots, roots, kit.Moss, life, .75f, 1.1f, 1.0f, 1.3f,
            Curve(0f, 0f, .02f, 0f, .06f, 1.1f, .09f, 1f, .62f, 1f, .84f, 0f, 1f, 0f),
            Curve(0f, 0f, .045f, 0f, .09f, 1.1f, .12f, 1f, .66f, 1f, .9f, 0f, 1f, 0f));
        Spikes(root, "Shoots", kit.Shoots, shoots, kit.Wood, life, .45f, .8f, .6f, .85f,
            Curve(0f, 0f, .008f, 0f, .04f, 1.15f, .07f, 1f, .6f, 1f, .8f, 0f, 1f, 0f),
            Curve(0f, 0f, .05f, 0f, .09f, 1.15f, .12f, 1f, .7f, 1f, .9f, 0f, 1f, 0f));
        var soil = Decal(root, "Soil", kit.Splat, thorns.vertexCount, Vector3.zero, 1.0f, 1.5f, 2.2f, .6f,
            new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .6f), 0f, .03f + depth);
        FromPoints(soil, thorns, false, 0f);
        var cracks = Decal(root, "Cracks", kit.Crack, roots.vertexCount, Vector3.zero, 1.1f, 1.6f, 2.2f, .6f,
            new Color(SoilDark.r, SoilDark.g, SoilDark.b, .9f), 0f, .04f + depth);
        FromPoints(cracks, roots, false, 0f);
        // Пыль кольца: мягкое облако, тёплая охра, мало альфы, у самой земли —
        // лежачие клубы, стоячих немного и они низкие.
        var dust = Dust(root, kit, "Dust", 14, Vector3.up * depth, Vector3.up, 0f, .1f, .2f, .5f, .8f, 1.2f, 1.0f, 1.5f, .20f, .02f, false, kit.Haze);
        dust.transform.localRotation = Quaternion.identity;
        FromPoints(dust, thorns, false, .7f);
        var dustLow = Dust(root, kit, "DustLow", 22, Vector3.up * (depth + .03f), Vector3.up, 0f, .1f, .3f, .7f, 1.6f, 2.4f, 1.4f, 2.0f, .20f, .03f, true, kit.Haze);
        dustLow.transform.localRotation = Quaternion.identity;
        FromPoints(dustLow, rim, false, .3f);
        // Пыль кольца темнее и теплее общей: на траве светлая охра читалась серо-зелёной.
        foreach (var ps in new[] { dust, dustLow })
        {
            var tone = ps.main;
            tone.startColor = new ParticleSystem.MinMaxGradient(new Color(.72f, .53f, .34f, ps == dust ? .22f : .20f),
                new Color(.58f, .42f, .27f, ps == dust ? .22f : .20f));
        }
        var clods = Debris(root, "Clods", kit.Clod, 26, Vector3.up * (depth + .1f), Vector3.up, 0f, .1f, 3.0f, 5.5f, .12f, .25f, 1.6f, 0f, SoilLight, SoilDark);
        clods.transform.localRotation = Quaternion.identity;
        FromPoints(clods, thorns, false, .35f);
        var bark = Debris(root, "Bark", kit.Bark, 10, Vector3.up * (depth + .1f), Vector3.up, 0f, .1f, 3.0f, 5.0f, .12f, .22f, 1.3f, .01f, BarkLight, BarkDark);
        bark.transform.localRotation = Quaternion.identity;
        FromPoints(bark, roots, false, .5f);
        var leaves = Leaves(root, kit, "Leaves", 10, Vector3.up * (depth + .15f), Vector3.up, 0f, 2.0f, 3.5f, .03f);
        leaves.transform.localRotation = Quaternion.identity;
        FromPoints(leaves, roots, false, .6f);
        Save(root);
    }

    /// <summary>
    /// Дыхание воя: бледная дымка изо рта черепа вперёд-вверх. Корень — у
    /// кости головы на ударе, +Z — взгляд зверя.
    /// </summary>
    private static void SaveHowlBreath(Kit kit)
    {
        var root = new GameObject(HowlBreath);
        var mist = Particles(root, "Mist", 8, .8f, 1.2f, 1.4f, 2.4f, .25f, .45f, 0f);
        mist.transform.localRotation = Aim(new Vector3(0f, .35f, 1f));
        var main = mist.main;
        main.gravityModifier = -.03f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.93f, .95f, .90f, .17f), new Color(.84f, .88f, .84f, .12f));
        var shape = mist.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = .08f;
        var emission = mist.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4), new ParticleSystem.Burst(.12f, (short)4) });
        var drag = mist.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.3f);
        drag.dampen = .12f;
        var size = mist.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .3f, 1.4f, 1f, 2.4f));
        var color = mist.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.1f, .35f);
        var noise = mist.noise; noise.enabled = true;
        noise.strength = .25f; noise.frequency = 1.2f;
        Sheet(mist, 2, 3.99f);
        var renderer = mist.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Dust;
        Save(root);
    }
}
