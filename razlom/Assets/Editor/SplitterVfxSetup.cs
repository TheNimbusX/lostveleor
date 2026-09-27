using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX раскола Расщепеня по целевому кадру владельца
/// (ART/…/8.forest-splitbark/production/vfx_target_frames_2026-09-26/1-burst-leap.png):
/// большой лопается в тёплой пыли, летят щепки коры, листья и маленькие
/// шляпки грибов, две половины коры разваливаются, два детёныша 0,6
/// выпрыгивают вбок.
///
/// Всё из паков и из самой модели: CFXR (размытые облака x4 — пыль; unlit
/// щепки дерева и комья 3×3; лист «leave a» с мешем листа, освещение снято —
/// в URP освещённые материалы CFXR бледнеют). Половины коры и шляпка гриба
/// вырезаны в Blender из меша Расщепеня в позе последнего кадра трещины
/// (ART/…/production/debris/cut_shells.py) и рисуются материалом тела.
///
/// Собирает в Assets/Resources/VFX/Splitter:
///   VFX_Splitter_Burst — раскол у ног (+Z — взгляд, +Y — вверх);
///   VFX_Splitter_ShellLand — пыль, где легла половина коры;
///   Splitter_ShellL / Splitter_ShellR — половина коры, корень в её центре,
///     ребёнок «Mesh» стоит так, что при корне в позе узла тела половина
///     ложится ровно на тело (SplitterCombatView ведёт полёт).
/// Системы — один залп, местные оси, постоянное зерно: вид ведёт их Simulate
/// по возрасту от тика Sim.
/// </summary>
public static class SplitterVfxSetup
{
    private const string Revision = "SplitterVfxV1";
    private const string Root = "Assets/Resources/VFX/Splitter";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string ShellLeftModel = Root + "/ShellHalfL.fbx";
    private const string ShellRightModel = Root + "/ShellHalfR.fbx";
    private const string MushroomModel = Root + "/MushroomCap.fbx";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";

    // Рамки половин в осях тела Unity (debris/shells_report.json: (-x, z, -y) Blender).
    private static readonly Bounds ShellLeftBounds = FromMinMax(new Vector3(-.8315f, .1188f, -.494f), new Vector3(.0041f, 1.224f, .5201f));
    private static readonly Bounds ShellRightBounds = FromMinMax(new Vector3(.0227f, .0451f, -.5333f), new Vector3(.8227f, 1.1391f, .5194f));

