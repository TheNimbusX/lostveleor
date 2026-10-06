using System.IO;

namespace Game.Sim
{
    /// <summary>
    /// Стол сборов (решения 06.10, план «Лагерь 06–10.10» §7 T3): стартовый навык — 1 из 3
    /// среди когда-либо взятых, два разных зелья и ячейка «с собой» — дар, а после первого
    /// босса 1 из 3 среди даров и открытых артефактов.
    ///
    /// ПОЧЕМУ ПРЕДЛОЖЕНИЯ ПО КЛЮЧУ AttemptCount. Новый набор появляется только после
    /// завершённого забега. Отмена, закрытие окна, перезагрузка и новый сид сессии его не
    /// меняют — бесплатного переброса нет. Набор пишется в секцию 8 целиком: после загрузки
    /// он тот же, даже если кандидаты за это время выросли (сохранение посреди забега).
    /// Набор собирается лениво, при первом обращении: вызовы в общих файлах не нужны.
    /// </summary>
    public sealed partial class Camp
    {
        // Свои потоки: стол не сдвигает расстановку врагов, дроп и криты. Поток даров — прежний
        // (день 1), поэтому до первого босса предложения те же, что игрок уже видел.
        const ulong PrepGiftStream = 0x4749465453UL, PrepCarryStream = 0x4341525259UL, PrepSkillStream = 0x534B494C4C53UL;
        const ulong PrepAttemptSpread = 0x9E3779B97F4A7C15UL;
        const int PrepOfferSlots = 3;

        int _preparedStarterId;
        CarryChoice _preparedCarry;
        readonly CarryChoice[] _carryOffers = new CarryChoice[PrepOfferSlots];
        // Индексы пула Пелага; −1 — пусто, пустые только в хвосте.
        readonly int[] _skillOffers = { -1, -1, -1 };
        ulong _offerSeed;
        bool _offerSeedInitialized;
        int _offerGeneration = -1;
        // Черновики сборки: набор пересобирается раз за забег, но без мусора в куче.
        readonly int[] _skillCandidates = new int[PelagKit.PoolSize];
        readonly CarryChoice[] _carryCandidates = new CarryChoice[(int)CampGift.SpareFlask + RunArtifacts.Count];

        public bool PreparationUnlocked => HasTravelTable;

        /// <summary>Стартовый навык забега. Без стола — Вихрь.</summary>
        public int PreparedStarterId
        {
            get
            {
                EnsurePreparationOffers();
                return !HasTravelTable || _preparedStarterId == 0 ? AbilityDefinition.WhirlwindId : _preparedStarterId;
            }
        }
        public int PreparedStarterPoolIndex => PelagKit.PoolIndexOf(PreparedStarterId);

        /// <summary>Сколько навыков предлагает стол: 3, а если когда-либо взятых меньше — все.</summary>
        public int SkillOfferCount
        {
            get
            {
                EnsurePreparationOffers();
                if (!HasTravelTable) return 0;
                int count = 0;
                while (count < PrepOfferSlots && _skillOffers[count] >= 0) count++;
                return count;
            }
        }

        /// <summary>Индекс пула предложенного навыка; −1 — пусто.</summary>
        public int SkillOfferAt(int index)
        {
            EnsurePreparationOffers();
            return HasTravelTable && (uint)index < PrepOfferSlots ? _skillOffers[index] : -1;
        }

        internal void InitializePreparationSeed(ulong seed)
        {
            if (_offerSeedInitialized) return;
            _offerSeed = seed; _offerSeedInitialized = true;
        }

