using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Префабы Абордажа v2 (см. PelagAbordageFoamVfxSetup.cs). Любая правка — поднять Version там.</summary>
public static partial class PelagAbordageFoamVfxSetup
{
    /// <summary>
    /// Лента воды (цепь, росчерк якоря): корень без поворота и масштаба, меш «Water» пишет
    /// вид (PelagAbordageRibbon). Капли «Drops» и комья «Foam» — мировые, без своих вспышек:
    /// их выбрасывает контроллер (с цепи в натяг, за головой в полёте, на зацепе и ударе).
    /// </summary>
    private static void SaveRibbonPrefab(Material ribbon, Material foam, Material drop)
    {
        var root = new GameObject(RibbonName);
        try
        {
            AddElement(root, PelagVfxId.AbordageRibbon, 6f, 1f);
            MeshLayer(root, "Water", ribbon);

            ParticleSystem drops = Emitted(root, "Drops", 240, .40f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startSize = new ParticleSystem.MinMaxCurve(.035f, .07f);
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(drops);
            Drag(drops, 1.5f);
            Renderer(drops, drop, ParticleSystemRenderMode.Stretch);
            var dropsRenderer = drops.GetComponent<ParticleSystemRenderer>();
            dropsRenderer.lengthScale = 1.4f;
            dropsRenderer.velocityScale = .03f;

            ParticleSystem bits = Emitted(root, "Foam", 60, .45f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .6f;
            bitsMain.startSize = new ParticleSystem.MinMaxCurve(.08f, .14f);
            bitsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(bits, .6f, 1f, .5f);
            Drag(bits, 3f);
            Renderer(bits, foam, ParticleSystemRenderMode.Billboard);

            Save(root, RibbonName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Обвал (кадр D): корень — центр на земле, меш «Ring» — всплеск Обвала (круг 4: PelagAbordageQuakeWater,
    /// материал M_Abordage_Quake на шейдере Razlom/Abordage Quake Splash). Выбрасываемые: «Spray» — капли с
    /// кончиков лопастей (круг 4: больше), «Foam» — круглые белые клочья (круг 4: капля пака вместо кляксы
    /// рывка с лучами), «Clods» — комья земли по кругу (бросает вид; круг 4: слоя полос грязи «Dirt» больше нет — лучи
    /// мокрой земли рисует всплеск).
    /// Разовые на выдаче (кулак в землю): «Slam» — кобальтовые капли наружу, «SlamFoam» — круглые капли
    /// кобальта низко (круг 3: не белый веер над героем).
    /// </summary>
    private static void SaveQuakePrefab(Material quake, Material crownDrop, Material clod)
    {
        var root = new GameObject(QuakeName);
        try
        {
            AddElement(root, PelagVfxId.AbordageQuake, 1f, 3f);
            MeshLayer(root, "Ring", quake);
            EmittedSpray(root, "Spray", crownDrop, 260, .45f, .08f, .15f, 1.6f);
            // Круг 4 (кадр D — белые и синие капли наружу): круглые клочья, не клякса с лучами.
            ParticleSystem bits = Emitted(root, "Foam", 140, .45f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .9f;
            bitsMain.startSize = new ParticleSystem.MinMaxCurve(.12f, .22f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            DropSheet(bits);
            Drag(bits, 2f);
            SizeCurve(bits, .7f, 1f, .6f);
            Renderer(bits, crownDrop, ParticleSystemRenderMode.Billboard);

            // Комья земли бросает вид (PelagVfxController.AbordageQuakeImpact) — низкой дугой по всему
            // кругу, падают внутри радиуса Sim; гравитация здесь = расчёт полёта там (×2,4). Круг 3:
            // сплошной гранёный камень пака (M_Abordage_Clod, случайная клетка 3×3), кувыркается.
            ParticleSystem clods = Emitted(root, "Clods", 40, .5f);
            var clodsMain = clods.main;
            clodsMain.gravityModifier = PelagAbordageVfxRules.QuakeClodGravity;
            clodsMain.startSize = new ParticleSystem.MinMaxCurve(PelagAbordageVfxRules.QuakeClodSizeMin, PelagAbordageVfxRules.QuakeClodSizeMax);
            clodsMain.startColor = new ParticleSystem.MinMaxGradient(ClodDark, ClodLight);
            AtlasRandom(clods, 3);
            Tumble(clods, 540f);
            var clodsSize = clods.sizeOverLifetime; clodsSize.enabled = true;
            clodsSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .7f), new Keyframe(.12f, 1f), new Keyframe(.85f, 1f), new Keyframe(1f, .55f)));
            Renderer(clods, clod, ParticleSystemRenderMode.Billboard);

            // Круг 3: капли кобальта наружу кольцом вокруг героя, низко (не столб вверх над ним).
            ParticleSystem slam = Burst(root, "Slam", 10, .30f, .45f, 3f, 5f, .07f, .11f, .5f);
            var slamMain = slam.main;
            slamMain.gravityModifier = 2f;
            slamMain.startColor = new ParticleSystem.MinMaxGradient(QuakeDropLight, QuakeDropBlue);
            Cone(slam, 65f, .45f, -90f);
            DropSheet(slam);
            Renderer(slam, crownDrop, ParticleSystemRenderMode.Stretch);
            var slamRenderer = slam.GetComponent<ParticleSystemRenderer>();
            slamRenderer.lengthScale = 1.6f;
            slamRenderer.velocityScale = .03f;

            // Круг 3: не сливочные кляксы над кулаком, а несколько круглых капель кобальта у земли.
            ParticleSystem slamFoam = Burst(root, "SlamFoam", 6, .35f, .50f, 1.5f, 3f, .12f, .20f, .55f);
            var slamFoamMain = slamFoam.main;
            slamFoamMain.gravityModifier = .8f;
            slamFoamMain.startColor = new ParticleSystem.MinMaxGradient(QuakeDropBlue, QuakeDropLight);
            Cone(slamFoam, 78f, .45f, -90f);
            Drag(slamFoam, 4f);
            DropSheet(slamFoam);
            Renderer(slamFoam, crownDrop, ParticleSystemRenderMode.Billboard);

            Save(root, QuakeName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Пробоина (кадр G): корень — вершина струи на земле, меш «Jet» (PelagAbordageJetWater).
    /// «Spray» — капли-лепестки веером с носа струи (вытянуты по скорости), «Foam» — клочья с краёв.
    /// </summary>
    private static void SaveBreachPrefab(Material ground, Material foam, Material crownDrop)
    {
        var root = new GameObject(BreachName);
        try
        {
            AddElement(root, PelagVfxId.AbordageBreach, 1f, 4f);
            MeshLayer(root, "Jet", ground);
            ParticleSystem petals = EmittedSpray(root, "Spray", crownDrop, 160, .50f, .08f, .15f, 1.4f);
            petals.GetComponent<ParticleSystemRenderer>().lengthScale = 1.6f;
            EmittedFoam(root, "Foam", foam, 80, .45f, .16f, .28f);
            Save(root, BreachName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Гейзер (кадр F): корень — основание на земле. «Column» — столб к камере
    /// (PelagAbordageColumnWater), «Ring» — кольцо падения на земле (PelagAbordageRingWater, круг 3 —
    /// тонкое M_Abordage_Fall), «BaseRing» — розетка у основания (Style.Rosette). Выбрасываемые:
    /// «Crown» — клубы пены (круг 4: слитыми массами — заливка M_Abordage_FoamFill, тот же клуб силуэтом в
    /// «CrownBack» на M_Abordage_FoamBack, вид выбрасывает их парой; облако вокруг подброшенной цели и кромка
    /// розетки), «Spray» — капли розетки, струи по стволу и капли кольца при падении. Разовый на выдаче
    /// (удар из земли): «Base» — брызги вверх у основания (круг 4: «BaseFoam» убран — его «яйца» кругом
    /// заменили клубы кромки, которые выбрасывает вид). Розетка «BaseRing» — M_Abordage_Rosette.
    /// </summary>
    private static void SaveGeyserPrefab(Material column, Material rosette, Material fall, Material crownDrop,
        Material foamFill, Material foamBack)
    {
        var root = new GameObject(GeyserName);
        try
        {
            AddElement(root, PelagVfxId.AbordageGeyser, 1.6f, 2f);
            MeshLayer(root, "Column", column);
            MeshLayer(root, "Ring", fall);
            // Розетка у основания (круглые лепестки, круг 4 — сплошной диск, PelagAbordageRingWater.Style.Rosette).
            MeshLayer(root, "BaseRing", rosette);
            // Круг 4: клубы слитыми массами — силуэт раньше в очереди, заливка без обвода; слои ведут клуб одинаково.
            PuffLayer(root, "CrownBack", foamBack);
            PuffLayer(root, "Crown", foamFill);
            // Круг 2: + струи капель вверх по стволу (~20 живых) поверх 40 капель кольца падения.
            EmittedSpray(root, "Spray", crownDrop, 200, .50f, .07f, .13f, 2f);

            ParticleSystem jet = Burst(root, "Base", 16, .35f, .50f, 3.5f, 6f, .08f, .13f, .5f);
            var jetMain = jet.main;
            jetMain.gravityModifier = 1.8f;
            jetMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            Cone(jet, 25f, .35f, -90f);
            DropSheet(jet);
            Renderer(jet, crownDrop, ParticleSystemRenderMode.Stretch);
            var jetRenderer = jet.GetComponent<ParticleSystemRenderer>();
            jetRenderer.lengthScale = 2.2f;
            jetRenderer.velocityScale = .03f;

            Save(root, GeyserName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Круг 4: слой клубов пены Гейзера (заливка или силуэт) — одни и те же модули у обоих, без случайных
    /// величин внутри слоя: место, скорость, размер, поворот, срок и цвет задаёт выброс вида (EmitParams),
    /// поэтому пара слоёв ведёт каждый клуб одинаково. Почти круглые, медленные, держатся у тела.
    /// </summary>
    private static void PuffLayer(GameObject root, string name, Material material)
    {
        ParticleSystem puffs = Emitted(root, name, PelagAbordageVfxRules.GeyserPuffLayerMax, .55f);
        var main = puffs.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f);
        RoundPuff(puffs, .30f, .30f, 1.45f);
        DropSheet(puffs);
        Drag(puffs, PelagAbordageVfxRules.GeyserPuffDrag);
        SizeCurve(puffs, .6f, 1f, .75f);
        Renderer(puffs, material, ParticleSystemRenderMode.Billboard);
    }

    /// <summary>Выбрасываемые капли: вытянуты по скорости, падают, тают порогом (лист капель пака).</summary>
    private static ParticleSystem EmittedSpray(GameObject root, string name, Material material, int max, float life,
        float sizeMin, float sizeMax, float gravity)
    {
        ParticleSystem spray = Emitted(root, name, max, life);
        var main = spray.main;
        main.gravityModifier = gravity;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
        DropSheet(spray);
        Drag(spray, 1f);
        Renderer(spray, material, ParticleSystemRenderMode.Stretch);
        var renderer = spray.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 1.5f;
        renderer.velocityScale = .025f;
        return spray;
    }

    /// <summary>Выбрасываемые клочья пены: клякса пака, случайный поворот, рост и сход.</summary>
    private static ParticleSystem EmittedFoam(GameObject root, string name, Material material, int max, float life,
        float sizeMin, float sizeMax)
    {
        ParticleSystem bits = Emitted(root, name, max, life);
        var main = bits.main;
        main.gravityModifier = .3f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
        SizeCurve(bits, .6f, 1f, .5f);
        Drag(bits, 2.5f);
        Renderer(bits, material, ParticleSystemRenderMode.Billboard);
        return bits;
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
