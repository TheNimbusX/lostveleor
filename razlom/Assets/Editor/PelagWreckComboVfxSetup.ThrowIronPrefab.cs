using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы базы Броска якоря (см. PelagWreckComboVfxSetup.ThrowIron). Ленты струны и борозды строит вид каждый кадр (объекты
/// стоят в нуле мира, вершины — в мире); частицы без залпа (Lane) вид выпускает сам (Emit) — с головы, по цепи, у ног тел.
/// Знак, ловля и звёзды — залпом на старте; корень знака — у груди задетого, +Z — ход броска; камни, комья и пыль знака вид
/// ставит к ногам (дочерние «Feet» и «Ground»).
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    private static void IronMeshChild(GameObject parent, string name, Material material, int order)
    {
        var host = new GameObject(name);
        host.transform.SetParent(parent.transform, false);
        host.AddComponent<MeshFilter>();
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = order;
    }

    /// <summary>Вспышка без залпа (вид выпускает: размер, жизнь, цвет): хлопок масштаба, порог материала режет по возрасту.</summary>
    private static ParticleSystem IronFlares(GameObject root, string name, Material material, int max)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, max, .10f, .10f, 0f, 0f, 1f, 1f);
        var main = p.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = BurstCobalt;
        var size = p.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .55f), new Keyframe(.18f, 1.12f), new Keyframe(.40f, 1f), new Keyframe(1f, .92f)));
        BitRenderer(p, material, ParticleSystemRenderMode.Billboard);
        Lane(p);
        return p;
    }

    private static void SaveIronChainPrefab(ThrowIronMaterials t, in IronPaint paint)
    {
        var root = new GameObject(paint.ChainName);
        try
        {
            AddElement(root, paint.ChainId, 8f);
            IronMeshChild(root, "Halo", t.Halo, 0);
            IronMeshChild(root, "Streak", t.Streak, 1);
            // Веер: звенья призрачной цепи боковых полос — поверх струны (вид строит ленту только у полос 1 и 2).
            if (paint.Links != null) IronMeshChild(root, "Links", paint.Links, 2);
            // Искры с головы в полёте и россыпь в натяг: вытянуты по скорости, белые и кобальт (цвет — EmitParams вида).
            ParticleSystem sparks = Sparks(root, "Sparks", t.Spark, 240, .12f, .26f, 1.5f, 5f, .03f, .06f, .6f, 0f);
            Lane(sparks);
            // Шевроны рывка: пары вытянутых искр под углом к цепи бегут к руке («>» остриём к Пелагу).
            ParticleSystem chevrons = Sparks(root, "Chevrons", t.Spark, 240, .14f, .22f, 4f, 8f, .045f, .07f, 0f, 0f);
            chevrons.GetComponent<ParticleSystemRenderer>().lengthScale = 4.6f;
            Lane(chevrons);
            // Вспышки головы (колючая звезда кобальта с белым ядром): каждый тик полёта, щелчок натяга, рывок.
            ParticleSystem flares = IronFlares(root, "Flares", t.Flare, 24);
            flares.GetComponent<ParticleSystemRenderer>().sortingFudge = -15f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(paint.ChainName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Знак на задетом: стек знака Крушения (звезда с перелётом, втягивает лучи; эхо-звезда светлее и позже), искры по ходу
    /// броска, сколы железа — у груди; комья, освещённые камни и пыль — от ног (дочерний «Feet», плоскость отскока «Ground»).
    /// </summary>
    private static void SaveIronHitPrefab(ThrowIronMaterials t, in IronPaint paint)
    {
        var root = new GameObject(paint.HitName);
        try
        {
            AddElement(root, paint.HitId, 1.45f);
            ParticleSystem star = Flash(root, "Burst", t.Burst, .22f, 0f, 1.05f, 1.05f, paint.Star, true, .50f, 1.12f, .05f);
            star.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
            // Эхо — сталь (V2 белым эхом знак читался кремовым пятном).
            ParticleSystem echo = Flash(root, "Echo", t.Burst, .18f, .035f, .70f, .70f, paint.Echo, true, .55f, 1.10f, .045f);
            echo.GetComponent<ParticleSystemRenderer>().sortingFudge = -22f;
            ParticleSystem sparks = Sparks(root, "Sparks", t.Spark, 16, .12f, .24f, 5f, 11f, .035f, .065f, .8f, 0f);
            Cone(sparks, 24f, .06f, -12f);
            ParticleSystem chips = Chips(root, "Chips", t.Chip, 3, .40f, .62f, 3f, 5.5f, .17f, .27f, 2.4f, 0f,
                Linear(PelagWreckComboLook.ChipFaceDark), Linear(PelagWreckComboLook.ChipFaceLight));
            Cone(chips, 50f, .25f, -32f);
            chips.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;

            var feet = new GameObject("Feet");
            feet.transform.SetParent(root.transform, false);
            feet.transform.localPosition = new Vector3(0f, -.75f, 0f);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(0f, -1.05f, 0f);
            ParticleSystem clods = Chips(feet, "Clods", t.Clod, 5, .45f, .70f, 2f, 4f, .14f, .24f, 2.6f, 0f, Clod, ClodLight);
            Cone(clods, 40f, .18f, -58f);
            ParticleSystem rocks = Rocks(feet, "Rocks", t.Rocks, ground.transform, PelagAnchorThrowIronLook.HitRocksHeavy, 2.6f, 4.6f,
                .26f, .46f);
            Cone(rocks, 38f, .15f, -55f);
            // Камни — серо-бурые (палитра выпада Крушения на рыжей земле читалась светлой галькой).
            var rocksMain = rocks.main;
            rocksMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.46f, .38f, .32f, 1f), new Color(.60f, .57f, .54f, 1f));
            ParticleSystem dust = Dust(feet, "Dust", t.Dust, 3, .4f, 1.0f);
            Cone(dust, 75f, .35f, -15f);
            foreach (var layer in new[] { clods, rocks, dust }) layer.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(paint.HitName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Борозда волока (лента — вид) и её передний край: комья, камни, пыль, сколы земли — Emit вида.</summary>
    private static void SaveIronFurrowPrefab(ThrowIronMaterials t)
    {
        var root = new GameObject(IronFurrowName);
        try
        {
            AddElement(root, PelagVfxId.AnchorThrowIronFurrow, 8f);
            IronMeshChild(root, "Strip", t.Furrow, 0);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            Lane(Chips(root, "Clods", t.Clod, 90, .45f, .75f, 0f, 0f, .12f, .24f, 2.6f, 0f, Clod, ClodLight));
            Lane(Rocks(root, "Rocks", t.Rocks, ground.transform, 30, 0f, 0f, .16f, .40f));
            Lane(Dust(root, "Dust", t.Dust, 40, 0f, 0f));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(IronFurrowName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Ловля у кулака: маленькая звезда (якорь в руке, не взрыв), искры веером навстречу якорю, пара сколов.</summary>
    private static void SaveIronCatchPrefab(ThrowIronMaterials t, in IronPaint paint)
    {
        var root = new GameObject(paint.CatchName);
        try
        {
            AddElement(root, paint.CatchId, .6f);
            float size = PelagAnchorThrowIronLook.CatchStar;
            ParticleSystem star = Flash(root, "Burst", t.Burst, .18f, 0f, size, size, paint.Star, true, .45f, 1.15f, .04f);
            star.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
            ParticleSystem sparks = Sparks(root, "Sparks", t.Spark, 12, .10f, .20f, 3f, 7f, .03f, .05f, .6f, 0f);
            Cone(sparks, 70f, .05f, -20f);
            ParticleSystem chips = Chips(root, "Chips", t.Chip, 2, .35f, .50f, 2f, 3.5f, .12f, .18f, 2.4f, 0f,
                Linear(PelagWreckComboLook.ChipFaceDark), Linear(PelagWreckComboLook.ChipFaceLight));
            Cone(chips, 60f, .1f, -40f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(paint.CatchName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Звёзды оглушения тяжёлого: три стальные звезды по орбите над головой (местное пространство — корень ведёт вид).</summary>
    private static void SaveIronStunPrefab(ThrowIronMaterials t, in IronPaint paint)
    {
        var root = new GameObject(paint.StunName);
        try
        {
            AddElement(root, paint.StunId, 2.2f);
            float orbit = 2f * Mathf.PI / PelagAnchorThrowIronLook.StunPeriod;
            ParticleSystem stars = PelagWhirlwindVfxSetup.NewParticles(root, "Stars", 3, 2f, 2f, 0f, 0f,
                PelagAnchorThrowIronLook.StunStar, PelagAnchorThrowIronLook.StunStar);
            IronOrbit(stars, orbit);
            var sm = stars.main;
            sm.startColor = paint.StunStar;
            var spin = stars.rotationOverLifetime; spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(2.5f);
            var pop = stars.sizeOverLifetime; pop.enabled = true;
            pop.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .2f), new Keyframe(.04f, 1.25f), new Keyframe(.08f, 1f), new Keyframe(.92f, 1f), new Keyframe(1f, .3f)));
            BitRenderer(stars, t.Star, ParticleSystemRenderMode.Billboard);
            if (paint.StunTrail)
            {
                // Гарпун (кадр form-harpoon-v2): за звёздами — пунктир орбиты из искорок краски.
                // V8 (игра V7: точки .035–.055 м — 2 px, орбиты не видно): крупнее и гуще, гаснут к концу жизни — светлое кольцо.
                ParticleSystem dots = PelagWhirlwindVfxSetup.NewParticles(root, "Orbit", 90, .20f, .28f, 0f, 0f, .07f, .10f);
                var dm = dots.main;
                dm.duration = 3f;
                dm.startColor = new ParticleSystem.MinMaxGradient(paint.Echo, paint.StunStar);
                var de = dots.emission;
                de.SetBursts(new ParticleSystem.Burst[0]);
                de.rateOverTime = 150f;
                var dc = dots.colorOverLifetime; dc.enabled = true;
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0f, 1f) });
                dc.color = new ParticleSystem.MinMaxGradient(fade);
                IronOrbit(dots, orbit);
                var ds = dots.shape;
                ds.arcMode = ParticleSystemShapeMultiModeValue.Random;
                BitRenderer(dots, t.Spark, ParticleSystemRenderMode.Billboard);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(paint.StunName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void IronOrbit(ParticleSystem p, float radiansPerSecond)
    {
        var main = p.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var shape = p.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = PelagAnchorThrowIronLook.StunOrbit;
        shape.radiusThickness = 0f;
        shape.arc = 360f;
        shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var velocity = p.velocityOverLifetime; velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(radiansPerSecond);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
        velocity.radial = new ParticleSystem.MinMaxCurve(0f);
    }
}
