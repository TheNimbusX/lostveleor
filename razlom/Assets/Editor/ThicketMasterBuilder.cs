using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// ХОЗЯИН ЧАЩИ (босс леса) — ИГРОВОЕ ТЕЛО ИЗ ПАКЕТА unity_package.
///
/// Пакет: ART/characters/act-1-enemies/boss-forest-master/production/unity_package
/// (ThicketMaster.fbx с дублями «ThicketMaster_&lt;Клип&gt;», текстуры рядом или в
/// textures/, отчёт клипов *.json со stride_m_per_cycle; без него шаг берётся из
/// production/animation/*_validation.json). Копия ложится в
/// Resources/Characters/Forest_ThicketMaster; новее в пакете — перезаписывается.
///
/// • Импорт: Generic, аватар из модели, без сжатия, globalScale 1. Клипы — по
///   дублям, корень «root» — узел движения, у каждого клипа корень запечён в позу
///   (lockRootPositionXZ / HeightY / Rotation с исходными значениями): Sim двигает
///   тело сам, клип ехать не может. Проверка: проезд корня по XZ больше 2 см — в лог.
///   RazlomCharacterImport держит для этой папки ветку новых мобов (Generic, тангенсы
///   Mikk под карту нормалей).
/// • Контроллер — из ThicketMasterClipRules: состояние на клип, Motion Time от
///   «&lt;Клип&gt;Phase». Нет клипа — состояние пропускается (вид играет Idle), нет Idle —
///   сборка падает. Длина в кадрах сверяется с контрактом. Клипы, что идут сами за собой
///   (PawR, PawL, Stomp — ThicketMasterClipRules.HasAlternate), получают вторую копию
///   состояния «&lt;Клип&gt;Alt» с параметром «&lt;Клип&gt;AltPhase»: вид переходит в неё
///   смесью, когда серия начинается той же лапой, которой кончилась прошлая. Поверх — два добавочных
///   слоя шагов разворота под ударом лапы «Paw Turn Legs R/L» (маска — задние лапы и опорная передняя,
///   TurnL/TurnR; AddPawTurnLayers, проверка находок 03.10).
/// • Материал — URP Lit без подъёма яркости (белый _BaseColor): цвет, нормали, ORM
///   (разложен в карты URP), второй слот — Texture Toon только с проходом
///   UnitOutlineMask (контур врага), как у Корнехвата и Расщепеня.
/// • Тело: FacingGuide обязателен (модель смотрит −Y в Blender, разворот только
///   вокруг вертикали к +Z). Модель — 3,6 м по контракту клипов, в игре 4,14 м (×1,15,
///   решение владельца 02.10; Simulation.ThicketModelHeight): узел тела масштабируется
///   до TargetHeight, шаг Walk — на ту же долю. Рамка скина — куб 10,4 м (тело
///   4,8 × 6,6 × 4,14 м) вместо пересчёта каждый кадр. Контактная тень — 3,9 м.
/// • Префаб ThicketMaster_Runtime: корень с ThicketMasterAnimatorView (шаг Walk — путь стоящей
///   лапы самого клипа за цикл в осях корня, MeasureWalkStride, 08.10; отчёт клипов × масштаб тела —
///   запас), тело — модель с Animator и контроллером.
///
/// Сам по себе не запускается: меню или RazlomCaptureBuild (BuildIfPackagePresent).
/// </summary>
public static partial class ThicketMasterBuilder
{
    public const string Root = "Assets/Resources/Characters/Forest_ThicketMaster/";
    public const string Model = Root + "ThicketMaster.fbx";
    public const string Prefab = Root + "ThicketMaster_Runtime.prefab";
    private const string ClipFolder = Root + "Clips";
    private const string ControllerPath = Root + "ThicketMaster.controller";
    private const string MaterialPath = Root + "ThicketMaster.mat";
    private const string MaskPath = Root + "ThicketMaster_OutlineMask.mat";
    private const string OcclusionTexture = Root + "T_ThicketMaster_Occlusion.png";
    private const string MetalSmoothTexture = Root + "T_ThicketMaster_MetalSmooth.png";
    private const string ExtractedTextures = Root + "Textures";

