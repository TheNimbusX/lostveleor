using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Расщепень (и его детёныш — то же тело в масштабе 0,6): игровое тело из
/// пакета ART/…/8.forest-splitbark/production/animation/unity_package.
///
/// Импорт Generic без корневого движения (все клипы на месте), клипы копиями в
/// .anim, контроллер из шести состояний, каждое — Motion Time от параметра
/// «&lt;Роль&gt;Phase»: их ведёт SplitterAnimatorView от тиков Sim.
///
/// Материал — URP Lit без подъёма яркости (белый _BaseColor): цвет, нормали
/// (NormalGL — соглашение Unity) и ORM. ORM (R — затенение, G — шероховатость,
/// B — металл) Lit напрямую не читает: сборщик раскладывает его в карту
/// металл/гладкость (R — металл, A — 1 − шероховатость) и карту затенения (G).
///
/// Контур врага. Общая обводка (UnitOutlineFeature) рисует маску из прохода
/// UnitOutlineMask шейдера Razlom/Texture Toon, которого у Lit нет. Второй
/// материал на том же меше — Texture Toon с выключенными проходами, кроме
/// маски: тело рисует Lit, а ширину и цвет контура ArenaView ставит блоком
/// свойств в оба слота, как всем врагам. Вспышки попадания и растворения у Lit
/// нет: смерть Расщепеня — раскол (SplitterCombatView).
///
/// После тела собираются обломки и VFX раскола (SplitterVfxSetup).
/// </summary>
public static class SplitterBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Splitter/";
    private const string Model = Root + "ForestSplitter.fbx";
    private const string PrefabPath = Root + "ForestSplitter_Runtime.prefab";
    public const string MaterialPath = Root + "ForestSplitter.mat";
    public const string ColorPath = Root + "ForestSplitter_Color.png";
    public const string NormalPath = Root + "ForestSplitter_NormalGL.png";
    private const string OrmPath = Root + "ForestSplitter_ORM.png";
    public const string MetallicPath = Root + "ForestSplitter_MetallicSmoothness.png";
    public const string OcclusionPath = Root + "ForestSplitter_Occlusion.png";
    private const string MaskPath = Root + "ForestSplitter_OutlineMask.mat";

    /// <summary>Рост взрослого по пакету (rig_report: height_m).</summary>
    public const float Height = 1.3f;

    private static readonly string[] Roles = { "Idle", "Walk", "Bite", "Hit", "Death", "Pop" };

    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () =>
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Model)) return;
        if (!File.Exists(PrefabPath)) Build();
    };

    [MenuItem("Разлом/Расщепень/Собрать представление")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        ConfigureTextures();
        DeriveOrmMaps();

        AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var originalClips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__")).ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "ForestSplitter.controller");
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "ForestSplitter.controller");
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>();
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        foreach (string role in Roles)
        {
            string take = "ForestSplitter_" + role;
            var original = originalClips.FirstOrDefault(c => c.name == take || c.name.EndsWith("|" + take) || c.name.EndsWith(take));
            if (original == null)
                throw new InvalidOperationException("[splitter] Нет клипа " + take + ": " + string.Join(",", originalClips.Select(c => c.name)));
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip);
            clip.name = role; // имя = имя файла .anim: иначе Unity ругается на каждой пересборке
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk";
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            var state = machine.AddState(role);
            state.motion = clip;
            state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase";
            state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
        }

        var material = BodyMaterial();
        var mask = OutlineMask();

        var root = new GameObject("ForestSplitter_Runtime");
        float height;
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.transform.SetParent(root.transform, false);
            var guide = body.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "FacingGuide");
            if (guide == null) throw new InvalidOperationException("[splitter] Нет метки направления FacingGuide");
            Vector3 facing = guide.position - body.transform.position;
            facing.y = 0;
            body.transform.localRotation = Quaternion.FromToRotation(facing.normalized, Vector3.forward);

            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>();
            height = MeasureHeight(renderers);
            // Пакет снят в метрах (1,3 м). Если импорт дал другой рост — чиним масштабом
            // узла, а не глобальным масштабом импорта: половины коры (SplitterVfxSetup)
            // ставятся по мировому масштабу этого узла и поедут вместе с ним.
            if (height > .01f && Mathf.Abs(height - Height) > .12f)
            {
                Debug.LogWarning($"[splitter] Рост импорта {height:0.000} м вместо {Height} — узел тела масштабирован.");
                body.transform.localScale *= Height / height;
            }

            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = mask != null ? new[] { material, mask } : new[] { material };
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 4f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            root.AddComponent<Game.View.SplitterAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[splitter] Клипов: {Roles.Length}, рост импорта {height:0.000} м, Lit + маска контура"
                  + (mask != null ? "" : " (нет Razlom/Texture Toon — без контура)") + ", игровой prefab готов.");
        SplitterVfxSetup.Build();
    }

    private static float MeasureHeight(SkinnedMeshRenderer[] renderers)
    {
        // Вершины меша в позе привязки через узел рендера: рамка рендера в редакторе
        // бывает свободной, рост по ней соврал бы.
        float top = float.MinValue, bottom = float.MaxValue;
        foreach (var r in renderers)
        {
            if (r.sharedMesh == null) continue;
            Matrix4x4 m = r.transform.localToWorldMatrix;
            Bounds b = r.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                float y = m.MultiplyPoint3x4(c).y;
                top = Mathf.Max(top, y);
                bottom = Mathf.Min(bottom, y);
            }
        }
        return top > bottom ? top - Mathf.Min(0f, bottom) : 0f;
    }

    // ------------------------------------------------------------ textures

    private static void ConfigureTextures()
    {
        ConfigureTexture(ColorPath, TextureImporterType.Default, true);
        ConfigureTexture(NormalPath, TextureImporterType.NormalMap, false);
        ConfigureTexture(OrmPath, TextureImporterType.Default, false);
    }

    private static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
    {
        if (!File.Exists(path)) return;
        AssetDatabase.ImportAsset(path);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool changed = importer.textureType != type || importer.sRGBTexture != srgb || importer.maxTextureSize != 2048;
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = false;
        if (changed) importer.SaveAndReimport();
    }

    /// <summary>
    /// ORM пакета → две карты Lit. Пересобираются, только если ORM новее: PNG читается
    /// с диска, импорт ORM остаётся нечитаемым.
    /// </summary>
    private static void DeriveOrmMaps()
    {
        if (!File.Exists(OrmPath)) { Debug.LogWarning("[splitter] Нет " + OrmPath + " — Lit без металла и затенения."); return; }
        DateTime orm = File.GetLastWriteTimeUtc(OrmPath);
        bool fresh = File.Exists(MetallicPath) && File.Exists(OcclusionPath)
                     && File.GetLastWriteTimeUtc(MetallicPath) >= orm && File.GetLastWriteTimeUtc(OcclusionPath) >= orm;
        if (!fresh)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(OrmPath), false))
                    throw new InvalidOperationException("[splitter] ORM не читается: " + OrmPath);
                Color32[] pixels = source.GetPixels32();
                var metallic = new Color32[pixels.Length];
                var occlusion = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    metallic[i] = new Color32(p.b, p.b, p.b, (byte)(255 - p.g));
                    occlusion[i] = new Color32(p.r, p.r, p.r, 255);
                }
                WritePng(MetallicPath, metallic, source.width, source.height);
                WritePng(OcclusionPath, occlusion, source.width, source.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            AssetDatabase.ImportAsset(MetallicPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(OcclusionPath, ImportAssetOptions.ForceUpdate);
        }
        ConfigureTexture(MetallicPath, TextureImporterType.Default, false);
        ConfigureTexture(OcclusionPath, TextureImporterType.Default, false);
    }

    private static void WritePng(string path, Color32[] pixels, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    // ----------------------------------------------------------- materials

    private static Material LoadOrCreate(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        return material;
    }

    /// <summary>URP Lit тела. Им же (копией с двусторонней отрисовкой) рисуются обломки.</summary>
    public static Material BodyMaterial()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) throw new InvalidOperationException("[splitter] Нет шейдера URP Lit");
        var material = LoadOrCreate(MaterialPath, lit);
        material.name = "ForestSplitter";
        ApplyLit(material);
        material.SetFloat("_Cull", 2f);
        material.doubleSidedGI = false;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Текстуры и ключи Lit по пакету. Цвет — без подъёма яркости.</summary>
    public static void ApplyLit(Material material)
    {
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_WorkflowMode", 1f);
        material.DisableKeyword("_SPECULAR_SETUP");

        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");

        var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);
        material.SetTexture("_MetallicGlossMap", metallic);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetFloat("_Metallic", 0f);
        // С картой гладкость = A карты × _Smoothness; без карты — матовая кора.
        material.SetFloat("_Smoothness", metallic != null ? 1f : .15f);
        if (metallic != null) material.EnableKeyword("_METALLICSPECGLOSSMAP"); else material.DisableKeyword("_METALLICSPECGLOSSMAP");

        var occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(OcclusionPath);
        material.SetTexture("_OcclusionMap", occlusion);
        material.SetFloat("_OcclusionStrength", 1f);
        if (occlusion != null) material.EnableKeyword("_OCCLUSIONMAP"); else material.DisableKeyword("_OCCLUSIONMAP");
        material.SetFloat("_EnvironmentReflections", 1f);
        material.SetFloat("_SpecularHighlights", 1f);
    }

    /// <summary>
    /// Второй слот: Texture Toon только ради прохода UnitOutlineMask. Тело он не
    /// рисует — проходы цвета, свечения, тени и глубины у материала выключены.
    /// </summary>
    private static Material OutlineMask()
    {
        var toon = Shader.Find("Razlom/Texture Toon");
        if (toon == null) { Debug.LogWarning("[splitter] Нет шейдера Razlom/Texture Toon — Расщепень без контура врага."); return null; }
        var mask = LoadOrCreate(MaskPath, toon);
        mask.name = "ForestSplitter_OutlineMask";
        mask.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath));
        mask.SetFloat("_OutlineWidth", 0f);
        foreach (string pass in new[] { "UniversalForward", "SRPDefaultUnlit", "InkOutline", "ShadowCaster", "DepthOnly" })
            mask.SetShaderPassEnabled(pass, false);
        mask.SetShaderPassEnabled("UnitOutlineMask", true);
        EditorUtility.SetDirty(mask);
        return mask;
    }
}
