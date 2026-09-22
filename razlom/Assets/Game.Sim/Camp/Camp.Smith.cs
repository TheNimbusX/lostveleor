namespace Game.Sim
{
    public enum SmithResult { Success, InvalidItem, Protected, NoAffix, AtMaximum, Exhausted, InsufficientFunds }
    public sealed partial class Camp
    {
        public static int ReforgeGold(in ItemInstance item)=>30*(item.ReforgeCount+1);
        public static int ReforgeShards(in ItemInstance item)=>3*(item.ReforgeCount+1);
        public SmithResult ReforgeRange(int slot,int affix,out Fix64 lower,out Fix64 upper)
        {
            lower=upper=Fix64.Zero;
            if(!Has(CampService.Smith) || (uint)slot>=Bag.Capacity)return SmithResult.InvalidItem;
            var item=Bag.At(slot);if(item.IsEmpty || item.ItemLevel>short.MaxValue-4)return SmithResult.InvalidItem;
            if(item.ReforgeCount>=3)return SmithResult.Exhausted;
            var rolled=new GeneratedItem();if(!ItemGenerator.Generate(item,Items,rolled))return SmithResult.InvalidItem;
            if((uint)affix>=rolled.AffixCount)return SmithResult.NoAffix;
            var current=rolled.GetAffix(affix);upper=current.Value;
            for(int i=0;i<Items.AffixCount;i++)if(Items.GetAffix(i).Id==current.AffixId)upper=Fix64.Max(upper,Items.GetAffix(i).MaxValue);
            lower=current.Value+(upper-current.Value)*Fix64.Ratio(1,4);
            return upper<=current.Value?SmithResult.AtMaximum:SmithResult.Success;
        }
        public SmithResult Reforge(int slot,int affix)
        {
            var result=ReforgeRange(slot,affix,out _,out _);if(result!=SmithResult.Success)return result;
            var item=Bag.At(slot);int gold=ReforgeGold(item),shards=ReforgeShards(item);
            if(Money(CurrencyType.Gold)<gold || Money(CurrencyType.Shards)<shards)return SmithResult.InsufficientFunds;
            var next=new ItemInstance(item.BaseId,(short)(item.ItemLevel+1+(int)item.Rarity),item.Rarity,item.Seed,
                (ushort)(item.ForgeRecipe|((affix+1)<<(4*item.ReforgeCount))));
            Spend(CurrencyType.Gold,gold);Spend(CurrencyType.Shards,shards);
            Bag.Put(slot,next,Bag.IsKept(slot));return SmithResult.Success;
        }
        public SmithResult Dismantle(int slot,out int shards)
        {
            shards=0;
            if(!Has(CampService.Smith) || (uint)slot>=Bag.Capacity || Bag.IsEmpty(slot))return SmithResult.InvalidItem;
            if(Bag.IsKept(slot))return SmithResult.Protected;
            shards=Inventory.ShardsFor(Bag.At(slot));Bag.Remove(slot);Earn(CurrencyType.Shards,shards);return SmithResult.Success;
        }
    }
}
