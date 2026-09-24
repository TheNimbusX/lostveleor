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
            if((uint)slot>=Bag.Capacity)return SmithResult.InvalidItem;
            return ReforgeRange(Bag.At(slot),affix,out lower,out upper);
        }
        /// <summary>Перековка надетой вещи прямо у кузнеца (владелец 23 сентября): без снятия в палатке.</summary>
        public SmithResult ReforgeRange(EquipSlot slot,int affix,out Fix64 lower,out Fix64 upper)
        {
            lower=upper=Fix64.Zero;
            if((uint)slot>=(uint)EquipSlot.Count)return SmithResult.InvalidItem;
            return ReforgeRange(Worn.Worn(slot),affix,out lower,out upper);
        }
        SmithResult ReforgeRange(in ItemInstance item,int affix,out Fix64 lower,out Fix64 upper)
        {
            lower=upper=Fix64.Zero;
            if(!Has(CampService.Smith) || item.IsEmpty || item.ItemLevel>short.MaxValue-4)return SmithResult.InvalidItem;
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
            if(!TryPayReforge(Bag.At(slot),affix,out var next))return SmithResult.InsufficientFunds;
            Bag.Put(slot,next,Bag.IsKept(slot));return SmithResult.Success;
        }
        /// <summary>Перековка надетой вещи: слот тот же, прибавки вещи пересчитываются сразу.</summary>
        public SmithResult Reforge(EquipSlot slot,int affix)
        {
            var result=ReforgeRange(slot,affix,out _,out _);if(result!=SmithResult.Success)return result;
            if(!TryPayReforge(Worn.Worn(slot),affix,out var next))return SmithResult.InsufficientFunds;
            Worn.Equip(in next,out _);return SmithResult.Success;
        }
        bool TryPayReforge(in ItemInstance item,int affix,out ItemInstance next)
        {
            int gold=ReforgeGold(item),shards=ReforgeShards(item);
            next=default;
            if(Money(CurrencyType.Gold)<gold || Money(CurrencyType.Shards)<shards)return false;
            next=new ItemInstance(item.BaseId,(short)(item.ItemLevel+1+(int)item.Rarity),item.Rarity,item.Seed,
                (ushort)(item.ForgeRecipe|((affix+1)<<(4*item.ReforgeCount))));
            Spend(CurrencyType.Gold,gold);Spend(CurrencyType.Shards,shards);
            return true;
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
