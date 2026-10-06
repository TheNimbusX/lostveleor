using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы Крушения «холодное железо» (см. PelagWreckIronVfxSetup.cs). Ловушки частиц Unity
/// учтены: кривые одного режима, мировые координаты у выбрасываемых слоёв, не больше 8 вспышек
/// на систему, Color32 в своём меше. Куски удара (камни, осколки, звенья) — не частицы: вид
/// ставит их по числам Sim и ведёт сам (встают из земли, светятся, оседают). Любая правка —
/// поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    /// <summary>
    /// Удар оземь: корень — точка удара на земле, без поворота и масштаба. «Ground» — меш земли
    /// (пишет вид), «Links/L0…3», «Slabs/S00…», «Rubble/R00…», «Irons/I00…» — куски с дочерним «Mesh»
    /// (приведён к 1 м вокруг центра), «Chunks» (меш-частицы кусков), «Shards», «Spray» (V7 — брызги
    /// земли), «Dust», «Sparks» — выбрасывает вид, «Light».
    /// </summary>
    private static void SaveSlamPrefab(Kit kit)
    {
        var root = new GameObject(SlamName);
        try
        {
            AddElement(root, PelagVfxId.WreckIronSlam, 2f, 1.2f);
            MeshLayer(root, "Ground", kit.Ground, false);

            var links = Group(root, "Links");
            for (int i = 0; i < PelagWreckIronRules.MaxLinks; i++)
            {
                // Своё звено WreckIronLink: длина 1 м по Z, центр в нуле; вид ставит размер LinkLength.
                Piece(links, "L" + i, kit.LinkMesh, kit.Link, Quaternion.identity, Vector3.zero, Vector3.one);
            }
            // V6: плиты — свои гранёные плита/осколок/клин по кругу (вид берёт подряд — разнообразие само),
            // блоки и комья — гранёный кусок; все меши уже 1 м вокруг центра.
            var slabs = Group(root, "Slabs");
            for (int i = 0; i < SlabSlots; i++)
                Piece(slabs, "S" + i.ToString("00"), kit.SlabMeshes[i % kit.SlabMeshes.Length], kit.Stone, Quaternion.identity, Vector3.zero, Vector3.one);
            var rubble = Group(root, "Rubble");
            for (int i = 0; i < RubbleSlots; i++)
                Piece(rubble, "R" + i.ToString("00"), kit.ChunkMesh, kit.Stone, Quaternion.identity, Vector3.zero, Vector3.one);
            var irons = Group(root, "Irons");
            for (int i = 0; i < IronSlots; i++)
                Piece(irons, "I" + i.ToString("00"), kit.IronMesh, kit.Iron, Quaternion.identity, Vector3.zero, Vector3.one);

            // Крупные куски камня, комья земли и железо (V5): гранёный меш-частицами, кувырком; вылетают из
            // удара и с фронта, падают внутри круга и полосы (вид бросает расчётом полёта).
            ParticleSystem chunks = Emitted(root, "Chunks", 140, .7f);
            var chunksMain = chunks.main;
            chunksMain.gravityModifier = 2.2f;
            chunksMain.startSize = new ParticleSystem.MinMaxCurve(.14f, .26f);
            chunksMain.startRotation3D = true;
            var tumble = chunks.rotationOverLifetime; tumble.enabled = true; tumble.separateAxes = true;
            tumble.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            tumble.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            tumble.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var chunkSize = chunks.sizeOverLifetime; chunkSize.enabled = true;
            chunkSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .8f), new Keyframe(.12f, 1f), new Keyframe(.82f, 1f), new Keyframe(1f, .35f)));
            var chunkRenderer = chunks.GetComponent<ParticleSystemRenderer>();
            chunkRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            chunkRenderer.mesh = kit.ChunkMesh;
            chunkRenderer.alignment = ParticleSystemRenderSpace.World;
            chunkRenderer.sharedMaterial = kit.Chunk;
            chunkRenderer.shadowCastingMode = ShadowCastingMode.On;
            chunkRenderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
            });

            // Мелкая крошка земли (лист обломков пака): тёмные бурые точки вокруг кусков.
            ParticleSystem shards = Emitted(root, "Shards", 220, .6f);
            var shardsMain = shards.main;
            shardsMain.gravityModifier = 2.2f;
            shardsMain.startSize = new ParticleSystem.MinMaxCurve(.07f, .15f);
            shardsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            DebrisSheet(shards);
            Spin(shards, 4f, 9f);
            Renderer(shards, kit.Debris, ParticleSystemRenderMode.Billboard, 0f, 0f);

            // Брызги земли (V7): плоские обломки пака (flat unlit 3×3, без обводки) низким веером наружу из
            // воронки и с краёв полосы за фронтом — рваный выброс земли, а не ровное кольцо; цвет — от частицы.
            ParticleSystem spray = Emitted(root, "Spray", 280, .55f);
            var sprayMain = spray.main;
            sprayMain.gravityModifier = 2.2f;
            sprayMain.startSize = new ParticleSystem.MinMaxCurve(.04f, .1f);
            sprayMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            DebrisSheet(spray);
            Spin(spray, 6f, 12f);
            SizeCurve(spray, 1f, 1f, .6f);
            Renderer(spray, kit.Dirt, ParticleSystemRenderMode.Billboard, 0f, 0f);

            // Пыль земли: бурые клубы у кромки круга и у фронта, поднимаются и тают.
            ParticleSystem dust = Emitted(root, "Dust", 90, .9f);
            var dustMain = dust.main;
            dustMain.gravityModifier = -.05f;
            dustMain.startSize = new ParticleSystem.MinMaxCurve(.55f, 1.0f);
            dustMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SmokeSheet(dust);
            SizeCurve(dust, .55f, 1f, 1.25f);
            FadeOut(dust);
            Drag(dust, 2.5f);
            Renderer(dust, kit.Dust, ParticleSystemRenderMode.Billboard, 0f, 0f);

            // Искры: белый жар и холодный голубой, короткие заострённые росчерки.
            ParticleSystem sparks = Emitted(root, "Sparks", 120, .3f);
            var sparksMain = sparks.main;
            sparksMain.gravityModifier = 1.2f;
            sparksMain.startSize = new ParticleSystem.MinMaxCurve(.05f, .09f);
            Drag(sparks, 4f);
            SizeCurve(sparks, 1f, 1f, .2f);
            Renderer(sparks, kit.Spark, ParticleSystemRenderMode.Stretch, 2.2f, .035f);

            var lightHost = new GameObject("Light");
            lightHost.transform.SetParent(root.transform, false);
            lightHost.transform.localPosition = Vector3.up * .8f;
            var light = lightHost.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.35f, .62f, 1f);
            light.range = 4.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            Save(root, SlamName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Росчерк маха у головы якоря: «Streak» (сталь), «Glints» (искры), «Chips» (обломки) — выбрасывает вид.</summary>
    private static void SaveStreakPrefab(Kit kit)
    {
        var root = new GameObject(StreakName);
        try
        {
            AddElement(root, PelagVfxId.WreckIronStreak, .6f, 1f);
            ParticleSystem streak = Emitted(root, "Streak", 8, .16f);
            var streakMain = streak.main;
            streakMain.startSize = new ParticleSystem.MinMaxCurve(.2f, .2f);
            SizeCurve(streak, .7f, 1f, .15f);
            FadeOut(streak);
            Renderer(streak, kit.Streak, ParticleSystemRenderMode.Stretch, 4f, 0f);
            ParticleSystem glints = Emitted(root, "Glints", 40, .22f);
            var glintsMain = glints.main;
            glintsMain.gravityModifier = 1f;
            glintsMain.startSize = new ParticleSystem.MinMaxCurve(.04f, .07f);
            Drag(glints, 5f);
            SizeCurve(glints, 1f, 1f, .2f);
            Renderer(glints, kit.Spark, ParticleSystemRenderMode.Stretch, 2f, .03f);
            ParticleSystem chips = Emitted(root, "Chips", 40, .5f);
            var chipsMain = chips.main;
            chipsMain.gravityModifier = 2.2f;
            chipsMain.startSize = new ParticleSystem.MinMaxCurve(.05f, .1f);
            chipsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            DebrisSheet(chips);
            Spin(chips, 5f, 10f);
            Renderer(chips, kit.Debris, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, StreakName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Искра железа на теле: веер искр по удару (локальная +Z), маленькая вспышка, пара обломков.</summary>
    private static void SaveHitPrefab(Kit kit)
    {
        var root = new GameObject(HitName);
        try
        {
            AddElement(root, PelagVfxId.WreckIronHit, .45f, 1f);
            ParticleSystem sparks = Burst(root, "Sparks", 8, .12f, .22f, 4.5f, 8.5f, .05f, .08f);
            Cone(sparks, 38f, .05f, 0f);
            Drag(sparks, 5f);
            SizeCurve(sparks, 1f, 1f, .2f);
            var sparksMain = sparks.main;
            sparksMain.startColor = new ParticleSystem.MinMaxGradient(HotWhite, SkyPale);
            Renderer(sparks, kit.Spark, ParticleSystemRenderMode.Stretch, 2.2f, .03f);
            ParticleSystem flash = Burst(root, "Flash", 1, .08f, .08f, 0f, 0f, .55f, .55f);
            var flashMain = flash.main;
            flashMain.startColor = new ParticleSystem.MinMaxGradient(SkyPale);
            SizeCurve(flash, .6f, 1f, .4f);
            FadeOut(flash);
            Renderer(flash, kit.Flash, ParticleSystemRenderMode.Billboard, 0f, 0f);
            ParticleSystem chips = Burst(root, "Chips", 4, .35f, .5f, 2f, 3.6f, .06f, .1f);
            Cone(chips, 50f, .08f, 0f);
            var chipsMain = chips.main;
            chipsMain.gravityModifier = 2.2f;
            chipsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            chipsMain.startColor = new ParticleSystem.MinMaxGradient(ChipStone, ChipIron);
            DebrisSheet(chips);
            Spin(chips, 5f, 10f);
            Renderer(chips, kit.Debris, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, HitName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Сбит с ног: бурая пыль кольцом у ног и камешки вверх (вместо короны пены).</summary>
    private static void SaveKnockPrefab(Kit kit)
    {
        var root = new GameObject(KnockName);
        try
        {
            AddElement(root, PelagVfxId.WreckIronKnock, .8f, 1f);
            ParticleSystem dust = Burst(root, "Dust", 4, .5f, .7f, .4f, 1.1f, .5f, .8f);
            Cone(dust, 75f, .3f, -90f);
            var dustMain = dust.main;
            dustMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dustMain.startColor = new ParticleSystem.MinMaxGradient(DustBrown);
            SmokeSheet(dust);
            SizeCurve(dust, .6f, 1f, 1.2f);
            FadeOut(dust);
            Drag(dust, 3f);
            Renderer(dust, kit.Dust, ParticleSystemRenderMode.Billboard, 0f, 0f);
            ParticleSystem pebbles = Burst(root, "Pebbles", 6, .45f, .6f, 2.4f, 3.8f, .06f, .11f);
            Cone(pebbles, 35f, .2f, -90f);
            var pebblesMain = pebbles.main;
            pebblesMain.gravityModifier = 2.2f;
            pebblesMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            pebblesMain.startColor = new ParticleSystem.MinMaxGradient(ChipStone, ChipIron);
            DebrisSheet(pebbles);
            Spin(pebbles, 5f, 10f);
            Renderer(pebbles, kit.Debris, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, KnockName);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
