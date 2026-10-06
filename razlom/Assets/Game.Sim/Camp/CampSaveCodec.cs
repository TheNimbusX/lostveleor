using System;
using System.IO;
namespace Game.Sim
{
    /// <summary>
    /// Сохранение лагеря, формат v10 (решение 06.10, план A.7).
    ///
    /// Файл: int32 Magic, int32 Version = 10, затем секции { uint16 tag, int32 length,
    /// payload } по возрастанию тега, в конце uint32 FNV-1a по всему, что перед ней.
    ///
    /// ПОЧЕМУ СЕКЦИИ. Клятвы, закалка и стол сборов дописываются завтра (T1–T3), а профиль
    /// владельца сбрасывается один раз — сегодня. Новая система добавляет свою секцию
    /// или поля в конец своей секции, версия остаётся 10: читатель пропускает незнакомый
    /// тег и хвост секции, а недостающие поля берёт по умолчанию.
    ///
    /// ДВА РОДА ОШИБОК. Повреждение — неверная структура (магия, контрольная сумма, длины,
    /// знак счётчиков, байты перечислений вне диапазона) — это InvalidDataException.
    /// Смена правил или справочника (неизвестная основа, снятый босс, закрытое зелье,
    /// неверный по нынешним правилам рецепт) профиль не роняет: значение подгоняется.
    /// Старые версии 1–9 не читаются вовсе (CampSaveObsoleteException) — профиль
    /// начинается заново, файл остаётся копией у CampSaveStore.
    /// </summary>
    public static class CampSaveCodec
    {
        const int Magic=0x43575254;
        public const int Version=10;

        // Теги секций. Значения лежат в файлах игроков: не переставлять, только дописывать.
        const ushort CoreTag=1,BagTag=2,WornTag=3,TraderTag=4,PotionsTag=5,ProgressTag=6,CollectionTag=7,
            PreparationTag=8,OathsTag=9,ForgeSessionTag=10,HeatVowsTag=11,LastTag=11;
        // Жёсткие пределы против раздутых длин: проверяются до выделения памяти.
        const int MaxTraderStock=64,MaxListEntries=64;
        const int ItemHeaderBytes=4+2+1+8+2,StepBytes=1+1+4+8;

