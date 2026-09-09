using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class RazlomPelagVfxAssetBuilder
{
    private const string HovlMagic = "Assets/Hovl Studio/Magic effects pack/";

    private static void LeapHovlLaunch(GameObject root)
    {
        var glow = Shader.Find("Razlom/Pelag Glow");
        var slashMaterial = FlipbookMaterial("M_LeapHovlSlash", glow,
            HovlMagic + "Textures/Mask1.png", new Color(1f, .91f, .75f, 1f), 2.1f);
        var sparkMaterial = FlipbookMaterial("M_LeapHovlSparks", glow,
            HovlMagic + "Textures/Point1.png", new Color(1f, .87f, .64f, 1f), 2.2f);
        var flashMaterial = FlipbookMaterial("M_LeapHovlFlash", glow,
            "Assets/Hovl Studio/HSFiles/Textures/Flash33.png", new Color(1f, .87f, .67f, 1f), 2.5f);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(HovlMagic +
            "Prefabs/Slash effects/Charge slash red.prefab");
        if (source == null) throw new System.InvalidOperationException("Не найден Hovl Charge slash red");

        var sourceMesh = source.transform.Find("Slash").GetComponent<ParticleSystemRenderer>().mesh;
        string meshPath = Root + "/Geometry/LeapHovlSlash.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) AssetDatabase.CreateAsset(Object.Instantiate(sourceMesh), meshPath);
        else { EditorUtility.CopySerialized(sourceMesh, mesh); EditorUtility.SetDirty(mesh); }

        // Геометрия и кривые роста сохраняют характер исходного эффекта.
        // Собственные материалы и одноразовая эмиссия отделяют его от демо пака.
        foreach (string layer in new[] { "Slash", "Slash2", "Sparks" })
        {
            var original = source.transform.Find(layer);
            var child = Object.Instantiate(original.gameObject, root.transform, false);
            child.name = "Hovl " + layer;
            child.transform.localRotation = Quaternion.Euler(0, 180, 0);
            child.transform.localScale = Vector3.one * .85f;
            var ps = child.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            bool sparks = layer == "Sparks";
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.prewarm = false;
            main.duration = .42f; main.startDelay = layer == "Slash2" ? .055f : 0f;
            main.startLifetime = sparks ? .22f : .28f;
            main.startColor = Color.white;
            main.startSpeed = sparks ? 4f : 1.2f;
            if (!sparks) { var shape = ps.shape; shape.enabled = false; }
            main.maxParticles = sparks ? 12 : 1;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = ps.emission;
            emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)(sparks ? 12 : 1)) });
            var color = ps.colorOverLifetime; color.enabled = true;
            var tint = new Gradient();
            tint.SetKeys(new[] { new GradientColorKey(Color.white, 0),
                new GradientColorKey(new Color(1, .30f, .13f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .28f),
                    new GradientAlphaKey(0, 1) });
            color.color = tint;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparks ? sparkMaterial : slashMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        }

        var flash = AddFlipbook(root, "Hovl tension burst", flashMaterial, 1.85f,
            false, Vector3.zero, 4, 4, .22f);
        var flashMain = flash.main; flashMain.startRotation = .35f;
        var sheet = flash.textureSheetAnimation;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, .999f));
    }
}
