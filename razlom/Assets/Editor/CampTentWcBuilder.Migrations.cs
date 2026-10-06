using System.Collections.Generic;
using Game.View;
using TMPro;
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
        /// v102 (01.10, владелец: ресурс Пелага — «Концентрация», «лавидий у Пелага звучит глупо»): заголовок группы
        /// листа героя «ЛАВИДИЙ» → «КОНЦЕНТРАЦИЯ» с нитью за новой шириной подписи; заготовки подписей строк.
        /// v103 (06.10, окна лагеря owner-review-0610): строка вкладок «СУМКА ◇ КЛЯТВЫ ◇ АТЛАС» над окнами и пепел справа,
        /// вкладка «Клятвы» (ряд слотов, карта владельца, 4 группы печатей, карточка), атлас 3×4 с карточкой вместо
        /// всплывашки, «[Esc] Закрыть» (CampTentWcBuilder.Oaths / .Atlas). Старые вкладки в панели выключены, сумка на месте.
        /// </summary>
        public const int LayoutVersion = 104;

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
            // Не общий SaveAssets: он сбрасывал на диск чужие грязные ассеты (02.10 откатил 3 материала босса). Префаб уже
            // записан SaveAsPrefabAsset, тема сохраняет себя сама; дописываются только материалы «Дыма и света», если миграция
            // их тронула.
            CampInkParts.SaveInkMaterials();
            return true;
        }

        static void Migrate(CampTentView view)
        {
            if (view.LayoutVersion < 101) MigrateTo101(view);
            if (view.LayoutVersion < 102) MigrateTo102(view);
            if (view.LayoutVersion < 103) MigrateTo103(view);
            if (view.LayoutVersion < 104) MigrateTo104(view);
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

        /// <summary>Строки листа героя с ресурсом способностей: StatNames[6] — запас, [7] — восстановление.</summary>
        static readonly int[] ResourceStatRows = { 6, 7 };

        /// <summary>
        /// v102 — ресурс способностей игроку «Концентрация». Заголовок группы ищется по старому имени узла
        /// («Группа Лавидий», так называла его сборка 26.09): нет такого — свежая сборка или ручная правка, не трогаем.
        /// Текст — прописными, как в Header; нить «Линия» начинается за новой подписью (та же формула, что в Header,
        /// но от фактического левого края подписи). Подписи строк в игре ставит CampInventoryView из CampServiceText,
        /// здесь меняются только старые заготовки — чтобы префаб в редакторе не показывал прежнее слово. Узлы
        /// переименовываются как у свежей сборки; идентификаторы в коде (StatType.MaxLavidium…) прежние.
        /// </summary>
        static void MigrateTo102(CampTentView view)
        {
            int changed = 0;
            Transform group = FindDeep(view.transform, "Группа Лавидий");
            if (group != null)
            {
                group.name = "Группа " + StatGroupNames[2];
                Transform caption = group.Find("Надпись");
                TMP_Text label = caption != null ? caption.GetComponent<TMP_Text>() : null;
                if (label != null)
                {
                    label.text = StatGroupNames[2].ToUpperInvariant();
                    EditorUtility.SetDirty(label);
                    var line = group.Find("Линия") as RectTransform;
                    if (line != null)
                        line.offsetMin = new Vector2(label.rectTransform.offsetMin.x + label.GetPreferredValues(label.text).x + 14f, line.offsetMin.y);
                    else Debug.LogWarning("[ui-kit] Палатка: у заголовка «" + group.name + "» нет «Линия» — нить не сдвинута, проверить руками");
                    changed++;
                }
                else Debug.LogWarning("[ui-kit] Палатка: у заголовка «" + group.name + "» нет «Надпись» — текст не тронут, проверить руками");
            }

            string[] old = { "Лавидий", "Лавидий/с" };
            for (int k = 0; k < ResourceStatRows.Length; k++)
            {
                int i = ResourceStatRows[k];
                if (view.StatRows != null && i < view.StatRows.Length && view.StatRows[i] != null && view.StatRows[i].name == "Стат " + old[k])
                    view.StatRows[i].name = "Стат " + StatNames[i];
                if (view.StatLabels == null || i >= view.StatLabels.Length || view.StatLabels[i] == null || view.StatLabels[i].text != old[k]) continue;
                view.StatLabels[i].text = StatNames[i];
                EditorUtility.SetDirty(view.StatLabels[i]);
                changed++;
            }
            Debug.Log("[ui-kit] Палатка: «лавидий» → «концентрация» в листе героя — подписей " + changed + " из 3.");
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
