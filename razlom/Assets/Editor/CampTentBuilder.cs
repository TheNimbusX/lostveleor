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
    /// Собирает палатку снаряжения по концепту владельца
    /// `ART/UI/concepts-2026-09-22/tent-O-portrait-card.png` (без ленты сверху) в языке
    /// боевого HUD и паузы: объёмные синие панели паузы, плитки со скосом и ромбами,
    /// коралловые вкладки, рамка портрета как в HUD. Фон — живой лагерь.
    ///
    /// Слева карточка героя: портрет, имя, уровень и опыт, 4 слота, лист характеристик
    /// и зелья. Справа вкладки «Сумка / Атлас».
    ///
    /// Префаб создаётся, только если его нет; дальше он правится руками, а
    /// изменения раскладки идут миграциями по <see cref="LayoutVersion"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class CampTentBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTent.prefab";
        const string PortraitPath = "Assets/Resources/UI/HUD/PelagPortraitCutout.png";

        /// <summary>
        /// v1–v3 (16 сентября) — синие плоские панели по v3-tent; владелец отверг их
        /// целиком 21 сентября, руками префаб не правили. v4 (22 сентября) — концепт O
        /// в языке HUD и паузы, пересборка; v5 — тоньше края плиток, шире подписи и подсказка.
        /// </summary>
        public const int LayoutVersion = 5;
        const int RebuildBelow = 5;

        static readonly Color Cream = Hex(0xF3E8D9);
        static readonly Color Muted = Hex(0x9DB6CB);
        static readonly Color Coral = Hex(0xE2563F);
        static readonly Color XpColour = Hex(0x4FC3F0);

        static readonly Vector2 Center = new Vector2(.5f, .5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 TopCenter = new Vector2(.5f, 1f);
        static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        static readonly Vector2 LeftMiddle = new Vector2(0f, .5f);
        static readonly Vector2 RightMiddle = new Vector2(1f, .5f);

        /// <summary>Порядок строк листа героя; совпадает с CampInventoryView.StatRows.</summary>
        static readonly string[] StatIcons =
        {
            "heart", "stat_damage", "stat_armor", "stat_attack_speed", "stat_crit", "stat_crit",
            "lavidium", "lavidium", "stat_range", "stat_duration", "stat_cooldown", "lavidium",
        };
        static readonly string[] StatNames =
        {
            "Здоровье", "Урон", "Броня", "Скор. атаки", "Шанс крита", "Сила крита",
            "Лавидий", "Восст. лавидия", "Скорость", "Скор. навыков", "Перезарядка", "Сопр. огню",
        };

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
            if (!force && existing != null)
            {
                var built = existing.GetComponentInChildren<CampTentView>(true);
                if (built != null && built.LayoutVersion >= RebuildBelow) return true;
                Debug.Log("[ui-kit] Палатка v" + (built != null ? built.LayoutVersion : 0) + " пересобирается по концепту O (v" + LayoutVersion + ").");
            }
            if (!EnsureEssentials()) return false;
            _regular = EnsureFont("Regular");
            _semibold = EnsureFont("SemiBold");
            _bold = EnsureFont("Bold");
            if (_regular == null || _semibold == null || _bold == null || PauseKit("pause_panel") == null || Chrome("slot") == null)
            {
                Debug.LogError("[ui-kit] Нет шрифтов или деталей пака — палатка не собрана.");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject root = Build();
            try { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ui-kit] Палатка снаряжения собрана: " + PrefabPath);
            return true;
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
            view.LayoutVersion = LayoutVersion;
            view.EmptyFrame = Chrome("slot");
            view.RarityFrames = new[] { Chrome("slot_common"), Chrome("slot_magic"), Chrome("slot_rare"), Chrome("slot_unique") };
            view.TabOn = PauseKit("pause_tab_on");
            view.TabOff = PauseKit("pause_tab_off");

            // Прозрачный слой: лагерь виден целиком, клики мимо окон не уходят в мир.
            RectTransform backdrop = Stretch("Backdrop", root.transform, 0f);
            Img(backdrop, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            view.Backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();

            RectTransform board = Node("Board", root.transform, Center, Center, Center, Vector2.zero, new Vector2(1920f, 1086f));
            BuildHero(board, view);
            BuildRight(board, view);
            view.Feedback = Label(Node("Feedback", board, Center, Center, Center, new Vector2(0f, -506f), new Vector2(900f, 40f)),
                "", _semibold, 19f, new Color32(0xFF, 0x9A, 0x86, 0xFF), TextAlignmentOptions.Center);
            view.Feedback.textWrappingMode = TextWrappingModes.Normal;
            BuildTooltip(board, view);
            return root;
        }

        // ---- карточка героя ----
        static void BuildHero(RectTransform board, CampTentView view)
        {
            RectTransform panel = Node("Hero Panel", board, Center, Center, Center, new Vector2(-460f, -10f), new Vector2(600f, 820f));
            Img(panel, PauseKit("pause_panel"), Color.white).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.HeroPanel = panel;

            // Портрет — тот же, что в HUD: волосы выходят за верх рамки.
            RectTransform tile = Node("Portrait Tile", panel, TopLeft, TopLeft, TopLeft, new Vector2(30f, -34f), new Vector2(200f, 196f));
            Img(tile, Chrome("hud_panel"), Color.white);
            RectTransform portrait = Stretch("Portrait", tile, 0f);
            portrait.offsetMin = new Vector2(6f, 6f);
            portrait.offsetMax = new Vector2(-6f, 24f);
            view.Portrait = portrait.gameObject.AddComponent<RawImage>();
            view.Portrait.uvRect = new Rect(.145f, .20f, .71f, .80f);
            view.Portrait.raycastTarget = false;
            view.Portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPath);

            RectTransform badge = Node("Level Badge", panel, TopRight, TopRight, TopRight, new Vector2(-28f, -30f), new Vector2(72f, 72f));
            Img(badge, Chrome("hud_panel"), Color.white);
            view.Level = Label(Stretch("Level", badge, 0f), "1", _bold, 32f, Color.white, TextAlignmentOptions.Center);

            RectTransform plate = Node("Name", panel, TopLeft, TopLeft, TopLeft, new Vector2(250f, -104f), new Vector2(300f, 62f));
            Img(plate, PauseKit("pause_tab_on"), Color.white);
            view.HeroName = Label(Stretch("Text", plate, 0f), "Пелаг", _bold, 28f, Color.white, TextAlignmentOptions.Center, 3f, true);

            RectTransform track = Node("XP", panel, TopLeft, TopLeft, TopLeft, new Vector2(250f, -176f), new Vector2(300f, 24f));
            Img(track, Chrome("bar_track"), Color.white);
            RectTransform fill = Stretch("Fill", Stretch("Fill Area", track, 4f), 0f);
            Img(fill, Chrome("bar_fill_white"), XpColour);
            fill.anchorMax = new Vector2(.3f, 1f);
            view.XpFill = fill;
            view.XpText = Label(Node("XP Text", panel, TopLeft, TopLeft, TopLeft, new Vector2(250f, -202f), new Vector2(300f, 26f)),
                "0 / 100", _semibold, 17f, Muted, TextAlignmentOptions.Center);

            string[] names = { "Weapon", "Armor", "Ring", "Talisman" };
            string[] labels = { "Оружие", "Броня", "Кольцо", "Талисман" };
            for (int i = 0; i < 4; i++)
                view.Worn[i] = WornSlot(panel, names[i], labels[i], new Vector2(30f + i * 142.67f, -252f));

            for (int i = 0; i < StatNames.Length; i++)
            {
                float x = i % 2 == 0 ? 30f : 315f, y = -412f - (i / 2) * 36f;
                RectTransform row = Node("Stat " + StatNames[i], panel, TopLeft, TopLeft, TopLeft, new Vector2(x, y), new Vector2(255f, 32f));
                Img(row, null, new Color(1f, 1f, 1f, 0f)).raycastTarget = true;
                IconImage(Node("Icon", row, LeftMiddle, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(24f, 24f)), StatIcons[i],
                    i == 11 ? new Color32(0xFF, 0x9A, 0x60, 0xFF) : Color.white);
                view.StatLabels[i] = Label(Node("Label", row, LeftMiddle, LeftMiddle, LeftMiddle, new Vector2(34f, 0f), new Vector2(150f, 30f)),
                    StatNames[i], _regular, 17f, Cream, TextAlignmentOptions.MidlineLeft);
                view.StatValues[i] = Label(Node("Value", row, RightMiddle, RightMiddle, RightMiddle, Vector2.zero, new Vector2(80f, 30f)),
                    "0", _bold, 19f, Color.white, TextAlignmentOptions.MidlineRight);
                view.StatRows[i] = row;
            }

            RectTransform divider = Node("Potions Divider", panel, TopLeft, TopLeft, LeftMiddle, new Vector2(30f, -646f), new Vector2(540f, 24f));
            Img(divider, PauseKit("pause_divider"), Color.white, false);
            RectTransform caption = Node("Caption", divider, Center, Center, Center, Vector2.zero, new Vector2(150f, 36f));
            Img(caption, PauseKit("pause_tab_off"), Color.white);
            Label(Stretch("Text", caption, 0f), "Зелья", _bold, 17f, Color.white, TextAlignmentOptions.Center, 2f, true);

            for (int i = 0; i < 4; i++)
            {
                RectTransform potion = Node("Potion " + i, panel, TopLeft, TopLeft, TopLeft, new Vector2(30f + i * 146.67f, -682f), new Vector2(100f, 100f));
                Img(potion, PauseKit("pause_tile"), Color.white).pixelsPerUnitMultiplier = 2.4f;
                view.Potions[i] = Clickable(potion, Chrome("slot_hover"), Color.white);
                view.PotionIcons[i] = Img(Stretch("Icon", potion, 16f), null, Color.white, false);
                view.PotionIcons[i].preserveAspect = true;
                view.PotionCounts[i] = Label(Node("Count", potion, BottomRight, BottomRight, BottomRight, new Vector2(-12f, 8f), new Vector2(60f, 26f)),
                    "0", _bold, 19f, Color.white, TextAlignmentOptions.BottomRight);
                RectTransform selected = Stretch("Selected", potion, -4f);
                Img(selected, Chrome("slot_focus"), Color.white);
                selected.gameObject.SetActive(false);
                view.PotionSelected[i] = selected.gameObject;
            }
        }

        static CampTentCell WornSlot(RectTransform panel, string name, string label, Vector2 position)
        {
            RectTransform rect = Node("Slot " + name, panel, TopLeft, TopLeft, TopLeft, position, new Vector2(112f, 112f));
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            cell.Frame = Img(rect, PauseKit("pause_tile"), Color.white);
            // Край плитки нарисован под 400 px; в слоте 112 он съедал бы всю плитку.
            cell.Frame.pixelsPerUnitMultiplier = 2.4f;
            cell.Frame.raycastTarget = true;
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();
            Hover(rect, Chrome("slot_hover"), Color.white, -3f, 1.04f);
            CellInsides(cell, rect, 18f, 20f);
            cell.Placeholder = Img(Stretch("Placeholder", rect, 30f), null, new Color(1f, 1f, 1f, .18f), false);
            cell.Placeholder.preserveAspect = true;
            cell.Placeholder.enabled = false;
            cell.Placeholder.transform.SetSiblingIndex(cell.Icon.transform.GetSiblingIndex());

            RectTransform caption = Node("Caption", rect, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(0f, -6f), new Vector2(138f, 30f));
            Img(caption, PauseKit("pause_button_coral"), Color.white).pixelsPerUnitMultiplier = 2f;
            Label(Stretch("Text", caption, 0f), label, _bold, 13f, Color.white, TextAlignmentOptions.Center, .5f, true);
            return cell;
        }

        /// <summary>Свечение и камень редкости, значок, уровень, отметки выбора и «беречь».</summary>
        static void CellInsides(CampTentCell cell, RectTransform rect, float iconInset, float gemSize)
        {
            cell.RarityGlow = Img(Stretch("Rarity Glow", rect, 8f), Chrome("rarity_glow"), Color.white, false);
            cell.RarityGlow.enabled = false;
            cell.Icon = Img(Stretch("Icon", rect, iconInset), null, Color.white, false);
            cell.Icon.preserveAspect = true;
            cell.Icon.enabled = false;
            RectTransform gem = Node("Rarity Gem", rect, TopLeft, TopLeft, TopLeft, new Vector2(8f, -8f), new Vector2(gemSize, gemSize));
            cell.Gem = Img(gem, Chrome("rarity_gem"), Color.white, false);
            cell.Gem.preserveAspect = true;
            cell.Gem.enabled = false;
            cell.Level = Label(Node("Level", rect, BottomRight, BottomRight, BottomRight, new Vector2(-9f, 5f), new Vector2(50f, 22f)),
                "", _bold, 15f, Color.white, TextAlignmentOptions.BottomRight);
            RectTransform lockIcon = Node("Lock", rect, TopRight, TopRight, TopRight, new Vector2(-6f, -6f), new Vector2(22f, 22f));
            IconImage(lockIcon, "lock", Cream);
            lockIcon.gameObject.SetActive(false);
            cell.Lock = lockIcon.gameObject;
            RectTransform lane = Stretch("Shine Lane", rect, 4f);
            lane.gameObject.AddComponent<RectMask2D>();
            RectTransform shine = Node("Shine", lane, Center, Center, Center, Vector2.zero, new Vector2(34f, 150f));
            shine.localRotation = Quaternion.Euler(0f, 0f, -20f);
            Img(shine, Chrome("pause_shine"), new Color(1f, 1f, 1f, .75f), false);
            shine.gameObject.SetActive(false);
            cell.Shine = shine;
            // Контурная отметка выбора поверх предмета: заливки нет, значок виден.
            RectTransform selected = Stretch("Selected", rect, -4f);
            Img(selected, Chrome("slot_focus"), Color.white);
            selected.gameObject.SetActive(false);
            cell.Selection = selected.gameObject;
        }

        // ---- сумка и атлас ----
        static void BuildRight(RectTransform board, CampTentView view)
        {
            RectTransform panel = Node("Bag Panel", board, Center, Center, Center, new Vector2(330f, -10f), new Vector2(900f, 820f));
            Img(panel, PauseKit("pause_panel"), Color.white).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.BagPanel = panel;

            RectTransform close = Node("Close", panel, TopRight, TopRight, Center, new Vector2(-6f, -6f), new Vector2(58f, 58f));
            Img(close, Chrome("icon_ring"), Color.white, false).raycastTarget = true;
            view.Close = Clickable(close, Chrome("icon_ring"), new Color(.6f, .9f, 1f, .9f));
            IconImage(Stretch("Icon", close, 17f), "menu_close", Cream);

            view.BagTab = Tab(panel, "Tab Bag", "Сумка", -125f, true);
            view.AtlasTab = Tab(panel, "Tab Atlas", "Атлас", 125f, false);

            RectTransform bag = Stretch("Bag Page", panel, 0f);
            view.BagPage = bag.gameObject;
            view.BagCount = Label(Node("Count", bag, TopCenter, TopCenter, Center, new Vector2(0f, -788f), new Vector2(200f, 30f)),
                "0 / 48", _semibold, 17f, Muted, TextAlignmentOptions.MidlineRight);
            string[] filters = { "Все", "Оружие", "Броня", "Кольца", "Талисманы" };
            for (int i = 0; i < filters.Length; i++)
            {
                RectTransform chip = Node("Filter " + i, bag, TopCenter, TopCenter, Center, new Vector2(-352f + i * 152f, -122f), new Vector2(146f, 42f));
                Img(chip, i == 0 ? PauseKit("pause_tab_on") : PauseKit("pause_tab_off"), Color.white).pixelsPerUnitMultiplier = 1.6f;
                view.Filters[i] = Clickable(chip, PauseKit("pause_tab_hover"), Color.white);
                Label(Stretch("Text", chip, 0f), filters[i], _semibold, 15f, Color.white, TextAlignmentOptions.Center, .5f, true)
                    .margin = new Vector4(14f, 0f, 14f, 0f);
            }
            RectTransform grid = Node("Grid", bag, TopCenter, TopCenter, TopCenter, new Vector2(0f, -166f), new Vector2(797f, 595f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(90f, 90f);
            layout.spacing = new Vector2(11f, 11f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            view.BagGrid = grid;
            view.BagTemplate = BagCell(grid);
            view.BagTemplate.gameObject.SetActive(false);

            RectTransform atlas = Stretch("Atlas Page", panel, 0f);
            view.AtlasPage = atlas.gameObject;
            view.AtlasCount = Label(Node("Count", atlas, TopCenter, TopCenter, Center, new Vector2(0f, -122f), new Vector2(600f, 36f)),
                "", _semibold, 19f, Muted, TextAlignmentOptions.Center);
            RectTransform atlasGrid = Node("Grid", atlas, TopCenter, TopCenter, TopCenter, new Vector2(0f, -160f), new Vector2(720f, 620f));
            var atlasLayout = atlasGrid.gameObject.AddComponent<GridLayoutGroup>();
            atlasLayout.cellSize = new Vector2(165f, 145f);
            atlasLayout.spacing = new Vector2(20f, 13f);
            atlasLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            atlasLayout.constraintCount = 4;
            atlasLayout.childAlignment = TextAnchor.UpperCenter;
            view.AtlasGrid = atlasGrid;
            view.AtlasTemplate = AtlasEntry(atlasGrid);
            view.AtlasTemplate.gameObject.SetActive(false);
            atlas.gameObject.SetActive(false);
        }

        static Button Tab(RectTransform panel, string name, string label, float x, bool on)
        {
            RectTransform tab = Node(name, panel, TopCenter, TopCenter, Center, new Vector2(x, -54f), new Vector2(230f, 58f));
            Img(tab, on ? PauseKit("pause_tab_on") : PauseKit("pause_tab_off"), Color.white);
            Button button = Clickable(tab, PauseKit("pause_tab_hover"), Color.white);
            Label(Stretch("Text", tab, 0f), label, _bold, 24f, Color.white, TextAlignmentOptions.Center, 3f, true);
            return button;
        }

        static CampTentCell BagCell(Transform grid)
        {
            RectTransform rect = Node("Bag Cell Template", grid, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(90f, 90f));
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            cell.Frame = Img(rect, Chrome("slot"), Color.white);
            cell.Frame.raycastTarget = true;
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();
            Hover(rect, Chrome("slot_hover"), Color.white, -2f, 1.04f);
            CellInsides(cell, rect, 11f, 18f);
            return cell;
        }

        static CampAtlasEntry AtlasEntry(Transform grid)
        {
            RectTransform rect = Node("Atlas Entry Template", grid, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(165f, 145f));
            Img(rect, null, new Color(1f, 1f, 1f, 0f)).raycastTarget = true;
            var entry = rect.gameObject.AddComponent<CampAtlasEntry>();
            RectTransform frame = Node("Frame", rect, TopCenter, TopCenter, TopCenter, Vector2.zero, new Vector2(104f, 104f));
            entry.Frame = Img(frame, Chrome("slot"), Color.white);
            entry.Icon = Img(Stretch("Icon", frame, 12f), null, Color.white, false);
            entry.Icon.preserveAspect = true;
            entry.Name = Label(Node("Name", rect, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(.5f, 0f), Vector2.zero, new Vector2(165f, 34f)),
                "", _semibold, 15f, Cream, TextAlignmentOptions.Center);
            entry.Name.textWrappingMode = TextWrappingModes.Normal;
            return entry;
        }

        // ---- подсказка ----
        static void BuildTooltip(RectTransform board, CampTentView view)
        {
            RectTransform card = Node("Tooltip", board, Center, Center, TopLeft, new Vector2(-920f, 480f), new Vector2(440f, 262f));
            Img(card, PauseKit("pause_panel"), Color.white).pixelsPerUnitMultiplier = 1.6f;
            view.Tooltip = card;
            view.TooltipGroup = card.gameObject.AddComponent<CanvasGroup>();
            view.TooltipGroup.blocksRaycasts = false;
            view.TooltipGroup.interactable = false;
            view.TooltipGroup.alpha = 0f;

            RectTransform frame = Node("Frame", card, TopLeft, TopLeft, TopLeft, new Vector2(30f, -30f), new Vector2(96f, 96f));
            view.ItemFrame = Img(frame, Chrome("slot"), Color.white);
            view.ItemArt = Img(Stretch("Art", frame, 10f), null, Color.white, false);
            view.ItemArt.preserveAspect = true;
            view.ItemArt.enabled = false;

            view.ItemTitle = Label(Node("Title", card, TopLeft, TopLeft, TopLeft, new Vector2(142f, -30f), new Vector2(270f, 32f)),
                "", _bold, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            view.ItemTitle.enableAutoSizing = true;
            view.ItemTitle.fontSizeMin = 15f;
            view.ItemTitle.fontSizeMax = 22f;
            view.ItemRarity = Label(Node("Rarity", card, TopLeft, TopLeft, TopLeft, new Vector2(142f, -64f), new Vector2(270f, 24f)),
                "", _semibold, 17f, Muted, TextAlignmentOptions.MidlineLeft);
            view.ItemKind = Label(Node("Kind", card, TopLeft, TopLeft, TopLeft, new Vector2(142f, -92f), new Vector2(270f, 24f)),
                "", _regular, 15f, Muted, TextAlignmentOptions.MidlineLeft);
            Img(Node("Divider", card, TopLeft, TopLeft, LeftMiddle, new Vector2(30f, -140f), new Vector2(380f, 12f)), PauseKit("pause_divider"), Color.white, false);
            view.ItemStats = Label(Node("Stats", card, TopLeft, TopLeft, TopLeft, new Vector2(32f, -154f), new Vector2(376f, 100f)),
                "", _regular, 17f, Cream, TextAlignmentOptions.TopLeft);
            view.ItemStats.textWrappingMode = TextWrappingModes.Normal;
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
