using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class StonehoofBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Stonehoof/";
    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () => {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(Root + "ForestStonehoof.fbx")) return;
        if (!System.IO.File.Exists(Root + "ForestStonehoof_Runtime.prefab")) Build();
    };
    [MenuItem("Разлом/Камнекопыт/Собрать представление")]
    public static void Build()
    {
        const string model = Root + "ForestStonehoof.fbx";
        AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(model);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false; importer.importAnimation = true;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(model);
        var originalClips = AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "ForestStonehoof.controller");
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "ForestStonehoof.controller");
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>(); controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        foreach (string role in new[] { "Idle", "Walk", "Windup", "Launch", "ChargeLoop", "Brake", "WallBrace", "WallImpact", "Hit", "Death" })
        {
            var original = originalClips.FirstOrDefault(c => c.name.EndsWith("Stonehoof_" + role));
            if (original == null) throw new InvalidOperationException("Нет клипа " + role + ": " + string.Join(",", originalClips.Select(c => c.name)));
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip); clip.name = "Stonehoof_" + role;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk" || role == "ChargeLoop"; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
            var state = machine.AddState(role); state.motion = clip; state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase"; state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Stonehoof_BaseColor.png");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + "ForestStonehoof.mat");
        if (mat == null) { mat = new Material(Shader.Find("Razlom/Texture Toon")); AssetDatabase.CreateAsset(mat, Root + "ForestStonehoof.mat"); }
        mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", new Color(1.45f,1.45f,1.45f,1));
        mat.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        mat.SetColor("_MidColor", new Color(.94f,.90f,.91f)); mat.SetFloat("_OutlineWidth", 0); mat.SetFloat("_Smoothness", .12f);

        var root = new GameObject("ForestStonehoof_Runtime");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source); body.transform.SetParent(root.transform, false);
            var guide = body.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "FacingGuide");
            if (guide == null) throw new InvalidOperationException("Нет метки направления FacingGuide");
            Vector3 facing = guide.position - body.transform.position; facing.y = 0;
            body.transform.localRotation = Quaternion.FromToRotation(facing.normalized, Vector3.forward);
            var animator = body.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(mat, renderer.sharedMaterials.Length).ToArray();
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(new Vector3(0, 1.8f, 0), Vector3.one * 9f);
            }
            root.AddComponent<Game.View.StonehoofAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, Root + "ForestStonehoof_Runtime.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        BuildEffects();
        Debug.Log("[stonehoof] Десять клипов, Generic rig, материал и игровой prefab готовы.");
    }

    private static void BuildEffects()
    {
        var sourceDust = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VFX/Wendigo/Materials/M_Wendigo_Dust.mat");
        if (sourceDust == null) throw new InvalidOperationException("Нет материала мягкой пыли.");
        var dust = AssetDatabase.LoadAssetAtPath<Material>(Root + "Stonehoof_Dust.mat");
        if (dust == null) { dust = new Material(sourceDust); AssetDatabase.CreateAsset(dust, Root + "Stonehoof_Dust.mat"); }
        var chip = AssetDatabase.LoadAssetAtPath<Material>(Root + "Stonehoof_Stone.mat");
        if (chip == null)
        {
            chip = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            chip.SetColor("_BaseColor", new Color(.22f,.19f,.12f,1)); chip.SetFloat("_Smoothness", .05f);
            AssetDatabase.CreateAsset(chip, Root + "Stonehoof_Stone.mat");
        }
        var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = temporary.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(temporary);
        foreach (bool wall in new[] { false, true })
        {
            var root = new GameObject(wall ? "VFX_Wall" : "VFX_Hoof");
            try
            {
                var go = new GameObject("Земля и крошка"); go.transform.SetParent(root.transform,false);
                var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.duration = 1.2f; main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.28f,wall ? .8f : .42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(wall ? 1.8f : .6f,wall ? 3.6f : 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(.025f,wall ? .11f : .065f);
                main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startRotation3D = true; main.startRotationX = new ParticleSystem.MinMaxCurve(0,6.28f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0,6.28f); main.startRotationZ = new ParticleSystem.MinMaxCurve(0,6.28f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.65f,.51f,.3f),new Color(.95f,.8f,.5f));
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = wall ? .25f : .10f; shape.rotation = new Vector3(-90,0,0);
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0,(short)(wall ? 22 : 7)) });
                var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = mesh; renderer.sharedMaterial = chip;
                var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true; rotation.x = 5; rotation.y = 3;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,0));
                var cloud = new GameObject("Мягкая земля"); cloud.transform.SetParent(root.transform,false);
                var smoke = cloud.AddComponent<ParticleSystem>(); smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var sm = smoke.main; sm.loop = false; sm.playOnAwake = false; sm.duration = 1.2f; sm.startLifetime = new ParticleSystem.MinMaxCurve(.4f,.8f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(.15f,wall ? 1.1f : .5f); sm.startSize = new ParticleSystem.MinMaxCurve(.12f,wall ? .6f : .28f);
                sm.startColor = new Color(.38f,.3f,.17f,wall ? .32f : .20f); sm.simulationSpace = ParticleSystemSimulationSpace.World;
                var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 70; ss.radius = wall ? .25f : .12f; ss.rotation = new Vector3(-90,0,0);
                var se = smoke.emission; se.rateOverTime = 0; se.SetBursts(new[] { new ParticleSystem.Burst(0,(short)(wall ? 12 : 5)) });
                var sr = smoke.GetComponent<ParticleSystemRenderer>(); sr.sharedMaterial = dust;
                var sc = smoke.colorOverLifetime; sc.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.08f),new GradientAlphaKey(0,1) }); sc.color = gradient;
                var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.4f,1,1.6f));
                ps.useAutoRandomSeed = false; ps.randomSeed = 734; smoke.useAutoRandomSeed = false; smoke.randomSeed = 1723;
                PrefabUtility.SaveAsPrefabAsset(root,Root+root.name+".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
    }
}
