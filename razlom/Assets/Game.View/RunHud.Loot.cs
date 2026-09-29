using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Layout = Game.View.RunLootLedger.Layout;

namespace Game.View
{
    /// <summary>
    /// Строка добычи под панелью забега (выбор владельца 30.09, кадр b1-loot-strip-under-panel):
    /// всегда на экране, пока идёт бой, — золото и круги вещей, которые унесёшь (кольцо и свет
    /// редкости, картинки — те же, что в окнах лагеря, ItemTexts). Новая вещь проявляется дымом с
    /// лёгким толчком и вспышкой своей редкости, рядом всплывает оранжевое «+1»; золото досчитывает.
    ///
    /// Удобство сверх кадра: ряд не заходит в середину экрана (полоса босса, «Новый уровень») при любом
    /// масштабе интерфейса — лишние старые вещи уходят в круг «+K»; вещь, которой не хватит места в
    /// сумке лагеря, притушена; под мышью — подсказка с именем и редкостью (у «+K» — кто там, у
    /// золота — что его уносит). Мышь строка не ловит: клик проходит в мир, как и раньше.
    /// Под завесой перехода новое ждёт — вещь проявится, когда мир откроется.
    /// </summary>
    public sealed partial class RunHud
    {
        readonly RunLootLedger _loot = new RunLootLedger();
        readonly RunLootLedger.GoldCounter _lootGold = new RunLootLedger.GoldCounter();
        RiftRun _lootRun;
        bool _lootDeveloper;
        /// <summary>Свободных мест в сумке лагеря на начало забега; меньше нуля — не знаем.</summary>
        int _lootBagFree = -1;
        /// <summary>Сколько вещей строка уже показала: всё сверх — новое и проявится дымом.</summary>
        int _lootRevealed;
        // Что стоит в ряду сейчас; пересборка — только когда это меняется.
        int _lootFirst = -1, _lootShown = -1, _lootHidden = -1, _lootBoundCount = -1, _lootBoundBag = int.MinValue;
        int[] _lootSlotEntry;
        int _lootGoldMeasured = -1, _lootGoldText = int.MinValue;
        float _lootGoldWidth;
        float _lootRowX = float.NaN, _lootShift, _lootWidth = -1f, _lootClockLast;
        // «+1»: секунды с начала (меньше нуля — не видно), сколько вещей и у какого круга.
        float _lootArrivalT = -1f;
        int _lootArrivalCount, _lootArrivalSlot;
        // Под мышью: −2 — ничего, −1 — золото, −3 — «+K», 0.. — круг ряда.
        int _lootHover = -2;
        long _lootTipStamp = long.MinValue;

        const int HoverNone = -2, HoverGold = -1, HoverMore = -3;
        const float ArrivalIn = .18f, ArrivalHold = 1.1f, ArrivalOut = .5f, ArrivalRise = 10f;
        /// <summary>Притушенная вещь, которой не хватит места в сумке лагеря.</summary>
        const float LostAlpha = .45f;

        private void RefreshLoot(RiftRun run, bool status)
        {
            if (_view.Loot == null) return;
            float now = UiMotion.Now;
            float dt = Mathf.Clamp(now - _lootClockLast, 0f, .1f);
            _lootClockLast = now;
            bool shown = status && run != null;
            RunHudView.SetActive(_view.Loot, shown);
            if (!shown) { LootHover(HoverNone); return; }

            GameSession session = _driver.Session;
            if (run != _lootRun || run.TakenRewardCount < _loot.Scanned || run.Gold < _lootGold.Target) StartLoot(run, session);
            // Под завесой перехода новое ждёт: вещь проявится и золото досчитает, когда мир откроется.
            if (!CampTransition.Covering)
            {
                while (_loot.Scanned < run.TakenRewardCount) _loot.Take(run.GetTaken(_loot.Scanned));
                if (_lootGold.Retarget(run.Gold) && _view.LootCoin != null) HudFx.Punch(_view.LootCoin.transform, 1.18f, .4f);
            }
            int gold = _lootGold.Advance(dt);
            if (gold != _lootGoldText)
            {
                _lootGoldText = gold;
                RunHudView.SetText(_view.LootGold, gold.ToString());
            }
            LayoutLoot(dt);
            ArrivalTick(dt);
            LootPointer();
        }