        public static byte[] Encode(Camp camp)
        {
            using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream))
            using(var body=new MemoryStream()) using(var b=new BinaryWriter(body))
            {
                w.Write(Magic);w.Write(Version);

                b.Write((byte)(camp.IsProgressive?1:0));b.Write((byte)camp.Act);b.Write(camp.Bag.Capacity);
                b.Write(camp.Level);b.Write(camp.Experience);
                b.Write((byte)CurrencyType.Count);for(int i=0;i<(int)CurrencyType.Count;i++)b.Write(camp.Money((CurrencyType)i));
                Section(w,b,body,CoreTag);

                for(int i=0;i<camp.Bag.Capacity;i++){Write(b,camp.Bag.At(i));b.Write(camp.Bag.IsKept(i));}
                Section(w,b,body,BagTag);

                b.Write((byte)EquipSlot.Count);for(int i=0;i<(int)EquipSlot.Count;i++)Write(b,camp.Worn.Worn((EquipSlot)i));
                Section(w,b,body,WornTag);

                b.Write(camp.TraderGeneration);b.Write(camp.TraderBossStock);b.Write(camp.TraderStoredStockCount);
                for(int i=0;i<camp.TraderStoredStockCount;i++)Write(b,camp.TraderStoredStock(i));
                camp.WriteTraderChoices(b);
                Section(w,b,body,TraderTag);

                b.Write((byte)Camp.PotionKindCount);for(int i=0;i<Camp.PotionKindCount;i++)b.Write(camp.PotionCount((PotionKind)i));
                b.Write((byte)camp.SelectedPotion(0));b.Write((byte)camp.SelectedPotion(1));
                Section(w,b,body,PotionsTag);

                b.Write(camp.AttemptCount);b.Write(camp.DeepestAttempt);
                b.Write((byte)((camp.ExtractedFind?1:0)|(camp.SmithFindDiscussed?2:0)));
                b.Write(camp.UsedPotionKinds);b.Write(camp.ChapterBits);b.Write((uint)camp.PendingUnlocks);
                WriteBossCounts(b,camp,false);WriteBossCounts(b,camp,true);
                Section(w,b,body,ProgressTag);

                b.Write((ushort)camp.EverTakenSkillCount);for(int i=0;i<camp.EverTakenSkillCount;i++)b.Write(camp.EverTakenSkillAt(i));
                b.Write(camp.OpenedArtifactMask);
                Section(w,b,body,CollectionTag);

                camp.WritePreparation(b);
                Section(w,b,body,PreparationTag);

                // Клятвы (T2) и сессия кузнеца (T1) пишут свои секции сами; пустая не пишется,
                // поэтому сегодняшний файл без них, а завтра они появятся без правки кодека.
                camp.WriteOaths(b);
                Section(w,b,body,OathsTag,skipEmpty:true);
                camp.WriteForgeSession(b);
                Section(w,b,body,ForgeSessionTag,skipEmpty:true);

                // Резерв под «Обеты сложности» (05.10): API нет, пока владелец к ним не вернётся.
                b.Write(0);
                Section(w,b,body,HeatVowsTag);

                w.Flush();byte[] payload=stream.ToArray();w.Write(Checksum(payload,payload.Length));w.Flush();return stream.ToArray();
            }
        }

        static void Section(BinaryWriter file,BinaryWriter b,MemoryStream body,ushort tag,bool skipEmpty=false)
        {
            b.Flush();
            int length=(int)body.Length;
            if(length>0||!skipEmpty){file.Write(tag);file.Write(length);file.Write(body.GetBuffer(),0,length);}
            body.SetLength(0);
        }

        /// <summary>Победы или сердца: число записей и пары (ключ, счёт) по возрастанию ключа, нули не пишутся.</summary>
        static void WriteBossCounts(BinaryWriter b,Camp camp,bool hearts)
        {
            var order=new int[RunBossKeys.Count];int count=0;
            for(int i=0;i<RunBossKeys.Count;i++)
            {
                if((hearts?camp.HeartsAt(i):camp.BossDefeatsAt(i))==0)continue;
                int at=count++;
                while(at>0&&RunBossKeys.At(order[at-1])>RunBossKeys.At(i)){order[at]=order[at-1];at--;}
                order[at]=i;
            }
            b.Write((byte)count);
            for(int i=0;i<count;i++){b.Write(RunBossKeys.At(order[i]));b.Write(hearts?camp.HeartsAt(order[i]):camp.BossDefeatsAt(order[i]));}
        }

        /// <summary>Версия файла без разбора; −1 — не наш файл или короче заголовка.</summary>
        public static int PeekVersion(byte[] bytes)
        {
            if(bytes==null||bytes.Length<8||BitConverter.ToInt32(bytes,0)!=Magic)return -1;
            return BitConverter.ToInt32(bytes,4);
        }

        public static Camp Decode(byte[] bytes,ItemDatabase items)
        {
            if(bytes==null||bytes.Length<8)throw new InvalidDataException("Неполное сохранение");
            if(BitConverter.ToInt32(bytes,0)!=Magic)throw new InvalidDataException("Неизвестный формат");
            int version=BitConverter.ToInt32(bytes,4);
            // Версия раньше контрольной суммы: у будущих версий она может считаться иначе.
            if(version>=1&&version<Version)throw new CampSaveObsoleteException(version);
            if(version>Version)throw new CampSaveFutureException(version);
            if(version!=Version||bytes.Length<12)throw new InvalidDataException("Некорректная версия сохранения");
            int end=bytes.Length-4;
            if(BitConverter.ToUInt32(bytes,end)!=Checksum(bytes,end))throw new InvalidDataException("Повреждено сохранение");
            try{return Read(bytes,end,items);}
            catch(EndOfStreamException e){throw new InvalidDataException("Неполная секция сохранения",e);}
        }

        static Camp Read(byte[] bytes,int end,ItemDatabase items)
        {
            // Сначала оглавление: секции идут в любом порядке, а применять их надо по
            // зависимостям (ранг Вена нужен до его резерва, уровень — до рангов).
            var offsets=new int[LastTag+1];var lengths=new int[LastTag+1];
            for(int i=0;i<=LastTag;i++)offsets[i]=-1;
            var unknown=new System.Collections.Generic.List<ushort>();
            int position=8;
            while(position<end)
            {
                if(end-position<6)throw new InvalidDataException("Обрезан заголовок секции");
                ushort tag=BitConverter.ToUInt16(bytes,position);int length=BitConverter.ToInt32(bytes,position+2);position+=6;
                if(length<0||length>end-position)throw new InvalidDataException("Длина секции вне файла");
                // Пишутся секции по возрастанию тега, но читаются в любом порядке. Повтор —
                // порча: какой из двух верный, не узнать.
                bool known=tag>=1&&tag<=LastTag;
                if(known?offsets[tag]>=0:unknown.Contains(tag))throw new InvalidDataException("Повтор секции "+tag);
                if(known){offsets[tag]=position;lengths[tag]=length;}else unknown.Add(tag);
                position+=length;
            }
            if(offsets[CoreTag]<0||offsets[BagTag]<0||offsets[WornTag]<0)throw new InvalidDataException("Нет обязательной секции");

            Camp camp;
            using(var r=Open(bytes,offsets,lengths,CoreTag))
            {
                if(!Has(r,1+1+4+4+4))throw new InvalidDataException("Обрезано ядро сохранения");
                byte flags=r.ReadByte();int act=r.ReadByte(),capacity=r.ReadInt32(),level=r.ReadInt32(),experience=r.ReadInt32();
                if((flags&~1)!=0||act<1||act>3||capacity!=Camp.DefaultBagSlots||level<1||experience<0)
                    throw new InvalidDataException("Некорректные параметры лагеря");
                // Кривая опыта могла стать круче: лишний опыт не поднимает уровень задним числом.
                int threshold=Progression.XpToNextLevel(level);
                if(experience>=threshold)experience=threshold-1;
                camp=new Camp(items,act,capacity,progressive:(flags&1)!=0);
                camp.RestoreProgression(level,experience);
                int wallet=Has(r,1)?r.ReadByte():0;
                for(int i=0;i<wallet&&Has(r,4);i++)
                {
                    int money=r.ReadInt32();if(money<0)throw new InvalidDataException("Отрицательные деньги");
                    if(i<(int)CurrencyType.Count)camp.Earn((CurrencyType)i,money);
                }
            }

            if(offsets[ProgressTag]>=0)using(var r=Open(bytes,offsets,lengths,ProgressTag))ReadProgress(r,camp);
            if(offsets[CollectionTag]>=0)using(var r=Open(bytes,offsets,lengths,CollectionTag))
            {
                int skills=Has(r,2)?r.ReadUInt16():0;
                if(skills>MaxListEntries)throw new InvalidDataException("Слишком длинный список навыков");
                for(int i=0;i<skills;i++)camp.RestoreSkillTaken(r.ReadInt32());
                if(Has(r,4))camp.RestoreOpenedArtifacts(r.ReadUInt32());
            }
            // Услуги, ранги и лавка выводятся из уже прочитанного: дальше по ним проверяется выбор игрока.
            camp.FinishRestore();

            var generated=new GeneratedItem();
            using(var r=Open(bytes,offsets,lengths,BagTag))
                for(int i=0;i<camp.Bag.Capacity;i++)
                {
                    var item=ReadItem(r,items,generated);bool keep=Has(r,1)&&r.ReadBoolean();
                    camp.Bag.Put(i,item,keep);
                }
            using(var r=Open(bytes,offsets,lengths,WornTag))
            {
                int count=Has(r,1)?r.ReadByte():0;
                for(int i=0;i<count;i++)
                {
                    var item=ReadItem(r,items,generated);
                    // Слотов стало меньше — вещь из исчезнувшего слота не надевается.
                    if(item.IsEmpty||i>=(int)EquipSlot.Count)continue;
                    // Категорию основы перенесли в другой слот — это смена справочника, а не порча
                    // файла: вещь не надевается, а ложится в первый свободный слот сумки (сумка уже
                    // прочитана); без места отбрасывается, как вещь с убранной основой (план A.7).
                    if(Equipment.SlotOf(items.GetBase(items.IndexOfBase(item.BaseId)).Category)!=(EquipSlot)i)
                    {camp.Bag.Add(item);continue;}
                    camp.Worn.Equip(item,out _);
                }
            }
            if(offsets[TraderTag]>=0)using(var r=Open(bytes,offsets,lengths,TraderTag))
            {
                if(Has(r,4+1+4))
                {
                    int generation=r.ReadInt32();bool boss=r.ReadBoolean();int count=r.ReadInt32();
                    if(generation<0||count<0||count>MaxTraderStock)throw new InvalidDataException("Некорректная лавка");
                    var stock=new ItemInstance[count];for(int i=0;i<count;i++)stock[i]=ReadItem(r,items,generated);
                    camp.RestoreTrader(generation,boss,stock);
                    camp.ReadTraderChoices(r);
                }
            }
            if(offsets[PotionsTag]>=0)using(var r=Open(bytes,offsets,lengths,PotionsTag))
            {
                int kinds=Has(r,1)?r.ReadByte():0;
                for(int i=0;i<kinds&&Has(r,4);i++)
                {
                    int count=r.ReadInt32();
                    if(count<0||count>Camp.PotionLimit)throw new InvalidDataException("Некорректный запас зелий");
                    if(i<Camp.PotionKindCount)camp.RestorePotionCount((PotionKind)i,count);
                }
                if(Has(r,2))camp.RestorePotionSelection((PotionKind)r.ReadByte(),(PotionKind)r.ReadByte());
            }
            camp.ValidatePotionSelection();
            if(offsets[PreparationTag]>=0)using(var r=Open(bytes,offsets,lengths,PreparationTag))camp.ReadPreparation(r);
            if(offsets[OathsTag]>=0)using(var r=Open(bytes,offsets,lengths,OathsTag))camp.ReadOaths(r,lengths[OathsTag]);
            if(offsets[ForgeSessionTag]>=0)using(var r=Open(bytes,offsets,lengths,ForgeSessionTag))camp.ReadForgeSession(r,lengths[ForgeSessionTag]);
            // Секция 11 (HeatVows) — резерв: читать пока нечего.
            return camp;
        }

        static void ReadProgress(BinaryReader r,Camp camp)
        {
            int attempts=I32(r),deepest=I32(r);byte flags=Has(r,1)?r.ReadByte():(byte)0;
            ulong used=Has(r,8)?r.ReadUInt64():0;byte chapters=Has(r,1)?r.ReadByte():(byte)0;uint pending=Has(r,4)?r.ReadUInt32():0;
            if(attempts<0||deepest<0)throw new InvalidDataException("Отрицательные счётчики лагеря");
            camp.RestoreCampProgression(attempts,deepest,(flags&1)!=0,(flags&2)!=0,used,chapters,pending);
            for(int list=0;list<2;list++)
            {
                int count=Has(r,1)?r.ReadByte():0;
                if(count>MaxListEntries)throw new InvalidDataException("Слишком длинный список боссов");
                int seen=0;
                for(int i=0;i<count;i++)
                {
                    int key=r.ReadInt32(),value=r.ReadInt32();
                    if(value<0)throw new InvalidDataException("Отрицательный счёт босса");
                    // Снятый или переименованный босс отбрасывается вместе со своими сердцами.
                    int index=RunBossKeys.IndexOf(key);if(index<0)continue;
                    if((seen&(1<<index))!=0)throw new InvalidDataException("Повтор босса");
                    seen|=1<<index;
                    if(list==0)camp.RestoreBossDefeats(index,value);else camp.RestoreHearts(index,value);
                }
            }
        }

        static BinaryReader Open(byte[] bytes,int[] offsets,int[] lengths,ushort tag)
            =>new BinaryReader(new MemoryStream(bytes,offsets[tag],lengths[tag],false));

        /// <summary>
        /// Осталось ли в секции ещё bytes байт. Нет — поле пишет более новая система,
        /// а этот файл старше её: читатель берёт значение по умолчанию.
        /// </summary>
        internal static bool Has(BinaryReader r,int bytes)=>r.BaseStream.Length-r.BaseStream.Position>=bytes;

        static int I32(BinaryReader r)=>Has(r,4)?r.ReadInt32():0;

        static void Write(BinaryWriter w,ItemInstance i)
        {
            // ForgeRecipe (трёхпопыточная перековка до v4) в v10 не пишется: такие вещи жили
            // только в старых сохранениях, а их v10 не читает.
            w.Write(i.BaseId);w.Write(i.ItemLevel);w.Write((byte)i.Rarity);w.Write(i.Seed);
            int count=i.Crafting?.Count??0;w.Write((ushort)count);
            for(int step=0;step<count;step++)
            {var operation=i.Crafting.Step(step);w.Write((byte)operation.Operation);w.Write(operation.Slot);w.Write(operation.AffixId);w.Write(operation.Fraction.Raw);}
        }

        static ItemInstance ReadItem(BinaryReader r,ItemDatabase db,GeneratedItem generated)
        {
            if(!Has(r,ItemHeaderBytes))return default;
            int baseId=r.ReadInt32();short level=r.ReadInt16();byte rarity=r.ReadByte();ulong seed=r.ReadUInt64();
            int count=r.ReadUInt16();
            if(rarity>(byte)ItemRarity.Unique)throw new InvalidDataException("Некорректная редкость");
            if(count>CraftingRecipe.MaximumSteps)throw new InvalidDataException("Слишком длинный рецепт");
            if(!Has(r,count*StepBytes))throw new InvalidDataException("Обрезан рецепт");
            CraftStep[] steps=count>0?new CraftStep[count]:null;
            for(int i=0;i<count;i++)
            {
                var step=new CraftStep((ForgeOperation)r.ReadByte(),r.ReadByte(),r.ReadInt32(),Fix64.FromRaw(r.ReadInt64()));
                if(!CraftingRecipe.IsStructurallyValid(in step))throw new InvalidDataException("Некорректное действие кузнеца");
                steps[i]=step;
            }
            var item=new ItemInstance(baseId,level,(ItemRarity)rarity,seed);
            if(item.IsEmpty)return default;
            // Основу убрали из справочника — слот пустеет, а профиль живёт дальше.
            int index=db.IndexOfBase(baseId);
            if(index<0)return default;
            if(level<1)throw new InvalidDataException("Некорректный уровень вещи");
            if(steps==null)return item;
            var category=db.GetBase(index).Category;
            for(int i=0;i<count;i++)if(!CraftingRecipe.IsAllowed(in steps[i],db,item.Rarity,category))return item;
            // Рецепт, который нынешние правила не разворачивают, срезается целиком: вещь
            // остаётся исходным роллом, а не грузится с половиной истории.
            var crafted=new ItemInstance(baseId,level,item.Rarity,seed,0,new CraftingRecipe(steps));
            return crafted.OriginalLevel>=1&&ItemGenerator.Generate(crafted,db,generated)?crafted:item;
        }

        static uint Checksum(byte[] bytes,int count){uint h=2166136261;for(int i=0;i<count;i++){h^=bytes[i];h=unchecked(h*16777619);}return h;}

        /// <summary>
        /// Пропускает секцию v10 целиком (заглушки шага 0 для клятв и сессии кузнеца).
        /// Читатель секции обязан съесть её до конца, иначе следующая прочтётся со сдвигом.
        /// </summary>
        internal static void SkipSection(BinaryReader r,int length)
        {
            if(length<0)throw new InvalidDataException("Некорректная длина секции");
            if(length>0&&r.ReadBytes(length).Length!=length)throw new InvalidDataException("Неполная секция");
        }
    }

    /// <summary>
    /// Сохранение старше v10 (решение 06.10): профиль сбрасывается, файл остаётся копией.
    /// Наследник NotSupportedException: старый CampSaveStore уже не перезаписывает такой файл.
    /// </summary>
    public sealed class CampSaveObsoleteException:NotSupportedException
    {
        public int Version{get;}
        public CampSaveObsoleteException(int version):base("Сохранение версии "+version+" устарело"){Version=version;}
    }

    /// <summary>
    /// Сохранение от более новой версии игры: не перезаписывать, показать сообщение,
    /// запись выключить (пробел плана №11).
    /// </summary>
    public sealed class CampSaveFutureException:NotSupportedException
    {
        public int Version{get;}
        public CampSaveFutureException(int version):base("Сохранение версии "+version+" новее игры"){Version=version;}
    }
}
