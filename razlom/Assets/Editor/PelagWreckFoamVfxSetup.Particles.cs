using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Слои частиц и узлы префабов Крушения v2 — тот же рецепт, что у Абордажа v2
/// (PelagAbordageFoamVfxSetup.Particles): свои копии помощников, чужие файлы не трогаются.
/// Любая правка — поднять Version в PelagWreckFoamVfxSetup.cs.
/// </summary>
public static partial class PelagWreckFoamVfxSetup
{
    /// <summary>Слой, который выбрасывает вид (EmitParams): мировые координаты, без своей эмиссии, в петле.</summary>
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

    /// <summary>Разовый слой: вспышка частиц в момент выдачи из пула; масштаб — от узла (Hierarchy).</summary>
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

    /// <summary>Конус вокруг вертикали (<paramref name="pitch"/> −90 — строго вверх); <paramref name="thickness"/> 0 — только кромка круга.</summary>
    private static void Cone(ParticleSystem particles, float angle, float radius, float pitch, float thickness)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.radiusThickness = thickness;
        shape.rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Линейное торможение: брызги встают рывком и зависают.</summary>
    private static void Drag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(20f);
        limit.drag = new ParticleSystem.MinMaxCurve(drag);
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    /// <summary>Рост на старте и сход к концу: start → peak на 20 % жизни → end.</summary>
    private static void SizeCurve(ParticleSystem particles, float start, float peak, float end)
    {
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, start), new Keyframe(.2f, peak), new Keyframe(1f, end)));
    }

    /// <summary>Лист капель пака 1×3: мягкий эллипс — порог шейдера даёт обвод.</summary>
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

    private static void Renderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode, float lengthScale, float velocityScale)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = mode == ParticleSystemRenderMode.HorizontalBillboard ? ParticleSystemRenderSpace.World : ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        if (mode == ParticleSystemRenderMode.Stretch)
        {
            renderer.lengthScale = lengthScale;
            renderer.velocityScale = velocityScale;
        }
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
    }

    private static void MeshLayer(GameObject root, string name, Material material)
    {
        var layer = new GameObject(name);
        layer.transform.SetParent(root.transform, false);
        layer.AddComponent<MeshFilter>();
        var renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime, float authoredRadius)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = authoredRadius;
    }

    private static void Save(GameObject root, string name) => PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
}
