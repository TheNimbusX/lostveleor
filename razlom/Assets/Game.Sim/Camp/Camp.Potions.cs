namespace Game.Sim
{
    // Existing values are save identifiers. Append new recipes only.
    public enum PotionKind : byte
    {
        SmallHealth = 0, LargeHealth = 1, SmallLavidium = 2, LargeLavidium = 3,
        LivingResin = 4, LavidiumSurge = 5, Mixed = 6, Clear = 7
    }
    public enum PotionPurchaseResult : byte
    {
        Success, InvalidKind, AlchemistUnavailable, RecipeLocked, StockFull, InsufficientGold
    }

    public sealed partial class Camp
    {
        public const int PotionLimit = 999;
        public const int PotionKindCount = 8;
        readonly int[] _potions = new int[PotionKindCount];
        readonly PotionKind[] _selectedPotions = { PotionKind.SmallHealth, PotionKind.SmallLavidium };

        public static int PotionSlot(PotionKind kind)
            => kind == PotionKind.SmallHealth || kind == PotionKind.LargeHealth || kind == PotionKind.LivingResin ? 0
                : kind == PotionKind.SmallLavidium || kind == PotionKind.LargeLavidium || kind == PotionKind.LavidiumSurge ? 1 : -1;
        public static int PotionPrice(PotionKind kind) => (int)kind >= 6 ? 55 : (int)kind >= 4 ? 70 : ((int)kind & 1) == 0 ? 15 : 40;
        public static int PotionPercent(PotionKind kind) => (int)kind >= 4 ? 20 : ((int)kind & 1) == 0 ? 10 : 30;
        public static byte PotionInputBit(PotionKind kind) => (uint)kind < 6
            ? (byte)(1 << ((int)kind < 4 ? (int)kind : (int)kind + 2)) : (byte)0;
        public int PotionCount(PotionKind kind) => (uint)kind < PotionKindCount ? _potions[(int)kind] : 0;
        public PotionKind SelectedPotion(int slot) => _selectedPotions[slot];
        /// <summary>
        /// Рецепты Лео (06.10) открываются только рангом лагеря; заказы удалены. Малые
        /// открыты всегда: новый профиль начинает с тремя малыми до приезда Лео. Sandbox
        /// стоит на ранге 3, поэтому там открыто всё.
        /// </summary>
        public bool PotionUnlocked(PotionKind kind)
        {
            if ((uint)kind >= PotionKindCount) return false;
            if (kind == PotionKind.SmallHealth || kind == PotionKind.SmallLavidium) return true;
            if (!HasResident(CampResident.Alchemist)) return false;
            int rank = Rank(CampResident.Alchemist);
            return (kind == PotionKind.LargeHealth || kind == PotionKind.LargeLavidium) && rank >= 1
                || (kind == PotionKind.LivingResin || kind == PotionKind.LavidiumSurge) && rank >= 2
                || (kind == PotionKind.Mixed || kind == PotionKind.Clear) && rank >= 3;
        }
        public bool SelectPotionForSlot(int slot, PotionKind kind)
        {
            if ((uint)slot >= 2 || !PotionUnlocked(kind) || _selectedPotions[1 - slot] == kind) return false;
            _selectedPotions[slot] = kind; return true;
        }
        public bool SelectPotion(PotionKind kind)
        {
            int slot = PotionSlot(kind);
            if (slot < 0 || !PotionUnlocked(kind)) return false;
            return SelectPotionForSlot(slot, kind);
        }
        public PotionKind CyclePotion(int slot)
        {
            if ((uint)slot >= 2) return PotionKind.SmallHealth;
            PotionKind[] choices = slot == 0
                ? new[] { PotionKind.SmallHealth, PotionKind.LargeHealth, PotionKind.LivingResin }
                : new[] { PotionKind.SmallLavidium, PotionKind.LargeLavidium, PotionKind.LavidiumSurge };
            int current = System.Array.IndexOf(choices, _selectedPotions[slot]);
            for (int step = 1; step <= choices.Length; step++)
            {
                var next = choices[(current + step + choices.Length) % choices.Length];
                if (PotionUnlocked(next) && next != _selectedPotions[1-slot]) { _selectedPotions[slot] = next; break; }
            }
            return _selectedPotions[slot];
        }
        public bool BuyPotion(PotionKind kind) => TryBuyPotion(kind) == PotionPurchaseResult.Success;
        public PotionPurchaseResult TryBuyPotion(PotionKind kind)
        {
            if ((uint)kind >= PotionKindCount) return PotionPurchaseResult.InvalidKind;
            if (!Has(CampService.Alchemist)) return PotionPurchaseResult.AlchemistUnavailable;
            if (!PotionUnlocked(kind)) return PotionPurchaseResult.RecipeLocked;
            if (_potions[(int)kind] >= PotionLimit) return PotionPurchaseResult.StockFull;
            if (!Spend(CurrencyType.Gold, PotionPrice(kind))) return PotionPurchaseResult.InsufficientGold;
            _potions[(int)kind]++;
            return PotionPurchaseResult.Success;
        }
        public void GrantPotions(PotionKind kind, int amount)
        {
            if ((uint)kind >= PotionKindCount || amount <= 0 || !PotionUnlocked(kind)) return;
            _potions[(int)kind] = (int)System.Math.Min(PotionLimit, (long)_potions[(int)kind] + amount);
        }
        internal bool ConsumePotion(PotionKind kind, Simulation sim, bool free = false, bool training = false)
        {
            if ((uint)kind >= PotionKindCount || sim == null || !sim.Entities.Alive[Simulation.PlayerId]
                || !PotionUnlocked(kind) || !training && _potions[(int)kind] == 0 || !PotionWouldHelp(kind,sim)) return false;
            if (!free && !training) _potions[(int)kind]--;
            int percent = PotionPercent(kind);
            var entities = sim.Entities;
            if (PotionSlot(kind) == 0 || kind == PotionKind.Mixed)
            {
                if (kind == PotionKind.Mixed) percent = 18;
                long amount = System.Math.Max(1, (long)entities.MaxHealth[0] * percent / 100);
                entities.Health[0] = (int)System.Math.Min(entities.MaxHealth[0], entities.Health[0] + amount);
            }
            if (PotionSlot(kind) == 1 || kind == PotionKind.Mixed)
                entities.Lavidium[0] = Fix64.Min(Fix64.FromInt(entities.MaxLavidium[0]),
                entities.Lavidium[0] + Fix64.FromInt(entities.MaxLavidium[0]) * Fix64.Ratio(percent, 100));
            if (kind == PotionKind.LivingResin) sim.ApplyResinPotion();
            if (kind == PotionKind.LavidiumSurge) sim.ApplySurgePotion();
            if (kind == PotionKind.Clear) sim.ApplyClearPotion();
            return true;
        }
        internal bool PotionWouldHelp(PotionKind kind, Simulation sim)
        {
            if (sim == null || sim.Entities.Count == 0 || !sim.Entities.Alive[0]) return false;
            var e = sim.Entities;
            if (kind == PotionKind.SmallHealth || kind == PotionKind.LargeHealth) return e.Health[0] < e.MaxHealth[0];
            if (kind == PotionKind.SmallLavidium || kind == PotionKind.LargeLavidium) return e.Lavidium[0] < Fix64.FromInt(e.MaxLavidium[0]);
            if (kind == PotionKind.Mixed) return e.Health[0] < e.MaxHealth[0] || e.Lavidium[0] < Fix64.FromInt(e.MaxLavidium[0]);
            return (uint)kind < PotionKindCount;
        }
        /// <summary>
        /// Запасы из сохранения v10. Кодек уже проверил границы; видов в файле может быть
        /// меньше (старее) или больше (новее) — лишнее отброшено, недостающее нулём.
        /// </summary>
        internal void RestorePotionCount(PotionKind kind, int count) => _potions[(int)kind] = count;

        /// <summary>Выбор из сохранения; закрытое или повторное подгоняет ValidatePotionSelection.</summary>
        internal void RestorePotionSelection(PotionKind first, PotionKind second)
        {
            _selectedPotions[0] = first; _selectedPotions[1] = second;
        }

        /// <summary>
        /// Прослойка для чужого незакоммиченного CampBridgePolishProbe (план 06.10, A.4):
        /// знакомства с Лео больше нет — он «знаком», как только приехал.
        /// </summary>
        public bool HasMetAlchemist => HasResident(CampResident.Alchemist);
        public void MeetAlchemist() { }

        void HashPotions(ref ulong hash)
        {
            foreach (int count in _potions) Hashing.Mix(ref hash, count);
            Hashing.Mix(ref hash, (int)_selectedPotions[0]); Hashing.Mix(ref hash, (int)_selectedPotions[1]);
        }
    }
}
