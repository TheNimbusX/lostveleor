using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class StonehoofBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Stonehoof/";
    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () => {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(Root + "ForestStonehoof.fbx")) return;
        if (!System.IO.File.Exists(Root + "ForestStonehoof_Runtime.prefab")) Build();
    };
    [MenuItem("Разлом/Камнекопыт/Собрать представление")]
    public static void Build()
    {
        const string model = Root + "ForestStonehoof.fbx";
        AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false; importer.importAnimation = true;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(model);
        var originalClips = AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "ForestStonehoof.controller");
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "ForestStonehoof.controller");
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>(); controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        int built = 0;
        foreach (string role in new[] { "Idle", "Walk", "Windup", "Launch", "ChargeLoop", "Brake", "WallBrace", "WallImpact", "Hit", "Death", "TurnLeft", "TurnRight", "Tusk" })
        {
            var original = originalClips.FirstOrDefault(c => c.name.EndsWith("Stonehoof_" + role));
            // Необязательные клипы: без разворота StonehoofAnimatorView переступает
            // фазой Walk, без взмаха клыками (Stonehoof_Tusk, 26 кадров, контакт на
            // 14-м) бьёт из Idle. Состояние Tusk ведётся фазой по тику Sim, без петли.
            if (original == null && OptionalRoles.Contains(role))
            { Debug.LogWarning("[stonehoof] Нет клипа " + role + " — состояние пропущено до экспорта клипа."); continue; }
            if (original == null) throw new InvalidOperationException("Нет клипа " + role + ": " + string.Join(",", originalClips.Select(c => c.name)));
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip); clip.name = "Stonehoof_" + role;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk" || role == "ChargeLoop"; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
            var state = machine.AddState(role); state.motion = clip; state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase"; state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
            built++;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Stonehoof_BaseColor.png");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + "ForestStonehoof.mat");
        if (mat == null) { mat = new Material(Shader.Find("Razlom/Texture Toon")); AssetDatabase.CreateAsset(mat, Root + "ForestStonehoof.mat"); }
        mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", new Color(1.45f,1.45f,1.45f,1));
        mat.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        mat.SetColor("_MidColor", new Color(.94f,.90f,.91f)); mat.SetFloat("_OutlineWidth", 0); mat.SetFloat("_Smoothness", .12f);

        var root = new GameObject("ForestStonehoof_Runtime");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source); body.transform.SetParent(root.transform, false);
            var guide = body.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "FacingGuide");
            if (guide == null) throw new InvalidOperationException("Нет метки направления FacingGuide");
            Vector3 facing = guide.position - body.transform.position; facing.y = 0;
            body.transform.localRotation = Quaternion.FromToRotation(facing.normalized, Vector3.forward);
            var animator = body.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(mat, renderer.sharedMaterials.Length).ToArray();
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(new Vector3(0, 1.8f, 0), Vector3.one * 9f);
            }
            root.AddComponent<Game.View.StonehoofAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, Root + "ForestStonehoof_Runtime.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        BuildEffects();
        Debug.Log("[stonehoof] Клипов: " + built + ", Generic rig, материал и игровой prefab готовы.");
    }

    /// <summary>Роли, без которых сборка не падает: клипы ещё в работе.</summary>
    private static readonly string[] OptionalRoles = { "TurnLeft", "TurnRight", "Tusk" };

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrBlurredSmoke = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrLeafMesh = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/cfxr mesh leave.fbx";

    [MenuItem("Разлом/Камнекопыт/Собрать эффекты")]
    public static void BuildEffectsOnly() => BuildEffects();

    private static void BuildEffects()
    {
        // Своя пыль лежит рядом с моделью. Материалы Вендиго удалены вместе с его эффектами —
        // от них сборщик больше не зависит: недостающую пыль берём из размытого облака CFXR.
        var dust = AssetDatabase.LoadAssetAtPath<Material>(Root + "Stonehoof_Dust.mat");
        if (dust == null) dust = CreateDust();
        var chip = AssetDatabase.LoadAssetAtPath<Material>(Root + "Stonehoof_Stone.mat");
        if (chip == null)
        {
            chip = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            chip.SetColor("_BaseColor", new Color(.22f,.19f,.12f,1)); chip.SetFloat("_Smoothness", .05f);
            AssetDatabase.CreateAsset(chip, Root + "Stonehoof_Stone.mat");
        }
        var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = temporary.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(temporary);
        foreach (bool wall in new[] { false, true })
        {
            var root = new GameObject(wall ? "VFX_Wall" : "VFX_Hoof");
            try
            {
                var go = new GameObject("Земля и крошка"); go.transform.SetParent(root.transform,false);
                var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.duration = 1.2f; main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.28f,wall ? .8f : .42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(wall ? 1.8f : .6f,wall ? 3.6f : 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(.025f,wall ? .11f : .065f);
                main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startRotation3D = true; main.startRotationX = new ParticleSystem.MinMaxCurve(0,6.28f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0,6.28f); main.startRotationZ = new ParticleSystem.MinMaxCurve(0,6.28f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.65f,.51f,.3f),new Color(.95f,.8f,.5f));
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = wall ? .25f : .10f; shape.rotation = new Vector3(-90,0,0);
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0,(short)(wall ? 22 : 7)) });
                var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = mesh; renderer.sharedMaterial = chip;
                var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true; rotation.x = 5; rotation.y = 3;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,0));
                var cloud = new GameObject("Мягкая земля"); cloud.transform.SetParent(root.transform,false);
                var smoke = cloud.AddComponent<ParticleSystem>(); smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var sm = smoke.main; sm.loop = false; sm.playOnAwake = false; sm.duration = 1.2f; sm.startLifetime = new ParticleSystem.MinMaxCurve(.4f,.8f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(.15f,wall ? 1.1f : .5f); sm.startSize = new ParticleSystem.MinMaxCurve(.12f,wall ? .6f : .28f);
                sm.startColor = new Color(.38f,.3f,.17f,wall ? .32f : .20f); sm.simulationSpace = ParticleSystemSimulationSpace.World;
                var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 70; ss.radius = wall ? .25f : .12f; ss.rotation = new Vector3(-90,0,0);
                var se = smoke.emission; se.rateOverTime = 0; se.SetBursts(new[] { new ParticleSystem.Burst(0,(short)(wall ? 12 : 5)) });
                var sr = smoke.GetComponent<ParticleSystemRenderer>(); sr.sharedMaterial = dust;
                var sc = smoke.colorOverLifetime; sc.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.08f),new GradientAlphaKey(0,1) }); sc.color = gradient;
                var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.4f,1,1.6f));
                ps.useAutoRandomSeed = false; ps.randomSeed = 734; smoke.useAutoRandomSeed = false; smoke.randomSeed = 1723;
                PrefabUtility.SaveAsPrefabAsset(root,Root+root.name+".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        BuildTuskBurst(dust);
        AssetDatabase.SaveAssets();
    }

    // ---- взмах клыками: комья земли и пыль в тик удара (ревью владельца 29.09) ----
    //
    // Целевой кадр — review/mobs-v2-concepts-2026-09-29/abilities/01-stonehoof-tusk-swipe.png:
    // из-под клыков летят комья тёмной земли, камешки и травинки, у морды и у
    // ног — рыжая пыль. Золотую дугу рисует знак на теле (EnemyBodyTelegraphView,
    // поток E); здесь только земля. Всё из паков CFXR без освещения: комья —
    // «debris unlit 3x3», трава — меш листа CFXR без света, пыль — размытое
    // облако (Stonehoof_Dust). Локальные оси префаба: +Z — направление удара из
    // Sim, клыки идут снизу-справа вверх-влево кабана, то есть к −X; корень —
    // земля в точке удара (плоскость отскока). Один залп, постоянные зёрна: вид
    // переигрывает системы через Simulate по возрасту от тика удара.
    private static readonly Color TuskSoilLight = new Color(.50f, .36f, .22f), TuskSoilDark = new Color(.27f, .18f, .10f);
    private static readonly Color TuskStoneLight = new Color(.60f, .56f, .48f), TuskStoneDark = new Color(.38f, .34f, .28f);
    private static readonly Color TuskGrassLight = new Color(.55f, .66f, .26f), TuskGrassDark = new Color(.34f, .46f, .16f);
    private static readonly Color TuskDustLight = new Color(.78f, .60f, .40f), TuskDustDark = new Color(.60f, .45f, .29f);

    // Самая долгая частица залпа живёт 1,8 с — вид гасит префаб через
    // Game.View.StonehoofCombatView.TuskBurstSeconds; длиннее жизни не ставить.
    private static void BuildTuskBurst(Material dust)
    {
        var clod = PackCopy("Stonehoof_Clod", CfxrDebrisUnlit);
        NoDissolve(clod); clod.DisableKeyword("_FADING_ON");
        if (clod.HasProperty("_UseSP")) clod.SetFloat("_UseSP", 0f);
        var leaf = PackCopy("Stonehoof_Grass", CfxrLeafMaterial);
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_FADING_ON", "_CFXR_DITHERED_SHADOWS_ON" })
            leaf.DisableKeyword(keyword);
        foreach (string property in new[] { "_UseLighting", "_UseNormalMap", "_UseSP", "_CFXR_DITHERED_SHADOWS" })
            if (leaf.HasProperty(property)) leaf.SetFloat(property, 0f);
        EditorUtility.SetDirty(clod); EditorUtility.SetDirty(leaf);
        Mesh leafMesh = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CfxrLeafMesh)) if (asset is Mesh m) { leafMesh = m; break; }

        var root = new GameObject("VFX_Tusk");
        try
        {
            // Клыки подбрасывают землю вперёд, вверх и к левому боку кабана.
            var kick = new Vector3(-.40f, .80f, .55f);
            // Комья крупнее и гуще (интеграция N, 29.09): в пробе под камерой боя
            // 12 комьев по 7–15 см терялись под дугой клыков.
            var clods = Burst(root, "Комья земли", 16, 1.1f, 1.5f, 2.2f, 4.0f, .09f, .19f, new Vector3(-.05f, .05f, -.10f), kick, 26f, .16f);
            Debris(clods, clod, 1.7f, TuskSoilLight, TuskSoilDark);
            var stones = Burst(root, "Камешки", 7, 1.0f, 1.4f, 2.6f, 4.8f, .05f, .10f, new Vector3(.05f, .05f, -.05f), kick + new Vector3(-.1f, .15f, .1f), 34f, .12f);
            Debris(stones, clod, 1.9f, TuskStoneLight, TuskStoneDark);

            var grass = Burst(root, "Травинки", 5, 1.3f, 1.8f, 1.5f, 2.8f, .10f, .16f, new Vector3(0f, .05f, -.15f), kick, 40f, .2f);
            var grassMain = grass.main;
            grassMain.gravityModifier = .45f;
            grassMain.startColor = new ParticleSystem.MinMaxGradient(TuskGrassLight, TuskGrassDark);
            grassMain.startRotation3D = true;
            grassMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            grassMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            grassMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var grassDrag = grass.limitVelocityOverLifetime; grassDrag.enabled = true;
            grassDrag.limit = new ParticleSystem.MinMaxCurve(1.6f); grassDrag.dampen = .12f;
            var grassSpin = grass.rotationOverLifetime; grassSpin.enabled = true; grassSpin.separateAxes = true;
            grassSpin.x = new ParticleSystem.MinMaxCurve(-5f, 5f); grassSpin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            grassSpin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            Bounce(grass, root.transform, .05f);
            var grassSize = grass.sizeOverLifetime; grassSize.enabled = true;
            grassSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.85f, 1), new Keyframe(1, 0)));
            var grassRenderer = grass.GetComponent<ParticleSystemRenderer>();
            grassRenderer.renderMode = leafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
            grassRenderer.mesh = leafMesh; grassRenderer.alignment = ParticleSystemRenderSpace.Local;
            grassRenderer.sharedMaterial = leaf;

            // Пыль из-под клыков: размытые клубы 2×2 поднимаются над точкой удара
            // (без мягких частиц земля срезала бы низ облака — корень поднят).
            var puff = Burst(root, "Пыль из-под клыков", 6, .7f, 1.1f, .5f, 1.4f, .35f, .70f, new Vector3(0f, .32f, -.05f), kick, 45f, .25f);
            Cloud(puff, dust, .42f, false);
            // Пыль по земле: плоские клубы расходятся от удара и оседают.
            var skid = Burst(root, "Пыль по земле", 4, .8f, 1.2f, .6f, 1.3f, .55f, .95f, new Vector3(0f, .03f, 0f), new Vector3(-.3f, 0f, 1f), 70f, .3f);
            Cloud(skid, dust, .26f, true);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
                if (ps.main.startLifetime.constantMax > Game.View.StonehoofCombatView.TuskBurstSeconds)
                    throw new InvalidOperationException("Частица клыков живёт дольше залпа: " + ps.name);
            PrefabUtility.SaveAsPrefabAsset(root, Root + "VFX_Tusk.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Система с одним залпом: местное пространство, постоянное зерно по имени
    /// (String.GetHashCode меняется от запуска к запуску), конус по direction.
    /// </summary>
    private static ParticleSystem Burst(GameObject root, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, Vector3 at, Vector3 direction, float cone, float radius)
    {
        var host = new GameObject(name); host.transform.SetParent(root.transform, false);
        host.transform.localPosition = at;
        host.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, direction.normalized);
        var ps = host.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = Mathf.Max(.2f, lifeMax);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = count;
        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone; shape.radius = Mathf.Max(.01f, radius);
        uint seed = 2166136261u;
        foreach (char c in "VFX_Tusk/" + name) seed = (seed ^ c) * 16777619u;
        ps.useAutoRandomSeed = false; ps.randomSeed = seed & 0x7FFFFFFF;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return ps;
    }

    /// <summary>Комья и камешки: спрайты CFXR 3×3 (кадр наугад), баллистика, отскок от земли, тают в конце.</summary>
    private static void Debris(ParticleSystem ps, Material material, float gravity, Color light, Color dark)
    {
        var main = ps.main; main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var spin = ps.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Bounce(ps, ps.transform.parent, .25f);
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.8f, 1), new Keyframe(1, 0)));
        Sheet(ps, 3, 8.99f);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.sharedMaterial = material;
    }

    /// <summary>Пыль: клубы тёплой охры с малой альфой, тормозят, растут и тают. flat — лежат на земле.</summary>
    private static void Cloud(ParticleSystem ps, Material dust, float alpha, bool flat)
    {
        var main = ps.main; main.gravityModifier = flat ? 0f : -.03f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(TuskDustLight.r, TuskDustLight.g, TuskDustLight.b, alpha),
            new Color(TuskDustDark.r, TuskDustDark.g, TuskDustDark.b, alpha));
        var drag = ps.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.25f); drag.dampen = .22f;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(.25f, .95f), new Keyframe(1, 1.45f)));
        var color = ps.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .08f), new GradientAlphaKey(1f, .45f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var spin = ps.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        Sheet(ps, 2, 3.99f);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = dust; renderer.sortingFudge = flat ? 1f : -1f;
    }

    private static void Bounce(ParticleSystem ps, Transform ground, float bounce)
    {
        var collision = ps.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes; collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground); collision.bounce = bounce; collision.dampen = .65f;
        collision.lifetimeLoss = 0f; collision.radiusScale = .45f; collision.minKillSpeed = 0f;
    }

    /// <summary>Атлас n×n: кадр наугад на частицу, без анимации.</summary>
    private static void Sheet(ParticleSystem ps, int tiles, float lastFrame)
    {
        var sheet = ps.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = tiles; sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
        sheet.cycleCount = 1;
    }

    /// <summary>Копия материала пака рядом с моделью (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        if (source == null) throw new InvalidOperationException("Нет материала пака: " + sourcePath);
        string path = Root + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        return material;
    }

    private static void NoDissolve(Material material)
    {
        material.DisableKeyword("_CFXR_DISSOLVE"); material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
    }

    /// <summary>Unlit-облако CFXR без растворения и мягких частиц: в URP без depth-текстуры они гасят пыль у земли.</summary>
    private static Material CreateDust()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(CfxrBlurredSmoke);
        if (source == null) throw new InvalidOperationException("Нет размытого облака CFXR: " + CfxrBlurredSmoke);
        var dust = new Material(source) { name = "Stonehoof_Dust" };
        dust.DisableKeyword("_CFXR_DISSOLVE"); dust.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X"); dust.DisableKeyword("_FADING_ON");
        foreach (string property in new[] { "_UseDissolve", "_UseDissolveOffsetUV", "_UseSP", "_UseLighting" })
            if (dust.HasProperty(property)) dust.SetFloat(property, 0f);
        AssetDatabase.CreateAsset(dust, Root + "Stonehoof_Dust.mat");
        return dust;
    }
}
