using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Собирает редактируемые prefab/material assets Pelag без ручного YAML.
/// Рецепт — источник истины: любой prefab можно удалить и пересобрать меню.
/// </summary>
public static class RazlomPelagVfxAssetBuilder
{
    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const int LibraryVersion = 29;
    private const int FlipbookTiles = 4;
    private const int FlipbookFrames = FlipbookTiles * FlipbookTiles;
    private const int ChainLinkCount = 96;
    // One continuous lasso needs links on both the outgoing side and the far arc.
    private const int EffectTriangleBudget = 14000;
    private const int RuntimeGeometryAllowance = 64;
    private const float FlipbookFramesPerSecond = 30f;
    private const float FlipbookLifetime = FlipbookFrames / FlipbookFramesPerSecond;
    private static string AutoBuildSessionKey => "Razlom.PelagVfx.AutoBuild.v" + LibraryVersion;

    private const string AnchorSpinTexturePath =
        Root + "/Textures/Pelag_FX_AnchorSpin_4x4.png";
    private const string ImpactBurstTexturePath =
        Root + "/Textures/Pelag_FX_ImpactBurst_4x4.png";
    private const string GroundCrackTexturePath =
        Root + "/Textures/Pelag_FX_GroundCrack_4x4.png";
    private const string DashSmearTexturePath =
        Root + "/Textures/Pelag_FX_DashSmear_4x4.png";
    private const string ChainGlintTexturePath =
        Root + "/Textures/Pelag_FX_ChainGlint_4x4.png";

    // ЛЕТИТ ГОЛОВА ЯКОРЯ, А НЕ ВЕСЬ МОТОК.
    //
    // До 1 сентября здесь стоял Pelag_AnchorChain.fbx — исходная генерация
    // целиком: якорь, свёрнутая спиралью цепь и рукоять с лентами, сплавленные
    // в один шелл на 22 824 треугольника. В полёте это читалось не как
    // брошенный якорь, а как летящий клубок; и стоило столько же, сколько
    // весь вражеский солдат.
    //
    // Голова вырезана из того же исходника по высоте (разделение по несвязным
    // кускам даёт ровно один объект — модель сплавлена) и упрощена до 2600
    // треугольников. Начало координат — в точке, где от неё уходит цепь,
    // чтобы код не искал её заново.
    private const string AnchorPath =
        "Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorHead.fbx";

    /// <summary>
    /// Рукоять с лентами: то, что остаётся в руке, когда якорь брошен.
    /// ПОКА НЕ ПОДКЛЮЧЕНА — вырезана и лежит, ждёт своей анимации.
    /// </summary>
    private const string AnchorGripPath =
        "Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorGrip.fbx";

    /// <summary>
    /// Одно оптимизированное звено для PelagChainLinkStrip (96 треугольников).
    /// Двадцать четыре звена вместе с головой якоря остаются ниже 5k tris.
    /// </summary>
    private const string ChainLinkPath =
        "Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_ChainLink.fbx";
    private const string HovlKnifeHitPath =
        "Assets/Hovl Studio/AOE Magic spells Vol.1/Prefabs/Knife hit.prefab";
    private const string HovlStoneSlashPath =
        "Assets/Hovl Studio/Magic effects pack/Prefabs/Slash effects/Stone slash.prefab";
    private static Mesh _chainLinkMesh;

