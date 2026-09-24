using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Палатка снаряжения на паке «Ночная акварель» (23 сентября 2026):
    /// Resources/UI/Prefabs/CampTentWc.prefab. Раскладка принятого концепта O
    /// (tent-O-portrait-card): слева карточка героя — портрет, имя, уровень и опыт,
    /// четыре слота, лист характеристик, зелья; справа вкладки «Сумка / Атлас»,
    /// фильтры и сетка. Вид — детали пака, смысл — прежний CampTentView и
    /// CampInventoryView. CampInventoryView берёт этот префаб первым, старый
    /// CampTent остаётся запасным. Префаб создаётся, только если его нет.
    /// </summary>
    public static partial class CampTentWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTentWc.prefab";
        const string Folder = "Assets/UI/CampTent/";

        static UiTheme T => UiTheme.Current;

        /// <summary>Порядок строк листа героя; совпадает с CampInventoryView.StatRows.</summary>
        static readonly string[] StatNames =
        {
            "Здоровье", "Урон", "Броня", "Скор. атаки", "Шанс крита", "Сила крита",
            "Лавидий", "Лавидий/с", "Скор. бега", "Скор. приёмов", "Перезарядка", "Сопр. огню",
        };
        /// <summary>Свой значок у каждого стата (владелец 23 сентября); недостающие дорисованы в том же стиле.</summary>
        static readonly string[] StatIcons =
        {
            "wc_stat_heart", "wc_stat_damage", "wc_stat_armor", "wc_stat_attack_speed", "wc_stat_crit_chance", "wc_stat_crit_power",
            "wc_stat_lavidium", "wc_stat_lavidium_regen", "wc_stat_move_speed", "wc_stat_ability_speed", "wc_stat_cooldown", "wc_stat_fire_resist",
        };

        [MenuItem("Разлом/UI/Собрать палатку «Ночная акварель»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Палатка", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        public static string Build(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return PrefabPath;
            UiThemeBuilder.Ensure(false);
            EnsurePrefabs();
            PrepareFrames();
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
            return PrefabPath;
        }

        /// <summary>Рамки редкости для сумки: серебро, бирюза, фиолет, коралл (9-slice как у wc_frame_s).</summary>
        static void PrepareFrames()
        {
            foreach (string name in new[] { "frame_empty", "frame_common", "frame_rare", "frame_epic", "frame_unique" })
            {
                string path = Folder + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.Sprite && importer.spriteBorder.x > 0f) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 200f;
                importer.spriteBorder = new Vector4(24f, 24f, 24f, 24f);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        static Sprite Frame(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + name + ".png");

        static Sprite KitIcon(string name)
        {
            if (name.StartsWith("wc_"))
            {
                string path = UiKitImport.KitRoot + "/Watercolor/" + name + ".png";
                UiKitImport.Ensure(path);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            return CombatHudBuilder.Icon(name);
        }

        static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            TMP_Text label = Label(box, "Надпись", text, font, size, role, align);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        static Button KitTab(RectTransform parent, string name, string text, float x, float y, float w, float size)
        {
            RectTransform tab = Place("Tab", parent, name);
            TopLeft(tab, x, y, w, 50f);
            TMP_Text label = tab.Find("Надпись").GetComponent<TMP_Text>();
            label.text = text;
            label.fontSize = size;
            Image catcher = Layer(tab, "Ловец", T.Pixel, Role.Panel, 0f);
            catcher.raycastTarget = true;
            catcher.transform.SetAsFirstSibling();
            var button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = catcher;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampTentWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Под паузой (300): Escape из палатки не должен прятать меню.
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var view = root.AddComponent<CampTentView>();
            view.LayoutVersion = 100;
            view.TabOn = view.TabOff = null;
            view.EmptyFrame = Frame("frame_empty");
            view.RarityFrames = new[] { Frame("frame_common"), Frame("frame_rare"), Frame("frame_epic"), Frame("frame_unique") };
            view.RarityColours = new Color[]
            {
                new Color32(0xA6, 0xB3, 0xC8, 0xFF), new Color32(0x3B, 0xF0, 0xF5, 0xFF),
                // Как в теме (роли Epic и Unique): уникальная не путается с оранжевым акцентом выбора.
                new Color32(0xA7, 0x65, 0xFF, 0xFF), new Color32(0xFF, 0x52, 0x36, 0xFF),
            };
            view.EmptyRing = new Color32(0xD8, 0xE1, 0xEE, 0xFF);
            view.RarityNames = new[] { "Обычная", "Редкая", "Эпическая", "Уникальная" };
            view.StatUp = new Color32(0x8C, 0xE0, 0x7A, 0xFF);
            view.StatDown = new Color32(0xFF, 0x7A, 0x66, 0xFF);

            // Лагерь виден целиком (решение владельца 22 сентября: без размытия и подложки),
            // прозрачный слой только ловит клики мимо окон.
            RectTransform backdrop = Stretch(Node("Фон", root.transform));
            Image catcher = backdrop.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            view.Backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();
            // Лёгкая виньетка по краям: панели не сливаются со светлой землёй.
            Layer(backdrop, "Виньетка", T.VeilRadial, Role.Veil, .8f);

            RectTransform board = Node("Доска", root.transform);
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(.5f, .5f);
            board.sizeDelta = new Vector2(1920f, 1080f);
            BuildHero(board, view);
            BuildBag(board, view);
            RectTransform feedback = TopLeft(Node("Сообщение", board), 460f, 1000f, 1000f, 36f);
            view.Feedback = Label(feedback, "Надпись", "", FontRole.Body, 19f, Role.Accent, TextAlignmentOptions.Center);
            BuildTooltip(board, view);
            return root;
        }

        // ---------------------------------------------------------------- карточка героя
        static void BuildHero(RectTransform board, CampTentView view)
        {
            RectTransform panel = Place("Panel", board, "Герой");
            TopLeft(panel, 96f, 100f, 610f, 860f);
            ((Image)panel.Find("Заливка").GetComponent<Image>()).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.HeroPanel = panel;
            var content = (RectTransform)panel.Find("Содержимое");

            // Портрет пака (кольцо, рисованный Пелаг, ромб уровня).
            RectTransform portrait = Place("Portrait", content, "Портрет");
            TopLeft(portrait, 26f, 22f, 200f, 200f);
            portrait.localScale = Vector3.one * .82f;
            view.Portrait = portrait.Find("Диск/Портрет").GetComponent<RawImage>();
            view.Portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/HUD/PelagPortraitPainted.png");
            view.Level = portrait.Find("Уровень/Число").GetComponent<TMP_Text>();
            view.Level.text = "1";

            view.HeroName = Text(content, "Имя", "Пелаг", 216f, 40f, 360f, 50f, FontRole.Heading, 42f, Role.Text);
            view.HeroName.characterSpacing = 2f;
            RectTransform xp = Place("Bar", content, "Опыт");
            TopLeft(xp, 218f, 106f, 350f, 14f);
            Object.DestroyImmediate(xp.GetComponent<WcBar>());
            var fill = (RectTransform)xp.Find("Заполнение");
            fill.GetComponent<ThemeColor>().SetRole(Role.Experience);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.3f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Transform glare = fill.Find("Свет");
            if (glare != null) glare.gameObject.SetActive(false);
            view.XpFill = fill;
            view.XpText = Text(content, "Опыт числом", "0 / 100", 218f, 126f, 350f, 26f, FontRole.Body, 17f, Role.TextMuted);

            string[] slots = { "Оружие", "Броня", "Кольцо", "Талисман" };
            for (int i = 0; i < 4; i++)
                view.Worn[i] = WornSlot(content, slots[i], 30f + i * 138f, 204f);

            // Статы группами (концепт tent-stats, владелец 23 сентября: «все 12, но понятно»):
            // слева «Нападение» и «Лавидий», справа «Защита» и «Темп» — по шесть строк в колонке.
            for (int i = 0; i < StatIcons.Length; i++) view.StatIcons[i] = KitIcon(StatIcons[i]);
            for (int g = 0; g < StatGroups.Length; g++)
            {
                float x = g % 2 == 0 ? 30f : 316f;
                float y = 364f + (g / 2) * (36f + StatGroups[g % 2].Length * StatPitch + 10f);
                TopLeft(GroupHeader(content, StatGroupNames[g]), x, y, 264f, 30f);
                y += 36f;
                foreach (int i in StatGroups[g])
                {
                    StatRow(content, view, i, x, y);
                    y += StatPitch;
                }
            }

            TopLeft(SectionHeaderAt(content, "Зелья"), 30f, 662f, 550f, 30f);
            string[] potionKeys = { "Малое здоровья", "Большое здоровья", "Малое лавидия", "Большое лавидия" };
            for (int i = 0; i < 4; i++)
            {
                RectTransform tile = TopLeft(Node("Зелье " + (i + 1), content), 30f + i * 138f, 704f, 100f, 100f);
                Image shadow = Layer(tile, "Тень", T.GlowSmall, Role.Veil, .8f, 18f);
                shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
                Image fillImage = Layer(tile, "Заливка", T.FillSmall, Role.Panel);
                fillImage.raycastTarget = true;
                Layer(tile, "Тень снизу", T.ShadeSmall, Role.Veil, .5f);
                Image halo = Mark(tile, "Свет", T.Blob, i < 2 ? Role.Health : Role.Lavidium, .2f, new Vector2(.5f, .5f), Vector2.zero, 90f);
                halo.preserveAspect = false;
                RectTransform icon = Stretch(Node("Бутылка", tile), 12f);
                view.PotionIcons[i] = icon.gameObject.AddComponent<Image>();
                view.PotionIcons[i].preserveAspect = true;
                view.PotionIcons[i].raycastTarget = false;
                Layer(tile, "Свет по кромке", T.HighlightSmall, Role.Highlight, .8f);
                // Одна рамка: серебро; зелье в быстром слоте — толстая оранжевая (та же рамка).
                WcSlotState state = SlotState(tile, Layer(tile, "Рамка", T.FrameSmall, Role.PanelLine), null);
                state.Rarity = WcSlotState.Plain;
                state.SelectedRole = Role.Accent;
                state.Apply();
                view.PotionStates[i] = state;
                RectTransform count = TopLeft(Node("Запас", tile), 46f, 70f, 48f, 26f);
                view.PotionCounts[i] = Label(count, "Надпись", "0", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.BottomRight);
                view.PotionCounts[i].fontStyle = FontStyles.Bold;
                var button = tile.gameObject.AddComponent<Button>();
                button.targetGraphic = fillImage;
                button.transition = Selectable.Transition.None;
                var motion = tile.gameObject.AddComponent<UiHoverMotion>();
                motion.HoverScale = 1.04f;
                view.Potions[i] = button;
            }
        }

        const float StatPitch = 32f;
        static readonly string[] StatGroupNames = { "Нападение", "Защита", "Лавидий", "Темп" };
        /// <summary>Номера строк StatNames в каждой группе.</summary>
        static readonly int[][] StatGroups =
        {
            new[] { 1, 3, 4, 5 },
            new[] { 0, 2, 11 },
            new[] { 6, 7 },
            new[] { 8, 9, 10 },
        };

        static RectTransform GroupHeader(RectTransform parent, string text)
        {
            RectTransform header = Node("Группа " + text, parent);
            Image diamond = Mark(header, "Ромб", T.DiamondSmall, Role.Accent, .9f, new Vector2(0f, .5f), new Vector2(8f, 0f), 12f);
            diamond.preserveAspect = true;
            TMP_Text label = Label(header, "Надпись", text.ToUpperInvariant(), FontRole.Heading, 16f, Role.Text);
            label.characterSpacing = 4f;
            label.rectTransform.offsetMin = new Vector2(22f, 0f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform line = Node("Линия", header);
            line.anchorMin = new Vector2(0f, .5f);
            line.anchorMax = new Vector2(1f, .5f);
            line.pivot = new Vector2(0f, .5f);
            line.offsetMin = new Vector2(22f + text.Length * 13f + 18f, -.5f);
            line.offsetMax = new Vector2(-4f, .5f);
            Tint(line.gameObject.AddComponent<Image>(), Role.PanelLine, .28f).GetComponent<Image>().sprite = T.Pixel;
            return header;
        }

        static void StatRow(RectTransform content, CampTentView view, int i, float x, float y)
        {
            RectTransform row = TopLeft(Node("Стат " + StatNames[i], content), x, y, 264f, StatPitch - 2f);
            Image hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            // Наведение на строку — мягкая полоса, без рамки.
            Image bar = Layer(row, "Наведение", T.FillSmall, Role.Text, .07f);
            var motion = row.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = bar;
            motion.HoverScale = 1f;
            motion.PressScale = 1f;
            motion.PulseWhileHovered = false;
            motion.HoverSound = false;
            Mark(row, "Значок", view.StatIcons[i], Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(16f, 0f), 20f);
            view.StatLabels[i] = Text(row, "Подпись", StatNames[i], 36f, 0f, 150f, StatPitch - 2f, FontRole.Body, 17f, Role.TextMuted);
            view.StatValues[i] = Text(row, "Число", "0", 170f, 0f, 86f, StatPitch - 2f, FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineRight);
            view.StatValues[i].fontStyle = FontStyles.Bold;
            view.StatRows[i] = row;
        }

        static RectTransform SectionHeaderAt(RectTransform parent, string text)
        {
            RectTransform header = Place("SectionHeader", parent, text);
            TMP_Text label = header.Find("Надпись").GetComponent<TMP_Text>();
            label.text = text.ToUpperInvariant();
            label.fontSize = 19f;
            label.characterSpacing = 5f;
            return header;
        }

        static CampTentCell WornSlot(RectTransform parent, string caption, float x, float y)
        {
            RectTransform rect = TopLeft(Node("Слот " + caption, parent), x, y, 112f, 112f);
            CampTentCell cell = Cell(rect, 12f, 20f);
            cell.Round = true;
            cell.Placeholder = Stretch(Node("Пустой слот", rect), 28f).gameObject.AddComponent<Image>();
            cell.Placeholder.color = new Color(1f, 1f, 1f, .16f);
            cell.Placeholder.preserveAspect = true;
            cell.Placeholder.raycastTarget = false;
            cell.Placeholder.enabled = false;
            cell.Placeholder.transform.SetSiblingIndex(cell.Icon.transform.GetSiblingIndex());
            RectTransform tag = Place("Tag", rect, "Подпись");
            tag.anchorMin = tag.anchorMax = new Vector2(.5f, 0f);
            tag.pivot = new Vector2(.5f, .5f);
            tag.anchoredPosition = new Vector2(0f, -8f);
            tag.sizeDelta = new Vector2(112f, 28f);
            tag.Find("Ромб").gameObject.SetActive(false);
            TMP_Text label = tag.Find("Надпись").GetComponent<TMP_Text>();
            label.text = caption;
            label.fontSize = 15f;
            label.rectTransform.offsetMin = Vector2.zero;
            return cell;
        }

        /// <summary>
        /// Ячейка палатки на паке (вариант А владельца 23 сентября): фон цвета редкости, вещь,
        /// одна рамка (WcSlotState: редкость, наведение и выбор меняют её саму), камень, уровень,
        /// замок «беречь», блик уникальной. Вспышка при надевании — слой «Свет редкости».
        /// </summary>
        static CampTentCell Cell(RectTransform rect, float iconInset, float gemSize)
        {
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            Image shadow = Layer(rect, "Тень", T.GlowSmall, Role.Veil, .75f, 16f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            Layer(rect, "Заливка", T.FillSmall, Role.Panel);
            Layer(rect, "Тень снизу", T.ShadeSmall, Role.Veil, .45f);
            Image fill = SlotFill(rect);
            RectTransform glow = Stretch(Node("Свет редкости", rect), 2f);
            cell.RarityGlow = glow.gameObject.AddComponent<Image>();
            cell.RarityGlow.sprite = T.InnerGlowSmall;
            cell.RarityGlow.type = Image.Type.Sliced;
            cell.RarityGlow.raycastTarget = false;
            cell.RarityGlow.enabled = false;
            cell.Icon = Stretch(Node("Вещь", rect), iconInset).gameObject.AddComponent<Image>();
            cell.Icon.preserveAspect = true;
            cell.Icon.raycastTarget = false;
            cell.Icon.enabled = false;
            Layer(rect, "Свет по кромке", T.HighlightSmall, Role.Highlight, .7f);
            RectTransform frame = Stretch(Node("Рамка", rect));
            cell.Frame = frame.gameObject.AddComponent<Image>();
            cell.Frame.sprite = T.FrameSmall;
            cell.Frame.type = Image.Type.Sliced;
            cell.Frame.raycastTarget = true;
            cell.State = SlotState(rect, cell.Frame, fill);
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();

            RectTransform gem = At(Node("Камень", rect), new Vector2(.5f, 1f), Vector2.zero, new Vector2(gemSize, gemSize));
            cell.Gem = gem.gameObject.AddComponent<Image>();
            cell.Gem.sprite = T.Gem;
            cell.Gem.preserveAspect = true;
            cell.Gem.raycastTarget = false;
            cell.Gem.enabled = false;
            RectTransform level = Node("Уровень", rect);
            level.anchorMin = level.anchorMax = level.pivot = new Vector2(1f, 0f);
            level.anchoredPosition = new Vector2(-6f, 3f);
            level.sizeDelta = new Vector2(46f, 22f);
            cell.Level = Label(level, "Надпись", "", FontRole.Body, 15f, Role.Text, TextAlignmentOptions.BottomRight);
            cell.Level.fontStyle = FontStyles.Bold;
            Image lockIcon = Mark(rect, "Беречь", CombatHudBuilder.Icon("lock"), Role.Text, .9f, new Vector2(1f, 1f), new Vector2(-14f, -14f), 20f);
            lockIcon.gameObject.SetActive(false);
            cell.Lock = lockIcon.gameObject;
            RectTransform lane = Stretch(Node("Дорожка блика", rect), 3f);
            lane.gameObject.AddComponent<RectMask2D>();
            RectTransform shine = At(Node("Блик", lane), new Vector2(.5f, .5f), Vector2.zero, new Vector2(30f, 160f));
            shine.localRotation = Quaternion.Euler(0f, 0f, -20f);
            Image shineImage = shine.gameObject.AddComponent<Image>();
            shineImage.sprite = T.Pixel;
            shineImage.color = new Color(1f, 1f, 1f, .22f);
            shineImage.raycastTarget = false;
            shine.gameObject.SetActive(false);
            cell.Shine = shine;
            var motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.05f;
            motion.PressScale = .97f;
            return cell;
        }

        // ---------------------------------------------------------------- сумка и атлас
        static void BuildBag(RectTransform board, CampTentView view)
        {
            RectTransform panel = Place("Panel", board, "Сумка");
            TopLeft(panel, 740f, 100f, 1084f, 860f);
            ((Image)panel.Find("Заливка").GetComponent<Image>()).raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>();
            view.BagPanel = panel;
            var content = (RectTransform)panel.Find("Содержимое");

            RectTransform close = Place("CloseButton", content, "Закрыть");
            TopLeft(close, 1084f - 70f, 18f, 52f, 52f);
            close.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.08f;
            view.Close = close.GetComponent<Button>();

            view.BagTab = KitTab(content, "Вкладка сумка", "Сумка", 1084f * .5f - 250f, 24f, 240f, 28f);
            view.AtlasTab = KitTab(content, "Вкладка атлас", "Атлас", 1084f * .5f + 10f, 24f, 240f, 28f);
            TopLeft(Place("Divider", content, "Линия"), 1084f * .5f - 330f, 82f, 660f, 16f);

            RectTransform bag = Stretch(Node("Страница сумки", content));
            view.BagPage = bag.gameObject;
            string[] filters = { "Все", "Оружие", "Броня", "Кольца", "Талисманы" };
            for (int i = 0; i < filters.Length; i++)
                view.Filters[i] = KitTab(bag, "Фильтр " + i, filters[i], 64f + i * 156f, 106f, 150f, 20f);
            view.BagCount = Text(bag, "Занято", "0 / 48", 870f, 114f, 150f, 34f, FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.MidlineRight);

            RectTransform grid = TopLeft(Node("Сетка", bag), 1084f * .5f - 423f, 172f, 846f, 632f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(96f, 96f);
            layout.spacing = new Vector2(10f, 11f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            view.BagGrid = grid;
            RectTransform template = Node("Ячейка сумки", grid);
            template.sizeDelta = new Vector2(96f, 96f);
            view.BagTemplate = Cell(template, 10f, 18f);
            template.gameObject.SetActive(false);

            RectTransform atlas = Stretch(Node("Страница атласа", content));
            view.AtlasPage = atlas.gameObject;
            view.AtlasCount = Text(atlas, "Найдено", "", 0f, 110f, 1084f, 36f, FontRole.Body, 20f, Role.TextMuted, TextAlignmentOptions.Center);
            RectTransform atlasGrid = TopLeft(Node("Сетка атласа", atlas), 1084f * .5f - 402f, 166f, 804f, 660f);
            var atlasLayout = atlasGrid.gameObject.AddComponent<GridLayoutGroup>();
            atlasLayout.cellSize = new Vector2(186f, 158f);
            atlasLayout.spacing = new Vector2(20f, 8f);
            atlasLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            atlasLayout.constraintCount = 4;
            atlasLayout.childAlignment = TextAnchor.UpperCenter;
            view.AtlasGrid = atlasGrid;
            view.AtlasTemplate = AtlasEntry(atlasGrid);
            view.AtlasTemplate.gameObject.SetActive(false);
            atlas.gameObject.SetActive(false);
        }

        static CampAtlasEntry AtlasEntry(Transform grid)
        {
            RectTransform rect = Node("Запись атласа", grid);
            rect.sizeDelta = new Vector2(186f, 158f);
            Image hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            var entry = rect.gameObject.AddComponent<CampAtlasEntry>();
            entry.Silhouette = new Color(.02f, .03f, .06f, .9f);
            RectTransform tile = At(Node("Плитка", rect), new Vector2(.5f, 1f), new Vector2(0f, -54f), new Vector2(104f, 104f));
            Layer(tile, "Заливка", T.FillSmall, Role.Panel);
            Layer(tile, "Тень снизу", T.ShadeSmall, Role.Veil, .45f);
            entry.Icon = Stretch(Node("Вещь", tile), 12f).gameObject.AddComponent<Image>();
            entry.Icon.preserveAspect = true;
            entry.Icon.raycastTarget = false;
            RectTransform frame = Stretch(Node("Рамка", tile));
            entry.Frame = frame.gameObject.AddComponent<Image>();
            entry.Frame.sprite = Frame("frame_common");
            entry.Frame.type = Image.Type.Sliced;
            entry.Frame.raycastTarget = false;
            RectTransform name = Node("Название", rect);
            name.anchorMin = name.anchorMax = name.pivot = new Vector2(.5f, 0f);
            name.anchoredPosition = Vector2.zero;
            name.sizeDelta = new Vector2(186f, 44f);
            entry.Name = Label(name, "Надпись", "", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
            entry.Name.textWrappingMode = TextWrappingModes.Normal;
            entry.Name.lineSpacing = -8f;
            return entry;
        }

        // ---------------------------------------------------------------- карточка вещи
        static void BuildTooltip(RectTransform board, CampTentView view)
        {
            // Карточка раскладывается сама (владелец 23 сентября: подсказка была нечитаемой —
            // тексты уезжали за край): шапка «картинка + название, редкость, вид», линия, свойства.
            RectTransform card = Node("Карточка", board);
            card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
            card.pivot = new Vector2(0f, 1f);
            card.sizeDelta = new Vector2(470f, 300f);
            foreach (Image layer in new[]
            {
                Layer(card, "Тень", T.Glow, Role.Veil, .9f, 24f),
                // Под заливкой темы (прозрачность 0,92) — вторая такая же: яркие ячейки не просвечивают.
                Layer(card, "Подложка", T.Fill, Role.Panel, 1f),
                Layer(card, "Заливка", T.Fill, Role.Panel, 1f),
                Layer(card, "Тень снизу", T.ShadeSprite, Role.Veil, .5f),
                Layer(card, "Свет по кромке", T.HighlightSprite, Role.Highlight),
                Layer(card, "Рамка", T.Frame, Role.PanelLine, .9f),
            })
                layer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            card.Find("Тень").GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -6f);
            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(26, 26, 24, 24);
            column.spacing = 12f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Tooltip = card;
            view.TooltipAutoLayout = true;
            view.TooltipGroup = card.gameObject.AddComponent<CanvasGroup>();
            view.TooltipGroup.blocksRaycasts = false;
            view.TooltipGroup.interactable = false;
            view.TooltipGroup.alpha = 0f;

            RectTransform head = Node("Шапка", card);
            var row = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            RectTransform frame = Node("Вещь", head);
            var frameSize = frame.gameObject.AddComponent<LayoutElement>();
            frameSize.preferredWidth = frameSize.preferredHeight = frameSize.minWidth = frameSize.minHeight = 96f;
            Layer(frame, "Заливка", T.FillSmall, Role.Panel);
            view.ItemArt = Stretch(Node("Картинка", frame), 10f).gameObject.AddComponent<Image>();
            view.ItemArt.preserveAspect = true;
            view.ItemArt.raycastTarget = false;
            view.ItemArt.enabled = false;
            RectTransform ring = Stretch(Node("Рамка", frame));
            view.ItemFrame = ring.gameObject.AddComponent<Image>();
            view.ItemFrame.sprite = Frame("frame_common");
            view.ItemFrame.type = Image.Type.Sliced;
            view.ItemFrame.raycastTarget = false;

            RectTransform names = Node("Подписи", head);
            var namesColumn = names.gameObject.AddComponent<VerticalLayoutGroup>();
            namesColumn.spacing = 4f;
            namesColumn.childControlWidth = namesColumn.childControlHeight = true;
            namesColumn.childForceExpandWidth = true;
            namesColumn.childForceExpandHeight = false;
            names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            view.ItemTitle = FlowText(names, "Название", FontRole.Heading, 28f, Role.Text);
            view.ItemRarity = FlowText(names, "Редкость", FontRole.Body, 19f, Role.Text);
            view.ItemKind = FlowText(names, "Вид", FontRole.Body, 17f, Role.TextMuted);

            RectTransform line = Place("DividerPlain", card, "Линия");
            var lineSize = line.gameObject.AddComponent<LayoutElement>();
            lineSize.preferredHeight = lineSize.minHeight = 16f;
            view.ItemStats = FlowText(card, "Свойства", FontRole.Body, 19f, Role.Text);
            view.ItemStats.lineSpacing = 6f;
        }

        /// <summary>Текст внутри раскладки: TMP на самом узле, высота — по содержимому.</summary>
        static TMP_Text FlowText(RectTransform parent, string name, FontRole font, float size, Role role)
        {
            RectTransform node = Node(name, parent);
            var label = node.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var themeFont = node.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            return label;
        }
    }
}
