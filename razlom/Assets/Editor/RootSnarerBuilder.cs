using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// КОРНЕХВАТ (Forest_RootSnarer) — ИГРОВОЕ ТЕЛО ИЗ ПАКЕТА animation_r01 (26.09).
///
/// Пакет: ART/characters/act-1-enemies/7.forest-root-snarer/production/animation/
/// unity_package (FBX, export.json, verification.json, textures/). Копия лежит в
/// Resources/Characters/Forest_RootSnarer; нет файла — сборка докопирует его из ART.
///
/// • Импорт: Generic, аватар из модели, клипы на месте (корень не едет, root
///   motion выключен), без сжатия. ВАЖНО: RazlomCharacterImport переводит всё в
///   Resources/Characters в Humanoid и без анимации, если для папки нет своей
///   ветки (как у Вендиго и Камнекопыта), и затирает тангенсы сглаженными
///   нормалями — для URP Lit с картой нормалей это неверно. Сборка проверяет
///   итог импорта и говорит, чего не хватает.
/// • Клипы Idle 0–60 и Walk 0–16 — циклы, Slam 0–72, Hit 0–12, Death 0–45 —
///   копии .anim рядом; время каждого состояния — параметр «Роль»Phase
///   (RootSnarerAnimatorView ведёт его по тикам Sim).
/// • Материал — URP Lit: цвет, нормали (OpenGL), ORM (R — AO, G — шероховатость,
///   B — металл) разложен в карты URP: AO и Metallic/Smoothness. Без подъёма
///   яркости. Запасной вариант — тун-шейдер (вспышка попадания, растворение,
///   контур наведения), тоже без подъёма: пункт меню «…(тун-шейдер)».
/// • Префаб ForestRootSnarer_Runtime: корень с RootSnarerAnimatorView, тело —
///   модель (1,35 м, взгляд +Z) с Animator и контроллером.
/// </summary>
public static class RootSnarerBuilder
{
    public const string Root = "Assets/Resources/Characters/Forest_RootSnarer/";
    public const string Model = Root + "ForestRootSnarer.fbx";
    public const string Prefab = Root + "ForestRootSnarer_Runtime.prefab";
    private const string ControllerPath = Root + "ForestRootSnarer.controller";
    private const string MaterialPath = Root + "ForestRootSnarer.mat";
    private const string MaskPath = Root + "ForestRootSnarer_OutlineMask.mat";
    private const string ColorTexture = Root + "T_ForestRootSnarer_Color.png";
    private const string NormalTexture = Root + "T_ForestRootSnarer_NormalGL.png";
    private const string OrmTexture = Root + "T_ForestRootSnarer_ORM.png";
    private const string OcclusionTexture = Root + "T_ForestRootSnarer_Occlusion.png";
    private const string MetalSmoothTexture = Root + "T_ForestRootSnarer_MetalSmooth.png";

    /// <summary>Пакет относительно корня репозитория (папка над razlom/).</summary>
    private const string Package = "ART/characters/act-1-enemies/7.forest-root-snarer/production/animation/unity_package/";

    /// <summary>Рост модели по rig_report / optimization_report, м.</summary>
    public const float TargetHeight = 1.35f;

