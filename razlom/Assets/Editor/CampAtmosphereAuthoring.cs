using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Один раз размещает редактируемые объекты атмосферы в SampleScene.</summary>
public static class CampAtmosphereAuthoring
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string MaterialFolder = "Assets/Resources/Environment/Camp/Atmosphere";
    const string RootName = "Атмосфера лагеря — мост и алхимик";

    [MenuItem("Разлом/Лагерь/Разместить атмосферу в SampleScene")]
    public static void PlaceInOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Выйдите из Play перед размещением атмосферы.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Откройте SampleScene.");
        if (FindRoot() != null)
        {
            Debug.Log("[camp-atmosphere] Объекты уже есть в SampleScene; существующую расстановку не меняем.");
            return;
        }

        var look = Object.FindAnyObjectByType<CampLookController>(FindObjectsInactive.Include);
        var passage = Object.FindAnyObjectByType<CampRiverPassage>(FindObjectsInactive.Include);
        var river = Object.FindAnyObjectByType<CampRiver>(FindObjectsInactive.Include);
        if (look == null || passage == null || river == null || passage.Bridge == null)
            throw new InvalidOperationException("Не найдены свет лагеря, река или мост.");

        Material smoke = Resources.Load<Material>("Environment/Camp/Unified/Chimney smoke");
        if (smoke == null)
            throw new InvalidOperationException("Не найден материал дыма.");

        Material mist = EnsureMaterial("Дымка над рекой.mat", smoke, 1.45f);
        Material vapor = EnsureMaterial("Пар перегонки.mat", smoke, 3f);

        var root = NewChild(RootName, look.transform);
        var details = root.AddComponent<CampAtmosphereDetails>();
        var bridge = NewChild("Мост — дымка", root.transform);
        var alchemy = NewChild("Алхимик — перегонка", root.transform);

        Bounds deck = passage.BridgeBounds;
        float water = river.transform.TransformPoint(new Vector3(0f, river.WaterLevel, 0f)).y;
        details.RiverMistLeft = RiverMist("Дымка слева от настила", bridge.transform,
            new Vector3(deck.center.x - 2.7f, water + .23f, deck.center.z), mist);
        details.RiverMistRight = RiverMist("Дымка справа от настила", bridge.transform,
            new Vector3(deck.center.x + 2.7f, water + .23f, deck.center.z), mist);

        Transform apparatus = null;
        Transform shelter = look.transform.root.Find("Alchemist Shelter");
        if (shelter == null)
            foreach (var candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.name == "Alchemist Shelter") { shelter = candidate; break; }
        if (shelter != null)
            foreach (var candidate in shelter.GetComponentsInChildren<Transform>(true))
                if (candidate.name.StartsWith("steampunk+alchemy+apparatus", StringComparison.Ordinal))
                { apparatus = candidate; break; }
        if (apparatus == null) throw new InvalidOperationException("Не найден аппарат алхимика.");
        Bounds apparatusBounds = BoundsOf(apparatus.GetComponentsInChildren<Renderer>(true));
        Vector3 mouth = new Vector3(apparatusBounds.center.x + .14f, apparatusBounds.max.y - .42f,
            apparatusBounds.center.z);
        details.AlchemyVapor = Vapor("Пар над перегонным аппаратом", alchemy.transform, mouth, vapor);
        var greenLight = NewChild("Зелёный отсвет колбы", alchemy.transform);
        greenLight.transform.position = new Vector3(apparatusBounds.center.x + .55f,
            apparatusBounds.center.y + .38f, apparatusBounds.center.z);
        details.AlchemyLight = PointLight(greenLight, new Color(.35f, .92f, .46f), 1.95f, 3.2f);

        look.AtmosphereDetails = details;
        EditorUtility.SetDirty(look);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[camp-atmosphere] Дымка и перегонка сохранены в SampleScene.");
    }

    public static void BuildInBatch()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/artifacts/"))
            throw new InvalidOperationException("Пакетная правка разрешена только в теневом проекте.");
        EditorSceneManager.OpenScene(ScenePath);
        PlaceInOpenScene();
    }

    static Transform FindRoot()
    {
        foreach (var candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.name == RootName) return candidate;
        return null;
    }

    static GameObject NewChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static Bounds BoundsOf(Renderer[] renderers)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("У объекта нет Renderer.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Material EnsureMaterial(string file, Material source, float density)
    {
        EnsureFolder();
        string path = MaterialFolder + "/" + file;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source) { name = Path.GetFileNameWithoutExtension(file) };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat("_Density", density);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Resources/Environment/Camp", "Atmosphere");
    }

    static Light PointLight(GameObject go, Color color, float intensity, float range)
    {
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }

    static ParticleSystem RiverMist(string name, Transform parent, Vector3 position, Material material)
    {
        var particles = Particles(name, parent, position, material, new Color(.71f, .87f, .86f, .24f),
            18, 3f, 5f, new Vector2(2.8f, 3.7f), new Vector3(.055f, .012f, -.015f));
        var main = particles.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(2.8f, 3.7f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(.75f, 1.1f);
        main.startSizeZ = 1f;
        particles.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(2.3f, .04f, 1.7f);
        return particles;
    }

    static ParticleSystem Vapor(string name, Transform parent, Vector3 position, Material material) =>
        Particles(name, parent, position, material, new Color(.75f, .91f, .72f, .62f),
            20, 1.1f, 3.2f, new Vector2(.58f, .88f), new Vector3(.075f, .31f, .025f));

    static ParticleSystem Particles(string name, Transform parent, Vector3 position, Material material,
        Color tint, int limit, float rate, float life, Vector2 size, Vector3 drift)
    {
        var go = NewChild(name, parent);
        go.transform.position = position;
        var particles = go.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = limit;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .75f, life * 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startSpeed = 0f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = tint;
        var emission = particles.emission;
        emission.rateOverTime = rate;
        var shape = particles.shape;
        shape.enabled = false;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = drift.x;
        velocity.y = drift.y;
        velocity.z = drift.z;
        var sizeOverLife = particles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.EaseInOut(0f, .6f, 1f, 1.7f));
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .25f),
                new GradientAlphaKey(.6f, .65f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }
}
