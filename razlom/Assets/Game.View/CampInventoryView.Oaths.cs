using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Страница палатки; порядок — порядок вкладок «Сумка · Клятвы · Атлас».</summary>
    public enum TentPage : byte { Bag = 0, Oaths = 1, Atlas = 2 }

    /// <summary>
    /// Вкладка «Клятвы» палатки (06.10, концепт oaths-b в нашем стиле ingame-style/oaths.png): ряд слотов, карта морехода
    /// владельца, четыре группы печатей, карточка справа. Покупка и переключение — только через сессию
    /// (GameSession.BuyOath / SetOathActive): они сразу обновляют героя лагеря, и лист статов досчитывает сам. На этой
    /// странице E — действие карточки («Поклясться [E]»), а не закрытие палатки; Esc закрывает, как везде.
    /// Внешний вид — CampTentView и CampOathSeal; правила и тексты — CampOathRules.
    /// </summary>
    public sealed partial class CampInventoryView
    {
        TentPage _page;
        // Страница, на которой открыть палатку (доска клятв в мире открывает сразу «Клятвы»).
        bool _hasPendingPage; TentPage _pendingPage;
        // Карточка показывает наведённую печать, без наведения — выбранную кликом.
        OathId _oathPicked = OathId.ToughHide, _oathHover = OathId.None;
        // Какая клятва стоит в каждом месте ряда слотов: клик по слоту выбирает её.
        readonly OathId[] _slotOaths = new OathId[CampOathRules.SlotPlaces];
        // Защита от двойного E: одно нажатие — одна покупка.
        float _oathActedAt = -10f;
        const float OathRepeatGuard = .35f;
        // Окончательные значки из Resources (если их уже положили), иначе временные из префаба.
        readonly Texture[] _oathArt = new Texture[OathIds.Count];
        Texture _ashArt;

        bool HasOathPage => _tent != null && _tent.OathsPage != null;

        /// <summary>Открыть палатку сразу на странице (доска клятв — на «Клятвах»).</summary>
        public void Open(TentPage page) { PreparePage(page); Open(); }

        /// <summary>Следующее открытие палатки — на этой странице (CampServicesView зовёт до CampPlayerView.OpenTent).</summary>
        internal void PreparePage(TentPage page) { _hasPendingPage = true; _pendingPage = page; }

        /// <summary>
        /// При открытии: наведение прошлого раза забыто (печати выключались вместе с палаткой и выхода мыши не получили),
        /// страница, которую просили до открытия, — без звука вкладки.
        /// </summary>
        void ApplyPendingPage()
        {
            _oathHover = OathId.None;
            _atlasHover = -1;
            if (!_hasPendingPage) return;
            _hasPendingPage = false;
            if (_tent != null) ShowPage(_pendingPage, silent: true);
        }

        /// <summary>Переключить страницу палатки; без вкладки клятв (старый префаб) «Клятвы» открывают сумку.</summary>
        internal void ShowPage(TentPage page, bool silent = false)
        {
            if (_tent == null) return;
            if (page == TentPage.Oaths && !HasOathPage) page = TentPage.Bag;
            if (_page == page) return;
            _page = page;
            // CampWalkCapture читает этот флаг отражением: он остаётся «атлас ли сейчас».
            _atlasPage = page == TentPage.Atlas;
            _hoverIndex = -1;
            _oathHover = OathId.None;
            _atlasHover = -1;
            if (!silent) UiSound.Play(UiSoundEvent.Tab);
            _tent.ShowPage((int)page);
            Refresh();
        }

        /// <summary>Подписка печатей, слотов и кнопки карточки; значки из Resources. Зовёт BuildTent один раз.</summary>
        void BuildOaths()
        {
            if (!HasOathPage) return;
            if (_tent.OathsTab != null) _tent.OathsTab.onClick.AddListener(() => ShowPage(TentPage.Oaths));
            for (int i = 0; i < OathIds.Count; i++)
            {
                Texture final = Resources.Load<Texture2D>("UI/OathIcons/" + CampOathRules.IconFile(CampOathRules.At(i)));
                Texture placeholder = _tent.OathIcons != null && i < _tent.OathIcons.Length ? _tent.OathIcons[i] : null;
                _oathArt[i] = final != null ? final : placeholder;
            }
            _ashArt = Resources.Load<Texture2D>("UI/CampCurrency/ash");
            if (_ashArt != null)
            {
                if (_tent.AshIcon != null) _tent.AshIcon.texture = _ashArt;
                if (_tent.OathCardPriceIcon != null) _tent.OathCardPriceIcon.texture = _ashArt;
            }
            if (_tent.OathSeals != null)
                foreach (var seal in _tent.OathSeals)
                {
                    if (seal == null) continue;
                    if (_ashArt != null && seal.PriceIcon != null) seal.PriceIcon.texture = _ashArt;
                    seal.Hovered = (s, on) => HoverOath(CampOathRules.At(s.Index), on);
                    seal.Clicked = (s, right) => ClickOath(CampOathRules.At(s.Index), right);
                }
            if (_tent.OathSlots != null)
                foreach (var slot in _tent.OathSlots)
                {
                    if (slot == null) continue;
                    slot.Hovered = (s, on) => HoverOath(SlotOath(s.Index), on);
                    slot.Clicked = (s, right) => ClickOath(SlotOath(s.Index), right);
                }
            if (_tent.OathAction != null) _tent.OathAction.onClick.AddListener(ActOnCard);
        }

        OathId SlotOath(int slot) => (uint)slot < (uint)_slotOaths.Length ? _slotOaths[slot] : OathId.None;

        void HoverOath(OathId id, bool on)
        {
            if (on && id != OathId.None) _oathHover = id;
            else if (!on && _oathHover == id) _oathHover = OathId.None;
            else return;
            RefreshOaths();
        }

        /// <summary>ЛКМ — закрепить клятву в карточке; ПКМ по купленной — включить или выключить.</summary>
        void ClickOath(OathId id, bool right)
        {
            if (id == OathId.None) return;
            if (right) { if (_driver.Session.Camp.OathRank(id) > 0) ToggleOath(id); return; }
            _oathPicked = id;
            RefreshOaths();
        }

        /// <summary>Клятва карточки: наведённая, иначе выбранная.</summary>
        OathId CardOath => _oathHover != OathId.None ? _oathHover : _oathPicked;

        /// <summary>Основная кнопка карточки (и E на странице клятв): поклясться, укрепить, включить или выключить.</summary>
        void ActOnCard()
        {
            if (!HasOathPage || Time.unscaledTime - _oathActedAt < OathRepeatGuard) return;
            _oathActedAt = Time.unscaledTime;
            var camp = _driver.Session.Camp;
            OathId id = CardOath;
            OathAction action = CampOathRules.ActionOf(camp, id);
            OathWarning warning = CampOathRules.WarningOf(camp, id);
            if (CampOathRules.Blocks(warning))
            {
                UiSound.Play(UiSoundEvent.Denied);
                SetFeedback(CampWindowText.Get(CampOathRules.WarningKey(warning), CampOathRules.WarningRu(warning, camp.NextOathPrice, camp.Money(CurrencyType.Ash))));
                return;
            }
            if (CampOathRules.IsPurchase(action)) Buy(id);
            else ToggleOath(id);
        }

        void Buy(OathId id)
        {
            var camp = _driver.Session.Camp;
            int ashBefore = camp.Money(CurrencyType.Ash);
            OathResult result = _driver.Session.BuyOath(id);
            if (result != OathResult.Success) { Refuse(result); return; }
            _oathPicked = id;
            UiSound.Play(UiSoundEvent.Apply);
            SetFeedback("");
            Refresh();
            CountAsh(ashBefore, camp.Money(CurrencyType.Ash));
            FlashOath(id);
        }

        /// <summary>Включить купленную клятву или выключить включённую (ПКМ по печати, клик кнопки «Включить/Выключить»).</summary>
        void ToggleOath(OathId id)
        {
            var camp = _driver.Session.Camp;
            bool on = !camp.OathActive(id);
            OathResult result = _driver.Session.SetOathActive(id, on);
            if (result != OathResult.Success) { Refuse(result); return; }
            UiSound.Play(UiSoundEvent.Toggle);
            SetFeedback("");
            Refresh();
            if (on) FlashOath(id);
        }

        void Refuse(OathResult result)
        {
            UiSound.Play(UiSoundEvent.Denied);
            SetFeedback(CampWindowText.Get("tent.oath.result." + (int)result, CampOathRules.ResultRu(result)));
            Refresh();
        }

        /// <summary>Отклик покупки и включения: огонь печати вспыхивает, клятва в ряду слотов — тоже.</summary>
        void FlashOath(OathId id)
        {
            int index = CampOathRules.IndexOf(id);
            if (_tent.OathSeals != null && index >= 0 && index < _tent.OathSeals.Length && _tent.OathSeals[index] != null) _tent.OathSeals[index].Flash();
            if (_tent.OathCardSeal != null) _tent.OathCardSeal.Flash();
            for (int s = 0; s < _slotOaths.Length; s++)
                if (_slotOaths[s] == id && _tent.OathSlots != null && s < _tent.OathSlots.Length && _tent.OathSlots[s] != null) _tent.OathSlots[s].Flash();
        }

        /// <summary>Пепел досчитывает вниз после покупки.</summary>
        void CountAsh(int from, int to)
        {
            var label = _tent.AshCount;
            if (label == null || from == to) return;
            UiMotion.Play(label, 53, .4f, t => label.text = Mathf.RoundToInt(Mathf.Lerp(from, to, t)).ToString());
        }

        void RefreshOaths()
        {
            if (!HasOathPage || _driver?.Session == null) return;
            PaintOaths(_tent, _driver.Session.Camp, CardOath, _oathPicked, _oathArt, _slotOaths);
        }

        /// <summary>Пепел справа сверху виден на всех страницах.</summary>
        void RefreshAsh()
        {
            if (_tent?.AshCount != null) _tent.AshCount.text = _driver.Session.Camp.Money(CurrencyType.Ash).ToString();
        }

        /// <summary>
        /// Вся страница клятв по лагерю. Статический — его же зовёт кадр сборщика без Play (CampTentWcBuilder.Preview) с
        /// примерным лагерем, так кадр и игра рисуют одно и то же.
        /// </summary>
        /// <param name="art">Значок каждой клятвы [OathId − 1]; null — значок из префаба.</param>
        /// <param name="slotOaths">Сюда пишется, какая клятва стоит в каждом месте ряда (длина 6); null — не нужно.</param>
        public static void PaintOaths(CampTentView tent, Camp camp, OathId card, OathId picked, Texture[] art, OathId[] slotOaths)
        {
            int ash = camp.Money(CurrencyType.Ash), price = camp.NextOathPrice;
            if (tent.AshCount != null) tent.AshCount.text = ash.ToString();

            for (int i = 0; i < OathIds.Count && tent.OathSeals != null && i < tent.OathSeals.Length; i++)
            {
                var seal = tent.OathSeals[i];
                if (seal == null) continue;
                OathId id = CampOathRules.At(i);
                int rank = camp.OathRank(id), max = RunBoons.MaxRank(id);
                seal.SetArt(Icon(tent, art, i));
                seal.Show(SealLook(CampOathRules.LookOf(camp, id)), id == picked);
                seal.SetRank(rank, max);
                seal.SetPrice(rank < max ? price.ToString() : null, ash >= price);
            }

            var order = slotOaths ?? new OathId[CampOathRules.SlotPlaces];
            int taken = CampOathRules.SlotOrder(camp, order), slots = camp.OathSlots;
            for (int s = 0; s < CampOathRules.SlotPlaces && tent.OathSlots != null && s < tent.OathSlots.Length; s++)
            {
                var slot = tent.OathSlots[s];
                if (slot == null) continue;
                bool locked = s >= slots;
                slot.SetLocked(locked, locked ? CampWindowText.Format("tent.oath.slot-level", "Ур. {0}", CampOathRules.SlotUnlockLevel(s)) : null);
                OathId id = s < taken ? order[s] : OathId.None;
                slot.SetArt(id != OathId.None ? Icon(tent, art, CampOathRules.IndexOf(id)) : null);
                slot.Show(id != OathId.None ? CampOathSeal.Look.Active : CampOathSeal.Look.Empty, id != OathId.None && id == picked);
            }
            if (tent.OathSlotsCount != null)
                tent.OathSlotsCount.text = CampWindowText.Format("tent.oath.slots", "В силе · {0} из {1}", taken, slots).ToUpperInvariant();

            for (int g = 0; g < 4 && tent.OathGroupTitles != null && g < tent.OathGroupTitles.Length; g++)
                if (tent.OathGroupTitles[g] != null)
                    tent.OathGroupTitles[g].text = CampWindowText.Get(CampOathRules.GroupKey((OathGroup)g), CampOathRules.GroupRu((OathGroup)g));

            PaintOathCard(tent, camp, card, art);
        }

        /// <summary>Цвет группы: герой, выживание, удача, добыча (как CampTentWcBuilder.OathGroupRoles).</summary>
        static readonly UiTheme.Role[] OathGroupRoles = { UiTheme.Role.Health, UiTheme.Role.Good, UiTheme.Role.Epic, UiTheme.Role.Coins };

        static Texture Icon(CampTentView tent, Texture[] art, int index)
        {
            if (index < 0) return null;
            if (art != null && index < art.Length && art[index] != null) return art[index];
            return tent.OathIcons != null && index < tent.OathIcons.Length ? tent.OathIcons[index] : null;
        }

        static CampOathSeal.Look SealLook(OathLook look)
        {
            switch (look)
            {
                case OathLook.Active: return CampOathSeal.Look.Active;
                case OathLook.Owned: return CampOathSeal.Look.Owned;
                case OathLook.Unaffordable: return CampOathSeal.Look.Faint;
                default: return CampOathSeal.Look.Dim;
            }
        }

        /// <summary>Карточка справа: большая печать, имя, группа и ступень, огоньки ступеней, действие, цена, кнопка, отказ.</summary>
        static void PaintOathCard(CampTentView tent, Camp camp, OathId id, Texture[] art)
        {
            if (id == OathId.None) id = OathId.ToughHide;
            int index = CampOathRules.IndexOf(id);
            int rank = camp.OathRank(id), max = RunBoons.MaxRank(id);
            bool active = camp.OathActive(id);
            int ash = camp.Money(CurrencyType.Ash), price = camp.NextOathPrice;
            OathGroup group = CampOathRules.GroupOf(id);

            if (tent.OathCardSeal != null)
            {
                tent.OathCardSeal.SetArt(Icon(tent, art, index));
                // Карточка не тускнеет вместе с печатью: знак виден целиком, огонь — по состоянию клятвы.
                tent.OathCardSeal.Show(active ? CampOathSeal.Look.Active : rank > 0 ? CampOathSeal.Look.Owned : CampOathSeal.Look.Dim, false);
                if (rank == 0) tent.OathCardSeal.SetArtAlpha(.85f);
                // Сияние внутри большой печати — цвет группы показанной клятвы (те же роли, что у печатей доски в сборщике).
                var glow = tent.OathCardSeal.Glow != null ? tent.OathCardSeal.Glow.GetComponent<ThemeColor>() : null;
                if (glow != null) glow.SetRole(OathGroupRoles[(int)group], .22f);
            }
            if (tent.OathCardName != null) tent.OathCardName.text = CampWindowText.Get(CampOathRules.NameKey(id), CampOathRules.NameRu(id));
            if (tent.OathCardMeta != null)
            {
                string meta = CampWindowText.Get(CampOathRules.GroupKey(group), CampOathRules.GroupRu(group));
                if (max > 1) meta += " · " + CampWindowText.Format("tent.oath.rank", "ступень {0} из {1}", rank, max);
                if (rank > 0) meta += " · " + (active ? CampWindowText.Get("tent.oath.active", "в силе") : CampWindowText.Get("tent.oath.reserve", "в запасе"));
                tent.OathCardMeta.text = meta;
            }
            if (tent.OathCardPips != null)
                for (int i = 0; i < tent.OathCardPips.Length; i++)
                {
                    var pip = tent.OathCardPips[i];
                    if (pip == null) continue;
                    bool shown = max > 1 && i < max;
                    pip.gameObject.SetActive(shown);
                    if (shown) pip.color = new Color(1f, 1f, 1f, i < rank ? 1f : .22f);
                }
            if (tent.OathCardEffect != null) tent.OathCardEffect.text = CampWindowText.Get(CampOathRules.EffectKey(id), CampOathRules.EffectRu(id));
            if (tent.OathCardProgress != null)
            {
                string progress = CampOathRules.ProgressRu(id, rank);
                tent.OathCardProgress.text = progress;
                tent.OathCardProgress.gameObject.SetActive(progress.Length > 0);
            }
            if (tent.OathCardNote != null)
            {
                bool waits = CampOathRules.WaitsForLootBlock(id);
                tent.OathCardNote.text = waits ? CampWindowText.Get(CampOathRules.LootBlockKey, CampOathRules.LootBlockRu) : "";
                tent.OathCardNote.gameObject.SetActive(waits);
            }

            OathAction action = CampOathRules.ActionOf(camp, id);
            OathWarning warning = CampOathRules.WarningOf(camp, id);
            bool purchase = CampOathRules.IsPurchase(action);
            if (tent.OathCardPriceRow != null) tent.OathCardPriceRow.SetActive(purchase);
            if (tent.OathCardPrice != null && purchase)
            {
                tent.OathCardPrice.text = price.ToString();
                var tint = tent.OathCardPrice.GetComponent<ThemeColor>();
                if (tint != null) tint.SetRole(ash >= price ? UiTheme.Role.Text : UiTheme.Role.Bad, 1f);
            }
            if (tent.OathActionLabel != null) tent.OathActionLabel.text = CampWindowText.Get(CampOathRules.ActionKey(action), CampOathRules.ActionRu(action));
            if (tent.OathActionKey != null) UiKeyHint.SetKeycap(tent.OathActionKey, "E"); // действие карточки — E (CampInventoryView.OathKeyPressed)
            SetInteractable(tent.OathAction, !CampOathRules.Blocks(warning));
            if (tent.OathCardHint != null)
            {
                // ПКМ по купленной печати — вторая половина управления: у клятв героя кнопка занята «Укрепить».
                string hint = rank <= 0 ? ""
                    : UiKeyHint.Hint(active ? CampWindowText.Get("tent.oath.hint-off", "выключить") : CampWindowText.Get("tent.oath.hint-on", "включить"), "ПКМ");
                tent.OathCardHint.text = hint;
                tent.OathCardHint.gameObject.SetActive(hint.Length > 0);
            }
            if (tent.OathCardWarning != null)
            {
                string text = warning == OathWarning.None ? "" : CampWindowText.Get(CampOathRules.WarningKey(warning), CampOathRules.WarningRu(warning, price, ash));
                tent.OathCardWarning.text = text;
                tent.OathCardWarning.gameObject.SetActive(text.Length > 0);
            }
        }
    }
}
