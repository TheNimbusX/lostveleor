using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Узлы и слои частиц префабов Крушения «холодное железо» — свои копии помощников (рецепт
/// Абордажа v2 / Крушения v2), чужие файлы не трогаются. Любая правка — поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    private static void AddElement(GameObject root, PelagVfxId id, float lifetime, float authoredRadius)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = authoredRadius;
    }

    private static Transform Group(GameObject root, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        return go.transform;
    }

    private static void MeshLayer(GameObject root, string name, Material material, bool shadows)
    {
        var layer = new GameObject(name);
        layer.transform.SetParent(root.transform, false);
        layer.AddComponent<MeshFilter>();
        var renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    /// <summary>
    /// Кусок удара: узел, который ставит вид (позиция, поворот, размер в метрах), и дочерний «Mesh» —
    /// меш, приведённый к 1 м вокруг своего центра. Выключен до выхода из земли.
    /// </summary>
    private static void Piece(Transform parent, string name, Mesh mesh, Material material, Quaternion rotation, Vector3 offset, Vector3 scale)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        var body = new GameObject("Mesh");
        body.transform.SetParent(pivot.transform, false);
        body.transform.localRotation = rotation;
        body.transform.localPosition = offset;
        body.transform.localScale = scale;
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = body.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;
    }

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

    /// <summary>Разовый слой: вспышка частиц в момент выдачи из пула; масштаб — от узла.</summary>
    private static ParticleSystem Burst(GameObject root, string name, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.duration = Mathf.Max(.2f, lifeMax);
        var shape = particles.shape; shape.enabled = false;
        return particles;
    }

    /// <summary>Конус вокруг локальной +Z (<paramref name="pitch"/> −90 — строго вверх).</summary>
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

    private static void Drag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(30f);
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

    /// <summary>Тает к концу жизни (цвет частицы × альфа по времени).</summary>
    private static void FadeOut(ParticleSystem particles)
    {
        var color = particles.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .45f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    /// <summary>Кувырок обломков, рад/с.</summary>
    private static void Spin(ParticleSystem particles, float min, float max)
    {
        var rotation = particles.rotationOverLifetime; rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-max, max);
    }

    /// <summary>Лист обломков пака 3×3: случайный кадр, без анимации.</summary>
    private static void DebrisSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 3;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f);
    }

    /// <summary>Лист облаков пака 2×2: случайный кадр (размытые клубы читаются пылью).</summary>
    private static void SmokeSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
    }

    private static void Renderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode, float lengthScale, float velocityScale)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
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

    private static void Save(GameObject root, string name) => PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
}
