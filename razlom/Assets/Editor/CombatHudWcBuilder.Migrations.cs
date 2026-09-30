using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции боевого HUD CombatHudWc поверх ручных правок владельца (по образцу
    /// PauseMenuBuilder.Migrations). Префаб не пересобирается: каждая миграция берёт то, что лежит
    /// в префабе, и меняет только сказанное; координаты — только локальные (у Canvas в сцене
    /// префаба нулевой масштаб). Номер — CombatHudView.LayoutVersion у CombatHudWc, свой счёт
    /// (у старого CombatHud свой, CombatHudBuilder).
    ///
    /// Запуск — сам, после компиляции, когда редактор не в Play (владелец и Костя играют в общем
    /// редакторе): не в Play — сразу, в Play — после выхода из него. Открыт префаб в Prefab Mode —
    /// ждём, пока его закроют: ручные правки в открытой сцене не трогаем.
    /// </summary>
    [InitializeOnLoad]
    public static partial class CombatHudWcBuilder
    {
        /// <summary>
        /// Версия раскладки CombatHudWc.
        /// v1 (29.09, ревью HUD владельца): портрет — один рисунок с мягкой маской вместо
        /// стенсил-маски и двух копий, живой портрет; числа здоровья и лавидия всегда видны, опыт —
        /// подписью «Ур. N · X / Y» под тонкой полосой; карта 230 → 253, подпись сдвинута, затемнение
        /// края по округлой форме карты; кошелёк лагеря.
        /// v2 (30.09, этап 4 — кадр 1a): строка эффектов героя над портретом (круги с кольцом-таймером,
        /// секунды и «×2» в углу значка, «+N», подсказка) вместо ряда значков зелий «Эффекты зелий».
        /// v3 (30.09, единый набор — лист 5): кейкапы — тёмный скруглённый квадрат вместо круга в дыме,
        /// «[Alt] Подробнее», вложенная подсказка ключевого слова у подсказки способности и артефакта.
        /// </summary>
        public const int LayoutVersion = 3;
        const float OldMapSize = 230f;

        static bool _waiting;

        static CombatHudWcBuilder()
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
                Debug.Log("[ui-kit] Боевой HUD открыт в Prefab Mode — доработка до v" + LayoutVersion + " после его закрытия.");
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
            var view = prefab != null ? prefab.GetComponentInChildren<CombatHudView>(true) : null;
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
            var built = prefab.GetComponentInChildren<CombatHudView>(true);
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponentInChildren<CombatHudView>(true);
                int from = view.LayoutVersion;
                Migrate(contents, view);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Боевой HUD доработан поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(GameObject root, CombatHudView view)
        {
            if (view.LayoutVersion < 1) MigrateTo1(root, view);
            if (view.LayoutVersion < 2) MigrateTo2(root, view);
            if (view.LayoutVersion < 3) MigrateTo3(root, view);
            view.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(view);
        }

        /// <summary>v1 — ревью HUD владельца 29.09 (P, M, N и кошелёк лагеря).</summary>
        static void MigrateTo1(GameObject root, CombatHudView view)
        {
            SoftenPortrait(view);
            // Числа здоровья и лавидия видны всегда: область «под мышью» (VitalsHit) ушла из CombatHudView,
            // её поле в префабе отпадёт при сохранении. Числа появляются по буквам вместе с полосами.
            foreach (TMP_Text value in new[] { view.HealthText, view.LavidiumText })
                if (value != null) UiInkKit.Revealed(value, VitalsTextDelay);
            ExperienceCaption(view.ExperienceText);
            GrowMinimap(view);
            if (view.MinimapArea != null && view.MinimapArea.Find(MapVeilName) is Transform veil) ShapedVeil(veil.GetComponent<Image>());
            else Debug.LogWarning("[ui-kit] Нет «Карта/" + MapVeilName + "» в боевом HUD: затемнение края карты не тронуто");
            if (view.CampWallet == null) BuildCampWallet((RectTransform)root.transform, view);
        }

        /// <summary>
        /// Старый портрет (26 сентября): «Портрет/Диск» со стенсил-маской, рисунок в «Диск/Низ» под
        /// RectMask2D и его копия в «Портрет/Над кругом». Рисунок переезжает прямо в «Портрет» сразу
        /// за диском — на то же место на экране, копия и обе маски уходят; дальше — мягкая маска и
        /// живой портрет, как у свежей сборки.
        /// </summary>
        static void SoftenPortrait(CombatHudView view)
        {
            RawImage art = view.Portrait;
            if (art == null)
            {
                Debug.LogWarning("[ui-kit] У боевого HUD нет портрета (CombatHudView.Portrait): портрет не тронут");
                return;
            }
            RectTransform artRect = art.rectTransform;
            RectTransform lower = artRect.parent as RectTransform;
            RectTransform disk = lower != null && lower.name == "Низ" ? lower.parent as RectTransform : null;
            RectTransform portrait = disk != null ? disk.parent as RectTransform : null;
            if (portrait != null)
            {
                // Место на экране не меняется: локальные сдвиги «Низа» и «Диска» уходят в позицию рисунка
                // (масштаб у них единичный, поворота нет — так собирает сборщик).
                Vector3 place = disk.localPosition + lower.localPosition + artRect.localPosition;
                artRect.SetParent(portrait, false);
                artRect.localPosition = place;
                artRect.SetSiblingIndex(disk.GetSiblingIndex() + 1);
                if (lower.childCount == 0) Object.DestroyImmediate(lower.gameObject);
            }
            else
            {
                // Уже без «Низа» (правка руками или повторный запуск): рисунок — прямо в «Портрет».
                portrait = artRect.parent as RectTransform;
                disk = portrait != null ? portrait.Find("Диск") as RectTransform : null;
            }
            if (portrait == null)
            {
                Debug.LogWarning("[ui-kit] Портрет боевого HUD не на месте: мягкая маска не поставлена");
                return;
            }

            Transform top = portrait.Find("Над кругом");
            if (top != null) Object.DestroyImmediate(top.gameObject);
            // Сравнение через оператор Unity: в редакторе GetComponent без компонента отдаёт «ненастоящий null».
            Mask stencil = disk != null ? disk.GetComponent<Mask>() : null;
            if (stencil != null) Object.DestroyImmediate(stencil);

            view.Portrait = SoftPortrait(art);
            CheckPortraitLayout(portrait, artRect);
            view.PortraitMotion = LivePortrait(portrait, disk, art, view.DangerPulse);
        }

        /// <summary>
        /// Карта +10 % (владелец 29.09: только карта). Карта растёт от своей опоры (у сборщика — правый
        /// верхний угол, поэтому влево и вниз); подпись под ней едет за нижней кромкой и серединой.
        /// </summary>
        static void GrowMinimap(CombatHudView view)
        {
            RectTransform frame = view.MinimapFrame;
            if (frame == null) return;
            Vector2 was = frame.sizeDelta;
            Vector2 size = was * (MapSize / OldMapSize);
            frame.sizeDelta = size;
            Vector2 grow = size - was;
            RectTransform caption = view.MinimapCaptionPanel;
            if (caption == null) return;
            if (caption.anchorMin != frame.anchorMin || caption.anchorMax != frame.anchorMax || caption.anchorMin != caption.anchorMax)
            {
                Debug.LogWarning("[ui-kit] Подпись карты привязана не так, как карта: не сдвинута, проверить руками");
                return;
            }
            // Середина карты по x сдвигается на (0,5 − опора.x)·рост, нижняя кромка — на −опора.y·рост.
            caption.anchoredPosition += new Vector2((.5f - frame.pivot.x) * grow.x, -frame.pivot.y * grow.y);
        }
    }
}
