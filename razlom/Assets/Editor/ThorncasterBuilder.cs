using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// ШИПОМЁТ — ИГРОВОЕ ТЕЛО (план мобов леса, 26.09; модель, риг и клипы
/// приняты владельцем). Пакет арт-конвейера
/// (ART/…/5.forest-elite-shipomet/production/animation/unity_package) лежит
/// в Resources/Characters/Forest_Thorncaster: FBX с семью дублями по 30 к/с
/// (Idle 0–60, Walk 0–24, LineCast 0–75, Burst 0–36, Shot 0–33, Hit 0–12,
/// Death 0–48), цвет, нормали (GL) и ORM.
///
/// Скелет — авториг Meshy с именами Mixamo в A-позе. Берём Generic: дубли
/// сняты на этом же скелете, гуманоидный перенос не нужен и только исказил бы
/// шипы на руках. Корня движения нет (Hips качается на месте), зеркала нет.
/// Клипы копируются в .anim рядом, как у Вендиго и Камнекопыта: Idle и Walk
/// в петле, остальные разовые. Контроллер — семь состояний, у каждого время
/// задаёт параметр «&lt;Роль&gt;Phase»: ThorncasterAnimatorView ведёт позу по тикам
/// Sim (замах, контакт, стойка), а не по часам Unity.
///
/// МАТЕРИАЛ. Тело врага — на нашем тун-шейдере, как у Вендиго, Камнекопыта и
/// Хранителя: только у него есть проход UnitOutlineMask (контур врага под
/// курсором), вспышка попадания _HitFlash и растворение смерти _DeathFade —
/// ArenaView ведёт их у всех врагов. Цвет белый, без подъёма яркости. Нормали
/// и ORM тун-шейдер не читает; для сравнения собирается и материал URP Lit со
/// всеми тремя картами (ORM раскладывается на карты URP: металл/гладкость и
/// затенение) — меню «…(URP Lit)». У тела на Lit нет контура, вспышки и
/// растворения: это вариант на показ владельцу, не замена.
///
/// Рост — 2,7 м: меряется по запечённой сетке в позе покоя, и тело
/// масштабируется под него. Направление — от кости Head к headfront.
/// </summary>
public static class ThorncasterBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Thorncaster/";
    private const string Model = Root + "ForestThorncaster.fbx";
    private const string ColorMap = Root + "ForestThorncaster_Color.png";
    private const string NormalMap = Root + "ForestThorncaster_NormalGL.png";
    private const string OrmMap = Root + "ForestThorncaster_ORM.png";
    private const string MetalSmoothMap = Root + "ForestThorncaster_MetalSmooth.png";
    private const string OcclusionMap = Root + "ForestThorncaster_Occlusion.png";
    private const string ToonMaterialPath = Root + "ForestThorncaster.mat";
    private const string LitMaterialPath = Root + "ForestThorncaster_Lit.mat";
    private const string ControllerPath = Root + "ForestThorncaster.controller";
    public const string PrefabPath = Root + "ForestThorncaster_Runtime.prefab";

    /// <summary>Рост тела в игре, м: 2,7 × 1,1 — ревью 01.10 «модельку увеличить на 10%» (ThorncasterAnimatorView.BodyGrowth).</summary>
    public const float Height = Game.View.ThorncasterAnimatorView.BodyHeight;

    /// <summary>Роли — имена состояний контроллера и дублей FBX (ForestThorncaster_&lt;Роль&gt;).</summary>
    public static readonly string[] Roles = { "Idle", "Walk", "LineCast", "Burst", "Shot", "Hit", "Death" };

    /// <summary>Последний кадр дубля по export.json: вид переводит кадры в доли клипа по нему.</summary>
    private static readonly int[] LastFrames = { 60, 24, 75, 36, 33, 12, 48 };

    /// <summary>
    /// Гладкость Lit — множитель к (1 − шероховатость) из ORM: кора в среднем
    /// 0,48 гладкости, под солнцем арены она блестела бы мокрой.
    /// </summary>
    private const float LitSmoothnessScale = .7f;

    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () =>
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Model)) return;
        if (File.Exists(PrefabPath)) return;
        try { Build(); }
        catch (Exception e) { Debug.LogException(e); }
    };

    [MenuItem("Разлом/Шипомёт/Собрать представление")]
    public static void Build() => Build(false);

    [MenuItem("Разлом/Шипомёт/Собрать представление (URP Lit: нормали и ORM)")]
    public static void BuildLit() => Build(true);

    public static void Build(bool lit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Шипомёт собирается только вне Play.");
        foreach (string path in new[] { Model, ColorMap, NormalMap, OrmMap })
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Нет файла пакета Шипомёта: " + path);
            if (AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        ConfigureTexture(ColorMap, true, false);
        ConfigureTexture(NormalMap, false, true);
        ConfigureTexture(OrmMap, false, false);

        AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false; importer.importAnimation = true;
        importer.isReadable = true;
        // Материал FBX не нужен: тело получает свой (он же не теряет текстуру при переимпорте).
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false; importer.importLights = false;
        importer.SaveAndReimport();

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (source == null) throw new InvalidOperationException("FBX Шипомёта не импортирован: " + Model);
        var controller = BuildController();
        var toon = ToonMaterial();
        var material = lit ? LitMaterial() : toon;
        SavePrefab(source, controller, material);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        Debug.Log("[thorncaster] Клипов: " + Roles.Length + ", Generic rig, материал " + (lit ? "URP Lit (нормали, ORM)" : "тун")
            + ", игровой prefab готов: " + PrefabPath);
    }

    // ------------------------------------------------------------- clips

    private static AnimatorController BuildController()
    {
        var originalClips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__")).ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>(); controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        for (int i = 0; i < Roles.Length; i++)
        {
            string role = Roles[i], take = "ForestThorncaster_" + role;
            // Имя дубля FBX — имя полосы NLA; Blender иногда приписывает объект («ARM_…|»).
            var original = originalClips.FirstOrDefault(c => c.name == take)
                ?? originalClips.FirstOrDefault(c => c.name.EndsWith("|" + take))
                ?? originalClips.FirstOrDefault(c => c.name.EndsWith(take));
            if (original == null)
                throw new InvalidOperationException("Нет дубля " + take + ": " + string.Join(",", originalClips.Select(c => c.name)));
            int frames = Mathf.RoundToInt(original.length * original.frameRate);
            if (frames != LastFrames[i] || Mathf.Abs(original.frameRate - 30f) > .01f)
                Debug.LogWarning($"[thorncaster] Дубль {take}: {frames} кадров при {original.frameRate} к/с, ждали {LastFrames[i]} при 30 — " +
                                 "фазы ThorncasterAnimatorView разойдутся с Sim.");
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip); clip.name = role; // имя = имя файла .anim
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk"; settings.loopBlend = false; settings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
            var state = machine.AddState(role); state.motion = clip; state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase"; state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
        }
        return controller;
    }

    // --------------------------------------------------------- materials

    private static Material ToonMaterial()
    {
        var shader = Shader.Find("Razlom/Texture Toon");
        if (shader == null) throw new InvalidOperationException("Нет шейдера Razlom/Texture Toon.");
        var material = LoadOrCreateMaterial(ToonMaterialPath, shader);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorMap));
        // Белый: без подъёма яркости (правило владельца — персонажей не высветлять).
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        material.SetColor("_MidColor", new Color(.94f, .90f, .91f));
        material.SetFloat("_OutlineWidth", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LitMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Нет шейдера URP Lit.");
        DeriveOrmMaps();
        var material = LoadOrCreateMaterial(LitMaterialPath, shader);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ColorMap));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMap));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
        // ORM: R — затенение, G — шероховатость, B — металл. URP Lit читает металл из R и
        // гладкость из A карты металла, затенение — из G своей карты: раскладка в DeriveOrmMaps.
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MetalSmoothMap));
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetFloat("_Smoothness", LitSmoothnessScale);
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OcclusionMap));
        material.SetFloat("_OcclusionStrength", 1f);
        material.EnableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// ORM → карты URP Lit: металл/гладкость (R = металл из B, A = 1 − шероховатость из G)
    /// и затенение (серое из R). Пересобираются, только если ORM новее.
    /// </summary>
    private static void DeriveOrmMaps()
    {
        DateTime orm = File.GetLastWriteTimeUtc(OrmMap);
        if (File.Exists(MetalSmoothMap) && File.Exists(OcclusionMap)
            && File.GetLastWriteTimeUtc(MetalSmoothMap) >= orm && File.GetLastWriteTimeUtc(OcclusionMap) >= orm) return;
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        var metal = (Texture2D)null; var occlusion = (Texture2D)null;
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(OrmMap))) throw new InvalidOperationException("ORM не читается: " + OrmMap);
            var pixels = source.GetPixels32();
            var metalPixels = new Color32[pixels.Length];
            var occlusionPixels = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                metalPixels[i] = new Color32(p.b, p.b, p.b, (byte)(255 - p.g));
                occlusionPixels[i] = new Color32(p.r, p.r, p.r, 255);
            }
            metal = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            metal.SetPixels32(metalPixels); metal.Apply();
            File.WriteAllBytes(MetalSmoothMap, metal.EncodeToPNG());
            occlusion = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            occlusion.SetPixels32(occlusionPixels); occlusion.Apply();
            File.WriteAllBytes(OcclusionMap, occlusion.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (metal != null) UnityEngine.Object.DestroyImmediate(metal);
            if (occlusion != null) UnityEngine.Object.DestroyImmediate(occlusion);
        }
        AssetDatabase.ImportAsset(MetalSmoothMap, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(OcclusionMap, ImportAssetOptions.ForceSynchronousImport);
        ConfigureTexture(MetalSmoothMap, false, false);
        ConfigureTexture(OcclusionMap, false, false);
    }

    private static void ConfigureTexture(string path, bool srgb, bool normal)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Нет текстуры: " + path);
        // Карта с пустым .meta (только guid) приходила кубмапой: материал терял цвет.
        bool changed = importer.importSettingsMissing;
        if (importer.textureShape != TextureImporterShape.Texture2D) { importer.textureShape = TextureImporterShape.Texture2D; changed = true; }
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type) { importer.textureType = type; changed = true; }
        if (!normal && importer.sRGBTexture != srgb) { importer.sRGBTexture = srgb; changed = true; }
        if (importer.alphaIsTransparency) { importer.alphaIsTransparency = false; changed = true; }
        if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; changed = true; }
        if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
        if (changed) importer.SaveAndReimport();
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        else if (material.shader != shader) material.shader = shader;
        material.name = Path.GetFileNameWithoutExtension(path);
        return material;
    }

    // ------------------------------------------------------------ prefab

    private static void SavePrefab(GameObject source, AnimatorController controller, Material material)
    {
        var root = new GameObject("ForestThorncaster_Runtime");
        var baked = new Mesh();
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            var bones = body.GetComponentsInChildren<Transform>(true);
            Transform head = bones.FirstOrDefault(t => t.name == "Head"), front = bones.FirstOrDefault(t => t.name == "headfront");
            if (bones.All(t => t.name != "Muzzle_RightSpike"))
                Debug.LogWarning("[thorncaster] Нет сокета Muzzle_RightSpike: выпуск шипа встанет по замеру export.json.");

            // Взгляд: от кости головы к кости «перед головы», только поворот вокруг вертикали
            // (FromToRotation на развороте в 180° мог бы положить тело набок).
            Vector3 facing = head != null && front != null ? front.position - head.position : Vector3.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning("[thorncaster] Кости Head/headfront не дали направления — тело смотрит по +Z как есть.");
                facing = Vector3.forward;
            }
            body.transform.localRotation = Quaternion.Euler(0f, -Vector3.SignedAngle(Vector3.forward, facing, Vector3.up), 0f);

            // Рост — по сетке в позе покоя (A-поза стоит ногами в нуле, export.json: rest_min_z ≈ 0).
            float min = float.MaxValue, max = float.MinValue;
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("В FBX Шипомёта нет SkinnedMeshRenderer.");
            foreach (var skin in skins)
            {
                skin.BakeMesh(baked, false);
                // BakeMesh без масштаба отдаёт вершины в мировых единицах без сдвига и поворота
                // рендера (с масштабом — в осях узла: у узла 100× рост вышел бы в сто раз меньше).
                var toWorld = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices)
                {
                    float y = toWorld.MultiplyPoint3x4(v).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }
            float measured = max - min;
            if (!(measured > .05f && measured < 100f)) throw new InvalidOperationException("Рост Шипомёта не измерен: " + measured);
            float scale = Height / measured;
            body.transform.localScale = body.transform.localScale * scale;
            body.transform.localPosition = new Vector3(0f, -min * scale, 0f);
            Debug.Log($"[thorncaster] Рост в FBX {measured:0.###} м, масштаб тела {scale:0.####} → {Height} м.");

            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var skin in skins)
            {
                int slots = skin.sharedMesh != null ? Mathf.Max(1, skin.sharedMesh.subMeshCount) : 1;
                skin.sharedMaterials = Enumerable.Repeat(material, slots).ToArray();
                skin.updateWhenOffscreen = true;
                skin.localBounds = new Bounds(new Vector3(0f, 1.4f, 0f) / scale, Vector3.one * (8f / scale));
            }
            root.AddComponent<Game.View.ThorncasterAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(baked);
        }
    }
}
