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
    private const int LibraryVersion = 9;

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
    /// Одно звено для процедурной цепи PelagChainLinkStrip.
    /// ПОКА НЕ ПОДКЛЮЧЕНО — цепь всё ещё рисуется линией, а не звеньями.
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
            AbilityVfxLibrary library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
            if (library == null || library.BuildVersion != LibraryVersion) Build();
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

        Shader vfxShader = Shader.Find("Razlom/Pelag VFX");
        Shader dustShader = Shader.Find("Razlom/Pelag Dust");
        if (vfxShader == null || dustShader == null)
        {
            Debug.LogWarning("[Pelag VFX] Шейдеры ещё импортируются; сборка перенесена.");
            return;
        }

        Material slash = VfxMaterial("M_SlashTrail", vfxShader,
            new Color(1.00f, 0.20f, 0.14f, 0.94f), new Color(1.00f, 0.95f, 0.78f, 1f), 1.35f, 0.58f);
        Material anchor = VfxMaterial("M_AnchorTrail", vfxShader,
            new Color(0.11f, 0.14f, 0.15f, 0.94f), new Color(0.62f, 0.72f, 0.72f, 0.92f), 0.82f, 0.28f);
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
        Material metal = AnchorMetalMaterial();
        _chainLinkMesh = CreateChainLinkMesh();

        GameObject[] prefabs = new GameObject[(int)PelagVfxId.Count];
        prefabs[(int)PelagVfxId.AutoAttackSlash] = SaveArc(PelagVfxId.AutoAttackSlash,
            "VFX_AutoAttack_Slash", slash, impact, 0.34f, 0.24f, false);
        // Keep this slot as a small warm fallback for future authored attacks.
        // The imported Knife Hit carried a blue sword/shockwave mesh that read
        // as a second weapon inside the target; gameplay contact now uses the
        // directional blade ribbon plus the compact CombatJuice burst.
        prefabs[(int)PelagVfxId.AutoAttackImpact] = SaveBurst(PelagVfxId.AutoAttackImpact,
            "VFX_AutoAttack_Impact", impact, 4, 0.14f, 3.2f, 0.11f, 0.24f);
        prefabs[(int)PelagVfxId.WhirlwindRing] = SaveWhirlwindBrush(PelagVfxId.WhirlwindRing,
            "VFX_Whirlwind_Brush", 1.0f);
        prefabs[(int)PelagVfxId.WhirlwindHit] = SaveBurst(PelagVfxId.WhirlwindHit,
            "VFX_Whirlwind_Hit", impact, 5, 0.19f, 3.8f, 0.16f, 0.34f);
        prefabs[(int)PelagVfxId.AnchorLeapThrow] = SaveAnchor(PelagVfxId.AnchorLeapThrow,
            "VFX_AnchorLeap_Throw", anchor, metal, 0.60f);
        prefabs[(int)PelagVfxId.AnchorLeapChain] = SaveDynamicLine(PelagVfxId.AnchorLeapChain,
            "VFX_AnchorLeap_Chain", anchor, metal, 0.045f, 0.90f, true);
        prefabs[(int)PelagVfxId.AnchorLeapLand] = SaveBurst(PelagVfxId.AnchorLeapLand,
            "VFX_AnchorLeap_Land", dust, 6, 0.34f, 2.2f, 0.24f, 0.58f);
        prefabs[(int)PelagVfxId.AnchorSweepThrow] = SaveAnchor(PelagVfxId.AnchorSweepThrow,
            "VFX_AnchorSweep_Throw", anchor, metal, 0.66f);
        prefabs[(int)PelagVfxId.AnchorSweepPull] = SaveDynamicLine(PelagVfxId.AnchorSweepPull,
            "VFX_AnchorSweep_Pull", anchor, metal, 0.055f, 1.18f, true);
        prefabs[(int)PelagVfxId.AnchorSweepEnemyPull] = SaveDynamicLine(PelagVfxId.AnchorSweepEnemyPull,
            "VFX_AnchorSweep_EnemyPull", dash, metal, 0.12f, 0.44f, false);
        prefabs[(int)PelagVfxId.ChainStepDash] = SaveTrail(PelagVfxId.ChainStepDash,
            "VFX_ChainStep_Dash", dash, 0.20f, 0.23f);
        prefabs[(int)PelagVfxId.ChainStepHit] = SaveBurst(PelagVfxId.ChainStepHit,
            "VFX_ChainStep_Hit", impact, 4, 0.15f, 3.0f, 0.13f, 0.28f);
        prefabs[(int)PelagVfxId.TargetFlash] = SaveBurst(PelagVfxId.TargetFlash,
            "VFX_TargetFlash", flash, 1, 0.11f, 0.02f, 0.48f, 0.15f);
        prefabs[(int)PelagVfxId.DustSmall] = SaveBurst(PelagVfxId.DustSmall,
            "VFX_DustSmall", dust, 3, 0.28f, 1.5f, 0.19f, 0.38f);
        prefabs[(int)PelagVfxId.DustHeavy] = SaveBurst(PelagVfxId.DustHeavy,
            "VFX_DustHeavy", dust, 6, 0.42f, 2.2f, 0.27f, 0.66f);

        CreateLibrary(prefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Pelag VFX] Созданы 15 pooled-prefabs, 13 материалов и AbilityVfxLibrary v{LibraryVersion}.");
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

    private static Material AnchorMetalMaterial()
    {
        string path = MaterialFolder + "/M_AnchorMetal.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        Material material = LoadOrCreateMaterial(path, shader);
        material.name = "M_AnchorMetal";
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.16f, 0.19f, 0.20f, 1f));
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.68f);
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
        Vector3[] points =
        {
            new Vector3(-0.82f, -0.18f, 0f), new Vector3(-0.45f, 0.12f, 0f),
            new Vector3(0f, 0.26f, 0f), new Vector3(0.45f, 0.12f, 0f),
            new Vector3(0.82f, -0.18f, 0f)
        };
        line.positionCount = points.Length;
        line.SetPositions(points);
        LineRenderer core = AddLine(root, coreMaterial, width * 0.30f, false, loop);
        core.positionCount = points.Length;
        core.SetPositions(points);
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
        return Save(root, name);
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
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(HovlStoneSlashPath);
        if (source == null)
        {
            Debug.LogWarning($"[Pelag VFX] Hovl slash не найден: {HovlStoneSlashPath}");
            return;
        }

        GameObject imported = UnityEngine.Object.Instantiate(source, root.transform, false);
        imported.name = "Stone Slash";
        imported.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        // The source prefab is authored as a hero-sized slash. The previous
        // 0.42 scale reduced its readable crescent to a handful of sparks at
        // gameplay zoom, leaving the procedural ribbon to carry the ability.
        imported.transform.localScale = Vector3.one;
        SanitizeImportedVfx(imported);
        ReplaceImportedParticleMaterials(imported);
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
            if (!sourcePath.StartsWith("Assets/Hovl Studio/Magic effects pack/Materials/",
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
        Material metalMaterial, float width, float lifetime, bool physicalChain)
    {
        GameObject root = RootObject(id, name, lifetime);
        root.GetComponent<PelagVfxElement>().DynamicLine = true;
        AddLine(root, material, width, true, false).positionCount = 0;
        if (physicalChain) AddChainLinks(root, metalMaterial);
        return Save(root, name);
    }

    private static GameObject SaveTrail(PelagVfxId id, string name, Material material,
        float width, float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        TrailRenderer trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material;
        trail.time = lifetime;
        trail.minVertexDistance = 0.025f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, width), new Keyframe(1f, 0f));
        trail.colorGradient = Gradient(new Color(1f, 0.91f, 0.78f, 0.82f),
            new Color(1f, 0.28f, 0.23f, 0f));
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        return Save(root, name);
    }

    private static GameObject SaveAnchor(PelagVfxId id, string name, Material trailMaterial,
        Material metalMaterial, float lifetime)
    {
        GameObject root = RootObject(id, name, lifetime);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPath);
        if (source != null)
        {
            GameObject anchor = (GameObject)PrefabUtility.InstantiatePrefab(source);
            anchor.name = "Physical Anchor";
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localScale = Vector3.one * 0.46f;
            anchor.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
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

        TrailRenderer trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMaterial;
        trail.time = 0.16f;
        trail.minVertexDistance = 0.03f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.10f), new Keyframe(1f, 0f));
        trail.colorGradient = Gradient(new Color(0.70f, 0.77f, 0.77f, 0.70f),
            new Color(0.10f, 0.13f, 0.14f, 0f));
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        return Save(root, name);
    }

    private static GameObject SaveBurst(PelagVfxId id, string name, Material material,
        int count, float lifetime, float speed, float size, float rootLifetime)
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
        return Save(root, name);
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
        const int Count = 28;
        GameObject host = new GameObject("Physical Chain Links");
        host.transform.SetParent(root.transform, false);
        PelagChainLinkStrip strip = host.AddComponent<PelagChainLinkStrip>();
        strip.Links = new Transform[Count];
        for (int i = 0; i < Count; i++)
        {
            GameObject link = new GameObject($"Link_{i:00}");
            link.transform.SetParent(host.transform, false);
            link.transform.localScale = Vector3.one * 0.92f;
            MeshFilter filter = link.AddComponent<MeshFilter>();
            filter.sharedMesh = _chainLinkMesh;
            MeshRenderer renderer = link.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            strip.Links[i] = link.transform;
        }
    }

    private static Mesh CreateChainLinkMesh()
    {
        const string path = Root + "/Geometry/Pelag_ChainLink.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int RingSegments = 14;
        const int TubeSegments = 6;
        const float MajorX = 0.070f;
        const float MajorY = 0.045f;
        const float Tube = 0.012f;
        var vertices = new Vector3[RingSegments * TubeSegments];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[RingSegments * TubeSegments * 6];

        for (int r = 0; r < RingSegments; r++)
        {
            float a = r * Mathf.PI * 2f / RingSegments;
            Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 center = new Vector3(radial.x * MajorX, radial.y * MajorY, 0f);
            for (int t = 0; t < TubeSegments; t++)
            {
                float b = t * Mathf.PI * 2f / TubeSegments;
                Vector3 normal = (radial * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)).normalized;
                int index = r * TubeSegments + t;
                vertices[index] = center + normal * Tube;
                normals[index] = normal;
                uv[index] = new Vector2(r / (float)RingSegments, t / (float)TubeSegments);
            }
        }

        int triangle = 0;
        for (int r = 0; r < RingSegments; r++)
        {
            int nextR = (r + 1) % RingSegments;
            for (int t = 0; t < TubeSegments; t++)
            {
                int nextT = (t + 1) % TubeSegments;
                int a = r * TubeSegments + t;
                int b = nextR * TubeSegments + t;
                int c = nextR * TubeSegments + nextT;
                int d = r * TubeSegments + nextT;
                triangles[triangle++] = a; triangles[triangle++] = b; triangles[triangle++] = c;
                triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "Pelag_ChainLink" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static GameObject Save(GameObject root, string name)
    {
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
            case PelagVfxId.AnchorSweepEnemyPull: return 10;
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
