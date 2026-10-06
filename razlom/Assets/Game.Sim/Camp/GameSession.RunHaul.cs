namespace Game.Sim
{
    /// <summary>
    /// Что забег отдаёт лагерю сразу (экономика 06.10, решение M1 плана «Лагерь 06–10.10»).
    ///
    /// Пепел, сталь, сердце и победа над боссом, когда-либо взятые навыки и открытые
    /// артефакты уходят в Camp в тот же шаг, что их нашли, — как опыт. Так смерть,
    /// уход и выход из игры посреди забега их не отнимают, а правило «при любом
    /// исходе» живёт в одном месте. В FinishRun переезжают только золото и вещи.
    ///
    /// Тестовый забег (IsDeveloperRun) не даёт ничего. Счётчики здесь — только для
    /// итогов (RunSummary) и для процентов клятв от суммы; источник правды — лагерь.
    ///
    /// Здесь же доска клятв со стороны сессии (BuyOath, SetOathActive): купленное сразу
    /// доходит до героя лагеря, а проценты добычи берутся из снимка забега (Run.Boons).
    /// </summary>
    public sealed partial class GameSession
    {
        // _runAshBase — пепел по ставкам 1/5/20, _runAsh — уже отданное лагерю с процентом.
        int _runAsh, _runAshBase, _runSkillMask, _takenSeen;

        /// <summary>
        /// Процент пепла («Пепельный след» — 120). Берётся из снимка забега, а не из лагеря:
        /// клятвы закреплены на вход в Разлом, и переключение доски посреди забега пепел этого
        /// забега не меняет. Тестовый забег идёт с пустым снимком — 100 (и пепла не даёт вовсе).
        /// </summary>
        int AshPercent => Run != null ? Run.Boons.AshPercent : 100;

        /// <summary>Сколько процентов золота доезжает при смерти («Цепкие руки» — 75). Без клятв 50.</summary>
        int DeathGoldPercent => Run != null ? Run.Boons.DeathGoldPercent : RunEconomy.DeathGoldKeptPercent;

        /// <summary>
        /// Купить ступень клятвы (доска в палатке). Удача — и герой лагеря сразу получает
        /// статовые клятвы: манекены должны чувствовать купленное (пробел №23).
        /// </summary>
        public OathResult BuyOath(OathId id)
        {
            OathResult result = Camp.BuyOath(id);
            if (result == OathResult.Success) RefreshCampOaths();
            return result;
        }

        /// <summary>Включить или выключить купленную клятву; герой лагеря — сразу с новым набором.</summary>
        public OathResult SetOathActive(OathId id, bool on)
        {
            OathResult result = Camp.SetOathActive(id, on);
            if (result == OathResult.Success) RefreshCampOaths();
            return result;
        }

        /// <summary>
        /// Статовые клятвы (шкура, шаг, глаз, запас) — герою лагеря и Полигона. Забег берёт
        /// свой снимок сам, в BeginRift. Пустой набор симуляции ничего не стоит (SetBoons
        /// выходит сразу), поэтому звать можно в любой момент — например, после загрузки.
        /// </summary>
        public void RefreshCampOaths()
        {
            RunBoons boons = Camp.CreateCampBoons();
            CampSim.SetBoons(in boons);
            Ground?.Sim.SetBoons(in boons);
        }

        void ResetRunHaul() { _runAsh = _runAshBase = _runSkillMask = _takenSeen = 0; }

        /// <summary>
        /// Пепел за смерть врага: босс 20, элита 5, остальные 1 — кем бы он ни был убит.
        /// Уход в землю по концу выживания события Death не даёт и пепла не приносит.
        /// Процент — от суммы забега, вниз: лагерь получает разницу с уже отданным.
        /// </summary>
        void CreditAsh(int target)
        {
            EntityStore entities = Run.Sim.Entities;
            if (target >= entities.Count || entities.Side[target] == entities.Side[Simulation.PlayerId]) return;
            // Босс помечен и элитой, поэтому проверяется первым (как RunStats.RankOf).
            int ash = target == Run.BossId ? RunEconomy.AshBoss
                : Run.Encounters != null && Run.Encounters.IsElite(target) ? RunEconomy.AshElite
                : RunEconomy.AshNormal;
            _runAshBase += ash;
            int total = RunEconomy.Percent(_runAshBase, AshPercent);
            Camp.Earn(CurrencyType.Ash, total - _runAsh);
            _runAsh = total;
        }

        /// <summary>
        /// Навыки и артефакты забега — в лагерь, как только они взяты.
        ///
        /// Навык считается взятым, если хоть раз стоял в слоте: стартовый, с карточки,
        /// с дропа элиты, заменой. Разобранный с экрана замены в слот не вставал и не
        /// пишется. Все пути в слот идут внутри RiftRun.Step, одна команда за шаг —
        /// поэтому проверки после каждого шага хватает. Лагерь хранит по
        /// AbilityDefinition.Id, маска здесь — только чтобы не звать его каждый тик.
        ///
        /// Артефакты — с экрана босса и из тайника: обе дороги пишут в список взятого.
        /// Отказ от награды (SkipReward) туда не пишет и артефакт не открывает.
        /// </summary>
        void TrackRunProgress()
        {
            if (IsDeveloperRun || Run == null) return;
            for (int slot = 0; slot < RunLoadout.Slots; slot++)
            {
                int pool = Run.Loadout.PoolIndexAt(slot);
                if ((uint)pool >= (uint)PelagKit.PoolSize || (_runSkillMask & (1 << pool)) != 0) continue;
                _runSkillMask |= 1 << pool;
                Camp.RecordSkillTaken(PelagKit.PoolDefinition(pool).Id);
            }
            for (; _takenSeen < Run.TakenRewardCount; _takenSeen++)
            {
                RewardOffer offer = Run.GetTaken(_takenSeen);
                if (offer.Kind == RewardKind.Artifact && RunArtifacts.IsValid(offer.Artifact))
                    Camp.OpenArtifact(offer.Artifact);
            }
        }
    }
}