        /// <summary>Новый забег: пустая строка; уже взятое (строка появилась посреди забега) — без анимации.</summary>
        private void StartLoot(RiftRun run, GameSession session)
        {
            _lootRun = run;
            _loot.Reset();
            while (_loot.Scanned < run.TakenRewardCount) _loot.Take(run.GetTaken(_loot.Scanned));
            _lootRevealed = _loot.Count;
            _lootGold.Snap(run.Gold);
            _lootGoldText = int.MinValue;
            _lootDeveloper = session != null && session.IsDeveloperRun;
            // Сумка лагеря за забег не меняется: FinishRun кладёт вещи по порядку взятия, лишнее теряется.
            _lootBagFree = session != null && session.Camp != null ? session.Camp.Bag.Free : -1;
            _lootFirst = _lootShown = _lootHidden = _lootBoundCount = -1;
            _lootBoundBag = int.MinValue;
            _lootRowX = float.NaN;
            _lootShift = 0f;
            _lootWidth = -1f;
            _lootArrivalT = -1f;
            RunHudView.SetActive(_view.LootArrival, false);
            RunHudView.SetActive(_view.LootMore, false);
            RunHudView.LootSlot[] slots = _view.LootSlots;
            if (_lootSlotEntry == null || _lootSlotEntry.Length != slots.Length) _lootSlotEntry = new int[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                _lootSlotEntry[i] = -1;
                if (slots[i] != null) RunHudView.SetActive(slots[i].Rect, false);
            }
            LootHover(HoverNone);
        }

        /// <summary>
        /// Раскладка строки: сколько кругов влезает до середины экрана (RunLootLedger.Capacity), ряд и
        /// «+K», черта, ширина дыма. Ряд едет плавно: вещь, ушедшая на место левее, не прыгает.
        /// </summary>
        private void LayoutLoot(float dt)
        {
            RunHudView v = _view;
            // Ширина числа — по цели счёта: ряд не ползёт, пока золото досчитывает.
            int target = _lootGold.Target;
            if (target != _lootGoldMeasured && v.LootGold != null)
            {
                _lootGoldMeasured = target;
                _lootGoldWidth = v.LootGold.GetPreferredValues(target.ToString()).x;
            }
            float rowStart = Layout.RowStart(_lootGoldWidth);
            int pool = v.LootSlots != null ? v.LootSlots.Length : 0;
            if (_lootSlotEntry == null || _lootSlotEntry.Length != pool)
            {
                _lootSlotEntry = new int[pool];
                for (int i = 0; i < pool; i++) _lootSlotEntry[i] = -1;
            }
            float canvasWidth = ((RectTransform)v.transform).rect.width;
            if (canvasWidth <= 0f) canvasWidth = 1920f;
            float stripLeft = v.Loot.anchoredPosition.x - v.Loot.pivot.x * v.Loot.sizeDelta.x;
            int capacity = pool >= RunLootLedger.MinSlots ? RunLootLedger.Capacity(canvasWidth, stripLeft + rowStart, pool) : 0;
            RunLootLedger.Window w = capacity > 0 ? RunLootLedger.WindowFor(_loot.Count, capacity) : new RunLootLedger.Window(0, 0, 0);

            float rowX = rowStart + (w.HasMore ? Layout.Pitch : 0f);
            // Вещи не прыгают: сдвиг ряда так, чтобы каждая осталась там, где была, и дальше он тает.
            if (!float.IsNaN(_lootRowX) && _lootFirst >= 0)
                _lootShift += _lootRowX - rowX + (w.First - _lootFirst) * Layout.Pitch;
            _lootRowX = rowX;
            _lootShift = Mathf.Abs(_lootShift) < .1f ? 0f : Mathf.Lerp(_lootShift, 0f, 1f - Mathf.Exp(-dt * 10f));

            if (w.First != _lootFirst || w.Shown != _lootShown || w.Hidden != _lootHidden || _loot.Count != _lootBoundCount
                || _lootBagFree != _lootBoundBag)
                BindLoot(w);

            if (v.LootRow != null) v.LootRow.anchoredPosition = new Vector2(rowX + _lootShift, v.LootRow.anchoredPosition.y);
            if (v.LootMore != null) v.LootMore.anchoredPosition = new Vector2(rowStart + Layout.Icon * .5f, v.LootMore.anchoredPosition.y);
            if (v.LootDivider != null)
            {
                RunHudView.SetActive(v.LootDivider, _loot.Count > 0);
                v.LootDivider.anchoredPosition = new Vector2(Layout.DividerX(_lootGoldWidth), v.LootDivider.anchoredPosition.y);
            }
            float width = Layout.Width(_lootGoldWidth, w.Slots);
            _lootWidth = _lootWidth < 0f ? width : Mathf.Lerp(_lootWidth, width, 1f - Mathf.Exp(-dt * 8f));
            if (Mathf.Abs(_lootWidth - width) < .1f) _lootWidth = width;
            if (!Mathf.Approximately(v.Loot.sizeDelta.x, _lootWidth)) v.Loot.sizeDelta = new Vector2(_lootWidth, v.Loot.sizeDelta.y);
        }

