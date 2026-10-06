using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
    /// Водоворот v3 (vortex-1; владелец 02.10, вечер). Корень — центр на земле,
    /// без поворота и масштаба. «Arms» — меш живой воды: его каждый кадр пишет
    /// вид (PelagMaelstromWater) на радиусе удара Вихря — рукава плавно
    /// наматываются внутрь и после контакта рвутся на капли. «Strands» (v4) —
    /// изогнутые струи воды тяги с края 4 м внутрь (меш пишет вид,
    /// PelagMaelstromStrands; рисуются поверх рукавов). «TipDrops» (v4) —
    /// капли, слетающие с внешних концов рукавов. «Foam» — комья пены по
    /// рукавам в контакт. Своих выбросов у частиц нет — их задаёт вид.
    /// </summary>
    private static void SaveMaelstrom(Material armMat, Material strandMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(MaelstromName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindMaelstrom, MaelstromLife);
            WaterLayer(root, "Arms", armMat);
            WaterLayer(root, "Strands", strandMat).sortingOrder = 1;
            ParticleSystem tips = Drops(root, "TipDrops", dropMat, 60, .30f, .45f, 0f, 0f, .10f, .16f, 1.6f, 0f);
            ByView(tips, MaelstromLife);
            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 24, .35f, .50f, 0f, 0f, .18f, .30f, .3f);
            ByView(foam, MaelstromLife);
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(MaelstromName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Кольцо Пенных волн v3 (waves-2; владелец 02.10, вечер). Корень — центр колец
    /// на земле, без поворота и масштаба. «Ring» — меш живой воды, его каждый кадр
    /// пишет вид (PelagFoamRingWater): волнистый гребень бежит по числам Sim с
    /// замедлением и рвётся на капли. «Spray» и «Foam» — брызги и комья с гребня,
    /// их пускает вид, пока гребень бежит, и выбросом при разрыве.
    /// </summary>
    private static void SaveFoamWave(Material waveMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(FoamWaveName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindFoamWave, 1.2f);
            WaterLayer(root, "Ring", waveMat);
            // v4: капли и клочья крупнее и реже — на waves-2 крупные капли, а не крошка.
            ParticleSystem spray = Drops(root, "Spray", dropMat, 100, .30f, .45f, 0f, 0f, .09f, .16f, 1.6f, 0f);
            ByView(spray, 1.6f);
            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 50, .30f, .45f, 0f, 0f, .22f, .36f, .3f);
            ByView(foam, 1.6f);
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(FoamWaveName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// След тяги Водоворота: копия префаба следа рывка (полоса «Wake», занос
    /// SkidFoam/SkidDrops, капли хвоста TailDrops) со своим материалом в цветах
    /// Водоворота. Полосу и выбросы ведёт вид кодом следа рывка (PelagDashWake)
    /// по настоящему пути притянутого тела. Префаб рывка только читается.
    /// </summary>
    private static void SaveMaelstromDrag(Material dragMat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(DashWakePrefab);
        try
        {
            root.name = MaelstromDragName;
            var element = root.GetComponent<PelagVfxElement>();
            if (element == null) element = root.AddComponent<PelagVfxElement>();
            element.Id = PelagVfxId.WhirlwindMaelstromDrag;
            element.DefaultLifetime = PelagDashWake.MaxLife;
            Transform strip = root.transform.Find("Wake");
            if (strip != null && strip.TryGetComponent(out MeshRenderer renderer)) renderer.sharedMaterial = dragMat;
            // v4: борозда за телом врага, а не метровая полоса рывка у ног героя («фиолетовые плиты»).
            if (strip != null) strip.localScale = new Vector3(.55f, 1f, 1f);
            if (strip != null && strip.TryGetComponent(out MeshFilter filter)) filter.sharedMesh = null;
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(MaelstromDragName));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
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

    /// <summary>
    /// Удар кольца Пенных волн по врагу (v4, waves-2: «маленькие короны у ног, их
    /// съедают вспышка удара и трава»): большой веер брызг выше пояса. Корень — у ног
    /// врага, вид сдвигает его к камере вдоль луча (на экране там же, рисуется перед
    /// телом); местная +Z — наружу от центра колец, веер наклонён туда. Лужа воды
    /// формы у ног, белые языки воды веером вверх-наружу, комья пены в теле веера,
    /// крупные капли дугой, клочья пены по земле. Корона у ног осталась Водовороту.
    /// </summary>
    private static void SaveWaveSplash(Material splatMat, Material dropMat, Material bitMat)
    {
        var root = new GameObject(WaveSplashName);
        try
        {
            FormElement(root, PelagVfxId.WhirlwindWaveSplash, .8f);
            ParticleSystem puddle = PelagWhirlwindVfxSetup.NewParticles(root, "Puddle", 1, .40f, .40f, 0f, 0f, 1.15f, 1.15f);
            var puddleMain = puddle.main;
            puddleMain.startColor = DropAqua;
            puddleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var puddleShape = puddle.shape; puddleShape.enabled = false;
            var puddleSize = puddle.sizeOverLifetime; puddleSize.enabled = true;
            puddleSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .5f), new Keyframe(.15f, 1f), new Keyframe(1f, 1.1f)));
            BlobRenderer(puddle, splatMat, ParticleSystemRenderMode.HorizontalBillboard);
            puddle.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;

            // Языки: вытянутые белые капли веером вверх и наружу, тормозят и зависают выше пояса.
            // Проба v4a: языки 0,10–0,15 со скоростью 7–9,5 читались тонкими голубыми нитками у колен.
            ParticleSystem fan = PelagWhirlwindVfxSetup.NewParticles(root, "Fan", 22, .32f, .46f, 9.5f, 12.5f, .16f, .24f);
            var fanMain = fan.main;
            fanMain.gravityModifier = 1.0f;
            fanMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            UpCone(fan, 34f, .30f, -64f);
            Brake(fan, 6f);
            var fanSize = fan.sizeOverLifetime; fanSize.enabled = true;
            fanSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .55f), new Keyframe(.18f, 1f), new Keyframe(1f, .6f)));
            SoftEllipse(fan);
            BlobRenderer(fan, dropMat, ParticleSystemRenderMode.Stretch);
            var fanRenderer = fan.GetComponent<ParticleSystemRenderer>();
            fanRenderer.lengthScale = 4.5f;
            fanRenderer.velocityScale = 0f;
            fanRenderer.sortingFudge = -2f;

            // Тело веера: крупные комья пены поднимаются по нему.
            ParticleSystem sheet = FoamBits(root, "Sheet", bitMat, 10, .32f, .46f, 4.2f, 6.5f, .30f, .46f, .5f);
            UpCone(sheet, 28f, .25f, -66f);
            Brake(sheet, 5f);
            sheet.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;

            // Крупные капли дугой наружу (на waves-2 — капли-«слёзы» до полуметра от тела).
            ParticleSystem drops = Drops(root, "Drops", dropMat, 16, .45f, .65f, 5.5f, 8.0f, .11f, .18f, 2.0f, 0f);
            UpCone(drops, 50f, .25f, -60f);
            Brake(drops, 1.0f);
            drops.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;

            ParticleSystem foam = FoamBits(root, "Foam", bitMat, 8, .32f, .46f, .5f, 1.2f, .22f, .34f, .4f);
            var foamShape = foam.shape;
            foamShape.enabled = true;
            foamShape.shapeType = ParticleSystemShapeType.Circle;
            foamShape.radius = .3f;
            foamShape.radiusThickness = 0f;
            foamShape.rotation = new Vector3(90f, 0f, 0f);
            foam.GetComponent<ParticleSystemRenderer>().sortingFudge = -1f;
            PrefabUtility.SaveAsPrefabAsset(root, FormPrefabPath(WaveSplashName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ----------------------------------------------------------------- layers

    /// <summary>Меш живой воды: MeshFilter без меша (его создаёт и пишет вид) и рендерер без теней.</summary>
    private static MeshRenderer WaterLayer(GameObject root, string name, Material material)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root.transform, false);
        host.AddComponent<MeshFilter>();
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return renderer;
    }

    /// <summary>Частицы без своих выбросов: их пускает вид (Emit) всё время жизни объекта.</summary>
    private static void ByView(ParticleSystem particles, float seconds)
    {
        var main = particles.main;
        main.loop = false;
        main.duration = Mathf.Max(.2f, seconds);
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        var shape = particles.shape; shape.enabled = false;
    }

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
