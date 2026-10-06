using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Махи Крушения на стеке сабли: меш полумесяца, материалы, префабы и слои (см. PelagWreckComboVfxSetup).</summary>
public static partial class PelagWreckComboVfxSetup
{
    // ----------------------------------------------------------------- mesh

    /// <summary>
    /// Полумесяц из полосы пака под мах якоря. Исходник — полоса r .20–1.00 на 180° в XY, выпуклость к −X, u=0 у угла
    /// −90° (хвост), u=1 у +90° (голова), v=1 на внешней окружности. Углы сжимаются к <paramref name="spanDegrees"/>;
    /// толщина (1 − r) — до <paramref name="compress"/>: от острого хвоста растёт к гребню (u≈.6) и к голове почти не
    /// сходит (тупая тяжёлая голова держит ~60% толщины; скругляет её шейдер). Внешний край — окружность.
    /// </summary>
    private static Mesh WaveMesh(string name, Mesh source, float spanDegrees, float compress)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        float k = spanDegrees / 180f;
        const float peak = .62f, headKeep = .48f;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];
            float r = new Vector2(p.x, p.y).magnitude;
            float theta = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            if (theta < 0f) theta += 360f;
            float phi = (theta - 180f) * k;
            float u = Mathf.Clamp01(uv[i].x);
            float profile = u < peak
                ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * .5f * Mathf.Pow(u / peak, 1.25f))), .55f)
                : Mathf.Lerp(1f, headKeep, Mathf.SmoothStep(0f, 1f, (u - peak) / (1f - peak)));
            float radius = 1f - (1f - r) / .8f * compress * profile;
            float angle = (180f + phi) * Mathf.Deg2Rad;
            vertices[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, source.triangles);
        return mesh;
    }

    // -------------------------------------------------------------- materials

    /// <summary>Материал с нуля при сохранении ассета и GUID (как Fresh у сабли).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static Material WaveMaterial(Shader shader, string name, Texture2D facets, float life,
        float headFrom, float headSeconds, float erodeFrom, float erodeTo, float fadeFrom, float fadeTo)
    {
        Material m = Fresh(name, shader);
        m.SetTexture("_FacetTex", facets);
        m.SetColor("_Deep", Deep);
        m.SetColor("_Mid", Mid);
        m.SetColor("_Light", Light);
        m.SetColor("_Hot", Hot);
        m.SetColor("_HotShade", HotShade);
        m.SetColor("_Outline", Ink);
        m.SetVector("_FacetScale", new Vector4(1.4f, 2.2f, .9f, .8f));
        m.SetVector("_SliverScale", new Vector4(.55f, 4.5f, .80f, 1.6f));
        m.SetVector("_Bands", new Vector4(.24f, .56f, .74f, .10f));
        m.SetFloat("_EdgeRag", .12f);
        m.SetFloat("_EdgeTeeth", .14f);
        m.SetFloat("_TeethCount", 16f);
        m.SetFloat("_HeadHot", .12f);
        m.SetFloat("_HeadRound", .10f);
        m.SetFloat("_OutlinePx", 2.4f);
        m.SetFloat("_Aspect", 5f);
        m.SetFloat("_ErodeGain", 1.6f);
        m.SetFloat("_Streaks", .8f);
        m.SetFloat("_Sheen", .6f);
        m.SetFloat("_SheenWidth", .025f);
        m.SetFloat("_SheenSeconds", .16f);
        m.SetFloat("_Glow", .30f);
        m.SetFloat("_Opacity", 1f);
        m.SetFloat("_LifeSeconds", life);
        m.SetFloat("_HeadFrom", headFrom);
        m.SetFloat("_HeadSeconds", headSeconds);
        m.SetFloat("_ErodeFrom", erodeFrom);
        m.SetFloat("_ErodeTo", erodeTo);
        m.SetFloat("_ErodeAlong", .7f);
        m.SetFloat("_ErodeBias", .07f);
        m.SetFloat("_FadeFrom", fadeFrom);
        m.SetFloat("_FadeTo", fadeTo);
        m.SetFloat("_FlowSpeed", .9f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material BitMaterial(Shader shader, string name, string texture, float gain,
        float cutFrom, float cutTo, float cutPower, float outline, float shade, float core)
    {
        Material m = Fresh(name, shader);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        m.SetFloat("_MaskGain", gain);
        m.SetColor("_Outline", Ink);
        m.SetColor("_Shade", new Color(.42f, .58f, .92f, 1f));
        m.SetColor("_Core", new Color(1.15f, 1.22f, 1.34f, 1f));
        m.SetFloat("_CutFrom", cutFrom);
        m.SetFloat("_CutTo", cutTo);
        m.SetFloat("_CutPower", cutPower);
        m.SetFloat("_OutlineWidth", outline);
        m.SetFloat("_ShadeWidth", shade);
        m.SetFloat("_CoreWidth", core);
        m.SetFloat("_BevelMul", 1.9f);
        // Цвет материала переводится из sRGB: скос стали — заметно светлее грани.
        m.SetColor("_BevelAdd", new Color(.38f, .45f, .58f, 1f));
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// Волна маха. Корень — в герое на высоте головы якоря, масштаб — наружный радиус, м; местная −X — направление
    /// удара, +Y — сторона, куда уходит якорь (голова волны), +Z — нормаль. Мах 2 — тот же расклад, корень повёрнут на
    /// 180° вокруг оси удара (контроллер). Слои — как у волны сабли: основная, эхо, искры и сколы с края.
    /// </summary>
    private static void SaveSwingPrefab(string name, PelagVfxId id, Mesh mesh, Material main, Material echo,
        Material spark, Material chip, float life, bool heavy)
    {
        var root = new GameObject(name);
        try
        {
            AddElement(root, id, life + .32f);
            // Основная волна: хлопок масштаба тяжелее сабельного и доворот по ходу якоря.
            ParticleSystem wave = MeshLayer(root, "Wave", mesh, main, life, 0f, 1f, 1f);
            Pop(wave, .82f, heavy ? 1.06f : 1.05f, .09f, life);
            Spin(wave, heavy ? -14f : -12f, .26f, life, 0f);
            // Эхо: шире, ниже, отстаёт, глубже по цвету и рассыпается первым — толщина металла.
            ParticleSystem echoLayer = MeshLayer(root, "Echo", mesh, echo, life, .035f, .62f, 1.06f);
            echoLayer.transform.localPosition = new Vector3(0f, 0f, -.07f);
            Pop(echoLayer, .88f, 1.03f, .09f, life);
            Spin(echoLayer, -9f, .26f, life, 8f);
            // Искры с наружного края веером наружу: вытянуты по скорости, короткие, горячие.
            ParticleSystem sparks = Sparks(root, "Sparks", spark, heavy ? 22 : 16, .12f, .26f, 3f, 11f, .035f, .06f, .9f, .8f);
            ArcShape(sparks, 1f, 180f - SpanDegrees * .5f, SpanDegrees);
            // Сколы железа с края: тяжёлые, вертятся, падают.
            ParticleSystem chips = Chips(root, "Chips", chip, heavy ? 6 : 5, .40f, .62f, 2f, 4.5f, .17f, .28f, 2.4f, 2.2f, IronFace, IronSteel);
            ArcShape(chips, .97f, 180f - SpanDegrees * .5f, SpanDegrees);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Знак на задетом: местная +Z — ход маха в точке цели (горизонталь), +Y — вверх; корень у груди ближе к камере.
    /// Масштаб корня меняет всё разом: у маха 2 крупнее. Слои — как у всплеска сабли: вспышка, искры, сколы, комья.
    /// </summary>
    private static void SaveHitPrefab(Material burst, Material spark, Material chip, Material clod)
    {
        var root = new GameObject(HitName);
        try
        {
            AddElement(root, PelagVfxId.WreckComboHit, .65f);
            // Вспышка-звезда во весь корпус: два кадра раскрывается с перелётом, потом втягивает лучи.
            ParticleSystem star = PelagWhirlwindVfxSetup.NewParticles(root, "Burst", 1, .22f, .22f, 0f, 0f, 1.15f, 1.15f);
            var starMain = star.main;
            starMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            starMain.startColor = BurstCobalt;
            starMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var starShape = star.shape; starShape.enabled = false;
            var starSize = star.sizeOverLifetime; starSize.enabled = true;
            starSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .50f), new Keyframe(.14f, 1.12f), new Keyframe(.34f, 1f), new Keyframe(1f, .94f)));
            BitRenderer(star, burst, ParticleSystemRenderMode.Billboard);

            // Искры конусом по ходу маха и чуть вверх.
            ParticleSystem sparks = Sparks(root, "Sparks", spark, 14, .12f, .22f, 5f, 10f, .035f, .06f, .8f, 0f);
            Cone(sparks, 26f, .06f, -14f);
            // Сколы железа — по ходу и вверх, с тяжестью.
            ParticleSystem chips = Chips(root, "Chips", chip, 3, .40f, .60f, 3f, 5.5f, .15f, .24f, 2.4f, 0f, IronFace, IronSteel);
            Cone(chips, 50f, .25f, -30f);
            // Сколы и комья — под вспышкой (V2: чёрные куски поверх белого ядра читались дырами).
            chips.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
            // Комья земли из-под ног задетого.
            ParticleSystem clods = Chips(root, "Clods", clod, 3, .45f, .65f, 2f, 3.6f, .16f, .26f, 2.6f, 0f, Clod, ClodLight);
            clods.transform.localPosition = new Vector3(0f, -.85f, .05f);
            Cone(clods, 35f, .15f, -55f);
            clods.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
            star.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(HitName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = 1f;
    }
}