        /// <summary>Круги ряда по окну <paramref name="w"/>; новые — дымом, толчком и вспышкой редкости.</summary>
        private void BindLoot(RunLootLedger.Window w)
        {
            RunHudView v = _view;
            int fresh = 0, newest = -1;
            for (int i = 0; i < v.LootSlots.Length; i++)
            {
                RunHudView.LootSlot slot = v.LootSlots[i];
                if (slot == null || slot.Rect == null) continue;
                if (i >= w.Shown)
                {
                    RunHudView.SetActive(slot.Rect, false);
                    _lootSlotEntry[i] = -1;
                    continue;
                }
                int entry = w.First + i;
                bool moved = _lootSlotEntry[i] != entry;
                FillSlot(slot, entry);
                RunHudView.SetActive(slot.Rect, true);
                if (entry >= _lootRevealed)
                {
                    RevealSlot(slot, _loot[entry].Rarity);
                    fresh++;
                    newest = i;
                }
                // Круг показывал другую вещь и ещё проявлялся — эта вещь уже была на экране: сразу.
                else if (moved && slot.Ink != null && slot.Ink.Running) slot.Ink.ShowInstant();
                _lootSlotEntry[i] = entry;
            }

            bool more = w.HasMore && v.LootMore != null;
            bool hadMore = v.LootMore != null && v.LootMore.gameObject.activeSelf;
            RunHudView.SetActive(v.LootMore, more);
            if (more)
            {
                RunHudView.SetText(v.LootMoreLabel, "+" + w.Hidden);
                if (!hadMore) { if (v.LootMoreInk != null) v.LootMoreInk.Show(); }
                else if (w.Hidden != _lootHidden) HudFx.Punch(v.LootMore, 1.15f, .4f);
            }

            if (fresh > 0 && v.LootArrival != null)
            {
                bool still = _lootArrivalT >= 0f && _lootArrivalT < ArrivalIn + ArrivalHold;
                _lootArrivalCount = still ? _lootArrivalCount + fresh : fresh;
                _lootArrivalT = 0f;
                _lootArrivalSlot = newest;
                RunHudView.SetText(v.LootArrival, "+" + _lootArrivalCount);
                RunHudView.SetActive(v.LootArrival, true);
            }

            _lootFirst = w.First;
            _lootShown = w.Shown;
            _lootHidden = w.Hidden;
            _lootBoundCount = _loot.Count;
            _lootBoundBag = _lootBagFree;
            _lootRevealed = _loot.Count;
            _lootTipStamp = long.MinValue;
        }

        private void FillSlot(RunHudView.LootSlot slot, int entry)
        {
            RunLootLedger.Entry item = _loot[entry];
            if (slot.Art != null)
            {
                Texture art = ItemTexts.Icon(item.BaseId);
                slot.Art.texture = art != null ? art : _view.LootFallbackIcon;
                slot.Art.enabled = slot.Art.texture != null;
            }
            if (slot.State != null) slot.State.Set(item.Rarity, false);
            // Не влезет в сумку лагеря — притушена (почему — в подсказке).
            if (slot.Group != null) slot.Group.alpha = RunLootLedger.Fits(entry, _lootBagFree) ? 1f : LostAlpha;
        }

