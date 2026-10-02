using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>Меши и префабы форм Вихря (сборка — PelagWhirlwindFoamVfxSetup.Forms.cs, версия там же).</summary>
public static partial class PelagWhirlwindFoamVfxSetup
{
    // ----------------------------------------------------------------- meshes

    /// <summary>
    /// Виток столба Бури: полоса-цилиндр радиуса 1 вокруг местной оси Z (корень
    /// X90: +Z — вниз, высота — −z). u по ходу витка (голова u = 1 на местной +Y,
    /// хвост уходит на <paramref name="spanDegrees"/>, как у колец пены), v снизу
    /// вверх: v = 1 — верхняя кромка, там пена гребня. Виток поднимается на
    /// <paramref name="rise"/> к голове, высота растёт от хвоста к голове
    /// (профиль колец пены), верх чуть раскрыт наружу на <paramref name="flare"/>.
    /// </summary>
    private static Mesh StormBandMesh(string name, float spanDegrees, float rise, float height, float flare)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 72;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float angle = (90f + spanDegrees * (1f - u)) * Mathf.Deg2Rad;
            float profile = Mathf.Lerp(.35f, 1f, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * .6f * u)), .7f));
            float bottom = rise * u, top = bottom + height * profile;
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[2 * i] = direction + new Vector3(0f, 0f, -bottom);
            vertices[2 * i + 1] = direction * (1f + flare) + new Vector3(0f, 0f, -top);
            uv[2 * i] = new Vector2(u, 0f);
            uv[2 * i + 1] = new Vector2(u, 1f);
            if (i == segments) continue;
            int v = i * 2, at = i * 6;
            triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
            triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    /// <summary>
    /// Рукава Водоворота в плоскости XY (корень X90 — плоско на земле): <paramref name="arms"/>
    /// спиралей от внешнего края (r = 1, хвост u = 0) к центру (r = <paramref name="inner"/>,
    /// голова u = 1), закрученных на <paramref name="sweepDegrees"/>. Ширина — от тупого
    /// хвоста к острию у центра; v = 1 — выпуклая наружная кромка, там пена.
    /// </summary>
    private static Mesh MaelstromArmsMesh(string name, int arms, float sweepDegrees, float inner, float width)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 48;
        int perArm = (segments + 1) * 2;
        var vertices = new Vector3[perArm * arms];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6 * arms];
        for (int k = 0; k < arms; k++)
        {
            float baseAngle = k * 360f / arms;
            for (int i = 0; i <= segments; i++)
            {
                float s = i / (float)segments;
                Vector2 c = ArmPoint(s, baseAngle, sweepDegrees, inner);
                Vector2 tangent = ArmPoint(Mathf.Min(1f, s + .01f), baseAngle, sweepDegrees, inner)
                                  - ArmPoint(Mathf.Max(0f, s - .01f), baseAngle, sweepDegrees, inner);
                var normal = new Vector2(-tangent.y, tangent.x).normalized;
                if (Vector2.Dot(normal, c) < 0f) normal = -normal;
                float w = width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Lerp(.08f, 1f, s))), .75f);
                int v = k * perArm + 2 * i;
                vertices[v] = c - normal * (w * .5f);
                vertices[v + 1] = c + normal * (w * .5f);
                uv[v] = new Vector2(s, 0f);
                uv[v + 1] = new Vector2(s, 1f);
                if (i == segments) continue;
                int at = (k * segments + i) * 6;
                triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
                triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
            }
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    private static Vector2 ArmPoint(float s, float baseAngle, float sweepDegrees, float inner)
    {
        float r = Mathf.Lerp(1f, inner, Mathf.Pow(s, .9f));
        float a = (baseAngle + sweepDegrees * s) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
    }

    /// <summary>
    /// Замкнутое кольцо радиуса 1 шириной <paramref name="width"/>, v = 1 снаружи. u
    /// зеркальный — от <paramref name="uFrom"/> на +X до 1 на −X и обратно: у шейдера
    /// волны струи и шум идут вдоль u и до u ≈ 0,4 гаснут, на обычном кольце был бы
    /// шов и голая треть; зеркальный u непрерывен, а струи видны по всему кругу.
    /// </summary>
    private static Mesh MirroredRingMesh(string name, float width, float uFrom)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        const int segments = 128;
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float u = uFrom + (1f - uFrom) * (1f - Mathf.Abs(2f * t - 1f));
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[2 * i] = direction * (1f - width);
            vertices[2 * i + 1] = direction;
            uv[2 * i] = new Vector2(u, 0f);
            uv[2 * i + 1] = new Vector2(u, 1f);
            if (i == segments) continue;
            int v = i * 2, at = i * 6;
            triangles[at] = v; triangles[at + 1] = v + 2; triangles[at + 2] = v + 1;
            triangles[at + 3] = v + 1; triangles[at + 4] = v + 2; triangles[at + 5] = v + 3;
        }
        PelagWhirlwindVfxSetup.Fill(mesh, vertices, uv, triangles);
        return mesh;
    }

    // ---------------------------------------------------------------- prefabs

    private static PelagVfxElement FormElement(GameObject root, PelagVfxId id, float lifetime)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = 1f;
        return element;
    }

    /// <summary>
    /// Столб Бури (storm-2). Корень у ног героя, X90, масштаб — радиус Вихря (стенка
    /// столба — край урона). Четыре яруса витков от земли до ~2 м: каждый рождает
    /// витки, пока Бурю держат, они обегают героя и рассыпаются; нижние прозрачнее —
    /// враги внутри читаются. Сверху — ломающийся гребень, с кромки — брызги,
    /// у земли — комья пены. «PulseSpray» выбрасывает вид на каждый оборот Sim.
    /// Все петли кончает вид (StopEmitting) по событию конца Бури.
    /// </summary>
    private static void SaveStormColumn(Mesh band, Mesh crest, Material bandMat, Material crestMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(StormColumnName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindStormColumn, 4.5f);
            float[] heights = { 0f, .19f, .38f, .56f };
            float[] opacity = { .40f, .62f, .85f, 1f };
            float[] spin = { -7f, -8f, -9f, -10f };
            for (int k = 0; k < heights.Length; k++)
            {
                ParticleSystem layer = MeshLayer(root, "Band" + k, band, bandMat, StormBandLife, 0f, opacity[k], 1f);
                layer.transform.localPosition = new Vector3(0f, 0f, -heights[k]);
                Pop(layer, .94f, 1.03f, .12f, StormBandLife);
                SteadySpin(layer, spin[k]);
                Looping(layer, 5f, 2, 4);
            }
            ParticleSystem top = MeshLayer(root, "Crest", crest, crestMat, StormCrestLife, 0f, 1f, 1.04f);
            top.transform.localPosition = new Vector3(0f, 0f, -.86f);
            Pop(top, .92f, 1.03f, .10f, StormCrestLife);
            SteadySpin(top, -6f);
            Looping(top, 4f, 1, 3);

            ParticleSystem spray = Drops(root, "Spray", dropMat, 30, .35f, .55f, 2.2f, 3.6f, .08f, .14f, 1.6f, 1.2f);
            spray.transform.localPosition = new Vector3(0f, 0f, -.78f);
            CircleShape(spray, 1.02f);
            Looping(spray, 26f, 0, 30);
            ParticleSystem fling = FoamBits(root, "Fling", bitMat, 12, .30f, .45f, 1.0f, 2.0f, .18f, .30f, .4f);
            fling.transform.localPosition = new Vector3(0f, 0f, -.45f);
            CircleShape(fling, 1f);
            Looping(fling, 9f, 0, 12);
            ParticleSystem baseFoam = FoamBits(root, "BaseFoam", bitMat, 16, .35f, .50f, .4f, .9f, .22f, .36f, .3f);
            baseFoam.transform.localPosition = new Vector3(0f, 0f, -.02f);
            CircleShape(baseFoam, .98f);
            Looping(baseFoam, 14f, 0, 16);
            ParticleSystem pulse = Drops(root, "PulseSpray", dropMat, 24, .30f, .48f, 3.0f, 5.0f, .08f, .14f, 1.6f, 1.0f);
            pulse.transform.localPosition = new Vector3(0f, 0f, -.45f);
            CircleShape(pulse, .95f);
            Looping(pulse, 0f, 0, 24);

            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(StormColumnName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Конец Бури: кольцо воды расходится по земле, капли и комья пены летят наружу.</summary>
    private static void SaveStormSplash(Mesh ring, Material ringMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(StormSplashName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindStormSplash, .7f);
            ParticleSystem wave = MeshLayer(root, "Ring", ring, ringMat, StormRingLife, 0f, 1f, 1f);
            wave.transform.localPosition = new Vector3(0f, 0f, -.03f);
            var size = wave.sizeOverLifetime; size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .78f), new Keyframe(.25f, 1.12f), new Keyframe(1f, 1.22f)));
            ParticleSystem drops = Drops(root, "Drops", dropMat, 26, .35f, .60f, 3f, 6f, .08f, .15f, 1.8f, 1.6f);
            drops.transform.localPosition = new Vector3(0f, 0f, -.5f);
            CircleShape(drops, .95f);
            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 14, .35f, .50f, .8f, 1.8f, .20f, .34f, .4f);
            foam.transform.localPosition = new Vector3(0f, 0f, -.03f);
            CircleShape(foam, .95f);
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(StormSplashName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Водоворот (vortex-1). Корень в центре на земле, X90, масштаб — радиус тяги с
    /// запасом. Шесть рукавов одной мешевой частицей: дорастают к центру, крутятся,
    /// сжимаются внутрь, пока Sim тянет, и рассыпаются от края. Струи пены бегут к
    /// центру, комья пены — по рукавам.
    /// </summary>
    private static void SaveMaelstrom(Mesh arms, Material armMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(MaelstromName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindMaelstrom, .9f);
            ParticleSystem layer = MeshLayer(root, "Arms", arms, armMat, MaelstromLife, 0f, 1f, 1f);
            var size = layer.sizeOverLifetime; size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1.10f), new Keyframe(.45f, .96f), new Keyframe(1f, .90f)));
            var main = layer.main;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f);
            var rotation = layer.rotationOverLifetime; rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, MaelstromSpin), new Keyframe(.45f, MaelstromSpin * .35f), new Keyframe(1f, MaelstromSpin * .1f)));

            ParticleSystem streaks = Drops(root, "Streaks", dropMat, 26, .30f, .40f, -9f, -6f, .06f, .10f, 0f, 0f);
            CircleShape(streaks, .92f);
            var streakShape = streaks.shape; streakShape.randomDirectionAmount = 0f;
            var streakEmission = streaks.emission;
            streakEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)14), new ParticleSystem.Burst(.10f, (short)12) });
            var streakRenderer = streaks.GetComponent<ParticleSystemRenderer>();
            streakRenderer.lengthScale = 2.5f;
            streakRenderer.velocityScale = .03f;
            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 16, .40f, .60f, .3f, 1.0f, .20f, .34f, .25f);
            CircleShape(foam, .55f);
            var foamEmission = foam.emission;
            foamEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10), new ParticleSystem.Burst(.12f, (short)6) });
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(MaelstromName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Вращение рукавов Водоворота, рад/с вокруг местной Z (корень X90: +Z вниз).
    /// Рукав закручен против часовой к центру (θ растёт внутрь), значит, чтобы
    /// узор бежал к центру, он крутится по часовой сверху — тот же знак, что у
    /// колец пены (−14: голова впереди по ходу сабли). Пробой не проверено.
    /// </summary>
    private const float MaelstromSpin = -5.5f;

    /// <summary>
    /// Кольцо Пенных волн (waves-2). Корень в центре колец на земле, X90; масштаб —
    /// фронт кольца, его каждый кадр ставит вид по числам Sim. Жизнь частицы кольца
    /// тоже ставит вид (ход кольца + рассыпание); брызги и комья по фронту идут,
    /// пока кольцо бежит.
    /// </summary>
    private static void SaveFoamWave(Mesh ring, Material waveMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(FoamWaveName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindFoamWave, .9f);
            MeshLayer(root, "Ring", ring, waveMat, .5f, 0f, 1f, 1f);
            ParticleSystem spray = Drops(root, "Spray", dropMat, 60, .25f, .40f, 1.5f, 3.0f, .06f, .11f, 1.6f, 1.4f);
            CircleShape(spray, 1f);
            Looping(spray, 70f, 0, 60);
            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 30, .30f, .45f, .2f, .6f, .16f, .26f, .3f);
            CircleShape(foam, .97f);
            Looping(foam, 36f, 0, 30);
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(FoamWaveName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Корона брызг у ног врага (удар кольца, оглушение Водоворота): корень на земле,
    /// местная +Z — куда клонится корона. Пятно воды, белые языки вверх веером,
    /// вырезанные капли выше и шире, комья пены кольцом — как корона рывка, крупнее.
    /// </summary>
    private static void SaveCrownSplash(Material splatMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(CrownSplashName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindCrownSplash, .55f);
            ParticleSystem puddle = PelagWhirlwindVfxSetup.NewParticles(root, "Puddle", 1, .34f, .34f, 0f, 0f, .85f, .85f);
            var puddleMain = puddle.main;
            puddleMain.startColor = DropAqua;
            puddleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var puddleShape = puddle.shape; puddleShape.enabled = false;
            var puddleSize = puddle.sizeOverLifetime; puddleSize.enabled = true;
            puddleSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.18f, 1.02f), new Keyframe(1f, 1.08f)));
            BlobRenderer(puddle, splatMat, ParticleSystemRenderMode.HorizontalBillboard);
            puddle.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            ParticleSystem crown = PelagWhirlwindVfxSetup.NewParticles(root, "Crown", 14, .24f, .32f, 2.6f, 3.6f, .09f, .12f);
            var crownMain = crown.main;
            crownMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            UpCone(crown, 45f, .22f, -82f);
            Brake(crown, 10f);
            var crownSize = crown.sizeOverLifetime; crownSize.enabled = true;
            crownSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .6f), new Keyframe(.15f, 1f), new Keyframe(1f, .55f)));
            SoftEllipse(crown);
            BlobRenderer(crown, dropMat, ParticleSystemRenderMode.Stretch);
            var crownRenderer = crown.GetComponent<ParticleSystemRenderer>();
            crownRenderer.lengthScale = 8f;
            crownRenderer.velocityScale = 0f;
            crownRenderer.sortingFudge = -2f;

            ParticleSystem drops = PelagWhirlwindVfxSetup.NewParticles(root, "Drops", 18, .30f, .42f, 4.5f, 6.5f, .07f, .11f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 2f;
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            UpCone(drops, 55f, .16f, -76f);
            Brake(drops, 2f);
            SoftEllipse(drops);
            BlobRenderer(drops, dropMat, ParticleSystemRenderMode.Billboard);
            drops.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;

            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 8, .30f, .42f, .3f, .9f, .20f, .30f, .4f);
            var foamShape = foam.shape;
            foamShape.enabled = true;
            foamShape.shapeType = ParticleSystemShapeType.Circle;
            foamShape.radius = .2f;
            foamShape.radiusThickness = 0f;
            foamShape.rotation = new Vector3(90f, 0f, 0f);
            foam.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(CrownSplashName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ----------------------------------------------------------------- layers

    /// <summary>Петля, пока её не остановит вид: rate в секунду, стартовая вспышка burst, потолок частиц.</summary>
    private static void Looping(ParticleSystem particles, float rate, int burst, int max)
    {
        var main = particles.main;
        main.loop = true;
        main.duration = StormLoopSeconds;
        main.maxParticles = Mathf.Max(1, max);
        var emission = particles.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);
    }

    /// <summary>Ровное вращение витка вокруг местной Z со случайным стартовым углом; к концу жизни — 70%.</summary>
    private static void SteadySpin(ParticleSystem layer, float radPerSecond)
    {
        var main = layer.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var rotation = layer.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
        rotation.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, radPerSecond), new Keyframe(1f, radPerSecond * .7f)));
    }

    /// <summary>Конус вокруг вертикали, наклонённый вперёд по +Z корня: −90 — строго вверх.</summary>
    private static void UpCone(ParticleSystem particles, float angle, float radius, float pitch)
    {
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Линейное торможение: брызги встают рывком и зависают.</summary>
    private static void Brake(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(20f);
        limit.drag = new ParticleSystem.MinMaxCurve(drag);
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }
}
