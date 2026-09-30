using System;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции меню паузы PauseMenuWc поверх ручных правок владельца (по образцу PauseMenuBuilder.Migrations
    /// и CombatHudWcBuilder.Migrations). Префаб не пересобирается: миграция берёт то, что лежит в префабе,
    /// и меняет только сказанное. Номер — PauseMenuView.LayoutVersion у PauseMenuWc, свой счёт (у старого
    /// PauseMenu.prefab свой, PauseMenuBuilder).
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем.
    /// </summary>
    [InitializeOnLoad]
    public static partial class PauseMenuWcBuilder
    {
        /// <summary>
        /// Версия раскладки PauseMenuWc.
        /// v1 (30.09, выбор владельца «Настройки Б»): окно настроек во всю ширину — вкладки столбцом
        /// слева со значками (Изображение / Звук / Игра / Интерфейс и доступность / Управление), опции
        /// в центре, описание справа, футер «Esc Назад · F Сбросить · Enter Применить». Окно
        /// «Управление» стало вкладкой. Новые опции: яркость, громкость интерфейса, звук в фоне, язык,
        /// пауза при сворачивании, цифры урона, полоски врагов, тряска экрана, вспышки, ряд способностей.
        /// v2 (30.09, единый набор — лист 5): кейкапы футера и вкладки «Управление» — тёмный скруглённый квадрат вместо
        /// круга в дыме; подсказка под вкладками — «[Tab] [Shift+Tab] Вкладки» (один формат подсказки клавиши); окно
        /// подтверждения — шкала листа 5 (32 / 18 / 14, кнопки 24) и кейкап Esc на «Отмене».
        /// </summary>
        public const int LayoutVersion = 2;

        static bool _waiting;

        static PauseMenuWcBuilder()
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
                Debug.Log("[ui-kit] Меню паузы открыто в Prefab Mode — доработка до v" + LayoutVersion + " после его закрытия.");
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
            var view = prefab != null ? prefab.GetComponentInChildren<PauseMenuView>(true) : null;
            return view != null && view.LayoutVersion < LayoutVersion;
        }

        /// <summary>
        /// Доводит готовый префаб до <see cref="LayoutVersion"/>; нет префаба или он уже новый — ничего.
        /// Ошибка посреди миграции — префаб не сохраняется (правки остаются только в выгружаемой копии).
        /// true — префаб в последней версии.
        /// </summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponentInChildren<PauseMenuView>(true);
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            bool saved = false;
            try
            {
                var view = contents.GetComponentInChildren<PauseMenuView>(true);
                int from = view.LayoutVersion;
                Migrate(view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                saved = true;
                Debug.Log("[ui-kit] Меню паузы доработано поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            catch (Exception error)
            {
                Debug.LogError("[ui-kit] Меню паузы: миграция до v" + LayoutVersion + " не удалась, префаб не тронут.");
                Debug.LogException(error);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            if (saved) AssetDatabase.SaveAssets();
            return saved;
        }

        static void Migrate(PauseMenuView view)
        {
            if (view.LayoutVersion < 1) MigrateTo1(view);
            if (view.LayoutVersion < 2) MigrateTo2(view);
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// v2 — единый набор: кейкапы листа 5 на месте (узлы, ширины и ссылки строк назначения не меняются) и подсказка
        /// вкладок в формате «[Клавиша] Действие». Руками переписанную подсказку не трогаем.
        /// </summary>
        static void MigrateTo2(PauseMenuView view)
        {
            int caps = UiInkKit.RestyleKeycaps(view.transform);
            Debug.Log("[ui-kit] Меню паузы: кейкапов листа 5 — " + caps + ".");
            Transform hint = view.SettingsPanel != null ? FindDeep(view.SettingsPanel, "Подсказка вкладок") : null;
            TMP_Text label = hint != null ? hint.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null) Debug.LogWarning("[ui-kit] В окне настроек нет «Подсказка вкладок» — подсказка не приведена к «[Tab] Вкладки»");
            else if (label.text == "Tab · Shift+Tab — вкладки")
            {
                label.text = TabsHintText;
                label.fontSize = T.Size(UiTheme.TextStep.Caption);
            }
            ConfirmToSheet(view);
        }

        /// <summary>
        /// Окно подтверждения по листу 5: заголовок — ступень Title, пояснение — Body, отсчёт — Caption, подписи кнопок —
        /// Heading; на «Отмене» — кейкап Esc (Esc отменяет подтверждение, PauseMenu). Место и размер окна не меняются.
        /// </summary>
        static void ConfirmToSheet(PauseMenuView view)
        {
            if (view.ConfirmPanel == null)
            {
                Debug.LogWarning("[ui-kit] В меню паузы нет окна подтверждения — к листу 5 не приведено");
                return;
            }
            if (view.ConfirmTitle != null)
            {
                view.ConfirmTitle.fontSize = T.Size(UiTheme.TextStep.Title);
                if (view.ConfirmTitle.enableAutoSizing) view.ConfirmTitle.fontSizeMax = T.Size(UiTheme.TextStep.Title);
            }
            if (view.ConfirmText != null) view.ConfirmText.fontSize = T.Size(UiTheme.TextStep.Body);
            if (view.ConfirmCountdown != null) view.ConfirmCountdown.fontSize = T.Size(UiTheme.TextStep.Caption);
            foreach (TMP_Text label in new[] { view.ConfirmYesLabel, view.ConfirmNoLabel })
                if (label != null)
                {
                    label.fontSize = T.Size(UiTheme.TextStep.Heading);
                    if (label.enableAutoSizing) label.fontSizeMax = T.Size(UiTheme.TextStep.Heading);
                }
            if (view.ConfirmNo != null) UiInkKit.ButtonKey((RectTransform)view.ConfirmNo.transform, "Esc", 30f);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        /// <summary>
        /// v1: прежние окна «Настройки» (три вкладки сверху, 1000 × 780 справа от паузы) и «Управление»
        /// уходят целиком — владелец выбрал другую раскладку, переставлять их части нечего. На их место
        /// встаёт окно «Настройки Б» (BuildSettings) тем же порядком среди детей: подтверждение остаётся
        /// поверх. Пауза, подтверждение, фон и числа движения в PauseMenuView не трогаются.
        /// </summary>
        static void MigrateTo1(PauseMenuView view)
        {
            var root = (RectTransform)view.transform;
            int index = view.SettingsPanel != null ? view.SettingsPanel.GetSiblingIndex()
                : view.ConfirmPanel != null ? view.ConfirmPanel.GetSiblingIndex() : root.childCount;
            foreach (RectTransform old in new[] { view.SettingsPanel, view.ControlsPanel })
                if (old != null) Object.DestroyImmediate(old.gameObject);

            // Ссылки, которых в новом окне нет: пустые, а не «Missing».
            view.ControlsPanel = null;
            view.ControlsReset = null;
            view.ControlsBack = null;
            view.GameText = null;
            view.Status = null;
            view.BindingsContent = null;

            RectTransform settings = BuildSettings(root, view);
            settings.SetSiblingIndex(Mathf.Clamp(index, 0, root.childCount - 1));
        }
    }
}
