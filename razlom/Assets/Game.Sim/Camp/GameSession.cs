namespace Game.Sim
{
    /// <summary>
    /// Итоги забега — то, что показывает экран выхода.
    ///
    /// Экономика 06.10: вещи доезжают при выходе или прохождении, золото — целиком,
    /// при смерти — половина. Сталь, пепел и сердце уже лежат в лагере: они
    /// начисляются сразу, как опыт (GameSession.RunHaul), и здесь только
    /// пересказаны. Способности и таланты живут внутри забега.
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

        /// <summary>Сталь забега: элитные встречи и босс. Уже в лагере при любом исходе.</summary>
        public readonly int SteelKept;

        /// <summary>Пепел забега: 1 / 5 / 20 за обычного, элиту, босса. Уже в лагере при любом исходе.</summary>
        public readonly int AshKept;

        /// <summary>Сердца боссов за забег. Уже в лагере при любом исходе.</summary>
        public readonly int HeartsKept;

        private readonly RunStats _stats;

        /// <summary>
        /// Статистика забега для экрана итогов: время, убийства, урон, лучший
        /// удар, зелья, уровни. Заморожена концом забега; до первого забега —
        /// RunStats.Empty, не null.
        /// </summary>
        public RunStats Stats => _stats ?? RunStats.Empty;

        public RunSummary(RunOutcome outcome, int depth, int riftsCleared, int itemsKept, int itemsLost,
            int goldKept = 0, int itemsLeftBehind = 0, int goldLeftBehind = 0, RunStats stats = null,
            int steelKept = 0, int ashKept = 0, int heartsKept = 0)
        {
            Outcome = outcome;
            Depth = depth;
            RiftsCleared = riftsCleared;
            ItemsKept = itemsKept;
            ItemsLost = itemsLost;
            GoldKept = goldKept;
            ItemsLeftBehind = itemsLeftBehind;
            GoldLeftBehind = goldLeftBehind;
            SteelKept = steelKept;
            AshKept = ashKept;
            HeartsKept = heartsKept;
            _stats = stats;
        }

        /// <summary>
        /// Статистики в хеше нет намеренно: она не решает ничего в бою и в
        /// лагере, а только рассказывает забег (RunStats).
        /// </summary>
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
            // Ресурсы лагеря — только если есть: итоги без них хешируются как до 06.10.
            if (SteelKept != 0 || AshKept != 0 || HeartsKept != 0)
            {
                Hashing.Mix(ref hash, 0x48415553);
                Hashing.Mix(ref hash, SteelKept);
                Hashing.Mix(ref hash, AshKept);
                Hashing.Mix(ref hash, HeartsKept);
            }
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
    public sealed partial class GameSession
    {
        private readonly ModuleSet _modules;
        private readonly LocationDefinition _location;
        private readonly int[] _itemBaseIds;
        private readonly int _simCapacity;

        private Pcg32 _runSeeds;

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
        /// Набор способностей в лагере и на Полигоне: выбранное у стола
        /// стартовое умение. Меню разработчика и съёмки меняют его для проверки.
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

        private RunStats _runStats;

        /// <summary>
        /// Статистика идущего забега — живые числа для HUD. После конца забега
        /// это она же в LastRun.Stats, уже замороженная; до первого забега —
        /// RunStats.Empty. Сессия считает её сама и в хеш не кладёт.
        /// </summary>
        public RunStats CurrentRunStats => _runStats ?? RunStats.Empty;

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

        /// <summary>
        /// Съёмка (capture.ps1): забег идёт эталонным героем 270/54 при любом профиле. Съёмка
        /// грузит новый лагерь, а он с 06.10 даёт 200/40 — без флага менялись бы темп боя на
        /// кадрах и время убийств, и записи «до/после» и замеры по эталону стали бы несравнимы.
        /// </summary>
        public bool CaptureReferenceHero { get; set; }

        /// <summary>Тестовый забег, съёмка и стенды меряют бой — им эталон 270/54 (Camp.HeroBaselineFor).</summary>
        private bool RunUsesReferenceHero(bool developer)
            => developer || CaptureReferenceHero || WhirlwindShowcase
            || CombatFeelShowcase != CombatFeelCaptureTier.None;

        public GameSession(ulong sessionSeed, Camp camp, ModuleSet modules, int[] itemBaseIds,
            int simCapacity = 512, LocationDefinition location = null)
        {
            Camp = camp;
            Camp.InitializePreparationSeed(sessionSeed);
            CampLoadout.ResetToStarter(Camp.PreparedStarterPoolIndex);
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
            // Уровень статов не даёт (29 сентября). База героя — от лагеря (06.10): новая игра
            // 200/40, Sandbox и тестовые забеги — эталон 270/54 (Camp.HeroBaselineFor).
            CampSim.ApplyHeroBaseline(Camp.HeroBaselineFor(false));
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
            => ConfigureCampTraining(definitions, FixVec2.Zero, Fix64.Zero);

        /// <summary>
        /// Манекены лагеря и зона полигона вокруг них: только там герой бьёт и колдует
        /// (владелец, 29 сентября). Зону меряет представление по забору полигона; нулевой
        /// радиус — зона по самим манекенам (<see cref="CampTraining.FallbackZoneMargin"/>).
        /// </summary>
        public void ConfigureCampTraining(CampDummyDefinition[] definitions, FixVec2 zoneCenter, Fix64 zoneRadius)
        {
            if (definitions == null || definitions.Length >= _simCapacity)
                throw new System.ArgumentException("Число мишеней превышает вместимость лагеря.");
            Ground = null;
            Training = new CampTraining(definitions, zoneCenter, zoneRadius);
            BindCampEquipment();
            Generation++;
        }

        /// <summary>
        /// Можно ли сейчас бить и колдовать: в Разломе и на старом Полигоне — всегда, в лагере
        /// с манекенами — только в зоне полигона. Кувырок доступен по всему лагерю. По этому
        /// же флагу HUD лагеря прячет и возвращает свою боевую часть.
        /// </summary>
        public bool CampCombatAllowed => Mode != GameMode.Camp || Ground != null || Training == null
            || Training.InZone(CampSim.Entities.Position[Simulation.PlayerId]);

        private void BindCampEquipment()
        {
            CampSim.ResetCampActivity();
            Camp.Worn.Bind(CampSim.Entities.Stats[Simulation.PlayerId]);
            CampSim.RefreshPlayerStats(true);
            CampSim.StopPlayerMovement();
            Training?.Populate(CampSim);
            // Статовые клятвы героя лагеря — и после загрузки сохранения, а не только после покупки.
            // Без клятв SetBoons выходит сразу, лист героя прежний.
            RefreshCampOaths();
        }

        /// <summary>
        /// Один шаг игры. Что именно шагает, решает режим: в Разломе — забег,
        /// в лагере — Полигон, если игрок на нём стоит, и ничего, если не стоит.
        /// На экране итогов не шагает ничто: он для того и нужен, чтобы игрок
        /// мог подумать, не теряя здоровья.
        /// </summary>
        public void Step(in InputFrame input)
        {
            HandlePotionInput(in input);
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
                    RequestRiftEntry();
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
                // Вне полигона удар и способности снимаются, кувырок проходит, ЛКМ ведёт героя.
                // Решает позиция до шага: та же, что видел игрок, когда нажимал.
                InputFrame gated = input;
                Training?.GateInput(ref gated, CampSim.Entities.Position[Simulation.PlayerId]);
                CampSim.Step(in gated);
                Training?.AfterStep(CampSim);
            }

            // Опыт уходит в лагерь каждый тик: уровень живёт в Camp и потому
            // переживает и уход с Полигона, и пересборку лагерной симуляции.
            // Симуляциям повышение ничего не несёт: статов уровень не даёт.
            Simulation stepped = Ground != null ? Ground.Sim : CampSim;
            Camp.GainExperience(stepped.TakePendingXp());
            if (_campPotionCooldownTicksLeft > 0) _campPotionCooldownTicksLeft--;
        }

        /// <summary>
        /// Подтверждает всем живым симуляциям базу героя (Camp.HeroBaselineFor). До 29
        /// сентября раздавал уровень лагеря и статы героя; теперь уровень
        /// статов не даёт (Progression), и вызов после ручной смены уровня
        /// разработчиком или в стенде баланса ничего не меняет — база уже стоит
        /// с создания симуляции, повтор той же базы её не трогает. Оставлен ради этих
        /// вызовов: уровень в меню разработчика должен оставаться безопасным.
        /// </summary>
        public void SyncPlayerLevel()
        {
            CampSim.ApplyHeroBaseline(Camp.HeroBaselineFor(false));
            Ground?.Sim.ApplyHeroBaseline(Camp.HeroBaselineFor(false));
            Run?.Sim.ApplyHeroBaseline(Camp.HeroBaselineFor(RunUsesReferenceHero(IsDeveloperRun)));
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
            Ground.Sim.ApplyHeroBaseline(Camp.HeroBaselineFor(false));
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
        /// Немедленно вернуться в лагерь — системное действие F8 и тестов (из паузы
        /// игрока его больше нет, 06.10). Активный забег считается покинутым: золото
        /// и вещи не переносятся, экран итогов не создаётся; уже начисленные пепел,
        /// сталь, сердца и опыт остаются в лагере. Это не команда боевого тика,
        /// поэтому меню может выполнить его даже на паузе.
        /// </summary>
        public void ReturnToCamp()
        {
            PreparationRequested = false;
            if (Mode == GameMode.Camp)
            {
                LeaveProvingGround();
                return;
            }

            Run = null;
            ResetRunProgressTracking();
            IsDeveloperRun = false;
            Ground = null;
            Mode = GameMode.Camp;
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
            PreparationRequested = false;
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

        public void StartWendigoTest(LocationDefinition location, ulong seed, bool withPack = false)
        {
            location?.ValidateCapacity(_simCapacity);
            LeaveProvingGround();
            BeginRift(location, seed, 1, false, true, wendigoShowcase: withPack ? 2 : 1);
        }

        public void StartStonehoofTest(LocationDefinition location, ulong seed, int count = 1, bool obstacle = false)
        {
            if (count < 1 || count > 3) throw new System.ArgumentOutOfRangeException(nameof(count));
            location?.ValidateCapacity(_simCapacity); LeaveProvingGround();
            BeginRift(location, seed, 1, false, true, stonehoofCount: count, stonehoofObstacle: obstacle);
        }

        private void BeginRift(LocationDefinition location, ulong seed, int level, bool nearBoss, bool developer,
            int forestBudCount = 0, int wendigoShowcase = 0, int stonehoofCount = 0, bool stonehoofObstacle = false)
        {
            PreparationRequested = false;
            // Закалка «Ещё удар?» закрывается при входе в Разлом с тем, что набрано (план T1);
            // оплаченные переплавка и добавление ждут выбора и остаются.
            Camp.SettleForgeSession();
            bool invulnerable = developer && DeveloperInvulnerable;
            LastRunSeed = seed;
            RunNumber++;
            IsDeveloperRun = developer;
            ResetRunProgressTracking();
            BeginRunStats();

            var sim = new Simulation(seed, _simCapacity);
            // База героя ДО расстановки: ConfigurePlayer вешает её прибавки. Тестовый
            // забег, съёмка и стенды — эталон 270/54 при любом профиле: на нём меряют бой (06.10).
            sim.ApplyHeroBaseline(Camp.HeroBaselineFor(RunUsesReferenceHero(developer)));

            // Привязка ДО StartRun: расстановка первого Разлома уже позовёт
            // Reapply, и снаряжению к этому моменту нужен лист.
            Camp.Worn.Bind(sim.Entities.Stats[Simulation.PlayerId]);

            Run = new RiftRun(sim, location?.Modules ?? _modules, Camp.Items, _itemBaseIds, location: location);
            Run.PlayerEquipment = Camp.Worn;
            if (!developer) { var preparation = Camp.CreateRunPreparation(); Run.SetPreparation(in preparation); }
            // Клятвы и грани сердца — только в настоящем забеге: тестовый идёт эталонным героем без них (M6).
            if (!developer) Run.SetBoons(Camp.CreateRunBoons());
            Run.WhirlwindShowcase = !developer && WhirlwindShowcase;
            Run.CombatFeelShowcase = developer ? CombatFeelCaptureTier.None : CombatFeelShowcase;
            Run.CombatFeelEnemyCount = CombatFeelEnemyCount;
            Run.ForestBudShowcaseCount = forestBudCount;
            Run.WendigoShowcase = wendigoShowcase;
            Run.StonehoofShowcase = stonehoofCount; Run.StonehoofTestObstacle = stonehoofObstacle;
            if (developer && location != null) Run.StartTestAtLevel(level, nearBoss);
            else Run.StartRun();
            sim.PlayerInvulnerable = invulnerable;
            if (developer || CarryCampLoadoutIntoRift)
            {
                Run.Loadout.CopyFrom(CampLoadout);
                Run.ApplyLoadout();
            }
            // Стартовый навык — уже «когда-либо взятый» (GameSession.RunHaul).
            TrackRunProgress();

            Mode = GameMode.Rift;
            Generation++;
        }

        private void StepRift(in InputFrame input)
        {
            int boss=Run.BossId;
            bool bossWasAlive=boss>=0 && Run.Sim.Entities.Alive[boss];
            RunPhase beforePhase=Run.Phase;
            int depthBefore = Run.Depth;
            int tickBefore = Run.Sim.Tick;
            Run.Step(in input);
            // Навыки и артефакты, взятые этим шагом, — сразу в лагерь (06.10).
            TrackRunProgress();
            bool simStepped = (beforePhase == RunPhase.Clearing || beforePhase == RunPhase.SeekingExit)
                && Run.Depth == depthBefore && Run.Sim.Tick != tickBefore;
            RecordRunStats(simStepped, boss);
            if (simStepped) { Run.AdvancePotionCooldown(); RecordMaterialDeaths(); }
            if(!IsDeveloperRun && bossWasAlive && Run.BossId==boss && !Run.Sim.Entities.Alive[boss] && Run.Sim.Entities.Alive[Simulation.PlayerId])Camp.RefreshTraderAfterBoss();

            // Опыт забега уходит в лагерь сразу, а не на экране итогов: смерть
            // не должна отнимать уровень. Разработческий забег опыта не даёт —
            // по тому же правилу, по которому его добыча не переезжает в сумку.
            // Повышение посреди боя статов и лечения не даёт (владелец, 29 сентября).
            int xp = Run.Sim.TakePendingXp();
            if (!IsDeveloperRun)
            {
                int levels = Camp.GainExperience(xp);
                _runStats?.CountExperience(xp, levels);
            }

            if (Run.Phase == RunPhase.Ended) FinishRun();
        }

        // ---- статистика забега ----

        /// <summary>
        /// Новый забег — новая статистика с нуля и снимок уровня лагеря на входе.
        /// Прежний объект остаётся в LastRun.Stats нетронутым.
        /// </summary>
        private void BeginRunStats() => _runStats = new RunStats(Camp.Level, Camp.Experience);

        /// <summary>
        /// Один шаг сессии в Разломе. События читаются только в тик, когда бой
        /// шагнул: на экранах награды, замены и пути симуляция стоит, а её
        /// список событий хранит прошлый тик — второй проход посчитал бы его
        /// дважды. Бой статистика не трогает: только читает события.
        /// </summary>
        private void RecordRunStats(bool simStepped, int bossId)
        {
            if (_runStats == null) return;
            _runStats.CountStep(simStepped);
            if (simStepped) _runStats.Record(Run.Sim.Events, Run.Sim, bossId);
        }

        /// <summary>
        /// Забег кончился — найденное переезжает в лагерь.
        ///
        /// Экономика 06.10: при выходе или прохождении доезжают вещи и всё золото,
        /// при смерти вещи теряются, а золота доезжает половина (DeathGoldPercent,
        /// округление вниз от суммы). Пепел, сталь, сердца, победы над боссами,
        /// навыки и артефакты сюда не ждут: они ушли в лагерь в момент находки,
        /// как опыт (GameSession.RunHaul), — выход из игры посреди забега их не
        /// отнимает. Не влезшее в сумку теряется — решение, принятое до входа.
        /// </summary>
        private void FinishRun()
        {
            bool real = !IsDeveloperRun, died = Run.Outcome == RunOutcome.Died, keeps = real && !died;
            int kept = 0, lost = 0, behind = 0;

            for (int i = 0; real && i < Run.TakenRewardCount; i++)
            {
                RewardOffer offer = Run.GetTaken(i);
                if (offer.Kind != RewardKind.Item) continue;

                if (!keeps) behind++;
                else if (Camp.Bag.Add(offer.Item) >= 0) kept++;
                else lost++;
            }

            int found = real ? Run.Gold : 0;
            int goldKept = died ? RunEconomy.Percent(found, DeathGoldPercent) : found;
            Camp.Earn(CurrencyType.Gold, goldKept);

            _runStats?.Finish(Camp.Level, Camp.Experience);
            LastRun = new RunSummary(Run.Outcome, Run.Depth, Run.RiftsCleared, kept, lost, goldKept, behind,
                found - goldKept, _runStats, steelKept: _runSteel, ashKept: _runAsh, heartsKept: _runHearts);
            CompleteRealAttempt(kept);
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
                    RequestRiftEntry();
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
            LastRun.HashInto(ref hash);

            Camp.HashInto(ref hash);
            HashSessionPreparation(ref hash);
            if (Run != null) Hashing.Mix(ref hash, Run.Hash());
            return hash;
        }
    }
}
