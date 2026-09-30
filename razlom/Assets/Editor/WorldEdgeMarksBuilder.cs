using System.IO;
using Game.Sim;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// «Тлеющие метки» у края экрана (владелец 30.09, выбор 2a на доске concepts-2026-09-30-hud-polish):
    /// Resources/UI/Prefabs/WorldEdgeMarksWc. Метка — клуб дыма, тёмный диск с тонким кольцом (вместилища —
    /// круги), знак врага тушью (Assets/UI/EdgeMarks, рисует tools/ui-kit/make-edge-mark-icons.py), шеврон
    /// наружу, «×N» слитой метки и подпись «Вендиго · 14 м» на дыму без кромки. Логика — WorldEdgeMarks.
    ///
    /// Префаб правится руками: готовый не пересобирается, а доводится миграциями до <see cref="LayoutVersion"/>
    /// (образец — SmokeTransitionBuilder.Migrations). Запуск — сам после компиляции, когда редактор не в Play
    /// и префаб не открыт в Prefab Mode; префаба ещё нет — собирается один раз. Съёмочная сборка зовёт
    /// Build(false) из RazlomCaptureBuild.
    /// </summary>
    [InitializeOnLoad]
    public static class WorldEdgeMarksBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/WorldEdgeMarksWc.prefab";
        public const string IconFolder = "Assets/UI/EdgeMarks";

        /// <summary>
        /// Версия раскладки WorldEdgeMarksWc.
        /// v1 (30.09) — первая сборка: метка, знаки врагов, выход и тайник.
        /// </summary>
        public const int LayoutVersion = 1;

        static UiTheme T => UiTheme.Current;
        static readonly Vector2 Center = new Vector2(.5f, .5f);
        static bool _waiting;

        static WorldEdgeMarksBuilder()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        [MenuItem("Разлом/UI/Собрать метки у края экрана «Дым и свет»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Метки у края экрана", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        /// <summary>Собрать префаб; готовый без <paramref name="force"/> только доводится миграциями.</summary>
        public static string Build(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                EnsureMigrated();
                return PrefabPath;
            }
            UiThemeBuilder.Ensure(false);
            GameObject root = Layout();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ui-kit] Метки у края экрана собраны: " + PrefabPath + " (v" + LayoutVersion + ").");
            return PrefabPath;
        }

        // ---------------------------------------------------------------- миграции

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
                Debug.Log("[ui-kit] Метки у края экрана открыты в Prefab Mode — доработка до v" + LayoutVersion + " после закрытия.");
                Wait();
                return;
            }
            // Новый префаб: без него меток в игре нет. Собирается один раз — дальше только миграции.
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
            EditorApplication.delayCall += MigrateWhenIdle;
        }

        static bool NeedsMigration()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var marks = prefab != null ? prefab.GetComponent<WorldEdgeMarks>() : null;
            return marks != null && marks.LayoutVersion < LayoutVersion;
        }

        /// <summary>Доводит готовый префаб до <see cref="LayoutVersion"/>; true — префаб в последней версии.</summary>
        public static bool EnsureMigrated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            var built = prefab.GetComponent<WorldEdgeMarks>();
            if (built == null) return false;
            if (built.LayoutVersion >= LayoutVersion) return true;
            UiThemeBuilder.Ensure(false);
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var marks = contents.GetComponent<WorldEdgeMarks>();
                int from = marks.LayoutVersion;
                Migrate(contents, marks);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ui-kit] Метки у края экрана доработаны поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static void Migrate(GameObject root, WorldEdgeMarks marks)
        {
            // v1 — первая версия: у префаба без номера (собран до миграций) дозаполняются только пустые знаки.
            if (marks.LayoutVersion < 1) FillIcons(marks, false);
            marks.LayoutVersion = LayoutVersion;
            EditorUtility.SetDirty(marks);
        }

        // ---------------------------------------------------------------- сборка

        static GameObject Layout()
        {
            var root = new GameObject("WorldEdgeMarksWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Над метками мира (8), под боевым HUD (10): панели HUD метку не прячут — она их обходит.
            canvas.sortingOrder = 9;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<UiScaleFollower>();
            // Мышь метки не ловят: холсту не нужен GraphicRaycaster.
            var view = root.AddComponent<WorldEdgeMarks>();
            view.LayoutVersion = LayoutVersion;
            FillIcons(view, true);
            view.Template = Mark((RectTransform)root.transform, view.KindIcons[(int)EnemyKind.ForestWendigo]);
            view.Template.gameObject.SetActive(false);
            return root;
        }

        static Texture2D Icon(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null && File.Exists(path))
            {
                AssetDatabase.ImportAsset(path);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (texture == null) Debug.LogWarning("[ui-kit] Нет знака метки: " + path);
            return texture;
        }

        static Texture2D EdgeIcon(string name) => Icon(IconFolder + "/" + name + ".png");
        static Texture2D RunIcon(string name) => Icon("Assets/UI/RunIcons/" + name + ".png");

        /// <summary>Знаки по номеру EnemyKind, выход и тайник; <paramref name="overwrite"/> false — только пустые места.</summary>
        static void FillIcons(WorldEdgeMarks view, bool overwrite)
        {
            if (view.KindIcons == null || view.KindIcons.Length < 10)
            {
                var grown = new Texture[10];
                if (view.KindIcons != null) System.Array.Copy(view.KindIcons, grown, view.KindIcons.Length);
                view.KindIcons = grown;
            }
            void Put(int index, Texture texture)
            {
                if (overwrite || view.KindIcons[index] == null) view.KindIcons[index] = texture;
            }
            // [0] — запасной знак врага: скрещённые сабли встречи.
            Put(0, RunIcon("encounter"));
            Put((int)EnemyKind.ForestGuardian, EdgeIcon("guardian"));
            Put((int)EnemyKind.ForestRootSwarm, EdgeIcon("rootswarm"));
            Put((int)EnemyKind.ForestBud, EdgeIcon("bud"));
            Put((int)EnemyKind.ForestWendigo, EdgeIcon("wendigo"));
            Put((int)EnemyKind.ForestStonehoof, EdgeIcon("stonehoof"));
            Put((int)EnemyKind.ForestThorncaster, EdgeIcon("thorncaster"));
            Put((int)EnemyKind.ForestRootSnarer, EdgeIcon("rootsnarer"));
            Put((int)EnemyKind.ForestSplitter, EdgeIcon("splitter"));
            Put((int)EnemyKind.ForestSplitling, EdgeIcon("splitter"));
            if (overwrite || view.ExitIcon == null) view.ExitIcon = RunIcon("exit");
            if (overwrite || view.CacheIcon == null) view.CacheIcon = RunIcon("cache");
        }

        /// <summary>
        /// Образец метки: клуб дыма; свет за диском (вид включает: красный пульс угрозы, спокойное золото
        /// выхода); «Пульс» — диск, кольцо и знак; шеврон на поворотном узле; «×N»; подпись внутрь экрана.
        /// Проявляется своей группой без огня: метки всплывают часто.
        /// </summary>
        static WorldEdgeMarksItem Mark(RectTransform root, Texture sample)
        {
            RectTransform mark = Node("Метка", root);
            mark.anchorMin = mark.anchorMax = Vector2.zero;
            mark.pivot = Center;
            mark.sizeDelta = new Vector2(46f, 46f);
            var group = mark.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            UiInkGroup ink = UiInkKit.Group(mark, UiInkGroup.Sweep.FromCenter, .3f, .08f);
            ink.Burn = 0f;
            ink.HideDuration = .18f;
            var item = mark.gameObject.AddComponent<WorldEdgeMarksItem>();
            item.Ink = ink;

            UiInkKit.SmokeLayer(mark, "Дым", "smoke_ring", .95f, 14f, 14f);
            item.Glow = UiInkKit.LightAt(mark, "Свечение", "light_glow", Center, Vector2.zero, new Vector2(104f, 104f), 0f, delay: .1f);

            RectTransform pulse = Stretch(Node("Пульс", mark));
            item.Pulse = pulse;
            Image disc = Layer(pulse, "Диск", T.CircleFill, Role.Panel, .88f);
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .03f);
            Image ring = Layer(pulse, "Кольцо", T.CircleFrame, Role.PanelLine, .55f);
            ring.material = UiInkKit.Plain;
            UiInkKit.Inked(ring, delay: .08f);
            item.Ring = ring.GetComponent<ThemeColor>();
            var icon = Stretch(Node("Знак", pulse), 10f).gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;
            icon.texture = sample;
            icon.material = UiInkKit.Plain;
            item.IconColor = Tint(icon, Role.Text, .92f);
            UiInkKit.Inked(icon, delay: .1f);
            item.Icon = icon;

            // Шеврон: узел нулевого размера в центре поворачивается к цели, стрелка лежит на нём справа.
            RectTransform chevron = At(Node("Шеврон", mark), Center, Vector2.zero, Vector2.zero);
            var arrow = At(Node("Стрелка", chevron), Center, new Vector2(33f, 0f), new Vector2(12f, 14f)).gameObject.AddComponent<RawImage>();
            arrow.raycastTarget = false;
            arrow.texture = EdgeIcon("chevron");
            arrow.material = UiInkKit.Plain;
            item.ChevronColor = Tint(arrow, Role.PanelLine, .8f);
            UiInkKit.Inked(arrow, delay: .14f);
            item.Chevron = chevron;

            // «×2» в правом верхнем углу слитой метки — как стаки у значков эффектов (1b).
            RectTransform count = At(Node("Счёт", mark), new Vector2(1f, 1f), new Vector2(2f, 0f), new Vector2(28f, 18f));
            UiInkKit.SmokeLayer(count, "Дым", "soft_blot", .9f, 6f, 4f, deep: true);
            TMP_Text countText = UiInkKit.Label(count, "Надпись", "×2", FontRole.Body, 13f, Role.Text, TextAlignmentOptions.Center, delay: .12f);
            countText.fontStyle = FontStyles.Bold;
            countText.textWrappingMode = TextWrappingModes.NoWrap;
            item.Count = countText;
            item.CountRow = count.gameObject;
            count.gameObject.SetActive(false);

            item.Label = BuildLabel(mark, out item.LabelGroup, out item.LabelText);
            item.Label.gameObject.SetActive(false);
            return item;
        }

        // ---------------------------------------------------------------- кадр без Play

        /// <summary>
        /// Кадр меток в редакторе без запуска игры (Play в общем редакторе не трогаем): образцы как на
        /// концепте 2a — «Вендиго · 14 м» слева, Шипомёт сверху, красные угрозы справа, тайник и выход
        /// золотом, слитая «×2». <paramref name="backdrop"/> — кадр игры под метками (необязательно).
        /// </summary>
        public static string Capture(string outPath, string backdrop = null)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst =>
            {
                var view = inst.GetComponent<WorldEdgeMarks>();
                var root = (RectTransform)inst.transform;
                if (!string.IsNullOrEmpty(backdrop) && File.Exists(backdrop))
                {
                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(File.ReadAllBytes(backdrop));
                    var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                    back.texture = texture;
                    back.transform.SetSiblingIndex(0);
                }
                void Show(Texture icon, WorldEdgeMarksItem.Look look, Vector2 at, float angle, int side, string label, int count = 1)
                {
                    WorldEdgeMarksItem item = Object.Instantiate(view.Template, view.Template.transform.parent);
                    item.gameObject.SetActive(true);
                    item.SetLook(look);
                    item.SetIcon(icon);
                    item.SetCount(count);
                    item.SetChevron(angle);
                    if (look == WorldEdgeMarksItem.Look.Threat) item.SetPulse(.6f, 1.04f);
                    item.SetLabel(label != null ? 1f : 0f, label, side, view.MarkSize * .5f);
                    ((RectTransform)item.transform).anchoredPosition = at;
                }
                const WorldEdgeMarksItem.Look enemy = WorldEdgeMarksItem.Look.Enemy, threat = WorldEdgeMarksItem.Look.Threat,
                    goal = WorldEdgeMarksItem.Look.Goal;
                Show(view.KindIcons[(int)EnemyKind.ForestWendigo], enemy, new Vector2(62f, 700f), 185f, WorldEdgeMarksLayout.SideLeft, "Вендиго · 14 м");
                Show(view.KindIcons[(int)EnemyKind.ForestThorncaster], enemy, new Vector2(560f, 1018f), 95f, WorldEdgeMarksLayout.SideTop, "Шипомёт · 19 м");
                Show(view.KindIcons[(int)EnemyKind.ForestThorncaster], threat, new Vector2(1858f, 800f), 25f, WorldEdgeMarksLayout.SideRight, null);
                Show(view.KindIcons[(int)EnemyKind.ForestStonehoof], threat, new Vector2(1858f, 330f), -30f, WorldEdgeMarksLayout.SideRight, "Камнекопыт · 17 м");
                Show(view.CacheIcon, goal, new Vector2(1858f, 560f), 0f, WorldEdgeMarksLayout.SideRight, "Тайник · 22 м");
                Show(view.ExitIcon, goal, new Vector2(1000f, 1018f), 90f, WorldEdgeMarksLayout.SideTop, null);
                Show(view.KindIcons[(int)EnemyKind.ForestBud], enemy, new Vector2(62f, 420f), 200f, WorldEdgeMarksLayout.SideLeft, null, 2);
            });
        }

        /// <summary>
        /// Подпись «Вендиго · 14 м»: тёмное мягкое пятно и полоса дыма под текстом, без нити по кромке
        /// (подсказки — без кромки, владелец 25.09). Опору и место ставит метка по своей стороне края.
        /// </summary>
        static RectTransform BuildLabel(RectTransform mark, out CanvasGroup group, out TMP_Text text)
        {
            RectTransform label = Node("Подпись", mark);
            label.anchorMin = label.anchorMax = Center;
            label.pivot = new Vector2(0f, .5f);
            label.anchoredPosition = new Vector2(33f, 0f);
            var row = label.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 14, 4, 5);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            group = label.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Image shade = UiInkKit.SmokeLayer(label, "Тень под текстом", "soft_blot", .85f, 22f, 12f, deep: true);
            Image smoke = UiInkKit.SmokeLayer(label, "Дым", "smoke_band_2", .9f, 26f, 10f);
            shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            smoke.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            RectTransform box = Node("Надпись", label);
            text = box.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = "Вендиго · 14 м";
            text.fontSize = T.Size(UiTheme.TextStep.Body);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            var font = box.gameObject.AddComponent<ThemeFont>();
            font.Role = FontRole.Body;
            font.Apply();
            Tint(text, Role.Text);
            UiInkKit.Revealed(text, .1f);
            return label;
        }
    }
}
