using System;

namespace Game.Sim
{
    public enum CampResident : byte { Smith, Trader, Alchemist }
    public enum CampChapterStatus : byte { Hidden, Active, Ready, Completed }

    /// <summary>
    /// Что в лагере открылось и ещё не показано игроку (флаги прибытия, 06.10).
    /// Лежит в сохранении числом: значения не менять, только дописывать.
    /// </summary>
    [Flags]
    public enum CampUnlock : uint
    {
        None = 0, Trader = 1, Alchemist = 2, TravelTable = 4,
        Rank1 = 8, Rank2 = 16, Rank3 = 32,
    }

    /// <summary>Ступень ранга лагеря: какой босс акта (по порядку) побеждён и какой уровень героя.</summary>
    public readonly struct CampRankRequirement
    {
        public readonly int BossIndex, Level;
        public CampRankRequirement(int bossIndex, int level) { BossIndex = bossIndex; Level = level; }
    }

    /// <summary>
    /// Развитие лагеря (решения 05–06.10). Очков лагеря больше нет: жители приходят
    /// по уровню героя, стол — после первого завершённого забега, ранг один на весь
    /// лагерь и требует босса вместе с уровнем. Всё, кроме истории (попытки, главы,
    /// победы), выводится из состояния: так сохранение не может разойтись с правилами.
    /// </summary>
    public sealed partial class Camp
    {
        /// <summary>Уровень, на котором в прогрессивный лагерь приходят Вен и Лео.</summary>
        public const int ResidentsLevel = 3;

        /// <summary>Наивысший ранг лагеря: три босса акта I.</summary>
        public const int MaxCampRank = 3;

        // Всё, что когда-либо объявлялось. Лишние биты из сохранения обрезаются по нему.
        internal const CampUnlock KnownUnlocks = CampUnlock.Trader | CampUnlock.Alchemist | CampUnlock.TravelTable
            | CampUnlock.Rank1 | CampUnlock.Rank2 | CampUnlock.Rank3;

        bool _usesCampProgression;
        readonly bool[] _chaptersCompleted = new bool[3];
        bool _extractedFind, _smithFindDiscussed;
        int _deepestAttempt;
        ulong _usedPotionKinds;
        // Что уже было достигнуто при прошлом пересчёте. Не сохраняется: после чтения
        // пересчёт без флагов ставит его заново, и старые открытия не объявляются второй раз.
        CampUnlock _reached;

        public bool UsesCampProgression => _usesCampProgression;
        public bool IsProgressive => _usesCampProgression;
        public int AttemptCount { get; private set; }

        /// <summary>Стол сборов — после первого завершённого реального забега (06.10, прежде — после второго).</summary>
        public bool HasTravelTable => !_usesCampProgression || AttemptCount >= 1;

        public int Rank(CampResident resident) => (uint)resident < 3 && HasResident(resident) ? CampRank : 0;
        public bool HasResident(CampResident resident) => resident == CampResident.Smith ? Has(CampService.Smith)
            : resident == CampResident.Trader ? Has(CampService.Trader)
            : resident == CampResident.Alchemist && Has(CampService.Alchemist);

        /// <summary>Открытия, которые игрок ещё не видел. Окно прибытия гасит их AcknowledgeUnlocks.</summary>
        public CampUnlock PendingUnlocks { get; private set; }

        public void AcknowledgeUnlocks(CampUnlock f) => PendingUnlocks &= ~f;

        /// <summary>
        /// Один ранг на весь лагерь (решение M4): наибольший r, все ступени 1…r которого
        /// выполнены. Ступень r требует именно босса r, а не «любых r боссов» (пробел №36).
        /// Sandbox тестов и проверочных лагерей вида — сразу на вершине.
        /// </summary>
        public int CampRank
        {
            get
            {
                if (!_usesCampProgression) return MaxCampRank;
                int rank = 0;
                while (rank < MaxCampRank)
                {
                    var next = RankRequirement(rank + 1);
                    if (Level < next.Level || !BossDefeated(RunBossKeys.At(next.BossIndex))) break;
                    rank++;
                }
                return rank;
            }
        }

        /// <summary>
        /// Ступень ранга r (05.10): 1 — первый босс и ур. 6, 2 — второй и ур. 12, 3 — третий и ур. 18.
        /// Другой r недостижим: босса −1 нет, уровень int.MaxValue.
        /// </summary>
        public static CampRankRequirement RankRequirement(int rank)
            => rank >= 1 && rank <= MaxCampRank ? new CampRankRequirement(rank - 1, 6 * rank) : new CampRankRequirement(-1, int.MaxValue);

