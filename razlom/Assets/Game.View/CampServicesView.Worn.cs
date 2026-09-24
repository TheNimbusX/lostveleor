using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Ряд «Надето» в окнах кузнеца и торговца (владелец 23 сентября: «чтоб было видно
    /// надетые вещи и чтоб можно было их перековать прям у них в окне»). Кузнец перековывает
    /// надетое, разбирает только из сумки; торговец показывает надетое для сравнения,
    /// продаёт только из сумки. Отдельный файл: реплики в Smith/Trader правит другой автор.
    /// </summary>
    public sealed partial class CampServicesView
    {
        bool _smithWorn, _traderWorn;

        void WireWorn(CampShopScreen s, System.Action<int> pick)
        {
            if (s.WornCaption != null) s.WornCaption.text = CampServiceText.Get("shop.worn");
            for (int i = 0; i < s.Worn.Length; i++)
            {
                int slot = i;
                if (s.Worn[i] != null && s.Worn[i].Button != null) s.Worn[i].Button.onClick.AddListener(() => pick(slot));
            }
        }

        /// <summary>selected — выбранный слот надетого или -1; locked — надетое сейчас недоступно (разбор).</summary>
        void ShowWorn(CampShopScreen s, Camp camp, int selected, bool locked)
        {
            var inventory = GetComponent<CampInventoryView>();
            for (int i = 0; i < s.Worn.Length; i++)
            {
                var cell = s.Worn[i];
                if (cell == null) continue;
                var item = i < (int)EquipSlot.Count ? camp.Worn.Worn((EquipSlot)i) : default;
                if (cell.Button != null) cell.Button.interactable = !item.IsEmpty && !locked;
                cell.Show(item.IsEmpty ? null : inventory.SpriteFor(item), item.IsEmpty ? "" : item.ItemLevel.ToString(), (int)item.Rarity, i == selected);
                cell.SetDimmed(locked && !item.IsEmpty);
            }
        }

        static ItemInstance WornOrBag(Camp camp, int slot, bool worn) =>
            slot < 0 ? default : worn ? (slot < (int)EquipSlot.Count ? camp.Worn.Worn((EquipSlot)slot) : default) : camp.Bag.At(slot);

        static string WornTag => "  ·  <color=#3BF0F5>" + CampServiceText.Get("shop.worn.tag") + "</color>";

        /// <summary>
        /// Проверка без прогресса (CampServicesProbe): надетая вещь выбирается в ряду «Надето»,
        /// перековывается кнопкой окна, остаётся надетой; на «Разборе» ряд недоступен;
        /// торговец показывает надетое без цены и не продаёт его.
        /// </summary>
        internal bool ProbeWornTransactions()
        {
            WireSmith(); WireTrader();
            var smith = _view.Smith;
            var original = _smithCamp;
            var traderOriginal = _traderCamp;
            try
            {
                var camp = new Camp(PrototypeContent.Items());
                camp.Earn(CurrencyType.Gold, 200); camp.Earn(CurrencyType.Shards, 20);
                camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 10, ItemRarity.Magic, 123));
                if (!camp.EquipFromBag(0) || smith.Worn.Length == 0) return false;
                _smithCamp = camp;
                _dismantling = false; _confirmDismantle = false;
                smith.Worn[0].Button.onClick.Invoke();
                if (!_smithWorn || _smithSlot != 0 || !smith.Action.interactable) return false;
                smith.Action.onClick.Invoke();
                var worn = camp.Worn.Worn(EquipSlot.Weapon);
                if (worn.ReforgeCount != 1 || !camp.Bag.IsEmpty(0) || camp.Money(CurrencyType.Gold) != 170) return false;
                smith.Tabs[1].onClick.Invoke();
                if (_smithWorn || smith.Worn[0].Button.interactable) return false;
                smith.Tabs[0].onClick.Invoke();

                var trader = _view.Trader;
                if (trader.Worn.Length == 0) return false;
                _traderCamp = camp;
                SetTradeMode(true);
                trader.Worn[0].Button.onClick.Invoke();
                bool shown = _traderWorn && !trader.Action.interactable && !trader.Price.activeSelf;
                trader.Action.onClick.Invoke();
                return shown && !camp.Worn.Worn(EquipSlot.Weapon).IsEmpty && camp.Money(CurrencyType.Gold) == 170;
            }
            finally
            {
                _smithCamp = original; _smithSlot = -1; _smithWorn = false; _dismantling = false; _confirmDismantle = false;
                if (_smithCamp != null) RefreshSmith();
                _traderCamp = traderOriginal; _traderSlot = -1; _traderWorn = false; _selling = false;
                if (_traderCamp != null) RefreshTraderPanel();
                _view.Smith.Message.text = "";
            }
        }
    }
}