    // Тона целевого кадра: тёплая охра пыли, светлое и тёмное дерево, свежая зелень.
    private static readonly Color DustLight = new Color(.80f, .63f, .43f), DustDark = new Color(.62f, .47f, .31f);
    private static readonly Color BarkLight = new Color(.70f, .55f, .40f), BarkDark = new Color(.46f, .34f, .24f);
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f), SoilDark = new Color(.34f, .24f, .15f);
    private static readonly Color LeafLight = new Color(.56f, .74f, .28f), LeafDark = new Color(.38f, .56f, .18f);

    private sealed class Kit
    {
        public Material Dust, Bark, Clod, Leaf, Shell;
        public Mesh LeafMesh, Cap;
    }

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += () => Install(false);

    [MenuItem("Разлом/Расщепень/VFX распада: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Расщепень/VFX распада: пересобрать")]
    public static void Build() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(ShellLeftModel) || !File.Exists(ShellRightModel)) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisWood) == null)
        {
            Debug.LogWarning("[splitter-vfx] Нет пака CFXR — раскол Расщепеня не собран.");
            return;
        }
        if (!force && Built()) return;
        Rebuild();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(Root + "/VFX_Splitter_Burst.prefab");
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Splitter_ShellL.prefab") != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Splitter_ShellR.prefab") != null;
    }

    private static void Rebuild()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "Splitter");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        foreach (string model in new[] { ShellLeftModel, ShellRightModel, MushroomModel }) ImportModel(model);
        var kit = Materials();
        SaveShell("Splitter_ShellL", ShellLeftModel, ShellLeftBounds, kit.Shell);
        SaveShell("Splitter_ShellR", ShellRightModel, ShellRightBounds, kit.Shell);
        SaveBurst(kit);
        SaveShellLand(kit);
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(Root + "/VFX_Splitter_Burst.prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[splitter-vfx] Раскол: пыль, щепки, листья, шляпки и две половины коры собраны, ревизия " + Revision + ".");
    }

    // ---------------------------------------------------------------- models

    /// <summary>Статичный меш без материалов, анимации и свёртки иерархии: корень файла — единица.</summary>
    private static void ImportModel(string path)
    {
        if (!File.Exists(path)) return;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { AssetDatabase.ImportAsset(path); importer = AssetImporter.GetAtPath(path) as ModelImporter; }
        if (importer == null) return;
        bool changed = importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation
            || importer.animationType != ModelImporterAnimationType.None || !importer.preserveHierarchy
            || importer.importBlendShapes || importer.importCameras || importer.importLights;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.preserveHierarchy = true;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        if (changed) importer.SaveAndReimport();
    }

    private static Mesh LoadMesh(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh) return mesh;
        return null;
    }

    private static Bounds FromMinMax(Vector3 min, Vector3 max)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    /// <summary>
    /// Половина коры: корень в центре её рамки, ребёнок «Mesh» — копия модели,
    /// сдвинутая на −центр. Оси импорта проверяются по рамке из Blender: если
    /// Unity повернул или отмасштабировал файл иначе, чем тело, подбирается
    /// поправка (поворот и масштаб), при которой рамки совпадают.
    /// </summary>
    private static void SaveShell(string name, string modelPath, Bounds expected, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { Debug.LogWarning("[splitter-vfx] Нет модели " + modelPath); return; }
        var root = new GameObject(name);
        try
        {
            var wrap = new GameObject("Mesh");
            wrap.transform.SetParent(root.transform, false);
            var copy = Object.Instantiate(model);
            copy.name = model.name;
            copy.transform.SetParent(wrap.transform, false);
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
                if (component is Collider || component is Animator) Object.DestroyImmediate(component);
            foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                int slots = filter != null && filter.sharedMesh != null ? Mathf.Max(1, filter.sharedMesh.subMeshCount) : 1;
                var materials = new Material[slots];
                for (int i = 0; i < slots; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            }

            Quaternion[] turns = { Quaternion.identity, Quaternion.Euler(-90f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f),
                Quaternion.Euler(0f, 180f, 0f), Quaternion.Euler(-90f, 180f, 0f), Quaternion.Euler(90f, 180f, 0f) };
            float[] scales = { 1f, .01f, 100f };
            float best = float.MaxValue;
            Quaternion bestTurn = Quaternion.identity;
            float bestScale = 1f;
            foreach (var turn in turns)
                foreach (float scale in scales)
                {
                    wrap.transform.localPosition = Vector3.zero;
                    wrap.transform.localRotation = turn;
                    wrap.transform.localScale = Vector3.one * scale;
                    Bounds b = Measure(root);
                    float score = (b.min - expected.min).magnitude + (b.max - expected.max).magnitude;
                    if (score < best) { best = score; bestTurn = turn; bestScale = scale; }
                }
            wrap.transform.localRotation = bestTurn;
            wrap.transform.localScale = Vector3.one * bestScale;
            wrap.transform.localPosition = Vector3.zero;
            if (bestTurn != Quaternion.identity || bestScale != 1f)
                Debug.LogWarning($"[splitter-vfx] {name}: импорт повёрнут/масштабирован иначе тела — поправка {bestTurn.eulerAngles} ×{bestScale}.");
            if (best > .15f)
                Debug.LogWarning($"[splitter-vfx] {name}: рамка не совпала с телом ({best:0.00} м) — проверь оси экспорта cut_shells.py.");
            Bounds fitted = Measure(root);
            wrap.transform.localPosition = -fitted.center;
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Bounds Measure(GameObject root)
    {
        bool any = false;
        var bounds = new Bounds();
        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                else bounds.Encapsulate(corner);
            }
        }
        return bounds;
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit
        {
            Dust = Plain(PackCopy("M_Splitter_Dust", CfxrSmokeBlurred)),
            Bark = PackCopy("M_Splitter_Bark", CfxrDebrisWood),
            Clod = PackCopy("M_Splitter_Clod", CfxrDebrisUnlit),
            Leaf = Unlit(PackCopy("M_Splitter_Leaf", CfxrLeafMaterial)),
            Shell = ShellMaterial(),
            LeafMesh = LoadMesh(CfxrLeafMesh),
            Cap = CapMesh()
        };
        return kit;
    }

    /// <summary>
    /// Кора обломков — Lit тела, но двусторонний: срез закрыт грубо, и изнанка
    /// шляпки гриба — открытая поверхность.
    /// </summary>
    private static Material ShellMaterial()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Splitter_Shell.mat", lit);
        SplitterBuilder.ApplyLit(material);
        material.SetFloat("_Cull", 0f);
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (source == null) return material;
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

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят облако у земли.</summary>
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

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        if (material == null) return null;
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_FADING_ON", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        foreach (string property in new[] { "_UseLighting", "_UseNormalMap", "_UseSP", "_CFXR_DITHERED_SHADOWS" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Шляпка гриба для мешевых частиц: меш из MushroomCap.fbx, центр в нуле,
    /// наибольший размер 1 (размер задаёт частица). Цвета вершин — Color32:
    /// float-цвета меша ломают цвет частиц.
    /// </summary>
    private static Mesh CapMesh()
    {
        var source = LoadMesh(MushroomModel);
        if (source == null) return null;
        string path = GeometryFolder + "/SplitterMushroomCap.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = "SplitterMushroomCap" };
            AssetDatabase.CreateAsset(mesh, path);
        }
        Bounds b = source.bounds;
        float size = Mathf.Max(1e-5f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        var vertices = source.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - b.center) / size;
        var colors = new Color32[vertices.Length];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.normals = source.normals;
        mesh.uv = source.uv;
        mesh.colors32 = colors;
        mesh.triangles = source.triangles;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
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

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    private static ParticleSystem Particles(GameObject root, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay, Vector3 at, float cone, float radius)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(root.name + "/" + name);
        particles.transform.localPosition = at;
        // Конус пака смотрит по +Z: ось поднимаем вверх.
        particles.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        return particles;
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

    /// <summary>Щепки и комья: спрайты CFXR 3×3, баллистика, отскок от земли корня, тают в конце жизни.</summary>
    private static void Debris(GameObject root, string name, Material material, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float life, Color light, Color dark)
    {
        if (material == null) return;
        var particles = Particles(root, name, count, life * .8f, life, speedMin, speedMax, sizeMin, sizeMax, 0f, at, cone, radius);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
        Collide(particles, root.transform, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .78f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
    }

    /// <summary>Мешевые обломки: листья CFXR и шляпки грибов — кувыркаются в 3D и ложатся на землю.</summary>
    private static void Tumblers(GameObject root, string name, Mesh mesh, Material material, int count, Vector3 at, float cone,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float drag, float life, Color light, Color dark)
    {
        if (material == null) return;
        var particles = Particles(root, name, count, life * .8f, life, speedMin, speedMax, sizeMin, sizeMax, 0f, at, cone, .25f);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        if (drag > 0f)
        {
            var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(drag);
            limit.dampen = .12f;
        }
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Collide(particles, root.transform, .08f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .82f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
    }

    /// <summary>Пыль: размытые облака CFXR (кадр 2×2 наугад), тёплая охра, тормозит, растёт и тает; flat — лежит на земле.</summary>
    private static void Dust(GameObject root, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, bool flat)
    {
        if (kit.Dust == null) return;
        // Стоячее облако поднято на половину размера: иначе земля срезала бы его прямой линией.
        var particles = Particles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, 0f,
            flat ? at : at + Vector3.up * (.45f * sizeMax), cone, radius);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.03f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(DustLight.r, DustLight.g, DustLight.b, alpha),
            new Color(DustDark.r, DustDark.g, DustDark.b, alpha));
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(.25f);
        limit.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .45f, .25f, .9f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.06f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        Sheet(particles, 2, 3.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Dust;
        renderer.sortingFudge = flat ? 1f : -1f;
    }

    // ---------------------------------------------------------------- prefabs

    private static void Save(GameObject root)
    {
        try { PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + root.name + ".prefab"); }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Раскол (1-burst-leap). Корень у ног Расщепеня, +Z — его взгляд. Клуб
    /// тёплой пыли из тела и юбка по земле, щепки коры и комья вверх-наружу,
    /// листья планируют, 4–5 шляпок грибов кувыркаются. Детёныш — тот же
    /// префаб в масштабе 0,6 (масштаб иерархии).
    /// </summary>
    private static void SaveBurst(Kit kit)
    {
        var root = new GameObject("VFX_Splitter_Burst");
        Dust(root, kit, "Puff", 11, Vector3.up * .3f, 75f, .35f, 1.0f, 2.4f, .55f, 1.0f, .85f, 1.3f, .50f, false);
        Dust(root, kit, "Skirt", 12, Vector3.up * .05f, 88f, .40f, 1.8f, 3.0f, .9f, 1.5f, 1.0f, 1.5f, .36f, true);
        Debris(root, "Splinters", kit.Bark, 22, Vector3.up * .55f, 65f, .30f, 3.0f, 5.5f, .10f, .24f, 1.4f, 1.6f, BarkLight, BarkDark);
        Debris(root, "Chips", kit.Clod, 8, Vector3.up * .2f, 55f, .30f, 2.0f, 3.6f, .06f, .12f, 1.6f, 1.4f, SoilLight, SoilDark);
        Tumblers(root, "Leaves", kit.LeafMesh, kit.Leaf, 12, Vector3.up * .7f, 75f, 1.8f, 3.6f, .16f, .26f, .45f, 1.6f, 2.0f, LeafLight, LeafDark);
        if (kit.Cap != null)
            Tumblers(root, "Caps", kit.Cap, kit.Shell, 5, Vector3.up * .8f, 55f, 2.4f, 4.2f, .09f, .15f, 1.3f, 0f, 1.8f, Color.white, Color.white);
        else
            Debris(root, "Caps", kit.Clod, 5, Vector3.up * .8f, 55f, .25f, 2.4f, 4.2f, .08f, .13f, 1.3f, 1.8f,
                new Color(.95f, .45f, .22f), new Color(.80f, .32f, .16f));
        Save(root);
    }

    /// <summary>Касание половины коры: низкая пыль по земле и пара комьев.</summary>
    private static void SaveShellLand(Kit kit)
    {
        var root = new GameObject("VFX_Splitter_ShellLand");
        Dust(root, kit, "Skirt", 6, Vector3.up * .04f, 88f, .25f, .8f, 1.6f, .5f, .9f, .6f, .9f, .34f, true);
        Debris(root, "Chips", kit.Clod, 5, Vector3.up * .08f, 60f, .20f, 1.2f, 2.2f, .05f, .10f, 1.6f, .9f, SoilLight, SoilDark);
        Save(root);
    }
}
