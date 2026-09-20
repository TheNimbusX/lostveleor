using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.CombatHudBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Собирает палатку снаряжения один в один по концепту владельца
    /// ART/UI/concepts-2026-09-15/v3-tent.png (координаты сняты с кадра 2000×1131
    /// и пересчитаны в раскладку 1920×1086, множитель 0.96):
    /// лента «Снаряжение» сверху; слева Пелаг на ковре, круглые слоты с подписями
    /// и полоса статов; справа сумка с вкладками-пилюлями; внизу справа подсказки;
    /// карточка предмета всплывает у ячейки.
    ///
    /// Детали — плоские синие панели с тонким кантом из Chrome, без тяжёлой фаски
    /// паузы: владелец 16 сентября: «очень много деталей… не совпадает с рефом».
    ///
    /// Префаб создаётся, только если его нет; дальше он правится руками, а
    /// изменения раскладки идут миграциями по <see cref="LayoutVersion"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class CampTentBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTent.prefab";
        public const string HeroArtPath = "Assets/Resources/UI/Tent/PelagArt.png";
        public const string RugPath = "Assets/Resources/UI/Tent/Rug.png";

        /// <summary>
        /// v1 (16 сентября, утро) — три панели по нашему концепту; владелец отверг,
        /// руками не правил, поэтому пересобирается. v2 — раскладка v3-tent.
        /// v3 — отзыв владельца на v2: контурная отметка круглого слота, свечение,
        /// камень и блик редкости, ровная полоса статов, «Закрыть» кнопкой, сцена героя.
        /// </summary>
        public const int LayoutVersion = 3;
        const int RebuildBelow = 2;

        static readonly Color Cream = Hex(0xF3E8D9);
        static readonly Color NavyInk = Hex(0x1C3A5E);
        static readonly Color NavySoft = Hex(0x5A7390);
        static readonly Color Muted = Hex(0x9DB6CB);
        static readonly Color Line = new Color(.62f, .78f, .92f, .35f);
        static readonly Color Coral = Hex(0xC8432F);

        static readonly Vector2 Center = new Vector2(.5f, .5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        static readonly Vector2 LeftMiddle = new Vector2(0f, .5f);

        static TMP_FontAsset _regular, _semibold, _bold;

        static CampTentBuilder()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EnsureBuilt(false);
            };
        }

        [MenuItem("Разлом/UI/Собрать палатку снаряжения")]
        static void RebuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Палатка снаряжения",
                    "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".", "Пересобрать", "Отмена"))
                return;
            EnsureBuilt(true);
        }

        public static bool EnsureBuilt(bool force)
        {
            PrepareItemIcons();
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            bool migrate = false;
            if (!force && existing != null)
            {
                var built = existing.GetComponentInChildren<CampTentView>(true);
                if (built != null && built.LayoutVersion >= LayoutVersion) return true;
                migrate = built != null && built.LayoutVersion >= RebuildBelow;
                if (!migrate)
                    Debug.Log("[ui-kit] Палатка v" + (built != null ? built.LayoutVersion : 0) + " пересобирается по v3-tent (v" + LayoutVersion + ").");
            }
            if (!EnsureEssentials()) return false;
            _regular = EnsureFont("Regular");
            _semibold = EnsureFont("SemiBold");
            _bold = EnsureFont("Bold");
            if (_regular == null || _semibold == null || _bold == null || Chrome("panel_navy") == null || Chrome("ring_focus") == null)
            {
                Debug.LogError("[ui-kit] Нет шрифтов или деталей Chrome — палатка не собрана.");
                return false;
            }

            if (migrate)
            {
                // Поверх ручных правок: префаб открывается и дополняется, не пересобирается.
                GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var view = contents.GetComponentInChildren<CampTentView>(true);
                    int from = view.LayoutVersion;
                    Migrate(view);
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    Debug.Log("[ui-kit] Палатка доработана поверх ручных правок: v" + from + " → v" + LayoutVersion + ".");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                AssetDatabase.SaveAssets();
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject root = Build();
            try
            {
                Migrate(root.GetComponent<CampTentView>());
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ui-kit] Палатка снаряжения собрана: " + PrefabPath);
            return true;
        }

        static Sprite TentSprite(string path)
        {
            if (!File.Exists(path)) return null;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static GameObject Build()
        {
            var root = new GameObject("CampTent", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Под паузой (300): Escape из палатки не должен прятать меню.
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1086f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var view = root.AddComponent<CampTentView>();
            // Собирается раскладка v2, дальше её дополняют те же миграции, что и ручной префаб.
            view.LayoutVersion = RebuildBelow;
            view.EmptyFrame = Chrome("slot");
            view.SelectedFrame = Chrome("slot_focus");
            view.RarityFrames = new[] { Chrome("slot_common"), Chrome("slot_magic"), Chrome("slot_rare"), Chrome("slot_unique") };

            BuildBackdrop(root.transform, view);
            RectTransform board = Node("Board", root.transform, Center, Center, Center, Vector2.zero, new Vector2(1920f, 1086f));
            BuildTitle(board, view);
            BuildHero(board, view);
            BuildBag(board, view);
            BuildHints(board, view);
            BuildTooltip(board, view);
            return root;
        }

        // ---- фон, лента, подсказки ----
        static void BuildBackdrop(Transform root, CampTentView view)
        {
            RectTransform backdrop = Stretch("Backdrop", root, 0f);
            Img(backdrop, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            view.Backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();
            RectTransform blur = Stretch("Blur", backdrop, 0f);
            var raw = blur.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            view.BackdropBlur = blur.gameObject.AddComponent<UiBackdropBlur>();
            view.BackdropBlur.Target = raw;
            // Лагерь за палаткой виден, как на концепте: лёгкое затемнение, не чёрный экран.
            Img(Stretch("Dim", backdrop, 0f), null, new Color(.01f, .03f, .06f, .28f));
        }

        static void BuildTitle(RectTransform board, CampTentView view)
        {
            RectTransform title = Node("Title", board, Center, Center, Center, new Vector2(0f, 454f), new Vector2(730f, 82f));
            title.gameObject.AddComponent<CanvasGroup>();
            view.TitleRibbon = title;
            Img(Stretch("Ribbon", title, 0f), Chrome("button_primary"), Color.white);
            Label(Stretch("Text", title, 0f), "Снаряжение", _bold, 42f, Color.white, TextAlignmentOptions.Center, 42f, true)
                .margin = new Vector4(40f, 0f, 40f, 4f);
            // Тонкие линии по бокам ленты, как на концепте.
            Img(Node("Line Left", title, LeftMiddle, LeftMiddle, new Vector2(1f, .5f), new Vector2(-10f, 0f), new Vector2(90f, 2f)), null, Line);
            Img(Node("Line Right", title, new Vector2(1f, .5f), new Vector2(1f, .5f), LeftMiddle, new Vector2(10f, 0f), new Vector2(90f, 2f)), null, Line);

            RectTransform close = Node("Close", board, TopRight, TopRight, TopRight, new Vector2(-36f, -26f), new Vector2(60f, 60f));
            Img(close, Chrome("icon_ring"), Color.white, false).raycastTarget = true;
            view.Close = Clickable(close, Chrome("icon_ring"), new Color(.6f, .9f, 1f, .9f));
            IconImage(Stretch("Icon", close, 17f), "menu_close", Cream);
        }

        static void BuildHints(RectTransform board, CampTentView view)
        {
            RectTransform bar = Node("Hints", board, Center, Center, Center, new Vector2(516f, -456f), new Vector2(811f, 60f));
            bar.gameObject.AddComponent<CanvasGroup>();
            view.HintBar = bar;
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            HintPill(bar, "Двойной клик", "Надеть", 270f, false);
            HintPill(bar, "ПКМ", "Беречь", 200f, false);
            HintPill(bar, "I", "Закрыть", 190f, true);

            view.Feedback = Label(Node("Feedback", board, Center, Center, Center, new Vector2(-100f, -456f), new Vector2(380f, 44f)),
                "", _semibold, 17f, new Color32(0xFF, 0x9A, 0x86, 0xFF), TextAlignmentOptions.MidlineRight);
            view.Feedback.textWrappingMode = TextWrappingModes.Normal;
            view.Hint = view.Feedback;
        }

        static void HintPill(RectTransform bar, string key, string action, float width, bool keycap)
        {
            RectTransform pill = Node(action, bar, Center, Center, Center, Vector2.zero, new Vector2(width, 56f));
            Layout(pill, width, 56f);
            Img(pill, Chrome("button_secondary"), Color.white);
            if (keycap)
            {
                RectTransform cap = Node("Key", pill, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(26f, 0f), new Vector2(38f, 38f));
                Img(cap, Chrome("keycap"), Color.white);
                Label(Stretch("Text", cap, 0f), key, _bold, 20f, Color.white, TextAlignmentOptions.Center);
                Label(Node("Action", pill, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(78f, 0f), new Vector2(width - 96f, 40f)),
                    action, _semibold, 20f, Cream, TextAlignmentOptions.MidlineLeft);
                return;
            }
            Label(Stretch("Text", pill, 0f), key + "   <color=#F3E8D9>" + action + "</color>", _semibold, 20f, Muted, TextAlignmentOptions.Center)
                .margin = new Vector4(24f, 0f, 24f, 0f);
        }

        // ---- герой ----
        static void BuildHero(RectTransform board, CampTentView view)
        {
            RectTransform panel = Node("Hero Panel", board, Center, Center, Center, new Vector2(-480f, -38f), new Vector2(806f, 826f));
            Img(panel, Chrome("panel_navy"), Color.white).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.HeroPanel = panel;

            Sprite rug = TentSprite(RugPath);
            RectTransform rugRect = Node("Rug", panel, TopLeft, TopLeft, Center, new Vector2(413f, -624f), new Vector2(430f, 150f));
            var rugImage = Img(rugRect, rug, rug != null ? Color.white : new Color(.8f, .3f, .3f, .55f), false);
            rugImage.preserveAspect = rug != null;

            RectTransform hero = Node("Hero", panel, TopLeft, TopLeft, new Vector2(.5f, 0f), new Vector2(413f, -660f), new Vector2(360f, 640f));
            view.Portrait = Stretch("Portrait", hero, 0f).gameObject.AddComponent<RawImage>();
            view.Portrait.raycastTarget = false;
            view.HeroArt = Img(Stretch("Art", hero, 0f), TentSprite(HeroArtPath), Color.white, false);
            view.HeroArt.preserveAspect = true;
            view.HeroArt.enabled = view.HeroArt.sprite != null;
            view.HeroName = null;

            // Круглые слоты: оружие и броня слева, кольцо и талисман справа.
            string[] names = { "Weapon", "Armor", "Ring", "Talisman" };
            string[] labels = { "Оружие", "Броня", "Кольцо", "Талисман" };
            Vector2[] centres = { new Vector2(124f, -192f), new Vector2(124f, -422f), new Vector2(688f, -192f), new Vector2(688f, -422f) };
            for (int i = 0; i < 4; i++)
                view.Worn[i] = RoundSlot(panel, names[i], labels[i], centres[i]);

            RectTransform stats = Node("Stats", panel, TopLeft, TopLeft, Center, new Vector2(403f, -745f), new Vector2(758f, 112f));
            Img(stats, Chrome("panel_navy"), new Color(.85f, .92f, 1f, 1f));
            string[] statLabels = { "Урон", "Броня", "Здоровье", "Лавидий" };
            string[] icons = { "stat_damage", "stat_armor", "heart", "lavidium" };
            for (int i = 0; i < 4; i++)
            {
                float left = 12f + i * 184f;
                RectTransform ring = Node(statLabels[i] + " Icon", stats, LeftMiddle, LeftMiddle, Center, new Vector2(left + 38f, 0f), new Vector2(58f, 58f));
                Img(ring, Chrome("icon_ring"), Color.white, false);
                IconImage(Stretch("Glyph", ring, 14f), icons[i], i == 2 || i == 3 ? Color.white : Cream);
                Label(Node(statLabels[i] + " Label", stats, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(left + 76f, 16f), new Vector2(104f, 24f)),
                    statLabels[i], _regular, 17f, Muted, TextAlignmentOptions.MidlineLeft);
                view.StatValues[i] = Label(Node(statLabels[i] + " Value", stats, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(left + 76f, -12f), new Vector2(104f, 34f)),
                    "0", _bold, 28f, Cream, TextAlignmentOptions.MidlineLeft);
                if (i > 0)
                    Img(Node("Divider " + i, stats, LeftMiddle, LeftMiddle, Center, new Vector2(left - 4f, 0f), new Vector2(2f, 60f)), null, Line);
            }
        }

        static CampTentCell RoundSlot(RectTransform panel, string name, string label, Vector2 centre)
        {
            RectTransform rect = Node("Slot " + name, panel, TopLeft, TopLeft, Center, centre, new Vector2(154f, 154f));
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            cell.Round = true;
            cell.Frame = Img(rect, Chrome("icon_ring"), Color.white, false);
            cell.Frame.raycastTarget = true;
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();
            Hover(rect, Chrome("icon_ring"), new Color(.6f, .92f, 1f, .9f), -8f, 1.04f);
            cell.Placeholder = Img(Stretch("Placeholder", rect, 42f), null, new Color(1f, 1f, 1f, .2f), false);
            cell.Placeholder.preserveAspect = true;
            cell.Placeholder.enabled = false;
            cell.Icon = Img(Stretch("Icon", rect, 26f), null, Color.white, false);
            cell.Icon.preserveAspect = true;
            cell.Icon.enabled = false;
            cell.Level = Label(Node("Level", rect, BottomRight, BottomRight, BottomRight, new Vector2(-30f, 22f), new Vector2(60f, 24f)),
                "", _bold, 16f, Cream, TextAlignmentOptions.BottomRight);
            RectTransform selected = Stretch("Selected", rect, -10f);
            Img(selected, Chrome("icon_ring"), new Color(.55f, .93f, 1f, 1f), false);
            selected.gameObject.SetActive(false);
            cell.Selection = selected.gameObject;

            RectTransform caption = Node("Caption", rect, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(0f, 12f), new Vector2(178f, 40f));
            Img(caption, Chrome("button_secondary"), Color.white);
            Label(Stretch("Text", caption, 0f), label, _semibold, 21f, Cream, TextAlignmentOptions.Center);
            return cell;
        }

        // ---- сумка ----
        static void BuildBag(RectTransform board, CampTentView view)
        {
            RectTransform panel = Node("Bag Panel", board, Center, Center, Center, new Vector2(437f, -19f), new Vector2(970f, 778f));
            Img(panel, Chrome("panel_navy"), Color.white).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.BagPanel = panel;

            RectTransform indicator = Node("Tab Indicator", panel, TopLeft, TopLeft, Center, new Vector2(34f + 75f, -60f), new Vector2(150f, 50f));
            Img(indicator, Chrome("button_primary"), Color.white);
            view.FilterIndicator = indicator;
            string[] names = { "All", "Weapon", "Armor", "Ring", "Talisman" };
            string[] labels = { "Все", "Оружие", "Броня", "Кольца", "Талисманы" };
            for (int i = 0; i < 5; i++)
            {
                RectTransform tab = Node("Tab " + names[i], panel, TopLeft, TopLeft, Center, new Vector2(34f + 75f + i * 158f, -60f), new Vector2(150f, 50f));
                Img(tab, Chrome("pill_navy"), Color.white);
                view.Filters[i] = Clickable(tab, Chrome("pill_navy"), new Color(.6f, .9f, 1f, .6f));
                Label(Stretch("Text", tab, 0f), labels[i], _semibold, 21f, Cream, TextAlignmentOptions.Center);
            }
            view.BagCount = Label(Node("Count", panel, TopRight, TopRight, new Vector2(1f, .5f), new Vector2(-36f, -60f), new Vector2(120f, 40f)),
                "0 / 48", _semibold, 19f, Muted, TextAlignmentOptions.MidlineRight);
            Img(Node("Divider", panel, TopLeft, TopLeft, LeftMiddle, new Vector2(34f, -104f), new Vector2(902f, 2f)), null, Line);

            RectTransform grid = Node("Grid", panel, TopLeft, TopLeft, TopLeft, new Vector2(38f, -122f), new Vector2(893f, 631f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(102f, 96f);
            layout.spacing = new Vector2(11f, 11f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 8;
            layout.childAlignment = TextAnchor.UpperLeft;
            view.BagGrid = grid;
            view.BagTemplate = BagCell(grid);
            view.BagTemplate.gameObject.SetActive(false);
        }

        static CampTentCell BagCell(Transform grid)
        {
            RectTransform rect = Node("Bag Cell Template", grid, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(102f, 96f));
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            cell.Frame = Img(rect, Chrome("slot"), Color.white);
            cell.Frame.raycastTarget = true;
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();
            Hover(rect, Chrome("slot_hover"), Color.white, -2f, 1.04f);
            cell.Icon = Img(Stretch("Icon", rect, 12f), null, Color.white, false);
            cell.Icon.preserveAspect = true;
            cell.Icon.enabled = false;
            cell.Level = Label(Node("Level", rect, BottomRight, BottomRight, BottomRight, new Vector2(-9f, 5f), new Vector2(50f, 22f)),
                "", _bold, 15f, Cream, TextAlignmentOptions.BottomRight);
            RectTransform lockIcon = Node("Lock", rect, TopRight, TopRight, TopRight, new Vector2(-6f, -6f), new Vector2(24f, 24f));
            IconImage(lockIcon, "lock", Cream);
            lockIcon.gameObject.SetActive(false);
            cell.Lock = lockIcon.gameObject;
            // Контурная отметка выбора поверх предмета: заливки нет, значок и кант редкости видны.
            RectTransform selected = Stretch("Selected", rect, -5f);
            Img(selected, Chrome("slot_focus"), Color.white);
            selected.gameObject.SetActive(false);
            cell.Selection = selected.gameObject;
            return cell;
        }

        // ---- карточка предмета ----
        static void BuildTooltip(RectTransform board, CampTentView view)
        {
            RectTransform card = Node("Item Tooltip", board, Center, Center, TopLeft, new Vector2(-920f, 480f), new Vector2(380f, 262f));
            Img(card, Chrome("card_cream"), Color.white);
            view.Tooltip = card;
            view.TooltipGroup = card.gameObject.AddComponent<CanvasGroup>();
            view.TooltipGroup.blocksRaycasts = false;
            view.TooltipGroup.interactable = false;
            view.TooltipGroup.alpha = 0f;

            RectTransform frame = Node("Frame", card, TopLeft, TopLeft, TopLeft, new Vector2(16f, -16f), new Vector2(104f, 104f));
            view.ItemFrame = Img(frame, Chrome("slot"), Color.white);
            view.ItemArt = Img(Stretch("Art", frame, 12f), null, Color.white, false);
            view.ItemArt.preserveAspect = true;
            view.ItemArt.enabled = false;

            view.ItemTitle = Label(Node("Title", card, TopLeft, TopLeft, TopLeft, new Vector2(134f, -18f), new Vector2(230f, 32f)),
                "", _bold, 24f, NavyInk, TextAlignmentOptions.MidlineLeft);
            view.ItemTitle.enableAutoSizing = true;
            view.ItemTitle.fontSizeMin = 16f;
            view.ItemTitle.fontSizeMax = 24f;
            view.ItemRarity = Label(Node("Rarity", card, TopLeft, TopLeft, TopLeft, new Vector2(134f, -52f), new Vector2(230f, 24f)),
                "", _semibold, 18f, NavySoft, TextAlignmentOptions.MidlineLeft);
            view.ItemKind = Label(Node("Kind", card, TopLeft, TopLeft, TopLeft, new Vector2(134f, -80f), new Vector2(230f, 24f)),
                "", _regular, 16f, NavySoft, TextAlignmentOptions.MidlineLeft);
            Img(Node("Divider", card, TopLeft, TopLeft, LeftMiddle, new Vector2(18f, -134f), new Vector2(344f, 2f)), null, new Color(.11f, .23f, .37f, .2f));
            view.ItemStats = Label(Node("Stats", card, TopLeft, TopLeft, TopLeft, new Vector2(18f, -144f), new Vector2(344f, 104f)),
                "", _regular, 17f, NavyInk, TextAlignmentOptions.TopLeft);
            view.ItemStats.textWrappingMode = TextWrappingModes.Normal;
        }

        // ---- миграции ----
        static void Migrate(CampTentView view)
        {
            if (view.LayoutVersion < 3) MigrateTo3(view);
            view.LayoutVersion = LayoutVersion;
        }

        /// <summary>
        /// Отзыв владельца на v2 (16 сентября). Узлы ищутся по ссылкам вида, а не по
        /// путям, и координаты — локальные: ручные перестановки панелей сохраняются.
        /// </summary>
        static void MigrateTo3(CampTentView view)
        {
            Color warmLight = new Color32(0xFF, 0xD9, 0xC7, 0xB0);
            Color rugGlow = new Color32(0xE2, 0x56, 0x3F, 0x66);

            // Круглые слоты: отметка выбора — контур, а не кольцо с синим диском поверх вещи.
            foreach (CampTentCell cell in view.Worn)
            {
                if (cell == null) continue;
                var rect = (RectTransform)cell.transform;
                if (cell.Selection != null && cell.Selection.GetComponent<Image>() is Image selected)
                {
                    selected.sprite = Chrome("ring_focus");
                    selected.color = Color.white;
                }
                Transform hover = rect.Find("Hover");
                if (hover != null && hover.GetComponent<Image>() is Image glow)
                {
                    glow.sprite = Chrome("ring_hover");
                    glow.color = new Color(.75f, .95f, 1f, 1f);
                }
                AddRarity(cell, rect, 10f, new Vector2(-16f, -16f), 30f);
            }

            // Ячейка сумки: свечение, камень и полоса блика под маской.
            CampTentCell template = view.BagTemplate;
            if (template != null)
            {
                var rect = (RectTransform)template.transform;
                AddRarity(template, rect, 6f, new Vector2(-8f, -8f), 20f);
                if (template.Gem != null)
                {
                    // Правый верх занят замком — камень уходит влево.
                    RectTransform gem = template.Gem.rectTransform;
                    gem.anchorMin = gem.anchorMax = gem.pivot = TopLeft;
                    gem.anchoredPosition = new Vector2(8f, -8f);
                }
                if (template.Shine == null)
                {
                    RectTransform lane = Stretch("Shine Lane", rect, 4f);
                    lane.gameObject.AddComponent<RectMask2D>();
                    RectTransform shine = Node("Shine", lane, Center, Center, Center, Vector2.zero, new Vector2(34f, 150f));
                    shine.localRotation = Quaternion.Euler(0f, 0f, -20f);
                    Img(shine, Chrome("pause_shine"), new Color(1f, 1f, 1f, .75f), false);
                    shine.gameObject.SetActive(false);
                    Transform level = rect.Find("Level");
                    if (level != null) lane.SetSiblingIndex(level.GetSiblingIndex());
                    template.Shine = shine;
                }
            }

            RebuildStats(view);

            // «I · Закрыть» — настоящая кнопка.
            if (view.CloseHint == null && view.HintBar != null)
            {
                Transform pill = view.HintBar.Find("Закрыть");
                if (pill is RectTransform pillRect && pillRect.GetComponent<Image>() != null)
                    view.CloseHint = Clickable(pillRect, Chrome("button_secondary"), new Color(.7f, .92f, 1f, .7f));
            }

            // Сцена героя: тёплое пятно за Пелагом, свечение ковра, пылинки.
            if (view.HeroArt != null && view.HeroStage == null)
            {
                var hero = (RectTransform)view.HeroArt.transform.parent;
                var panel = (RectTransform)hero.parent;
                view.HeroStage = hero;
                Transform rug = panel.Find("Rug");
                int behind = rug != null ? rug.GetSiblingIndex() : hero.GetSiblingIndex();
                Vector2 feet = hero.anchoredPosition;

                RectTransform light = Node("Hero Light", panel, hero.anchorMin, hero.anchorMax, Center, feet + new Vector2(0f, 280f), new Vector2(640f, 640f));
                view.HeroLight = Img(light, Chrome("rarity_glow"), warmLight, false);
                light.SetSiblingIndex(behind);
                RectTransform glow = Node("Rug Glow", panel, hero.anchorMin, hero.anchorMax, Center, feet + new Vector2(0f, 34f), new Vector2(560f, 200f));
                view.RugGlow = Img(glow, Chrome("rarity_glow"), rugGlow, false);
                glow.SetSiblingIndex(behind + 1);

                RectTransform motes = Node("Motes", panel, hero.anchorMin, hero.anchorMax, new Vector2(.5f, 0f), feet + new Vector2(0f, 10f), new Vector2(540f, 600f));
                motes.SetSiblingIndex(hero.GetSiblingIndex() + 1);
                view.MotesArea = motes;
                RectTransform mote = Node("Mote", motes, Center, Center, Center, Vector2.zero, new Vector2(8f, 8f));
                view.MoteTemplate = Img(mote, Chrome("rarity_glow"), new Color32(0xF3, 0xE8, 0xD9, 0xFF), false);
                mote.gameObject.SetActive(false);
            }

            // Карточка: полоса цвета редкости сверху.
            if (view.Tooltip != null && view.TooltipBand == null)
            {
                RectTransform band = Node("Rarity Band", view.Tooltip, TopLeft, TopRight, new Vector2(.5f, 1f), new Vector2(0f, -7f), new Vector2(-40f, 5f));
                view.TooltipBand = Img(band, null, Color.white);
            }
        }

        static void AddRarity(CampTentCell cell, RectTransform rect, float glowInset, Vector2 gemPosition, float gemSize)
        {
            if (cell.RarityGlow == null)
            {
                RectTransform glow = Stretch("Rarity Glow", rect, glowInset);
                cell.RarityGlow = Img(glow, Chrome("rarity_glow"), Color.white, false);
                cell.RarityGlow.enabled = false;
                int below = cell.Icon != null ? cell.Icon.transform.GetSiblingIndex() : 1;
                glow.SetSiblingIndex(below);
            }
            if (cell.Gem == null)
            {
                RectTransform gem = Node("Rarity Gem", rect, TopRight, TopRight, TopRight, gemPosition, new Vector2(gemSize, gemSize));
                cell.Gem = Img(gem, Chrome("rarity_gem"), Color.white, false);
                cell.Gem.preserveAspect = true;
                cell.Gem.enabled = false;
                if (cell.Level != null) gem.SetSiblingIndex(cell.Level.transform.GetSiblingIndex());
            }
        }

        /// <summary>
        /// Полоса статов на раскладке: четыре равные ячейки, в каждой значок в кольце
        /// и столбик «подпись над числом». В v2 всё стояло ручными сдвигами и плыло.
        /// </summary>
        static void RebuildStats(CampTentView view)
        {
            if (view.StatValues.Length == 0 || view.StatValues[0] == null) return;
            var stats = (RectTransform)view.StatValues[0].transform.parent;
            for (int i = stats.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(stats.GetChild(i).gameObject);

            var row = stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            string[] statLabels = { "Урон", "Броня", "Здоровье", "Лавидий" };
            string[] icons = { "stat_damage", "stat_armor", "heart", "lavidium" };
            for (int i = 0; i < 4; i++)
            {
                RectTransform cell = Node(statLabels[i], stats, Center, Center, Center, Vector2.zero, Vector2.zero);
                var line = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
                line.padding = new RectOffset(22, 10, 0, 0);
                line.spacing = 12f;
                line.childAlignment = TextAnchor.MiddleLeft;
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = line.childForceExpandHeight = false;

                RectTransform ring = Node("Icon", cell, Center, Center, Center, Vector2.zero, new Vector2(58f, 58f));
                Layout(ring, 58f, 58f);
                Img(ring, Chrome("icon_ring"), Color.white, false);
                IconImage(Stretch("Glyph", ring, 14f), icons[i], i >= 2 ? Color.white : Cream);

                RectTransform column = Node("Text", cell, Center, Center, Center, Vector2.zero, Vector2.zero);
                Layout(column, 96f, -1f).flexibleWidth = 1f;
                var stack = column.gameObject.AddComponent<VerticalLayoutGroup>();
                stack.childAlignment = TextAnchor.MiddleLeft;
                stack.childControlWidth = stack.childControlHeight = true;
                stack.childForceExpandWidth = true;
                stack.childForceExpandHeight = false;
                stack.spacing = -2f;
                RectTransform caption = Node("Label", column, Center, Center, Center, Vector2.zero, Vector2.zero);
                Layout(caption, -1f, 22f);
                Label(caption, statLabels[i], _regular, 17f, Muted, TextAlignmentOptions.MidlineLeft);
                RectTransform value = Node("Value", column, Center, Center, Center, Vector2.zero, Vector2.zero);
                Layout(value, -1f, 36f);
                view.StatValues[i] = Label(value, "0", _bold, 30f, Cream, TextAlignmentOptions.MidlineLeft);

                if (i > 0)
                {
                    RectTransform divider = Node("Divider", cell, new Vector2(0f, .5f), new Vector2(0f, .5f), Center, Vector2.zero, new Vector2(2f, 60f));
                    divider.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    Img(divider, null, Line);
                }
            }
        }

        /// <summary>Значки вещей читаются кодом как Texture2D; прозрачный край без ореола.</summary>
        static void PrepareItemIcons()
        {
            const string folder = "Assets/Resources/UI/Items";
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                if (importer == null || (importer.alphaIsTransparency && importer.wrapMode == TextureWrapMode.Clamp && importer.mipmapEnabled)) continue;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        // ---- детали ----
        static Button Clickable(RectTransform rect, Sprite glow, Color glowColour)
        {
            Image image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            Hover(rect, glow, glowColour, -3f, 1.03f);
            return button;
        }

        /// <summary>Живая подсветка наведения, как в паузе: проявляется и дышит.</summary>
        static void Hover(RectTransform rect, Sprite sprite, Color colour, float inset, float scale)
        {
            RectTransform frame = Stretch("Hover", rect, inset);
            var glow = Img(frame, sprite, colour, sprite != null && sprite.border != Vector4.zero);
            frame.SetSiblingIndex(0);
            var motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = glow;
            motion.HoverScale = scale;
            motion.PressScale = .97f;
        }

        static void IconImage(RectTransform rect, string icon, Color tint)
        {
            Image image = Img(rect, Icon(icon), tint, false);
            image.preserveAspect = true;
        }

        static TextMeshProUGUI Label(RectTransform rect, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment, float spacing = 0f, bool upper = false)
        {
            TextMeshProUGUI label = Text(rect, text, font, size, color, alignment, spacing);
            if (upper) label.fontStyle = FontStyles.UpperCase;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }
    }
}
