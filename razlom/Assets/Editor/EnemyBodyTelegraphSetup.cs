using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ЗНАК НА ТЕЛЕ МОБОВ (план «Мобы леса v2», поток E; выбор владельца 29.09 —
/// вариант A: ART/characters/act-1-enemies/review/mobs-v2-concepts-2026-09-29/
/// body-sign/01-a-claw-ember-windup.png, 02-a-three-claw-streaks-impact.png,
/// 05-swarm-crawler-bite-sign.png). Префабы для EnemyBodyTelegraphView.
///
/// • Уголь (VFX_BodySign_Ember): красный ореол, горячее ядро, отблеск-
///   четырёхлучник к концу замаха и редкие искры вверх; к тику удара растёт
///   и в удар вспыхивает, потом гаснет хвостом. Время префаба: замах —
///   EmberWindupSeconds (вид растягивает его на любой замах), хвост —
///   EmberTailSeconds.
/// • Когти (VFX_BodySign_Claw): три изогнутые полосы от когтя к цели, голова
///   бежит от лапы, хвост рвётся эрозией; искры по ходу. Два набора полос
///   (StreaksL / StreaksR) — зеркала под левую и правую лапу, вид включает один.
/// • Укус (VFX_BodySign_Bite): два серпа по бокам пасти смыкаются, вспышка
///   кадр, эрозия; пара искр.
/// • Клыки (VFX_BodySign_Tusk): одна дуга перед мордой кабана, проходит с
///   бока на бок; искры по ходу.
///
/// Паки: CFXR — unlit-свечения «proc glow soft hdr ab» и «p4 add», отблеск
/// «magic star», искры «ember blur», маска плёнки меча «plain» (копии без
/// мягких частиц и dissolve: в URP без depth-текстуры они гасят спрайт).
/// Полосы, серпы и дуга — шейдер серпа Вихря (Razlom/Whirlwind Sweep: полосы
/// поперёк, сужение, голова, эрозия и уход по возрасту частицы), как ленты
/// когтя Вендиго; меши свои, цвет вершин 8-битный. Палитра — тёплая
/// красно-оранжевая: кремовое ядро, оранжевая середина, красная кромка,
/// тонкий тёмно-бордовый обвод (читается на ярком лугу).
///
/// Корни префабов: +Z — направление удара, +Y — вверх. Вид ставит корень и
/// ведёт системы по возрасту от тика Sim.
/// </summary>
public static class EnemyBodyTelegraphSetup
{
    // V2 (интеграция N, 29.09): ядро и отблеск рисуются поверх ореола
    // (sortingFudge), ореол меньше и прозрачнее, ядро полос золотое.
    private const string Revision = "BodySignV2";
    private const string Root = "Assets/Resources/VFX/BodyTelegraph";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrGlowSoft = CfxrGraphics + "cfxr proc glow soft hdr ab.mat";
    private const string CfxrGlowHot = CfxrGraphics + "cfxr proc glow soft hdr p4 add.mat";
    private const string CfxrSparkle = CfxrGraphics + "cfxr magic star hdr ab.mat";
    private const string CfxrEmber = CfxrGraphics + "cfxr ember blur hdr ab.mat";
    private const string CfxrTrailMask = CfxrGraphics + "cfxr sword trail mask plain.png";
    private const string SweepShaderName = "Razlom/Whirlwind Sweep";

    // Палитра следа: цель 02 — белёсо-жёлтое ядро, оранжевая середина,
    // красный край. Ядро уходит за порог блума; обвод тёмный, но тёплый.
    // Ядро золотое, а не кремовое: нейтральный тонмап боя выбеливает
    // (1.45, 1.12, .70) почти в белый, цель 02 — жёлто-золотая сердцевина.
    private static readonly Color Core = new Color(1.40f, .98f, .42f);
    private static readonly Color Mid = new Color(1.40f, .45f, .10f);
    private static readonly Color Edge = new Color(1.12f, .10f, .04f);
    private static readonly Color Rim = new Color(.30f, .045f, .03f);

    private sealed class Kit
    {
        public Material Halo, Hot, Sparkle, Spark, Streak, Bite, Tusk;
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

    [MenuItem("Разлом/Знак на теле мобов/VFX: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Знак на теле мобов/VFX: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Shader.Find(SweepShaderName) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrGlowSoft) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrEmber) == null)
        {
            Debug.LogWarning("[body-sign] Нет пака CFXR или шейдера серпа, знак на теле не собран.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(EnemyBodyTelegraphView.EmberPrefab));
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(EnemyBodyTelegraphView.ClawPrefab)) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(EnemyBodyTelegraphView.BitePrefab)) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(EnemyBodyTelegraphView.TuskPrefab)) != null;
    }

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static void Build()
    {
        EnsureFolder("Assets/Resources/VFX", "BodyTelegraph");
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Geometry");
        var kit = Materials();
        SaveEmber(kit);
        SaveClaw(kit);
        SaveBite(kit);
        SaveTusk(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabPath(EnemyBodyTelegraphView.EmberPrefab));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[body-sign] Уголь, когти, укус и клыки собраны из CFXR и шейдера серпа, ревизия " + Revision + ".");
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        // Свечения CFXR — unlit по природе; HDR-множитель пака умерен: цвет
        // частицы 8-битный, яркость даёт он.
        // Ореол слабее: с 2.2 он тонмапился в плоский красный диск без спада.
        kit.Halo = Hdr(Plain(PackCopy("M_BodySign_Halo", CfxrGlowSoft)), 1.6f);
        kit.Hot = Hdr(Plain(PackCopy("M_BodySign_Hot", CfxrGlowHot)), 3.5f);
        kit.Sparkle = Hdr(Plain(PackCopy("M_BodySign_Sparkle", CfxrSparkle)), 2.5f);
        kit.Spark = Hdr(Plain(PackCopy("M_BodySign_Spark", CfxrEmber)), 3f);
        // Полосы когтей: голова бежит от лапы за .07 с, без кадра-вспышки
        // (вспышка показала бы полосу целиком раньше головы), хвост рвётся с .12.
        kit.Streak = Sweep("M_BodySign_Streak", 1.4f, .7f, 11f);
        Timing(kit.Streak, EnemyBodyTelegraphView.ClawLife, -1f, 0f, .07f, .12f, .36f, .28f, .42f);
        // Серпы укуса: целиком сразу, кадр-вспышка, эрозия с хвоста.
        kit.Bite = Sweep("M_BodySign_Bite", 1f, .75f, 23f);
        Timing(kit.Bite, EnemyBodyTelegraphView.BiteLife, .035f, 1f, .01f, .10f, .30f, .22f, .34f);
        // Дуга клыков: голова идёт с бока на бок за .09 с, толще к голове.
        kit.Tusk = Sweep("M_BodySign_Tusk", 1.6f, .7f, 37f);
        Timing(kit.Tusk, EnemyBodyTelegraphView.TuskLife, -1f, 0f, .09f, .14f, .40f, .30f, .45f);
        return kit;
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

    /// <summary>Без dissolve, мягких частиц, света и дизеринг-теней: только цвет частиц.</summary>
    private static Material Plain(Material material)
    {
        foreach (var keyword in new[] { "_CFXR_DISSOLVE", "_CFXR_DISSOLVE_ALONG_UV_X", "_FADING_ON",
            "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT", "_CFXR_LIGHTING_WPOS_OFFSET",
            "_NORMALMAP", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        foreach (var property in new[] { "_UseDissolve", "_UseDissolveOffsetUV", "_UseSP", "_UseLighting",
            "_UseNormalMap", "_CFXR_DITHERED_SHADOWS" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Hdr(Material material, float multiply)
    {
        if (material.HasProperty("_HdrMultiply")) material.SetFloat("_HdrMultiply", multiply);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Полоса на шейдере серпа Вихря в палитре знака; силуэт — маска plain CFXR и сужение.</summary>
    private static Material Sweep(string name, float taperSkew, float taperPower, float seed)
    {
        Shader shader = Shader.Find(SweepShaderName);
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        material.name = name;
        material.SetColor("_Core", Core);
        material.SetColor("_Mid", Mid);
        material.SetColor("_Edge", Edge);
        material.SetColor("_Rim", Rim);
        material.SetVector("_Bands", new Vector4(.10f, .26f, .44f, .80f));
        material.SetFloat("_RimOuter", .90f);
        material.SetFloat("_Head", 1f);
        material.SetFloat("_Erode", 0f);
        material.SetFloat("_ErodeAlong", .75f);
        material.SetVector("_NoiseScale", new Vector4(14f, 2.5f, 0f, 0f));
        material.SetFloat("_Periodic", 0f);
        material.SetFloat("_Streaks", .35f);
        material.SetFloat("_Glow", .55f);
        material.SetFloat("_Flash", 0f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_Seed", seed);
        material.SetFloat("_Taper", 1f);
        material.SetFloat("_TaperPower", taperPower);
        material.SetFloat("_TaperSkew", taperSkew);
        material.SetFloat("_BandWobble", .08f);
        // Слот маски — настоящий ассет: пустая ссылка в сборке плеера гасит меш
        // (PelagWhirlwindVfxSetup.WhiteTexture).
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrTrailMask);
        material.SetTexture("_Mask", mask != null ? mask : PelagWhirlwindVfxSetup.WhiteTexture());
        material.SetFloat("_MaskCut", mask != null ? .2f : 0f);
        material.SetFloat("_Timed", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Раскадровка шейдера по возрасту частицы, секунды от рождения слоя.</summary>
    private static void Timing(Material material, float life, float flashEnd, float headFrom, float headSeconds,
        float erodeFrom, float erodeTo, float fadeFrom, float fadeTo)
    {
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_FlashEnd", flashEnd);
        material.SetFloat("_HeadFrom", headFrom);
        material.SetFloat("_HeadSeconds", headSeconds);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeTo);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeTo);
        material.SetFloat("_FlowSpeed", 1.2f);
        EditorUtility.SetDirty(material);
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Уголь (цель 01): ореол красный, ядро горячее, к концу замаха —
    /// отблеск-четырёхлучник, редкие искры вверх со второй трети. Корень — у
    /// кости, системы в местном пространстве (едут за лапой), искры — в
    /// мировом (отстают от движения лапы).
    /// </summary>
    private static void SaveEmber(Kit kit)
    {
        float windup = EnemyBodyTelegraphView.EmberWindupSeconds;
        float total = windup + EnemyBodyTelegraphView.EmberTailSeconds;
        float f = windup / total;
        System.Func<float, float> w = p => p * f;
        System.Func<float, float> tail = x => f + x * (1f - f);

        var root = new GameObject(EnemyBodyTelegraphView.EmberPrefab);

        // Ореол .42 м (было .55: к удару диск выходил .74 м — шире лапы).
        var halo = NewSystem(root, "Halo", 1, total, kit.Halo);
        Once(halo, 0f, total, .42f, new Color(1f, .16f, .06f, 1f));
        SizeOverLife(halo, Curve(0f, 0f, w(.05f), .2f, w(.3f), .4f, w(.6f), .62f, w(.85f), .86f,
            f, 1f, tail(.15f), 1.35f, tail(.45f), 1.1f, 1f, .15f));
        ColorOverLife(halo,
            new[] { Key(.85f, .12f, .06f, 0f), Key(1f, .2f, .08f, w(.55f)), Key(1f, .42f, .14f, f),
                    Key(1f, .78f, .45f, tail(.2f)), Key(1f, .35f, .12f, 1f) },
            new[] { Alpha(0f, 0f), Alpha(.6f, w(.05f)), Alpha(.82f, w(.9f)), Alpha(.7f, tail(.5f)), Alpha(0f, 1f) });

        // Ядро, отблеск и искры — поверх ореола: у частиц в одной точке
        // порядок прозрачных случаен, и ореол закрывал ядро целиком.
        var hot = NewSystem(root, "Core", 1, total, kit.Hot);
        hot.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
        Once(hot, 0f, total, .24f, new Color(1f, .8f, .5f, 1f));
        SizeOverLife(hot, Curve(0f, 0f, w(.08f), .2f, w(.45f), .5f, w(.8f), .85f, f, 1f,
            tail(.2f), 1.7f, tail(.6f), 1f, 1f, 0f));
        ColorOverLife(hot,
            new[] { Key(1f, .45f, .2f, 0f), Key(1f, .8f, .5f, w(.8f)), Key(1f, .95f, .8f, tail(.2f)), Key(1f, .6f, .3f, 1f) },
            new[] { Alpha(0f, 0f), Alpha(.7f, w(.1f)), Alpha(1f, w(.8f)), Alpha(.8f, tail(.6f)), Alpha(0f, 1f) });

        // Отблеск: загорается на .55 замаха, пик — в тик удара.
        const float glintAt = .55f;
        float glintLife = total - glintAt;
        System.Func<float, float> g = x => (x - glintAt) / glintLife;
        var glint = NewSystem(root, "Glint", 1, total, kit.Sparkle);
        glint.GetComponent<ParticleSystemRenderer>().sortingFudge = -40f;
        Once(glint, glintAt, glintLife, .28f, new Color(1f, .7f, .35f, 1f));
        var glintMain = glint.main;
        glintMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var spin = glint.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(.9f);
        SizeOverLife(glint, Curve(0f, 0f, g(.7f), .5f, g(.95f), .85f, g(windup), 1f, g(windup + .03f), 1.4f,
            g(windup + .08f), .9f, 1f, 0f));
        ColorOverLife(glint,
            new[] { Key(1f, .6f, .3f, 0f), Key(1f, .95f, .8f, g(windup + .03f)), Key(1f, .5f, .2f, 1f) },
            new[] { Alpha(0f, 0f), Alpha(.9f, g(.7f)), Alpha(1f, g(windup + .03f)), Alpha(0f, 1f) });

        var sparks = NewSystem(root, "Sparks", 16, total, kit.Spark);
        sparks.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
        var sparksMain = sparks.main;
        sparksMain.simulationSpace = ParticleSystemSimulationSpace.World;
        sparksMain.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .5f);
        sparksMain.startSpeed = new ParticleSystem.MinMaxCurve(.35f, .8f);
        sparksMain.startSize = new ParticleSystem.MinMaxCurve(.025f, .05f);
        sparksMain.gravityModifier = -.12f;
        var sparksEmission = sparks.emission;
        sparksEmission.rateOverTime = new ParticleSystem.MinMaxCurve(10f,
            Curve(0f, 0f, w(.3f), 0f, w(.32f), 1f, f - .005f, 1f, f, 0f, 1f, 0f));
        var sparksShape = sparks.shape; sparksShape.enabled = true;
        sparksShape.shapeType = ParticleSystemShapeType.Cone;
        sparksShape.angle = 30f;
        sparksShape.radius = .05f;
        // Конус пускает по своей +Z; −90° по X ставят его вверх.
        sparksShape.rotation = new Vector3(-90f, 0f, 0f);
        ColorOverLife(sparks,
            new[] { Key(1f, .55f, .18f, 0f), Key(1f, .15f, .05f, 1f) },
            new[] { Alpha(1f, 0f), Alpha(1f, .5f), Alpha(0f, 1f) });

        Save(root);
    }

    /// <summary>
    /// Когти (цель 02): корень у когтя, +Z — к цели. Три полосы, средняя
    /// длиннее, изгиб в сторону бьющей лапы; два зеркальных набора, вид
    /// включает нужный. Искры — по ходу удара.
    /// </summary>
    private static void SaveClaw(Kit kit)
    {
        var root = new GameObject(EnemyBodyTelegraphView.ClawPrefab);
        float life = EnemyBodyTelegraphView.ClawLife;
        Strip(root, EnemyBodyTelegraphView.ClawLeftChild, ClawStreaks("BodySignClawStreaksL", -1f), kit.Streak, life);
        Strip(root, EnemyBodyTelegraphView.ClawRightChild, ClawStreaks("BodySignClawStreaksR", 1f), kit.Streak, life);
        var sparks = Sparks(root, kit.Spark, 8, .02f, .16f, .28f, 3.5f, 6f, .03f, .05f);
        var shape = sparks.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = .08f;
        Save(root);
    }

    /// <summary>
    /// Укус (цель 05): корень перед пастью, +Z — укус. Два серпа «( )» по
    /// бокам смыкаются за .07 с, чуть сжимаясь из увеличенного; искры в стороны.
    /// </summary>
    private static void SaveBite(Kit kit)
    {
        var root = new GameObject(EnemyBodyTelegraphView.BitePrefab);
        float life = EnemyBodyTelegraphView.BiteLife;
        const float close = .07f, speed = 1.8f;
        foreach (var (name, side) in new[] { ("JawL", -1f), ("JawR", 1f) })
        {
            var jaw = Strip(root, name, Jaw("BodySignBite" + name, side), kit.Bite, life);
            var velocity = jaw.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Все оси — кривыми: модули скорости не принимают разные режимы осей.
            velocity.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, -side * speed, close / life, 0f, 1f, 0f));
            velocity.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, 1f, 0f));
            velocity.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, 1f, 0f));
            SizeOverLife(jaw, Curve(0f, 1.15f, close / life, 1f, 1f, 1f));
        }
        var sparks = Sparks(root, kit.Spark, 6, .02f, .14f, .24f, 1.4f, 2.6f, .025f, .04f);
        var shape = sparks.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .06f;
        Save(root);
    }

    /// <summary>
    /// Клыки (цель 01 кабана): корень в центре кабана на высоте клыков, +Z —
    /// взмах. Дуга ±65° на TuskArcRadius, голова идёт справа налево; искры из
    /// середины дуги вперёд, когда голова её проходит.
    /// </summary>
    private static void SaveTusk(Kit kit)
    {
        var root = new GameObject(EnemyBodyTelegraphView.TuskPrefab);
        Strip(root, "Arc", TuskArc("BodySignTuskArc"), kit.Tusk, EnemyBodyTelegraphView.TuskLife);
        var sparks = Sparks(root, kit.Spark, 7, .045f, .18f, .30f, 2f, 3.4f, .03f, .05f);
        sparks.transform.localPosition = new Vector3(0f, 0f, EnemyBodyTelegraphView.TuskArcRadius);
        var shape = sparks.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = .15f;
        Save(root);
    }

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(root.name)); }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------- particles

    /// <summary>
    /// Пустая система: без цикла и автозапуска, местное пространство, масштаб
    /// по иерархии (вид масштабирует корень), зерно постоянное — перемотка
    /// даёт тот же кадр. Эмиссия и форма выключены, билборд.
    /// </summary>
    private static ParticleSystem NewSystem(GameObject root, string name, int maxParticles, float duration, Material material)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(root.name + "/" + name);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(.05f, duration);
        main.startDelay = 0f;
        main.startLifetime = duration;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startRotation = 0f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = Mathf.Max(1, maxParticles);
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        var shape = particles.shape; shape.enabled = false;
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return particles;
    }

    /// <summary>Одна частица в момент at, живёт life, размер и цвет стартовые.</summary>
    private static void Once(ParticleSystem particles, float at, float life, float size, Color color)
    {
        var main = particles.main;
        main.startLifetime = life;
        main.startSize = size;
        main.startColor = color;
        var emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(at, (short)1) });
    }

    /// <summary>
    /// Слой-меш с возрастом в шейдере серпа: одна частица в нуле, местные
    /// оси корня, AgePercent в TEXCOORD0.z — как ленты когтя Вендиго.
    /// </summary>
    private static ParticleSystem Strip(GameObject root, string name, Mesh mesh, Material material, float life)
    {
        var particles = NewSystem(root, name, 1, life + .02f, material);
        Once(particles, 0f, life, 1f, Color.white);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
        return particles;
    }

    /// <summary>Искры удара: вытянутые угольки CFXR, гаснут к концу жизни, чуть падают.</summary>
    private static ParticleSystem Sparks(GameObject root, Material material, int count, float at,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        var sparks = NewSystem(root, "Sparks", count, lifeMax + at + .05f, material);
        var main = sparks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.gravityModifier = .5f;
        var emission = sparks.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(at, (short)count) });
        ColorOverLife(sparks,
            new[] { Key(1f, .62f, .25f, 0f), Key(1f, .18f, .05f, 1f) },
            new[] { Alpha(1f, 0f), Alpha(1f, .45f), Alpha(0f, 1f) });
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.2f;
        renderer.velocityScale = .02f;
        return sparks;
    }

    private static void SizeOverLife(ParticleSystem particles, AnimationCurve curve)
    {
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void ColorOverLife(ParticleSystem particles, GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        var gradient = new Gradient();
        gradient.SetKeys(colors, alphas);
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static GradientColorKey Key(float r, float g, float b, float time) => new GradientColorKey(new Color(r, g, b), Mathf.Clamp01(time));

    private static GradientAlphaKey Alpha(float alpha, float time) => new GradientAlphaKey(alpha, Mathf.Clamp01(time));

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

    /// <summary>Постоянное зерно по имени: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static uint Seed(string name)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in name) hash = (hash ^ c) * 16777619u;
            return hash;
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
    }

    // ----------------------------------------------------------------- meshes
    //
    // Меши в метрах корня: +Z — удар, +Y — вверх. У полос u идёт вдоль (0 —
    // хвост, 1 — голова), v поперёк, v = 1 — выпуклая сторона: сужение
    // шейдера прижимает полосу к ней. Цвет вершин 8-битный: с Float32-цветом
    // мешевая частица читает мусор (ленты Вендиго выходили синими).

    private const int Segments = 32;

    /// <summary>
    /// Три полосы когтей длиной ClawLength от когтя (z = 0) к цели: средняя
    /// длиннее и шире, крайние расходятся веером и начинаются позже; изгиб
    /// дугой в сторону бьющей лапы (side −1 — левая, +1 — правая), к концу
    /// полосы чуть опускаются — к корпусу героя.
    /// </summary>
    private static Mesh ClawStreaks(string name, float side)
    {
        const float bow = .14f, drop = .22f;
        float length = EnemyBodyTelegraphView.ClawLength;
        var streaks = new (float offset, float from, float to, float width)[]
        {
            (-.15f, .07f, .93f, .10f), (0f, 0f, 1f, .13f), (.15f, .04f, .96f, .10f)
        };
        var data = new MeshData();
        foreach (var s in streaks)
        {
            var centers = new Vector3[Segments + 1];
            var outward = new Vector3[Segments + 1];
            var widths = new float[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float t = Mathf.Lerp(s.from, s.to, i / (float)Segments);
                centers[i] = new Vector3(side * (s.offset * (1f + .45f * t) + bow * Mathf.Sin(Mathf.PI * t)), -drop * t, t * length);
                var tangent = new Vector3(side * (s.offset * .45f + bow * Mathf.PI * Mathf.Cos(Mathf.PI * t)), 0f, length);
                outward[i] = Vector3.Cross(Vector3.up, tangent.normalized).normalized * side;
                widths[i] = s.width;
            }
            data.AddStrip(centers, outward, widths);
        }
        return data.Save(name);
    }

    /// <summary>
    /// Серп укуса: дуга по бокам средней линии, середина на x = ±gap, концы у
    /// линии впереди и сзади — «( )» при взгляде сверху. u — сзади вперёд.
    /// </summary>
    private static Mesh Jaw(string name, float side)
    {
        const float halfLength = .24f, gap = .14f, width = .075f;
        float spread = 55f * Mathf.Deg2Rad;
        float radius = halfLength / Mathf.Sin(spread);
        float center = gap - radius;
        var centers = new Vector3[Segments + 1];
        var outward = new Vector3[Segments + 1];
        var widths = new float[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Lerp(-spread, spread, i / (float)Segments);
            centers[i] = new Vector3(side * (center + radius * Mathf.Cos(angle)), 0f, radius * Mathf.Sin(angle));
            outward[i] = new Vector3(side * Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            widths[i] = width;
        }
        var data = new MeshData();
        data.AddStrip(centers, outward, widths);
        return data.Save(name);
    }

    /// <summary>Дуга клыков ±65° на TuskArcRadius перед кабаном; u — справа (+X) налево.</summary>
    private static Mesh TuskArc(string name)
    {
        const float width = .30f;
        float spread = 65f * Mathf.Deg2Rad, radius = EnemyBodyTelegraphView.TuskArcRadius;
        var centers = new Vector3[Segments + 1];
        var outward = new Vector3[Segments + 1];
        var widths = new float[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Lerp(spread, -spread, i / (float)Segments);
            outward[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            centers[i] = outward[i] * radius;
            widths[i] = width;
        }
        var data = new MeshData();
        data.AddStrip(centers, outward, widths);
        return data.Save(name);
    }

    private sealed class MeshData
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>Полоса по точкам: v = 0 — с внутренней стороны, v = 1 — со стороны outward.</summary>
        public void AddStrip(Vector3[] centers, Vector3[] outward, float[] widths)
        {
            int start = _vertices.Count;
            int last = centers.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                float u = i / (float)last;
                Vector3 half = outward[i] * (widths[i] * .5f);
                _vertices.Add(centers[i] - half); _uv.Add(new Vector2(u, 0f));
                _vertices.Add(centers[i] + half); _uv.Add(new Vector2(u, 1f));
                _colors.Add(new Color32(255, 255, 255, 255));
                _colors.Add(new Color32(255, 255, 255, 255));
                if (i == last) continue;
                int v = start + i * 2;
                _triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }

        public Mesh Save(string name)
        {
            string path = GeometryFolder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            mesh.name = name;
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
