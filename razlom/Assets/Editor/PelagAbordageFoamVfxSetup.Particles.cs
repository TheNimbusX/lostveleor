using UnityEngine;

/// <summary>
/// Слои частиц Абордажа v2 — тот же рецепт, что у Шквала v2 (PelagSquallFoamVfxSetup.Prefabs):
/// ловушки частиц Unity учтены (кривые одного режима, без Float32-цветов меша, мировые
/// координаты у выбрасываемых). Любая правка — поднять Version в PelagAbordageFoamVfxSetup.cs.
/// </summary>
public static partial class PelagAbordageFoamVfxSetup
{
    /// <summary>
    /// Слой, который выбрасывает вид (EmitParams): мировые координаты, без своей эмиссии,
    /// в петле — объект всегда играет и принимает выброс, сколько бы ни жил.
    /// </summary>
    private static ParticleSystem Emitted(GameObject root, string name, int max, float lifeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, max, lifeMax * .7f, lifeMax, 0f, 0f, .1f, .1f);
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = max;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 0f;
        var shape = particles.shape; shape.enabled = false;
        return particles;
    }

    /// <summary>Разовый слой: вспышка частиц в момент выдачи из пула; масштаб — от корня.</summary>
    private static ParticleSystem Burst(GameObject root, string name, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float duration)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.duration = Mathf.Max(.2f, duration);
        return particles;
    }

    /// <summary>Конус вокруг вертикали, наклонённый по +Z: <paramref name="pitch"/> −90 — строго вверх.</summary>
    private static void Cone(ParticleSystem particles, float angle, float radius, float pitch)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Линейное торможение: брызги встают рывком и зависают, а не улетают выше колена.</summary>
    private static void Drag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(20f);
        limit.drag = new ParticleSystem.MinMaxCurve(drag);
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    /// <summary>Рост на старте и сход к концу: start → peak на 20% жизни → end.</summary>
    private static void SizeCurve(ParticleSystem particles, float start, float peak, float end)
    {
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, start), new Keyframe(.2f, peak), new Keyframe(1f, end)));
    }

    /// <summary>Лист капель пака 1×3: мягкий эллипс — у жёсткого порогу не из чего сделать обвод.</summary>
    private static void DropSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
    }

    /// <summary>
    /// Круг 3: случайная клетка атласа N×N на всю жизнь (камни пака «debris 3x3»): кадр задан
    /// постоянной стартового кадра, по времени не листается.
    /// </summary>
    private static void AtlasRandom(ParticleSystem particles, int tiles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = tiles;
        sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tiles * tiles - .01f);
        sheet.cycleCount = 1;
    }

    /// <summary>Круг 3: случайный стартовый поворот и кувырок в плоскости экрана до ±degrees град/с (две константы).</summary>
    private static void Tumble(ParticleSystem particles, float degrees)
    {
        var rotation = particles.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = false;
        rotation.z = new ParticleSystem.MinMaxCurve(-degrees * Mathf.Deg2Rad, degrees * Mathf.Deg2Rad);
        var main = particles.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    }

    /// <summary>Круг 3: клуб пены — мягкий эллипс листа капель 2:1, по высоте ×aspect — почти круглый.</summary>
    private static void RoundPuff(ParticleSystem particles, float sizeMin, float sizeMax, float aspect)
    {
        var main = particles.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSizeY = new ParticleSystem.MinMaxCurve(sizeMin * aspect, sizeMax * aspect);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
    }

    private static void Renderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
    }
}
