using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ЖИВАЯ КИСЛАЯ ЛУЖА Плюй-плода, V6 — по кадру владельца
/// ART/characters/act-1-enemies/review/mobs-v2-concepts-2026-09-29/abilities/10-bud-puddle-alive-b-vapour-crust.png
/// (выбор G10 от 29.09; V5 с нарисованной лаймовой кляксой отклонена: «как наклейка»).
///
/// Что в кадре и откуда это берётся:
///   шлепок — всплеск пака CFXR «Water Splash (Smaller)» в тонах кислоты,
///     ошмётки плода (CFXR debris unlit), капли и осколки кожуры (ForestPuddleSurface);
///   растекание за 9 тиков, мутная кислота, рябь, пузыри — ForestPuddleSurface
///     и шейдер Razlom/Forest Acid Surface;
///   тёмная земля и вялая трава — слой умножения Razlom/Forest Acid Ground
///     и пучки Razlom/Forest Acid Grass (текстура пучка из пака Fantasy Forest);
///   тяжёлый зелёный пар — размытый дым CFXR (smoke cloud x4 ab blurred):
///     низкий ковёр плашмя, клубы и завитки-ленты (trails с плёнкой fume blur);
///   сухая корка у кромки и после высыхания — слой корки того же шейдера земли.
/// Все материалы паков — unlit-копии без dissolve и мягких частиц.
///
/// Собирает в Assets/Resources/VFX/ForestPuddle:
///   VFX_Puddle_Acid — лужа целиком: от падения плода до угасания корки;
///   VFX_Puddle_Pulse — тик кислоты: выдох пара и лопнувшая пена (раз в 15 тиков).
/// Корень на земле; системы — местные оси, постоянное зерно: ForestPuddleView
/// ведёт их Simulate по возрасту от тика Sim и глушит постоянные эмиттеры на
/// тике высыхания.
/// </summary>
public static class ForestPuddleVfxSetup
{
    // V7 (интеграция N, 29.09): корка темнее, плиты мельче, трещины тоньше —
    // в пробе под камерой боя светлые плиты V6 читались мощёной дорожкой.
    private const string Revision = "ForestPuddleVfxV7";
    private const string Root = "Assets/Resources/VFX/ForestPuddle";
    private const string MaterialFolder = Root + "/Materials";
    private const string TuftTexture = Root + "/Textures/WiltedTuft.png";
    private const string FruitPrefab = "Assets/Resources/Characters/Forest_Bud/Forest_Bud_Projectile.prefab";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrBubble = CfxrGraphics + "cfxr bubble ab.mat";
    private const string CfxrFume = CfxrGraphics + "cfxr stretch smoke fume blur ab.mat";
    private const string CfxrHidden = CfxrGraphics + "cfxr hidden.mat";
    private const string CfxrSmudge = CfxrGraphics + "cfxr perlin smudge.png";
    private const string CfxrClouds = CfxrGraphics + "cfxr noise clouds big.png";
    private const string CfxrSplash = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Liquids/CFXR Water Splash (Smaller).prefab";

    public const string AcidName = "VFX_Puddle_Acid", PulseName = "VFX_Puddle_Pulse";

    /// <summary>Остатки V5, которые V6 больше не использует (в Resources они попали бы в сборку).</summary>
    private static readonly string[] Stale =
    {
        MaterialFolder + "/M_Puddle_Acid.mat", MaterialFolder + "/M_Puddle_Blob.mat",
        MaterialFolder + "/M_Puddle_Ring.mat", MaterialFolder + "/M_Puddle_Bubble.mat",
        MaterialFolder + "/M_Puddle_Soil.mat", Root + "/Textures/AcidPuddleSurface.png",
    };

