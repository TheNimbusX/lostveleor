using System;
using System.IO;
namespace Game.Sim
{
    public static class CampSaveCodec
    {
        const int Magic=0x43575254;

        // Версия 2 добавила прокачку: уровень, опыт и таланты сабельной ветки.
        // Версия 3 убрала таланты: с разворота в роглайк (15 сентября) они живут
        // в забеге. Ранги версии 2 проверяются и отбрасываются, уровень остаётся.
        // Версия 1 читается как новый герой первого уровня —
        // старое сохранение не должно становиться нечитаемым из-за новой системы.
        // Версия 4 добавляет рецепт трёх перековок к каждому предмету; версии 1–3 читаются без него.
        // Версия 5 сохраняет ассортимент, проданные позиции и номер обновления торговца.
        // Версия 6 сохраняет четыре запаса зелий и размеры двух быстрых слотов.
        // Версия 7 сохраняет атлас — открытые основы; в старых открыто всё, что лежит в сумке и на герое.
        // Версия 8 добавляет два рецепта, выбранные виды и постоянные состояния заказов алхимика.
        const int Version=8;
        const int LegacyTalentLines=4, LegacyTalentsPerLine=5;

        public static byte[] Encode(Camp camp)
        {
            using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream))
            {
                w.Write(Magic);w.Write(Version);w.Write(camp.Act);w.Write(camp.Bag.Capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++)w.Write(camp.Money((CurrencyType)i));
                for(int i=0;i<camp.Bag.Capacity;i++){Write(w,camp.Bag.At(i));w.Write(camp.Bag.IsKept(i));}
                for(int i=0;i<(int)EquipSlot.Count;i++)Write(w,camp.Worn.Worn((EquipSlot)i));
                w.Write(camp.Level);w.Write(camp.Experience);
                w.Write(camp.TraderGeneration);w.Write(camp.TraderBossStock);w.Write(camp.TraderStockCount);
                for(int i=0;i<camp.TraderStockCount;i++)Write(w,camp.TraderStock(i));
                for(int i=0;i<Camp.PotionKindCount;i++)w.Write(camp.PotionCount((PotionKind)i));
                w.Write((byte)camp.SelectedPotion(0));w.Write((byte)camp.SelectedPotion(1));
                w.Write(camp.DiscoveredCount);for(int i=0;i<camp.DiscoveredCount;i++)w.Write(camp.DiscoveredAt(i));
                w.Write(camp.HasMetAlchemist);
                w.Write((byte)camp.AlchemyStatus(AlchemistOrder.Resin));
                w.Write((byte)camp.AlchemyStatus(AlchemistOrder.Surge));
                w.Flush();byte[] payload=stream.ToArray();w.Write(Checksum(payload,payload.Length));w.Flush();return stream.ToArray();
            }
        }
        public static Camp Decode(byte[] bytes,ItemDatabase items)
        {
            if(bytes==null||bytes.Length<24)throw new InvalidDataException("Неполное сохранение");
            using(var stream=new MemoryStream(bytes))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new InvalidDataException("Неизвестный формат");
                int version=r.ReadInt32();
                if(version<1||version>Version)throw new NotSupportedException("Неизвестная версия сохранения");
                if(BitConverter.ToUInt32(bytes,bytes.Length-4)!=Checksum(bytes,bytes.Length-4))throw new InvalidDataException("Повреждено сохранение");
                int act=r.ReadInt32(),capacity=r.ReadInt32();
                if(act<1||act>3||capacity!=48)throw new InvalidDataException("Некорректные параметры лагеря");
                var camp=new Camp(items,act,capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++){int money=r.ReadInt32();if(money<0)throw new InvalidDataException();camp.Earn((CurrencyType)i,money);}
                for(int i=0;i<capacity;i++){var item=Read(r,items,version);bool keep=r.ReadBoolean();camp.Bag.Put(i,item,keep);}
                for(int i=0;i<(int)EquipSlot.Count;i++)
                {var item=Read(r,items,version);if(item.IsEmpty)continue;if(Equipment.SlotOf(items.GetBase(items.IndexOfBase(item.BaseId)).Category)!=(EquipSlot)i)throw new InvalidDataException();camp.Worn.Equip(item,out _);}
                if(version>=2)ReadProgression(r,camp,version);
                if(version>=5)
                {
                    int generation=r.ReadInt32();bool boss=r.ReadBoolean();int count=r.ReadInt32();
                    // Число позиций менялось с набором предметов (21 сентября: 4 → 8); другой размер
                    // читается целиком, а RestoreTrader раскладывает прилавок заново.
                    if(count<0||count>64)throw new InvalidDataException("Некорректный размер лавки");
                    var stock=new ItemInstance[count];for(int i=0;i<count;i++)stock[i]=Read(r,items,version);
                    camp.RestoreTrader(generation,boss,stock);
                }
                if(version>=8)
                {
                    var counts=new int[Camp.PotionKindCount];
                    for(int i=0;i<counts.Length;i++)counts[i]=r.ReadInt32();
                    camp.RestorePotions(counts,(PotionKind)r.ReadByte(),(PotionKind)r.ReadByte());
                }
                else if(version>=6)
                {
                    var counts=new int[4];for(int i=0;i<4;i++)counts[i]=r.ReadInt32();
                    camp.RestorePotions(counts,r.ReadByte());
                }
                if(version>=7)
                {
                    int count=r.ReadInt32();if(count<0||count>items.BaseCount)throw new InvalidDataException("Некорректный атлас");
                    var ids=new int[count];for(int i=0;i<count;i++)ids[i]=r.ReadInt32();camp.RestoreDiscovered(ids);
                }
                if(version>=8)camp.RestoreAlchemy(r.ReadBoolean(),(AlchemistOrderStatus)r.ReadByte(),(AlchemistOrderStatus)r.ReadByte());
                camp.DiscoverHeld();
                if(stream.Position!=bytes.Length-4)throw new InvalidDataException("Лишние данные");return camp;
            }
        }
        static void ReadProgression(BinaryReader r,Camp camp,int version)
        {
            int level=r.ReadInt32(),experience=r.ReadInt32();
            if(level<1||experience<0||experience>=Progression.XpToNextLevel(level))throw new InvalidDataException("Некорректный уровень");
            if(version==2)
                for(int i=0;i<LegacyTalentLines;i++)
                {int rank=r.ReadInt32();if(rank<0||rank>LegacyTalentsPerLine)throw new InvalidDataException("Некорректный талант");}
            camp.RestoreProgression(level,experience);
        }
        static void Write(BinaryWriter w,ItemInstance i){w.Write(i.BaseId);w.Write(i.ItemLevel);w.Write((byte)i.Rarity);w.Write(i.Seed);w.Write(i.ForgeRecipe);}
        static ItemInstance Read(BinaryReader r,ItemDatabase db,int version)
        {var i=new ItemInstance(r.ReadInt32(),r.ReadInt16(),(ItemRarity)r.ReadByte(),r.ReadUInt64(),version>=4?r.ReadUInt16():(ushort)0);if(i.ForgeRecipe>0x666 || i.OriginalLevel<0 || (i.ForgeRecipe!=0 && ((i.ForgeRecipe&15)==0 || (i.ForgeRecipe&15)>6 || ((i.ForgeRecipe>>4)&15)>6 || ((i.ForgeRecipe>>8)>0 && ((i.ForgeRecipe>>4)&15)==0))))throw new InvalidDataException("Некорректная перековка");if(!i.IsEmpty&&(db.IndexOfBase(i.BaseId)<0||i.OriginalLevel<1||(int)i.Rarity>(int)ItemRarity.Unique))throw new InvalidDataException("Некорректный предмет");return i;}
        static uint Checksum(byte[] bytes,int count){uint h=2166136261;for(int i=0;i<count;i++){h^=bytes[i];h=unchecked(h*16777619);}return h;}
    }
}
