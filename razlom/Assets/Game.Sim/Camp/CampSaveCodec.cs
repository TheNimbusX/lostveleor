using System;
using System.IO;
namespace Game.Sim
{
    public static class CampSaveCodec
    {
        const int Magic=0x43575254;

        // Версия 2 добавила прокачку: уровень, опыт и таланты сабельной ветки.
        // Версия 1 читается как новый герой первого уровня без талантов —
        // старое сохранение не должно становиться нечитаемым из-за новой системы.
        const int Version=2;

        public static byte[] Encode(Camp camp)
        {
            using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream))
            {
                w.Write(Magic);w.Write(Version);w.Write(camp.Act);w.Write(camp.Bag.Capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++)w.Write(camp.Money((CurrencyType)i));
                for(int i=0;i<camp.Bag.Capacity;i++){Write(w,camp.Bag.At(i));w.Write(camp.Bag.IsKept(i));}
                for(int i=0;i<(int)EquipSlot.Count;i++)Write(w,camp.Worn.Worn((EquipSlot)i));
                w.Write(camp.Level);w.Write(camp.Experience);
                for(int i=0;i<SabreTalents.LineCount;i++)w.Write(camp.SabreTalentRank((SabreTalentLine)i));
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
                if(version!=1&&version!=2)throw new NotSupportedException("Неизвестная версия сохранения");
                if(BitConverter.ToUInt32(bytes,bytes.Length-4)!=Checksum(bytes,bytes.Length-4))throw new InvalidDataException("Повреждено сохранение");
                int act=r.ReadInt32(),capacity=r.ReadInt32();
                if(act<1||act>3||capacity!=48)throw new InvalidDataException("Некорректные параметры лагеря");
                var camp=new Camp(items,act,capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++){int money=r.ReadInt32();if(money<0)throw new InvalidDataException();camp.Earn((CurrencyType)i,money);}
                for(int i=0;i<capacity;i++){var item=Read(r,items);bool keep=r.ReadBoolean();camp.Bag.Put(i,item,keep);}
                for(int i=0;i<(int)EquipSlot.Count;i++)
                {var item=Read(r,items);if(item.IsEmpty)continue;if(Equipment.SlotOf(items.GetBase(items.IndexOfBase(item.BaseId)).Category)!=(EquipSlot)i)throw new InvalidDataException();camp.Worn.Equip(item,out _);}
                if(version>=2)ReadProgression(r,camp);
                if(stream.Position!=bytes.Length-4)throw new InvalidDataException("Лишние данные");return camp;
            }
        }
        static void ReadProgression(BinaryReader r,Camp camp)
        {
            int level=r.ReadInt32(),experience=r.ReadInt32();
            if(level<1||experience<0||experience>=Progression.XpToNextLevel(level))throw new InvalidDataException("Некорректный уровень");
            var ranks=new int[SabreTalents.LineCount];int spent=0;
            for(int i=0;i<ranks.Length;i++)
            {ranks[i]=r.ReadInt32();if(ranks[i]<0||ranks[i]>SabreTalents.TalentsPerLine)throw new InvalidDataException("Некорректный талант");spent+=ranks[i];}
            if(spent>Progression.TalentPointsAtLevel(level))throw new InvalidDataException("Талантов больше, чем очков");
            camp.RestoreProgression(level,experience,ranks);
        }
        static void Write(BinaryWriter w,ItemInstance i){w.Write(i.BaseId);w.Write(i.ItemLevel);w.Write((byte)i.Rarity);w.Write(i.Seed);}
        static ItemInstance Read(BinaryReader r,ItemDatabase db)
        {var i=new ItemInstance(r.ReadInt32(),r.ReadInt16(),(ItemRarity)r.ReadByte(),r.ReadUInt64());if(!i.IsEmpty&&(db.IndexOfBase(i.BaseId)<0||i.ItemLevel<1||(int)i.Rarity>2))throw new InvalidDataException("Некорректный предмет");return i;}
        static uint Checksum(byte[] bytes,int count){uint h=2166136261;for(int i=0;i<count;i++){h^=bytes[i];h=unchecked(h*16777619);}return h;}
    }
}
