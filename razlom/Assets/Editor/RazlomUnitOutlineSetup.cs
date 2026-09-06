using Game.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.EditorTools
{
    /// <summary>Один рецепт подключения обводки для редактора и Windows-сборки.</summary>
    public sealed class RazlomUnitOutlineSetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Configure();

        [InitializeOnLoadMethod]
        private static void OnLoad() => EditorApplication.delayCall += Configure;

        [MenuItem("Разлом/Настроить обводку юнитов")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Shaders/RazlomUnitOutline.shader");
            if (renderer == null || shader == null) return;

            bool changed = false;
            UnitOutlineFeature outline = null;
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature is UnitOutlineFeature found) outline = found;
                // Старую оболочку сохраняем в ассете выключенной: два контура
                // одновременно дали бы ту самую двойную зубчатую кромку.
                if (feature != null && feature.name == "EnemyInkOutline" && feature.isActive)
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                    changed = true;
                }
            }
            if (outline == null)
            {
                outline = ScriptableObject.CreateInstance<UnitOutlineFeature>();
                outline.name = "UnitSilhouetteOutline";
                outline.SetShader(shader);
                AssetDatabase.AddObjectToAsset(outline, renderer);
                renderer.rendererFeatures.Add(outline);
                outline.Create();
                changed = true;
            }
            if (!changed) return;
            EditorUtility.SetDirty(outline);
            renderer.SetDirty();
            AssetDatabase.SaveAssets();
        }
    }
}