    /// <summary>Пакет относительно корня репозитория (папка над razlom/).</summary>
    public const string Package = "ART/characters/act-1-enemies/boss-forest-master/production/unity_package/";

    /// <summary>Рост модели по контракту клипов (сцена Blender), м.</summary>
    public const float AuthoredHeight = 3.6f;

    /// <summary>Рост тела в игре, м: ×1,15 к модели (решение владельца 02.10) = Simulation.ThicketModelHeight.</summary>
    public const float TargetHeight = 4.14f, HeightTolerance = .03f;

    /// <summary>Рамка скина, м: куб вокруг середины тела (9 м × 1,15).</summary>
    public const float BoundsSize = 10.4f;

    /// <summary>Проезд корня в клипе по XZ больше этого, м, — нарушение контракта (root motion нет).</summary>
    private const float RootDriftLimit = .02f;

    private static readonly string[] RequiredBones = { "root", "head", "crown_L", "crown_R", "bush",
        "leg_front_L_paw", "leg_front_R_paw", "leg_front_L_toe", "leg_front_R_toe",
        "tail_01", "tail_02", "tail_03", "tail_04", "tail_05" };

    private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

    [MenuItem("Разлом/Босс/Хозяин Чащи/Собрать представление")]
    public static void BuildFromMenu() => Build();

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    private static string PackageDirectory() => Path.Combine(RepositoryRoot(), Package);

    private static string PackageModel() => Path.Combine(PackageDirectory(), "ThicketMaster.fbx");

    /// <summary>Есть ли из чего собирать: FBX в пакете ART или уже в Resources.</summary>
    public static bool PackagePresent() => File.Exists(PackageModel()) || File.Exists(Model);

    /// <summary>
    /// Для сборки плеера съёмки: пакета нет — тихо пропускает (бой рисует заглушку);
    /// префаб свежее пакета — не пересобирает; сломанный пакет — ошибка в лог, но
    /// сборка плеера идёт дальше.
    /// </summary>
    public static void BuildIfPackagePresent()
    {
        if (!PackagePresent())
        {
            Debug.Log("[thicketmaster] Пакета клипов нет (" + Package + ") — представление босса не собирается, бой рисует заглушку.");
            return;
        }
        string source = File.Exists(PackageModel()) ? PackageModel() : Model;
        if (File.Exists(Prefab) && File.GetLastWriteTimeUtc(Prefab) >= File.GetLastWriteTimeUtc(source)) return;
        try { Build(); }
        catch (Exception e) { Debug.LogError("[thicketmaster] Представление босса не собрано: " + e.Message); }
    }

    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Хозяин Чащи собирается только вне Play.");
        if (Mathf.Abs(Simulation.ThicketModelHeight.ToFloat() - TargetHeight) > .005f)
            Debug.LogWarning($"[thicketmaster] Рост в Sim {Simulation.ThicketModelHeight.ToFloat():0.###} м, а сборщик ставит {TargetHeight} м — сверь.");
        SyncPackage();
        var importer = ImportModel();
        var textures = FindTextures(importer);
        ConfigureTextures(textures);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (source == null) throw new InvalidOperationException("Модель не импортировалась: " + Model);
        var controller = BuildController(out int built, out var clips);
        var material = LitMaterial(textures);
        var mask = OutlineMask(textures.Color);
        float stride = ReadWalkStride();

