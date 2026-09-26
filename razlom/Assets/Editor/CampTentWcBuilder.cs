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
    /// Палатка снаряжения: Resources/UI/Prefabs/CampTentWc.prefab. Раскладка принятого концепта O
    /// (tent-O-portrait-card, 23 сентября): слева карточка героя — портрет, имя, уровень и опыт,
    /// четыре слота, лист характеристик, зелья; справа вкладки «Сумка / Атлас», фильтры и сетка.
    /// Материал — «Дым и свет» (владелец 26 сентября: «перевести вообще всё на новую версию»):
    /// глубокий дым вместо панелей пака, нити света вместо серебряных линий, круглые слоты и
    /// портрет, ячейки UiInkKit.Cell. Префабов пака (Place) больше нет — детали строятся на месте.
    /// Смысл — прежний CampTentView и CampInventoryView. CampInventoryView берёт этот префаб
    /// первым, старый CampTent остаётся запасным. Префаб создаётся, только если его нет.
    /// </summary>
    public static partial class CampTentWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTentWc.prefab";
        const string PortraitCutout = "Assets/Resources/UI/HUD/PelagPortraitPaintedCutout.png";
        const string PortraitPainted = "Assets/Resources/UI/HUD/PelagPortraitPainted.png";

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

        /// <summary>Надпись в своём узле; проявляется по буквам вслед за дымом панели.</summary>
        static TMP_Text Text(RectTransform parent, string name, string text, float x, float y, float w, float h, FontRole font, float size, Role role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float delay = .12f)
        {
            RectTransform box = TopLeft(Node(name, parent), x, y, w, h);
            TMP_Text label = UiInkKit.Label(box, "Надпись", text, font, size, role, align, delay: delay);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>
        /// Вкладка или фильтр «Дыма и света» (UiInkKit.Tab): подпись и нить света под выбранной.
        /// Мышь ловит прозрачный прямоугольник на самой вкладке; выбор переключает CampShopView.SetTab.
        /// </summary>
        static Button InkTab(RectTransform parent, string name, string text, float x, float y, float w, float size)
        {
            RectTransform tab = UiInkKit.Tab(parent, name, text, false, w, 50f, size);
            TopLeft(tab, x, y, w, 50f);
            Image hit = UiInkKit.HitArea(tab);
            var button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        /// <summary>
        /// Панель окна «Дыма и света»: глубокий дым шире рамки (UiInkKit.Plate), прозрачный ловец
        /// мыши на весь прямоугольник (картинки дыма мышь не ловят — клик провалился бы в ходьбу по
        /// лагерю), свой CanvasGroup (приезд панели, CampTentView.PlayOpen) и своя группа проявления.
        /// Дети — в «Содержимое», как у панели пака.
        /// </summary>
        static RectTransform InkPanel(RectTransform board, string name, float x, float y, float w, float h, out RectTransform content)
        {
            RectTransform panel = TopLeft(Node(name, board), x, y, w, h);
            UiInkKit.Plate(panel);
            UiInkKit.HitArea(panel);
            panel.gameObject.AddComponent<CanvasGroup>();
            content = Stretch(Node("Содержимое", panel));
            // Палатку открывают часто: короткое мягкое проявление сверху вниз, огонь по кромке едва тлеет.
            UiInkGroup appear = UiInkKit.Group(panel, UiInkGroup.Sweep.TopToBottom, .35f, .2f);
            appear.Burn = .2f;
            appear.HideDuration = .16f;
            return panel;
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampTentWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Под паузой (300): Escape из палатки не должен прятать меню.
            canvas.sortingOrder = 100;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal).
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
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
            // Рамок-спрайтов редкости у ячеек «Дыма и света» нет: рамка одна, цвет редкости ставит WcSlotState.
            view.EmptyFrame = null;
            view.RarityFrames = new Sprite[4];
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
            view.HeroPanel = InkPanel(board, "Герой", 96f, 100f, 610f, 860f, out RectTransform content);

            BuildPortrait(content, view);
            view.HeroName = Text(content, "Имя", "Пелаг", 216f, 40f, 360f, 50f, FontRole.Heading, 42f, Role.Text, delay: .05f);
            view.HeroName.characterSpacing = 2f;
            view.XpFill = ExperienceBar(content);
            view.XpText = Text(content, "Опыт числом", "0 / 100", 218f, 126f, 350f, 26f, FontRole.Body, 17f, Role.TextMuted, delay: .2f);

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
                TopLeft(Header(content, "Группа " + StatGroupNames[g], StatGroupNames[g], 16f, 4f), x, y, 264f, 30f);
                y += 36f;
                foreach (int i in StatGroups[g])
                {
                    StatRow(content, view, i, x, y);
                    y += StatPitch;
                }
            }

            TopLeft(Header(content, "Зелья", "Зелья", 19f, 5f), 30f, 662f, 550f, 30f);
            for (int i = 0; i < 4; i++) Potion(content, view, i);
        }

        /// <summary>
        /// Портрет — круг, как на боевом HUD: клуб дыма, тёмный диск с маской, внутри вырез
        /// рисованного Пелага (PelagPortraitPaintedCutout) и тёплый свет за головой, тонкое кремовое
        /// кольцо. Уровень — малый круг справа снизу (дым, диск, кольцо, число), не ромб пака.
        /// Размер — прежний портрет пака (200 × 0,82).
        /// </summary>
        static void BuildPortrait(RectTransform content, CampTentView view)
        {
            const float size = 164f;
            RectTransform portrait = TopLeft(Node("Портрет", content), 26f, 22f, size, size);
            UiInkKit.SmokeLayer(portrait, "Дым", "smoke_blot_1", 1f, size * .28f, size * .28f, deep: true);
            Image disc = UiKitBuilder.Layer(portrait, "Диск", T.CircleFill, Role.SmokeDeep, 1f);
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // Вместо огненного кольца пака — мягкий тёплый свет за головой.
            UiInkKit.LightAt(disc.rectTransform, "Тёплый свет", "light_glow", new Vector2(.5f, .5f), new Vector2(0f, size * .14f),
                new Vector2(size * .95f, size * .95f), .3f, delay: .1f);
            // Вырез без своего фона: крупнее диска и чуть ниже — голова целиком в круге, плечи уходят под край.
            var cutout = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitCutout);
            bool cut = cutout != null;
            RectTransform art = At(Node("Портрет", disc.rectTransform), new Vector2(.5f, .5f), new Vector2(0f, cut ? -size * .04f : 0f),
                Vector2.one * size * (cut ? 1.12f : 1f));
            view.Portrait = art.gameObject.AddComponent<RawImage>();
            view.Portrait.texture = cut ? cutout : AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPainted);
            view.Portrait.raycastTarget = false;
            view.Portrait.material = UiInkKit.Art;
            UiInkKit.Inked(view.Portrait, delay: .1f);
            Ring(portrait, "Кольцо", T.CircleFrameLarge, .5f);

            RectTransform badge = At(Node("Уровень", portrait), new Vector2(1f, 0f), new Vector2(-19f, 22f), new Vector2(44f, 44f));
            UiInkKit.SmokeLayer(badge, "Дым", "soft_blot", 1f, 10f, 10f, deep: true);
            Image badgeDisc = UiKitBuilder.Layer(badge, "Диск", T.CircleFill, Role.SmokeDeep, .95f);
            badgeDisc.material = UiInkKit.Plain;
            UiInkKit.Inked(badgeDisc, delay: .1f);
            Ring(badge, "Кольцо", T.CircleFrame, .6f);
            view.Level = UiInkKit.Label(badge, "Число", "1", FontRole.Body, 21f, Role.Text, TextAlignmentOptions.Center, delay: .2f);
            view.Level.fontStyle = FontStyles.Bold;
            view.Level.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>Тонкое кремовое кольцо на весь узел (без течения, проявляется с диском).</summary>
        static Image Ring(RectTransform parent, string name, Sprite sprite, float alpha)
        {
            Image ring = UiKitBuilder.Layer(parent, name, sprite, Role.Text, alpha);
            ring.material = UiInkKit.Plain;
            UiInkKit.Inked(ring, delay: .12f);
            return ring;
        }

        /// <summary>
        /// Опыт — мазок кистью, как полосы боевого HUD: тёмный мазок дорожкой, заливка — мазок
        /// цвета опыта во всю длину под маской (долю ставит CampInventoryView через anchorMax.x
        /// «Заполнения», мазок не сжимается), поверх — тот же мазок слабым светом.
        /// </summary>
        static RectTransform ExperienceBar(RectTransform content)
        {
            const float length = 350f;
            RectTransform xp = TopLeft(Node("Опыт", content), 218f, 106f, length, 14f);
            Stretch(UiInkKit.StrokeLayer(xp, "Дорожка", "brush_stroke_1", Role.Smoke, 1f).rectTransform, -4f);
            RectTransform fill = Node("Заполнение", xp);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(.3f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<RectMask2D>();
            RectTransform stroke = Node("Мазок", fill);
            stroke.anchorMin = Vector2.zero;
            stroke.anchorMax = new Vector2(0f, 1f);
            stroke.pivot = new Vector2(0f, .5f);
            stroke.offsetMin = new Vector2(0f, -2f);
            stroke.offsetMax = new Vector2(length, 2f);
            UiInkKit.StrokeLayer(stroke, "Краска", "brush_stroke_2", Role.Experience, 1f, .05f);
            Color light = T.Get(Role.Experience);
            light.a = .3f;
            UiInkKit.LightLayer(stroke, "Свет", "brush_stroke_2", 1f, 3f, new Vector2(0f, .5f), .1f).color = light;
            return fill;
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

        /// <summary>
        /// Заголовок группы или раздела: огонёк-ромб (ромб остаётся только мелким светом), подпись
        /// прописными и нить света от подписи до края.
        /// </summary>
        static RectTransform Header(RectTransform parent, string name, string text, float size, float spacing)
        {
            RectTransform header = Node(name, parent);
            UiInkKit.LightAt(header, "Ромб", "light_gem", new Vector2(0f, .5f), new Vector2(8f, 0f), new Vector2(12f, 13f), .9f, delay: .15f);
            string caption = text.ToUpperInvariant();
            TMP_Text label = UiInkKit.Label(header, "Надпись", caption, FontRole.Heading, size, Role.Text, spacing: spacing, delay: .08f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.offsetMin = new Vector2(22f, 0f);
            // Нить начинается сразу за подписью: ширина подписи мерится до раскладки.
            float start = 22f + label.GetPreferredValues(caption).x + 14f;
            RectTransform line = UiInkKit.Divider(header, "Линия", 100f, false, .35f);
            line.anchorMin = new Vector2(0f, .5f);
            line.anchorMax = new Vector2(1f, .5f);
            line.pivot = new Vector2(0f, .5f);
            line.offsetMin = new Vector2(start, -8f);
            line.offsetMax = new Vector2(-4f, 8f);
            return header;
        }

        static void StatRow(RectTransform content, CampTentView view, int i, float x, float y)
        {
            RectTransform row = TopLeft(Node("Стат " + StatNames[i], content), x, y, 264f, StatPitch - 2f);
            Image hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            // Наведение на строку — мягкое пятно света, без рамки.
            Image bar = UiInkKit.LightLayer(row, "Наведение", "soft_blot", .12f, 4f, delay: .2f);
            var motion = row.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = bar;
            motion.HoverScale = 1f;
            motion.PressScale = 1f;
            motion.PulseWhileHovered = false;
            motion.HoverSound = false;
            // Значки статов — белые силуэты, краска темы.
            Image icon = Mark(row, "Значок", view.StatIcons[i], Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(16f, 0f), 20f);
            icon.material = UiInkKit.Plain;
            UiInkKit.Inked(icon, delay: .12f);
            view.StatLabels[i] = Text(row, "Подпись", StatNames[i], 36f, 0f, 150f, StatPitch - 2f, FontRole.Body, 17f, Role.TextMuted, delay: .15f);
            view.StatValues[i] = Text(row, "Число", "0", 170f, 0f, 86f, StatPitch - 2f, FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineRight, .2f);
            view.StatValues[i].fontStyle = FontStyles.Bold;
            view.StatRows[i] = row;
        }

        /// <summary>
        /// Зелье — круглый слот «Дыма и света» (UiInkKit.SlotOrb): клуб дыма, тёмный диск, слабое
        /// сияние цвета зелья внутри, тонкое кольцо без огня (владелец 26 сентября). Зелье в быстром
        /// слоте HUD — оранжевое толстое кольцо и свет выбора (WcSlotState.Selected, роль Accent).
        /// </summary>
        static void Potion(RectTransform content, CampTentView view, int i)
        {
            RectTransform tile = UiInkKit.SlotOrb(content, "Зелье " + (i + 1), 100f, i < 2 ? Role.Health : Role.Lavidium);
            TopLeft(tile, 30f + i * 138f, 704f, 100f, 100f);
            view.PotionIcons[i] = ItemImage(tile, "Бутылка", 12f);
            WcSlotState state = tile.GetComponent<WcSlotState>();
            state.SelectedRole = Role.Accent;
            state.Apply();
            view.PotionStates[i] = state;
            RectTransform count = TopLeft(Node("Запас", tile), 46f, 70f, 48f, 26f);
            view.PotionCounts[i] = UiInkKit.Label(count, "Надпись", "0", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.BottomRight, delay: .2f);
            view.PotionCounts[i].fontStyle = FontStyles.Bold;
            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.Find("Ловец").GetComponent<Image>();
            button.transition = Selectable.Transition.None;
            var motion = tile.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.04f;
            view.Potions[i] = button;
        }

        /// <summary>
        /// Картинка вещи спрайтом (вид палатки ставит Sprite, полёт вещи — тоже) вместо RawImage
        /// «Предмет» ячейки «Дыма и света»: на её месте среди детей — под рамкой, над светом редкости.
        /// Материал — как у «Предмета»: без течения и дымки, только проявление.
        /// </summary>
        static Image ItemImage(RectTransform slot, string name, float inset)
        {
            Transform old = slot.Find("Предмет");
            int index = old != null ? old.GetSiblingIndex() : slot.Find("Рамка").GetSiblingIndex();
            if (old != null) Object.DestroyImmediate(old.gameObject);
            RectTransform rect = Stretch(Node(name, slot), inset);
            rect.SetSiblingIndex(index);
            var image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.material = UiInkKit.Art;
            UiInkKit.Inked(image, delay: .12f);
            return image;
        }

        /// <summary>
        /// Надетое — круглый слот (UiInkKit.SlotOrb), под ним подпись на мягком клубе дыма вместо
        /// ярлыка пака. Пустой слот показывает бледный значок своего места.
        /// </summary>
        static CampTentCell WornSlot(RectTransform parent, string caption, float x, float y)
        {
            RectTransform rect = UiInkKit.SlotOrb(parent, "Слот " + caption, 112f);
            TopLeft(rect, x, y, 112f, 112f);
            CampTentCell cell = TentCell(rect, 20f, 18f, true);
            cell.Round = true;
            cell.Placeholder = Stretch(Node("Пустой слот", rect), 30f).gameObject.AddComponent<Image>();
            cell.Placeholder.color = new Color(1f, 1f, 1f, .16f);
            cell.Placeholder.preserveAspect = true;
            cell.Placeholder.raycastTarget = false;
            cell.Placeholder.material = UiInkKit.Art;
            UiInkKit.Inked(cell.Placeholder, delay: .12f);
            cell.Placeholder.enabled = false;
            cell.Placeholder.transform.SetSiblingIndex(cell.Icon.transform.GetSiblingIndex());

            RectTransform tag = Node("Подпись", rect);
            tag.anchorMin = tag.anchorMax = new Vector2(.5f, 0f);
            tag.pivot = new Vector2(.5f, .5f);
            tag.anchoredPosition = new Vector2(0f, -8f);
            tag.sizeDelta = new Vector2(112f, 28f);
            UiInkKit.SmokeLayer(tag, "Дым", "soft_blot", .95f, 16f, 8f, deep: true);
            TMP_Text label = UiInkKit.Label(tag, "Надпись", caption, FontRole.Body, 15f, Role.Text, TextAlignmentOptions.Center, delay: .15f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return cell;
        }

        /// <summary>
        /// Детали палатки поверх ячейки «Дыма и света» (UiInkKit.Cell или SlotOrb): вещь спрайтом,
        /// вспышка цвета редкости при надевании, камень редкости — малый светящийся шарик на верхней
        /// кромке (ромбов-камней больше нет: ромб остаётся только мелким светом), уровень, замок
        /// «беречь», блик уникальной. Одна рамка и свет редкости — у WcSlotState ячейки (вариант А
        /// владельца 23 сентября: наведение и выбор меняют ту же рамку).
        /// </summary>
        static CampTentCell TentCell(RectTransform rect, float iconInset, float gemSize, bool round)
        {
            var cell = rect.gameObject.AddComponent<CampTentCell>();
            // Редкость ставит CampTentCell прямо в WcSlotState. WcRarity ячейки (она для лавок) при
            // каждом включении сбрасывала бы редкость на «обычную» до первого обновления вида.
            var rarity = rect.GetComponent<WcRarity>();
            if (rarity != null) Object.DestroyImmediate(rarity);
            cell.State = rect.GetComponent<WcSlotState>();
            cell.Frame = rect.Find("Рамка").GetComponent<Image>();
            cell.Icon = ItemImage(rect, "Вещь", iconInset);
            cell.Icon.enabled = false;
            // Вспышка при надевании — свет цвета редкости чуть шире ячейки, за вещью.
            cell.RarityGlow = UiInkKit.LightLayer(rect, "Свет редкости", "soft_blot", 0f, rect.sizeDelta.x * .08f, delay: .2f);
            cell.RarityGlow.enabled = false;
            cell.RarityGlow.transform.SetSiblingIndex(cell.Icon.transform.GetSiblingIndex());
            cell.Group = rect.gameObject.AddComponent<CanvasGroup>();

            RectTransform gem = At(Node("Камень", rect), new Vector2(.5f, 1f), Vector2.zero, new Vector2(gemSize, gemSize));
            cell.GemGlow = UiInkKit.LightLayer(gem, "Сияние", "soft_blot", 1f, gemSize * .4f, delay: .2f);
            cell.GemGlow.enabled = false;
            RectTransform core = At(Node("Ядро", gem), new Vector2(.5f, .5f), Vector2.zero, new Vector2(gemSize * .5f, gemSize * .5f));
            cell.Gem = core.gameObject.AddComponent<Image>();
            cell.Gem.sprite = T.CircleFill;
            cell.Gem.raycastTarget = false;
            cell.Gem.material = UiInkKit.Plain;
            UiInkKit.Inked(cell.Gem, delay: .2f);
            cell.Gem.enabled = false;

            RectTransform level = Node("Уровень", rect);
            level.anchorMin = level.anchorMax = level.pivot = new Vector2(1f, 0f);
            // У круглого слота угол пуст — число садится на кольцо справа снизу.
            level.anchoredPosition = round ? new Vector2(-4f, 14f) : new Vector2(-6f, 3f);
            level.sizeDelta = new Vector2(46f, 22f);
            cell.Level = UiInkKit.Label(level, "Надпись", "", FontRole.Body, 15f, Role.Text, TextAlignmentOptions.BottomRight, delay: .2f);
            cell.Level.fontStyle = FontStyles.Bold;
            // Замок — белый знак (Kit/Icons/lock), краска темы.
            Image lockIcon = Mark(rect, "Беречь", CombatHudBuilder.Icon("lock"), Role.Text, .9f, new Vector2(1f, 1f), new Vector2(-14f, -14f), 20f);
            lockIcon.material = UiInkKit.Plain;
            UiInkKit.Inked(lockIcon, delay: .2f);
            lockIcon.gameObject.SetActive(false);
            cell.Lock = lockIcon.gameObject;

            // Блик уникальной — мягкая полоса света под маской ячейки (у круглого слота маска — круг).
            RectTransform lane = Stretch(Node("Дорожка блика", rect), 3f);
            if (round)
            {
                var mask = lane.gameObject.AddComponent<Image>();
                mask.sprite = T.CircleFill;
                mask.raycastTarget = false;
                lane.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            }
            else lane.gameObject.AddComponent<RectMask2D>();
            Image shine = UiInkKit.LightAt(lane, "Блик", "soft_blot", new Vector2(.5f, .5f), Vector2.zero, new Vector2(30f, 160f), .35f, delay: .2f);
            shine.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);
            shine.gameObject.SetActive(false);
            cell.Shine = shine.rectTransform;

            var motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.HoverScale = 1.05f;
            motion.PressScale = .97f;
            return cell;
        }

        // ---------------------------------------------------------------- сумка и атлас
        static void BuildBag(RectTransform board, CampTentView view)
        {
            const float w = 1084f;
            view.BagPanel = InkPanel(board, "Сумка", 740f, 100f, w, 860f, out RectTransform content);

            RectTransform close = UiInkKit.CloseButton(content, "Закрыть", 52f);
            TopLeft(close, w - 70f, 18f, 52f, 52f);
            view.Close = close.GetComponent<Button>();

            view.BagTab = InkTab(content, "Вкладка сумка", "Сумка", w * .5f - 250f, 24f, 240f, 28f);
            view.AtlasTab = InkTab(content, "Вкладка атлас", "Атлас", w * .5f + 10f, 24f, 240f, 28f);
            TopLeft(UiInkKit.Divider(content, "Линия", 660f), w * .5f - 330f, 82f, 660f, 16f);

            RectTransform bag = Stretch(Node("Страница сумки", content));
            view.BagPage = bag.gameObject;
            string[] filters = { "Все", "Оружие", "Броня", "Кольца", "Талисманы" };
            for (int i = 0; i < filters.Length; i++)
                view.Filters[i] = InkTab(bag, "Фильтр " + i, filters[i], 64f + i * 156f, 106f, 150f, 20f);
            view.BagCount = Text(bag, "Занято", "0 / 48", 870f, 114f, 150f, 34f, FontRole.Body, 19f, Role.TextMuted, TextAlignmentOptions.MidlineRight);

            RectTransform grid = TopLeft(Node("Сетка", bag), w * .5f - 423f, 172f, 846f, 632f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(96f, 96f);
            layout.spacing = new Vector2(10f, 11f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            view.BagGrid = grid;
            // Шаблон клонирует CampInventoryView: после клонов он пересобирает группы проявления.
            RectTransform template = UiInkKit.Cell(grid, "Ячейка сумки", 96f);
            view.BagTemplate = TentCell(template, 10f, 18f, false);
            template.gameObject.SetActive(false);

            RectTransform atlas = Stretch(Node("Страница атласа", content));
            view.AtlasPage = atlas.gameObject;
            view.AtlasCount = Text(atlas, "Найдено", "", 0f, 110f, w, 36f, FontRole.Body, 20f, Role.TextMuted, TextAlignmentOptions.Center);
            RectTransform atlasGrid = TopLeft(Node("Сетка атласа", atlas), w * .5f - 402f, 166f, 804f, 660f);
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

        /// <summary>Запись атласа: плитка — ячейка «Дыма и света» (рамка и свет редкости — WcSlotState), под ней название.</summary>
        static CampAtlasEntry AtlasEntry(Transform grid)
        {
            RectTransform rect = Node("Запись атласа", grid);
            rect.sizeDelta = new Vector2(186f, 158f);
            UiInkKit.HitArea(rect);
            var entry = rect.gameObject.AddComponent<CampAtlasEntry>();
            entry.Silhouette = new Color(.02f, .03f, .06f, .9f);
            RectTransform tile = UiInkKit.Cell(rect, "Плитка", 104f);
            At(tile, new Vector2(.5f, 1f), new Vector2(0f, -54f), new Vector2(104f, 104f));
            var rarity = tile.GetComponent<WcRarity>();
            if (rarity != null) Object.DestroyImmediate(rarity);
            entry.State = tile.GetComponent<WcSlotState>();
            entry.Frame = tile.Find("Рамка").GetComponent<Image>();
            entry.Icon = ItemImage(tile, "Вещь", 12f);
            RectTransform name = Node("Название", rect);
            name.anchorMin = name.anchorMax = name.pivot = new Vector2(.5f, 0f);
            name.anchoredPosition = Vector2.zero;
            name.sizeDelta = new Vector2(186f, 44f);
            entry.Name = UiInkKit.Label(name, "Надпись", "", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center, delay: .15f);
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
            // Подложка — глубокий дым всплывашки, как у подсказок боевого HUD; слои вне раскладки.
            UiInkKit.Plate(card);
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
            // Карточка всплывает на каждом наведении: чернила без огня — тлеющая кромка на
            // всплывашке читалась красной вспышкой (владелец 25 сентября). Заново проявляет CampTentView.
            UiInkGroup appear = UiInkKit.Group(card, UiInkGroup.Sweep.TopToBottom, .3f, .12f);
            appear.Burn = 0f;
            appear.HideDuration = .12f;
            view.TooltipInk = appear;

            RectTransform head = Node("Шапка", card);
            var row = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            // Картинка — в малой ячейке «Дыма и света»: рамка и свет цвета редкости (CampTentView.SetItemFrame).
            // «Рамка» (ItemFrame) — прямой ребёнок «Вещи»: CampInventoryView прячет картинку через её родителя.
            RectTransform frame = UiInkKit.Cell(head, "Вещь", 96f, true);
            var frameSize = frame.gameObject.AddComponent<LayoutElement>();
            frameSize.preferredWidth = frameSize.preferredHeight = frameSize.minWidth = frameSize.minHeight = 96f;
            var rarity = frame.GetComponent<WcRarity>();
            if (rarity != null) Object.DestroyImmediate(rarity);
            // Карточка мышь не ловит (CanvasGroup), ловец ячейки ей не нужен.
            frame.Find("Ловец").GetComponent<Image>().raycastTarget = false;
            view.ItemArt = ItemImage(frame, "Картинка", 10f);
            view.ItemArt.enabled = false;
            view.ItemFrame = frame.Find("Рамка").GetComponent<Image>();
            view.ItemState = frame.GetComponent<WcSlotState>();

            RectTransform names = Node("Подписи", head);
            var namesColumn = names.gameObject.AddComponent<VerticalLayoutGroup>();
            namesColumn.spacing = 4f;
            namesColumn.childControlWidth = namesColumn.childControlHeight = true;
            namesColumn.childForceExpandWidth = true;
            namesColumn.childForceExpandHeight = false;
            names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            view.ItemTitle = FlowText(names, "Название", FontRole.Heading, 28f, Role.Text, .05f);
            view.ItemRarity = FlowText(names, "Редкость", FontRole.Body, 19f, Role.Text, .1f);
            view.ItemKind = FlowText(names, "Вид", FontRole.Body, 17f, Role.TextMuted, .12f);

            RectTransform line = UiInkKit.Divider(card, "Линия", 400f, false, .5f);
            var lineSize = line.gameObject.AddComponent<LayoutElement>();
            lineSize.preferredHeight = lineSize.minHeight = 16f;
            view.ItemStats = FlowText(card, "Свойства", FontRole.Body, 19f, Role.Text, .12f);
            view.ItemStats.lineSpacing = 6f;
            UiInkKit.Revealed(view.ItemStats, .12f).Softness = .6f;
        }

        /// <summary>Текст внутри раскладки: TMP на самом узле, высота — по содержимому; проявляется по буквам.</summary>
        static TMP_Text FlowText(RectTransform parent, string name, FontRole font, float size, Role role, float delay)
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
            UiInkKit.Revealed(label, delay);
            return label;
        }
    }
}
