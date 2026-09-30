using System;
using System.IO;

namespace Game.Sim
{
    public enum CampResident : byte { Smith, Trader, Alchemist }
    public enum CampUpgradeResult : byte { Success, Unavailable, MaximumRank, LevelRequired, InsufficientPoints, InvalidResident }
    public enum CampChapterStatus : byte { Hidden, Active, Ready, Completed }
    public enum ForgeMaterial : byte { Steel, Core, Count }

    public sealed partial class Camp
    {
        bool _usesCampProgression;
        readonly byte[] _residentRanks = new byte[3];
        readonly bool[] _chaptersCompleted = new bool[3];
        readonly int[] _forgeMaterials = new int[(int)ForgeMaterial.Count];
        bool _extractedFind, _smithFindDiscussed;
        int _deepestAttempt;
        ulong _usedPotionKinds;
        public bool UsesCampProgression => _usesCampProgression;
        public bool IsProgressive => _usesCampProgression;
        public int AttemptCount { get; private set; }
        public int SpentCampPoints { get; private set; }
        public int AvailableCampPoints => Math.Max(0, Level - 1 - SpentCampPoints);
        public bool HasTravelTable => !_usesCampProgression || AttemptCount >= 2;
        public int Rank(CampResident resident) => (uint)resident < 3 ? _residentRanks[(int)resident] : 0;
        public bool HasResident(CampResident resident) => resident == CampResident.Smith ? Has(CampService.Smith)
            : resident == CampResident.Trader ? Has(CampService.Trader)
            : resident == CampResident.Alchemist && Has(CampService.Alchemist);
        public static int RankMinimumLevel(int rank) => rank == 1 ? 2 : rank == 2 ? 5 : rank == 3 ? 8 : int.MaxValue;
        public CampUpgradeResult CanUpgradeResident(CampResident resident)
        {
            if ((uint)resident >= 3) return CampUpgradeResult.InvalidResident;
            if (!HasResident(resident)) return CampUpgradeResult.Unavailable;
            int next = Rank(resident) + 1;
            if (next > 3) return CampUpgradeResult.MaximumRank;
            if (Level < RankMinimumLevel(next)) return CampUpgradeResult.LevelRequired;
            return AvailableCampPoints > 0 ? CampUpgradeResult.Success : CampUpgradeResult.InsufficientPoints;
        }
        public CampUpgradeResult TryUpgradeResident(CampResident resident)
        {
            CampUpgradeResult result = CanUpgradeResident(resident);
            if (result != CampUpgradeResult.Success) return result;
            _residentRanks[(int)resident]++;
            SpentCampPoints++;
            if (resident == CampResident.Trader) ExpandTraderStock();
            return CampUpgradeResult.Success;
        }
        public void RecordRealAttemptEnded(int depth, int extractedItems, bool bossDefeated = false)
        {
            // Вызывает только реальная сессия, один раз: гибель тоже знакомит лагерь с походом.
            if (AttemptCount < int.MaxValue) AttemptCount++;
            _deepestAttempt = Math.Max(_deepestAttempt, Math.Max(0, depth));
            if (extractedItems > 0) _extractedFind = true;
            if (_usesCampProgression)
            {
                if (AttemptCount >= 1) Services |= CampService.Trader;
                if (AttemptCount >= 3) Services |= CampService.Alchemist;
            }
        }
        public void RecordRealPotionUsed(PotionKind kind)
        {
            if ((uint)kind < PotionKindCount) _usedPotionKinds |= 1UL << (int)kind;
        }
        public void DiscussSmithFind()
        {
            if (HasResident(CampResident.Smith) && _extractedFind) _smithFindDiscussed = true;
        }
        public CampChapterStatus ChapterStatus(CampResident resident)
        {
            if ((uint)resident >= 3 || !HasResident(resident)) return CampChapterStatus.Hidden;
            int index = (int)resident;
            if (_chaptersCompleted[index]) return CampChapterStatus.Completed;
            for (int i = 0; i < index; i++) if (!_chaptersCompleted[i]) return CampChapterStatus.Hidden;
            bool ready = resident == CampResident.Smith ? _extractedFind && _smithFindDiscussed
                : resident == CampResident.Trader ? _deepestAttempt >= 3
                : (_usedPotionKinds & (_usedPotionKinds - 1)) != 0;
            return ready ? CampChapterStatus.Ready : CampChapterStatus.Active;
        }
        public bool TurnInChapter(CampResident resident)
        {
            if (ChapterStatus(resident) != CampChapterStatus.Ready) return false;
            _chaptersCompleted[(int)resident] = true;
            if (resident == CampResident.Smith) EarnForgeMaterial(ForgeMaterial.Steel, 1);
            else if (resident == CampResident.Trader) { EarnForgeMaterial(ForgeMaterial.Steel, 1); Earn(CurrencyType.Gold, 50); }
            else
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    PotionKind selected = SelectedPotion(slot);
                    if (PotionUnlocked(selected) && (slot == 0 || SelectedPotion(0) != selected)) GrantPotions(selected, 2);
                }
            }
            return true;
        }
        public int MaterialCount(ForgeMaterial material) => (uint)material < (uint)ForgeMaterial.Count ? _forgeMaterials[(int)material] : 0;
        public void EarnForgeMaterial(ForgeMaterial material, int count)
        {
            if ((uint)material >= (uint)ForgeMaterial.Count || count <= 0) return;
            _forgeMaterials[(int)material] = (int)Math.Min(int.MaxValue, (long)_forgeMaterials[(int)material] + count);
        }
        bool SpendForgeMaterial(ForgeMaterial material, int count)
        {
            if (count < 0 || MaterialCount(material) < count) return false;
            _forgeMaterials[(int)material] -= count; return true;
        }
        void HashCampProgression(ref ulong hash)
        {
            Hashing.Mix(ref hash, _usesCampProgression ? 1 : 0); Hashing.Mix(ref hash, AttemptCount);
            Hashing.Mix(ref hash, SpentCampPoints); Hashing.Mix(ref hash, _deepestAttempt);
            Hashing.Mix(ref hash, _extractedFind ? 1 : 0); Hashing.Mix(ref hash, _smithFindDiscussed ? 1 : 0);
            Hashing.Mix(ref hash, _usedPotionKinds);
            for (int i = 0; i < 3; i++) { Hashing.Mix(ref hash, _residentRanks[i]); Hashing.Mix(ref hash, _chaptersCompleted[i] ? 1 : 0); }
            foreach (int count in _forgeMaterials) Hashing.Mix(ref hash, count);
        }
        internal void WriteCampProgression(BinaryWriter w)
        {
            w.Write(_usesCampProgression); w.Write((uint)Services); w.Write(AttemptCount); w.Write(SpentCampPoints);
            for (int i = 0; i < 3; i++) { w.Write(_residentRanks[i]); w.Write(_chaptersCompleted[i]); }
            w.Write(_deepestAttempt); w.Write(_extractedFind); w.Write(_smithFindDiscussed); w.Write(_usedPotionKinds);
            foreach (int count in _forgeMaterials) w.Write(count);
        }
        internal void ReadCampProgression(BinaryReader r)
        {
            _usesCampProgression = r.ReadBoolean(); uint services = r.ReadUInt32();
            if ((services & ~255U) != 0) throw new InvalidDataException("Некорректные услуги лагеря");
            Services = (CampService)services; AttemptCount = r.ReadInt32(); SpentCampPoints = r.ReadInt32();
            int ranks = 0;
            for (int i = 0; i < 3; i++) { _residentRanks[i] = r.ReadByte(); _chaptersCompleted[i] = r.ReadBoolean(); ranks += _residentRanks[i]; }
            _deepestAttempt = r.ReadInt32(); _extractedFind = r.ReadBoolean(); _smithFindDiscussed = r.ReadBoolean(); _usedPotionKinds = r.ReadUInt64();
            for (int i = 0; i < _forgeMaterials.Length; i++) { _forgeMaterials[i] = r.ReadInt32(); if (_forgeMaterials[i] < 0) throw new InvalidDataException("Некорректный материал"); }
            if (AttemptCount < 0 || SpentCampPoints < 0 || SpentCampPoints != ranks || _deepestAttempt < 0
                || _residentRanks[0] > 3 || _residentRanks[1] > 3 || _residentRanks[2] > 3 || (_usedPotionKinds >> PotionKindCount) != 0
                || _smithFindDiscussed && !_extractedFind || _chaptersCompleted[2] && !_chaptersCompleted[1]
                || _chaptersCompleted[1] && !_chaptersCompleted[0]) throw new InvalidDataException("Некорректное развитие лагеря");
            if (_usesCampProgression && (!HasResident(CampResident.Smith) || !Has(CampService.RiftPortal)
                || HasResident(CampResident.Trader) != (AttemptCount >= 1) || HasResident(CampResident.Alchemist) != (AttemptCount >= 3)))
                throw new InvalidDataException("Некорректное прибытие жителей");
        }
    }
}