        var root = new GameObject("ThicketMaster_Runtime");
        float height;
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.transform.SetParent(root.transform, false);
            FaceForward(body);
            CheckBones(body);
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
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            height = MeasureHeight(renderers);
            if (height < .01f) Debug.LogWarning("[thicketmaster] Рост модели не измерен — масштаб не проверен.");
            else if (Mathf.Abs(height - TargetHeight) > TargetHeight * HeightTolerance)
            {
                // Тело должно совпасть с Sim: лапа 4,14 м, топот 5,2 м, круп 1,265 м — всё ×1,15 от модели 3,6 м.
                string note = $"[thicketmaster] Рост модели {height:0.###} м → {TargetHeight} м: узел тела ×{TargetHeight / height:0.###}";
                if (Mathf.Abs(height - AuthoredHeight) <= AuthoredHeight * HeightTolerance)
                    Debug.Log(note + " (рост ×1,15, решение 02.10).");
                else
                    Debug.LogWarning(note + $" — модель не {AuthoredHeight} м по контракту клипов, проверь пакет.");
                body.transform.localScale *= TargetHeight / height;
                // Шаг Walk замерен в метрах модели, а вид делит путь на него в масштабе корня
                // префаба: узел тела растянут — шаг тоже, иначе лапы скользят на ту же долю.
                stride *= TargetHeight / height;
                height = TargetHeight;
            }
            SetBounds(root.transform, renderers);
            CheckRootDrift(body, clips);
            // Шаг Walk — по самому клипу (владелец 08.10: «ноги проскальзывают при ходьбе»): путь стоящей лапы за цикл
            // в осях корня, как его делит вид; отчёт пакета × масштаб тела — только запас.
            if (clips.TryGetValue(ThicketClip.Walk, out var walkClip)) stride = MeasureWalkStride(root.transform, body, walkClip, stride);
            var view = root.AddComponent<ThicketMasterAnimatorView>();
            // Одежда фаз (руны, ягоды, цветы) — сразу в префаб: в пакетной сборке delayCall
            // постпроцессора ThicketMasterDressingSetup не надёжен (ревью 02.10).
            root.AddComponent<ThicketMasterPhaseDressing>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_walkStride").floatValue = stride;
            serialized.FindProperty("_contactShadowMetres").floatValue = ThicketMasterAnimatorView.DefaultContactShadowMetres;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log($"[thicketmaster] Клипов: {built} из {ThicketMasterClipRules.All.Length}, Generic, URP Lit"
                  + (textures.Normal != null ? " с нормалями" : " без нормалей") + (textures.Orm != null ? " и ORM" : "")
                  + (mask != null ? " + маска контура" : "") + $", рост {height:0.00} м, шаг Walk {stride:0.###} м/цикл, prefab: {Prefab}.");
    }

    // ------------------------------------------------------------ package

    /// <summary>FBX и картинки пакета (корень и textures/) → Resources; копирует новое и недостающее.</summary>
    private static void SyncPackage()
    {
        string package = PackageDirectory();
        if (!Directory.Exists(package))
        {
            if (File.Exists(Model)) { Debug.Log("[thicketmaster] Пакета в ART нет — собираю из копии в Resources."); return; }
            throw new FileNotFoundException("Нет пакета Хозяина Чащи", package);
        }
        var sources = new List<string>();
        foreach (string folder in new[] { package, Path.Combine(package, "textures") })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.GetFiles(folder))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".fbx" || TextureExtensions.Contains(extension)) sources.Add(file);
            }
        }
        if (!sources.Any(f => Path.GetFileName(f) == "ThicketMaster.fbx") && !File.Exists(Model))
            throw new FileNotFoundException("В пакете нет ThicketMaster.fbx", PackageModel());
        Directory.CreateDirectory(Root);
        bool copied = false;
        foreach (string from in sources)
        {
            string to = Root + Path.GetFileName(from);
            if (File.Exists(to) && File.GetLastWriteTimeUtc(to) >= File.GetLastWriteTimeUtc(from)) continue;
            File.Copy(from, to, true);
            copied = true;
        }
        if (copied) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    /// <summary>Отчёт проверки анимации рядом с пакетом: production/animation/*_validation.json.</summary>
    private const string AnimationReports = "ART/characters/act-1-enemies/boss-forest-master/production/animation/";

    /// <summary>
    /// Шаг Walk (stride_m_per_cycle): сначала любой json пакета, потом отчёт проверки анимации
    /// (production/animation/*_validation.json, туда его пишет validate_anim); нет — число по умолчанию.
    /// </summary>
    private static float ReadWalkStride()
    {
        string package = PackageDirectory();
        string reports = Path.Combine(RepositoryRoot(), AnimationReports);
        var files = new List<string>();
        if (Directory.Exists(package)) files.AddRange(Directory.GetFiles(package, "*.json", SearchOption.AllDirectories));
        if (Directory.Exists(reports)) files.AddRange(Directory.GetFiles(reports, "*_validation.json", SearchOption.TopDirectoryOnly));
        foreach (string file in files)
        {
            var match = Regex.Match(File.ReadAllText(file), "\"stride_m_per_cycle\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)");
            if (!match.Success) continue;
            if (float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float stride) && stride > .1f)
            {
                Debug.Log($"[thicketmaster] Шаг Walk {stride:0.###} м/цикл — из {Path.GetFileName(file)}.");
                return stride;
            }
        }
        Debug.LogWarning($"[thicketmaster] Ни в пакете, ни в {AnimationReports} нет stride_m_per_cycle — шаг Walk {ThicketMasterClipRules.DefaultWalkStride} м по умолчанию (замер 02.10), ноги могут скользить.");
        return ThicketMasterClipRules.DefaultWalkStride;
    }

    /// <summary>Замер шага расходится с отчётом пакета × масштаб тела больше этой доли — в лог.</summary>
    private const float StrideMismatch = .02f;

    /// <summary>
    /// Шаг Walk по самому клипу, м/цикл в осях корня префаба (их делит ThicketMasterAnimatorView): костяшка передней правой
    /// (leg_front_R_toe) внутри окна опоры ThicketWalkRules едет назад на шаг за цикл — путь между двумя кадрами клипа / доля
    /// цикла между ними. Поза модели после замера возвращается как была (префаб — без позы клипа). Замер 08.10
    /// (artifacts/tools/boss-feet/walk_feet.py по Walk.anim): 2,1477 = отчёт 1,7857 × тело 1,2027 — совпадает. Замер не
    /// вышел (нет кости, клип пуст, исключение) — fromReport; расходится больше StrideMismatch — предупреждение, верит замеру.
    /// </summary>
    private static float MeasureWalkStride(Transform root, GameObject body, AnimationClip walk, float fromReport)
    {
        var bones = body.GetComponentsInChildren<Transform>(true);
        var toe = bones.FirstOrDefault(t => t.name == "leg_front_R_toe");
        if (toe == null || walk == null || walk.length <= 0f)
        {
            Debug.LogWarning($"[thicketmaster] Шаг Walk не замерен по клипу (нет leg_front_R_toe или клип пуст) — {fromReport:0.###} м/цикл из отчёта.");
            return fromReport;
        }
        var positions = new Vector3[bones.Length];
        var rotations = new Quaternion[bones.Length];
        var scales = new Vector3[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            positions[i] = bones[i].localPosition;
            rotations[i] = bones[i].localRotation;
            scales[i] = bones[i].localScale;
        }
        try
        {
            int paw = ThicketWalkRules.FrontRight;
            float from = ThicketWalkRules.LockStart(paw) + .05f, span = ThicketWalkRules.LockSpan(paw) - .1f;
            walk.SampleAnimation(body, Mathf.Repeat(from, 1f) * walk.length);
            Vector3 a = root.InverseTransformPoint(toe.position);
            walk.SampleAnimation(body, Mathf.Repeat(from + span, 1f) * walk.length);
            Vector3 b = root.InverseTransformPoint(toe.position);
            float measured = (a.z - b.z) / span;
            if (!(measured > .3f && measured < 6f) || Mathf.Abs(a.x - b.x) > .05f * measured)
            {
                Debug.LogWarning($"[thicketmaster] Замер шага Walk по клипу странный ({measured:0.###} м/цикл, вбок {Mathf.Abs(a.x - b.x):0.###} м) — " +
                                 $"{fromReport:0.###} м/цикл из отчёта.");
                return fromReport;
            }
            if (Mathf.Abs(measured - fromReport) > StrideMismatch * fromReport)
                Debug.LogWarning($"[thicketmaster] Шаг Walk по клипу {measured:0.###} м/цикл, а отчёт пакета × масштаб тела — {fromReport:0.###}: " +
                                 "беру замер клипа (иначе лапы скользят на разницу), отчёт пакета устарел.");
            else
                Debug.Log($"[thicketmaster] Шаг Walk по клипу {measured:0.###} м/цикл (отчёт × масштаб тела {fromReport:0.###}).");
            return measured;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[thicketmaster] Шаг Walk по клипу не замерен ({e.Message}) — {fromReport:0.###} м/цикл из отчёта.");
            return fromReport;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = positions[i];
                bones[i].localRotation = rotations[i];
                bones[i].localScale = scales[i];
            }
        }
    }

    // ------------------------------------------------------------ import

    private static ModelImporter ImportModel()
    {
        AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(Model) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Нет модели " + Model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        importer.importAnimation = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();

        importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        if (importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation)
            throw new InvalidOperationException("Импорт Хозяина Чащи не Generic или без анимации: RazlomCharacterImport.OnPreprocessModel " +
                "переписывает настройки для Resources/Characters — нужна ветка «/Resources/Characters/Forest_ThicketMaster/» (HasLitNormalMap).");

        // Узел движения — кость root: запекание корня в позу относится к ней, а не к узлу файла.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        string motionNode = model != null ? PathOf(model.transform, "root") : null;
        if (motionNode == null) Debug.LogWarning("[thicketmaster] В модели нет кости root — узел движения не задан.");

        var takes = importer.defaultClipAnimations;
        if (takes == null || takes.Length == 0) throw new InvalidOperationException("В ThicketMaster.fbx нет ни одного дубля.");
        foreach (var take in takes)
        {
            take.loopTime = ThicketMasterClipRules.All.Any(c => ThicketMasterClipRules.Loops(c)
                && take.name.EndsWith(ThicketMasterClipRules.Take(c), StringComparison.Ordinal));
            take.loopPose = false;
            take.lockRootPositionXZ = true; take.keepOriginalPositionXZ = true;
            take.lockRootHeightY = true; take.keepOriginalPositionY = true;
            take.lockRootRotation = true; take.keepOriginalOrientation = true;
        }
        importer.clipAnimations = takes;
        if (motionNode != null) importer.motionNodeName = motionNode;
        importer.SaveAndReimport();
        return (ModelImporter)AssetImporter.GetAtPath(Model);
    }

    /// <summary>Путь узла name от корня модели («Rig/root»); null — нет такого.</summary>
    private static string PathOf(Transform top, string name)
    {
        foreach (var t in top.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return AnimationUtility.CalculateTransformPath(t, top);
        return null;
    }

    // ------------------------------------------------------------ controller

    private static AnimatorController BuildController(out int built, out Dictionary<ThicketClip, AnimationClip> clips)
    {
        var originals = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__", StringComparison.Ordinal)).ToArray();
        if (!AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Clips");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>();
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        clips = new Dictionary<ThicketClip, AnimationClip>();
        built = 0;
        foreach (ThicketClip role in ThicketMasterClipRules.All)
        {
            string take = ThicketMasterClipRules.Take(role);
            string name = ThicketMasterClipRules.Name(role);
            var original = originals.FirstOrDefault(c => c.name == take || c.name.EndsWith("|" + take, StringComparison.Ordinal))
                           ?? originals.FirstOrDefault(c => c.name.EndsWith(take, StringComparison.Ordinal));
            if (original == null)
            {
                if (ThicketMasterClipRules.Required(role))
                    throw new InvalidOperationException("Нет клипа " + take + ": " + string.Join(", ", originals.Select(c => c.name)));
                Debug.LogWarning("[thicketmaster] Нет клипа " + take + " — состояние пропущено, вид играет вместо него Idle.");
                continue;
            }
            int expected = ThicketMasterClipRules.Frames(role);
            float authored = original.length * original.frameRate;
            if (expected > 0 && Mathf.Abs(authored - expected) > .6f)
                Debug.LogWarning($"[thicketmaster] Клип {original.name}: {authored:0.#} кадров вместо {expected} по контракту — контакт сдвинется с тика удара.");
            if (Mathf.Abs(original.frameRate - ThicketMasterClipRules.FramesPerSecond) > .01f)
                Debug.LogWarning($"[thicketmaster] Клип {original.name}: {original.frameRate} кадров/с вместо 30 — кадр клипа ≠ тик Sim.");
            string path = ClipFolder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip);
            clip.name = name; // имя = имя файла .anim: иначе Unity ругается на каждой пересборке
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = ThicketMasterClipRules.Loops(role);
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            var state = machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = false;
            string parameter = ThicketMasterClipRules.PhaseParameter(role);
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            state.timeParameter = parameter;
            state.timeParameterActive = true;
            if (role == ThicketClip.Idle) machine.defaultState = state;
            if (ThicketMasterClipRules.HasAlternate(role))
            {
                // Вторая копия того же клипа со своим параметром: смесь «отход → кадр 0» того же клипа.
                var alternate = machine.AddState(ThicketMasterClipRules.AlternateName(role));
                alternate.motion = clip;
                alternate.writeDefaultValues = false;
                string alternateParameter = ThicketMasterClipRules.AlternatePhaseParameter(role);
                controller.AddParameter(alternateParameter, AnimatorControllerParameterType.Float);
                alternate.timeParameter = alternateParameter;
                alternate.timeParameterActive = true;
            }
            clips[role] = clip;
            built++;
        }
        AddPawTurnLayers(controller, clips);
        return controller;
    }

    // ------------------------------------------------------------ paw turn legs

    /// <summary>Маски слоёв шагов под ударом лапы: «…_R.mask» (бьёт правая) и «…_L.mask».</summary>
    private const string PawTurnMaskPath = Root + "ThicketMaster_PawTurnLegs_";

    /// <summary>
    /// Шаги разворота под ударом лапы (проверка находок 03.10): Sim крутит корпус в серии 3,5°/тик (первый удар —
    /// до ~60° за замах, контракт § 11), а клип удара стоит на месте — задние лапы и опорная передняя ехали бы по
    /// земле. Два добавочных слоя (вес 0, ведёт ThicketMasterAnimatorView по ThicketPawTurnLegs): маска — задние
    /// лапы и опорная передняя (ThicketMasterClipRules.PawTurnMasks), состояния Empty / TurnL / TurnR с Motion Time
    /// от «LegsR/L + TurnL/R + Phase». Добавочный слой считается от кадра 0 клипа разворота — к позе удара
    /// прибавляются только шаги, корпус и бьющая лапа остаются из клипа удара. Нет клипов разворота — слоёв нет.
    /// </summary>
    private static void AddPawTurnLayers(AnimatorController controller, Dictionary<ThicketClip, AnimationClip> clips)
    {
        if (!clips.ContainsKey(ThicketClip.TurnL) || !clips.ContainsKey(ThicketClip.TurnR))
        {
            Debug.LogWarning("[thicketmaster] Нет клипов TurnL/TurnR — слоёв шагов под ударом лапы нет: корпус в серии крутится на лапах клипа.");
            return;
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (model == null) return;
        var bones = model.GetComponentsInChildren<Transform>(true);
        foreach (bool right in new[] { true, false })
        {
            var mask = PawTurnMask(model.transform, bones, right);
            controller.AddLayer(ThicketMasterClipRules.PawTurnLayer(right));
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Additive;
            layer.defaultWeight = 0f;
            controller.layers = layers;
            var machine = layer.stateMachine;
            var empty = machine.AddState(ThicketMasterClipRules.PawTurnEmptyState);
            empty.writeDefaultValues = false;
            machine.defaultState = empty;
            foreach (ThicketClip turn in new[] { ThicketClip.TurnL, ThicketClip.TurnR })
            {
                var state = machine.AddState(ThicketMasterClipRules.Name(turn));
                state.motion = clips[turn];
                state.writeDefaultValues = false;
                string parameter = ThicketMasterClipRules.PawTurnParameter(right, turn);
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                state.timeParameter = parameter;
                state.timeParameterActive = true;
            }
        }
    }

    /// <summary>
    /// Маска слоя шагов: все узлы модели (пути от корня модели — как у кривых клипов; корень — пустой путь),
    /// включены только кости ThicketMasterClipRules.PawTurnMasks и их дети. Ассет переиспользуется.
    /// </summary>
    private static AvatarMask PawTurnMask(Transform top, Transform[] bones, bool right)
    {
        string path = PawTurnMaskPath + (right ? "R" : "L") + ".mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        bool created = mask == null;
        if (created) mask = new AvatarMask();
        var paths = new List<string>();
        var active = new List<bool>();
        foreach (var bone in bones)
        {
            string relative = bone == top ? "" : AnimationUtility.CalculateTransformPath(bone, top);
            paths.Add(relative);
            active.Add(relative.Length > 0 && relative.Split('/').Any(n => ThicketMasterClipRules.PawTurnMasks(n, right)));
        }
        mask.transformCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, active[i]);
        }
        if (created) AssetDatabase.CreateAsset(mask, path);
        else EditorUtility.SetDirty(mask);
        int on = active.Count(a => a);
        if (on == 0) Debug.LogWarning("[thicketmaster] В модели нет костей leg_hind_* / leg_front_* — маска шагов под ударом лапы пуста.");
        else Debug.Log($"[thicketmaster] Слой шагов под ударом {(right ? "правой" : "левой")} лапы: {on} узлов в маске.");
        return mask;
    }

    // ------------------------------------------------------------ checks

    /// <summary>
    /// Взгляд модели — по FacingGuide (обязателен): Blender −Y, FBX −Z. Разворот только
    /// вокруг вертикали — без переворота, как у Корнехвата.
    /// </summary>
    private static void FaceForward(GameObject body)
    {
        var guide = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FacingGuide");
        if (guide == null) throw new InvalidOperationException("Нет метки направления FacingGuide в ThicketMaster.fbx.");
        Vector3 facing = guide.position - body.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) throw new InvalidOperationException("FacingGuide стоит в центре модели — направление не читается.");
        float yaw = Vector3.SignedAngle(facing.normalized, Vector3.forward, Vector3.up);
        body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * body.transform.localRotation;
        if (Mathf.Abs(yaw) > 1f) Debug.Log($"[thicketmaster] Модель довёрнута на {yaw:0.#}° к +Z.");
    }

    /// <summary>Кости, на которые опираются вид и знак лапы: нет — предупреждение.</summary>
    private static void CheckBones(GameObject body)
    {
        var names = new HashSet<string>(body.GetComponentsInChildren<Transform>(true).Select(t => t.name));
        var missing = RequiredBones.Where(b => !names.Contains(b)).ToArray();
        if (missing.Length > 0)
            Debug.LogWarning("[thicketmaster] Нет костей: " + string.Join(", ", missing) + " — знак лапы и вид боя возьмут запасные точки.");
    }

    /// <summary>Контракт: корень не уезжает по XZ (Sim двигает тело). Проезд больше 2 см — в лог.</summary>
    private static void CheckRootDrift(GameObject body, Dictionary<ThicketClip, AnimationClip> clips)
    {
        string path = PathOf(body.transform, "root");
        if (path == null) return;
        Transform bone = body.transform.Find(path);
        float unit = bone != null && bone.parent != null ? Mathf.Abs(bone.parent.lossyScale.x) : 1f;
        foreach (var pair in clips)
        {
            float drift = 0f;
            foreach (string axis in new[] { "m_LocalPosition.x", "m_LocalPosition.z" })
            {
                var curve = AnimationUtility.GetEditorCurve(pair.Value, EditorCurveBinding.FloatCurve(path, typeof(Transform), axis));
                if (curve == null || curve.length == 0) continue;
                float first = curve.keys[0].value;
                foreach (var key in curve.keys) drift = Mathf.Max(drift, Mathf.Abs(key.value - first) * unit);
            }
            if (drift > RootDriftLimit)
                Debug.LogWarning($"[thicketmaster] Клип {pair.Value.name}: корень уезжает на {drift * 100f:0.#} см — root motion по контракту нет.");
        }
    }

    /// <summary>Рост тела в позе привязки, м: запечённый скин, мировые координаты.</summary>
    private static float MeasureHeight(SkinnedMeshRenderer[] renderers)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var renderer in renderers)
        {
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                var toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices)
                {
                    float y = toWorld.MultiplyPoint3x4(v).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }
            catch (Exception e) { Debug.LogWarning("[thicketmaster] Рост не измерен: " + e.Message); }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return max > min ? max - min : 0f;
    }

    /// <summary>Рамка скина — куб BoundsSize вокруг середины тела в осях корневой кости: без пересчёта каждый кадр.</summary>
    private static void SetBounds(Transform root, SkinnedMeshRenderer[] renderers)
    {
        Vector3 centre = root.position + Vector3.up * (TargetHeight * .5f);
        foreach (var renderer in renderers)
        {
            Transform space = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            float unit = Mathf.Max(1e-4f, Mathf.Abs(space.lossyScale.x));
            renderer.localBounds = new Bounds(space.InverseTransformPoint(centre), Vector3.one * (BoundsSize / unit));
            renderer.updateWhenOffscreen = false;
        }
    }
}
