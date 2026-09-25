namespace Game.Sim
{
    /// <summary>
    /// Петля забега: вход в Разлом → зачистка → смерть или выход →
    /// выбор одной награды из трёх → следующий Разлом глубже.
    ///
    /// СМЕРТЬ ЗАВЕРШАЕТ ЗАБЕГ И ОТНИМАЕТ НАЙДЕННОЕ: вещи и золото доезжают до
    /// лагеря только при выходе (GameSession.FinishRun), способности и таланты
    /// живут в забеге всегда. Так решение «идти дальше или уйти с добычей»
    /// становится настоящим решением.
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
        public int ForestBudShowcaseCount { get; set; }
        public int WendigoShowcase { get; set; }

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

        /// <summary>Способности и таланты этого забега. Смерть или выход забирают их вместе с забегом.</summary>
        public RunLoadout Loadout { get; } = new RunLoadout();

        /// <summary>Ставит набор забега в симуляцию. Зовётся после любой смены набора.</summary>
        public void ApplyLoadout() => Loadout.ApplyTo(_sim);

        /// <summary>Золото, найденное в забеге. Доезжает до лагеря только при выходе или прохождении.</summary>
        public int Gold { get; private set; }

        /// <summary>
        /// Артефакт забега (владелец, 24 сентября): один слот, с босса выбор 1 из 3, из тайника
        /// очень редко. Смерть или выход — пропадает вместе с забегом.
        /// </summary>
        public RunArtifact Artifact { get; private set; }

        /// <summary>Экран награды сейчас — выбор артефакта после босса (можно отказаться).</summary>
        public bool ChoosingArtifact => Phase == RunPhase.ChoosingReward && _offers[0].Kind == RewardKind.Artifact;

        /// <summary>Способность, ждущая замены при полной панели; −1 — не ждёт.</summary>
        public int PendingAbility { get; private set; } = -1;

        /// <summary>Разбор способности: 15 + 5 за уровень Разлома. Решение владельца от 15 сентября.</summary>
        public const int SalvageBaseGold = 15;
        public const int SalvageGoldPerDepth = 5;
        public int SalvageGold => SalvageBaseGold + SalvageGoldPerDepth * Depth;

        // Веса карточек из пропорций владельца: способность / вещь / талант.
        private const int AbilityWeight = 35, ItemWeight = 30, TalentWeight = 35;
        private const int FullAbilityWeight = 15, FullTalentWeight = 55;

        // ---- добыча с элит ----

        /// <summary>Сколько предметов может лежать на одной арене.</summary>
        public const int MaxDrops = 16;

        /// <summary>С элиты падает вещь в 65% случаев, способность — в 35%. Решение владельца от 15 сентября.</summary>
        public const int EliteItemChance = 65;

        /// <summary>Подбор вещи и способности при свободном слоте — как у тайника.</summary>
        public static readonly Fix64 PickupRadius = Fix64.Ratio(3, 2);

        /// <summary>Мини-меню над способностью при полной панели живёт в этом радиусе.</summary>
        public static readonly Fix64 DropMenuRadius = Fix64.Ratio(5, 2);

        private readonly RunDrop[] _drops = new RunDrop[MaxDrops];
        private int _dropCount;
        private readonly bool[] _eliteDropped;

        public int DropCount => _dropCount;
        public RunDrop GetDrop(int index) => _drops[index];

        public RiftRun(Simulation sim, ModuleSet modules, ItemDatabase items, int[] itemBaseIds,
            int maxModules = 64, LocationDefinition location = null)
        {
            _sim = sim;
            _enemyBranch = new int[sim.Entities.Capacity];
            _eliteDropped = new bool[sim.Entities.Capacity];
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
            Artifact = RunArtifact.None;
            _sim.SetArtifact(RunArtifact.None);
            Outcome = RunOutcome.None;
            Gold = 0;
            PendingAbility = -1;
            Loadout.ResetToStarter();

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
            // Не подобранное на прошлой арене осталось там.
            _dropCount = 0;
            System.Array.Clear(_eliteDropped, 0, _eliteDropped.Length);

            LevelSettings = _location?.GetLevel(Depth) ?? RiftLevelSettings.Prototype(Depth);
            LayoutSeed = LayoutGenerator.RollSeed(ref _sim.Rng.Layout);
            LevelSettings.Generate(_generator, _modules, _map, LayoutSeed);

            SpawnSeed = LayoutGenerator.RollSeed(ref _sim.Rng.Spawns);
            Encounters = null;
            if (WendigoShowcase > 0)
                Encounters = _sim.SetupWendigoEncounter(_map, SpawnSeed, WendigoShowcase > 1);
            else if (ForestBudShowcaseCount > 0)
                Encounters = _sim.SetupForestBudEncounter(_map, SpawnSeed, ForestBudShowcaseCount);
            else if (CombatFeelShowcase != CombatFeelCaptureTier.None)
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
            // Способности тоже вешаются заново: набор принадлежит забегу,
            // и каждый новый Разлом обязан начинаться с ним.
            ApplyLoadout();

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

                case RunPhase.ReplacingAbility:
                    StepReplacing(command);
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
            UpdateDrops(command);
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
            UpdateDrops(command);
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
                ItemInstance item = Tier(ItemDrop.Roll(ref rng, baseId, (short)(Depth * 5)));
                _branchClaimed[b] = true;
                // Очень редко тайник отдаёт артефакт вместо вещи — если артефакта ещё нет.
                // Бросок из того же локального потока тайника: награды и аффиксы забега не сдвигаются.
                if (Artifact == RunArtifact.None && rng.NextInt(0, 100) < RunArtifacts.CacheChancePercent)
                {
                    RunArtifact found = RunArtifacts.At(rng.NextInt(0, RunArtifacts.Count));
                    TakeArtifact(found);
                    _taken[_takenCount++] = RewardOffer.OfArtifact(found);
                    BranchesClaimed++;
                    continue;
                }
                _taken[_takenCount++] = RewardOffer.OfItem(in item);
                BranchesClaimed++;
            }
        }

        /// <summary>Дропы с только что погибших элит, подбор рядом стоящих и команда мини-меню.</summary>
        private void UpdateDrops(RunCommand command)
        {
            SpawnEliteDrops();
            CollectDrops();
            HandleDropCommand(command);
        }

        /// <summary>
        /// Каждая погибшая элита роняет ровно один предмет, босс — ничего (у него
        /// будет своя награда). Ролл идёт отдельным потоком от сида расстановки и
        /// номера сущности: общие потоки Loot и Affix не сдвигаются от того, в
        /// каком порядке игрок убивал.
        /// </summary>
        private void SpawnEliteDrops()
        {
            if (Encounters == null) return;
            EntityStore entities = _sim.Entities;
            for (int i = 1; i < entities.Count; i++)
            {
                if (entities.Alive[i] || _eliteDropped[i] || i == BossId || !Encounters.IsElite(i)) continue;
                _eliteDropped[i] = true;
                if (_dropCount >= MaxDrops) continue;

                var rng = new Pcg32(LayoutSeed ^ unchecked((ulong)(i + 1) * 0x9E3779B97F4A7C15UL), 0x454C495445UL);
                if (TryRollEliteDrop(ref rng, out RewardOffer offer))
                    _drops[_dropCount++] = new RunDrop(entities.Position[i], offer);
            }
        }

        private bool TryRollEliteDrop(ref Pcg32 rng, out RewardOffer offer)
        {
            bool wantsItem = rng.NextInt(0, 100) < EliteItemChance;
            int candidates = 0;
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (IsDropAbilityCandidate(pool)) candidates++;

            if ((!wantsItem || _itemBaseIds.Length == 0) && candidates > 0)
            {
                int pick = rng.NextInt(0, candidates);
                for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                    if (IsDropAbilityCandidate(pool) && pick-- == 0)
                    {
                        offer = RewardOffer.OfAbility(pool);
                        return true;
                    }
            }

            if (_itemBaseIds.Length == 0)
            {
                offer = default;
                return false;
            }

            int baseId = _itemBaseIds[rng.NextInt(0, _itemBaseIds.Length)];
            ItemInstance item = Tier(ItemDrop.Roll(ref rng, baseId, (short)(Depth * 5)));
            offer = RewardOffer.OfItem(in item);
            return true;
        }

        /// <summary>Способности нет в наборе и её не лежит на арене.</summary>
        private bool IsDropAbilityCandidate(int pool)
        {
            if (Loadout.Owns(pool)) return false;
            for (int d = 0; d < _dropCount; d++)
                if (!_drops[d].Claimed && _drops[d].Offer.Kind == RewardKind.Ability && _drops[d].Offer.PoolIndex == pool)
                    return false;
            return true;
        }

        /// <summary>Вещь и способность при свободном слоте поднимаются сами, когда герой рядом.</summary>
        private void CollectDrops()
        {
            FixVec2 player = _sim.Entities.Position[Simulation.PlayerId];
            Fix64 limit = PickupRadius * PickupRadius;
            for (int d = 0; d < _dropCount; d++)
            {
                if (_drops[d].Claimed || FixVec2.DistanceSq(player, _drops[d].Position) > limit) continue;
                if (_drops[d].Offer.Kind == RewardKind.Ability)
                {
                    // Полная панель — ждём решения в мини-меню, предмет лежит.
                    if (!Loadout.Add(_drops[d].Offer.PoolIndex)) continue;
                    ApplyLoadout();
                }
                ClaimDrop(d);
            }
        }

        /// <summary>
        /// Выбор в мини-меню. Действует на ближайшую лежащую способность в радиусе
        /// меню; если её нет — игрок отошёл или передумал — команда ничего не делает.
        /// </summary>
        private void HandleDropCommand(RunCommand command)
        {
            bool salvage = command == RunCommand.PickupSalvage;
            int slot = (int)command - (int)RunCommand.PickupReplaceSlot1;
            if (!salvage && (slot < 0 || slot >= RunLoadout.Slots)) return;

            int d = NearestAbilityDrop(DropMenuRadius);
            if (d < 0) return;

            if (salvage) Gold += SalvageGold;
            else
            {
                if (!Loadout.Put(slot, _drops[d].Offer.PoolIndex)) return;
                ApplyLoadout();
            }
            ClaimDrop(d);
        }

        private void ClaimDrop(int index)
        {
            _drops[index].Claimed = true;
            if (_takenCount < MaxTakenRewards) _taken[_takenCount++] = _drops[index].Offer;
        }

        /// <summary>Ближайшая неподобранная способность в радиусе от героя или −1.</summary>
        public int NearestAbilityDrop(Fix64 radius)
        {
            FixVec2 player = _sim.Entities.Position[Simulation.PlayerId];
            int best = -1;
            Fix64 bestDistance = radius * radius;
            for (int d = 0; d < _dropCount; d++)
            {
                if (_drops[d].Claimed || _drops[d].Offer.Kind != RewardKind.Ability) continue;
                Fix64 distance = FixVec2.DistanceSq(player, _drops[d].Position);
                if (best < 0 ? distance <= bestDistance : distance < bestDistance)
                {
                    best = d;
                    bestDistance = distance;
                }
            }
            return best;
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
            if (command == RunCommand.SkipReward && ChoosingArtifact)
            {
                FinishChoice();
                return;
            }

            int choice = (int)command - (int)RunCommand.ChooseReward1;
            if (choice < 0 || choice >= RewardChoices) return;

            RewardOffer offer = _offers[choice];
            if (offer.Kind == RewardKind.Artifact && !RunArtifacts.IsValid(offer.Artifact)) return;
            if (_takenCount < MaxTakenRewards) _taken[_takenCount++] = offer;

            if (offer.Kind == RewardKind.Artifact)
                TakeArtifact(offer.Artifact);
            else if (offer.Kind == RewardKind.Talent)
                Loadout.TakeTalent(offer.PoolIndex, offer.TalentIndex);
            else if (offer.Kind == RewardKind.Ability && !Loadout.Add(offer.PoolIndex))
            {
                // Панель полна — решение за игроком: заменить или разобрать.
                PendingAbility = offer.PoolIndex;
                Phase = RunPhase.ReplacingAbility;
                return;
            }

            FinishChoice();
        }

        /// <summary>
        /// Новая способность при полной панели. Бой по-прежнему стоит.
        /// Уход отсюда оставляет способность несобранной — забег кончается.
        /// </summary>
        private void StepReplacing(RunCommand command)
        {
            if (command == RunCommand.Leave)
            {
                End(RunOutcome.Left);
                return;
            }

            if (command == RunCommand.SalvageAbility)
                Gold += SalvageGold;
            else
            {
                int slot = (int)command - (int)RunCommand.ReplaceSlot1;
                if (slot < 0 || slot >= RunLoadout.Slots) return;
                Loadout.Put(slot, PendingAbility);
            }

            PendingAbility = -1;
            FinishChoice();
        }

        /// <summary>Награда взята: следующий Разлом или итог локации. Набор ставится в EnterNextRift.</summary>
        private void FinishChoice()
        {
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
            // Уровень с боссом: вместо карточек — выбор артефакта (владелец, 24 сентября).
            if (BossId >= 0 && RollArtifactOffers()) return;
            for (int i = 0; i < RewardChoices; i++)
                _offers[i] = RollOffer(i);
        }

        /// <summary>
        /// Три разных артефакта, кроме того, что уже в руках. Если свободных меньше трёх,
        /// пустые места занимает «нет артефакта» — экран их не показывает. False — нечего предложить.
        /// </summary>
        private bool RollArtifactOffers()
        {
            var pool = new RunArtifact[RunArtifacts.Count];
            int count = 0;
            for (int i = 0; i < RunArtifacts.Count; i++)
                if (RunArtifacts.At(i) != Artifact) pool[count++] = RunArtifacts.At(i);
            if (count == 0) return false;
            for (int i = 0; i < RewardChoices; i++)
            {
                if (i >= count) { _offers[i] = RewardOffer.OfArtifact(RunArtifact.None); continue; }
                int pick = i + _sim.Rng.Loot.NextInt(0, count - i);
                RunArtifact chosen = pool[pick];
                pool[pick] = pool[i];
                pool[i] = chosen;
                _offers[i] = RewardOffer.OfArtifact(chosen);
            }
            return true;
        }

        /// <summary>Меню разработчика: снять артефакт (в игре его можно только заменить).</summary>
        public void ClearArtifactForDeveloper()
        {
            Artifact = RunArtifact.None;
            _sim.SetArtifact(RunArtifact.None);
        }

        /// <summary>Взять артефакт: прежний (если был) уходит. Действует со следующего тика.</summary>
        public void TakeArtifact(RunArtifact artifact)
        {
            if (!RunArtifacts.IsValid(artifact)) return;
            Artifact = artifact;
            _sim.SetArtifact(artifact);
        }

        /// <summary>
        /// Одна карточка по весам владельца от 15 сентября: способность 35,
        /// вещь 30, талант 35; при полной панели 15 / 30 / 55. Вид, у которого
        /// сейчас нет кандидата, выпадает из суммы, и остальные делят его долю.
        /// </summary>
        private RewardOffer RollOffer(int filled)
        {
            bool full = Loadout.IsFull;
            int ability = CountAbilityCandidates(filled) > 0 ? (full ? FullAbilityWeight : AbilityWeight) : 0;
            int item = _itemBaseIds.Length > 0 ? ItemWeight : 0;
            int talent = CountTalentCandidates(filled) > 0 ? (full ? FullTalentWeight : TalentWeight) : 0;
            int total = ability + item + talent;
            if (total == 0) return RollStatOffer();

            int roll = _sim.Rng.Loot.NextInt(0, total);
            if (roll < ability) return RollAbilityOffer(filled);
            if (roll < ability + item) return RollItemOffer();
            return RollTalentOffer(filled);
        }

        /// <summary>Находка берёт основу своей редкости (обычная или редкая), RNG не расходует.</summary>
        private ItemInstance Tier(ItemInstance item) => _items != null ? _items.MatchTier(item) : item;

        private RewardOffer RollItemOffer()
        {
            int baseIndex = _sim.Rng.Loot.NextInt(0, _itemBaseIds.Length);
            ItemInstance item = Tier(ItemDrop.Roll(ref _sim.Rng.Affix, _itemBaseIds[baseIndex], (short)(Depth * 5)));
            return RewardOffer.OfItem(in item);
        }

        private bool OfferedOnPanel(int filled, RewardKind kind, int poolIndex)
        {
            for (int i = 0; i < filled; i++)
                if (_offers[i].Kind == kind && _offers[i].PoolIndex == poolIndex) return true;
            return false;
        }

        private bool IsAbilityCandidate(int pool, int filled)
            => !Loadout.Owns(pool) && !OfferedOnPanel(filled, RewardKind.Ability, pool);

        private bool IsTalentCandidate(int pool, int filled)
            => Loadout.CanTakeTalent(pool) && !OfferedOnPanel(filled, RewardKind.Talent, pool);

        private int CountAbilityCandidates(int filled)
        {
            int count = 0;
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (IsAbilityCandidate(pool, filled)) count++;
            return count;
        }

        private int CountTalentCandidates(int filled)
        {
            int count = 0;
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (IsTalentCandidate(pool, filled)) count++;
            return count;
        }

        private RewardOffer RollAbilityOffer(int filled)
        {
            int pick = _sim.Rng.Loot.NextInt(0, CountAbilityCandidates(filled));
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (IsAbilityCandidate(pool, filled) && pick-- == 0) return RewardOffer.OfAbility(pool);
            return RollItemOffer();
        }

        /// <summary>
        /// Случайное ещё не взятое усиление случайной имеющейся способности.
        /// Порядка нет (владелец, 24 сентября): любое из оставшихся с равным шансом.
        /// </summary>
        private RewardOffer RollTalentOffer(int filled)
        {
            int pick = _sim.Rng.Loot.NextInt(0, CountTalentCandidates(filled));
            for (int pool = 0; pool < PelagKit.PoolSize; pool++)
                if (IsTalentCandidate(pool, filled) && pick-- == 0)
                {
                    int left = SabreTalents.TalentsPerLine - Loadout.TalentCount(pool);
                    int index = Loadout.UntakenTalentAt(pool, _sim.Rng.Loot.NextInt(0, left));
                    return RewardOffer.OfTalent(pool, index);
                }
            return RollItemOffer();
        }

        private RewardOffer RollStatOffer()
        {
            // Прибавка к одному из статов персонажа. Слой Increased: он
            // затухает с ростом, поэтому его можно раздавать щедро.
            // Разыгрываются только боевые статы до сопротивления огню. Лавидий
            // добавлен в лист позже, и NextInt по всему списку молча начал бы
            // выдавать его прибавки и заодно сдвинул бы исход каждого сида.
            var stat = (StatType)_sim.Rng.Loot.NextInt(0, (int)StatType.FireResist + 1);
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

            Loadout.HashInto(ref hash);
            Hashing.Mix(ref hash, Gold);
            Hashing.Mix(ref hash, (int)Artifact);
            Hashing.Mix(ref hash, PendingAbility);
            Hashing.Mix(ref hash, _dropCount);
            for (int d = 0; d < _dropCount; d++) _drops[d].HashInto(ref hash);
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
