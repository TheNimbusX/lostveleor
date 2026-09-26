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
    public static partial class CombatHudWcBuilder
    {
        // ---------------------------------------------------------------- зелья
        static void BuildPotions(RectTransform root, CombatHudView view)
        {
            // Вариант B: два зелья за разделителем, размером со способность (владелец: «мелковаты»).
            // Бутылку по выбранному размеру ставит CombatHudView (Resources/UI/Items/potion_*).
            RectTransform panel = Box(Node("Зелья", root), BottomCenter, Vector2.zero, new Vector2(PotionsX, RowBottom),
                new Vector2(PotionPitch + Slot, Slot));
            view.PotionPanel = panel;
            string[] art = { "potion_health_small", "potion_lavidium_small" };
            for (int i = 0; i < 2; i++)
            {
                // Имена частей — те, что ищет CombatHudView.Potions: «Art», «Count», «Key».
                RectTransform tileRect = Box(Node(i == 0 ? "Health Potion" : "Lavidium Potion", panel), Vector2.zero, Vector2.zero,
                    new Vector2(i * PotionPitch, 0f), new Vector2(Slot, Slot));
                // «Дым и свет»: банка в чернильной кляксе и слабый свет цвета зелья изнутри. Огненного
                // кольца у зелий нет (владелец 26 сентября: «огня слишком много») — огонь только у способностей.
                UiInkKit.SmokeLayer(tileRect, "Клякса", "smoke_ring", 1f, Slot * .2f, Slot * .2f);
                Layer(tileRect, "Заливка", T.CircleFill, Role.Panel, .9f, -2f);
                Image halo = Mark(tileRect, "Свет", T.Blob, i == 0 ? Role.Health : Role.Lavidium, .18f, new Vector2(.5f, .5f), Vector2.zero, Slot);
                halo.preserveAspect = false;
                var raw = Stretch(Node("Art", tileRect), 6f).gameObject.AddComponent<RawImage>();
                raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + art[i] + ".png");
                raw.raycastTarget = false;
                if (i == 0)
                {
                    // Здоровья мало, а Живица есть — банка мягко дышит светом (HudPulse, CombatHudView).
                    Image call = Mark(tileRect, "Пора пить", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, Slot * 1.7f);
                    Additive(call, new Color(1f, .42f, .3f, 0f));
                    call.enabled = false;
                    var pulse = call.gameObject.AddComponent<HudPulse>();
                    pulse.Target = call;
                    pulse.Min = .18f;
                    pulse.Max = .55f;
                    pulse.Period = 1.3f;
                    view.HealthPotionPulse = pulse;
                }
                RectTransform count = Box(Node("Count", tileRect), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-5f, 12f), new Vector2(40f, 24f));
                TMP_Text countLabel = LabelOn(count, "0", FontRole.Body, 19f, Role.Text, TextAlignmentOptions.BottomRight);
                countLabel.fontStyle = FontStyles.Bold;
                Shadowed(countLabel);
                UiInkKit.Revealed(countLabel, .25f);
                // Клавиша — кружком на нижней кромке, как у способностей.
                RectTransform cap = UiInkKit.Keycap(tileRect, "Клавиша", "5", 26f);
                cap.anchorMin = cap.anchorMax = new Vector2(.5f, 0f);
                cap.pivot = new Vector2(.5f, .5f);
                cap.anchoredPosition = new Vector2(0f, -4f);
            }

            // Подсказка зелья: малая подложка «Дыма и света», высота по тексту.
            RectTransform tip = Box(Node("Подсказка зелья", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 200f), new Vector2(320f, 90f));
            UiInkKit.Plate(tip, small: true);
            UiInkKit.Group(tip, UiInkGroup.Sweep.FromCenter, .35f, .1f).Burn = 0f;
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(18, 18, 12, 12);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Текст", tip);
            view.PotionTooltipText = LabelOn(text, "Зелье здоровья · 10%\nЛКМ — выпить\nПКМ — сменить размер", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
            UiInkKit.Revealed(view.PotionTooltipText, .08f);
            view.PotionTooltip = tip;
            tip.gameObject.SetActive(false);
        }

        /// <summary>
        /// Надпись прямо на узле. Label пака кладёт текст дочерним объектом, а здесь
        /// текст нужен на самом узле: CombatHudView ищет «Count» и «Key» с TMP_Text,
        /// а раскладка подсказки берёт высоту строки у самого текста.
        /// </summary>
        static TMP_Text LabelOn(RectTransform rect, string text, FontRole font, float size, Role role, TextAlignmentOptions align)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            var themeFont = rect.gameObject.AddComponent<ThemeFont>();
            themeFont.Role = font;
            themeFont.Apply();
            Tint(label, role);
            return label;
        }

        // ---------------------------------------------------------------- подсказка способности
        /// <summary>
        /// Подсказка способности: подложка «Дыма и света» (UiInkKit.Plate), шапка (круглая иконка,
        /// название, клавиша), ряд точек усилений, описание, сетка параметров, блок под Alt и строка
        /// состояния. Высота по содержимому, низом над плиткой — ставит CombatHudView.
        /// </summary>
        static void BuildTooltip(RectTransform root, CombatHudView view)
        {
            RectTransform tip = Box(Node("Подсказка", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 180f), new Vector2(420f, 200f));
            UiInkKit.Plate(tip);
            // Подсказка всплывает на каждом наведении: чернила без огня — тлеющая кромка здесь
            // смотрелась красной вспышкой (владелец 25 сентября).
            UiInkKit.Group(tip, UiInkGroup.Sweep.TopToBottom, .38f, .16f).Burn = 0f;
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20, 20, 16, 18);
            column.spacing = 10f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.Tooltip = tip;

            // Шапка: иконка в круге (как на плитке), название антиквой, клавиша.
            RectTransform header = Node("Шапка", tip);
            var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Size(header, -1f, 56f);
            RectTransform cell = Node("Иконка", header);
            Size(cell, 56f, 56f);
            UiInkKit.SmokeLayer(cell, "Клякса", "smoke_ring", 1f, 12f, 12f, deep: true);
            Layer(cell, "Заливка", T.CircleFill, Role.Panel);
            RectTransform mask = Stretch(Node("Маска", cell), 2f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = T.CircleFill;
            maskImage.type = Image.Type.Simple;
            maskImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            view.TooltipIcon = Stretch(Node("Картинка", mask)).gameObject.AddComponent<RawImage>();
            view.TooltipIcon.raycastTarget = false;
            // Тонкое тлеющее кольцо по кромке иконки — то же, что у плитки в покое.
            UiInkKit.LightLayer(cell, "Кольцо", "light_ring", .3f, 19f, delay: .15f);

            RectTransform title = Node("Название", header);
            Size(title, -1f, 40f).flexibleWidth = 1f;
            view.TooltipTitle = LabelOn(title, "Способность", FontRole.Heading, 26f, Role.Text, TextAlignmentOptions.MidlineLeft);
            UiInkKit.Revealed(view.TooltipTitle, .05f);
            view.TooltipTitle.characterSpacing = 1f;
            view.TooltipTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.TooltipTitle.enableAutoSizing = true;
            view.TooltipTitle.fontSizeMin = 16f;
            view.TooltipTitle.fontSizeMax = 26f;
            // Клавиша: круг у одной буквы, капсула у длинной подписи — ширину узла «Клавиша» подгоняет
            // CombatHudView (у кувырка «SPACE» не помещалась в квадрат 34 и переносилась: «SP/AC/E»).
            RectTransform keyBox = Node("Клавиша", header);
            Size(keyBox, 34f, 34f);
            RectTransform cap = UiInkKit.Keycap(keyBox, "Плашка", "Q", 34f);
            Stretch(cap);
            view.TooltipKey = cap.Find("Буква").GetComponent<TMP_Text>();

            // Усиления (концепт 1-gem-ingame): ряд из 8 точек, первые N горят, «Усилений: N из 8»;
            // справа — «Alt подробнее», пока Alt не зажат (26 сентября: ромбики стали точками).
            RectTransform upgrades = Node("Усиления", tip);
            var upgradeRow = upgrades.gameObject.AddComponent<HorizontalLayoutGroup>();
            upgradeRow.spacing = 5f;
            upgradeRow.childAlignment = TextAnchor.MiddleLeft;
            upgradeRow.childControlWidth = upgradeRow.childControlHeight = true;
            upgradeRow.childForceExpandWidth = upgradeRow.childForceExpandHeight = false;
            Size(upgrades, -1f, 22f);
            view.TooltipUpgradePips = new Image[Game.Sim.RunLoadout.MaxUpgrades];
            view.UpgradePipFilled = T.CircleFill;
            view.UpgradePipEmpty = T.CircleFill;
            for (int i = 0; i < view.TooltipUpgradePips.Length; i++)
            {
                RectTransform pipBox = Node("Точка " + (i + 1), upgrades);
                Size(pipBox, 11f, 11f);
                var pip = pipBox.gameObject.AddComponent<Image>();
                pip.sprite = T.CircleFill;
                pip.preserveAspect = true;
                pip.raycastTarget = false;
                pip.color = new Color(1f, 1f, 1f, .24f);
                view.TooltipUpgradePips[i] = pip;
            }
            RectTransform upgradeText = Node("Счёт", upgrades);
            Size(upgradeText, -1f, 22f).flexibleWidth = 1f;
            view.TooltipUpgradeText = LabelOn(upgradeText, "Усилений: 0 из 8", FontRole.Body, 15f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            view.TooltipUpgradeText.textWrappingMode = TextWrappingModes.NoWrap;
            view.TooltipDetailHint = DetailHint(upgrades).gameObject;
            view.TooltipUpgradeRow = upgrades.gameObject;

            RectTransform divider = UiInkKit.Divider(tip, "Разделитель", 380f, gem: false, strength: .45f);
            Size(divider, -1f, 16f);

            RectTransform body = Node("Описание", tip);
            view.TooltipBody = LabelOn(body, "Описание способности.", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.TopLeft);
            UiInkKit.Revealed(view.TooltipBody, .08f).Softness = .6f;
            view.TooltipBody.lineSpacing = 2f;

            // Параметры: сетка в три столбца, черта только между столбцами. Число, изменённое
            // усилениями, CombatHudView красит цветом «хорошо».
            RectTransform metrics = Node("Параметры", tip);
            var grid = metrics.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(126f, 32f);
            grid.spacing = new Vector2(0f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            view.TooltipMetricColumns = 3;
            view.TooltipMetrics = new HudTooltipMetric[6];
            for (int i = 0; i < 6; i++)
            {
                RectTransform item = Node("Параметр " + (i + 1), metrics);
                var metric = item.gameObject.AddComponent<HudTooltipMetric>();
                RectTransform sep = Box(Node("Черта", item), new Vector2(0f, .5f), new Vector2(0f, .5f), Vector2.zero, new Vector2(1.5f, 22f));
                var sepImage = sep.gameObject.AddComponent<Image>();
                sepImage.sprite = T.Pixel;
                Tint(sepImage, Role.PanelLine, .3f);
                metric.Separator = sep.gameObject;
                metric.Icon = Mark(item, "Значок", T.Pixel, Role.TextMuted, 1f, new Vector2(0f, .5f), new Vector2(24f, 0f), 22f);
                RectTransform value = Box(Node("Значение", item), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(42f, 0f), new Vector2(80f, 30f));
                metric.Value = Label(value, "Надпись", "0", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineLeft);
                metric.Value.textWrappingMode = TextWrappingModes.NoWrap;
                metric.Value.enableAutoSizing = true;
                metric.Value.fontSizeMin = 12f;
                metric.Value.fontSizeMax = 18f;
                view.TooltipMetrics[i] = metric;
            }

            BuildTooltipDetail(tip, view);

            // Состояние («Перезарядка 2,1 с»): CombatHudView включает и прячет сам текст.
            RectTransform status = Node("Состояние", tip);
            Size(status, -1f, 24f);
            view.TooltipStatus = LabelOn(status, "", FontRole.Body, 16f, Role.Bad, TextAlignmentOptions.MidlineLeft);
            status.gameObject.SetActive(false);

            // Хвостик — огонёк-ромб на нижней кромке, напротив плитки (ромб остался только мелким светом).
            RectTransform tail = Box(Node("Хвостик", tip), new Vector2(.5f, 0f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(22f, 24f));
            tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiInkKit.LightLayer(tail, "Огненный ромб", "light_gem", .95f, 4f, delay: .15f);
            view.TooltipTail = tail;
            tip.gameObject.SetActive(false);
        }

        /// <summary>«[Alt] подробнее» справа в строке усилений: клавиша-капсула и приглушённая подпись.</summary>
        static RectTransform DetailHint(RectTransform row)
        {
            RectTransform hint = Node("Подсказка Alt", row);
            var layout = hint.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            RectTransform cap = UiInkKit.Keycap(hint, "Клавиша", "Alt", 20f);
            Size(cap, cap.sizeDelta.x, cap.sizeDelta.y);
            RectTransform text = Node("Надпись", hint);
            TMP_Text label = LabelOn(text, "подробнее", FontRole.Body, 13f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            UiInkKit.Revealed(label, .15f);
            Size(text, label.GetPreferredValues("подробнее").x + 2f, 20f);
            Size(hint, -1f, 22f);
            hint.gameObject.SetActive(false);
            return hint;
        }

        /// <summary>
        /// Блок под Alt (владелец 26 сентября: «видеть, что на скилле уже висит поверх базы»): черта,
        /// «ВЗЯТО» и один текст — взятые усиления (имя жирным, описание приглушённо) и «было → стало»
        /// по числам, изменённым усилениями. Текст пишет CombatHudView. У блока своя группа
        /// появления: при нажатии Alt он проявляется, а не выскакивает; огня нет — это частая всплывашка.
        /// </summary>
        static void BuildTooltipDetail(RectTransform tip, CombatHudView view)
        {
            RectTransform detail = Node("Взято", tip);
            var column = detail.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 6f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            RectTransform line = UiInkKit.Divider(detail, "Черта", 380f, gem: false, strength: .4f);
            Size(line, -1f, 12f);
            RectTransform caption = Node("Заголовок", detail);
            Size(caption, -1f, 20f);
            TMP_Text captionText = LabelOn(caption, "ВЗЯТО", FontRole.Heading, 14f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            captionText.characterSpacing = 4f;
            UiInkKit.Revealed(captionText, .02f);
            RectTransform text = Node("Текст", detail);
            view.TooltipDetailText = LabelOn(text, "Усиление  Что оно даёт.", FontRole.Body, 15f, Role.Text, TextAlignmentOptions.TopLeft);
            view.TooltipDetailText.lineSpacing = 2f;
            view.TooltipDetailText.paragraphSpacing = 5f;
            UiInkKit.Revealed(view.TooltipDetailText, .06f).Softness = .6f;
            UiInkKit.Group(detail, UiInkGroup.Sweep.TopToBottom, .3f, .1f).Burn = 0f;
            view.TooltipDetail = detail.gameObject;
            detail.gameObject.SetActive(false);
        }

        static LayoutElement Size(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = element.minWidth = width;
            if (height >= 0f) element.preferredHeight = element.minHeight = height;
            return element;
        }

        // ---------------------------------------------------------------- отказ при нажатии
        // ---------------------------------------------------------------- новый уровень
        const float BannerWidth = 560f, BannerHeight = 110f, BannerMedal = 100f, BannerMedalX = 62f, BannerTextX = 140f;

        /// <summary>
        /// Баннер «Новый уровень» и входа на арену в «Дыме и свете» (владелец 26 сентября: бумажная
        /// плашка «громоздкая» и «не сочетается с интерфейсом»). Сверху по центру, верх на −150 —
        /// ниже объявления «Разлом зачищен» и полосы босса (−22…−126). Полоса глубокого дыма с нитью
        /// света под строкой; слева круглый медальон (круг — одна фигура с портретом и способностями):
        /// клуб дыма, мягкий тёплый свет, тёмный диск, тонкое кремовое кольцо, едва заметная искра огня
        /// и число антиквой с мягкой тенью (26 сентября, первая съёмка: оранжевый свет и кольцо огня
        /// давали яркое красное кольцо — «огня меньше»); справа подпись и одна строка прибавок через
        /// « · ». Ширина — по тексту
        /// (HudLevelBanner.FitWidth), поэтому медальон и строки стоят от левого края, а дым и нить
        /// растянуты. Своя группа появления: дым проигрывается на каждом показе, кромка тлеет мягко.
        /// Движение — в HudLevelBanner.
        /// </summary>
        static void BuildLevelBanner(RectTransform root, CombatHudView view)
        {
            RectTransform banner = Box(Node("Новый уровень", root), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -150f - BannerHeight * .5f),
                new Vector2(BannerWidth, BannerHeight));
            var bannerView = banner.gameObject.AddComponent<HudLevelBanner>();
            bannerView.Group = banner.gameObject.AddComponent<CanvasGroup>();
            bannerView.Group.blocksRaycasts = false;
            bannerView.Group.interactable = false;

            RectTransform plate = Stretch(Node("Плашка", banner));
            bannerView.Plate = plate;
            // Полоса глубокого дыма: вверх не выше −130, в зону объявления не заходит.
            UiInkKit.SmokeLayer(plate, "Дым", "smoke_band_1", .96f, 56f, 20f, deep: true);

            var left = new Vector2(0f, .5f);
            var centre = new Vector2(.5f, .5f);
            RectTransform medal = Box(Node("Медальон", plate), left, centre, new Vector2(BannerMedalX, 0f), new Vector2(BannerMedal, BannerMedal));
            UiInkKit.SmokeLayer(medal, "Дым", "smoke_blot_2", 1f, 16f, 16f, deep: true);
            // Мягкий тёплый свет за диском: виден ореолом по краю, вздыхает на щелчке числа (силу ставит
            // баннер). Белое сияние, окрашенное в тёплый крем: у оранжевого light_glow край диска ложился
            // на красный спад пятна и читался красным кольцом.
            Image halo = UiInkKit.LightAt(medal, "Свет", "light_glow", centre, Vector2.zero, new Vector2(196f, 196f), 0f, delay: .1f);
            halo.sprite = RoundGlow;
            halo.color = new Color(1f, .84f, .6f, 0f);
            bannerView.Flash = halo;
            Image disc = Layer(medal, "Диск", T.CircleFill, Role.SmokeDeep, .92f);
            disc.type = Image.Type.Simple;
            disc.material = UiInkKit.Plain;
            UiInkKit.Inked(disc, delay: .05f);
            // Тонкое кремовое кольцо — как у значка уровня на портрете; без огня кромку держит оно.
            Image ring = Layer(medal, "Кольцо", T.CircleFrame, Role.Text, 1f);
            Object.DestroyImmediate(ring.GetComponent<ThemeColor>());
            ring.color = new Color(1f, .9f, .74f, .78f);
            ring.type = Image.Type.Simple;
            ring.material = UiInkKit.Plain;
            UiInkKit.Inked(ring, delay: .1f);
            // Кольцо огня — едва заметная искра (сила .1 от баннера; при .3 оно горело красным кольцом) и
            // медленно вращается. Окружность в спрайте — 0,656 ширины и 0,693 высоты: спрайт больше
            // медальона, чтобы огонь и его красная дымка легли снаружи кремового кольца, а не на диск.
            const float fireRing = BannerMedal + 10f;
            bannerView.Rays = UiInkKit.LightAt(medal, "Огонь", "light_ring", centre, Vector2.zero,
                new Vector2(fireRing / .656f, fireRing / .693f), 0f);

            // Число: тёплый крем в золото, мягкая тень шрифта; без оранжевой обводки и свечения старой плашки.
            RectTransform number = Box(Node("Число", medal), centre, centre, new Vector2(0f, 2f), new Vector2(BannerMedal - 16f, BannerMedal - 20f));
            TMP_Text digits = LabelOn(number, "4", FontRole.Heading, 56f, Role.Text, TextAlignmentOptions.Center);
            Object.DestroyImmediate(digits.GetComponent<ThemeColor>());
            digits.color = Color.white;
            digits.enableVertexGradient = true;
            digits.colorGradient = new VertexGradient(new Color(1f, .95f, .84f), new Color(1f, .95f, .84f), new Color(1f, .8f, .52f), new Color(1f, .8f, .52f));
            digits.textWrappingMode = TextWrappingModes.NoWrap;
            digits.enableAutoSizing = true;
            digits.fontSizeMin = 34f;
            digits.fontSizeMax = 56f;
            Shadowed(digits);
            UiInkKit.Revealed(digits, .05f);
            bannerView.Number = digits;

            RectTransform caption = Box(Node("Надпись", plate), left, left, new Vector2(BannerTextX, 15f), new Vector2(500f, 26f));
            TMP_Text captionText = LabelOn(caption, "НОВЫЙ УРОВЕНЬ", FontRole.Heading, 20f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            captionText.characterSpacing = 5f;
            captionText.textWrappingMode = TextWrappingModes.NoWrap;
            UiInkKit.Revealed(captionText, .1f);
            bannerView.Caption = captionText;
            // Одна строка прибавок: остальные строки склеивает HudLevelBanner через « · ». Своего
            // проявления по буквам у неё нет — выезд и прозрачность ведёт баннер, второе легло бы поверх.
            RectTransform gainBox = Box(Node("Прибавки", plate), left, left, new Vector2(BannerTextX, -14f), new Vector2(500f, 30f));
            TMP_Text gain = LabelOn(gainBox, "<color=#FFD27A>+30</color> здоровья · <color=#FFD27A>+5</color> урона · <color=#FFD27A>+10</color> лавидия",
                FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineLeft);
            gain.textWrappingMode = TextWrappingModes.NoWrap;
            gain.richText = true;
            bannerView.GainLines = new[] { gain };

            // Нить света под строкой: от медальона до правого края, растягивается с шириной баннера.
            RectTransform threadBox = Node("Нить", plate);
            threadBox.anchorMin = left;
            threadBox.anchorMax = new Vector2(1f, .5f);
            threadBox.pivot = centre;
            threadBox.offsetMin = new Vector2(BannerTextX - 26f, -55f);
            threadBox.offsetMax = new Vector2(-18f, -25f);
            var thread = threadBox.gameObject.AddComponent<Image>();
            thread.sprite = UiInkKit.Sprite("light_thread");
            thread.type = Image.Type.Simple;
            thread.raycastTarget = false;
            thread.material = UiInkKit.Light;
            // Тише, как нить полосы HUD (огня меньше): при .45 под строкой горела красная черта.
            thread.color = new Color(1f, 1f, 1f, .3f);
            UiInkKit.Inked(thread, left, .25f);

            // Угли из медальона: горстка на щелчке числа (Burst из HudLevelBanner), в покое не летят;
            // медленные и недолгие — до объявления сверху не долетают.
            UiEmbers embers = UiInkKit.Embers(banner, "Угли", left, new Vector2(BannerMedalX, 0f), new Vector2(BannerMedal + 20f, BannerMedal), 0f);
            embers.Life = new Vector2(1f, 1.8f);
            embers.Speed = new Vector2(10f, 24f);
            embers.Size = new Vector2(4f, 9f);
            embers.Sway = 8f;
            embers.SpawnBand = .5f;
            embers.Max = 16;
            bannerView.Embers = embers;
            bannerView.EmberBurst = 10;

            // Своя группа: группа HUD играет только первый раз. Большой момент — огонь есть, но слабый и медленный.
            UiInkGroup group = UiInkKit.Group(banner, UiInkGroup.Sweep.FromCenter, .5f, .2f);
            group.InkDuration = .7f;
            group.Curve = UiInkGroup.Easing.Smooth;
            group.EdgeScale = 1.4f;
            group.Burn = .4f;

            bannerView.FitWidth = true;
            bannerView.WidthRange = new Vector2(400f, 680f);
            bannerView.RightPad = 48f;
            bannerView.PopScale = 1.06f;
            bannerView.HoldTime = 2.4f;
            bannerView.OutTime = .5f;
            bannerView.ClickAt = .6f;
            bannerView.NumberPunch = 1.18f;
            bannerView.FlashPeak = .4f;
            bannerView.FlashRest = .12f;
            bannerView.RaysAlpha = .1f;
            bannerView.RaysSpin = 6f;
            view.LevelBanner = bannerView;
            banner.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- эффекты зелий
        /// <summary>
        /// Ряд значков действующих эффектов зелий над панелью героя (концепт 4-buffs-level-ingame).
        /// У каждого — круг с кольцом-таймером и подпись, что эффект даёт: без наведения понятно.
        /// Значки — временные, из концепта; финальные бутылки-иконки готовит владелец.
        /// </summary>
        static void BuildBuffs(RectTransform root, CombatHudView view)
        {
            RectTransform row = Box(Node("Эффекты зелий", root), BottomCenter, new Vector2(0f, 0f), new Vector2(HeroX - 12f, Bottom + StripHeight + 44f), new Vector2(500f, 48f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            view.ResinChip = BuffChip(row, "Живица", "wc_buff_resin", "Живица", "−25% получаемого урона");
            view.SurgeChip = BuffChip(row, "Порыв", "wc_buff_surge", "Порыв", "+20% к бегу и приёмам");
        }

        static HudBuffChip BuffChip(RectTransform row, string name, string icon, string title, string effect)
        {
            RectTransform chip = Node(name, row);
            chip.sizeDelta = new Vector2(244f, 48f);
            var buff = chip.gameObject.AddComponent<HudBuffChip>();
            buff.Group = chip.gameObject.AddComponent<CanvasGroup>();
            buff.Group.blocksRaycasts = false;
            buff.Group.interactable = false;
            buff.Name = title;
            buff.EffectText = effect;

            // Подложка под текстом — светлая земля не съедает подпись.
            RectTransform plate = Box(Node("Подложка", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(222f, 40f));
            UiInkKit.SmokeLayer(plate, "Дым", "smoke_band_2", .95f, 36f, 22f, origin: new Vector2(0f, .5f));
            // Частая всплывашка (каждое зелье) — без тлеющей кромки: огонь на ней читался красной вспышкой.
            UiInkKit.Group(chip, UiInkGroup.Sweep.LeftToRight, .4f, .12f).Burn = 0f;

            RectTransform circle = Box(Node("Круг", chip), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(24f, 0f), new Vector2(46f, 46f));
            Layer(circle, "Тень", T.CircleFill, Role.Panel, 1f);
            Image art = Mark(circle, "Значок", Kit(icon), Role.Text, 1f, new Vector2(.5f, .5f), Vector2.zero, 38f);
            Object.DestroyImmediate(art.GetComponent<ThemeColor>());
            art.color = Color.white;
            Layer(circle, "Ободок", T.CircleFrame, Role.PanelLine, .5f);
            Image pop = Mark(circle, "Вспышка", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, 110f);
            Additive(pop, new Color(1f, .7f, .35f, 0f));
            pop.enabled = false;
            buff.Glow = pop;
            buff.Circle = circle;
            Image ring = Layer(circle, "Кольцо", T.CircleFrame, Role.Accent, 1f);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = false;
            ring.fillAmount = .7f;
            buff.Ring = ring;

            RectTransform effectBox = Box(Node("Эффект", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(54f, 8f), new Vector2(186f, 22f));
            buff.Effect = LabelOn(effectBox, effect, FontRole.Body, 15f, Role.Text, TextAlignmentOptions.MidlineLeft);
            UiInkKit.Revealed(buff.Effect);
            buff.Effect.fontStyle = FontStyles.Bold;
            buff.Effect.textWrappingMode = TextWrappingModes.NoWrap;
            buff.Effect.enableAutoSizing = true;
            buff.Effect.fontSizeMin = 13f;
            buff.Effect.fontSizeMax = 15f;
            RectTransform titleBox = Box(Node("Название", chip), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(54f, -10f), new Vector2(186f, 18f));
            buff.Title = LabelOn(titleBox, title + " · 4 с", FontRole.Body, 13f, Role.TextMuted, TextAlignmentOptions.MidlineLeft);
            chip.gameObject.SetActive(false);
            return buff;
        }

        static void BuildFeedback(RectTransform root, CombatHudView view)
        {
            RectTransform pill = Box(Node("Отказ", root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, Bottom + StripHeight + 22f), new Vector2(240f, 40f));
            UiInkKit.SmokeLayer(pill, "Дым", "smoke_band_1", 1f, 60f, 26f);
            UiInkKit.LightAt(pill, "Нить", "light_thread", new Vector2(.5f, 0f), new Vector2(0f, -2f), new Vector2(260f, 30f), .6f, delay: .1f);
            foreach (Transform child in pill) child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(26, 26, 6, 6);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Сообщение", pill);
            view.FeedbackText = LabelOn(text, "Перезарядка", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            UiInkKit.Revealed(view.FeedbackText, .05f);
            UiInkKit.Group(pill, UiInkGroup.Sweep.FromCenter, .3f, .08f).Burn = 0f;
            view.FeedbackText.textWrappingMode = TextWrappingModes.NoWrap;
            view.Feedback = pill;
            pill.gameObject.SetActive(false);
        }
    }
}
