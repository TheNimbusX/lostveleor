using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Рассекающий удар «Раскол» (целевой кадр владельца 24.09,
/// ART/PELAG/animation/cleave/concept/2026-09-24-target): на контакте —
/// огромный плоский серп от макушки до земли в белом/серебре с золотистой
/// кромкой (как на иконке способности), звезда удара на теле цели с искрами
/// по ходу клинка, веер трещин на земле от точки удара, щепки и ромбы. Без
/// дыма и пыли.
///
/// Серп — полумеш Cartoon FX Remaster «sword_trail 180 edge» (форма и
/// сужение концов от пака) на шейдере серпа Вихря: жёсткие полосы поперёк,
/// чёрный обвод, кадр-вспышка и эрозия от хвоста. Звезда — CFXR «Sword Hit
/// (Cross)» под плоской четырёхлучевой звездой. Линия — свой меш на том же
/// шейдере. Всё собирается кодом из копий ассетов пака, как Вихрь; прежние
/// префабы прошлого автора в папке CFXR не трогаются.
/// </summary>
public static class PelagCleaveVfxSetup
{
    private const string Revision = "PelagCleaveSplitV16";
    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrFolder = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/";
    private const string CfxrTrailPrefab = CfxrFolder + "CFXR4 Sword Trail PLAIN (360 Spiral).prefab";
    private const string CfxrHitPrefab = CfxrFolder + "CFXR4 Sword Hit PLAIN (Cross).prefab";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string SweepShaderName = "Razlom/Whirlwind Sweep";
    private const string SplitName = "VFX_Pelag_Cleave_Split";
    private const string StarName = "VFX_Pelag_Cleave_Star";
    private const string CrackName = "VFX_Pelag_Cleave_Crack";

    // Палитра Рассекающего — белое/серебро по иконке способности
    // (ART/characters/pelag/Icon_Cleave.png; владелец 24.09: «палитру чуть в
    // белое/серебро увести бы, как на иконке»): белое ядро, светло-стальная
    // середина, холодная серо-голубая кромка, тонкий графитовый обвод; вместо
    // золота — белые осколки. Полосы близки по светлоте: серп читается белым
    // клинком, а не радугой Вихря.
    // Ядро уходит за порог блума синим, а не поровну: с тёплым тонмаппингом
    // ровное белое ядро даёт жёлтое гало (V9 читалось кремовым), синий избыток
    // даёт холодное стальное.
    private static readonly Color Core = new Color(1.25f, 1.32f, 1.5f);
    private static readonly Color Mid = new Color(1.0f, 1.04f, 1.12f);
    private static readonly Color Edge = new Color(.72f, .80f, .95f);
    private static readonly Color Rim = new Color(.20f, .22f, .28f);
    private static readonly Color Chip = new Color(.16f, .13f, .12f);
    private static readonly Color Ember = new Color(1.2f, 1.3f, 1.5f);

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Pelag VFX/Рассекающий: подключить «Раскол»")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Рассекающий: пересобрать «Раскол»")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(SweepShaderName) == null || AssetDatabase.LoadAssetAtPath<GameObject>(CfxrTrailPrefab) == null)
        {
            Debug.LogWarning("[cleave-setup] Нет шейдера серпа или префаба CFXR, «Раскол» не собран.");
            return;
        }
        if (force || !Built()) Build();
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.CleaveSlash, PrefabFolder + "/" + SplitName + ".prefab", 3);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.CleaveHit, PrefabFolder + "/" + StarName + ".prefab", 6);
        PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.CleaveGround, PrefabFolder + "/" + CrackName + ".prefab", 3);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + SplitName + ".prefab");
        if (importer == null || importer.userData != Revision) return false;
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + StarName + ".prefab") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + CrackName + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        Shader sweepShader = Shader.Find(SweepShaderName);
        Shader glowShader = Shader.Find("Razlom/Pelag Glow");
        var meshes = new System.Collections.Generic.Dictionary<string, Mesh>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh) meshes[mesh.name] = mesh;
        // Полумеш «180 edge»: полоса r .70–1.00 — пропорция серпа целевого кадра.
        Mesh edge = meshes["cfxr mesh sword_trail 180 edge"];

        // Серп: три слоя на одной форме — основной, внутренний штрих (позже,
        // меньше, эрозия позже) и эхо (позже, больше, прозрачнее, стирается
        // первым). Раскадровка по возрасту частицы живёт в материале, поэтому у
        // каждого слоя свой ассет.
        Material arc = ArcMaterial(sweepShader, "M_Cleave_Arc", .06f, .26f);
        Material arcInner = ArcMaterial(sweepShader, "M_Cleave_ArcInner", .09f, .22f);
        Material arcEcho = ArcMaterial(sweepShader, "M_Cleave_ArcEcho", .04f, .20f);
        Material crack = BandMaterial(sweepShader, "M_Cleave_Crack", new Vector4(.14f, .34f, .58f, .80f), .88f, new Vector4(6f, 1.5f, 0f, 0f), .75f, .35f);
        // Трещина: широкая у точки удара, острая к концу, рваные края —
        // «обрубок» ровной полосы владелец отверг. Голова выстреливает от
        // точки удара, хвост стирается, уходит по прозрачности.
        crack.SetColor("_Core", new Color(1.25f, 1.32f, 1.5f));
        crack.SetFloat("_Glow", .6f);
        crack.SetFloat("_Taper", 1f);
        crack.SetFloat("_TaperSkew", .35f);
        crack.SetFloat("_TaperPower", .6f);
        crack.SetFloat("_BandWobble", .12f);
        Timed(crack, CrackLife, .04f, .20f, .26f, .36f, .12f, 2.2f, .30f, .06f);
        EditorUtility.SetDirty(crack);
        // Ударная волна по земле: кольцо Вихря в палитре Рассекающего, u по
        // окружности (периодический шум), без сужения и штрихов; растёт
        // кривой размера и гаснет по прозрачности.
        Material ring = BandMaterial(sweepShader, "M_Cleave_Ring", new Vector4(.12f, .30f, .50f, .84f), .90f, new Vector4(16f, 2.5f, 0f, 0f), 0f, 0f);
        ring.SetFloat("_Periodic", 1f);
        ring.SetFloat("_Glow", .35f);
        Timed(ring, RingLife, .04f, 1000f, 1f, .10f, .12f, 0f, 1f, .01f);
        EditorUtility.SetDirty(ring);
        Fresh("M_Cleave_Chip", sweepShader); Fresh("M_Cleave_Ember", sweepShader); Fresh("M_Cleave_Star", sweepShader);
        Material chip = PelagWhirlwindVfxSetup.FlatMaterial(sweepShader, "M_Cleave_Chip", Chip);
        Material ember = PelagWhirlwindVfxSetup.FlatMaterial(sweepShader, "M_Cleave_Ember", Ember);
        Material starFlat = PelagWhirlwindVfxSetup.FlatMaterial(sweepShader, "M_Cleave_Star", Core);
        Texture2D white = PelagWhirlwindVfxSetup.WhiteTexture();
        foreach (var flat in new[] { chip, ember, starFlat })
        {
            flat.SetTexture("_Mask", white);
            EditorUtility.SetDirty(flat);
        }
        Material spark = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Cleave_Spark.mat", glowShader);
        spark.SetTexture("_BaseMap", white);
        spark.SetColor("_BaseColor", new Color(1.3f, 1.35f, 1.45f, 1f));
        spark.SetFloat("_Emission", 2.4f);
        EditorUtility.SetDirty(spark);

        Mesh starMesh = PelagWhirlwindVfxSetup.StarMesh();
        Mesh chipMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Pelag_ImpactChip.asset");
        if (chipMesh == null) chipMesh = starMesh;

        SaveSplitPrefab(edge, arc, arcInner, arcEcho);
        SaveStarPrefab(spark, starMesh, starFlat);
        SaveCrackPrefab(crack, ring, chipMesh, chip, starMesh, ember);

        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + SplitName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[cleave-setup] «Раскол» собран из CFXR: серп, звезда, линия раскола, ревизия " + Revision + ".");
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>Жизнь слоёв серпа, с: вспышка два кадра, эрозия сверху вниз, уход по прозрачности с .30.</summary>
    private const float ArcLife = .42f;
    private const float CrackLife = .48f;
    private const float RingLife = .22f;

    /// <summary>
    /// Серп: билборд к камере (контроллер поворачивает корень в плоскости
    /// экрана по проекции направления удара), местная +X — выпуклость от
    /// героя к цели, ±Y — верх и низ. Полумеш пака вписан в 1×2: при размере
    /// 1 серп в два метра высотой, контроллер масштабирует корень. Полосы
    /// поперёк (обвод, кромка, середина, ядро) и эрозия от хвоста — в шейдере
    /// серпа Вихря; форма и сужение концов — от меша пака. Плёнка пака с её
    /// dissolve здесь не годится: она ведёт «голову» вдоль дуги и серп никогда
    /// не виден целиком, а целевой кадр — весь серп разом.
    ///
    /// Каждый слой — одна мешевая частица: хлопок масштаба (недолёт →
    /// перелёт → размер) и сжатие-растяжение — кривые размера по осям, дрейф
    /// вниз по ходу удара — скорость по времени жизни, доворот — вращение по
    /// времени жизни; всё остальное — шейдер по возрасту частицы.
    /// </summary>
    private static void SaveSplitPrefab(Mesh arcMesh, Material arc, Material arcInner, Material arcEcho)
    {
        var root = new GameObject(SplitName);
        try
        {
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.CleaveSlash;
            element.DefaultLifetime = ArcLife + .05f;
            element.AuthoredRadius = 1f;
            var view = root.AddComponent<PelagCleaveSplitView>();
            // Полумеш пака выпуклый к −X, его u=0 внизу: поворот на 180° даёт
            // выпуклость к цели (+X) и хвост u=0 наверху — эрозия идёт сверху вниз.
            // Дрейф задан в осях корня (−Y вниз, +X к цели) и переводится в
            // оси повёрнутого слоя; корень масштабируется контроллером в
            // ~1,8, дрейф растёт вместе с серпом.
            view.Arc = ArcLayer(root, "Arc", arcMesh, arc, 0f, 1f, 1f, 0f, new Vector2(.12f, -.38f), 1f, 0f);
            // Внутренний тонкий штрих ближе к камере: три мазка вместо одной линии.
            // Слои разведены по довороту и ритму: одна и та же форма в одном ритме
            // читалась одной пластиной (владелец 25.09: «плоско»).
            view.Inner = ArcLayer(root, "Inner", arcMesh, arcInner, .02f, .80f, .95f, -.03f, new Vector2(.06f, -.22f), 1.3f, -12f);
            // Эхо: тот же меш, чуть дальше от камеры, отстаёт и уходит первым.
            view.Echo = ArcLayer(root, "Echo", arcMesh, arcEcho, .06f, 1.07f, .5f, .03f, new Vector2(.28f, -.85f), .7f, 10f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + SplitName + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static ParticleSystem ArcLayer(GameObject root, string name, Mesh mesh, Material material,
        float delay, float size, float opacity, float z, Vector2 drift, float rollWeight, float baseRoll)
    {
        ParticleSystem layer = MeshParticle(root, name, mesh, material, ArcLife, delay, opacity);
        layer.transform.localPosition = new Vector3(0f, 0f, z);
        layer.transform.localRotation = Quaternion.Euler(0f, 0f, 180f + baseRoll);
        var main = layer.main;
        main.startSize3D = true;
        main.startSizeX = size;
        main.startSizeY = size;
        main.startSizeZ = 1f;
        // Хлопок: .70 → 1.06 → 1 за .09 с; сжатие-растяжение (.90, 1.08) → 1 вместе с ним.
        float popMid = .045f / ArcLife, popEnd = .09f / ArcLife;
        var sizeOver = layer.sizeOverLifetime; sizeOver.enabled = true;
        sizeOver.separateAxes = true;
        sizeOver.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .70f * .90f), new Keyframe(popMid, 1.06f * .95f), new Keyframe(popEnd, 1f), new Keyframe(1f, 1f)));
        sizeOver.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .70f * 1.08f), new Keyframe(popMid, 1.06f * 1.04f), new Keyframe(popEnd, 1f), new Keyframe(1f, 1f)));
        sizeOver.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
        // Дрейф с ease-out за .34 с: скорость от 2,6·D/T к нулю. Слой повёрнут
        // на 180°, поэтому оси корня входят со знаком минус; корень в ~1,8 —
        // делим, чтобы в мире вышли авторские метры.
        const float driftSeconds = .34f, rootScale = 1.5f * .75f; // базовая досягаемость × CleaveSplitScale
        float peak = 2.6f / driftSeconds / rootScale;
        var velocity = layer.velocityOverLifetime; velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(1f, DriftCurve(-drift.x * peak, driftSeconds / ArcLife));
        velocity.y = new ParticleSystem.MinMaxCurve(1f, DriftCurve(-drift.y * peak, driftSeconds / ArcLife));
        velocity.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        // Доворот: от +7° к −5° за .30 с с ease-out — серп «проходит» сквозь цель, а не висит.
        const float rollSeconds = .30f;
        float rollRate = -12f * Mathf.Deg2Rad * 2.2f / rollSeconds * rollWeight;
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, DriftCurve(rollRate, rollSeconds / ArcLife));
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(7f * Mathf.Deg2Rad * rollWeight);
        return layer;
    }

    /// <summary>Кривая скорости ease-out: пик в нуле, ноль к <paramref name="endFraction"/> жизни.</summary>
    private static AnimationCurve DriftCurve(float peak, float endFraction)
    {
        float end = Mathf.Clamp01(endFraction);
        return new AnimationCurve(
            new Keyframe(0f, peak), new Keyframe(end * .5f, peak * .33f), new Keyframe(end, 0f), new Keyframe(1f, 0f));
    }

    /// <summary>
    /// Слой-частица: одна мешевая частица, живёт <paramref name="life"/> с,
    /// возраст уходит в шейдер вершинным потоком AgePercent (TEXCOORD0.z).
    /// Ориентация — по трансформу системы (Local), масштаб — по иерархии.
    /// </summary>
    private static ParticleSystem MeshParticle(GameObject root, string name, Mesh mesh, Material material, float life, float delay, float opacity)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = life + delay + .05f;
        main.startDelay = delay;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startColor = new Color(1f, 1f, 1f, opacity);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 1;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
        return particles;
    }

    /// <summary>Звезда удара на теле цели: жёсткая плоская звезда во весь корпус, крест пака под ней, искры по ходу клинка.</summary>
    private static void SaveStarPrefab(Material spark, Mesh starMesh, Material starFlat)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrHitPrefab);
        var root = Object.Instantiate(prefab);
        root.name = StarName;
        try
        {
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(root);
            // Крест пака — 3 м на размере 1: .46 даёт около 1,4 м, во весь корпус стража.
            root.transform.localScale = Vector3.one * .46f;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = particles.gameObject == root
                    ? new Color(1.25f, 1.32f, 1.5f, 1f) : new Color(1.0f, 1.06f, 1.2f, 1f);
            }
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.CleaveHit;
            element.DefaultLifetime = .30f;
            // Маркер авторского масштаба: контроллер не домножает корень.
            element.AuthoredRadius = 1f;

            // Плоская четырёхлучевая звезда как на целевом кадре: два кадра во
            // весь корпус, затем сжимается. Крест пака остаётся под ней.
            var star = PelagWhirlwindVfxSetup.NewParticles(root, "Star", 1, .11f, .11f, 0f, 0f, 2.4f, 2.4f);
            var starMain = star.main;
            starMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            starMain.startRotation = 15f * Mathf.Deg2Rad;
            var starSize = star.sizeOverLifetime; starSize.enabled = true;
            starSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(.35f, .95f), new Keyframe(1f, .25f)));
            var starRenderer = star.GetComponent<ParticleSystemRenderer>();
            starRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            starRenderer.mesh = starMesh;
            starRenderer.alignment = ParticleSystemRenderSpace.View;
            starRenderer.sharedMaterial = starFlat;

            var sparks = PelagWhirlwindVfxSetup.NewParticles(root, "Sparks", 10, .18f, .32f, 4.2f, 7.0f, .05f, .10f);
            var sparksMain = sparks.main;
            sparksMain.gravityModifier = .7f;
            sparksMain.scalingMode = ParticleSystemScalingMode.Shape;
            var sparksShape = sparks.shape;
            sparksShape.shapeType = ParticleSystemShapeType.Cone;
            sparksShape.angle = 22f;
            sparksShape.radius = .05f;
            var sparksColor = sparks.colorOverLifetime; sparksColor.enabled = true;
            sparksColor.color = PelagWhirlwindVfxSetup.Gradient(new Color(1f, 1f, 1f, 1f), new Color(.8f, .86f, 1f, 0f));
            var sparksRenderer = sparks.GetComponent<ParticleSystemRenderer>();
            sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparksRenderer.lengthScale = 3.4f;
            sparksRenderer.velocityScale = .02f;
            sparksRenderer.sharedMaterial = spark;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + StarName + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Линия раскола: корень стоит на земле у ног героя, местная +X — вдоль
    /// удара, +Z — вверх (контроллер строит поворот). Меш длиной 1 растягивает
    /// раскадровка по длине способности; щепки и ромбы не зависят от масштаба.
    /// </summary>
    private static void SaveCrackPrefab(Material crack, Material ring, Mesh chipMesh, Material chip, Mesh starMesh, Material ember)
    {
        var root = new GameObject(CrackName);
        try
        {
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.CleaveGround;
            element.DefaultLifetime = .50f;
            element.AuthoredRadius = 1f;
            var view = root.AddComponent<PelagCleaveSplitView>();
            Mesh crackMesh = CrackMesh("CleaveCrack", 0f);
            // Ответвления с лёгким изгибом (два зеркальных меша): прямые отрезки
            // под фиксированными углами читались чертежом (владелец 25.09).
            Mesh[] forkMeshes = { CrackMesh("CleaveCrackBendL", .06f), CrackMesh("CleaveCrackBendR", -.06f) };
            view.Crack = MeshParticle(root, "Crack", crackMesh, crack, CrackLife, 0f, 1f);
            // Ответвления: из разных точек главной линии, короче и тоньше, каждое
            // сужается к своему концу (положение и размер ставит раскадровка по
            // длине). Две ровные боковые полосы из точки удара владелец отверг:
            // «полоска поперёк основной — обрубок».
            var forks = new (float at, float angle, float length, float width, float delay)[]
            {
                (.30f, 34f, .42f, .55f, .015f), (.48f, -28f, .50f, .60f, .025f),
                (.66f, 22f, .38f, .50f, .035f), (.80f, -36f, .30f, .45f, .045f)
            };
            view.Forks = new PelagCleaveSplitView.Fork[forks.Length];
            for (int i = 0; i < forks.Length; i++)
                view.Forks[i] = new PelagCleaveSplitView.Fork
                {
                    Line = MeshParticle(root, "Fork" + i, forkMeshes[i % 2], crack, CrackLife, forks[i].delay, 1f),
                    At = forks[i].at, Angle = forks[i].angle, Length = forks[i].length, Width = forks[i].width
                };
            // Ударная волна: кольцо на земле от точки удара (меш кольца Вихря,
            // радиус 1): .30 → 1.0 м за .12 с с ease-out, гаснет к .22 с.
            Mesh ringMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GeometryFolder + "/WhirlwindRing.asset");
            if (ringMesh != null)
            {
                view.Ring = MeshParticle(root, "Ring", ringMesh, ring, RingLife, 0f, 1f);
                view.Ring.transform.localPosition = new Vector3(.45f, 0f, .003f);
                float growEnd = .12f / RingLife;
                var ringSize = view.Ring.sizeOverLifetime; ringSize.enabled = true;
                ringSize.separateAxes = false;
                ringSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, .30f), new Keyframe(growEnd * .35f, .80f), new Keyframe(growEnd, 1.0f), new Keyframe(1f, 1.0f)));
            }

            // Щепки летят вперёд и вверх из точки удара, падают на землю.
            var chips = PelagWhirlwindVfxSetup.NewParticles(root, "Chips", 8, .32f, .48f, 1.8f, 3.2f, .08f, .13f);
            var chipsMain = chips.main;
            chipsMain.gravityModifier = 1.4f;
            chipsMain.scalingMode = ParticleSystemScalingMode.Local;
            chipsMain.startRotation3D = true;
            chipsMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            chipsMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            chipsMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var chipsEmission = chips.emission;
            chipsEmission.SetBursts(new[] { new ParticleSystem.Burst(.02f, (short)8) });
            var chipsShape = chips.shape;
            chipsShape.shapeType = ParticleSystemShapeType.Cone;
            chipsShape.angle = 30f;
            chipsShape.radius = .08f;
            chipsShape.position = new Vector3(.9f, 0f, 0f);
            // Конус эмитит вдоль своей +Z; поворот на Y 50° наклоняет его от «вверх» к «вперёд».
            chipsShape.rotation = new Vector3(0f, 50f, 0f);
            var chipsRotation = chips.rotationOverLifetime; chipsRotation.enabled = true;
            chipsRotation.separateAxes = false;
            chipsRotation.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var chipsRenderer = chips.GetComponent<ParticleSystemRenderer>();
            chipsRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            chipsRenderer.mesh = chipMesh;
            chipsRenderer.alignment = ParticleSystemRenderSpace.Local;
            chipsRenderer.sharedMaterial = chip;

            // Белые осколки (как на иконке) поднимаются вдоль всей линии чуть позже вспышки.
            var embers = PelagWhirlwindVfxSetup.NewParticles(root, "Embers", 8, .22f, .36f, .6f, 1.3f, .04f, .07f);
            var embersMain = embers.main;
            embersMain.gravityModifier = -.2f;
            embersMain.scalingMode = ParticleSystemScalingMode.Local;
            embersMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var embersEmission = embers.emission;
            embersEmission.SetBursts(new[] { new ParticleSystem.Burst(.08f, (short)8) });
            var embersShape = embers.shape;
            embersShape.shapeType = ParticleSystemShapeType.Box;
            embersShape.scale = new Vector3(1.6f, .25f, .05f);
            embersShape.position = new Vector3(1.0f, 0f, 0f);
            var embersSize = embers.sizeOverLifetime; embersSize.enabled = true;
            embersSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(.6f, .9f), new Keyframe(1f, 0f)));
            var embersRenderer = embers.GetComponent<ParticleSystemRenderer>();
            embersRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            embersRenderer.mesh = starMesh;
            embersRenderer.alignment = ParticleSystemRenderSpace.View;
            embersRenderer.sharedMaterial = ember;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + CrackName + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // -------------------------------------------------------------- materials

    /// <summary>
    /// Полосы поперёк формы на шейдере серпа Вихря: чёрный обвод, красная
    /// кромка, оранжевая середина, кремовое ядро; эрозия идёт от хвоста u=0.
    /// </summary>
    /// <summary>
    /// Материал с нуля при сохранении ассета и GUID. Ранняя версия сборщика
    /// записала в M_Cleave_Arc/M_Cleave_Crack пустую текстуру `_Mask`
    /// (fileID 0): в редакторе это безобидно, а в сборке плеера такой
    /// материал не рисовал меш вовсе — кольцо Вихря без записи `_Mask`
    /// рисовалось. Свойства ставятся заново после сброса.
    /// </summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    /// <summary>Материал слоя серпа: полосы, сужение концов и штрихи общие, эрозия по возрасту — своя.</summary>
    private static Material ArcMaterial(Shader shader, string name, float erodeFrom, float erodeSeconds)
    {
        // Эрозия не ровной волной сверху вниз, а рваная: доля «вдоль u» .5 и
        // крупный шум — хвост распадается на штрихи (владелец 25.09: «линейно»).
        Material arc = BandMaterial(shader, name, new Vector4(.08f, .22f, .40f, .88f), .94f, new Vector4(3.2f, 1.2f, 0f, 0f), .5f, .7f);
        // Сужение концов серпа, как в целевом кадре: тонкий хвост наверху,
        // самое широкое место ниже середины, острие у земли.
        arc.SetFloat("_MaskCut", 0f);
        arc.SetFloat("_Taper", 1f);
        arc.SetFloat("_TaperPower", .7f);
        arc.SetFloat("_TaperSkew", 1.3f);
        // Движение внутри формы: линии скорости бегут (поток по возрасту),
        // границы полос чуть дрожат — серп не линейка.
        arc.SetFloat("_Streaks", .7f);
        arc.SetFloat("_BandWobble", .10f);
        arc.SetFloat("_Glow", .55f);
        Timed(arc, ArcLife, .045f, erodeFrom, erodeSeconds, .30f, .12f, 1.6f, 1f, .01f);
        EditorUtility.SetDirty(arc);
        return arc;
    }

    /// <summary>Раскадровка по возрасту частицы (шейдер: _Timed), секунды от рождения слоя.</summary>
    private static void Timed(Material material, float life, float flashEnd, float erodeFrom, float erodeSeconds,
        float fadeFrom, float fadeSeconds, float flowSpeed, float headFrom, float headSeconds)
    {
        material.SetFloat("_Timed", 1f);
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_FlashEnd", flashEnd);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeFrom + erodeSeconds);
        material.SetFloat("_FadeFrom", fadeFrom);
        material.SetFloat("_FadeTo", fadeFrom + fadeSeconds);
        material.SetFloat("_FlowSpeed", flowSpeed);
        material.SetFloat("_HeadFrom", headFrom);
        material.SetFloat("_HeadSeconds", headSeconds);
    }

    private static Material BandMaterial(Shader shader, string name, Vector4 bands, float rimOuter,
        Vector4 noiseScale, float erodeAlong, float streaks)
    {
        Material material = Fresh(name, shader);
        // Маска не используется (_MaskCut 0), но слот заполняется настоящим
        // ассетом: Fresh() пишет в слот пустую ссылку, а с ней плеер не
        // рисует меш вовсе (см. PelagWhirlwindVfxSetup.WhiteTexture).
        material.SetTexture("_Mask", PelagWhirlwindVfxSetup.WhiteTexture());
        material.SetColor("_Core", Core);
        material.SetColor("_Mid", Mid);
        material.SetColor("_Edge", Edge);
        material.SetColor("_Rim", Rim);
        material.SetVector("_Bands", bands);
        material.SetFloat("_RimOuter", rimOuter);
        material.SetFloat("_Head", 1f);
        material.SetFloat("_Erode", 0f);
        material.SetFloat("_ErodeAlong", erodeAlong);
        material.SetVector("_NoiseScale", noiseScale);
        material.SetFloat("_Periodic", 0f);
        material.SetFloat("_Streaks", streaks);
        material.SetFloat("_Glow", .4f);
        material.SetFloat("_Flash", 0f);
        material.SetFloat("_Opacity", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ----------------------------------------------------------------- meshes

    /// <summary>
    /// Полоса длиной 1 вдоль +X, ширина .22 в плоскости XY; u вдоль, v поперёк.
    /// <paramref name="bend"/> — прогиб середины по Y в долях длины (0 — прямая).
    /// </summary>
    private static Mesh CrackMesh(string name, float bend)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 16;
        const float halfWidth = .11f;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float bow = bend * Mathf.Sin(Mathf.PI * t);
            vertices[2 * i] = new Vector3(t, bow - halfWidth, 0f);
            vertices[2 * i + 1] = new Vector3(t, bow + halfWidth, 0f);
            uv[2 * i] = new Vector2(t, 0f);
            uv[2 * i + 1] = new Vector2(t, 1f);
            if (i == segments) continue;
            int v = i * 2, at = i * 6;
            triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
            triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
    }
}