        /// <summary>Новая вещь: круг проявляется дымом, чуть толкается, за ним вспыхивает свет её редкости.</summary>
        private static void RevealSlot(RunHudView.LootSlot slot, int rarity)
        {
            if (slot.Ink != null) slot.Ink.Show();
            HudFx.Punch(slot.Rect, 1.14f, .5f);
            if (slot.Flash == null) return;
            Color tone = UiTheme.Current.Get(WcSlotState.RoleFor(rarity));
            tone.a = 0f;
            slot.Flash.color = tone;
            HudFx.Burst(slot.Flash, .55f, .8f, 1.5f, .8f);
        }

        /// <summary>«+1» у новой вещи: проступает, чуть всплывает, держится и гаснет.</summary>
        private void ArrivalTick(float dt)
        {
            TMPro.TMP_Text label = _view.LootArrival;
            if (label == null || _lootArrivalT < 0f) return;
            _lootArrivalT += dt;
            float t = _lootArrivalT, total = ArrivalIn + ArrivalHold + ArrivalOut;
            if (t >= total || _lootArrivalSlot >= _lootShown)
            {
                _lootArrivalT = -1f;
                RunHudView.SetActive(label, false);
                return;
            }
            float alpha = t < ArrivalIn ? t / ArrivalIn : t < ArrivalIn + ArrivalHold ? 1f : 1f - (t - ArrivalIn - ArrivalHold) / ArrivalOut;
            float k = Mathf.Clamp01(t / total);
            float rise = ArrivalRise * (1f - (1f - k) * (1f - k));
            label.alpha = Mathf.Clamp01(alpha);
            RectTransform box = label.transform.parent as RectTransform;
            if (box != null && box != _view.LootRow)
                box.anchoredPosition = new Vector2(_lootArrivalSlot * Layout.Pitch + Layout.Icon + 4f, rise);
        }

        // ---------------------------------------------------------------- мышь и подсказка

        private static bool PointerPosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            position = mouse != null ? mouse.position.ReadValue() : default;
            return mouse != null;
#else
            position = Input.mousePosition;
            return true;
#endif
        }

        /// <summary>Что под мышью: строка лучей не ловит (клик уходит в мир), попадание считается здесь.</summary>
        private void LootPointer()
        {
            int hover = HoverNone;
            if (!TickDriver.GamepadLastUsed && PointerPosition(out Vector2 pointer)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(_view.Loot, pointer, null, out Vector2 local))
            {
                // От опоры строки — левого верхнего угла: x вправо, y вниз.
                float x = local.x + _view.Loot.pivot.x * _view.Loot.sizeDelta.x;
                float y = (1f - _view.Loot.pivot.y) * _view.Loot.sizeDelta.y - local.y;
                if (y >= 0f && y <= _view.Loot.sizeDelta.y && x >= 0f && x <= _view.Loot.sizeDelta.x) hover = LootHit(x);
            }
            LootHover(hover);
        }

        private int LootHit(float x)
        {
            if (x < Layout.DividerX(_lootGoldWidth)) return HoverGold;
            float rowStart = Layout.RowStart(_lootGoldWidth);
            if (_lootHidden > 0 && x >= rowStart - Layout.Gap * .5f && x < rowStart + Layout.Icon + Layout.Gap * .5f) return HoverMore;
            float rowX = (float.IsNaN(_lootRowX) ? rowStart : _lootRowX) + _lootShift;
            int index = Mathf.FloorToInt((x - rowX + Layout.Gap * .5f) / Layout.Pitch);
            return index >= 0 && index < _lootShown ? index : HoverNone;
        }

