using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Кузница Эни вкладками внутри окна кузнеца (06.10, temper-a.png; префаб CampShopsWc v4): «Закалить · Переплавить ·
    /// Добавить свойство · Сердце · Разобрать». Перенос логики минимального CampForgeView во вкладки: цена, риск и допуск —
    /// из Camp.Quote через CampTemperRules (до клика, исход броска окно не знает), действия — Strike/TakeTemper/RiskyStrike/
    /// BeginRemelt/BeginAdd/ChooseSessionCandidate/InlayHeart/Dismantle. Вещь и вкладка — состояние окна, перерисовка только
    /// по событию. Префаб до v4 (Temper.Root пуст) — прежняя одна страница и CampForgeView (CampServicesView.Smith).
    /// Esc (ловушка 5 ui-common): снять подтверждение → «Взять», если идёт закалка → закрыть окно.
    /// </summary>
    public sealed partial class CampServicesView
    {
        EniTab _temperTab;
        int _temperProperty, _temperChoice = -1;
        bool _temperConfirm, _temperWired;
        float _temperLockUntil;
        string _temperStatus = "";
        UiTheme.Role _temperStatusRole = UiTheme.Role.TextMuted;
        // Какое свойство стоит за карточкой: −1 — базовое обычной вещи; варианты и грани — номер варианта.
        readonly int[] _temperCardProperty = new int[8];
        float _pipCenter, _pipStep = 56f;
        Texture[] _temperFacetArt;
        readonly GeneratedItem _temperRoll = new GeneratedItem(), _temperAfter = new GeneratedItem();

        /// <summary>Префаб с кузницей вкладками (миграция v4): окно кузнеца работает по ней.</summary>
        internal bool TemperTabs => _view != null && _view.Temper != null && _view.Temper.Root != null;

        static string TemperText(string key, string ru) => CampWindowText.Get("temper." + key, ru);
        static string TemperFormat(string key, string ru, params object[] args) => CampWindowText.Format("temper." + key, ru, args);

        ForgeTarget TemperTarget => _smithWorn ? ForgeTarget.Worn((EquipSlot)_smithSlot) : ForgeTarget.Bag(_smithSlot);

        void WireTemper()
        {
            if (_temperWired) return;
            _temperWired = true;
            var t = _view.Temper;
            for (int i = 0; i < t.Tabs.Length; i++)
            {
                int tab = i;
                if (t.Tabs[i] != null) t.Tabs[i].onClick.AddListener(() => PickTemperTab((EniTab)tab));
            }
            for (int i = 0; i < t.Cards.Length; i++)
            {
                int card = i;
                if (t.Cards[i]?.Button != null) t.Cards[i].Button.onClick.AddListener(() => PickTemperCard(card));
            }
            if (t.Primary != null) t.Primary.onClick.AddListener(ApplyTemper);
            if (t.Secondary != null) t.Secondary.onClick.AddListener(TemperSecondary);
            // Отметки попыток стоят по центру колонки: середина и шаг — из собранного префаба (ручные правки владельца).
            if (t.Pips.Length > 1 && t.Pips[0]?.Root != null && t.Pips[t.Pips.Length - 1]?.Root != null)
            {
                float first = ((RectTransform)t.Pips[0].Root.transform).anchoredPosition.x;
                float last = ((RectTransform)t.Pips[t.Pips.Length - 1].Root.transform).anchoredPosition.x;
                _pipCenter = (first + last) * .5f;
                _pipStep = (last - first) / (t.Pips.Length - 1);
            }
            // Окончательный арт стали, сердца и граней кладут в Resources — без пересборки префаба.
            Texture steel = Resources.Load<Texture2D>("UI/CampCurrency/steel"), heart = Resources.Load<Texture2D>("UI/CampCurrency/heart");
            foreach (RawImage[] row in new[] { t.PriceIcons, t.WalletIcons })
            {
                if (steel != null && row.Length > 2 && row[2] != null) { row[2].texture = steel; row[2].enabled = true; }
                if (heart != null && row.Length > 3 && row[3] != null) { row[3].texture = heart; row[3].enabled = true; }
            }
            string[] facets = { "roots", "pollen", "bloom" };
            _temperFacetArt = new Texture[facets.Length];
            for (int i = 0; i < facets.Length; i++)
            {
                Texture final = Resources.Load<Texture2D>("UI/HeartFacets/" + facets[i]);
                _temperFacetArt[i] = final != null ? final : i < t.FacetIcons.Length ? t.FacetIcons[i] : null;
            }
        }

        /// <summary>
        /// Открытие окна Эни с кузницей вкладками. Закалка прошлого визита закрыта с набранным (закрыть окно — «взять»; рост
        /// уже в вещи). Вещь — ждущего выбора (вкладка сразу на нём), открытой закалки или надетое оружие.
        /// </summary>
        void OpenTemper()
        {
            var camp = _smithCamp;
            camp.SettleForgeSession();
            _smithSlot = CampTemperRules.DefaultTarget(camp, out _smithWorn);
            _temperTab = CampTemperRules.ChoicePending(camp) ? CampTemperRules.PendingTab(camp) : EniTab.Temper;
            _temperChoice = -1;
            _temperConfirm = false;
            _temperLockUntil = 0f;
            _temperProperty = TemperDefaultProperty();
            TemperStatus("", UiTheme.Role.TextMuted);
            var t = _view.Temper;
            if (t.BagScroll != null) t.BagScroll.verticalNormalizedPosition = 1f;
            if (t.PrimaryKey != null) UiKeyHint.SetKeycap(t.PrimaryKey, GameKeyBindings.Label(GameAction.Interact));
        }

        void TemperStatus(string text, UiTheme.Role role)
        {
            _temperStatus = text ?? "";
            _temperStatusRole = role;
        }

        /// <summary>Свойство при выборе вещи: свойство открытой закалки, у обычной — базовое (−1), иначе первое, которое растёт.</summary>
        int TemperDefaultProperty()
        {
            var camp = _smithCamp;
            if (_smithSlot < 0) return 0;
            var session = camp.Session;
            if (session.IsOpen && session.Target.Same(TemperTarget)) return session.Property;
            var item = WornOrBag(camp, _smithSlot, _smithWorn);
            int count = !item.IsEmpty && ItemGenerator.Generate(item, camp.Items, _temperRoll) ? _temperRoll.AffixCount : 0;
            return CampShopDeals.DefaultAffix(camp, _smithSlot, _smithWorn, count);
        }

        /// <summary>
        /// Смена вещи или вкладки при открытой закалке — сначала «Взять» (рост уже в вещи): Strike по другому свойству или
        /// вещи иначе тихо закрыл бы её и заплатил за новую.
        /// </summary>
        void TakeTemperBeforeSwitch()
        {
            if (_smithSlot < 0 || !CampTemperRules.TemperOpenOn(_smithCamp, TemperTarget) || _smithCamp.Session.Strikes <= 0) return;
            if (_smithCamp.TakeTemper() == SmithResult.Success)
                TemperStatus(TemperText("taken.switch", "Закалка закончена: рост сохранён"), UiTheme.Role.Accent);
        }

        void PickTemperItem(int slot, bool worn)
        {
            if (slot == _smithSlot && worn == _smithWorn) return;
            // Оплаченный выбор запирает вещь: ячейки спят, а этот путь — для проб и щелчка до перерисовки.
            if (CampTemperRules.ChoicePending(_smithCamp)) return;
            TakeTemperBeforeSwitch();
            if (_temperStatus.Length > 0 && _temperStatusRole != UiTheme.Role.Accent) TemperStatus("", UiTheme.Role.TextMuted);
            _smithSlot = slot;
            _smithWorn = worn;
            _temperChoice = -1;
            _temperConfirm = false;
            _temperProperty = TemperDefaultProperty();
            _view.Smith.Message.text = "";
            RefreshSmith();
        }

        void PickTemperTab(EniTab tab)
        {
            UiSound.Play(UiSoundEvent.Tab);
            if (tab == _temperTab) return;
            if (tab != EniTab.Temper) TakeTemperBeforeSwitch();
            else TemperStatus("", UiTheme.Role.TextMuted);
            _temperTab = tab;
            _temperChoice = -1;
            _temperConfirm = false;
            _temperProperty = TemperDefaultProperty();
            RefreshSmith();
        }

        void PickTemperCard(int card)
        {
            var plan = TemperPlan();
            if (plan.Step == TemperStep.Choose || plan.Step == TemperStep.Heart || _temperTab == EniTab.Heart) _temperChoice = card;
            else if (_temperTab == EniTab.Temper || _temperTab == EniTab.Remelt)
            {
                int property = _temperCardProperty[card];
                // Идёт закалка — другое свойство заперто (карточка спит); здесь — страховка от щелчка до перерисовки.
                if (CampTemperRules.TemperOpenOn(_smithCamp, TemperTarget) && _smithCamp.Session.Property != property) return;
                _temperProperty = property;
            }
            else return;
            _temperConfirm = false;
            TemperStatus("", UiTheme.Role.TextMuted);
            RefreshSmith();
        }

        CampTemperRules.Plan TemperPlan() => CampTemperRules.PlanTab(_smithCamp, _temperTab, _smithSlot, _smithWorn, _temperProperty, _temperChoice);

        // ---------------------------------------------------------------- перерисовка

        void RefreshTemper()
        {
            var s = _view.Smith; var t = _view.Temper; var camp = _smithCamp; var inventory = GetComponent<CampInventoryView>();
            s.Title.text = TemperText("title", "Кузница");
            int boss = CampTemperRules.HeartBoss(camp);
            int[] wallet = { camp.Money(CurrencyType.Gold), camp.Money(CurrencyType.Shards), camp.Money(CurrencyType.Steel), camp.HeartCount(boss) };
            for (int i = 0; i < t.WalletValues.Length && i < wallet.Length; i++)
            {
                if (t.WalletValues[i] == null) continue;
                t.WalletValues[i].text = wallet[i].ToString();
                // Сердца — только когда есть: до первой победы над боссом строка кошелька короче.
                if (i == 3) Show(t.WalletValues[i].transform.parent.gameObject, wallet[i] > 0);
            }

            bool pending = CampTemperRules.ChoicePending(camp);
            var session = camp.Session;
            s.GridCaption.text = Format("trader.bag.count", SmithText("bag.caption"), camp.Bag.Used, camp.Bag.Capacity);
            for (int i = 0; i < s.Cells.Length; i++)
            {
                var value = i < camp.Bag.Capacity ? camp.Bag.At(i) : default;
                bool held = pending && session.Target.Same(ForgeTarget.Bag(i));
                s.Cells[i].Button.interactable = !value.IsEmpty && !pending;
                s.Cells[i].Show(value.IsEmpty ? null : inventory.SpriteFor(value), value.IsEmpty ? "" : value.ItemLevel.ToString(), (int)value.Rarity,
                    i == _smithSlot && !_smithWorn);
                s.Cells[i].SetDimmed(pending && !value.IsEmpty && !held);
            }
            ShowWorn(s, camp, _smithWorn ? _smithSlot : -1, pending);

            var item = WornOrBag(camp, _smithSlot, _smithWorn);
            if (item.IsEmpty && _smithSlot >= 0) { _smithSlot = -1; _smithWorn = false; }
            var plan = TemperPlan();
            if (!plan.NeedsConfirm || !plan.CanAct) _temperConfirm = false;

            RefreshTemperTabs(t, camp, pending);
            RefreshTemperAnvil(t, camp, inventory, item, plan);
            RefreshTemperCards(t, camp, item, plan, boss);
            RefreshTemperButtons(s, t, plan);
            RefreshTemperPrice(t, plan);
            RefreshTemperWarning(t, camp, inventory, plan);
        }

        void RefreshTemperTabs(CampTemperScreen t, Camp camp, bool pending)
        {
            for (int i = 0; i < t.Tabs.Length; i++)
            {
                var tab = (EniTab)i;
                bool open = CampTemperRules.TabOpen(camp, tab);
                CampShopView.SetTab(t.Tabs[i], tab == _temperTab);
                // Закрытая рангом — тусклая подпись, замок и «Эни · ранг N»; нажимается (покажет, чем открывается).
                if (!open && tab != _temperTab && t.Tabs[i].transform.Find("Надпись")?.GetComponent<ThemeColor>() is ThemeColor label)
                    label.SetRole(UiTheme.Role.TextMuted, .6f);
                if (i < t.TabLocks.Length) Show(t.TabLocks[i], !open);
                if (i < t.TabLockLabels.Length && !open)
                    Say(t.TabLockLabels[i], TemperFormat("tab.rank", "Эни · ранг {0}", CampTemperRules.TabRank(tab)));
            }
        }

        void RefreshTemperAnvil(CampTemperScreen t, Camp camp, CampInventoryView inventory, ItemInstance item, in CampTemperRules.Plan plan)
        {
            bool risky = plan.Step == TemperStep.Risky, overflow = plan.Step == TemperStep.Pay && plan.Overflow;
            int risk = plan.Step == TemperStep.Strike || plan.Step == TemperStep.NextStrike || risky || overflow ? plan.RiskPercent : 0;
            Say(t.RiskCaption, risky ? TemperText("risk.shatter", "Риск рассыпаться") : overflow ? TemperText("risk.overflow", "Риск перелива")
                : TemperText("risk.crack", "Риск трещины"));
            Say(t.RiskValue, risk + "%");
            bool bad = CampTemperRules.RiskIsBad(risk);
            Tone(t.RiskValue, bad ? UiTheme.Role.Bad : UiTheme.Role.Text);
            if (t.RiskFill != null)
            {
                // Огонь дуги дотекает до нового риска, а не прыгает: удар читается по дуге.
                Image fill = t.RiskFill;
                float from = fill.fillAmount, to = CampTemperRules.RiskFill(risk);
                fill.color = bad && UiTheme.Current != null ? UiTheme.Current.Get(UiTheme.Role.Bad) : Color.white;
                if (!Mathf.Approximately(from, to)) UiMotion.Play(fill, 6, .3f, k => fill.fillAmount = Mathf.Lerp(from, to, k));
            }

            Sprite sprite = item.IsEmpty ? null : inventory.SpriteFor(item);
            bool anvil = t.Anvil != null && t.Anvil.texture != null;
            if (t.AnvilItem != null) { t.AnvilItem.sprite = sprite; t.AnvilItem.enabled = anvil && sprite != null; }
            if (t.ItemMedal?.Root != null) Show(t.ItemMedal.Root, !anvil);
            if (t.ItemMedalArt != null) { t.ItemMedalArt.sprite = sprite; t.ItemMedalArt.enabled = !anvil && sprite != null; }
            Show(t.Embers, sprite != null);
            if (item.IsEmpty)
            {
                Say(t.ItemName, TemperText("choose", "Выбери вещь"));
                Say(t.ItemMeta, TemperText("choose.hint", "Сумка и надетое — справа"));
            }
            else
            {
                Say(t.ItemName, inventory.ItemName(item.BaseId));
                string state = camp.IsMasterpiece(item) ? "  ·  " + Paint(TemperText("masterpiece", "шедевр"), UiTheme.Role.Accent)
                    : camp.IsShattered(item) ? "  ·  " + Paint(TemperText("shattered", "расколота"), UiTheme.Role.Bad) : "";
                Say(t.ItemMeta, RarityAndLevel(item) + (_smithWorn ? WornTag : "") + state);
            }

            int limit = item.IsEmpty ? 0 : plan.AttemptLimit, shown = Mathf.Min(limit, t.Pips.Length);
            for (int i = 0; i < t.Pips.Length; i++)
            {
                var pip = t.Pips[i];
                if (pip?.Root == null) continue;
                var state = CampTemperRules.Pip(i, plan.AttemptsUsed, limit, plan.Cracks);
                Show(pip.Root, state != TemperPip.Hidden);
                if (state == TemperPip.Hidden) continue;
                var rect = (RectTransform)pip.Root.transform;
                rect.anchoredPosition = new Vector2(CampTemperRules.PipX(i, shown, _pipCenter, _pipStep), rect.anchoredPosition.y);
                Show(pip.Fire, state == TemperPip.Spent);
                if (pip.Art != null) pip.Art.enabled = state == TemperPip.Crack && pip.Art.texture != null;
            }
            Say(t.AttemptsLabel, limit <= 0 ? "" : TemperFormat("attempts", "Попытки {0} из {1}", plan.AttemptsUsed, limit)
                + (plan.Cracks > 0 ? "  ·  " + TemperFormat("cracks", "трещины {0} из {1}", plan.Cracks, Camp.CracksToShatter) : ""));
        }

        void RefreshTemperCards(CampTemperScreen t, Camp camp, ItemInstance item, in CampTemperRules.Plan plan, int boss)
        {
            float numbers = UiTheme.Current != null ? UiTheme.Current.Size(UiTheme.TextStep.Heading) : 24f;
            float words = UiTheme.Current != null ? UiTheme.Current.Size(UiTheme.TextStep.Body) : 18f;
            int count = 0;
            Say(t.Replaced, "");
            if (plan.Step == TemperStep.Choose)
            {
                // Три оплаченных варианта на месте карточек свойств: выбор обязателен и ждёт и после закрытия окна.
                Say(t.PropsCaption, TemperText("choose.caption", "Выбери одно из трёх"));
                var session = camp.Session;
                if (session.Kind == ForgeSessionKind.Remelt && ItemGenerator.Generate(session.Snapshot, camp.Items, _temperRoll)
                    && session.Property >= 0 && session.Property < _temperRoll.AffixCount)
                    Say(t.Replaced, TemperFormat("instead", "Вместо: {0}", AffixLine(_temperRoll.GetAffix(session.Property))));
                else Say(t.Replaced, TemperText("paid", "Оплачено · выбор обязателен"));
                for (int i = 0; i < camp.SessionCandidateCount && count < t.Cards.Length; i++, count++)
                {
                    var a = camp.SessionCandidate(i);
                    TemperCard(t.Cards[count], StatArt(t, a.Stat), StatLabel(a.Stat), Paint(AffixValue(a.Value, a.Op, a.Stat), UiTheme.Role.Good), numbers, "",
                        i == _temperChoice, true);
                    _temperCardProperty[count] = i;
                }
            }
            else if (_temperTab == EniTab.Heart && plan.Step != TemperStep.Locked && !item.IsEmpty)
            {
                // Сердце: грань 1 из 3; уже вплавленная в эту вещь — спит с пометкой.
                Say(t.PropsCaption, TemperText("heart.caption", "Грань сердца"));
                Say(t.Replaced, TemperFormat("heart.stock", "Сердец в лагере: {0}  ·  без риска, попытку не тратит", camp.HeartCount(boss)));
                for (int i = 0; i < Camp.HeartFacetCount(boss) && count < t.Cards.Length; i++, count++)
                {
                    var facet = Camp.HeartFacetAt(boss, i);
                    bool inside = HasFacet(item, boss, facet);
                    TemperCard(t.Cards[count], _temperFacetArt != null && i < _temperFacetArt.Length ? _temperFacetArt[i] : null, FacetName(facet),
                        FacetEffect(facet), words, inside ? TemperText("heart.inside", "уже в вещи") : "", i == _temperChoice, !inside, false);
                    _temperCardProperty[count] = i;
                }
            }
            else if (!item.IsEmpty && ItemGenerator.Generate(item, camp.Items, _temperRoll))
            {
                Say(t.PropsCaption, TemperText("props.caption", "Свойства предмета"));
                bool pick = (_temperTab == EniTab.Temper || _temperTab == EniTab.Remelt) && plan.Step != TemperStep.Locked;
                bool tempering = CampTemperRules.TemperOpenOn(camp, TemperTarget);
                var target = TemperTarget;
                if (item.Rarity == ItemRarity.Normal && _temperRoll.HasImplicit)
                {
                    count = TemperPropertyCard(t, camp, target, count, -1, _temperRoll.ImplicitStat, _temperRoll.ImplicitValue, _temperRoll.ImplicitOp,
                        plan, pick && _temperTab == EniTab.Temper, tempering, numbers);
                }
                else
                    for (int i = 0; i < _temperRoll.AffixCount && count < t.Cards.Length; i++)
                    {
                        var a = _temperRoll.GetAffix(i);
                        count = TemperPropertyCard(t, camp, target, count, i, a.Stat, a.Value, a.Op, plan, pick, tempering, numbers);
                    }
                // Добавление: призрачная карточка нового свойства — что даст оплата.
                if (_temperTab == EniTab.Add && plan.Step == TemperStep.Pay && count < t.Cards.Length)
                {
                    TemperCard(t.Cards[count], null, TemperText("add.new", "Новое свойство"),
                        Paint(TemperText("add.new.value", "одно из трёх на выбор"), UiTheme.Role.Accent), words, "", false, false);
                    _temperCardProperty[count] = -2;
                    count++;
                }
            }
            else Say(t.PropsCaption, TemperText("props.caption", "Свойства предмета"));
            for (int i = count; i < t.Cards.Length; i++)
                if (t.Cards[i]?.Button != null) Show(t.Cards[i].Button.gameObject, false);
            Say(t.HeartsLine, HeartsInItem(item));
        }

        /// <summary>Карточка свойства вещи; у выбранной под закалкой — «12 → 15» по превью удачного удара. Отдаёт следующий номер карточки.</summary>
        int TemperPropertyCard(CampTemperScreen t, Camp camp, ForgeTarget target, int card, int property, StatType stat, Fix64 value, ModifierOp op,
            in CampTemperRules.Plan plan, bool pick, bool tempering, float size)
        {
            if (card >= t.Cards.Length) return card;
            bool chosen = pick && property == _temperProperty;
            string shown = Paint(AffixValue(value, op, stat), UiTheme.Role.Text);
            string note = "";
            if (_temperTab == EniTab.Temper && pick)
            {
                var quote = camp.Quote(EniAction.Temper, target, property);
                if (quote.Status == SmithResult.AtMaximum) note = SmithText("max");
                if (chosen && (plan.Step == TemperStep.Strike || plan.Step == TemperStep.NextStrike))
                {
                    var after = camp.PreviewTemperStrike(target, property);
                    if (!after.IsEmpty && ItemGenerator.Generate(after, camp.Items, _temperAfter))
                    {
                        Fix64 next = property < 0 ? _temperAfter.ImplicitValue : property < _temperAfter.AffixCount ? _temperAfter.GetAffix(property).Value : value;
                        shown += "  " + Paint("→", UiTheme.Role.Accent) + "  " + Paint(AffixValue(next, op, stat), UiTheme.Role.Good);
                    }
                }
            }
            else if (_temperTab == EniTab.Remelt && chosen) note = TemperText("remelt.out", "уйдёт в переплавку");
            // Идёт закалка — другие свойства заперты: щелчок по ним тихо заплатил бы за новую закалку.
            bool locked = tempering && camp.Session.Property != property;
            if (locked) note = TemperText("locked.session", "сначала «Взять»");
            TemperCard(t.Cards[card], StatArt(t, stat), StatLabel(stat), shown, size, note, chosen, pick && !locked);
            _temperCardProperty[card] = property;
            return card + 1;
        }

        static void TemperCard(CampTemperCard card, Texture art, string name, string value, float size, string note, bool chosen, bool clickable,
            bool whiteMask = true)
        {
            if (card?.Button == null) return;
            Show(card.Button.gameObject, true);
            card.Button.interactable = clickable;
            Show(card.Chosen, chosen);
            if (card.Icon != null)
            {
                Show(card.Icon.Fire, chosen);
                if (card.Icon.Art != null)
                {
                    card.Icon.Art.texture = art;
                    card.Icon.Art.enabled = art != null;
                    // Белые маски статов — краской темы; расписные грани — как нарисованы.
                    var tint = card.Icon.Art.GetComponent<ThemeColor>();
                    if (tint != null) tint.enabled = whiteMask;
                    if (!whiteMask) card.Icon.Art.color = Color.white;
                }
            }
            Say(card.Name, name);
            if (card.Value != null) { card.Value.text = value; card.Value.fontSize = size; }
            Say(card.Note, note);
            Tone(card.Name, chosen ? UiTheme.Role.Text : UiTheme.Role.TextMuted);
        }

        static Texture StatArt(CampTemperScreen t, StatType stat) => (int)stat < t.StatIcons.Length ? t.StatIcons[(int)stat] : null;

        static string AffixLine(RolledAffix affix) => StatLabel(affix.Stat) + " " + AffixValue(affix.Value, affix.Op, affix.Stat);

        static bool HasFacet(ItemInstance item, int boss, HeartFacet facet)
        {
            var crafting = item.Crafting;
            int hearts = crafting?.HeartCount ?? 0;
            for (int i = 0; i < hearts; i++) { crafting.HeartAt(i, out int key, out var f); if (key == boss && f == facet) return true; }
            return false;
        }

        static string FacetName(HeartFacet facet) => facet == HeartFacet.ThicketRoots ? TemperText("facet.roots", "Корни")
            : facet == HeartFacet.ThicketPollen ? TemperText("facet.pollen", "Пыльца") : facet == HeartFacet.ThicketBloom ? TemperText("facet.bloom", "Цветение") : "";

        // Что делает грань в забеге (RunBoons.HeartFacet): числа — у Sim, здесь только смысл.
        static string FacetEffect(HeartFacet facet) => facet == HeartFacet.ThicketRoots ? TemperText("facet.roots.effect", "Кувырок связывает врагов рядом")
            : facet == HeartFacet.ThicketPollen ? TemperText("facet.pollen.effect", "Убитый враг оставляет замедляющее облако")
            : facet == HeartFacet.ThicketBloom ? TemperText("facet.bloom.effect", "Раз в 20 с бутон лечит") : "";

        /// <summary>Сердца, уже вплавленные в вещь: «Сердце Чащи · Корни».</summary>
        static string HeartsInItem(ItemInstance item)
        {
            var crafting = item.Crafting;
            int hearts = item.IsEmpty ? 0 : crafting?.HeartCount ?? 0;
            string line = "";
            for (int i = 0; i < hearts; i++)
            {
                crafting.HeartAt(i, out int boss, out var facet);
                string name = boss == RunBossKeys.ThicketMaster ? TemperText("heart.thicket", "Сердце Чащи") : TemperText("heart.other", "Сердце босса");
                line += (line.Length > 0 ? "   " : "") + name + " · " + FacetName(facet);
            }
            return line;
        }

        void RefreshTemperButtons(CampShopScreen s, CampTemperScreen t, in CampTemperRules.Plan plan)
        {
            string label;
            switch (plan.Step)
            {
                case TemperStep.NextStrike: label = TemperText("strike.again", "Ещё удар?"); break;
                case TemperStep.AtMaximum: label = TemperText("strike.max", "На пределе"); break;
                case TemperStep.Risky: label = _temperConfirm ? TemperText("risky.confirm", "Точно ударить?") : TemperText("risky", "Рискованный удар"); break;
                case TemperStep.Choose: label = TemperText("choose.action", "Выбрать"); break;
                case TemperStep.Dismantle: label = _temperConfirm ? TemperText("dismantle.confirm", "Точно разобрать?") : TemperText("dismantle", "Разобрать"); break;
                default: label = TabVerb(_temperTab); break;
            }
            Say(t.PrimaryLabel, label);
            if (t.Primary != null) t.Primary.interactable = plan.CanAct;
            // Вторая кнопка: «Взять [Esc]» в открытой закалке, «Отменить [Esc]» при вопросе; иначе её нет.
            bool second = plan.CanTake || _temperConfirm;
            if (t.Secondary != null) Show(t.Secondary.gameObject, second);
            Say(t.SecondaryLabel, _temperConfirm ? CampServiceText.Get("key.cancel") : TemperText("take", "Взять"));
            if (s.CloseKeyLabel != null)
                s.CloseKeyLabel.text = UiKeyHint.Verb(_temperConfirm ? CampServiceText.Get("key.cancel") : plan.CanTake ? TemperText("take", "Взять") : CampServiceText.Get("close.action"));
            // Клавиши Enter/Del одной страницы 29.09 спрятаны: клавиша основной кнопки — на ней самой.
            Show(s.MainKey, false);
            Show(s.SecondKey, false);
        }

        static string TabVerb(EniTab tab)
        {
            switch (tab)
            {
                case EniTab.Remelt: return TemperText("remelt", "Переплавить");
                case EniTab.Add: return TemperText("add", "Добавить");
                case EniTab.Heart: return TemperText("heart", "Вплавить");
                case EniTab.Dismantle: return TemperText("dismantle", "Разобрать");
                default: return TemperText("strike", "Ударить");
            }
        }

        /// <summary>«Цена: [золото] 80 [осколки] 5 [сталь] 1 [сердце] 1»: нехватка — красным; у разбора — «Выход: +9».</summary>
        void RefreshTemperPrice(CampTemperScreen t, in CampTemperRules.Plan plan)
        {
            bool dismantle = plan.Step == TemperStep.Dismantle && plan.Yield > 0;
            Show(t.PriceRow, plan.ShowsPrice || dismantle);
            if (!plan.ShowsPrice && !dismantle) return;
            Say(t.PriceCaption, dismantle ? TemperText("yield", "Выход:") : TemperText("price", "Цена:"));
            int[] amount = dismantle ? new[] { 0, plan.Yield, 0, 0 } : new[] { plan.Gold, plan.Shards, plan.Steel, plan.Hearts };
            int[] lack = dismantle ? new int[4] : new[] { plan.GoldShort, plan.ShardsShort, plan.SteelShort, plan.HeartsShort };
            for (int i = 0; i < t.PriceValues.Length && i < amount.Length; i++)
            {
                var value = t.PriceValues[i];
                if (value == null) continue;
                Show(value.transform.parent.gameObject, amount[i] > 0);
                value.text = (dismantle ? "+" : "") + amount[i];
                Tone(value, lack[i] > 0 ? UiTheme.Role.Bad : i == 0 ? UiTheme.Role.Coins : UiTheme.Role.Text);
            }
        }

        /// <summary>Строка под ценой: результат последнего действия, иначе риск, нехватка или причина — до нажатия.</summary>
        void RefreshTemperWarning(CampTemperScreen t, Camp camp, CampInventoryView inventory, in CampTemperRules.Plan plan)
        {
            if (t.Warning == null) return;
            if (_temperStatus.Length > 0) { t.Warning.text = _temperStatus; Tone(t.Warning, _temperStatusRole); return; }
            string text = ""; var role = UiTheme.Role.TextMuted;
            switch (plan.Step)
            {
                case TemperStep.NoItem: text = TemperText("hint.noitem", "Выбери вещь в сумке или надетое"); break;
                case TemperStep.Locked:
                {
                    var need = Camp.RankRequirement(CampTemperRules.TabRank(_temperTab));
                    text = TemperFormat("hint.locked", "Ранг {0}: босс {1} и уровень {2}", CampTemperRules.TabRank(_temperTab), need.BossIndex + 1, need.Level);
                    break;
                }
                case TemperStep.Blocked:
                    text = TemperBlockReason(plan.Status, camp, inventory);
                    role = plan.Status == SmithResult.SessionOpen ? UiTheme.Role.Accent : UiTheme.Role.TextMuted;
                    break;
                case TemperStep.Strike:
                    if (plan.Short) { text = TemperLack(plan); role = UiTheme.Role.Bad; }
                    else text = TemperText("hint.strike", "Первый удар без риска");
                    break;
                case TemperStep.NextStrike:
                    text = TemperFormat("hint.next", "Следующий удар бесплатный · трещина {0}% сожжёт рост этой закалки", plan.RiskPercent);
                    role = CampTemperRules.RiskIsBad(plan.RiskPercent) ? UiTheme.Role.Bad : UiTheme.Role.Accent;
                    break;
                case TemperStep.AtMaximum: text = TemperText("hint.max", "Свойство на пределе — «Взять»"); role = UiTheme.Role.Accent; break;
                case TemperStep.Risky:
                    role = UiTheme.Role.Bad;
                    if (plan.Short) text = TemperLack(plan);
                    else if (_temperConfirm) text = TemperFormat("hint.risky.confirm", "Нажми ещё раз: шедевр — или вещь рассыпется (+{0} осколков)", plan.Yield);
                    else text = TemperFormat("hint.risky", "Попытки кончились: шедевр (+25% ко всем свойствам) или осколки · {0}%", plan.RiskPercent)
                        + (_smithWorn ? "  ·  " + TemperText("hint.risky.worn", "снимется с героя") : "");
                    break;
                case TemperStep.Pay:
                    if (plan.Short) { text = TemperLack(plan); role = UiTheme.Role.Bad; }
                    else if (plan.Overflow) { text = TemperFormat("hint.overflow", "Сверх предела редкости: трещина {0}%, иначе три свойства на выбор", plan.RiskPercent); role = UiTheme.Role.Bad; }
                    else text = _temperTab == EniTab.Remelt ? TemperText("hint.remelt", "Три варианта без старого свойства · выбор обязателен")
                        : TemperText("hint.add", "Три новых свойства на выбор · выбор обязателен");
                    break;
                case TemperStep.Choose:
                    text = TemperText("hint.choose", "Оплачено · выбор обязателен и ждёт после закрытия окна"); role = UiTheme.Role.Accent; break;
                case TemperStep.Heart:
                    if (plan.HeartsShort > 0) { text = TemperText("hint.noheart", "Нет сердца босса — оно падает с победы над ним"); role = UiTheme.Role.Bad; }
                    else if (plan.Short) { text = TemperLack(plan); role = UiTheme.Role.Bad; }
                    else if (_temperChoice < 0) text = TemperText("hint.facet", "Выбери грань сердца");
                    else text = TemperText("hint.heart", "Грань действует в забеге, пока вещь надета");
                    break;
                case TemperStep.Dismantle:
                    switch (plan.DismantleBlock)
                    {
                        case CampShopDeals.Block.Worn: text = TemperText("hint.worn", "Надетое не разбирается — сними его в палатке"); break;
                        case CampShopDeals.Block.Protected: text = SmithText("protected.short").Replace('\n', ' '); break;
                        case CampShopDeals.Block.Session: text = TemperText("hint.session.item", "Вещь у Эни в работе"); break;
                        default:
                            text = _temperConfirm ? TemperText("hint.dismantle.confirm", "Нажми ещё раз — вещь исчезнет") : SmithText("destroy.warning");
                            role = _temperConfirm ? UiTheme.Role.Bad : UiTheme.Role.TextMuted;
                            break;
                    }
                    break;
            }
            t.Warning.text = text;
            Tone(t.Warning, role);
        }

        static string TemperLack(in CampTemperRules.Plan plan)
        {
            string lack = "";
            if (plan.GoldShort > 0) lack = GoldText(plan.GoldShort);
            if (plan.ShardsShort > 0) lack += (lack.Length > 0 ? ", " : "") + ShardsText(plan.ShardsShort);
            if (plan.SteelShort > 0) lack += (lack.Length > 0 ? ", " : "") + TemperFormat("unit.steel", "{0} стали", plan.SteelShort);
            if (plan.HeartsShort > 0) lack += (lack.Length > 0 ? ", " : "") + TemperFormat("unit.heart", "{0} сердца", plan.HeartsShort);
            return Format("smith.short", lack);
        }

        string TemperBlockReason(SmithResult status, Camp camp, CampInventoryView inventory)
        {
            switch (status)
            {
                case SmithResult.SessionOpen:
                {
                    var session = camp.Session;
                    var waiting = session.Target.IsWorn ? camp.Worn.Worn((EquipSlot)session.Target.Slot) : camp.Bag.At(session.Target.Slot);
                    return TemperFormat("reason.session", "Эни ждёт выбора: {0} · вкладка «{1}»", waiting.IsEmpty ? "" : inventory.ItemName(waiting.BaseId),
                        TemperTabName(CampTemperRules.PendingTab(camp)));
                }
                case SmithResult.Shattered: return TemperText("reason.shattered", "Вещь расколота: три трещины · можно носить или разобрать");
                case SmithResult.Masterpiece: return TemperText("reason.masterpiece", "Шедевр: дальше только сердце");
                case SmithResult.AtMaximum: return TemperText("reason.max", "Свойство на пределе — выбери другое");
                case SmithResult.NoAffix: return TemperText("reason.noaffix", "Выбери свойство");
                case SmithResult.Exhausted: return TemperText("reason.exhausted", "Попытки закалки этой вещи использованы");
                case SmithResult.NoSpace: return TemperText("reason.nospace", "Предел свойств этой редкости и перелива");
                case SmithResult.Incompatible: return TemperText("reason.incompatible", "Сюда это не вплавить");
                case SmithResult.HeartsFull: return TemperFormat("reason.heartsfull", "Сердец в вещи столько, сколько позволяет ранг · второе — с ранга {0}",
                    CampTemperRules.SecondHeartRank);
                case SmithResult.NoHeart: return TemperText("hint.noheart", "Нет сердца босса — оно падает с победы над ним");
                case SmithResult.InvalidItem:
                    // Переплавка и добавление — только редкой и эпической (Quote отдаёт общий InvalidItem).
                    return _temperTab == EniTab.Remelt || _temperTab == EniTab.Add ? TemperText("reason.rarity", "Только для редких и эпических вещей")
                        : TemperText("reason.invalid", "Эни это не возьмёт");
                default: return SmithFailure(status);
            }
        }

        static string TemperTabName(EniTab tab)
        {
            switch (tab)
            {
                case EniTab.Remelt: return TemperText("tab.remelt", "Переплавить");
                case EniTab.Add: return TemperText("tab.add", "Добавить свойство");
                case EniTab.Heart: return TemperText("tab.heart", "Сердце");
                case EniTab.Dismantle: return TemperText("tab.dismantle", "Разобрать");
                default: return TemperText("tab.temper", "Закалить");
            }
        }

        // ---------------------------------------------------------------- действия

        /// <summary>Основная кнопка вкладки (E, Enter, щелчок). Рискованный удар и разбор — только вторым нажатием.</summary>
        void ApplyTemper()
        {
            if (!TemperTabs || _smithCamp == null || Time.unscaledTime < _temperLockUntil) return;
            var camp = _smithCamp; var s = _view.Smith;
            var plan = TemperPlan();
            if (!plan.CanAct) return;
            if (plan.NeedsConfirm && !_temperConfirm)
            {
                _temperConfirm = true;
                TemperStatus("", UiTheme.Role.TextMuted);
                RefreshSmith();
                return;
            }
            _temperConfirm = false;
            var target = TemperTarget;
            SmithResult result;
            bool good = true;
            switch (plan.Step)
            {
                case TemperStep.Strike:
                case TemperStep.NextStrike:
                {
                    result = camp.Strike(target, _temperProperty, out var outcome);
                    // Молот опускается: двойное E не даёт второго удара с риском.
                    _temperLockUntil = Time.unscaledTime + CampTemperRules.StrikeLockSeconds;
                    if (result != SmithResult.Success) break;
                    if (outcome == StrikeOutcome.Grew)
                    {
                        TemperStatus(TemperText("done.grew", "Удар лёг. Ещё удар — или «Взять»"), UiTheme.Role.Accent);
                        s.Message.text = CampServiceText.Get("dialogue.smith.reforge");
                        GameSound.Sequence(("smith_hammer", 0f, .8f), ("smith_ring", .22f, .45f));
                    }
                    else
                    {
                        // Неудача — системная строка, Эни молчит (AGENTS/CAMP-NPC-DIALOGUE.md).
                        good = false;
                        TemperStatus(outcome == StrikeOutcome.Shattered ? TemperText("done.shattered", "Третья трещина — вещь расколота")
                            : TemperText("done.cracked", "Трещина: рост этой закалки сгорел"), UiTheme.Role.Bad);
                        s.Message.text = "";
                        GameSound.Sequence(("smith_hammer", 0f, .8f), ("smith_break", .12f, .7f));
                    }
                    break;
                }
                case TemperStep.Risky:
                {
                    result = camp.RiskyStrike(target, out bool masterpiece, out int shards);
                    _temperLockUntil = Time.unscaledTime + CampTemperRules.StrikeLockSeconds;
                    if (result != SmithResult.Success) break;
                    if (masterpiece)
                    {
                        TemperStatus(TemperText("done.masterpiece", "Шедевр! Клеймо Эни: +25% ко всем свойствам"), UiTheme.Role.Accent);
                        GameSound.Sequence(("smith_hammer", 0f, .9f), ("smith_sizzle", .25f, .5f), ("smith_ring", .5f, .5f));
                    }
                    else
                    {
                        good = false;
                        TemperStatus(TemperFormat("done.crumbled", "Вещь рассыпалась: +{0}", ShardsText(shards)), UiTheme.Role.Bad);
                        _smithSlot = -1; _smithWorn = false;
                        GameSound.Sequence(("smith_break", 0f, .8f), ("smith_debris", .08f, .6f));
                    }
                    s.Message.text = "";
                    break;
                }
                case TemperStep.Pay:
                {
                    bool cracked = false;
                    result = _temperTab == EniTab.Add ? camp.BeginAdd(target, out cracked) : camp.BeginRemelt(target, _temperProperty);
                    if (result != SmithResult.Success) break;
                    _temperChoice = -1;
                    if (cracked)
                    {
                        good = false;
                        TemperStatus(TemperText("done.overflow", "Перелив: трещина, свойство не легло"), UiTheme.Role.Bad);
                        GameSound.Sequence(("smith_hammer", 0f, .8f), ("smith_break", .12f, .7f));
                    }
                    else
                    {
                        TemperStatus("", UiTheme.Role.TextMuted);
                        GameSound.Sequence(("smith_hammer", 0f, .8f), ("smith_sizzle", .25f, .5f));
                    }
                    break;
                }
                case TemperStep.Choose:
                    result = camp.ChooseSessionCandidate(_temperChoice);
                    if (result != SmithResult.Success) break;
                    _temperChoice = -1;
                    _temperProperty = TemperDefaultProperty();
                    TemperStatus(TemperText("done.chosen", "Готово: свойство влито"), UiTheme.Role.Accent);
                    s.Message.text = CampServiceText.Get("dialogue.smith.reforge");
                    GameSound.Sequence(("smith_sizzle", 0f, .5f), ("smith_ring", .25f, .45f));
                    break;
                case TemperStep.Heart:
                {
                    int boss = CampTemperRules.HeartBoss(camp);
                    var facet = Camp.HeartFacetAt(boss, _temperChoice);
                    result = camp.InlayHeart(target, boss, facet);
                    if (result != SmithResult.Success) break;
                    _temperChoice = -1;
                    TemperStatus(TemperFormat("done.heart", "Сердце вплавлено: «{0}»", FacetName(facet)), UiTheme.Role.Accent);
                    s.Message.text = CampServiceText.Get("dialogue.smith.reforge");
                    GameSound.Sequence(("smith_hammer", 0f, .7f), ("smith_sizzle", .2f, .6f), ("smith_ring", .5f, .45f));
                    break;
                }
                case TemperStep.Dismantle:
                {
                    // Разбор необратим: запрет проверяется заново, а не по кнопке с прошлого обновления.
                    if (!CampShopDeals.PlanDismantle(camp, _smithSlot, _smithWorn).Allowed) { result = SmithResult.InvalidItem; break; }
                    result = camp.Dismantle(_smithSlot, out int shards);
                    if (result != SmithResult.Success) break;
                    _smithSlot = -1; _smithWorn = false;
                    TemperStatus(Format("smith.dismantled", ShardsText(shards)), UiTheme.Role.Accent);
                    s.Message.text = CampServiceText.Get("dialogue.smith.dismantle");
                    // Разбор короткий (владелец: «слишком долгий»): удар и осыпающиеся детали.
                    GameSound.Sequence(("smith_break", 0f, .8f), ("smith_debris", .08f, .6f));
                    break;
                }
                default: return;
            }
            if (result != SmithResult.Success)
            {
                TemperStatus(TemperBlockReason(result, camp, GetComponent<CampInventoryView>()), UiTheme.Role.Bad);
                RefreshSmith();
                return;
            }
            RefreshSmith();
            var t = _view.Temper;
            if (good) Punch(_smithSlot >= 0 ? (Component)(t.AnvilItem != null && t.AnvilItem.enabled ? t.AnvilItem : t.ItemMedalArt) : t.ItemName);
            if (t.WalletValues.Length > 0) Punch(t.WalletValues[0], .1f);
        }

        /// <summary>«Взять [Esc]» или «Отменить [Esc]» — вторая кнопка, то же, что первый Esc.</summary>
        void TemperSecondary() => CancelTemperConfirm();

        /// <summary>
        /// Esc в кузнице (ловушка 5 ui-common): снять вопрос (рискованный удар, разбор) → «Взять», если идёт закалка (окно
        /// остаётся) → false, и Esc закрывает окно. Оплаченный выбор переплавки при закрытии ждёт.
        /// </summary>
        internal bool CancelTemperConfirm()
        {
            if (!TemperTabs || _smithCamp == null || !_view.Smith.Group.gameObject.activeSelf) return false;
            if (_temperConfirm)
            {
                _temperConfirm = false;
                RefreshSmith();
                return true;
            }
            if (_smithSlot >= 0 && CampTemperRules.TemperOpenOn(_smithCamp, TemperTarget) && _smithCamp.Session.Strikes > 0
                && _smithCamp.TakeTemper() == SmithResult.Success)
            {
                TemperStatus(TemperText("taken", "Взято: рост остаётся в вещи"), UiTheme.Role.Accent);
                _view.Smith.Message.text = CampServiceText.Get("dialogue.smith.reforge");
                GameSound.Play("smith_ring", .4f);
                RefreshSmith();
                return true;
            }
            return false;
        }

        /// <summary>Клавиши кузницы: E (Interact) и Enter — основная кнопка; Esc обработан раньше (CancelTemperConfirm).</summary>
        void TemperKeys(bool primary)
        {
            if (primary && _view.Temper.Primary != null) Press(_view.Temper.Primary);
        }

        // ---------------------------------------------------------------- самопроверка

        /// <summary>
        /// Самопроверка кузницы вкладками на изолированном лагере (CampServicesProbe): без вещи кнопки спят; вещь из сумки —
        /// первый удар платный и без риска, «Взять» появляется, Esc забирает без второй оплаты; закрытая рангом вкладка — замок и
        /// спящая кнопка; разбор — вопрос, Esc снимает, два нажатия разбирают с выходом осколков.
        /// </summary>
        internal bool ProbeTemperTransactions()
        {
            var original = _smithCamp; var t = _view.Temper; var s = _view.Smith;
            try
            {
                WireSmith();
                var camp = new Camp(PrototypeContent.Items());
                camp.Earn(CurrencyType.Gold, 2000); camp.Earn(CurrencyType.Shards, 200);
                camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 123));
                _smithCamp = camp; _smithSlot = -1; _smithWorn = false; _temperTab = EniTab.Temper; _temperChoice = -1; _temperConfirm = false;
                _temperLockUntil = 0f; TemperStatus("", UiTheme.Role.TextMuted);
                RefreshSmith();
                if (t.Primary.interactable) return false;
                s.Cells[0].Button.onClick.Invoke();
                if (_smithSlot != 0 || _smithWorn || !t.Primary.interactable || !t.PriceRow.activeSelf) return false;
                var quote = camp.Quote(EniAction.Temper, ForgeTarget.Bag(0), _temperProperty);
                int gold = camp.Money(CurrencyType.Gold);
                t.Primary.onClick.Invoke();
                if (camp.Money(CurrencyType.Gold) != gold - quote.Gold || !camp.Session.IsOpen || camp.Session.Strikes != 1 || !t.Secondary.gameObject.activeSelf)
                    return false;
                // Сразу второй удар не проходит: молот ещё опускается.
                t.Primary.onClick.Invoke();
                if (camp.Session.IsOpen && camp.Session.Strikes != 1) return false;
                if (!CancelShopConfirm() || camp.Session.IsOpen || camp.Money(CurrencyType.Gold) != gold - quote.Gold) return false;
                // Пауза молота в пробе не ждём: дальше проверяются вкладки, а не таймер.
                _temperLockUntil = 0f;
                // Переплавка: замок — ровно когда ранг лагеря её не открыл (у проверочного лагеря ранг высший); стали нет —
                // кнопка спит в любом случае.
                t.Tabs[(int)EniTab.Remelt].onClick.Invoke();
                if (t.TabLocks[(int)EniTab.Remelt].activeSelf == CampTemperRules.TabOpen(camp, EniTab.Remelt) || t.Primary.interactable) return false;
                t.Tabs[(int)EniTab.Dismantle].onClick.Invoke();
                int reward = camp.SalvageShards(camp.Bag.At(0)), shards = camp.Money(CurrencyType.Shards);
                t.Primary.onClick.Invoke();
                if (camp.Bag.IsEmpty(0) || !_temperConfirm || !CancelShopConfirm() || _temperConfirm) return false;
                t.Primary.onClick.Invoke(); t.Primary.onClick.Invoke();
                return camp.Bag.IsEmpty(0) && camp.Money(CurrencyType.Shards) == shards + reward && _smithSlot < 0 && !t.Primary.interactable;
            }
            finally
            {
                _smithCamp = original; _smithSlot = -1; _smithWorn = false; _temperConfirm = false; _temperTab = EniTab.Temper; _temperLockUntil = 0f;
                TemperStatus("", UiTheme.Role.TextMuted);
                if (_smithCamp != null) RefreshSmith();
                s.Message.text = "";
            }
        }

        /// <summary>
        /// Надетое в кузнице вкладками: оружие выбирается в ряду «Надето», закаляется на месте (остаётся надетым), разбор для
        /// него спит и ничего не делает.
        /// </summary>
        internal bool ProbeTemperWorn()
        {
            var original = _smithCamp; var t = _view.Temper; var s = _view.Smith;
            try
            {
                WireSmith();
                var camp = new Camp(PrototypeContent.Items());
                camp.Earn(CurrencyType.Gold, 2000); camp.Earn(CurrencyType.Shards, 200);
                camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 123));
                if (!camp.EquipFromBag(0) || s.Worn.Length == 0) return false;
                _smithCamp = camp; _smithSlot = -1; _smithWorn = false; _temperTab = EniTab.Temper; _temperChoice = -1; _temperConfirm = false;
                _temperLockUntil = 0f; TemperStatus("", UiTheme.Role.TextMuted);
                RefreshSmith();
                s.Worn[0].Button.onClick.Invoke();
                if (!_smithWorn || _smithSlot != 0 || !t.Primary.interactable) return false;
                t.Primary.onClick.Invoke();
                if (camp.Worn.Worn(EquipSlot.Weapon).IsEmpty || !camp.Bag.IsEmpty(0) || !camp.Session.IsOpen) return false;
                CancelShopConfirm();
                _temperLockUntil = 0f;
                t.Tabs[(int)EniTab.Dismantle].onClick.Invoke();
                if (t.Primary.interactable) return false;
                t.Primary.onClick.Invoke();
                return !_temperConfirm && !camp.Worn.Worn(EquipSlot.Weapon).IsEmpty;
            }
            finally
            {
                _smithCamp = original; _smithSlot = -1; _smithWorn = false; _temperConfirm = false; _temperTab = EniTab.Temper; _temperLockUntil = 0f;
                TemperStatus("", UiTheme.Role.TextMuted);
                if (_smithCamp != null) RefreshSmith();
                s.Message.text = "";
            }
        }
    }
}
