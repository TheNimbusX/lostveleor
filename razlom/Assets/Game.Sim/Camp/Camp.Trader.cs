using System;
using System.IO;

namespace Game.Sim
{
    public sealed partial class Camp
    {
        public const int TraderRefreshPrice = 50;
        public const int TraderRareChance = 10, TraderBossRareChance = 25;
        ItemInstance[] _traderStock;
        public int TraderGeneration { get; private set; }
        public bool TraderBossStock { get; private set; }
        public int TraderReservedSlot { get; private set; } = -1;
        public int TraderCategoryChoice { get; private set; } = -1;
        public int TraderStockCount => Has(CampService.Trader) ? _traderStock.Length : 0;
        public ItemInstance TraderStock(int index) => (uint)index < TraderStockCount ? _traderStock[index] : default;
        internal int TraderStoredStockCount => _traderStock.Length;
        internal ItemInstance TraderStoredStock(int index) => _traderStock[index];
        void InitializeTrader()
        {
            int count = 0; for (int i = 0; i < Items.BaseCount; i++) if (Stocked(Items.GetBase(i))) count++;
            _traderStock = new ItemInstance[_usesCampProgression ? Math.Min(count, 4) : count]; RollTrader(false);
        }
        int BaseForStockSlot(int slot, int category = -1)
        {
            if (category < 0 && _usesCampProgression) category = slot % 4;
            int first = -1, count = 0;
            for (int i = 0; i < Items.BaseCount; i++)
                if (Stocked(Items.GetBase(i)) && (category < 0 || (int)Items.GetBase(i).Category == category)) { if (first < 0) first = i; count++; }
            if (count == 0) return -1;
            int pick = category >= 0 ? (slot / 4) % count : slot % count;
            for (int i = first; i < Items.BaseCount; i++)
                if (Stocked(Items.GetBase(i)) && (category < 0 || (int)Items.GetBase(i).Category == category) && pick-- == 0) return i;
            return first;
        }
        void RollTrader(bool boss)
        {
            TraderBossStock = boss;
            // Лавка использует собственный поток; резерв не расходует и не возвращает броски.
            var rng = new Pcg32((ulong)TraderGeneration + 0x545241444552UL, 0x53544F434BUL);
            bool categoryApplied = false;
            for (int slot = 0; slot < _traderStock.Length; slot++)
            {
                var rarity = rng.NextInt(0, 100) < (boss ? TraderBossRareChance : TraderRareChance) ? ItemRarity.Magic : ItemRarity.Normal;
                ulong seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
                if (slot == TraderReservedSlot && !_traderStock[slot].IsEmpty) continue;
                int category = !categoryApplied && TraderCategoryChoice >= 0 ? TraderCategoryChoice : -1;
                int definition = BaseForStockSlot(slot, category);
                if (category >= 0) categoryApplied = true;
                if (definition < 0) { _traderStock[slot] = default; continue; }
                var item = new ItemInstance(Items.GetBase(definition).Id, (short)(Act * 3), rarity, seed);
                _traderStock[slot] = rarity == ItemRarity.Normal ? item : Items.MatchTier(item);
            }
        }
        void ExpandTraderStock()
        {
            if (!_usesCampProgression || Rank(CampResident.Trader) < 1 || _traderStock.Length >= 6) return;
            var old = _traderStock; _traderStock = new ItemInstance[6]; RollTrader(TraderBossStock);
            Array.Copy(old, _traderStock, old.Length);
        }
        public bool ReserveTraderStock(int index)
        {
            if (!HasResident(CampResident.Trader) || Rank(CampResident.Trader) < 2) return false;
            if (index == -1) { TraderReservedSlot = -1; return true; }
            if ((uint)index >= TraderStockCount || _traderStock[index].IsEmpty) return false;
            TraderReservedSlot = index; return true;
        }
        public bool ChooseTraderCategory(ItemCategory category)
        {
            if (!HasResident(CampResident.Trader) || Rank(CampResident.Trader) < 3
                || (uint)category >= (uint)ItemCategory.Artifact || BaseForStockSlot(0, (int)category) < 0) return false;
            TraderCategoryChoice = (int)category; return true;
        }
        public bool RefreshTrader()
        {
            if (!Has(CampService.Trader) || TraderGeneration == int.MaxValue || !Spend(CurrencyType.Gold, TraderRefreshPrice)) return false;
            TraderGeneration++; RollTrader(false); return true;
        }
        public void RefreshTraderAfterBoss()
        {
            if (!Has(CampService.Trader) || TraderGeneration == int.MaxValue) return;
            TraderGeneration++; RollTrader(true);
        }
        static bool Stocked(ItemBaseDefinition definition) => definition.Category != ItemCategory.Artifact && !definition.Rare;
        internal void RestoreTrader(int generation, bool boss, ItemInstance[] stock)
        {
            if (generation < 0) throw new InvalidDataException("Некорректный ассортимент");
            foreach (var item in stock)
            {
                int index = Items.IndexOfBase(item.BaseId);
                if (!item.IsEmpty && (index < 0 || item.Rarity > ItemRarity.Magic || Items.GetBase(index).Category == ItemCategory.Artifact
                    || item.ForgeRecipe != 0 || (item.Crafting?.Count ?? 0) > 0)) throw new InvalidDataException("Некорректный товар");
            }
            // Старый прилавок переносится целиком, включая купленные пустые позиции.
            TraderGeneration = generation; TraderBossStock = boss; _traderStock = stock;
        }
        void HashTraderChoices(ref ulong hash)
        { Hashing.Mix(ref hash, TraderReservedSlot); Hashing.Mix(ref hash, TraderCategoryChoice); }
        internal void WriteTraderChoices(BinaryWriter writer)
        { writer.Write(TraderReservedSlot); writer.Write(TraderCategoryChoice); }
        internal void ReadTraderChoices(BinaryReader reader)
        {
            TraderReservedSlot = reader.ReadInt32(); TraderCategoryChoice = reader.ReadInt32();
            if (TraderReservedSlot < -1 || TraderReservedSlot >= _traderStock.Length || TraderReservedSlot >= 0 && _traderStock[TraderReservedSlot].IsEmpty
                || TraderCategoryChoice < -1 || TraderCategoryChoice >= (int)ItemCategory.Artifact
                || TraderReservedSlot >= 0 && Rank(CampResident.Trader) < 2 || TraderCategoryChoice >= 0 && Rank(CampResident.Trader) < 3)
                throw new InvalidDataException("Некорректный выбор торговца");
        }
    }
}
