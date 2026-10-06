using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Префабы Шквала v2 (см. PelagSquallFoamVfxSetup.cs). Любая правка — поднять Version там.</summary>
public static partial class PelagSquallFoamVfxSetup
{
    /// <summary>Жизнь колец удара, с: обычный и последний.</summary>
    private const float StrikeLife = .48f, FinishLife = .62f;

    /// <summary>
    /// Вода прыжка: корень — начало пути на земле, без поворота и масштаба; меш «Water»
    /// пишет вид (PelagSquallWater). Частицы — мировые, без своих вспышек: их выбрасывает
    /// контроллер (отрыв, занос по краям, живые гребни полосы).
    /// </summary>
    private static void SaveWaterPrefab(Material water, Material foam, Material drop)
    {
        var root = new GameObject(WaterName);
        try
        {
            AddElement(root, PelagVfxId.SquallWater, 8f, 1f);
            var strip = new GameObject("Water");
            strip.transform.SetParent(root.transform, false);
            strip.AddComponent<MeshFilter>();
            MeshRenderer(strip.AddComponent<MeshRenderer>(), water);

            // Капли у старта: лежат и тают порогом — старый конец струи рассыпается, а не иглой.
            ParticleSystem tail = Emitted(root, "TailDrops", 12, .40f);
            var tailMain = tail.main;
            tailMain.startColor = DropWhite;
            DropSheet(tail);
            Renderer(tail, drop, ParticleSystemRenderMode.Billboard);

            // Занос: клочья пены скользят с гребней наружу по земле.
            ParticleSystem skidFoam = Emitted(root, "SkidFoam", 64, .44f);
            var foamMain = skidFoam.main;
            foamMain.gravityModifier = .5f;
            foamMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            foamMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(skidFoam, .7f, 1f, .9f);
            Drag(skidFoam, 4f);
            Renderer(skidFoam, foam, ParticleSystemRenderMode.Billboard);

            // ...и низкие брызги, вытянутые по скорости.
            ParticleSystem skidDrops = Emitted(root, "SkidDrops", 64, .34f);
            var dropsMain = skidDrops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(skidDrops);
            Renderer(skidDrops, drop, ParticleSystemRenderMode.Stretch);
            var dropsRenderer = skidDrops.GetComponent<ParticleSystemRenderer>();
            dropsRenderer.lengthScale = 1.5f;
            dropsRenderer.velocityScale = .025f;

            // Живая полоса: комья всплывают на гребнях, растут и тают (без гравитации, медленно расходятся).
            ParticleSystem edge = Emitted(root, "EdgeFoam", 160, .70f);
            var edgeMain = edge.main;
            edgeMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            edgeMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(edge, .5f, 1f, .45f);
            Drag(edge, 1.5f);
            Renderer(edge, foam, ParticleSystemRenderMode.Billboard);

            Save(root, WaterName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Кольцо пены у ног цели (кадры: пенный водоворот вокруг каждого задетого). Корень на
    /// земле, +Z — ход клинка, авторский радиус 0,5 м (вид масштабирует по телу цели).
    /// Лужица воды, завиток комьев по кругу (SwirlA — по часовой, SwirlB — против; лишний
    /// вид гасит), клочья по ходу клинка, вырезанные капли вверх. Последний удар — шире,
    /// гуще и с короной языков пены (как корона рывка).
    /// </summary>
    private static void SaveRingPrefab(string name, PelagVfxId id, bool finish, Material foam, Material crownDrop)
    {
        var root = new GameObject(name);
        try
        {
            float life = finish ? FinishLife : StrikeLife;
            AddElement(root, id, life, .5f);

            float puddleSize = finish ? 1.40f : 1.05f;
            ParticleSystem puddle = Burst(root, "Puddle", 1, life * .9f, life * .9f, 0f, 0f, puddleSize, puddleSize, life);
            var puddleMain = puddle.main;
            puddleMain.startColor = DropAqua;
            puddleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var puddleShape = puddle.shape; puddleShape.enabled = false;
            var puddleSize3 = puddle.sizeOverLifetime; puddleSize3.enabled = true;
            puddleSize3.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.18f, 1.02f), new Keyframe(1f, 1.06f)));
            Renderer(puddle, foam, ParticleSystemRenderMode.HorizontalBillboard);
            puddle.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            for (int k = 0; k < 2; k++)
            {
                ParticleSystem swirl = Burst(root, k == 0 ? "SwirlA" : "SwirlB", finish ? 14 : 10, .32f, .46f, 0f, 0f, .14f, .24f, life);
                var swirlMain = swirl.main;
                swirlMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                swirlMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
                var shape = swirl.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = finish ? .52f : .40f;
                shape.radiusThickness = 0f;
                shape.rotation = new Vector3(90f, 0f, 0f);
                Orbit(swirl, k == 0 ? 5.5f : -5.5f, .6f);
                SizeCurve(swirl, .7f, 1f, .55f);
                Renderer(swirl, foam, ParticleSystemRenderMode.Billboard);
                swirl.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;
            }

            ParticleSystem kick = Burst(root, "Kick", finish ? 9 : 6, .30f, .42f, 1.5f, 3.0f, .12f, .22f, life);
            var kickMain = kick.main;
            kickMain.gravityModifier = .8f;
            kickMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            kickMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(kick, 25f, .10f, -15f);
            Drag(kick, 3f);
            Renderer(kick, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem drops = Burst(root, "Drops", finish ? 16 : 8, .30f, .40f, 3.0f, 5.0f, .05f, .09f, life);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 2.0f;
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(drops, 50f, .20f, -78f);
            Drag(drops, 2f);
            DropSheet(drops);
            Renderer(drops, crownDrop, ParticleSystemRenderMode.Billboard);
            drops.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;

            if (finish)
            {
                // Корона языков пены (рецепт короны рывка): растут из земли и рвутся порогом.
                ParticleSystem crown = Burst(root, "Crown", 14, .24f, .32f, 2.6f, 3.6f, .08f, .10f, life);
                var crownMain = crown.main;
                crownMain.gravityModifier = 0f;
                crownMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
                Cone(crown, 45f, .30f, -84f);
                Drag(crown, 10f);
                SizeCurve(crown, .6f, 1f, .55f);
                DropSheet(crown);
                Renderer(crown, crownDrop, ParticleSystemRenderMode.Stretch);
                var crownRenderer = crown.GetComponent<ParticleSystemRenderer>();
                crownRenderer.lengthScale = 8f;
                crownRenderer.velocityScale = 0f;
                crownRenderer.sortingFudge = -2f;
            }

            Save(root, name);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Метка добычи Охоты (кадр Охоты: «кипящее кольцо белой пены у ног — добыча»):
    /// петли, пока вид держит метку; корень у ног цели, авторский радиус 0,5 м.
    /// Перекрывающиеся лужицы и кружащие комья — кольцо живое, не застывшее.
    /// </summary>
    private static void SaveMarkPrefab(Material foam, Material crownDrop)
    {
        var root = new GameObject(MarkName);
        try
        {
            AddElement(root, PelagVfxId.SquallMark, 2.1f, .5f);

            ParticleSystem churn = Looping(root, "Churn", 26f, 40, .30f, .45f, 0f, 0f, .12f, .20f);
            var churnMain = churn.main;
            churnMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            churnMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            var shape = churn.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .45f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Orbit(churn, 4.5f, .2f);
            SizeCurve(churn, .6f, 1f, .5f);
            Renderer(churn, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem pool = Looping(root, "Pool", 5f, 6, .45f, .60f, 0f, 0f, .90f, 1.10f);
            var poolMain = pool.main;
            poolMain.startColor = DropAqua;
            poolMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var poolShape = pool.shape; poolShape.enabled = false;
            SizeCurve(pool, .8f, 1.05f, .9f);
            Renderer(pool, foam, ParticleSystemRenderMode.HorizontalBillboard);
            pool.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            ParticleSystem spits = Looping(root, "Spits", 9f, 12, .25f, .35f, 1.2f, 2.0f, .04f, .07f);
            var spitsMain = spits.main;
            spitsMain.gravityModifier = 2f;
            spitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(spits, 20f, .40f, -90f);
            DropSheet(spits);
            Renderer(spits, crownDrop, ParticleSystemRenderMode.Billboard);

            Save(root, MarkName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Знаки у ног (выбрасывает вид каждый кадр): пена у щиколоток замедленных пеной,
    /// лужица под ними, брызги; вуаль неуязвимости у ног героя. Петля без своей
    /// эмиссии — объект всегда «играет» и принимает выброс.
    /// </summary>
    private static void SaveCuePrefab(Material foam, Material drop, Material crownDrop)
    {
        var root = new GameObject(CueName);
        try
        {
            AddElement(root, PelagVfxId.SquallCue, 1.5f, 1f);

            ParticleSystem ankle = Emitted(root, "AnkleFoam", 160, .50f);
            var ankleMain = ankle.main;
            ankleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ankleMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(ankle, .7f, 1f, .6f);
            Drag(ankle, 2.5f);
            Renderer(ankle, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem pool = Emitted(root, "AnklePool", 48, .60f);
            var poolMain = pool.main;
            poolMain.startColor = DropAqua;
            SizeCurve(pool, .7f, 1f, .95f);
            Renderer(pool, foam, ParticleSystemRenderMode.HorizontalBillboard);
            pool.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            ParticleSystem ankleDrops = Emitted(root, "AnkleDrops", 80, .35f);
            var ankleDropsMain = ankleDrops.main;
            ankleDropsMain.gravityModifier = 2f;
            ankleDropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(ankleDrops);
            Renderer(ankleDrops, drop, ParticleSystemRenderMode.Billboard);

            ParticleSystem veil = Emitted(root, "Veil", 64, .40f);
            var veilMain = veil.main;
            veilMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            veilMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(veil, .6f, 1f, .5f);
            Drag(veil, 3f);
            Renderer(veil, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem veilDrops = Emitted(root, "VeilDrops", 32, .30f);
            var veilDropsMain = veilDrops.main;
            veilDropsMain.gravityModifier = 1.8f;
            veilDropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            DropSheet(veilDrops);
            Renderer(veilDrops, crownDrop, ParticleSystemRenderMode.Billboard);

            Save(root, CueName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Двойник Неуловимого: 12 слотов позы (MeshFilter + MeshRenderer с M_Squall_Ghost,
    /// меш ставит вид — копия позы героя), капли пены с силуэта, лужица у ног, всплеск
    /// при растворении. Слоты и частицы связываются в PelagSquallGhostParts.
    /// </summary>
    private static void SaveGhostPrefab(Material ghost, Material foam, Material drop)
    {
        var root = new GameObject(GhostName);
        try
        {
            AddElement(root, PelagVfxId.SquallGhost, 6.5f, 1f);
            var filters = new MeshFilter[PelagSquallGhostParts.MaxParts];
            var renderers = new MeshRenderer[PelagSquallGhostParts.MaxParts];
            for (int i = 0; i < filters.Length; i++)
            {
                var part = new GameObject("Part" + i);
                part.transform.SetParent(root.transform, false);
                filters[i] = part.AddComponent<MeshFilter>();
                renderers[i] = part.AddComponent<MeshRenderer>();
                MeshRenderer(renderers[i], ghost);
                renderers[i].enabled = false;
            }

            ParticleSystem drips = Emitted(root, "Drips", 48, .60f);
            var dripsMain = drips.main;
            dripsMain.gravityModifier = 2.2f;
            dripsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(drips);
            Renderer(drips, drop, ParticleSystemRenderMode.Stretch);
            var dripsRenderer = drips.GetComponent<ParticleSystemRenderer>();
            dripsRenderer.lengthScale = 1.4f;
            dripsRenderer.velocityScale = .03f;

            ParticleSystem pool = Emitted(root, "Pool", 12, .70f);
            var poolMain = pool.main;
            poolMain.startColor = DropAqua;
            SizeCurve(pool, .6f, 1f, 1f);
            Renderer(pool, foam, ParticleSystemRenderMode.HorizontalBillboard);
            pool.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            ParticleSystem burst = Emitted(root, "Burst", 40, .45f);
            var burstMain = burst.main;
            burstMain.gravityModifier = .4f;
            burstMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            burstMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(burst, .7f, 1f, .5f);
            Drag(burst, 2f);
            Renderer(burst, foam, ParticleSystemRenderMode.Billboard);

            root.AddComponent<PelagSquallGhostParts>().Configure(filters, renderers, drips, pool, burst);
            Save(root, GhostName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ----------------------------------------------------------------- helpers

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime, float authoredRadius)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = authoredRadius;
    }

    private static void Save(GameObject root, string name)
        => PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));

    private static void MeshRenderer(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

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

    /// <summary>Петля со своей эмиссией (метка): рождает, пока вид не остановит.</summary>
    private static ParticleSystem Looping(GameObject root, string name, float rate, int max,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, max,
            lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = max;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = rate;
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

    /// <summary>
    /// Закрутка вокруг корня (рад/с) и уход наружу (м/с). Все кривые модуля — константы
    /// в одном режиме («Particle Velocity curves must all be in the same mode»).
    /// </summary>
    private static void Orbit(ParticleSystem particles, float orbitalY, float radial)
    {
        var velocity = particles.velocityOverLifetime; velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(orbitalY);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        velocity.radial = new ParticleSystem.MinMaxCurve(radial);
    }

    /// <summary>Рост на старте и сход к концу: start → 1 на 20% жизни → end.</summary>
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
