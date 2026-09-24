namespace Game.Sim
{
    /// <summary>
    /// Итоги забега — то, что показывает экран выхода.
    ///
    /// В лагерь уезжают ТОЛЬКО предметы и золото, и только при выходе или
    /// прохождении: способности и таланты живут внутри забега, а смерть
    /// отнимает всё найденное (решение владельца от 15 сентября).
    /// </summary>
    public readonly struct RunSummary
    {
        public readonly RunOutcome Outcome;
        public readonly int Depth;
        public readonly int RiftsCleared;

        /// <summary>Сколько предметов доехало до сумки.</summary>
        public readonly int ItemsKept;

        /// <summary>Сколько не влезло. Ненулевое значение — это повод зайти в лагерь.</summary>
        public readonly int ItemsLost;

        /// <summary>Золото забега, доехавшее до кошелька.</summary>
        public readonly int GoldKept;

        /// <summary>Сколько предметов осталось в Разломе после смерти.</summary>
        public readonly int ItemsLeftBehind;

        /// <summary>Сколько золота осталось в Разломе после смерти.</summary>
        public readonly int GoldLeftBehind;

        public RunSummary(RunOutcome outcome, int depth, int riftsCleared, int itemsKept, int itemsLost,
            int goldKept = 0, int itemsLeftBehind = 0, int goldLeftBehind = 0)
        {
            Outcome = outcome;
            Depth = depth;
            RiftsCleared = riftsCleared;
            ItemsKept = itemsKept;
            ItemsLost = itemsLost;
            GoldKept = goldKept;
            ItemsLeftBehind = itemsLeftBehind;
            GoldLeftBehind = goldLeftBehind;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Outcome);
            Hashing.Mix(ref hash, Depth);
            Hashing.Mix(ref hash, RiftsCleared);
            Hashing.Mix(ref hash, ItemsKept);
            Hashing.Mix(ref hash, ItemsLost);
            Hashing.Mix(ref hash, GoldKept);
            Hashing.Mix(ref hash, ItemsLeftBehind);
            Hashing.Mix(ref hash, GoldLeftBehind);
        }
    }

    /// <summary>
    /// Игра целиком: лагерь, вход в Разлом, забег, экран итогов и обратно.
    ///
    /// Забег — единица жизни симуляции: каждый вход в Разлом создаёт новую,
    /// с новыми сущностями и новыми листами статов. Всё, что живёт дольше
    /// забега — сумка, кошелёк, надетое, — лежит в лагере и переживает
    /// пересоздание.
    ///
    /// Сиды забегов роллятся из одного потока сессии, поэтому цепочка забегов
    /// воспроизводима целиком, а не только каждый по отдельности. Это то самое
    /// свойство, на котором потом стоит проверка топ-100.
    /// </summary>
    public sealed class GameSession
    {
        private readonly ModuleSet _modules;
        private readonly LocationDefinition _location;
        private readonly int[] _itemBaseIds;
        private readonly int _simCapacity;

        private Pcg32 _runSeeds;
        private int _alchemyTrackedDepth;
        private bool _alchemyLevelWithoutPotion;
        public bool AlchemyCleanLevelInProgress => Mode == GameMode.Rift && !IsDeveloperRun
            && _alchemyTrackedDepth == Run.Depth && _alchemyLevelWithoutPotion
            && Camp.AlchemyStatus(AlchemistOrder.Surge) == AlchemistOrderStatus.Accepted;

        public Camp Camp { get; }
        public GameMode Mode { get; private set; }

        /// <summary>Текущий забег. null в лагере.</summary>
        public RiftRun Run { get; private set; }

        /// <summary>Полигон, пока игрок на нём стоит. null, когда сошёл.</summary>
        public ProvingGround Ground { get; private set; }

        public bool OnProvingGround => Ground != null;
        public Simulation CampSim { get; private set; }
        public CampTraining Training { get; private set; }

        /// <summary>
        /// Набор способностей в лагере и на Полигоне: автоатака и Вихрь. Меню
        /// разработчика и съёмки меняют его для проверки. Не сохраняется —
        /// способности живут в забеге, а лагерный набор только инструмент.
        /// </summary>
        public RunLoadout CampLoadout { get; } = new RunLoadout();

        /// <summary>Набор, который сейчас в руках у героя: забега или лагеря.</summary>
        public RunLoadout ActiveLoadout => Mode == GameMode.Rift && Run != null ? Run.Loadout : CampLoadout;

        /// <summary>
        /// Только съёмки: забег начинается с лагерным набором вместо стартового.
        /// Тестовые забеги из меню разработчика берут его всегда.
        /// </summary>
        public bool CarryCampLoadoutIntoRift { get; set; }

        /// <summary>Правка забега из меню разработчика делает его тестовым: добыча не переносится, опыт не идёт.</summary>
        public void MarkDeveloperRun()
        {
            if (Run != null) IsDeveloperRun = true;
        }

        /// <summary>
        /// Что сейчас рисовать. Меняется вместе с Generation — представление
        /// обязано сравнивать поколение со своим и пересобирать привязки:
        /// у новой симуляции индексы сущностей начинаются заново.
        /// </summary>
        public Simulation ActiveSim
            => Mode == GameMode.Camp
                ? (Ground != null ? Ground.Sim : CampSim)
                : (Run != null ? Run.Sim : null);

        /// <summary>Растёт при каждой смене активной симуляции.</summary>
        public int Generation { get; private set; }

        public ulong LastRunSeed { get; private set; }
        public int RunNumber { get; private set; }
        public RunSummary LastRun { get; private set; }
        public bool IsDeveloperRun { get; private set; }
        public bool DeveloperInvulnerable => IsDeveloperRun && Run != null && Run.Sim.PlayerInvulnerable;

        public void SetDeveloperInvulnerable(bool value)
        {
            if (Mode != GameMode.Rift || Run == null)
                throw new System.InvalidOperationException("Сначала войди в разлом.");
            if (value) IsDeveloperRun = true;
            Run.Sim.PlayerInvulnerable = value;
        }

        /// <summary>Используется только автоматизированной съёмкой combat slice.</summary>
        public bool WhirlwindShowcase { get; set; }

        public CombatFeelCaptureTier CombatFeelShowcase { get; set; }
        public int CombatFeelEnemyCount { get; set; } = 1;

        public GameSession(ulong sessionSeed, Camp camp, ModuleSet modules, int[] itemBaseIds,
            int simCapacity = 512, LocationDefinition location = null)
        {
            Camp = camp;
            _location = location;
            _location?.ValidateCapacity(simCapacity);
            _modules = location?.Modules ?? modules;
            _itemBaseIds = itemBaseIds;
            _simCapacity = simCapacity;

            // Свой поток сидов, независимый от боевых: он не должен сдвигаться
            // от того, сколько раз в забеге бросили на крит.
            _runSeeds = new Pcg32(sessionSeed, 0x853C49E6748FEA9BUL);

            Mode = GameMode.Camp;
            CampSim = new Simulation(sessionSeed, simCapacity);
            CampSim.SetPlayerLevel(Camp.Level);
            CampSim.SetupCamp(FixVec2.Zero, null);
            BindCampEquipment();
        }

        public void ConfigureCampWorld(FixVec2 spawn, CampWalkMap map)
        {
            CampSim.SetupCamp(spawn, map);
            BindCampEquipment();
            Generation++;
        }

        public void ConfigureCampTraining(CampDummyDefinition[] definitions)
        {
            if (definitions == null || definitions.Length >= _simCapacity)
                throw new System.ArgumentException("Число мишеней превышает вместимость лагеря.");
            Ground = null;
            Training = new CampTraining(definitions);
            BindCampEquipment();
            Generation++;
        }

        private void BindCampEquipment()
        {
            CampSim.ResetCampActivity();
            Camp.Worn.Bind(CampSim.Entities.Stats[Simulation.PlayerId]);
            CampSim.RefreshPlayerStats(true);
            CampSim.StopPlayerMovement();
            Training?.Populate(CampSim);
        }

        /// <summary>
        /// Один шаг игры. Что именно шагает, решает режим: в Разломе — забег,
        /// в лагере — Полигон, если игрок на нём стоит, и ничего, если не стоит.
        /// На экране итогов не шагает ничто: он для того и нужен, чтобы игрок
        /// мог подумать, не теряя здоровья.
        /// </summary>
        public void Step(in InputFrame input)
        {
            bool potionAllowed=Mode==GameMode.Camp || (Mode==GameMode.Rift && !IsDeveloperRun && (Run.Phase==RunPhase.Clearing || Run.Phase==RunPhase.SeekingExit));
            if(potionAllowed && ActiveSim.Entities.Alive[0])
            {
                for(int slot=0;slot<2;slot++)if((input.PotionMask&(16<<slot))!=0)Camp.CyclePotion(slot);
                for(int kind=0;kind<Camp.PotionKindCount;kind++)
                    if((input.PotionMask&Camp.PotionInputBit((PotionKind)kind))!=0
                        && Camp.ConsumePotion((PotionKind)kind,ActiveSim) && Mode==GameMode.Rift)
                        _alchemyLevelWithoutPotion=false;
            }
            switch (Mode)
            {
                case GameMode.Camp: StepCamp(in input); break;
                case GameMode.Rift: StepRift(in input); break;
                case GameMode.Summary: StepSummary((CampCommand)input.Command); break;
            }
        }

        // ---- лагерь ----

        private void StepCamp(in InputFrame input)
        {
            switch ((CampCommand)input.Command)
            {
                case CampCommand.EnterRift:
                    if (Camp.Has(CampService.RiftPortal)) EnterRift();
                    return;

                case CampCommand.SalvageJunk:
                    Camp.SalvageJunk();
                    break;

                case CampCommand.ToggleProvingGround:
                    if (OnProvingGround) LeaveProvingGround();
                    else EnterProvingGround();
                    return;
            }

            if (Ground != null) Ground.Step(in input);
            else
            {
                CampSim.Step(in input);
                Training?.AfterStep(CampSim);
            }

            // Опыт уходит в лагерь каждый тик: уровень живёт в Camp и потому
            // переживает и уход с Полигона, и пересборку лагерной симуляции.
            Simulation stepped = Ground != null ? Ground.Sim : CampSim;
            if (Camp.GainExperience(stepped.TakePendingXp()) > 0) SyncPlayerLevel();
        }

        /// <summary>
        /// Раздаёт уровень лагеря всем живым симуляциям. Зовётся при повышении
        /// посреди боя и после ручной смены уровня разработчиком: статы героя
        /// обязаны вырасти сразу, а не со следующей расстановки.
        /// </summary>
        public void SyncPlayerLevel()
        {
            CampSim.SetPlayerLevel(Camp.Level);
            Ground?.Sim.SetPlayerLevel(Camp.Level);
            Run?.Sim.SetPlayerLevel(Camp.Level);
        }

        /// <summary>
        /// Встать на Полигон. Манекен собирается заново каждый раз: счётчики
        /// прошлого билда не должны смешиваться с новым — ради сравнения
        /// Полигон и существует.
        /// </summary>
        public void EnterProvingGround(int dummyHealth = 100000)
        {
            // В авторском лагере тренировка уже находится рядом с игроком.
            if (Training != null) return;
            if (!Camp.Has(CampService.ProvingGround)) return;

            Ground = new ProvingGround();
            Ground.Sim.SetPlayerLevel(Camp.Level);
            Ground.Setup(dummyHealth, Fix64.Zero, Fix64.Zero);
            Camp.Worn.Bind(Ground.Sim.Entities.Stats[Simulation.PlayerId]);
            Ground.Sim.RefreshPlayerStats(true);

            Generation++;
        }

        /// <summary>
        /// Перенастроить манекен, не сходя с Полигона. Именно ради этого он
        /// и «с настраиваемым HP и сопротивлениями»: билд проверяется против
        /// разных целей, а не против одной удобной.
        /// </summary>
        public void RetuneDummy(int dummyHealth, Fix64 armor, Fix64 fireResist)
        {
            if (Ground == null) return;

            Ground.Setup(dummyHealth, armor, fireResist);
            Camp.Worn.Bind(Ground.Sim.Entities.Stats[Simulation.PlayerId]);
            Ground.Sim.RefreshPlayerStats(true);

            Generation++;
        }

        public void LeaveProvingGround()
        {
            if (Ground == null) return;

            Ground = null;
            BindCampEquipment();
            Generation++;
        }

        /// <summary>
        /// Немедленно вернуться в лагерь из системного меню. Активный забег
        /// считается покинутым: незавершённые награды не переносятся, экран
        /// итогов не создаётся. Это отдельное системное действие, а не команда
        /// боевого тика, поэтому меню может выполнить его даже на паузе.
        /// </summary>
        public void ReturnToCamp()
        {
            if (Mode == GameMode.Camp)
            {
                LeaveProvingGround();
                return;
            }

            Run = null;
            IsDeveloperRun = false;
            Ground = null;
            Mode = GameMode.Camp;
            _alchemyTrackedDepth=0;_alchemyLevelWithoutPotion=false;
            BindCampEquipment();
            Generation++;
        }

        // ---- Разлом ----

        /// <summary>
        /// Вход в Разлом. Новый сид, новая симуляция, надетое переезжает
        /// на нового персонажа.
        /// </summary>
        public void EnterRift()
        {
            LeaveProvingGround();

            ulong seed = LayoutGenerator.RollSeed(ref _runSeeds);
            BeginRift(_location, seed, 1, false, false);
        }

        public void StartDeveloperRift(LocationDefinition location, int level, bool nearBoss, ulong seed)
        {
            if (location == null || level < 1 || level > location.LevelCount || (nearBoss && !location.GetLevel(level).Boss))
                throw new System.ArgumentException("Choose a valid authored level (with a boss for a boss jump).");
            location.ValidateCapacity(_simCapacity);
            LeaveProvingGround();
            BeginRift(location, seed, level, nearBoss, true);
        }

        /// <summary>Изолированный боевой тест: штатные управление и урон, без переносимой добычи.</summary>
        public void StartForestBudTest(LocationDefinition location, ulong seed, int count = 1)
        {
            if (count < 1 || count > 40 || count >= _simCapacity)
                throw new System.ArgumentOutOfRangeException(nameof(count));
            location?.ValidateCapacity(_simCapacity);
            LeaveProvingGround();
            BeginRift(location, seed, 1, false, true, count);
        }

        /// <summary>Изолированный стенд алхимика: обычные правила заказов, но гарантированный Бутон.</summary>
        public void StartAlchemyBudTrial(LocationDefinition location, ulong seed)
        {
            if (location == null) throw new System.ArgumentNullException(nameof(location));
            location.ValidateCapacity(_simCapacity);
            LeaveProvingGround();
            BeginRift(location, seed, 1, false, false, 1);
        }

        private void BeginRift(LocationDefinition location, ulong seed, int level, bool nearBoss, bool developer,
            int forestBudCount = 0)
        {
            bool invulnerable = developer && DeveloperInvulnerable;
            LastRunSeed = seed;
            RunNumber++;
            IsDeveloperRun = developer;

            var sim = new Simulation(seed, _simCapacity);
            // Уровень ДО расстановки: ConfigurePlayer вешает его прибавки.
            sim.SetPlayerLevel(Camp.Level);

            // Привязка ДО StartRun: расстановка первого Разлома уже позовёт
            // Reapply, и снаряжению к этому моменту нужен лист.
            Camp.Worn.Bind(sim.Entities.Stats[Simulation.PlayerId]);

            Run = new RiftRun(sim, location?.Modules ?? _modules, Camp.Items, _itemBaseIds, location: location);
            Run.PlayerEquipment = Camp.Worn;
            Run.WhirlwindShowcase = !developer && WhirlwindShowcase;
            Run.CombatFeelShowcase = developer ? CombatFeelCaptureTier.None : CombatFeelShowcase;
            Run.CombatFeelEnemyCount = CombatFeelEnemyCount;
            Run.ForestBudShowcaseCount = forestBudCount;
            if (developer && location != null) Run.StartTestAtLevel(level, nearBoss);
            else Run.StartRun();
            sim.PlayerInvulnerable = invulnerable;
            if (developer || CarryCampLoadoutIntoRift)
            {
                Run.Loadout.CopyFrom(CampLoadout);
                Run.ApplyLoadout();
            }

            Mode = GameMode.Rift;
            _alchemyTrackedDepth=Run.Depth;
            _alchemyLevelWithoutPotion=!developer;
            Generation++;
        }

        private void StepRift(in InputFrame input)
        {
            int boss=Run.BossId;
            bool bossWasAlive=boss>=0 && Run.Sim.Entities.Alive[boss];
            RunPhase beforePhase=Run.Phase;
            Run.Step(in input);
            if(!IsDeveloperRun)
            {
                if(beforePhase==RunPhase.Clearing || beforePhase==RunPhase.SeekingExit)
                    RecordAlchemyDeaths(Run.Sim.Events,Run.Sim);
                if(_alchemyTrackedDepth==Run.Depth && _alchemyLevelWithoutPotion
                    && Run.Phase==RunPhase.ChoosingReward && beforePhase!=RunPhase.ChoosingReward)
                    Camp.CompleteAlchemyOrder(AlchemistOrder.Surge);
            }
            if(Run.Depth!=_alchemyTrackedDepth)
            {
                _alchemyTrackedDepth=Run.Depth;
                _alchemyLevelWithoutPotion=!IsDeveloperRun;
            }
            if(!IsDeveloperRun && bossWasAlive && Run.BossId==boss && !Run.Sim.Entities.Alive[boss] && Run.Sim.Entities.Alive[Simulation.PlayerId])Camp.RefreshTraderAfterBoss();

            // Опыт забега уходит в лагерь сразу, а не на экране итогов: смерть
            // не должна отнимать уровень. Разработческий забег опыта не даёт —
            // по тому же правилу, по которому его добыча не переезжает в сумку.
            int xp = Run.Sim.TakePendingXp();
            if (!IsDeveloperRun && Camp.GainExperience(xp) > 0) SyncPlayerLevel();

            if (Run.Phase == RunPhase.Ended) FinishRun();
        }

        internal void RecordAlchemyDeaths(System.Collections.Generic.IReadOnlyList<SimEvent> events, Simulation sim)
        {
            if(IsDeveloperRun || Camp.AlchemyStatus(AlchemistOrder.Resin)!=AlchemistOrderStatus.Accepted)return;
            foreach(var e in events)
                if(e.Type==SimEventType.Death && e.Source==Simulation.PlayerId && e.Target>0
                    && e.Target<sim.Entities.Count && sim.Entities.Kind[e.Target]==EnemyKind.ForestBud)
                    Camp.CompleteAlchemyOrder(AlchemistOrder.Resin);
        }

        /// <summary>
        /// Забег кончился — найденное переезжает в лагерь.
        ///
        /// СМЕРТЬ ОТНИМАЕТ ВСЁ. Решение владельца от 15 сентября: вещи и золото
        /// доезжают до лагеря только при выходе или прохождении, поэтому выбор
        /// «идти глубже или уйти с добычей» и есть главное решение Разлома.
        /// Уровень и опыт смерть не трогает — они уходят в лагерь сразу.
        /// Не влезшее в сумку теряется — и это тоже решение, принятое до входа.
        /// </summary>
        private void FinishRun()
        {
            bool keeps = !IsDeveloperRun && Run.Outcome != RunOutcome.Died;
            int kept = 0, lost = 0, behind = 0;

            for (int i = 0; !IsDeveloperRun && i < Run.TakenRewardCount; i++)
            {
                RewardOffer offer = Run.GetTaken(i);
                if (offer.Kind != RewardKind.Item) continue;

                if (!keeps) behind++;
                else if (Camp.Bag.Add(offer.Item) >= 0) kept++;
                else lost++;
            }

            int gold = IsDeveloperRun ? 0 : Run.Gold;
            if (keeps) Camp.Earn(CurrencyType.Gold, gold);

            LastRun = new RunSummary(Run.Outcome, Run.Depth, Run.RiftsCleared, kept, lost,
                keeps ? gold : 0, behind, keeps ? 0 : gold);
            Mode = GameMode.Summary;
        }

        // ---- экран итогов ----

        /// <summary>
        /// Экран выхода. Отсюда ровно два пути: повторить одним нажатием
        /// или вернуться в лагерь.
        ///
        /// Кнопка «повторить» обязана существовать, иначе лагерь становится
        /// принудительным коридором. Значит, лагерь конкурирует с ней за
        /// внимание и должен выигрывать честно — тем, что в нём есть дело,
        /// а не тем, что мимо него не пройти.
        /// </summary>
        private void StepSummary(CampCommand command)
        {
            switch (command)
            {
                case CampCommand.RepeatRift:
                    EnterRift();
                    break;

                case CampCommand.ReturnToCamp:
                    ReturnToCamp();
                    break;
            }
        }

        /// <summary>
        /// Зачем возвращаться в лагерь. Показывается прямо на экране итогов,
        /// рядом с кнопкой «повторить».
        ///
        /// Сейчас в списке только то, что реально существует: новые предметы,
        /// которые стоит проверить на Полигоне, и мусор под разбор. Незакрытая
        /// ячейка Летописи, готовый к перековке предмет и невзятый заказ
        /// добавятся сюда вместе со своими механиками.
        /// </summary>
        public int NewItemsToTry => LastRun.ItemsKept;
        public int JunkToSalvage => Camp.Bag.UnkeptCount;

        public ulong Hash()
        {
            ulong hash = Hashing.Offset;
            Hashing.Mix(ref hash, (int)Mode);
            Hashing.Mix(ref hash, RunNumber);
            if (IsDeveloperRun) Hashing.Mix(ref hash, 0x444556);
            Hashing.Mix(ref hash, LastRunSeed);
            Hashing.Mix(ref hash, _alchemyTrackedDepth);
            Hashing.Mix(ref hash, _alchemyLevelWithoutPotion ? 1 : 0);
            LastRun.HashInto(ref hash);

            Camp.HashInto(ref hash);
            if (Run != null) Hashing.Mix(ref hash, Run.Hash());
            return hash;
        }
    }
}
