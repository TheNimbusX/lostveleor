using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы Девятого вала (см. PelagWreckComboVfxSetup.Ninth). Выпад: корень на земле в точке удара, +Z — ход полосы;
/// все системы без залпа (куски выпускает контроллер на шагах фронта Sim), ленты гребней — меш контроллера. Заряд и
/// свечение — у головы якоря, масштаб корня — размер (контроллер).
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    // Цвета частиц — вершинные (шейдер Bit их не переводит): сразу в линейном пространстве.
    internal static readonly Color NinthIndigo = new Color(.075f, .05f, .64f, 1f);
    internal static readonly Color NinthViolet = new Color(.30f, .25f, 1.0f, 1f);
    internal static readonly Color NinthPale = new Color(.72f, .68f, 1.15f, 1f);
    internal static readonly Color NinthRockDark = new Color(.40f, .36f, .38f, 1f);
    internal static readonly Color NinthRockLight = new Color(.58f, .53f, .54f, 1f);

    internal const float NinthLungeLife = 2.0f, NinthChargeLife = .5f, NinthGlowLife = 30f;

    private static void SaveNinthLungePrefab(NinthMaterials m)
    {
        var root = new GameObject(NinthLungeName);
        try
        {
            AddElement(root, PelagVfxId.WreckNinthLunge, NinthLungeLife);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);

            // Веер у точки удара: стоячая полузвезда по ширине полосы (размер — контроллер), хлопок и порог.
            ParticleSystem fan = Flash(root, "Fan", m.Half, .36f, 0f, 3f, 1.5f, NinthViolet, false, .40f, 1.16f, .08f);
            fan.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            fan.GetComponent<ParticleSystemRenderer>().sortingFudge = 6f;
            NinthLane(fan, 4);
            // По земле: шипы-звёзды по ширине вала и светящиеся трещины (поворот и размер — контроллер).
            ParticleSystem stars = Flash(root, "GroundStars", m.GroundStar, .46f, 0f, 1f, 1f, NinthIndigo, false, .55f, 1.08f, .06f);
            stars.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            stars.GetComponent<ParticleSystemRenderer>().sortingFudge = 30f;
            NinthLane(stars, 40);
            ParticleSystem cracks = Flash(root, "Cracks", m.Crack, .55f, 0f, 1f, 1f, NinthViolet, false, .70f, 1.04f, .06f);
            cracks.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            cracks.GetComponent<ParticleSystemRenderer>().sortingFudge = 28f;
            NinthLane(cracks, 40);
            // Искры с краёв вала и крупные камни.
            ParticleSystem sparks = Sparks(root, "Sparks", m.Spark, 140, .14f, .28f, 4f, 10f, .035f, .065f, .9f, .5f);
            NinthLane(sparks, 140);
            ParticleSystem rocks = NinthRocks(root, "Rocks", m, ground.transform, 48);
            NinthLane(rocks, 48);

            // Индиговый свет у фронта (камням и мобам; пол арены доп. свет не берёт — земле светят звёзды и трещины).
            var lightHost = new GameObject("Light");
            lightHost.transform.SetParent(root.transform, false);
            lightHost.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.color = new Color(.42f, .36f, 1f, 1f);
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            // Вал по земле (меш — контроллер) — под всем; ленты: дальний край, второе эхо, эхо, центр, ближний край.
            CrestRenderer(root, "Wave", m.Ground, -1);
            CrestRenderer(root, "CrestFar", m.Flank, 0);
            CrestRenderer(root, "CrestEcho2", m.Echo2, 1);
            CrestRenderer(root, "CrestEcho", m.Echo, 2);
            CrestRenderer(root, "Crest", m.Crest, 3);
            CrestRenderer(root, "CrestNear", m.Flank, 4);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(NinthLungeName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Заряд на махе: вспышка-звезда, кольцо с лучами (растёт), искры веером; корень — голова якоря.</summary>
    private static void SaveNinthChargePrefab(NinthMaterials m)
    {
        var root = new GameObject(NinthChargeName);
        try
        {
            AddElement(root, PelagVfxId.WreckNinthCharge, NinthChargeLife);
            ParticleSystem flare = Flash(root, "Flare", m.Flare, .24f, 0f, 1f, 1f, NinthViolet, true, .45f, 1.18f, .05f);
            flare.GetComponent<ParticleSystemRenderer>().sortingFudge = -12f;
            ParticleSystem ring = Flash(root, "Ring", m.Ring, .26f, .02f, 1.7f, 1.7f, NinthViolet, true, .30f, .80f, .26f);
            ring.GetComponent<ParticleSystemRenderer>().sortingFudge = -6f;
            ParticleSystem sparks = Sparks(root, "Sparks", m.Spark, 12, .12f, .24f, 3f, 7.5f, .03f, .055f, .6f, 0f);
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .15f;
            var main = sparks.main;
            main.startColor = new ParticleSystem.MinMaxGradient(NinthPale, NinthViolet);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(NinthChargeName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Свечение головы: два ореола с лучами (внутренний ярче, вертятся навстречу) позади головы, искорки за ней.</summary>
    private static void SaveNinthGlowPrefab(NinthMaterials m)
    {
        var root = new GameObject(NinthGlowName);
        try
        {
            AddElement(root, PelagVfxId.WreckNinthGlow, NinthGlowLife);
            ParticleSystem outer = Flash(root, "AuraOut", m.AuraOut, NinthGlowLife, 0f, 1.05f, 1.05f, new Color(.16f, .12f, .86f, 1f), true, .5f, 1.12f, .07f);
            ParticleSystem inner = Flash(root, "AuraIn", m.AuraIn, NinthGlowLife, 0f, .62f, .62f, NinthViolet, true, .5f, 1.15f, .06f);
            outer.GetComponent<ParticleSystemRenderer>().sortingFudge = 8f;
            inner.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;
            foreach (var (layer, speed) in new[] { (outer, 40f), (inner, -75f) })
            {
                var spin = layer.rotationOverLifetime; spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(speed * Mathf.Deg2Rad);
            }
            ParticleSystem motes = Sparks(root, "Motes", m.Spark, 90, .18f, .34f, 0f, 0f, .025f, .045f, -.15f, 0f);
            NinthLane(motes, 90);
            var main = motes.main; main.duration = 8f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(NinthGlowName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Система по фронту: без залпа, мир, долгий срок (контроллер выпускает куски сам).</summary>
    private static void NinthLane(ParticleSystem p, int max)
    {
        Lane(p);
        var main = p.main;
        main.maxParticles = Mathf.Max(1, max);
        main.duration = Mathf.Max(main.duration, 3f);
        main.startDelay = 0f;
    }

    /// <summary>Крупные камни: меши «Standard Rock» на освещённом материале, без GPU-инстансинга (иначе 2–3 из десятков).</summary>
    private static ParticleSystem NinthRocks(GameObject root, string name, NinthMaterials m, Transform ground, int count)
    {
        var lunge = new LungeMaterials { Rock = m.Rock, Rocks = m.Rocks };
        ParticleSystem p = Rocks(root, name, lunge, ground, count, 0f, 0f, .5f, .8f);
        var main = p.main;
        main.startColor = new ParticleSystem.MinMaxGradient(NinthRockDark, NinthRockLight);
        main.gravityModifier = 2.4f;
        var renderer = p.GetComponent<ParticleSystemRenderer>();
        renderer.enableGPUInstancing = false;
        return p;
    }

    /// <summary>
    /// Камни пака «Standard Rock» (LOD3, 315–505 вершин) копиями в свои меши: центр — в начало координат, наибольшая
    /// ось — 1 м (размер частицы = метры); все вершины — в одну клетку палитры пака (камень), объём даёт свет арены.
    /// </summary>
    private static Mesh[] NinthRockMeshes()
    {
        var list = new List<Mesh>();
        // Частицам — не больше четырёх мешей (SetMeshes режет пятый): глыбы 1, 2, 4, 5; плоский валун 3 не берём.
        foreach (int i in new[] { 1, 2, 4, 5 })
        {
            Mesh source = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(NinthRockModels + i + ".fbx"))
                if (asset is Mesh mesh && mesh.name.EndsWith("_LOD3")) source = mesh;
            if (source == null) continue;
            Mesh copy = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/WreckNinthRock" + i + ".asset", "WreckNinthRock" + i);
            Vector3[] vertices = source.vertices;
            Bounds b = source.bounds;
            float k = 1f / Mathf.Max(.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            for (int v = 0; v < vertices.Length; v++) vertices[v] = (vertices[v] - b.center) * k;
            copy.Clear();
            copy.vertices = vertices;
            copy.normals = source.normals;
            var uv = new Vector2[vertices.Length];
            for (int v = 0; v < uv.Length; v++) uv[v] = new Vector2(.3125f, .3955f);
            copy.uv = uv;
            copy.triangles = source.triangles;
            copy.RecalculateBounds();
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            list.Add(copy);
        }
        return list.ToArray();
    }
}
