using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префаб выпада Крушения на стеке сабли: корень на земле в точке удара, местная +Z — ход полосы, +Y — вверх, масштаб 1
/// (размеры — под круг удара Sim 1,2 м). Удар — системы с залпом на старте; полоса — системы без залпа (контроллер
/// выпускает куски на шагах фронта Sim) и две ленты гребня (меш строит контроллер). См. PelagWreckComboVfxSetup.Lunge.
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    // Цвета частиц — вершинные (шейдер Bit их не переводит): сразу в линейном пространстве.
    internal static readonly Color LungeCobalt = new Color(.12f, .36f, 1.0f, 1f);
    internal static readonly Color LungeDeep = new Color(.035f, .12f, .55f, 1f);
    internal static readonly Color LungeWhite = new Color(.80f, .92f, 1.25f, 1f);
    internal static readonly Color CraterInk = new Color(.045f, .03f, .025f, .92f);
    internal static readonly Color ScorchInk = new Color(.05f, .035f, .03f, .9f);
    internal static readonly Color DustTan = new Color(.50f, .40f, .30f, .8f);
    internal static readonly Color DustDark = new Color(.42f, .32f, .23f, .8f);
    // Камни — освещённые частицы URP, цвет множит палитру пака: земля бурее, камень серее.
    internal static readonly Color RockEarth = new Color(.72f, .63f, .56f, 1f);
    internal static readonly Color RockStone = new Color(.86f, .86f, .90f, 1f);

    private static void SaveLungePrefab(LungeMaterials m)
    {
        var root = new GameObject(LungeName);
        try
        {
            AddElement(root, PelagVfxId.WreckComboLunge, LungeLife);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);

            // ---- СТОЯЧИЙ ВСПЛЕСК: полузвезда от земли (низ на земле, лучи к экрану вверх), звезда у головы якоря, эхо.
            float impact = 1.2f;
            float halfW = PelagWreckComboLungeLook.HalfWidth(impact);
            ParticleSystem half = Flash(root, "Half", m.Half, .34f, 0f, halfW, halfW * .5f, LungeCobalt, false, .40f, 1.16f, .075f);
            half.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            float star = PelagWreckComboLungeLook.StarDiameter(impact);
            ParticleSystem starLayer = Flash(root, "Star", m.Burst, .27f, 0f, star, star, LungeCobalt, true, .50f, 1.12f, .06f);
            starLayer.transform.localPosition = new Vector3(0f, PelagWreckComboLungeLook.StarHeight, 0f);
            starLayer.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
            ParticleSystem starEcho = Flash(root, "StarEcho", m.Burst, .21f, .035f, star * .62f, star * .62f, LungeWhite, true, .55f, 1.10f, .05f);
            starEcho.transform.localPosition = new Vector3(0f, PelagWreckComboLungeLook.StarHeight + .1f, 0f);
            starEcho.GetComponent<ParticleSystemRenderer>().sortingFudge = -14f;

            // ---- ПО ЗЕМЛЕ (единственное плоское у удара): ударная звезда, светящиеся трещины, тёмный кратер.
            ParticleSystem groundStar = Flash(root, "GroundStar", m.Burst, .30f, 0f, star * 1.2f, star * 1.2f, LungeDeep, true, .35f, 1.08f, .07f);
            Horizontal(groundStar, .045f);
            float crack = PelagWreckComboLungeLook.CrackDiameter(impact);
            ParticleSystem glow = Flash(root, "Glow", m.Glow, .55f, 0f, crack, crack, LungeCobalt, false, .70f, 1.04f, .06f);
            Horizontal(glow, .035f);
            ParticleSystem crater = Flash(root, "Crater", m.Crater, 1.5f, 0f, crack, crack, CraterInk, false, .75f, 1.02f, .05f);
            Horizontal(crater, .025f);
            var fade = crater.colorOverLifetime; fade.enabled = true;
            var fadeKeys = new Gradient();
            fadeKeys.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .65f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(fadeKeys);

            // ---- ОСКОЛКИ УДАРА: искры, сколы, комья, настоящие камни, пыль — вверх и вперёд по полосе.
            ParticleSystem sparks = Sparks(root, "Sparks", m.Spark, 26, .14f, .30f, 5f, 12f, .04f, .07f, 1.0f, .6f);
            Cone(sparks, 62f, .25f, -62f);
            ParticleSystem chips = Chips(root, "Chips", m.Chip, 6, .50f, .75f, 3f, 6f, .16f, .26f, 2.4f, 0f, IronFace, IronSteel);
            Cone(chips, 50f, .3f, -66f);
            ParticleSystem clods = Chips(root, "Clods", m.Clod, 6, .45f, .70f, 2.5f, 5f, .18f, .30f, 2.6f, 0f, Clod, ClodLight);
            Cone(clods, 62f, .5f, -60f);
            ParticleSystem rocks = Rocks(root, "Rocks", m, ground.transform, 7, 3.6f, 6.2f, .40f, .65f);
            Cone(rocks, 55f, .2f, -68f);
            ParticleSystem dust = Dust(root, "Dust", m.Dust, 4, .6f, 1.4f);
            Cone(dust, 85f, .55f, -12f);
            foreach (var layer in new[] { chips, clods }) layer.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
            // Над землёй: наклонённый диск конуса иначе рождает половину кусков под землёй (их прячет земля, камни
            // застревают под плоскостью отскока — в пробе из шести был виден один).
            foreach (var layer in new[] { sparks, chips, clods, rocks, dust }) layer.transform.localPosition = new Vector3(0f, .35f, 0f);

            // ---- ПО ПОЛОСЕ (залпа нет — контроллер выпускает на шагах фронта Sim): то же, плюс тонкий след трещин.
            ParticleSystem laneSparks = Sparks(root, "LaneSparks", m.Spark, 90, .14f, .28f, 4f, 10f, .035f, .065f, .9f, .5f);
            Lane(laneSparks);
            Lane(Chips(root, "LaneChips", m.Chip, 30, .45f, .70f, 0f, 0f, .14f, .24f, 2.4f, 0f, IronFace, IronSteel));
            Lane(Chips(root, "LaneClods", m.Clod, 40, .50f, .80f, 0f, 0f, .16f, .30f, 2.6f, 0f, Clod, ClodLight));
            Lane(Rocks(root, "LaneRocks", m, ground.transform, 16, 0f, 0f, .16f, .46f));
            Lane(Dust(root, "LaneDust", m.Dust, 30, 0f, 0f));
            Lane(Decal(root, "Scorch", m.Scorch, 24, 1.1f));
            Lane(Decal(root, "ScorchGlow", m.LaneGlow, 24, .30f));
            ParticleSystem endHalf = Flash(root, "EndHalf", m.Half, .30f, 0f, 1.6f, .8f, LungeCobalt, false, .40f, 1.16f, .07f);
            endHalf.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            var endMain = endHalf.main; endMain.simulationSpace = ParticleSystemSimulationSpace.World; endMain.maxParticles = 3;
            Lane(endHalf);

            // ---- ГРЕБЕНЬ: две ленты (меш — контроллер), эхо рисуется раньше основного.
            CrestRenderer(root, "CrestEcho", m.Echo, 0);
            CrestRenderer(root, "Crest", m.Crest, 1);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(LungeName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Одна частица-вспышка: хлопок масштаба (недолёт → перелёт → размер), порог материала режет по возрасту.</summary>
    private static ParticleSystem Flash(GameObject root, string name, Material material, float life, float delay,
        float sizeX, float sizeY, Color color, bool randomRotation, float popFrom, float popPeak, float popSeconds)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, 1, life, life, 0f, 0f, 1f, 1f);
        var main = p.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.duration = life + delay + .05f;
        main.startSize3D = true;
        main.startSizeX = sizeX;
        main.startSizeY = sizeY;
        main.startSizeZ = 1f;
        main.startColor = color;
        main.startRotation = randomRotation ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        var shape = p.shape; shape.enabled = false;
        float end = Mathf.Clamp01(popSeconds / life);
        var size = p.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, popFrom), new Keyframe(end * .55f, popPeak), new Keyframe(end, 1f), new Keyframe(1f, .97f)));
        BitRenderer(p, material, ParticleSystemRenderMode.Billboard);
        return p;
    }

    /// <summary>Лежит на земле: горизонтальный билборд чуть над землёй.</summary>
    private static void Horizontal(ParticleSystem p, float lift)
    {
        p.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        p.transform.localPosition = new Vector3(0f, lift, 0f);
    }

    /// <summary>Система полосы: без залпа, мир; куски выпускает контроллер (EmitParams).</summary>
    private static void Lane(ParticleSystem p)
    {
        var emission = p.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        var main = p.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var shape = p.shape; shape.enabled = false;
    }

    /// <summary>
    /// Настоящие камни: меши «Tiny Rock» на освещённом материале, случайный поворот, вертятся по скорости (на земле
    /// замирают), падают с тяжестью и отскакивают от плоскости земли, к концу уходят в землю.
    /// </summary>
    private static ParticleSystem Rocks(GameObject root, string name, LungeMaterials m, Transform ground, int count,
        float speedMin, float speedMax, float sizeMin, float sizeMax)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, count, 1.05f, 1.35f, speedMin, speedMax, sizeMin, sizeMax);
        var main = p.main;
        main.gravityModifier = 2.2f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(RockEarth, RockStone);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var spin = p.rotationBySpeed; spin.enabled = true;
        spin.separateAxes = true;
        spin.range = new Vector2(0f, 6f);
        var up = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var down = AnimationCurve.Linear(0f, 0f, 1f, -1f);
        spin.x = new ParticleSystem.MinMaxCurve(11f, down, up);
        spin.y = new ParticleSystem.MinMaxCurve(7f, down, up);
        spin.z = new ParticleSystem.MinMaxCurve(11f, down, up);
        var collision = p.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground);
        collision.dampen = .55f;
        collision.bounce = .32f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .45f;
        var size = p.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .6f), new Keyframe(.08f, 1f), new Keyframe(.72f, 1f), new Keyframe(1f, 0f)));
        var renderer = p.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(m.Rocks);
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.sharedMaterial = m.Rock;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
        });
        return p;
    }

    /// <summary>Пыль: мультяшные клубы пака «smoke cloud x4» (случайный кадр, два тона), раздуваются, вязнут и сжимаются.</summary>
    private static ParticleSystem Dust(GameObject root, string name, Material material, int count, float speedMin, float speedMax)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, count, .30f, .45f, speedMin, speedMax, .32f, .50f);
        var main = p.main;
        main.gravityModifier = -.06f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startColor = new ParticleSystem.MinMaxGradient(DustTan, DustDark);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var limit = p.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(.5f);
        limit.dampen = .12f;
        var size = p.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .45f), new Keyframe(.25f, 1f), new Keyframe(.7f, 1.05f), new Keyframe(1f, 0f)));
        var sheet = p.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, .999f);
        sheet.cycleCount = 1;
        BitRenderer(p, material, ParticleSystemRenderMode.Billboard);
        return p;
    }

    /// <summary>След трещин по полосе: горизонтальные билборды, длинная ось — вдоль полосы (поворот и размер — контроллер).</summary>
    private static ParticleSystem Decal(GameObject root, string name, Material material, int count, float life)
    {
        ParticleSystem p = PelagWhirlwindVfxSetup.NewParticles(root, name, count, life, life, 0f, 0f, 1f, 1f);
        var main = p.main;
        main.startSize3D = true;
        BitRenderer(p, material, ParticleSystemRenderMode.HorizontalBillboard);
        return p;
    }

    /// <summary>Лента гребня: меш строит контроллер кадр за кадром; порядок — эхо позади основного.</summary>
    private static void CrestRenderer(GameObject root, string name, Material material, int order)
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
        renderer.sortingOrder = order;
    }
}
