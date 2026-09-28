using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Кислая лужа гнилого плода Плюй-плода по целевому кадру владельца
/// (ART/…/4.forest-butonranged/vfx_target_frames_2026-09-27/2-acid-puddle-flat.png):
/// кандидат V5: густая глянцевая кислота, неровная мокрая кромка, объёмные
/// надувающиеся и лопающиеся пузыри, остаток настоящего плода со смещением.
///
/// Тело — авторская RGBA-текстура на сетке рельефа. Дополнительные брызги,
/// дымка, ошмётки и кольца лопающихся пузырей — CFXR/Hovl. Шейдер поверхности
/// раскрывает маску растекания по Sim-возрасту и учитывает главный свет сцены.
/// Игровая V4 отклонена владельцем; художественная приёмка V5 ещё ожидается.
///
/// Собирает в Assets/Resources/VFX/ForestPuddle:
///   VFX_Puddle_Acid — лужа целиком от падения плода до ухода: брызги и
///     ошмётки в первый миг, лужа растекается за 9 тиков созревания, пузыри и
///     дымка всю жизнь, кромка кислоты по радиусу опасной области (1,2 м);
///   VFX_Puddle_Pulse — тик кислоты: короткая волна от центра к кромке и
///     лопнувшие пузыри (раз в 15 тиков, ритм урона виден).
/// Корень на земле; системы — местные оси, постоянное зерно: ForestPuddleView
/// ведёт их Simulate по возрасту от тика Sim.
/// </summary>
public static class ForestPuddleVfxSetup
{
    private const string Revision = "ForestPuddleVfxV5";
    private const string Root = "Assets/Resources/VFX/ForestPuddle";
    private const string MaterialFolder = Root + "/Materials";
    private const string LiquidTexture = Root + "/Textures/AcidPuddleSurface.png";
    private const string FruitPrefab = "Assets/Resources/Characters/Forest_Bud/Forest_Bud_Projectile.prefab";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrGlowSoft = CfxrGraphics + "cfxr proc glow soft ab.mat";
    private const string CfxrRing = CfxrGraphics + "cfxr proc ring ab.mat";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string HovlTextures = "Assets/Hovl Studio/HSFiles/Textures/";

    public const string AcidName = "VFX_Puddle_Acid", PulseName = "VFX_Puddle_Pulse";

    // Тона кадра: кислая зелень поверх тёмной мокрой земли, ошмётки плода рыже-бурые.
    // V2 (съёмка 28.09): V1 читалась лаймовым неоном без кромки — кислота темнее и гуще,
    // земля шире лужи и темнее, дымка втрое прозрачнее и ниже (не мутит героя).
    private static readonly Color AcidLight = new Color(.66f, .88f, .025f), AcidDeep = new Color(.34f, .56f, .015f);
    private static readonly Color RimSoil = new Color(.20f, .15f, .09f), FruitLight = new Color(.86f, .46f, .20f),
        FruitDark = new Color(.52f, .30f, .14f), Haze = new Color(.62f, .80f, .34f);

    private sealed class Kit { public Material Soil, Glow, Ring, Haze, Chunk, Blob, Bubble, Liquid, Drop; }

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += () => Install(false);

