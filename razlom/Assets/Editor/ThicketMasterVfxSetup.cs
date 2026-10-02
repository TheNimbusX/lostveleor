using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ЭФФЕКТЫ АТАК ХОЗЯИНА ЧАЩИ (план artifacts/tools/wf/boss-vfx-plan.md §3, 02.10).
///
/// Собирает 26 префабов в Resources/VFX/ThicketMaster/Attacks/Prefabs из паков —
/// CFXR (комья, щепки, пыль-облака, листья, дуга когтей, искры, лепестки, сок,
/// ветровые штрихи), Hovl (маски трещин, колец и пятен земли, горб HalfSphere2,
/// столб CylinderFromGround, цветок Flower) — на своих копиях материалов в
/// Attacks/Materials: без освещения CFXR (в URP бледнеет), без dissolve и мягких
/// частиц у декалей (без depth-текстуры гаснут у земли); свои цвета и масштабы.
/// Корни и шипы — свои трубы в коре RootBark (как у Корнехвата и Вендиго), ягода —
/// своя икосфера на URP Particles/Lit. Наш слой — раскладка, тайминг, отклик.
///
/// Круги «от тела» (лапа, топот и его кольцо, рёв, нырок) и круги касты (прорастание,
/// ливень, пыльца, свет бури) берутся из констант Simulation — ревизия включает их,
/// и смена радиуса в Sim пересобирает префабы сама.
///
/// Меню «Разлом/Босс/Хозяин Чащи/Собрать эффекты» — пересборка целиком (идемпотентна:
/// материалы и меши перезаписываются на месте). После компиляции — само, если сборки
/// нет или ревизия старая. В Play не собирает. В консоль — что собрано и чего не нашлось.
/// Звука нет.
/// </summary>
public static class ThicketMasterVfxSetup
{
    private const string RevisionBase = "ThicketVfxV1";
    private const string VfxFolder = "Assets/Resources/VFX";
    private const string BossFolder = VfxFolder + "/ThicketMaster";
    private const string Root = BossFolder + "/Attacks";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string Log = "[thicketmaster-vfx] ";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrPrefabs = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrCloudBlur = CfxrGraphics + "cfxr cloud blur.png";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string CfxrGlowSoft = CfxrGraphics + "cfxr proc glow soft ab.mat";
    private const string CfxrGlowCrisp = CfxrGraphics + "cfxr proc glow crisp ab.mat";
    private const string CfxrMagicStar = CfxrGraphics + "cfxr magic star hdr ab.mat";
    private const string CfxrPetal = CfxrGraphics + "cfxr petal pink x4 ab lit normal.mat";
    private const string CfxrWind = CfxrGraphics + "cfxr stretch smoke fume blur ab.mat";
    private const string CfxrStreak = CfxrGraphics + "cfxr stretch trait blur ab.mat";
    private const string CfxrJuice = CfxrGraphics + "cfxr blood splash dissolve ab.mat";
    private const string CfxrJuiceTexture = CfxrGraphics + "cfxr blood splash.png";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrSlashMeshes = CfxrMeshes + "cfxr mesh sword_slashes.fbx";
    private const string CfxrTrailPrefab = CfxrPrefabs + "Sword Trails/Plain/CFXR4 Sword Trail PLAIN (360 Spiral).prefab";
    private const string CfxrSplashPrefab = CfxrPrefabs + "Liquids/CFXR2 Blood Shape Splash.prefab";
    private const string Hovl = "Assets/Hovl Studio/HSFiles/";
    private const string HovlTextures = Hovl + "Textures/";
    private const string HovlModels = Hovl + "Models/";
    private const string RootBarkTexture = "Assets/Resources/VFX/RootSnarer/Textures/RootBark.png";
    private const string BarkFallback = "Assets/Fantasy Forest Environment Free Sample/Textures/bark01_bottom.tga";

