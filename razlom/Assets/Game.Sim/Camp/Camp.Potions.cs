namespace Game.Sim
{
    public enum PotionKind:byte { SmallHealth, LargeHealth, SmallLavidium, LargeLavidium }
    public sealed partial class Camp
    {
        readonly int[] _potions=new int[4];
        readonly bool[] _largePotion=new bool[2];
        public const int PotionLimit=999;
        public static int PotionPrice(PotionKind kind)=>((int)kind&1)==0?15:40;
        public static int PotionPercent(PotionKind kind)=>((int)kind&1)==0?10:30;
        public int PotionCount(PotionKind kind)=>(uint)kind<4?_potions[(int)kind]:0;
        public PotionKind SelectedPotion(int slot)=>(PotionKind)(slot*2+(_largePotion[slot]?1:0));
        public void SelectPotion(PotionKind kind){if((uint)kind<4)_largePotion[(int)kind/2]=((int)kind&1)!=0;}
        public bool BuyPotion(PotionKind kind)
        {
            if(!Has(CampService.Alchemist) || (uint)kind>=4 || _potions[(int)kind]>=PotionLimit || !Spend(CurrencyType.Gold,PotionPrice(kind)))return false;
            _potions[(int)kind]++;return true;
        }
        internal bool ConsumePotion(PotionKind kind,Simulation sim)
        {
            if((uint)kind>=4 || sim==null || !sim.Entities.Alive[0] || _potions[(int)kind]==0)return false;
            _potions[(int)kind]--;
            int percent=PotionPercent(kind);var entities=sim.Entities;
            if((int)kind<2)
            {
                long amount=System.Math.Max(1,(long)entities.MaxHealth[0]*percent/100);
                entities.Health[0]=(int)System.Math.Min(entities.MaxHealth[0],entities.Health[0]+amount);
            }
            else entities.Lavidium[0]=Fix64.Min(Fix64.FromInt(entities.MaxLavidium[0]),entities.Lavidium[0]+Fix64.FromInt(entities.MaxLavidium[0])*Fix64.Ratio(percent,100));
            return true;
        }
        internal void RestorePotions(int[] counts,byte selection)
        {
            if(counts.Length!=4 || selection>3)throw new System.IO.InvalidDataException("Некорректные зелья");
            for(int i=0;i<4;i++){if(counts[i]<0 || counts[i]>PotionLimit)throw new System.IO.InvalidDataException("Некорректный запас зелий");_potions[i]=counts[i];}
            _largePotion[0]=(selection&1)!=0;_largePotion[1]=(selection&2)!=0;
        }
        public byte PotionSelection=>(byte)((_largePotion[0]?1:0)|(_largePotion[1]?2:0));
        void HashPotions(ref ulong hash){foreach(int count in _potions)Hashing.Mix(ref hash,count);Hashing.Mix(ref hash,(int)PotionSelection);}
    }
}
