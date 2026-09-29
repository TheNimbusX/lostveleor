using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции дымной завесы SmokeTransition поверх ручных правок владельца (по образцу
    /// CombatHudWcBuilder.Migrations). Префаб не пересобирается: миграция берёт то, что лежит в префабе,
    /// и добавляет только сказанное. Номер — <see cref="SmokeTransition.LayoutVersion"/>, свой счёт.
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем. Съёмочная сборка (batchmode)
    /// зовёт миграцию сама — через Build(false) из RazlomCaptureBuild.
    /// </summary>
    [InitializeOnLoad]
    public static partial class SmokeTransitionBuilder
    {
        /// <summary>
        /// Версия раскладки SmokeTransition.
        /// v1 (30.09, выбор владельца «Карта тушью», концепты a5/a6): путь забега на закрытом дыму —
        /// узел «Карта тушью» (<see cref="SmokeRouteMap"/>) между дымкой и углями.
        /// v2 (30.09, первый кадр карты в игре): огонь спокойнее — нить заголовка, свечение и кольцо
        /// загоревшегося узла тише; у мазков видна рваная кромка кисти; карта растворяется в начале рассеивания,
        /// а не поверх открывшегося мира.
        /// </summary>
        public const int LayoutVersion = 2;

        static bool _waiting;

        static SmokeTransitionBuilder()
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
                Debug.Log("[ui-kit] Дымная завеса открыта в Prefab Mode — доработка до v" + LayoutVersion + " после её закрытия.");
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
            var veil = prefab != null ? prefab.GetComponent<SmokeTransition>() : null;
            return veil != null && veil.LayoutVersion < LayoutVersion;
        }

        /// <summary>
        /// Доводит готовый префаб до <see cref="LayoutVersion"/>; нет префаба или он уже новый — ничего.
        /// true — префаб в последней версии.
        /// </summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponent<SmokeTransition>();
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var veil = contents.GetComponent<SmokeTransition>();
                int from = veil.LayoutVersion;
                Migrate(contents, veil);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Дымная завеса доработана поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(GameObject root, SmokeTransition veil)
        {
            if (veil.LayoutVersion < 1) MigrateTo1(root, veil);
            if (veil.LayoutVersion < 2) MigrateTo2(veil);
            veil.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(veil);
        }

        /// <summary>
        /// v1 — «Карта тушью» (30.09). Прошлая попытка (если узел уже есть) уходит целиком, новая карта встаёт
        /// на место «Углей» — над дымкой, под искрами. Клубы, основа, дымка и угли не меняются.
        /// </summary>
        static void MigrateTo1(GameObject root, SmokeTransition veil)
        {
            var rect = (RectTransform)root.transform;
            Transform old = rect.Find(MapName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            RectTransform map = BuildMap(rect, veil);
            Transform embers = veil.Embers != null ? veil.Embers.transform : rect.Find("Угли");
            if (embers != null && embers.parent == rect) map.SetSiblingIndex(embers.GetSiblingIndex());
            else map.SetAsLastSibling();
        }

        /// <summary>
        /// v2 — настройка карты по первому кадру в игре (те же числа ставит свежая сборка, TuneMap).
        /// Карты нет (ручная правка убрала) — ничего.
        /// </summary>
        static void MigrateTo2(SmokeTransition veil)
        {
            if (veil.Map == null)
            {
                Debug.LogWarning("[ui-kit] В дымной завесе нет карты тушью (SmokeTransition.Map): v2 не тронула карту");
                return;
            }
            TuneMap(veil.Map);
        }
    }
}
