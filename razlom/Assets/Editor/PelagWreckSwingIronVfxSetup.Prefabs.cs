using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Махи «железо» на технике сабли — префабы. Оси корня маха — как у волны сабли: местная −X — направление этапа,
/// +Y — сторона, куда уходит голова якоря, +Z — нормаль плоскости; масштаб корня — наружный радиус, м (ставит
/// контроллер). Мах 2 — тот же расклад, корень повёрнут на 180° вокруг оси удара. Знак на задетом: корень в груди
/// цели, +Z — ход маха; «Burst» — звезда удара (выброс при старте), «Sparks», «Chips», «Dust» — выбрасывает вид.
/// </summary>
public static partial class PelagWreckSwingIronVfxSetup
{
    private static readonly Color SparkWhite = new Color(1.15f, 1.22f, 1.30f, 1f);
    private static readonly Color SparkBlue = new Color(.62f, .88f, 1.15f, 1f);

    private static void SaveSwingPrefab(string name, PelagVfxId id, Mesh wave, Mesh[] links, Materials m, bool heavy)
    {
        var root = new GameObject(name);
        try
        {
            float life = heavy ? HeavyLife : LightLife;
            AddElement(root, id, life + .25f);
            // V3: одна полоса (кадр — без второго тёмного гребня), раскрывается за доли кадра и доворачивается по ходу.
            ParticleSystem body = MeshLayer(root, "Wave", wave, heavy ? m.HeavyWave : m.Wave, life, 0f, 1f, 1f);
            Pop(body, .90f, 1.03f, .07f, life);
            Spin(body, heavy ? -10f : -8f, .22f, life, 0f);
            // Звенья-призраки едут с полосой (тот же доворот): стороны хвоста и фронта — сразу, стороны головы — когда
            // голова дошла; гаснут вместе с полосой.
            float linkLife = life * .8f;
            for (int s = 0; s < 2; s++)
            {
                ParticleSystem strip = MeshLayer(root, s == 0 ? "LinksTail" : "LinksHead", links[s], m.Links, linkLife, s == 0 ? .0f : .03f, 1f, 1f);
                strip.transform.localPosition = new Vector3(0f, 0f, .02f);
                var main = strip.main;
                main.startColor = new Color(.92f, 1.04f, 1.16f, 1f);
                Pop(strip, .92f, 1.03f, .07f, linkLife);
                Spin(strip, heavy ? -10f : -8f, .22f, linkLife, 0f);
            }
            // Искры с наружной кромки у фронта — по ходу маха и наружу (кадр: редкие тонкие голубые штрихи).
            ParticleSystem sparks = Sparks(root, "Sparks", m.Spark, heavy ? 9 : 6, .10f, .18f, 4f, 8f, .035f, .06f);
            var sparksMain = sparks.main;
            sparksMain.startDelay = .02f;
            // Дуга корня: отступ 0 (фронт) — угол 180°; искры — от фронта к стороне головы (отступы 0…60°).
            ArcShape(sparks, .98f, 120f, 60f);
            Save(root, name);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void SaveHitPrefab(Materials m)
    {
        var root = new GameObject(HitName);
        try
        {
            AddElement(root, PelagVfxId.WreckIronSwingHit, .75f);
            // Звезда удара: рисованные шипы пака, бело-голубая, раскрывается за два кадра и втягивает лучи.
            ParticleSystem burst = PelagWhirlwindVfxSetup.NewParticles(root, "Burst", 1, .15f, .15f, 0f, 0f, .95f, .95f);
            var burstMain = burst.main;
            burstMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            burstMain.startColor = new Color(1.10f, 1.18f, 1.28f, 1f);
            burstMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var burstShape = burst.shape; burstShape.enabled = false;
            var burstSize = burst.sizeOverLifetime; burstSize.enabled = true;
            burstSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .45f), new Keyframe(.18f, 1.08f), new Keyframe(.4f, 1f), new Keyframe(1f, .9f)));
            BlobRenderer(burst, m.Burst, ParticleSystemRenderMode.Billboard);
            // Остальное выбрасывает вид (направление маха, земля у ног): без своих выбросов.
            Sparks(root, "Sparks", m.Spark, 0, .12f, .22f, 0f, 0f, .05f, .09f).GetComponent<ParticleSystemRenderer>().lengthScale = 4.2f;
            ParticleSystem chips = Emitted(root, "Chips", m.Chip, 32, 1.9f);
            var chipsMain = chips.main;
            chipsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var spin = chips.rotationOverLifetime; spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var sheet = chips.textureSheetAnimation; sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 3; sheet.numTilesY = 3;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f);
            sheet.cycleCount = 1;
            BlobRenderer(chips, m.Chip, ParticleSystemRenderMode.Billboard);
            ParticleSystem dust = Emitted(root, "Dust", m.Dust, 12, -.05f);
            var dustSize = dust.sizeOverLifetime; dustSize.enabled = true;
            dustSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .55f), new Keyframe(.35f, 1f), new Keyframe(1f, 1.15f)));
            var dustSheet = dust.textureSheetAnimation; dustSheet.enabled = true;
            dustSheet.mode = ParticleSystemAnimationMode.Grid;
            dustSheet.numTilesX = 2; dustSheet.numTilesY = 2;
            dustSheet.animation = ParticleSystemAnimationType.WholeSheet;
            dustSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            dustSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            dustSheet.cycleCount = 1;
            var dustDrag = dust.limitVelocityOverLifetime; dustDrag.enabled = true;
            dustDrag.drag = 2.5f; dustDrag.limit = 10f;
            BlobRenderer(dust, m.Dust, ParticleSystemRenderMode.Billboard);
            Save(root, HitName);
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

    private static void Save(GameObject root, string name) => PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));

    /// <summary>Слой-полоса: одна мешевая частица в осях системы, масштаб по иерархии, возраст — в шейдер (AgePercent).</summary>
    private static ParticleSystem MeshLayer(GameObject root, string name, Mesh mesh, Material material, float life, float delay, float opacity, float size)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = life + delay + .05f;
        main.startDelay = delay;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = size; main.startSizeY = size; main.startSizeZ = 1f;
        main.startColor = new Color(1f, 1f, 1f, opacity);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 1;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.SetActiveVertexStreams(Streams());
        return particles;
    }

    private static void Pop(ParticleSystem layer, float from, float peak, float seconds, float life)
    {
        float end = Mathf.Clamp01(seconds / life);
        var size = layer.sizeOverLifetime; size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, from), new Keyframe(end * .5f, peak), new Keyframe(end, 1f), new Keyframe(1f, 1f)));
    }

    /// <summary>Доворот вокруг нормали по ходу маха (ease-out), все оси — в одном режиме кривых.</summary>
    private static void Spin(ParticleSystem layer, float degrees, float seconds, float life, float startDegrees)
    {
        var main = layer.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(startDegrees * Mathf.Deg2Rad);
        float end = Mathf.Clamp01(seconds / life);
        float peak = degrees * Mathf.Deg2Rad / (seconds * .45f);
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, peak), new Keyframe(end * .5f, peak * .33f), new Keyframe(end, 0f), new Keyframe(1f, 0f)));
    }

    /// <summary>Искры: вытянутые по скорости капли пака (мягкий эллипс листа 1×3) на Blob, бело-голубые, падают.</summary>
    private static ParticleSystem Sparks(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, Mathf.Max(count, 32),
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = .9f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(SparkWhite, SparkBlue);
        var emission = particles.emission;
        emission.SetBursts(count > 0 ? new[] { new ParticleSystem.Burst(0f, (short)count) } : new ParticleSystem.Burst[0]);
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1; sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
        BlobRenderer(particles, material, ParticleSystemRenderMode.Stretch);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        // V2: искры железа — короче и тоньше, вытянуты сильнее (V1 читались каплями).
        renderer.lengthScale = 3.4f;
        renderer.velocityScale = .03f;
        return particles;
    }

    /// <summary>Частицы без своих выбросов (выбрасывает вид в мировых координатах).</summary>
    private static ParticleSystem Emitted(GameObject root, string name, Material material, int max, float gravity)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, max, .3f, .5f, 0f, 0f, .1f, .1f);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        var shape = particles.shape; shape.enabled = false;
        return particles;
    }

    private static void BlobRenderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
    }

    private static void ArcShape(ParticleSystem particles, float radius, float fromDegrees, float arcDegrees)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0f;
        shape.arc = arcDegrees;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
        shape.rotation = new Vector3(0f, 0f, fromDegrees);
    }

    private static System.Collections.Generic.List<ParticleSystemVertexStream> Streams()
        => new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        };
}
