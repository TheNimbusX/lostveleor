using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Что забег отдаёт лагерю (экономика 06.10, план «Лагерь 06–10.10», решение M1 и S.9).
    ///
    /// Пепел (1 / 5 / 20), сталь, сердце и победа над боссом, когда-либо взятые навыки и
    /// открытые артефакты уходят в лагерь сразу, как опыт: смерть, уход и брошенный через F8
    /// забег их не отнимают. Золото и вещи — только в конце: при уходе всё, при смерти
    /// половина золота и ни одной вещи, при брошенном забеге ничего. Тестовый забег не даёт
    /// ничего, его герой — эталон 270/54, герой новой игры — 200/40.
    /// </summary>
    public sealed class RunHaulTests
    {
        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        /// <summary>Поджечь насмерть от имени героя: смерть идёт штатным путём шага боя (событие Death).</summary>
        private static void Burn(Simulation sim, int id)
            => sim.Statuses.ApplyBurn(id, Fix64.FromInt(10000000), 1, Simulation.PlayerId, -1);

        /// <summary>Враги не бьют: тесты проверяют счёт добычи, а не бой.</summary>
        private static void Freeze(Simulation sim)
        {
            for (int id = 1; id < sim.Entities.Count; id++) sim.Entities.NextAttackTick[id] = int.MaxValue;
        }

        private static void Die(GameSession session)
        {
            session.Run.Sim.Entities.Alive[Simulation.PlayerId] = false;
            session.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Summary, session.Mode);
            Assert.AreEqual(RunOutcome.Died, session.LastRun.Outcome);
        }

        /// <summary>Снимает врагов (волны тоже), ставит героя у выхода: экран награды.</summary>
        private static void ReachReward(GameSession session)
        {
            RiftRun run = session.Run;
            for (int guard = 0; guard < 4000 && run.Phase == RunPhase.Clearing; guard++)
            {
                EntityStore e = run.Sim.Entities;
                for (int i = 0; i < e.Count; i++)
                    if (e.Side[i] != Faction.Wole) e.Alive[i] = false;
                session.Step(InputFrame.Empty);
            }
            Assert.AreEqual(RunPhase.SeekingExit, run.Phase);
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);
        }

        /// <summary>Первая карточка; способность при полной панели встаёт заменой — без золота разбора.</summary>
        private static void TakeFirst(GameSession session)
        {
            session.Step(FormBaselineScenarios.Choice(0));
            if (session.Mode == GameMode.Rift && session.Run.Phase == RunPhase.ReplacingAbility)
                session.Step(Command(RunCommand.ReplaceSlot2));
        }

        private static int TakenItems(RiftRun run)
        {
            int items = 0;
            for (int i = 0; i < run.TakenRewardCount; i++)
                if (run.GetTaken(i).Kind == RewardKind.Item) items++;
            return items;
        }

        /// <summary>
        /// Восемь простых арен и босс (FormBaselineScenarios). Арены снимаются правкой Alive —
        /// ни пепла, ни золота элит; за зачистку 2×N, итого 72. Босс — временный Хранитель:
        /// без вступления его можно поджечь в первый же тик.
        /// </summary>
        private static GameSession BossSession(ulong seed, Camp camp)
        {
            LocationDefinition location = FormBaselineScenarios.Location(true);
            var session = new GameSession(seed, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            for (int depth = 1; depth <= FormBaselineScenarios.ArenaCount; depth++)
            {
                ReachReward(session);
                TakeFirst(session);
                Assert.AreEqual(RunPhase.ChoosingRoute, session.Run.Phase);
                if (depth == FormBaselineScenarios.ArenaCount) session.Run.Sim.ThicketMasterBossEnabled = false;
                session.Step(Command(RunCommand.ChooseRoute1));
            }
            Assert.Greater(session.Run.BossId, 0);
            Freeze(session.Run.Sim);
            return session;
        }

        /// <summary>Одна арена без маршрутов: обычные хранители и элитная пачка у выхода (с Расщепенем — по желанию).</summary>
        private static LocationDefinition EliteLocation(bool splitter)
        {
            var normal = new EncounterPack(1, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 1) });
            var elite = new EncounterPack(2, 100, splitter
                ? new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2, elite: true), new EncounterGroup(EnemyKind.ForestSplitter, 1, 1) }
                : new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2, elite: true) });
            var settings = new EncounterSettings(new[] { normal }, new[] { normal }, new[] { normal }, new[] { elite },
                1, 0, 100, Fix64.FromInt(5));
            return new LocationDefinition(StableId.Of("location.test-camp-materials"), PrototypeContent.Modules(),
                new[] { new RiftLevelSettings(12, 1, 0, 0, 0, 0, 100, settings) });
        }

        private static GameSession EliteSession(ulong seed, Camp camp, bool splitter = false)
        {
            LocationDefinition location = EliteLocation(splitter);
            var session = new GameSession(seed, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            Freeze(session.Run.Sim);
            return session;
        }

        // ---- золото и вещи ----

        [Test]
        public void Death_KeepsHalfGold_LosesItems_CreditedSteelAshHeartStay()
        {
            Camp camp = PrototypeContent.NewCamp();
            int gold0 = camp.Money(CurrencyType.Gold), steel0 = camp.Money(CurrencyType.Steel), ash0 = camp.Money(CurrencyType.Ash);
            int bag0 = camp.Bag.Used;
            GameSession session = BossSession(5, camp);
            Assert.AreEqual(72, session.Run.Gold, "восемь арен: 2 × (1 + … + 8)");
            int items = TakenItems(session.Run);
            int steelBeforeBoss = camp.Money(CurrencyType.Steel);

            Burn(session.Run.Sim, session.Run.BossId);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Rift, session.Mode);
            Assert.AreEqual(72 + RunEconomy.BossLevelGold, session.Run.Gold, "босс и подмога мертвы — золото уровня");
            Assert.AreEqual(ash0 + RunEconomy.AshBoss, camp.Money(CurrencyType.Ash), "пепел — сразу");
            Assert.AreEqual(steelBeforeBoss + RunEconomy.BossSteel, camp.Money(CurrencyType.Steel), "сталь — сразу");
            Assert.AreEqual(1, camp.HeartCount(RunBossKeys.ThicketMaster), "сердце — сразу");

            int found = session.Run.Gold;
            Die(session);
            Assert.AreEqual(gold0 + found / 2, camp.Money(CurrencyType.Gold));
            Assert.AreEqual(found / 2, session.LastRun.GoldKept);
            Assert.AreEqual(found - found / 2, session.LastRun.GoldLeftBehind);
            Assert.AreEqual(0, session.LastRun.ItemsKept);
            Assert.AreEqual(items, session.LastRun.ItemsLeftBehind);
            Assert.AreEqual(bag0, camp.Bag.Used, "вещи при смерти теряются");
            Assert.AreEqual(ash0 + RunEconomy.AshBoss, camp.Money(CurrencyType.Ash), "смерть пепел не отнимает");
            Assert.AreEqual(steelBeforeBoss + RunEconomy.BossSteel, camp.Money(CurrencyType.Steel));
            Assert.AreEqual(1, camp.HeartCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(camp.Money(CurrencyType.Steel) - steel0, session.LastRun.SteelKept);
            Assert.AreEqual(RunEconomy.AshBoss, session.LastRun.AshKept);
            Assert.AreEqual(1, session.LastRun.HeartsKept);
        }

        [Test]
        public void Leave_KeepsAllGold()
        {
            Camp camp = PrototypeContent.NewCamp();
            int gold0 = camp.Money(CurrencyType.Gold), bag0 = camp.Bag.Used;
            LocationDefinition location = FormBaselineScenarios.Location(true);
            var session = new GameSession(9, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            ReachReward(session);
            TakeFirst(session);
            session.Step(Command(RunCommand.ChooseRoute1));
            ReachReward(session);
            int found = session.Run.Gold, items = TakenItems(session.Run);
            Assert.AreEqual(2 + 4, found, "А1 + А2");

            session.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunOutcome.Left, session.LastRun.Outcome);
            Assert.AreEqual(gold0 + found, camp.Money(CurrencyType.Gold));
            Assert.AreEqual(found, session.LastRun.GoldKept);
            Assert.AreEqual(0, session.LastRun.GoldLeftBehind);
            Assert.AreEqual(items, session.LastRun.ItemsKept);
            Assert.AreEqual(bag0 + items, camp.Bag.Used);
        }

        // ---- пепел, сталь, сердце ----

        [Test]
        public void Ash_CountsOneFiveTwentyByClass_CreditedImmediately()
        {
            Camp camp = PrototypeContent.NewCamp();
            GameSession session = EliteSession(846, camp);
            Simulation sim = session.Run.Sim;
            int normal = -1, elite = -1;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.Entities.Alive[id] || sim.Entities.Side[id] == Faction.Wole) continue;
                if (session.Run.Encounters.IsElite(id)) { if (elite < 0) elite = id; }
                else if (normal < 0) normal = id;
            }
            Assert.Greater(normal, 0);
            Assert.Greater(elite, 0);

            int ash = camp.Money(CurrencyType.Ash);
            Burn(sim, normal);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Rift, session.Mode);
            Assert.AreEqual(ash + RunEconomy.AshNormal, camp.Money(CurrencyType.Ash), "обычный — 1, сразу");
            Burn(sim, elite);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(ash + RunEconomy.AshNormal + RunEconomy.AshElite, camp.Money(CurrencyType.Ash), "элита — 5");

            Camp bossCamp = PrototypeContent.NewCamp();
            GameSession boss = BossSession(5, bossCamp);
            int before = bossCamp.Money(CurrencyType.Ash);
            Burn(boss.Run.Sim, boss.Run.BossId);
            boss.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Rift, boss.Mode);
            Assert.AreEqual(before + RunEconomy.AshBoss, bossCamp.Money(CurrencyType.Ash), "босс — 20, не 5: он помечен и элитой");
        }

        [Test]
        public void BossDeath_GivesSteelHeartAndKey_EvenIfHeroDiesSameTick()
        {
            Assert.AreEqual(RunBossKeys.ThicketMaster, RunBossKeys.Of(EnemyKind.ForestThicketMaster));
            Assert.AreEqual(RunBossKeys.ThicketMaster, RunBossKeys.Of(EnemyKind.ForestGuardian), "временный Хранитель — тот же босс леса");

            Camp camp = PrototypeContent.NewCamp();
            GameSession session = BossSession(13, camp);
            int steel = camp.Money(CurrencyType.Steel);
            Assert.IsFalse(camp.BossDefeated(RunBossKeys.ThicketMaster));

            Burn(session.Run.Sim, session.Run.BossId);
            session.Run.Sim.Entities.Alive[Simulation.PlayerId] = false;
            session.Step(InputFrame.Empty);

            Assert.AreEqual(GameMode.Summary, session.Mode);
            Assert.AreEqual(RunOutcome.Died, session.LastRun.Outcome);
            Assert.IsTrue(camp.BossDefeated(RunBossKeys.ThicketMaster), "победа в тик смерти босса, даже если герой пал тогда же");
            Assert.AreEqual(1, camp.HeartCount(RunBossKeys.ThicketMaster));
            Assert.AreEqual(steel + RunEconomy.BossSteel, camp.Money(CurrencyType.Steel));
            Assert.AreEqual(1, session.LastRun.HeartsKept);
        }

        /// <summary>
        /// Сталь элитной встречи — когда легли все её члены и дети Расщепеня, и сразу в лагерь.
        /// finish: 0 — смерть при живых детях; 1 — дети мертвы, уход с экрана награды; 2 — дети
        /// мертвы, затем смерть; 3 — дети мертвы, затем F8 «В лагерь». Уже начисленная сталь
        /// остаётся и при брошенном забеге (решение M7), поэтому 0/1/1/1.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 1)]
        public void EliteEncounterSteel_AfterAllSplitChildren_EvenOnDeath(int finish, int expected)
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3);
            GameSession s = EliteSession(846, camp, splitter: true);
            var sim = s.ActiveSim; var plan = s.Run.Encounters;
            EncounterPlacement encounter = default; bool found = false;
            for (int i = 0; i < plan.Count; i++) if (plan.Get(i).Role == EncounterRole.ExitGuard) { encounter = plan.Get(i); found = true; }
            Assert.True(found); Assert.AreEqual(3, encounter.EnemyCount);
            int end = encounter.FirstEntity + encounter.EnemyCount;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (id >= encounter.FirstEntity && id < end) sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, 0, -1);
                else sim.Entities.Alive[id] = false;
            }
            s.Step(InputFrame.Empty); Assert.True(sim.HasPendingSplits);
            int deadline = sim.Tick + Simulation.SplitterDeathReleaseTicks + 1;
            while (sim.HasPendingSplits && sim.Tick <= deadline) s.Step(InputFrame.Empty);
            Assert.False(sim.HasPendingSplits); int children = 0;
            for (int id = end; id < sim.Entities.Count; id++) if (sim.SplitParentOf(id) >= encounter.FirstEntity && sim.Entities.Alive[id])
            {
                children++;
                if (finish != 0) sim.Statuses.ApplyBurn(id, Fix64.FromInt(100000), 1, 0, -1);
            }
            Assert.AreEqual(2, children);
            Assert.AreEqual(0, camp.Money(CurrencyType.Steel), "пока живы дети Расщепеня, встреча не зачищена");
            if (finish != 0) s.Step(InputFrame.Empty);
            if (finish == 0 || finish == 2) { sim.Entities.Alive[0] = false; sim.Entities.Health[0] = 0; s.Step(InputFrame.Empty); }
            else if (finish == 3) s.ReturnToCamp();
            else
            {
                Assert.AreEqual(RunPhase.SeekingExit, s.Run.Phase);
                sim.Entities.Position[Simulation.PlayerId] = s.Run.Map.ExitPoint(0);
                s.Step(InputFrame.Empty);
                Assert.AreEqual(RunPhase.ChoosingReward, s.Run.Phase);
                s.Step(Command(RunCommand.Leave));
                Assert.AreEqual(RunOutcome.Left, s.LastRun.Outcome);
            }
            Assert.AreEqual(expected, camp.Money(CurrencyType.Steel));
        }

        // ---- навыки и артефакты ----

        [Test]
        public void SkillsHeld_IncludeStarterAndReplacedSkills_OnDeath()
        {
            Assert.IsTrue(FindReplaceScreen(false, out GameSession kept, out int keptPool));
            Camp camp = kept.Camp;
            Assert.IsTrue(camp.SkillEverTaken(PelagKit.PoolDefinition(PelagKit.StarterPoolIndex).Id), "стартовый — с первого шага");
            kept.Step(Command(RunCommand.ReplaceSlot2));
            Assert.IsTrue(camp.SkillEverTaken(PelagKit.PoolDefinition(keptPool).Id), "встал заменой — записан сразу");
            Die(kept);
            Assert.IsTrue(camp.SkillEverTaken(PelagKit.PoolDefinition(keptPool).Id), "смерть не отнимает");
            Assert.AreEqual(RunLoadout.Slots + 1, camp.EverTakenSkillCount, "стартовый, три слота и замена");

            Assert.IsTrue(FindReplaceScreen(true, out GameSession salvaged, out int salvagedPool));
            salvaged.Step(Command(RunCommand.SalvageAbility));
            Assert.IsFalse(salvaged.Camp.SkillEverTaken(PelagKit.PoolDefinition(salvagedPool).Id), "разобранный в слот не вставал");
            Assert.AreEqual(RunLoadout.Slots, salvaged.Camp.EverTakenSkillCount);
        }

        /// <summary>Прототипный забег с полной панелью и способностью на экране награды → экран замены.</summary>
        private static bool FindReplaceScreen(bool skipFirst, out GameSession session, out int pool)
        {
            bool skipped = !skipFirst;
            for (ulong seed = 1; seed < 400; seed++)
            {
                session = PrototypeContent.NewSession(seed);
                session.EnterRift();
                for (int slot = 1; slot < RunLoadout.Slots; slot++) session.Run.Loadout.Put(slot, slot);
                session.Run.ApplyLoadout();
                ReachReward(session);
                for (int card = 0; card < RiftRun.RewardChoices; card++)
                {
                    RewardOffer offer = session.Run.GetOffer(card);
                    if (offer.Kind != RewardKind.Ability) continue;
                    pool = offer.PoolIndex;
                    session.Step(FormBaselineScenarios.Choice(card));
                    if (session.Run.Phase != RunPhase.ReplacingAbility) break;
                    if (!skipped) { skipped = true; break; }
                    return true;
                }
            }
            session = null;
            pool = -1;
            return false;
        }

        [Test]
        public void ArtifactsTaken_RecordedOnAnyOutcome_SkippedNotRecorded()
        {
            Camp skipCamp = PrototypeContent.NewCamp();
            GameSession skip = BossSession(17, skipCamp);
            ReachReward(skip);
            Assert.IsTrue(skip.Run.ChoosingArtifact);
            skip.Step(Command(RunCommand.SkipReward));
            Assert.AreEqual(GameMode.Summary, skip.Mode);
            for (int i = 0; i < RunArtifacts.Count; i++)
                Assert.IsFalse(skipCamp.ArtifactOpened(RunArtifacts.At(i)), "отказ не открывает");

            Camp takeCamp = PrototypeContent.NewCamp();
            GameSession take = BossSession(17, takeCamp);
            ReachReward(take);
            RunArtifact chosen = take.Run.GetOffer(0).Artifact;
            Assert.IsTrue(RunArtifacts.IsValid(chosen));
            take.Step(FormBaselineScenarios.Choice(0));
            Assert.AreEqual(RunOutcome.Completed, take.LastRun.Outcome);
            Assert.IsTrue(takeCamp.ArtifactOpened(chosen), "взятый с босса открыт в атласе");
            for (int i = 0; i < RunArtifacts.Count; i++)
                if (RunArtifacts.At(i) != chosen) Assert.IsFalse(takeCamp.ArtifactOpened(RunArtifacts.At(i)));
        }

        // ---- тестовый и брошенный забеги ----

        [Test]
        public void DeveloperRun_DeliversNothing()
        {
            Camp camp = PrototypeContent.NewCamp();
            LocationDefinition location = EliteLocation(false);
            var session = new GameSession(3, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            var wallet = new int[(int)CurrencyType.Count];
            for (int c = 0; c < wallet.Length; c++) wallet[c] = camp.Money((CurrencyType)c);
            int skills = camp.EverTakenSkillCount, bag = camp.Bag.Used, xp = camp.Experience, level = camp.Level;

            session.StartDeveloperRift(location, 1, false, 99);
            Assert.IsTrue(session.IsDeveloperRun);
            Simulation sim = session.Run.Sim;
            Freeze(sim);
            for (int id = 1; id < sim.Entities.Count; id++)
                if (sim.Entities.Alive[id] && sim.Entities.Side[id] != Faction.Wole) Burn(sim, id);
            session.Step(InputFrame.Empty);
            Assert.Greater(session.CurrentRunStats.Kills, 0);
            Assert.Greater(session.Run.Gold, 0, "золото элит забег нашёл");
            Die(session);

            for (int c = 0; c < wallet.Length; c++) Assert.AreEqual(wallet[c], camp.Money((CurrencyType)c), ((CurrencyType)c).ToString());
            Assert.AreEqual(skills, camp.EverTakenSkillCount);
            Assert.AreEqual(bag, camp.Bag.Used);
            Assert.AreEqual(xp, camp.Experience);
            Assert.AreEqual(level, camp.Level);
            Assert.AreEqual(0, session.LastRun.GoldKept);
            Assert.AreEqual(0, session.LastRun.AshKept);
            Assert.AreEqual(0, session.LastRun.SteelKept);
        }

        [Test]
        public void DeveloperAbandon_LosesGoldAndItems_KeepsCreditedResources()
        {
            Camp camp = PrototypeContent.NewCamp();
            int gold = camp.Money(CurrencyType.Gold), bag = camp.Bag.Used;
            int ash = camp.Money(CurrencyType.Ash), steel = camp.Money(CurrencyType.Steel);
            GameSession session = EliteSession(21, camp);
            Simulation sim = session.Run.Sim;
            int killed = 0, elites = 0;
            for (int id = 1; id < sim.Entities.Count; id++)
            {
                if (!sim.Entities.Alive[id] || sim.Entities.Side[id] == Faction.Wole) continue;
                Burn(sim, id);
                killed++;
                if (session.Run.Encounters.IsElite(id)) elites++;
            }
            session.Step(InputFrame.Empty);
            Assert.AreEqual(2, elites);
            Assert.AreEqual(RunEconomy.EliteGold * elites, session.Run.Gold);
            int credited = (killed - elites) * RunEconomy.AshNormal + elites * RunEconomy.AshElite;
            Assert.AreEqual(ash + credited, camp.Money(CurrencyType.Ash));
            Assert.AreEqual(steel + RunEconomy.EliteEncounterSteel, camp.Money(CurrencyType.Steel));

            session.ReturnToCamp();
            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.AreEqual(gold, camp.Money(CurrencyType.Gold), "брошенный забег золото не отдаёт");
            Assert.AreEqual(bag, camp.Bag.Used);
            Assert.AreEqual(ash + credited, camp.Money(CurrencyType.Ash), "начисленный пепел остаётся");
            Assert.AreEqual(steel + RunEconomy.EliteEncounterSteel, camp.Money(CurrencyType.Steel), "начисленная сталь остаётся");
        }

        /// <summary>Пепел считается по смертям врагов; когда всех убил герой, классы сходятся со статистикой забега.</summary>
        [Test]
        public void HaulKills_MatchRunStats_WhenHeroKillsAll()
        {
            Camp camp = PrototypeContent.NewCamp();
            GameSession session = EliteSession(33, camp);
            Simulation sim = session.Run.Sim;
            int ash = camp.Money(CurrencyType.Ash);
            for (int id = 1; id < sim.Entities.Count; id++)
                if (sim.Entities.Alive[id] && sim.Entities.Side[id] != Faction.Wole) Burn(sim, id);
            session.Step(InputFrame.Empty);
            RunStats stats = session.CurrentRunStats;
            Assert.Greater(stats.EliteKills, 0);
            int expected = (stats.Kills - stats.EliteKills - stats.BossKills) * RunEconomy.AshNormal
                + stats.EliteKills * RunEconomy.AshElite + stats.BossKills * RunEconomy.AshBoss;
            Assert.AreEqual(ash + expected, camp.Money(CurrencyType.Ash));

            Camp bossCamp = PrototypeContent.NewCamp();
            GameSession boss = BossSession(19, bossCamp);
            ash = bossCamp.Money(CurrencyType.Ash);
            Burn(boss.Run.Sim, boss.Run.BossId);
            boss.Step(InputFrame.Empty);
            stats = boss.CurrentRunStats;
            Assert.AreEqual(1, stats.BossKills);
            expected = (stats.Kills - stats.EliteKills - stats.BossKills) * RunEconomy.AshNormal
                + stats.EliteKills * RunEconomy.AshElite + stats.BossKills * RunEconomy.AshBoss;
            Assert.AreEqual(ash + expected, bossCamp.Money(CurrencyType.Ash));
        }

        // ---- база героя и клятвы ----

        [Test]
        public void FreshProfileHeroIs200x40_SandboxAndDeveloperRunKeep270x54()
        {
            Assert.AreEqual(270 - 200, HeroBaseline.Reference.Health - HeroBaseline.Fresh.Health);
            Assert.AreEqual(54 - 40, HeroBaseline.Reference.Damage - HeroBaseline.Fresh.Damage);

            LocationDefinition location = FormBaselineScenarios.Location(false);
            var fresh = new GameSession(5, PrototypeContent.NewCamp(), location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            var sandbox = new GameSession(5, new Camp(PrototypeContent.Items(), act: 3), location.Modules,
                PrototypeContent.ItemBaseIds(), location: location);
            var developer = new GameSession(5, PrototypeContent.NewCamp(), location.Modules, PrototypeContent.ItemBaseIds(), location: location);

            // Лагерь: тот же сдвиг, что в Разломе.
            EntityStore freshCamp = fresh.CampSim.Entities, sandboxCamp = sandbox.CampSim.Entities;
            Assert.AreEqual(sandboxCamp.MaxHealth[0] - 70, freshCamp.MaxHealth[0]);
            Assert.AreEqual(sandboxCamp.Damage[0] - 14, freshCamp.Damage[0]);

            fresh.EnterRift();
            sandbox.EnterRift();
            developer.StartDeveloperRift(location, 1, false, 5);
            EntityStore f = fresh.Run.Sim.Entities, s = sandbox.Run.Sim.Entities, d = developer.Run.Sim.Entities;
            Assert.AreEqual(40, f.Damage[0], "новая игра: 34 + 6");
            Assert.AreEqual(54, s.Damage[0], "Sandbox: эталон 34 + 20");
            Assert.AreEqual(54, d.Damage[0], "тестовый забег — эталон при любом профиле");
            Assert.AreEqual(s.MaxHealth[0] - 70, f.MaxHealth[0], "новая игра: +50 вместо +120");
            Assert.AreEqual(s.MaxHealth[0], d.MaxHealth[0]);
            Assert.AreEqual(s.MaxLavidium[0], f.MaxLavidium[0], "лавидий одинаков");
            Assert.AreEqual(f.MaxHealth[0], f.Health[0], "забег начинается с полным здоровьем");

            // Эталон хешируется как прежде: перегрузка без базы и с эталоном — одно и то же.
            var a = new Simulation(77, 64); var b = new Simulation(77, 64); var c = new Simulation(77, 64);
            a.ApplyHeroBaseline(); b.ApplyHeroBaseline(HeroBaseline.Reference); c.ApplyHeroBaseline(HeroBaseline.Fresh);
            Assert.AreEqual(a.StateHash(), b.StateHash());
            Assert.AreNotEqual(a.StateHash(), c.StateHash());
        }

        [Test]
        public void EmptyBoons_LeaveStateHashUnchanged()
        {
            RiftRun plain = FormBaselineScenarios.NewArenaRun(9);
            RiftRun empty = FormBaselineScenarios.NewArenaRun(9);
            Assert.IsTrue(empty.SetBoons(RunBoons.Empty));
            Assert.IsTrue(empty.Boons.IsEmpty);
            plain.StartRun();
            empty.StartRun();
            Assert.IsFalse(empty.SetBoons(RunBoons.Empty.WithRank(OathId.ToughHide, 1)), "клятвы закреплены за начатым забегом");
            Assert.IsTrue(empty.Boons.IsEmpty);
            var input = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(4), Fix64.FromInt(3)),
                Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack),
                AttackTarget = -1, AbilityTarget = -1,
            };
            for (int t = 0; t < 150; t++)
            {
                if (t % 40 == 0) input.AbilityMask = 1; else input.AbilityMask = 0;
                plain.Step(in input);
                empty.Step(in input);
                Assert.AreEqual(plain.Sim.StateHash(), empty.Sim.StateHash(), "тик " + t);
            }
            Assert.AreEqual(plain.Hash(), empty.Hash());
        }

        // ---- клятвы добычи (T2) ----

        /// <summary>Лагерь с одной купленной (и сразу включённой) клятвой.</summary>
        private static Camp CampWithOath(OathId id)
        {
            Camp camp = PrototypeContent.NewCamp();
            camp.Earn(CurrencyType.Ash, camp.NextOathPrice);
            Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
            Assert.IsTrue(camp.OathActive(id));
            return camp;
        }

        [Test]
        public void Death_WithTenaciousHands_DeliversSeventyFivePercent()
        {
            Camp camp = CampWithOath(OathId.TenaciousHands);
            int gold0 = camp.Money(CurrencyType.Gold);
            LocationDefinition location = FormBaselineScenarios.Location(true);
            var session = new GameSession(9, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
            session.EnterRift();
            Assert.AreEqual(75, session.Run.Boons.DeathGoldPercent);
            for (int arena = 0; arena < 3; arena++)
            {
                ReachReward(session);
                TakeFirst(session);
                session.Step(Command(RunCommand.ChooseRoute1));
            }
            int found = session.Run.Gold;
            Assert.GreaterOrEqual(found, 2 + 4 + 6);

            Die(session);
            int kept = found * 75 / 100;
            Assert.AreNotEqual(found / 2, kept, "иначе тест не отличил бы клятву от обычной смерти");
            Assert.AreEqual(kept, session.LastRun.GoldKept, "«Цепкие руки»: 75% вместо 50%");
            Assert.AreEqual(found - kept, session.LastRun.GoldLeftBehind);
            Assert.AreEqual(gold0 + kept, camp.Money(CurrencyType.Gold));
        }

        [Test]
        public void AshTrail_AddsTwentyPercent()
        {
            Camp plainCamp = PrototypeContent.NewCamp(), trailCamp = CampWithOath(OathId.AshTrail);
            GameSession plain = EliteSession(846, plainCamp), trail = EliteSession(846, trailCamp);
            Assert.AreEqual(120, trail.Run.Boons.AshPercent);
            int plain0 = plainCamp.Money(CurrencyType.Ash), trail0 = trailCamp.Money(CurrencyType.Ash);

            // По одному: процент — от суммы забега, вниз (1 → 1, затем 6 → 7), а не от каждой смерти.
            int kills = 0;
            for (int id = 1; id < plain.Run.Sim.Entities.Count; id++)
            {
                if (!plain.Run.Sim.Entities.Alive[id] || plain.Run.Sim.Entities.Side[id] == Faction.Wole) continue;
                Burn(plain.Run.Sim, id);
                Burn(trail.Run.Sim, id);
                plain.Step(InputFrame.Empty);
                trail.Step(InputFrame.Empty);
                int raw = plainCamp.Money(CurrencyType.Ash) - plain0;
                Assert.AreEqual(RunEconomy.Percent(raw, 120), trailCamp.Money(CurrencyType.Ash) - trail0, "после убийства " + id);
                kills++;
                if (plain.Mode != GameMode.Rift) break;
            }
            Assert.Greater(kills, 2);
            int total = plainCamp.Money(CurrencyType.Ash) - plain0;
            Assert.Greater(RunEconomy.Percent(total, 120), total, "след добавил пепла");
            Assert.AreEqual(RunEconomy.Percent(total, 120), trailCamp.Money(CurrencyType.Ash) - trail0);
        }
    }
}
