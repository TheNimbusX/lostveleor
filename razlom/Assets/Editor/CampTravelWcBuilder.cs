using System.IO;
using Game.Sim;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;
using TextStep = Game.View.UiTheme.TextStep;

namespace Game.EditorTools
{
    /// <summary>
    /// Стол сборов «Перед походом»: Resources/UI/Prefabs/CampTravelWc.prefab (06.10). Раскладка — выбранный владельцем
    /// table-a, материал — «Дым и свет» по ingame-style/table.png: три колонки на клубах глубокого дыма без рамок
    /// (рамки table-a — язык пака, его заменили), круглые расписные медальоны в тёмных кольцах, наведение — тонкая
    /// тлеющая кромка, выбор — огненное кольцо, кнопки-мазки с кейкапами справа. Координаты — холст 1920×1080
    /// (концепт 1672×941 × 1,148, ui-common 6.3); всё, кроме вуалей, — в «Раскладке» с UiFitFrame, чтобы окно помещалось
    /// при масштабе интерфейса 80–120% и на 16:10. Смысл и клики — CampTravelPanel и CampPreparationView; прежний
    /// CampPreparation (столбец кнопок CampPolishUiBuilder) остаётся запасным. Детали — общие CampInkParts (копии
    /// помощников принятых окон); общего AssetDatabase.SaveAssets здесь нет.
    /// </summary>
    public static partial class CampTravelWcBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/Prefabs/CampTravelWc.prefab";
        /// <summary>Узел раскладки 1920×1080 с UiFitFrame (имя — как у окон жителей).</summary>
        const string FrameName = "Раскладка";

        static UiTheme T => UiTheme.Current;

        // Колонки — единицы раскладки от левого верхнего угла (ui-common 6.3 по ingame-style/table.png).
        const float HeaderY = 222f;
        const float SkillX = 76f, SkillW = 580f, SkillCenter = 360f, SkillStep = 200f, SkillY = 419f, SkillSize = 172f;
        const float PotionX = 712f, PotionW = 496f, PotionCenter = 960f, PotionY = 413f, PotionSize = 160f, PotionGap = 230f;
        const float OtherY = 700f, OtherSize = 72f, OtherStep = 94f;
        const float CarryX = 1270f, CarryW = 610f, CarryMainX = 1427f, CarryMainY = 402f, CarrySize = 172f;
        const float CarryTextX = 1535f, CarryTextW = 345f;
        const float OfferY = 632f, OfferSize = 132f;
        static readonly float[] OfferX = { 1367f, 1555f, 1745f };