        private void LootHover(int hover)
        {
            RunHudView v = _view;
            if (hover != _lootHover)
            {
                // Свет наведения круга — тот же, что в окнах лагеря (WcSlotState).
                SlotHover(_lootHover, false);
                SlotHover(hover, true);
                _lootHover = hover;
                _lootTipStamp = long.MinValue;
            }
            if (v.LootTip == null) return;
            if (hover == HoverNone)
            {
                RunHudView.SetActive(v.LootTip, false);
                return;
            }
            long stamp = ((long)hover << 40) ^ ((long)_loot.Count << 32) ^ (uint)_lootGold.Target;
            if (stamp != _lootTipStamp)
            {
                _lootTipStamp = stamp;
                FillLootTip(hover);
            }
            PlaceLootTip(hover);
            // Включение проявляет подсказку дымом (своя группа); между кругами — только новый текст.
            RunHudView.SetActive(v.LootTip, true);
        }

        private void SlotHover(int index, bool on)
        {
            if (index < 0 || _view.LootSlots == null || index >= _view.LootSlots.Length) return;
            WcSlotState state = _view.LootSlots[index]?.State;
            if (state == null) return;
            if (on) state.OnPointerEnter(null);
            else state.OnPointerExit(null);
        }

        private void FillLootTip(int hover)
        {
            RunHudView v = _view;
            string title, line, note = null;
            UiTheme.Role role = UiTheme.Role.Text;
            if (hover == HoverGold)
            {
                title = "Золото забега · " + _lootGold.Target;
                role = UiTheme.Role.Coins;
                line = "Унесёшь в лагерь, если выйдешь живым";
            }
            else if (hover == HoverMore)
            {
                int hidden = _lootHidden;
                int form = CampShopDeals.PluralForm(hidden);
                title = "Ещё " + hidden + " " + (form == 0 ? "вещь" : form == 1 ? "вещи" : "вещей");
                var names = new System.Text.StringBuilder();
                const int listed = 4;
                for (int i = 0; i < hidden && i < listed; i++)
                    names.Append(i > 0 ? " · " : "").Append(ItemTexts.Name(_loot[i].BaseId));
                if (hidden > listed) names.Append(" · …");
                line = names.ToString();
                int lost = 0;
                for (int i = 0; i < hidden; i++) if (!RunLootLedger.Fits(i, _lootBagFree)) lost++;
                if (lost > 0) note = "Не влезет в сумку лагеря: " + lost;
            }
            else
            {
                int entry = _lootFirst + hover;
                RunLootLedger.Entry item = _loot[entry];
                WcRarity.Tier tier = WcRarity.FromItem(item.Rarity);
                title = ItemTexts.Name(item.BaseId);
                role = WcRarity.RoleFor(tier);
                line = WcRarity.Name(tier) + " · ур. " + item.Level;
                if (!RunLootLedger.Fits(entry, _lootBagFree)) note = "В сумке лагеря нет места — вещь пропадёт";
            }
            // Тестовый забег (F8) ничего не уносит — GameSession.FinishRun.
            if (_lootDeveloper) note = "Тестовый забег — в лагерь ничего не уйдёт";
            RunHudView.SetText(v.LootTipTitle, title);
            ThemeColor tint = v.LootTipTitle != null ? v.LootTipTitle.GetComponent<ThemeColor>() : null;
            if (tint != null) tint.SetRole(role);
            RunHudView.SetText(v.LootTipLine, line);
            RunHudView.SetActive(v.LootTipNote, !string.IsNullOrEmpty(note));
            if (!string.IsNullOrEmpty(note)) RunHudView.SetText(v.LootTipNote, note);
        }

        /// <summary>Подсказка — под тем, что под мышью, но не за левым краем экрана.</summary>
        private void PlaceLootTip(int hover)
        {
            RectTransform tip = _view.LootTip;
            float centre;
            if (hover == HoverGold) centre = Layout.DividerX(_lootGoldWidth) * .5f;
            else if (hover == HoverMore) centre = Layout.RowStart(_lootGoldWidth) + Layout.Icon * .5f;
            else centre = (float.IsNaN(_lootRowX) ? 0f : _lootRowX) + _lootShift + hover * Layout.Pitch + Layout.Icon * .5f;
            float stripLeft = _view.Loot.anchoredPosition.x - _view.Loot.pivot.x * _view.Loot.sizeDelta.x;
            float half = tip.sizeDelta.x * tip.pivot.x;
            centre = Mathf.Max(centre, half - stripLeft + 8f);
            tip.anchoredPosition = new Vector2(centre, tip.anchoredPosition.y);
        }
    }
}