    // Палитра плана §1.2 (как у Корнехвата и Вендиго). За порог блума 1,05 выходит только
    // красный канал янтаря и ядро дуги — героя и тело ничто не высветляет.
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f), SoilDark = new Color(.30f, .20f, .12f);
    private static readonly Color DustLight = new Color(.76f, .58f, .39f), DustDark = new Color(.58f, .43f, .28f);
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f), LeafDark = new Color(.42f, .52f, .20f);
    private static readonly Color BarkLight = new Color(.66f, .52f, .36f), BarkDark = new Color(.45f, .33f, .22f);
    private static readonly Color RockLight = new Color(.50f, .38f, .27f), RockDark = new Color(.34f, .25f, .17f);
    private static readonly Color PollenGold = new Color(1f, .84f, .38f), PollenDeep = new Color(.90f, .66f, .20f);
    private static readonly Color BerryRed = new Color(.86f, .12f, .10f), BerryDeep = new Color(.52f, .05f, .08f), Juice = new Color(.70f, .06f, .14f);
    private static readonly Color PetalWhite = new Color(1f, .96f, .92f), PetalPink = new Color(1f, .70f, .80f), PetalGold = new Color(1f, .86f, .45f);
    private static readonly Color ShaftCore = new Color(1.25f, 1.12f, .80f), ShaftEdge = new Color(1f, .86f, .45f);
    private static readonly Color Ivory = new Color(1.16f, 1.07f, .88f);
    private static readonly Color CrackTone = new Color(SoilDark.r * .7f, SoilDark.g * .7f, SoilDark.b * .7f, .95f);

    private sealed class Kit
    {
        public Material Clod, Splinter, Leaf, Haze, Soil, Crack, Star, Furrow, Ring, RingThin, RingRibbon, Slash, Spark, Drop,
            StarMote, Petal, Wind, Streak, Juice, JuiceStain, Berry, Flower, Pillar, Wood, WoodDark;
        public Mesh LeafMesh, HalfSphere, Cylinder, FlowerMesh, BerryMesh, ArcThick, ArcEdge;
        public Mesh[] Spikes, Curls, Tips;
    }

    private static readonly List<string> Missing = new List<string>();
    private static readonly List<string> Built = new List<string>();
    private static readonly List<string> Failed = new List<string>();

    /// <summary>Ревизия с радиусами Sim: сменился круг в Sim — сборка устарела.</summary>
    private static string Revision
    {
        get
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return RevisionBase + "/" + string.Join(",", new[]
            {
                Simulation.ThicketPawRadius.ToFloat(), Simulation.ThicketStompRadius.ToFloat(), Simulation.ThicketStompRingOuterRadius.ToFloat(),
                Simulation.ThicketRoarInnerRadius.ToFloat(), Simulation.ThicketRoarOuterRadius.ToFloat(), Simulation.ThicketDiveRadius.ToFloat(),
                Simulation.ThicketSproutRadius.ToFloat(), Simulation.ThicketRainRadius.ToFloat(), Simulation.ThicketPollenRadius.ToFloat(),
                Simulation.ThicketStormSafeRadius.ToFloat(), ThicketMasterCombatView.BodyScale,
            }.Select(f => f.ToString("0.###", c)));
        }
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += () => Install(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => Install(false);
        };
    }

    [MenuItem("Разлом/Босс/Хозяин Чащи/Собрать эффекты")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!force && IsBuilt()) return;
        string[] required = { CfxrDebrisUnlit, CfxrSmokeBlurred, CfxrTrailMaterial, CfxrCloudBlur, HovlTextures + "Crater19.png" };
        var absent = new List<string>();
        foreach (string path in required) if (AssetDatabase.LoadMainAssetAtPath(path) == null) absent.Add(path);
        if (absent.Count > 0)
        {
            Debug.LogWarning(Log + "Эффекты атак НЕ собраны: нет ассетов паков CFXR/Hovl —\n  " + string.Join("\n  ", absent));
            return;
        }
        Build();
    }

    private static bool IsBuilt()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterCombatView.PawSlashName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in ThicketMasterCombatView.AttackPrefabNames)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static void Build()
    {
        Missing.Clear(); Built.Clear(); Failed.Clear();
        PelagWhirlwindVfxSetup.EnsureFolder(VfxFolder, "ThicketMaster");
        PelagWhirlwindVfxSetup.EnsureFolder(BossFolder, "Attacks");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var kit = Materials();
        Geometry(kit);

        Step(ThicketMasterCombatView.PawSlashName, () => SavePawSlash(kit));
        Step(ThicketMasterCombatView.PawImpactName, () => SavePawImpact(kit));
        Step(ThicketMasterCombatView.StompRearName, () => SaveStompRear(kit));
        Step(ThicketMasterCombatView.StompQuakeName, () => SaveStompQuake(kit));
        Step(ThicketMasterCombatView.StompOuterName, () => SaveStompOuter(kit));
        Step(ThicketMasterCombatView.DiveBurstName, () => SaveDiveBurst(kit));
        Step(ThicketMasterCombatView.MoundName, () => SaveMound(kit));
        Step(ThicketMasterCombatView.DiveTremorName, () => SaveDiveTremor(kit));
        Step(ThicketMasterCombatView.EmergeName, () => SaveEmerge(kit));
        Step(ThicketMasterCombatView.SproutPressName, () => SaveSproutPress(kit));
        Step(ThicketMasterCombatView.SproutTremorName, () => SaveSproutTremor(kit));
        Step(ThicketMasterCombatView.SproutSpikesName, () => SaveSproutSpikes(kit));
        Step(ThicketMasterCombatView.PollenShakeName, () => SavePollenShake(kit));
        Step(ThicketMasterCombatView.PollenFallName, () => SavePollenFall(kit));
        Step(ThicketMasterCombatView.PollenCloudName, () => SavePollenCloud(kit));
        Step(ThicketMasterCombatView.BushPuffName, () => SaveBushPuff(kit));
        Step(ThicketMasterCombatView.BerryName, () => SaveBerry(kit));
        Step(ThicketMasterCombatView.BerrySplatName, () => SaveBerrySplat(kit));
        Step(ThicketMasterCombatView.CrownShedName, () => SaveCrownShed(kit));
        Step(ThicketMasterCombatView.StormVortexName, () => SaveStormVortex(kit));
        Step(ThicketMasterCombatView.LightPillarName, () => SaveLightPillar(kit));
        Step(ThicketMasterCombatView.StormWaveName, () => SaveStormWave(kit));
        Step(ThicketMasterCombatView.RoarInhaleName, () => SaveRoarInhale(kit));
        Step(ThicketMasterCombatView.RoarBlastName, () => SaveRoarBlast(kit));
        Step(ThicketMasterCombatView.WakeTearName, () => SaveWakeTear(kit));
        Step(ThicketMasterCombatView.DeathBloomName, () => SaveDeathBloom(kit));

        AssetDatabase.SaveAssets();
        // Ловушка 25.09: свежесохранённые материалы держатся в памяти не теми — переимпорт с диска.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterCombatView.PawSlashName));
        if (importer != null && Failed.Count == 0 && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        string report = Log + $"Собрано {Built.Count} из {ThicketMasterCombatView.AttackPrefabNames.Length} префабов в {PrefabFolder}, ревизия {Revision}.";
        if (Failed.Count > 0) report += "\n  НЕ собраны (ошибка выше): " + string.Join(", ", Failed);
        if (Missing.Count > 0) report += "\n  Не нашлось в паках (взята замена или слой пропущен):\n    " + string.Join("\n    ", Missing);
        if (Failed.Count > 0) Debug.LogError(report);
        else if (Missing.Count > 0) Debug.LogWarning(report);
        else Debug.Log(report + " Всё из паков на месте.");
    }

    private static void Step(string name, System.Action save)
    {
        try
        {
            save();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) != null) Built.Add(name);
            else Failed.Add(name);
        }
        catch (System.Exception e)
        {
            Failed.Add(name);
            Debug.LogException(e);
        }
    }

    private static void Note(string what)
    {
        if (!Missing.Contains(what)) Missing.Add(what);
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        kit.Clod = PackCopy("M_Thicket_Clod", CfxrDebrisUnlit);
        kit.Splinter = PackCopy("M_Thicket_Splinter", CfxrDebrisWood) ?? kit.Clod;
        kit.Leaf = Unlit(PackCopy("M_Thicket_Leaf", CfxrLeafMaterial)) ?? kit.Clod;
        kit.Haze = Textured(Plain(PackCopy("M_Thicket_Haze", CfxrSmokeBlurred)), CfxrCloudBlur, true);
        kit.Soil = Textured(Plain(PackCopy("M_Thicket_Soil", CfxrSmokeBlurred)), HovlTextures + "Crater40.png", false);
        kit.Crack = Textured(NoDissolve(PackCopy("M_Thicket_Crack", CfxrTrailMaterial)), HovlTextures + "Crack4.png", true);
        kit.Star = Textured(NoDissolve(PackCopy("M_Thicket_Star", CfxrTrailMaterial)), HovlTextures + "Crater19.png", true);
        kit.Furrow = Textured(NoDissolve(PackCopy("M_Thicket_Furrow", CfxrTrailMaterial)), HovlTextures + "Crack5.png", true);
        kit.Ring = Textured(NoDissolve(PackCopy("M_Thicket_Ring", CfxrTrailMaterial)), HovlTextures + "Circle17.png", true);
        kit.RingThin = Textured(NoDissolve(PackCopy("M_Thicket_RingThin", CfxrTrailMaterial)), HovlTextures + "Circle41.png", true);
        kit.RingRibbon = Textured(NoDissolve(PackCopy("M_Thicket_RingRibbon", CfxrTrailMaterial)), HovlTextures + "Circle82.png", true);
        kit.Pillar = Textured(NoDissolve(PackCopy("M_Thicket_Pillar", CfxrTrailMaterial)), HovlTextures + "Trail25.png", true);
        // Дуга когтей — плёнка пака с её dissolve вдоль UV.x (принятый подход Вихря и Раскола).
        kit.Slash = PackCopy("M_Thicket_Slash", CfxrTrailMaterial);
        kit.Spark = Plain(PackCopy("M_Thicket_Spark", CfxrGlowSoft)) ?? kit.Haze;
        kit.Drop = Plain(PackCopy("M_Thicket_Drop", CfxrGlowCrisp)) ?? kit.Spark;
        kit.StarMote = Plain(PackCopy("M_Thicket_StarMote", CfxrMagicStar)) ?? kit.Spark;
        kit.Wind = Plain(PackCopy("M_Thicket_Wind", CfxrWind)) ?? kit.Haze;
        kit.Streak = Plain(PackCopy("M_Thicket_Streak", CfxrStreak)) ?? kit.Drop;
        // Лепесток: одним каналом — цвет целиком из частиц (белые, розовые, золотые из одной текстуры).
        kit.Petal = Unlit(PackCopy("M_Thicket_Petal", CfxrPetal));
        if (kit.Petal != null && kit.Petal.HasProperty("_SingleChannel")) kit.Petal.SetFloat("_SingleChannel", 1f);
        if (kit.Petal == null) kit.Petal = kit.Leaf;
        kit.Juice = PackCopy("M_Thicket_Juice", CfxrJuice);
        if (kit.Juice != null)
        {
            kit.Juice.DisableKeyword("_FADING_ON");
            if (kit.Juice.HasProperty("_UseSP")) kit.Juice.SetFloat("_UseSP", 0f);
        }
        kit.JuiceStain = Textured(NoDissolve(PackCopy("M_Thicket_JuiceStain", CfxrTrailMaterial)), CfxrJuiceTexture, true);
        kit.Berry = UrpParticles("M_Thicket_Berry", "Universal Render Pipeline/Particles/Lit", null, false) ?? kit.Drop;
        kit.Flower = UrpParticles("M_Thicket_Flower", "Universal Render Pipeline/Particles/Simple Lit", HovlTextures + "Flower2.png", true) ?? kit.Petal;
        kit.Wood = Wood("M_Thicket_Wood", Color.white);
        kit.WoodDark = Wood("M_Thicket_WoodDark", new Color(.80f, .76f, .72f));

        // Декали земли — до пыли и комьев; лепестки и листья — поверх пыли; свет бури — последним.
        foreach (var decal in new[] { kit.Soil, kit.Crack, kit.Star, kit.Furrow, kit.JuiceStain }) Queue(decal, 2990);
        foreach (var ring in new[] { kit.Ring, kit.RingThin, kit.RingRibbon }) Queue(ring, 2995);
        Queue(kit.Leaf, 3005); Queue(kit.Petal, 3006); Queue(kit.Pillar, 3008); Queue(kit.StarMote, 3008);
        return kit;
    }

    private static void Queue(Material material, int queue)
    {
        if (material == null) return;
        material.renderQueue = queue;
        EditorUtility.SetDirty(material);
    }

    /// <summary>Копия материала пака в Attacks/Materials (перезаписывается при пересборке); нет источника — null.</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        if (source == null) { Note(sourcePath + " (материал " + name + ")"); return null; }
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
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
        if (material == null) return null;
        NoDissolve(material);
        material.DisableKeyword("_FADING_ON");
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material NoDissolve(Material material)
    {
        if (material == null) return null;
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
        if (material == null) return null;
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

    /// <summary>Маска Hovl (или своя текстура) на материал CFXR; нет текстуры — материал как есть, отмечено.</summary>
    private static Material Textured(Material material, string texturePath, bool singleChannel)
    {
        if (material == null) return null;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        else Note(texturePath + " (маска " + material.name + ")");
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", singleChannel ? 1f : 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Стандартные частицы URP (ягода — Particles/Lit с блеском; цветок — Particles/Simple Lit
    /// с вырезом по альфе): цвет из частиц, свет URP, без бледности освещённых CFXR.
    /// Слот текстуры без своей карты не пишется (умолчание шейдера — белая).
    /// </summary>
    private static Material UrpParticles(string name, string shaderName, string texturePath, bool clip)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) { Note("шейдер " + shaderName + " (материал " + name + ")"); return null; }
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", clip ? .2f : .65f);
        if (texturePath != null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else Note(texturePath + " (текстура " + name + ")");
        }
        if (clip)
        {
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", .35f);
            material.EnableKeyword("_ALPHATEST_ON");
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Кора корней и шипов: URP Lit с RootBark.png Корнехвата (запасная — кора пака), тень есть.</summary>
    private static Material Wood(string name, Color tint)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Lit"));
        var bark = AssetDatabase.LoadAssetAtPath<Texture2D>(RootBarkTexture);
        if (bark == null)
        {
            Note(RootBarkTexture + " (кора корней; взята " + BarkFallback + ")");
            bark = AssetDatabase.LoadAssetAtPath<Texture2D>(BarkFallback);
        }
        if (bark != null) material.SetTexture("_BaseMap", bark);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", .12f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // -------------------------------------------------------------- geometry

    private static void Geometry(Kit kit)
    {
        kit.LeafMesh = LoadMesh(CfxrLeafMesh, null);
        kit.ArcThick = LoadMesh(CfxrSlashMeshes, "sword_trail 180 thick");
        kit.ArcEdge = LoadMesh(CfxrSlashMeshes, "sword_trail 180 edge") ?? kit.ArcThick;
        // Модели Hovl (замер Blender 02.10, ось +Y вверх, основание в нуле): горб r1 h1,
        // цилиндр r1 h2, цветок ~2,3 × 1,74 × 2,4 — чашкой вверх.
        kit.HalfSphere = LoadMesh(HovlModels + "HalfSphere2.fbx", null);
        kit.Cylinder = LoadMesh(HovlModels + "CylinderFromGround.fbx", "CylinderFromGround");
        kit.FlowerMesh = LoadMesh(HovlModels + "Flower.fbx", null);
        kit.BerryMesh = Icosphere("ThicketBerry");
        // Шипы прорастания (код корня-шипа Вендиго): радиус, загиб, скрутка, узлы, колючки.
        kit.Spikes = new[]
        {
            RootSpike("ThicketSpikeA", 11, .15f, .12f, 1.4f, .10f, 1),
            RootSpike("ThicketSpikeB", 12, .13f, .20f, 2.2f, .12f, 2),
            RootSpike("ThicketSpikeC", 13, .16f, .08f, 1.0f, .08f, 0),
            RootSpike("ThicketSpikeD", 14, .12f, .24f, 2.8f, .16f, 1),
        };
        // Корни выхода из-под земли и пробуждения (код корня Корнехвата): длина, радиус, загиб, узлы, колючки, скрутка.
        kit.Curls = new[]
        {
            CurlRoot("ThicketCurlA", 21, 1.15f, .15f, 1.0f, .16f, 3, 2.2f),
            CurlRoot("ThicketCurlB", 22, .95f, .13f, 1.3f, .18f, 2, 2.8f),
            CurlRoot("ThicketCurlC", 23, 1.30f, .16f, .8f, .14f, 2, 1.8f),
        };
        // Кончики корней под кругом прорастания — высовываются за 6 тиков до шипов.
        kit.Tips = new[]
        {
            CurlRoot("ThicketTipA", 31, .26f, .06f, .9f, .12f, 1, 2f),
            CurlRoot("ThicketTipB", 32, .22f, .05f, 1.2f, .14f, 0, 2.6f),
        };
    }

    /// <summary>Меш из файла пака; nameSuffix — подмеш по окончанию имени (FBX с несколькими мешами).</summary>
    private static Mesh LoadMesh(string path, string nameSuffix)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh && (nameSuffix == null || mesh.name.EndsWith(nameSuffix, System.StringComparison.Ordinal))) return mesh;
        Note(path + (nameSuffix != null ? " → меш «" + nameSuffix + "»" : " (меш)"));
        return null;
    }

    /// <summary>Ягода: икосфера одного деления (80 треугольников), радиус 0,5, белый Color32 (Float32-цвет ломает меш-частицу).</summary>
    private static Mesh Icosphere(string name)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var points = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var cache = new Dictionary<long, int>();
        int Middle(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int found)) return found;
            points.Add(((points[a] + points[b]) * .5f).normalized);
            cache[key] = points.Count - 1;
            return points.Count - 1;
        }
        for (int i = 0; i < 12; i++) points[i] = points[i].normalized;
        var triangles = new List<int>();
        for (int f = 0; f < faces.Length; f += 3)
        {
            int a = faces[f], b = faces[f + 1], c = faces[f + 2];
            int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
            triangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var uv = new List<Vector2>();
        foreach (var p in points)
        {
            vertices.Add(p * .5f);
            colors.Add(new Color32(255, 255, 255, 255));
            uv.Add(new Vector2(.5f + Mathf.Atan2(p.z, p.x) / (2f * Mathf.PI), .5f + p.y * .5f));
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Корень, загнутый к +Z (код Корнехвата): основание под землёй (−0,14 м), растёт по +Y,
    /// угол от вертикали curl·s^1,7; сечение с долями и скруткой, пята, узлы, колючки.
    /// </summary>
    private static Mesh CurlRoot(string name, int salt, float length, float radius, float curl, float knots, int thorns, float twist)
    {
        const int rings = 18;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        var centers = new Vector3[rings + 1];
        var radii = new float[rings + 1];
        var p = new Vector3(0f, -.14f, 0f);
        centers[0] = p;
        for (int r = 1; r <= rings; r++)
        {
            float s = (r - .5f) / rings;
            float phi = curl * Mathf.Pow(s, 1.7f);
            float wobble = .18f * Mathf.Sin(s * 5f + phase);
            p += new Vector3(wobble, Mathf.Cos(phi), Mathf.Sin(phi)).normalized * (length / rings);
            centers[r] = p;
        }
        for (int r = 0; r <= rings; r++)
        {
            float s = r / (float)rings;
            radii[r] = radius * Mathf.Pow(1f - s, .75f)
                * (1f + .5f * Mathf.Pow(Mathf.Max(0f, 1f - s / .16f), 2f))
                * (1f + knots * (Mathf.Sin(s * 13f + phase) + .5f * Mathf.Sin(s * 29f + 2f * phase)) * (1f - s)) + .004f;
        }
        return Tube(name, salt, centers, radii, 9, twist, thorns, length * .9f);
    }

    /// <summary>Труба вдоль хребта (код Корнехвата): рамки переносом, сечение с долями, колючки; кора u — вокруг, v — вдоль.</summary>
    private static Mesh Tube(string name, int salt, Vector3[] centers, float[] radii, int sides, float twist, int thorns, float vLength)
    {
        int rings = centers.Length - 1;
        float phase = Hash01(salt, 11) * Mathf.PI * 2f;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        var tangents = new Vector3[rings + 1];
        var normals = new Vector3[rings + 1];
        var binormals = new Vector3[rings + 1];
        Vector3 carried = Vector3.zero;
        for (int r = 0; r <= rings; r++)
        {
            Vector3 tangent = (centers[Mathf.Min(rings, r + 1)] - centers[Mathf.Max(0, r - 1)]).normalized;
            Vector3 normal = r == 0 ? Vector3.Cross(tangent, Vector3.forward) : carried - tangent * Vector3.Dot(carried, tangent);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right);
            normal.Normalize();
            carried = normal;
            tangents[r] = tangent; normals[r] = normal; binormals[r] = Vector3.Cross(normal, tangent);
        }
        var white = new Color32(255, 255, 255, 255);
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .24f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(centers[r] + (normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a)) * radii[r] * lobe);
                uv.Add(new Vector2(s / (float)sides, t * vLength));
                colors.Add(white);
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        for (int k = 0; k < thorns; k++)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(.22f, .7f, (k + Hash01(salt, 20 + k)) / thorns) * rings), 1, rings - 1);
            float a = Hash01(salt, 30 + k) * Mathf.PI * 2f;
            Vector3 outward = normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a);
            Vector3 axis = (outward * .8f + tangents[r] * .6f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangents[r]).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = centers[r] + outward * radii[r] * .7f;
            float baseRadius = radii[r] * .4f, length = Mathf.Max(.06f, radii[r] * (1.1f + .6f * Hash01(salt, 40 + k)));
            int start = vertices.Count;
            float v0 = r / (float)rings * vLength;
            for (int s = 0; s <= 5; s++)
            {
                float b = s / 5f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s * .06f, v0)); colors.Add(white);
                vertices.Add(root + axis * length); uv.Add(new Vector2(s * .06f, v0 + .1f)); colors.Add(white);
                if (s < 5) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        var meshNormals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            meshNormals[a] = meshNormals[b] = (meshNormals[a] + meshNormals[b]).normalized;
        }
        mesh.normals = meshNormals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Корень-шип (код Вендиго): длина 1 по −Z, основание в нуле — меш-частица с
    /// выравниванием по направлению смотрит −Z вдоль нормали точки. Кора: v = 0 у земли.
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
            RingFrame(spine, t, out Vector3 center, out _, out Vector3 side, out Vector3 other);
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
        var colors = new List<Color32>();
        for (int i = 0; i < vertices.Count; i++) { vertices[i] = upright * vertices[i]; colors.Add(new Color32(255, 255, 255, 255)); }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
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

    /// <summary>Точки эмиссии: вершины — места, нормали — направления, треугольники вырожденные.</summary>
    private static Mesh Points(string name, List<Vector3> positions, List<Vector3> directions)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(positions);
        mesh.SetNormals(directions);
        var colors = new List<Color32>();
        for (int i = 0; i < positions.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
        mesh.SetColors(colors);
        var triangles = new int[((positions.Count + 2) / 3) * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = Mathf.Min(i, positions.Count - 1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>count мест по кольцу [inner, outer], направление — вверх с наклоном наружу tiltMin…tiltMax.</summary>
    private static Mesh RingPoints(string name, int count, float inner, float outer, float tiltMin, float tiltMax, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, Hash01(i, salt + 1));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Шипы прорастания: центр + count−1 мест на r·0,3–0,9, наклон наружу 10–35°.</summary>
    private static Mesh SpikePoints(string name, int count, float radius, int salt)
    {
        var positions = new List<Vector3> { Vector3.down * .1f };
        var directions = new List<Vector3> { Vector3.up };
        for (int i = 1; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .6f) / (count - 1) * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(10f, 35f, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius * Mathf.Lerp(.3f, .9f, Hash01(i, salt + 1)) + Vector3.down * .1f);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Холм смерти: count мест в эллипсе 4,2 × 5,7 м ×BS (длинная ось — вдоль взгляда тела), направление вверх.</summary>
    private static Mesh HillPoints(string name, int count, int salt)
    {
        var places = new List<Vector3>();
        var ups = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float a = Hash01(i, salt) * Mathf.PI * 2f, d = Mathf.Sqrt(Hash01(i, salt + 1));
            places.Add(new Vector3(Mathf.Cos(a) * 2.1f * ThicketMasterCombatView.BodyScale * d, 0f,
                Mathf.Sin(a) * 2.85f * ThicketMasterCombatView.BodyScale * d));
            ups.Add(Vector3.up);
        }
        return Points(name, places, ups);
    }

    /// <summary>Детерминированный разброс: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
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
                new GradientAlphaKey(1f, Mathf.Max(fadeIn + .001f, fadeOut)), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    private static Quaternion Aim(Vector3 direction) => Quaternion.FromToRotation(Vector3.forward, direction.normalized);

    private static GameObject Child(GameObject parent, string name, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = local;
        return go;
    }

    /// <summary>Система с одним залпом, местное пространство, постоянное зерно: вид переигрывает её по возрасту.</summary>
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

    private static void World(ParticleSystem particles)
    {
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
    }

    /// <summary>Эмиссия потоком: rate частиц в секунду seconds секунд (вместо залпа).</summary>
    private static void Stream(ParticleSystem particles, float rate, float seconds, int max)
    {
        var main = particles.main;
        main.duration = Mathf.Max(.05f, seconds);
        main.maxParticles = Mathf.Max(1, max);
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = rate;
    }

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

    /// <summary>Комья, камни, щепки: спрайты CFXR 3×3, баллистика, отскок от земли ground, тают в конце.</summary>
    private static ParticleSystem Debris(GameObject host, Transform ground, string name, Material material, int count, Vector3 at,
        Vector3 direction, float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity,
        float delay, Color light, Color dark)
    {
        var particles = Particles(host, name, count, 1.3f, 1.9f, speedMin, speedMax, sizeMin, sizeMax, delay);
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
        Collide(particles, ground, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR без освещения, кувыркаются, планируют и ложатся на землю ground.</summary>
    private static ParticleSystem Leaves(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at,
        Vector3 direction, float cone, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, 1.8f, 2.4f, speedMin, speedMax, .16f, .26f, delay);
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
        if (ground != null) Collide(particles, ground, .05f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>Мягкое облако CFXR (пыль, пыльца): малая альфа, тормозит, растёт и тает. flat — лежит на земле.</summary>
    private static ParticleSystem Dust(GameObject host, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay, bool flat)
        => Cloud(host, kit, name, count, at, cone, radius, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, delay, flat,
            new Color(DustLight.r, DustLight.g, DustLight.b, alpha), new Color(DustDark.r, DustDark.g, DustDark.b, alpha));

    private static ParticleSystem Cloud(GameObject host, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float delay, bool flat, Color a, Color b)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        // Без мягких частиц земля срезала бы стоячее облако прямой линией — поднимаем.
        particles.transform.localPosition = flat ? at : at + Vector3.up * (.45f * sizeMax);
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.25f);
        drag.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .25f, .95f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.08f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.flip = new Vector3(.5f, .5f, 0f);
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Haze;
        renderer.sortingFudge = flat ? 1f : -1f;
        return particles;
    }

    /// <summary>Пятно на земле: горизонтальный билборд, быстро раскрывается, тает с fadeOut (доля жизни).</summary>
    private static ParticleSystem Decal(GameObject host, string name, Material material, float sizeMin, float sizeMax,
        float life, float fadeOut, Color color, float delay, float lift)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, sizeMin, sizeMax, delay);
        particles.transform.localPosition = Vector3.up * lift;
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

    /// <summary>
    /// Кольцо волны: горизонтальный билборд маски Hovl одним каналом, растёт от from до
    /// 1 за grow доли жизни. size — поперечник частицы: билборд лёжа рисуется ≈ 1/√2 от
    /// startSize (проба 29.09), поэтому зовущие дают 2r·√2.
    /// </summary>
    private static ParticleSystem Wave(GameObject host, string name, Material material, float size, float life,
        float from, float grow, Color color, float delay)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, size, size, delay);
        particles.transform.localPosition = Vector3.up * .05f;
        var main = particles.main;
        main.startColor = color;
        main.startRotation = 0f;
        var shape = particles.shape; shape.enabled = false;
        var curve = particles.sizeOverLifetime; curve.enabled = true;
        curve.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, from, grow * .6f, Mathf.Lerp(from, 1f, .86f), grow, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, .45f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 3f;
        return particles;
    }

    /// <summary>Искры и пылинки: всплывают и гаснут (alpha-blend — героя не высветляют).</summary>
    private static ParticleSystem Motes(GameObject host, string name, Material material, int count, Vector3 at, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color color, float delay, bool ring)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = -.06f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, color.a * .9f),
            new Color(color.r * .85f, color.g * .9f, color.b * .7f, color.a * .7f));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ring ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
        shape.angle = ring ? 0f : 20f;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.8f);
        drag.dampen = .15f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .4f, .2f, 1f, 1f, .2f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.1f, .55f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -2f;
        return particles;
    }

    /// <summary>Разноцветие лепестков: 50 % белых, 35 % розовых, 15 % золотых — случайный цвет из ступенчатого градиента.</summary>
    private static ParticleSystem.MinMaxGradient PetalColors(float alpha)
    {
        var gradient = new Gradient { mode = GradientMode.Fixed };
        gradient.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .5f), new GradientColorKey(PetalPink, .85f), new GradientColorKey(PetalGold, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>Лепестки: атлас 2×2 одним каналом, кувыркаются в 3D, медленно падают кружась.</summary>
    private static ParticleSystem Petals(GameObject host, Kit kit, string name, int count, Vector3 at, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, .10f, .22f, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = .08f;
        main.startColor = PetalColors(1f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
        spin.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.2f);
        drag.dampen = .1f;
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .4f; noise.frequency = .6f; noise.scrollSpeed = .3f;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.05f, .8f);
        Sheet(particles, 2, 3.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Petal;
        return particles;
    }

    /// <summary>Эмиссия из вершин меша по порядку (Loop): каждое место — одна частица.</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, bool align, float randomDirection)
    {
        particles.transform.localRotation = Quaternion.identity;
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
    /// Шипы-корни (код Вендиго): частица на точку, ось — нормаль точки, меш из набора
    /// (−Z — длина, основание в нуле). Растёт только длина: кривые early/late по доле жизни.
    /// </summary>
    private static ParticleSystem Spikes(GameObject root, string name, Mesh[] meshes, Mesh points, Material material, float life,
        float lengthMin, float lengthMax, float thickMin, float thickMax, AnimationCurve early, AnimationCurve late)
    {
        var particles = Particles(root, name, points.vertexCount, life, life, 0f, 0f, lengthMin, lengthMax, 0f);
        var main = particles.main;
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

    /// <summary>Растущий корень: MeshRenderer-ребёнок корня префаба «Grow|мс|°|подпись» (RootSnarerCombatView.AnimateGrows).</summary>
    private static void GrowChild(GameObject root, string label, Mesh mesh, Material material, Vector3 position, Quaternion rotation,
        float scale, int delayMs, int twistDegrees)
    {
        if (mesh == null) return;
        var go = new GameObject(RootSnarerCombatView.GrowPrefix + delayMs + RootSnarerCombatView.GrowSeparator + twistDegrees
            + RootSnarerCombatView.GrowSeparator + label);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>
    /// Префаб в Attacks/Prefabs. Перед сохранением у всех систем — постоянное зерно (вид меняет его
    /// от номера действия: повтор удара — тот же кадр), без автозапуска и без петли.
    /// </summary>
    private static void Save(GameObject root)
    {
        try
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps.useAutoRandomSeed)
                {
                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = Seed(root.name + "/" + ps.name);
                }
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(root.name));
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------- prefabs: paw, stomp

    private const float BS = ThicketMasterCombatView.BodyScale;
    private const float Root2 = 1.4142f;

    /// <summary>Наклон дуги когтей вокруг оси удара: правый край выше — замах «сверху-сбоку вниз к земле».</summary>
    private const float PawTiltDegrees = 20f;

    /// <summary>
    /// Дуга когтей одного удара серии. Корень — в центре тела, +Z — середина сектора удара;
    /// левая лапа — тот же префаб, вид ставит корню X = −1. Две дуги CFXR «sword_trail 180»
    /// (thick — радиусом лапы ×0,95, edge — ×0,8 выше и на 0,02 с позже) на копии плёнки с
    /// dissolve вдоль UV.x: голова бежит по дуге. Полумеш лежит в XY (выпуклость +X), «Swing»
    /// кладёт его плашмя выпуклостью вперёд и наклоняет на 20°.
    /// TODO (смотреть в игре): направление бега головы — если дуга бежит слева направо у правой
    /// лапы, поменять знак PawSpin или startDegrees 180.
    /// </summary>
    private static void SavePawSlash(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PawSlashName);
        float reach = Simulation.ThicketPawRadius.ToFloat();
        if (kit.ArcThick == null || AssetDatabase.LoadAssetAtPath<GameObject>(CfxrTrailPrefab) == null)
        {
            Note(CfxrTrailPrefab + " / меш sword_trail 180 — дуги когтей нет, остаётся только уголь EnemyBodyTelegraphView");
            Save(root);
            return;
        }
        var swing = Child(root, "Swing", new Vector3(0f, 1f * BS, 0f));
        swing.transform.localRotation = Quaternion.Euler(0f, 0f, PawTiltDegrees) * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(90f, 0f, 0f);
        const float spin = -4f;
        Tint(PelagWhirlwindVfxSetup.AddCfxrArc(swing, "Arc", kit.ArcThick, kit.Slash, reach * .95f, .40f, 0f, 0f, spin, 0f), .85f);
        // Местная +Z дуги после X90 смотрит вниз: −0,35 — выше.
        Tint(PelagWhirlwindVfxSetup.AddCfxrArc(swing, "Edge", kit.ArcEdge, kit.Slash, reach * .8f, .36f, .02f, 0f, spin, -.35f), .6f);
        Save(root);
    }

    /// <summary>Цвет дуги по жизни: ядро слоновой кости → тёмная листва, альфа уходит к 0,35 с.</summary>
    private static void Tint(ParticleSystem arc, float opacity)
    {
        var color = arc.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Ivory, 0f), new GradientColorKey(Ivory, .25f), new GradientColorKey(LeafLight, .6f), new GradientColorKey(LeafDark, 1f) },
            new[] { new GradientAlphaKey(opacity, 0f), new GradientAlphaKey(opacity, .45f), new GradientAlphaKey(0f, .88f) });
        color.color = gradient;
        var main = arc.main;
        main.startColor = Color.white;
    }

    /// <summary>Удар лапы о землю: корень — под пальцами лапы. Звезда трещин, пыль, комья, щепки, листья.</summary>
    private static void SavePawImpact(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PawImpactName);
        var ground = root.transform;
        Decal(root, "Star", kit.Star, 1.8f * BS * Root2, 1.9f * BS * Root2, 1.4f, .55f, CrackTone, 0f, .04f);
        Decal(root, "Soil", kit.Soil, 1.2f * BS, 1.4f * BS, 1.4f, .6f, new Color(SoilDark.r, SoilDark.g, SoilDark.b, .7f), 0f, .03f);
        Dust(root, kit, "Dust", 8, Vector3.zero, 60f, .3f, .8f, 1.8f, 1.0f * BS, 1.6f * BS, .9f, 1.3f, .32f, 0f, false);
        Dust(root, kit, "DustLow", 6, Vector3.up * .03f, 85f, .3f, 1.2f, 2.4f, .9f, 1.3f, .8f, 1.1f, .28f, .01f, true);
        Debris(root, ground, "Clods", kit.Clod, 14, Vector3.up * .08f, new Vector3(0f, 1f, .35f), 45f, .3f, 2.6f, 4.8f, .10f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        Debris(root, ground, "Splinters", kit.Splinter, 4, Vector3.up * .1f, new Vector3(0f, 1f, .2f), 40f, .2f, 2.2f, 3.6f, .10f, .18f, 1.3f, .01f, BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 4, Vector3.up * .15f, Vector3.up, 50f, 1.2f, 2.6f, .02f);
        Save(root);
    }

    /// <summary>Дыбом: из-под задних лап юбка пыли и комья. Корень — центр тела, +Z — взгляд.</summary>
    private static void SaveStompRear(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompRearName);
        var rear = Child(root, "Rear", new Vector3(0f, 0f, -ThicketMasterCombatView.StompRearBack * BS));
        Dust(rear, kit, "Skirt", 12, Vector3.up * .03f, 85f, .8f * BS, 1.6f, 3.0f, 1.0f * BS, 1.6f * BS, 1.0f, 1.3f, .30f, 0f, true);
        Dust(rear, kit, "Puff", 6, Vector3.zero, 55f, .6f, .6f, 1.4f, 1.0f, 1.5f, .9f, 1.2f, .28f, .02f, false);
        Debris(rear, root.transform, "Clods", kit.Clod, 6, Vector3.up * .08f, new Vector3(0f, 1f, -.4f), 45f, .6f, 2.0f, 3.6f, .10f, .2f, 1.6f, 0f, SoilLight, SoilDark);
        Save(root);
    }

    /// <summary>
    /// Топот, круг (r из Sim): кольцо Circle17 разбегается до края за 40 % жизни, кольцо пыли,
    /// комья радиально, сеть трещин Crack4 в центре, звёзды трещин под передними лапами, листья.
    /// Корень — центр тела, +Z — взгляд.
    /// </summary>
    private static void SaveStompQuake(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompQuakeName);
        var ground = root.transform;
        float r = Simulation.ThicketStompRadius.ToFloat();
        Wave(root, "Ring", kit.Ring, 2f * r * Root2, 1.0f, .3f, .4f, new Color(DustLight.r * 1.1f, DustLight.g * 1.05f, DustLight.b, .75f), 0f);
        Decal(root, "Cracks", kit.Crack, 2f * r * .7f, 2f * r * .72f, 2.2f, .65f, CrackTone, 0f, .04f);
        foreach (float side in new[] { -1f, 1f })
        {
            var paw = Child(root, side < 0f ? "Paw L" : "Paw R", new Vector3(.95f * side, 0f, 2f));
            Decal(paw, "Star", kit.Star, 2.2f * Root2, 2.3f * Root2, 2.0f, .6f, CrackTone, 0f, .045f);
            Debris(paw, ground, "Clods", kit.Clod, 6, Vector3.up * .08f, Vector3.up, 45f, .3f, 2.6f, 4.4f, .1f, .2f, 1.6f, 0f, SoilLight, SoilDark);
        }
        var rim = Dust(root, kit, "DustRing", 24, Vector3.up * .03f, 0f, .1f, 1.5f, 3.0f, 1.3f, 2.0f, 1.1f, 1.6f, .30f, .02f, false);
        FromPoints(rim, RingPoints("ThicketStompRimPoints", 24, r * .6f, r, 60f, 80f, 101), false, .3f);
        var clods = Debris(root, ground, "Clods", kit.Clod, 26, Vector3.up * .1f, Vector3.up, 0f, .1f, 4f, 7f, .12f, .26f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketStompClodPoints", 26, r * .2f, r * .5f, 35f, 60f, 111), false, .25f);
        var rocks = Debris(root, ground, "Rocks", kit.Clod, 8, Vector3.up * .1f, Vector3.up, 0f, .1f, 3f, 5f, .2f, .34f, 1.9f, .01f, RockLight, RockDark);
        FromPoints(rocks, RingPoints("ThicketStompRockPoints", 8, r * .3f, r * .6f, 35f, 55f, 121), false, .25f);
        var leaves = Leaves(root, ground, kit, "Leaves", 6, Vector3.up * .15f, Vector3.up, 0f, 2.5f, 4.5f, .03f);
        FromPoints(leaves, RingPoints("ThicketStompLeafPoints", 6, r * .3f, r * .7f, 30f, 60f, 131), false, .5f);
        Save(root);
    }

    /// <summary>Топот, второе кольцо (полоса r..r_out из Sim): пылевая стена, комья наружу, тонкое кольцо до внешнего края.</summary>
    private static void SaveStompOuter(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompOuterName);
        float inner = Simulation.ThicketStompRadius.ToFloat(), outer = Simulation.ThicketStompRingOuterRadius.ToFloat();
        Wave(root, "Ring", kit.RingThin, 2f * outer * Root2, .9f, inner / outer, .35f, new Color(DustLight.r, DustLight.g, DustLight.b, .6f), 0f);
        var wall = Dust(root, kit, "Wall", 28, Vector3.up * .03f, 0f, .1f, 1.2f, 2.4f, 1.4f, 2.2f, 1.0f, 1.4f, .30f, 0f, false);
        FromPoints(wall, RingPoints("ThicketStompWallPoints", 28, inner, outer, 10f, 30f, 141), false, .2f);
        var low = Dust(root, kit, "Low", 18, Vector3.up * .03f, 0f, .1f, 1.0f, 2.0f, 1.2f, 1.8f, .9f, 1.2f, .26f, .02f, true);
        FromPoints(low, RingPoints("ThicketStompLowPoints", 18, inner, outer, 70f, 85f, 151), false, .2f);
        var clods = Debris(root, root.transform, "Clods", kit.Clod, 16, Vector3.up * .1f, Vector3.up, 0f, .1f, 3.5f, 6f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketStompOuterClodPoints", 16, inner, (inner + outer) * .5f, 40f, 65f, 161), false, .25f);
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: dive

    /// <summary>Уход в землю (нос в земле): пятно разрытой земли, комья двумя волнами, щепки, крупная пыль, листья.</summary>
    private static void SaveDiveBurst(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DiveBurstName);
        var ground = root.transform;
        Decal(root, "Soil", kit.Soil, 2.3f * BS * Root2, 2.4f * BS * Root2, 2.4f, .7f, new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .85f), 0f, .03f);
        Decal(root, "Cracks", kit.Star, 2.6f * BS * Root2, 2.7f * BS * Root2, 2.4f, .65f, CrackTone, 0f, .04f);
        Debris(root, ground, "Clods", kit.Clod, 30, Vector3.up * .1f, Vector3.up, 40f, .6f * BS, 4f, 8f, .12f, .30f, 1.6f, 0f, SoilLight, SoilDark);
        Debris(root, ground, "Clods Late", kit.Clod, 12, Vector3.up * .1f, Vector3.up, 35f, .5f * BS, 3f, 6f, .10f, .24f, 1.6f, .2f, SoilLight, SoilDark);
        Debris(root, ground, "Splinters", kit.Splinter, 10, Vector3.up * .1f, Vector3.up, 45f, .5f, 3f, 5.5f, .10f, .2f, 1.3f, .02f, BarkLight, BarkDark);
        Dust(root, kit, "Dust", 10, Vector3.zero, 45f, .8f * BS, 1f, 2.5f, 1.6f, 2.4f, 1.2f, 1.7f, .34f, .03f, false);
        Dust(root, kit, "DustLow", 10, Vector3.up * .03f, 85f, .8f, 2f, 3.5f, 1.2f, 1.8f, 1.0f, 1.4f, .3f, 0f, true);
        Leaves(root, ground, kit, "Leaves", 6, Vector3.up * .2f, Vector3.up, 55f, 2.5f, 4.5f, .04f);
        Save(root);
    }

    /// <summary>
    /// Бугор, ползущий к герою: корень едет за MoundPosition (вид), +Z — ход. «Hump» — горб Hovl
    /// HalfSphere2 (местный, вид его потряхивает); следы — в мире: комья, низкая пыль, трава,
    /// борозда Crack5. Эмиссия следов — потоком по времени: бугор всегда в движении, а поток
    /// не зависит от того, видит ли Simulate смещение эмиттера.
    /// </summary>
    private static void SaveMound(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.MoundName);
        var ground = root.transform;
        var hump = Particles(root, ThicketMasterCombatView.HumpChild, 1, 99f, 99f, 0f, 0f, 1f, 1f, 0f);
        var humpMain = hump.main;
        humpMain.startRotation = 0f;
        humpMain.startColor = new Color(SoilDark.r * 1.1f, SoilDark.g * 1.1f, SoilDark.b * 1.1f, 1f);
        humpMain.startSize3D = true;
        humpMain.startSizeX = .8f * BS; humpMain.startSizeY = .55f * BS; humpMain.startSizeZ = 1f * BS;
        var humpShape = hump.shape; humpShape.enabled = false;
        var humpGrow = hump.sizeOverLifetime; humpGrow.enabled = true;
        humpGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, .0015f, 1.05f, .003f, 1f, 1f, 1f));
        var humpRenderer = hump.GetComponent<ParticleSystemRenderer>();
        if (kit.HalfSphere != null)
        {
            humpRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            humpRenderer.mesh = kit.HalfSphere;
            humpRenderer.alignment = ParticleSystemRenderSpace.Local;
        }
        else humpRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        humpRenderer.sharedMaterial = kit.Soil;

        var clods = Debris(root, ground, "Trail Clods", kit.Clod, 60, Vector3.up * .1f, Vector3.up, 50f, .5f * BS, 1.0f, 2.4f, .08f, .18f, 1.4f, 0f, SoilLight, SoilDark);
        Stream(clods, 18f, 3f, 60); World(clods);
        var dust = Dust(root, kit, "Trail Dust", 30, Vector3.up * .03f, 80f, .6f * BS, .4f, 1.0f, .8f, 1.3f, .9f, 1.3f, .28f, 0f, true);
        Stream(dust, 9f, 3f, 30); World(dust);
        var grass = Leaves(root, ground, kit, "Trail Grass", 16, Vector3.up * .1f, Vector3.up, 55f, .8f, 1.8f, 0f);
        Stream(grass, 4.5f, 3f, 16); World(grass);
        var grassMain = grass.main; grassMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.46f, .54f, .22f), new Color(.30f, .38f, .14f));
        var furrow = Decal(root, "Trail Furrow", kit.Furrow, 1.3f * Root2, 1.5f * Root2, 1.5f, .55f, CrackTone, 0f, .035f);
        Stream(furrow, 3f, 3f, 8); World(furrow);
        Save(root);
    }

    /// <summary>Круг выхода лёг: дрожь — клубы внутри круга (всё чаще), подпрыгивают камешки, растёт сеть трещин.</summary>
    private static void SaveDiveTremor(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DiveTremorName);
        float r = Simulation.ThicketDiveRadius.ToFloat();
        float windup = Simulation.ThicketDiveLockTicks / (float)Simulation.TicksPerSecond;
        var puffs = Dust(root, kit, "Puffs", 12, Vector3.zero, 85f, r * .8f, .2f, .6f, .6f, 1.0f, .5f, .8f, .22f, 0f, false);
        Stream(puffs, 14f, windup, 12);
        var puffRate = puffs.emission; puffRate.rateOverTime = new ParticleSystem.MinMaxCurve(14f, Curve(0f, .15f, 1f, 1f));
        var pebbles = Debris(root, root.transform, "Pebbles", kit.Clod, 14, Vector3.up * .05f, Vector3.up, 10f, r * .75f, .8f, 1.6f, .05f, .10f, 1.6f, 0f, RockLight, RockDark);
        Stream(pebbles, 15f, windup, 14);
        var cracks = Decal(root, "Cracks", kit.Crack, 2f * r * .6f, 2f * r * .62f, windup + .4f, .8f, CrackTone, 0f, .04f);
        var grow = cracks.sizeOverLifetime;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .75f, 1f, 1f, 1f));
        Save(root);
    }

    /// <summary>
    /// Выход из-под земли: комья вверх, щепки, столб пыли, пятно, кольцо пыли до круга нырка,
    /// шесть корней вокруг ямы («Grow|мс|°|…»): выход 0,11 с, держатся до 1 с, уходят 0,3 с (вид).
    /// </summary>
    private static void SaveEmerge(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.EmergeName);
        var ground = root.transform;
        float r = Simulation.ThicketDiveRadius.ToFloat();
        Decal(root, "Soil", kit.Soil, 2.6f * BS * Root2, 2.7f * BS * Root2, 3f, .75f, new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f, .85f), 0f, .03f);
        Decal(root, "Cracks", kit.Star, 3.0f * BS * Root2, 3.1f * BS * Root2, 3f, .7f, CrackTone, 0f, .04f);
        Wave(root, "Ring", kit.Ring, 2f * r * Root2, .8f, .2f, .5f, new Color(DustLight.r, DustLight.g, DustLight.b, .5f), 0f);
        Debris(root, ground, "Clods", kit.Clod, 40, Vector3.up * .1f, Vector3.up, 30f, .8f * BS, 5f, 9f, .12f, .30f, 1.6f, 0f, SoilLight, SoilDark);
        Debris(root, ground, "Rocks", kit.Clod, 10, Vector3.up * .1f, Vector3.up, 35f, .7f, 4f, 6.5f, .2f, .38f, 1.9f, 0f, RockLight, RockDark);
        Debris(root, ground, "Splinters", kit.Splinter, 12, Vector3.up * .1f, Vector3.up, 40f, .6f, 4f, 7f, .10f, .22f, 1.3f, .01f, BarkLight, BarkDark);
        Dust(root, kit, "Column", 10, Vector3.zero, 20f, .9f * BS, 1.5f, 3f, 2f, 3f, 1.3f, 1.8f, .34f, 0f, false);
        var ring = Dust(root, kit, "DustRing", 20, Vector3.up * .03f, 0f, .1f, 1.5f, 3f, 1.2f, 1.8f, 1.0f, 1.4f, .3f, .03f, true);
        FromPoints(ring, RingPoints("ThicketEmergeRimPoints", 20, r * .6f, r, 70f, 85f, 201), false, .25f);
        Leaves(root, ground, kit, "Leaves", 8, Vector3.up * .2f, Vector3.up, 55f, 2.5f, 5f, .03f);
        for (int i = 0; i < 6; i++)
        {
            float angle = (i + (Hash01(i, 211) - .5f) * .5f) / 6f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float at = Mathf.Lerp(1.0f, 1.6f, Hash01(i, 212)) * BS;
            GrowChild(root, "Корень " + i, kit.Curls[i % kit.Curls.Length], i % 3 == 2 ? kit.WoodDark : kit.Wood, outward * at,
                Quaternion.LookRotation(outward, Vector3.up), Mathf.Lerp(.8f, 1.1f, Hash01(i, 213)) * BS,
                Mathf.RoundToInt(Hash01(i, 214) * 60f), 0);
        }
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: sprout

    /// <summary>Жест прорастания — лапы в землю: у каждой передней лапы («Left»/«Right», ставит вид) клубы и комья.</summary>
    private static void SaveSproutPress(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutPressName);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var paw = Child(root, side, Vector3.zero);
            Dust(paw, kit, "Dust", 5, Vector3.up * .03f, 80f, .35f, .8f, 1.6f, .7f, 1.1f, .7f, 1.0f, .30f, 0f, true);
            Debris(paw, paw.transform, "Clods", kit.Clod, 2, Vector3.up * .06f, Vector3.up, 40f, .2f, 1.8f, 3f, .1f, .18f, 1.6f, 0f, SoilLight, SoilDark);
        }
        Save(root);
    }

    /// <summary>
    /// Дрожь под кругом прорастания до его удара (30 тиков): клубы внутри круга всё чаще,
    /// камешки, сеть трещин растёт 0,2 → 1; за 6 тиков до удара высовываются три кончика корней.
    /// </summary>
    private static void SaveSproutTremor(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutTremorName);
        float r = Simulation.ThicketSproutRadius.ToFloat();
        float windup = Simulation.ThicketSproutImpactTicks / (float)Simulation.TicksPerSecond;
        var puffs = Dust(root, kit, "Puffs", 8, Vector3.zero, 85f, r * .7f, .2f, .5f, .5f, .8f, .5f, .7f, .22f, 0f, false);
        Stream(puffs, 9f, windup, 8);
        var puffRate = puffs.emission; puffRate.rateOverTime = new ParticleSystem.MinMaxCurve(9f, Curve(0f, .2f, 1f, 1f));
        var pebbles = Debris(root, root.transform, "Pebbles", kit.Clod, 8, Vector3.up * .05f, Vector3.up, 10f, r * .7f, .7f, 1.4f, .05f, .09f, 1.6f, 0f, RockLight, RockDark);
        Stream(pebbles, 9f, windup, 8);
        var cracks = Decal(root, "Cracks", kit.Crack, 2f * r * Root2 * .8f, 2f * r * Root2 * .82f, windup + .2f, .9f, CrackTone, 0f, .04f);
        var grow = cracks.sizeOverLifetime;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .8f, 1f, 1f, 1f));
        int tipsAt = Mathf.RoundToInt((Simulation.ThicketSproutImpactTicks - 6) * 1000f / Simulation.TicksPerSecond);
        for (int i = 0; i < 3; i++)
        {
            float angle = (i + Hash01(i, 301) * .6f) / 3f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            GrowChild(root, "Кончик " + i, kit.Tips[i % kit.Tips.Length], kit.Wood, outward * r * Mathf.Lerp(.2f, .5f, Hash01(i, 302)),
                Quaternion.LookRotation(outward, Vector3.up), Mathf.Lerp(.5f, .65f, Hash01(i, 303)), tipsAt + i * 30, 0);
        }
        Save(root);
    }

    /// <summary>
    /// Удар круга прорастания: шипы-корни в коре (7 мест: центр и кольцо r·0,3–0,9, наклон наружу),
    /// выходят за 0,1 с, стоят до 0,6 с, уходят к 1,1 с; комья, кольцо пыли, звезда трещин.
    /// </summary>
    private static void SaveSproutSpikes(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutSpikesName);
        var ground = root.transform;
        float r = Simulation.ThicketSproutRadius.ToFloat();
        const float life = 1.6f;
        Spikes(root, "Spikes", kit.Spikes, SpikePoints("ThicketSproutSpikePoints", 7, r, 401), kit.Wood, life, 1.2f, 2.0f, 1.2f, 1.7f,
            Curve(0f, 0f, .01f, 0f, .05f, 1.12f, .07f, 1f, .36f, 1f, .6f, 0f, 1f, 0f),
            Curve(0f, 0f, .03f, 0f, .075f, 1.12f, .095f, 1f, .4f, 1f, .69f, 0f, 1f, 0f));
        Decal(root, "Star", kit.Star, 2f * r * Root2, 2f * r * Root2 * 1.05f, life, .6f, CrackTone, 0f, .04f);
        Decal(root, "Soil", kit.Soil, 1.6f * r, 1.7f * r, life, .6f, new Color(SoilDark.r, SoilDark.g, SoilDark.b, .75f), 0f, .03f);
        var clods = Debris(root, ground, "Clods", kit.Clod, 14, Vector3.up * .1f, Vector3.up, 0f, .1f, 3f, 5.5f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketSproutClodPoints", 14, r * .2f, r * .8f, 20f, 45f, 411), false, .35f);
        var dust = Dust(root, kit, "DustRing", 8, Vector3.up * .03f, 0f, .1f, .6f, 1.4f, 1.0f, 1.5f, .9f, 1.3f, .28f, .02f, true);
        FromPoints(dust, RingPoints("ThicketSproutDustPoints", 8, r * .8f, r * 1.15f, 70f, 85f, 421), false, .3f);
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: pollen

    /// <summary>Крона трясётся: у каждой кроны («Left»/«Right», вид держит их на костях) золотые клубы, искры, листья — в мире.</summary>
    private static void SavePollenShake(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenShakeName);
        float shake = Simulation.ThicketCastGestureTicks / (float)Simulation.TicksPerSecond;
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var gold = Cloud(crown, kit, "Gold", 10, Vector3.zero, 70f, .6f, .3f, .8f, .6f, 1.0f, .9f, 1.3f, 0f, false,
                new Color(PollenGold.r, PollenGold.g, PollenGold.b, .35f), new Color(PollenDeep.r, PollenDeep.g, PollenDeep.b, .30f));
            Stream(gold, 16f, shake, 10); World(gold);
            var goldMain = gold.main; goldMain.gravityModifier = .05f;
            var sparks = Motes(crown, "Sparks", kit.Spark, 15, Vector3.zero, .6f, .4f, 1.2f, .05f, .10f, .8f, 1.2f,
                new Color(PollenGold.r, PollenGold.g, PollenGold.b, .9f), 0f, false);
            Stream(sparks, 25f, shake, 15); World(sparks);
            var sparksMain = sparks.main; sparksMain.gravityModifier = .25f;
            var leaves = Leaves(crown, root.transform, kit, "Leaves", 3, Vector3.zero, Vector3.up, 70f, .6f, 1.4f, .05f);
            World(leaves);
        }
        Save(root);
    }

    /// <summary>Над облаком до его падения: золотой столб с высоты 3,5 м — искры и клубы вниз, расширяются к земле.</summary>
    private static void SavePollenFall(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenFallName);
        float fall = Simulation.ThicketPollenFallTicks / (float)Simulation.TicksPerSecond;
        var top = Child(root, "Column", Vector3.up * 3.5f);
        var sparks = Motes(top, "Sparks", kit.Spark, 24, Vector3.zero, .5f, 3f, 4.5f, .06f, .12f, .7f, .9f,
            new Color(PollenGold.r, PollenGold.g, PollenGold.b, .9f), 0f, false);
        sparks.transform.localRotation = Aim(Vector3.down);
        var sparksMain = sparks.main; sparksMain.gravityModifier = .3f;
        var sparksShape = sparks.shape; sparksShape.angle = 12f;
        var sparksDrag = sparks.limitVelocityOverLifetime; sparksDrag.enabled = false;
        Stream(sparks, 40f, fall * .75f, 24);
        var clouds = Cloud(top, kit, "Clouds", 6, Vector3.zero, 15f, .4f, 2.5f, 3.5f, .6f, .8f, .8f, 1.0f, 0f, false,
            new Color(PollenGold.r, PollenGold.g, PollenGold.b, .30f), new Color(PollenDeep.r, PollenDeep.g, PollenDeep.b, .26f));
        clouds.transform.localPosition = Vector3.zero;
        clouds.transform.localRotation = Aim(Vector3.down);
        var cloudsDrag = clouds.limitVelocityOverLifetime; cloudsDrag.enabled = false;
        var cloudsGrow = clouds.sizeOverLifetime; cloudsGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, 1f, 2.2f));
        Stream(clouds, 10f, fall * .6f, 6);
        Save(root);
    }

    /// <summary>
    /// Облако лежит (зона Sim, 4 с): золотые клубы по диску кружат медленно, пылинки вверх, низкая
    /// плёнка. Эмиссия до (жизнь зоны − 0,6) с, клубы живут ≤ 0,9 с — к концу гаснут сами.
    /// </summary>
    private static void SavePollenCloud(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenCloudName);
        float r = Simulation.ThicketPollenRadius.ToFloat();
        float lies = Simulation.ThicketPollenLifeTicks / (float)Simulation.TicksPerSecond;
        var clouds = Cloud(root, kit, "Clouds", 12, Vector3.zero, 0f, r * .75f, .05f, .2f, 1.4f, 2.2f, .7f, .9f, 0f, false,
            new Color(PollenGold.r, PollenGold.g, PollenGold.b, .38f), new Color(PollenDeep.r, PollenDeep.g, PollenDeep.b, .38f));
        clouds.transform.localPosition = Vector3.up * .45f;
        var cloudsShape = clouds.shape; cloudsShape.shapeType = ParticleSystemShapeType.Circle; cloudsShape.radius = r * .75f;
        clouds.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * Quaternion.identity;
        var orbit = clouds.velocityOverLifetime; orbit.enabled = true;
        orbit.space = ParticleSystemSimulationSpace.Local;
        orbit.orbitalZ = new ParticleSystem.MinMaxCurve(.15f);
        Stream(clouds, 12f, lies - .6f, 12);
        var motes = Motes(root, "Motes", kit.Spark, 14, Vector3.up * .2f, r * .8f, .2f, .5f, .05f, .09f, .9f, 1.2f,
            new Color(PollenGold.r, PollenGold.g, PollenGold.b, .85f), 0f, true);
        Stream(motes, 12f, lies - .6f, 14);
        foreach (int k in new[] { 0, 1 })
        {
            var film = Decal(root, "Film " + k, kit.Haze, 2f * r * 1.1f * Root2, 2f * r * 1.15f * Root2, lies, .85f,
                new Color(PollenGold.r, PollenGold.g, PollenGold.b, .26f), k * .08f, .06f + k * .01f);
            var spin = film.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(k == 0 ? .12f : -.1f);
        }
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: rain

    /// <summary>Пуф куста на залп (корень — на кости куста): листья, красные капли, клубы — в мире.</summary>
    private static void SaveBushPuff(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BushPuffName);
        var leaves = Leaves(root, null, kit, "Leaves", 8, Vector3.zero, Vector3.forward + Vector3.up, 60f, 1.2f, 2.6f, 0f);
        World(leaves);
        var drops = Particles(root, "Drops", 10, .5f, .8f, 1.5f, 3f, .06f, .10f, 0f);
        drops.transform.localRotation = Aim(Vector3.forward + Vector3.up);
        var dropsMain = drops.main; dropsMain.gravityModifier = 1f; dropsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
        var dropsShape = drops.shape; dropsShape.enabled = true; dropsShape.shapeType = ParticleSystemShapeType.Cone; dropsShape.angle = 50f; dropsShape.radius = .3f;
        var dropsRenderer = drops.GetComponent<ParticleSystemRenderer>();
        dropsRenderer.renderMode = ParticleSystemRenderMode.Billboard; dropsRenderer.sharedMaterial = kit.Drop;
        World(drops);
        var puff = Cloud(root, kit, "Puff", 2, Vector3.zero, 50f, .3f, .4f, .9f, .7f, 1.0f, .6f, .8f, 0f, false,
            new Color(LeafLight.r, LeafLight.g, LeafLight.b, .25f), new Color(LeafDark.r, LeafDark.g, LeafDark.b, .22f));
        puff.transform.localPosition = Vector3.zero;
        World(puff);
        Save(root);
    }

    /// <summary>
    /// Ягода в полёте (вид ведёт корень по дуге куст → круг): крупная и две мелкие ягоды —
    /// меш-частицы своей икосферы на URP Particles/Lit, крутятся; капли сока по пути — в мире.
    /// </summary>
    private static void SaveBerry(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BerryName);
        foreach (int k in new[] { 0, 1 })
        {
            int count = k == 0 ? 1 : 2;
            float size = (k == 0 ? .30f : .16f) * BS;
            var berry = Particles(root, k == 0 ? "Big" : "Small", count, 4f, 4f, 0f, 0f, size, size * 1.1f, 0f);
            var main = berry.main;
            main.startColor = k == 0 ? new ParticleSystem.MinMaxGradient(BerryRed) : new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = berry.shape; shape.enabled = k == 1; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .14f * BS;
            var spin = berry.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(4f, 7f); spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f); spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var renderer = berry.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = kit.BerryMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
            renderer.mesh = kit.BerryMesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = kit.Berry;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        var juice = Particles(root, "Juice", 14, .5f, .7f, 0f, .3f, .04f, .06f, 0f);
        var juiceMain = juice.main; juiceMain.gravityModifier = 1f; juiceMain.startColor = new ParticleSystem.MinMaxGradient(Juice, BerryRed);
        var juiceShape = juice.shape; juiceShape.enabled = true; juiceShape.shapeType = ParticleSystemShapeType.Sphere; juiceShape.radius = .12f;
        juice.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Drop;
        Stream(juice, 10f, 1.4f, 14); World(juice);
        Save(root);
    }

    /// <summary>
    /// Шлепок сока в круге залпа: копия CFXR2 Blood Shape Splash (меш шлепка с dissolve, капли
    /// «Stretched»/«Circles») в цветах сока, пятно сока на земле, три кусочка ягоды.
    /// </summary>
    private static void SaveBerrySplat(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BerrySplatName);
        float r = Simulation.ThicketRainRadius.ToFloat();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrSplashPrefab);
        if (source != null)
        {
            var splash = Object.Instantiate(source);
            splash.name = "Splash";
            splash.transform.SetParent(root.transform, false);
            splash.transform.localPosition = Vector3.up * .05f;
            splash.transform.localScale = Vector3.one * (.9f * r / 1.4f);
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(splash);
            foreach (var ps in splash.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                bool sheet = ps.gameObject == splash;
                main.startColor = sheet ? new ParticleSystem.MinMaxGradient(Juice, new Color(.80f, .08f, .14f))
                    : new ParticleSystem.MinMaxGradient(BerryRed, Juice);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                string material = renderer.sharedMaterial.name;
                if (material.Contains("splash") && kit.Juice != null) renderer.sharedMaterial = kit.Juice;
                else if (material.Contains("glow")) renderer.sharedMaterial = kit.Drop;
            }
        }
        else
        {
            Note(CfxrSplashPrefab + " (шлепок сока — только капли)");
            var drops = Particles(root, "Drops", 18, .4f, .6f, 2f, 4f, .06f, .12f, 0f);
            drops.transform.localRotation = Aim(Vector3.up);
            var dropsMain = drops.main; dropsMain.gravityModifier = 1.2f; dropsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, Juice);
            var dropsShape = drops.shape; dropsShape.enabled = true; dropsShape.shapeType = ParticleSystemShapeType.Cone; dropsShape.angle = 60f; dropsShape.radius = .3f;
            drops.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Drop;
        }
        Decal(root, "Stain", kit.JuiceStain, r * 1.2f * Root2, r * 1.3f * Root2, 1.8f, .6f, new Color(Juice.r, Juice.g, Juice.b, .8f), 0f, .035f);
        var bits = Particles(root, "Bits", 3, 1.2f, 1.6f, 1.5f, 3f, .07f * BS, .10f * BS, 0f);
        bits.transform.localPosition = Vector3.up * .1f;
        bits.transform.localRotation = Aim(Vector3.up);
        var bitsMain = bits.main; bitsMain.gravityModifier = 1.4f; bitsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
        var bitsShape = bits.shape; bitsShape.enabled = true; bitsShape.shapeType = ParticleSystemShapeType.Cone; bitsShape.angle = 50f; bitsShape.radius = .2f;
        Collide(bits, root.transform, .3f);
        var bitsRenderer = bits.GetComponent<ParticleSystemRenderer>();
        bitsRenderer.renderMode = kit.BerryMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        bitsRenderer.mesh = kit.BerryMesh;
        bitsRenderer.sharedMaterial = kit.Berry;
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: storm

    /// <summary>
    /// Крона сыплет лепестки всю бурю: «~»-системы у каждой кроны («Left»/«Right»), длительность
    /// эмиссии ставит вид (до LastImpactTick + 30); лепестки в мире, кружат и падают.
    /// </summary>
    private static void SaveCrownShed(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.CrownShedName);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var petals = Petals(crown, kit, ThicketMasterCombatView.TimedPrefix + "Petals", 90, Vector3.zero, 2.2f, 3.0f, .6f, 1.4f, 0f);
            var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .8f;
            Stream(petals, 24f, 4.5f, 90); World(petals);
            Collide(petals, root.transform, .02f);
        }
        Save(root);
    }

    private static float StormSeconds => (Simulation.ThicketStormFirstWaveTicks + Simulation.ThicketStormSecondWaveTicks) / (float)Simulation.TicksPerSecond;

    /// <summary>
    /// Вихрь лепестков по арене: коробка 22 × 2,5 × 17 м вокруг центра поляны (20 × 15), лепестки
    /// кружат вокруг вертикали 0,35 рад/с и поднимаются, ветровые штрихи; поток с пиками к волнам.
    /// </summary>
    private static void SaveStormVortex(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StormVortexName);
        float span = StormSeconds + .5f;
        float first = Simulation.ThicketStormFirstWaveTicks / (float)Simulation.TicksPerSecond / span;
        float second = StormSeconds / span;
        var box = new Vector3(22f, 2.5f, 17f);
        var petals = Petals(root, kit, "Petals", 380, Vector3.up * 1.6f, 2.0f, 2.8f, 0f, .3f, 0f);
        petals.transform.localRotation = Quaternion.identity;
        var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        Stream(petals, 150f, span, 600);
        var rate = petals.emission;
        rate.rateOverTime = new ParticleSystem.MinMaxCurve(150f, Curve(0f, .25f, first - .08f, .5f, first, 1f, first + .1f, .45f,
            second - .08f, .55f, second, 1f, Mathf.Min(.99f, second + .06f), .35f, 1f, .15f));
        var swirl = petals.velocityOverLifetime; swirl.enabled = true; swirl.space = ParticleSystemSimulationSpace.Local;
        swirl.orbitalY = new ParticleSystem.MinMaxCurve(.35f);
        swirl.y = new ParticleSystem.MinMaxCurve(.4f);
        var petalsMain = petals.main; petalsMain.gravityModifier = 0f;
        var wind = Particles(root, "Wind", 40, .8f, 1.2f, 0f, 0f, .5f, .9f, 0f);
        wind.transform.localPosition = Vector3.up * 1.4f;
        var windShape = wind.shape; windShape.enabled = true; windShape.shapeType = ParticleSystemShapeType.Box; windShape.scale = box;
        var windMain = wind.main; windMain.startColor = new Color(PetalWhite.r, PetalWhite.g, PetalWhite.b, .22f);
        var windSwirl = wind.velocityOverLifetime; windSwirl.enabled = true; windSwirl.space = ParticleSystemSimulationSpace.Local;
        windSwirl.orbitalY = new ParticleSystem.MinMaxCurve(.5f);
        var windFade = wind.colorOverLifetime; windFade.enabled = true; windFade.color = Alpha(.2f, .5f);
        var windRenderer = wind.GetComponent<ParticleSystemRenderer>();
        windRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        windRenderer.lengthScale = 4f; windRenderer.velocityScale = .15f;
        windRenderer.sharedMaterial = kit.Wind;
        Stream(wind, 12f, span, 40);
        Save(root);
    }

    /// <summary>
    /// Столб света над кругом-укрытием (r из Sim): цилиндр Hovl CylinderFromGround (r1, h2) с
    /// градиентом Trail25 одним каналом, alpha-blend, ядро ≤ 1,25 по R — героя в круге не
    /// высветляет; диск Circle17 по краю круга, звёздочки вверх («~» — пока держится). Гасит вид.
    /// </summary>
    private static void SaveLightPillar(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.LightPillarName);
        float r = Simulation.ThicketStormSafeRadius.ToFloat();
        const float life = 6f, height = 3.5f;
        if (kit.Cylinder != null)
        {
            var shaft = Particles(root, "Shaft", 1, life, life, 0f, 0f, 1f, 1f, 0f);
            var main = shaft.main;
            main.startRotation = 0f;
            main.startSize3D = true;
            main.startSizeX = r; main.startSizeY = height / 2f; main.startSizeZ = r;
            main.startColor = new Color(ShaftCore.r, ShaftCore.g, ShaftCore.b, .45f);
            var shape = shaft.shape; shape.enabled = false;
            var grow = shaft.sizeOverLifetime; grow.enabled = true; grow.separateAxes = true;
            grow.x = new ParticleSystem.MinMaxCurve(1f); grow.z = new ParticleSystem.MinMaxCurve(1f);
            grow.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, .15f / life, 1f, 1f, 1f));
            var renderer = shaft.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = kit.Cylinder;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = kit.Pillar;
        }
        Decal(root, "Disk", kit.Ring, 2f * r * Root2, 2f * r * Root2, life, .97f, new Color(ShaftEdge.r, ShaftEdge.g, ShaftEdge.b, .5f), 0f, .06f);
        var stars = Motes(root, ThicketMasterCombatView.TimedPrefix + "Stars", kit.StarMote, 18, Vector3.up * .1f, r * .85f, .6f, 1.2f, .10f, .18f,
            1.0f, 1.4f, new Color(ShaftEdge.r, ShaftEdge.g, ShaftEdge.b, .8f), 0f, true);
        Stream(stars, 8f, 2f, 18);
        Save(root);
    }

    /// <summary>Порыв волны бури: лепестки из всей арены наружу-вверх и тормозят, листья, низкая пыль.</summary>
    private static void SaveStormWave(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StormWaveName);
        var box = new Vector3(20f, 1f, 15f);
        var petals = Petals(root, kit, "Petals", 160, Vector3.up * .8f, 1.2f, 1.7f, 0f, 0f, 0f);
        petals.transform.localRotation = Quaternion.identity;
        var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var burst = petals.velocityOverLifetime; burst.enabled = true; burst.space = ParticleSystemSimulationSpace.Local;
        burst.radial = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 6f, .3f, 1.5f, 1f, .3f));
        burst.y = new ParticleSystem.MinMaxCurve(1.5f);
        var drag = petals.limitVelocityOverLifetime; drag.limit = new ParticleSystem.MinMaxCurve(8f);
        var leaves = Leaves(root, root.transform, kit, "Leaves", 30, Vector3.up * .5f, Vector3.up, 70f, 3f, 6f, 0f);
        leaves.transform.localRotation = Quaternion.identity;
        var leafShape = leaves.shape; leafShape.shapeType = ParticleSystemShapeType.Box; leafShape.scale = box;
        var dust = Dust(root, kit, "DustLow", 20, Vector3.up * .03f, 0f, .1f, 1f, 2.5f, 1.4f, 2.2f, 1.0f, 1.4f, .24f, 0f, true);
        dust.transform.localRotation = Quaternion.identity;
        var dustShape = dust.shape; dustShape.shapeType = ParticleSystemShapeType.Box; dustShape.scale = new Vector3(20f, .1f, 15f);
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: roar, wake

    /// <summary>
    /// Вдох рёва (36 тиков): с крон («Left»/«Right») сыплются листья; последние 12 тиков кольцо пыли
    /// на внешнем радиусе рёва стягивается внутрь. Корень — центр тела.
    /// </summary>
    private static void SaveRoarInhale(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.RoarInhaleName);
        float outer = Simulation.ThicketRoarOuterRadius.ToFloat();
        float windup = Simulation.ThicketRoarWindupTicks / (float)Simulation.TicksPerSecond;
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var leaves = Leaves(crown, root.transform, kit, "Leaves", 4, Vector3.zero, Vector3.up, 80f, .3f, .9f, .1f);
            Stream(leaves, 5f, windup * .8f, 4); World(leaves);
        }
        var ring = Dust(root, kit, "Inhale", 24, Vector3.up * .03f, 0f, .1f, 0f, 0f, 1.0f, 1.5f, .45f, .6f, .26f, windup - 12f / Simulation.TicksPerSecond, true);
        FromPoints(ring, RingPoints("ThicketRoarInhalePoints", 24, outer * .9f, outer, 80f, 89f, 501), false, 0f);
        var pull = ring.velocityOverLifetime; pull.enabled = true; pull.space = ParticleSystemSimulationSpace.Local;
        pull.radial = new ParticleSystem.MinMaxCurve(-2.5f);
        var drag = ring.limitVelocityOverLifetime; drag.enabled = false;
        Save(root);
    }

    /// <summary>
    /// Рёв: тонкое кольцо Circle41 от внутреннего до внешнего радиуса (рост 0,3 с, гаснет к 0,5 с),
    /// витое Circle82 следом, пыль полосой кольца наружу, листья с крон вверх-наружу.
    /// </summary>
    private static void SaveRoarBlast(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.RoarBlastName);
        float inner = Simulation.ThicketRoarInnerRadius.ToFloat(), outer = Simulation.ThicketRoarOuterRadius.ToFloat();
        Wave(root, "Ring", kit.RingThin, 2f * outer * Root2, .5f, inner / outer, .6f, new Color(Ivory.r * .9f, Ivory.g * .88f, Ivory.b * .8f, .7f), 0f);
        Wave(root, "Ribbon", kit.RingRibbon, 2f * outer * Root2, .55f, inner / outer, .6f, new Color(DustLight.r, DustLight.g, DustLight.b, .5f), .05f);
        var dust = Dust(root, kit, "Dust", 28, Vector3.up * .03f, 0f, .1f, 1.5f, 3f, 1.2f, 1.8f, .9f, 1.3f, .28f, 0f, false);
        FromPoints(dust, RingPoints("ThicketRoarDustPoints", 28, inner, outer, 60f, 80f, 511), false, .2f);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var leaves = Leaves(crown, root.transform, kit, "Leaves", 15, Vector3.zero, Vector3.up, 60f, 2.5f, 5f, 0f);
            World(leaves);
        }
        Save(root);
    }

    /// <summary>Пробуждение: у передней лапы (корень на земле под пальцами) рвутся пять коротких корней и уходят, комья, пыль, мох.</summary>
    private static void SaveWakeTear(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.WakeTearName);
        var ground = root.transform;
        for (int i = 0; i < 5; i++)
        {
            float angle = (i + Hash01(i, 601) * .6f) / 5f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            GrowChild(root, "Корень " + i, kit.Curls[i % kit.Curls.Length], i % 2 == 0 ? kit.Wood : kit.WoodDark,
                outward * Mathf.Lerp(.3f, .6f, Hash01(i, 602)), Quaternion.LookRotation(outward, Vector3.up),
                Mathf.Lerp(.45f, .6f, Hash01(i, 603)), Mathf.RoundToInt(Hash01(i, 604) * 50f), 0);
        }
        Debris(root, ground, "Clods", kit.Clod, 16, Vector3.up * .08f, Vector3.up, 40f, .4f, 2.4f, 4.4f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        Dust(root, kit, "Dust", 6, Vector3.zero, 55f, .4f, .6f, 1.4f, .9f, 1.3f, .9f, 1.2f, .3f, 0f, false);
        var moss = Leaves(root, ground, kit, "Moss", 6, Vector3.up * .15f, Vector3.up, 50f, 1.4f, 2.8f, .02f);
        var mossMain = moss.main; mossMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.46f, .54f, .22f), new Color(.30f, .38f, .14f));
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: death

    /// <summary>
    /// «Цветущий холм». Корень — где умер, +Z — взгляд тела. Сразу: тёплая вспышка в кусте
    /// («Bush», alpha-blend — тело не высветляет), лепестки с куста и крон. Когда тело легло
    /// (вид ставит «Late …» на такт убийства LandsAt): 26 цветков Hovl и 18 листьев по эллипсу
    /// 4,2 × 5,7 м ×BS, растут за 0,5 с с разбросом до 1 с, лепестки сыплются 3 с; холм держится
    /// до ухода тела + 2 с.
    /// </summary>
    private static void SaveDeathBloom(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DeathBloomName);
        var ground = root.transform;
        var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
        float settle = beat.LandsAt > 0f ? beat.LandsAt
            : beat.HitStopSeconds + EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster).FallSeconds;
        float bloom = Mathf.Max(3f, Mathf.Max(settle + 2f, beat.BodyGoneAt) + 2f - settle);

        var bush = Child(root, ThicketMasterCombatView.BushChild, Vector3.up * 1.5f);
        var flash = Particles(bush, "Flash", 1, .35f, .35f, 0f, 0f, 2f, 2f, 0f);
        var flashMain = flash.main; flashMain.startColor = new Color(1.2f, .75f, .35f, .6f);
        var flashShape = flash.shape; flashShape.enabled = false;
        var flashGrow = flash.sizeOverLifetime; flashGrow.enabled = true; flashGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .3f, 1.1f, 1f, 1.25f));
        var flashFade = flash.colorOverLifetime; flashFade.enabled = true; flashFade.color = Alpha(.08f, .3f);
        flash.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Spark;
        var bushPetals = Petals(bush, kit, "Petals", 40, Vector3.zero, 2.5f, 3.2f, 1.5f, 3.5f, .05f);
        World(bushPetals); Collide(bushPetals, ground, .02f);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.up * 3.3f);
            var petals = Petals(crown, kit, "Petals", 20, Vector3.zero, 2.5f, 3.2f, 1.2f, 3f, .05f);
            World(petals); Collide(petals, ground, .02f);
        }

        // Холм: свои места цветам и листьям (эмиссия Loop берёт вершины с нулевой) по эллипсу вдоль взгляда тела.
        var hill = HillPoints("ThicketDeathFlowerPoints", 26, 701);
        var hillLeaves = HillPoints("ThicketDeathLeafPoints", 18, 711);
        var bloomColors = new Gradient { mode = GradientMode.Fixed };
        bloomColors.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .45f), new GradientColorKey(PetalPink, .8f), new GradientColorKey(BerryRed, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        float growIn = .5f / bloom, shrinkFrom = 1f - .45f / bloom;
        var flowers = Particles(root, ThicketMasterCombatView.LatePrefix + "Flowers", 26, bloom, bloom, 0f, 0f, .25f, .45f, 0f);
        FromPoints(flowers, hill, false, 0f);
        Stream(flowers, 26f, 1f, 26);
        var flowersMain = flowers.main;
        flowersMain.startColor = new ParticleSystem.MinMaxGradient(bloomColors) { mode = ParticleSystemGradientMode.RandomColor };
        flowersMain.startRotation3D = true;
        flowersMain.startRotationX = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        flowersMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        flowersMain.startRotationZ = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        var flowersGrow = flowers.sizeOverLifetime; flowersGrow.enabled = true;
        flowersGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, growIn * .8f, 1.1f, growIn, 1f, shrinkFrom, 1f, 1f, 0f));
        var flowersRenderer = flowers.GetComponent<ParticleSystemRenderer>();
        flowersRenderer.renderMode = kit.FlowerMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        flowersRenderer.mesh = kit.FlowerMesh;
        flowersRenderer.alignment = ParticleSystemRenderSpace.Local;
        flowersRenderer.sharedMaterial = kit.FlowerMesh != null ? kit.Flower : kit.Petal;
        flowersRenderer.shadowCastingMode = ShadowCastingMode.On;

        var leaves = Particles(root, ThicketMasterCombatView.LatePrefix + "Leaves", 18, bloom, bloom, 0f, 0f, .3f, .5f, 0f);
        FromPoints(leaves, hillLeaves, false, 0f);
        Stream(leaves, 18f, 1f, 18);
        var leavesMain = leaves.main;
        leavesMain.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        leavesMain.startRotation3D = true;
        leavesMain.startRotationX = new ParticleSystem.MinMaxCurve(1.2f, 1.9f);
        leavesMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var leavesGrow = leaves.sizeOverLifetime; leavesGrow.enabled = true;
        leavesGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, growIn, 1f, shrinkFrom, 1f, 1f, 0f));
        var leavesRenderer = leaves.GetComponent<ParticleSystemRenderer>();
        leavesRenderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        leavesRenderer.mesh = kit.LeafMesh;
        leavesRenderer.alignment = ParticleSystemRenderSpace.Local;
        leavesRenderer.sharedMaterial = kit.Leaf;

        var falling = Petals(root, kit, ThicketMasterCombatView.LatePrefix + "Falling", 45, Vector3.up * 2.6f, 2.0f, 2.6f, 0f, .3f, 0f);
        falling.transform.localRotation = Quaternion.identity;
        var fallingShape = falling.shape; fallingShape.enabled = true; fallingShape.shapeType = ParticleSystemShapeType.Box;
        fallingShape.scale = new Vector3(4.2f * BS, .5f, 5.7f * BS);
        var fallingMain = falling.main; fallingMain.gravityModifier = .1f;
        Stream(falling, 15f, 3f, 45);
        Collide(falling, ground, .02f);
        Save(root);
    }
}
