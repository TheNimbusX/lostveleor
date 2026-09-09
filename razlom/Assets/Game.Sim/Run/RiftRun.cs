namespace Game.Sim
{
    /// <summary>
    /// Петля забега: вход в Разлом → зачистка → смерть или выход →
    /// выбор одной награды из трёх → следующий Разлом глубже.
    ///
    /// СМЕРТЬ ЗАВЕРШАЕТ ЗАБЕГ. Награды за пройденное остаются, глубже в этот
    /// раз не пойдёшь. Это не наказание, а то, что делает решение «идти дальше
    /// или уйти с добычей» настоящим решением: без риска потерять следующий
    /// уровень выбор был бы всегда «идти дальше».
    ///
    /// Всё, что решает игрок, приходит в InputFrame.Command. Отдельного API
    /// для выбора награды нет намеренно: реплей обязан воспроизводить забег
    /// целиком, включая нажатия на экране награды.
    /// </summary>
    public sealed class RiftRun
    {
        /// <summary>Сколько предложений на экране награды. Ровно три, см. бриф.</summary>
        public const int RewardChoices = 3;

        /// <summary>Потолок собранных наград за забег.</summary>
        private const int MaxTakenRewards = 64;

        private readonly LocationDefinition _location;

        private readonly Simulation _sim;
        private readonly ModuleSet _modules;
        private readonly LayoutMap _map;
        private readonly LayoutGenerator _generator = new LayoutGenerator();

        private readonly RewardOffer[] _offers = new RewardOffer[RewardChoices];
        private readonly RewardOffer[] _taken = new RewardOffer[MaxTakenRewards];
        private int _takenCount;
        private readonly int[] _enemyBranch;
        private readonly bool[] _branchClaimed = new bool[8];
        public int BranchesClaimed { get; private set; }
        public bool IsBranchClaimed(int branch) => _branchClaimed[branch];

        public int CountRequiredEnemies()
        {
            int count = 0;
            for (int i = 0; i < _sim.Entities.Count; i++)
                if (_sim.Entities.Alive[i] && _sim.Entities.Side[i] != Faction.Wole && _enemyBranch[i] < 0) count++;
            return count;
        }

        public int BranchGuardsAlive(int branch)
        {
            int count = 0;
            for (int i = 0; i < _sim.Entities.Count; i++)
                if (_sim.Entities.Alive[i] && _enemyBranch[i] == branch) count++;
            return count;
        }

        private readonly ItemDatabase _items;
        private readonly int[] _itemBaseIds;

        /// <summary>Capture-only расстановка combat slice; false — обычный Разлом.</summary>
        public bool WhirlwindShowcase { get; set; }

        public CombatFeelCaptureTier CombatFeelShowcase { get; set; }
        public int CombatFeelEnemyCount { get; set; } = 1;

        public Simulation Sim => _sim;
        public LayoutMap Map => _map;
        public ulong LayoutSeed { get; private set; }
        public ulong SpawnSeed { get; private set; }
        public RiftLevelSettings LevelSettings { get; private set; }
        public EncounterPlan Encounters { get; private set; }

        /// <summary>Справочник предметов: нужен, чтобы развернуть предложенный рецепт в числа.</summary>
        public ItemDatabase Items => _items;

        /// <summary>
        /// Снаряжение игрока. Свойство, а не параметр конструктора: забег может
        /// идти и на голом персонаже — так его гоняют тесты, — а лагерь, где
        /// снаряжение меняют, ещё не написан.
        ///
        /// Лист статов, на который оно повешено, обязан быть листом слота игрока:
        /// вход в следующий Разлом рождает игрока заново и лист сбрасывает.
        /// </summary>
        public Equipment PlayerEquipment { get; set; }

        public RunPhase Phase { get; private set; }
        public RunOutcome Outcome { get; private set; }

        /// <summary>Глубина: номер Разлома в этом забеге, с единицы.</summary>
        public int Depth { get; private set; }
        public int TotalLevels => _location != null && _location.CompleteAtEnd ? _location.LevelCount : 0;
        public bool IsFinalLevel => TotalLevels > 0 && Depth == TotalLevels;
        public int BossId => Encounters?.BossId ?? -1;
        public bool BossEnraged { get; private set; }

        public int RiftsCleared { get; private set; }
        public int TakenRewardCount => _takenCount;
        public RewardOffer GetTaken(int index) => _taken[index];
        public RewardOffer GetOffer(int index) => _offers[index];

        public RiftRun(Simulation sim, ModuleSet modules, ItemDatabase items, int[] itemBaseIds,
            int maxModules = 64, LocationDefinition location = null)
        {
            _sim = sim;
            _enemyBranch = new int[sim.Entities.Capacity];
            _location = location;
            _location?.ValidateCapacity(sim.Entities.Capacity);
            _modules = location?.Modules ?? modules;
            _items = items;
            _itemBaseIds = itemBaseIds;
            _map = new LayoutMap(_modules, location?.MaxModules ?? maxModules);

            Phase = RunPhase.Idle;
        }

        /// <summary>Начинает забег заново. Тем же сидом — тот же забег.</summary>
        public void StartRun()
        {
            Depth = 0;
            RiftsCleared = 0;
            _takenCount = 0;
            Outcome = RunOutcome.None;

            EnterNextRift();
        }

        /// <summary>Fresh test run: allocate skipped level seeds, without kills or rewards.</summary>
        public void StartTestAtLevel(int level, bool nearBoss)
        {
            if (Phase != RunPhase.Idle || _location == null || level < 1 || level > _location.LevelCount)
                throw new System.ArgumentException("Testing requires a fresh run and an authored level.");
            if (nearBoss && !_location.GetLevel(level).Boss)
                throw new System.ArgumentException("This level has no boss.");
            for (int i = 1; i < level; i++)
            {
                LayoutGenerator.RollSeed(ref _sim.Rng.Layout);
                LayoutGenerator.RollSeed(ref _sim.Rng.Spawns);
            }
            Depth = level - 1;
            EnterNextRift();
            if (nearBoss) PlaceNearBoss();
        }

        private void PlaceNearBoss()
        {
            if (BossId < 0 || _map.Routes == null) throw new System.InvalidOperationException("Boss did not spawn.");
            var entities = _sim.Entities;
            var boss = entities.Position[BossId];
            var best = Fix64.MaxValue;
            var point = _map.EntryPoint;
            for (int c = 0; c < _map.Routes.CellCount; c++)
            {
                if (_map.Routes.GetCell(c).Module != Encounters.Get(Encounters.ForEntity(BossId)).Module) continue;
                var candidate = _map.Routes.GetCell(c).Center;
                var distance = FixVec2.DistanceSq(candidate, boss);
                if (distance < Fix64.FromInt(16) || distance >= best ||
                    !_map.IsWalkable(candidate, entities.BodyRadius[Simulation.PlayerId])) continue;
                bool free = true;
                for (int i = 1; i < entities.Count; i++)
                {
                    var clearance = entities.BodyRadius[i] + entities.BodyRadius[Simulation.PlayerId] + Fix64.One;
                    if (entities.Alive[i] && FixVec2.DistanceSq(candidate, entities.Position[i]) < clearance * clearance)
                    { free = false; break; }
                }
                if (free) { point = candidate; best = distance; }
            }
            if (best == Fix64.MaxValue) throw new System.InvalidOperationException("No free approach to the boss.");
            entities.Position[Simulation.PlayerId] = point;
            entities.Facing[Simulation.PlayerId] = (boss - point).Normalized();
            _sim.StopPlayerMovement();
            _sim.Grid.Rebuild(entities);
        }

        /// <summary>
        /// Вход в следующий Разлом. Тир растёт с глубиной: комнат больше,
        /// врагов больше, здоровья у них больше.
        /// </summary>
        private void EnterNextRift()
        {
            Depth++;
            BossEnraged = false;

            LevelSettings = _location?.GetLevel(Depth) ?? RiftLevelSettings.Prototype(Depth);
            LayoutSeed = LayoutGenerator.RollSeed(ref _sim.Rng.Layout);
            LevelSettings.Generate(_generator, _modules, _map, LayoutSeed);

            SpawnSeed = LayoutGenerator.RollSeed(ref _sim.Rng.Spawns);
            Encounters = null;
            if (CombatFeelShowcase != CombatFeelCaptureTier.None)
                _sim.SetupCombatFeelShowcase(_map, CombatFeelEnemyCount, CombatFeelShowcase);
            else if (WhirlwindShowcase)
                _sim.SetupWhirlwindShowcase(_map);
            else if (_location == null)
                _sim.SetupForestEncounter(_map, SpawnSeed, LevelSettings.EnemyHealth);
            else
                Encounters = LevelSettings.Spawn(_sim, _map, SpawnSeed);

            System.Array.Clear(_branchClaimed, 0, _branchClaimed.Length);
            BranchesClaimed = 0;
            for (int i = 0; i < _sim.Entities.Count; i++)
            {
                _enemyBranch[i] = -1;
                if (_sim.Entities.Side[i] == Faction.Wole) continue;
                if (Encounters != null)
                {
                    int encounter = Encounters.ForEntity(i);
                    if (encounter >= 0) _enemyBranch[i] = Encounters.Get(encounter).Branch;
                    continue;
                }
                for (int b = 0; b < _map.RewardBranchCount; b++)
                    if (_map.ContainsWorld(_map.GetRewardBranch(b), _sim.Entities.Position[i]))
                    { _enemyBranch[i] = b; break; }
            }

            // Расстановка родила игрока заново, а рождение сбрасывает лист статов
            // целиком: индекс — это identity, и лист принадлежит слоту, а не
            // персонажу. Значит всё нажитое вешается обратно здесь — и здесь же
            // задан порядок, в котором это происходит.
            PlayerEquipment?.Reapply();
            ApplyStatRewards(_sim.Entities.Stats[Simulation.PlayerId]);
            _sim.RefreshPlayerStats(heal: true);

            Phase = RunPhase.Clearing;
        }

        /// <summary>
        /// Один шаг забега. Симуляция шагает только в фазе зачистки: на экране
        /// награды бой стоит, иначе игрок терял бы здоровье, пока читает.
        /// </summary>
        public void Step(in InputFrame input)
        {
            var command = (RunCommand)input.Command;

            switch (Phase)
            {
                case RunPhase.Clearing:
                    StepClearing(in input, command);
                    break;

                case RunPhase.SeekingExit:
                    StepSeekingExit(in input, command);
                    break;

                case RunPhase.ChoosingReward:
                    StepChoosing(command);
                    break;
            }
        }

        private void StepClearing(in InputFrame input, RunCommand command)
        {
            if (command == RunCommand.Leave)
            {
                End(RunOutcome.Left);
                return;
            }

            if (BossId >= 0 && !BossEnraged && _sim.Entities.Alive[BossId]
                && _sim.Entities.Health[BossId] <= _sim.Entities.MaxHealth[BossId] / 2)
            {
                BossEnraged = true;
                _sim.Entities.Stats[BossId].SetBase(StatType.Damage, _sim.Entities.Damage[BossId] * Fix64.Ratio(13, 10));
                _sim.Entities.RefreshStats(BossId);
            }
            _sim.Step(in input);

            // Смерть проверяется ПЕРВОЙ. Если игрок и последний враг погибли
            // на одном тике, забег заканчивается смертью: иначе труп получал бы
            // награду, и это читалось бы как ошибка.
            if (!_sim.Entities.Alive[Simulation.PlayerId])
            {
                End(RunOutcome.Died);
                return;
            }

            CollectBranchRewards();
            if (CountRequiredEnemies() == 0)
            {
                RiftsCleared++;
                Phase = RunPhase.SeekingExit;
            }
        }

        /// <summary>
        /// Враги мертвы, симуляция продолжает шагать: игрок сам доходит до
        /// помеченного выхода. Награда роллится только по приходу — так же,
        /// как раньше роллилась сразу по зачистке.
        /// </summary>
        private void StepSeekingExit(in InputFrame input, RunCommand command)
        {
            if (command == RunCommand.Leave)
            {
                End(RunOutcome.Left);
                return;
            }

            _sim.Step(in input);

            if (!_sim.Entities.Alive[Simulation.PlayerId])
            {
                End(RunOutcome.Died);
                return;
            }

            CollectBranchRewards();
            if (_sim.PlayerReachedExit(_map))
            {
                RollOffers();
                Phase = RunPhase.ChoosingReward;
            }
        }

        private void CollectBranchRewards()
        {
            if (_takenCount >= MaxTakenRewards || _itemBaseIds.Length == 0) return;
            var player = _sim.Entities.Position[Simulation.PlayerId];
            for (int b = 0; b < _map.RewardBranchCount && _takenCount < MaxTakenRewards; b++)
            {
                if (_branchClaimed[b] || BranchGuardsAlive(b) != 0) continue;
                int placement = _map.GetRewardBranch(b);
                if (FixVec2.DistanceSq(player, _map.CenterOf(placement)) > Fix64.Ratio(9, 4)) continue;
                // Bonus drops never shift the normal reward or affix streams.
                var rng = new Pcg32(LayoutSeed ^ unchecked((ulong)(placement + 1) * 0x9E3779B97F4A7C15UL), 0x4252414E4348UL);
                int baseId = _itemBaseIds[rng.NextInt(0, _itemBaseIds.Length)];
                ItemInstance item = ItemDrop.Roll(ref rng, baseId, (short)(Depth * 5));
                _taken[_takenCount++] = RewardOffer.OfItem(in item);
                _branchClaimed[b] = true;
                BranchesClaimed++;
            }
        }

        private void StepChoosing(RunCommand command)
        {
            if (command == RunCommand.Leave)
            {
                End(RunOutcome.Left);
                return;
            }

            // Приведение к int явное: вычитание значений enum на byte
            // считается в byte и на команде None ушло бы в переполнение.
            int choice = (int)command - (int)RunCommand.ChooseReward1;
            if (choice < 0 || choice >= RewardChoices) return;

            if (_takenCount < MaxTakenRewards) _taken[_takenCount++] = _offers[choice];

            if (IsFinalLevel)
            {
                End(RunOutcome.Completed);
                return;
            }
            EnterNextRift();
        }

        private void End(RunOutcome outcome)
        {
            Outcome = outcome;
            Phase = RunPhase.Ended;
        }

        /// <summary>
        /// Три предложения из потока Loot.
        ///
        /// Число бросков зависит от того, какой вид награды выпал, и это
        /// осознанно: вид роллится ПЕРВЫМ из того же потока, поэтому вся
        /// последовательность остаётся функцией от сида забега.
        ///
        /// Оговорка на будущее: добавляя новый вид награды, вставляй его
        /// проверку В КОНЕЦ лестницы порогов. Вставка в середину сдвинет
        /// все последующие пороги и поменяет награды у всех сохранённых сидов.
        /// </summary>
        private void RollOffers()
        {
            for (int i = 0; i < RewardChoices; i++)
                _offers[i] = RollOffer();
        }

        private RewardOffer RollOffer()
        {
            int kindRoll = _sim.Rng.Loot.NextInt(0, 100);
            short itemLevel = (short)(Depth * 5);

            // Узел дерева выпадает реже предмета: их в игре конечное число,
            // а предметы бесконечны.
            if (kindRoll < 20) return RollNodeOffer();
            if (kindRoll < 60) return RollStatOffer();

            int baseIndex = _itemBaseIds.Length == 0
                ? -1
                : _sim.Rng.Loot.NextInt(0, _itemBaseIds.Length);

            if (baseIndex < 0) return RollStatOffer();

            ItemInstance item = ItemDrop.Roll(ref _sim.Rng.Affix, _itemBaseIds[baseIndex], itemLevel);
            return RewardOffer.OfItem(in item);
        }

        private RewardOffer RollNodeOffer()
        {
            // Узлы «Печати пламени» — единственная реализованная способность.
            // Когда способностей станет двадцать, здесь появится выбор дерева,
            // а структура награды не поменяется.
            int which = _sim.Rng.Loot.NextInt(0, 3);
            AbilityNode node = which == 0 ? AbilityDefinition.NodeHotter()
                : which == 1 ? AbilityDefinition.NodeSplit()
                : AbilityDefinition.NodeSpreads();

            return RewardOffer.OfNode(in node);
        }

        private RewardOffer RollStatOffer()
        {
            // Прибавка к одному из статов персонажа. Слой Increased: он
            // затухает с ростом, поэтому его можно раздавать щедро.
            var stat = (StatType)_sim.Rng.Loot.NextInt(0, (int)StatType.Count);
            int percent = 5 + _sim.Rng.Loot.NextInt(0, 16);

            return RewardOffer.OfStat(stat, ModifierOp.Increased, Fix64.Ratio(percent, 100));
        }

        /// <summary>
        /// Вешает собранные за забег награды на лист статов.
        /// Предметы и узлы сюда не идут — у них свои системы.
        ///
        /// Номер награды в списке взятых служит идентификатором источника,
        /// поэтому повторный вызов ничего не удваивает: каждая прибавка сначала
        /// снимает свою прошлую.
        /// </summary>
        public void ApplyStatRewards(StatSheet sheet)
        {
            for (int i = 0; i < _takenCount; i++)
            {
                if (_taken[i].Kind != RewardKind.StatBoost) continue;

                sheet.RemoveSource(ModifierSource.TreeNode, i);
                sheet.Add(new StatModifier(_taken[i].Stat, _taken[i].Op, _taken[i].Value,
                    ModifierSource.TreeNode, i));
            }
        }

        public ulong Hash()
        {
            ulong hash = Hashing.Offset;
            Hashing.Mix(ref hash, (int)Phase);
            Hashing.Mix(ref hash, (int)Outcome);
            Hashing.Mix(ref hash, Depth);
            Hashing.Mix(ref hash, RiftsCleared);
            if (TotalLevels > 0) Hashing.Mix(ref hash, TotalLevels);
            if (BossEnraged) Hashing.Mix(ref hash, 0x424F5353);
            for (int b = 0; b < _map.RewardBranchCount; b++) Hashing.Mix(ref hash, _branchClaimed[b] ? 1 : 0);
            for (int i = 0; i < _sim.Entities.Count; i++) Hashing.Mix(ref hash, _enemyBranch[i]);

            Hashing.Mix(ref hash, _takenCount);
            for (int i = 0; i < _takenCount; i++) _taken[i].HashInto(ref hash);

            if (Phase == RunPhase.ChoosingReward)
                for (int i = 0; i < RewardChoices; i++) _offers[i].HashInto(ref hash);

            // Снаряжение хешируется рецептами отдельно от своих прибавок:
            // прибавки уже пришли в хеш через лист статов, а расхождение
            // в самих надетых вещах так видно раньше.
            PlayerEquipment?.HashInto(ref hash);

            Hashing.Mix(ref hash, _map.Hash());
            Encounters?.HashInto(ref hash);
            Hashing.Mix(ref hash, _sim.StateHash());
            return hash;
        }
    }
}
