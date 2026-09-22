using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// Атлас находок (владелец, 21 сентября): какие основы игрок уже держал в руках.
    /// Основа открывается, как только вещь легла в сумку — из забега, от торговца,
    /// при снятии с героя. Порядок — по id, как в справочнике: сохранение побайтово
    /// повторяется.
    /// </summary>
    public sealed partial class Camp
    {
        readonly List<int> _discovered = new List<int>();

        public int DiscoveredCount => _discovered.Count;
        public int DiscoveredAt(int index) => _discovered[index];
        public bool Discovered(int baseId) => _discovered.BinarySearch(baseId) >= 0;

        void Discover(int baseId)
        {
            if (Items.IndexOfBase(baseId) < 0) return;
            int at = _discovered.BinarySearch(baseId);
            if (at < 0) _discovered.Insert(~at, baseId);
        }

        /// <summary>Сохранения до атласа: открыто всё, что уже лежит в сумке и на герое.</summary>
        internal void DiscoverHeld()
        {
            for (int i = 0; i < Bag.Capacity; i++) if (!Bag.IsEmpty(i)) Discover(Bag.At(i).BaseId);
            for (int i = 0; i < (int)EquipSlot.Count; i++)
            {
                var item = Worn.Worn((EquipSlot)i);
                if (!item.IsEmpty) Discover(item.BaseId);
            }
        }

        internal void RestoreDiscovered(int[] ids)
        {
            foreach (int id in ids)
            {
                if (Items.IndexOfBase(id) < 0) throw new System.IO.InvalidDataException("Неизвестная основа в атласе");
                Discover(id);
            }
        }
    }
}
