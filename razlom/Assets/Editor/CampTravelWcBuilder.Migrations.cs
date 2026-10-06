using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции стола «Перед походом» CampTravelWc поверх ручных правок владельца (по образцу CampTentWcBuilder.Migrations).
    /// Готовый префаб не пересобирается: каждая миграция берёт то, что лежит в префабе, и меняет только сказанное. Номер —
    /// CampTravelPanel.LayoutVersion. Свежая сборка (Build(true)) — раскладка сборщика и те же миграции.
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем редакторе): не в Play —
    /// сразу, в Play — после выхода из него. Префаба ещё нет — собирается (новое окно, чужого ничего не трогает). Открыт
    /// префаб в Prefab Mode — ждём, пока его закроют: ручные правки в открытой сцене не трогаем. Съёмочная сборка
    /// (batchmode) зовёт Build(false) сама — RazlomCaptureBuild.
    /// </summary>
    [InitializeOnLoad]
    public static partial class CampTravelWcBuilder
    {
        /// <summary>Версия, которую ставит сама сборка (Layout): окно 06.10.</summary>
        const int BuiltVersion = 1;

        /// <summary>
        /// Версия раскладки CampTravelWc.
        /// v1 (06.10) — окно «Перед походом» в материале «Дым и свет» по table-a: три колонки на дыме, медальоны умений,
        /// зелий и «с собой», кнопки «Отправиться [E]» / «Остаться [Esc]», «[Esc] Закрыть».
        /// </summary>
        public const int LayoutVersion = 1;

        static bool _waiting;

        static CampTravelWcBuilder()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static void MigrateWhenIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Wait();
                return;
            }
            // Идёт импорт или компиляция — следующий такт (после компиляции домен перезагрузится сам).
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += MigrateWhenIdle;
                return;
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PrefabPath)
            {
                if (!NeedsMigration()) return;
                Debug.Log("[ui-kit] Стол «Перед походом» открыт в Prefab Mode — доработка до v" + LayoutVersion + " после его закрытия.");
                Wait();
                return;
            }
            // Нового окна ещё нет — собрать: без префаба стол показывает прежний столбец кнопок (CampPreparation).
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build(false);
            else EnsureMigrated();
        }

        /// <summary>Повторить после выхода из Play или закрытия префаба — один раз, без двойных подписок.</summary>
        static void Wait()
        {
            if (_waiting) return;
            _waiting = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
            PrefabStage.prefabStageClosing += OnStageClosing;
        }

        static void StopWaiting()
        {
            _waiting = false;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            PrefabStage.prefabStageClosing -= OnStageClosing;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            StopWaiting();
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static void OnStageClosing(PrefabStage stage)
        {
            if (stage.assetPath != PrefabPath) return;
            StopWaiting();
            // Сцена префаба ещё закрывается — файл трогаем на следующем такте.
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static bool NeedsMigration()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab != null ? prefab.GetComponentInChildren<CampTravelPanel>(true) : null;
            return panel != null && panel.LayoutVersion < LayoutVersion;
        }

        /// <summary>
        /// Доводит готовый префаб до <see cref="LayoutVersion"/>; нет префаба или он уже новый — ничего.
        /// true — префаб в последней версии.
        /// </summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponentInChildren<CampTravelPanel>(true);
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = contents.GetComponentInChildren<CampTravelPanel>(true);
                int from = panel.LayoutVersion;
                Migrate(panel);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Стол «Перед походом» доработан поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            // Не общий SaveAssets: он сбрасывал на диск чужие грязные ассеты (02.10 откатил 3 материала босса). Префаб уже
            // записан SaveAsPrefabAsset; дописываются только материалы «Дыма и света», если миграция их тронула.
            CampInkParts.SaveInkMaterials();
            return true;
        }

        /// <summary>Цепочка миграций: следующая — «if (panel.LayoutVersion &lt; 2) MigrateTo2(panel);» перед строкой версии.</summary>
        static void Migrate(CampTravelPanel panel)
        {
            panel.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(panel);
        }
    }
}
