using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Элементы боевого HUD пака «Ночная акварель» по листу
    /// ART/UI/concepts-2026-09-22/final-2/kit-sheet-4-hud.png: портрет с уровнем,
    /// полосы здоровья и лавидия, ячейка умения в четырёх состояниях, ячейка зелья,
    /// бафф с таймером, полоса босса, полоса врага, цифры урона, миникарта,
    /// подсказка взаимодействия.
    /// </summary>
    public static partial class UiKitBuilder
    {
        const string MapSamplePath = UiKitImport.KitRoot + "/Watercolor/wc_map_sample.png";

        static void EnsureHudPrefabs()
        {
            Save("Portrait", () => Portrait(null, "Portrait", "5"));
            Save("AbilitySlot", () => AbilitySlot(null, "AbilitySlot", null, "Q"));
            Save("PotionSlot", () => PotionSlot(null, "PotionSlot", null, "2", "F"));
            Save("Buff", () => Buff(null, "Buff", "stat_attack_speed", Role.Rare, .7f, "8с"));
            Save("BossBar", () => BossBar(null, "BossBar", "Лесной страж", .62f));
            Save("EnemyBar", () => EnemyBar(null, "EnemyBar", .7f, false));
            Save("EnemyBarElite", () => EnemyBar(null, "EnemyBarElite", .5f, true));
            Save("DamageNumber", () => DamageNumber(null, "DamageNumber", "-18", false));
            Save("DamageNumberCrit", () => DamageNumber(null, "DamageNumberCrit", "-62", true));
            Save("Minimap", () => Minimap(null, "Minimap"));
            Save("InteractPrompt", () => InteractPrompt(null, "InteractPrompt", "E", "Открыть"));
        }

        static Texture Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        /// <summary>Клавиша: плашка пака и буква рубленым шрифтом (как на листе).</summary>
        public static RectTransform Keycap(Transform parent, string name, string key, float size = 34f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Layer(root, "Заливка", t.FillSmall, Role.Panel, .95f);
            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight);
            Layer(root, "Рамка", t.FrameSmall, Role.PanelLine, .8f);
            Label(root, "Буква", key, FontRole.Body, size * .56f, Role.Text, TextAlignmentOptions.Center);
            return root;
        }

        /// <summary>
        /// Портрет героя: вырез на тёмном диске с тёплой подсветкой за плечом, тонкое
        /// серебряное кольцо, ромб уровня на кольце справа снизу.
        /// </summary>
        public static RectTransform Portrait(Transform parent, string name, string level, float size = 200f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Image shadow = Layer(root, "Тень", RoundShadow, Role.Veil, .85f, 16f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Image disk = Layer(root, "Диск", t.CircleFill, Role.Panel);
            disk.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // Рисованный портрет (23 сентября, по листу HUD): свой тёмный фон и тёплая
            // подсветка за плечом уже в картинке — вписывается в диск целиком.
            RectTransform art = Stretch(Node("Портрет", disk.rectTransform));
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = Tex("Assets/Resources/UI/HUD/PelagPortraitPainted.png");
            raw.raycastTarget = false;
            Layer(disk.rectTransform, "Затемнение края", t.VeilRadial, Role.Veil, .44f);
            Layer(root, "Кольцо", t.CircleFrameLarge, Role.PanelLine);

            RectTransform badge = Place("LevelBadge", root, "Уровень");
            At(badge, new Vector2(1f, 0f), new Vector2(-size * .115f, size * .135f), new Vector2(size * .27f, size * .27f));
            // На портрете ромб серебряный и тёмный, без бирюзы: бирюза в паке — только редкость.
            badge.Find("Свечение").gameObject.SetActive(false);
            badge.Find("Рамка внутри").gameObject.SetActive(false);
            badge.Find("Заливка").GetComponent<ThemeColor>().SetRole(Role.Panel, 1f);
            badge.Find("Рамка").GetComponent<ThemeColor>().SetRole(Role.PanelLine, 1f);
            var number = badge.Find("Число").GetComponent<TMP_Text>();
            number.text = level;
            number.fontSize = size * .15f;
            var font = number.GetComponent<ThemeFont>();
            font.Role = FontRole.Body;
            font.Apply();
            return root;
        }

        /// <summary>
        /// Ячейка умения: свечение и рамка по состоянию, значок, перезарядка (значок гаснет,
        /// вокруг секунд кольцо делений), камень «нет лавидия» на углу.
        /// </summary>
        public static RectTransform AbilitySlot(Transform parent, string name, Texture icon, string key, float size = 104f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Image glow = Layer(root, "Свечение", t.GlowSmall, Role.Rare, .45f, 24f);
            Layer(root, "Заливка", t.FillSmall, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSmall, Role.Veil, .5f);
            RectTransform art = Stretch(Node("Значок", root), 5f);
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;

            RectTransform cooldown = Stretch(Node("Перезарядка", root), size * .14f);
            Layer(cooldown, "Свет", t.Blob, Role.Text, .07f, size * .1f);
            Layer(cooldown, "Деления", t.RingTicks, Role.Text, .16f);
            Image progress = Layer(cooldown, "Прошло", t.RingTicks, Role.Text, .95f);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = (int)Image.Origin360.Top;
            progress.fillClockwise = true;
            TMP_Text seconds = Label(cooldown, "Секунды", "4,8", FontRole.Body, size * .25f, Role.Text, TextAlignmentOptions.Center);

            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight, .8f);
            Image frame = Layer(root, "Рамка", t.FrameBoldSmall, Role.Rare);
            Image mark = Mark(root, "Нет лавидия", t.Gem, Role.Lavidium, 1f, new Vector2(1f, 1f), new Vector2(-3f, -3f), 26f);

            RectTransform cap = Keycap(root, "Клавиша", key, 40f);
            cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
            cap.pivot = new Vector2(.5f, 1f);
            cap.anchoredPosition = new Vector2(0f, -14f);

            var slot = root.gameObject.AddComponent<WcAbilitySlot>();
            slot.Frame = frame.GetComponent<ThemeColor>();
            slot.Glow = glow.GetComponent<ThemeColor>();
            slot.Icon = raw;
            slot.CooldownRing = cooldown.gameObject;
            slot.CooldownShade = progress;
            slot.CooldownText = seconds;
            slot.NoLavidiumMark = mark.gameObject;
            slot.Set(WcAbilitySlot.State.Ready);
            return root;
        }

        /// <summary>Ячейка зелья: бутылка, количество, клавиша.</summary>
        public static RectTransform PotionSlot(Transform parent, string name, Texture icon, string count, string key, float size = 104f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Layer(root, "Заливка", t.FillSmall, Role.Panel);
            Layer(root, "Тень снизу", t.ShadeSmall, Role.Veil, .5f);
            RectTransform art = Stretch(Node("Бутылка", root), 12f);
            var raw = art.gameObject.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
            raw.enabled = icon != null;
            Layer(root, "Свет по кромке", t.HighlightSmall, Role.Highlight, .8f);
            Layer(root, "Рамка", t.FrameSmall, Role.PanelLine);
            RectTransform countBox = Node("Количество", root);
            countBox.anchorMin = countBox.anchorMax = new Vector2(1f, 0f);
            countBox.pivot = new Vector2(1f, 0f);
            countBox.anchoredPosition = new Vector2(-8f, 4f);
            countBox.sizeDelta = new Vector2(50f, 28f);
            TMP_Text label = Label(countBox, "Надпись", "×" + count, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.BottomRight);
            label.fontStyle = FontStyles.Bold;
            RectTransform cap = Keycap(root, "Клавиша", key, 40f);
            cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
            cap.pivot = new Vector2(.5f, 1f);
            cap.anchoredPosition = new Vector2(0f, -14f);
            return root;
        }

        /// <summary>
        /// Бафф: тёмный диск со светящимся значком, серое кольцо и поверх — кольцо
        /// оставшегося времени в цвете эффекта со свечением; секунды под ним.
        /// </summary>
        public static RectTransform Buff(Transform parent, string name, string icon, Role color, float left, string seconds, float size = 88f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Layer(root, "Диск", t.CircleFill, Role.Panel);
            Mark(root, "Свечение", t.Blob, color, .38f, new Vector2(.5f, .5f), Vector2.zero, size * .9f);
            Mark(root, "Значок", CombatHudBuilder.Icon(icon), color, 1f, new Vector2(.5f, .5f), Vector2.zero, size * .56f);
            Layer(root, "Кольцо", t.CircleRing, Role.TextMuted, .3f);
            Image ringGlow = Layer(root, "Свечение кольца", t.RingGlow, color, .35f, size * .25f);
            Image timer = Layer(root, "Время", t.CircleRing, color);
            foreach (Image ring in new[] { ringGlow, timer })
            {
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillAmount = left;
            }
            RectTransform labelBox = Node("Секунды", root);
            labelBox.anchorMin = new Vector2(0f, 0f);
            labelBox.anchorMax = new Vector2(1f, 0f);
            labelBox.pivot = new Vector2(.5f, 1f);
            labelBox.anchoredPosition = new Vector2(0f, -10f);
            labelBox.sizeDelta = new Vector2(0f, 28f);
            Label(labelBox, "Надпись", seconds, FontRole.Body, 21f, Role.Text, TextAlignmentOptions.Center);
            return root;
        }

        /// <summary>
        /// Полоса босса: рамка, верхнюю кромку которой разрывает имя антиквой; внутри —
        /// крупная полоса с гранёными ромбами на концах.
        /// </summary>
        public static RectTransform BossBar(Transform parent, string name, string boss, float value, float w = 680f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 104f);
            RectTransform box = Node("Рамка", root);
            box.anchorMin = Vector2.zero;
            box.anchorMax = Vector2.one;
            box.offsetMin = Vector2.zero;
            box.offsetMax = new Vector2(0f, -22f);
            Layer(box, "Заливка", t.Fill, Role.Panel, .55f);
            Layer(box, "Контур", t.FrameOpenTop, Role.PanelLine, .9f);

            // Верхняя кромка: от углов рамки (18 единиц — угол спрайта) до имени.
            RectTransform head = Node("Имя", box);
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(.5f, .5f);
            head.sizeDelta = new Vector2(0f, 44f);
            head.anchoredPosition = new Vector2(0f, -.75f);
            var row = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 18, 0, 0);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 22f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            void Line(string part)
            {
                RectTransform lineBox = Node(part, head);
                var le = lineBox.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.preferredHeight = 44f;
                RectTransform line = Node("Черта", lineBox);
                line.anchorMin = new Vector2(0f, .5f);
                line.anchorMax = new Vector2(1f, .5f);
                line.offsetMin = new Vector2(0f, -.75f);
                line.offsetMax = new Vector2(0f, .75f);
                var img = line.gameObject.AddComponent<Image>();
                img.sprite = t.Pixel;
                img.raycastTarget = false;
                Tint(img, Role.PanelLine, .85f);
            }
            Line("Кромка слева");
            RectTransform labelRect = Node("Надпись", head);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = boss;
            label.fontSize = 38f;
            label.characterSpacing = 2f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            labelRect.gameObject.AddComponent<ThemeFont>().Role = FontRole.Heading;
            labelRect.GetComponent<ThemeFont>().Apply();
            Tint(label, Role.Text);
            Line("Кромка справа");

            RectTransform bar = Place("BarLarge", box, "Здоровье");
            bar.anchorMin = new Vector2(0f, .5f);
            bar.anchorMax = new Vector2(1f, .5f);
            bar.anchoredPosition = new Vector2(0f, -3f);
            bar.sizeDelta = new Vector2(-96f, 26f);
            bar.GetComponent<WcBar>().Set(value);
            Mark(box, "Ромб слева", t.DiamondLarge, Role.PanelLine, 1f, new Vector2(0f, .5f), new Vector2(29f, -3f), 22f);
            Mark(box, "Ромб справа", t.DiamondLarge, Role.PanelLine, 1f, new Vector2(1f, .5f), new Vector2(-29f, -3f), 22f);
            return root;
        }

        /// <summary>Полоса над врагом: тонкая капсула; у элиты на конце заполнения — светлый ромб в красной оправе.</summary>
        public static RectTransform EnemyBar(Transform parent, string name, float value, bool elite, float w = 200f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 12f);
            RectTransform bar = Place("Bar", root, "Здоровье");
            Stretch(bar);
            bar.GetComponent<WcBar>().Set(value);
            if (elite)
            {
                // Ромб — ребёнок заполнения: сам едет за значением.
                var fill = (RectTransform)bar.Find("Заполнение");
                RectTransform gem = At(Node("Элита", fill), new Vector2(1f, .5f), Vector2.zero, new Vector2(24f, 24f));
                Layer(gem, "Свечение", t.Blob, Role.Health, .6f, 10f);
                Layer(gem, "Заливка", t.DiamondFill, Role.Text);
                Layer(gem, "Оправа", t.DiamondFrameSmall, Role.Health);
            }
            return root;
        }

        /// <summary>Цифра урона: обычная — светлая, критическая — крупнее, оранжевая, со свечением и искрой.</summary>
        public static RectTransform DamageNumber(Transform parent, string name, string text, bool crit)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, crit ? 190f : 150f, crit ? 96f : 80f);
            if (crit)
            {
                Image glow = Mark(root, "Свечение", t.Blob, Role.Lavidium, .2f, new Vector2(.5f, .5f), new Vector2(0f, -2f), 60f);
                glow.preserveAspect = false;
                glow.rectTransform.sizeDelta = new Vector2(150f, 74f);
            }
            TMP_Text label = Label(root, "Число", text, FontRole.Body, crit ? 68f : 52f, crit ? Role.Lavidium : Role.Text, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            if (crit)
            {
                label.fontStyle = FontStyles.Bold;
                // Сверху светлее, книзу гуще: цвет темы умножается на градиент.
                label.enableVertexGradient = true;
                label.colorGradient = new VertexGradient(Color.white, Color.white, new Color(1f, .78f, .72f), new Color(1f, .78f, .72f));
                Image spark = Mark(root, "Искра", t.Spark, Role.Lavidium, 1f, new Vector2(1f, 1f), new Vector2(-26f, -12f), 46f);
                Mark(spark.rectTransform, "Сердце искры", t.Blob, Role.Text, .8f, new Vector2(.5f, .5f), Vector2.zero, 16f);
            }
            return root;
        }

        /// <summary>
        /// Миникарта: квадратная серебряная рамка с полыми ромбами по углам, местность,
        /// стрелка героя, выход — камень редкости в пунктирной зоне. Метки врагов кладёт код.
        /// </summary>
        public static RectTransform Minimap(Transform parent, string name, float size = 290f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, size, size);
            Image shadow = Layer(root, "Тень", t.Glow, Role.Veil, .6f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            Layer(root, "Заливка", t.Pixel, Role.Panel, .95f);
            RectTransform map = Stretch(Node("Карта", root), 1.5f);
            var terrain = map.gameObject.AddComponent<RawImage>();
            terrain.texture = Tex(MapSamplePath);
            terrain.raycastTarget = false;
            Layer(map, "Затемнение края", t.VeilRadial, Role.Veil, .36f);
            void Edge(string part, Vector2 min, Vector2 max, Vector2 size2)
            {
                RectTransform edge = Node(part, root);
                edge.anchorMin = min;
                edge.anchorMax = max;
                edge.pivot = new Vector2(.5f, .5f);
                edge.anchoredPosition = Vector2.zero;
                edge.sizeDelta = size2;
                var img = edge.gameObject.AddComponent<Image>();
                img.sprite = t.Pixel;
                img.raycastTarget = false;
                Tint(img, Role.PanelLine, .85f);
            }
            Edge("Кромка сверху", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1.5f));
            Edge("Кромка снизу", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1.5f));
            Edge("Кромка слева", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1.5f, 0f));
            Edge("Кромка справа", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1.5f, 0f));
            foreach (var corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
            {
                RectTransform gem = At(Node("Ромб угла", root), corner, Vector2.zero, new Vector2(16f, 16f));
                Layer(gem, "Заливка", t.DiamondFill, Role.Panel);
                Layer(gem, "Оправа", t.DiamondFrameSmall, Role.PanelLine);
            }
            RectTransform exit = At(Node("Выход", map), new Vector2(.86f, .86f), Vector2.zero, new Vector2(58f, 58f));
            Layer(exit, "Зона", t.FrameDashed, Role.TextMuted, .55f);
            Mark(exit, "Свечение", t.Blob, Role.Rare, .35f, new Vector2(.5f, .5f), Vector2.zero, 44f);
            Mark(exit, "Камень", t.Gem, Role.Rare, 1f, new Vector2(.5f, .5f), Vector2.zero, 22f);
            Image player = Mark(map, "Герой", t.Arrow, Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, 26f);
            player.rectTransform.localEulerAngles = new Vector3(0f, 0f, -24f);
            return root;
        }

        /// <summary>Метка врага на миникарте: красная точка с тёмной обводкой.</summary>
        public static void MinimapEnemy(RectTransform map, Vector2 at)
        {
            UiTheme t = Theme;
            RectTransform dot = At(Node("Враг", map), at, Vector2.zero, new Vector2(15f, 15f));
            Layer(dot, "Обводка", t.CircleFill, Role.Veil, .9f);
            Stretch(Layer(dot, "Точка", t.CircleFill, Role.Health).rectTransform, 2.5f);
        }

        /// <summary>
        /// Подсказка взаимодействия: плашка со скруглением окна, крупная клавиша, ромб,
        /// действие антиквой; по бокам указатели — линии, сходящиеся в ромб.
        /// </summary>
        public static RectTransform InteractPrompt(Transform parent, string name, string key, string action, float w = 280f)
        {
            UiTheme t = Theme;
            RectTransform root = Root(parent, name, w, 66f);
            Image shadow = Layer(root, "Тень", t.Glow, Role.Veil, .6f, 24f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            Layer(root, "Заливка", t.Fill, Role.Panel, .92f);
            Layer(root, "Свет по кромке", t.HighlightSprite, Role.Highlight);
            Layer(root, "Рамка", t.Frame, Role.PanelLine, .9f);
            RectTransform cap = Keycap(root, "Клавиша", key, 46f);
            cap.anchorMin = cap.anchorMax = new Vector2(0f, .5f);
            cap.pivot = new Vector2(0f, .5f);
            cap.anchoredPosition = new Vector2(12f, 0f);
            Mark(root, "Ромб", t.DiamondSmall, Role.PanelLine, 1f, new Vector2(0f, .5f), new Vector2(80f, 0f), 10f);
            TMP_Text label = Label(root, "Действие", action, FontRole.Heading, 28f, Role.Text);
            label.rectTransform.offsetMin = new Vector2(98f, 0f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            foreach (bool right in new[] { false, true })
            {
                Image pointer = Mark(root, right ? "Указатель справа" : "Указатель слева", t.Pointer, Role.PanelLine, 1f,
                    new Vector2(right ? 1f : 0f, .5f), new Vector2(right ? 17f : -17f, 0f), 30f);
                pointer.preserveAspect = false;
                pointer.rectTransform.sizeDelta = new Vector2(22f, 30f);
                if (!right) pointer.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            }
            return root;
        }
    }
}
