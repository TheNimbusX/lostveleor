using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class CampProgressionTests
    {
        /// <summary>
        /// Прибытия 06.10: на ур. 1 в лагере только Эни, на ур. 3 приходят Вен и Лео,
        /// стол сборов — после первого завершённого забега. Каждое открытие поднимает
        /// флаг прибытия ровно один раз.
        /// </summary>
        [Test] public void ArrivalsFollowLevelAndTableFollowsFirstEndedRun()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.True(camp.UsesCampProgression); Assert.True(camp.HasResident(CampResident.Smith));
            Assert.False(camp.HasResident(CampResident.Trader)); Assert.False(camp.HasResident(CampResident.Alchemist)); Assert.False(camp.HasTravelTable);
            Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
            Assert.AreEqual(1, camp.GainExperience(camp.ExperienceToNextLevel));
            Assert.False(camp.HasResident(CampResident.Trader)); Assert.AreEqual(CampUnlock.None, camp.PendingUnlocks);
            Assert.AreEqual(1, camp.GainExperience(camp.ExperienceToNextLevel));
            Assert.AreEqual(Camp.ResidentsLevel, camp.Level);
            Assert.True(camp.HasResident(CampResident.Trader)); Assert.True(camp.HasResident(CampResident.Alchemist));
            Assert.AreEqual(CampUnlock.Trader | CampUnlock.Alchemist, camp.PendingUnlocks);
            Assert.AreEqual(4, camp.TraderStockCount); Assert.False(camp.HasTravelTable);
            camp.RecordRealAttemptEnded(1, 0);
            Assert.True(camp.HasTravelTable);
            Assert.AreEqual(CampUnlock.Trader | CampUnlock.Alchemist | CampUnlock.TravelTable, camp.PendingUnlocks);
            Assert.AreEqual(0, camp.CampRank); Assert.AreEqual(0, camp.Rank(CampResident.Smith));
            camp.RecordRealAttemptEnded(1, 0);
            Assert.AreEqual(CampUnlock.Trader | CampUnlock.Alchemist | CampUnlock.TravelTable, camp.PendingUnlocks);
        }
        [Test] public void ChapterHistoryPersistsBeforeActivationAndRewardsOnlyOnce()
        {
            var camp = PrototypeContent.NewCamp();
            camp.RecordRealPotionUsed(PotionKind.SmallHealth); camp.RecordRealPotionUsed(PotionKind.SmallLavidium);
            camp.RecordRealAttemptEnded(4, 1); camp.DeveloperSetLevel(Camp.ResidentsLevel);
            Assert.AreEqual(CampChapterStatus.Hidden, camp.ChapterStatus(CampResident.Trader));
            Assert.False(camp.TurnInChapter(CampResident.Smith)); camp.DiscussSmithFind();
            Assert.True(camp.TurnInChapter(CampResident.Smith)); Assert.AreEqual(1, camp.Money(CurrencyType.Steel));
            Assert.AreEqual(CampChapterStatus.Ready, camp.ChapterStatus(CampResident.Trader));
            camp = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            Assert.True(camp.TurnInChapter(CampResident.Trader)); Assert.AreEqual(50, camp.Money(CurrencyType.Gold)); Assert.AreEqual(2, camp.Money(CurrencyType.Steel));
            int healthBefore = camp.PotionCount(PotionKind.SmallHealth), lavidiumBefore = camp.PotionCount(PotionKind.SmallLavidium);
            Assert.AreEqual(CampChapterStatus.Ready, camp.ChapterStatus(CampResident.Alchemist)); Assert.True(camp.TurnInChapter(CampResident.Alchemist));
            Assert.AreEqual(healthBefore + 2, camp.PotionCount(PotionKind.SmallHealth)); Assert.AreEqual(lavidiumBefore + 2, camp.PotionCount(PotionKind.SmallLavidium));
            foreach (CampResident resident in Enum.GetValues(typeof(CampResident))) Assert.False(camp.TurnInChapter(resident));
            Assert.AreEqual(50, camp.Money(CurrencyType.Gold));
        }
        /// <summary>
        /// Всё, что лагерь копит между забегами, переживает сохранение побайтово: победы,
        /// сердца, пепел, сталь, взятые навыки, открытые артефакты, выбор Вена и
        /// ещё не показанные открытия.
        /// </summary>
        [Test] public void ProgressiveSaveKeepsBossesHeartsAshChoicesAndUnlocks()
        {
            var camp = PrototypeContent.NewCamp(); var early = CampSaveCodec.Encode(camp);
            var restored = CampSaveCodec.Decode(early, camp.Items); CollectionAssert.AreEqual(early, CampSaveCodec.Encode(restored));
            restored.RecordRealAttemptEnded(1, 0); restored.DeveloperSetLevel(18);
            for (int boss = 0; boss < RunBossKeys.Count; boss++) restored.DeveloperCreditBoss(boss);
            Assert.AreEqual(3, restored.CampRank); Assert.AreEqual(6, restored.TraderStockCount);
            Assert.True(restored.ReserveTraderStock(0)); Assert.True(restored.ChooseTraderCategory(ItemCategory.Talisman));
            restored.Earn(CurrencyType.Ash, 37);
            restored.RecordSkillTaken(AbilityDefinition.CleaveId); restored.OpenArtifact(RunArtifacts.At(2));
            restored.AcknowledgeUnlocks(CampUnlock.Trader);
            var save = CampSaveCodec.Encode(restored); var back = CampSaveCodec.Decode(save, camp.Items);
            CollectionAssert.AreEqual(save, CampSaveCodec.Encode(back));
            ulong expected = 0, actual = 0; restored.HashInto(ref expected); back.HashInto(ref actual); Assert.AreEqual(expected, actual);
            Assert.AreEqual(3, back.Rank(CampResident.Trader)); Assert.AreEqual(0, back.TraderReservedSlot);
            Assert.AreEqual((int)ItemCategory.Talisman, back.TraderCategoryChoice);
            Assert.AreEqual(37, back.Money(CurrencyType.Ash)); Assert.AreEqual(RunBossKeys.Count * RunEconomy.BossSteel, back.Money(CurrencyType.Steel));
            for (int boss = 0; boss < RunBossKeys.Count; boss++)
            { Assert.True(back.BossDefeated(RunBossKeys.At(boss))); Assert.AreEqual(1, back.HeartCount(RunBossKeys.At(boss))); }
            Assert.True(back.SkillEverTaken(AbilityDefinition.CleaveId)); Assert.True(back.ArtifactOpened(RunArtifacts.At(2)));
            Assert.AreEqual(restored.PendingUnlocks, back.PendingUnlocks); Assert.AreEqual(CampUnlock.None, back.PendingUnlocks & CampUnlock.Trader);
            Assert.AreNotEqual(CampUnlock.None, back.PendingUnlocks & CampUnlock.Rank3);
        }
    }
}
