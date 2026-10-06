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
    /// Палатка v103 (06.10): строка вкладок «СУМКА ◇ КЛЯТВЫ ◇ АТЛАС» над окнами с пеплом справа, вкладка «Клятвы» (концепт
    /// владельца oaths-b в нашем стиле ingame-style/oaths.png) и «[Esc] Закрыть». Атлас 3×4 — CampTentWcBuilder.Atlas.
    /// Детали — общие CampInkParts (копии принятых помощников); кегли — шкала темы 56/32/24/18/14.
    /// Координаты страниц — от левого верхнего угла правой панели «Сумка» (740, 100; 1084 × 860), строки вкладок — от доски.
    /// </summary>
    public static partial class CampTentWcBuilder
    {
        /// <summary>Цвет группы печатей — слабое сияние внутри диска (знак остаётся белым): герой, выживание, удача, добыча.</summary>
        static readonly Role[] OathGroupRoles = { Role.Health, Role.Good, Role.Epic, Role.Coins };

        /// <summary>Места групп на карте (от левого верхнего угла страницы): заголовок x, y и центр первой печати cx, cy.</summary>
        static readonly float[][] OathGroupPlaces =
        {
            new[] { 36f, 146f, 190f, 236f },
            new[] { 386f, 146f, 540f, 236f },
            new[] { 36f, 470f, 190f, 560f },
            new[] { 386f, 470f, 540f, 560f },
        };

        /// <summary>Печатей в ряду: герой 3+3, выживание 2+2, удача 3, добыча 2+2 (концепт oaths-b).</summary>
        static readonly int[][] OathGroupRows = { new[] { 3, 3 }, new[] { 2, 2 }, new[] { 3 }, new[] { 2, 2 } };

        const float OathSealSize = 84f, OathStepX = 104f, OathStepY = 120f, SlotSize = 88f, SlotStep = 112f;

        /// <summary>Копия карты владельца для окна; нет — сама текстура стойки (тот же рисунок).</summary>
        const string OathMapPath = "Assets/UI/CampOaths/OathMap.png";
        const string OathMapFallback = "Assets/Resources/Environment/Camp/Pack0610/Props/OathBoard_OwnerMap.png";

        /// <summary>
        /// v103 — вкладки и клятвы. Идемпотентно: каждая часть строится, только если её узла ещё нет. Старые вкладки «Сумка /
        /// Атлас» внутри панели и нить под ними выключаются (не удаляются: ручные правки владельца остаются в префабе),
        /// ссылки вида переходят на новую строку. Сумка не сдвигается — сдвиг вверх на место старых вкладок решает владелец.
        /// </summary>
        static void MigrateTo103(CampTentView view)
        {
            if (view.BagPanel == null) { Debug.LogWarning("[ui-kit] Палатка: нет панели «Сумка» — вкладки v103 не собраны"); return; }
            var board = (RectTransform)view.BagPanel.parent;
            var content = view.BagPanel.Find("Содержимое") as RectTransform;
            if (content == null) { Debug.LogWarning("[ui-kit] Палатка: у «Сумки» нет «Содержимое» — v103 не собрана"); return; }
            // Новые массивы вида — нужной длины, даже если префаб сохранил их иначе.
            if (view.OathSlots == null || view.OathSlots.Length != CampOathRules.SlotPlaces) view.OathSlots = new CampOathSeal[CampOathRules.SlotPlaces];
            if (view.OathSeals == null || view.OathSeals.Length != OathIds.Count) view.OathSeals = new CampOathSeal[OathIds.Count];
            if (view.OathIcons == null || view.OathIcons.Length != OathIds.Count) view.OathIcons = new Texture[OathIds.Count];
            if (view.OathGroupTitles == null || view.OathGroupTitles.Length != 4) view.OathGroupTitles = new TMP_Text[4];
            if (view.OathCardPips == null || view.OathCardPips.Length != 3) view.OathCardPips = new Image[3];
            if (view.AtlasSeals == null || view.AtlasSeals.Length != CampOathRules.AtlasPlaces) view.AtlasSeals = new CampOathSeal[CampOathRules.AtlasPlaces];

            if (board.Find("Вкладки") == null) BuildTabBar(board, content, view);
            if (content.Find("Страница клятв") == null) BuildOathPage(content, view);
            BuildRoundAtlas(content, view);
            if (content.Find("Клавиши") == null)
            {
                // Холст 1920×1080: правый край 1880, строка 1000 — в единицах панели (740, 100).
                CampInkParts.EscHint(content, out TMP_Text esc, 1880f - 740f, 1000f - 100f);
                view.EscLabel = esc;
            }
            for (int i = 0; i < OathIds.Count; i++)
            {
                OathId id = CampOathRules.At(i);
                view.OathIcons[i] = CampInkParts.ArtOr("UI/OathIcons/" + CampOathRules.IconFile(id), CampOathRules.PlaceholderIcon(id));
            }
            // Крестик панели — поверх страниц: карточки справа доходят до его угла.
            if (view.Close != null) view.Close.transform.SetAsLastSibling();
            Debug.Log("[ui-kit] Палатка: вкладки «Сумка · Клятвы · Атлас», клятвы и атлас 3×4 — собраны (v103).");
        }

        /// <summary>Значок пепла: окончательный из Resources/UI/CampCurrency/ash.png, а пока — знак Разлома (пепел несёт Разлом).</summary>
        static Texture AshArt => CampInkParts.ArtOr("UI/CampCurrency/ash", "rift");

        // ---------------------------------------------------------------- строка вкладок

        /// <summary>
        /// Строка вкладок над окнами (DESIGN 06.10: «вкладки слева капсом»): ◇ СУМКА ◇ КЛЯТВЫ ◇ АТЛАС ◇ — нить до пепла справа.
        /// Своя группа проявления и CanvasGroup: приезжает первой вместе с панелями (CampTentView.Panels).
        /// </summary>
        static void BuildTabBar(RectTransform board, RectTransform content, CampTentView view)
        {
            RectTransform bar = TopLeft(Node("Вкладки", board), 96f, 24f, 1728f, 56f);
            bar.gameObject.AddComponent<CanvasGroup>();
            // Тихий клуб дыма под строкой: подписи не тонут в светлой земле лагеря. Мышь он не ловит.
            UiInkKit.SmokeAt(bar, "Дым", "smoke_band_1", new Vector2(0f, .5f), new Vector2(640f, 0f), new Vector2(1380f, 120f), .8f, deep: true);
            string[] labels = { "Сумка", "Клятвы", "Атлас" };
            Button[] tabs = CampInkParts.TabStrip(bar, "Строка", labels, 0f, 2f, 52f, TextStep.Heading, true, pad: 20f, lineTo: 1570f, edgeGems: true);

            // Старые вкладки внутри панели и нить под ними — выключить; ссылки вида — на новую строку.
            foreach (Button old in new[] { view.BagTab, view.AtlasTab })
                if (old != null && old.transform.IsChildOf(content)) old.gameObject.SetActive(false);
            Transform line = content.Find("Линия");
            if (line != null) line.gameObject.SetActive(false);
            view.BagTab = tabs[0];
            view.OathsTab = tabs[1];
            view.AtlasTab = tabs[2];

            // Пепел справа сверху (1700…1824): значок и число Philosopher.
            CampInkParts.WalletRow(bar, "Пепел", 1590f, 9f, 138f, new[] { AshArt }, new[] { Role.Text }, TextAnchor.MiddleRight,
                out TMP_Text[] values, TextStep.Heading);
            view.AshCount = values[0];
            view.AshIcon = bar.Find("Пепел/Валюта 1/Значок").GetComponent<RawImage>();
            view.TabBar = bar;

            UiInkGroup appear = UiInkKit.Group(bar, UiInkGroup.Sweep.LeftToRight, .35f, .2f);
            appear.Burn = .2f;
            appear.HideDuration = .16f;
        }

        // ---------------------------------------------------------------- клятвы

        static void BuildOathPage(RectTransform content, CampTentView view)
        {
            RectTransform page = Stretch(Node("Страница клятв", content));
            view.OathsPage = page.gameObject;
            Texture ash = AshArt;

            // Ряд слотов над картой: 6 мест, закрытые — замок и «Ур. N» (пороги Camp.OathSlots: 6 / 12 / 18).
            RectTransform slots = Stretch(Node("Ряд слотов", page));
            UiInkKit.SmokeAt(slots, "Дым", "smoke_band_2", new Vector2(0f, 1f), new Vector2(360f, -72f), new Vector2(800f, 150f), .9f, deep: true);
            view.OathSlotsCount = CampInkParts.Text(slots, "В силе", "В СИЛЕ · 0 ИЗ 3", 160f, 0f, 400f, 24f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Center);
            view.OathSlotsCount.characterSpacing = 2f;
            // Нить под рядом, как на концепте: ряд — отдельная полка над картой.
            TopLeft(UiInkKit.Divider(slots, "Нить", 700f, false, .35f), 10f, 120f, 700f, 16f);
            for (int s = 0; s < CampOathRules.SlotPlaces; s++)
            {
                InkMedal medal = CampInkParts.IconMedallion(slots, "Слот " + (s + 1), 360f + (s - 2.5f) * SlotStep, 72f, SlotSize, null, true, lockable: true);
                view.OathSlots[s] = Seal(medal, s, null);
            }

            // Карта морехода владельца — фон доски: края тают в дым (UiInkKit.Map), рисунок чуть приглушён, чтобы белые
            // знаки читались. Кадр карты чуть уже 4:3 — три острова целиком, пустой пергамент по краям срезан.
            RectTransform map = TopLeft(Node("Карта", page), 10f, 132f, 700f, 630f);
            UiInkKit.SmokeLayer(map, "Дым", "smoke_blot_2", 1f, 70f, 56f, deep: true);
            var art = Stretch(Node("Рисунок", map)).gameObject.AddComponent<RawImage>();
            art.texture = OathMapTexture();
            art.uvRect = new Rect(.1f, .02f, .8f, .96f);
            art.color = new Color(.8f, .78f, .74f, 1f);
            art.raycastTarget = false;
            art.material = UiInkKit.Map;
            UiInkKit.Inked(art, delay: .05f);
            view.OathMap = art;

            for (int g = 0; g < 4; g++) BuildOathGroup(page, view, (OathGroup)g, ash);
            BuildOathCard(page, view, ash);
            page.gameObject.SetActive(false);
        }

        static Texture2D OathMapTexture()
        {
            var copy = AssetDatabase.LoadAssetAtPath<Texture2D>(OathMapPath);
            return copy != null ? copy : AssetDatabase.LoadAssetAtPath<Texture2D>(OathMapFallback);
        }

        /// <summary>
        /// Группа на острове: заголовок (ромб, имя антиквой, нить), мягкий тёмный клуб под печатями для читаемости и печати 84
        /// с ценой пепла под каждой. Печать ложится в CampTentView.OathSeals по номеру клятвы.
        /// </summary>
        static void BuildOathGroup(RectTransform page, CampTentView view, OathGroup group, Texture ash)
        {
            int g = (int)group;
            float[] place = OathGroupPlaces[g];
            int[] rows = OathGroupRows[g];
            OathId[] members = CampOathRules.Members(group);
            RectTransform node = Stretch(Node("Группа " + CampOathRules.GroupRu(group), page));

            int widest = 0;
            foreach (int n in rows) widest = Mathf.Max(widest, n);
            float top = place[1] - 12f, bottom = place[3] + (rows.Length - 1) * OathStepY + OathSealSize * .5f + 34f;
            UiInkKit.SmokeAt(node, "Дым", "soft_blot", new Vector2(0f, 1f), new Vector2(place[2], -(top + bottom) * .5f),
                new Vector2(widest * OathStepX + 90f, bottom - top + 40f), .62f, deep: true);
            view.OathGroupTitles[g] = CampInkParts.SectionHeader(node, "Заголовок", CampOathRules.GroupRu(group), place[0], place[1], 300f, TextStep.Heading, false);

            var arts = new Texture[members.Length];
            for (int i = 0; i < members.Length; i++)
                arts[i] = CampInkParts.ArtOr("UI/OathIcons/" + CampOathRules.IconFile(members[i]), CampOathRules.PlaceholderIcon(members[i]));
            InkMedal[] medals = CampInkParts.SealSet(node, "Печати", place[2], place[3], rows, OathSealSize, OathStepX, OathStepY, arts, true, ash);
            for (int i = 0; i < members.Length && i < medals.Length; i++)
            {
                int index = CampOathRules.IndexOf(members[i]);
                medals[i].Root.name = "Печать " + CampOathRules.NameRu(members[i]);
                CampOathSeal seal = Seal(medals[i], index, OathGroupRoles[g]);
                if (RunBoons.MaxRank(members[i]) > 1) seal.Pips = Pips(medals[i].Root, OathSealSize);
                view.OathSeals[index] = seal;
            }
        }

        /// <summary>
        /// Карточка справа (340 шириной): большая печать 150 с огнём, имя (Title 32), «Герой · ступень 2 из 3», огоньки ступеней,
        /// нить, действие и «сейчас / дальше», заметка, нить, цена пепла, «Поклясться [E]», подсказка ПКМ и отказ.
        /// </summary>
        static void BuildOathCard(RectTransform page, CampTentView view, Texture ash)
        {
            const float w = 340f, cx = w * .5f;
            RectTransform card = TopLeft(Node("Карточка клятвы", page), 724f, 0f, w, 860f);
            Texture first = view.OathIcons[0] != null ? view.OathIcons[0] : CampInkParts.KitTexture(CampOathRules.PlaceholderIcon(OathId.ToughHide));
            InkMedal big = CampInkParts.IconMedallion(card, "Печать", cx, 108f, 150f, first, true, clickable: false);
            view.OathCardSeal = Seal(big, 0, OathGroupRoles[0]);
            view.OathCardSeal.Fire.gameObject.SetActive(true);

            view.OathCardName = CampInkParts.Text(card, "Название", "Крепкая шкура", 0f, 194f, w, 46f, FontRole.Heading, TextStep.Title, Role.Text,
                TextAlignmentOptions.Center);
            FitLine(view.OathCardName, TextStep.Heading);
            view.OathCardMeta = CampInkParts.Text(card, "Группа", "Герой · ступень 0 из 3", 0f, 240f, w, 22f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Center);
            RectTransform pips = TopLeft(Node("Ступени", card), cx - 45f, 262f, 90f, 18f);
            for (int i = 0; i < 3; i++)
                view.OathCardPips[i] = UiInkKit.LightAt(pips, "Ступень " + (i + 1), "light_gem", new Vector2(0f, .5f), new Vector2(15f + i * 30f, 0f),
                    new Vector2(16f, 18f), 1f, delay: .22f);
            TopLeft(UiInkKit.Divider(card, "Нить 1", w - 40f, true, .5f), 20f, 288f, w - 40f, 16f);

            view.OathCardEffect = CampInkParts.Text(card, "Действие", "+30 к здоровью за каждую ступень.", 10f, 310f, w - 20f, 92f, FontRole.Body,
                TextStep.Body, Role.Text, TextAlignmentOptions.TopLeft);
            view.OathCardProgress = CampInkParts.Text(card, "Сейчас и дальше", "Первая ступень: +30 здоровья", 10f, 404f, w - 20f, 24f, FontRole.Body,
                TextStep.Caption, Role.TextMuted, TextAlignmentOptions.TopLeft);
            view.OathCardNote = CampInkParts.Text(card, "Заметка", "", 10f, 430f, w - 20f, 40f, FontRole.Body, TextStep.Caption, Role.Accent,
                TextAlignmentOptions.TopLeft);
            view.OathCardNote.gameObject.SetActive(false);
            TopLeft(UiInkKit.Divider(card, "Нить 2", w - 40f, false, .4f), 20f, 478f, w - 40f, 16f);

            RectTransform price = CampInkParts.WalletRow(card, "Цена", 0f, 494f, w, new[] { ash }, new[] { Role.Text }, TextAnchor.MiddleCenter,
                out TMP_Text[] values, TextStep.Title);
            view.OathCardPriceRow = price.gameObject;
            view.OathCardPrice = values[0];
            view.OathCardPriceIcon = price.Find("Валюта 1/Значок").GetComponent<RawImage>();

            view.OathAction = CampInkParts.PrimaryWithKey(card, "Действие клятвы", "Поклясться", "E", 20f, 560f, w - 40f, 64f,
                out view.OathActionLabel, out view.OathActionKey);
            view.OathCardHint = CampInkParts.Text(card, "Подсказка", "", 0f, 636f, w, 22f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.Center);
            view.OathCardWarning = CampInkParts.Text(card, "Отказ", "", 10f, 662f, w - 20f, 44f, FontRole.Body, TextStep.Caption, Role.Bad,
                TextAlignmentOptions.Top);
        }

        /// <summary>Одна строка: длинное имя ужимается до ступени <paramref name="min"/>, не ниже шкалы темы.</summary>
        static void FitLine(TMP_Text label, TextStep min)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = T.Size(min);
        }

        // ---------------------------------------------------------------- печать

        /// <summary>
        /// Компонент печати (CampOathSeal) поверх медальона CampInkParts: ссылки на его части; с <paramref name="glow"/> — слабое
        /// сияние цвета группы внутри диска, под знаком (над диском, под маской рисунка).
        /// </summary>
        static CampOathSeal Seal(InkMedal medal, int index, Role? glow)
        {
            var seal = medal.Root.gameObject.AddComponent<CampOathSeal>();
            seal.Index = index;
            seal.Disc = medal.Disc;
            seal.Art = medal.Art;
            seal.Ring = medal.Ring;
            seal.Fire = medal.Fire;
            seal.Picked = medal.Picked;
            seal.Locked = medal.Locked;
            seal.LockLabel = medal.LockLabel;
            seal.Price = medal.Note;
            if (medal.Note != null && medal.Note.transform.parent.Find("Значок") is Transform icon) seal.PriceIcon = icon.GetComponent<RawImage>();
            if (glow.HasValue)
            {
                Image inner = UiInkKit.LightLayer(medal.Root, "Сияние", "soft_blot", 1f, -medal.Root.sizeDelta.x * .12f, delay: .2f);
                Tint(inner, glow.Value, .22f);
                Transform mask = medal.Root.Find("Маска");
                if (mask != null) inner.transform.SetSiblingIndex(mask.GetSiblingIndex());
                seal.Glow = inner;
            }
            return seal;
        }

        /// <summary>Ступени клятвы героя: три огонька на нижней дуге кольца (как точки усилений плиток HUD).</summary>
        static Image[] Pips(RectTransform root, float size)
        {
            RectTransform node = Stretch(Node("Ступени", root));
            var pips = new Image[3];
            float r = size * .5f - 3f;
            for (int i = 0; i < 3; i++)
            {
                float a = (250f + i * 20f) * Mathf.Deg2Rad;
                pips[i] = UiInkKit.LightAt(node, "Ступень " + (i + 1), "light_gem", new Vector2(.5f, .5f), new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                    new Vector2(12f, 13f), 1f, delay: .22f);
            }
            return pips;
        }

        /// <summary>
        /// v104 (06.10, после первых кадров) — глубокий дым под правыми карточками «Клятв» и «Атласа», как на концепте:
        /// фон палатки по решению 22.09 остаётся светлым (лагерь виден целиком), поэтому текст карточки на земле
        /// читался хуже, чем в окнах жителей. Дым — только под карточкой, края тают; повторный вызов ничего не дублирует.
        /// </summary>
        static void MigrateTo104(CampTentView view)
        {
            foreach (var page in new[] { view.OathsPage, view.AtlasPage })
            {
                if (page == null) continue;
                foreach (var name in new[] { "Карточка клятвы", "Карточка артефакта" })
                {
                    var card = page.transform.Find(name) as RectTransform;
                    if (card == null || card.Find("Дым под карточкой") != null) continue;
                    float w = card.rect.width > 1f ? card.rect.width : 360f, h = card.rect.height > 1f ? card.rect.height : 860f;
                    var shade = UiInkKit.SmokeAt(card, "Тень под карточкой", "soft_blot", new Vector2(0f, 1f), new Vector2(w * .5f, -h * .45f),
                        new Vector2(w + 300f, h + 160f), .72f, deep: true);
                    var smoke = UiInkKit.SmokeAt(card, "Дым под карточкой", "smoke_blot_1", new Vector2(0f, 1f), new Vector2(w * .5f, -h * .42f),
                        new Vector2(w + 160f, h * .9f), .62f, delay: -.1f, deep: true);
                    smoke.transform.SetAsFirstSibling();
                    shade.transform.SetAsFirstSibling();
                    shade.raycastTarget = smoke.raycastTarget = false;
                }
            }
        }
    }
}
