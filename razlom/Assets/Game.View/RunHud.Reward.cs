using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Экран награды (выбор владельца 30.09, кадр 3a): наведённая карточка приподнимается и клонится к курсору,
    /// по ней пробегает перелив цвета её редкости (HudGlint); на карточке — сравнение «было → станет» (вещь —
    /// с надетой и «Надето: …», усиление — число способности до и после, прибавка и родник — число героя);
    /// кейкап набора в углу. Справа от наведённой — подсказка: название и полное описание, ключевые слова
    /// подсвечены (UiKeywords), под ней — вложенная подсказка слова (первого в описании или того, что под мышью).
    ///
    /// БЛОК ВВОДА: экран открылся (или «Резервный план» сменил карточки) — RunHudChoiceLock.DefaultDuration с
    /// ни клик, ни клавиша карточку не берут (<see cref="ChoiceLocked"/>: RequestOffer, «Отказаться»/«Резервный
    /// план», TickDriver.LatchSlots), кольцо на кейкапах наполняется; дошло — кейкапы загораются.
    /// </summary>
    public sealed partial class RunHud
    {
        readonly RunHudChoiceLock _choiceLock = new RunHudChoiceLock();
        static RunHud _choiceOwner;
        ulong _shownOffers;

        const int Cards = 3;
        /// <summary>Наклон к курсору, градусы, и подъём наведённой карточки, единицы холста.</summary>
        const float CardTilt = 1.2f, CardLift = 8f;
        /// <summary>Перелив повторяется, пока карточка под мышью.</summary>
        const float GlintEvery = 2.6f, GlintPeak = .32f;
        const float TipGap = 20f, TipMargin = 16f, TipWidth = 380f, TipMinWidth = 300f, KeywordIndent = 36f, KeywordWidth = 340f;

        readonly Vector2[] _cardRest = new Vector2[Cards];
        readonly float[] _cardLift = new float[Cards];
        readonly float[] _cardTiltNow = new float[Cards];
        bool _cardRestKnown;
        int _hoveredCard = -1;
        float _glintNext, _choiceClockLast;

        // Подсказка: тексты карточек заготовлены при заполнении экрана, не на каждом наведении.
        readonly string[] _tipTitle = new string[Cards];
        readonly string[] _tipBody = new string[Cards];
        readonly UiKeywords.Id[] _tipKeyword = new UiKeywords.Id[Cards];
        readonly Color[] _tipColour = new Color[Cards];
        readonly List<UiKeywords.Id> _found = new List<UiKeywords.Id>(8);
        bool _tipsEnabled;
        int _tipShown = -1, _tipLink = -2;

        /// <summary>Только для съёмки (UiMomentsCapture, -capture-ui-route): номер карточки «под мышью»; −1 — обычная мышь.</summary>
        internal static int CaptureHover = -1;
        static readonly Vector3[] Corners = new Vector3[4];
        UiKeywords.Id _keywordShown, _tipPointed;

        // Черновики сравнения: заводятся раз, считаются при заполнении экрана.
        readonly StatSheet _compareSheet = new StatSheet(64);
        readonly RunHudCompare.StatRow[] _compareRows = new RunHudCompare.StatRow[6];
        readonly AbilityBuild _talentBefore = new AbilityBuild(), _talentAfter = new AbilityBuild();
        readonly AbilityNode[] _talentNodes = new AbilityNode[16];
        readonly GeneratedItem _compareRoll = new GeneratedItem();
        readonly Vector2[] _bodyOffsetMin = new Vector2[Cards];
        readonly bool[] _bodyKnown = new bool[Cards];

        /// <summary>
        /// Экран выбора только открылся: клавиши и клики карточку ещё не берут. Экран не заполнен (фаза сменилась
        /// в этом кадре) — тоже закрыто. Нет префаба (запасной IMGUI) — блока нет.
        /// </summary>
        public static bool ChoiceLocked => _choiceOwner != null && _choiceOwner.IsChoiceLocked();

        private bool IsChoiceLocked()
        {
            if (_view == null || _driver == null) return false;
            RiftRun run = _driver.Run;
            if (run == null || (run.Phase != RunPhase.ChoosingReward && run.Phase != RunPhase.ChoosingRoute)) return false;
            if (_shownPhase != run.Phase || _shownDepth != run.Depth) return true;
            return _choiceLock.Locked(UiMotion.Now);
        }

        /// <summary>Подпись трёх карточек: «Резервный план» меняет их без смены фазы.</summary>
        private static ulong OffersSignature(RiftRun run)
        {
            ulong hash = Hashing.Offset;
            for (int i = 0; i < RiftRun.RewardChoices; i++) run.GetOffer(i).HashInto(ref hash);
            return hash;
        }

        /// <summary>Экран открылся или карточки сменились: блок ввода, кольца с нуля, подсказка закрыта.</summary>
        private void OpenChoice(bool rerolled)
        {
            _choiceOwner = this;
            _choiceLock.Start(UiMotion.Now);
            HideTips();
            KeepSkipOnScreen();
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                KeyLockState(card.KeyLock, 0f, false);
                if (rerolled && card.gameObject.activeInHierarchy) HudFx.Punch(card.transform, 1.04f, .35f);
            }
        }

        /// <summary>Каждый кадр: блок и кейкапы, наведение, наклон, перелив, подсказки.</summary>
        private void UpdateChoice(bool choosing)
        {
            float now = UiMotion.Now;
            float dt = Mathf.Clamp(now - _choiceClockLast, 0f, .1f);
            _choiceClockLast = now;
            RememberCardRest();
            if (!choosing)
            {
                _choiceLock.Clear();
                if (_hoveredCard >= 0 || _tipShown >= 0) HideTips();
                _hoveredCard = -1;
                SettleCards();
                return;
            }

            float progress = _choiceLock.Progress(now);
            bool released = _choiceLock.TakeRelease(now);
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null || card.KeyLock == null) continue;
                if (released) KeyLockRelease(card.KeyLock);
                else if (progress < 1f) KeyLockState(card.KeyLock, progress, false);
            }

            bool pointed = PointerPosition(out Vector2 mouse);
            int hover = _replaceOffer >= 0 || TickDriver.GamepadLastUsed || !pointed ? -1 : CardAt(mouse);
            // Только съёмка (UiMomentsCapture): карточка «под мышью» без мыши — наклон, перелив и подсказка на кадре.
            if (CaptureHover >= 0 && CaptureHover < Cards && _replaceOffer < 0 && CaptureHover < _view.Offers.Length
                && _view.Offers[CaptureHover] != null)
            {
                hover = CaptureHover;
                ((RectTransform)_view.Offers[hover].transform).GetWorldCorners(Corners);
                mouse = Vector2.Lerp(Corners[0], Corners[2], .8f);
            }
            // Мышь ушла с карточки на её подсказку — подсказка остаётся, карточка — поднятой.
            if (hover < 0 && _tipShown >= 0 && _replaceOffer < 0 && PointerOnTips(mouse)) hover = _tipShown;
            if (hover != _hoveredCard)
            {
                _hoveredCard = hover;
                if (hover >= 0) StartGlint(hover, now);
                if (hover >= 0 && _tipsEnabled && !string.IsNullOrEmpty(_tipBody[hover])) ShowTip(hover);
                else HideTips();
            }
            else if (hover >= 0 && now >= _glintNext) StartGlint(hover, now);
            TiltCards(dt, mouse);
            if (_tipShown >= 0) KeywordUnderPointer(mouse);
        }

        /// <summary>«Отказаться / Резервный план» не уходит за край узкого холста (масштаб 120%, 16:10).</summary>
        private void KeepSkipOnScreen()
        {
            if (_view.Skip == null) return;
            var rect = (RectTransform)_view.Skip.transform;
            if (rect.anchorMin.x != .5f || rect.anchorMax.x != .5f) return;
            float half = ((RectTransform)_view.transform).rect.width * .5f;
            float edge = Mathf.Max(0f, half - TipMargin - rect.sizeDelta.x * (1f - rect.pivot.x));
            float left = Mathf.Min(0f, -half + TipMargin + rect.sizeDelta.x * rect.pivot.x);
            float x = Mathf.Clamp(rect.anchoredPosition.x, left, edge);
            if (!Mathf.Approximately(x, rect.anchoredPosition.x)) rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        }

        // ---------------------------------------------------------------- кейкапы и блок

        /// <summary>Кейкап во время блока: кольцо наполняется, кромка и буква тусклые.</summary>
        private static void KeyLockState(RunHudView.KeyLock key, float progress, bool open)
        {
            if (key == null) return;
            UiTheme theme = UiTheme.Current;
            if (key.Ring != null)
            {
                bool ring = !open && KeyIsRound(key);
                RunHudView.SetActive(key.Ring, ring);
                if (ring) key.Ring.fillAmount = progress;
            }
            if (key.Edge != null)
            {
                Color edge = open ? theme.Get(UiTheme.Role.Accent) : theme.Get(UiTheme.Role.PanelLine);
                edge.a = open ? .85f : .3f;
                key.Edge.color = edge;
            }
            if (key.Letter != null) key.Letter.color = theme.Get(open ? UiTheme.Role.Text : UiTheme.Role.TextMuted);
        }

        /// <summary>Кольцо — вокруг квадратного кейкапа; у длинной подписи («Space», «Mouse4») его нет.</summary>
        private static bool KeyIsRound(RunHudView.KeyLock key)
            => key.Cap == null || key.Cap.sizeDelta.x <= key.Cap.sizeDelta.y + 1f;

        /// <summary>Блок кончился: кольцо гаснет, кейкап загорается — вспышка по FlashScale.</summary>
        private static void KeyLockRelease(RunHudView.KeyLock key)
        {
            if (key == null) return;
            KeyLockState(key, 1f, true);
            if (key.Cap != null) HudFx.Punch(key.Cap, 1.18f, .4f);
            if (key.Glow == null) return;
            Color glow = UiTheme.Current.Get(UiTheme.Role.Accent);
            glow.a = 0f;
            key.Glow.color = glow;
            HudFx.Burst(key.Glow, .75f, .8f, 1.9f, .6f);
        }

        // ---------------------------------------------------------------- наведение, наклон, перелив

        private void RememberCardRest()
        {
            if (_cardRestKnown) return;
            _cardRestKnown = true;
            for (int i = 0; i < Cards && i < _view.Offers.Length; i++)
                if (_view.Offers[i] != null) _cardRest[i] = ((RectTransform)_view.Offers[i].transform).anchoredPosition;
        }

        private int CardAt(Vector2 pointer)
        {
            for (int i = 0; i < Cards && i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)card.transform, pointer, null)) return i;
            }
            return -1;
        }

        private bool PointerOnTips(Vector2 pointer)
            => _view.OfferTip != null && _view.OfferTip.gameObject.activeInHierarchy
               && RectTransformUtility.RectangleContainsScreenPoint(_view.OfferTip, pointer, null)
               || _view.KeywordTip != null && _view.KeywordTip.gameObject.activeInHierarchy
               && RectTransformUtility.RectangleContainsScreenPoint(_view.KeywordTip, pointer, null);

        /// <summary>Наведённая карточка поднимается и клонится стороной к курсору; остальные мягко возвращаются.</summary>
        private void TiltCards(float dt, Vector2 pointer)
        {
            float k = 1f - Mathf.Exp(-dt * 14f);
            for (int i = 0; i < Cards && i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                var rect = (RectTransform)card.transform;
                float tilt = 0f, lift = 0f;
                if (i == _hoveredCard)
                {
                    lift = CardLift;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer, null, out Vector2 local))
                    {
                        float half = Mathf.Max(1f, rect.rect.width * .5f);
                        float nx = Mathf.Clamp((local.x - rect.rect.center.x) / half, -1f, 1f);
                        tilt = nx * CardTilt;
                    }
                }
                _cardLift[i] = Mathf.Lerp(_cardLift[i], lift, k);
                _cardTiltNow[i] = Mathf.Lerp(_cardTiltNow[i], tilt, k);
                ApplyCardPose(i, rect);
            }
        }

        /// <summary>Экран закрыт: карточки на своих местах, без наклона.</summary>
        private void SettleCards()
        {
            for (int i = 0; i < Cards && i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null || _cardLift[i] == 0f && _cardTiltNow[i] == 0f) continue;
                _cardLift[i] = 0f;
                _cardTiltNow[i] = 0f;
                ApplyCardPose(i, (RectTransform)card.transform);
            }
        }

        private void ApplyCardPose(int i, RectTransform rect)
        {
            if (Mathf.Abs(_cardLift[i]) < .01f) _cardLift[i] = 0f;
            if (Mathf.Abs(_cardTiltNow[i]) < .001f) _cardTiltNow[i] = 0f;
            rect.anchoredPosition = _cardRest[i] + new Vector2(0f, _cardLift[i]);
            rect.localRotation = Quaternion.Euler(0f, 0f, _cardTiltNow[i]);
        }

        /// <summary>Перелив цвета редкости по наведённой карточке; повтор, пока мышь на ней. Сила — FlashScale.</summary>
        private void StartGlint(int index, float now)
        {
            _glintNext = now + GlintEvery;
            RunOfferCard card = _view.Offers[index];
            if (card == null || card.Glint == null || card.Glint.Stripe == null) return;
            Color colour = RarityLight(card);
            colour.a = 0f;
            card.Glint.Stripe.color = colour;
            card.Glint.Peak = GlintPeak * GameUserSettings.FlashScale;
            card.Glint.Play();
        }

        /// <summary>
        /// Свет редкости: у обычной — тёплый кремовый (серый перелив читался пылью), у остальных — цвет редкости,
        /// у отмеченной карточки формы — акцент (RunHud.Forms).
        /// </summary>
        private static Color RarityLight(RunOfferCard card)
        {
            if (card.Rarity == null || card.Rarity.Plain) return new Color(1f, .9f, .75f, 1f);
            return UiTheme.Current.Get(card.Rarity.Role);
        }

        // ---------------------------------------------------------------- тексты карточек

        /// <summary>
        /// Сравнение и подсказка карточки награды. <paramref name="body"/> — полное описание (RunHud.Describe):
        /// оно уходит в подсказку, на карточке — сравнение и начало описания, ключевые слова подсвечены.
        /// </summary>
        private void PolishOffer(int index, RunOfferCard card, in RewardOffer offer, RiftRun run, string body)
        {
            if (index >= Cards || card == null) return;
            _tipsEnabled = true;
            string good = Hex(UiTheme.Role.Good), bad = Hex(UiTheme.Role.Bad), muted = Hex(UiTheme.Role.TextMuted);
            string compare = string.Empty;
            bool expand = false;
            string tipBody = body;
            StatSheet hero = run.Sim.Entities.Stats[Simulation.PlayerId];
            switch (offer.Kind)
            {
                case RewardKind.Item:
                {
                    int rows = RunHudCompare.ItemRows(hero, in offer.Item, run.Items, _compareRoll, _compareSheet, _compareRows, out EquipSlot slot);
                    if (rows < 0) break;
                    var lines = new System.Text.StringBuilder();
                    for (int r = 0; r < rows && r < 2; r++) lines.Append(StatLine(_compareRows[r], good, bad)).Append('\n');
                    ItemInstance worn = run.PlayerEquipment != null ? run.PlayerEquipment.Worn(slot) : default;
                    string wornName = worn.IsEmpty ? null : ItemTexts.Name(worn.BaseId);
                    lines.Append("<color=").Append(muted).Append("><size=90%>").Append(RunHudCompare.Worn(wornName)).Append("</size></color>");
                    compare = lines.ToString();
                    expand = true;
                    // В подсказке — свойства вещи строками и все изменения, не только два.
                    var tip = new System.Text.StringBuilder(body.Replace(" · ", "\n"));
                    for (int r = 0; r < rows; r++) tip.Append(r == 0 ? "\n\n" : "\n").Append(StatLine(_compareRows[r], good, bad));
                    tipBody = tip.ToString();
                    break;
                }
                case RewardKind.Talent:
                    if (RunHudCompare.TalentRow(run.Loadout, offer.PoolIndex, offer.TalentIndex, _talentBefore, _talentAfter, _talentNodes,
                            out RunHudCompare.AbilityRow row) && SabreTalents.TryLineOf(offer.PoolIndex, out SabreTalentLine line))
                        compare = RunHudCompare.Line(RunHudCompare.AbilityStatName(line, row.Stat), RunHudCompare.AbilityStatValue(row.Stat, row.Before),
                            RunHudCompare.AbilityStatValue(row.Stat, row.After), row.Direction, row.Better, good, bad);
                    break;
                case RewardKind.StatBoost:
                    if (RunHudCompare.StatBoostRow(hero, offer.Stat, offer.Op, offer.Value, _compareSheet, out RunHudCompare.StatRow boost))
                        compare = StatLine(boost, good, bad);
                    break;
                case RewardKind.Spring:
                {
                    int health = run.Sim.Entities.Health[Simulation.PlayerId];
                    int max = run.Sim.Entities.MaxHealth[Simulation.PlayerId];
                    int after = Mathf.Min(max, health + run.SpringHealAmount);
                    compare = RunHudCompare.Line(StatText.Name(StatType.MaxHealth), health.ToString(), after.ToString(),
                        after > health ? 1 : 0, after > health ? 1 : 0, good, bad);
                    break;
                }
            }
            SetOfferBody(index, card, compare.Length > 0 ? compare + (expand ? "" : "\n" + UiKeywords.Themed(body)) : UiKeywords.Themed(body), expand);
            SetTip(index, card, tipBody);
        }

        /// <summary>Артефакт босса: описание эффекта с ключевыми словами на карточке и в подсказке.</summary>
        private void PolishArtifact(int index, RunOfferCard card, string effect)
        {
            if (index >= Cards || card == null) return;
            _tipsEnabled = true;
            SetOfferBody(index, card, UiKeywords.Themed(effect), false);
            SetTip(index, card, effect);
        }

        /// <summary>Выбор арены: всё уже на карточке — подсказки нет.</summary>
        private void PolishRoute()
        {
            _tipsEnabled = false;
            HideTips();
            for (int i = 0; i < Cards && i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                _tipBody[i] = null;
                ExpandBody(i, card, false);
            }
        }

        private string StatLine(in RunHudCompare.StatRow row, string good, string bad)
            => RunHudCompare.Line(StatText.Name(row.Stat), StatText.Value(row.Stat, row.Before), StatText.Value(row.Stat, row.After),
                row.Direction, row.Direction, good, bad);

        private void SetOfferBody(int index, RunOfferCard card, string text, bool expand)
        {
            if (card.Description == null) return;
            RunHudView.SetText(card.Description, text);
            card.Description.overflowMode = TextOverflowModes.Ellipsis;
            ExpandBody(index, card, expand);
        }

        /// <summary>У вещи строки значения нет — описание занимает и её место (три строки сравнения).</summary>
        private void ExpandBody(int index, RunOfferCard card, bool expand)
        {
            if (card.Description == null || !(card.Description.transform.parent is RectTransform body) || body.name != "Описание") return;
            if (!_bodyKnown[index])
            {
                _bodyKnown[index] = true;
                _bodyOffsetMin[index] = body.offsetMin;
            }
            Vector2 min = expand ? new Vector2(_bodyOffsetMin[index].x, 0f) : _bodyOffsetMin[index];
            if (body.offsetMin != min) body.offsetMin = min;
        }

        private void SetTip(int index, RunOfferCard card, string body)
        {
            _found.Clear();
            _tipBody[index] = string.IsNullOrEmpty(body) ? null : UiKeywords.Themed(body, _found);
            _tipTitle[index] = card.Title != null ? card.Title.text : string.Empty;
            _tipKeyword[index] = _found.Count > 0 ? _found[0] : UiKeywords.Id.None;
            _tipColour[index] = card.Rarity != null && !card.Rarity.Plain
                ? UiTheme.Current.Get(card.Rarity.Role)
                : UiTheme.Current.Get(UiTheme.Role.Text);
            if (_tipShown == index) _tipShown = -1;
        }

        private static string Hex(UiTheme.Role role) => "#" + ColorUtility.ToHtmlStringRGB(UiTheme.Current.Get(role));

        // ---------------------------------------------------------------- подсказка справа

        private void ShowTip(int index)
        {
            RectTransform tip = _view.OfferTip;
            if (tip == null || _view.OfferTipBody == null) return;
            _tipShown = index;
            _tipLink = -2;
            RunHudView.SetText(_view.OfferTipTitle, _tipTitle[index]);
            RunHudView.SetText(_view.OfferTipBody, _tipBody[index]);
            bool reopen = !tip.gameObject.activeSelf;
            RunHudView.SetActive(tip, true);
            // Цвет — после включения: краска темы на включении вернула бы обычный.
            if (_view.OfferTipTitle != null) _view.OfferTipTitle.color = _tipColour[index];
            if (!reopen)
            {
                // Между карточками — только новый текст и место, без второго проявления.
                var ink = tip.GetComponent<UiInkGroup>();
                if (ink != null && ink.Running) ink.ShowInstant();
            }
            ShowKeyword(_tipKeyword[index], false);
            PlaceTip(index);
        }

        private void HideTips()
        {
            _tipShown = -1;
            _tipLink = -2;
            _keywordShown = UiKeywords.Id.None;
            RunHudView.SetActive(_view.OfferTip, false);
            RunHudView.SetActive(_view.KeywordTip, false);
        }

        /// <summary>
        /// Подсказка — справа от карточки, верхом вровень с ней. Не влезает справа (масштаб 120%, 16:10) —
        /// у правого края экрана поверх края карточки; снизу не выходит за экран вместе с вложенной.
        /// </summary>
        private void PlaceTip(int index)
        {
            RectTransform tip = _view.OfferTip;
            RunOfferCard card = _view.Offers[index];
            if (tip == null || card == null) return;
            var cardRect = (RectTransform)card.transform;
            var canvas = (RectTransform)_view.transform;
            float half = canvas.rect.width * .5f, height = canvas.rect.height;
            float cardRight = _cardRest[index].x + (1f - cardRect.pivot.x) * cardRect.sizeDelta.x;
            float cardTop = _cardRest[index].y + (1f - cardRect.pivot.y) * cardRect.sizeDelta.y;
            float x = cardRight + TipGap;
            float room = half - TipMargin - x;
            float width = Mathf.Min(TipWidth, room);
            bool overCard = width < TipMinWidth;
            if (overCard)
            {
                width = TipMinWidth + 40f;
                x = half - TipMargin - width;
            }
            tip.sizeDelta = new Vector2(width, tip.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip);
            float tipHeight = tip.rect.height;
            float keyword = 0f;
            if (_view.KeywordTip != null && _view.KeywordTip.gameObject.activeSelf)
            {
                // Высота вложенной — по её ширине: ширину ставим до пересчёта.
                _view.KeywordTip.sizeDelta = new Vector2(Mathf.Min(width, KeywordWidth), _view.KeywordTip.sizeDelta.y);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_view.KeywordTip);
                keyword = _view.KeywordTip.rect.height + 12f;
            }
            // Сверху вниз от верха холста: карточки стоят от верхнего края.
            float top = -cardTop;
            // Поверх края карточки (120%, 16:10) — ниже её кейкапа: подсказка закрывала клавишу выбора (проверка 30.09).
            if (overCard && card.KeyLock != null && card.KeyLock.Cap != null)
            {
                card.KeyLock.Cap.GetWorldCorners(Corners);
                float capBottom = canvas.rect.yMax - canvas.InverseTransformPoint(Corners[0]).y;
                top = Mathf.Max(top, capBottom + 10f);
            }
            top = Mathf.Min(top, height - 24f - tipHeight - keyword);
            top = Mathf.Max(top, 16f);
            tip.anchoredPosition = new Vector2(x, -top);
            PlaceKeyword(x, top + tipHeight + 12f, width);
        }

        private void PlaceKeyword(float x, float top, float width)
        {
            RectTransform keyword = _view.KeywordTip;
            if (keyword == null) return;
            float w = Mathf.Min(width, KeywordWidth);
            float half = ((RectTransform)_view.transform).rect.width * .5f;
            float kx = Mathf.Min(x + KeywordIndent, half - TipMargin - w);
            keyword.sizeDelta = new Vector2(w, keyword.sizeDelta.y);
            keyword.anchoredPosition = new Vector2(kx, -top);
        }

        /// <summary>
        /// Вложенная подсказка слова: название цветом слова и определение (в нём тоже подсвечены слова). Показ — общий
        /// (UiKeywordTip), тот же, что у подсказок способности и артефакта в боевом HUD.
        /// </summary>
        private void ShowKeyword(UiKeywords.Id id, bool place)
        {
            if (!UiKeywordTip.Show(_view.KeywordTip, _view.KeywordTipTitle, _view.KeywordTipBody, id, ref _keywordShown)) return;
            if (place && _tipShown >= 0) PlaceTip(_tipShown);
        }

        /// <summary>Слово под мышью в описании подсказки — его определение во вложенной; ушла — первое слово описания.</summary>
        private void KeywordUnderPointer(Vector2 pointer)
        {
            if (_view.OfferTipBody == null || _tipShown < 0) return;
            UiKeywords.Id id = UiKeywordTip.Pointed(_view.OfferTipBody, pointer, _tipKeyword[_tipShown], ref _tipLink, ref _tipPointed);
            if (id != _keywordShown) ShowKeyword(id, true);
        }
    }
}
