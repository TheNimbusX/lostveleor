namespace Game.Sim
{
    /// <summary>
    /// Итоги забега — то, что показывает экран выхода.
    ///
    /// В сумку уезжают ТОЛЬКО предметы. Прибавки к статам и узлы, взятые
    /// в награду, живут внутри забега и с ним же кончаются: постоянная
    /// прогрессия — это Созвездия и дерево способностей, и дублировать их
    /// наградами Разлома значило бы завести вторую шкалу силы, которую потом
    /// нечем балансировать.
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

        public RunSummary(RunOutcome outcome, int depth, int riftsCleared, int itemsKept, int itemsLost)
        {
            Outcome = outcome;
            Depth = depth;
            RiftsCleared = riftsCleared;
            ItemsKept = itemsKept;
            ItemsLost = itemsLost;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, (int)Outcome);
            Hashing.Mix(ref hash, Depth);
            Hashing.Mix(ref hash, RiftsCleared);
            Hashing.Mix(ref hash, ItemsKept);
            Hashing.Mix(ref hash, ItemsLost);
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

        /// <summary>
        /// Что сейчас рисовать. Меняется вместе с Generation — представление
        /// обязано сравнивать поколение со своим и пересобирать привязки:
        /// у новой симуляции индексы сущностей начинаются заново.
        /// </summary>
        public Simulation ActiveSim
            => Mode == GameMode.Camp
                ? (Ground != null ? Ground.Sim : null)
                : (Run != null ? Run.Sim : null);

        /// <summary>Растёт при каждой смене активной симуляции.</summary>
        public int Generation { get; private set; }

        public ulong LastRunSeed { get; private set; }
        public int RunNumber { get; private set; }
        public RunSummary LastRun { get; private set; }

        public GameSession(ulong sessionSeed, Camp camp, ModuleSet modules, int[] itemBaseIds,
            int simCapacity = 512)
        {
            Camp = camp;
            _modules = modules;
            _itemBaseIds = itemBaseIds;
            _simCapacity = simCapacity;

            // Свой поток сидов, независимый от боевых: он не должен сдвигаться
            // от того, сколько раз в забеге бросили на крит.
            _runSeeds = new Pcg32(sessionSeed, 0x853C49E6748FEA9BUL);

            Mode = GameMode.Camp;
        }

        /// <summary>
        /// Один шаг игры. Что именно шагает, решает режим: в Разломе — забег,
        /// в лагере — Полигон, если игрок на нём стоит, и ничего, если не стоит.
        /// На экране итогов не шагает ничто: он для того и нужен, чтобы игрок
        /// мог подумать, не теряя здоровья.
        /// </summary>
        public void Step(in InputFrame input)
        {
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
        }

        /// <summary>
        /// Встать на Полигон. Манекен собирается заново каждый раз: счётчики
        /// прошлого билда не должны смешиваться с новым — ради сравнения
        /// Полигон и существует.
        /// </summary>
        public void EnterProvingGround(int dummyHealth = 100000)
        {
            if (!Camp.Has(CampService.ProvingGround)) return;

            Ground = new ProvingGround();
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
            LastRunSeed = seed;
            RunNumber++;

            var sim = new Simulation(seed, _simCapacity);

            // Привязка ДО StartRun: расстановка первого Разлома уже позовёт
            // Reapply, и снаряжению к этому моменту нужен лист.
            Camp.Worn.Bind(sim.Entities.Stats[Simulation.PlayerId]);

            Run = new RiftRun(sim, _modules, Camp.Items, _itemBaseIds);
            Run.PlayerEquipment = Camp.Worn;
            Run.StartRun();

            Mode = GameMode.Rift;
            Generation++;
        }

        private void StepRift(in InputFrame input)
        {
            Run.Step(in input);
            if (Run.Phase == RunPhase.Ended) FinishRun();
        }

        /// <summary>
        /// Забег кончился — добытое переезжает в сумку.
        ///
        /// Смерть добытого не отнимает: без этого выбор «идти глубже или уйти»
        /// превратился бы в «уйти сразу», а он и есть главное решение Разлома.
        /// Не влезшее в сумку теряется — и это тоже решение, принятое до входа.
        /// </summary>
        private void FinishRun()
        {
            int kept = 0;
            int lost = 0;

            for (int i = 0; i < Run.TakenRewardCount; i++)
            {
                RewardOffer offer = Run.GetTaken(i);
                if (offer.Kind != RewardKind.Item) continue;

                if (Camp.Bag.Add(offer.Item) >= 0) kept++;
                else lost++;
            }

            LastRun = new RunSummary(Run.Outcome, Run.Depth, Run.RiftsCleared, kept, lost);
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
                    Run = null;
                    Mode = GameMode.Camp;
                    Generation++;
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
            Hashing.Mix(ref hash, LastRunSeed);
            LastRun.HashInto(ref hash);

            Camp.HashInto(ref hash);
            if (Run != null) Hashing.Mix(ref hash, Run.Hash());
            return hash;
        }
    }
}
