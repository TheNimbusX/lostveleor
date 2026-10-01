using Game.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.EditorTools
{
    /// <summary>
    /// Подключение комикс-рисовки (проба 01.10) к PC_Renderer: один рецепт для редактора, Windows-сборки и
    /// зеркала съёмки (RazlomCaptureBuild зовёт Configure явно — в batchmode delayCall не надёжен).
    /// Идемпотентно: фича уже есть — ничего не пишет. Сама фича выключена, пока не включена рисовка
    /// (ComicStyle.Enabled): тогда она не ставит проходов, и игра выглядит как без неё.
    /// Шейдеры лежат в Resources и дополнительно указаны в фиче — сборка их не выбросит.
    /// </summary>
    public sealed class ComicStyleSetup : IPreprocessBuildWithReport
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string StyleShaderPath = "Assets/Resources/Shaders/RazlomComicStyle.shader";
        private const string TiltShaderPath = "Assets/Resources/Shaders/RazlomComicTiltShift.shader";
        private const string ToggleMenu = "Разлом/Комикс-рисовка/Включена (редактор и Play)";
        private static int _retries;

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => ConfigureNow();

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            _retries = 0;
            EditorApplication.delayCall += Configure;
            ComicStyle.Changed -= RepaintViews;
            ComicStyle.Changed += RepaintViews;
        }

        /// <summary>Из редактора: не в Play и не посреди компиляции/импорта — иначе позже.</summary>
        [MenuItem("Разлом/Комикс-рисовка/Подключить к рендереру")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (_retries++ < 20) EditorApplication.delayCall += Configure;
                return;
            }
            ConfigureNow();
        }

        /// <summary>Для сборки: вызывается до сериализации плеера.</summary>
        public static void ConfigureNow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var style = AssetDatabase.LoadAssetAtPath<Shader>(StyleShaderPath);
            var tilt = AssetDatabase.LoadAssetAtPath<Shader>(TiltShaderPath);
            if (renderer == null || style == null || tilt == null)
            {
                Debug.LogWarning($"[comic-style] не найдено: рендерер={renderer != null}, шейдер стиля={style != null}, диорамы={tilt != null}.");
                return;
            }

            ComicStyleFeature feature = null;
            foreach (var candidate in renderer.rendererFeatures)
                if (candidate is ComicStyleFeature found) { feature = found; break; }

            bool changed = false;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<ComicStyleFeature>();
                feature.name = "ComicStyle";
                feature.hideFlags |= HideFlags.HideInHierarchy;
                feature.SetShaders(style, tilt);
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
                changed = true;
            }
            else if (feature.StyleShader != style || feature.TiltShader != tilt)
            {
                feature.SetShaders(style, tilt);
                changed = true;
            }
            if (!changed) return;

            feature.Create();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            renderer.SetDirty();
            // Сохраняется только рендерер: чужие несохранённые правки в редакторе не трогаем.
            AssetDatabase.SaveAssetIfDirty(renderer);
            Debug.Log("[comic-style] Фича ComicStyle подключена к PC_Renderer. Выключена, пока не включена рисовка: F8 или «Разлом → Комикс-рисовка».");
        }

        [MenuItem(ToggleMenu)]
        private static void Toggle() => ComicStyle.SetEnabled(!ComicStyle.Enabled);

        [MenuItem(ToggleMenu, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, ComicStyle.Enabled);
            return true;
        }

        private static void RepaintViews() => InternalEditorUtility.RepaintAllViews();
    }
}
