using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ForestWendigoBuilder
{
    private const string Root = "Assets/Resources/Characters/Forest_Wendigo/";
    [InitializeOnLoadMethod]
    private static void QueueBuild() => EditorApplication.delayCall += () => {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(Root + "ForestWendigo.fbx")) return;
        if (!System.IO.File.Exists(Root + "ForestWendigo_Runtime.prefab")) Build();
    };
    [MenuItem("Разлом/Лесной вендиго/Собрать представление")]
    public static void Build()
    {
        const string model = Root + "ForestWendigo.fbx";
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
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "ForestWendigo.controller");
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "ForestWendigo.controller");
        foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
        controller.layers = Array.Empty<AnimatorControllerLayer>(); controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        foreach (string role in new[] { "Idle", "Walk", "Claw", "Leap", "Hit", "Death" })
        {
            var original = originalClips.FirstOrDefault(c => c.name.EndsWith("Wendigo_" + role));
            if (original == null) throw new InvalidOperationException("Нет клипа " + role + ": " + string.Join(",", originalClips.Select(c => c.name)));
            string path = Root + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(original, clip); clip.name = "Wendigo_" + role;
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = role == "Idle" || role == "Walk"; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
            var state = machine.AddState(role); state.motion = clip; state.writeDefaultValues = false;
            controller.AddParameter(role + "Phase", AnimatorControllerParameterType.Float);
            state.timeParameter = role + "Phase"; state.timeParameterActive = true;
            if (role == "Idle") machine.defaultState = state;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "ForestWendigo_BaseColor.png");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + "ForestWendigo.mat");
        if (mat == null) { mat = new Material(Shader.Find("Razlom/Texture Toon")); AssetDatabase.CreateAsset(mat, Root + "ForestWendigo.mat"); }
        mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", new Color(1.7f,1.7f,1.7f,1));
        mat.SetColor("_ShadowColor", Game.View.ViewMaterials.ToonShadow);
        mat.SetColor("_MidColor", new Color(.94f,.90f,.91f)); mat.SetFloat("_OutlineWidth", 0); mat.SetFloat("_Smoothness", .12f);
        if (AssetDatabase.LoadAssetAtPath<Material>(Root + "WendigoWarning.mat") == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("Razlom/Wendigo Warning")), Root + "WendigoWarning.mat");
        var root = new GameObject("ForestWendigo_Runtime");
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
            root.AddComponent<Game.View.ForestWendigoAnimatorView>();
            PrefabUtility.SaveAsPrefabAsset(root, Root + "ForestWendigo_Runtime.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        Debug.Log("[wendigo] Шесть клипов, Generic rig, материал и игровой prefab готовы.");
    }
}
