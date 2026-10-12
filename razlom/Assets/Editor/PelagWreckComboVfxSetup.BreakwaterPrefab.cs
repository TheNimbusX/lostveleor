using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префаб Волнореза (см. PelagWreckComboVfxSetup.Breakwater): корень на земле в точке удара, +Z — ход полосы. Пул плит
/// «Slabs/Slab00…» (MeshRenderer, выключены — контроллер ставит, растягивает и включает плиту, когда её место проходит
/// фронт Sim); системы частиц без залпа (куски выпускает контроллер на выходе плит, у несомых, в обрушении); зелёный
/// точечный свет у фронта (мобам, обломкам и плитам — пол арены доп. свет не берёт, земле светят трещины).
/// </summary>
public static partial class PelagWreckComboVfxSetup
{
    internal static readonly Color BreakDustTan = new Color(.50f, .40f, .30f, .8f);
    internal static readonly Color BreakDustDark = new Color(.42f, .32f, .23f, .8f);
    internal static readonly Color BreakChunkLight = new Color(.95f, .94f, .92f, 1f);
    internal static readonly Color BreakChunkDark = new Color(.72f, .70f, .68f, 1f);

    /// <summary>Жизнь префаба по умолчанию, с (контроллер задаёт свою: ход фронта + распад + обломки на земле).</summary>
    internal const float BreakWallLife = 3.2f;

    private static void SaveBreakWallPrefab(BreakMaterials m)
    {
        var root = new GameObject(BreakWallName);
        try
        {
            AddElement(root, PelagVfxId.WreckBreakWall, BreakWallLife);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);

            // ---- пул плит: меши по кругу (5 вариантов), камень с трещинами; тени — да (объём на земле).
            var slabs = new GameObject("Slabs");
            slabs.transform.SetParent(root.transform, false);
            for (int i = 0; i < PelagWreckBreakwaterLook.PoolSize; i++)
            {
                var slab = new GameObject("Slab" + i.ToString("00"));
                slab.transform.SetParent(slabs.transform, false);
                slab.AddComponent<MeshFilter>().sharedMesh = m.Slabs[i % m.Slabs.Length];
                var renderer = slab.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m.Slab;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
            }

            // ---- свет в щелях: стоячие полосы от земли (низ на земле, вертикальный билборд), режутся порогом с возрастом.
            ParticleSystem seams = BreakLane(PelagWhirlwindVfxSetup.NewParticles(root, "Seams", 60, .5f, .5f, 0f, 0f, 1f, 1f), 60);
            var seamMain = seams.main; seamMain.startSize3D = true;
            BitRenderer(seams, m.Seam, ParticleSystemRenderMode.VerticalBillboard);
            seams.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            seams.GetComponent<ParticleSystemRenderer>().sortingFudge = 10f;
            // ---- земля под стеной: светящиеся и тёмные трещины (горизонтальные билборды, поворот и размер — контроллер).
            ParticleSystem glow = BreakLane(Decal(root, "GroundGlow", m.GroundGlow, 40, .8f), 40);
            glow.transform.localPosition = new Vector3(0f, .04f, 0f);
            glow.GetComponent<ParticleSystemRenderer>().sortingFudge = 30f;
            ParticleSystem cracks = BreakLane(Decal(root, "GroundCrack", m.GroundCrack, 40, 1.6f), 40);
            cracks.GetComponent<ParticleSystemRenderer>().sortingFudge = 34f;
            var fade = cracks.colorOverLifetime; fade.enabled = true;
            var fadeKeys = new Gradient();
            fadeKeys.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(fadeKeys);

            // ---- пыль и комья у подошвы, искры, обломки — 3D-камни на освещённом камне плит, без GPU-инстансинга.
            BreakLane(Dust(root, "Dust", m.Dust, 90, 0f, 0f), 90);
            BreakLane(Chips(root, "Clods", m.Clod, 90, .5f, .8f, 0f, 0f, .16f, .30f, 2.6f, 0f, Clod, ClodLight), 90);
            ParticleSystem sparks = BreakLane(Sparks(root, "Sparks", m.Spark, 160, .14f, .30f, 4f, 10f, .035f, .07f, .9f, .4f), 160);
            var sparkMain = sparks.main; sparkMain.startColor = new ParticleSystem.MinMaxGradient(BreakMint, BreakGreen);
            ParticleSystem chunks = BreakChunks(root, "Chunks", m, ground.transform, 140);
            BreakLane(chunks, 140);

            // ---- всплески: полузвезда от земли (гребень под несомым, обрушение), звезда удара о преграду.
            ParticleSystem half = Flash(root, "Half", m.Half, .32f, 0f, 1.6f, .8f, BreakGreen, false, .40f, 1.16f, .075f);
            half.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, -.5f, 0f);
            half.GetComponent<ParticleSystemRenderer>().sortingFudge = 4f;
            BreakLane(half, 12);
            ParticleSystem star = Flash(root, "Star", m.Star, .26f, 0f, 2f, 2f, BreakGreen, true, .50f, 1.12f, .06f);
            star.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;
            BreakLane(star, 4);

            var lightHost = new GameObject("Light");
            lightHost.transform.SetParent(root.transform, false);
            lightHost.transform.localPosition = new Vector3(0f, 1f, 0f);
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4.5f;
            light.color = new Color(.30f, 1f, .64f, 1f);
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(BreakWallName));
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Система без залпа в мире (куски выпускает контроллер); запас частиц, длинный срок системы.</summary>
    private static ParticleSystem BreakLane(ParticleSystem p, int max)
    {
        Lane(p);
        var main = p.main;
        main.maxParticles = Mathf.Max(1, max);
        main.duration = Mathf.Max(main.duration, 3f);
        main.startDelay = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        return p;
    }

    /// <summary>Обломки: меши камней пака на камне плит (частицы — цвет частицы), отскок от земли, к концу уходят в землю.</summary>
    private static ParticleSystem BreakChunks(GameObject root, string name, BreakMaterials m, Transform ground, int count)
    {
        var stone = new LungeMaterials { Rock = m.Chunk, Rocks = m.Chunks };
        ParticleSystem p = Rocks(root, name, stone, ground, count, 0f, 0f, .2f, .4f);
        var main = p.main;
        main.startColor = new ParticleSystem.MinMaxGradient(BreakChunkDark, BreakChunkLight);
        main.gravityModifier = 2.4f;
        var renderer = p.GetComponent<ParticleSystemRenderer>();
        renderer.enableGPUInstancing = false;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color
        });
        return p;
    }
}
