namespace Game.Sim
{
    public sealed partial class GameSession
    {
        int _campPotionCooldownTicksLeft;
        int _runSteel, _runCore;
        bool _runBossDefeated, _runProgressRecorded;
        int _materialDepth;
        readonly bool[] _materialEncounterClaimed = new bool[512];
        public bool PreparationRequested { get; private set; }
        public void CancelRiftEntryRequest() => PreparationRequested = false;
        public void RequestRiftEntry()
        {
            if (Mode == GameMode.Rift || !Camp.Has(CampService.RiftPortal)) return;
            if (!Camp.HasTravelTable) { EnterRift(); return; }
            ReturnToCamp();
            PreparationRequested = true;
        }
        public int PotionCooldownTicksLeft => Mode == GameMode.Rift && Run != null
            ? Run.PotionCooldownTicksLeft : _campPotionCooldownTicksLeft;
        public RunPreparation Preparation => Mode == GameMode.Rift && Run != null ? Run.Preparation : Camp.CreateRunPreparation();
        public bool SetPreparedStarter(int poolIndex)
        {
            if (Mode == GameMode.Rift || !Camp.SelectStarterSkill(poolIndex)) return false;
            CampLoadout.ResetToStarter(poolIndex); CampLoadout.ApplyTo(CampSim);
            if (Ground != null) CampLoadout.ApplyTo(Ground.Sim);
            return true;
        }
        public bool SetPreparedGift(CampGift gift) => Mode != GameMode.Rift && Camp.SelectGift(gift);
        public bool SetPreparedPotion(int slot, PotionKind kind)
            => Mode != GameMode.Rift && Camp.SelectPotionForSlot(slot, kind);
        bool CanDrinkNow
            => Mode == GameMode.Rift ? !IsDeveloperRun && Run != null &&
                (Run.Phase == RunPhase.Clearing || Run.Phase == RunPhase.SeekingExit)
                : Mode == GameMode.Camp && (Ground != null || Training != null &&
                    Training.InZone(CampSim.Entities.Position[Simulation.PlayerId]));
        public bool CanUsePotionSlot(int slot)
        {
            if ((uint)slot >= 2 || !CanDrinkNow || PotionCooldownTicksLeft > 0) return false;
            var kind = Preparation.PotionAt(slot);
            return Camp.PotionUnlocked(kind) && Camp.PotionWouldHelp(kind, ActiveSim)
                && (Mode == GameMode.Camp || Camp.PotionCount(kind) > 0);
        }
        void HandlePotionInput(in InputFrame input)
        {
            // Вступление Хозяина Чащи (кат-сцена): ввод героя не читается — и бутылки тоже.
            if (!CanDrinkNow || !ActiveSim.Entities.Alive[0] || ActiveSim.ThicketIntroHoldsHero) return;
            if (Mode == GameMode.Camp)
                for (int slot = 0; slot < 2; slot++) if ((input.PotionMask & (16 << slot)) != 0) Camp.CyclePotion(slot);
            int slots = input.PotionSlotMask & 3;
            var preparation = Preparation;
            // Старые записи ввода читаются, но не позволяют подменить выбранные виды в дороге.
            for (int slot = 0; slot < 2; slot++)
                if ((input.PotionMask & Camp.PotionInputBit(preparation.PotionAt(slot))) != 0) slots |= 1 << slot;
            for (int slot = 0; slot < 2; slot++)
            {
                if ((slots & (1 << slot)) == 0 || !CanUsePotionSlot(slot)) continue;
                var kind = preparation.PotionAt(slot); bool training = Mode == GameMode.Camp;
                if (!Camp.ConsumePotion(kind, ActiveSim, free: !training && Run.FirstPotionFree, training: training)) continue;
                if (training) _campPotionCooldownTicksLeft = 8 * Simulation.TicksPerSecond;
                else
                {
                    Run.PotionWasUsed(); Camp.RecordRealPotionUsed(kind);
                    _alchemyLevelWithoutPotion = false; _runStats?.CountPotion(kind);
                }
                // Оба нажатия в одном тике имеют фиксированный приоритет первого полезного слота.
                break;
            }
        }
        void ResetRunProgressTracking()
        { _runSteel = _runCore = _materialDepth = 0; _runBossDefeated = _runProgressRecorded = false;
            System.Array.Clear(_materialEncounterClaimed, 0, _materialEncounterClaimed.Length); }
        void RecordMaterialDeaths()
        {
            if (IsDeveloperRun) return;
            var events = Run.Sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i]; if (e.Type != SimEventType.Death || e.Target <= 0) continue;
                if (e.Target == Run.BossId && !_runBossDefeated) { _runCore++; _runBossDefeated = true; }
            }
            var plan = Run.Encounters; if (plan == null) return;
            if (_materialDepth != Run.Depth)
            { _materialDepth = Run.Depth; System.Array.Clear(_materialEncounterClaimed, 0, _materialEncounterClaimed.Length); }
            if (Run.Sim.ActiveEncounter != null)
            {
                // Волны одной аренной встречи дают одну сталь: последний элитный моб
                // не завершает встречу, пока живы её остальные волны или дети Расщепеня.
                if (_materialEncounterClaimed[0] || Run.Sim.EncounterWavesPending || Run.Sim.HasPendingSplits) return;
                bool elite = false;
                for (int id = 1; id < Run.Sim.Entities.Count; id++)
                { if (Run.Sim.Entities.Alive[id] && Run.Sim.Entities.Side[id] != Faction.Wole) return; elite |= plan.IsElite(id) && id != Run.BossId; }
                if (elite) { _materialEncounterClaimed[0] = true; _runSteel++; }
                return;
            }
            for (int index = 0; index < plan.Count && index < _materialEncounterClaimed.Length; index++)
            {
                if (_materialEncounterClaimed[index] || plan.Alive(index, Run.Sim.Entities) != 0) continue;
                var encounter = plan.Get(index); bool elite = false, splitting = false;
                for (int id = encounter.FirstEntity; id < encounter.FirstEntity + encounter.EnemyCount; id++)
                { elite |= plan.IsElite(id) && id != Run.BossId; splitting |= Run.Sim.HasPendingSplitFor(id); }
                for (int id = encounter.FirstEntity + encounter.EnemyCount; id < Run.Sim.Entities.Count; id++)
                {
                    int parent = Run.Sim.SplitParentOf(id);
                    if (Run.Sim.Entities.Alive[id] && parent >= encounter.FirstEntity
                        && parent < encounter.FirstEntity + encounter.EnemyCount) splitting = true;
                }
                if (elite && !splitting) { _materialEncounterClaimed[index] = true; _runSteel++; }
            }
        }
        void CompleteRealAttempt(int keptItems, bool keeps)
        {
            if (IsDeveloperRun || _runProgressRecorded) return;
            _runProgressRecorded = true;
            if (keeps)
            {
                Camp.EarnForgeMaterial(ForgeMaterial.Steel, _runSteel);
                Camp.EarnForgeMaterial(ForgeMaterial.Core, _runCore);
            }
            Camp.RecordRealAttemptEnded(Run.Depth, keptItems, _runBossDefeated);
        }
        void HashSessionPreparation(ref ulong hash)
        {
            if (_runSteel == 0 && _runCore == 0 && !_runBossDefeated && _campPotionCooldownTicksLeft == 0 && !PreparationRequested) return;
            Hashing.Mix(ref hash, 0x43505250); Hashing.Mix(ref hash, _runSteel); Hashing.Mix(ref hash, _runCore);
            Hashing.Mix(ref hash, _runBossDefeated ? 1 : 0); Hashing.Mix(ref hash, _campPotionCooldownTicksLeft);
            Hashing.Mix(ref hash, PreparationRequested ? 1 : 0);
            Hashing.Mix(ref hash, _materialDepth);
            for (int i = 0; i < _materialEncounterClaimed.Length; i++) if (_materialEncounterClaimed[i]) Hashing.Mix(ref hash, i + 1);
        }
    }
}
