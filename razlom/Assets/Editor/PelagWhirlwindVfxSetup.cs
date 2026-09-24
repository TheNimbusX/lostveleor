using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Вихрь на основе Cartoon FX Remaster: рисованные спирали пака (меши
/// sword_trail 360 thick / 360 spiral / 180 thick, маска и dissolve плёнки
/// «sword trail plain») собираются в три дуги на разной высоте с собственным
/// вращением, плюс кадр-вспышка, тонкое кольцо на земле, щепки и ромбы.
/// Удар по цели — CFXR «Sword Hit (Cross)» крупнее оригинала с искрами.
///
/// Всё собирается кодом из копий ассетов пака: полная пересборка
/// «Разлом/Pelag VFX» сохраняет записи WhirlwindRing и WhirlwindHit, а этот
/// сборщик пересоздаёт свои префабы только по маркеру ревизии или по пункту
/// меню. Прежняя спираль владельца VFX_Pelag_Whirlwind_Heavy не трогается и
/// возвращается отдельным пунктом.
/// </summary>
public static class PelagWhirlwindVfxSetup
{
    private const string Revision = "PelagWhirlwindCfxrV4";
    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrFolder = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/";
    private const string CfxrTrailPrefab = CfxrFolder + "CFXR4 Sword Trail PLAIN (360 Spiral).prefab";
    private const string CfxrHitPrefab = CfxrFolder + "CFXR4 Sword Hit PLAIN (Cross).prefab";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh sword_slashes.fbx";
    private const string CfxrTrailMaterial = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr sword trail plain.mat";
    private const string StrikeName = "VFX_Pelag_Whirlwind_Strike";
    private const string SweepShaderName = "Razlom/Whirlwind Sweep";
    private const string PaletteKey = "Razlom.Whirlwind.Palette";
    private const string PaletteEnv = "RAZLOM_WHIRLWIND_PALETTE";
    private const string HeavyPalette = "Heavy";

    private struct Palette
    {
        public string Key, Title;
        public Color Core, Mid, Edge, Rim, Chip, Ember;
        // Обвод кольца на земле: кромка и внешний край. Выбор владельца 24.09 —
        // тёплый серп A с чёрным обводом, как у C.
        public Color RingEdge, RingRim;
    }

    // За порог блума 1.05 выходит только красный канал: Neutral tonemapping
    // иначе уводит тёплое свечение в жёлтое (см. ArenaView, контур врага).
    private static readonly Palette[] Palettes =
    {
        new Palette
        {
            Key = "A", Title = "Сабля",
            Core = new Color(1.3f, 1.22f, 1.1f), Mid = new Color(1.3f, .55f, .15f),
            Edge = new Color(1.35f, .14f, .03f), Rim = new Color(.25f, .08f, .06f),
            Chip = new Color(.20f, .13f, .11f), Ember = new Color(1.3f, .60f, .18f),
            RingEdge = new Color(.10f, .03f, .03f), RingRim = new Color(.03f, .02f, .03f)
        },
        new Palette
        {
            Key = "B", Title = "Холодная сталь",
            Core = new Color(1.3f, 1.28f, 1.25f), Mid = new Color(.70f, .86f, 1.05f),
            Edge = new Color(.22f, .40f, 1.0f), Rim = new Color(.06f, .09f, .22f),
            Chip = new Color(.12f, .14f, .20f), Ember = new Color(.85f, .95f, 1.15f),
            RingEdge = new Color(.22f, .40f, 1.0f), RingRim = new Color(.06f, .09f, .22f)
        },
        new Palette
        {
            Key = "C", Title = "Чернила",
            Core = new Color(1.3f, 1.2f, 1.0f), Mid = new Color(1.35f, .25f, .08f),
            Edge = new Color(.10f, .03f, .03f), Rim = new Color(.03f, .02f, .03f),
            Chip = new Color(.03f, .02f, .03f), Ember = new Color(.05f, .03f, .04f),
            RingEdge = new Color(.10f, .03f, .03f), RingRim = new Color(.03f, .02f, .03f)
        }
    };

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Вихрь: подключить серп")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Вихрь: пересобрать серп")]
    public static void Rebuild() => Install(true);

    [MenuItem("Разлом/Pelag VFX/Вихрь: палитра A — Сабля")]
    public static void PaletteA() => ChoosePalette("A");

    [MenuItem("Разлом/Pelag VFX/Вихрь: палитра B — Холодная сталь")]
    public static void PaletteB() => ChoosePalette("B");

    [MenuItem("Разлом/Pelag VFX/Вихрь: палитра C — Чернила")]
    public static void PaletteC() => ChoosePalette("C");

    [MenuItem("Разлом/Pelag VFX/Вихрь: вернуть спираль Heavy")]
    public static void RestoreHeavy() => ChoosePalette(HeavyPalette);

    private static void ChoosePalette(string key)
    {
        EditorPrefs.SetString(PaletteKey, key);
        Install(false);
        Debug.Log($"[whirlwind-setup] Вихрь: выбран вариант «{key}».");
    }

    /// <summary>Вариант из окружения (batch-сборка съёмки) или из настроек редактора; по умолчанию A.</summary>
    private static string SelectedPalette()
    {
        string env = System.Environment.GetEnvironmentVariable(PaletteEnv);
        if (!string.IsNullOrEmpty(env)) return env.Trim();
        return EditorPrefs.GetString(PaletteKey, "A");
    }

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        string palette = SelectedPalette();
        if (palette == HeavyPalette)
        {
            Bind(library, PelagVfxId.WhirlwindRing, CfxrFolder + "VFX_Pelag_Whirlwind_Heavy.prefab", 3);
            Bind(library, PelagVfxId.WhirlwindHit, CfxrFolder + "VFX_Pelag_Whirlwind_Hit.prefab", 16);
            AssetDatabase.SaveAssetIfDirty(library);
            return;
        }
        if (Shader.Find(SweepShaderName) == null || AssetDatabase.LoadAssetAtPath<GameObject>(CfxrTrailPrefab) == null)
        {
            Debug.LogWarning("[whirlwind-setup] Нет шейдера кольца или префаба CFXR, серп не собран.");
            return;
        }
        if (force || !Built()) Build();
        if (!Bind(library, PelagVfxId.WhirlwindRing, SweepPath(palette), 3))
            Bind(library, PelagVfxId.WhirlwindRing, SweepPath("A"), 3);
        Bind(library, PelagVfxId.WhirlwindHit, PrefabFolder + "/" + StrikeName + ".prefab", 16);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static string SweepPath(string palette) => PrefabFolder + "/VFX_Pelag_Whirlwind_Sweep_" + palette + ".prefab";

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + StrikeName + ".prefab");
        if (importer == null || importer.userData != Revision) return false;
        foreach (var palette in Palettes)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SweepPath(palette.Key)) == null) return false;
        return true;
    }

    private static void Build()
    {
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Geometry");
        Shader sweepShader = Shader.Find(SweepShaderName);
        Shader glowShader = Shader.Find("Razlom/Pelag Glow");
        var meshes = new System.Collections.Generic.Dictionary<string, Mesh>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrMeshes))
            if (asset is Mesh mesh) meshes[mesh.name] = mesh;
        Mesh thick = meshes["cfxr mesh sword_trail 360 thick"];
        Mesh spiral = meshes["cfxr mesh sword_trail 360 spiral"];
        Mesh half = meshes["cfxr mesh sword_trail 180 thick"];
        Mesh edge = meshes["cfxr mesh sword_trail 360 edge"];

        Mesh ringMesh = RingMesh();
        Mesh starMesh = StarMesh();
        Mesh chipMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Pelag_ImpactChip.asset");
        if (chipMesh == null) chipMesh = starMesh;

        var trailSource = AssetDatabase.LoadAssetAtPath<Material>(CfxrTrailMaterial);
        Material trail = CfxrMaterial("M_Whirlwind_CfxrTrail", trailSource, keepDissolve: true);
        Material flash = CfxrMaterial("M_Whirlwind_CfxrFlash", trailSource, keepDissolve: false);
        foreach (var palette in Palettes)
        {
            Material ring = RingMaterial(sweepShader, palette);
            Material chip = FlatMaterial(sweepShader, "M_Whirlwind_Chip_" + palette.Key, palette.Chip);
            Material ember = FlatMaterial(sweepShader, "M_Whirlwind_Ember_" + palette.Key, palette.Ember);
            SaveSweepPrefab(palette, thick, spiral, half, edge, ringMesh, chipMesh, starMesh, trail, flash, ring, chip, ember);
        }

        Material spark = SparkMaterial(glowShader);
        SaveStrikePrefab(spark);

        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + StrikeName + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[whirlwind-setup] Серп Вихря собран из CFXR: три палитры, удар по цели, ревизия " + Revision + ".");
    }

    // ---------------------------------------------------------------- prefabs

    private static void SaveSweepPrefab(Palette palette, Mesh thick, Mesh spiral, Mesh half, Mesh edge, Mesh ringMesh,
        Mesh chipMesh, Mesh starMesh, Material trail, Material flash, Material ring, Material chip, Material ember)
    {
        var root = new GameObject("VFX_Pelag_Whirlwind_Sweep_" + palette.Key);
        try
        {
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindRing;
            element.DefaultLifetime = .62f;
            // Толстая спираль пака вписана в квадрат 2×2: радиус головы равен
            // единице, контроллер масштабирует корень в радиус способности.
            element.AuthoredRadius = 1f;
            var view = root.AddComponent<PelagWhirlwindSweepView>();

            // Кадр-вспышка: та же обычная спираль с маской и dissolve пака, но
            // белая и горячая, на два кадра. Толстая без dissolve давала
            // плоский кремовый диск на всю площадку.
            var flashArc = AddCfxrArc(root, "Flash", spiral, trail, 1.04f, .06f, 0f, 0f, -14f, 0f);
            var flashColor = flashArc.colorOverLifetime; flashColor.enabled = false;
            var flashMain = flashArc.main; flashMain.startColor = new Color(1.6f, 1.5f, 1.3f, 1f);

            // Три дуги: основная на высоте клинка, эхо ниже и позже, завиток выше и ещё позже.
            // Основная дуга — обычная спираль: она держится у внешнего радиуса и
            // не закрывает героя; толстая остаётся только кадру-вспышке.
            Tint(AddCfxrArc(root, "Main", spiral, trail, 1f, .38f, 0f, 0f, -14f, 0f), palette, 1f);
            Tint(AddCfxrArc(root, "Echo", edge, trail, .96f, .36f, .04f, 130f, -11f, .16f), palette, .7f);
            Tint(AddCfxrArc(root, "Wisp", half, trail, .55f, .30f, .08f, 250f, -12f, -.15f), palette, .6f);

            view.Ring = MeshChild(root, "Ring", ringMesh, ring);
            AddChips(root, chipMesh, chip);
            AddEmbers(root, starMesh, ember);
            PrefabUtility.SaveAsPrefabAsset(root, SweepPath(palette.Key));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Копия спирали CFXR как дуга Вихря. Меш, материал, кривые dissolve и
    /// custom data — из пака; здесь только размер, время, поворот и вращение.
    /// z — смещение по местной оси корня: после X90 положительное значение
    /// уходит вниз, к земле.
    /// </summary>
    private static ParticleSystem AddCfxrArc(GameObject root, string name, Mesh mesh, Material material,
        float size, float life, float delay, float startDegrees, float spinRadPerSecond, float z)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrTrailPrefab);
        var go = Object.Instantiate(prefab);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(go);
        var particles = go.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(.2f, life + delay);
        main.startDelay = delay;
        main.startLifetime = life;
        main.startSize = size;
        main.startRotation = startDegrees * Mathf.Deg2Rad;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var rotation = particles.rotationOverLifetime;
        rotation.enabled = spinRadPerSecond != 0f;
        rotation.separateAxes = false;
        // Вращение быстрое на выходе и затухающее к хвосту: дуга «вылетает» из-под клинка.
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, spinRadPerSecond), new Keyframe(.4f, spinRadPerSecond * .3f), new Keyframe(1f, 0f)));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        return particles;
    }

    /// <summary>Палитра дуги: цвет по жизни от ядра к кромке, альфа пака сохранена по форме.</summary>
    private static void Tint(ParticleSystem particles, Palette palette, float opacity)
    {
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(palette.Core, 0f), new GradientColorKey(palette.Core, .22f),
                new GradientColorKey(palette.Mid, .5f), new GradientColorKey(palette.Edge, .8f),
                new GradientColorKey(palette.Edge, 1f)
            },
            new[] { new GradientAlphaKey(opacity, 0f), new GradientAlphaKey(opacity, .45f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    private static void SaveStrikePrefab(Material spark)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrHitPrefab);
        var root = Object.Instantiate(prefab);
        root.name = StrikeName;
        try
        {
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(root);
            // Крест пака в родном масштабе — 3 м на размере 1: держим корень
            // на .32, чтобы удар был около метра на теле цели.
            root.transform.localScale = Vector3.one * .32f;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = particles.gameObject == root
                    ? new Color(1.3f, 1.22f, 1.05f, 1f) : new Color(1.35f, .45f, .12f, 1f);
            }
            var element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindHit;
            element.DefaultLifetime = .30f;

            // Искры уходят по касательной хода клинка: корень поворачивает контроллер.
            var sparks = NewParticles(root, "Sparks", 5, .16f, .24f, 3.6f, 5.4f, .05f, .085f);
            var sparksMain = sparks.main;
            sparksMain.gravityModifier = .6f;
            sparksMain.scalingMode = ParticleSystemScalingMode.Shape;
            var sparksShape = sparks.shape;
            sparksShape.shapeType = ParticleSystemShapeType.Cone;
            sparksShape.angle = 20f;
            sparksShape.radius = .04f;
            var sparksColor = sparks.colorOverLifetime; sparksColor.enabled = true;
            sparksColor.color = Gradient(new Color(1f, .97f, .85f, 1f), new Color(1f, .5f, .18f, 0f));
            var sparksRenderer = sparks.GetComponent<ParticleSystemRenderer>();
            sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparksRenderer.lengthScale = 3.2f;
            sparksRenderer.velocityScale = .02f;
            sparksRenderer.sharedMaterial = spark;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + StrikeName + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Renderer MeshChild(GameObject root, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    /// <summary>Тёмные щепки, как на иконке: разлетаются с высоты клинка и падают на землю.</summary>
    private static void AddChips(GameObject root, Mesh chipMesh, Material material)
    {
        var chips = NewParticles(root, "Chips", 9, .30f, .42f, 1.1f, 1.9f, .06f, .09f);
        var main = chips.main;
        main.gravityModifier = 1.3f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var emission = chips.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(.02f, (short)9) });
        var shape = chips.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = .45f;
        shape.randomDirectionAmount = .12f;
        var rotation = chips.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = false;
        rotation.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        var renderer = chips.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = chipMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
    }

    /// <summary>Мелкие ромбы, оставшиеся от серпа: расходятся наружу и чуть вверх после вспышки.</summary>
    private static void AddEmbers(GameObject root, Mesh starMesh, Material material)
    {
        var embers = NewParticles(root, "Embers", 12, .28f, .42f, .35f, .80f, .05f, .08f);
        var main = embers.main;
        main.gravityModifier = -.25f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var emission = embers.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(.13f, (short)12) });
        var shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = .82f;
        shape.radiusThickness = .35f;
        var size = embers.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(.6f, .9f), new Keyframe(1f, 0f)));
        var renderer = embers.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = starMesh;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
    }

    private static ParticleSystem NewParticles(GameObject root, string name, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(.2f, lifeMax);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = Mathf.Max(1, count);
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return particles;
    }

    private static Gradient Gradient(Color start, Color end)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(start.a, .55f), new GradientAlphaKey(end.a, 1f) });
        return gradient;
    }

    // -------------------------------------------------------------- materials

    /// <summary>Копия плёнки CFXR «sword trail plain»: маска и dissolve пака, без правок в оригинале.</summary>
    private static Material CfxrMaterial(string name, Material source, bool keepDissolve)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        if (!keepDissolve)
        {
            material.DisableKeyword("_CFXR_DISSOLVE");
            material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
            if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
            if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material RingMaterial(Shader shader, Palette palette)
    {
        Material material = LoadOrCreateMaterial(MaterialFolder + "/M_Whirlwind_Ring_" + palette.Key + ".mat", shader);
        material.SetColor("_Core", palette.Core);
        material.SetColor("_Mid", palette.Mid);
        material.SetColor("_Edge", palette.RingEdge);
        material.SetColor("_Rim", palette.RingRim);
        material.SetVector("_Bands", new Vector4(.12f, .30f, .50f, .84f));
        material.SetFloat("_RimOuter", .90f);
        material.SetFloat("_Head", 1f);
        material.SetFloat("_Erode", 0f);
        material.SetFloat("_ErodeAlong", 0f);
        material.SetVector("_NoiseScale", new Vector4(16f, 2.5f, 0f, 0f));
        material.SetFloat("_Periodic", 1f);
        material.SetFloat("_Streaks", 0f);
        material.SetFloat("_Glow", .3f);
        material.SetFloat("_Flash", 0f);
        material.SetFloat("_Opacity", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Плоский цвет ядра без обвода и эрозии: щепки, ромбы.</summary>
    private static Material FlatMaterial(Shader shader, string name, Color color)
    {
        Material material = LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        material.SetColor("_Core", color);
        material.SetColor("_Mid", color);
        material.SetColor("_Edge", color);
        material.SetColor("_Rim", color);
        material.SetFloat("_Head", 1f);
        material.SetFloat("_Erode", 0f);
        material.SetFloat("_Streaks", 0f);
        material.SetFloat("_Glow", 0f);
        material.SetFloat("_Flash", 1f);
        material.SetFloat("_Opacity", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material SparkMaterial(Shader glowShader)
    {
        Material material = LoadOrCreateMaterial(MaterialFolder + "/M_Whirlwind_Spark.mat", glowShader);
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        material.SetColor("_BaseColor", new Color(1.25f, .92f, .55f, 1f));
        material.SetFloat("_Emission", 2.4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        material.name = System.IO.Path.GetFileNameWithoutExtension(path);
        return material;
    }

    // ----------------------------------------------------------------- meshes

    /// <summary>Замкнутое тонкое кольцо радиуса 1 в плоскости XY; u идёт по кругу.</summary>
    private static Mesh RingMesh()
    {
        Mesh mesh = LoadOrCreateMesh(GeometryFolder + "/WhirlwindRing.asset", "WhirlwindRing");
        const int segments = 96;
        const float width = .085f;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[2 * i] = direction * (1f - width);
            vertices[2 * i + 1] = direction;
            uv[2 * i] = new Vector2(t, 0f);
            uv[2 * i + 1] = new Vector2(t, 1f);
            if (i == segments) continue;
            int v = i * 2, at = i * 6;
            triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
            triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
        }
        Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    /// <summary>Четырёхлучевая звезда диаметром 1 в плоскости XY.</summary>
    private static Mesh StarMesh()
    {
        Mesh mesh = LoadOrCreateMesh(GeometryFolder + "/WhirlwindStar.asset", "WhirlwindStar");
        var vertices = new Vector3[9];
        var uv = new Vector2[9];
        var triangles = new int[8 * 3];
        vertices[0] = Vector3.zero; uv[0] = new Vector2(.5f, .5f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * .25f;
            float radius = (i % 2 == 0) ? .5f : .13f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            uv[i + 1] = new Vector2(.5f + vertices[i + 1].x, .5f + vertices[i + 1].y);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = 1 + (i + 1) % 8;
            triangles[i * 3 + 2] = 1 + i;
        }
        Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    private static Mesh LoadOrCreateMesh(string path, string name)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear();
        mesh.name = name;
        return mesh;
    }

    private static void Fill(Mesh mesh, Vector3[] vertices, Vector2[] uv, int[] triangles)
    {
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
    }

    // ---------------------------------------------------------------- library

    private static bool Bind(AbilityVfxLibrary library, PelagVfxId id, string path, int prewarm)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return false;
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == id);
        if (index >= 0 && library.Entries[index].Prefab == prefab && library.Entries[index].Prewarm == prewarm) return true;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = id, Prefab = prefab, Prewarm = prewarm };
        EditorUtility.SetDirty(library);
        return true;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
    }
}
