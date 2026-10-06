using System.Collections.Generic;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// «Терновник» — префабы (V17, ThicketMasterVfxSetup.Seeds.cs; заменил веер V16). Корни: стручок — в нуле мира (ставит вид);
/// куст, прорастание и выпуск — центр куста на земле, +Z — линия 0 креста (линия k — поворот −90°·k, ThicketMasterSeedRules);
/// попадание и конец линии — на земле в точке остановки, +Z — ход шипа (назад, к кусту, — −Z). Каждая система — один залп
/// (≤ 8 залпов на систему), Color32 у мешей.
/// </summary>
public static partial class ThicketMasterVfxSetup
{
    /// <summary>
    /// Стручок в полёте (ведёт вид): «Pod» — сетка ThicketSeedPod (кора + тлеющие кончики, тень на поляну), «Ribbon» —
    /// лента следа (LineRenderer, точки пишет вид), системы следа без эмиссии и своего времени — частицы ставит вид:
    /// пыль (облако CFXR), листья (меш листа CFXR), щепки коры (CFXR debris wood 3×3), угольки и ореол (свечение).
    /// </summary>
    private static void SaveSeedPod(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SeedPodName);
        var pod = Child(root, ThicketMasterCombatView.SeedPodChild, Vector3.zero);
        pod.transform.localScale = Vector3.one * ThicketMasterSeedRules.PodScale;
        pod.AddComponent<MeshFilter>().sharedMesh = kit.SeedPod;
        var renderer = pod.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { kit.SeedWood, kit.SeedTip };
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;

