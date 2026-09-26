using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ForestBudCombatBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Bud/";
    private const string Model = Root + "ForestBudRanged.fbx";
    private const string Controller = Root + "Forest_Bud_Combat.controller";
    // Hit — реакция на попадание, 11 кадров; View ведёт её параметром HitPhase.
    private static readonly string[] Roles = { "Idle", "Walk", "Ranged_Attack", "Hit", "Death" };

    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += BuildIfNeeded;

    /// <summary>
    /// Модель с новым клипом может импортироваться уже после перезагрузки домена:
    /// тогда сборка при загрузке не нашла бы клип, а позже её никто бы не позвал.
    /// </summary>
    private sealed class ModelWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] movedTo, string[] movedFrom)
        {
            if (Array.IndexOf(imported, Model) >= 0) EditorApplication.delayCall += BuildIfNeeded;
        }
    }

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Model)) return;
        AddMeadowEncounter();
        if (File.Exists(Controller) && File.GetLastWriteTimeUtc(Controller) >= File.GetLastWriteTimeUtc(Model)
            && File.GetLastWriteTimeUtc(Controller) >= File.GetLastWriteTimeUtc("Assets/Editor/ForestBudCombatBuilder.cs")) return;
        Build();
    }

    [MenuItem("Разлом/Лесной бутон/Собрать игровое представление")]
    public static void Build()
    {
        AddMeadowEncounter();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (source == null) throw new InvalidOperationException("ForestBudRanged.fbx не импортирован.");
        var sourceAnimator = source.GetComponent<Animator>();
        if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isValid || sourceAnimator.avatar.isHuman)
            throw new InvalidOperationException("Forest_Bud: для авторских лап требуется действительный Generic avatar.");
        var socketNames = source.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
        for (int i = 1; i <= 5; i++)
            if (!socketNames.Contains("Spawn_Fruit_" + i.ToString("00")))
                throw new InvalidOperationException("Forest_Bud: отсутствует сокет плода " + i);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
        // Клипы ищутся до правки контроллера: ошибка посреди сборки оставила бы его без слоёв.
        var sources = Roles.Select(role => clips.FirstOrDefault(c => c.name == role || c.name.EndsWith("|" + role) || c.name.EndsWith("_" + role))).ToArray();
        for (int i = 0; i < Roles.Length; i++)
            if (sources[i] == null) throw new InvalidOperationException("Forest_Bud: отсутствует клип " + Roles[i]);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Controller);
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>();
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        controller.AddParameter("AttackPhase", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkPhase", AnimatorControllerParameterType.Float);
        controller.AddParameter("HitPhase", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        AnimationClip hit = null;
        for (int r = 0; r < Roles.Length; r++)
        {
            string role = Roles[r];
            var original = sources[r];
            string path = Root + "Forest_Bud_" + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip); clip.name = "Forest_Bud_" + role;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk";
            settings.loopBlend = settings.loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            var state = machine.AddState(role); state.motion = clip; state.writeDefaultValues = false;
            if (role == "Idle") machine.defaultState = state;
            if (role == "Ranged_Attack") { state.timeParameter = "AttackPhase"; state.timeParameterActive = true; }
            if (role == "Walk") { state.timeParameter = "WalkPhase"; state.timeParameterActive = true; }
            if (role == "Hit") { state.timeParameter = "HitPhase"; state.timeParameterActive = true; hit = clip; }
        }
        // Тот же Hit аддитивным слоем: поверх залпа и шага. Опорная поза — первый
        // кадр клипа, он же поза покоя, поэтому в начале и в конце слой ничего не
        // добавляет и вес можно включать и гасить без щелчка. Вес задаёт View.
        controller.AddLayer("Hit Additive");
        var layers = controller.layers;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
        layers[1].defaultWeight = 0f;
        controller.layers = layers;
        var additive = layers[1].stateMachine.AddState("Hit");
        additive.motion = hit; additive.writeDefaultValues = false;
        additive.timeParameter = "HitPhase"; additive.timeParameterActive = true;
        layers[1].stateMachine.defaultState = additive;
        EditorUtility.SetDirty(controller);
        string vfxFolder = Root.TrimEnd('/') + "/VFX";
        if (!AssetDatabase.IsValidFolder(vfxFolder)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "VFX");
        string markPath = vfxFolder + "/ForestBud_Landing.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(markPath) == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("Razlom/Forest Bud Landing")), markPath);
        var markImporter = AssetImporter.GetAtPath(vfxFolder + "/ForestBud_LandingMark.png") as TextureImporter;
        if (markImporter != null && (!markImporter.alphaIsTransparency || markImporter.wrapMode != TextureWrapMode.Clamp
            || markImporter.maxTextureSize != 1024 || markImporter.textureCompression != TextureImporterCompression.Uncompressed))
        {
            markImporter.textureType = TextureImporterType.Default; markImporter.sRGBTexture = true;
            markImporter.alphaSource = TextureImporterAlphaSource.FromInput; markImporter.alphaIsTransparency = true;
            markImporter.wrapMode = TextureWrapMode.Clamp; markImporter.mipmapEnabled = true;
            markImporter.filterMode = FilterMode.Trilinear; markImporter.maxTextureSize = 1024;
            markImporter.textureCompression = TextureImporterCompression.Uncompressed; markImporter.SaveAndReimport();
        }
        var prefab = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            var animator = prefab.GetComponent<Animator>() ?? prefab.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = ToonMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, Root + "Forest_Bud_Runtime.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(prefab); }
        var fruitSource = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ProjectileFruit.fbx");
        if (fruitSource == null) throw new InvalidOperationException("Forest_Bud: отсутствует модель плода.");
        var fruitPrefab = (GameObject)PrefabUtility.InstantiatePrefab(fruitSource);
        try
        {
            foreach (var renderer in fruitPrefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = ToonMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(fruitPrefab, Root + "Forest_Bud_Projectile.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(fruitPrefab); }
        AssetDatabase.SaveAssets();
        Debug.Log("[forest-bud] Five clips (Hit also as additive layer), five sockets and independent toon materials connected. "
            + string.Join(", ", clips.Select(c => c.name + "=" + c.length.ToString("F3") + "s")));
    }

    /// <summary>
    /// Пачка Плюй-плода в MeadowEncounters — только если её нет. Существующую
    /// не трогает: числа пачек правятся в ассете, а прежняя «починка» до 2–3
    /// Хранителей с уроном 180% откатывала баланс видов (EnemyArchetypes).
    /// Поток арен пачки профиля не читает — встречи там из шаблонов
    /// (ForestEncounterTemplates), — поэтому пачка нужна только старой
    /// расстановке по модулям. Состав повторяет ассет: 1–2 плода и 2–3
    /// Корнеполза со второго уровня, проценты 100 — подстройка к таблице
    /// видов, а не множитель; Хранителей в пачке не больше двух.
    /// </summary>
    private static void AddMeadowEncounter()
    {
        var profile = AssetDatabase.LoadAssetAtPath<Game.Data.EncounterProfileAsset>("Assets/Resources/Locations/MeadowEncounters.asset");
        const string key = "encounter.meadow.forest_bud";
        if (profile == null || profile.MainPath.Any(pack => pack.StableKey == key)) return;
        var packs = profile.MainPath.ToList();
        packs.Add(new Game.Data.EncounterPackAsset {
            StableKey = key, Weight = 75, MinLevel = 2,
            Groups = new[] {
                new Game.Data.EncounterGroupAsset { Kind = Game.Sim.EnemyKind.ForestBud,
                    Min = 1, Max = 2, HealthPercent = 100, DamagePercent = 100 },
                new Game.Data.EncounterGroupAsset { Kind = Game.Sim.EnemyKind.ForestRootSwarm,
                    Min = 2, Max = 3, HealthPercent = 100, DamagePercent = 100, GrowWithDepth = true }
            }
        });
        profile.MainPath = packs.ToArray();
        profile.ToDefinition(1);
        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
    }

    private static Material ToonMaterial(Material source)
    {
        if (source == null) return null;
        string name = source.name.Replace(" (Instance)", "").Replace('/', '_');
        string path = Root + "Runtime_" + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null) { result = new Material(Shader.Find("Razlom/Texture Toon")); AssetDatabase.CreateAsset(result, path); }
        if (name.Contains("Fruit")) result.shader = Shader.Find("Razlom/Forest Fruit");
        Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        // FBX хранит узловые цвета Blender в linear; Color-свойство материала Unity принимает sRGB.
        result.SetTexture("_BaseMap", texture);
        result.SetColor("_BaseColor", texture != null ? new Color(1.12f, 1.12f, 1.12f, 1f) : color.gamma);
        result.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        result.SetColor("_MidColor", new Color(.94f, .90f, .91f));
        result.SetFloat("_OutlineWidth", 0f); result.SetFloat("_Smoothness", .12f);
        EditorUtility.SetDirty(result); return result;
    }
}
