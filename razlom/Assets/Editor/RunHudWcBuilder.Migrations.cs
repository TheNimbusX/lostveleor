using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции экранов забега RunHudWc поверх ручных правок владельца (по образцу
    /// CombatHudWcBuilder.Migrations). Префаб не пересобирается: каждая миграция берёт то, что лежит
    /// в префабе, и меняет только сказанное; координаты — локальные, в единицах 1920×1080. Номер —
    /// RunHudView.LayoutVersion, свой счёт у RunHudWc. Свежая сборка (Build(true)) — раскладка
    /// 26 сентября и те же миграции, так что вид у обоих путей один.
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем.
    /// </summary>
    [InitializeOnLoad]
    public static partial class RunHudWcBuilder
    {
        /// <summary>
        /// Версия раскладки RunHudWc.
        /// v1 (30.09, выбор владельца — кадр b1-loot-strip-under-panel): строка добычи под панелью
        /// состояния — золото и круги вещей с кольцом редкости, «+K», «+1» и подсказка под мышью;
        /// таймер выживания опускается под строку.
        /// v2 (30.09): v1 собирала строку, но не записывала ссылку RunHudView.Loot — строка лежала в
        /// префабе невидимой, а золото оставалось в панели. v2 находит собранную строку и подключает её.
        /// v3 (30.09, доска concepts-2026-09-30-hud-polish — 1a с подписью фазы из 1b, 3a, 4): засечки, круглая
        /// голова, подпись фазы и числа полосы босса; пул монет строки добычи; перелив, кейкап набора с кольцом
        /// блокировки и подсказки справа на экране награды; «Улучшение» — свиток вместо ромба; значок тайников
        /// панели; итоги со статистикой, стоп-кадром, «Убито» и полосами «Потеряно» / «Остаётся»
        /// (RunHudWcBuilder.Polish, RunHudWcBuilder.SummaryStats).
        /// v4 (30.09, единый набор — лист 5): остальные кейкапы (слоты замены способности и прочие круглые клавиши
        /// «Дыма и света») — тёмный скруглённый квадрат, как у карточек награды; подсказки клавиш пишет вид в формате
        /// «[1] [2] [3] Выбрать   ·   [L] Уйти с добычей».
        /// v5 (02.10, план форм — шаг 3): отметка формы навыка на карточках награды — скрытая группа: огненная нить с
        /// камнем и отсвет за медальоном, её показывает WcRarity.MarkedOnly на экране «Выбери форму» (RunHudWcBuilder.Forms).
        /// </summary>
        public const int LayoutVersion = 5;

        static bool _waiting;

        static RunHudWcBuilder()
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
                Debug.Log("[ui-kit] Экраны забега открыты в Prefab Mode — доработка до v" + LayoutVersion + " после их закрытия.");
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
            var view = prefab != null ? prefab.GetComponentInChildren<RunHudView>(true) : null;
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
            var built = prefab.GetComponentInChildren<RunHudView>(true);
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponentInChildren<RunHudView>(true);
                int from = view.LayoutVersion;
                Migrate(contents, view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Экраны забега доработаны поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(GameObject root, RunHudView view)
        {
            if (view.LayoutVersion < 1) MigrateTo1(view);
            if (view.LayoutVersion < 2) MigrateTo2(view);
            if (view.LayoutVersion < 3) MigrateTo3(view);
            if (view.LayoutVersion < 4) MigrateTo4(view);
            if (view.LayoutVersion < 5) MigrateTo5(view);
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        /// <summary>v4 — единый набор: все оставшиеся круглые кейкапы экранов забега → кейкап листа 5 на месте.</summary>
        static void MigrateTo4(RunHudView view)
        {
            int caps = UiInkKit.RestyleKeycaps(view.transform);
            Debug.Log("[ui-kit] Экраны забега: кейкапов листа 5 — " + caps + ".");
        }

        /// <summary>v1 — строка добычи под панелью состояния (выбор владельца 30.09).</summary>
        static void MigrateTo1(RunHudView view)
        {
            RectTransform root = (RectTransform)view.transform;
            RectTransform strip = view.Loot != null ? view.Loot : BuildLoot(root, view);
            MakeRoomUnderLoot(view, strip);
        }

        /// <summary>
        /// v2 — подключить строку добычи, собранную v1 без ссылки: строку узнаём по её знаку золота или
        /// по имени среди детей экрана. Строки нет (убрана руками) — не собираем заново, только предупреждаем:
        /// без неё золото остаётся в панели, как до v1.
        /// </summary>
        static void MigrateTo2(RunHudView view)
        {
            if (view.Loot != null) return;
            RectTransform strip = view.LootCoin != null ? view.LootCoin.transform.parent as RectTransform : null;
            if (strip == null || strip.name != LootName) strip = view.transform.Find(LootName) as RectTransform;
            if (strip == null)
            {
                Debug.LogWarning("[ui-kit] Строки добычи в экранах забега нет (убрана руками?) — золото остаётся в панели состояния");
                return;
            }
            view.Loot = strip;
        }
    }
}
