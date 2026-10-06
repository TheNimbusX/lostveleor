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
        /// <summary>
        /// Ранг 1 добавляет Вену два товара. Повторный вызов ничего не делает, поэтому его
        /// можно звать из каждого пересчёта открытий.
        /// </summary>
        void ExpandTraderStock()
        {
            if (_traderStock == null || !_usesCampProgression || Rank(CampResident.Trader) < 1 || _traderStock.Length >= 6) return;
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
        /// <summary>
        /// Прилавок из сохранения v10. Товар, который лавка сегодня продать не может
        /// (неизвестная основа, редкость выше редкой, артефакт, кованая история), становится
        /// проданной позицией: подгонка к правилам вместо отказа загружать профиль.
        /// Старый размер переносится целиком, включая купленные пустые позиции.
        /// </summary>
        internal void RestoreTrader(int generation, bool boss, ItemInstance[] stock)
        {
            for (int i = 0; i < stock.Length; i++)
            {
                var item = stock[i]; int index = Items.IndexOfBase(item.BaseId);
                if (!item.IsEmpty && (index < 0 || item.Rarity > ItemRarity.Magic || Items.GetBase(index).Category == ItemCategory.Artifact
                    || item.ForgeRecipe != 0 || (item.Crafting?.Count ?? 0) > 0)) stock[i] = default;
            }
            TraderGeneration = generation; TraderBossStock = boss; _traderStock = stock;
            if (TraderReservedSlot >= stock.Length) TraderReservedSlot = -1;
            ExpandTraderStock();
        }
        void HashTraderChoices(ref ulong hash)
        { Hashing.Mix(ref hash, TraderReservedSlot); Hashing.Mix(ref hash, TraderCategoryChoice); }
        internal void WriteTraderChoices(BinaryWriter writer)
        { writer.Write(TraderReservedSlot); writer.Write(TraderCategoryChoice); }
        /// <summary>
        /// Резерв и категория из сохранения. Недоступные при нынешнем ранге или указывающие
        /// на пустую позицию сбрасываются в −1: ранг выводится заново и мог стать ниже.
        /// </summary>
        internal void ReadTraderChoices(BinaryReader reader)
        {
            if (!CampSaveCodec.Has(reader, 8)) return;
            int reserved = reader.ReadInt32(), category = reader.ReadInt32();
            TraderReservedSlot = reserved >= 0 && reserved < _traderStock.Length && !_traderStock[reserved].IsEmpty
                && Rank(CampResident.Trader) >= 2 ? reserved : -1;
            TraderCategoryChoice = category >= 0 && category < (int)ItemCategory.Artifact
                && Rank(CampResident.Trader) >= 3 ? category : -1;
        }
    }
}
