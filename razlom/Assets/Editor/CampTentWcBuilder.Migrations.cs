using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции палатки CampTentWc поверх ручных правок владельца (по образцу CampShopsWcBuilder.Migrations).
    /// Префаб не пересобирается: каждая миграция берёт то, что лежит в префабе, и меняет только сказанное. Номер —
    /// CampTentView.LayoutVersion: сборка «Дыма и света» (26.09) ставила 100 — счёт идёт от него.
    /// Свежая сборка (Build(true)) — раскладка сборщика и те же миграции.
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем.
    /// </summary>
    [InitializeOnLoad]
    public static partial class CampTentWcBuilder
    {
        /// <summary>Версия, которую ставит сама сборка (Layout): палатка «Дыма и света» 26 сентября.</summary>
        const int BuiltVersion = 100;

        /// <summary>
        /// Версия раскладки CampTentWc.
        /// v100 (26.09) — палатка в материале «Дым и свет», собрана сборщиком.
        /// v101 (30.09, владелец: шрифт кнопок и вкладок «как в главном меню»): подписи вкладок «Сумка / Атлас» и
        /// фильтров сумки — Philosopher, как у кнопок. Только шрифт: размер и раскладка те же.
        /// </summary>
        public const int LayoutVersion = 101;

        static bool _waiting;

        static CampTentWcBuilder()
        {
            // Съёмочная сборка (batchmode) зовёт миграцию сама — через Build(false).
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
                Debug.Log("[ui-kit] Палатка открыта в Prefab Mode — доработка до v" + LayoutVersion + " после её закрытия.");
                Wait();
                return;
            }
            EnsureMigrated();
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
            var view = prefab != null ? prefab.GetComponentInChildren<CampTentView>(true) : null;
            return view != null && view.LayoutVersion < LayoutVersion;
        }

        /// <summary>
        /// Доводит готовый префаб до <see cref="LayoutVersion"/>; нет префаба или он уже новый — ничего.
        /// true — префаб в последней версии.
        /// </summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponentInChildren<CampTentView>(true);
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponentInChildren<CampTentView>(true);
                int from = view.LayoutVersion;
                Migrate(view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Палатка доработана поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(CampTentView view)
        {
            if (view.LayoutVersion < 101) MigrateTo101(view);
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// v101 — подписи вкладок «Сумка / Атлас» и фильтров сумки → Philosopher. Вкладки берутся по ссылкам вида
        /// (CampTentView.BagTab, AtlasTab, Filters), подпись — по самой кнопке (UiInkKit.HeadingLabels), так что
        /// переименованные руками узлы не мешают. Числа запаса зелий и подписи слотов остаются Nunito.
        /// </summary>
        static void MigrateTo101(CampTentView view)
        {
            var tabs = new List<Selectable> { view.BagTab, view.AtlasTab };
            if (view.Filters != null) tabs.AddRange(view.Filters);
            tabs.RemoveAll(tab => tab == null);
            Debug.Log("[ui-kit] Палатка: подписей вкладок и фильтров на Philosopher — " + UiInkKit.HeadingLabels(tabs) + " из " + tabs.Count + ".");
        }
    }
}
