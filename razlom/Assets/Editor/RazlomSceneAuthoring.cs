using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает только авторскую основу SampleScene. Runtime-сущности и комнаты
/// Разлома сюда намеренно не попадают: ими по-прежнему владеет симуляция.
/// </summary>
public static class RazlomSceneAuthoring
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string LookProfilePath = "Assets/Settings/CombatLook.asset";
    private const string MaterialFolder = "Assets/Art/Environment/CampBlockout/Materials";

    [MenuItem("Tools/Lost Veleor/Reset Sample Scene Blockout")]
    public static void ResetSampleSceneBlockout()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Reset Sample Scene Blockout",
            "This deletes Environment and Authored World, including manual scene edits, then rebuilds the original blockout.",
            "Reset Blockout",
            "Cancel");
        if (!confirmed) return;

        Rebuild(EditorSceneManager.GetActiveScene(), true);
    }

    [MenuItem("Tools/Lost Veleor/Validate Sample Scene")]
    public static void ValidateSampleScene()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        Bootstrap bootstrap = Object.FindAnyObjectByType<Bootstrap>(FindObjectsInactive.Include);
        SceneWorldView world = Object.FindAnyObjectByType<SceneWorldView>(FindObjectsInactive.Include);
        Camera camera = FindSceneObject<Camera>(scene, "Environment/Gameplay Camera");
        Volume volume = FindSceneObject<Volume>(scene, "Environment/Global Volume");
        Light key = FindSceneObject<Light>(scene, "Environment/Lighting/Key Light");
        Light rim = FindSceneObject<Light>(scene, "Environment/Lighting/Rim Light");
        Light fill = FindSceneObject<Light>(scene, "Environment/Lighting/Fill Light");

        bool bootstrapWired = false;
        if (bootstrap != null)
        {
            SerializedObject serialized = new SerializedObject(bootstrap);
            bootstrapWired = serialized.FindProperty("_gameplayCamera").objectReferenceValue == camera
                             && serialized.FindProperty("_sceneWorld").objectReferenceValue == world;
        }

        UniversalAdditionalCameraData cameraData =
            camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;

        bool valid = scene.path == ScenePath
                     && bootstrapWired
                     && world != null && world.ValidateContract(false)
                     && camera != null && camera.CompareTag("MainCamera")
                     && camera.GetComponent<CameraFollow>() != null
                     && cameraData != null && cameraData.renderPostProcessing
                     && volume != null && volume.isGlobal && volume.sharedProfile != null
                     && key != null && rim != null && fill != null;

        Debug.Log(valid
            ? "[Разлом] Контракт SampleScene собран: camera/look/camp/proving ground назначены."
            : "[Разлом] Контракт SampleScene неполон. Проверь Inspector или сбрось сцену через Reset Sample Scene Blockout.");
    }

    private static void Rebuild(Scene scene, bool allowOpen)
    {
        if (scene.path != ScenePath)
        {
            if (!allowOpen)
            {
                Debug.LogError("[Разлом] Для миграции должна быть открыта SampleScene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Bootstrap bootstrap = Object.FindAnyObjectByType<Bootstrap>(FindObjectsInactive.Include);
        if (bootstrap == null)
        {
            GameObject bootstrapObject = new GameObject("Bootstrap");
            bootstrap = bootstrapObject.AddComponent<Bootstrap>();
        }

        bootstrap.gameObject.name = "Bootstrap";
        ResetTransform(bootstrap.transform);

        DestroyRoot(scene, "Directional Light");
        DestroyRoot(scene, "Global Volume");
        DestroyRoot(scene, "Environment");
        DestroyRoot(scene, "Authored World");

        VolumeProfile lookProfile = CreateOrUpdateLookProfile();
        GameObject environment = NewRoot("Environment");
        Camera camera = BuildCamera(environment.transform);
        BuildLighting(environment.transform);
        BuildVolume(environment.transform, lookProfile);

        GameObject authoredWorld = NewRoot("Authored World");
        GameObject campRoot = NewChild("CampRoot", authoredWorld.transform);
        GameObject provingRoot = NewChild("ProvingGroundRoot", authoredWorld.transform);

        BuildCamp(campRoot.transform);
        BuildProvingGround(provingRoot.transform);
        provingRoot.SetActive(false);

        SceneWorldView world = authoredWorld.AddComponent<SceneWorldView>();
        SerializedObject worldObject = new SerializedObject(world);
        worldObject.FindProperty("_campRoot").objectReferenceValue = campRoot;
        worldObject.FindProperty("_provingGroundRoot").objectReferenceValue = provingRoot;
        worldObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject bootstrapObjectRef = new SerializedObject(bootstrap);
        bootstrapObjectRef.FindProperty("_gameplayCamera").objectReferenceValue = camera;
        bootstrapObjectRef.FindProperty("_sceneWorld").objectReferenceValue = world;
        bootstrapObjectRef.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(bootstrap);
        EditorUtility.SetDirty(world);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log("[Разлом] SampleScene пересобрана: авторские camera/look/camp/proving ground сохранены в сцене.");
    }

    private static Camera BuildCamera(Transform parent)
    {
        GameObject go = NewChild("Gameplay Camera", parent);
        go.tag = "MainCamera";
        go.transform.rotation = Quaternion.Euler(38f, 45f, 0f);
        go.transform.position = -go.transform.forward * 60f;

        Camera camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4.8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.46f, 0.70f, 0.80f, 1f);
        camera.allowHDR = true;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 180f;

        UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.stopNaN = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;

        go.AddComponent<AudioListener>();
        go.AddComponent<CombatCameraJuice>();
        CameraFollow follow = go.AddComponent<CameraFollow>();
        follow.Target = go.transform;
        return camera;
    }

    private static void BuildLighting(Transform parent)
    {
        GameObject lighting = NewChild("Lighting", parent);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 0.74f;
        RenderSettings.ambientSkyColor = new Color(0.68f, 0.79f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.55f, 0.59f);
        RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.25f);
        RenderSettings.reflectionIntensity = 0.82f;

        Light key = NewDirectional(lighting.transform, "Key Light", new Vector3(46f, -118f, 0f),
            new Color(1.00f, 0.92f, 0.78f), 1.72f, true);
        key.shadowStrength = 0.58f;
        key.shadowBias = 0.045f;
        key.shadowNormalBias = 0.20f;
        RenderSettings.sun = key;

        NewDirectional(lighting.transform, "Rim Light", new Vector3(42f, 62f, 0f),
            new Color(0.52f, 0.78f, 1.00f), 0.42f, false);
        NewDirectional(lighting.transform, "Fill Light", new Vector3(58f, -18f, 0f),
            new Color(1.00f, 0.82f, 0.62f), 0.24f, false);
    }

    private static Light NewDirectional(Transform parent, string name, Vector3 angles, Color color,
        float intensity, bool shadows)
    {
        GameObject go = NewChild(name, parent);
        go.transform.rotation = Quaternion.Euler(angles);
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        return light;
    }

    private static void BuildVolume(Transform parent, VolumeProfile profile)
    {
        GameObject go = NewChild("Global Volume", parent);
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;
    }

    private static VolumeProfile CreateOrUpdateLookProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(LookProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CombatLook";
            AssetDatabase.CreateAsset(profile, LookProfilePath);
        }

        profile.components.RemoveAll(component => component == null);

        Bloom bloom = GetOrAddVolumeComponent<Bloom>(profile);
        bloom.active = true;
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.36f);
        bloom.scatter.Override(0.54f);
        bloom.highQualityFiltering.Override(true);

        Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.Neutral);

        ColorAdjustments color = GetOrAddVolumeComponent<ColorAdjustments>(profile);
        color.active = true;
        color.postExposure.Override(0.30f);
        color.contrast.Override(8f);
        color.saturation.Override(16f);
        color.colorFilter.Override(new Color(1.00f, 0.99f, 0.96f, 1f));

        Vignette vignette = GetOrAddVolumeComponent<Vignette>(profile);
        vignette.active = true;
        vignette.color.Override(new Color(0.12f, 0.25f, 0.32f, 1f));
        vignette.intensity.Override(0.075f);
        vignette.smoothness.Override(0.58f);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component)) component = profile.Add<T>(true);

        if (!AssetDatabase.Contains(component))
        {
            component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        return component;
    }

    private static void BuildCamp(Transform root)
    {
        EnsureFolder(MaterialFolder);
        Material ground = Material("Camp Ground", new Color(0.25f, 0.38f, 0.24f));
        Material path = Material("Camp Path", new Color(0.47f, 0.43f, 0.34f));
        Material stone = Material("Camp Stone", new Color(0.28f, 0.31f, 0.32f));
        Material wood = Material("Camp Wood", new Color(0.32f, 0.20f, 0.13f));
        Material canvas = Material("Camp Canvas", new Color(0.64f, 0.20f, 0.17f));
        Material ember = Material("Camp Ember", new Color(0.82f, 0.18f, 0.035f),
            new Color(3.8f, 0.55f, 0.05f));

        Primitive(root, "Ground Clearing", PrimitiveType.Cylinder,
            new Vector3(0f, -0.10f, 0f), new Vector3(8f, 0.10f, 8f), ground);
        Primitive(root, "Main Path", PrimitiveType.Cube,
            new Vector3(0f, 0.01f, -2.5f), new Vector3(2.2f, 0.08f, 10f), path);

        GameObject fire = NewChild("Campfire", root);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 0.25f;
            Primitive(fire.transform, $"Stone {i + 1}", PrimitiveType.Cube,
                new Vector3(Mathf.Cos(angle) * 0.72f, 0.14f, Mathf.Sin(angle) * 0.72f),
                new Vector3(0.36f, 0.22f, 0.30f), stone,
                Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f));
        }
        Primitive(fire.transform, "Embers", PrimitiveType.Cylinder,
            new Vector3(0f, 0.08f, 0f), new Vector3(0.55f, 0.06f, 0.55f), ember);
        Primitive(fire.transform, "Log A", PrimitiveType.Cylinder,
            new Vector3(0f, 0.28f, 0f), new Vector3(0.14f, 0.70f, 0.14f), wood,
            Quaternion.Euler(0f, 0f, 68f));
        Primitive(fire.transform, "Log B", PrimitiveType.Cylinder,
            new Vector3(0f, 0.28f, 0f), new Vector3(0.14f, 0.70f, 0.14f), wood,
            Quaternion.Euler(68f, 0f, 0f));

        Light fireLight = NewChild("Fire Light", fire.transform).AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.32f, 0.08f);
        fireLight.intensity = 2.4f;
        fireLight.range = 5.5f;
        fireLight.shadows = LightShadows.None;
        fireLight.transform.position = new Vector3(0f, 1.1f, 0f);

        BuildTent(root, "Tent - Player", new Vector3(-4.5f, 0f, 2.6f), 18f, canvas, wood);
        BuildTent(root, "Tent - Trader", new Vector3(4.7f, 0f, 2.3f), -22f, canvas, wood);
        BuildServiceStall(root, "Smith Shelter", new Vector3(4.6f, 0f, -3.3f), wood, stone);
        BuildServiceStall(root, "Trader Shelter", new Vector3(-4.8f, 0f, -3.1f), wood, canvas);
        BuildPortalArch(root, new Vector3(0f, 0f, 5.7f), stone, ember);

        NewAnchor(root, "Anchor - Player", new Vector3(0f, 0f, -1.8f));
        NewAnchor(root, "Anchor - Smith", new Vector3(4.2f, 0f, -2.7f));
        NewAnchor(root, "Anchor - Trader", new Vector3(-4.2f, 0f, -2.6f));
        NewAnchor(root, "Anchor - Rift Portal", new Vector3(0f, 0f, 5.2f));
    }

    private static void BuildProvingGround(Transform root)
    {
        EnsureFolder(MaterialFolder);
        Material sand = Material("Proving Sand", new Color(0.48f, 0.39f, 0.25f));
        Material border = Material("Proving Border", new Color(0.20f, 0.23f, 0.24f));
        Material player = Material("Proving Player Marker", new Color(0.18f, 0.55f, 0.54f));
        Material dummy = Material("Proving Dummy Marker", new Color(0.72f, 0.22f, 0.18f));

        Primitive(root, "Training Floor", PrimitiveType.Cube,
            new Vector3(0f, -0.10f, 0f), new Vector3(13f, 0.20f, 9f), sand);
        Primitive(root, "Rail North", PrimitiveType.Cube,
            new Vector3(0f, 0.45f, 4.45f), new Vector3(13.4f, 0.9f, 0.25f), border);
        Primitive(root, "Rail South", PrimitiveType.Cube,
            new Vector3(0f, 0.45f, -4.45f), new Vector3(13.4f, 0.9f, 0.25f), border);
        Primitive(root, "Rail East", PrimitiveType.Cube,
            new Vector3(6.55f, 0.45f, 0f), new Vector3(0.25f, 0.9f, 9f), border);
        Primitive(root, "Rail West", PrimitiveType.Cube,
            new Vector3(-6.55f, 0.45f, 0f), new Vector3(0.25f, 0.9f, 9f), border);

        Marker(root, "Player Start", Vector3.zero, player);
        Marker(root, "Damage Dummy Start", new Vector3(1.5f, 0f, 0f), dummy);
        Marker(root, "Sparring Dummy Start", new Vector3(-1.5f, 0f, 0f), dummy);

        NewAnchor(root, "Anchor - Player", Vector3.zero);
        NewAnchor(root, "Anchor - Damage Dummy", new Vector3(1.5f, 0f, 0f));
        NewAnchor(root, "Anchor - Sparring Dummy", new Vector3(-1.5f, 0f, 0f));
    }

    private static void BuildTent(Transform parent, string name, Vector3 position, float yaw,
        Material canvas, Material wood)
    {
        GameObject tent = NewChild(name, parent);
        tent.transform.position = position;
        tent.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Primitive(tent.transform, "Floor", PrimitiveType.Cube,
            new Vector3(0f, 0.08f, 0f), new Vector3(3.2f, 0.16f, 2.6f), wood);
        Primitive(tent.transform, "Canvas", PrimitiveType.Cube,
            new Vector3(0f, 1.15f, 0.2f), new Vector3(3.0f, 2.1f, 2.3f), canvas,
            Quaternion.Euler(0f, 0f, 4f));
        Primitive(tent.transform, "Ridge", PrimitiveType.Cylinder,
            new Vector3(0f, 2.25f, 0.2f), new Vector3(0.08f, 1.65f, 0.08f), wood,
            Quaternion.Euler(0f, 0f, 90f));
    }

    private static void BuildServiceStall(Transform parent, string name, Vector3 position,
        Material wood, Material accent)
    {
        GameObject stall = NewChild(name, parent);
        stall.transform.position = position;

        Primitive(stall.transform, "Deck", PrimitiveType.Cube,
            new Vector3(0f, 0.10f, 0f), new Vector3(3.2f, 0.20f, 2.4f), wood);
        Primitive(stall.transform, "Post Left", PrimitiveType.Cube,
            new Vector3(-1.35f, 1.25f, 0f), new Vector3(0.18f, 2.5f, 0.18f), wood);
        Primitive(stall.transform, "Post Right", PrimitiveType.Cube,
            new Vector3(1.35f, 1.25f, 0f), new Vector3(0.18f, 2.5f, 0.18f), wood);
        Primitive(stall.transform, "Canopy", PrimitiveType.Cube,
            new Vector3(0f, 2.45f, 0f), new Vector3(3.4f, 0.18f, 2.7f), accent,
            Quaternion.Euler(0f, 0f, 4f));
        Primitive(stall.transform, "Counter", PrimitiveType.Cube,
            new Vector3(0f, 0.72f, -0.75f), new Vector3(2.8f, 0.85f, 0.55f), wood);
    }

    private static void BuildPortalArch(Transform parent, Vector3 position, Material stone, Material glow)
    {
        GameObject portal = NewChild("Rift Portal", parent);
        portal.transform.position = position;

        Primitive(portal.transform, "Pillar Left", PrimitiveType.Cube,
            new Vector3(-1.25f, 1.6f, 0f), new Vector3(0.55f, 3.2f, 0.75f), stone);
        Primitive(portal.transform, "Pillar Right", PrimitiveType.Cube,
            new Vector3(1.25f, 1.6f, 0f), new Vector3(0.55f, 3.2f, 0.75f), stone);
        Primitive(portal.transform, "Lintel", PrimitiveType.Cube,
            new Vector3(0f, 3.25f, 0f), new Vector3(3.05f, 0.55f, 0.75f), stone);
        Primitive(portal.transform, "Portal Glow", PrimitiveType.Cube,
            new Vector3(0f, 1.65f, 0.15f), new Vector3(1.85f, 2.65f, 0.08f), glow);
    }

    private static void Marker(Transform parent, string name, Vector3 position, Material material)
    {
        Primitive(parent, name, PrimitiveType.Cylinder,
            position + new Vector3(0f, 0.025f, 0f), new Vector3(0.52f, 0.025f, 0.52f), material);
    }

    private static void NewAnchor(Transform parent, string name, Vector3 position)
    {
        GameObject anchor = NewChild(name, parent);
        anchor.transform.localPosition = position;
    }

    private static GameObject Primitive(Transform parent, string name, PrimitiveType type,
        Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        return go;
    }

    private static Material Material(string name, Color color, Color? emission = null)
    {
        string path = $"{MaterialFolder}/{name.Replace(' ', '_')}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static GameObject NewRoot(string name)
    {
        GameObject go = new GameObject(name);
        ResetTransform(go.transform);
        return go;
    }

    private static GameObject NewChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void ResetTransform(Transform transform)
    {
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private static void DestroyRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) Object.DestroyImmediate(root);
        }
    }

    private static T FindSceneObject<T>(Scene scene, string path) where T : Component
    {
        string[] parts = path.Split('/');
        Transform current = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == parts[0])
            {
                current = root.transform;
                break;
            }
        }

        for (int i = 1; current != null && i < parts.Length; i++) current = current.Find(parts[i]);
        return current != null ? current.GetComponent<T>() : null;
    }
}