        /// <summary>Стартовый навык — только из предложений стола (имя прежнее: его зовёт GameSession).</summary>
        public bool SelectStarterSkill(int poolIndex)
        {
            EnsurePreparationOffers();
            if (!HasTravelTable || poolIndex < 0) return false;
            for (int i = 0; i < PrepOfferSlots; i++)
                if (_skillOffers[i] == poolIndex) { _preparedStarterId = PelagKit.PoolDefinition(poolIndex).Id; return true; }
            return false;
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

        /// <summary>Ячейка «с собой»: всегда 3 предложения, пока стол открыт (три базовых дара открыты с начала).</summary>
        public int CarryOfferCount
        {
            get
            {
                EnsurePreparationOffers();
                if (!HasTravelTable) return 0;
                int count = 0;
                while (count < PrepOfferSlots && _carryOffers[count].Kind != CarryKind.None) count++;
                return count;
            }
        }

        public CarryChoice CarryOfferAt(int index)
        {
            EnsurePreparationOffers();
            return HasTravelTable && (uint)index < PrepOfferSlots ? _carryOffers[index] : default;
        }

        /// <summary>Что взято «с собой». None — ничего не выбрано, и «В путь» неактивна.</summary>
        public CarryChoice PreparedCarry
        {
            get { EnsurePreparationOffers(); return HasTravelTable ? _preparedCarry : default; }
        }

        public bool SelectCarry(in CarryChoice c)
        {
            EnsurePreparationOffers();
            if (!HasTravelTable || !CarryAvailable(in c)) return false;
            for (int i = 0; i < PrepOfferSlots; i++)
                if (_carryOffers[i].SameAs(in c)) { _preparedCarry = _carryOffers[i]; return true; }
            return false;
        }

        // Прежний API даров (день 1): им ещё пользуются тесты сохранения и съёмка кузницы,
        // а они вне пакета T3. Артефакт в ячейке для них — «нет дара».
        public CampGift PreparedGift => PreparedCarry.Gift;
        public int GiftOfferCount => CarryOfferCount;
        public CampGift GiftOfferAt(int index) => CarryOfferAt(index).Gift;
        public bool SelectGift(CampGift gift) => SelectCarry(CarryChoice.Of(gift));

        /// <summary>
        /// Снимок решений для забега. Ничего не выбирает за игрока: только достраивает набор
        /// предложений этой попытки, если его ещё не было.
        /// </summary>
        public RunPreparation CreateRunPreparation()
        {
            EnsurePreparationOffers();
            if (!HasTravelTable)
                return new RunPreparation(AbilityDefinition.WhirlwindId, CampGift.None, SelectedPotion(0), SelectedPotion(1));
            int starter = PreparedStarterId;
            if (PelagKit.PoolIndexOf(starter) < 0) starter = AbilityDefinition.WhirlwindId;
            return new RunPreparation(starter, _preparedCarry.Gift, SelectedPotion(0), SelectedPotion(1), _preparedCarry.Artifact);
        }

        /// <summary>
        /// Набор предложений текущей попытки. Пересобирается, только когда сменился номер
        /// попытки (или набор признан негодным при чтении), — так выбор нельзя перебросить.
        /// </summary>
        internal void EnsurePreparationOffers()
        {
            if (!HasTravelTable || _offerGeneration == AttemptCount) return;
            ulong key = _offerSeed ^ (ulong)AttemptCount * PrepAttemptSpread;

            int skills = CollectSkillCandidates();
            var rng = new Pcg32(key, PrepSkillStream);
            for (int i = 0; i < PrepOfferSlots; i++)
            {
                if (i >= skills) { _skillOffers[i] = -1; continue; }
                // Три и меньше — все по порядку пула, бросок не нужен; больше — частичный Фишер–Йейтс.
                if (skills > PrepOfferSlots)
                {
                    int pick = rng.NextInt(i, skills);
                    int chosen = _skillCandidates[pick]; _skillCandidates[pick] = _skillCandidates[i]; _skillCandidates[i] = chosen;
                }
                _skillOffers[i] = _skillCandidates[i];
            }
            KeepStarterAmongOffers();

            bool artifacts = ArtifactsJoinCarry;
            int carry = CollectCarryCandidates(artifacts);
            rng = new Pcg32(key, artifacts ? PrepCarryStream : PrepGiftStream);
            for (int i = 0; i < PrepOfferSlots; i++)
            {
                if (i >= carry) { _carryOffers[i] = default; continue; }
                int pick = rng.NextInt(i, carry);
                var chosen = _carryCandidates[pick]; _carryCandidates[pick] = _carryCandidates[i]; _carryCandidates[i] = chosen;
                _carryOffers[i] = chosen;
            }
            _offerGeneration = AttemptCount;
            // Как в день 1: прежний выбор переносится на первое предложение, без выбора — пусто.
            if (_preparedCarry.Kind != CarryKind.None) _preparedCarry = _carryOffers[0];
        }

        /// <summary>
        /// Кандидаты в навыки: когда-либо взятые, которые ещё идут в награды. Sandbox открывает
        /// весь пул. Пусто (взятых нет — профиль без завершённого реального старта) — один Вихрь,
        /// чтобы стол не остался без стартового навыка.
        /// </summary>
        int CollectSkillCandidates()
        {
            int count = 0;
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (PelagKit.InRewardPool(pool) && (!IsProgressive || SkillEverTaken(PelagKit.PoolDefinition(pool).Id)))
                    _skillCandidates[count++] = pool;
            if (count == 0) _skillCandidates[count++] = PelagKit.PoolIndexOf(AbilityDefinition.WhirlwindId);
            return count;
        }

        /// <summary>Годится ли навык из сохранения: те же правила, Вихрь — как запасной кандидат.</summary>
        bool SkillOfferAllowed(int pool)
        {
            if (!PelagKit.InRewardPool(pool)) return false;
            if (!IsProgressive || pool == PelagKit.PoolIndexOf(AbilityDefinition.WhirlwindId)) return true;
            return SkillEverTaken(PelagKit.PoolDefinition(pool).Id);
        }

        /// <summary>
        /// Прежний выбор остаётся, если он среди предложений; иначе — первое предложение.
        /// Sandbox без выбора остаётся на Вихре: это эталон тестов и стендов, его забег не
        /// должен зависеть от того, что выпало на столе.
        /// </summary>
        void KeepStarterAmongOffers()
        {
            if (_skillOffers[0] < 0 || _preparedStarterId == 0 && !IsProgressive) return;
            int current = PelagKit.PoolIndexOf(_preparedStarterId == 0 ? AbilityDefinition.WhirlwindId : _preparedStarterId);
            for (int i = 0; i < PrepOfferSlots; i++) if (current >= 0 && _skillOffers[i] == current) return;
            _preparedStarterId = PelagKit.PoolDefinition(_skillOffers[0]).Id;
        }

        /// <summary>Артефакты идут в ячейку только после первой победы над любым боссом.</summary>
        bool ArtifactsJoinCarry
        {
            get
            {
                for (int i = 0; i < RunBossKeys.Count; i++) if (BossDefeated(RunBossKeys.At(i))) return true;
                return false;
            }
        }

        int CollectCarryCandidates(bool artifacts)
        {
            int count = 0;
            for (int gift = 1; gift <= (int)CampGift.SpareFlask; gift++)
                if (GiftUnlocked((CampGift)gift)) _carryCandidates[count++] = CarryChoice.Of((CampGift)gift);
            if (artifacts)
                for (int i = 0; i < RunArtifacts.Count; i++)
                    if (ArtifactOpened(RunArtifacts.At(i))) _carryCandidates[count++] = CarryChoice.Of(RunArtifacts.At(i));
            return count;
        }

        /// <summary>Можно ли сейчас взять это «с собой» по правилам (без проверки предложений).</summary>
        bool CarryAvailable(in CarryChoice c)
        {
            if (c.Kind == CarryKind.Gift) return c.Gift != CampGift.None && GiftUnlocked(c.Gift);
            return c.Kind == CarryKind.Artifact && ArtifactsJoinCarry && ArtifactOpened(c.Artifact);
        }

        internal void HashPreparation(ref ulong hash)
        {
            if (!_offerSeedInitialized && _preparedStarterId == 0 && _offerGeneration < 0 && _preparedCarry.Kind == CarryKind.None) return;
            Hashing.Mix(ref hash, 0x50524550); Hashing.Mix(ref hash, _preparedStarterId);
            Hashing.Mix(ref hash, (int)_preparedCarry.Kind); Hashing.Mix(ref hash, (int)_preparedCarry.Value);
            Hashing.Mix(ref hash, _offerSeed); Hashing.Mix(ref hash, _offerSeedInitialized ? 1 : 0); Hashing.Mix(ref hash, _offerGeneration);
            for (int i = 0; i < PrepOfferSlots; i++)
            {
                Hashing.Mix(ref hash, (int)_carryOffers[i].Kind); Hashing.Mix(ref hash, (int)_carryOffers[i].Value);
                Hashing.Mix(ref hash, _skillOffers[i]);
            }
        }

        /// <summary>
        /// Секция 8 сохранения v10. Голова — формат дня 1 (артефакт в ней пишется как «нет
        /// дара»), хвост T3 дописан в конец: ячейка целиком, предложения «с собой» (виды, затем
        /// значения) и навыки по id способности, 0 — пусто.
        /// </summary>
        internal void WritePreparation(BinaryWriter writer)
        {
            EnsurePreparationOffers();
            writer.Write(_preparedStarterId); writer.Write((byte)_preparedCarry.Gift);
            writer.Write(_offerSeed); writer.Write(_offerSeedInitialized); writer.Write(_offerGeneration);
            for (int i = 0; i < PrepOfferSlots; i++) writer.Write((byte)_carryOffers[i].Gift);
            writer.Write((byte)_preparedCarry.Kind); writer.Write(_preparedCarry.Value);
            for (int i = 0; i < PrepOfferSlots; i++) writer.Write((byte)_carryOffers[i].Kind);
            for (int i = 0; i < PrepOfferSlots; i++) writer.Write(_carryOffers[i].Value);
            for (int i = 0; i < PrepOfferSlots; i++)
                writer.Write(_skillOffers[i] >= 0 ? PelagKit.PoolDefinition(_skillOffers[i]).Id : 0);
        }

        /// <summary>
        /// Подгонка вместо отказа. Вид ячейки вне 0..2 — порча файла. Набор, который нынешние
        /// правила не принимают (снятый навык, закрытый дар, повтор), пересобирается тем же
        /// сидом и номером попытки — переброса не даёт. Файл дня 1 без хвоста пересобирается
        /// один раз; до первого босса дары выходят те же (тот же поток и кандидаты).
        /// </summary>
        internal void ReadPreparation(BinaryReader reader)
        {
            if (!CampSaveCodec.Has(reader, 4 + 1 + 8 + 1 + 4 + PrepOfferSlots)) return;
            _preparedStarterId = reader.ReadInt32(); var headGift = (CampGift)reader.ReadByte();
            _offerSeed = reader.ReadUInt64(); _offerSeedInitialized = reader.ReadBoolean();
            _offerGeneration = reader.ReadInt32();
            var headOffers = reader.ReadBytes(PrepOfferSlots);

            bool tail = CampSaveCodec.Has(reader, 2 + 2 * PrepOfferSlots + 4 * PrepOfferSlots), offersValid = tail;
            if (tail)
            {
                byte kind = reader.ReadByte(), value = reader.ReadByte();
                if (kind > (byte)CarryKind.Artifact) throw new InvalidDataException("Некорректная ячейка «с собой»");
                _preparedCarry = CarryChoice.FromSave(kind, value);
                var kinds = reader.ReadBytes(PrepOfferSlots);
                for (int i = 0; i < PrepOfferSlots; i++)
                {
                    if (kinds[i] > (byte)CarryKind.Artifact) throw new InvalidDataException("Некорректное предложение «с собой»");
                    _carryOffers[i] = CarryChoice.FromSave(kinds[i], reader.ReadByte());
                }
                for (int i = 0; i < PrepOfferSlots; i++)
                {
                    int id = reader.ReadInt32();
                    _skillOffers[i] = id == 0 ? -1 : PelagKit.PoolIndexOf(id);
                    // Снятый из пула навык: набор пересобирается.
                    if (id != 0 && _skillOffers[i] < 0) offersValid = false;
                }
            }
            else
            {
                _preparedCarry = CarryChoice.Of(headGift);
                for (int i = 0; i < PrepOfferSlots; i++) { _carryOffers[i] = CarryChoice.Of((CampGift)headOffers[i]); _skillOffers[i] = -1; }
            }
            // Неизвестный дар или артефакт — смена справочника, а не порча: ячейка пустеет.
            if (_preparedCarry.Kind == CarryKind.Gift && _preparedCarry.Gift > CampGift.SpareFlask
                || _preparedCarry.Kind == CarryKind.Artifact && !RunArtifacts.IsValid(_preparedCarry.Artifact)) _preparedCarry = default;
            int starterPool = PelagKit.PoolIndexOf(_preparedStarterId);
            if (_preparedStarterId != 0 && starterPool < 0) _preparedStarterId = 0;

            offersValid &= HasTravelTable && _offerGeneration >= 0 && _offerGeneration <= AttemptCount;
            for (int i = 0; i < PrepOfferSlots && offersValid; i++)
            {
                offersValid = CarryAvailable(in _carryOffers[i]);
                for (int j = 0; j < i; j++) offersValid &= !_carryOffers[i].SameAs(in _carryOffers[j]);
            }
            int skills = 0;
            for (int i = 0; i < PrepOfferSlots && offersValid; i++)
            {
                int pool = _skillOffers[i];
                if (pool < 0) continue;
                // Пустые — только в хвосте.
                offersValid = skills == i && SkillOfferAllowed(pool);
                for (int j = 0; j < i; j++) offersValid &= _skillOffers[j] != pool;
                skills++;
            }
            offersValid &= skills > 0;

            if (!offersValid)
            {
                // Выбор переживает пересборку: EnsurePreparationOffers оставит навык, если он
                // среди новых, а ячейку перенесёт на первое предложение.
                _offerGeneration = -1;
                for (int i = 0; i < PrepOfferSlots; i++) { _carryOffers[i] = default; _skillOffers[i] = -1; }
                return;
            }
            KeepStarterAmongOffers();
            if (_preparedCarry.Kind == CarryKind.None) return;
            for (int i = 0; i < PrepOfferSlots; i++) if (_carryOffers[i].SameAs(in _preparedCarry)) return;
            _preparedCarry = _carryOffers[0];
        }

        /// <summary>
        /// Выбранные зелья после чтения (06.10): закрытое по новым правилам или повторное
        /// возвращается к малым, а не роняет загрузку — ранги и рецепты меняются патчами.
        /// Запас закрытого зелья не трогаем: он просто недоступен, пока рецепт закрыт.
        /// </summary>
        internal void ValidatePotionSelection()
        {
            if (!PotionUnlocked(_selectedPotions[0])) _selectedPotions[0] = PotionKind.SmallHealth;
            if (!PotionUnlocked(_selectedPotions[1])) _selectedPotions[1] = PotionKind.SmallLavidium;
            if (_selectedPotions[0] == _selectedPotions[1])
            { _selectedPotions[0] = PotionKind.SmallHealth; _selectedPotions[1] = PotionKind.SmallLavidium; }
        }
    }
}
