using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Game.EditorTools
{
    /// <summary>
    /// Собирает префаб боевого HUD из UI-пака (Assets/UI/Kit).
    ///
    /// ЗАЧЕМ СБОРЩИК, А НЕ РУЧНОЙ ПРЕФАБ. Первую версию проще и точнее
    /// разложить кодом по концепту v3-hud, чем кликать. Дальше префаб —
    /// собственность владельца: сборщик сам создаёт его ТОЛЬКО если файла
    /// нет, а пересборка из меню спрашивает подтверждение, потому что
    /// стирает ручные правки.
    ///
    /// Заодно ставит то, без чего TextMeshPro не работает: TMP Essential
    /// Resources (шейдеры и настройки) и SDF-шрифты гарнитуры проекта (GameTypography.Family).
    /// </summary>
    [InitializeOnLoad]
    public static partial class CombatHudBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CombatHud.prefab";

        /// <summary>
        /// Версия раскладки. Префаб с меньшей версией пересобирается сам и
        /// теряет ручные правки — поднимать ТОЛЬКО с согласия владельца.
        /// v2 (15 сентября) — раскладка один в один по v3-hud, владелец согласился.
        /// v3 (15 сентября) — НЕ пересборка, а правка поверх ручных правок владельца:
        /// HUD на 65%, имя под портретом, карта под маской формы рамки.
        /// v4 (15 сентября) — тоже поверх правок: карта +10%, герой вплотную к
        /// способностям, подложка кувырка как у зелий, читаемые числа опыта, метки карты в стиле.
        /// v5 (15 сентября) — починка: полосы жизни и лавидия, пропавшие после v3, и место героя.
        /// v6 (15 сентября) — гарнитура проекта сменена на Tektur; размеры и цвета текста не трогаются.
        /// v7 (15 сентября) — сетка нижней группы: общая нижняя линия, высота и полосы (по просьбе владельца).
        /// v8 (15 сентября) — значок уровня внутри угла портрета, не на плашке имени.
        /// v9 (15 сентября) — ряд способностей по центру, плитка кувырка почти как зелье,
        /// полосы крупнее, карта и подпись +10%, mip-уровни у картинок HUD.
        /// v10 (15 сентября) — подсказка: тёмный текст, сетка параметров, плоские значки.
        /// v11 (15 сентября) — метки и подсказки карты +10%, лента подписи +10% и шире текста.
        /// v12 (15 сентября) — длинное название способности сжимается, а не обрезается.
        /// </summary>
        public const int LayoutVersion = 12;

        /// <summary>Префаб старше этой версии собирается заново; новее — дорабатывается миграциями.</summary>
        const int RebuildBelow = 2;
        const string FontFolder = "Assets/UI/Fonts";
        const string EssentialsSettings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>Кириллица, латиница, цифры и знаки HUD — запекаются в атлас заранее.</summary>
        const string Charset =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
            " .,:;!?+-−–—/\\%()[]«»\"'·×";

        static CombatHudBuilder()
        {
            // В живом редакторе префаб появляется сам после первой компиляции.
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                // Открытый в Prefab Mode HUD чинится прямо на сцене префаба:
                // несохранённые ручные правки владельца не теряются.
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                var staged = stage != null && stage.assetPath == PrefabPath
                    ? stage.prefabContentsRoot.GetComponentInChildren<CombatHudView>(true) : null;
                if (staged != null && staged.LayoutVersion < LayoutVersion)
                {
                    // Пересборка перезаписывает файл: открытая сцена префаба ушла бы в разнобой.
                    StageUtility.GoToMainStage();
                    stage = null;
                }
                if (stage != null && stage.assetPath == PrefabPath)
                {
                    if (RepairMetrics(stage.prefabContentsRoot))
                    {
                        EditorSceneManager.MarkSceneDirty(stage.scene);
                        Debug.Log("[ui-kit] Строки параметров подсказки починены в открытом префабе — сохрани его.");
                    }
                    return;
                }
                EnsureBuilt(false);
            };
        }

        /// <summary>
        /// Первая сборка сохранила строки параметров с «missing script»: класс
        /// лежал не в своём файле. Снимает пустые компоненты, ставит настоящие
        /// и заново связывает их с иконкой, числом и списком у CombatHudView.
        /// </summary>
        static bool RepairMetrics(GameObject root)
        {
            var view = root.GetComponentInChildren<CombatHudView>(true);
            Transform metrics = view != null && view.Tooltip != null ? view.Tooltip.Find("Metrics") : null;
            if (metrics == null) return false;
            bool changed = false;
            var found = new System.Collections.Generic.List<HudTooltipMetric>();
            foreach (Transform item in metrics)
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) > 0)
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(item.gameObject);
                    changed = true;
                }
                var metric = item.GetComponent<HudTooltipMetric>();
                if (metric == null) { metric = item.gameObject.AddComponent<HudTooltipMetric>(); changed = true; }
                if (metric.Icon == null) { metric.Icon = item.Find("Icon")?.GetComponent<UnityEngine.UI.Image>(); changed = true; }
                if (metric.Value == null) { metric.Value = item.Find("Value")?.GetComponent<TMP_Text>(); changed = true; }
                found.Add(metric);
            }
            bool wired = view.TooltipMetrics != null && view.TooltipMetrics.Length == found.Count;
            for (int i = 0; wired && i < found.Count; i++) wired = view.TooltipMetrics[i] == found[i];
            if (!wired) { view.TooltipMetrics = found.ToArray(); changed = true; }
            if (changed) EditorUtility.SetDirty(view);
            return changed;
        }

        [MenuItem("Разлом/UI/Собрать боевой HUD")]
        static void RebuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Боевой HUD",
                    "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            EnsureBuilt(true);
        }

        /// <summary>Создаёт префаб, если его нет (или всегда при <paramref name="force"/>). false — не получилось.</summary>
        public static bool EnsureBuilt(bool force)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            CombatHudView built = existing != null ? existing.GetComponentInChildren<CombatHudView>(true) : null;
            if (!force && existing != null && built != null && built.LayoutVersion < RebuildBelow)
            {
                Debug.Log("[ui-kit] Раскладка HUD устарела (v" + built.LayoutVersion + " → v" + LayoutVersion
                    + "), префаб пересобирается; ручные правки сброшены с согласия владельца.");
                force = true;
            }
            if (!force && existing != null)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    bool changed = RepairMetrics(contents);
                    var view = contents.GetComponentInChildren<CombatHudView>(true);
                    if (view != null && view.LayoutVersion < LayoutVersion)
                    {
                        int from = view.LayoutVersion;
                        Migrate(contents, view);
                        changed = true;
                        Debug.Log("[ui-kit] HUD доработан поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                return true;
            }
            if (!EnsureEssentials()) return false;

            var kit = new Kit
            {
                Regular = EnsureFont("Regular"),
                SemiBold = EnsureFont("SemiBold"),
                Bold = EnsureFont("Bold"),
            };
            if (kit.Regular == null || kit.SemiBold == null || kit.Bold == null)
            {
                Debug.LogError("[ui-kit] Шрифты " + GameTypography.Family + " SDF не созданы — префаб HUD не собран.");
                return false;
            }
            if (Chrome("panel_navy") == null)
            {
                Debug.LogError("[ui-kit] Нет спрайтов в " + UiKitImport.KitRoot + "/Chrome — префаб HUD не собран.");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject root = BuildLayout(kit);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ui-kit] Боевой HUD собран: " + PrefabPath);
            return true;
        }

        internal sealed class Kit
        {
            public TMP_FontAsset Regular, SemiBold, Bold;
        }

        internal static bool EnsureEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(EssentialsSettings) != null) return true;
            string package = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(package))
            {
                Debug.LogError("[ui-kit] Не найден " + package);
                return false;
            }
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(EssentialsSettings) != null) return true;
            // В открытом редакторе импорт пакета завершается позже вызова.
            AssetDatabase.importPackageCompleted -= RetryAfterImport;
            AssetDatabase.importPackageCompleted += RetryAfterImport;
            Debug.Log("[ui-kit] Импортирую TMP Essential Resources; HUD соберётся после импорта.");
            return false;
        }

        static void RetryAfterImport(string name)
        {
            AssetDatabase.importPackageCompleted -= RetryAfterImport;
            EditorApplication.delayCall += () => EnsureBuilt(false);
        }

        internal static TMP_FontAsset EnsureFont(string weight)
        {
            string family = GameTypography.Family;
            string path = FontFolder + "/" + family + "-" + weight + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/UI/Fonts/" + family + "-" + weight + ".ttf");
            if (font == null) return null;
            Directory.CreateDirectory(FontFolder);
            // Динамический атлас: заранее запечён набор HUD, редкие знаки
            // (имена, новые подписи) дорисуются из TTF сами.
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null) return null;
            asset.name = family + "-" + weight + " SDF";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            asset.TryAddCharacters(Charset, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[ui-kit] В " + family + "-" + weight + " нет знаков: " + missing);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        internal static Sprite Chrome(string name) => KitSprite("Chrome", name);
        internal static Sprite Icon(string name) => KitSprite("Icons", name);
        internal static Sprite Ornament(string name) => KitSprite("Ornaments", name);
        /// <summary>Объёмные детали паузы (панель, плитка, вкладки): ими же собраны окна лагеря.</summary>
        internal static Sprite PauseKit(string name) => KitSprite("Pause", name);

        static Sprite KitSprite(string folder, string name)
        {
            string path = UiKitImport.KitRoot + "/" + folder + "/" + name + ".png";
            UiKitImport.Ensure(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[ui-kit] Нет спрайта " + path);
            return sprite;
        }
    }
}
