using Game.View;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class CampChoiceUiAuthoring
    {
        public static void Build()
        {
            foreach (var name in new[] { "CampPreparation", "CampForge", "CampTraderProgression" })
            {
                string path = "Assets/Resources/UI/Prefabs/" + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { CampChoiceFeedback.Install(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }
}
