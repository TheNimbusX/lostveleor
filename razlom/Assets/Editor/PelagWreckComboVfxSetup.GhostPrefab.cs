using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы Призрачного якоря (см. PelagWreckComboVfxSetup.Ghost). Корень — на земле в точке выпада; у удара местная +Z —
/// ход выпада, размеры — под круг Sim 3 м (контроллер масштабирует корень по радиусу события 77).
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    // Цвета частиц — вершинные (шейдер Bit их не переводит): сразу в линейном пространстве.
    internal static readonly Color GhostViolet = new Color(.27f, .20f, 1.0f, 1f);
    internal static readonly Color GhostDeep = new Color(.10f, .065f, .69f, 1f);
    // Холоднее, чем кажется нужным: тёплый грейд арены сдвигает белое в кремовое, фиолетовое — в пурпур.
    internal static readonly Color GhostPale = new Color(.62f, .60f, 1.08f, 1f);
    internal static readonly Color GhostWhite = new Color(.88f, .88f, 1.25f, 1f);
    internal static readonly Color GhostShardDark = new Color(.30f, .24f, .80f, 1f);
    internal static readonly Color GhostShardLight = new Color(.62f, .56f, 1.0f, 1f);
    // Камни — тёмные (сланец), объём от солнца арены и фиолетового света удара.
    internal static readonly Color GhostRockDark = new Color(.30f, .28f, .34f, 1f);
    internal static readonly Color GhostRockLight = new Color(.46f, .43f, .50f, 1f);

    /// <summary>Радиус круга Sim, под который собран удар (WreckGhostRadius).</summary>
    internal const float GhostAuthoredRadius = PelagWreckGhostLook.AuthoredRadius;

    /// <summary>
    /// Падающий якорь: «Tilt» (контроллер: высота падения, наклон к камере, сплющивание) → «Body» (тело), «GlowIn» и
    /// «GlowOut» (ореол) — один меш якоря с цепью высотой AnchorHeight; следы падения, осколки и искорки распада — системы
    /// мира без залпа.
    /// </summary>
    private static void SaveGhostAnchorPrefab(GhostMaterials g)
    {
        var root = new GameObject(GhostAnchorName);
        try
        {
            AddElement(root, PelagVfxId.WreckGhostAnchor, PelagWreckGhostLook.FallSeconds + PelagWreckGhostLook.AfterLandSeconds + .8f);
            var tilt = new GameObject("Tilt");
            tilt.transform.SetParent(root.transform, false);
            GhostMeshChild(tilt, "Body", g.AnchorMesh, g.Body, 0);
            GhostMeshChild(tilt, "GlowIn", g.AnchorMesh, g.GlowIn, 1);
            GhostMeshChild(tilt, "GlowOut", g.AnchorMesh, g.GlowOut, 2);
            // Следы падения: тонкие черты за лапами и кольцом, белые и фиолетовые, короткие.
            ParticleSystem streaks = Sparks(root, "Streaks", g.Spark, 90, .08f, .14f, 0f, 0f, .05f, .08f, 0f, 0f);
            Lane(streaks);
            var streakRenderer = streaks.GetComponent<ParticleSystemRenderer>();
            streakRenderer.lengthScale = 6f;
            streakRenderer.velocityScale = .03f;
            // Распад вверх: осколки-кристаллы с вращением и искорки, всплывают.
            Lane(Chips(root, "Shards", g.Shard, 70, .40f, .65f, 0f, 0f, .12f, .24f, -.35f, 0f, GhostShardDark, GhostShardLight));
            ParticleSystem motes = Sparks(root, "Motes", g.Spark, 90, .25f, .45f, 0f, 0f, .035f, .06f, -.25f, 0f);
            Lane(motes);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(GhostAnchorName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Удар якоря оземь: кольцо и эхо, стоячий всплеск и лучи, по земле — звезда, трещины, кратер; камни и свет.</summary>
    private static void SaveGhostLandPrefab(GhostMaterials g)
    {
        var root = new GameObject(GhostLandName);
        try
        {
            AddElement(root, PelagVfxId.WreckGhostLand, 1.8f);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            float r = GhostAuthoredRadius;

            // ---- КОЛЬЦО до круга Sim (диск: линия — ровно радиус) и эхо чуть внутри, позже и тоньше.
            ParticleSystem ring = GhostRing(root, "Ring", g.Disc, g.Ring, .66f, 0f, r, Color.white);
            ring.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;
            ParticleSystem echo = GhostRing(root, "RingEcho", g.Disc, g.RingEcho, .50f, .05f, r * .93f, Color.white);
            echo.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;
            ring.transform.localPosition = new Vector3(0f, .05f, 0f);
            echo.transform.localPosition = new Vector3(0f, .04f, 0f);

            // ---- СТОЯЧИЙ ВСПЛЕСК у лап: полузвезда от земли (бледная — белым светится сам якорь), звезда; лучи вверх.
            // Без белого «блина» в кадр удара (раунд 1 в игре): всплеск ниже и короче, белым светится сам якорь.
            ParticleSystem half = Flash(root, "Half", g.Half, .26f, 0f, 3.4f, 1.7f, GhostPale, false, .40f, 1.16f, .06f);
            half.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            ParticleSystem star = Flash(root, "Star", g.Star, .16f, 0f, 1.5f, 1.5f, GhostPale, true, .50f, 1.12f, .05f);
            star.transform.localPosition = new Vector3(0f, .9f, 0f);
            star.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
            ParticleSystem rays = Sparks(root, "Rays", g.Spark, 30, .16f, .30f, 7f, 12f, .08f, .13f, 0f, 0f);
            var raysMain = rays.main; raysMain.startColor = new ParticleSystem.MinMaxGradient(GhostWhite, GhostPale);
            Cone(rays, 5f, 1.7f, -90f);
            var raysShape = rays.shape; raysShape.randomDirectionAmount = .02f;
            var raysRenderer = rays.GetComponent<ParticleSystemRenderer>();
            raysRenderer.lengthScale = 7f;
            raysRenderer.velocityScale = .02f;

            // ---- ПО ЗЕМЛЕ: ударная звезда, светящиеся трещины к кольцу, тёмный кратер.
            ParticleSystem groundStar = Flash(root, "GroundStar", g.Star, .26f, 0f, 3.4f, 3.4f, GhostPale, true, .35f, 1.08f, .07f);
            Horizontal(groundStar, .045f);
            ParticleSystem glow = Flash(root, "Glow", g.Crack, .90f, 0f, 7.4f, 7.4f, GhostPale, false, .70f, 1.04f, .06f);
            Horizontal(glow, .035f);
            ParticleSystem crater = Flash(root, "Crater", g.Crater, 1.6f, 0f, 6.6f, 6.6f, CraterInk, false, .75f, 1.02f, .05f);
            Horizontal(crater, .025f);
            var fade = crater.colorOverLifetime; fade.enabled = true;
            var fadeKeys = new Gradient();
            fadeKeys.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .65f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(fadeKeys);

            // ---- ОСКОЛКИ: искры веером, тёмные камни из кратера (крупные и мелкие), комья, сколы-кристаллы, пыль.
            ParticleSystem sparks = Sparks(root, "Sparks", g.Spark, 40, .14f, .32f, 5f, 13f, .04f, .075f, 1.0f, .5f);
            var sparkMain = sparks.main; sparkMain.startColor = new ParticleSystem.MinMaxGradient(GhostWhite, GhostViolet * 1.4f);
            Cone(sparks, 72f, .9f, -90f);
            var rockSet = new LungeMaterials { Rock = g.Rock, Rocks = g.Rocks };
            // Камни висят в воздухе дольше, чем у выпада (на целевом кадре — в полёте над кругом): круче вверх, тяжесть 1.
            ParticleSystem rocksBig = Rocks(root, "RocksBig", rockSet, ground.transform, PelagWreckGhostLook.RocksBig, 4.5f, 7f,
                PelagWreckGhostLook.RockBigMin, PelagWreckGhostLook.RockBigMax);
            Cone(rocksBig, 40f, .9f, -90f);
            ParticleSystem rocksSmall = Rocks(root, "RocksSmall", rockSet, ground.transform, PelagWreckGhostLook.RocksSmall, 5f, 9.5f,
                PelagWreckGhostLook.RockSmallMin, PelagWreckGhostLook.RockSmallMax);
            Cone(rocksSmall, 55f, 1.3f, -90f);
            foreach (var layer in new[] { rocksBig, rocksSmall })
            {
                var main = layer.main;
                main.startColor = new ParticleSystem.MinMaxGradient(GhostRockDark, GhostRockLight);
                main.gravityModifier = 1.0f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.25f, 1.55f);
                // GPU-инстансинг частиц-мешей на «Particles/Simple Lit» рисует из десятков камней 2–3 (проба 07.10).
                layer.GetComponent<ParticleSystemRenderer>().enableGPUInstancing = false;
            }
            ParticleSystem clods = Chips(root, "Clods", g.Clod, 14, .50f, .80f, 2.5f, 6f, .16f, .32f, 2.6f, 0f, Clod, ClodLight);
            Cone(clods, 64f, 1.1f, -90f);
            ParticleSystem shards = Chips(root, "Shards", g.Shard, 10, .45f, .75f, 3f, 6.5f, .16f, .30f, 2.2f, 0f, GhostShardDark, GhostShardLight);
            Cone(shards, 45f, .5f, -90f);
            ParticleSystem dust = Dust(root, "Dust", g.Dust, 5, .6f, 1.3f);
            Cone(dust, 85f, 1.4f, -10f);
            foreach (var layer in new[] { clods, shards }) layer.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
            foreach (var layer in new[] { sparks, rocksBig, rocksSmall, clods, dust }) layer.transform.localPosition = new Vector3(0f, .35f, 0f);
            shards.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            rays.transform.localPosition = new Vector3(0f, .1f, 0f);

            // ---- СВЕТ: фиолетовый точечный (контроллер: вспышка и спад) — камням, мобам и якорю пака; пол арены его
            // не берёт (свои шейдеры без дополнительных огней) — свет земли рисует кольцо (канал b маски).
            var lightHost = new GameObject("Light");
            lightHost.transform.SetParent(root.transform, false);
            lightHost.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var light = lightHost.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.62f, .52f, 1f, 1f);
            light.range = 7.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(GhostLandName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void GhostMeshChild(GameObject parent, string name, Mesh mesh, Material material, int order)
    {
        var host = new GameObject(name);
        host.transform.SetParent(parent.transform, false);
        host.transform.localScale = Vector3.one * PelagWreckGhostLook.AnchorHeight;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = order;
    }

    /// <summary>
    /// Кольцо: одна частица-меш (диск в XZ, масштаб = радиус, м), растёт до радиуса по PelagWreckGhostLook.RingFraction
    /// без перелёта; случайный поворот вокруг вертикали (шипы каждый раз на других местах); порог материала по возрасту.
    /// </summary>
    private static ParticleSystem GhostRing(GameObject root, string name, Mesh mesh, Material material, float life, float delay,
        float radius, Color color)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, 1, life, life, 0f, 0f, radius, radius);
        var main = p.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.duration = life + delay + .05f;
        main.startColor = color;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f);
        var shape = p.shape; shape.enabled = false;
        const int keys = 14;
        var frames = new Keyframe[keys];
        for (int i = 0; i < keys; i++)
        {
            float t = i / (keys - 1f);
            float seconds = t * Mathf.Min(life, PelagWreckGhostLook.RingExpandSeconds * 2.2f);
            frames[i] = new Keyframe(seconds / life, PelagWreckGhostLook.RingFraction(seconds));
        }
        var curve = new AnimationCurve(frames);
        curve.AddKey(new Keyframe(1f, 1f));
        for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        var size = p.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        var renderer = p.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
        return p;
    }
}
