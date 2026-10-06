using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Материалы, меши и префабы Крушения на рисованных текстурах (см. PelagWreckPaintedVfxSetup).</summary>
public static partial class PelagWreckPaintedVfxSetup
{
    /// <summary>Материал с нуля при сохранении ассета и GUID (как PelagSabreComboVfxSetup.Fresh).</summary>
    private static Material Painted(Shader shader, string name, Texture2D texture, float cut, float hdr, float glow, float ageMode,
        float life, float revealFrom, float revealSeconds, float bandMix, float along, float erodeFrom, float erodeTo, float push)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        material.SetTexture("_MainTex", texture);
        material.SetColor("_Ink", Ink);
        material.SetFloat("_Cut", cut);
        material.SetFloat("_OutlineCut", .14f);
        material.SetFloat("_OutlineBias", 1.6f);
        material.SetFloat("_Gain", 1f);
        material.SetFloat("_Hdr", hdr);
        material.SetFloat("_Glow", glow);
        material.SetColor("_TintMul", Color.white);
        material.SetFloat("_TintWhite", 0f);
        material.SetFloat("_AgeMode", ageMode);
        material.SetFloat("_LifeSeconds", life);
        material.SetFloat("_RevealFrom", revealFrom);
        material.SetFloat("_RevealSeconds", revealSeconds);
        material.SetFloat("_BandMix", bandMix);
        material.SetFloat("_Along", along);
        material.SetFloat("_ErodeFrom", erodeFrom);
        material.SetFloat("_ErodeTo", erodeTo);
        material.SetFloat("_ErodeRim", .04f);
        material.SetFloat("_CameraPush", push);
        return material;
    }

    /// <summary>
    /// Полумесяц маха из полосы пака «sword_trail 180 thick» (u вдоль дуги, v=1 на внешней окружности). Угол берётся
    /// из u: хвост (u=0) — на <paramref name="tailBack"/> назад от −X (направления удара) в сторону −Y, голова (u=1) —
    /// на <paramref name="headPast"/> дальше −X в сторону +Y; радиус — из v: от <paramref name="inner"/> до 1 ровно,
    /// без профиля (сужение хвоста и толщину головы даёт рисунок). UV и треугольники пака не трогаются.
    /// </summary>
    private static Mesh ArcMesh(string name, Mesh source, float tailBack, float headPast, float inner)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        float from = 180f + tailBack, to = 180f - headPast;
        for (int i = 0; i < vertices.Length; i++)
        {
            float u = Mathf.Clamp01(uv[i].x), v = Mathf.Clamp01(uv[i].y);
            float angle = Mathf.Lerp(from, to, u) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(inner, 1f, v);
            vertices[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, source.triangles);
        return mesh;
    }

    /// <summary>Квад 1 × 1 м в плоскости XZ, центр в начале, нормаль вверх, u вдоль +X, v вдоль +Z.</summary>
    private static Mesh GroundQuad(string name)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        var vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f) };
        var uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, new[] { 0, 2, 1, 1, 2, 3 });
        return mesh;
    }

    private static PelagVfxElement AddElement(GameObject root, PelagVfxId id, float lifetime)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = 1f;
        return element;
    }

    /// <summary>
    /// Мах. Корень — центр дуги на высоте пояса, масштаб — наружный радиус, м; местная −X — направление удара, +Y —
    /// сторона головы, +Z — нормаль. Мах 2 — тот же рисунок с корнем, повёрнутым вокруг оси удара (зеркало).
    /// </summary>
    private static void SaveSwingPrefab(string name, PelagVfxId id, Mesh arc, Material material, float life, float spin)
    {
        var root = new GameObject(name);
        try
        {
            AddElement(root, id, life + .05f);
            ParticleSystem wave = MeshLayer(root, "Wave", arc, material, life);
            Pop(wave, .90f, 1.03f, .08f, life);
            Spin(wave, spin, .22f, life);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Знак на задетом: звезда удара (одна частица) и сколы-спрайты (выпускает вид), местная +Z — ход маха.</summary>
    private static void SaveHitPrefab(Materials m)
    {
        var root = new GameObject(HitName);
        try
        {
            AddElement(root, PelagVfxId.WreckPaintedHit, .8f);
            // Звезда: размер холста = рисунок / StarFill; корень масштабирует вид (мах 2 — крупнее).
            float size = 1f / PelagWreckPaintedLook.StarFill;
            ParticleSystem star = PelagWhirlwindVfxSetup.NewParticles(root, "Star", 1, .26f, .26f, 0f, 0f, size, size);
            var main = star.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            var shape = star.shape; shape.enabled = false;
            var grow = star.sizeOverLifetime; grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .45f), new Keyframe(.18f, 1.08f), new Keyframe(.36f, 1f), new Keyframe(1f, 1f)));
            Sprites(star, m.HitStar, 1, 1, 0f);
            ParticleSystem chips = ChipParticles(root, "Chips", m.Chips, 24, 1.7f);
            chips.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(HitName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Выпад: «Line» — рисованная полоса на земле (меш и блок свойств пишет вид), «Star» — звезда удара на земляном
    /// кваде (масштаб и возраст пишет вид), «Chunks» — комья-спрайты вдоль полосы (выпускает вид).
    /// </summary>
    private static void SaveLungePrefab(Materials m, Mesh quad)
    {
        var root = new GameObject(LungeName);
        try
        {
            AddElement(root, PelagVfxId.WreckPaintedLunge, 1.4f);
            MeshPart(root, "Line", null, m.Lunge);
            MeshPart(root, "Star", quad, m.LungeStar);
            ParticleSystem chunks = ChipParticles(root, "Chunks", m.Chunks, 32, 1f);
            chunks.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(LungeName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void MeshPart(GameObject root, string name, Mesh mesh, Material material)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var filter = host.AddComponent<MeshFilter>();
        if (mesh != null) filter.sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>Сколы и комья: спрайты листа 4×2 (случайный из 6), вращение, тяжесть; выпуск — только из вида.</summary>
    private static ParticleSystem ChipParticles(GameObject root, string name, Material material, int max, float gravity)
    {
        ParticleSystem chips = PelagWhirlwindVfxSetup.NewParticles(root, name, max, .5f, .7f, 0f, 0f, .3f, .3f);
        var main = chips.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = Color.white;
        main.maxParticles = max;
        // Комья выпада выпускаются, пока фронт идёт по полосе (до ~0,4 с после рождения): система не должна кончиться раньше.
        main.duration = 2f;
        var emission = chips.emission;
        emission.enabled = false;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        var shape = chips.shape; shape.enabled = false;
        Sprites(chips, material, 4, 2, 5.99f);
        return chips;
    }

    /// <summary>Рисованные билборды: лист <paramref name="tilesX"/>×<paramref name="tilesY"/>, случайный кадр, без смены.</summary>
    private static void Sprites(ParticleSystem particles, Material material, int tilesX, int tilesY, float lastFrame)
    {
        if (tilesX * tilesY > 1)
        {
            var sheet = particles.textureSheetAnimation; sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tilesX;
            sheet.numTilesY = tilesY;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
            sheet.cycleCount = 1;
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
    }

    /// <summary>Слой-полумесяц: одна мешевая частица по трансформу системы, масштаб по иерархии, возраст — AgePercent.</summary>
    private static ParticleSystem MeshLayer(GameObject root, string name, Mesh mesh, Material material, float life)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, 1, life, life, 0f, 0f, 1f, 1f);
        var main = particles.main;
        main.duration = life + .05f;
        main.startSize3D = true;
        main.startSizeX = 1f;
        main.startSizeY = 1f;
        main.startSizeZ = 1f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
        return particles;
    }

    /// <summary>Хлопок масштаба: недолёт → перелёт → размер за <paramref name="seconds"/>.</summary>
    private static void Pop(ParticleSystem layer, float from, float peak, float seconds, float life)
    {
        float end = Mathf.Clamp01(seconds / life);
        var size = layer.sizeOverLifetime; size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, from), new Keyframe(end * .5f, peak), new Keyframe(end, 1f), new Keyframe(1f, 1f)));
    }

    /// <summary>Доворот вокруг нормали по ходу маха (ease-out); все оси — в одном режиме кривых.</summary>
    private static void Spin(ParticleSystem layer, float degrees, float seconds, float life)
    {
        var main = layer.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f);
        float end = Mathf.Clamp01(seconds / life);
        float peak = degrees * Mathf.Deg2Rad / (seconds * .45f);
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, peak), new Keyframe(end * .5f, peak * .33f), new Keyframe(end, 0f), new Keyframe(1f, 0f)));
    }

    private static System.Collections.Generic.List<ParticleSystemVertexStream> Streams()
        => new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        };
}
