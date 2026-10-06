using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы махов Крушения V2 (см. PelagWreckSwingVfxSetup.cs). Ловушки частиц Unity учтены: выбрасываемые слои в мировых
/// координатах без своей эмиссии, кривые одного режима, не больше одного всплеска; растянутые частицы — длина
/// = размер × PelagWreckSwingImpact.StreakAspect (скорость не тянет). Свои копии помощников (рецепт V1 / «железа»),
/// чужие файлы не трогаются. Любая правка — поднять Version.
/// </summary>
public static partial class PelagWreckSwingVfxSetup
{
    /// <summary>
    /// Дуга маха: «Crescent» — меш пишет вид (PelagWreckSwingRibbon), «Link0…2» — меш звена «железа», их ставит и гасит
    /// вид, «Glints» — искры с головы (выбрасывает вид).
    /// </summary>
    private static void SaveArcPrefab(Kit kit)
    {
        var root = new GameObject(ArcName);
        try
        {
            AddElement(root, PelagVfxId.WreckSwingArc, 1f, 1f);
            var crescent = new GameObject("Crescent");
            crescent.transform.SetParent(root.transform, false);
            crescent.AddComponent<MeshFilter>();
            Quiet(crescent.AddComponent<MeshRenderer>(), kit.Crescent);
            for (int i = 0; i < 3; i++)
            {
                var link = new GameObject("Link" + i);
                link.transform.SetParent(root.transform, false);
                link.AddComponent<MeshFilter>().sharedMesh = kit.LinkMesh;
                MeshRenderer renderer = link.AddComponent<MeshRenderer>();
                Quiet(renderer, kit.Link);
                renderer.enabled = false;
            }
            ParticleSystem glints = Emitted(root, "Glints", 64, .25f);
            Drag(glints, 5f);
            SizeCurve(glints, 1f, 1f, .2f);
            Renderer(glints, kit.Spark, ParticleSystemRenderMode.Stretch, 2.2f, .03f);
            Save(root, ArcName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Знак на задетом: «Ink» (тёмный контур росчерка, рисуется под телом), «Streak» (тело в цвет формы), «Core»
    /// (сердцевина к белому), «Chips» (сколы листа обломков пака, падают), «Dust» (пыль у ног). Всё выбрасывает вид.
    /// </summary>
    private static void SaveHitPrefab(Kit kit)
    {
        var root = new GameObject(HitName);
        try
        {
            AddElement(root, PelagVfxId.WreckSwingHit, 1.1f, 1f);
            // Росчерк: три слоя одного листа; сортировка — контур дальше всех, сердцевина ближе всех.
            StreakLayer(root, "Ink", kit.Streak, 20f);
            StreakLayer(root, "Streak", kit.Streak, 10f);
            StreakLayer(root, "Core", kit.Streak, 0f);

            ParticleSystem chips = Emitted(root, "Chips", 24, .6f);
            var chipsMain = chips.main;
            chipsMain.gravityModifier = 2.2f;
            chipsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            DebrisSheet(chips);
            Spin(chips, 10f);
            var chipSize = chips.sizeOverLifetime; chipSize.enabled = true;
            chipSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .8f), new Keyframe(.1f, 1f), new Keyframe(.8f, 1f), new Keyframe(1f, .4f)));
            Drag(chips, 1.2f);
            Renderer(chips, kit.Chip, ParticleSystemRenderMode.Billboard, 0f, 0f);

            ParticleSystem dust = Emitted(root, "Dust", 12, .8f);
            var dustMain = dust.main;
            dustMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SmokeSheet(dust);
            SizeCurve(dust, .6f, 1f, 1.2f);
            FadeOut(dust);
            Drag(dust, 3f);
            Renderer(dust, kit.Dust, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, HitName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Слой росчерка: растянутая частица (длина — размер × StreakAspect), быстро тормозит, тает к концу.</summary>
    private static void StreakLayer(GameObject root, string name, Material material, float fudge)
    {
        ParticleSystem streak = Emitted(root, name, 16, .15f);
        Drag(streak, 6f);
        SizeCurve(streak, .7f, 1f, .55f);
        FadeOut(streak);
        Renderer(streak, material, ParticleSystemRenderMode.Stretch, PelagWreckSwingImpact.StreakAspect, 0f);
        streak.GetComponent<ParticleSystemRenderer>().sortingFudge = fudge;
    }

    private static void Quiet(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    // ---------------------------------------------------------------- помощники (копии рецепта V1 / «железа»)

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime, float authoredRadius)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = authoredRadius;
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

    /// <summary>Кувырок скола, рад/с.</summary>
    private static void Spin(ParticleSystem particles, float max)
    {
        var rotation = particles.rotationOverLifetime; rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-max, max);
    }

    /// <summary>Лист обломков пака 3×3: случайный кадр, без анимации.</summary>
    private static void DebrisSheet(ParticleSystem particles) => Sheet(particles, 3, 8.99f);

    /// <summary>Лист облаков пака 2×2: случайный кадр (размытые клубы читаются пылью).</summary>
    private static void SmokeSheet(ParticleSystem particles) => Sheet(particles, 2, 3.99f);

    private static void Sheet(ParticleSystem particles, int tiles, float lastFrame)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = tiles;
        sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
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
