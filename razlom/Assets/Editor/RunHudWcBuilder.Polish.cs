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
    /// Миграция v3 экранов забега — полировка по доске ART/UI/concepts-2026-09-30-hud-polish (выбор владельца
    /// 30.09: «1 а. из 1b взять секунды и стаки в углу значка и подпись «фаза 2»… 3 а. 4 ок»):
    /// * полоса босса — засечки 66/50/33 на самой полосе с кружками, круглая «голова» заливки (ромб концепта —
    ///   кругом), подпись «Босс · фаза 2» под именем и «2900 / 5000» под полосой;
    /// * пул монет, летящих в строку добычи;
    /// * экран награды — перелив редкости по карточке, кейкап набора (тёмный скруглённый квадрат, кремовая
    ///   буква, тонкая кромка — лист 5) с кольцом блокировки, подсказка справа и вложенная подсказка слова
    ///   (без кромки и огня: «подсказки без кромки»), «Отказаться / Резервный план» — шире, надпись ужимается;
    /// * «Улучшение» на выборе арены — свиток вместо ромба кристалла; ссылка на значок тайников панели;
    /// * итоги со статистикой (кадр 4): стоп-кадр в тлеющем круге, строки, «Убито», «Потеряно» / «Остаётся»,
    ///   кейкапы на кнопках.
    /// Всё — поверх того, что лежит в префабе: узлы добавляются, старые детали итогов гаснут, сдвигаются только
    /// узлы на местах сборки (руками переставленные остаются, в журнал — предупреждение).
    /// </summary>
    public static partial class RunHudWcBuilder
    {
        const string AdditivePath = "Assets/UI/Shaders/UiAdditive.mat";

        static Material AdditiveMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AdditivePath);
            if (mat != null) return mat;
            var shader = Shader.Find("Razlom/UI Additive");
            if (shader == null) return null;
            mat = new Material(shader) { name = "UiAdditive" };
            AssetDatabase.CreateAsset(mat, AdditivePath);
            return mat;
        }

        /// <summary>Свет: аддитивный материал и свой цвет вместо цвета темы.</summary>
        static void Additive(Graphic graphic, Color colour)
        {
            graphic.material = AdditiveMaterial();
            var tint = graphic.GetComponent<ThemeColor>();
            if (tint != null) Object.DestroyImmediate(tint);
            graphic.color = colour;
            graphic.raycastTarget = false;
        }

        /// <summary>
        /// Картинка без краски темы: цвет ставит вид (засечки, кейкап, кольцо блокировки). <paramref name="inked"/> —
        /// проявляется тушью вместе с группой своего экрана; свет (аддитивный) — без проявления.
        /// </summary>
        static Image Plain(RectTransform parent, string name, Sprite sprite, Color colour, Vector2 anchor, Vector2 position, Vector2 size,
            bool inked = true)
        {
            RectTransform rect = At(Node(name, parent), anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            image.color = colour;
            if (inked)
            {
                image.material = UiInkKit.Plain;
                UiInkKit.Inked(image, delay: .15f);
            }
            return image;
        }

        static void Shift(RectTransform rect, float dy)
        {
            if (rect != null) rect.anchoredPosition += new Vector2(0f, dy);
        }

        /// <summary>v3 — полировка экранов забега (доска concepts-2026-09-30-hud-polish).</summary>
        static void MigrateTo3(RunHudView view)
        {
            var root = (RectTransform)view.transform;
            PolishBoss(view);
            BuildCoins(root, view);
            PolishChoice(view);
            PolishStatus(view);
            PolishSummary(view);
        }

        // ---------------------------------------------------------------- полоса босса

        const float BossSubtitleHeight = 22f, BossNumbersHeight = 20f, BossMarkDot = 12f;

        static void PolishBoss(RunHudView view)
        {
            RectTransform boss = view.Boss;
            if (boss == null || view.BossBar == null || view.BossName == null)
            {
                Debug.LogWarning("[ui-kit] Полосы босса в экранах забега нет (или она без полосы и имени) — засечки и подпись фазы не собраны");
                return;
            }
            var bar = (RectTransform)view.BossBar.transform;
            var nameBox = view.BossName.transform.parent as RectTransform;
            float nameBottom = nameBox != null && nameBox.anchorMin == nameBox.anchorMax
                ? nameBox.anchoredPosition.y - nameBox.pivot.y * nameBox.sizeDelta.y
                : -55f;

            // Подпись фазы — сразу под именем; полоса опускается, если подпись на неё ложится.
            float subtitleY = nameBottom - BossSubtitleHeight * .5f + 4f;
            RectTransform subtitle = Box(Node("Подпись фазы", boss), new Vector2(.5f, 1f), Center, new Vector2(0f, subtitleY),
                new Vector2(nameBox != null ? nameBox.sizeDelta.x : 640f, BossSubtitleHeight));
            view.BossSubtitle = UiInkKit.Label(subtitle, "Надпись", "Босс", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.TextMuted, TextAlignmentOptions.Center, 1f, 1f, .2f);
            view.BossSubtitle.textWrappingMode = TextWrappingModes.NoWrap;
            float want = subtitleY - BossSubtitleHeight * .5f - 6f - bar.sizeDelta.y * .5f;
            float drop = Mathf.Min(0f, want - bar.anchoredPosition.y);
            if (drop < 0f)
            {
                Shift(bar, drop);
                Shift(boss.Find("Нить") as RectTransform, drop);
            }

            // Круглая «голова» на конце заливки: мягкий свет и кремовый круг поверх заливки и следа.
            RectTransform head = At(Node("Голова", bar), new Vector2(.64f, .5f), Vector2.zero, new Vector2(18f, 18f));
            Image glow = Plain(head, "Свет", RoundGlow, Color.white, Center, Vector2.zero, new Vector2(46f, 46f), false);
            Additive(glow, new Color(1f, .78f, .55f, .45f));
            Plain(head, "Круг", T.CircleFill, new Color(1f, .93f, .82f, 1f), Center, Vector2.zero, new Vector2(11f, 11f));
            view.BossBar.Head = head;

            // Засечки поверх заливки: черта поперёк полосы и кружок на её нижней кромке.
            RectTransform marks = Stretch(Node("Засечки", bar));
            view.BossMarks = new RunHudView.BossMark[RunHudBossMarks.Count];
            float barHeight = bar.sizeDelta.y;
            for (int i = 0; i < RunHudBossMarks.Count; i++)
            {
                RectTransform mark = At(Node("Засечка " + RunHudBossMarks.PercentAt(i), marks), new Vector2(RunHudBossMarks.Fraction(i), .5f),
                    Vector2.zero, new Vector2(BossMarkDot + 6f, barHeight + 12f));
                Plain(mark, "Черта", T.Pixel, new Color(1f, .92f, .8f, .55f), Center, Vector2.zero, new Vector2(2f, barHeight + 10f));
                Vector2 dotAt = new Vector2(0f, -barHeight * .5f - 3f);
                Image flash = Plain(mark, "Вспышка", RoundGlow, Color.white, Center, dotAt, new Vector2(34f, 34f), false);
                Additive(flash, new Color(1f, .6f, .3f, 0f));
                flash.enabled = false;
                Image dot = Plain(mark, "Кружок", T.CircleFill, T.Get(Role.SmokeDeep), Center, dotAt, new Vector2(BossMarkDot, BossMarkDot));
                Color ringColour = T.Get(Role.PanelLine);
                ringColour.a = .55f;
                Image ring = Plain(mark, "Кольцо", T.CircleFrame, ringColour, Center, dotAt, new Vector2(BossMarkDot + 3f, BossMarkDot + 3f));
                view.BossMarks[i] = new RunHudView.BossMark { Rect = mark, Dot = dot, Ring = ring, Flash = flash };
            }

            // Числа под полосой: «2900 / 5000».
            float barBottom = bar.anchoredPosition.y - bar.sizeDelta.y * .5f;
            RectTransform numbers = Box(Node("Числа", boss), new Vector2(.5f, 1f), Center, new Vector2(0f, barBottom - 6f - BossNumbersHeight * .5f),
                new Vector2(300f, BossNumbersHeight));
            view.BossNumbers = UiInkKit.Label(numbers, "Надпись", "2900 / 5000", FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.TextMuted, TextAlignmentOptions.Center, 0f, 1f, .25f);
            view.BossNumbers.textWrappingMode = TextWrappingModes.NoWrap;

            // Клуб дыма полосы растёт вниз вместе с содержимым.
            float bottom = barBottom - 6f - BossNumbersHeight - 6f;
            float need = -bottom;
            if (boss.sizeDelta.y < need) boss.sizeDelta = new Vector2(boss.sizeDelta.x, need);
        }

        // ---------------------------------------------------------------- монеты

        const float CoinSize = 22f;

        static void BuildCoins(RectTransform root, RunHudView view)
        {
            RectTransform layer = Stretch(Node("Монеты", root));
            // Над строкой добычи, под полосой босса и экранами выбора.
            if (view.Loot != null) layer.SetSiblingIndex(view.Loot.GetSiblingIndex() + 1);
            view.CoinLayer = layer;
            view.Coins = new RectTransform[RunHudCoins.Pool];
            Texture gold = Icon("gold");
            for (int i = 0; i < view.Coins.Length; i++)
            {
                RectTransform coin = At(Node("Монета " + (i + 1), layer), Center, Vector2.zero, new Vector2(CoinSize, CoinSize));
                var art = coin.gameObject.AddComponent<RawImage>();
                art.texture = gold;
                art.raycastTarget = false;
                Tint(art, Role.Coins);
                coin.gameObject.SetActive(false);
                view.Coins[i] = coin;
            }
        }

        // ---------------------------------------------------------------- экран награды

        const float KitKeycap = UiTheme.KeycapSizeDefault, TipWidthDefault = 380f;

        static void PolishChoice(RunHudView view)
        {
            if (view.Choice == null) return;
            var screen = (RectTransform)view.Choice.transform;
            foreach (RunOfferCard card in view.Offers)
            {
                if (card == null) continue;
                AddGlint(card);
                card.KeyLock = KitKey(card.Key, KitKeycap);
            }
            // «Улучшение» — свиток способности, не ромб кристалла (контейнеры и знаки — круги, не ромбы).
            if (view.RouteIcons != null && view.RouteIcons.Length > 0 && (view.RouteIcons[0] == null || view.RouteIcons[0] == Icon("talent")))
                view.RouteIcons[0] = Icon("ability");
            PolishSkip(view);
            BuildOfferTip(screen, view);
        }

        /// <summary>Перелив редкости: полоса света под маской карточки, бег — HudGlint, цвет ставит RunHud.</summary>
        static void AddGlint(RunOfferCard card)
        {
            var rect = (RectTransform)card.transform;
            RectTransform box = Stretch(Node("Перелив", rect), 6f);
            Transform text = rect.Find("Текст");
            if (text != null) box.SetSiblingIndex(text.GetSiblingIndex());
            box.gameObject.AddComponent<RectMask2D>();
            RectTransform stripe = Box(Node("Полоса", box), Center, Center, Vector2.zero, new Vector2(80f, 320f));
            stripe.localRotation = Quaternion.Euler(0f, 0f, -24f);
            var image = stripe.gameObject.AddComponent<Image>();
            image.sprite = RoundGlow;
            Additive(image, new Color(.6f, .9f, 1f, 0f));
            image.enabled = false;
            var glint = box.gameObject.AddComponent<HudGlint>();
            glint.Stripe = image;
            glint.Peak = .32f;
            glint.Duration = .9f;
            card.Glint = glint;
        }

        /// <summary>
        /// Кейкап набора (лист 5: тёмный скруглённый квадрат, кремовая буква, тонкая кромка) из круглого кейкапа
        /// «Дыма и света»: кольцо становится скруглённой кромкой, под буквой — тёмная подложка; вокруг — кольцо
        /// блокировки ввода (заливка по кругу) и свет, которым кейкап загорается.
        /// </summary>
        static RunHudView.KeyLock KitKey(TMP_Text letter, float size)
        {
            if (letter == null || !(letter.transform.parent is RectTransform cap)) return new RunHudView.KeyLock();
            // Форма — общий кейкап листа 5 (UiInkKit.Keycap собирает его сразу, старый круглый UiInkKit.KitKeycap
            // переделывает на месте): одна форма на все окна, не своя копия здесь.
            UiInkKit.KitKeycap(cap);
            Transform edgeNode = cap.Find("Кольцо");
            Image edge = edgeNode != null ? edgeNode.GetComponent<Image>() : null;
            if (edge != null)
            {
                // Кромку красит блок ввода (RunHud.KeyLockState): краска темы на включении перебила бы её.
                var tint = edge.GetComponent<ThemeColor>();
                if (tint != null) Object.DestroyImmediate(tint);
                edge.color = new Color(T.PanelLine.r, T.PanelLine.g, T.PanelLine.b, .3f);
            }
            Transform back = cap.Find("Подложка");

            Image glow = Plain(cap, "Свет", RoundGlow, Color.white, Center, Vector2.zero, new Vector2(size * 2.4f, size * 2.4f), false);
            Additive(glow, new Color(1f, .6f, .3f, 0f));
            glow.enabled = false;
            glow.transform.SetSiblingIndex(back != null ? back.GetSiblingIndex() : 0);

            Image ring = Plain(cap, "Кольцо блокировки", T.CircleFrameBold, T.Get(Role.Accent), Center, Vector2.zero, new Vector2(size * 1.55f, size * 1.55f), false);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            ring.gameObject.SetActive(false);
            return new RunHudView.KeyLock { Cap = cap, Letter = letter, Ring = ring, Glow = glow, Edge = edge };
        }

        /// <summary>
        /// «Отказаться» и «Резервный план · перебросить» — одна кнопка: шире, надпись ужимается под длинную подпись,
        /// стоит слева от нижней карточки (справа — подсказка); на узком холсте RunHud прижимает её к краю экрана.
        /// </summary>
        static void PolishSkip(RunHudView view)
        {
            if (view.Skip == null) return;
            var rect = (RectTransform)view.Skip.transform;
            if (rect.sizeDelta.x < 300f) rect.sizeDelta = new Vector2(300f, rect.sizeDelta.y);
            // Справа от карточек теперь подсказка наведённой — кнопка уходит налево, зеркально (если стоит на месте сборки).
            if (rect.anchorMin == new Vector2(.5f, 1f) && rect.anchorMax == rect.anchorMin && Mathf.Abs(rect.anchoredPosition.x - 665f) < 2f)
                rect.anchoredPosition = new Vector2(-665f, rect.anchoredPosition.y);
            else Debug.LogWarning("[ui-kit] «Отказаться» на экране награды переставлена руками — справа её может закрыть подсказка карточки, проверить");
            TMP_Text label = view.Skip.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            label.enableAutoSizing = true;
            label.fontSizeMin = 15f;
            label.fontSizeMax = Mathf.Max(18f, label.fontSize);
        }

        /// <summary>
        /// Подсказка справа от наведённой карточки и вложенная подсказка слова под ней: малая подложка «Дыма и
        /// света» без огненной нити и без огня по кромке (всплывает на каждом наведении).
        /// </summary>
        static void BuildOfferTip(RectTransform screen, RunHudView view)
        {
            // Подложки и строки подсказок — общие (UiInkKit.TipPlate/TipText/KeywordTip), размеры — шкала листа 5.
            RectTransform tip = UiInkKit.TipPlate(screen, "Подсказка награды", TipWidthDefault, new RectOffset(22, 22, 16, 18));
            tip.anchoredPosition = new Vector2(510f, -294f);
            view.OfferTipTitle = UiInkKit.TipText(tip, "Название", "Шквал", FontRole.Heading, T.Size(UiTheme.TextStep.Heading), Role.Text, .04f);
            // Цвет названий подсказок ставит RunHud (редкость карточки, тон слова) — краска темы его не перебивает.
            Object.DestroyImmediate(view.OfferTipTitle.GetComponent<ThemeColor>());
            view.OfferTipBody = UiInkKit.TipText(tip, "Описание", "Рывок сквозь врагов.", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text, .1f);
            view.OfferTipBody.alpha = .92f;
            view.OfferTip = tip;

            RectTransform keyword = UiInkKit.KeywordTip(screen, "Ключевое слово", 340f, out view.KeywordTipTitle, out view.KeywordTipBody);
            keyword.anchoredPosition = new Vector2(510f, -294f);
            view.KeywordTip = keyword;
        }

        // ---------------------------------------------------------------- панель состояния

        static void PolishStatus(RunHudView view)
        {
            if (view.Status == null) return;
            Transform icon = view.Status.Find("Значок тайников");
            view.StatusExtraIcon = icon != null ? icon.GetComponent<Graphic>() : null;
            if (view.StatusExtraIcon == null)
                Debug.LogWarning("[ui-kit] В панели состояния забега нет «Значок тайников» — при «Путь открыт» прятать нечего");
        }
    }
}