        var ribbon = Child(root, ThicketMasterCombatView.SeedRibbonChild, Vector3.zero).AddComponent<LineRenderer>();
        ribbon.sharedMaterial = kit.SeedRibbon;
        ribbon.useWorldSpace = true;
        ribbon.positionCount = ThicketMasterCombatView.SeedRibbonPoints;
        ribbon.widthMultiplier = .2f;
        ribbon.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.7f, .7f), new Keyframe(1f, 1f));
        ribbon.textureMode = LineTextureMode.Stretch;
        ribbon.alignment = LineAlignment.View;
        ribbon.shadowCastingMode = ShadowCastingMode.Off;
        ribbon.receiveShadows = false;
        ribbon.lightProbeUsage = LightProbeUsage.Off;
        ribbon.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ribbon.enabled = false;

        var dust = SeedTrailSystem(root, ThicketMasterCombatView.SeedDustSystem, kit.Haze, 40, null);
        var dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
        dustRenderer.flip = new Vector3(.5f, .5f, 0f);
        dustRenderer.sortingFudge = 1f;
        SeedTrailSystem(root, ThicketMasterCombatView.SeedLeafSystem, kit.Leaf, 24, kit.LeafMesh);
        var splinters = SeedTrailSystem(root, ThicketMasterCombatView.SeedSplinterSystem, kit.Splinter, 16, null);
        Sheet(splinters, 3, 8.99f);
        var motes = SeedTrailSystem(root, ThicketMasterCombatView.SeedMoteSystem, kit.Glow, 32, null);
        motes.GetComponent<ParticleSystemRenderer>().sortingFudge = -2f;
        Save(root);
    }

    /// <summary>
    /// Система следа стручка: в мире, без эмиссии, формы и модулей по жизни — частицы ставит вид (SetParticles), срок им
    /// пишет он же. mesh — меш-частицы (3D-поворот частицы, выравнивание по миру), иначе билборды.
    /// </summary>
    private static ParticleSystem SeedTrailSystem(GameObject root, string name, Material material, int capacity, Mesh mesh)
    {
        var particles = Particles(root, name, capacity, 10f, 10f, 0f, 0f, .1f, .1f, 0f);
        World(particles);
        var main = particles.main;
        main.duration = 1f;
        main.maxParticles = capacity;
        main.gravityModifier = 0f;
        main.startRotation3D = mesh != null;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 0f;
        emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        if (mesh != null)
        {
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
        }
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    // ------------------------------------------------------------- куст

    /// <summary>Листва куста — падуб поляны ×BushCoreScale (~1,15 × 1,1 м, верх ~1,05 м): куст в Sim — круг r 0,6.</summary>
    private const float BushCoreScale = 1.15f;

    /// <summary>Шипы линий: основание в BushLaneRoot м от центра на BushLaneHeight над землёй, подъём над горизонталью, длина, толщина.</summary>
    private const float BushLaneRoot = .12f, BushLaneHeight = .42f, BushLaneElevation = 14f, BushLaneLength = .95f, BushLaneThick = .95f;

    /// <summary>Стеблей с шипами: по два на диагональ креста и два почти стоячих в середине.</summary>
    private const int BushCaneCount = 10;

    /// <summary>
    /// Куст терновника (вид ставит корень на центр куста, +Z — линия 0; позу частей, тон и частицы пишет вид по срокам Sim):
    /// «Core» — падуб поляны (копии меша и материала — ветер и фактура как у кустов арены), «Lanes/Lane 0…3» — четыре больших
    /// шипа ThornSpire по линиям креста (основание в листве, остриё наружу и чуть вверх, над устьем стручка), «Canes/Cane …» —
    /// колючие стебли Шипомёта по диагоналям и в середине. Системы без эмиссии (частицы ставит вид): «Bush Ground» — пятно
    /// разрытой земли (атлас dirt_0–5), «Bush Cracks» — рваная звезда трещин, «Bush Leaves» — листья (меш листа CFXR),
    /// «Bush Motes» — угольки на кончиках шипов, «Bush Glint» — звёздочка блика, «Bush Dust» — пыль у основания.
    /// </summary>
    private static void SaveBush(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BushName);
        var core = Child(root, ThicketMasterCombatView.BushCoreChild, Vector3.zero);
        // Меш падуба — Z вверх (как в префабе CreatingBush): поворот −90° по X.
        core.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        core.transform.localScale = Vector3.one * BushCoreScale;
        core.AddComponent<MeshFilter>().sharedMesh = kit.BushLeafMesh;
        var leaves = core.AddComponent<MeshRenderer>();
        leaves.sharedMaterial = kit.BushLeaves;
        leaves.shadowCastingMode = ShadowCastingMode.On;
        leaves.receiveShadows = true;
        leaves.lightProbeUsage = LightProbeUsage.BlendProbes;
        leaves.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var lanes = Child(root, ThicketMasterCombatView.BushLanesChild, Vector3.zero).transform;
        float rise = BushLaneElevation * Mathf.Deg2Rad;
        for (int k = 0; k < Simulation.ThicketBushLanes; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, ThicketMasterSeedRules.LaneYawDegrees(k), 0f) * Vector3.forward;
            Vector3 axis = dir * Mathf.Cos(rise) + Vector3.up * Mathf.Sin(rise);
            BushThorn(lanes, ThicketMasterCombatView.BushLanePrefix + k, kit.BushSpire, kit.SeedWood,
                dir * BushLaneRoot + Vector3.up * BushLaneHeight, axis, BushLaneLength, BushLaneThick, 40f + 70f * k);
        }

        var canes = Child(root, ThicketMasterCombatView.BushCanesChild, Vector3.zero).transform;
        var meshes = kit.BushCanes ?? new Mesh[0];
        for (int i = 0; i < BushCaneCount && meshes.Length > 0; i++)
        {
            bool middle = i >= BushCaneCount - 2;
            // Диагонали креста (между линиями): 45° + 90°·k, по два стебля с разбросом; середина — почти стоят.
            float yaw = middle ? Hash01(i, 1601) * 360f : 45f + 90f * (i % 4) + (i < 4 ? -14f : 14f) + (Hash01(i, 1602) - .5f) * 16f;
            float tilt = middle ? Mathf.Lerp(8f, 18f, Hash01(i, 1603)) : Mathf.Lerp(26f, 50f, Hash01(i, 1604));
            float r0 = middle ? Mathf.Lerp(.02f, .1f, Hash01(i, 1605)) : Mathf.Lerp(.1f, .26f, Hash01(i, 1606));
            float length = middle ? Mathf.Lerp(1.05f, 1.3f, Hash01(i, 1607)) : Mathf.Lerp(.75f, 1.15f, Hash01(i, 1608));
            float thick = Mathf.Lerp(.7f, .95f, Hash01(i, 1609));
            float y = yaw * Mathf.Deg2Rad, t = tilt * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y));
            var axis = outward * Mathf.Sin(t) + Vector3.up * Mathf.Cos(t);
            BushThorn(canes, "Cane " + i, meshes[i % meshes.Length], kit.SeedWood, outward * r0 + Vector3.down * .06f, axis, length, thick,
                Hash01(i, 1610) * 360f);
        }

        var ground = BushSystem(root, ThicketMasterCombatView.BushGroundSystem, kit.DirtPatch, 1, null, ParticleSystemRenderMode.HorizontalBillboard, 5f);
        var sheet = ground.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 3; sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 5.99f);
        sheet.cycleCount = 1;
        ground.GetComponent<ParticleSystemRenderer>().receiveShadows = true;
        BushSystem(root, ThicketMasterCombatView.BushCrackSystem, kit.CrackRadial, 1, null, ParticleSystemRenderMode.HorizontalBillboard, 4f);
        BushSystem(root, ThicketMasterCombatView.BushLeafSystem, kit.Leaf, 24, kit.LeafMesh, ParticleSystemRenderMode.Mesh, 0f);
        BushSystem(root, ThicketMasterCombatView.BushMoteSystem, kit.Glow, 16, null, ParticleSystemRenderMode.Billboard, -2f);
        BushSystem(root, ThicketMasterCombatView.BushGlintSystem, kit.StarMote, Simulation.ThicketBushLanes, null, ParticleSystemRenderMode.Billboard, -3f);
        var dust = BushSystem(root, ThicketMasterCombatView.BushDustSystem, kit.Haze, 10, null, ParticleSystemRenderMode.Billboard, -1f);
        dust.GetComponent<ParticleSystemRenderer>().flip = new Vector3(.5f, .5f, 0f);
        Save(root);
    }

    /// <summary>Шип куста — MeshRenderer: основание в at, ось меша +Y — по axis, длина length (м), толщина thick, поворот вокруг оси roll.</summary>
    private static void BushThorn(Transform parent, string name, Mesh mesh, Material material, Vector3 at, Vector3 axis, float length, float thick,
        float roll)
    {
        if (mesh == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized) * Quaternion.AngleAxis(roll, Vector3.up);
        go.transform.localScale = new Vector3(thick, length, thick);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>Система куста без эмиссии (частицы ставит вид), в мире; mode — билборд, лёжа на земле или меш.</summary>
    private static ParticleSystem BushSystem(GameObject root, string name, Material material, int capacity, Mesh mesh,
        ParticleSystemRenderMode mode, float sortingFudge)
    {
        var particles = SeedTrailSystem(root, name, material, capacity, mesh);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (mesh == null) renderer.renderMode = mode;
        renderer.sortingFudge = sortingFudge;
        return particles;
    }

    /// <summary>
    /// Прорастание (корень — центр куста на земле, тик — прорастание Sim): земля рвётся — плиты дёрна вскидываются и падают
    /// наружу, комья каменистой земли, зерно, рваная трава, низкая пыль, щепки, пара листьев и угольков. Пятно и трещины под
    /// кустом ведёт вид (живут, пока живёт куст).
    /// </summary>
    private static void SaveBushSprout(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BushSproutName);
        var ground = root.transform;
        var slabs = EarthChunks(root, ground, "Slabs", kit.Slabs, kit.Turf, 6, 1.6f, 2.8f, .3f, .55f, 1.6f, .9f, 1.3f, 6f, .45f, .2f, .35f,
            Color.white, new Color(.84f, .82f, .8f));
        SlabSize(slabs, .28f, .55f, .45f, .8f);
        FromPoints(slabs, AroundPoints("ThicketBushSlabSpots", 6, 1501, .3f, .55f, .04f, 55f, 75f), false, .15f);
        StonyClods(root, ground, kit, "Clods", 22, AroundPoints("ThicketBushClodSpots", 22, 1511, .15f, .55f, .08f, 50f, 80f), 2.2f, 4.2f,
            .05f, .14f, 1.6f, 0f);
        var grains = EarthGrains(root, ground, kit, "Grains", 24, 2f, 4.5f, 0f);
        FromPoints(grains, AroundPoints("ThicketBushGrainSpots", 24, 1521, .1f, .5f, .05f, 45f, 85f), false, .4f);
        EarthDust(root, kit, "Dust", 8, AroundPoints("ThicketBushDustSpots", 8, 1531, .35f, .6f, .15f, 5f, 20f), 1f, 2f, .6f, 1f, .45f, .8f, .4f, 0f);
        TornGrass(root, ground, kit, "Torn Grass", 6, AroundPoints("ThicketBushTuftSpots", 6, 1541, .35f, .6f, .04f, 50f, 70f), 1.6f, 2.8f, 0f);
        Debris(root, ground, "Splinters", kit.Splinter, 6, Vector3.up * .2f, Vector3.up, 45f, .3f, 2.4f, 4f, .08f, .16f, 1.3f, .05f,
            BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 5, Vector3.up * .4f, Vector3.up, 60f, 1.6f, 3f, .06f);
        Embers(root, kit, "Embers", 6, Vector3.up * .2f, .3f, 2f, 4f, .7f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        Save(root);
    }

    /// <summary>
    /// Выпуск (корень — центр куста, +Z — линия 0, тик — выпуск Sim): из устьев четырёх линий — листья, щепки коры и искры
    /// вперёд по линиям, короткий уголь в середине и облачко листвы.
    /// </summary>
    private static void SaveBushLaunch(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BushLaunchName);
        var ground = root.transform;
        var mouths = LaneMouthPoints("ThicketBushLaunchPoints", ThicketMasterSeedRules.MouthRadius * .8f, ThicketMasterSeedRules.MouthLift + .05f, 12f);
        var leaves = Leaves(root, ground, kit, "Leaves", 16, Vector3.zero, Vector3.up, 0f, 2.2f, 4.2f, 0f);
        FromPoints(leaves, mouths, false, .25f);
        var splinters = Debris(root, ground, "Splinters", kit.Splinter, 16, Vector3.zero, Vector3.up, 0f, .1f, 3f, 5.5f, .06f, .13f, 1.3f, 0f,
            BarkLight, BarkDark);
        FromPoints(splinters, mouths, false, .2f);
        var sparks = Embers(root, kit, "Sparks", 12, Vector3.zero, .1f, 2.5f, 4.5f, .45f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        FromPoints(sparks, mouths, false, .2f);
        Flash(root, kit, "Flash", Vector3.up * .45f, 1.4f, .12f, new Color(Ember.r, Ember.g, Ember.b, .45f), 0f, false);
        Cloud(root, kit, "Puff", 4, Vector3.up * .3f, 70f, .3f, .4f, 1f, .5f, .8f, .35f, .55f, 0f, false,
            new Color(LeafLight.r, LeafLight.g, LeafLight.b, .2f), new Color(LeafDark.r, LeafDark.g, LeafDark.b, .18f));
        Save(root);
    }

    /// <summary>Устья четырёх линий креста (линия k — поворот −90°·k от +Z): место radius м от центра на высоте y, направление — по линии с подъёмом.</summary>
    private static Mesh LaneMouthPoints(string name, float radius, float y, float elevation)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        float rise = elevation * Mathf.Deg2Rad;
        for (int k = 0; k < Simulation.ThicketBushLanes; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, ThicketMasterSeedRules.LaneYawDegrees(k), 0f) * Vector3.forward;
            positions.Add(dir * radius + Vector3.up * y);
            directions.Add((dir * Mathf.Cos(rise) + Vector3.up * Mathf.Sin(rise)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>V17: префабы веера с груди (замах в кусте, выпуск) больше не нужны — снимаются, только когда терновник собрался.</summary>
    private static void DeleteStaleSeedFan()
    {
        foreach (string name in new[] { "VFX_Thicket_SeedSwell", "VFX_Thicket_SeedLaunch" })
        {
            string path = PrefabPath(name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null && AssetDatabase.DeleteAsset(path)) Debug.Log(Log + "Снят префаб веера V16: " + path);
        }
    }

    /// <summary>
    /// Попал (корень на земле у тела героя, +Z — ход семени): стручок лопается — короткий уголь (вспышка и искры), шипы
    /// Шипомёта осколками, комья шелухи, щепки коры, листья и низкое облачко; всё летит назад и вбок — не сквозь героя.
    /// </summary>
    private static void SaveSeedHit(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SeedHitName);
        var ground = root.transform;
        var at = Vector3.up * (ThicketMasterSeedRules.FlightLift + .05f);
        var back = Vector3.back + Vector3.up * .8f;
        Flash(root, kit, "Flash", at, .85f, .1f, new Color(Ember.r, Ember.g, Ember.b, .55f), 0f, false);
        Embers(root, kit, "Embers", 9, at, .15f, 2f, 4.2f, .55f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        SeedShards(root, ground, kit, "Thorn Shards", 7, at, back, 55f, 3f, 5.5f, .26f, .4f);
        Chunks(root, ground, kit, "Husk", 6, at, Vector3.back + Vector3.up, 60f, .08f, 2.4f, 4.4f, .09f, .16f, 1.5f, 0f, BarkLight, BarkDark);
        Debris(root, ground, "Splinters", kit.Splinter, 14, at, Vector3.back + Vector3.up * .6f, 70f, .1f, 2.6f, 5.2f, .07f, .14f, 1.3f, 0f,
            BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 6, at, Vector3.up + Vector3.back * .4f, 65f, 1.2f, 2.8f, 0f);
        Dust(root, kit, "Dust", 4, Vector3.up * .1f, 50f, .15f, .5f, 1.2f, .4f, .7f, .45f, .65f, .3f, 0f, false);
        Save(root);
    }

    /// <summary>
    /// Конец линии без попадания (корень на земле, стручок уходит в неё): низкий клуб пыли кольцом, комья и зерно земли
    /// поляны вверх-вперёд, пара листьев и щепок — маленький, без пятна на земле.
    /// </summary>
    private static void SaveSeedDrop(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SeedDropName);
        var ground = root.transform;
        LowPuffs(root, kit, "Puff", 8, AroundPoints("ThicketSeedDropPuffPoints", 8, 911, .1f, .35f, .08f, 10f, 35f), .8f, 1.8f,
            .35f, .6f, .4f, .6f, .32f, 0f);
        Clods(root, ground, kit, "Clods", 8, Vector3.up * .05f, Vector3.up + Vector3.forward * .5f, 45f, .12f, 1.6f, 3f, .05f, .12f, 1.6f, 0f);
        Grains(root, ground, kit, "Grains", 12, Vector3.up * .05f, Vector3.up + Vector3.forward * .4f, 50f, .12f, 1.5f, 3.2f, 0f);
        Leaves(root, ground, kit, "Leaves", 4, Vector3.up * .2f, Vector3.up, 60f, .8f, 1.8f, 0f);
        Debris(root, ground, "Splinters", kit.Splinter, 4, Vector3.up * .1f, Vector3.up + Vector3.forward * .3f, 50f, .1f, 1.5f, 3f, .05f, .1f,
            1.3f, 0f, BarkLight, BarkDark);
        Save(root);
    }

    /// <summary>
    /// Осколки шипов: меш-частицы шипов Шипомёта (ось +Y, длина 1) на коре стручка — кувырок в 3D, баллистика, отскок от
    /// ground, к концу уходят. Размер — длина осколка, м.
    /// </summary>
    private static ParticleSystem SeedShards(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        if (kit.SeedShards == null || kit.SeedShards.Length == 0 || kit.SeedShards[0] == null || kit.SeedWood == null)
            return Debris(host, ground, name, kit.Splinter, count, at, direction, cone, .1f, speedMin, speedMax, .08f, .14f, 1.4f, 0f,
                BarkLight, BarkDark);
        var particles = Particles(host, name, count, 1.1f, 1.5f, speedMin, speedMax, sizeMin, sizeMax, 0f);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = 1.4f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .1f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
        spin.y = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        Collide(particles, ground, .3f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .75f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(AtMostFourMeshes(kit.SeedShards));
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.SeedWood;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return particles;
    }
}
