using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Разбор файла сохранения v10 на секции для тестов: подменить одну секцию и
    /// пересчитать контрольную сумму, не повторяя кодек.
    /// </summary>
    internal static class CampSaveFile
    {
        internal const ushort CoreTag = 1, BagTag = 2, WornTag = 3, TraderTag = 4, PotionsTag = 5,
            ProgressTag = 6, CollectionTag = 7, PreparationTag = 8;

        internal sealed class Section
        {
            public ushort Tag; public byte[] Payload;
            public Section(ushort tag, byte[] payload) { Tag = tag; Payload = payload; }
        }

        internal static List<Section> Sections(byte[] file)
        {
            var list = new List<Section>(); int position = 8, end = file.Length - 4;
            while (position < end)
            {
                ushort tag = BitConverter.ToUInt16(file, position); int length = BitConverter.ToInt32(file, position + 2);
                var payload = new byte[length]; Array.Copy(file, position + 6, payload, 0, length);
                list.Add(new Section(tag, payload)); position += 6 + length;
            }
            return list;
        }

        internal static byte[] Build(List<Section> sections, int version = 10)
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(version);
                foreach (var section in sections) { w.Write(section.Tag); w.Write(section.Payload.Length); w.Write(section.Payload); }
                w.Flush(); w.Write(Checksum(stream.ToArray())); w.Flush(); return stream.ToArray();
            }
        }

        internal static uint Checksum(byte[] bytes)
        {
            uint h = 2166136261;
            foreach (byte b in bytes) { h ^= b; h = unchecked(h * 16777619); }
            return h;
        }

        internal static byte[] Payload(byte[] file, ushort tag)
        {
            foreach (var section in Sections(file)) if (section.Tag == tag) return section.Payload;
            return null;
        }

        internal static byte[] Replace(byte[] file, ushort tag, byte[] payload)
        {
            var sections = Sections(file);
            foreach (var section in sections) if (section.Tag == tag) section.Payload = payload;
            return Build(sections);
        }

        internal static void PutInt(byte[] payload, int at, int value) => Array.Copy(BitConverter.GetBytes(value), 0, payload, at, 4);
    }

    /// <summary>
    /// Сохранение v10 (06.10): секции с тегами, старые версии не читаются, порча —
    /// InvalidDataException, смена правил и справочника — подгонка без исключения.
    /// </summary>
    public sealed class CampSaveV10Tests
    {
        /// <summary>Лагерь, в котором заполнено всё, что пишет v10.</summary>
        static Camp FullCamp()
        {
            var camp = PrototypeContent.NewCamp();
            camp.RecordRealAttemptEnded(4, 1); camp.RecordRealAttemptEnded(2, 0);
            camp.DeveloperSetLevel(18); camp.GainExperience(123);
            for (int boss = 0; boss < RunBossKeys.Count; boss++) camp.DeveloperCreditBoss(boss);
            camp.DeveloperCreditBoss(0);
            for (int i = 0; i < (int)CurrencyType.Count; i++) camp.Earn((CurrencyType)i, 1000 + i);
            camp.RecordRealPotionUsed(PotionKind.SmallHealth); camp.RecordRealPotionUsed(PotionKind.LargeLavidium);
            camp.DiscussSmithFind(); Assert.True(camp.TurnInChapter(CampResident.Smith));
            int sword = camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 25, ItemRarity.Rare, 123));
            // Первый удар закалки без риска; сессия остаётся открытой и тоже проходит круг сохранения.
            Assert.AreEqual(SmithResult.Success, camp.Strike(ForgeTarget.Bag(sword), 0, out _));
            camp.Bag.SetKeep(sword, true);
            int jacket = camp.Bag.Add(new ItemInstance(StableId.Of("base.quilted_jacket"), 7, ItemRarity.Magic, 9));
            Assert.True(camp.EquipFromBag(jacket));
            camp.Bag.Add(new ItemInstance(StableId.Of("base.sea_knot"), 3, ItemRarity.Normal, 5));
            Assert.True(camp.ReserveTraderStock(1)); Assert.True(camp.ChooseTraderCategory(ItemCategory.Armor));
            Assert.True(camp.BuyPotion(PotionKind.LargeHealth)); Assert.True(camp.BuyPotion(PotionKind.Mixed));
            Assert.True(camp.SelectPotionForSlot(0, PotionKind.LargeHealth));
            camp.RecordSkillTaken(AbilityDefinition.CleaveId); camp.RecordSkillTaken(AbilityDefinition.BlazeId);
            Assert.True(camp.SelectStarterSkill(PelagKit.PoolIndexOf(AbilityDefinition.CleaveId)));
            Assert.True(camp.SelectGift(camp.GiftOfferAt(1)));
            camp.OpenArtifact(RunArtifacts.At(0)); camp.OpenArtifact(RunArtifacts.At(5));
            camp.AcknowledgeUnlocks(CampUnlock.Trader | CampUnlock.Rank1);
            return camp;
        }

        [Test] public void FullStateRoundTripIsByteStable()
        {
            var camp = FullCamp(); var bytes = CampSaveCodec.Encode(camp);
            Assert.AreEqual(CampSaveCodec.Version, CampSaveCodec.PeekVersion(bytes));
            var back = CampSaveCodec.Decode(bytes, camp.Items);
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(back));
            ulong expected = 0, actual = 0; camp.HashInto(ref expected); back.HashInto(ref actual);
            Assert.AreEqual(expected, actual); Assert.AreEqual(camp.PersistStamp, back.PersistStamp);
            Assert.AreEqual(camp.Services, back.Services); Assert.AreEqual(camp.PendingUnlocks, back.PendingUnlocks);
            Assert.AreEqual(2, back.BossDefeats(RunBossKeys.ThicketMaster)); Assert.AreEqual(2, back.HeartCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(1004, back.Money(CurrencyType.Ash)); Assert.AreEqual(camp.Money(CurrencyType.Steel), back.Money(CurrencyType.Steel));
            Assert.AreEqual(1, back.AttemptsUsed(back.Bag.At(0))); Assert.True(back.Session.IsOpen); Assert.True(back.Bag.IsKept(0));
            Assert.False(back.Worn.Worn(EquipSlot.Armor).IsEmpty);
            Assert.AreEqual(PotionKind.LargeHealth, back.SelectedPotion(0)); Assert.AreEqual(1, back.PotionCount(PotionKind.Mixed));
            Assert.AreEqual(AbilityDefinition.CleaveId, back.PreparedStarterId); Assert.AreEqual(camp.PreparedGift, back.PreparedGift);
            Assert.AreEqual(2, back.EverTakenSkillCount); Assert.True(back.ArtifactOpened(RunArtifacts.At(5)));
            Assert.AreEqual(CampChapterStatus.Completed, back.ChapterStatus(CampResident.Smith));
            Assert.AreEqual(1, back.TraderReservedSlot); Assert.AreEqual((int)ItemCategory.Armor, back.TraderCategoryChoice);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void VersionsBelowTenAreObsolete(int version)
        {
            var e = Assert.Throws<CampSaveObsoleteException>(() => CampSaveCodec.Decode(LegacyCampSaveFixture.Header(version), PrototypeContent.Items()));
            Assert.AreEqual(version, e.Version); Assert.AreEqual(version, CampSaveCodec.PeekVersion(LegacyCampSaveFixture.Header(version)));
        }

        /// <summary>Файл новее игры не разбирается вовсе: его формат и контрольная сумма нам неизвестны.</summary>
        [Test] public void FutureVersionIsReportedNotParsed()
        {
            var future = LegacyCampSaveFixture.Header(11); future[12] ^= 0x5A;
            var e = Assert.Throws<CampSaveFutureException>(() => CampSaveCodec.Decode(future, PrototypeContent.Items()));
            Assert.AreEqual(11, e.Version);
        }

        [Test] public void CorruptChecksumThrowsInvalidData()
        {
            var items = PrototypeContent.Items(); var bytes = CampSaveCodec.Encode(FullCamp());
            var flipped = (byte[])bytes.Clone(); flipped[bytes.Length / 2] ^= 1;
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(flipped, items));
            var magic = (byte[])bytes.Clone(); magic[0] ^= 1;
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(magic, items));
            Assert.AreEqual(-1, CampSaveCodec.PeekVersion(magic));
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(new byte[7], items));
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(null, items));
            var tail = new byte[bytes.Length - 1]; Array.Copy(bytes, tail, tail.Length);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(tail, items));
        }

        /// <summary>
        /// Расширение без новой версии: незнакомая секция пропускается, у короткой секции
        /// недостающие поля берутся по умолчанию. Без обязательной секции, с повтором или
        /// с длиной за концом файла — порча.
        /// </summary>
        [Test] public void UnknownSectionIsSkippedAndShortSectionDefaults()
        {
            var camp = FullCamp(); var items = camp.Items; var bytes = CampSaveCodec.Encode(camp);
            var sections = CampSaveFile.Sections(bytes);
            sections.Insert(3, new CampSaveFile.Section(200, new byte[] { 1, 2, 3, 4, 5 }));
            var potions = CampSaveFile.Payload(bytes, CampSaveFile.PotionsTag); var shortPotions = new byte[1 + 4];
            Array.Copy(potions, shortPotions, shortPotions.Length);
            foreach (var section in sections) if (section.Tag == CampSaveFile.PotionsTag) section.Payload = shortPotions;
            var back = CampSaveCodec.Decode(CampSaveFile.Build(sections), items);
            Assert.AreEqual(camp.PotionCount(PotionKind.SmallHealth), back.PotionCount(PotionKind.SmallHealth));
            Assert.AreEqual(0, back.PotionCount(PotionKind.Mixed));
            Assert.AreEqual(PotionKind.SmallHealth, back.SelectedPotion(0)); Assert.AreEqual(PotionKind.SmallLavidium, back.SelectedPotion(1));
            Assert.AreEqual(camp.Level, back.Level); Assert.AreEqual(camp.Money(CurrencyType.Ash), back.Money(CurrencyType.Ash));

            var withoutBag = CampSaveFile.Sections(bytes); withoutBag.RemoveAll(s => s.Tag == CampSaveFile.BagTag);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveFile.Build(withoutBag), items));
            var twice = CampSaveFile.Sections(bytes); twice.Add(new CampSaveFile.Section(CampSaveFile.PotionsTag, potions));
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveFile.Build(twice), items));
            var longer = (byte[])bytes.Clone(); CampSaveFile.PutInt(longer, 8 + 2, int.MaxValue);
            Array.Copy(BitConverter.GetBytes(CampSaveFile.Checksum(Head(longer))), 0, longer, longer.Length - 4, 4);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(longer, items));
            var badBag = CampSaveFile.Payload(bytes, CampSaveFile.CoreTag); CampSaveFile.PutInt(badBag, 2, 47);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveFile.Replace(bytes, CampSaveFile.CoreTag, badBag), items));
            var negative = CampSaveFile.Payload(bytes, CampSaveFile.CoreTag); CampSaveFile.PutInt(negative, 15, -1);
            Assert.Throws<InvalidDataException>(() => CampSaveCodec.Decode(CampSaveFile.Replace(bytes, CampSaveFile.CoreTag, negative), items));
        }

        static byte[] Head(byte[] file) { var head = new byte[file.Length - 4]; Array.Copy(file, head, head.Length); return head; }

        /// <summary>
        /// Патч поменял правила или справочник — профиль грузится, значения подгоняются:
        /// закрытое выбранное зелье, снятый босс, навык не из пула, опыт выше нового
        /// порога, неизвестная основа, рецепт вне правил.
        /// </summary>
        [Test] public void RuleChangesNormalizeInsteadOfThrowing()
        {
            var camp = PrototypeContent.NewCamp(); var items = camp.Items;
            camp.DeveloperSetLevel(6); camp.DeveloperCreditBoss(0); camp.RecordSkillTaken(AbilityDefinition.CleaveId);
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 5, ItemRarity.Magic, 77));
            camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 9, ItemRarity.Magic, 78,
                crafting: new CraftingRecipe(new[] { new CraftStep(ForgeOperation.Remelt, 0, 424242, Fix64.Half) })));
            var bytes = CampSaveCodec.Encode(camp);

            var potions = CampSaveFile.Payload(bytes, CampSaveFile.PotionsTag);
            potions[1 + 4 * Camp.PotionKindCount] = (byte)PotionKind.Clear;
            bytes = CampSaveFile.Replace(bytes, CampSaveFile.PotionsTag, potions);
            var progress = CampSaveFile.Payload(bytes, CampSaveFile.ProgressTag);
            Assert.AreEqual(1, progress[22]); Assert.AreEqual(RunBossKeys.ThicketMaster, BitConverter.ToInt32(progress, 23));
            // Победы (пара с 23-го байта) и сердца (пара с 32-го): снятый босс уходит из обоих списков.
            Assert.AreEqual(1, progress[31]); Assert.AreEqual(RunBossKeys.ThicketMaster, BitConverter.ToInt32(progress, 32));
            CampSaveFile.PutInt(progress, 23, StableId.Of("boss.removed")); CampSaveFile.PutInt(progress, 32, StableId.Of("boss.removed"));
            bytes = CampSaveFile.Replace(bytes, CampSaveFile.ProgressTag, progress);
            var collection = CampSaveFile.Payload(bytes, CampSaveFile.CollectionTag);
            CampSaveFile.PutInt(collection, 2, StableId.Of("ability.removed"));
            bytes = CampSaveFile.Replace(bytes, CampSaveFile.CollectionTag, collection);
            var core = CampSaveFile.Payload(bytes, CampSaveFile.CoreTag); CampSaveFile.PutInt(core, 10, 1000000);
            bytes = CampSaveFile.Replace(bytes, CampSaveFile.CoreTag, core);
            var bag = CampSaveFile.Payload(bytes, CampSaveFile.BagTag); CampSaveFile.PutInt(bag, 0, StableId.Of("base.removed"));
            bytes = CampSaveFile.Replace(bytes, CampSaveFile.BagTag, bag);

            var back = CampSaveCodec.Decode(bytes, items);
            Assert.AreEqual(PotionKind.SmallHealth, back.SelectedPotion(0), "Ясное закрыто на ранге 0");
            Assert.False(back.BossDefeated(RunBossKeys.ThicketMaster)); Assert.AreEqual(0, back.HeartCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(0, back.CampRank);
            Assert.AreEqual(0, back.EverTakenSkillCount);
            Assert.AreEqual(6, back.Level); Assert.AreEqual(back.ExperienceToNextLevel - 1, back.Experience);
            Assert.True(back.Bag.IsEmpty(0), "основу убрали из справочника");
            Assert.False(back.Bag.IsEmpty(1)); Assert.IsNull(back.Bag.At(1).Crafting, "рецепт с неизвестным аффиксом срезан");
            Assert.AreEqual(78UL, back.Bag.At(1).Seed);
            var again = CampSaveCodec.Encode(back);
            CollectionAssert.AreEqual(again, CampSaveCodec.Encode(CampSaveCodec.Decode(again, items)));
        }

        /// <summary>
        /// Вещь, чья основа по нынешнему справочнику ведёт в другой слот, — смена правил, а не
        /// порча: профиль не сбрасывается, вещь уходит в сумку, а без места отбрасывается.
        /// </summary>
        [Test] public void WornItemInWrongSlotMovesToBagInsteadOfThrowing()
        {
            var camp = PrototypeContent.NewCamp(); var items = camp.Items;
            int sword = camp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 4, ItemRarity.Magic, 31));
            Assert.True(camp.EquipFromBag(sword));
            var bytes = CampSaveCodec.Encode(camp);
            // Первый байт секции — число слотов, дальше вещь слота «Оружие»: её основа — с 1-го байта.
            var worn = CampSaveFile.Payload(bytes, CampSaveFile.WornTag);
            Assert.AreEqual(StableId.Of("base.rusty_sword"), BitConverter.ToInt32(worn, 1));
            CampSaveFile.PutInt(worn, 1, StableId.Of("base.quilted_jacket"));
            var moved = CampSaveFile.Replace(bytes, CampSaveFile.WornTag, worn);

            var back = CampSaveCodec.Decode(moved, items);
            Assert.True(back.Worn.Worn(EquipSlot.Weapon).IsEmpty);
            Assert.True(back.Worn.Worn(EquipSlot.Armor).IsEmpty, "куртка не надевается сама в чужой слот");
            Assert.AreEqual(1, back.Bag.Used);
            Assert.AreEqual(StableId.Of("base.quilted_jacket"), back.Bag.At(0).BaseId);

            var full = PrototypeContent.NewCamp();
            int weapon = full.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"), 4, ItemRarity.Magic, 31));
            Assert.True(full.EquipFromBag(weapon));
            while (!full.Bag.IsFull) full.Bag.Add(new ItemInstance(StableId.Of("base.sea_knot"), 1, ItemRarity.Normal, 7));
            bytes = CampSaveCodec.Encode(full);
            worn = CampSaveFile.Payload(bytes, CampSaveFile.WornTag);
            CampSaveFile.PutInt(worn, 1, StableId.Of("base.quilted_jacket"));
            var dropped = CampSaveCodec.Decode(CampSaveFile.Replace(bytes, CampSaveFile.WornTag, worn), items);
            Assert.True(dropped.Worn.Worn(EquipSlot.Weapon).IsEmpty);
            Assert.True(dropped.Bag.IsFull);
            for (int i = 0; i < dropped.Bag.Capacity; i++)
                Assert.AreNotEqual(StableId.Of("base.quilted_jacket"), dropped.Bag.At(i).BaseId, "без места вещь отбрасывается");
        }
    }
}