        public void RecordRealAttemptEnded(int depth, int extractedItems, bool bossDefeated = false)
        {
            // Вызывает только реальная сессия, один раз: гибель тоже знакомит лагерь с походом.
            // Жители по числу попыток больше не приходят (06.10) — только стол.
            if (AttemptCount < int.MaxValue) AttemptCount++;
            _deepestAttempt = Math.Max(_deepestAttempt, Math.Max(0, depth));
            if (extractedItems > 0) _extractedFind = true;
            // Закалка, начатая до забега, закрыта с тем, что набрано: рост уже в вещи (Camp.Temper).
            SettleForgeSession();
            RefreshUnlocks();
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
            if (resident == CampResident.Smith) Earn(CurrencyType.Steel, 1);
            else if (resident == CampResident.Trader) { Earn(CurrencyType.Steel, 1); Earn(CurrencyType.Gold, 50); }
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

        // ---- пересчёт открытий ----

        /// <summary>
        /// Услуги, ранги и флаги прибытия из уровня, попыток и побед. Флаги поднимаются
        /// только на том, чего при прошлом пересчёте ещё не было: повторный вызов ничего
        /// не объявляет. raise = false — после чтения сохранения: всё достигнутое уже видено
        /// или лежит в PendingUnlocks из файла.
        /// </summary>
        void RefreshUnlocks(bool raise = true)
        {
            if (_usesCampProgression)
            {
                // Жители выводятся из уровня в обе стороны: иначе понижение уровня через F8
                // оставило бы Вена в лагере до перезагрузки, а после неё — нет.
                const CampService residents = CampService.Trader | CampService.Alchemist;
                Services = Level >= ResidentsLevel ? Services | residents : Services & ~residents;
            }
            CampUnlock now = CurrentUnlocks();
            if (raise && _usesCampProgression) PendingUnlocks |= now & ~_reached;
            _reached = now;
            ExpandTraderStock();
        }

        CampUnlock CurrentUnlocks()
        {
            CampUnlock now = CampUnlock.None;
            if (HasResident(CampResident.Trader)) now |= CampUnlock.Trader;
            if (HasResident(CampResident.Alchemist)) now |= CampUnlock.Alchemist;
            if (HasTravelTable) now |= CampUnlock.TravelTable;
            int rank = CampRank;
            if (rank >= 1) now |= CampUnlock.Rank1;
            if (rank >= 2) now |= CampUnlock.Rank2;
            if (rank >= 3) now |= CampUnlock.Rank3;
            return now;
        }

        void HashCampProgression(ref ulong hash)
        {
            Hashing.Mix(ref hash, _usesCampProgression ? 1 : 0); Hashing.Mix(ref hash, AttemptCount);
            Hashing.Mix(ref hash, _deepestAttempt);
            Hashing.Mix(ref hash, _extractedFind ? 1 : 0); Hashing.Mix(ref hash, _smithFindDiscussed ? 1 : 0);
            Hashing.Mix(ref hash, _usedPotionKinds);
            for (int i = 0; i < 3; i++) Hashing.Mix(ref hash, _chaptersCompleted[i] ? 1 : 0);
            Hashing.Mix(ref hash, (int)PendingUnlocks);
            HashRunLink(ref hash);
            HashForgeSession(ref hash);
        }

        // ---- сохранение v10, секция Progress (6) ----

        internal byte ChapterBits => (byte)((_chaptersCompleted[0] ? 1 : 0) | (_chaptersCompleted[1] ? 2 : 0) | (_chaptersCompleted[2] ? 4 : 0));
        internal int DeepestAttempt => _deepestAttempt;
        internal bool ExtractedFind => _extractedFind;
        internal bool SmithFindDiscussed => _smithFindDiscussed;
        internal ulong UsedPotionKinds => _usedPotionKinds;

        /// <summary>
        /// История из сохранения. Числа уже проверены кодеком на знак; лишние биты
        /// обрезаются здесь — правила могли сузиться патчем.
        /// </summary>
        internal void RestoreCampProgression(int attempts, int deepest, bool extractedFind, bool smithFindDiscussed,
            ulong usedPotionKinds, byte chapters, uint pending)
        {
            AttemptCount = attempts; _deepestAttempt = deepest;
            _extractedFind = extractedFind; _smithFindDiscussed = smithFindDiscussed && extractedFind;
            _usedPotionKinds = usedPotionKinds & ((1UL << PotionKindCount) - 1);
            // Главы идут по порядку: следующая не закрывается раньше предыдущей.
            _chaptersCompleted[0] = (chapters & 1) != 0;
            _chaptersCompleted[1] = _chaptersCompleted[0] && (chapters & 2) != 0;
            _chaptersCompleted[2] = _chaptersCompleted[1] && (chapters & 4) != 0;
            PendingUnlocks = (CampUnlock)pending & KnownUnlocks;
        }

        /// <summary>
        /// После чтения: услуги, ранги и лавка выводятся заново, флаги не поднимаются.
        /// В Sandbox флагов нет вовсе: там всё открыто с начала.
        /// </summary>
        internal void FinishRestore()
        {
            if (!_usesCampProgression) PendingUnlocks = CampUnlock.None;
            RefreshUnlocks(raise: false);
        }
    }
}