        [MenuItem("Разлом/UI/Собрать стол «Перед походом»")]
        static void BuildFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Перед походом", "Префаб уже есть. Пересборка сотрёт ручные правки в " + PrefabPath + ".",
                    "Пересобрать", "Отмена"))
                return;
            Build(true);
        }

        /// <summary>
        /// Свежая сборка — раскладка и миграции до <see cref="LayoutVersion"/>; <paramref name="force"/> = false у готового
        /// префаба только доводит его миграциями (ручные правки владельца остаются).
        /// </summary>
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
                Migrate(root.GetComponent<CampTravelPanel>());
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[ui-kit] Стол «Перед походом» собран: CampTravelWc v" + LayoutVersion + ".");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            // Не общий SaveAssets (02.10 он сбросил на диск и откатил 3 материала босса): префаб записан SaveAsPrefabAsset,
            // тема сохраняет себя сама; дописываются только материалы «Дыма и света», если сборка их пометила.
            CampInkParts.SaveInkMaterials();
            return PrefabPath;
        }

        static GameObject Layout()
        {
            var root = new GameObject("CampTravelWc", typeof(RectTransform)) { layer = 5 };
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Временные окна лагеря — 160: над палаткой (100) и окнами жителей (105), под паузой (300).
            canvas.sortingOrder = 160;
            // Шейдер «Дыма и света» берёт данные элемента из uv1/uv2 (UiInkReveal): без каналов не проявятся ни дым, ни текст.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UiScaleFollower>();
            var panel = root.AddComponent<CampTravelPanel>();
            panel.LayoutVersion = BuiltVersion;
            panel.Group = root.AddComponent<CanvasGroup>();
            var rect = (RectTransform)root.transform;

            // Фон — как у окон жителей (CampShopsWcBuilder.Screen): лагерь виден, вуаль ловит мышь — клик мимо колонок
            // не уводит героя ходить по лагерю.
            Image veil = Layer(rect, "Вуаль", T.Pixel, Role.Veil, 1f);
            veil.raycastTarget = true;
            Layer(rect, "Глубина", T.Pixel, Role.Panel, .32f);
            Layer(rect, "Виньетка", T.VeilRadial, Role.Veil, 1f);

            RectTransform frame = FitFrame(rect);
            Smoke(frame);
            panel.Title = CampInkParts.Title(frame, CampWindowText.Get("table.title", "Перед походом"), 960f, 77f, 900f, 1060f);
            BuildSkills(frame, panel);
            BuildPotions(frame, panel);
            BuildCarry(frame, panel);
            BuildButtons(frame, panel);

            // Окно открывают у арки и у стола перед каждым походом: короткое мягкое проявление слева направо, огонь по
            // кромке едва тлеет (как у окон жителей; «красная вспышка» на частых окнах отвергнута).
            UiInkGroup appear = UiInkKit.Group(rect, UiInkGroup.Sweep.LeftToRight, .36f, .2f);
            appear.Burn = .2f;
            appear.HideDuration = .16f;
            return root;
        }

        /// <summary>Раскладка 1920×1080 с опорой на низ по середине (UiFitFrame): при 120% и 16:10 ужимается целиком.</summary>
        static RectTransform FitFrame(RectTransform root)
        {
            RectTransform frame = Node(FrameName, root);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, 0f);
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(1920f, 1080f);
            frame.gameObject.AddComponent<UiFitFrame>();
            return frame;
        }

        /// <summary>
        /// Клубы глубокого дыма под заголовком и под каждой колонкой — без рамок и краёв (ingame-style): мягкое пятно
        /// шире колонки и рваный клуб поверх, края тают в лагерь, между колонками лагерь просвечивает.
        /// </summary>
        static void Smoke(RectTransform frame)
        {
            RectTransform smoke = Stretch(Node("Дым", frame));
            UiInkKit.SmokeAt(smoke, "Под заголовком", "soft_blot", new Vector2(0f, 1f), new Vector2(960f, -120f), new Vector2(1180f, 230f), .6f, deep: true);
            Column(smoke, "Под умением", 360f, 500f, 640f, 660f);
            Column(smoke, "Под зельями", 960f, 540f, 560f, 740f);
            Column(smoke, "Под «с собой»", 1575f, 560f, 660f, 780f);
        }

        static void Column(RectTransform parent, string name, float cx, float cy, float w, float h)
        {
            UiInkKit.SmokeAt(parent, name + " · тень", "soft_blot", new Vector2(0f, 1f), new Vector2(cx, -cy), new Vector2(w + 180f, h + 220f), .72f,
                origin: new Vector2(.5f, .8f), deep: true);
            UiInkKit.SmokeAt(parent, name + " · клуб", "smoke_blot_1", new Vector2(0f, 1f), new Vector2(cx, -cy), new Vector2(w + 80f, h + 110f), .82f,
                origin: new Vector2(.5f, .85f), delay: -.1f, deep: true);
        }

        // ---------------------------------------------------------------- умение

        static void BuildSkills(RectTransform frame, CampTravelPanel panel)
        {
            RectTransform column = Stretch(Node("Умение", frame));
            panel.SkillHeader = CampInkParts.SectionHeader(column, "Заголовок", CampWindowText.Get("table.skill.title", "Умение"), SkillX, HeaderY, SkillW,
                TextStep.Title, caps: false, icon: CampInkParts.KitTexture("ability"), strength: .45f);
            panel.SkillCenterX = SkillCenter;
            panel.SkillStep = SkillStep;
            panel.SkillY = SkillY;
            panel.Skills = new CampTravelMedal[CampTravelRules.SkillSlots];
            for (int i = 0; i < panel.Skills.Length; i++)
            {
                CampTravelMedal medal = Medal(column, "Умение " + (i + 1), SkillCenter + (i - 1) * SkillStep, SkillY, SkillSize, true, false, false);
                // Выбранное — огненное кольцо и огонёк-ромб у его низа, как отметка выбора на концепте.
                UiInkKit.LightAt((RectTransform)medal.Fire.transform, "Ромб", "light_gem", new Vector2(.5f, 0f), new Vector2(0f, SkillSize * .16f),
                    new Vector2(16f, 18f), 1f, delay: .25f);
                medal.Name = Under(medal.Root, "Имя", 18f, 196f, 58f, FontRole.Heading, TextStep.Heading, Role.Text);
                panel.Skills[i] = medal;
            }
            panel.SkillBody = CampInkParts.Text(column, "Описание", "", SkillX + 10f, 598f, SkillW - 20f, 104f, FontRole.Body, TextStep.Body, Role.TextMuted,
                TextAlignmentOptions.Top);
            TopLeft(UiInkKit.Divider(column, "Нить", 520f, true, .45f), SkillCenter - 260f, 714f, 520f, 16f);
            panel.SkillRule = CampInkParts.Text(column, "Правило", CampWindowText.Get("table.skill.rule", "Одно из умений прошлых походов · без талантов"),
                SkillX, 736f, SkillW, 26f, FontRole.Body, TextStep.Caption, Role.TextMuted, TextAlignmentOptions.Center);
            panel.SkillRule.textWrappingMode = TextWrappingModes.NoWrap;
        }

        // ---------------------------------------------------------------- зелья

        static void BuildPotions(RectTransform frame, CampTravelPanel panel)
        {
            RectTransform column = Stretch(Node("Зелья", frame));
            panel.PotionHeader = CampInkParts.SectionHeader(column, "Заголовок", CampWindowText.Get("table.potions.title", "Зелья"), PotionX, HeaderY, PotionW,
                TextStep.Title, caps: false, icon: CampInkParts.KitTexture("alchemist"), strength: .45f);
            panel.PotionSlots = new CampTravelMedal[2];
            for (int slot = 0; slot < 2; slot++)
            {
                float cx = PotionCenter + (slot == 0 ? -.5f : .5f) * PotionGap;
                CampTravelMedal medal = Medal(column, "Ячейка " + (slot + 1), cx, PotionY, PotionSize, true, false, true);
                // Имя в две строки: «Малое зелье концентрации» длинное.
                medal.Name = Under(medal.Root, "Имя", 16f, 220f, 58f, FontRole.Heading, TextStep.Heading, Role.Text);
                medal.Note = Under(medal.Root, "Запас", 74f, 220f, 26f, FontRole.Body, TextStep.Body, Role.TextMuted);
                medal.Note.textWrappingMode = TextWrappingModes.NoWrap;
                panel.PotionSlots[slot] = medal;
            }
            TopLeft(UiInkKit.Divider(column, "Нить", 440f, true, .45f), PotionCenter - 220f, 600f, 440f, 16f);
            panel.OthersCaption = CampInkParts.Text(column, "Другие зелья", CampWindowText.Get("table.potions.others", "Другие зелья (можно заменить)"),
                PotionX, 616f, PotionW, 30f, FontRole.Body, TextStep.Body, Role.Text, TextAlignmentOptions.Center);
            panel.OthersCaption.textWrappingMode = TextWrappingModes.NoWrap;

            // Шесть малых медальонов в ряд (все виды, кроме двух взятых): шаг 94 — под каждым помещается «Лео · ранг 2».
            panel.OtherPotions = new CampTravelMedal[CampTravelRules.OtherPotionSlots];
            for (int i = 0; i < panel.OtherPotions.Length; i++)
            {
                float cx = PotionCenter + (i - (panel.OtherPotions.Length - 1) * .5f) * OtherStep;
                CampTravelMedal medal = Medal(column, "Зелье " + (i + 1), cx, OtherY, OtherSize, true, true, true);
                medal.Note = Under(medal.Root, "Запас", 8f, OtherStep, 22f, FontRole.Body, TextStep.Caption, Role.TextMuted);
                medal.Note.textWrappingMode = TextWrappingModes.NoWrap;
                panel.OtherPotions[i] = medal;
            }
            panel.PotionName = CampInkParts.Text(column, "Имя зелья", "", PotionX, 772f, PotionW, 28f, FontRole.Heading, TextStep.Body, Role.Text,
                TextAlignmentOptions.Center);
            panel.PotionName.textWrappingMode = TextWrappingModes.NoWrap;
            panel.PotionBody = CampInkParts.Text(column, "Действие", "", PotionX + 10f, 802f, PotionW - 20f, 70f, FontRole.Body, TextStep.Body, Role.TextMuted,
                TextAlignmentOptions.Top);
        }

        // ---------------------------------------------------------------- с собой

        static void BuildCarry(RectTransform frame, CampTravelPanel panel)
        {
            RectTransform column = Stretch(Node("С собой", frame));
            panel.CarryHeader = CampInkParts.SectionHeader(column, "Заголовок", CampWindowText.Get("table.carry.title", "Возьми с собой"), CarryX, HeaderY, CarryW,
                TextStep.Title, caps: false, icon: CampInkParts.KitTexture("cache"), strength: .45f);

            // Большая ячейка — только показывает взятое (или подсмотренное наведением); выбор — малыми вариантами ниже.
            panel.CarryMain = Medal(column, "Взято", CarryMainX, CarryMainY, CarrySize, false, false, false);
            panel.CarryName = CampInkParts.Text(column, "Имя", "", CarryTextX, 316f, CarryTextW, 36f, FontRole.Heading, TextStep.Heading, Role.Text);
            panel.CarryName.textWrappingMode = TextWrappingModes.NoWrap;
            FitDown(panel.CarryName, TextStep.Body, TextStep.Heading);
            // Действие артефакта бывает в 200 знаков («Сердце Вечной Зимы»): ужимается до ступени Caption, не ниже шкалы.
            panel.CarryEffect = CampInkParts.Text(column, "Действие", "", CarryTextX, 356f, CarryTextW, 120f, FontRole.Body, TextStep.Body, Role.TextMuted,
                TextAlignmentOptions.TopLeft);
            FitDown(panel.CarryEffect, TextStep.Caption, TextStep.Body);
            panel.CarryUse = CampInkParts.Text(column, "Как включить", "", CarryTextX, 478f, CarryTextW, 24f, FontRole.Body, TextStep.Caption, Role.Text);
            panel.CarryUse.textWrappingMode = TextWrappingModes.NoWrap;

            panel.OffersHeader = CampInkParts.SectionHeader(column, "Варианты", CampWindowText.Get("table.carry.offers", "Варианты на выбор"), CarryX + 10f, 512f,
                CarryW - 10f, TextStep.Body, caps: false, strength: .35f);
            panel.CarryOffers = new CampTravelMedal[CampTravelRules.CarrySlots];
            for (int i = 0; i < panel.CarryOffers.Length; i++)
            {
                CampTravelMedal medal = Medal(column, "Вариант " + (i + 1), OfferX[i], OfferY, OfferSize, true, false, false);
                medal.Name = Under(medal.Root, "Имя", 12f, 180f, 50f, FontRole.Heading, TextStep.Body, Role.Text);
                panel.CarryOffers[i] = medal;
            }
            panel.CarryRule = CampInkParts.Text(column, "Правило", "", CarryX + 20f, 776f, CarryW - 40f, 46f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Top);

            // Знаки даров — ссылками в префабе: Assets/UI/RunIcons вне Resources, Resources.Load их в игре не найдёт.
            panel.GiftSigns = new Texture[(int)CampGift.SpareFlask + 1];
            for (int gift = 1; gift < panel.GiftSigns.Length; gift++)
                panel.GiftSigns[gift] = CampInkParts.KitTexture(CampTravelRules.GiftPlaceholder((CampGift)gift));
            panel.EmptyCarrySign = CampInkParts.KitTexture("items");
        }

        // ---------------------------------------------------------------- кнопки

        static void BuildButtons(RectTransform frame, CampTravelPanel panel)
        {
            panel.Depart = CampInkParts.PrimaryWithKey(frame, "Отправиться", CampWindowText.Get("table.depart", "Отправиться"), "E", 1275f, 910f, 320f, 68f,
                out panel.DepartLabel, out panel.DepartKey);
            panel.Stay = CampInkParts.SecondaryWithKey(frame, "Остаться", CampWindowText.Get("table.stay", "Остаться"), "Esc", 1615f, 910f, 250f, 68f,
                out panel.StayLabel, out _);
            if (panel.Stay.TryGetComponent(out UiHoverMotion motion)) motion.ClickSound = UiSoundEvent.Back;
            CampInkParts.EscHint(frame, out panel.CloseLabel, 1880f, 1012f);
        }

        // ---------------------------------------------------------------- детали

        /// <summary>
        /// Медальон окна (CampInkParts.IconMedallion: тёмный диск, рисунок под маской, тихое кольцо, огненное кольцо выбора,
        /// тлеющая кромка наведения) и ссылки для вида. Рисунок ставит вид: расписной круг, бутылка с полями или белый
        /// знак — CampTravelPanel.SetArt.
        /// </summary>
        /// <param name="glow">Мягкий свет семьи зелья внутри диска (цвет ставит вид).</param>
        static CampTravelMedal Medal(RectTransform parent, string name, float cx, float cy, float size, bool clickable, bool lockable, bool glow)
        {
            InkMedal ink = CampInkParts.IconMedallion(parent, name, cx, cy, size, null, false, clickable, lockable);
            var medal = new CampTravelMedal
            {
                Root = ink.Root, Button = ink.Button, Art = ink.Art, Ring = ink.Ring, Fire = ink.Fire, Locked = ink.Locked,
            };
            // Толстое кольцо «выбрано рукой» окну не нужно: выбор — огонь, активная ячейка зелья — тот же огонь ярче.
            if (ink.Picked != null) Object.DestroyImmediate(ink.Picked.gameObject);
            if (glow)
            {
                // Над диском, под рисунком: «Дым», «Диск», затем свет — бутылка остаётся поверх.
                medal.Glow = UiInkKit.LightLayer(ink.Root, "Свет семьи", "soft_blot", .35f, -size * .1f, delay: .15f);
                medal.Glow.transform.SetSiblingIndex(2);
            }
            if (clickable) medal.Hover = ink.Root.gameObject.AddComponent<CampHoverRelay>();
            if (lockable && ink.LockLabel != null)
            {
                // У малого медальона подпись ранга — под ним («Лео · ранг 2»), внутри только замок по центру.
                Object.DestroyImmediate(ink.LockLabel.transform.parent.gameObject);
                if (ink.Locked.transform.Find("Замок") is RectTransform padlock)
                {
                    padlock.anchoredPosition = Vector2.zero;
                    padlock.sizeDelta = new Vector2(size * .36f, size * .36f);
                }
            }
            return medal;
        }

        /// <summary>Надпись под медальоном, по центру, на <paramref name="gap"/> ниже его круга; растёт с ним при наведении.</summary>
        static TMP_Text Under(RectTransform medal, string name, float gap, float w, float h, FontRole font, TextStep step, Role role)
        {
            RectTransform box = Node(name, medal);
            box.anchorMin = box.anchorMax = new Vector2(.5f, 0f);
            box.pivot = new Vector2(.5f, 1f);
            box.anchoredPosition = new Vector2(0f, -gap);
            box.sizeDelta = new Vector2(w, h);
            TMP_Text label = UiInkKit.Label(box, "Надпись", "", font, CampInkParts.Size(step), role, TextAlignmentOptions.Top, delay: .15f);
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        /// <summary>Длинная строка ужимается между ступенями шкалы темы (не ниже <paramref name="min"/>), а не выезжает за колонку.</summary>
        static void FitDown(TMP_Text label, TextStep min, TextStep max)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = CampInkParts.Size(min);
            label.fontSizeMax = CampInkParts.Size(max);
        }
    }
}