    [InitializeOnLoadMethod]
    private static void ScheduleBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
            AbilityVfxLibrary library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
            if (library == null || library.BuildVersion != LibraryVersion)
            {
                SessionState.SetBool(AutoBuildSessionKey, true);
                Build();
            }
        };
    }

    [MenuItem("Разлом/Pelag VFX/Пересобрать prefabs и материалы")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources", "VFX");
        EnsureFolder("Assets/Resources/VFX", "Pelag");
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Geometry");

        if (!EnsureFlipbookTextures()) return;

        Shader vfxShader = Shader.Find("Razlom/Pelag VFX");
        Shader dustShader = Shader.Find("Razlom/Pelag Dust");
        Shader flipbookShader = Shader.Find("Razlom/Pelag Flipbook");
        if (vfxShader == null || dustShader == null || flipbookShader == null)
        {
            Debug.LogWarning("[Pelag VFX] Шейдеры ещё импортируются; сборка перенесена.");
            return;
        }

        Material slash = VfxMaterial("M_SlashTrail", vfxShader,
            new Color(0.22f, 0.23f, 0.25f, 0.94f), new Color(1.00f, 0.97f, 0.88f, 1f), 1.35f, 0.58f);
        // ЛИНИЯ ЦЕПИ, А НЕ ТРЕЙЛ ЯКОРЯ.
        //
        // Объявление вернулось 1 сентября: предыдущий заход убирал с брошенного
        // якоря обычный TrailRenderer в пользу вращающегося flipbook-смаза — и
        // заодно снёс этот материал, оставив два вызова на него. Проект перестал
        // компилироваться: `error CS0103: имя 'anchor' не существует`.
        //
        // Материал нужен не трейлу. Им красится LineRenderer НАТЯНУТОЙ ЦЕПИ в
        // AnchorLeapChain и CycloneChain, и ассет `M_AnchorTrail.mat` всё это
        // время лежал на диске. Имя осталось историческим; переименовывать его
        // сейчас значит потерять ссылки в трёх prefab'ах ради косметики.
        Material anchor = VfxMaterial("M_AnchorTrail", vfxShader,
            new Color(0.11f, 0.14f, 0.15f, 0.94f), new Color(0.62f, 0.72f, 0.72f, 0.92f),
            0.82f, 0.28f);
        Material impact = VfxMaterial("M_Impact", vfxShader,
            new Color(1.00f, 0.28f, 0.14f, 0.96f), new Color(1.00f, 0.97f, 0.84f, 1f), 1.42f, 0.62f);
        Material whirlwind = VfxMaterial("M_WhirlwindBrush", vfxShader,
            new Color(0.03f, 0.45f, 0.56f, 1.00f), new Color(0.18f, 0.90f, 0.90f, 1.00f), 1.15f, 0.38f);
        Material whirlwindAccent = VfxMaterial("M_WhirlwindAccent", vfxShader,
            new Color(0.78f, 0.05f, 0.22f, 0.92f), new Color(1.00f, 0.42f, 0.40f, 1.00f), 1.05f, 0.32f);
        Material dash = VfxMaterial("M_DashStreak", vfxShader,
            new Color(1.00f, 0.30f, 0.25f, 0.46f), new Color(1.00f, 0.86f, 0.72f, 0.86f), 0.95f, 0.36f);
        Material flash = VfxMaterial("M_TargetFlash", vfxShader,
            new Color(1.00f, 0.64f, 0.40f, 0.68f), new Color(1.00f, 0.97f, 0.88f, 0.95f), 1.08f, 0.66f);
        Material dust = DustMaterial("M_DustStylized", dustShader,
            new Color(0.72f, 0.56f, 0.38f, 0.46f));
        Material anchorSpin = FlipbookMaterial("M_Flipbook_AnchorSpin", flipbookShader,
            AnchorSpinTexturePath, Color.white, 1.00f);
        Material impactBurst = FlipbookMaterial("M_Flipbook_ImpactBurst", flipbookShader,
            ImpactBurstTexturePath, Color.white, 1.12f);
        Material groundCrack = FlipbookMaterial("M_Flipbook_GroundCrack", flipbookShader,
            GroundCrackTexturePath, Color.white, 0.92f);
        Material dashSmear = FlipbookMaterial("M_Flipbook_DashSmear", flipbookShader,
            DashSmearTexturePath, Color.white, 1.00f);
        Material chainGlint = FlipbookMaterial("M_Flipbook_ChainGlint", flipbookShader,
            ChainGlintTexturePath, Color.white, 1.08f);
        Material metal = AnchorMetalMaterial();
        _chainLinkMesh = LoadChainLinkMesh();
        if (_chainLinkMesh == null || !ValidateGeometryBudget()) return;

        GameObject[] prefabs = new GameObject[(int)PelagVfxId.Count];
        prefabs[(int)PelagVfxId.AutoAttackSlash] = SaveArc(PelagVfxId.AutoAttackSlash,
            "VFX_AutoAttack_Slash", slash, impact, 0.34f, 0.24f, false);
        // Keep this slot as a small warm fallback for future authored attacks.
        // The imported Knife Hit carried a blue sword/shockwave mesh that read
        // as a second weapon inside the target; gameplay contact now uses the
        // directional blade ribbon plus the compact CombatJuice burst.
        prefabs[(int)PelagVfxId.AutoAttackImpact] = SaveBurst(PelagVfxId.AutoAttackImpact,
            "VFX_AutoAttack_Impact", impact, 4, 0.14f, 3.2f, 0.11f, 0.24f,
            impactBurst, 0.90f);
        prefabs[(int)PelagVfxId.WhirlwindRing] = SaveWhirlwindBrush(PelagVfxId.WhirlwindRing,
            "VFX_Whirlwind_Brush", 1.0f);
        prefabs[(int)PelagVfxId.WhirlwindHit] = SaveBurst(PelagVfxId.WhirlwindHit,
            "VFX_Whirlwind_Hit", impact, 5, 0.16f, 3.8f, 0.16f, 0.34f,
            impactBurst, 1.10f);
        prefabs[(int)PelagVfxId.AnchorLeapThrow] = SaveAnchor(PelagVfxId.AnchorLeapThrow,
            "VFX_AnchorLeap_Throw", metal, anchorSpin, 0.60f);
        prefabs[(int)PelagVfxId.AnchorLeapChain] = SaveDynamicLine(PelagVfxId.AnchorLeapChain,
            "VFX_AnchorLeap_Chain", anchor, metal, chainGlint, 0.045f, 0.90f, true);
        prefabs[(int)PelagVfxId.AnchorLeapLand] = SaveBurst(PelagVfxId.AnchorLeapLand,
            "VFX_AnchorLeap_Land", dust, 18, 0.40f, 3.8f, 0.28f, FlipbookLifetime);
        prefabs[(int)PelagVfxId.CycloneHook] = SaveAnchor(PelagVfxId.CycloneHook,
            "VFX_Cyclone_Hook", metal, anchorSpin, 2.30f);
        prefabs[(int)PelagVfxId.CycloneChain] = SaveDynamicLine(PelagVfxId.CycloneChain,
            "VFX_Cyclone_Chain", anchor, metal, null, 0.018f, 2.30f, true);
        prefabs[(int)PelagVfxId.CycloneWake] = SaveTrail(PelagVfxId.CycloneWake,
            "VFX_Cyclone_Wake", slash, 2.30f);
        prefabs[(int)PelagVfxId.ChainStepDash] = SaveTrail(PelagVfxId.ChainStepDash,
            "VFX_ChainStep_Dash", slash, 0.23f);
        prefabs[(int)PelagVfxId.ChainStepHit] = SaveArc(PelagVfxId.ChainStepHit,
            "VFX_ChainStep_Hit", slash, slash, .35f, .22f, false);
        prefabs[(int)PelagVfxId.TargetFlash] = SaveBurst(PelagVfxId.TargetFlash,
            "VFX_TargetFlash", flash, 1, 0.11f, 0.02f, 0.48f, 0.15f,
            impactBurst, 0.62f);
        prefabs[(int)PelagVfxId.DustSmall] = SaveBurst(PelagVfxId.DustSmall,
            "VFX_DustSmall", dust, 3, 0.28f, 1.5f, 0.19f, 0.38f);
        prefabs[(int)PelagVfxId.DustHeavy] = SaveBurst(PelagVfxId.DustHeavy,
            "VFX_DustHeavy", dust, 6, 0.42f, 2.2f, 0.27f, 0.66f);

        CreateLibrary(prefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Pelag VFX] Созданы 15 pooled-prefabs, 18 материалов и AbilityVfxLibrary v{LibraryVersion}.");
    }

    private static Material VfxMaterial(string name, Shader shader, Color edge, Color core,
        float intensity, float softness)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = LoadOrCreateMaterial(path, shader);
        material.name = name;
        material.SetColor("_BaseColor", edge);
        material.SetColor("_CoreColor", core);
        material.SetFloat("_Intensity", intensity);
        material.SetFloat("_Softness", softness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material DustMaterial(string name, Shader shader, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = LoadOrCreateMaterial(path, shader);
        material.name = name;
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material FlipbookMaterial(string name, Shader shader, string texturePath,
        Color tint, float emission)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = LoadOrCreateMaterial(path, shader);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.name = name;
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Emission", emission);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static bool EnsureFlipbookTextures()
    {
        string[] paths =
        {
            AnchorSpinTexturePath,
            ImpactBurstTexturePath,
            GroundCrackTexturePath,
            DashSmearTexturePath,
            ChainGlintTexturePath
        };

        bool ready = true;
        for (int i = 0; i < paths.Length; i++)
        {
            TextureImporter importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
            }

            if (importer == null)
            {
                Debug.LogWarning("[Pelag VFX] Flipbook ещё не импортирован: " + paths[i]);
                ready = false;
                continue;
            }

            if (RazlomPelagVfxTexturePolicy.Apply(importer)) importer.SaveAndReimport();

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            if (texture == null || texture.width != 2048 || texture.height != 2048)
            {
                string dimensions = texture == null ? "null" : $"{texture.width}x{texture.height}";
                Debug.LogError($"[Pelag VFX] Ожидался RGBA atlas 2048x2048, получен {dimensions}: {paths[i]}");
                ready = false;
            }
        }
        return ready;
    }

    private static Material AnchorMetalMaterial()
    {
        string path = MaterialFolder + "/M_AnchorMetal.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        Material material = LoadOrCreateMaterial(path, shader);
        material.name = "M_AnchorMetal";
        material.enableInstancing = true;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.45f, 0.42f, 0.36f, 1f));
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.08f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.42f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            if (shader != null) material.shader = shader;
            return material;
        }
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject SaveArc(PelagVfxId id, string name, Material material, Material coreMaterial,
        float width, float lifetime, bool loop)
    {
        GameObject root = RootObject(id, name, lifetime);
        LineRenderer line = AddLine(root, material, width, false, loop);
        // Непрерывная дуга сохраняет острый кончик без углов из пяти отрезков.
        Vector3[] points = new Vector3[33];
        for (int i = 0; i < points.Length; i++)
        {
            float u = i / (float)(points.Length - 1);
            points[i] = new Vector3(Mathf.Lerp(-.92f, .92f, u),
                -.18f + .48f * Mathf.Sin(u * Mathf.PI), 0f);
        }
        line.positionCount = points.Length;
        line.SetPositions(points);
        LineRenderer core = AddLine(root, coreMaterial, width * 0.30f, false, loop);
        core.positionCount = points.Length;
        core.SetPositions(points);
        if (id == PelagVfxId.ChainStepHit)
            AddMetalSparks(root, coreMaterial, 7, .16f, 3.8f);
        return Save(root, name);
    }

    private static GameObject SaveRing(PelagVfxId id, string name, Material material, Material coreMaterial,
        float radius, float width, float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        LineRenderer line = AddLine(root, material, width, false, true);
        const int Count = 48;
        line.positionCount = Count;
        for (int i = 0; i < Count; i++)
        {
            float angle = i * Mathf.PI * 2f / Count;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        LineRenderer core = AddLine(root, coreMaterial, width * 0.28f, false, true);
        core.positionCount = Count;
        for (int i = 0; i < Count; i++) core.SetPosition(i, line.GetPosition(i));
        return Save(root, name);
    }

    private static GameObject SaveWhirlwindBrush(PelagVfxId id, string name, float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        AddHovlWhirlwindAccents(root);
        // Horizontal authored strokes share one palette; omit the detached
        // upright charge projectile that fought the circular silhouette.
        Shader shader = Shader.Find("Razlom/Pelag VFX");
        Material edge = VfxMaterial("M_Whirlwind_CopperStroke", shader,
            new Color(1f, .47f, .12f, .82f), new Color(1.4f, .88f, .38f, .96f), 1.25f, .35f);
        Material core = VfxMaterial("M_Whirlwind_SteelCore", shader,
            new Color(1f, .88f, .64f, .9f), new Color(1.5f, 1.32f, .95f, 1f), 1.4f, .55f);
        edge.SetFloat("_Brush", 0.85f);
        EditorUtility.SetDirty(edge);
        AddBrushArc(root, edge, 2.12f, -30f, 205f, 0.36f, -0.1f, 64);
        AddBrushArc(root, edge, 1.72f, 38f, 118f, 0.23f, 0.16f, 48);
        AddBrushArc(root, edge, 2.25f, 188f, 72f, 0.16f, -0.22f, 36);
        AddBrushArc(root, core, 2.14f, -22f, 186f, 0.052f, -0.08f, 64);
        AddBrushArc(root, edge, 1.88f, 195f, 82f, 0.12f, 0.08f, 36);
        AddMetalSparks(root, core, 14, .26f, 5.2f);
        return Save(root, name);
    }

    private static void AddMetalSparks(GameObject root, Material material, int count, float lifetime, float speed)
    {
        // Крупные короткие штрихи читаются на изометрии и не скрывают корпус.
        var host = new GameObject("Metal flecks");
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = false; main.playOnAwake = false; main.duration = lifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .55f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(.065f, .12f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(.015f, (short)count) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.rotation = new Vector3(90f, 0f, 0f); shape.radius = .35f;
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Gradient(new Color(1f, .97f, .8f, 1f), new Color(1f, .55f, .2f, 0f));
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.8f; renderer.velocityScale = .035f;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }

    private static GameObject SaveHovlImpact(PelagVfxId id, string name, Material fallbackMaterial)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(HovlKnifeHitPath);
        if (source == null)
        {
            Debug.LogWarning($"[Pelag VFX] Hovl hit не найден: {HovlKnifeHitPath}");
            return SaveBurst(id, name, fallbackMaterial, 5, 0.16f, 2.7f, 0.12f, 0.30f);
        }

        GameObject root = RootObject(id, name, 0.30f);
        GameObject imported = UnityEngine.Object.Instantiate(source, root.transform, false);
        imported.name = "Hovl Knife Hit";
        imported.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        imported.transform.localScale = Vector3.one * 0.40f;
        // Keep the readable hit core and sparks, but remove the authored
        // ground shockwave/smoke layers: a melee contact should stay on the
        // target silhouette and never paint a second ring on the floor.
        KeepNamedBranches(imported.transform, "Sparks", "SparksExpl");
        SanitizeImportedVfx(imported);
        return Save(root, name);
    }

    private static void AddHovlWhirlwindAccents(GameObject root)
    {
        const string path = "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/AoE slash blue.prefab";
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null)
        {
            Debug.LogWarning($"[Pelag VFX] Hovl slash не найден: {path}");
            return;
        }

        GameObject imported = UnityEngine.Object.Instantiate(source, root.transform, false);
        imported.name = "Saber Cyclone";
        imported.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        // Keep the authored crescent meshes and textures; remove the halo and
        // lens flares so the effect reads as steel cutting through the air.
        imported.transform.localScale = Vector3.one;
        KeepNamedBranches(imported.transform, "Slash", "Sparks");
        SanitizeImportedVfx(imported);
        ReplaceImportedParticleMaterials(imported);
        TuneWhirlwind(imported);
    }

    private static void TuneWhirlwind(GameObject root)
    {
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps.gameObject == root)
            {
                var rootEmission = ps.emission;
                rootEmission.enabled = false;
                continue;
            }
            bool sparks = ps.name == "Sparks";
            var main = ps.main;
            main.startDelay = 0f;
            main.startLifetime = sparks ? 0.28f : 0.30f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startColor = Color.white;
            main.startSize = sparks ? 0.085f : 4.8f;
            main.startSpeed = sparks ? 0.9f : 0f;
            main.startRotation = 0f;
            main.startRotation3D = false;
            main.maxParticles = sparks ? 8 : 4;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] {
                new GradientColorKey(new Color(1.08f, .80f, .52f), 0f),
                new GradientColorKey(new Color(.72f, .38f, .17f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.88f, 0.10f),
                    new GradientAlphaKey(0.64f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(sparks ? 6 : 1)) });
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) continue;
            if (!sparks)
            {
                var shape = ps.shape; shape.enabled = false;
                var velocity = ps.velocityOverLifetime; velocity.enabled = false;
                var force = ps.forceOverLifetime; force.enabled = false;
                var noise = ps.noise; noise.enabled = false;
                var rotation = ps.rotationOverLifetime; rotation.enabled = false;
                var size = ps.sizeOverLifetime; size.enabled = false;
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                renderer.alignment = ParticleSystemRenderSpace.Local;
            }
            Material source = renderer.sharedMaterial;
            string path = MaterialFolder + "/M_Whirlwind_" + source.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Emission")) material.SetFloat("_Emission", 0.9f);
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(material);
        }
    }

    private static void ReplaceImportedParticleMaterials(GameObject root)
    {
        // Hovl's legacy Particles/Alpha Blended shader is not included by the
        // URP player build. Keep the imported textures and curves, but point
        // this prefab's renderers at local player-compatible material copies.
        Shader particleShader = Shader.Find("Hovl/Particles/Blend_CenterGlow")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Razlom/CombatFx");
        if (particleShader == null)
        {
            Debug.LogWarning("[Pelag VFX] Совместимый particle shader не найден.");
            return;
        }

        ParticleSystemRenderer[] renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material source = renderers[i].sharedMaterial;
            if (source == null) continue;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!sourcePath.StartsWith("Assets/Hovl Studio/",
                    StringComparison.OrdinalIgnoreCase)) continue;

            Material compatible = LoadOrCreateImportedParticleMaterial(source, particleShader);
            if (compatible != null) renderers[i].sharedMaterial = compatible;
        }
    }

    private static Material LoadOrCreateImportedParticleMaterial(Material source, Shader shader)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        string targetPath = MaterialFolder + "/M_StoneSlash_" + sourceName + ".mat";
        Material target = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
        if (target == null)
        {
            target = new Material(shader) { name = "M_StoneSlash_" + sourceName };
            AssetDatabase.CreateAsset(target, targetPath);
        }
        else
        {
            target.shader = shader;
        }

        Texture texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
        if (texture != null)
        {
            if (target.HasProperty("_MainTex"))
            {
                target.SetTexture("_MainTex", texture);
                target.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }
            if (target.HasProperty("_BaseMap")) target.SetTexture("_BaseMap", texture);
        }

        Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        if (target.HasProperty("_Color")) target.SetColor("_Color", color);
        if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", color);
        if (target.HasProperty("_Emission")) target.SetFloat("_Emission", 1.5f);
        if (target.HasProperty("_Opacity")) target.SetFloat("_Opacity", 1f);
        if (target.HasProperty("_Usecenterglow")) target.SetFloat("_Usecenterglow", 0f);
        if (target.HasProperty("_Usealphacenterglow")) target.SetFloat("_Usealphacenterglow", 0f);
        if (target.HasProperty("_CullMode")) target.SetFloat("_CullMode", 0f);

        if (target.HasProperty("_Blend")) target.SetFloat("_Blend", 0f);
        if (target.HasProperty("_SrcBlend")) target.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (target.HasProperty("_DstBlend")) target.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (target.HasProperty("_ZWrite")) target.SetFloat("_ZWrite", 0f);
        if (target.HasProperty("_Cull")) target.SetFloat("_Cull", (float)CullMode.Off);
        target.renderQueue = 3000;
        target.EnableKeyword("_ALPHABLEND_ON");
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void TuneStoneSlashPalette(GameObject root)
    {
        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            if (particles[i].gameObject == root)
            {
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.00f, 0.16f, 0.10f, 1.00f),
                    new Color(1.00f, 0.95f, 0.76f, 1.00f));
                main.startSizeMultiplier *= 1.05f;
            }
            else if (particles[i].gameObject.name == "Flash")
            {
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.00f, 0.72f, 0.38f, 0.88f),
                    new Color(1.00f, 0.98f, 0.84f, 1.00f));
            }
            else if (particles[i].gameObject.name == "Sparks")
            {
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.00f, 0.24f, 0.13f, 1.00f),
                    new Color(1.00f, 0.86f, 0.48f, 1.00f));
            }
        }
    }

    private static void KeepNamedBranches(Transform root, params string[] branchNames)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        var keep = new System.Collections.Generic.HashSet<Transform> { root };

        for (int i = 0; i < transforms.Length; i++)
        {
            bool namedBranch = false;
            for (int nameIndex = 0; nameIndex < branchNames.Length; nameIndex++)
            {
                if (transforms[i].name != branchNames[nameIndex]) continue;
                namedBranch = true;
                break;
            }
            if (!namedBranch) continue;

            Transform current = transforms[i];
            while (current != null)
            {
                keep.Add(current);
                if (current == root) break;
                current = current.parent;
            }

            Transform[] descendants = transforms[i].GetComponentsInChildren<Transform>(true);
            for (int descendant = 0; descendant < descendants.Length; descendant++)
                keep.Add(descendants[descendant]);
        }

        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            if (transforms[i] == root || keep.Contains(transforms[i])) continue;
            if (transforms[i] != null) UnityEngine.Object.DestroyImmediate(transforms[i].gameObject);
        }
    }

    private static void AddAuthoredAccent(GameObject root, string path, float scale, float lifetime,
        bool ground, params string[] branches)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) { Debug.LogWarning("[Pelag VFX] Missing accent: " + path); return; }
        GameObject accent = UnityEngine.Object.Instantiate(source, root.transform, false);
        accent.name = "Pelag " + source.name;
        accent.transform.localScale = Vector3.one * scale;
        accent.transform.localPosition = Vector3.zero;
        if (branches.Length > 0) KeepNamedBranches(accent.transform, branches);
        SanitizeImportedVfx(accent);
        ReplaceImportedParticleMaterials(accent);
        foreach (ParticleSystem ps in accent.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.startDelay = 0f;
            if (ps.name.Contains("ShockWave") || ps.name.Contains("Glow"))
            { var hide = ps.emission; hide.enabled = false; continue; }
            if (ps.name.Contains("Slash"))
            {
                main.startSpeed = 0f;
                var velocity = ps.velocityOverLifetime; velocity.enabled = false;
                var force = ps.forceOverLifetime; force.enabled = false;
            }
            main.duration = lifetime; main.startLifetimeMultiplier = lifetime;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Min(24, Mathf.Max(4, main.maxParticles));
            bool debris = ground && (ps.name.Contains("Smoke") || ps.name.Contains("Stones") || ps.name.Contains("Crater"));
            if (ps.name.Contains("Smoke")) main.startSizeMultiplier *= 0.55f;
            main.startColor = debris ? new Color(0.72f, 0.52f, 0.27f, 0.7f) : new Color(1.35f, 0.87f, 0.55f, 1f);
            var color = ps.colorOverLifetime; color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(debris ? Color.white : new Color(1f, 0.32f, 0.14f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.35f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var emission = ps.emission;
            if (emission.enabled)
            {
                emission.rateOverTime = 0f; emission.rateOverDistance = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(ps.name.Contains("Smoke") ? 3 : debris ? 6 : ps.name.Contains("Sparks") ? 10 : 1)) });
            }
        }
    }

    private static void SanitizeImportedVfx(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[i].gameObject);

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null) UnityEngine.Object.DestroyImmediate(behaviours[i]);
        }

        Light[] lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++) UnityEngine.Object.DestroyImmediate(lights[i]);

        AudioSource[] audioSources = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audioSources.Length; i++)
            UnityEngine.Object.DestroyImmediate(audioSources[i]);

        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            main.loop = false;
            main.playOnAwake = false;

            ParticleSystemRenderer renderer = particles[i].GetComponent<ParticleSystemRenderer>();
            if (renderer == null) continue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private static void AddBrushArc(GameObject root, Material material, float radius,
        float startDegrees, float sweepDegrees, float width, float height, int segments)
    {
        LineRenderer line = AddLine(root, material, width, false, false);
        line.positionCount = segments;
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.04f), new Keyframe(0.12f, 0.72f),
            new Keyframe(0.36f, 1f), new Keyframe(0.82f, 0.58f),
            new Keyframe(1f, 0.02f));
        Gradient visibility = new Gradient();
        visibility.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.96f, 0.12f),
                new GradientAlphaKey(0.78f, 0.76f), new GradientAlphaKey(0f, 1f)
            });
        line.colorGradient = visibility;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float angle = (startDegrees + sweepDegrees * t) * Mathf.Deg2Rad;
            float brokenEdge = Mathf.Sin(t * Mathf.PI * 5f) * 0.025f;
            float r = radius + brokenEdge;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * r,
                height + Mathf.Sin(t * Mathf.PI) * 0.035f,
                Mathf.Sin(angle) * r));
        }
    }

    private static GameObject SaveDynamicLine(PelagVfxId id, string name, Material material,
        Material metalMaterial, Material glintMaterial, float width, float lifetime, bool physicalChain)
    {
        GameObject root = RootObject(id, name, lifetime);
        root.GetComponent<PelagVfxElement>().DynamicLine = true;
        AddLine(root, material, width, true, false).positionCount = 0;
        if (physicalChain)
        {
            AddChainLinks(root, metalMaterial);
            AddFlipbook(root, "Chain Glint Flipbook", glintMaterial, 0.70f,
                false, Vector3.zero, 4);
        }
        return Save(root, name);
    }

    private static GameObject SaveTrail(PelagVfxId id, string name, Material flipbookMaterial,
        float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        var trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = flipbookMaterial;
        trail.time = 0.13f;
        trail.widthCurve = AnimationCurve.Linear(0f, 0.48f, 1f, 0f);
        trail.startColor = new Color(1f, 0.97f, 0.88f, 1f);
        trail.endColor = new Color(0.7f, 0.7f, 0.72f, 0f);
        trail.minVertexDistance = 0.03f;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        return Save(root, name);
    }

    private static GameObject SaveAnchor(PelagVfxId id, string name,
        Material metalMaterial, Material spinMaterial, float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPath);
        if (source != null)
        {
            GameObject anchor = (GameObject)PrefabUtility.InstantiatePrefab(source);
            anchor.name = "Physical Anchor";
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localScale = Vector3.one;
            anchor.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = 0.62f / Mathf.Max(0.001f, size);
                anchor.transform.localScale = Vector3.one * scale;
                anchor.transform.localPosition = -bounds.center * scale;
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int m = 0; m < materials.Length; m++) materials[m] = metalMaterial;
                renderers[i].sharedMaterials = materials;
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
            }
        }
        else
        {
            Debug.LogWarning("[Pelag VFX] Anchor FBX ещё не импортирован: " + AnchorPath);
        }

        // The physical head and chain carry the silhouette. A thin steel
        // wake follows the real projectile instead of a second painted anchor.
        var trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_AnchorTrail.mat");
        trail.time = 0.07f;
        trail.startWidth = 0.06f;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.04f;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        return Save(root, name);
    }

    private static GameObject SaveBurst(PelagVfxId id, string name, Material material,
        int count, float lifetime, float speed, float size, float rootLifetime,
        Material impactFlipbook = null, float impactSize = 1f,
        Material groundFlipbook = null, float groundSize = 1f)
    {
        GameObject root = RootObject(id, name, rootLifetime);
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.05f, rootLifetime);
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.72f, size * 1.20f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(8, count + 2);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        shape.randomDirectionAmount = 1f;

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        if (id == PelagVfxId.AnchorLeapLand)
        {
            // Удар выбивает направленные сколы вместо одинаковых светящихся точек.
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.rotation = new Vector3(-22f, 0f, 0f);
            shape.randomDirectionAmount = .15f;
            main.gravityModifier = 1.8f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(.07f,.12f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(.035f,.065f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(.16f,.24f);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ImpactChipMesh();
            renderer.alignment = ParticleSystemRenderSpace.Velocity;
            AddAuthoredAccent(root, "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Ground AOE explosion.prefab",
                0.48f, 0.32f, true, "Stones");
        }
        if (id == PelagVfxId.WhirlwindHit)
            AddAuthoredAccent(root, "Assets/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Punch Hit.prefab",
                0.55f, 0.24f, false);
        if (impactFlipbook != null)
            AddFlipbook(root, "Impact Burst Flipbook", impactFlipbook, impactSize,
                false, Vector3.zero, 5);
        if (groundFlipbook != null)
            AddFlipbook(root, "Ground Crack Flipbook", groundFlipbook, groundSize,
                true, new Vector3(0f, 0.015f, 0f), -2);
        return Save(root, name);
    }

    private static Mesh ImpactChipMesh()
    {
        string path = Root + "/Pelag_ImpactChip.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
        mesh.Clear(); mesh.name = "Pelag Impact Chip";
        mesh.vertices = new[] { new Vector3(-.5f,0,-.3f), new Vector3(.5f,0,-.5f),
            new Vector3(.22f,0,.5f), new Vector3(-.25f,0,.35f), new Vector3(0,.5f,0) };
        mesh.triangles = new[] { 0,4,1, 1,4,2, 2,4,3, 3,4,0, 0,1,2, 0,2,3 };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.5f,.5f) };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static ParticleSystem AddFlipbook(GameObject root, string name, Material material,
        float size, bool horizontal, Vector3 localPosition, int sortingOrder)
    {
        if (material == null) return null;

        GameObject host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        host.transform.localPosition = localPosition;

        ParticleSystem particles = host.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = FlipbookLifetime;
        main.startLifetime = FlipbookLifetime;
        main.startSpeed = 0f;
        main.startSize = size;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 1;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.numTilesX = FlipbookTiles;
        sheet.numTilesY = FlipbookTiles;
        sheet.cycleCount = 1;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.Linear(0f, 0f, 1f, 1f));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = horizontal
            ? ParticleSystemRenderMode.HorizontalBillboard
            : ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enableGPUInstancing = true;
        return particles;
    }

    private static GameObject RootObject(PelagVfxId id, string name, float lifetime)
    {
        GameObject root = new GameObject(name);
        PelagVfxElement element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        return root;
    }

    private static LineRenderer AddLine(GameObject root, Material material, float width,
        bool worldSpace, bool loop)
    {
        int lineIndex = root.GetComponentsInChildren<LineRenderer>(true).Length;
        GameObject lineObject = new GameObject(lineIndex == 0 ? "Line Edge" : $"Line Core {lineIndex:00}");
        lineObject.transform.SetParent(root.transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = worldSpace;
        line.loop = loop;
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.22f),
            new Keyframe(0.18f, 1f), new Keyframe(0.82f, 0.72f), new Keyframe(1f, 0.08f));
        line.numCapVertices = 3;
        line.numCornerVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.colorGradient = Gradient(Color.white, new Color(1f, 1f, 1f, 0.12f));
        return line;
    }

    private static Gradient Gradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        return gradient;
    }

    private static void AddChainLinks(GameObject root, Material material)
    {
        GameObject host = new GameObject("Physical Chain Links");
        host.transform.SetParent(root.transform, false);
        PelagChainLinkStrip strip = host.AddComponent<PelagChainLinkStrip>();
        strip.Links = new Transform[ChainLinkCount];
        for (int i = 0; i < ChainLinkCount; i++)
        {
            GameObject link = new GameObject($"Link_{i:00}");
            link.transform.SetParent(host.transform, false);
            link.transform.localScale = Vector3.one;
            MeshFilter filter = link.AddComponent<MeshFilter>();
            filter.sharedMesh = _chainLinkMesh;
            MeshRenderer renderer = link.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            strip.Links[i] = link.transform;
        }
    }

    private static Mesh LoadChainLinkMesh()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ChainLinkPath);
        if (source == null)
        {
            Debug.LogError("[Pelag VFX] Оптимизированное звено не импортировано: " + ChainLinkPath);
            return null;
        }

        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
        Mesh best = null;
        long bestIndices = -1;
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null) continue;
            long indices = IndexCount(mesh);
            if (indices <= bestIndices) continue;
            best = mesh;
            bestIndices = indices;
        }

        if (best == null)
            Debug.LogError("[Pelag VFX] В FBX звена нет MeshFilter: " + ChainLinkPath);
        if (best == null) return null;
        Mesh normalized = UnityEngine.Object.Instantiate(best);
        normalized.name = "Pelag_ChainLink_Centered";
        Vector3 size = best.bounds.size;
        Vector3 axis = size.x > size.y && size.x > size.z ? Vector3.right
            : size.y > size.z ? Vector3.up : Vector3.forward;
        Quaternion rotation = Quaternion.FromToRotation(axis, Vector3.forward);
        float scale = 0.16f / Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        Vector3[] vertices = normalized.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = rotation * (vertices[i] - best.bounds.center) * scale;
        normalized.vertices = vertices;
        normalized.RecalculateNormals();
        normalized.RecalculateBounds();
        const string path = Root + "/Geometry/Pelag_ChainLink_Centered.asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { AssetDatabase.CreateAsset(normalized, path); saved = normalized; }
        else { EditorUtility.CopySerialized(normalized, saved); UnityEngine.Object.DestroyImmediate(normalized); }
        EditorUtility.SetDirty(saved);
        return saved;
    }

    private static bool ValidateGeometryBudget()
    {
        GameObject anchor = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPath);
        if (anchor == null)
        {
            Debug.LogError("[Pelag VFX] Голова якоря не импортирована: " + AnchorPath);
            return false;
        }

        long anchorTriangles = TriangleCount(anchor);
        long linkTriangles = IndexCount(_chainLinkMesh) / 3L;
        long combined = anchorTriangles + linkTriangles * ChainLinkCount + RuntimeGeometryAllowance;
        if (combined > EffectTriangleBudget)
        {
            Debug.LogError($"[Pelag VFX] Геометрия якоря и цепи превышает бюджет: " +
                           $"{combined} > {EffectTriangleBudget} tris " +
                           $"(anchor {anchorTriangles}, links {linkTriangles} x {ChainLinkCount}, " +
                           $"runtime allowance {RuntimeGeometryAllowance}).");
            return false;
        }

        Debug.Log($"[Pelag VFX] Геометрия в бюджете: {combined}/{EffectTriangleBudget} tris " +
                  $"(anchor {anchorTriangles}, links {linkTriangles} x {ChainLinkCount}).");
        return true;
    }

    private static long TriangleCount(GameObject root)
    {
        long indices = 0;
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++) indices += IndexCount(filters[i].sharedMesh);
        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++) indices += IndexCount(skinned[i].sharedMesh);
        return indices / 3L;
    }

    private static long IndexCount(Mesh mesh)
    {
        if (mesh == null) return 0;
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i);
        return count;
    }

    private static GameObject Save(GameObject root, string name)
    {
        if (name.Contains("Whirlwind") || name.Contains("AnchorLeap_Land"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>("Assets/Resources/VFX/Pelag/Graphs/Pelag_CoralImpact.vfx");
            if (asset != null)
            {
                var accents = new GameObject("Coral ink sparks VFX Graph");
                accents.transform.SetParent(root.transform, false);
                accents.AddComponent<UnityEngine.VFX.VisualEffect>().visualEffectAsset = asset;
            }
        }
        string path = PrefabFolder + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static void CreateLibrary(GameObject[] prefabs)
    {
        AbilityVfxLibrary library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<AbilityVfxLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        library.name = Path.GetFileNameWithoutExtension(LibraryPath);
        library.BuildVersion = LibraryVersion;

        library.Entries = new AbilityVfxLibrary.Entry[(int)PelagVfxId.Count];
        for (int i = 0; i < library.Entries.Length; i++)
        {
            PelagVfxId id = (PelagVfxId)i;
            library.Entries[i] = new AbilityVfxLibrary.Entry
            {
                Id = id,
                Prefab = prefabs[i],
                Prewarm = Prewarm(id)
            };
        }
        EditorUtility.SetDirty(library);
    }

    private static int Prewarm(PelagVfxId id)
    {
        switch (id)
        {
            case PelagVfxId.WhirlwindHit: return 10;
            case PelagVfxId.CycloneWake: return 10;
            case PelagVfxId.TargetFlash: return 12;
            case PelagVfxId.DustSmall: return 12;
            case PelagVfxId.AutoAttackImpact:
            case PelagVfxId.ChainStepDash:
            case PelagVfxId.ChainStepHit: return 6;
            default: return 3;
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}

/// <summary>
/// Deterministic import policy for authored Pelag flipbooks. Keeping this next
/// to the prefab recipe means a newly regenerated atlas cannot silently become
/// a Sprite, gain mip bleed between cells, or switch away from straight alpha.
/// </summary>
internal static class RazlomPelagVfxTexturePolicy
{
    private const string Prefix = "Assets/Resources/VFX/Pelag/Textures/Pelag_FX_";
    private const string Suffix = "_4x4.png";

    public static bool Applies(string path)
    {
        return !string.IsNullOrEmpty(path)
               && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
               && path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Apply(TextureImporter importer)
    {
        bool changed = false;
        Set(ref changed, importer.textureType != TextureImporterType.Default,
            () => importer.textureType = TextureImporterType.Default);
        Set(ref changed, importer.textureShape != TextureImporterShape.Texture2D,
            () => importer.textureShape = TextureImporterShape.Texture2D);
        Set(ref changed, !importer.sRGBTexture, () => importer.sRGBTexture = true);
        Set(ref changed, importer.alphaSource != TextureImporterAlphaSource.FromInput,
            () => importer.alphaSource = TextureImporterAlphaSource.FromInput);
        Set(ref changed, !importer.alphaIsTransparency, () => importer.alphaIsTransparency = true);
        Set(ref changed, importer.mipmapEnabled, () => importer.mipmapEnabled = false);
        Set(ref changed, importer.streamingMipmaps, () => importer.streamingMipmaps = false);
        Set(ref changed, importer.wrapMode != TextureWrapMode.Clamp,
            () => importer.wrapMode = TextureWrapMode.Clamp);
        Set(ref changed, importer.filterMode != FilterMode.Bilinear,
            () => importer.filterMode = FilterMode.Bilinear);
        Set(ref changed, importer.anisoLevel != 0, () => importer.anisoLevel = 0);
        Set(ref changed, importer.npotScale != TextureImporterNPOTScale.None,
            () => importer.npotScale = TextureImporterNPOTScale.None);
        Set(ref changed, importer.maxTextureSize != 2048, () => importer.maxTextureSize = 2048);
        Set(ref changed, importer.textureCompression != TextureImporterCompression.CompressedHQ,
            () => importer.textureCompression = TextureImporterCompression.CompressedHQ);
        Set(ref changed, importer.crunchedCompression, () => importer.crunchedCompression = false);
        Set(ref changed, importer.isReadable, () => importer.isReadable = false);
        return changed;
    }

    private static void Set(ref bool changed, bool condition, Action apply)
    {
        if (!condition) return;
        apply();
        changed = true;
    }
}

internal sealed class RazlomPelagVfxTextureImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!RazlomPelagVfxTexturePolicy.Applies(assetPath)) return;
        RazlomPelagVfxTexturePolicy.Apply((TextureImporter)assetImporter);
    }
}
