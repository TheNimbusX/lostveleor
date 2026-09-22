namespace Game.Sim
{
    public sealed partial class Camp
    {
        public const int TraderRefreshPrice=50;
        public const int TraderRareChance=10,TraderBossRareChance=25;
        ItemInstance[] _traderStock;
        public int TraderGeneration { get; private set; }
        public bool TraderBossStock { get; private set; }
        public int TraderStockCount=>Has(CampService.Trader)?_traderStock.Length:0;
        public ItemInstance TraderStock(int index)=>(uint)index<TraderStockCount?_traderStock[index]:default;
        void InitializeTrader()
        {
            int count=0;for(int i=0;i<Items.BaseCount;i++)if(Stocked(Items.GetBase(i)))count++;
            _traderStock=new ItemInstance[count];RollTrader(false);
        }
        void RollTrader(bool boss)
        {
            TraderBossStock=boss;
            // Лавка имеет собственный поток: её обновление не меняет добычу и бой.
            var rng=new Pcg32((ulong)TraderGeneration+0x545241444552UL,0x53544F434BUL);int slot=0;
            for(int i=0;i<Items.BaseCount;i++)
            {
                var definition=Items.GetBase(i);if(!Stocked(definition))continue;
                var rarity=rng.NextInt(0,100)<(boss?TraderBossRareChance:TraderRareChance)?ItemRarity.Magic:ItemRarity.Normal;
                ulong seed=((ulong)rng.NextUInt()<<32)|rng.NextUInt();
                var item=new ItemInstance(definition.Id,(short)(Act*3),rarity,seed);
                // Редкая позиция становится одной из редких основ того же слота.
                _traderStock[slot++]=rarity==ItemRarity.Normal?item:Items.MatchTier(item);
            }
        }
        public bool RefreshTrader()
        {
            if(!Has(CampService.Trader) || TraderGeneration==int.MaxValue || !Spend(CurrencyType.Gold,TraderRefreshPrice))return false;
            TraderGeneration++;RollTrader(false);return true;
        }
        public void RefreshTraderAfterBoss()
        {
            if(!Has(CampService.Trader) || TraderGeneration==int.MaxValue)return;
            TraderGeneration++;RollTrader(true);
        }
        /// <summary>Позиция прилавка — каждая обычная основа, кроме артефактов.</summary>
        static bool Stocked(ItemBaseDefinition definition)=>definition.Category!=ItemCategory.Artifact && !definition.Rare;
        internal void RestoreTrader(int generation,bool boss,ItemInstance[] stock)
        {
            if(generation<0)throw new System.IO.InvalidDataException("Некорректный ассортимент");
            // Прилавок из сохранения до нового набора предметов (другое число позиций) не переносится:
            // лавка того же номера обновления раскладывается заново.
            if(stock.Length!=_traderStock.Length){TraderGeneration=generation;RollTrader(boss);return;}
            foreach(var item in stock)
                if(!item.IsEmpty && (item.Rarity>ItemRarity.Magic || Items.GetBase(Items.IndexOfBase(item.BaseId)).Category==ItemCategory.Artifact || item.ForgeRecipe!=0))throw new System.IO.InvalidDataException("Некорректный товар");
            TraderGeneration=generation;TraderBossStock=boss;_traderStock=stock;
        }
    }
}