    // Тона кадра (sRGB): мутная оливковая кислота, светлее её — пар, бурая
    // гниющая кожура с бледной мякотью.
    private static readonly Color AcidLight = new Color(.60f, .62f, .20f), AcidMid = new Color(.44f, .46f, .12f),
        Froth = new Color(.74f, .76f, .36f), Vapour = new Color(.52f, .56f, .20f), VapourLight = new Color(.64f, .68f, .28f),
        Rind = new Color(.42f, .17f, .11f), Pulp = new Color(.70f, .64f, .30f);

    private sealed class Kit { public Material Liquid, Scorch, Crust, Grass, Drop, Haze, Wisp, Froth, Chunk, Hidden; }

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += () => Install(false);

    [MenuItem("Разлом/Плюй-плод/VFX луж: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Плюй-плод/VFX луж: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null || AssetDatabase.LoadAssetAtPath<GameObject>(CfxrSplash) == null)
        {
            Debug.LogWarning("[puddle-vfx] Нет пака CFXR — лужа Плюй-плода не собрана.");
            return;
        }
        if (!File.Exists(TuftTexture) || Shader.Find(Game.View.ForestPuddleSurface.ShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.GroundShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.GrassShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.DropShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.FruitShaderName) == null)
        {
            Debug.LogWarning("[puddle-vfx] Нет текстуры пучка WiltedTuft или шейдеров Forest Acid* — V6 не собрана.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(Root + "/" + AcidName + ".prefab");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + AcidName + ".prefab");
        return importer != null && importer.userData == Revision
            && prefab != null && prefab.GetComponent<Game.View.ForestPuddleSurface>() != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Puddle_Liquid.mat") != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Puddle_Scorch.mat") != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Puddle_Crust.mat") != null
            && AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Puddle_Grass.mat") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + PulseName + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "ForestPuddle");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        foreach (string path in Stale)
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        ConfigureTuftTexture();
        var kit = new Kit
        {
            Liquid = LiquidMaterial(),
            Scorch = GroundMaterial("M_Puddle_Scorch", 0, BlendMode.DstColor, BlendMode.Zero, 2984, .03f),
            Crust = GroundMaterial("M_Puddle_Crust", 1, BlendMode.One, BlendMode.OneMinusSrcAlpha, 2986, .035f),
            Grass = GrassMaterial(),
            Drop = DropMaterial(),
            Haze = Plain(PackCopy("M_Puddle_Haze", CfxrSmokeBlurred)),
            Wisp = Plain(PackCopy("M_Puddle_Wisp", CfxrFume)),
            Froth = Plain(PackCopy("M_Puddle_Froth", CfxrBubble)),
            Chunk = PackCopy("M_Puddle_Chunk", CfxrDebrisUnlit),
            Hidden = AssetDatabase.LoadAssetAtPath<Material>(CfxrHidden),
        };
        SaveAcid(kit);
        SavePulse(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(Root + "/" + AcidName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[puddle-vfx] Живая лужа по кадру 10 (пар и корка) и тик кислоты собраны, ревизия " + Revision + ".");
    }

    private static void ConfigureTuftTexture()
    {
        if (AssetImporter.GetAtPath(TuftTexture) == null)
            AssetDatabase.ImportAsset(TuftTexture, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(TuftTexture) as TextureImporter;
        if (importer == null) return;
        bool changed = importer.textureType != TextureImporterType.Default || !importer.sRGBTexture
            || !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp
            || !importer.mipmapEnabled || importer.maxTextureSize != 256;
        if (!changed) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();
    }

    // ------------------------------------------------------------- materials

    private static Material LiquidMaterial()
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Puddle_Liquid.mat",
            Shader.Find(Game.View.ForestPuddleSurface.ShaderName));
        material.SetTexture("_NoiseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrSmudge));
        material.SetColor("_Deep", new Color(.24f, .25f, .04f));
        material.SetColor("_Body", new Color(.38f, .38f, .09f));
        material.SetColor("_Sheen", new Color(.58f, .59f, .18f));
        material.SetColor("_Glint", new Color(.86f, .90f, .55f));
        material.SetColor("_Meniscus", new Color(.17f, .16f, .04f));
        material.SetFloat("_Opacity", 1f); material.SetFloat("_Dry", 0f);
        material.SetFloat("_Radius", Game.Sim.Simulation.PuddleRadius.ToFloat());
        material.SetFloat("_Lift", .05f);
        material.renderQueue = 2995;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Слой земли: 0 — тёмная вялая трава умножением, 1 — корка поверх.</summary>
    private static Material GroundMaterial(string name, int layer, BlendMode source, BlendMode destination, int queue, float lift)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat",
            Shader.Find(Game.View.ForestPuddleSurface.GroundShaderName));
        material.SetTexture("_NoiseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrClouds));
        // Множители тёмной земли подобраны по кадру: трава (81,80,23) → у кромки (54,42,23), дальше (66,55,20).
        material.SetColor("_WetSoil", new Color(.62f, .50f, .80f));
        material.SetColor("_Scorch", new Color(.84f, .72f, .88f));
        material.SetColor("_UnderAcid", new Color(.50f, .42f, .62f));
        material.SetColor("_CrustLight", new Color(.47f, .36f, .19f));
        material.SetColor("_CrustDark", new Color(.37f, .28f, .14f));
        material.SetColor("_Crack", new Color(.21f, .15f, .08f));
        material.SetFloat("_Layer", layer);
        material.SetFloat("_SrcBlend", (float)source);
        material.SetFloat("_DstBlend", (float)destination);
        material.SetFloat("_Fade", 1f); material.SetFloat("_Dry", 0f);
        material.SetFloat("_Radius", Game.Sim.Simulation.PuddleRadius.ToFloat());
        material.SetFloat("_Lift", lift);
        material.renderQueue = queue;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GrassMaterial()
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Puddle_Grass.mat",
            Shader.Find(Game.View.ForestPuddleSurface.GrassShaderName));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TuftTexture));
        material.SetColor("_Fresh", new Color(.80f, .84f, .62f));
        material.SetColor("_Wilted", new Color(.47f, .33f, .15f));
        material.SetColor("_WiltedBase", new Color(.22f, .15f, .07f));
        material.SetFloat("_Cutoff", .42f);
        material.SetFloat("_Opacity", 1f);
        material.renderQueue = 2450;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material DropMaterial()
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Puddle_Drop.mat",
            Shader.Find(Game.View.ForestPuddleSurface.DropShaderName));
        material.SetColor("_Deep", new Color(.20f, .21f, .03f));
        material.SetColor("_Bright", new Color(.50f, .52f, .13f));
        material.SetColor("_Specular", new Color(.95f, .98f, .72f));
        material.SetFloat("_Opacity", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (source == null) return material;
        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят пар у земли.</summary>
    private static Material Plain(Material material)
    {
        if (material == null) return null;
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        material.DisableKeyword("_FADING_ON");
        foreach (string property in new[] { "_UseDissolve", "_UseDissolveOffsetUV", "_UseSP" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------- particles

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

    private static Gradient Alpha(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(.001f, fadeIn)),
                new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    private static ParticleSystem Particles(GameObject root, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(root.name + "/" + name);
        particles.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        return particles;
    }

    /// <summary>
    /// Постоянный эмиттер на всю жизнь лужи: вид глушит его на тике высыхания,
    /// поэтому длительность с запасом (Песочные Часы продлевают лужу).
    /// </summary>
    private static void Continuous(ParticleSystem particles, float rate, int max)
    {
        var main = particles.main;
        main.duration = 30f;
        main.maxParticles = max;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = rate;
    }

    private static void Circle(ParticleSystem particles, float radius, float thickness, float height)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = thickness;
        // Ось Z системы смотрит вверх (см. Particles): подъём — по местной Z.
        shape.position = new Vector3(0f, 0f, height);
    }

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Лужа целиком. Sim: 9 тиков созревания, 105 тиков кислоты, 9 тиков
    /// угасания; вид держит корку ещё 42 тика. Опасная область — 1,2 м.
    /// </summary>
    private static void SaveAcid(Kit kit)
    {
        var root = new GameObject(AcidName);
        float radius = Game.Sim.Simulation.PuddleRadius.ToFloat();
        const float arm = 9f / 30f;

        root.AddComponent<Game.View.ForestPuddleSurface>().ConfigureAssets(kit.Liquid, kit.Scorch, kit.Crust, kit.Grass,
            kit.Drop, AssetDatabase.LoadAssetAtPath<GameObject>(FruitPrefab));

        AddPackSplash(root);

        // Ошмётки лопнувшего плода — бурая кожура и бледная мякоть.
        if (kit.Chunk != null)
        {
            var chunks = Particles(root, "Fruit Chunks", 9, .9f, 1.3f, 1.4f, 2.8f, .07f, .14f, 0f);
            var main = chunks.main;
            main.gravityModifier = 1.5f;
            main.startColor = new ParticleSystem.MinMaxGradient(Rind, Pulp);
            var shape = chunks.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 60f; shape.radius = .25f;
            var spin = chunks.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var collision = chunks.collision; collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes; collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.SetPlane(0, root.transform); collision.bounce = .15f; collision.dampen = .7f; collision.radiusScale = .45f;
            var sheet = chunks.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = 3; sheet.numTilesY = 3; sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f); sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f);
            var renderer = chunks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Chunk;
        }

        if (kit.Haze != null)
        {
            // Тяжёлый пар, нижний ковёр: плашмя над лужей, расползается за кромку
            // и не режется землёй. Первый клуб — вместе со шлепком.
            var carpet = Particles(root, "Vapour Carpet", 30, 2.6f, 3.4f, .06f, .22f, .9f, 1.5f, .08f);
            Continuous(carpet, 6f, 30);
            var main = carpet.main;
            main.startColor = new Color(Vapour.r, Vapour.g, Vapour.b, .22f);
            var emission = carpet.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)5) });
            Circle(carpet, radius * .8f, 1f, .18f);
            var drag = carpet.limitVelocityOverLifetime; drag.enabled = true;
            drag.limit = new ParticleSystem.MinMaxCurve(.12f); drag.dampen = .08f;
            var size = carpet.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, 1f, 1.35f));
            var turn = carpet.rotationOverLifetime; turn.enabled = true; turn.z = new ParticleSystem.MinMaxCurve(-.3f, .3f);
            var fade = carpet.colorOverLifetime; fade.enabled = true; fade.color = Alpha(.2f, .55f);
            Sheet(carpet);
            var renderer = carpet.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = kit.Haze;

            // Клубы: медленно поднимаются и крутятся — пар «дышит» над лужей.
            var plumes = Particles(root, "Vapour Plumes", 12, 1.8f, 2.4f, .02f, .08f, .55f, .9f, arm * .5f);
            Continuous(plumes, 3f, 12);
            var plumeMain = plumes.main;
            plumeMain.startColor = new Color(VapourLight.r, VapourLight.g, VapourLight.b, .16f);
            Circle(plumes, radius * .7f, 1f, .32f);
            var rise = plumes.velocityOverLifetime; rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.Local;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f); rise.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.z = new ParticleSystem.MinMaxCurve(.14f, .26f);
            rise.orbitalX = new ParticleSystem.MinMaxCurve(0f); rise.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            rise.orbitalZ = new ParticleSystem.MinMaxCurve(.35f);
            var plumeSize = plumes.sizeOverLifetime; plumeSize.enabled = true;
            plumeSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, 1f, 1.5f));
            var plumeFade = plumes.colorOverLifetime; plumeFade.enabled = true; plumeFade.color = Alpha(.25f, .5f);
            Sheet(plumes);
            var plumeRenderer = plumes.GetComponent<ParticleSystemRenderer>();
            plumeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            plumeRenderer.sharedMaterial = kit.Haze;
        }

        // Завитки пара — ленты-следы с плёнкой CFXR fume, как «Wind Trails» пака,
        // только медленные и закрученные вокруг лужи.
        if (kit.Wisp != null && kit.Hidden != null)
        {
            var wisps = Particles(root, "Vapour Wisps", 8, 1.6f, 2.2f, .25f, .45f, .28f, .42f, arm);
            Continuous(wisps, 2.2f, 8);
            var main = wisps.main;
            main.startColor = new Color(VapourLight.r, VapourLight.g, VapourLight.b, .18f);
            Circle(wisps, radius * .6f, 1f, .22f);
            var swirl = wisps.velocityOverLifetime; swirl.enabled = true;
            swirl.space = ParticleSystemSimulationSpace.Local;
            swirl.x = new ParticleSystem.MinMaxCurve(0f, 0f); swirl.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            swirl.z = new ParticleSystem.MinMaxCurve(.02f, .06f);
            swirl.orbitalX = new ParticleSystem.MinMaxCurve(0f); swirl.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            swirl.orbitalZ = new ParticleSystem.MinMaxCurve(.9f);
            var noise = wisps.noise; noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(.25f); noise.frequency = .6f; noise.scrollSpeed = .2f;
            noise.damping = true;
            var trails = wisps.trails; trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.lifetime = new ParticleSystem.MinMaxCurve(.8f);
            trails.minVertexDistance = .06f;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.worldSpace = false;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .35f, 1f, 1f, .4f));
            var fade = wisps.colorOverLifetime; fade.enabled = true; fade.color = Alpha(.2f, .6f);
            var renderer = wisps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Hidden;
            renderer.trailMaterial = kit.Wisp;
        }

        // Мелкая пена: пузырьки пака вспухают и лопаются по телу кислоты.
        if (kit.Froth != null)
        {
            var froth = Particles(root, "Froth", 14, .5f, .9f, 0f, .02f, .05f, .12f, arm * .7f);
            Continuous(froth, 10f, 14);
            var main = froth.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(Froth.r, Froth.g, Froth.b, .85f),
                new Color(AcidLight.r, AcidLight.g, AcidLight.b, .75f));
            Circle(froth, radius * .78f, 1f, .07f);
            var size = froth.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .25f, .8f, 1f, 1f, 1.2f));
            var fade = froth.colorOverLifetime; fade.enabled = true; fade.color = Alpha(.1f, .85f);
            var renderer = froth.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Froth;
        }
        Save(root);
    }

    private static void Sheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 2; sheet.numTilesY = 2; sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f); sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
    }

    /// <summary>
    /// Шлепок — всплеск пака CFXR «Water Splash (Smaller)» целиком (корона,
    /// капли вверх с дочерними всплесками и рябью, пена), в тонах кислоты и
    /// крупнее. Скрипт пака снят; зерно у каждой системы постоянное — вид
    /// перематывает верхнюю систему вместе с детьми.
    /// </summary>
    private static void AddPackSplash(GameObject root)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrSplash);
        if (source == null) return;
        var splash = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(splash, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        splash.name = "Splat (CFXR Water Splash)";
        splash.transform.SetParent(root.transform, false);
        splash.transform.localPosition = Vector3.up * .06f;
        splash.transform.localRotation = Quaternion.identity;
        splash.transform.localScale = Vector3.one * 1.45f;
        RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(splash);
        var plain = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var ps in splash.GetComponentsInChildren<ParticleSystem>(true))
        {
            // Пена лежит у земли: мягкие частицы пака (_FADING_ON, гаснут в метре
            // от глубины сцены) съели бы её целиком — берём plain-копии.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.sharedMaterial = PlainSplash(renderer.sharedMaterial, plain);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = Seed(AcidName + "/" + ps.name);
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            bool drops = ps.name.Contains("Drops"), foam = ps.name.Contains("Foam");
            Color tint = foam ? Froth : drops ? AcidMid : AcidLight;
            main.startColor = Tint(main.startColor, tint);
            var overLife = ps.colorOverLifetime;
            if (overLife.enabled) overLife.color = Whiten(overLife.color);
        }
    }

    /// <summary>
    /// Plain-копия материала пака для всплеска: только если у него включены
    /// мягкие частицы. Кольца ряби (процедурный шейдер пака) остаются как есть.
    /// </summary>
    private static Material PlainSplash(Material source, System.Collections.Generic.Dictionary<Material, Material> made)
    {
        if (source == null || !source.IsKeywordEnabled("_FADING_ON")) return source;
        if (made.TryGetValue(source, out var copy)) return copy;
        string path = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(path)) return source;
        string name = "M_Puddle_Splash_" + source.name.Replace("cfxr ", "").Replace(' ', '_');
        copy = Plain(PackCopy(name, path)) ?? source;
        made[source] = copy;
        return copy;
    }

    /// <summary>Цвет старта — тон кислоты, прозрачность — как у пака.</summary>
    private static ParticleSystem.MinMaxGradient Tint(ParticleSystem.MinMaxGradient source, Color tint)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(new Color(tint.r, tint.g, tint.b, source.colorMin.a),
                    new Color(tint.r * .85f, tint.g * .85f, tint.b * .85f, source.colorMax.a));
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                    source.gradient != null ? source.gradient.alphaKeys : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                return new ParticleSystem.MinMaxGradient(gradient);
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(new Color(tint.r, tint.g, tint.b, 1f), new Color(tint.r * .85f, tint.g * .85f, tint.b * .85f, 1f));
            default:
                return new ParticleSystem.MinMaxGradient(new Color(tint.r, tint.g, tint.b, source.color.a));
        }
    }

    /// <summary>Цвет по жизни — только прозрачность пака, без его голубизны.</summary>
    private static ParticleSystem.MinMaxGradient Whiten(ParticleSystem.MinMaxGradient source)
    {
        if (source.mode != ParticleSystemGradientMode.Gradient || source.gradient == null) return source;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, source.gradient.alphaKeys);
        return new ParticleSystem.MinMaxGradient(gradient);
    }

    /// <summary>Тик кислоты: выдох пара над лужей и лопнувшая пена.</summary>
    private static void SavePulse(Kit kit)
    {
        var root = new GameObject(PulseName);
        float radius = Game.Sim.Simulation.PuddleRadius.ToFloat();
        if (kit.Haze != null)
        {
            var breath = Particles(root, "Breath", 3, .9f, 1.2f, .05f, .15f, .6f, .9f, 0f);
            var main = breath.main;
            main.startColor = new Color(VapourLight.r, VapourLight.g, VapourLight.b, .2f);
            Circle(breath, radius * .45f, 1f, .3f);
            var rise = breath.velocityOverLifetime; rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.Local;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f); rise.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.z = new ParticleSystem.MinMaxCurve(.25f, .4f);
            var size = breath.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, 1f, 1.4f));
            var fade = breath.colorOverLifetime; fade.enabled = true; fade.color = Alpha(.15f, .45f);
            Sheet(breath);
            var renderer = breath.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Haze;
        }
        if (kit.Froth != null)
        {
            var pops = Particles(root, "Pops", 6, .25f, .4f, 0f, .05f, .08f, .16f, 0f);
            var main = pops.main;
            main.startColor = new Color(Froth.r, Froth.g, Froth.b, .85f);
            Circle(pops, radius * .8f, 1f, .07f);
            var size = pops.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, 1f, 1.4f));
            var fade = pops.colorOverLifetime; fade.enabled = true; fade.color = Alpha(0f, .4f);
            var renderer = pops.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Froth;
        }
        Save(root);
    }
}
