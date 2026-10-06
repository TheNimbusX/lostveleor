using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Слои махов Крушения — те же приёмы, что у серии сабли (MeshLayer, Pop, Spin, капли/клочья), свои частицы.</summary>
public static partial class PelagWreckComboVfxSetup
{
    /// <summary>Слой-волна: одна мешевая частица, ориентация по трансформу системы, масштаб по иерархии; возраст — в шейдер.</summary>
    private static ParticleSystem MeshLayer(GameObject root, string name, Mesh mesh, Material material,
        float life, float delay, float opacity, float size)
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
        main.startSizeX = size;
        main.startSizeY = size;
        main.startSizeZ = 1f;
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

    /// <summary>Хлопок масштаба: недолёт → перелёт → размер за <paramref name="seconds"/>.</summary>
    private static void Pop(ParticleSystem layer, float from, float peak, float seconds, float life)
    {
        float end = Mathf.Clamp01(seconds / life);
        var size = layer.sizeOverLifetime; size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, from), new Keyframe(end * .5f, peak), new Keyframe(end, 1f), new Keyframe(1f, 1f)));
    }

    /// <summary>Доворот вокруг нормали: старт <paramref name="startDegrees"/>, ещё <paramref name="degrees"/> за <paramref name="seconds"/> с ease-out.</summary>
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

    /// <summary>
    /// Искры: вытянутые по скорости билборды, мягкий клин пака «stretch spike fade» режется порогом с обводом и
    /// горячим ядром; порог растёт — искра укорачивается и гаснет. Белые и голубые вперемешку, слегка падают.
    /// </summary>
    private static ParticleSystem Sparks(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float upward)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkBlue);
        if (upward > 0f)
        {
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(upward);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
        }
        // Торможение: искра вылетает хлёстко и вязнет, не улетает за кадр.
        var shape = particles.shape;
        shape.randomDirectionAmount = .3f;
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 12f), new Keyframe(1f, 3f)));
        limit.dampen = .18f;
        BitRenderer(particles, material, ParticleSystemRenderMode.Stretch);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 2.8f;
        renderer.velocityScale = .045f;
        return particles;
    }

    /// <summary>Сколы и комья: лист «cfxr debris unlit 3x3» (случайный кадр), вертятся, падают, к концу сжимаются.</summary>
    private static ParticleSystem Chips(GameObject root, string name, Material material, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax,
        float gravity, float upward, Color dark, Color light)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(dark, light);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        if (upward > 0f)
        {
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(upward);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
        }
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = false;
        spin.z = new ParticleSystem.MinMaxCurve(-14f, 14f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .7f), new Keyframe(.12f, 1f), new Keyframe(.75f, 1f), new Keyframe(1f, .2f)));
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 3;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        // Случайный постоянный кадр листа на всю жизнь частицы.
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, .999f);
        sheet.cycleCount = 1;
        BitRenderer(particles, material, ParticleSystemRenderMode.Billboard);
        return particles;
    }

    private static void BitRenderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(Streams());
    }

    /// <summary>Эмиттер по дуге окружности в плоскости XY корня: от угла <paramref name="fromDegrees"/> на <paramref name="arcDegrees"/>, наружу.</summary>
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

    /// <summary>Конус вдоль местной +Z, ось поднята на −<paramref name="pitch"/>° (вверх); размеры и скорости — по иерархии.</summary>
    private static void Cone(ParticleSystem particles, float angle, float radius, float pitch)
    {
        var main = particles.main;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.rotation = new Vector3(pitch, 0f, 0f);
    }

    private static List<ParticleSystemVertexStream> Streams()
        => new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        };
}