    private static readonly (string Role, int Frames, bool Loop)[] Roles =
    {
        ("Idle", 60, true), ("Walk", 16, true), ("Slam", 72, false), ("Hit", 12, false), ("Death", 45, false),
        // «Волна из корней» (27.09): 50 кадров = 50 тиков лечения, волна на 30-м.
        ("Mend", 50, false),
    };

    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () =>
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Model) || File.Exists(Prefab)) return;
        try { Build(); }
        catch (Exception e) { Debug.LogWarning("[rootsnarer] Представление не собрано: " + e.Message); }
    };

    [MenuItem("Разлом/Корнехват/Собрать представление")]
    public static void Build() => Build(false);

    [MenuItem("Разлом/Корнехват/Собрать представление (тун-шейдер)")]
    public static void BuildToon() => Build(true);

    public static void Build(bool toon)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Корнехват собирается только вне Play.");
        SyncPackage();
        ConfigureTextures();
        AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        if (importer == null) throw new InvalidOperationException("Нет модели " + Model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false; importer.importAnimation = true;
        importer.globalScale = 1f; importer.useFileScale = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importBlendShapes = false; importer.importCameras = false; importer.importLights = false;
        importer.SaveAndReimport();
        importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation)
            throw new InvalidOperationException("Импорт Корнехвата не Generic или без анимации: RazlomCharacterImport.OnPreprocessModel " +
                "переписывает настройки для Resources/Characters. Нужна ветка для «/Resources/Characters/Forest_RootSnarer/», как у Вендиго.");

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var originalClips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__", StringComparison.Ordinal)).ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>();
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        foreach (var (role, frames, loop) in Roles)
        {
            var original = originalClips.FirstOrDefault(c => c.name.EndsWith("RootSnarer_" + role, StringComparison.Ordinal));
            if (original == null)
                throw new InvalidOperationException("Нет клипа " + role + ": " + string.Join(", ", originalClips.Select(c => c.name)));
            float authored = original.length * original.frameRate;
            if (Mathf.Abs(authored - frames) > .6f)
                Debug.LogWarning($"[rootsnarer] Клип {original.name}: {authored:0.#} кадров вместо {frames} — фазы вида сдвинутся.");
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip);
            clip.name = role; // имя = имя файла .anim: иначе Unity ругается на каждой пересборке
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            var state = machine.AddState(role);
            state.motion = clip; state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase"; state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
        }

        var material = toon ? ToonMaterial() : LitMaterial();
        var mask = toon ? null : OutlineMask();
        var root = new GameObject("ForestRootSnarer_Runtime");
        float height;
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.transform.SetParent(root.transform, false);
            FaceForward(body);
            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("В модели нет SkinnedMeshRenderer.");
            foreach (var renderer in renderers)
            {
                var slots = Enumerable.Repeat(material, Mathf.Max(1, renderer.sharedMaterials.Length));
                renderer.sharedMaterials = (mask != null ? slots.Append(mask) : slots).ToArray();
                renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            height = MeasureHeight(body);
            // Масштаб трогаем, только если единицы явно разъехались (см вместо м и т.п.):
            // тело должно совпасть с радиусом тела Sim и дальностью удара.
            if (height > .01f && (height < TargetHeight * .5f || height > TargetHeight * 2f))
            {
                Debug.LogWarning($"[rootsnarer] Рост модели {height:0.###} м вместо {TargetHeight} — тело масштабируется.");
                body.transform.localScale *= TargetHeight / height;
                height = TargetHeight;
            }
            root.AddComponent<Game.View.RootSnarerAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log($"[rootsnarer] Клипов: {Roles.Length}, Generic rig, {(toon ? "тун-материал" : "URP Lit с нормалями и ORM")}, " +
                  $"рост {height:0.00} м, игровой prefab готов: {Prefab}.");
    }

    // ------------------------------------------------------------ package

    /// <summary>Недостающие файлы пакета копируются из ART (существующие не перезаписываются).</summary>
    private static void SyncPackage()
    {
        string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        bool copied = false;
        foreach (var (from, to) in new[]
        {
            (Package + "ForestRootSnarer.fbx", Model),
            (Package + "textures/T_ForestRootSnarer_Color.png", ColorTexture),
            (Package + "textures/T_ForestRootSnarer_NormalGL.png", NormalTexture),
            (Package + "textures/T_ForestRootSnarer_ORM.png", OrmTexture),
        })
        {
            if (File.Exists(to)) continue;
            string source = Path.Combine(repo, from);
            if (!File.Exists(source)) throw new FileNotFoundException("Нет файла пакета Корнехвата", source);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(source, to);
            copied = true;
        }
        if (copied) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    // ----------------------------------------------------------- textures

    private static void ConfigureTextures()
    {
        ConfigureTexture(ColorTexture, TextureImporterType.Default, true);
        ConfigureTexture(NormalTexture, TextureImporterType.NormalMap, false);
        ConfigureTexture(OrmTexture, TextureImporterType.Default, false);
        SplitOrm();
        ConfigureTexture(OcclusionTexture, TextureImporterType.Default, false);
        ConfigureTexture(MetalSmoothTexture, TextureImporterType.Default, false);
    }

    private static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Нет текстуры " + path);
        bool changed = importer.textureType != type || importer.sRGBTexture != srgb || !importer.mipmapEnabled
                       || importer.maxTextureSize != 2048;
        if (!changed) return;
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// ORM пакета в карты URP Lit: AO — в зелёном канале _OcclusionMap (URP читает
    /// .g), металл — в красном и гладкость (1 − шероховатость) — в альфе
    /// _MetallicGlossMap. Пересобирается, если ORM новее.
    /// </summary>
    private static void SplitOrm()
    {
        if (File.Exists(OcclusionTexture) && File.Exists(MetalSmoothTexture)
            && File.GetLastWriteTimeUtc(OcclusionTexture) >= File.GetLastWriteTimeUtc(OrmTexture)
            && File.GetLastWriteTimeUtc(MetalSmoothTexture) >= File.GetLastWriteTimeUtc(OrmTexture)) return;
        var orm = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!orm.LoadImage(File.ReadAllBytes(OrmTexture))) throw new InvalidOperationException("Не читается " + OrmTexture);
            var pixels = orm.GetPixels32();
            var occlusion = new Color32[pixels.Length];
            var metalSmooth = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte ao = pixels[i].r, rough = pixels[i].g, metal = pixels[i].b;
                occlusion[i] = new Color32(ao, ao, ao, 255);
                metalSmooth[i] = new Color32(metal, metal, metal, (byte)(255 - rough));
            }
            WritePng(OcclusionTexture, orm.width, orm.height, occlusion);
            WritePng(MetalSmoothTexture, orm.width, orm.height, metalSmooth);
        }
        finally { UnityEngine.Object.DestroyImmediate(orm); }
        AssetDatabase.ImportAsset(OcclusionTexture, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(MetalSmoothTexture, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void WritePng(string path, int width, int height, Color32[] pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    // ---------------------------------------------------------- materials

    /// <summary>URP Lit: цвет без подъёма яркости, нормали, AO и металл/гладкость из ORM.</summary>
    private static Material LitMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Нет шейдера URP/Lit.");
        var material = LoadOrCreate(shader);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorTexture));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Surface", 0f);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalTexture));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OcclusionTexture));
        material.SetFloat("_OcclusionStrength", 1f);
        material.EnableKeyword("_OCCLUSIONMAP");
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MetalSmoothTexture));
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_SPECULAR_SETUP");
        // С картой металла гладкость = альфа карты × _Smoothness; канал — альфа карты металла.
        material.SetFloat("_Smoothness", 1f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.SetFloat("_Metallic", 0f);
        material.DisableKeyword("_EMISSION");
        return material;
    }

    /// <summary>Тун-шейдер врагов (вспышка, растворение, контур) — без подъёма яркости, без нормалей и ORM.</summary>
    private static Material ToonMaterial()
    {
        var shader = Shader.Find("Razlom/Texture Toon");
        if (shader == null) throw new InvalidOperationException("Нет шейдера Razlom/Texture Toon.");
        var material = LoadOrCreate(shader);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorTexture));
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        material.SetColor("_MidColor", new Color(.94f, .90f, .91f));
        material.SetFloat("_OutlineWidth", 0f);
        material.SetFloat("_Smoothness", .12f);
        return material;
    }

    /// <summary>
    /// Второй слот под URP Lit: Texture Toon только ради прохода UnitOutlineMask —
    /// постоянной кромки врага (ArenaView), как у Расщепеня. Тело он не рисует:
    /// проходы цвета, свечения, тени и глубины выключены. Тун-телу не нужен.
    /// </summary>
    private static Material OutlineMask()
    {
        var toon = Shader.Find("Razlom/Texture Toon");
        if (toon == null) { Debug.LogWarning("[rootsnarer] Нет шейдера Razlom/Texture Toon — Корнехват без контура врага."); return null; }
        var mask = AssetDatabase.LoadAssetAtPath<Material>(MaskPath);
        if (mask == null) { mask = new Material(toon) { name = "ForestRootSnarer_OutlineMask" }; AssetDatabase.CreateAsset(mask, MaskPath); }
        else if (mask.shader != toon) mask.shader = toon;
        mask.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorTexture));
        mask.SetFloat("_OutlineWidth", 0f);
        foreach (string pass in new[] { "UniversalForward", "SRPDefaultUnlit", "InkOutline", "ShadowCaster", "DepthOnly" })
            mask.SetShaderPassEnabled(pass, false);
        mask.SetShaderPassEnabled("UnitOutlineMask", true);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static Material LoadOrCreate(Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "ForestRootSnarer" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
        }
        return material;
    }

    // --------------------------------------------------------------- body

    /// <summary>
    /// Взгляд модели — +Z (export.json). Проверяется по скелету: голова впереди
    /// таза на ~0,9 м. Разворот только вокруг вертикали — без переворота.
    /// </summary>
    private static void FaceForward(GameObject body)
    {
        var bones = body.GetComponentsInChildren<Transform>(true);
        var guide = bones.FirstOrDefault(t => t.name == "FacingGuide");
        var head = bones.FirstOrDefault(t => t.name == "head");
        var pelvis = bones.FirstOrDefault(t => t.name == "pelvis");
        Vector3 facing = guide != null ? guide.position - body.transform.position
            : head != null && pelvis != null ? head.position - pelvis.position : Vector3.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) return;
        float yaw = Vector3.SignedAngle(facing.normalized, Vector3.forward, Vector3.up);
        body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * body.transform.localRotation;
        if (Mathf.Abs(yaw) > 1f) Debug.Log($"[rootsnarer] Модель довёрнута на {yaw:0.#}° к +Z.");
    }

    /// <summary>Рост тела в позе покоя, м: запечённый скин, мировые координаты.</summary>
    private static float MeasureHeight(GameObject body)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh();
            try
            {
                // Без масштаба узла BakeMesh отдаёт вершины уже в мировых единицах; с ним
                // (true) — в осях узла, и рост FBX с узлом 100× читался как 0,013 м.
                renderer.BakeMesh(baked, false);
                var toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices)
                {
                    float y = toWorld.MultiplyPoint3x4(v).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }
            catch (Exception e) { Debug.LogWarning("[rootsnarer] Рост не измерен: " + e.Message); }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return max > min ? max - min : 0f;
    }
}
