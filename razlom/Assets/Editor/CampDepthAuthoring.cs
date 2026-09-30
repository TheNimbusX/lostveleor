using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    public static class CampDepthAuthoring
    {
        public const string PalettePath = "Assets/Resources/Environment/Camp/Depth/CampDepthPalette.asset";
        [MenuItem("Разлом/Лагерь/Глубина земли и окружения")]
        public static void InstallFromMenu() { Debug.Log(Install()); }
        public static string Install()
            => LoadedSceneInstall();
        // Работает с уже открытой сценой, в том числе с её временной копией для capturebuild.
        public static string LoadedSceneInstall()
        {
            if (Application.isPlaying) return "[camp-depth] Устанавливать в остановленном редакторе";
            var scene = EditorSceneManager.GetActiveScene(); GameObject camp = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var world in root.GetComponentsInChildren<SceneWorldView>(true))
                    if (world.CampRoot != null && world.CampRoot.scene == scene) { camp = world.CampRoot; break; }
                if (camp != null) break;
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    if (node.name == "CampRoot") { camp = node.gameObject; break; }
                if (camp != null) break;
            }
            if (camp == null) return "[camp-depth] Нет CampRoot";
            const string folder = "Assets/Resources/Environment/Camp/Depth";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources/Environment/Camp", "Depth");
            var palette = AssetDatabase.LoadAssetAtPath<CampDepthPalette>(PalettePath);
            if (palette == null) { palette = ScriptableObject.CreateInstance<CampDepthPalette>(); AssetDatabase.CreateAsset(palette, PalettePath); }
            Undo.SetCurrentGroupName("Глубина земли и окружения лагеря"); int undo = Undo.GetCurrentGroup();
            var pass = camp.GetComponent<CampDepthPass>(); if (pass == null) pass = Undo.AddComponent<CampDepthPass>(camp);
            Undo.RecordObject(pass, "Палитра глубины лагеря"); pass.Palette = palette; pass.PreviewEnabled = true; pass.DiscoverAnchors(); pass.RefreshPass();
            EditorUtility.SetDirty(pass); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(camp.scene); Undo.CollapseUndoOperations(undo);
            SceneView.RepaintAll(); return pass.Describe();
        }
        [MenuItem("Разлом/Лагерь/Выключить глубину земли")]
        public static void DisableFromMenu()
        {
            var pass = Object.FindAnyObjectByType<CampDepthPass>(FindObjectsInactive.Include); if (pass == null) return;
            Undo.RecordObject(pass, "Выключить глубину земли"); pass.PreviewEnabled = false; pass.RefreshPass();
            EditorUtility.SetDirty(pass); EditorSceneManager.MarkSceneDirty(pass.gameObject.scene); SceneView.RepaintAll();
        }
    }
}
