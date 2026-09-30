using System.IO;

namespace Game.Sim
{
    public sealed partial class Camp
    {
        int _preparedStarterId;
        CampGift _preparedGift;
        readonly CampGift[] _giftOffers = new CampGift[3];
        ulong _giftOfferSeed;
        bool _giftSeedInitialized;
        int _giftOfferGeneration = -1;

        public bool PreparationUnlocked => HasTravelTable;
        public int PreparedStarterId => _preparedStarterId == 0 ? AbilityDefinition.WhirlwindId : _preparedStarterId;
        public int PreparedStarterPoolIndex => PelagKit.PoolIndexOf(PreparedStarterId);
        public CampGift PreparedGift { get { EnsureGiftOffers(); return HasTravelTable ? _preparedGift : CampGift.None; } }
        public int GiftOfferCount => HasTravelTable ? 3 : 0;
        public CampGift GiftOfferAt(int index)
        { EnsureGiftOffers(); return (uint)index < 3 && HasTravelTable ? _giftOffers[index] : CampGift.None; }

        internal void InitializePreparationSeed(ulong seed)
        {
            if (_giftSeedInitialized) return;
            _giftOfferSeed = seed; _giftSeedInitialized = true;
        }
        public bool StarterSkillUnlocked(int poolIndex)
            => poolIndex == 0 || HasTravelTable && (poolIndex == 1 && Rank(CampResident.Smith) >= 1
                || poolIndex == 3 && Rank(CampResident.Smith) >= 2 || poolIndex == 2 && Rank(CampResident.Smith) >= 3);
        public bool SelectStarterSkill(int poolIndex)
        {
            if (!StarterSkillUnlocked(poolIndex)) return false;
            _preparedStarterId = PelagKit.PoolDefinition(poolIndex).Id; return true;
        }
        public bool GiftUnlocked(CampGift gift)
        {
            if (!HasTravelTable) return gift == CampGift.None;
            switch (gift)
            {
                case CampGift.DryRation: case CampGift.EniWhetstone: case CampGift.LightPack: return true;
                case CampGift.SeaKnot: return Rank(CampResident.Smith) >= 1;
                case CampGift.BackupPlan: return Rank(CampResident.Trader) >= 1;
                case CampGift.SpareFlask: return Rank(CampResident.Alchemist) >= 1;
                default: return false;
            }
        }
        public bool SelectGift(CampGift gift)
        {
            EnsureGiftOffers();
            for (int i = 0; i < 3; i++) if (_giftOffers[i] == gift && GiftUnlocked(gift))
            { _preparedGift = gift; return true; }
            return false;
        }
        void EnsureGiftOffers()
        {
            if (!HasTravelTable || _giftOfferGeneration == AttemptCount) return;
            var candidates = new CampGift[6]; int count = 0;
            for (int id = 1; id <= 6; id++) if (GiftUnlocked((CampGift)id)) candidates[count++] = (CampGift)id;
            // Свой поток: стол не сдвигает расстановку врагов, дроп и криты.
            var rng = new Pcg32(_giftOfferSeed ^ (ulong)AttemptCount * 0x9E3779B97F4A7C15UL, 0x4749465453UL);
            for (int i = 0; i < 3; i++)
            {
                int pick = rng.NextInt(i, count);
                var chosen = candidates[pick]; candidates[pick] = candidates[i]; candidates[i] = chosen;
                _giftOffers[i] = chosen;
            }
            _giftOfferGeneration = AttemptCount;
            if (_preparedGift != CampGift.None) _preparedGift = _giftOffers[0];
        }
        public RunPreparation CreateRunPreparation()
        {
            EnsureGiftOffers();
            int starter = HasTravelTable && StarterSkillUnlocked(PreparedStarterPoolIndex)
                ? PreparedStarterId : AbilityDefinition.WhirlwindId;
            return new RunPreparation(starter, HasTravelTable ? _preparedGift : CampGift.None,
                SelectedPotion(0), SelectedPotion(1));
        }
        internal void HashPreparation(ref ulong hash)
        {
            if (!_giftSeedInitialized && _preparedStarterId == 0 && _giftOfferGeneration < 0) return;
            Hashing.Mix(ref hash, 0x50524550); Hashing.Mix(ref hash, PreparedStarterId);
            Hashing.Mix(ref hash, (int)_preparedGift); Hashing.Mix(ref hash, _giftOfferSeed);
            Hashing.Mix(ref hash, _giftSeedInitialized ? 1 : 0); Hashing.Mix(ref hash, _giftOfferGeneration);
            for (int i = 0; i < 3; i++) Hashing.Mix(ref hash, (int)_giftOffers[i]);
        }
        internal void WritePreparation(BinaryWriter writer)
        {
            EnsureGiftOffers(); writer.Write(_preparedStarterId); writer.Write((byte)_preparedGift);
            writer.Write(_giftOfferSeed); writer.Write(_giftSeedInitialized); writer.Write(_giftOfferGeneration);
            for (int i = 0; i < 3; i++) writer.Write((byte)_giftOffers[i]);
        }
        internal void ReadPreparation(BinaryReader reader, int saveVersion)
        {
            if (saveVersion < 9) return;
            _preparedStarterId = reader.ReadInt32(); _preparedGift = (CampGift)reader.ReadByte();
            _giftOfferSeed = reader.ReadUInt64(); _giftSeedInitialized = reader.ReadBoolean();
            _giftOfferGeneration = reader.ReadInt32();
            for (int i = 0; i < 3; i++) _giftOffers[i] = (CampGift)reader.ReadByte();
            if (!StarterSkillUnlocked(PreparedStarterPoolIndex) || (uint)_preparedGift > 6
                || _giftOfferGeneration < -1 || _giftOfferGeneration > AttemptCount)
                throw new InvalidDataException("Некорректная подготовка похода");
            if (_giftOfferGeneration >= 0)
                for (int i = 0; i < 3; i++)
                {
                    if (!GiftUnlocked(_giftOffers[i]) || _giftOffers[i] == CampGift.None)
                        throw new InvalidDataException("Некорректные предложения даров");
                    for (int j = 0; j < i; j++) if (_giftOffers[i] == _giftOffers[j])
                        throw new InvalidDataException("Повтор предложения дара");
                }
            if (_preparedGift != CampGift.None && (_preparedGift != _giftOffers[0]
                && _preparedGift != _giftOffers[1] && _preparedGift != _giftOffers[2]))
                throw new InvalidDataException("Выбранный дар отсутствует в предложениях");
        }
        internal void ValidatePotionSelection()
        {
            if ((uint)_selectedPotions[0] >= PotionKindCount || (uint)_selectedPotions[1] >= PotionKindCount
                || _selectedPotions[0] == _selectedPotions[1]) throw new InvalidDataException("Некорректные выбранные зелья");
            // До приезда Лео исходные два пустых слота допустимы; бутылки купить нельзя.
            for (int i = 0; i < 2; i++)
                if (!PotionUnlocked(_selectedPotions[i]) && (_selectedPotions[i] != PotionKind.SmallHealth
                    && _selectedPotions[i] != PotionKind.SmallLavidium || _potions[(int)_selectedPotions[i]] > 0))
                    throw new InvalidDataException("Выбрано закрытое зелье");
            for (int i = 0; i < PotionKindCount; i++)
                if (_potions[i] > 0 && !PotionUnlocked((PotionKind)i)) throw new InvalidDataException("Запас закрытого зелья");
        }
    }
}