    [MenuItem("Разлом/Плюй-плод/VFX луж: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Плюй-плод/VFX луж: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrGlowSoft) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrRing) == null)
        {
            Debug.LogWarning("[puddle-vfx] Нет пака CFXR — лужа Плюй-плода не собрана.");
            return;
        }
        if (!File.Exists(LiquidTexture) || Shader.Find(Game.View.ForestPuddleSurface.ShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.DropShaderName) == null
            || Shader.Find(Game.View.ForestPuddleSurface.FruitShaderName) == null)
        {
            Debug.LogWarning("[puddle-vfx] Нет текстуры AcidPuddleSurface или шейдеров густой кислоты — V5 не собрана.");
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
            && AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + PulseName + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "ForestPuddle");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        ConfigureLiquidTexture();
        var kit = new Kit
        {
            Soil = Textured(Plain(PackCopy("M_Puddle_Soil", CfxrSmokeBlurred)), HovlTextures + "Crater2.png"),
            Glow = Plain(PackCopy("M_Puddle_Acid", CfxrGlowSoft)),
            // V3: процедурное кольцо CFXR без своих данных частиц невидимо — кромка и
            // пузыри из масок Hovl одним каналом (диск с кромкой Circle17, тонкое кольцо
            // Circle38), тело лужи — неровное пятно Circle18.
            Ring = SingleChannel(PackCopy("M_Puddle_Ring", CfxrTrailMaterial), HovlTextures + "Circle17.png"),
            Bubble = SingleChannel(PackCopy("M_Puddle_Bubble", CfxrTrailMaterial), HovlTextures + "Circle38.png"),
            Blob = SingleChannel(PackCopy("M_Puddle_Blob", CfxrTrailMaterial), HovlTextures + "Circle18.png"),
            Haze = Plain(PackCopy("M_Puddle_Haze", CfxrSmokeBlurred)),
            Chunk = PackCopy("M_Puddle_Chunk", CfxrDebrisUnlit),
            Liquid = LiquidMaterial(),
            Drop = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Puddle_Drop.mat",
                Shader.Find(Game.View.ForestPuddleSurface.DropShaderName)),
        };
        // Декали — до пузырей и дымки.
        foreach (var decal in new[] { kit.Soil, kit.Glow, kit.Ring, kit.Blob })
            if (decal != null) { decal.renderQueue = 2990; EditorUtility.SetDirty(decal); }
        SaveAcid(kit);
        SavePulse(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(Root + "/" + AcidName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[puddle-vfx] Густая лужа по кадру 2, остаток плода и тик кислоты собраны, ревизия " + Revision + ".");
    }

    private static void ConfigureLiquidTexture()
    {
        var importer = AssetImporter.GetAtPath(LiquidTexture) as TextureImporter;
        if (importer == null) return;
        bool changed = importer.textureType != TextureImporterType.Default || !importer.sRGBTexture
            || importer.alphaSource != TextureImporterAlphaSource.FromInput || !importer.alphaIsTransparency
            || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048;
        if (!changed) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static Material LiquidMaterial()
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Puddle_Liquid.mat",
            Shader.Find(Game.View.ForestPuddleSurface.ShaderName));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LiquidTexture));
        material.SetFloat("_Opacity", 1f); material.SetFloat("_Spread", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------- materials

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

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят пятно у земли.</summary>
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

    /// <summary>Плёнка CFXR одним каналом с маской Hovl: цвет — из частиц, форма — из маски.</summary>
    private static Material SingleChannel(Material material, string texturePath)
    {
        if (material == null) return null;
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Textured(Material material, string texturePath)
    {
        if (material == null) return null;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", 0f);
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

    /// <summary>Пятно на земле: лежит плашмя, растекается за spread секунд, держится life и тает с fadeFrom.</summary>
    private static void Decal(GameObject root, string name, Material material, float size, float life, float spread,
        float fadeFrom, Color color, float lift, float fudge)
    {
        if (material == null) return;
        var particles = Particles(root, name, 1, life, life, 0f, 0f, size, size, 0f);
        particles.transform.localPosition = Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var sizeOver = particles.sizeOverLifetime; sizeOver.enabled = true;
        float k = Mathf.Clamp01(spread / life);
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .35f, k, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeFrom);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = fudge;
    }

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Лужа целиком. Жизнь: 9 тиков созревания (растекается), 105 тиков кислоты,
    /// 9 тиков угасания — 4,1 с. Кромка кислоты — кольцо ровно по радиусу урона.
    /// </summary>
    private static void SaveAcid(Kit kit)
    {
        var root = new GameObject(AcidName);
        const float arm = 9f / 30f, life = 123f / 30f;
        float radius = Game.Sim.Simulation.PuddleRadius.ToFloat();

        // The authored liquid sits on a terrain-sampled mesh. No horizontal
        // glow billboards under it: those floated above slopes and read as fog.
        root.AddComponent<Game.View.ForestPuddleSurface>().ConfigureAssets(kit.Liquid, kit.Drop,
            AssetDatabase.LoadAssetAtPath<GameObject>(FruitPrefab));

        // Брызги кислоты и ошмётки лопнувшего плода — первые 0,6 с.
        var splash = Particles(root, "Splash", 14, .45f, .7f, 1.6f, 3.2f, .05f, .11f, 0f);
        var splashMain = splash.main;
        splashMain.gravityModifier = 1.3f;
        splashMain.startColor = new ParticleSystem.MinMaxGradient(new Color(AcidLight.r, AcidLight.g, AcidLight.b, .9f),
            new Color(AcidDeep.r, AcidDeep.g, AcidDeep.b, .85f));
        var splashShape = splash.shape; splashShape.enabled = true;
        splashShape.shapeType = ParticleSystemShapeType.Cone; splashShape.angle = 55f; splashShape.radius = .3f;
        var splashRenderer = splash.GetComponent<ParticleSystemRenderer>();
        splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        splashRenderer.sharedMaterial = kit.Glow;
        if (kit.Chunk != null)
        {
            var chunks = Particles(root, "Fruit Chunks", 9, .9f, 1.3f, 1.4f, 2.8f, .07f, .14f, 0f);
            var main = chunks.main;
            main.gravityModifier = 1.5f;
            main.startColor = new ParticleSystem.MinMaxGradient(FruitLight, FruitDark);
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

        // Пузыри: всю жизнь лужи вспухают и лопаются кольцами по её телу.
        if (kit.Bubble != null)
        {
            var bubbles = Particles(root, "Bubbles", 60, .35f, .6f, .05f, .2f, .09f, .2f, arm);
            var main = bubbles.main;
            main.duration = life - arm - .4f;
            main.gravityModifier = -.05f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(AcidLight.r, AcidLight.g, AcidLight.b, .8f),
                new Color(.80f, .95f, .50f, .7f));
            var emission = bubbles.emission;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            emission.rateOverTime = 3f;
            var shape = bubbles.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius * .85f; shape.radiusThickness = 1f;
            var size = bubbles.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .3f, .8f, 1f, 1f, 1.25f));
            var fade = bubbles.colorOverLifetime; fade.enabled = true;
            fade.color = Alpha(.15f, .8f);
            var renderer = bubbles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Bubble;
            bubbles.transform.localPosition = Vector3.up * .06f;
        }

        // Низкая кислая дымка — тонко, чтобы не мутить героя.
        if (kit.Haze != null)
        {
            var haze = Particles(root, "Haze", 10, 1.0f, 1.5f, .04f, .1f, .6f, .9f, arm);
            var main = haze.main;
            main.duration = life - arm - 1.2f;
            main.gravityModifier = -.02f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(Haze.r, Haze.g, Haze.b, .035f));
            var emission = haze.emission;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            emission.rateOverTime = 5f;
            var shape = haze.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius * .7f;
            var size = haze.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, 1f, 1.3f));
            var fade = haze.colorOverLifetime; fade.enabled = true;
            fade.color = Alpha(.25f, .6f);
            var sheet = haze.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = 2; sheet.numTilesY = 2; sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f); sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var renderer = haze.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = kit.Haze;
            haze.transform.localPosition = Vector3.up * .15f;
        }
        Save(root);
    }

    /// <summary>Тик кислоты: светлая волна от центра к кромке за 0,25 с и лопнувшие пузыри.</summary>
    private static void SavePulse(Kit kit)
    {
        var root = new GameObject(PulseName);
        float radius = Game.Sim.Simulation.PuddleRadius.ToFloat();
        if (kit.Ring != null)
        {
            // The pulse travels within the conformed liquid shader. The pack
            // contributes only the little popped-bubble rings above the surface.
            var pops = Particles(root, "Pops", 6, .25f, .4f, .2f, .5f, .12f, .22f, 0f);
            pops.transform.localPosition = Vector3.up * .08f;
            var popMain = pops.main; popMain.startColor = new Color(.80f, .95f, .45f, .8f);
            var popShape = pops.shape; popShape.enabled = true;
            popShape.shapeType = ParticleSystemShapeType.Circle; popShape.radius = radius * .8f;
            var popSize = pops.sizeOverLifetime; popSize.enabled = true;
            popSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, 1f, 1.4f));
            var popFade = pops.colorOverLifetime; popFade.enabled = true; popFade.color = Alpha(0f, .4f);
            var popRenderer = pops.GetComponent<ParticleSystemRenderer>();
            popRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            popRenderer.sharedMaterial = kit.Bubble != null ? kit.Bubble : kit.Ring;
        }
        Save(root);
    }
}
