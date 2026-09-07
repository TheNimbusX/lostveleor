using System;
using System.IO;
namespace Game.Sim
{
    public static class CampSaveCodec
    {
        const int Magic=0x43575254;
        public static byte[] Encode(Camp camp)
        {
            using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream))
            {
                w.Write(Magic);w.Write(1);w.Write(camp.Act);w.Write(camp.Bag.Capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++)w.Write(camp.Money((CurrencyType)i));
                for(int i=0;i<camp.Bag.Capacity;i++){Write(w,camp.Bag.At(i));w.Write(camp.Bag.IsKept(i));}
                for(int i=0;i<(int)EquipSlot.Count;i++)Write(w,camp.Worn.Worn((EquipSlot)i));
                w.Flush();byte[] payload=stream.ToArray();w.Write(Checksum(payload,payload.Length));w.Flush();return stream.ToArray();
            }
        }
        public static Camp Decode(byte[] bytes,ItemDatabase items)
        {
            if(bytes==null||bytes.Length<24)throw new InvalidDataException("Неполное сохранение");
            using(var stream=new MemoryStream(bytes))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new InvalidDataException("Неизвестный формат");
                if(r.ReadInt32()!=1)throw new NotSupportedException("Неизвестная версия сохранения");
                if(BitConverter.ToUInt32(bytes,bytes.Length-4)!=Checksum(bytes,bytes.Length-4))throw new InvalidDataException("Повреждено сохранение");
                int act=r.ReadInt32(),capacity=r.ReadInt32();
                if(act<1||act>3||capacity!=48)throw new InvalidDataException("Некорректные параметры лагеря");
                var camp=new Camp(items,act,capacity);
                for(int i=0;i<(int)CurrencyType.Count;i++){int money=r.ReadInt32();if(money<0)throw new InvalidDataException();camp.Earn((CurrencyType)i,money);}
                for(int i=0;i<capacity;i++){var item=Read(r,items);bool keep=r.ReadBoolean();camp.Bag.Put(i,item,keep);}
                for(int i=0;i<(int)EquipSlot.Count;i++)
                {var item=Read(r,items);if(item.IsEmpty)continue;if(Equipment.SlotOf(items.GetBase(items.IndexOfBase(item.BaseId)).Category)!=(EquipSlot)i)throw new InvalidDataException();camp.Worn.Equip(item,out _);}
                if(stream.Position!=bytes.Length-4)throw new InvalidDataException("Лишние данные");return camp;
            }
        }
        static void Write(BinaryWriter w,ItemInstance i){w.Write(i.BaseId);w.Write(i.ItemLevel);w.Write((byte)i.Rarity);w.Write(i.Seed);}
        static ItemInstance Read(BinaryReader r,ItemDatabase db)
        {var i=new ItemInstance(r.ReadInt32(),r.ReadInt16(),(ItemRarity)r.ReadByte(),r.ReadUInt64());if(!i.IsEmpty&&(db.IndexOfBase(i.BaseId)<0||i.ItemLevel<1||(int)i.Rarity>2))throw new InvalidDataException("Некорректный предмет");return i;}
        static uint Checksum(byte[] bytes,int count){uint h=2166136261;for(int i=0;i<count;i++){h^=bytes[i];h=unchecked(h*16777619);}return h;}
    }
}
